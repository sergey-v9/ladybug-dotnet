using System;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class ResilientLadybugExecutorTests
{
    private static ResilientLadybugExecutor Wrap(ILadybugExecutor inner, LadybugResilienceOptions options)
        => new(inner, Options.Create(options));

    [Fact]
    public async Task Passes_through_on_success()
    {
        var inner = FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty);
        var sut = Wrap(inner, new LadybugResilienceOptions { EnableRetries = false });

        LadybugExecutionResult result = await sut.ExecuteAsync("RETURN 1");

        Assert.Same(LadybugExecutionResult.Empty, result);
        Assert.Equal(1, inner.ExecuteCount);
    }

    [Fact]
    public async Task Retries_transient_failures_then_succeeds()
    {
        int calls = 0;
        var inner = new FakeLadybugExecutor((_, _) =>
        {
            calls++;
            if (calls < 2)
            {
                throw new TimeoutException("transient");
            }

            return Task.FromResult(LadybugExecutionResult.Empty);
        });
        var sut = Wrap(inner, new LadybugResilienceOptions
        {
            MaxRetryAttempts = 2,
            RetryDelay = TimeSpan.Zero,
        });

        LadybugExecutionResult result = await sut.ExecuteAsync("RETURN 1");

        Assert.Same(LadybugExecutionResult.Empty, result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Non_transient_failure_is_not_retried()
    {
        int calls = 0;
        var inner = new FakeLadybugExecutor((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("hard failure");
        });
        var sut = Wrap(inner, new LadybugResilienceOptions { MaxRetryAttempts = 5, RetryDelay = TimeSpan.Zero });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync("RETURN 1"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Timeout_surfaces_as_TimeoutException()
    {
        var inner = new FakeLadybugExecutor(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return LadybugExecutionResult.Empty;
        });
        var sut = Wrap(inner, new LadybugResilienceOptions
        {
            Timeout = TimeSpan.FromMilliseconds(20),
            EnableRetries = false,
        });

        await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));
    }

    [Fact]
    public async Task Circuit_opens_after_threshold_then_rejects_fast()
    {
        var inner = new FakeLadybugExecutor((_, _) => throw new TimeoutException("boom"));
        var sut = Wrap(inner, new LadybugResilienceOptions
        {
            EnableRetries = false,
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerBreakDuration = TimeSpan.FromMinutes(5),
        });

        await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));
        await Assert.ThrowsAsync<TimeoutException>(() => sut.ExecuteAsync("RETURN 1"));

        // Circuit now open: rejects without invoking inner.
        int before = inner.ExecuteCount;
        await Assert.ThrowsAsync<LadybugCircuitOpenException>(() => sut.ExecuteAsync("RETURN 1"));
        Assert.Equal(before, inner.ExecuteCount);
    }

    [Fact]
    public async Task External_cancellation_is_not_wrapped_as_timeout()
    {
        using var cts = new CancellationTokenSource();
        var inner = new FakeLadybugExecutor(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return LadybugExecutionResult.Empty;
        });
        var sut = Wrap(inner, new LadybugResilienceOptions { EnableRetries = false });

        cts.CancelAfter(20);
        await Assert.ThrowsAsync<TaskCanceledException>(() => sut.ExecuteAsync("RETURN 1", cts.Token));
    }
}
