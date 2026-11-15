using DocuRag.Core.Chunking;
using DocuRag.Core.Models;
using DocuRag.Core.Options;

namespace DocuRag.Core.Tests;

public class ParagraphChunkerTests
{
    private static readonly ApproximateTokenCounter Counter = new();

    private static string Paragraph(int words, string seed) =>
        string.Join(' ', Enumerable.Range(0, words).Select(i => $"{seed}{i}")) + ".";

    [Fact]
    public void Empty_input_produces_no_chunk()
    {
        var chunker = new ParagraphChunker(new ChunkingOptions());
        Assert.Empty(chunker.Chunk([]));
        Assert.Empty(chunker.Chunk([new PageText(1, "   \n\n  ")]));
    }

    [Fact]
    public void Short_document_fits_in_a_single_chunk_with_its_page()
    {
        var chunker = new ParagraphChunker(new ChunkingOptions { TargetTokens = 800, OverlapTokens = 100 });
        var chunks = chunker.Chunk([new PageText(3, "Article 1.\n\nLe présent acte uniforme s'applique.")]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(3, chunk.PageStart);
        Assert.Equal(3, chunk.PageEnd);
        Assert.Contains("Article 1.", chunk.Text);
    }

    [Fact]
    public void Chunks_respect_target_plus_overlap_and_are_indexed_in_order()
    {
        var options = new ChunkingOptions { TargetTokens = 200, OverlapTokens = 40 };
        var pages = Enumerable.Range(1, 6)
            .Select(p => new PageText(p, string.Join("\n\n", Enumerable.Range(0, 5).Select(i => Paragraph(30, $"p{p}w{i}x")))))
            .ToList();

        var chunks = new ParagraphChunker(options).Chunk(pages);

        Assert.True(chunks.Count > 3);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.True(Counter.Count(c.Text) <= options.TargetTokens + options.OverlapTokens + 10,
            $"Extrait {c.Index} trop long : {Counter.Count(c.Text)} tokens"));
        Assert.All(chunks, c => Assert.True(c.PageStart <= c.PageEnd));
        Assert.Equal(1, chunks[0].PageStart);
        Assert.Equal(6, chunks[^1].PageEnd);
    }

    [Fact]
    public void Consecutive_chunks_overlap()
    {
        var options = new ChunkingOptions { TargetTokens = 160, OverlapTokens = 60 };
        var text = string.Join("\n\n", Enumerable.Range(0, 12).Select(i => Paragraph(12, $"para{i}mot")));

        var chunks = new ParagraphChunker(options).Chunk([new PageText(1, text)]);

        Assert.True(chunks.Count >= 2);
        for (var i = 1; i < chunks.Count; i++)
        {
            var previousLastParagraph = chunks[i - 1].Text.Split("\n\n")[^1];
            Assert.StartsWith(previousLastParagraph, chunks[i].Text);
        }
    }

    [Fact]
    public void Paragraph_longer_than_target_is_split_and_nothing_is_lost()
    {
        var options = new ChunkingOptions { TargetTokens = 100, OverlapTokens = 0 };
        var huge = Paragraph(400, "mot"); // ~ 400 mots sans aucune ponctuation intermédiaire

        var chunks = new ParagraphChunker(options).Chunk([new PageText(7, huge)]);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Counter.Count(c.Text) <= options.TargetTokens + 5));
        var allWords = chunks.SelectMany(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.Contains("mot0", allWords);
        Assert.Contains("mot399.", allWords);
        Assert.Equal(400, allWords.Count);
        Assert.All(chunks, c => Assert.Equal(7, c.PageStart));
    }

    [Fact]
    public void Chunk_spanning_two_pages_reports_both()
    {
        var options = new ChunkingOptions { TargetTokens = 800, OverlapTokens = 0 };
        var chunks = new ParagraphChunker(options).Chunk([new PageText(1, "Début."), new PageText(2, "Suite.")]);

        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.PageStart);
        Assert.Equal(2, chunk.PageEnd);
    }
}
