using System.Text.Json.Serialization;

namespace DocuRag.Core.Messaging;

/// <summary>
/// Message publié par la Function d'ingestion sur la queue Service Bus
/// <c>doc-processing</c> et consommé par le worker. Contient tout ce qu'il faut
/// pour retrouver le blob et écrire dans Cosmos, sans relire l'événement d'origine.
/// </summary>
public sealed class DocumentMessage
{
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("blobUrl")]
    public required string BlobUrl { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }
}
