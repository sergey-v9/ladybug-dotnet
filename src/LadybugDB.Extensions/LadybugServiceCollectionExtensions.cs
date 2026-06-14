using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LadybugDB.Extensions;

/// <summary>Dependency-injection entry points for the Ladybug extensions.</summary>
public static class LadybugServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LadybugOptions"/> from <paramref name="cfg"/>. The caller is expected to
    /// register an <see cref="ILadybugExecutor"/> (e.g. a <see cref="LadybugConnectionExecutor"/> over
    /// a connection it owns); this method wires options and is the anchor other Add* calls build on.
    /// </summary>
    public static IServiceCollection AddLadybug(this IServiceCollection s, Action<LadybugOptions> cfg)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        if (cfg is null)
        {
            throw new ArgumentNullException(nameof(cfg));
        }

        s.Configure(cfg);
        return s;
    }

    /// <summary>Registers a <see cref="LadybugHealthCheck"/> under <paramref name="name"/>.</summary>
    public static IServiceCollection AddLadybugHealthCheck(this IServiceCollection s, string name = "ladybug")
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        s.AddHealthChecks().Add(new HealthCheckRegistration(
            name,
            provider => new LadybugHealthCheck(provider.GetRequiredService<ILadybugExecutor>()),
            failureStatus: null,
            tags: null));

        return s;
    }

    /// <summary>
    /// Decorates the registered <see cref="ILadybugExecutor"/> with a
    /// <see cref="ResilientLadybugExecutor"/> (timeout/retry/circuit-breaker).
    /// </summary>
    public static IServiceCollection AddLadybugResilience(
        this IServiceCollection s,
        Action<LadybugResilienceOptions>? cfg = null)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        if (cfg is null)
        {
            s.AddOptions<LadybugResilienceOptions>();
        }
        else
        {
            s.Configure(cfg);
        }

        // Capture the inner executor registered before this call, then replace the public
        // ILadybugExecutor with the resilient decorator.
        ServiceDescriptor? inner = null;
        for (int i = s.Count - 1; i >= 0; i--)
        {
            if (s[i].ServiceType == typeof(ILadybugExecutor))
            {
                inner = s[i];
                break;
            }
        }

        if (inner is null)
        {
            throw new InvalidOperationException(
                "AddLadybugResilience requires an ILadybugExecutor to be registered first.");
        }

        s.AddSingleton(provider =>
        {
            ILadybugExecutor innerExecutor = Materialize(provider, inner);
            IOptions<LadybugResilienceOptions> options =
                provider.GetRequiredService<IOptions<LadybugResilienceOptions>>();
            return new ResilientLadybugExecutor(innerExecutor, options);
        });

        s.AddSingleton<ILadybugExecutor>(provider => provider.GetRequiredService<ResilientLadybugExecutor>());
        return s;
    }

    private static ILadybugExecutor Materialize(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ILadybugExecutor instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (ILadybugExecutor)factory(provider);
        }

        if (descriptor.ImplementationType is { } type)
        {
            return (ILadybugExecutor)ActivatorUtilities.CreateInstance(provider, type);
        }

        throw new InvalidOperationException("The registered ILadybugExecutor cannot be resolved.");
    }
}
