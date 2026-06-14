using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Run;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;
using Path = System.IO.Path;

namespace LadybugDB.Build.Tasks;

/// <summary>
/// Runs the benchmark project's <c>--ci-gate</c> over committed baseline/candidate JSON when both
/// are present. Opt-in: only runs when BENCH_BASELINE and BENCH_CANDIDATE env vars point at files,
/// so the default Test/Pack flow is unaffected. The latency ceiling comes from
/// LADYBUG_BENCH_RATIO_MAX (read inside the benchmark process).
/// </summary>
[TaskName("BenchCiGate")]
public sealed class BenchCiGateTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context)
    {
        string? baseline = Environment.GetEnvironmentVariable("BENCH_BASELINE");
        string? candidate = Environment.GetEnvironmentVariable("BENCH_CANDIDATE");
        return !string.IsNullOrEmpty(baseline) && File.Exists(baseline)
            && !string.IsNullOrEmpty(candidate) && File.Exists(candidate);
    }

    public override void Run(BuildContext context)
    {
        string baseline = Environment.GetEnvironmentVariable("BENCH_BASELINE")!;
        string candidate = Environment.GetEnvironmentVariable("BENCH_CANDIDATE")!;
        string project = Path.Combine(context.Root, "benchmarks", "LadybugDB.Benchmarks", "LadybugDB.Benchmarks.csproj");

        context.DotNetRun(project, new DotNetRunSettings
        {
            Configuration = context.BuildConfiguration,
            ArgumentCustomization = a => a
                .Append("--")
                .Append("--ci-gate")
                .AppendQuoted(baseline)
                .AppendQuoted(candidate),
        });
    }
}
