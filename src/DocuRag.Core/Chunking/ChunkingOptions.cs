namespace DocuRag.Core.Chunking;

/// <summary>
/// Paramètres de découpage. Les cibles sont exprimées en tokens estimés
/// (approximation ~1,3 token par mot, suffisante pour dimensionner les chunks).
/// </summary>
public sealed class ChunkingOptions
{
    /// <summary>Taille cible d'un chunk, en tokens estimés.</summary>
    public int TargetTokens { get; init; } = 800;

    /// <summary>Chevauchement entre deux chunks consécutifs, en tokens estimés.</summary>
    public int OverlapTokens { get; init; } = 100;

    public static ChunkingOptions Default { get; } = new();
}
