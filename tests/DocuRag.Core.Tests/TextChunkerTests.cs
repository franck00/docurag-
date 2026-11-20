using DocuRag.Core.Chunking;
using DocuRag.Core.Models;
using Xunit;

namespace DocuRag.Core.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_EmptyPages_ReturnsEmpty()
    {
        var result = TextChunker.Chunk([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_BlankPage_IsSkipped()
    {
        var pages = new[] { new PageText(1, "   \n\n  ") };
        Assert.Empty(TextChunker.Chunk(pages));
    }

    [Fact]
    public void Chunk_ShortSinglePage_ProducesOneChunk_WithPageNumber()
    {
        var pages = new[] { new PageText(7, "Un paragraphe court sur une seule page.") };

        var chunks = TextChunker.Chunk(pages);

        var chunk = Assert.Single(chunks);
        Assert.Equal(7, chunk.Page);
        Assert.Contains("paragraphe court", chunk.Content);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultipleChunks()
    {
        // ~2000 mots => doit dépasser une cible de 800 tokens et produire plusieurs chunks.
        var paragraph = string.Join(" ", Enumerable.Repeat("mot", 2000));
        var pages = new[] { new PageText(1, paragraph) };

        var chunks = TextChunker.Chunk(pages, new ChunkingOptions { TargetTokens = 800, OverlapTokens = 100 });

        Assert.True(chunks.Count > 1, "un texte de ~2000 mots doit donner plusieurs chunks");
        Assert.All(chunks, c => Assert.True(
            TextChunker.EstimateTokens(c.Content) <= 1000,
            "aucun chunk ne doit dépasser nettement la cible"));
    }

    [Fact]
    public void Chunk_PreservesPageOfOriginatingText()
    {
        var pages = new[]
        {
            new PageText(1, string.Join(" ", Enumerable.Repeat("alpha", 700))),
            new PageText(2, string.Join(" ", Enumerable.Repeat("beta", 700))),
        };

        var chunks = TextChunker.Chunk(pages, new ChunkingOptions { TargetTokens = 800, OverlapTokens = 50 });

        Assert.Contains(chunks, c => c.Page == 1);
        Assert.Contains(chunks, c => c.Page == 2);
    }

    [Fact]
    public void Chunk_ConsecutiveChunks_Overlap()
    {
        // Phrases numérotées : on vérifie qu'une partie de la fin d'un chunk réapparaît au début du suivant.
        var sentences = Enumerable.Range(1, 300).Select(i => $"Phrase numero {i} du document.");
        var pages = new[] { new PageText(1, string.Join(" ", sentences)) };

        var chunks = TextChunker.Chunk(pages, new ChunkingOptions { TargetTokens = 200, OverlapTokens = 60 });

        Assert.True(chunks.Count >= 2);
        // Le dernier mot significatif du chunk 0 doit se retrouver dans le chunk 1 (chevauchement).
        var firstWords = chunks[0].Content.Split(' ');
        var tailToken = firstWords[^2]; // un numéro de phrase proche de la fin
        Assert.Contains(tailToken, chunks[1].Content);
    }

    [Fact]
    public void EstimateTokens_EmptyOrWhitespace_IsZero()
    {
        Assert.Equal(0, TextChunker.EstimateTokens(""));
        Assert.Equal(0, TextChunker.EstimateTokens("   "));
    }
}
