using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LadybugDB.Benchmarks;

/// <summary>One benchmark's mean latency in milliseconds.</summary>
public readonly record struct LatencySample(string Name, double MeanMs);

/// <summary>A single gate decision for one benchmark.</summary>
public readonly record struct GateResult(string Name, double Ratio, double Ceiling, bool Passed)
{
    public override string ToString() =>
        $"{(Passed ? "PASS" : "FAIL")} {Name}: ratio {Ratio.ToString("0.###", CultureInfo.InvariantCulture)} " +
        $"(ceiling {Ceiling.ToString("0.###", CultureInfo.InvariantCulture)})";
}

/// <summary>
/// Pure latency-regression gate: for each candidate sample with a matching baseline, computes the
/// candidate/baseline mean ratio and fails when it exceeds the ceiling. The ceiling comes from the
/// <c>LADYBUG_BENCH_RATIO_MAX</c> environment variable (default 1.25 = allow a 25% regression).
/// Native- and BenchmarkDotNet-free so it is unit-tested without staging an engine.
/// </summary>
public static class CiGate
{
    public const double DefaultCeiling = 1.25;

    /// <summary>The environment variable that overrides the ratio ceiling.</summary>
    public const string CeilingEnvVar = "LADYBUG_BENCH_RATIO_MAX";

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

    /// <summary>
    /// Evaluates each candidate against the baseline of the same name. Candidates with no baseline
    /// are reported as passing (a new benchmark cannot regress). Throws <see cref="ArgumentException"/>
    /// if a baseline mean is non-positive (a corrupt baseline, not a regression).
    /// </summary>
    public static IReadOnlyList<GateResult> Evaluate(
        IEnumerable<LatencySample> baseline,
        IEnumerable<LatencySample> candidate,
        double ceiling)
    {
        Dictionary<string, double> baselineByName =
            baseline.ToDictionary(b => b.Name, b => b.MeanMs, StringComparer.Ordinal);

        var results = new List<GateResult>();
        foreach (LatencySample c in candidate)
        {
            if (!baselineByName.TryGetValue(c.Name, out double baseMs))
            {
                results.Add(new GateResult(c.Name, 1.0, ceiling, Passed: true));
                continue;
            }

            if (baseMs <= 0)
            {
                throw new ArgumentException($"Baseline mean for '{c.Name}' is non-positive ({baseMs}).");
            }

            double ratio = c.MeanMs / baseMs;
            results.Add(new GateResult(c.Name, ratio, ceiling, Passed: ratio <= ceiling));
        }

        return results;
    }

    /// <summary>True when every gate result passed.</summary>
    public static bool AllPassed(IReadOnlyList<GateResult> results) => results.All(r => r.Passed);
}
