namespace DocuRag.Core.Chunking;

public interface IChunker
{
    /// <summary>Decoupe le texte de pages en chunks avec chevauchement, en conservant le numero de page.</summary>
    IReadOnlyList<DocumentChunk> Chunk(IEnumerable<PageText> pages);
}
