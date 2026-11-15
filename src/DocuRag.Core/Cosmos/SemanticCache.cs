using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Core.Cosmos;

public sealed record CachedAnswer
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required string DocType { get; init; }
    public required string Answer { get; init; }
    public required IReadOnlyList<SourceCitation> Sources { get; init; }
    public required IReadOnlyList<string> DocumentIds { get; init; }
    public float[]? Embedding { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CacheHit(CachedAnswer Entry, string Kind, double Similarity);

/// <summary>
/// Cache sémantique des réponses, stocké dans un conteneur Cosmos avec index vectoriel et TTL.
/// Deux niveaux :
/// <list type="number">
/// <item><b>exact</b> : lecture ponctuelle par id (hash de la question normalisée) — quelques ms, sans appel d'embedding ;</item>
/// <item><b>semantic</b> : recherche vectorielle sur les questions déjà posées, seuil de similarité 0,95.</item>
/// </list>
/// Expiration : TTL du conteneur (Bicep). Invalidation : le change feed supprime les entrées qui citent un document réindexé.
/// </summary>
/// <remarks>
/// Le plan prévoyait Redis ; Cosmos est utilisé à la place pour ne pas ajouter une ressource payante
/// (Azure Managed Redis avec RediSearch n'a pas de tier gratuit) et rester sur le même moteur vectoriel.
/// </remarks>
public sealed partial class SemanticCache(CosmosClient client, IOptions<DocuRagOptions> options)
{
    private const string AllDocTypes = "*";

    private readonly Container _container = client.GetContainer(
        options.Value.Cosmos.Database, options.Value.Cosmos.CacheContainer);

    private readonly double _threshold = options.Value.Cache.SimilarityThreshold;

    public static string Normalize(string question) =>
        Spaces().Replace(question.Trim().ToLowerInvariant(), " ").TrimEnd('?', '.', '!', ' ');

    public static string BuildId(string question, string? docType)
    {
        var key = $"{Normalize(question)}|{NormalizeDocType(docType)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40];
    }

    private static string NormalizeDocType(string? docType) =>
        string.IsNullOrWhiteSpace(docType) ? AllDocTypes : docType.Trim().ToLowerInvariant();

    public async Task<CacheHit?> GetExactAsync(string question, string? docType, CancellationToken cancellationToken)
    {
        var id = BuildId(question, docType);
        try
        {
            var response = await _container.ReadItemAsync<CachedAnswer>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return new CacheHit(response.Resource, "exact", 1.0);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CacheHit?> GetSimilarAsync(float[] questionEmbedding, string? docType, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT TOP 1 c.id, c.question, c.docType, c.answer, c.sources, c.documentIds, c.createdAt, " +
                "VectorDistance(c.embedding, @embedding) AS similarity FROM c WHERE c.docType = @docType " +
                "ORDER BY VectorDistance(c.embedding, @embedding)")
            .WithParameter("@embedding", questionEmbedding)
            .WithParameter("@docType", NormalizeDocType(docType));

        var results = await DocumentRepository.ReadAllAsync(
            _container.GetItemQueryIterator<CachedAnswerWithScore>(query), cancellationToken);
        var best = results.FirstOrDefault();
        if (best is null || best.Similarity < _threshold)
        {
            return null;
        }

        var entry = new CachedAnswer
        {
            Id = best.Id,
            Question = best.Question,
            DocType = best.DocType,
            Answer = best.Answer,
            Sources = best.Sources,
            DocumentIds = best.DocumentIds,
            CreatedAt = best.CreatedAt,
        };
        return new CacheHit(entry, "semantic", best.Similarity);
    }

    public Task StoreAsync(string question, string? docType, float[] embedding, string answer,
        IReadOnlyList<SourceCitation> sources, CancellationToken cancellationToken)
    {
        var entry = new CachedAnswer
        {
            Id = BuildId(question, docType),
            Question = question,
            DocType = NormalizeDocType(docType),
            Answer = answer,
            Sources = sources,
            DocumentIds = sources.Select(s => s.DocumentId).Distinct().ToList(),
            Embedding = embedding,
        };
        return _container.UpsertItemAsync(entry, new PartitionKey(entry.Id), cancellationToken: cancellationToken);
    }

    /// <summary>Supprime les réponses qui citent ce document (appelé par le change feed quand il est réindexé).</summary>
    public async Task<int> InvalidateDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT VALUE c.id FROM c WHERE ARRAY_CONTAINS(c.documentIds, @documentId)")
            .WithParameter("@documentId", documentId);
        var ids = await DocumentRepository.ReadAllAsync(_container.GetItemQueryIterator<string>(query), cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await _container.DeleteItemAsync<object>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Déjà expiré (TTL) ou supprimé par une autre instance.
            }
        }

        return ids.Count;
    }

    private sealed record CachedAnswerWithScore
    {
        public required string Id { get; init; }
        public required string Question { get; init; }
        public required string DocType { get; init; }
        public required string Answer { get; init; }
        public required List<SourceCitation> Sources { get; init; }
        public required List<string> DocumentIds { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public double Similarity { get; init; }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
