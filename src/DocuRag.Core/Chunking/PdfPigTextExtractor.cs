using UglyToad.PdfPig;

namespace DocuRag.Core.Chunking;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<PageText> ExtractPages(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);

        return document.GetPages()
            .Select(page => new PageText(page.Number, page.Text))
            .ToList();
    }
}
