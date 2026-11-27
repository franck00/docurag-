using DocuRag.Core.Models;
using Xunit;

namespace DocuRag.Core.Tests;

public class DocumentIdentityTests
{
    [Fact]
    public void ToDocumentId_IsDeterministic()
    {
        var a = DocumentIdentity.ToDocumentId("ohada/acte-uniforme.pdf");
        var b = DocumentIdentity.ToDocumentId("ohada/acte-uniforme.pdf");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToDocumentId_IsValidCosmosId()
    {
        var id = DocumentIdentity.ToDocumentId("ohada/Acte Uniforme (2024).pdf");
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('\\', id);
        Assert.DoesNotContain('#', id);
        Assert.DoesNotContain('?', id);
    }

    [Fact]
    public void ToDocumentId_DifferentPaths_DifferentIds()
    {
        var a = DocumentIdentity.ToDocumentId("ohada/doc.pdf");
        var b = DocumentIdentity.ToDocumentId("dgi/doc.pdf");
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("ohada/acte.pdf", "ohada")]
    [InlineData("douanes-cemac/code.pdf", "douanes-cemac")]
    [InlineData("racine.pdf", "unknown")]
    public void ToDocumentType_UsesFirstSegment(string path, string expected)
    {
        Assert.Equal(expected, DocumentIdentity.ToDocumentType(path));
    }

    [Theory]
    [InlineData("OHADA", "ohada")]
    [InlineData(" Dgi ", "dgi")]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void NormalizeDocumentTypeFilter_MatchesStoredFormat(string? input, string? expected)
    {
        Assert.Equal(expected, DocumentIdentity.NormalizeDocumentTypeFilter(input));
    }

    [Fact]
    public void NormalizeDocumentTypeFilter_RoundTripsWithToDocumentType()
    {
        var stored = DocumentIdentity.ToDocumentType("Douanes-CEMAC/code.pdf");
        Assert.Equal(stored, DocumentIdentity.NormalizeDocumentTypeFilter("DOUANES-cemac"));
    }

    [Theory]
    [InlineData("ohada/sous/acte.pdf", "acte.pdf")]
    [InlineData("racine.pdf", "racine.pdf")]
    public void ToFileName_UsesLastSegment(string path, string expected)
    {
        Assert.Equal(expected, DocumentIdentity.ToFileName(path));
    }

    [Theory]
    [InlineData("https://st.blob.core.windows.net/documents/ohada/acte.pdf", "ohada/acte.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents/racine.pdf", "racine.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents/ohada/sous/acte.pdf", "ohada/sous/acte.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents/ohada/Acte%20Uniforme.pdf", "ohada/Acte Uniforme.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents/dgi/d%C3%A9cret.pdf", "dgi/décret.pdf")]
    public void ToBlobPath_StripsContainerAndDecodes(string url, string expected)
    {
        Assert.Equal(expected, DocumentIdentity.ToBlobPath(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas une url")]
    [InlineData("ohada/acte.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents")]
    [InlineData("https://st.blob.core.windows.net/documents/")]
    public void ToBlobPath_InvalidUrl_ReturnsNull(string? url)
    {
        Assert.Null(DocumentIdentity.ToBlobPath(url));
    }

    [Fact]
    public void ToBlobPath_EncodedAndPlainNames_GiveSameIdentity()
    {
        var path = DocumentIdentity.ToBlobPath(
            "https://st.blob.core.windows.net/documents/ohada/Acte%20Uniforme.pdf")!;

        Assert.Equal(DocumentIdentity.ToDocumentId("ohada/Acte Uniforme.pdf"), DocumentIdentity.ToDocumentId(path));
        Assert.Equal("Acte Uniforme.pdf", DocumentIdentity.ToFileName(path));
    }
}
