using System;

namespace LadybugDB.Extensions;

/// <summary>Settings for <see cref="ResilientLadybugExecutor"/> (timeout, retry, circuit breaker).</summary>
public sealed class LadybugResilienceOptions
{
    /// <summary>Maximum wall-clock time for a single execute attempt.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether transient failures are retried.</summary>
    public bool EnableRetries { get; set; } = true;

    /// <summary>Number of retries (in addition to the first attempt) for transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Consecutive failures before the circuit opens.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>How long the circuit stays open before allowing a probe.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
