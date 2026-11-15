using DocuRag.Core.Chunking;
using DocuRag.Core.Options;
using DocuRag.Core.Pdf;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DocuRag.Core.Tests;

public class PdfTextExtractorTests
{
    private static byte[] BuildPdf(params string[][] pagesOfLines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pagesOfLines)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780;
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    y -= 30; // ligne vide = espace vertical = nouveau paragraphe
                    continue;
                }

                page.AddText(line, 11, new PdfPoint(50, y), font);
                y -= 14;
            }
        }

        return builder.Build();
    }

    [Fact]
    public void Extracts_text_page_by_page_with_page_numbers()
    {
        var pdf = BuildPdf(
            ["Article premier", "Le present code s'applique au territoire douanier."],
            ["Article 2", "Les marchandises sont soumises au controle."]);

        var pages = PdfTextExtractor.Extract(pdf);

        Assert.Equal(2, pages.Count);
        Assert.Equal(1, pages[0].PageNumber);
        Assert.Equal(2, pages[1].PageNumber);
        Assert.Contains("Article premier", pages[0].Text);
        Assert.Contains("territoire douanier.", pages[0].Text);
        Assert.Contains("Article 2", pages[1].Text);
        Assert.DoesNotContain("Article 2", pages[0].Text);
    }

    [Fact]
    public void Vertical_gaps_become_paragraph_breaks()
    {
        var pdf = BuildPdf(["Premier paragraphe ligne un", "ligne deux", "", "Second paragraphe"]);

        var text = Assert.Single(PdfTextExtractor.Extract(pdf)).Text;
        var paragraphs = text.Split("\n\n");

        Assert.Equal(2, paragraphs.Length);
        Assert.Contains("ligne deux", paragraphs[0]);
        Assert.Equal("Second paragraphe", paragraphs[1]);
    }

    [Fact]
    public void Extracted_pages_feed_the_chunker_and_keep_page_numbers()
    {
        var pdf = BuildPdf(["Page un"], ["Page deux"], ["Page trois"]);
        var chunks = new ParagraphChunker(new ChunkingOptions()).Chunk(PdfTextExtractor.Extract(pdf));

        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.PageStart);
        Assert.Equal(3, chunk.PageEnd);
    }

    [Fact]
    public void Random_bytes_are_rejected_as_invalid_pdf()
    {
        var garbage = new byte[2048];
        new Random(42).NextBytes(garbage);
        Assert.Throws<InvalidPdfException>(() => PdfTextExtractor.Extract(garbage));
    }

    [Fact]
    public void Pdf_header_followed_by_garbage_is_rejected_as_invalid_pdf()
    {
        var garbage = new byte[2048];
        new Random(7).NextBytes(garbage);
        var corrupted = "%PDF-1.7\n"u8.ToArray().Concat(garbage).ToArray();
        Assert.Throws<InvalidPdfException>(() => PdfTextExtractor.Extract(corrupted));
    }

    [Fact]
    public void Pdf_without_text_is_rejected()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var ex = Assert.Throws<InvalidPdfException>(() => PdfTextExtractor.Extract(builder.Build()));
        Assert.Contains("OCR", ex.Message);
    }
}
