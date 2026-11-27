using System.Text.Json.Serialization;

namespace DocuRag.Core.Models;

/// <summary>
/// Un message en dead-letter queue, tel que restitué par <c>GET /dead-letters</c> (J10) :
/// de quoi comprendre pourquoi un document n'a pas été indexé sans ouvrir le portail.
/// </summary>
public sealed class DeadLetterEntry
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; init; }

    /// <summary>Identifiant du document, si le corps du message est lisible.</summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    /// <summary>Motif posé par le worker (<c>DeserializationError</c>, <c>MaxDeliveryCountExceeded</c>…).</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; init; }

    [JsonPropertyName("deliveryCount")]
    public int DeliveryCount { get; init; }

    [JsonPropertyName("enqueuedAt")]
    public DateTimeOffset EnqueuedAt { get; init; }
}
