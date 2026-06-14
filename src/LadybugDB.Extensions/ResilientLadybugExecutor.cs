using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace LadybugDB.Extensions;

/// <summary>Thrown when the resilience circuit breaker is open and short-circuits an execute call.</summary>
public sealed class LadybugCircuitOpenException : InvalidOperationException
{
    public LadybugCircuitOpenException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Decorates an <see cref="ILadybugExecutor"/> with a per-call timeout (linked CTS), transient-aware
/// retry with delay, and a lock-guarded consecutive-failure circuit breaker. No Polly dependency.
/// </summary>
public sealed class ResilientLadybugExecutor : ILadybugExecutor
{
    private readonly ILadybugExecutor _inner;
    private readonly LadybugResilienceOptions _options;
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;

    public ResilientLadybugExecutor(ILadybugExecutor inner, IOptions<LadybugResilienceOptions> options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => _inner.Name;

    public async Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default)
    {
        ThrowIfCircuitOpen();

        int maxAttempts = Math.Max(1, _options.MaxRetryAttempts + 1);
        Exception? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.Timeout);

            try
            {
                LadybugExecutionResult result = await _inner.ExecuteAsync(cypher, timeoutCts.Token)
                    .ConfigureAwait(false);
                OnSuccess();
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller-driven cancellation: propagate as-is, do not count as a failure.
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastError = new TimeoutException(
                    $"The Ladybug query timed out after {_options.Timeout.TotalSeconds:0.###}s.", ex);
                OnFailure();
            }
            catch (Exception ex)
            {
                lastError = ex;
                OnFailure();
                if (!IsTransient(ex))
                {
                    throw;
                }
            }

            if (!_options.EnableRetries || attempt == maxAttempts)
            {
                break;
            }

            await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        throw lastError ?? new InvalidOperationException("Resilient execution failed without an error.");
    }

    private static bool IsTransient(Exception ex)
        => ex is TimeoutException or OperationCanceledException;

    private void ThrowIfCircuitOpen()
    {
        lock (_sync)
        {
            if (_openUntil is null)
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= _openUntil.Value)
            {
                _openUntil = null;
                _consecutiveFailures = 0;
                return;
            }

            throw new LadybugCircuitOpenException(
                $"The Ladybug circuit breaker is open until {_openUntil.Value:O}.");
        }
    }

    private void OnSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
        }
    }

    private void OnFailure()
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= Math.Max(1, _options.CircuitBreakerFailureThreshold))
            {
                _openUntil = DateTimeOffset.UtcNow.Add(_options.CircuitBreakerBreakDuration);
            }
        }
    }
}
