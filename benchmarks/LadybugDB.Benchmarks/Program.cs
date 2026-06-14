using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    IReadOnlyList<GateResult> results = CiGate.Evaluate(baseline, candidate, ceiling);
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

BenchmarkRunner.Run<QueryBenchmarks>();
return 0;

static List<LatencySample> LoadSamples(string path)
{
    // Accepts either our own [{ "Name": ..., "MeanMs": ... }] array, or a BenchmarkDotNet
    // "*-report-full.json" file (Benchmarks[].{Method}, Statistics.Mean in ns).
    using FileStream fs = File.OpenRead(path);
    using JsonDocument doc = JsonDocument.Parse(fs);
    JsonElement root = doc.RootElement;

    if (root.ValueKind == JsonValueKind.Array)
    {
        return root.EnumerateArray()
            .Select(e => new LatencySample(
                e.GetProperty("Name").GetString() ?? "",
                e.GetProperty("MeanMs").GetDouble()))
            .ToList();
    }

    var samples = new List<LatencySample>();
    foreach (JsonElement b in root.GetProperty("Benchmarks").EnumerateArray())
    {
        string name = b.TryGetProperty("Method", out JsonElement m) ? m.GetString() ?? "" : "";
        double meanNs = b.GetProperty("Statistics").GetProperty("Mean").GetDouble();
        samples.Add(new LatencySample(name, meanNs / 1_000_000.0)); // ns -> ms
    }

    return samples;
}
