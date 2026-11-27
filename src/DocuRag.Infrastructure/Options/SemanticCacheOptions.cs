using System.ComponentModel.DataAnnotations;

namespace DocuRag.Infrastructure.Options;

/// <summary>
/// Configuration du cache sémantique de <c>/ask</c> (J13). Désactivé par défaut : s'active
/// par <c>SemanticCache__Enabled=true</c> (variable d'environnement ou App Configuration).
/// </summary>
public sealed class SemanticCacheOptions
{
    public const string SectionName = "SemanticCache";

    public bool Enabled { get; set; }

    /// <summary>Similarité cosinus minimale pour considérer deux questions identiques (plan : 0,95).</summary>
    [Range(0.5, 1.0)]
    public double SimilarityThreshold { get; set; } = 0.95;

    /// <summary>
    /// Durée de vie d'une entrée. C'est aussi le mécanisme d'invalidation : une réponse ne
    /// survit pas plus longtemps que ça à une ré-indexation du corpus.
    /// </summary>
    [Range(1, 1440)]
    public int TtlMinutes { get; set; } = 30;

    /// <summary>Nombre maximal d'entrées en mémoire (les plus anciennes sont évincées).</summary>
    [Range(1, 10_000)]
    public int MaxEntries { get; set; } = 500;
}
