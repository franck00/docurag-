using System.ClientModel;
using Microsoft.Extensions.Logging;

namespace DocuRag.Core.AI;

/// <summary>
/// Retry avec backoff exponentiel + jitter, qui respecte l'en-tête <c>Retry-After</c> quand le service le fournit.
/// Utilisé autour des appels Azure OpenAI (le throttling 429 est la norme dès qu'on vectorise un lot de PDF).
/// Le retry intégré du SDK est désactivé pour que ce comportement soit explicite et testable.
/// </summary>
public sealed class RetryPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly Func<Exception, bool> _isTransient;
    private readonly Func<Exception, TimeSpan?> _retryAfter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger? _logger;

    public RetryPolicy(
        int maxAttempts,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        Func<Exception, bool> isTransient,
        Func<Exception, TimeSpan?>? retryAfter = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger? logger = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
        _isTransient = isTransient;
        _retryAfter = retryAfter ?? (_ => null);
        _delay = delay ?? Task.Delay;
        _logger = logger;
    }

    /// <summary>Politique standard pour Azure OpenAI : 429, 408 et 5xx sont retentés.</summary>
    public static RetryPolicy ForAzureOpenAI(int maxAttempts, ILogger? logger = null) => new(
        maxAttempts,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(60),
        IsTransientOpenAIError,
        GetRetryAfter,
        logger: logger);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxAttempts && _isTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                var delay = ComputeDelay(attempt, _retryAfter(ex));
                _logger?.LogWarning(ex, "Erreur transitoire (tentative {Attempt}/{MaxAttempts}), nouvel essai dans {DelayMs} ms",
                    attempt, _maxAttempts, (int)delay.TotalMilliseconds);
                await _delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } hinted && hinted > TimeSpan.Zero)
        {
            return hinted < _maxDelay ? hinted : _maxDelay;
        }

        var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * _baseDelay.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(Math.Min(_maxDelay.TotalMilliseconds, exponential + jitter));
    }

    public static bool IsTransientOpenAIError(Exception ex) =>
        ex is ClientResultException { Status: 429 or 408 or >= 500 };

    private static TimeSpan? GetRetryAfter(Exception ex)
    {
        if (ex is not ClientResultException clientError || clientError.GetRawResponse() is not { } response)
        {
            return null;
        }

        if (response.Headers.TryGetValue("retry-after-ms", out var ms) && double.TryParse(ms, out var millis))
        {
            return TimeSpan.FromMilliseconds(millis);
        }

        if (response.Headers.TryGetValue("Retry-After", out var seconds) && double.TryParse(seconds, out var secs))
        {
            return TimeSpan.FromSeconds(secs);
        }

        return null;
    }
}
