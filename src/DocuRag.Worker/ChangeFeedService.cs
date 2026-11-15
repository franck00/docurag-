using DocuRag.Core.Cosmos;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace DocuRag.Worker;

/// <summary>
/// Change feed processor sur le conteneur <c>documents</c> (J13).
/// À chaque document qui passe à <c>Indexed</c> (ou <c>Failed</c>) :
/// <list type="bullet">
/// <item>mise à jour des statistiques d'index (conteneur <c>stats</c>) ;</item>
/// <item>invalidation des réponses en cache qui citent ce document (il a peut-être changé).</item>
/// </list>
/// Les baux (leases) sont dans le conteneur <c>leases</c> : plusieurs replicas du worker se partagent
/// le travail automatiquement, et le traitement reprend là où il s'était arrêté après un redémarrage.
/// </summary>
public sealed class ChangeFeedService(
    CosmosClient client,
    StatsRepository stats,
    SemanticCache cache,
    IOptions<DocuRagOptions> options,
    ILogger<ChangeFeedService> logger) : IHostedService
{
    private ChangeFeedProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cosmos = options.Value.Cosmos;
        var database = client.GetDatabase(cosmos.Database);

        _processor = database.GetContainer(cosmos.DocumentsContainer)
            .GetChangeFeedProcessorBuilder<DocumentRecord>("docurag-stats", HandleChangesAsync)
            .WithInstanceName($"{Environment.MachineName}-{Environment.ProcessId}")
            .WithLeaseContainer(database.GetContainer(cosmos.LeasesContainer))
            .WithStartTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc))
            .WithErrorNotification((lease, ex) =>
            {
                logger.LogError(ex, "Erreur du change feed sur le bail {Lease}", lease);
                return Task.CompletedTask;
            })
            .Build();

        await _processor.StartAsync();
        logger.LogInformation("Change feed processor démarré sur {Container}", cosmos.DocumentsContainer);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopAsync();
        }
    }

    private async Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<DocumentRecord> changes,
        CancellationToken cancellationToken)
    {
        var finished = changes
            .Where(c => c.Status is DocumentStatus.Indexed or DocumentStatus.Failed)
            .ToList();
        if (finished.Count == 0)
        {
            return;
        }

        await stats.ApplyAsync(finished, cancellationToken);

        var invalidated = 0;
        foreach (var document in finished.Where(d => d.Status == DocumentStatus.Indexed))
        {
            invalidated += await cache.InvalidateDocumentAsync(document.Id, cancellationToken);
        }

        logger.LogInformation("Change feed : {Count} documents comptabilisés, {Invalidated} réponses en cache invalidées (bail {Lease})",
            finished.Count, invalidated, context.LeaseToken);
    }
}
