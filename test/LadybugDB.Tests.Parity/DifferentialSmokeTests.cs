using System.IO;
using LadybugDB.Benchmarks;
using Xunit;

namespace LadybugDB.Tests.Parity;

/// <summary>Native-gated wrapper that runs the differential smoke runner as part of the suite.</summary>
public sealed class DifferentialSmokeTests
{
    [SkippableFact]
    public void Differential_runner_reports_no_mismatches()
    {
        Skip.IfNot(ParityEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        using var log = new StringWriter();
        bool ok = DifferentialRunner.Run(log);
        Assert.True(ok, log.ToString());
    }
}
