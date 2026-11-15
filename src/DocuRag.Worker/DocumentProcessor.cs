using DocuRag.Core.AI;
using DocuRag.Core.Chunking;
using DocuRag.Core.Cosmos;
using DocuRag.Core.Models;
using DocuRag.Core.Pdf;
using DocuRag.Core.Telemetry;
using Azure.Core;
using Azure.Storage.Blobs;

namespace DocuRag.Worker;

/// <summary>
/// Traitement d'un document : blob → texte (PdfPig) → extraits → embeddings par lots → Cosmos.
/// Idempotent : ids d'extraits déterministes + upsert + suppression des extraits orphelins.
/// </summary>
public sealed class DocumentProcessor(
    TokenCredential credential,
    DocumentRepository documents,
    ChunkRepository chunks,
    ParagraphChunker chunker,
    ITextEmbedder embedder,
    ILogger<DocumentProcessor> logger)
{
    public async Task<int> ProcessAsync(IngestionMessage message, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(message.DocumentId, cancellationToken)
                       ?? NewRecord(message);
        await documents.UpsertAsync(document with { Status = DocumentStatus.Processing, Error = null }, cancellationToken);

        var pdf = await DownloadAsync(message.BlobUrl, cancellationToken);
        var pages = PdfTextExtractor.Extract(pdf);
        var textChunks = chunker.Chunk(pages);
        logger.LogInformation("{FileName} : {PageCount} pages, {ChunkCount} extraits", document.FileName, pages.Count, textChunks.Count);

        var vectors = await embedder.EmbedAsync(textChunks.Select(c => c.Text).ToList(), cancellationToken);

        var records = textChunks.Select((chunk, i) => new ChunkRecord
        {
            Id = ChunkRecord.BuildId(message.DocumentId, chunk.Index),
            DocumentId = message.DocumentId,
            ChunkIndex = chunk.Index,
            FileName = document.FileName,
            DocType = message.DocType,
            PageStart = chunk.PageStart,
            PageEnd = chunk.PageEnd,
            Content = chunk.Text,
            TokenCount = chunk.TokenCount,
            Embedding = vectors[i],
        }).ToList();

        await chunks.UpsertAsync(message.DocumentId, records, cancellationToken);
        var removed = await chunks.DeleteFromIndexAsync(message.DocumentId, records.Count, cancellationToken);
        if (removed > 0)
        {
            logger.LogInformation("{Removed} extraits d'une version précédente supprimés", removed);
        }

        await documents.UpsertAsync(document with
        {
            Status = DocumentStatus.Indexed,
            PageCount = pages.Count,
            ChunkCount = records.Count,
            Error = null,
        }, cancellationToken);

        DocuRagTelemetry.ChunksGenerated.Add(records.Count, new KeyValuePair<string, object?>("doc_type", message.DocType));
        return records.Count;
    }

    public async Task MarkFailedAsync(IngestionMessage message, string error, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(message.DocumentId, cancellationToken) ?? NewRecord(message);
        await documents.UpsertAsync(document with { Status = DocumentStatus.Failed, Error = error }, cancellationToken);
    }

    private async Task<byte[]> DownloadAsync(string blobUrl, CancellationToken cancellationToken)
    {
        var blob = new BlobClient(new Uri(blobUrl), credential);
        var content = await blob.DownloadContentAsync(cancellationToken);
        return content.Value.Content.ToArray();
    }

    private static DocumentRecord NewRecord(IngestionMessage message)
    {
        BlobPath.TryParse(message.BlobUrl, out var path);
        return new DocumentRecord
        {
            Id = message.DocumentId,
            FileName = path?.FileName ?? message.BlobName,
            BlobName = message.BlobName,
            BlobUrl = message.BlobUrl,
            DocType = message.DocType,
        };
    }
}
