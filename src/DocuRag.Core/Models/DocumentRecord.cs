using System.Text.Json.Serialization;

namespace DocuRag.Core.Models;

/// <summary>
/// Enregistrement de métadonnées d'un document, stocké dans le conteneur Cosmos
/// <c>documents</c>. La partition est l'<see cref="Id"/> lui-même (un document =
/// une partition logique), ce qui rend les lectures de statut directes.
/// </summary>
public sealed class DocumentRecord
{
    /// <summary>Identifiant déterministe du document (dérivé du chemin du blob).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Nom du fichier d'origine, ex. <c>code-douanes-cemac.pdf</c>.</summary>
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>URL du blob source dans le Storage Account.</summary>
    [JsonPropertyName("blobUrl")]
    public required string BlobUrl { get; init; }

    /// <summary>
    /// Type/famille de document, utilisé comme filtre de métadonnées à la recherche
    /// (ex. <c>douanes-cemac</c>, <c>ohada</c>, <c>dgi</c>). Déduit du préfixe du blob.
    /// </summary>
    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }

    [JsonPropertyName("status")]
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    /// <summary>Nombre de chunks générés (renseigné une fois indexé).</summary>
    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; set; }

    /// <summary>Nombre de pages extraites du PDF.</summary>
    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    /// <summary>Message d'erreur si <see cref="Status"/> vaut <see cref="DocumentStatus.Failed"/>.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
