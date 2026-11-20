using DocuRag.Core.Models;

namespace DocuRag.Core.Contracts;

/// <summary>Extrait le texte d'un PDF, page par page (implémentation : PdfPig).</summary>
public interface IPdfTextExtractor
{
    /// <summary>Renvoie le texte de chaque page, dans l'ordre, numéros 1-based.</summary>
    IReadOnlyList<PageText> Extract(byte[] pdfBytes);
}
