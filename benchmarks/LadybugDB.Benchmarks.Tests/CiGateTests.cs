using System;
using System.Collections.Generic;
using System.Linq;
using LadybugDB.Benchmarks;
using Xunit;

namespace LadybugDB.Benchmarks.Tests;

/// <summary>
/// Non-gated unit tests for the pure <see cref="CiGate"/> latency-ratio logic. These never touch the
/// native engine or BenchmarkDotNet, so they are plain <see cref="FactAttribute"/>s that always run.
/// </summary>
public sealed class CiGateTests
{
    private static readonly LatencySample[] Baseline =
    {
        new("Query", 10.0, 1_000),
        new("Prepare", 5.0, 500),
    };

    [Fact]
    public void Within_ceiling_passes()
    {
        var candidate = new[] { new LatencySample("Query", 11.0, 1_100), new LatencySample("Prepare", 5.5, 500) };
        IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
        Assert.True(CiGate.AllPassed(results));
    }

    [Fact]
    public void Over_ceiling_fails_the_offending_benchmark()
    {
        var candidate = new[] { new LatencySample("Query", 14.0, 1_000), new LatencySample("Prepare", 5.0, 500) };
        IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
        Assert.False(CiGate.AllPassed(results));
        GateResult query = results.Single(r => r.Name == "Query");
        Assert.False(query.Passed);
        Assert.Equal(1.4, query.Ratio, 3);
    }

    [Fact]
    public void Allocation_over_ceiling_fails_the_offending_benchmark()
    {
        var candidate = new[] { new LatencySample("Query", 10.0, 1_500), new LatencySample("Prepare", 5.0, 500) };
        IReadOnlyList<GateResult> results = CiGate.Evaluate(
            Baseline,
            candidate,
            ceiling: 1.25,
            allocationCeiling: 1.25);

        Assert.False(CiGate.AllPassed(results));
        GateResult query = results.Single(r => r.Name == "Query");
        Assert.False(query.Passed);
        Assert.True(query.LatencyPassed);
        Assert.False(query.AllocationPassed);
        Assert.Equal(1.5, query.AllocationRatio);
    }

    [Fact]
    public void Missing_allocation_data_only_gates_latency()
    {
        var baseline = new[] { new LatencySample("Query", 10.0, AllocatedBytes: null) };
        var candidate = new[] { new LatencySample("Query", 11.0, 99_000) };
        IReadOnlyList<GateResult> results = CiGate.Evaluate(baseline, candidate, ceiling: 1.25);

        GateResult query = results.Single();
        Assert.True(query.Passed);
        Assert.Null(query.AllocationRatio);
    }

    [Fact]
    public void New_benchmark_without_baseline_passes()
    {
        var candidate = new[] { new LatencySample("BrandNew", 99.0, 99_000) };
        IReadOnlyList<GateResult> results = CiGate.Evaluate(Baseline, candidate, ceiling: 1.25);
        Assert.True(CiGate.AllPassed(results));
    }

    [Fact]
    public void Non_positive_baseline_throws()
    {
        var bad = new[] { new LatencySample("Query", 0.0) };
        var candidate = new[] { new LatencySample("Query", 10.0) };
        Assert.Throws<ArgumentException>(() => CiGate.Evaluate(bad, candidate, ceiling: 1.25));
    }

    [Fact]
    public void Resolve_ceiling_parses_env_or_falls_back()
    {
        Assert.Equal(1.5, CiGate.ResolveCeiling("1.5"));
        Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling(null));
        Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling("garbage"));
        Assert.Equal(CiGate.DefaultCeiling, CiGate.ResolveCeiling("-2"));
    }

    [Fact]
    public void Gate_result_to_string_reports_pass_fail_and_ratio()
    {
        var pass = new GateResult("Query", 1.1, 1.25, Passed: true);
        Assert.StartsWith("PASS Query:", pass.ToString());
        var fail = new GateResult("Query", 2.0, 1.25, Passed: false);
        Assert.StartsWith("FAIL Query:", fail.ToString());
        var allocationFail = new GateResult("Query", 1.0, 1.25, true, 1.5, 1.25, false);
        Assert.Contains("alloc ratio 1.5", allocationFail.ToString());
    }
}
