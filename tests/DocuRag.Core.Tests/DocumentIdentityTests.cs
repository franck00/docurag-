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
}
