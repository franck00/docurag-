using System.Text.Json;
using Azure.Messaging;
using Azure.Messaging.EventGrid.SystemEvents;
using DocuRag.Core.Documents;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocuRag.Ingestion;

/// <summary>
/// Declenchee par le system topic Event Grid du storage account (evenement
/// Microsoft.Storage.BlobCreated, filtre sur les blobs ".pdf"). Cree
/// l'enregistrement de suivi du document dans Cosmos (statut Pending) et
/// publie un message sur la queue Service Bus "doc-processing" pour que le
/// worker (J4) prenne le relais.
/// </summary>
public sealed class BlobUploadedFunction
{
    private const string BlobCreatedEventType = "Microsoft.Storage.BlobCreated";

    [Function(nameof(BlobUploaded))]
    public BlobUploadedOutput? BlobUploaded(
        [EventGridTrigger] CloudEvent cloudEvent,
        FunctionContext context)
    {
        var logger = context.GetLogger<BlobUploadedFunction>();

        if (!string.Equals(cloudEvent.Type, BlobCreatedEventType, StringComparison.Ordinal))
        {
            logger.LogInformation("Evenement ignore (type {EventType})", cloudEvent.Type);
            return null;
        }

        var eventData = cloudEvent.Data?.ToObjectFromJson<StorageBlobCreatedEventData>();
        if (eventData is null)
        {
            logger.LogWarning("Evenement BlobCreated sans donnees exploitables, ignore.");
            return null;
        }

        if (!eventData.Url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Blob ignore (pas un PDF) : {BlobUrl}", eventData.Url);
            return null;
        }

        var blobName = Uri.UnescapeDataString(new Uri(eventData.Url).Segments[^1]);
        var documentId = Guid.NewGuid().ToString("n");

        var document = new DocumentRecord
        {
            Id = documentId,
            BlobName = blobName,
            BlobUrl = eventData.Url,
            ContentType = eventData.ContentType ?? "application/pdf",
            Status = DocumentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var message = new DocumentProcessingMessage(documentId, blobName, eventData.Url);

        logger.LogInformation(
            "Document {DocumentId} ({BlobName}) enregistre en Pending, message publie sur doc-processing.",
            documentId,
            blobName);

        return new BlobUploadedOutput
        {
            Document = document,
            ServiceBusMessage = JsonSerializer.Serialize(message)
        };
    }
}

/// <summary>
/// Regroupe les deux sorties de la fonction : l'ecriture Cosmos et le message
/// Service Bus. Le worker isole ne supporte qu'une seule valeur de retour par
/// fonction, donc chaque binding de sortie est une propriete de cette classe.
/// </summary>
public sealed class BlobUploadedOutput
{
    [CosmosDBOutput(
        databaseName: "docurag",
        containerName: "documents",
        Connection = "CosmosConnection",
        CreateIfNotExists = true,
        PartitionKey = "/id")]
    public DocumentRecord? Document { get; set; }

    [ServiceBusOutput("doc-processing", Connection = "ServiceBusConnection")]
    public string? ServiceBusMessage { get; set; }
}
