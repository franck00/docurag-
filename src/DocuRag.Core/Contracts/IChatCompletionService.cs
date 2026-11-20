namespace DocuRag.Core.Contracts;

/// <summary>
/// Appelle le modèle de chat (<c>gpt-4o-mini</c>) pour générer la réponse finale
/// à partir du prompt construit avec les extraits retrouvés.
/// </summary>
public interface IChatCompletionService
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
