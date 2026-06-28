using System.Globalization;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// The logical data type of a result column or value: its <see cref="DataTypeId"/>, the element
/// type for LIST/ARRAY columns (<see cref="ChildType"/>), and the fixed length for ARRAY columns
/// (<see cref="FixedArraySize"/>). Wraps the engine's <c>lbug_logical_type</c>.
/// </summary>
public sealed class LogicalType
{
    /// <summary>The logical type identifier.</summary>
    public DataTypeId Id { get; }

    /// <summary>The element type for LIST and ARRAY types; <see langword="null"/> otherwise.</summary>
    public LogicalType? ChildType { get; }

    /// <summary>The fixed element count for ARRAY types; <see langword="null"/> otherwise.</summary>
    public ulong? FixedArraySize { get; }

    private LogicalType(DataTypeId id, LogicalType? childType, ulong? fixedArraySize)
    {
        Id = id;
        ChildType = childType;
        FixedArraySize = fixedArraySize;
    }

    /// <summary>
    /// Builds a fully-decoded <see cref="LogicalType"/> from an owned native logical-type handle and
    /// destroys that handle (and any child handles it walks). The returned managed type holds no
    /// native resources.
    /// </summary>
    internal static LogicalType FromOwnedHandle(ref LbugLogicalType handle)
    {
        var id = (DataTypeId)Native.DataTypeGetId(ref handle);

        LogicalType? child = null;
        ulong? fixedSize = null;

        if (id is DataTypeId.List or DataTypeId.Array)
        {
            if (Native.DataTypeGetChildType(ref handle, out LbugLogicalType childHandle) == LbugState.Success)
            {
                child = FromOwnedHandle(ref childHandle); // recursion destroys childHandle
            }
        }

        if (id is DataTypeId.Array)
        {
            if (Native.DataTypeGetNumElementsInArray(ref handle, out ulong n) == LbugState.Success)
            {
                fixedSize = n;
            }
        }

        Native.DataTypeDestroy(ref handle);
        return new LogicalType(id, child, fixedSize);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        string name = Id.ToString().ToUpperInvariant();
        return Id switch
        {
            DataTypeId.List when ChildType is not null => $"LIST({ChildType})",
            DataTypeId.Array when ChildType is not null && FixedArraySize is ulong n
                => $"ARRAY({ChildType}, {n.ToString(CultureInfo.InvariantCulture)})",
            _ => name,
        };
    }
}
