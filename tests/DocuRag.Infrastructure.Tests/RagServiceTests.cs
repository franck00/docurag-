using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class RagServiceTests
{
    private static VectorSearchResult Hit(string file, int page, string content, double score)
        => new(
            new DocumentChunk
            {
                Id = $"{file}-{page}",
                DocumentId = file,
                ChunkIndex = page,
                FileName = file,
                DocumentType = "ohada",
                Page = page,
                Content = content,
            },
            score);

    [Fact]
    public async Task AskAsync_NoHits_ReturnsNoSourceAnswer_WithoutCallingChat()
    {
        var store = new InMemoryVectorStore { NextSearchResults = [] };
        var chat = new FakeChat();
        var rag = new RagService(new FakeEmbeddingGenerator(), store, chat, NullLogger<RagService>.Instance);

        var response = await rag.AskAsync(
            new AskRequest { Question = "Question ?" }, CancellationToken.None);

        Assert.Empty(response.Sources);
        Assert.Equal(0, chat.Calls); // pas d'appel LLM si aucun extrait
        Assert.Contains("Aucun extrait", response.Answer);
    }

    [Fact]
    public async Task AskAsync_WithHits_ReturnsAnswerAndMappedSources()
    {
        var store = new InMemoryVectorStore
        {
            NextSearchResults =
            [
                Hit("acte.pdf", 12, "Le contenu pertinent du premier extrait.", 0.91),
                Hit("note.pdf", 3, "Second extrait.", 0.82),
            ],
        };
        var chat = new FakeChat("Réponse sourcée.");
        var rag = new RagService(new FakeEmbeddingGenerator(), store, chat, NullLogger<RagService>.Instance);

        var response = await rag.AskAsync(
            new AskRequest { Question = "Quelles conditions ?" }, CancellationToken.None);

        Assert.Equal("Réponse sourcée.", response.Answer);
        Assert.Equal(2, response.Sources.Count);
        Assert.Equal("acte.pdf", response.Sources[0].FileName);
        Assert.Equal(12, response.Sources[0].Page);
        Assert.Equal(0.91, response.Sources[0].Score, 3);
        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task AskAsync_BuildsPrompt_WithQuestionAndExcerpts()
    {
        var store = new InMemoryVectorStore
        {
            NextSearchResults = [Hit("acte.pdf", 7, "Texte de référence à citer.", 0.88)],
        };
        var chat = new FakeChat();
        var rag = new RagService(new FakeEmbeddingGenerator(), store, chat, NullLogger<RagService>.Instance);

        await rag.AskAsync(
            new AskRequest { Question = "Ma question précise ?" }, CancellationToken.None);

        Assert.Contains("Ma question précise ?", chat.LastUserPrompt);
        Assert.Contains("Texte de référence à citer.", chat.LastUserPrompt);
        Assert.Contains("acte.pdf", chat.LastUserPrompt);
    }

    [Fact]
    public async Task AskAsync_LongExcerpt_IsTruncated()
    {
        var longText = new string('x', 500);
        var store = new InMemoryVectorStore
        {
            NextSearchResults = [Hit("acte.pdf", 1, longText, 0.5)],
        };
        var rag = new RagService(
            new FakeEmbeddingGenerator(), store, new FakeChat(), NullLogger<RagService>.Instance);

        var response = await rag.AskAsync(
            new AskRequest { Question = "?" }, CancellationToken.None);

        Assert.True(response.Sources[0].Excerpt.Length < longText.Length);
    }

    [Theory]
    [InlineData("OHADA", "ohada")]
    [InlineData("  Douanes-CEMAC ", "douanes-cemac")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task AskAsync_NormalizesDocumentTypeFilter(string? requested, string? expected)
    {
        var store = new InMemoryVectorStore { NextSearchResults = [] };
        var rag = new RagService(
            new FakeEmbeddingGenerator(), store, new FakeChat(), NullLogger<RagService>.Instance);

        await rag.AskAsync(
            new AskRequest { Question = "Question ?", DocumentType = requested },
            CancellationToken.None);

        Assert.Equal(expected, store.LastSearchDocumentType);
    }

    [Fact]
    public async Task AskAsync_BlankQuestion_Throws()
    {
        var rag = new RagService(
            new FakeEmbeddingGenerator(), new InMemoryVectorStore(), new FakeChat(),
            NullLogger<RagService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => rag.AskAsync(new AskRequest { Question = "   " }, CancellationToken.None));
    }
}
