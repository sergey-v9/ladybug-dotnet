using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LadybugDB.SourceGen;

/// <summary>
/// Incremental source generator that emits reflection-free <c>Map&lt;T&gt;()</c> /
/// <c>MapAsync&lt;T&gt;()</c> materializers for every <c>[LadybugRow]</c> type.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class LadybugRowGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "LadybugDB.LadybugRowAttribute";

    /// <summary>
    /// The core assembly that DEFINES <c>QueryResult</c> / <c>LadybugRowConvert</c>. The generator runs
    /// as an analyzer during core's own compile too, but core must NOT export a <c>LadybugRowMappers</c>
    /// type — every consumer emits its own, and a copy in core would collide (CS0121 / CS0433 / CS0436)
    /// with the consumer's. So emission is suppressed for exactly this assembly.
    /// </summary>
    private const string CoreAssemblyName = "LadybugDB";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<RowModel?> models = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => ModelBuilder.Build(ctx))
            .WithTrackingName("LadybugRowModels");

        IncrementalValueProvider<ImmutableArray<RowModel>> collected =
            models.Where(static m => m is not null).Select(static (m, _) => m!).Collect();

        // The assembly name gates whether the container is emitted at all (suppressed for core itself).
        IncrementalValueProvider<bool> isCore =
            context.CompilationProvider.Select(static (c, _) => c.AssemblyName == CoreAssemblyName);

        IncrementalValueProvider<(ImmutableArray<RowModel> Models, bool IsCore)> input = collected.Combine(isCore);

        context.RegisterSourceOutput(input, static (spc, pair) =>
        {
            // Core defines QueryResult/LadybugRowConvert; it must not also export the mapper container.
            if (pair.IsCore)
            {
                return;
            }

            var mappable = ImmutableArray.CreateBuilder<RowModel>();
            foreach (RowModel model in pair.Models)
            {
                if (model.Members.IsDefaultOrEmpty)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.NoMappableMembers, model.DeclarationLocation, model.TypeName));
                    continue;
                }

                mappable.Add(model);
            }

            // Always emit the LadybugRowMappers container in a consumer assembly, even with zero
            // [LadybugRow] types. Core does NOT ship a real LadybugRowMappers type, so this generated
            // copy is the single definition in the consumer assembly. Were core to also declare it, a
            // consumer that names the type directly would get CS0436 (an error under
            // TreatWarningsAsErrors). Emitting unconditionally also guarantees the type exists for the
            // §4.7 'result.Map<T>()' extension form to resolve.
            string source = Emitter.Emit(mappable.ToImmutable());
            spc.AddSource("LadybugRowMappers.g.cs", source);
        });
    }
}
