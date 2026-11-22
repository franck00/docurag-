namespace DocuRag.Core.Models;

/// <summary>
/// Règles de validation d'une <see cref="AskRequest"/>, appliquées par l'API avant tout
/// appel à Azure OpenAI ou Cosmos : une question démesurée coûte des tokens, un top-K
/// énorme gonfle le prompt et la requête vectorielle.
/// </summary>
public static class AskRequestValidator
{
    /// <summary>Longueur maximale de la question, en caractères.</summary>
    public const int MaxQuestionLength = 2000;

    /// <summary>Nombre maximal d'extraits demandés (top-K).</summary>
    public const int MaxTopK = 20;

    /// <summary>Longueur maximale du filtre <c>documentType</c>.</summary>
    public const int MaxDocumentTypeLength = 64;

    /// <summary>
    /// Retourne la liste des erreurs (vide si la requête est valide). Un <c>topK</c> à 0
    /// reste accepté : <c>RagService</c> le remplace par la valeur par défaut.
    /// </summary>
    public static IReadOnlyList<string> Validate(AskRequest? request)
    {
        if (request is null)
        {
            return ["Le corps de la requête est requis."];
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            errors.Add("Le champ 'question' est requis.");
        }
        else if (request.Question.Length > MaxQuestionLength)
        {
            errors.Add($"Le champ 'question' ne doit pas dépasser {MaxQuestionLength} caractères.");
        }

        if (request.TopK is < 0 or > MaxTopK)
        {
            errors.Add($"Le champ 'topK' doit être compris entre 1 et {MaxTopK} (0 = valeur par défaut).");
        }

        // Le type vient du premier segment du chemin blob (ex. « douanes-cemac ») et le filtre
        // Cosmos est paramétré : seule la longueur est bornée, le jeu de caractères reste libre.
        if (request.DocumentType is { Length: > MaxDocumentTypeLength })
        {
            errors.Add($"Le champ 'documentType' ne doit pas dépasser {MaxDocumentTypeLength} caractères.");
        }

        return errors;
    }
}
