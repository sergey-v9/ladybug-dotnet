using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LadybugDB.Extensions;

/// <summary>
/// An <see cref="IHealthCheck"/> that runs a cheap probe query against the Ladybug executor and
/// reports healthy/unhealthy accordingly.
/// </summary>
public sealed class LadybugHealthCheck : IHealthCheck
{
    private readonly ILadybugExecutor _executor;
    private readonly string _probeQuery;

    public LadybugHealthCheck(ILadybugExecutor executor, string probeQuery = "RETURN 1")
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _probeQuery = probeQuery ?? throw new ArgumentNullException(nameof(probeQuery));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LadybugExecutionResult result =
                await _executor.ExecuteAsync(_probeQuery, cancellationToken).ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["executor"] = _executor.Name,
                ["columns"] = result.Columns.Count,
            };

            return HealthCheckResult.Healthy($"Ladybug executor '{_executor.Name}' is healthy.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Ladybug executor '{_executor.Name}' health probe failed.", ex);
        }
    }
}
