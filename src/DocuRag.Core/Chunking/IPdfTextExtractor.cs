namespace DocuRag.Core.Chunking;

public interface IPdfTextExtractor
{
    /// <summary>Extrait le texte brut de chaque page d'un flux PDF.</summary>
    IReadOnlyList<PageText> ExtractPages(Stream pdfStream);
}
