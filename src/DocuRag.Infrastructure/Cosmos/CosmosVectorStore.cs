using System.Net;
using System.Text.Json.Serialization;
using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Infrastructure.Cosmos;

/// <summary>
/// Implémentation <see cref="IVectorStore"/> sur Cosmos DB for NoSQL.
/// Les chunks sont écrits dans le conteneur <c>chunks</c> (partition <c>/documentId</c>)
/// et la recherche utilise <c>VectorDistance()</c> sur l'index DiskANN du chemin
/// <c>/embedding</c>.
/// </summary>
public sealed class CosmosVectorStore : IVectorStore
{
    private readonly Container _documents;
    private readonly Container _chunks;

    public CosmosVectorStore(CosmosClient client, IOptions<CosmosOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        var o = options.Value;
        _documents = client.GetContainer(o.Database, o.DocumentsContainer);
        _chunks = client.GetContainer(o.Database, o.ChunksContainer);
    }

    public async Task UpsertDocumentAsync(
        DocumentRecord document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await _documents.UpsertItemAsync(
            document, new PartitionKey(document.Id), cancellationToken: cancellationToken);
    }

    public async Task<DocumentRecord?> GetDocumentAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _documents.ReadItemAsync<DocumentRecord>(
                id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DocumentRecord>> ListDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.createdAt DESC");
        var results = new List<DocumentRecord>();
        using var iterator = _documents.GetItemQueryIterator<DocumentRecord>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }

    public async Task UpsertChunksAsync(
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            return;
        }

        // Bulk execution activé sur le client : des upserts concurrents sont regroupés
        // par le SDK. L'id déterministe garantit l'idempotence (ré-écriture, pas doublon).
        var tasks = chunks.Select(chunk =>
            _chunks.UpsertItemAsync(
                chunk, new PartitionKey(chunk.DocumentId), cancellationToken: cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        string? documentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);

        var filter = string.IsNullOrWhiteSpace(documentType)
            ? string.Empty
            : "WHERE c.documentType = @type";

        // ORDER BY VectorDistance(...) renvoie les chunks du plus similaire au moins similaire.
        var sql =
            $"""
             SELECT TOP @k c.id, c.documentId, c.chunkIndex, c.fileName, c.documentType,
                    c.page, c.content, VectorDistance(c.embedding, @vec) AS score
             FROM c
             {filter}
             ORDER BY VectorDistance(c.embedding, @vec)
             """;

        var query = new QueryDefinition(sql)
            .WithParameter("@k", topK)
            .WithParameter("@vec", queryVector);

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            query = query.WithParameter("@type", documentType);
        }

        var results = new List<VectorSearchResult>(topK);
        using var iterator = _chunks.GetItemQueryIterator<ChunkHit>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var hit in page)
            {
                results.Add(new VectorSearchResult(
                    new DocumentChunk
                    {
                        Id = hit.Id,
                        DocumentId = hit.DocumentId,
                        ChunkIndex = hit.ChunkIndex,
                        FileName = hit.FileName,
                        DocumentType = hit.DocumentType,
                        Page = hit.Page,
                        Content = hit.Content,
                    },
                    hit.Score));
            }
        }

        return results;
    }

    /// <summary>Projection de la requête vectorielle (sans le vecteur, inutile en retour).</summary>
    private sealed class ChunkHit
    {
        [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
        [JsonPropertyName("documentId")] public string DocumentId { get; init; } = string.Empty;
        [JsonPropertyName("chunkIndex")] public int ChunkIndex { get; init; }
        [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
        [JsonPropertyName("documentType")] public string DocumentType { get; init; } = string.Empty;
        [JsonPropertyName("page")] public int Page { get; init; }
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
        [JsonPropertyName("score")] public double Score { get; init; }
    }
}
