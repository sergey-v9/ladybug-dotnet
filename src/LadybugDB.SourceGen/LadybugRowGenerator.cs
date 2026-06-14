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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<RowModel?> models = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => ModelBuilder.Build(ctx))
            .WithTrackingName("LadybugRowModels");

        IncrementalValueProvider<ImmutableArray<RowModel>> collected =
            models.Where(static m => m is not null).Select(static (m, _) => m!).Collect();

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
    }
}
