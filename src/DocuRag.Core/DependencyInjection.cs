using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.AI;
using DocuRag.Core.Chunking;
using DocuRag.Core.Cosmos;
using DocuRag.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocuRag.Core;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les clients Azure (tous authentifiés par Entra ID, aucune clé) et les services partagés.
    /// Les clients sont créés à la première utilisation : un service qui n'a pas besoin d'Azure OpenAI
    /// (la Function d'ingestion) n'a pas à le configurer.
    /// </summary>
    public static IServiceCollection AddDocuRagCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocuRagOptions>(configuration.GetSection(DocuRagOptions.SectionName));

        // Managed identity en Azure (AZURE_CLIENT_ID désigne l'identité user-assigned), az login / VS en local.
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.Cosmos;
            return new CosmosClient(Require(options.Endpoint, "DocuRag:Cosmos:Endpoint"), sp.GetRequiredService<TokenCredential>(),
                new CosmosClientOptions
                {
                    ApplicationName = "docurag",
                    UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    CosmosClientTelemetryOptions = new CosmosClientTelemetryOptions { DisableDistributedTracing = false },
                });
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.ServiceBus;
            return new ServiceBusClient(Require(options.FullyQualifiedNamespace, "DocuRag:ServiceBus:FullyQualifiedNamespace"),
                sp.GetRequiredService<TokenCredential>());
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.OpenAI;
            var clientOptions = new AzureOpenAIClientOptions
            {
                // Le retry est géré explicitement par RetryPolicy (backoff exponentiel + Retry-After).
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            };
            return new AzureOpenAIClient(new Uri(Require(options.Endpoint, "DocuRag:OpenAI:Endpoint")),
                sp.GetRequiredService<TokenCredential>(), clientOptions);
        });

        services.AddSingleton(sp => sp.GetRequiredService<AzureOpenAIClient>()
            .GetEmbeddingClient(sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.OpenAI.EmbeddingDeployment));
        services.AddSingleton(sp => sp.GetRequiredService<AzureOpenAIClient>()
            .GetChatClient(sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.OpenAI.ChatDeployment));

        services.AddSingleton<ITextEmbedder, AzureOpenAIEmbedder>();
        services.AddSingleton<IChatCompleter, AzureOpenAIChatCompleter>();
        services.AddSingleton<ITokenCounter, ApproximateTokenCounter>();
        services.AddSingleton(sp => new ParagraphChunker(
            sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.Chunking, sp.GetRequiredService<ITokenCounter>()));

        services.AddSingleton<DocumentRepository>();
        services.AddSingleton<ChunkRepository>();
        services.AddSingleton<SemanticCache>();
        services.AddSingleton<StatsRepository>();

        return services;
    }

    private static string Require(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Configuration manquante : {key} (variable d'environnement {key.Replace(":", "__")}).")
            : value;
}
