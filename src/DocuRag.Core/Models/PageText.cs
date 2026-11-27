namespace DocuRag.Core.Models;

/// <summary>
/// Texte brut d'une page de PDF, tel que renvoyé par l'extracteur (PdfPig).
/// On garde le numéro de page pour pouvoir l'attacher à chaque chunk.
/// </summary>
/// <param name="Page">Numéro de page, 1-based.</param>
/// <param name="Text">Texte extrait de la page.</param>
public readonly record struct PageText(int Page, string Text);
