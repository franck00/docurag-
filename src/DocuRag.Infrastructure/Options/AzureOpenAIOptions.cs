using System.ComponentModel.DataAnnotations;

namespace DocuRag.Infrastructure.Options;

/// <summary>Configuration d'Azure OpenAI. Aucun secret : l'auth passe par managed identity.</summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Endpoint du compte, ex. <c>https://mon-openai.openai.azure.com/</c>.</summary>
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Nom du déploiement d'embeddings (<c>text-embedding-3-small</c>).</summary>
    [Required]
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

    /// <summary>Nom du déploiement de chat (<c>gpt-4o-mini</c>).</summary>
    [Required]
    public string ChatDeployment { get; set; } = "gpt-4o-mini";

    /// <summary>Dimensions des embeddings (1536 pour text-embedding-3-small).</summary>
    public int EmbeddingDimensions { get; set; } = 1536;
}
