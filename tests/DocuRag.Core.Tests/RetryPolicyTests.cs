using DocuRag.Core.AI;

namespace DocuRag.Core.Tests;

public class RetryPolicyTests
{
    private sealed class TransientException : Exception
    {
    }

    private static RetryPolicy Create(int maxAttempts, List<TimeSpan> delays, Func<Exception, TimeSpan?>? retryAfter = null) => new(
        maxAttempts,
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(10),
        ex => ex is TransientException,
        retryAfter,
        (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

    [Fact]
    public async Task Retries_transient_errors_with_growing_delays_then_succeeds()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        var result = await Create(5, delays).ExecuteAsync(_ =>
        {
            calls++;
            return calls < 4 ? throw new TransientException() : Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(4, calls);
        Assert.Equal(3, delays.Count);
        Assert.True(delays[1] > delays[0] - TimeSpan.FromMilliseconds(100));
        Assert.True(delays[2] >= TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task Gives_up_after_max_attempts()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        await Assert.ThrowsAsync<TransientException>(() => Create(3, delays).ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new TransientException();
        }, CancellationToken.None));

        Assert.Equal(3, calls);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Does_not_retry_permanent_errors()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(5, delays).ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new InvalidOperationException();
        }, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Honors_retry_after_hint()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        await Create(2, delays, _ => TimeSpan.FromSeconds(3)).ExecuteAsync(_ =>
        {
            calls++;
            return calls == 1 ? throw new TransientException() : Task.FromResult(true);
        }, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(delays));
    }

    [Fact]
    public void Delay_is_capped()
    {
        var policy = Create(20, []);
        Assert.True(policy.ComputeDelay(15, null) <= TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.ComputeDelay(1, TimeSpan.FromMinutes(5)));
    }
}
