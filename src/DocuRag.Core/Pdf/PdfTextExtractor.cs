using System.Text;
using DocuRag.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocuRag.Core.Pdf;

/// <summary>
/// Erreur non transitoire : le fichier n'est pas un PDF lisible. Le worker envoie directement le message
/// en dead-letter au lieu de le retenter (un retry ne réparera pas le fichier).
/// </summary>
public sealed class InvalidPdfException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Extraction de texte page par page avec PdfPig.
/// On reconstruit lignes et paragraphes à partir de la position des mots : PdfPig donne des mots positionnés,
/// pas des paragraphes, et ce sont les paragraphes qui alimentent le chunking.
/// </summary>
public static class PdfTextExtractor
{
    public static IReadOnlyList<PageText> Extract(byte[] pdf)
    {
        if (pdf.Length < 5 || Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-")
        {
            throw new InvalidPdfException("Le fichier ne commence pas par l'en-tête %PDF- : ce n'est pas un PDF.");
        }

        var pages = new List<PageText>();
        try
        {
            using var document = PdfDocument.Open(pdf);
            foreach (var page in document.GetPages())
            {
                pages.Add(new PageText(page.Number, ReadPage(page)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidPdfException)
        {
            throw new InvalidPdfException($"PDF illisible ou corrompu : {ex.Message}", ex);
        }

        if (pages.Count == 0 || pages.All(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            throw new InvalidPdfException("Aucun texte extractible : PDF scanné (image) ? L'OCR n'est pas géré par ce pipeline.");
        }

        return pages;
    }

    private static string ReadPage(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }

        // 1. Regrouper les mots en lignes (même ligne de base, à une demi-hauteur de caractère près).
        var lines = new List<Line>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var box = word.BoundingBox;
            var tolerance = Math.Max(1.0, box.Height * 0.5);
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(line.Bottom - box.Bottom) <= tolerance)
            {
                line.Add(word);
            }
            else
            {
                lines.Add(new Line(word));
            }
        }

        // 2. Un écart vertical nettement supérieur à l'interligne habituel = nouveau paragraphe.
        var heights = lines.Select(l => l.Height).Where(h => h > 0).OrderBy(h => h).ToList();
        var typicalHeight = heights.Count > 0 ? heights[heights.Count / 2] : 10.0;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                var gap = lines[i - 1].Bottom - lines[i].Top;
                sb.Append(gap > typicalHeight * 0.8 ? "\n\n" : "\n");
            }

            sb.Append(lines[i].Text);
        }

        return sb.ToString();
    }

    private sealed class Line
    {
        private readonly List<Word> _words = [];

        public Line(Word first) => Add(first);

        public double Bottom { get; private set; } = double.MaxValue;
        public double Top { get; private set; } = double.MinValue;
        public double Height => Top - Bottom;
        public string Text => string.Join(' ', _words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));

        public void Add(Word word)
        {
            _words.Add(word);
            Bottom = Math.Min(Bottom, word.BoundingBox.Bottom);
            Top = Math.Max(Top, word.BoundingBox.Top);
        }
    }
}
