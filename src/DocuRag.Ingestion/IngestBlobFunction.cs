using System.Text.Json;
using Azure.Messaging;
using DocuRag.Core.Contracts;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocuRag.Ingestion;

/// <summary>
/// Déclenchée par Event Grid sur <c>Microsoft.Storage.BlobCreated</c> (filtré sur
/// <c>.pdf</c> côté abonnement). Crée l'enregistrement du document en <c>Pending</c>
/// dans Cosmos puis publie un message sur la queue Service Bus.
/// </summary>
public sealed class IngestBlobFunction
{
    private readonly IVectorStore _store;
    private readonly IDocumentQueue _queue;
    private readonly ILogger<IngestBlobFunction> _logger;

    public IngestBlobFunction(
        IVectorStore store, IDocumentQueue queue, ILogger<IngestBlobFunction> logger)
    {
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    [Function("IngestBlob")]
    public async Task Run(
        [EventGridTrigger] CloudEvent cloudEvent, CancellationToken cancellationToken)
    {
        var blobUrl = ExtractBlobUrl(cloudEvent);
        if (blobUrl is null)
        {
            _logger.LogWarning("Événement sans URL de blob exploitable : {Type}", cloudEvent.Type);
            return;
        }

        if (!blobUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Blob ignoré (pas un PDF) : {Blob}", blobUrl);
            return;
        }

        var blobPath = DocumentIdentity.ToBlobPath(blobUrl);
        if (blobPath is null)
        {
            // URL malformée : la relancer ne changerait rien, on l'ignore plutôt que de
            // laisser l'exception déclencher les retries Event Grid.
            _logger.LogWarning("URL de blob invalide, événement ignoré : {Blob}", blobUrl);
            return;
        }

        var documentId = DocumentIdentity.ToDocumentId(blobPath);
        var documentType = DocumentIdentity.ToDocumentType(blobPath);
        var fileName = DocumentIdentity.ToFileName(blobPath);

        var record = new DocumentRecord
        {
            Id = documentId,
            FileName = fileName,
            BlobUrl = blobUrl,
            DocumentType = documentType,
            Status = DocumentStatus.Pending,
        };
        await _store.UpsertDocumentAsync(record, cancellationToken);

        await _queue.EnqueueAsync(
            new DocumentMessage
            {
                DocumentId = documentId,
                BlobUrl = blobUrl,
                FileName = fileName,
                DocumentType = documentType,
            },
            cancellationToken);

        _logger.LogInformation(
            "Document {DocumentId} ({Type}) mis en file depuis {Blob}.",
            documentId, documentType, blobUrl);
    }

    /// <summary>Lit l'URL du blob dans la donnée de l'événement <c>BlobCreated</c>.</summary>
    private static string? ExtractBlobUrl(CloudEvent cloudEvent)
    {
        if (cloudEvent.Data is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(cloudEvent.Data);
        return doc.RootElement.TryGetProperty("url", out var url)
            ? url.GetString()
            : null;
    }
}
