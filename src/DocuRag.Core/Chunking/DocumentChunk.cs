namespace DocuRag.Core.Chunking;

/// <summary>
/// Un chunk de texte pret a etre embedde (J5). Le numero de page est celui de
/// la page ou commence le chunk : c'est ce qui permettra de citer une source
/// credible dans les reponses de l'API (J6).
/// </summary>
public sealed record DocumentChunk(int Index, int PageNumber, string Text);
