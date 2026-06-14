using System;
using System.Threading.Tasks;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class LadybugHealthCheckTests
{
    [Fact]
    public async Task Healthy_when_probe_query_succeeds()
    {
        var executor = FakeLadybugExecutor.Returning(
            new LadybugExecutionResult(new[] { "x" }, new[] { new object?[] { 1L } }));
        var check = new LadybugHealthCheck(executor);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, executor.ExecuteCount);
    }

    [Fact]
    public async Task Unhealthy_when_probe_query_throws()
    {
        var executor = new FakeLadybugExecutor((_, _) => throw new InvalidOperationException("down"));
        var check = new LadybugHealthCheck(executor);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task Uses_the_configured_probe_query()
    {
        string? seen = null;
        var executor = new FakeLadybugExecutor((q, _) =>
        {
            seen = q;
            return Task.FromResult(LadybugExecutionResult.Empty);
        });
        var check = new LadybugHealthCheck(executor, probeQuery: "RETURN 42");

        await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal("RETURN 42", seen);
    }
}
