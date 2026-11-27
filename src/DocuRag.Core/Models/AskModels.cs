using System.Text.Json.Serialization;

namespace DocuRag.Core.Models;

/// <summary>Question posée à l'API <c>/ask</c>.</summary>
public sealed class AskRequest
{
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    /// <summary>Filtre optionnel par type de document (ex. <c>ohada</c>).</summary>
    [JsonPropertyName("documentType")]
    public string? DocumentType { get; init; }

    /// <summary>Nombre d'extraits à récupérer (top-K). Défaut 5.</summary>
    [JsonPropertyName("topK")]
    public int TopK { get; init; } = 5;
}

/// <summary>Une source citée dans la réponse, avec de quoi la retrouver.</summary>
public sealed class Citation
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }

    [JsonPropertyName("page")]
    public required int Page { get; init; }

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("excerpt")]
    public required string Excerpt { get; init; }
}

/// <summary>Réponse de l'API <c>/ask</c> : le texte généré et ses sources.</summary>
public sealed class AskResponse
{
    [JsonPropertyName("answer")]
    public required string Answer { get; init; }

    [JsonPropertyName("sources")]
    public required IReadOnlyList<Citation> Sources { get; init; }
}
