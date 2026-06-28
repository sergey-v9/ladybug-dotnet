using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LadybugDB.Benchmarks;

/// <summary>One benchmark's mean latency and optional allocation count.</summary>
public readonly record struct LatencySample(string Name, double MeanMs, double? AllocatedBytes = null);

/// <summary>A single gate decision for one benchmark.</summary>
public readonly record struct GateResult
{
    public GateResult(string name, double ratio, double ceiling, bool Passed)
        : this(name, ratio, ceiling, Passed, allocationRatio: null, allocationCeiling: null, allocationPassed: true)
    {
    }

    public GateResult(
        string name,
        double ratio,
        double ceiling,
        bool latencyPassed,
        double? allocationRatio,
        double? allocationCeiling,
        bool allocationPassed)
    {
        Name = name;
        Ratio = ratio;
        Ceiling = ceiling;
        LatencyPassed = latencyPassed;
        AllocationRatio = allocationRatio;
        AllocationCeiling = allocationCeiling;
        AllocationPassed = allocationPassed;
    }

    public string Name { get; init; }

    /// <summary>Candidate/baseline latency ratio.</summary>
    public double Ratio { get; init; }

    /// <summary>Maximum allowed latency ratio.</summary>
    public double Ceiling { get; init; }

    public bool LatencyPassed { get; init; }

    /// <summary>Candidate/baseline allocation ratio, when both samples include allocation data.</summary>
    public double? AllocationRatio { get; init; }

    /// <summary>Maximum allowed allocation ratio, when allocation data is gated.</summary>
    public double? AllocationCeiling { get; init; }

    public bool AllocationPassed { get; init; }

    public bool Passed => LatencyPassed && AllocationPassed;

    public override string ToString()
    {
        string line = $"{(Passed ? "PASS" : "FAIL")} {Name}: ratio {Format(Ratio)} (ceiling {Format(Ceiling)})";
        if (AllocationRatio is double allocationRatio && AllocationCeiling is double allocationCeiling)
        {
            line += $", alloc ratio {Format(allocationRatio)} (ceiling {Format(allocationCeiling)})";
        }

        return line;
    }

    private static string Format(double value) =>
        double.IsPositiveInfinity(value)
            ? "inf"
            : value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Pure regression gate: for each candidate sample with a matching baseline, computes latency and
/// optional allocation ratios and fails when either exceeds its ceiling. The latency ceiling comes
/// from <c>LADYBUG_BENCH_RATIO_MAX</c>; allocation uses <c>LADYBUG_BENCH_ALLOC_RATIO_MAX</c>.
/// Native- and BenchmarkDotNet-free so it is unit-tested without staging an engine.
/// </summary>
public static class CiGate
{
    public const double DefaultCeiling = 1.25;

    public const double DefaultAllocationCeiling = 1.25;

    /// <summary>The environment variable that overrides the ratio ceiling.</summary>
    public const string CeilingEnvVar = "LADYBUG_BENCH_RATIO_MAX";

    /// <summary>The environment variable that overrides the allocation ratio ceiling.</summary>
    public const string AllocationCeilingEnvVar = "LADYBUG_BENCH_ALLOC_RATIO_MAX";

    /// <summary>A legacy alias accepted for the ratio ceiling env var.</summary>
    public const string LegacyCeilingEnvVar = "LADYBUG_BENCH_MAX_RATIO";

    public static double ResolveCeiling(string? raw, double fallback = DefaultCeiling) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0
            ? v
            : fallback;

    /// <summary>
    /// Resolves the ceiling from the environment, preferring <see cref="CeilingEnvVar"/> and falling
    /// back to the legacy <see cref="LegacyCeilingEnvVar"/>, then to <see cref="DefaultCeiling"/>.
    /// </summary>
    public static double ResolveCeilingFromEnvironment()
    {
        string? primary = Environment.GetEnvironmentVariable(CeilingEnvVar);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return ResolveCeiling(primary);
        }

        return ResolveCeiling(Environment.GetEnvironmentVariable(LegacyCeilingEnvVar));
    }

    public static double ResolveAllocationCeilingFromEnvironment() =>
        ResolveCeiling(Environment.GetEnvironmentVariable(AllocationCeilingEnvVar), DefaultAllocationCeiling);

    /// <summary>
    /// Evaluates each candidate against the baseline of the same name. Candidates with no baseline
    /// are reported as passing (a new benchmark cannot regress). Allocation is gated only when both
    /// sides include allocation data. Throws <see cref="ArgumentException"/> if a baseline mean is
    /// non-positive (a corrupt baseline, not a regression).
    /// </summary>
    public static IReadOnlyList<GateResult> Evaluate(
        IEnumerable<LatencySample> baseline,
        IEnumerable<LatencySample> candidate,
        double ceiling,
        double allocationCeiling = DefaultAllocationCeiling)
    {
        Dictionary<string, LatencySample> baselineByName =
            baseline.ToDictionary(b => b.Name, StringComparer.Ordinal);

        var results = new List<GateResult>();
        foreach (LatencySample c in candidate)
        {
            if (!baselineByName.TryGetValue(c.Name, out LatencySample b))
            {
                results.Add(new GateResult(c.Name, 1.0, ceiling, Passed: true));
                continue;
            }

            if (b.MeanMs <= 0)
            {
                throw new ArgumentException($"Baseline mean for '{c.Name}' is non-positive ({b.MeanMs}).");
            }

            double ratio = c.MeanMs / b.MeanMs;
            bool latencyPassed = ratio <= ceiling;
            double? allocationRatio = null;
            bool allocationPassed = true;

            if (b.AllocatedBytes is double baseBytes && c.AllocatedBytes is double candidateBytes)
            {
                if (baseBytes < 0)
                {
                    throw new ArgumentException(
                        $"Baseline allocated bytes for '{c.Name}' is negative ({baseBytes}).");
                }

                allocationRatio = baseBytes == 0
                    ? candidateBytes == 0 ? 1.0 : double.PositiveInfinity
                    : candidateBytes / baseBytes;
                allocationPassed = allocationRatio <= allocationCeiling;
            }

            results.Add(new GateResult(
                c.Name,
                ratio,
                ceiling,
                latencyPassed,
                allocationRatio,
                allocationRatio.HasValue ? allocationCeiling : null,
                allocationPassed));
        }

        return results;
    }

    /// <summary>True when every gate result passed.</summary>
    public static bool AllPassed(IReadOnlyList<GateResult> results) => results.All(r => r.Passed);
}
