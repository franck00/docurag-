namespace DocuRag.Core.Chunking;

/// <summary>Texte brut extrait d'une page de PDF.</summary>
public sealed record PageText(int PageNumber, string Text);
