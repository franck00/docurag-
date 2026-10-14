using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocuRag.Infrastructure.Storage;

/// <summary>Extraction de texte PDF avec PdfPig, page par page.</summary>
public sealed class PdfTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<PageText> Extract(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var document = PdfDocument.Open(pdfBytes);
        var pages = new List<PageText>(document.NumberOfPages);

        foreach (Page page in document.GetPages())
        {
            // ContentOrderTextExtractor reconstitue un ordre de lecture correct,
            // y compris sur des mises en page à colonnes fréquentes dans les textes de loi.
            var text = ContentOrderTextExtractor.GetText(page);
            pages.Add(new PageText(page.Number, text ?? string.Empty));
        }

        return pages;
    }
}
