using DocuRag.Core.Models;
using DocuRag.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Worker;

/// <summary>
/// Change feed processor (J13) sur le conteneur <c>documents</c> : à chaque document qui
/// passe à <c>Indexed</c>, met à jour un document de statistiques agrégées (nombre de
/// documents indexés, total de chunks). Utilise un conteneur de baux pour la reprise.
/// </summary>
public sealed class StatsChangeFeedService : BackgroundService
{
    private const string StatsDocId = "__stats__";

    private readonly CosmosClient _client;
    private readonly CosmosOptions _options;
    private readonly ILogger<StatsChangeFeedService> _logger;
    private ChangeFeedProcessor? _processor;

    public StatsChangeFeedService(
        CosmosClient client,
        IOptions<CosmosOptions> options,
        ILogger<StatsChangeFeedService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var documents = _client.GetContainer(_options.Database, _options.DocumentsContainer);
        var leases = _client.GetContainer(_options.Database, _options.LeasesContainer);

        _processor = documents
            .GetChangeFeedProcessorBuilder<DocumentRecord>("stats", HandleChangesAsync)
            .WithInstanceName(Environment.MachineName)
            .WithLeaseContainer(leases)
            .Build();

        try
        {
            await _processor.StartAsync();
            _logger.LogInformation("Change feed processor (stats) démarré.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }
        finally
        {
            if (_processor is not null)
            {
                await _processor.StopAsync();
            }
        }
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<DocumentRecord> changes,
        CancellationToken cancellationToken)
    {
        var newlyIndexed = 0;
        var chunksAdded = 0;

        foreach (var doc in changes)
        {
            // Le stats-doc lui-même transite aussi par le change feed : on l'ignore.
            if (doc.Id == StatsDocId)
            {
                continue;
            }

            if (doc.Status == DocumentStatus.Indexed)
            {
                newlyIndexed++;
                chunksAdded += doc.ChunkCount;
            }
        }

        if (newlyIndexed == 0)
        {
            return;
        }

        var documents = _client.GetContainer(_options.Database, _options.DocumentsContainer);
        await IncrementStatsAsync(documents, newlyIndexed, chunksAdded, cancellationToken);

        _logger.LogInformation(
            "Stats mises à jour : +{Docs} documents indexés, +{Chunks} chunks.",
            newlyIndexed, chunksAdded);
    }

    private static async Task IncrementStatsAsync(
        Container documents, int docs, int chunks, CancellationToken cancellationToken)
    {
        var key = new PartitionKey(StatsDocId);
        var patch = new[]
        {
            PatchOperation.Increment("/indexedDocuments", docs),
            PatchOperation.Increment("/totalChunks", chunks),
            PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow),
        };

        try
        {
            await documents.PatchItemAsync<dynamic>(
                StatsDocId, key, patch, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Première fois : on crée le document de stats puis on réapplique l'incrément.
            await documents.CreateItemAsync(
                new
                {
                    id = StatsDocId,
                    indexedDocuments = docs,
                    totalChunks = chunks,
                    updatedAt = DateTimeOffset.UtcNow,
                },
                key,
                cancellationToken: cancellationToken);
        }
    }
}
