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
    // what the generator emits against and keeps these tests native-free.
    internal const string StubForTests = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        namespace LadybugDB
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
            public sealed class LadybugRowAttribute : Attribute { }
            public enum DataTypeId { String, Bool, Int8, Int16, Int32, Int64, Serial, UInt8, UInt16, UInt32, UInt64, Float, Double }
            public sealed class LogicalType { public DataTypeId Id => DataTypeId.String; }
            public readonly record struct ColumnSchema(string Name, LogicalType Type);
            public sealed class FlatTuple : IDisposable
            {
                public void Dispose() { }
                public string? GetString(int i) => "";
                public bool GetBoolOrDefault(int i) => default;
                public sbyte GetInt8OrDefault(int i) => default;
                public short GetInt16OrDefault(int i) => default;
                public int GetInt32OrDefault(int i) => default;
                public long GetInt64OrDefault(int i) => default;
                public byte GetUInt8OrDefault(int i) => default;
                public ushort GetUInt16OrDefault(int i) => default;
                public uint GetUInt32OrDefault(int i) => default;
                public ulong GetUInt64OrDefault(int i) => default;
                public float GetFloatOrDefault(int i) => default;
                public double GetDoubleOrDefault(int i) => default;
            }
            public sealed class QueryResult
            {
                public IReadOnlyList<string> ColumnNames => Array.Empty<string>();
                public IReadOnlyList<ColumnSchema> Columns => Array.Empty<ColumnSchema>();
                public bool HasNext() => false;
                public FlatTuple GetNext() => new();
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
