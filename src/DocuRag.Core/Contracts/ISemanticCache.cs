using DocuRag.Core.Models;

namespace DocuRag.Core.Contracts;

/// <summary>
/// Cache sémantique des réponses de <c>/ask</c> (J13) : avant d'appeler le LLM, on cherche une
/// question déjà posée dont l'embedding est assez proche (similarité cosinus ≥ seuil).
/// Le filtre <c>documentType</c> et le <c>topK</c> font partie de la clé : une même question
/// posée sur un autre périmètre n'est jamais servie depuis le cache.
/// </summary>
public interface ISemanticCache
{
    /// <summary>Réponse en cache pour une question proche, ou <c>null</c>.</summary>
    Task<AskResponse?> TryGetAsync(
        float[] questionVector,
        string? documentType,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>Mémorise la réponse générée pour cette question.</summary>
    Task SetAsync(
        float[] questionVector,
        string? documentType,
        int topK,
        AskResponse response,
        CancellationToken cancellationToken = default);
}
