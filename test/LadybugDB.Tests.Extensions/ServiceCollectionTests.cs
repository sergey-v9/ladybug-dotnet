using System;
using LadybugDB.Extensions;
using LadybugDB.Tests.Extensions.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class ServiceCollectionTests
{
    [Fact]
    public void Options_default_database_path_is_empty()
    {
        var options = new LadybugOptions();
        Assert.Equal(string.Empty, options.DatabasePath);
        Assert.False(options.ReadOnly);
    }

    [Fact]
    public void AddLadybug_configures_options_and_registers_executor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
        services.AddLadybug(o => o.DatabasePath = "/tmp/db");

        using ServiceProvider provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<LadybugOptions>>().Value;
        Assert.Equal("/tmp/db", options.DatabasePath);
        Assert.NotNull(provider.GetRequiredService<ILadybugExecutor>());
    }

    [Fact]
    public void AddLadybugResilience_wraps_the_registered_executor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
        services.AddLadybugResilience(o => o.MaxRetryAttempts = 1);

        using ServiceProvider provider = services.BuildServiceProvider();
        ILadybugExecutor executor = provider.GetRequiredService<ILadybugExecutor>();
        Assert.IsType<ResilientLadybugExecutor>(executor);
    }

    [Fact]
    public void AddLadybugHealthCheck_registers_a_named_check()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILadybugExecutor>(FakeLadybugExecutor.Returning(LadybugExecutionResult.Empty));
        services.AddLadybugHealthCheck("graph");

        using ServiceProvider provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "graph");
    }
}
