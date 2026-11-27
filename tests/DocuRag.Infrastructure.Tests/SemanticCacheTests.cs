using DocuRag.Core.Models;
using DocuRag.Infrastructure.Options;
using DocuRag.Infrastructure.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class SemanticCacheTests
{
    private static readonly AskResponse Cached = new() { Answer = "Réponse en cache.", Sources = [] };

    /// <summary>Horloge manuelle pour tester l'expiration sans attendre.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static InMemorySemanticCache CreateCache(
        TimeProvider? time = null, bool enabled = true, int maxEntries = 500)
        => new(
            Microsoft.Extensions.Options.Options.Create(new SemanticCacheOptions
            {
                Enabled = enabled,
                SimilarityThreshold = 0.95,
                TtlMinutes = 30,
                MaxEntries = maxEntries,
            }),
            time);

    [Fact]
    public async Task TryGet_SameVectorAndScope_ReturnsCachedResponse()
    {
        var cache = CreateCache();
        await cache.SetAsync([1f, 0f, 0f], "ohada", 5, Cached);

        var hit = await cache.TryGetAsync([1f, 0f, 0f], "ohada", 5);

        Assert.Same(Cached, hit);
    }

    [Fact]
    public async Task TryGet_VectorBelowThreshold_ReturnsNull()
    {
        var cache = CreateCache();
        await cache.SetAsync([1f, 0f, 0f], null, 5, Cached);

        // cos ≈ 0,707 : question différente.
        var hit = await cache.TryGetAsync([1f, 1f, 0f], null, 5);

        Assert.Null(hit);
    }

    [Theory]
    [InlineData("douanes-cemac", 5)]
    [InlineData(null, 5)]
    [InlineData("ohada", 10)]
    public async Task TryGet_OtherScope_ReturnsNull(string? documentType, int topK)
    {
        var cache = CreateCache();
        await cache.SetAsync([1f, 0f, 0f], "ohada", 5, Cached);

        Assert.Null(await cache.TryGetAsync([1f, 0f, 0f], documentType, topK));
    }

    [Fact]
    public async Task TryGet_AfterTtl_ReturnsNull()
    {
        var time = new ManualTimeProvider();
        var cache = CreateCache(time);
        await cache.SetAsync([1f, 0f, 0f], null, 5, Cached);

        time.Now = time.Now.AddMinutes(31);

        Assert.Null(await cache.TryGetAsync([1f, 0f, 0f], null, 5));
    }

    [Fact]
    public async Task Set_OverMaxEntries_EvictsOldest()
    {
        var cache = CreateCache(maxEntries: 1);
        await cache.SetAsync([1f, 0f, 0f], null, 5, Cached);
        await cache.SetAsync([0f, 1f, 0f], null, 5, Cached);

        Assert.Null(await cache.TryGetAsync([1f, 0f, 0f], null, 5));
        Assert.NotNull(await cache.TryGetAsync([0f, 1f, 0f], null, 5));
    }

    [Fact]
    public async Task Disabled_NeverReturnsAnything()
    {
        var cache = CreateCache(enabled: false);
        await cache.SetAsync([1f, 0f, 0f], null, 5, Cached);

        Assert.Null(await cache.TryGetAsync([1f, 0f, 0f], null, 5));
    }

    [Fact]
    public void CosineSimilarity_HandlesDimensionMismatchAndZeroVector()
    {
        Assert.Equal(1.0, InMemorySemanticCache.CosineSimilarity([2f, 0f], [5f, 0f]), 6);
        Assert.Equal(0.0, InMemorySemanticCache.CosineSimilarity([1f, 0f], [1f, 0f, 0f]));
        Assert.Equal(0.0, InMemorySemanticCache.CosineSimilarity([0f, 0f], [1f, 0f]));
    }

    [Fact]
    public async Task RagService_SecondIdenticalQuestion_IsServedFromCache()
    {
        var store = new InMemoryVectorStore
        {
            NextSearchResults =
            [
                new(new DocumentChunk
                {
                    Id = "acte.pdf-1",
                    DocumentId = "acte.pdf",
                    ChunkIndex = 0,
                    FileName = "acte.pdf",
                    DocumentType = "ohada",
                    Page = 1,
                    Content = "Extrait.",
                }, 0.9),
            ],
        };
        var chat = new FakeChat("Réponse sourcée.");
        var rag = new RagService(
            new FakeEmbeddingGenerator(dimensions: 8), store, chat, NullLogger<RagService>.Instance, CreateCache());
        var request = new AskRequest { Question = "Quel délai de recours ?" };

        var first = await rag.AskAsync(request, CancellationToken.None);
        var second = await rag.AskAsync(request, CancellationToken.None);

        Assert.Equal(1, chat.Calls); // le LLM n'est appelé qu'une fois
        Assert.Equal(first.Answer, second.Answer);
    }

    [Fact]
    public async Task RagService_NoHits_IsNotCached()
    {
        var store = new InMemoryVectorStore { NextSearchResults = [] };
        var cache = CreateCache();
        var embeddings = new FakeEmbeddingGenerator(dimensions: 8);
        var rag = new RagService(embeddings, store, new FakeChat(), NullLogger<RagService>.Instance, cache);

        await rag.AskAsync(new AskRequest { Question = "Rien ?" }, CancellationToken.None);

        var vector = await embeddings.EmbedAsync("Rien ?");
        Assert.Null(await cache.TryGetAsync(vector, null, 5));
    }
}
