using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Running;
using LadybugDB.Benchmarks;

if (args.Length > 0 && args[0] == "--ci-gate")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: --ci-gate <baseline.json> <candidate.json>");
        return 2;
    }

    List<LatencySample> baseline = LoadSamples(args[1]);
    List<LatencySample> candidate = LoadSamples(args[2]);
    double ceiling = CiGate.ResolveCeilingFromEnvironment();
    double allocationCeiling = CiGate.ResolveAllocationCeilingFromEnvironment();

    IReadOnlyList<GateResult> results = CiGate.Evaluate(baseline, candidate, ceiling, allocationCeiling);
    foreach (GateResult r in results)
    {
        Console.WriteLine(r.ToString());
    }

    bool ok = CiGate.AllPassed(results);
    Console.WriteLine(ok ? "ci-gate: PASS" : "ci-gate: FAIL");
    return ok ? 0 : 1;
}

if (args.Length > 0 && args[0] == "--diff")
{
    return DifferentialRunner.Run(Console.Out) ? 0 : 1;
}

BenchmarkSwitcher.FromAssembly(typeof(QueryBenchmarks).Assembly).Run(args);
return 0;

static List<LatencySample> LoadSamples(string path)
{
    // Accepts either our compact baseline array or a BenchmarkDotNet "*-report-full.json" file.
    using FileStream fs = File.OpenRead(path);
    using JsonDocument doc = JsonDocument.Parse(fs);
    JsonElement root = doc.RootElement;

    if (root.ValueKind == JsonValueKind.Array)
    {
        var simpleSamples = new List<LatencySample>();
        foreach (JsonElement e in root.EnumerateArray())
        {
            simpleSamples.Add(new LatencySample(
                e.GetProperty("Name").GetString() ?? "",
                e.GetProperty("MeanMs").GetDouble(),
                ReadFirstDouble(e, "AllocatedBytes", "AllocatedBytesPerOperation", "AllocatedBytesPerOp")));
        }

        return simpleSamples;
    }

    var samples = new List<LatencySample>();
    foreach (JsonElement b in root.GetProperty("Benchmarks").EnumerateArray())
    {
        string name = b.TryGetProperty("Method", out JsonElement m) ? m.GetString() ?? "" : "";
        double meanNs = b.GetProperty("Statistics").GetProperty("Mean").GetDouble();
        samples.Add(new LatencySample(name, meanNs / 1_000_000.0, ReadAllocatedBytes(b))); // ns -> ms
    }

    return samples;
}

static double? ReadAllocatedBytes(JsonElement benchmark)
{
    if (benchmark.TryGetProperty("Memory", out JsonElement memory))
    {
        double? value = ReadFirstDouble(memory, "BytesAllocatedPerOperation", "AllocatedBytesPerOperation", "AllocatedBytesPerOp", "AllocatedBytes");
        if (value.HasValue)
        {
            return value;
        }
    }

    double? topLevel = ReadFirstDouble(benchmark, "BytesAllocatedPerOperation", "AllocatedBytesPerOperation", "AllocatedBytesPerOp", "AllocatedBytes");
    if (topLevel.HasValue)
    {
        return topLevel;
    }

    if (!benchmark.TryGetProperty("Metrics", out JsonElement metrics) || metrics.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    foreach (JsonProperty metric in metrics.EnumerateObject())
    {
        if (!metric.Name.Contains("Alloc", StringComparison.OrdinalIgnoreCase) &&
            !metric.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (metric.Value.ValueKind == JsonValueKind.Number && metric.Value.TryGetDouble(out double directValue))
        {
            return directValue;
        }

        double? value = ReadFirstDouble(metric.Value, "Value", "Nanoseconds", "Bytes");
        if (value.HasValue)
        {
            return value;
        }
    }

    return null;
}

static double? ReadFirstDouble(JsonElement element, params string[] names)
{
    if (element.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    foreach (string name in names)
    {
        if (element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out double number))
        {
            return number;
        }
    }

    return null;
}
