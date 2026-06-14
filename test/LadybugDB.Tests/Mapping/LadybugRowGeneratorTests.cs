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
        Assert.Contains("public static System.Collections.Generic.IReadOnlyList<global::Demo.Person> Map<T>", allGenerated);
        Assert.Contains("global::Demo.Person", allGenerated);
    }
}
