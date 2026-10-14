using System.Text.Json.Serialization;

namespace DocuRag.Core.Models;

/// <summary>
/// Un fragment de texte vectorisé, stocké dans le conteneur Cosmos <c>chunks</c>
/// (partition <c>/documentId</c>). L'<see cref="Id"/> est déterministe
/// (<c>{documentId}-{chunkIndex}</c>) : rejouer un message ne duplique pas les chunks
/// mais les ré-écrit à l'identique (idempotence, J10).
/// </summary>
public sealed class DocumentChunk
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    /// <summary>Position du chunk dans le document (0-based), pour l'ordre et l'id déterministe.</summary>
    [JsonPropertyName("chunkIndex")]
    public required int ChunkIndex { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }

    /// <summary>Numéro de la page d'où provient (majoritairement) le chunk — base des citations.</summary>
    [JsonPropertyName("page")]
    public required int Page { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Vecteur d'embedding (1536 dimensions pour <c>text-embedding-3-small</c>).
    /// Chemin indexé par la <c>vectorEmbeddingPolicy</c> du conteneur.
    /// </summary>
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}
