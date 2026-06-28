using System;
using System.Threading;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// A single row of a <see cref="QueryResult"/>. The underlying native tuple buffer is reused by the
/// engine across iterations, so read or copy values before advancing the result. The high-level
/// <see cref="QueryResult.Rows"/> helper handles this for you.
/// </summary>
public sealed class FlatTuple : IDisposable
{
    private LbugFlatTuple _handle;
    private int _disposed;

    internal FlatTuple(LbugFlatTuple handle)
    {
        _handle = handle;
    }

    /// <summary>Returns the value at the given zero-based column index.</summary>
    public Value GetValue(ulong index)
    {
        ThrowIfDisposed();
        return new Value(GetRawValue(index));
    }

    /// <summary>Returns the value at the given zero-based column index.</summary>
    public Value GetValue(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return GetValue((ulong)index);
    }

    /// <summary>Returns whether the value at the given zero-based column index is SQL NULL.</summary>
    public bool IsNull(int index)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            return Native.ValueIsNull(ref value);
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    /// <summary>Reads a STRING value at the given zero-based column index.</summary>
    public string? GetString(int index)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                return null;
            }

            EnsureSuccess(Native.ValueGetString(ref value, out IntPtr pointer), DataTypeId.String);
            return Native.TakeString(pointer);
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public bool GetBool(int index)
    {
        if (TryGetBool(index, out bool value))
        {
            return value;
        }

        throw new LadybugException("Value is NULL; use GetBoolOrDefault for nullable access.");
    }

    public bool GetBoolOrDefault(int index) => TryGetBool(index, out bool value) ? value : default;

    public bool TryGetBool(int index, out bool result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetBool(ref value, out byte raw), DataTypeId.Bool);
            result = raw != 0;
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public sbyte GetInt8(int index) => TryGetInt8(index, out sbyte value)
        ? value
        : throw new LadybugException("Value is NULL; use GetInt8OrDefault for nullable access.");

    public sbyte GetInt8OrDefault(int index) => TryGetInt8(index, out sbyte value) ? value : default;

    public bool TryGetInt8(int index, out sbyte result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetInt8(ref value, out result), DataTypeId.Int8);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public short GetInt16(int index) => TryGetInt16(index, out short value)
        ? value
        : throw new LadybugException("Value is NULL; use GetInt16OrDefault for nullable access.");

    public short GetInt16OrDefault(int index) => TryGetInt16(index, out short value) ? value : default;

    public bool TryGetInt16(int index, out short result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetInt16(ref value, out result), DataTypeId.Int16);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public int GetInt32(int index) => TryGetInt32(index, out int value)
        ? value
        : throw new LadybugException("Value is NULL; use GetInt32OrDefault for nullable access.");

    public int GetInt32OrDefault(int index) => TryGetInt32(index, out int value) ? value : default;

    public bool TryGetInt32(int index, out int result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetInt32(ref value, out result), DataTypeId.Int32);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public long GetInt64(int index) => TryGetInt64(index, out long value)
        ? value
        : throw new LadybugException("Value is NULL; use GetInt64OrDefault for nullable access.");

    public long GetInt64OrDefault(int index) => TryGetInt64(index, out long value) ? value : default;

    public bool TryGetInt64(int index, out long result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetInt64(ref value, out result), DataTypeId.Int64);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public byte GetUInt8(int index) => TryGetUInt8(index, out byte value)
        ? value
        : throw new LadybugException("Value is NULL; use GetUInt8OrDefault for nullable access.");

    public byte GetUInt8OrDefault(int index) => TryGetUInt8(index, out byte value) ? value : default;

    public bool TryGetUInt8(int index, out byte result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetUInt8(ref value, out result), DataTypeId.UInt8);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public ushort GetUInt16(int index) => TryGetUInt16(index, out ushort value)
        ? value
        : throw new LadybugException("Value is NULL; use GetUInt16OrDefault for nullable access.");

    public ushort GetUInt16OrDefault(int index) => TryGetUInt16(index, out ushort value) ? value : default;

    public bool TryGetUInt16(int index, out ushort result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetUInt16(ref value, out result), DataTypeId.UInt16);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public uint GetUInt32(int index) => TryGetUInt32(index, out uint value)
        ? value
        : throw new LadybugException("Value is NULL; use GetUInt32OrDefault for nullable access.");

    public uint GetUInt32OrDefault(int index) => TryGetUInt32(index, out uint value) ? value : default;

    public bool TryGetUInt32(int index, out uint result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetUInt32(ref value, out result), DataTypeId.UInt32);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public ulong GetUInt64(int index) => TryGetUInt64(index, out ulong value)
        ? value
        : throw new LadybugException("Value is NULL; use GetUInt64OrDefault for nullable access.");

    public ulong GetUInt64OrDefault(int index) => TryGetUInt64(index, out ulong value) ? value : default;

    public bool TryGetUInt64(int index, out ulong result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetUInt64(ref value, out result), DataTypeId.UInt64);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public float GetFloat(int index) => TryGetFloat(index, out float value)
        ? value
        : throw new LadybugException("Value is NULL; use GetFloatOrDefault for nullable access.");

    public float GetFloatOrDefault(int index) => TryGetFloat(index, out float value) ? value : default;

    public bool TryGetFloat(int index, out float result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetFloat(ref value, out result), DataTypeId.Float);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    public double GetDouble(int index) => TryGetDouble(index, out double value)
        ? value
        : throw new LadybugException("Value is NULL; use GetDoubleOrDefault for nullable access.");

    public double GetDoubleOrDefault(int index) => TryGetDouble(index, out double value) ? value : default;

    public bool TryGetDouble(int index, out double result)
    {
        LbugValue value = GetRawValue(CheckedIndex(index));
        try
        {
            if (Native.ValueIsNull(ref value))
            {
                result = default;
                return false;
            }

            EnsureSuccess(Native.ValueGetDouble(ref value, out result), DataTypeId.Double);
            return true;
        }
        finally
        {
            Native.ValueDestroy(ref value);
        }
    }

    /// <inheritdoc />
    public override string? ToString()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return Native.TakeString(Native.FlatTupleToString(ref _handle));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Native.FlatTupleDestroy(ref _handle);
    }

    private LbugValue GetRawValue(ulong index)
    {
        ThrowIfDisposed();
        LbugState state = Native.FlatTupleGetValue(ref _handle, index, out LbugValue value);
        if (state != LbugState.Success)
        {
            throw new LadybugException($"Failed to read value at column {index}.");
        }

        return value;
    }

    private static ulong CheckedIndex(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return (ulong)index;
    }

    private static void EnsureSuccess(LbugState state, DataTypeId typeId)
    {
        if (state != LbugState.Success)
        {
            throw new LadybugException($"Failed to read a {typeId} value.");
        }
    }

    private void ThrowIfDisposed()
    {
        ThrowHelpers.ThrowIfDisposed(Volatile.Read(ref _disposed) != 0, this);
    }
}
