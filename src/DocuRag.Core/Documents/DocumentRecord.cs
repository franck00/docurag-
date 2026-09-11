using System.Text.Json.Serialization;

namespace DocuRag.Core.Documents;

/// <summary>
/// Enregistrement de suivi d'un document dans le conteneur Cosmos "documents"
/// (partition key /id). Distinct du futur conteneur "chunks" (J5) qui portera
/// les vecteurs d'embedding.
/// </summary>
public sealed class DocumentRecord
{
    /// <summary>
    /// Cosmos exige une propriete "id" en minuscule : on la mappe explicitement
    /// pour pouvoir garder du PascalCase partout ailleurs en C#.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    public required string BlobName { get; set; }

    public required string BlobUrl { get; set; }

    public required string ContentType { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? IndexedAt { get; set; }

    public string? FailureReason { get; set; }
}
