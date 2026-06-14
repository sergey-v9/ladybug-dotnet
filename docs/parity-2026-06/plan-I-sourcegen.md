# WS-I: POCO Source Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Ship a reflection-free, AOT-safe `[LadybugRow]` Roslyn incremental source generator (`LadybugDB.SourceGen`, netstandard2.0 analyzer) plus the `LadybugRowAttribute` in core, so consumers get generated `QueryResult.Map<T>()` / `MapAsync<T>()` materialization over `QueryResult` rows.

**Architecture:** A netstandard2.0 Roslyn `IIncrementalGenerator` discovers every type annotated `[LadybugDB.LadybugRowAttribute]`, reads its constructor parameters and/or settable properties, and emits a `static partial class LadybugRowMappers` with one `Map<T>()`/`MapAsync<T>()` extension per type plus a per-type row converter that maps columns to members **by name** using the binding's CLR result types. The generator is referenced by the core `LadybugDB` project as an `Analyzer` so the generated extensions compile against the consumer's `[LadybugRow]` types; it ships as the `analyzers/dotnet/cs` asset inside the core NuGet package (WS-K wires the actual `<None>`/`PackagePath` packaging). The attribute itself lives in core (`Mapping/LadybugRowAttribute.cs`) so consumers reference only `LadybugDB`.

**Tech Stack:** C# / Roslyn `Microsoft.CodeAnalysis.CSharp` 4.x `IIncrementalGenerator`, netstandard2.0; xUnit + `Microsoft.CodeAnalysis.CSharp` test harness for generator-output assertions (no native lib); `Xunit.SkippableFact` + `TestEnvironment.NativeAvailable` for the one end-to-end round-trip.

---

## Files

**Create**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Mapping\LadybugRowAttribute.cs` — the `[LadybugRow]` attribute (core, both TFMs).
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj` — netstandard2.0 analyzer project.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugRowGenerator.cs` — the `IIncrementalGenerator`.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\RowModel.cs` — the equatable model record(s) the pipeline carries.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\Emitter.cs` — pure string emission from `RowModel` to C# source.
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\Diagnostics.cs` — `DiagnosticDescriptor`s (LBUG1001…).
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\` — new folder for generator + mapping tests (see Test below).

**Modify**
- `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj` — reference the generator as an `Analyzer` (so consumers get the generator transitively) and pack it as an `analyzers/dotnet/cs` asset.
- `W:\code\ladybug\tools\csharp_api\LadybugDB.slnx` — add the two new projects (`LadybugDB.SourceGen` under `/src/`).
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj` — add `Microsoft.CodeAnalysis.CSharp` (+ `Basic.Reference.Assemblies`) package refs for the in-process generator harness; reference the generator project as an analyzer for the end-to-end test.

**Test**
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\GeneratorDriver.cs` — shared Roslyn `CSharpGeneratorDriver` harness helper (no native).
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\LadybugRowGeneratorTests.cs` — generator-output + compiles + diagnostics tests (no native).
- `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\MapRoundTripTests.cs` — one native-gated `SkippableFact` end-to-end `Map<T>()` round-trip.

> **Public-API contract (pinned in `03-high-level-plan.md` §4.7 — match EXACTLY):**
> ```csharp
> [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
> public sealed class LadybugRowAttribute : Attribute { }
> public IReadOnlyList<T> QueryResult.Map<T>();                       // reflection-free
> public IAsyncEnumerable<T> QueryResult.MapAsync<T>(CancellationToken ct = default);
> ```
> `QueryResult` is sealed and owned by WS-B, so `Map<T>()` / `MapAsync<T>()` are surfaced as **extension methods** on `QueryResult` in a generated `static class LadybugRowMappers` (the signature the user calls — `result.Map<T>()` — is identical to an instance method). The generic `<T>` is dispatched at compile time to the per-type generated mapper; an unmapped `T` produces a diagnostic, never a runtime reflection fallback.

---

## TASK 0 — Project skeleton: analyzer csproj builds empty and is wired into core + solution

- [ ] Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj` with this content (netstandard2.0 analyzer; pinned Roslyn version; not packed on its own — it ships inside core):
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
      <TargetFramework>netstandard2.0</TargetFramework>
      <AssemblyName>LadybugDB.SourceGen</AssemblyName>
      <RootNamespace>LadybugDB.SourceGen</RootNamespace>
      <!-- Roslyn analyzers must target ns2.0 and not produce a NuGet of their own;
           the core package carries this assembly as an analyzers/dotnet/cs asset. -->
      <IsPackable>false</IsPackable>
      <IncludeBuildOutput>false</IncludeBuildOutput>
      <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
      <IsRoslynComponent>true</IsRoslynComponent>
      <!-- ImplicitUsings/Nullable inherit from Directory.Build.props. The generator
           runs in the compiler process, so keep it dependency-free at runtime. -->
    </PropertyGroup>

    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    </ItemGroup>

  </Project>
  ```
- [ ] Add a placeholder generator so the assembly compiles. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugRowGenerator.cs`:
  ```csharp
  using Microsoft.CodeAnalysis;

  namespace LadybugDB.SourceGen;

  /// <summary>
  /// Incremental source generator that emits reflection-free <c>Map&lt;T&gt;()</c> /
  /// <c>MapAsync&lt;T&gt;()</c> materializers for every <c>[LadybugRow]</c> type.
  /// </summary>
  [Generator(LanguageNames.CSharp)]
  public sealed partial class LadybugRowGenerator : IIncrementalGenerator
  {
      public void Initialize(IncrementalGeneratorInitializationContext context)
      {
          // Filled in by later tasks.
      }
  }
  ```
- [ ] Add the project to the solution. Edit `W:\code\ladybug\tools\csharp_api\LadybugDB.slnx` so the `/src/` folder lists both projects:
  ```xml
  <Solution>
    <Folder Name="/src/">
      <Project Path="src/LadybugDB/LadybugDB.csproj" />
      <Project Path="src/LadybugDB.SourceGen/LadybugDB.SourceGen.csproj" />
    </Folder>
    <Folder Name="/test/">
      <Project Path="test/LadybugDB.Tests/LadybugDB.Tests.csproj" />
    </Folder>
  </Solution>
  ```
- [ ] Build the new project alone (expected PASS — empty but valid generator):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj -c Debug
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen LadybugDB.slnx
  git commit -m "WS-I: scaffold LadybugDB.SourceGen analyzer project"
  ```

## TASK 1 — `[LadybugRow]` attribute in core (no native; struct-layout-style managed test)

- [ ] Write the FAILING test first. Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\LadybugRowAttributeTests.cs`:
  ```csharp
  using System;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Mapping;

  public sealed class LadybugRowAttributeTests
  {
      [Fact]
      public void Attribute_targets_class_and_struct_only_and_is_sealed()
      {
          Type t = typeof(LadybugRowAttribute);
          Assert.True(t.IsSealed);
          Assert.True(typeof(Attribute).IsAssignableFrom(t));

          var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(t, typeof(AttributeUsageAttribute))!;
          Assert.Equal(AttributeTargets.Class | AttributeTargets.Struct, usage.ValidOn);
      }
  }
  ```
- [ ] Run it (expected FAIL — `LadybugRowAttribute` does not exist, compile error):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowAttributeTests"
  ```
- [ ] Implement. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Mapping\LadybugRowAttribute.cs`:
  ```csharp
  using System;

  namespace LadybugDB;

  /// <summary>
  /// Marks a record, class, or struct as a target of the LadybugDB POCO source generator. The
  /// generator emits reflection-free <see cref="O:LadybugDB.LadybugRowMappers.Map"/> /
  /// <c>MapAsync</c> extension methods on <see cref="QueryResult"/> that materialize result rows
  /// into instances of the annotated type, mapping result columns to constructor parameters or
  /// settable properties by name (case-insensitive). No runtime reflection is used, so mapping is
  /// AOT-safe.
  /// </summary>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
  public sealed class LadybugRowAttribute : Attribute
  {
  }
  ```
- [ ] Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowAttributeTests"
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB/Mapping/LadybugRowAttribute.cs test/LadybugDB.Tests/Mapping/LadybugRowAttributeTests.cs
  git commit -m "WS-I: add [LadybugRow] attribute to core"
  ```

## TASK 2 — Test harness: in-process Roslyn generator driver (no native)

- [ ] Add the harness package references to the test project. Edit `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj`, adding to the existing `<ItemGroup>` of `PackageReference`s:
  ```xml
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
    <PackageReference Include="Basic.Reference.Assemblies.Net80" Version="1.7.0" />
  ```
- [ ] Reference the generator project as an **analyzer** in the same csproj (so the end-to-end test in Task 8 sees generated `Map<T>()`), adding a new `<ItemGroup>`:
  ```xml
    <ItemGroup>
      <ProjectReference Include="..\..\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj"
                        OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    </ItemGroup>
  ```
- [ ] Create the shared driver helper `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\GeneratorDriver.cs`:
  ```csharp
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
          var stubTree = CSharpSyntaxTree.ParseText(StubSource);

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
          var stubTree = CSharpSyntaxTree.ParseText(StubSource);
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
      private const string StubSource = """
          using System;
          using System.Collections.Generic;
          namespace LadybugDB
          {
              [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
              public sealed class LadybugRowAttribute : Attribute { }
              public sealed class QueryResult
              {
                  public IReadOnlyList<string> ColumnNames => Array.Empty<string>();
                  public IEnumerable<object?[]> Rows() => Array.Empty<object?[]>();
              }
          }
          """;
  }
  ```
- [ ] Build the test project to verify the harness compiles (expected PASS — `RunResult` returns an empty generator, but the harness type-checks):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug
  ```
- [ ] Commit:
  ```
  git add test/LadybugDB.Tests/LadybugDB.Tests.csproj test/LadybugDB.Tests/Mapping/GeneratorDriver.cs
  git commit -m "WS-I: add Roslyn generator test harness"
  ```

## TASK 3 — Generator emits a mapper for a simple `[LadybugRow]` record (positional ctor)

- [ ] Write the FAILING test. Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\LadybugRowGeneratorTests.cs`:
  ```csharp
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
          Assert.Contains("static class LadybugRowMappers", allGenerated);
          Assert.Contains("public static System.Collections.Generic.IReadOnlyList<global::Demo.Person> Map<T>", allGenerated);
          Assert.Contains("global::Demo.Person", allGenerated);
      }
  }
  ```
- [ ] Run it (expected FAIL — generator emits nothing yet):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowGeneratorTests.Generates_a_mapper_extension_class_for_a_record"
  ```
- [ ] Implement the model record. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\RowModel.cs`:
  ```csharp
  using System.Collections.Immutable;

  namespace LadybugDB.SourceGen;

  /// <summary>How a single target member is populated.</summary>
  internal enum MemberKind
  {
      ConstructorParameter,
      SettableProperty,
  }

  /// <summary>One mappable member of a [LadybugRow] type: its source column name and CLR type.</summary>
  internal sealed record RowMember(
      string MemberName,        // e.g. "Name"
      string ColumnName,        // e.g. "Name" (column the result must contain)
      string TypeFullName,      // fully-qualified, e.g. "string" / "long" / "System.Guid"
      bool IsNullable,          // annotated nullable reference or Nullable<T>
      MemberKind Kind,
      int ConstructorOrdinal);  // -1 for properties

  /// <summary>An equatable, fully-resolved description of one [LadybugRow] type for emission.</summary>
  internal sealed record RowModel(
      string TypeFullName,                  // "global::Demo.Person"
      string TypeNamespace,                 // "Demo" or "" for global
      string TypeName,                      // "Person"
      bool IsValueType,
      bool UsePositionalConstructor,
      ImmutableArray<RowMember> Members)
  {
      public bool Equals(RowModel? other)
          => other is not null
             && TypeFullName == other.TypeFullName
             && IsValueType == other.IsValueType
             && UsePositionalConstructor == other.UsePositionalConstructor
             && Members.SequenceEqual(other.Members);

      public override int GetHashCode()
      {
          int hash = TypeFullName.GetHashCode();
          foreach (RowMember m in Members)
          {
              hash = (hash * 397) ^ m.GetHashCode();
          }

          return hash;
      }
  }
  ```
- [ ] Implement the emitter. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\Emitter.cs`:
  ```csharp
  using System.Collections.Immutable;
  using System.Linq;
  using System.Text;

  namespace LadybugDB.SourceGen;

  /// <summary>Pure string emission: turns the resolved <see cref="RowModel"/> set into C# source.</summary>
  internal static class Emitter
  {
      public const string MapperClassName = "LadybugRowMappers";

      public static string Emit(ImmutableArray<RowModel> models)
      {
          var sb = new StringBuilder();
          sb.AppendLine("// <auto-generated/>");
          sb.AppendLine("#nullable enable");
          sb.AppendLine("namespace LadybugDB");
          sb.AppendLine("{");
          sb.AppendLine($"    public static partial class {MapperClassName}");
          sb.AppendLine("    {");

          foreach (RowModel model in models)
          {
              EmitMapForType(sb, model);
          }

          sb.AppendLine("    }");
          sb.AppendLine("}");
          return sb.ToString();
      }

      private static void EmitMapForType(StringBuilder sb, RowModel model)
      {
          // Map<T>() — constrained at compile time to this exact T via an overload keyed on T.
          sb.AppendLine($"        public static System.Collections.Generic.IReadOnlyList<{model.TypeFullName}> Map<T>(this global::LadybugDB.QueryResult result)");
          sb.AppendLine($"            where T : {model.TypeFullName}");
          sb.AppendLine("        {");
          sb.AppendLine("            if (result is null) throw new System.ArgumentNullException(nameof(result));");
          sb.AppendLine("            var __cols = result.ColumnNames;");
          sb.AppendLine("            var __idx = BuildIndex(__cols);");
          sb.AppendLine($"            var __list = new System.Collections.Generic.List<{model.TypeFullName}>();");
          sb.AppendLine("            foreach (var __row in result.Rows())");
          sb.AppendLine("            {");
          sb.AppendLine($"                __list.Add({BuildConstruction(model)});");
          sb.AppendLine("            }");
          sb.AppendLine("            return __list;");
          sb.AppendLine("        }");
          sb.AppendLine();

          // Per-type column-name -> ordinal index, case-insensitive, computed once per call.
          sb.AppendLine("        private static System.Collections.Generic.Dictionary<string, int> BuildIndex(System.Collections.Generic.IReadOnlyList<string> cols)");
          sb.AppendLine("        {");
          sb.AppendLine("            var d = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);");
          sb.AppendLine("            for (int i = 0; i < cols.Count; i++) d[cols[i]] = i;");
          sb.AppendLine("            return d;");
          sb.AppendLine("        }");
          sb.AppendLine();
      }

      private static string BuildConstruction(RowModel model)
      {
          // Positional construction for records: ctor params in order, each converted from its column.
          var ctorArgs = model.Members
              .Where(m => m.Kind == MemberKind.ConstructorParameter)
              .OrderBy(m => m.ConstructorOrdinal)
              .Select(m => Convert(m));

          return $"new {model.TypeFullName}({string.Join(", ", ctorArgs)})";
      }

      private static string Convert(RowMember m)
      {
          // Reflection-free conversion: index the row by the column ordinal, cast through the
          // binding's CLR result type. NULL flows through as default for the target type.
          string cell = $"__row[__idx[\"{m.ColumnName}\"]]";
          return $"global::LadybugDB.LadybugRowConvert.To<{m.TypeFullName}>({cell})";
      }
  }
  ```
- [ ] Wire the emitter into the generator. Replace the body of `Initialize` in `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugRowGenerator.cs`:
  ```csharp
  using System.Collections.Immutable;
  using System.Linq;
  using Microsoft.CodeAnalysis;
  using Microsoft.CodeAnalysis.CSharp.Syntax;

  namespace LadybugDB.SourceGen;

  [Generator(LanguageNames.CSharp)]
  public sealed partial class LadybugRowGenerator : IIncrementalGenerator
  {
      private const string AttributeFullName = "LadybugDB.LadybugRowAttribute";

      public void Initialize(IncrementalGeneratorInitializationContext context)
      {
          IncrementalValuesProvider<RowModel?> models = context.SyntaxProvider.ForAttributeWithMetadataName(
              AttributeFullName,
              predicate: static (node, _) => node is TypeDeclarationSyntax,
              transform: static (ctx, _) => ModelBuilder.Build(ctx));

          IncrementalValueProvider<ImmutableArray<RowModel>> collected =
              models.Where(static m => m is not null).Select(static (m, _) => m!).Collect();

          context.RegisterSourceOutput(collected, static (spc, all) =>
          {
              if (all.IsDefaultOrEmpty)
              {
                  return;
              }

              string source = Emitter.Emit(all);
              spc.AddSource("LadybugRowMappers.g.cs", source);
          });
      }
  }
  ```
- [ ] Add the model builder. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\ModelBuilder.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Linq;
  using Microsoft.CodeAnalysis;

  namespace LadybugDB.SourceGen;

  /// <summary>Resolves a [LadybugRow] type symbol into the equatable <see cref="RowModel"/>.</summary>
  internal static class ModelBuilder
  {
      public static RowModel? Build(GeneratorAttributeSyntaxContext ctx)
      {
          if (ctx.TargetSymbol is not INamedTypeSymbol type)
          {
              return null;
          }

          string ns = type.ContainingNamespace.IsGlobalNamespace
              ? string.Empty
              : type.ContainingNamespace.ToDisplayString();
          string full = "global::" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
              .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));

          // Prefer a non-trivial instance constructor (records' positional ctor / a declared ctor);
          // its parameters are mapped by name. Otherwise map settable instance properties.
          IMethodSymbol? ctor = type.InstanceConstructors
              .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
              .OrderByDescending(c => c.Parameters.Length)
              .FirstOrDefault();

          var members = ImmutableArray.CreateBuilder<RowMember>();
          bool positional = ctor is not null;

          if (positional)
          {
              for (int i = 0; i < ctor!.Parameters.Length; i++)
              {
                  IParameterSymbol p = ctor.Parameters[i];
                  members.Add(new RowMember(
                      MemberName: p.Name,
                      ColumnName: p.Name,
                      TypeFullName: p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                      IsNullable: p.NullableAnnotation == NullableAnnotation.Annotated,
                      Kind: MemberKind.ConstructorParameter,
                      ConstructorOrdinal: i));
              }
          }
          else
          {
              foreach (IPropertySymbol prop in type.GetMembers().OfType<IPropertySymbol>()
                           .Where(p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public } && !p.IsStatic))
              {
                  members.Add(new RowMember(
                      MemberName: prop.Name,
                      ColumnName: prop.Name,
                      TypeFullName: prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                      IsNullable: prop.NullableAnnotation == NullableAnnotation.Annotated,
                      Kind: MemberKind.SettableProperty,
                      ConstructorOrdinal: -1));
              }
          }

          return new RowModel(
              TypeFullName: full,
              TypeNamespace: ns,
              TypeName: type.Name,
              IsValueType: type.IsValueType,
              UsePositionalConstructor: positional,
              Members: members.ToImmutable());
      }
  }
  ```
- [ ] The emitter references `LadybugRowConvert.To<T>` and `LadybugRowMappers` is `partial`; both ship in core. Create the conversion helper now so the emitted code is self-consistent — `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Mapping\LadybugRowConvert.cs`:
  ```csharp
  using System;

  namespace LadybugDB;

  /// <summary>
  /// Conversion shim used by generated row mappers. Bridges the binding's CLR result types (what
  /// <see cref="Value.GetValue"/> / <see cref="QueryResult.Rows"/> produce) to a target member type
  /// without reflection. The generic dispatch is resolved at compile time by the generated code, so
  /// this stays AOT-safe.
  /// </summary>
  public static class LadybugRowConvert
  {
      /// <summary>Converts a raw cell value to <typeparamref name="T"/>.</summary>
      public static T To<T>(object? cell)
      {
          if (cell is null)
          {
              return default!;
          }

          if (cell is T already)
          {
              return already;
          }

          // Common widenings the engine does not pre-coerce (e.g. INT64 cell into an int member).
          object converted = System.Convert.ChangeType(cell, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
          return (T)converted;
      }
  }
  ```
- [ ] Make the core mapper container `partial` so generated and hand-written halves merge. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB\Mapping\LadybugRowMappers.cs`:
  ```csharp
  namespace LadybugDB;

  /// <summary>
  /// Container for the source-generated <c>Map&lt;T&gt;()</c> / <c>MapAsync&lt;T&gt;()</c> extension
  /// methods. The generator emits the other half of this partial class for each <c>[LadybugRow]</c>
  /// type in the consuming compilation. Defined here so the type exists even when no row types are
  /// declared (and so XML docs/IntelliSense resolve).
  /// </summary>
  public static partial class LadybugRowMappers
  {
  }
  ```
- [ ] Build the generator + core (expected PASS):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj -c Debug
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug
  ```
- [ ] Run the generator test (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowGeneratorTests.Generates_a_mapper_extension_class_for_a_record"
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen src/LadybugDB/Mapping
  git commit -m "WS-I: emit Map<T>() mapper for positional [LadybugRow] records"
  ```

## TASK 4 — Generated mapper compiles cleanly (no compiler errors in the generated source)

- [ ] Add the FAILING test to `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\LadybugRowGeneratorTests.cs`:
  ```csharp
      [Fact]
      public void Generated_mapper_compiles_without_errors()
      {
          (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(SimpleRecord);

          Assert.Empty(run.Diagnostics);
          var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
          Assert.True(errors.Length == 0, "Generated source produced compile errors: "
              + string.Join("\n", errors.Select(e => e.ToString())));
      }
  ```
  (Add `using System.Linq;` and `using Microsoft.CodeAnalysis;` if not already imported.)
- [ ] The stub in `GeneratorHarness` does not yet declare `LadybugRowConvert` / the `partial LadybugRowMappers`, so the compile will FAIL. Run it (expected FAIL):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generated_mapper_compiles_without_errors"
  ```
- [ ] Extend the harness stub so generated output binds. In `GeneratorDriver.cs`, replace the `StubSource` constant body's namespace block to add the convert shim and partial mapper container:
  ```csharp
      private const string StubSource = """
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
  ```
- [ ] Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generated_mapper_compiles_without_errors"
  ```
- [ ] Commit:
  ```
  git add test/LadybugDB.Tests/Mapping
  git commit -m "WS-I: assert generated mapper compiles against the core surface"
  ```

## TASK 5 — Property-init POCO (non-positional) maps via settable properties

- [ ] Add the FAILING test to `LadybugRowGeneratorTests.cs`:
  ```csharp
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
  ```
- [ ] Run it (expected FAIL — emitter only handles positional construction; property path produces `new Account()` with no initializers):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generates_property_initializer_mapper_for_a_class"
  ```
- [ ] Implement object-initializer emission. Replace `BuildConstruction` in `Emitter.cs`:
  ```csharp
      private static string BuildConstruction(RowModel model)
      {
          if (model.UsePositionalConstructor)
          {
              var ctorArgs = model.Members
                  .Where(m => m.Kind == MemberKind.ConstructorParameter)
                  .OrderBy(m => m.ConstructorOrdinal)
                  .Select(Convert);
              return $"new {model.TypeFullName}({string.Join(", ", ctorArgs)})";
          }

          var inits = model.Members
              .Where(m => m.Kind == MemberKind.SettableProperty)
              .Select(m => $"{m.MemberName} = {Convert(m)}");
          return $"new {model.TypeFullName}() {{ {string.Join(", ", inits)} }}";
      }
  ```
- [ ] Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generates_property_initializer_mapper_for_a_class"
  ```
- [ ] Re-run the whole generator-test class to confirm no regression (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowGeneratorTests"
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen/Emitter.cs test/LadybugDB.Tests/Mapping/LadybugRowGeneratorTests.cs
  git commit -m "WS-I: map property-init POCOs via object initializers"
  ```

## TASK 6 — `MapAsync<T>()` emission (IAsyncEnumerable, both TFMs)

- [ ] Add the FAILING test to `LadybugRowGeneratorTests.cs`:
  ```csharp
      [Fact]
      public void Generates_map_async_returning_iasyncenumerable()
      {
          (GeneratorRunResult run, var diagnostics) = GeneratorHarness.RunAndCompile(SimpleRecord);

          Assert.Empty(run.Diagnostics);
          Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

          string generated = string.Concat(run.GeneratedSources.Select(s => s.SourceText.ToString()));
          Assert.Contains("System.Collections.Generic.IAsyncEnumerable<global::Demo.Person> MapAsync<T>", generated);
          Assert.Contains("System.Runtime.CompilerServices.EnumeratorCancellation", generated);
      }
  ```
- [ ] Run it (expected FAIL — no `MapAsync` emitted yet):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generates_map_async_returning_iasyncenumerable"
  ```
- [ ] Add `MapAsync<T>` emission. In `Emitter.cs`, inside `EmitMapForType` (just after the `Map<T>` block, before `BuildIndex`), append:
  ```csharp
          sb.AppendLine($"        public static async System.Collections.Generic.IAsyncEnumerable<{model.TypeFullName}> MapAsync<T>(this global::LadybugDB.QueryResult result, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)");
          sb.AppendLine($"            where T : {model.TypeFullName}");
          sb.AppendLine("        {");
          sb.AppendLine("            if (result is null) throw new System.ArgumentNullException(nameof(result));");
          sb.AppendLine("            var __cols = result.ColumnNames;");
          sb.AppendLine("            var __idx = BuildIndex(__cols);");
          sb.AppendLine("            foreach (var __row in result.Rows())");
          sb.AppendLine("            {");
          sb.AppendLine("                ct.ThrowIfCancellationRequested();");
          sb.AppendLine($"                yield return {BuildConstruction(model)};");
          sb.AppendLine("            }");
          sb.AppendLine("            await System.Threading.Tasks.Task.CompletedTask;");
          sb.AppendLine("        }");
          sb.AppendLine();
  ```
- [ ] `IAsyncEnumerable`/`EnumeratorCancellation` need `Microsoft.Bcl.AsyncInterfaces` on ns2.0. The generated code is compiled in the **consumer's** project, so core must transitively supply it on ns2.0. Add to `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj` (after the `InternalsVisibleTo` ItemGroup):
  ```xml
    <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
      <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
    </ItemGroup>
  ```
- [ ] The test harness compiles generated `MapAsync` against `Basic.Reference.Assemblies.Net80`, which already has `IAsyncEnumerable`. Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generates_map_async_returning_iasyncenumerable"
  ```
- [ ] Build core on both TFMs to confirm the ns2.0 async dep resolves (expected PASS):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen/Emitter.cs src/LadybugDB/LadybugDB.csproj test/LadybugDB.Tests/Mapping/LadybugRowGeneratorTests.cs
  git commit -m "WS-I: emit MapAsync<T>() IAsyncEnumerable mapper"
  ```

## TASK 7 — Diagnostic for an unsupported `[LadybugRow]` shape (no mappable members)

- [ ] Add the FAILING test to `LadybugRowGeneratorTests.cs`:
  ```csharp
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
  ```
- [ ] Run it (expected FAIL — no diagnostic raised; emitter would produce a `new Empty() {  }` with no members but also no warning):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Reports_diagnostic_when_no_mappable_members"
  ```
- [ ] Add the descriptor. Create `W:\code\ladybug\tools\csharp_api\src\LadybugDB.SourceGen\Diagnostics.cs`:
  ```csharp
  using Microsoft.CodeAnalysis;

  namespace LadybugDB.SourceGen;

  internal static class Diagnostics
  {
      public static readonly DiagnosticDescriptor NoMappableMembers = new(
          id: "LBUG1001",
          title: "[LadybugRow] type has no mappable members",
          messageFormat: "Type '{0}' is annotated with [LadybugRow] but exposes no public constructor parameters or public settable properties to map result columns to",
          category: "LadybugDB.Mapping",
          defaultSeverity: DiagnosticSeverity.Warning,
          isEnabledByDefault: true);
  }
  ```
- [ ] Carry a location + emptiness signal on the model. Add a field to `RowModel` in `RowModel.cs` — append `, Location? DeclarationLocation = null` to the record's parameter list and `using Microsoft.CodeAnalysis;` at the top; exclude it from equality by leaving `Equals`/`GetHashCode` as-is (they already ignore it). Then in `ModelBuilder.Build`, capture the location and pass it:
  ```csharp
          Location? location = type.Locations.FirstOrDefault();
          // ... in the returned RowModel, add:  DeclarationLocation: location
  ```
- [ ] Raise the diagnostic in the generator. In `LadybugRowGenerator.Initialize`, change the `RegisterSourceOutput` callback body to report empties and skip them:
  ```csharp
          context.RegisterSourceOutput(collected, static (spc, all) =>
          {
              if (all.IsDefaultOrEmpty)
              {
                  return;
              }

              var mappable = ImmutableArray.CreateBuilder<RowModel>();
              foreach (RowModel model in all)
              {
                  if (model.Members.IsDefaultOrEmpty)
                  {
                      spc.ReportDiagnostic(Diagnostic.Create(
                          Diagnostics.NoMappableMembers, model.DeclarationLocation, model.TypeName));
                      continue;
                  }

                  mappable.Add(model);
              }

              if (mappable.Count == 0)
              {
                  return;
              }

              string source = Emitter.Emit(mappable.ToImmutable());
              spc.AddSource("LadybugRowMappers.g.cs", source);
          });
  ```
- [ ] Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Reports_diagnostic_when_no_mappable_members"
  ```
- [ ] Re-run the full generator-test class (expected PASS — no regressions):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~LadybugRowGeneratorTests"
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen test/LadybugDB.Tests/Mapping/LadybugRowGeneratorTests.cs
  git commit -m "WS-I: diagnostic LBUG1001 for unmappable [LadybugRow] shapes"
  ```

## TASK 8 — End-to-end native-gated `Map<T>()` round-trip

- [ ] Define a real `[LadybugRow]` type in the test assembly and a SkippableFact. Create `W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\Mapping\MapRoundTripTests.cs`:
  ```csharp
  using System.Collections.Generic;
  using System.Linq;
  using LadybugDB;
  using Xunit;

  namespace LadybugDB.Tests.Mapping;

  [LadybugRow]
  public sealed record PersonRow(string Name, long Age);

  public sealed class MapRoundTripTests
  {
      [SkippableFact]
      public void Map_materializes_rows_into_poco()
      {
          Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

          string dbPath = TestEnvironment.NewTempDbPath();
          try
          {
              using var db = new Database(dbPath);
              using var conn = new Connection(db);

              conn.Query("CREATE NODE TABLE Person(Name STRING, Age INT64, PRIMARY KEY(Name))").Dispose();
              conn.Query("CREATE (:Person {Name: 'Alice', Age: 30})").Dispose();
              conn.Query("CREATE (:Person {Name: 'Bob', Age: 42})").Dispose();

              using QueryResult result = conn.Query("MATCH (p:Person) RETURN p.Name AS Name, p.Age AS Age ORDER BY p.Age");

              IReadOnlyList<PersonRow> people = result.Map<PersonRow>();

              Assert.Equal(2, people.Count);
              Assert.Equal(new PersonRow("Alice", 30), people[0]);
              Assert.Equal(new PersonRow("Bob", 42), people[1]);
          }
          finally
          {
              TestEnvironment.TryDelete(dbPath);
          }
      }
  }
  ```
- [ ] This requires the generator to run against the **test** project (Task 2 already added the `OutputItemType="Analyzer"` ProjectReference). Build the test project so the generator emits `LadybugRowMappers.Map<PersonRow>` (expected PASS — compiles; `result.Map<PersonRow>()` now resolves):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug
  ```
- [ ] Run it. Without a staged native lib it SKIPS (expected: skipped, not failed):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~MapRoundTripTests"
  ```
- [ ] If a native lib is staged (per `MAINTAINING.md` / `scripts/build-native-and-test.ps1`), confirm it PASSES with the gate forced:
  ```
  pwsh -Command "$env:LADYBUG_REQUIRE_NATIVE='1'; dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter 'FullyQualifiedName~MapRoundTripTests'"
  ```
- [ ] Commit:
  ```
  git add test/LadybugDB.Tests/Mapping/MapRoundTripTests.cs
  git commit -m "WS-I: native-gated end-to-end Map<T>() round-trip"
  ```

## TASK 9 — Generator robustness: incremental caching + namespaced/global-namespace types

- [ ] Add a FAILING test asserting the model pipeline is incremental (cached step output is reused). Append to `LadybugRowGeneratorTests.cs`:
  ```csharp
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
              .SelectMany(s => s.Outputs);

          Assert.All(steps, o => Assert.Equal(
              Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached, o.Reason));
      }
  ```
- [ ] Expose `StubForTests` for this test. In `GeneratorDriver.cs`, change `private const string StubSource` to `internal const string StubForTests` (and update the two internal references from `StubSource` to `StubForTests`).
- [ ] Run it (expected FAIL — the model step is unnamed, so `TrackedSteps["LadybugRowModels"]` is empty and `Assert.All` over an empty set passes vacuously OR the step isn't tracked; make it deterministic by naming the step next):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generator_is_incremental_and_caches_unchanged_models"
  ```
- [ ] Name the tracked step and ensure the model is equatable end-to-end. In `LadybugRowGenerator.Initialize`, name the provider:
  ```csharp
          IncrementalValuesProvider<RowModel?> models = context.SyntaxProvider.ForAttributeWithMetadataName(
              AttributeFullName,
              predicate: static (node, _) => node is TypeDeclarationSyntax,
              transform: static (ctx, _) => ModelBuilder.Build(ctx))
              .WithTrackingName("LadybugRowModels");
  ```
- [ ] Confirm `RowModel`/`RowMember` equality excludes `Location` (Roslyn `Location` is not value-equatable and would defeat caching). Verify `RowModel.Equals`/`GetHashCode` (Task 3) do not reference `DeclarationLocation` — they don't. Add an explicit XML note above `DeclarationLocation` in `RowModel.cs`:
  ```csharp
      // NOTE: excluded from Equals/GetHashCode — Location is not value-equatable and would defeat
      // incremental caching. Only used for diagnostic reporting.
  ```
- [ ] Run it (expected PASS):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj --filter "FullyQualifiedName~Generator_is_incremental_and_caches_unchanged_models"
  ```
- [ ] Commit:
  ```
  git add src/LadybugDB.SourceGen test/LadybugDB.Tests/Mapping
  git commit -m "WS-I: track incremental model step and assert caching"
  ```

## TASK 10 — Package the analyzer into the core NuGet (analyzers/dotnet/cs) and reference it from core

- [ ] Reference the generator from core as an analyzer so apps that consume `LadybugDB` get the generator transitively, AND mark it for packaging into the core nupkg. Edit `W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj`, adding a new `<ItemGroup>`:
  ```xml
    <!-- Ship the POCO source generator inside the core package as an analyzer asset (WS-I).
         ReferenceOutputAssembly=false: the generator is a build-time component, not a runtime ref. -->
    <ItemGroup>
      <ProjectReference Include="..\LadybugDB.SourceGen\LadybugDB.SourceGen.csproj"
                        OutputItemType="Analyzer"
                        ReferenceOutputAssembly="false"
                        PrivateAssets="all" />
    </ItemGroup>

    <!-- Pack the generator assembly under analyzers/dotnet/cs so NuGet consumers run it. -->
    <Target Name="PackLadybugAnalyzer" BeforeTargets="GenerateNuspec" DependsOnTargets="ResolveProjectReferences">
      <ItemGroup>
        <None Include="$(OutputPath)..\netstandard2.0\LadybugDB.SourceGen.dll"
              Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false"
              Condition="Exists('$(OutputPath)..\netstandard2.0\LadybugDB.SourceGen.dll')" />
      </ItemGroup>
    </Target>
  ```
  > NOTE: WS-K owns the final packaging wiring (`cake/**`, `nuget/**`). This target is the WS-I-local default so a plain `dotnet pack` includes the analyzer; if WS-K centralizes analyzer packing, this target defers to theirs. Flag in the merge step (see Open Questions).
- [ ] Build core (expected PASS — generator now flows into the core build as an analyzer; core source itself has no `[LadybugRow]` types so no extra source is emitted into core):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug
  ```
- [ ] Verify the analyzer lands in the pack. Run and inspect (expected PASS — the nupkg contains `analyzers/dotnet/cs/LadybugDB.SourceGen.dll`):
  ```
  dotnet pack W:\code\ladybug\tools\csharp_api\src\LadybugDB\LadybugDB.csproj -c Debug -o W:\code\ladybug\tools\csharp_api\artifacts\ws-i-check
  ```
  ```
  dotnet tool run dotnet-validate package local W:\code\ladybug\tools\csharp_api\artifacts\ws-i-check\LadybugDB.*.nupkg 2>$null; (Get-ChildItem W:\code\ladybug\tools\csharp_api\artifacts\ws-i-check\LadybugDB.*.nupkg | Select-Object -First 1) | ForEach-Object { (Add-Type -AssemblyName System.IO.Compression.FileSystem); [System.IO.Compression.ZipFile]::OpenRead($_.FullName).Entries.FullName } | Select-String "analyzers"
  ```
  (If `dotnet-validate` is not installed, the ZIP-listing one-liner alone is sufficient evidence; assert it prints `analyzers/dotnet/cs/LadybugDB.SourceGen.dll`.)
- [ ] Commit:
  ```
  git add src/LadybugDB/LadybugDB.csproj
  git commit -m "WS-I: pack POCO source generator as analyzer in core package"
  ```

## TASK 11 — Full-suite + both-TFM gate for the workstream

- [ ] Build the whole solution on both TFMs (expected PASS, warning-clean):
  ```
  dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
  ```
- [ ] Run the entire managed test suite; generator + attribute + mapping tests run unconditionally, the round-trip skips without native (expected PASS, with `MapRoundTripTests` skipped):
  ```
  dotnet test W:\code\ladybug\tools\csharp_api\test\LadybugDB.Tests\LadybugDB.Tests.csproj -c Debug
  ```
- [ ] Confirm the `StructLayoutTests` and other native-independent suites are still green (no collateral damage) — they are part of the run above; verify the summary shows 0 failed.
- [ ] Commit any final touch-ups (e.g. XML-doc fixes flagged by `GenerateDocumentationFile`):
  ```
  git add -A
  git commit -m "WS-I: green build + tests across net10.0 and netstandard2.0"
  ```

---

## Self-review / done criteria (tied to WS-I "Done = `Map<T>()` generates; tests")

- [ ] `Mapping/LadybugRowAttribute.cs` exists in core with `[AttributeUsage(Class | Struct)]`, sealed, matching §4.7 exactly.
- [ ] `src/LadybugDB.SourceGen/**` is a netstandard2.0 `IIncrementalGenerator` (`IsRoslynComponent`, `Microsoft.CodeAnalysis.CSharp` `PrivateAssets="all"`), added to `LadybugDB.slnx`.
- [ ] Generator emits `static partial class LadybugRowMappers` with `Map<T>()` returning `IReadOnlyList<T>` and `MapAsync<T>(CancellationToken)` returning `IAsyncEnumerable<T>` — signatures matching §4.7.
- [ ] Columns map to constructor params (records / declared ctors) and to public settable properties (POCOs) **by name, case-insensitive**; conversion goes through `LadybugRowConvert.To<T>` over the binding's CLR result types (`QueryResult.Rows()` / `Value.GetValue`).
- [ ] Generated code is **reflection-free** (no `System.Type`/`Activator`/`GetProperty` in emitted source) → AOT-safe.
- [ ] `LBUG1001` diagnostic fires for a `[LadybugRow]` type with no mappable members.
- [ ] Generator is incremental: the model step is `WithTrackingName("LadybugRowModels")` and equatable (`Location` excluded from equality); caching asserted by test.
- [ ] Roslyn-harness tests (output-shape, compiles-clean, property-init path, async shape, diagnostic, caching) run **without** native and pass.
- [ ] `MapRoundTripTests` is a `SkippableFact` gated on `TestEnvironment.NativeAvailable`; skips without native, passes with `LADYBUG_REQUIRE_NATIVE=1` + staged lib.
- [ ] The generator ships as `analyzers/dotnet/cs/LadybugDB.SourceGen.dll` inside the core `LadybugDB` package; core references it `OutputItemType="Analyzer"` `PrivateAssets="all"`.
- [ ] `dotnet build LadybugDB.slnx -c Debug` green on `net10.0` + `netstandard2.0`; full `dotnet test` green (round-trip skipped without native).
