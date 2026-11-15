using DocuRag.Core.Models;

namespace DocuRag.Core.Tests;

public class BlobPathTests
{
    [Theory]
    [InlineData("https://st.blob.core.windows.net/documents/ohada/acte%20uniforme.pdf", "documents", "ohada/acte uniforme.pdf", "ohada", "acte uniforme.pdf")]
    [InlineData("https://st.blob.core.windows.net/documents/code.pdf", "documents", "code.pdf", "general", "code.pdf")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/documents/DGI/note.PDF", "documents", "DGI/note.PDF", "dgi", "note.PDF")]
    public void Parses_blob_urls(string url, string container, string blobName, string docType, string fileName)
    {
        Assert.True(BlobPath.TryParse(url, out var path));
        Assert.NotNull(path);
        Assert.Equal(container, path.Container);
        Assert.Equal(blobName, path.BlobName);
        Assert.Equal(docType, path.DocType);
        Assert.Equal(fileName, path.FileName);
        Assert.True(path.IsPdf);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas une url")]
    [InlineData("https://st.blob.core.windows.net/documents")]
    public void Rejects_invalid_urls(string? url) => Assert.False(BlobPath.TryParse(url, out _));

    [Fact]
    public void Document_id_is_deterministic_and_distinct()
    {
        var a1 = new BlobPath("documents", "ohada/a.pdf").DocumentId;
        var a2 = new BlobPath("documents", "ohada/a.pdf").DocumentId;
        var b = new BlobPath("documents", "ohada/b.pdf").DocumentId;

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.Equal(32, a1.Length);
    }

    [Fact]
    public void Chunk_ids_are_deterministic() =>
        Assert.Equal("abc-00042", ChunkRecord.BuildId("abc", 42));
}
