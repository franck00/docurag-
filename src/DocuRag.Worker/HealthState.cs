namespace DocuRag.Worker;

/// <summary>État de santé partagé entre le processeur Service Bus et le heartbeat.</summary>
public sealed class HealthState
{
    private volatile bool _started;

    public bool Started => _started;

    public void MarkStarted() => _started = true;
}

/// <summary>
/// Le worker n'expose pas de port HTTP. Il écrit un fichier "heartbeat" toutes les 15 s ;
/// le HEALTHCHECK du Dockerfile vérifie que ce fichier a moins de 60 s.
/// </summary>
public sealed class HeartbeatService(HealthState health, ILogger<HeartbeatService> logger) : BackgroundService
{
    public const string HeartbeatPath = "/tmp/docurag-heartbeat";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            if (health.Started)
            {
                try
                {
                    await File.WriteAllTextAsync(HeartbeatPath, DateTimeOffset.UtcNow.ToString("O"), stoppingToken);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Impossible d'écrire le heartbeat");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
