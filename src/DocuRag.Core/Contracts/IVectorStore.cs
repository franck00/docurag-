using DocuRag.Core.Models;

namespace DocuRag.Core.Contracts;

/// <summary>Un chunk retrouvé par la recherche vectorielle, avec son score de similarité.</summary>
/// <param name="Chunk">Le chunk stocké.</param>
/// <param name="Score">Similarité cosinus (1 = identique).</param>
public readonly record struct VectorSearchResult(DocumentChunk Chunk, double Score);

/// <summary>
/// Accès au stockage Cosmos : métadonnées de documents (conteneur <c>documents</c>)
/// et chunks vectorisés (conteneur <c>chunks</c>, recherche par <c>VectorDistance()</c>).
/// </summary>
public interface IVectorStore
{
    Task UpsertDocumentAsync(DocumentRecord document, CancellationToken cancellationToken = default);

    Task<DocumentRecord?> GetDocumentAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentRecord>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Écrit (ou ré-écrit, idempotence) un lot de chunks d'un même document.</summary>
    Task UpsertChunksAsync(
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche les <paramref name="topK"/> chunks les plus proches du vecteur de requête,
    /// avec un filtre optionnel sur le type de document.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        string? documentType = null,
        CancellationToken cancellationToken = default);
}
