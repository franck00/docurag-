using System.Text;
using System.Text.RegularExpressions;
using DocuRag.Core.Models;

namespace DocuRag.Core.Chunking;

/// <summary>
/// Résultat d'un découpage : le texte du chunk et la page dont il provient
/// (page du premier segment qui le compose — la citation renvoie vers là).
/// </summary>
/// <param name="Page">Numéro de page 1-based.</param>
/// <param name="Content">Texte du chunk.</param>
public readonly record struct Chunk(int Page, string Content);

/// <summary>
/// Découpe un document (liste de pages) en chunks qui respectent, autant que possible,
/// les frontières de paragraphes, avec une cible en tokens et un chevauchement.
/// L'estimation de tokens est volontairement grossière (~1,3 token/mot) : le but est
/// de dimensionner les chunks, pas de compter précisément.
/// </summary>
public static class TextChunker
{
    private const double TokensPerWord = 1.3;

    // Sépare sur les fins de phrase suivies d'un espace, en gardant la ponctuation.
    private static readonly Regex SentenceSplitter = new(
        @"(?<=[.!?])\s+", RegexOptions.Compiled);

    /// <summary>Estime le nombre de tokens d'un texte à partir de son nombre de mots.</summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var words = CountWords(text);
        return (int)Math.Ceiling(words * TokensPerWord);
    }

    /// <summary>
    /// Découpe les pages en chunks. Chaque chunk est tagué avec la page de son premier
    /// segment. Les paragraphes trop longs sont subdivisés en phrases, puis en mots,
    /// pour qu'aucun segment ne dépasse à lui seul la cible.
    /// </summary>
    public static IReadOnlyList<Chunk> Chunk(
        IReadOnlyList<PageText> pages, ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        options ??= ChunkingOptions.Default;

        var segments = BuildSegments(pages, options.TargetTokens);
        if (segments.Count == 0)
        {
            return [];
        }

        var chunks = new List<Chunk>();
        var current = new List<(int Page, string Text)>();
        var currentTokens = 0;

        foreach (var segment in segments)
        {
            var segTokens = EstimateTokens(segment.Text);

            if (current.Count > 0 && currentTokens + segTokens > options.TargetTokens)
            {
                chunks.Add(Emit(current));
                var overlap = TakeOverlap(current, options.OverlapTokens);
                current = overlap;
                currentTokens = overlap.Sum(s => EstimateTokens(s.Text));
            }

            current.Add(segment);
            currentTokens += segTokens;
        }

        if (current.Count > 0)
        {
            chunks.Add(Emit(current));
        }

        return chunks;
    }

    private static Chunk Emit(List<(int Page, string Text)> segments)
    {
        var page = segments[0].Page;
        var content = string.Join("\n\n", segments.Select(s => s.Text)).Trim();
        return new Chunk(page, content);
    }

    /// <summary>
    /// Construit l'amorce du chunk suivant : les derniers mots du chunk émis, bornés à
    /// environ <paramref name="overlapTokens"/> tokens. On travaille au niveau des mots
    /// (et non des segments entiers) pour que le chevauchement reste borné même quand un
    /// segment dépasse à lui seul le budget — sinon les chunks enfleraient de proche en proche.
    /// </summary>
    private static List<(int Page, string Text)> TakeOverlap(
        List<(int Page, string Text)> segments, int overlapTokens)
    {
        if (overlapTokens <= 0 || segments.Count == 0)
        {
            return [];
        }

        var overlapWords = Math.Max(1, (int)(overlapTokens / TokensPerWord));
        var combined = string.Join(' ', segments.Select(s => s.Text));
        var words = combined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var take = Math.Min(overlapWords, words.Length);
        var tailText = string.Join(' ', words[^take..]);

        // La page de l'amorce est celle de la fin du chunk précédent : continuité de lecture.
        return [(segments[^1].Page, tailText)];
    }

    /// <summary>
    /// Transforme les pages en segments taggés par page, aucun ne dépassant la cible :
    /// paragraphes → (si trop long) phrases → (si trop long) paquets de mots.
    /// </summary>
    private static List<(int Page, string Text)> BuildSegments(
        IReadOnlyList<PageText> pages, int targetTokens)
    {
        var segments = new List<(int Page, string Text)>();

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            foreach (var paragraph in SplitParagraphs(page.Text))
            {
                if (EstimateTokens(paragraph) <= targetTokens)
                {
                    segments.Add((page.Page, paragraph));
                    continue;
                }

                foreach (var piece in SplitLongText(paragraph, targetTokens))
                {
                    segments.Add((page.Page, piece));
                }
            }
        }

        return segments;
    }

    private static IEnumerable<string> SplitParagraphs(string pageText)
    {
        var normalized = pageText.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = Regex.Split(normalized, @"\n\s*\n");
        foreach (var raw in paragraphs)
        {
            var collapsed = Regex.Replace(raw, @"\s+", " ").Trim();
            if (collapsed.Length > 0)
            {
                yield return collapsed;
            }
        }
    }

    /// <summary>Subdivise un texte trop long en phrases, puis regroupe jusqu'à la cible.</summary>
    private static IEnumerable<string> SplitLongText(string text, int targetTokens)
    {
        var sentences = SentenceSplitter.Split(text);
        var buffer = new StringBuilder();
        var bufferTokens = 0;

        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var sentenceTokens = EstimateTokens(trimmed);

            // Une seule phrase dépasse la cible : on tombe au niveau des mots.
            if (sentenceTokens > targetTokens)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                    bufferTokens = 0;
                }

                foreach (var wordPack in SplitByWords(trimmed, targetTokens))
                {
                    yield return wordPack;
                }

                continue;
            }

            if (bufferTokens + sentenceTokens > targetTokens && buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
                bufferTokens = 0;
            }

            buffer.Append(trimmed).Append(' ');
            bufferTokens += sentenceTokens;
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
    }

    private static IEnumerable<string> SplitByWords(string text, int targetTokens)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordsPerChunk = Math.Max(1, (int)(targetTokens / TokensPerWord));

        for (var i = 0; i < words.Length; i += wordsPerChunk)
        {
            yield return string.Join(' ', words.Skip(i).Take(wordsPerChunk));
        }
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }
}
