using DocuRag.Core.Chunking;
using DocuRag.Infrastructure.Storage;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

/// <summary>
/// Vérifie l'intégration réelle de PdfPig sur un vrai PDF réglementaire public
/// (acte uniforme OHADA), bundlé comme fixture. Valide que l'extraction renvoie
/// du texte exploitable et que le chunking en tire des chunks paginés.
/// </summary>
public class PdfTextExtractorTests
{
    private static byte[] SamplePdf()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "ohada-sample.pdf");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Extract_RealPdf_ReturnsPagesWithText()
    {
        var extractor = new PdfTextExtractor();

        var pages = extractor.Extract(SamplePdf());

        Assert.NotEmpty(pages);
        Assert.Contains(pages, p => !string.IsNullOrWhiteSpace(p.Text));
        Assert.All(pages, p => Assert.True(p.Page >= 1));
    }

    [Fact]
    public void Extract_ThenChunk_ProducesPagedChunks()
    {
        var pages = new PdfTextExtractor().Extract(SamplePdf());

        var chunks = TextChunker.Chunk(pages);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Content));
            Assert.True(c.Page >= 1);
        });
    }

    [Fact]
    public void Extract_InvalidBytes_Throws()
    {
        var extractor = new PdfTextExtractor();
        Assert.ThrowsAny<Exception>(() => extractor.Extract([0, 1, 2, 3, 4]));
    }
}
