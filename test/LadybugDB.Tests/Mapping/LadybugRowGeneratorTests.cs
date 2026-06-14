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
}
