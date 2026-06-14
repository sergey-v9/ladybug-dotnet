using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace LadybugDB.Tests.Mapping;

/// <summary>
/// SG-3 regression: the generator must emit the <c>LadybugRowMappers</c> container into the consumer
/// assembly even when no <c>[LadybugRow]</c> types are declared, and core must NOT ship a real
/// <c>LadybugRowMappers</c> type. If both the referenced core assembly and the generator declared the
/// type, a consumer that names it directly would get CS0436 (an error under TreatWarningsAsErrors).
///
/// This compiles against the REAL core <c>LadybugDB.dll</c> as a metadata reference (a separate
/// assembly) plus the generator, then names the type directly with CS0436 promoted to an error.
/// </summary>
public sealed class LadybugRowMappersContainerTests
{
    // References LadybugRowMappers by name with no [LadybugRow] type in the compilation. The type must
    // still resolve (generator emits the container unconditionally) and must be unambiguous (no CS0436
    // from a duplicate in the referenced core assembly).
    private const string NamesTypeDirectly = """
        namespace Consumer
        {
            public static class Touch
            {
                public static System.Type Probe() => typeof(global::LadybugDB.LadybugRowMappers);
            }
        }
        """;

    [Fact]
    public void Generator_emits_container_unconditionally_without_cs0436()
    {
        var userTree = CSharpSyntaxTree.ParseText(NamesTypeDirectly);

        // Real core assembly as a metadata reference + the framework reference set. This is the
        // two-assembly setup: if core still declared LadybugRowMappers, the generator's emitted copy
        // in THIS compilation would collide with the imported one (CS0436).
        //
        // Use the running net10.0 runtime's reference assemblies (not Basic.Reference.Net80) so they
        // match the version core was built against — otherwise CS1705 (System.Runtime 10 vs 8) masks
        // the diagnostic under test.
        var refs = TrustedPlatformAssemblies()
            .Append(MetadataReference.CreateFromFile(typeof(LadybugDB.LadybugRowConvert).Assembly.Location))
            .ToList();

        // Promote CS0436 (and everything else) to errors, mirroring a consumer under
        // <TreatWarningsAsErrors>true</TreatWarningsAsErrors>.
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: ReportDiagnostic.Error);

        var compilation = CSharpCompilation.Create(
            "Consumer.Sample",
            new[] { userTree },
            refs,
            options);

        var generator = new LadybugDB.SourceGen.LadybugRowGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        // The container must be emitted into the consumer even with zero [LadybugRow] types — that is
        // the unconditional-emission half of the fix and what makes 'typeof(LadybugRowMappers)' resolve.
        string generated = string.Concat(driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
        Assert.Contains("static partial class LadybugRowMappers", generated);

        Diagnostic[] errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        // CS0436 (source type conflicts with imported metadata type) is the exact SG-3 symptom; CS0121
        // / CS0433 are the same duplicate-definition root cause seen as ambiguity / type clash.
        Assert.DoesNotContain(errors, d => d.Id is "CS0436" or "CS0433" or "CS0121");
        Assert.True(errors.Length == 0,
            "Consumer compilation produced errors: " + string.Join("\n", errors.Select(e => e.ToString())));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformAssemblies()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            if (path.Length == 0)
            {
                continue;
            }

            // Skip any LadybugDB* assembly from the host TPA set: core (added explicitly below as the
            // single reference under test) and the running test assembly itself — the latter carries a
            // generated LadybugRowMappers (the test project has [LadybugRow] types), which would
            // otherwise import a second copy and turn the diagnostic under test into CS0433.
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("LadybugDB", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return MetadataReference.CreateFromFile(path);
        }
    }
}
