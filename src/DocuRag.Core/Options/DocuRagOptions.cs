namespace DocuRag.Core.Options;

/// <summary>
/// Configuration de DocuRAG, section <c>DocuRag</c>.
/// Aucune clé ni chaîne de connexion : uniquement des endpoints, l'authentification passe par Entra ID
/// (<c>DefaultAzureCredential</c> → managed identity en Azure, <c>az login</c> en local).
/// En variables d'environnement : <c>DocuRag__Cosmos__Endpoint</c>, etc.
/// </summary>
public sealed class DocuRagOptions
{
    public const string SectionName = "DocuRag";

    public CosmosOptions Cosmos { get; set; } = new();
    public ServiceBusOptions ServiceBus { get; set; } = new();
    public StorageOptions Storage { get; set; } = new();
    public OpenAIOptions OpenAI { get; set; } = new();
    public ChunkingOptions Chunking { get; set; } = new();
    public RagOptions Rag { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
}

public sealed class CosmosOptions
{
    public string? Endpoint { get; set; }
    public string Database { get; set; } = "docurag";
    public string DocumentsContainer { get; set; } = "documents";
    public string ChunksContainer { get; set; } = "chunks";
    public string CacheContainer { get; set; } = "cache";
    public string StatsContainer { get; set; } = "stats";
    public string LeasesContainer { get; set; } = "leases";
}

public sealed class ServiceBusOptions
{
    /// <summary>Ex. <c>sb-docurag-xyz.servicebus.windows.net</c>.</summary>
    public string? FullyQualifiedNamespace { get; set; }
    public string QueueName { get; set; } = "doc-processing";
    public int MaxConcurrentCalls { get; set; } = 4;
    /// <summary>Doit rester aligné sur le <c>maxDeliveryCount</c> de la queue (Bicep).</summary>
    public int MaxDeliveryCount { get; set; } = 5;
}

public sealed class StorageOptions
{
    /// <summary>Ex. <c>https://stdocuragxyz.blob.core.windows.net</c>.</summary>
    public string? BlobServiceUri { get; set; }
    public string Container { get; set; } = "documents";
}

public sealed class OpenAIOptions
{
    public string? Endpoint { get; set; }
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public string ChatDeployment { get; set; } = "gpt-4o-mini";
    public int EmbeddingDimensions { get; set; } = 1536;
    /// <summary>Nombre d'extraits envoyés par appel d'embedding (le SDK accepte plusieurs inputs par appel).</summary>
    public int EmbeddingBatchSize { get; set; } = 64;
    public int MaxRetries { get; set; } = 6;
}

public sealed class ChunkingOptions
{
    public int TargetTokens { get; set; } = 800;
    public int OverlapTokens { get; set; } = 100;
}

public sealed class RagOptions
{
    public int TopK { get; set; } = 5;
    public int MaxOutputTokens { get; set; } = 800;
    public float Temperature { get; set; } = 0.1f;
}

public sealed class CacheOptions
{
    /// <summary>Similarité cosinus minimale pour considérer deux questions comme équivalentes.</summary>
    public double SimilarityThreshold { get; set; } = 0.95;
}
