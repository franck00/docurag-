using System.Diagnostics;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Cosmos;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using DocuRag.Core.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocuRag.Ingestion;

/// <summary>
/// Event Grid (system topic du storage, filtré sur BlobCreated + .pdf) → enregistrement Cosmos "Pending" → message Service Bus.
/// La Function reste volontairement légère : le travail lourd (extraction, embeddings) est fait par le worker,
/// qui scale indépendamment sur la longueur de la queue.
/// </summary>
public sealed class OnBlobCreated(
    DocumentRepository documents,
    ServiceBusSender sender,
    IOptions<DocuRagOptions> options,
    ILogger<OnBlobCreated> logger)
{
    private readonly string _container = options.Value.Storage.Container;

    [Function(nameof(OnBlobCreated))]
    public async Task RunAsync([EventGridTrigger] EventGridEvent eventGridEvent, CancellationToken cancellationToken)
    {
        using var activity = DocuRagTelemetry.ActivitySource.StartActivity("ingestion.blob-created", ActivityKind.Consumer);
        activity?.SetTag("eventgrid.event_id", eventGridEvent.Id);

        if (eventGridEvent.EventType != SystemEventNames.StorageBlobCreated)
        {
            logger.LogInformation("Événement ignoré : type {EventType}", eventGridEvent.EventType);
            return;
        }

        if (!eventGridEvent.TryGetSystemEventData(out var data) || data is not StorageBlobCreatedEventData blob)
        {
            logger.LogWarning("Événement BlobCreated sans données exploitables : {EventId}", eventGridEvent.Id);
            return;
        }

        if (!BlobPath.TryParse(blob.Url, out var path) || path is null)
        {
            logger.LogWarning("URL de blob invalide : {Url}", blob.Url);
            return;
        }

        // Le filtre Event Grid fait déjà ce tri ; on revalide côté code (défense en profondeur, tests locaux).
        if (!string.Equals(path.Container, _container, StringComparison.Ordinal) || !path.IsPdf)
        {
            logger.LogInformation("Blob ignoré (hors conteneur {Container} ou pas un PDF) : {BlobName}", _container, path.BlobName);
            return;
        }

        var documentId = path.DocumentId;
        activity?.SetTag("docurag.document_id", documentId);
        activity?.SetTag("docurag.doc_type", path.DocType);

        var existing = await documents.GetAsync(documentId, cancellationToken);
        await documents.UpsertAsync(new DocumentRecord
        {
            Id = documentId,
            FileName = path.FileName,
            BlobName = path.BlobName,
            BlobUrl = blob.Url,
            DocType = path.DocType,
            ContentLength = blob.ContentLength,
            Status = DocumentStatus.Pending,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
        }, cancellationToken);

        var message = IngestionMessageSerializer.ToServiceBusMessage(new IngestionMessage
        {
            DocumentId = documentId,
            BlobUrl = blob.Url,
            BlobName = path.BlobName,
            DocType = path.DocType,
            EventId = eventGridEvent.Id,
        }, Activity.Current);

        await sender.SendMessageAsync(message, cancellationToken);

        logger.LogInformation("Document {DocumentId} ({BlobName}) en attente de traitement, message {MessageId} publié",
            documentId, path.BlobName, message.MessageId);
    }
}
