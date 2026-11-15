using System.Text.Json.Serialization;

namespace DocuRag.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<DocumentStatus>))]
public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed,
}

/// <summary>
/// Un document source (un PDF déposé dans le conteneur blob). Conteneur Cosmos <c>documents</c>, clé de partition <c>/id</c>.
/// </summary>
public sealed record DocumentRecord
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string BlobName { get; init; }
    public required string BlobUrl { get; init; }
    public required string DocType { get; init; }
    public DocumentStatus Status { get; init; } = DocumentStatus.Pending;
    public long? ContentLength { get; init; }
    public int? PageCount { get; init; }
    public int? ChunkCount { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Un extrait vectorisé. Conteneur Cosmos <c>chunks</c>, clé de partition <c>/documentId</c>,
/// index vectoriel DiskANN sur <c>/embedding</c> (1536 dimensions, cosinus).
/// L'id est déterministe (<c>{documentId}-{chunkIndex:D5}</c>) : rejouer un message écrase au lieu de dupliquer.
/// </summary>
public sealed record ChunkRecord
{
    public required string Id { get; init; }
    public required string DocumentId { get; init; }
    public required int ChunkIndex { get; init; }
    public required string FileName { get; init; }
    public required string DocType { get; init; }
    public required int PageStart { get; init; }
    public required int PageEnd { get; init; }
    public required string Content { get; init; }
    public required int TokenCount { get; init; }
    public required float[] Embedding { get; init; }
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;

    public static string BuildId(string documentId, int chunkIndex) => $"{documentId}-{chunkIndex:D5}";
}

/// <summary>Résultat de la recherche vectorielle (projection, sans le vecteur).</summary>
public sealed record ChunkHit
{
    public required string Id { get; init; }
    public required string DocumentId { get; init; }
    public required string FileName { get; init; }
    public required string DocType { get; init; }
    public required int PageStart { get; init; }
    public required int PageEnd { get; init; }
    public required string Content { get; init; }
    public double Score { get; init; }
}

/// <summary>Message publié sur la queue Service Bus <c>doc-processing</c> par la Function d'ingestion.</summary>
public sealed record IngestionMessage
{
    public required string DocumentId { get; init; }
    public required string BlobUrl { get; init; }
    public required string BlobName { get; init; }
    public required string DocType { get; init; }
    public string? EventId { get; init; }
}

/// <summary>Texte d'une page PDF (numérotation à partir de 1).</summary>
public sealed record PageText(int PageNumber, string Text);

/// <summary>Un extrait produit par le chunker, avant vectorisation.</summary>
public sealed record TextChunk(int Index, string Text, int PageStart, int PageEnd, int TokenCount);

/// <summary>Une source citée dans une réponse.</summary>
public sealed record SourceCitation
{
    public required int Rank { get; init; }
    public required string DocumentId { get; init; }
    public required string FileName { get; init; }
    public required string DocType { get; init; }
    public required int PageStart { get; init; }
    public required int PageEnd { get; init; }
    public required double Score { get; init; }
    public required string Excerpt { get; init; }
}
