using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using DocuRag.Core.Contracts;
using DocuRag.Infrastructure.Cosmos;
using DocuRag.Infrastructure.Messaging;
using DocuRag.Infrastructure.OpenAI;
using DocuRag.Infrastructure.Options;
using DocuRag.Infrastructure.Processing;
using DocuRag.Infrastructure.Rag;
using DocuRag.Infrastructure.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocuRag.Infrastructure.DependencyInjection;

/// <summary>
/// Composition des services d'infrastructure. Chaque hôte (Ingestion, Worker, API)
/// n'ajoute que ce dont il a besoin. Auth unifiée par <see cref="DefaultAzureCredential"/> :
/// aucune connection string nulle part (J11).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    private static readonly JsonSerializerOptions CosmosJson = new(JsonSerializerDefaults.Web);

    /// <summary>Enregistre la credential partagée (managed identity en prod, dev creds en local).</summary>
    public static IServiceCollection AddDocuRagCredential(this IServiceCollection services)
    {
        services.TryAddCredential();
        return services;
    }

    public static IServiceCollection AddCosmosVectorStore(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddCredential();
        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection(CosmosOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
            var credential = sp.GetRequiredService<TokenCredential>();
            var clientOptions = new CosmosClientOptions
            {
                AllowBulkExecution = true,
                UseSystemTextJsonSerializerWithOptions = CosmosJson,
            };
            return new CosmosClient(options.Endpoint, credential, clientOptions);
        });

        services.AddSingleton<IVectorStore, CosmosVectorStore>();
        return services;
    }

    public static IServiceCollection AddServiceBusQueue(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddServiceBusClient(configuration);
        services.AddSingleton<IDocumentQueue, ServiceBusDocumentQueue>();
        return services;
    }

    /// <summary>Lecture seule de la dead-letter queue (endpoint <c>/dead-letters</c>, J10).</summary>
    public static IServiceCollection AddDeadLetterInspection(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddServiceBusClient(configuration);
        services.AddSingleton<IDeadLetterInspector, ServiceBusDeadLetterInspector>();
        return services;
    }

    private static void TryAddServiceBusClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(ServiceBusClient)))
        {
            return;
        }

        services.TryAddCredential();
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            var credential = sp.GetRequiredService<TokenCredential>();
            return new ServiceBusClient(options.Namespace, credential);
        });
    }

    public static IServiceCollection AddBlobStorage(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddCredential();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            var credential = sp.GetRequiredService<TokenCredential>();
            return new BlobServiceClient(new Uri(options.BlobEndpoint), credential);
        });

        services.AddSingleton<IDocumentDownloader, BlobDocumentDownloader>();
        return services;
    }

    public static IServiceCollection AddAzureOpenAI(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddCredential();
        services.AddOptions<AzureOpenAIOptions>()
            .Bind(configuration.GetSection(AzureOpenAIOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
            var credential = sp.GetRequiredService<TokenCredential>();
            // Retry exponentiel sur les 429 (throttling) et erreurs transitoires (J10).
            var clientOptions = new AzureOpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries: 5),
            };
            return new AzureOpenAIClient(new Uri(options.Endpoint), credential, clientOptions);
        });

        services.AddSingleton<IEmbeddingGenerator, AzureOpenAIEmbeddingGenerator>();
        services.AddSingleton<IChatCompletionService, AzureOpenAIChatService>();
        return services;
    }

    public static IServiceCollection AddPdfExtraction(this IServiceCollection services)
    {
        services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
        return services;
    }

    /// <summary>Tout ce qu'il faut au worker pour traiter un document de bout en bout.</summary>
    public static IServiceCollection AddDocumentProcessing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCosmosVectorStore(configuration);
        services.AddBlobStorage(configuration);
        services.AddAzureOpenAI(configuration);
        services.AddPdfExtraction();
        services.AddSingleton<DocumentProcessor>();
        return services;
    }

    /// <summary>Tout ce qu'il faut à l'API pour répondre.</summary>
    public static IServiceCollection AddRag(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCosmosVectorStore(configuration);
        services.AddAzureOpenAI(configuration);
        services.AddSingleton<RagService>();
        return services;
    }

    private static void TryAddCredential(this IServiceCollection services)
    {
        services.TryAddSingletonCredential();
    }

    private static void TryAddSingletonCredential(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(TokenCredential)))
        {
            return;
        }

        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
    }
}
