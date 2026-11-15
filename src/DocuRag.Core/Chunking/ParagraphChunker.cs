using System.Text;
using System.Text.RegularExpressions;
using DocuRag.Core.Models;
using DocuRag.Core.Options;

namespace DocuRag.Core.Chunking;

public interface ITokenCounter
{
    int Count(string text);
}

/// <summary>
/// Approximation ~4 caractères par token (ordre de grandeur de cl100k_base sur du français).
/// Suffisant pour dimensionner des extraits ; on ne facture rien sur la base de ce chiffre.
/// </summary>
public sealed class ApproximateTokenCounter : ITokenCounter
{
    public int Count(string text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
}

/// <summary>
/// Découpage par paragraphes avec chevauchement.
/// <list type="bullet">
/// <item>Unité de base : le paragraphe ; un paragraphe trop long est redécoupé en phrases, puis en mots.</item>
/// <item>On remplit chaque extrait jusqu'à <see cref="ChunkingOptions.TargetTokens"/>.</item>
/// <item>L'extrait suivant reprend les ~<see cref="ChunkingOptions.OverlapTokens"/> derniers tokens du précédent.</item>
/// <item>Chaque extrait garde ses pages de début et de fin : c'est ce qui rend les citations vérifiables.</item>
/// </list>
/// </summary>
public sealed partial class ParagraphChunker(ChunkingOptions options, ITokenCounter tokenCounter)
{
    private readonly int _target = Math.Max(50, options.TargetTokens);
    private readonly int _overlap = Math.Clamp(options.OverlapTokens, 0, Math.Max(50, options.TargetTokens) / 2);

    public ParagraphChunker(ChunkingOptions options) : this(options, new ApproximateTokenCounter())
    {
    }

    public IReadOnlyList<TextChunk> Chunk(IEnumerable<PageText> pages)
    {
        var units = pages.SelectMany(SplitPage).ToList();
        var chunks = new List<TextChunk>();
        var current = new List<Unit>();
        var currentTokens = 0;
        var hasNewContent = false;

        foreach (var unit in units)
        {
            if (hasNewContent && currentTokens + unit.Tokens > _target)
            {
                chunks.Add(Build(chunks.Count, current));
                current = TakeOverlap(current);
                currentTokens = current.Sum(u => u.Tokens);
                hasNewContent = false;
            }

            current.Add(unit);
            currentTokens += unit.Tokens;
            hasNewContent = true;
        }

        if (hasNewContent)
        {
            chunks.Add(Build(chunks.Count, current));
        }

        return chunks;
    }

    private TextChunk Build(int index, List<Unit> units)
    {
        var text = string.Join("\n\n", units.Select(u => u.Text));
        return new TextChunk(index, text, units.Min(u => u.Page), units.Max(u => u.Page), tokenCounter.Count(text));
    }

    private List<Unit> TakeOverlap(List<Unit> previous)
    {
        if (_overlap == 0 || previous.Count == 0)
        {
            return [];
        }

        var overlap = new List<Unit>();
        var tokens = 0;
        for (var i = previous.Count - 1; i >= 0; i--)
        {
            var unit = previous[i];
            if (tokens + unit.Tokens <= _overlap)
            {
                overlap.Insert(0, unit);
                tokens += unit.Tokens;
                continue;
            }

            if (overlap.Count == 0)
            {
                // Le dernier paragraphe est plus long que le chevauchement : on reprend sa fin, mot par mot.
                var tail = TailWords(unit.Text, _overlap);
                if (tail.Length > 0)
                {
                    overlap.Add(new Unit(tail, unit.Page, tokenCounter.Count(tail)));
                }
            }

            break;
        }

        return overlap;
    }

    private string TailWords(string text, int maxTokens)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var taken = new List<string>();
        for (var i = words.Length - 1; i >= 0; i--)
        {
            taken.Insert(0, words[i]);
            if (tokenCounter.Count(string.Join(' ', taken)) > maxTokens)
            {
                taken.RemoveAt(0);
                break;
            }
        }

        return string.Join(' ', taken);
    }

    private IEnumerable<Unit> SplitPage(PageText page)
    {
        var normalized = page.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var rawParagraph in ParagraphSeparator().Split(normalized))
        {
            var paragraph = Whitespace().Replace(rawParagraph, " ").Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            foreach (var piece in SplitToFit(paragraph))
            {
                yield return new Unit(piece, page.PageNumber, tokenCounter.Count(piece));
            }
        }
    }

    /// <summary>Paragraphe → phrases → groupes de mots, jusqu'à ce que chaque morceau tienne dans la cible.</summary>
    private IEnumerable<string> SplitToFit(string paragraph)
    {
        if (tokenCounter.Count(paragraph) <= _target)
        {
            yield return paragraph;
            yield break;
        }

        var buffer = new StringBuilder();
        foreach (var sentence in SentenceSeparator().Split(paragraph))
        {
            IEnumerable<string> pieces = tokenCounter.Count(sentence) <= _target ? new[] { sentence } : SplitWords(sentence);
            foreach (var piece in pieces)
            {
                var candidate = buffer.Length == 0 ? piece : $"{buffer} {piece}";
                if (buffer.Length > 0 && tokenCounter.Count(candidate) > _target)
                {
                    yield return buffer.ToString();
                    buffer.Clear().Append(piece);
                }
                else
                {
                    buffer.Clear().Append(candidate);
                }
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private IEnumerable<string> SplitWords(string sentence)
    {
        var buffer = new List<string>();
        foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            buffer.Add(word);
            if (tokenCounter.Count(string.Join(' ', buffer)) > _target && buffer.Count > 1)
            {
                buffer.RemoveAt(buffer.Count - 1);
                yield return string.Join(' ', buffer);
                buffer = [word];
            }
        }

        if (buffer.Count > 0)
        {
            yield return string.Join(' ', buffer);
        }
    }

    private sealed record Unit(string Text, int Page, int Tokens);

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphSeparator();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[\.!\?;:])\s+")]
    private static partial Regex SentenceSeparator();
}
