using System.Net;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Core.Cosmos;

public sealed record IndexStats
{
    public string Id { get; init; } = StatsRepository.GlobalId;
    public int DocumentCount { get; init; }
    public int ChunkCount { get; init; }
    public int FailedCount { get; init; }
    public Dictionary<string, int> DocumentsByType { get; init; } = [];
    public DateTimeOffset? LastIndexedAt { get; init; }

    /// <summary>État par document : c'est ce qui rend la mise à jour idempotente (le change feed est « at least once »).</summary>
    public Dictionary<string, DocumentStat> Documents { get; init; } = [];
}

public sealed record DocumentStat(string DocType, DocumentStatus Status, int Chunks, DateTimeOffset UpdatedAt);

/// <summary>
/// Statistiques d'indexation, alimentées par le change feed processor du conteneur <c>documents</c>.
/// Concurrence optimiste par ETag : deux instances du worker peuvent traiter des lots en parallèle.
/// </summary>
public sealed class StatsRepository(CosmosClient client, IOptions<DocuRagOptions> options)
{
    public const string GlobalId = "global";

    private readonly Container _container = client.GetContainer(
        options.Value.Cosmos.Database, options.Value.Cosmos.StatsContainer);

    public async Task<IndexStats> GetAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Stats;

    public async Task ApplyAsync(IReadOnlyCollection<DocumentRecord> changes, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (current, etag) = await ReadAsync(cancellationToken);
            var documents = new Dictionary<string, DocumentStat>(current.Documents);
            foreach (var change in changes.Where(c => c.Status is DocumentStatus.Indexed or DocumentStatus.Failed))
            {
                documents[change.Id] = new DocumentStat(change.DocType, change.Status, change.ChunkCount ?? 0, change.UpdatedAt);
            }

            var indexed = documents.Values.Where(d => d.Status == DocumentStatus.Indexed).ToList();
            var updated = new IndexStats
            {
                Documents = documents,
                DocumentCount = indexed.Count,
                ChunkCount = indexed.Sum(d => d.Chunks),
                FailedCount = documents.Values.Count(d => d.Status == DocumentStatus.Failed),
                DocumentsByType = indexed.GroupBy(d => d.DocType).ToDictionary(g => g.Key, g => g.Count()),
                LastIndexedAt = indexed.Count == 0 ? null : indexed.Max(d => d.UpdatedAt),
            };

            try
            {
                if (etag is null)
                {
                    await _container.CreateItemAsync(updated, new PartitionKey(GlobalId), cancellationToken: cancellationToken);
                }
                else
                {
                    await _container.ReplaceItemAsync(updated, GlobalId, new PartitionKey(GlobalId),
                        new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
                }

                return;
            }
            catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict && attempt < 10)
            {
                // Quelqu'un a écrit entre notre lecture et notre écriture : on relit et on recommence.
            }
        }
    }

    private async Task<(IndexStats Stats, string? ETag)> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<IndexStats>(GlobalId, new PartitionKey(GlobalId), cancellationToken: cancellationToken);
            return (response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (new IndexStats(), null);
        }
    }
}
