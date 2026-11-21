using System.Diagnostics;
using DocuRag.Core.Chunking;
using DocuRag.Core.Contracts;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocuRag.Infrastructure.Processing;

/// <summary>
/// Orchestration du traitement d'un document : téléchargement → extraction → chunking
/// → embeddings (par lots) → écriture Cosmos → passage à <c>Indexed</c>.
///
/// Idempotent : les chunks ont un id déterministe (<c>{documentId}-{index}</c>), donc
/// rejouer un message ré-écrit les mêmes chunks au lieu d'en créer des doublons (J10).
/// En cas d'échec, le document passe à <c>Failed</c> et l'exception est propagée pour que
/// l'appelant (worker) décide d'abandonner ou de dead-letter.
/// </summary>
public sealed class DocumentProcessor
{
    private const int EmbeddingBatchSize = 16;

    private static readonly ActivitySource ActivitySource = new("DocuRag.Worker");

    private readonly IDocumentDownloader _downloader;
    private readonly IPdfTextExtractor _extractor;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly IVectorStore _store;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        IDocumentDownloader downloader,
        IPdfTextExtractor extractor,
        IEmbeddingGenerator embeddings,
        IVectorStore store,
        ILogger<DocumentProcessor> logger)
    {
        _downloader = downloader;
        _extractor = extractor;
        _embeddings = embeddings;
        _store = store;
        _logger = logger;
    }

    public async Task ProcessAsync(DocumentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var activity = ActivitySource.StartActivity("ProcessDocument");
        activity?.SetTag("document.id", message.DocumentId);
        activity?.SetTag("document.type", message.DocumentType);

        var record = await _store.GetDocumentAsync(message.DocumentId, cancellationToken)
                     ?? new DocumentRecord
                     {
                         Id = message.DocumentId,
                         FileName = message.FileName,
                         BlobUrl = message.BlobUrl,
                         DocumentType = message.DocumentType,
                     };

        try
        {
            record.Status = DocumentStatus.Processing;
            record.Error = null;
            await _store.UpsertDocumentAsync(record, cancellationToken);

            var bytes = await _downloader.DownloadAsync(message.BlobUrl, cancellationToken);
            var pages = _extractor.Extract(bytes);
            var chunks = TextChunker.Chunk(pages);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Aucun texte exploitable extrait de {message.FileName} (PDF image ou vide ?).");
            }

            var documentChunks = await BuildChunksAsync(message, chunks, cancellationToken);
            await _store.UpsertChunksAsync(documentChunks, cancellationToken);

            record.Status = DocumentStatus.Indexed;
            record.PageCount = pages.Count;
            record.ChunkCount = documentChunks.Count;
            await _store.UpsertDocumentAsync(record, cancellationToken);

            activity?.SetTag("document.chunks", documentChunks.Count);
            DocumentProcessingMetrics.ChunksGenerated.Add(
                documentChunks.Count,
                new KeyValuePair<string, object?>(DocumentProcessingMetrics.DocumentTypeTag, message.DocumentType));
            RecordProcessed(message, "indexed");
            _logger.LogInformation(
                "Document {DocumentId} indexé : {Pages} pages, {Chunks} chunks.",
                message.DocumentId, pages.Count, documentChunks.Count);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordProcessed(message, "failed");
            record.Status = DocumentStatus.Failed;
            record.Error = ex.Message;
            await SafeMarkFailedAsync(record, cancellationToken);
            _logger.LogError(ex, "Échec du traitement de {DocumentId}.", message.DocumentId);
            throw;
        }
    }

    private async Task<List<DocumentChunk>> BuildChunksAsync(
        DocumentMessage message, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken)
    {
        var result = new List<DocumentChunk>(chunks.Count);

        // Embeddings par lots : un appel réseau pour EmbeddingBatchSize chunks, pas un par chunk.
        for (var start = 0; start < chunks.Count; start += EmbeddingBatchSize)
        {
            var batch = chunks.Skip(start).Take(EmbeddingBatchSize).ToList();
            var inputs = batch.Select(c => c.Content).ToList();

            var startedAt = Stopwatch.GetTimestamp();
            var vectors = await _embeddings.EmbedBatchAsync(inputs, cancellationToken);
            DocumentProcessingMetrics.EmbeddingDuration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                new KeyValuePair<string, object?>(DocumentProcessingMetrics.DocumentTypeTag, message.DocumentType));

            for (var i = 0; i < batch.Count; i++)
            {
                var index = start + i;
                result.Add(new DocumentChunk
                {
                    Id = $"{message.DocumentId}-{index}",
                    DocumentId = message.DocumentId,
                    ChunkIndex = index,
                    FileName = message.FileName,
                    DocumentType = message.DocumentType,
                    Page = batch[i].Page,
                    Content = batch[i].Content,
                    Embedding = vectors[i],
                });
            }
        }

        return result;
    }

    private static void RecordProcessed(DocumentMessage message, string outcome) =>
        DocumentProcessingMetrics.DocumentsProcessed.Add(
            1,
            new KeyValuePair<string, object?>(DocumentProcessingMetrics.OutcomeTag, outcome),
            new KeyValuePair<string, object?>(DocumentProcessingMetrics.DocumentTypeTag, message.DocumentType));

    private async Task SafeMarkFailedAsync(DocumentRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _store.UpsertDocumentAsync(record, cancellationToken);
        }
        catch (Exception ex)
        {
            // Ne pas masquer l'erreur d'origine si même la mise à jour du statut échoue.
            _logger.LogWarning(ex, "Impossible de marquer {DocumentId} en échec.", record.Id);
        }
    }
}
