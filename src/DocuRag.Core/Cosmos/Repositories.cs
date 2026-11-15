using System.Net;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Core.Cosmos;

public sealed class DocumentRepository(CosmosClient client, IOptions<DocuRagOptions> options)
{
    private readonly Container _container = client.GetContainer(
        options.Value.Cosmos.Database, options.Value.Cosmos.DocumentsContainer);

    public async Task<DocumentRecord?> GetAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<DocumentRecord>(id, new PartitionKey(id), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task UpsertAsync(DocumentRecord document, CancellationToken cancellationToken) =>
        _container.UpsertItemAsync(document with { UpdatedAt = DateTimeOffset.UtcNow }, new PartitionKey(document.Id),
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.updatedAt DESC");
        return await ReadAllAsync(_container.GetItemQueryIterator<DocumentRecord>(query), cancellationToken);
    }

    internal static async Task<IReadOnlyList<T>> ReadAllAsync<T>(FeedIterator<T> iterator, CancellationToken cancellationToken)
    {
        var items = new List<T>();
        using (iterator)
        {
            while (iterator.HasMoreResults)
            {
                items.AddRange(await iterator.ReadNextAsync(cancellationToken));
            }
        }

        return items;
    }
}

public sealed class ChunkRepository(CosmosClient client, IOptions<DocuRagOptions> options)
{
    // Un TransactionalBatch est limité à 100 opérations et 2 Mo ; un extrait avec son vecteur pèse ~20 Ko.
    private const int BatchSize = 40;

    private readonly Container _container = client.GetContainer(
        options.Value.Cosmos.Database, options.Value.Cosmos.ChunksContainer);

    /// <summary>
    /// Upsert idempotent : les ids sont déterministes, un message rejoué réécrit les mêmes extraits.
    /// Tous les extraits d'un document partagent la même partition → batch transactionnel.
    /// </summary>
    public async Task UpsertAsync(string documentId, IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKey(documentId);
        foreach (var group in chunks.Chunk(BatchSize))
        {
            var batch = _container.CreateTransactionalBatch(partitionKey);
            foreach (var chunk in group)
            {
                batch.UpsertItem(chunk);
            }

            using var response = await batch.ExecuteAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CosmosBatchException(documentId, response.StatusCode, response.ErrorMessage);
            }
        }
    }

    /// <summary>Supprime les extraits d'une version précédente plus longue du même document.</summary>
    public async Task<int> DeleteFromIndexAsync(string documentId, int firstIndexToDelete, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT VALUE c.id FROM c WHERE c.documentId = @documentId AND c.chunkIndex >= @index")
            .WithParameter("@documentId", documentId)
            .WithParameter("@index", firstIndexToDelete);
        var partitionKey = new PartitionKey(documentId);
        var ids = await DocumentRepository.ReadAllAsync(
            _container.GetItemQueryIterator<string>(query, requestOptions: new QueryRequestOptions { PartitionKey = partitionKey }),
            cancellationToken);

        foreach (var id in ids)
        {
            await _container.DeleteItemAsync<object>(id, partitionKey, cancellationToken: cancellationToken);
        }

        return ids.Count;
    }

    /// <summary>
    /// Recherche vectorielle : <c>VectorDistance()</c> en cosinus renvoie une similarité (plus grand = plus proche),
    /// d'où le tri croissant de la distance par défaut côté moteur. Filtre optionnel par type de document.
    /// </summary>
    public async Task<IReadOnlyList<ChunkHit>> SearchAsync(float[] embedding, int topK, string? docType, CancellationToken cancellationToken)
    {
        var where = string.IsNullOrWhiteSpace(docType) ? string.Empty : "WHERE c.docType = @docType ";
        var sql = "SELECT TOP @topK c.id, c.documentId, c.fileName, c.docType, c.pageStart, c.pageEnd, c.content, " +
                  "VectorDistance(c.embedding, @embedding) AS score FROM c " +
                  where +
                  "ORDER BY VectorDistance(c.embedding, @embedding)";

        var query = new QueryDefinition(sql)
            .WithParameter("@topK", topK)
            .WithParameter("@embedding", embedding);
        if (!string.IsNullOrWhiteSpace(docType))
        {
            query = query.WithParameter("@docType", docType.Trim().ToLowerInvariant());
        }

        return await DocumentRepository.ReadAllAsync(_container.GetItemQueryIterator<ChunkHit>(query), cancellationToken);
    }
}

public sealed class CosmosBatchException(string documentId, HttpStatusCode status, string? message)
    : Exception($"Échec du batch Cosmos pour le document {documentId} ({(int)status}) : {message}")
{
    public HttpStatusCode Status { get; } = status;
}
