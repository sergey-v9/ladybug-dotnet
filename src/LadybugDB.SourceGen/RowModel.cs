using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

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
    ImmutableArray<RowMember> Members,
    // NOTE: excluded from Equals/GetHashCode — Location is not value-equatable and would defeat
    // incremental caching. Only used for diagnostic reporting.
    Location? DeclarationLocation = null)
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
