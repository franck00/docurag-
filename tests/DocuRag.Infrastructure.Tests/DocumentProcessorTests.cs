using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class DocumentProcessorTests
{
    private static DocumentMessage Message() => new()
    {
        DocumentId = "ohada-acte-ab12cd34",
        BlobUrl = "https://acct.blob.core.windows.net/documents/ohada/acte.pdf",
        FileName = "acte.pdf",
        DocumentType = "ohada",
    };

    private static IReadOnlyList<PageText> Pages(int count)
        => Enumerable.Range(1, count)
            .Select(p => new PageText(p, string.Join(" ", Enumerable.Repeat($"page{p}", 400))))
            .ToArray();

    private static DocumentProcessor Build(
        FakeExtractor extractor, FakeEmbeddingGenerator embeddings, InMemoryVectorStore store,
        FakeDownloader? downloader = null)
        => new(
            downloader ?? new FakeDownloader(),
            extractor,
            embeddings,
            store,
            NullLogger<DocumentProcessor>.Instance);

    [Fact]
    public async Task ProcessAsync_IndexesDocument_WithStatusAndCounts()
    {
        var store = new InMemoryVectorStore();
        var embeddings = new FakeEmbeddingGenerator();
        var processor = Build(new FakeExtractor(Pages(2)), embeddings, store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        var doc = store.Documents["ohada-acte-ab12cd34"];
        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.Equal(2, doc.PageCount);
        Assert.True(doc.ChunkCount > 0);
        Assert.Equal(doc.ChunkCount, store.Chunks.Count);
        Assert.Null(doc.Error);
    }

    [Fact]
    public async Task ProcessAsync_ChunkIds_AreDeterministicAndEmbedded()
    {
        var store = new InMemoryVectorStore();
        var processor = Build(new FakeExtractor(Pages(1)), new FakeEmbeddingGenerator(), store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.All(store.Chunks.Values, c =>
        {
            Assert.Equal($"ohada-acte-ab12cd34-{c.ChunkIndex}", c.Id);
            Assert.Equal("ohada-acte-ab12cd34", c.DocumentId);
            Assert.NotEmpty(c.Embedding);
        });
    }

    [Fact]
    public async Task ProcessAsync_Replayed_IsIdempotent_NoDuplicateChunks()
    {
        var store = new InMemoryVectorStore();
        var processor = Build(new FakeExtractor(Pages(2)), new FakeEmbeddingGenerator(), store);

        await processor.ProcessAsync(Message(), CancellationToken.None);
        var countAfterFirst = store.Chunks.Count;

        // Rejoue le même message : mêmes ids déterministes => ré-écriture, pas de doublons.
        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.Equal(countAfterFirst, store.Chunks.Count);
    }

    [Fact]
    public async Task ProcessAsync_EmbedsInBatches_NeverExceeding16()
    {
        var store = new InMemoryVectorStore();
        var embeddings = new FakeEmbeddingGenerator();
        // Beaucoup de pages => nombre de chunks largement supérieur à la taille de lot.
        var processor = Build(new FakeExtractor(Pages(30)), embeddings, store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.True(store.Chunks.Count > 16, "le document doit produire plus de 16 chunks");
        Assert.True(embeddings.MaxBatchSize <= 16, "aucun lot d'embeddings ne doit dépasser 16");
        Assert.Equal(store.Chunks.Count, embeddings.TotalEmbedded);
        Assert.True(embeddings.BatchCalls >= 2, "les embeddings doivent être envoyés en plusieurs lots");
    }

    [Fact]
    public async Task ProcessAsync_EmptyText_SetsFailed_AndThrows()
    {
        var store = new InMemoryVectorStore();
        var blankPages = new[] { new PageText(1, "   \n\n   ") };
        var processor = Build(new FakeExtractor(blankPages), new FakeEmbeddingGenerator(), store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(Message(), CancellationToken.None));

        var doc = store.Documents["ohada-acte-ab12cd34"];
        Assert.Equal(DocumentStatus.Failed, doc.Status);
        Assert.False(string.IsNullOrWhiteSpace(doc.Error));
        Assert.Empty(store.Chunks);
    }
}
