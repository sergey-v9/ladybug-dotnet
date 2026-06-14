using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LadybugDB.Tests.Mapping;

/// <summary>
/// Runs <see cref="LadybugDB.SourceGen.LadybugRowGenerator"/> against an in-memory compilation so
/// the test suite can assert on the generated source without the native library. Pure Roslyn.
/// </summary>
internal static class GeneratorHarness
{
    public static GeneratorRunResult Run(string userSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(userSource);

        // A stub of the public surface the generated code binds against, compiled alongside the
        // user source so the generator output type-checks without referencing the real assembly.
        var stubTree = CSharpSyntaxTree.ParseText(StubForTests);

        IEnumerable<MetadataReference> refs = Basic.Reference.Assemblies.Net80.References.All;

        var compilation = CSharpCompilation.Create(
            assemblyName: "LadybugDB.SourceGen.Tests.Sample",
            syntaxTrees: new[] { syntaxTree, stubTree },
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new LadybugDB.SourceGen.LadybugRowGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        return driver.GetRunResult().Results.Single();
    }

    public static (GeneratorRunResult Run, ImmutableArray<Diagnostic> CompileDiagnostics) RunAndCompile(string userSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(userSource);
        var stubTree = CSharpSyntaxTree.ParseText(StubForTests);
        IEnumerable<MetadataReference> refs = Basic.Reference.Assemblies.Net80.References.All;

        var compilation = CSharpCompilation.Create(
            "LadybugDB.SourceGen.Tests.Sample",
            new[] { syntaxTree, stubTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new LadybugDB.SourceGen.LadybugRowGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        ImmutableArray<Diagnostic> diagnostics = output.GetDiagnostics();
        return (driver.GetRunResult().Results.Single(), diagnostics);
    }

    // Minimal stand-ins for the core public surface that generated code references. Mirrors only
    // what the generator emits against (QueryResult.ColumnNames / Rows() and the attribute).
    internal const string StubForTests = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        namespace LadybugDB
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class LadybugRowAttribute : Attribute { }
            public sealed class QueryResult
            {
                public IReadOnlyList<string> ColumnNames => Array.Empty<string>();
                public IEnumerable<object?[]> Rows() => Array.Empty<object?[]>();
            }
            public static class LadybugRowConvert
            {
                public static T To<T>(object? cell)
                {
                    if (cell is null) return default!;
                    if (cell is T already) return already;
                    return (T)Convert.ChangeType(cell, typeof(T), CultureInfo.InvariantCulture);
                }
            }
            public static partial class LadybugRowMappers { }
        }
        """;
}
