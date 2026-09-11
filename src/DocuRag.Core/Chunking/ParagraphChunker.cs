using System.Text;

namespace DocuRag.Core.Chunking;

/// <summary>
/// Decoupe le texte par paragraphes, en accumulant jusqu'a ~800 tokens estimes
/// par chunk avec ~100 tokens de chevauchement entre deux chunks consecutifs
/// (garde le contexte a la frontiere, evite de couper une idee en deux).
/// </summary>
public sealed class ParagraphChunker : IChunker
{
    private const int TargetTokens = 800;
    private const int OverlapTokens = 100;

    // Heuristique ~4 caracteres/token (correcte pour l'anglais, approximative
    // pour le francais). Suffisant pour calibrer la taille des chunks ; le
    // compte exact de tokens n'importera qu'au moment de facturer les
    // embeddings (J5), pas ici.
    private const int CharsPerToken = 4;

    public IReadOnlyList<DocumentChunk> Chunk(IEnumerable<PageText> pages)
    {
        var paragraphs = pages
            .SelectMany(page => SplitParagraphs(page.Text).Select(text => (page.PageNumber, Text: text)))
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .ToList();

        var chunks = new List<DocumentChunk>();
        var buffer = new StringBuilder();
        var bufferPageNumber = paragraphs.Count > 0 ? paragraphs[0].PageNumber : 1;
        var index = 0;

        foreach (var (pageNumber, text) in paragraphs)
        {
            if (buffer.Length == 0)
            {
                bufferPageNumber = pageNumber;
            }

            if (buffer.Length > 0)
            {
                buffer.Append("\n\n");
            }

            buffer.Append(text);

            if (EstimateTokens(buffer.Length) >= TargetTokens)
            {
                var chunkText = buffer.ToString();
                chunks.Add(new DocumentChunk(index++, bufferPageNumber, chunkText));

                var overlap = TakeOverlap(chunkText, OverlapTokens * CharsPerToken);
                buffer.Clear();
                buffer.Append(overlap);
                bufferPageNumber = pageNumber;
            }
        }

        if (buffer.Length > 0)
        {
            chunks.Add(new DocumentChunk(index, bufferPageNumber, buffer.ToString()));
        }

        return chunks;
    }

    private static int EstimateTokens(int characterCount) => characterCount / CharsPerToken;

    private static string TakeOverlap(string text, int overlapChars) =>
        text.Length <= overlapChars ? text : text[^overlapChars..];

    private static IEnumerable<string> SplitParagraphs(string text) =>
        text.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
