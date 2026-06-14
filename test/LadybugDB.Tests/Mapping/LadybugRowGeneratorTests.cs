using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace LadybugDB.Tests.Mapping;

public sealed class LadybugRowGeneratorTests
{
    private const string SimpleRecord = """
        using LadybugDB;
        namespace Demo
        {
            [LadybugRow]
            public record Person(string Name, long Age);
        }
        """;

    [Fact]
    public void Generates_a_mapper_extension_class_for_a_record()
    {
        GeneratorRunResult result = GeneratorHarness.Run(SimpleRecord);

        // No generator diagnostics for a clean record.
        Assert.Empty(result.Diagnostics);

        // One generated hint per annotated type plus the shared mapper container.
        string allGenerated = string.Concat(result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("static partial class LadybugRowMappers", allGenerated);
        // Single generic entry point returning IReadOnlyList<T>, dispatched at compile time.
        Assert.Contains("public static System.Collections.Generic.IReadOnlyList<T> Map<T>", allGenerated);
        // Per-type private materializer over the concrete row type.
        Assert.Contains("global::Demo.Person", allGenerated);
    }

    [Fact]
    public void Generated_mapper_compiles_without_errors()
    {
        (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(SimpleRecord);

        Assert.Empty(run.Diagnostics);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, "Generated source produced compile errors: "
            + string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void Generated_mapper_is_reflection_free()
    {
        GeneratorRunResult result = GeneratorHarness.Run(SimpleRecord);
        string generated = string.Concat(result.GeneratedSources.Select(s => s.SourceText.ToString()));

        // AOT-safety: the emitted source must not reach for reflection-based member access at
        // runtime. (typeof(T) equality used for compile-time dispatch is AOT-safe and allowed.)
        Assert.DoesNotContain("System.Activator", generated);
        Assert.DoesNotContain("GetProperty", generated);
        Assert.DoesNotContain("GetMethod", generated);
        Assert.DoesNotContain("MakeGenericType", generated);
        Assert.DoesNotContain(".GetType()", generated);
    }

    private const string PropertyClass = """
        using LadybugDB;
        namespace Demo
        {
            [LadybugRow]
            public class Account
            {
                public string Owner { get; set; } = "";
                public int Balance { get; set; }
            }
        }
        """;

    [Fact]
    public void Generates_property_initializer_mapper_for_a_class()
    {
        (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(PropertyClass);

        Assert.Empty(run.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        string generated = string.Concat(run.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("Owner =", generated);
        Assert.Contains("Balance =", generated);
    }

    [Fact]
    public void Generates_map_async_returning_iasyncenumerable()
    {
        (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(SimpleRecord);

        Assert.Empty(run.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        string generated = string.Concat(run.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("System.Collections.Generic.IAsyncEnumerable<T> MapAsync<T>", generated);
        Assert.Contains("System.Runtime.CompilerServices.EnumeratorCancellation", generated);
    }

    [Fact]
    public void Maps_columns_case_insensitively_by_name()
    {
        GeneratorRunResult result = GeneratorHarness.Run(SimpleRecord);
        string generated = string.Concat(result.GeneratedSources.Select(s => s.SourceText.ToString()));

        // The column index is built with an ordinal-ignore-case comparer.
        Assert.Contains("System.StringComparer.OrdinalIgnoreCase", generated);
    }

    [Fact]
    public void Reports_diagnostic_when_no_mappable_members()
    {
        const string empty = """
            using LadybugDB;
            namespace Demo
            {
                [LadybugRow]
                public class Empty { }
            }
            """;

        GeneratorRunResult run = GeneratorHarness.Run(empty);

        Assert.Contains(run.Diagnostics, d => d.Id == "LBUG1001");
    }

    [Fact]
    public void Emits_a_single_shared_index_builder_for_multiple_types()
    {
        const string twoTypes = """
            using LadybugDB;
            namespace Demo
            {
                [LadybugRow]
                public record A(string X);
                [LadybugRow]
                public record B(string Y);
            }
            """;

        (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(twoTypes);

        // Two mappers in one partial class must still compile (no duplicate BuildIndex).
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string generated = string.Concat(run.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("global::Demo.A", generated);
        Assert.Contains("global::Demo.B", generated);
    }

    [Fact]
    public void Generator_is_incremental_and_caches_unchanged_models()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(SimpleRecord);
        var stub = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(GeneratorHarness.StubForTests);
        var refs = Basic.Reference.Assemblies.Net80.References.All;
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
            Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Sample", new[] { tree, stub }, refs, options);

        var generator = new LadybugDB.SourceGen.LadybugRowGenerator();
        Microsoft.CodeAnalysis.GeneratorDriver driver =
            Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(
                new[] { generator.AsSourceGenerator() },
                driverOptions: new Microsoft.CodeAnalysis.GeneratorDriverOptions(
                    Microsoft.CodeAnalysis.IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        // Re-run with an unrelated trivial edit: outputs should be served from cache.
        var compilation2 = compilation.AddSyntaxTrees(
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace Unrelated { class Z { } }"));
        driver = driver.RunGenerators(compilation2);

        var steps = driver.GetRunResult().Results
            .SelectMany(r => r.TrackedSteps)
            .Where(kvp => kvp.Key == "LadybugRowModels")
            .SelectMany(kvp => kvp.Value)
            .SelectMany(s => s.Outputs)
            .ToArray();

        Assert.NotEmpty(steps);
        // After an unrelated edit the equatable model is either served straight from cache or
        // recomputed to an equal value (Unchanged) — both prevent downstream regeneration, which
        // is the property that matters. A non-equatable model would report Modified here.
        Assert.All(steps, o => Assert.True(
            o.Reason == Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached
            || o.Reason == Microsoft.CodeAnalysis.IncrementalStepRunReason.Unchanged,
            $"Expected the model step to be cached/unchanged, but was {o.Reason} (model not equatable?)."));
    }
}
