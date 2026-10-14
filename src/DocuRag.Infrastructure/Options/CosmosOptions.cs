using System.ComponentModel.DataAnnotations;

namespace DocuRag.Infrastructure.Options;

/// <summary>Configuration Cosmos DB for NoSQL. Auth par managed identity (RBAC data plane).</summary>
public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    /// <summary>Endpoint du compte, ex. <c>https://mon-cosmos.documents.azure.com:443/</c>.</summary>
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string Database { get; set; } = "docurag";

    public string DocumentsContainer { get; set; } = "documents";

    public string ChunksContainer { get; set; } = "chunks";
}
