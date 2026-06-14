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

        Location? location = type.Locations.FirstOrDefault();

        return new RowModel(
            TypeFullName: full,
            TypeNamespace: ns,
            TypeName: type.Name,
            IsValueType: type.IsValueType,
            UsePositionalConstructor: positional,
            Members: members.ToImmutable(),
            DeclarationLocation: location);
    }
}
