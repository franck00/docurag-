namespace DocuRag.Core.Contracts;

/// <summary>
/// Génère des embeddings. L'implémentation (Azure OpenAI) doit batcher les entrées :
/// un seul appel réseau pour plusieurs textes, pas un appel par chunk.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>Dimension des vecteurs produits (1536 pour <c>text-embedding-3-small</c>).</summary>
    int Dimensions { get; }

    /// <summary>
    /// Vectorise un lot de textes. L'ordre de sortie correspond à l'ordre d'entrée.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);

    /// <summary>Vectorise un texte unique (ex. la question posée à l'API).</summary>
    Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default);
}
