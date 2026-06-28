using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LadybugDB.Interop;

namespace LadybugDB;

/// <summary>
/// A parameterized, pre-compiled Cypher statement. Bind parameters with the fluent <c>Bind</c>
/// overloads, then run it with <see cref="Execute"/> (or <c>Connection.Execute</c>). Reusing a
/// prepared statement avoids re-planning the query on each execution.
/// </summary>
public sealed class PreparedStatement : IDisposable
{
    private static readonly long UnixEpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly Connection _connection;
    private LbugPreparedStatement _handle;
    private int _disposed;

    internal PreparedStatement(Connection connection, LbugPreparedStatement handle)
    {
        _connection = connection;
        _handle = handle;
    }

    /// <summary>Whether the statement was prepared successfully.</summary>
    public bool IsSuccess => Volatile.Read(ref _disposed) == 0 && Native.PreparedStatementIsSuccess(ref _handle);

    /// <summary>Whether the statement performs only read operations.</summary>
    public bool IsReadOnly
    {
        get
        {
            ThrowIfDisposed();
            return Native.PreparedStatementIsReadOnly(ref _handle);
        }
    }

    /// <summary>The error message when preparation failed, otherwise <see langword="null"/>.</summary>
    public string? GetErrorMessage()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        return Native.TakeString(Native.PreparedStatementGetErrorMessage(ref _handle));
    }

    public PreparedStatement Bind(string name, bool value) => Do(name, Native.PreparedStatementBindBool(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, sbyte value) => Do(name, Native.PreparedStatementBindInt8(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, short value) => Do(name, Native.PreparedStatementBindInt16(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, int value) => Do(name, Native.PreparedStatementBindInt32(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, long value) => Do(name, Native.PreparedStatementBindInt64(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, byte value) => Do(name, Native.PreparedStatementBindUInt8(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, ushort value) => Do(name, Native.PreparedStatementBindUInt16(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, uint value) => Do(name, Native.PreparedStatementBindUInt32(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, ulong value) => Do(name, Native.PreparedStatementBindUInt64(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, float value) => Do(name, Native.PreparedStatementBindFloat(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, double value) => Do(name, Native.PreparedStatementBindDouble(ref _handle, Name(name), value));

    public PreparedStatement Bind(string name, string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Do(name, Native.PreparedStatementBindString(ref _handle, Name(name), value));
    }

    public PreparedStatement Bind(string name, Guid value) => Bind(name, value.ToString());

    public PreparedStatement Bind(string name, DateTime value)
    {
        long micros = (ToUtcTicks(value) - UnixEpochTicks) / 10L;
        return Do(name, Native.PreparedStatementBindTimestamp(ref _handle, Name(name), new LbugTimestamp { Value = micros }));
    }

    public PreparedStatement Bind(string name, DateTimeOffset value)
    {
        long micros = (value.UtcDateTime.Ticks - UnixEpochTicks) / 10L;
        return Do(name, Native.PreparedStatementBindTimestampTz(ref _handle, Name(name), new LbugTimestamp { Value = micros }));
    }

    public PreparedStatement Bind(string name, Interval value)
        => Do(name, Native.PreparedStatementBindInterval(ref _handle, Name(name), new LbugInterval { Months = value.Months, Days = value.Days, Micros = value.Micros }));

#if NET7_0_OR_GREATER
    public PreparedStatement Bind(string name, DateOnly value)
    {
        int days = value.DayNumber - new DateOnly(1970, 1, 1).DayNumber;
        return Do(name, Native.PreparedStatementBindDate(ref _handle, Name(name), new LbugDate { Days = days }));
    }
#endif

    /// <summary>Binds a CLR <see cref="decimal"/> as a DECIMAL parameter.</summary>
    public PreparedStatement Bind(string name, decimal value)
        => Bind(name, ToLadybugDecimal(value));

    /// <summary>Binds a <see cref="LadybugDecimal"/> losslessly as a DECIMAL parameter.</summary>
    public PreparedStatement Bind(string name, LadybugDecimal value)
        => BindValue(name, CreateDecimalValue(value));

    /// <summary>Binds a <see cref="BigInteger"/> as an INT128 parameter.</summary>
    public PreparedStatement Bind(string name, BigInteger value)
        => BindValue(name, CreateInt128Value(value));

    /// <summary>
    /// Binds a <see cref="byte"/> array as a BLOB. The C API has no BLOB value creator, so the bytes
    /// are bound as an escaped <c>'\xNN…'</c> string literal; the surrounding query is expected to
    /// <c>CAST(... AS BLOB)</c>.
    /// </summary>
    public PreparedStatement Bind(string name, byte[] value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Bind(name, ToBlobLiteral(value));
    }

    /// <summary>Binds a dictionary as a STRUCT parameter.</summary>
    public PreparedStatement Bind(string name, IReadOnlyDictionary<string, object?> value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return BindValue(name, CreateStructValue(value));
    }

    /// <summary>Binds a key/value sequence as a MAP parameter.</summary>
    public PreparedStatement BindMap(string name, IEnumerable<KeyValuePair<object, object?>> value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return BindValue(name, CreateMapValue(value));
    }

    /// <summary>Binds a parameter whose CLR type is determined at runtime.</summary>
    public PreparedStatement Bind(string name, object? value)
    {
        switch (value)
        {
            case null: return BindValue(name, CreateNativeValue(null));
            case bool v: return Bind(name, v);
            case sbyte v: return Bind(name, v);
            case short v: return Bind(name, v);
            case int v: return Bind(name, v);
            case long v: return Bind(name, v);
            case byte v: return Bind(name, v);
            case ushort v: return Bind(name, v);
            case uint v: return Bind(name, v);
            case ulong v: return Bind(name, v);
            case float v: return Bind(name, v);
            case double v: return Bind(name, v);
            case string v: return Bind(name, v);
            case Guid v: return Bind(name, v);
            case DateTimeOffset v: return Bind(name, v);
            case DateTime v: return Bind(name, v);
            case Interval v: return Bind(name, v);
#if NET7_0_OR_GREATER
            case DateOnly v: return Bind(name, v);
#endif
            case decimal v: return Bind(name, v);
            case LadybugDecimal v: return Bind(name, v);
            case BigInteger v: return Bind(name, v);
            case byte[] v: return Bind(name, v);
            case IReadOnlyDictionary<string, object?> v: return Bind(name, v);
            case IEnumerable<KeyValuePair<object, object?>> v: return BindMap(name, v);
            case IEnumerable v: return BindValue(name, CreateNativeValue(v));
            default:
                throw new NotSupportedException($"Cannot bind a parameter of type {value.GetType()}.");
        }
    }

    /// <summary>Executes the statement on its owning connection.</summary>
    public QueryResult Execute() => _connection.Execute(this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Native.PreparedStatementDestroy(ref _handle);
    }

    internal LbugState ExecuteOn(ref LbugConnection connection, out LbugQueryResult outQueryResult)
    {
        ThrowIfDisposed();
        return Native.ConnectionExecute(ref connection, ref _handle, out outQueryResult);
    }

    // Evaluated while building the native bind call's arguments, so it guards against use-after-dispose
    // before the native function actually runs.
    private string Name(string name)
    {
        ThrowIfDisposed();
        return name ?? throw new ArgumentNullException(nameof(name));
    }

    private static long ToUtcTicks(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc).Ticks
            : value.ToUniversalTime().Ticks;

    private static IntPtr CreateNativeValue(object? value)
    {
        IntPtr handle = value switch
        {
            null => Native.ValueCreateNull(),
            bool v => Native.ValueCreateBool(v),
            sbyte v => Native.ValueCreateInt8(v),
            short v => Native.ValueCreateInt16(v),
            int v => Native.ValueCreateInt32(v),
            long v => Native.ValueCreateInt64(v),
            byte v => Native.ValueCreateUInt8(v),
            ushort v => Native.ValueCreateUInt16(v),
            uint v => Native.ValueCreateUInt32(v),
            ulong v => Native.ValueCreateUInt64(v),
            float v => Native.ValueCreateFloat(v),
            double v => Native.ValueCreateDouble(v),
            string v => Native.ValueCreateString(v),
            Guid v => Native.ValueCreateString(v.ToString()),
            DateTimeOffset v => Native.ValueCreateTimestampTz(new LbugTimestamp { Value = (v.UtcDateTime.Ticks - UnixEpochTicks) / 10L }),
            DateTime v => Native.ValueCreateTimestamp(new LbugTimestamp { Value = (ToUtcTicks(v) - UnixEpochTicks) / 10L }),
            Interval v => Native.ValueCreateInterval(new LbugInterval { Months = v.Months, Days = v.Days, Micros = v.Micros }),
#if NET7_0_OR_GREATER
            DateOnly v => Native.ValueCreateDate(new LbugDate { Days = v.DayNumber - new DateOnly(1970, 1, 1).DayNumber }),
#endif
            decimal v => CreateDecimalValue(ToLadybugDecimal(v)),
            LadybugDecimal v => CreateDecimalValue(v),
            BigInteger v => CreateInt128Value(v),
            // PORT-1: a nested byte[] (inside LIST/STRUCT/MAP) cannot be bound. The engine C API has no
            // BLOB value constructor, so BLOB is only bindable as a TOP-LEVEL parameter the query
            // explicitly CASTs to BLOB (see the Bind(string, byte[]) overload). Silently binding a nested
            // byte[] as a string literal would round-trip as STRING, not BLOB, so fail loudly instead.
            byte[] => throw new NotSupportedException(
                "BLOB is only supported as a top-level parameter that the query explicitly CASTs to BLOB; " +
                "a byte[] nested inside a LIST/STRUCT/MAP cannot be bound (the engine C API has no BLOB " +
                "value constructor)."),
            IReadOnlyDictionary<string, object?> v => CreateStructValue(v),
            IEnumerable<KeyValuePair<object, object?>> v => CreateMapValue(v),
            IEnumerable v => CreateNativeList(v),
            _ => throw new NotSupportedException($"Cannot bind a parameter of type {value.GetType()}.")
        };

        if (handle == IntPtr.Zero)
        {
            throw new LadybugException("Failed to create a native parameter value.");
        }

        return handle;
    }

    private static LadybugDecimal ToLadybugDecimal(decimal value)
    {
        // decimal's scale lives in bits 16-23 of the flags word (index 3 of GetBits).
        int[] bits = decimal.GetBits(value);
        byte scale = (byte)((bits[3] >> 16) & 0x7F);
        BigInteger mantissa = MantissaOf(value);
        return new LadybugDecimal(value < 0 ? -mantissa : mantissa, scale);
    }

    private static BigInteger MantissaOf(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        uint lo = (uint)bits[0];
        uint mid = (uint)bits[1];
        uint hi = (uint)bits[2];
        return (new BigInteger(hi) << 64) | (new BigInteger(mid) << 32) | lo;
    }

    private static IntPtr CreateDecimalValue(LadybugDecimal value)
    {
        // Native create_decimal takes the textual form plus precision and scale; precision must be
        // at least the number of significant digits in the mantissa.
        string text = value.ToString();
        uint scale = value.Scale;
        uint precision = (uint)BigInteger.Abs(value.Unscaled).ToString(CultureInfo.InvariantCulture).TrimStart('0').Length;
        if (precision < scale + 1)
        {
            precision = scale + 1u;
        }

        IntPtr handle = Native.ValueCreateDecimal(text, precision, scale);
        if (handle == IntPtr.Zero)
        {
            throw new LadybugException("Failed to create a DECIMAL parameter value.");
        }

        return handle;
    }

    private static IntPtr CreateInt128Value(BigInteger value)
    {
        // Use the native string parser so the full 128-bit range is honored exactly.
        if (Native.Int128FromString(value.ToString(CultureInfo.InvariantCulture), out LbugInt128 int128) != LbugState.Success)
        {
            throw new LadybugException($"Value {value} is out of INT128 range.");
        }

        IntPtr handle = Native.ValueCreateInt128(int128);
        if (handle == IntPtr.Zero)
        {
            throw new LadybugException("Failed to create an INT128 parameter value.");
        }

        return handle;
    }

    private static string ToBlobLiteral(byte[] value)
    {
#if NET7_0_OR_GREATER
        return string.Create(value.Length * 4, value, static (chars, bytes) => FillBlobLiteral(chars, bytes));
#else
        char[] chars = new char[value.Length * 4];
        FillBlobLiteral(chars, value);
        return new string(chars);
#endif
    }

#if NET7_0_OR_GREATER
    private static void FillBlobLiteral(Span<char> chars, byte[] value)
#else
    private static void FillBlobLiteral(char[] chars, byte[] value)
#endif
    {
        int offset = 0;
        foreach (byte b in value)
        {
            chars[offset++] = '\\';
            chars[offset++] = 'x';
            chars[offset++] = ToUpperHex(b >> 4);
            chars[offset++] = ToUpperHex(b & 0xF);
        }
    }

    private static char ToUpperHex(int value) =>
        (char)(value < 10 ? '0' + value : 'A' + value - 10);

    private static IntPtr CreateStructValue(IReadOnlyDictionary<string, object?> fields)
    {
        var names = new IntPtr[fields.Count];
        var values = new IntPtr[fields.Count];
        int allocated = 0;
        try
        {
            foreach (KeyValuePair<string, object?> field in fields)
            {
                names[allocated] = Utf8ToCoTaskMem(field.Key);
                allocated++;
                values[allocated - 1] = CreateNativeValue(field.Value);
            }

            LbugState state = Native.ValueCreateStruct((ulong)allocated, names, values, out IntPtr structHandle);
            if (state != LbugState.Success || structHandle == IntPtr.Zero)
            {
                throw new LadybugException("Failed to create a STRUCT parameter value.");
            }

            return structHandle;
        }
        finally
        {
            for (int i = 0; i < allocated; i++)
            {
                if (values[i] != IntPtr.Zero)
                {
                    Native.ValueDestroy(values[i]);
                }

                if (names[i] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(names[i]);
                }
            }
        }
    }

    // Allocates a NUL-terminated UTF-8 copy of the string in unmanaged memory (CoTaskMem), portable
    // across both target frameworks (Marshal.StringToCoTaskMemUTF8 is unavailable on ns2.0). Free
    // with Marshal.FreeCoTaskMem.
    private static unsafe IntPtr Utf8ToCoTaskMem(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        IntPtr buffer = Marshal.AllocCoTaskMem(byteCount + 1);
        try
        {
            fixed (char* chars = value)
            {
                Encoding.UTF8.GetBytes(chars, value.Length, (byte*)buffer, byteCount);
            }

            Marshal.WriteByte(buffer, byteCount, 0);
        }
        catch
        {
            Marshal.FreeCoTaskMem(buffer);
            throw;
        }

        return buffer;
    }

    private static IntPtr CreateMapValue(IEnumerable<KeyValuePair<object, object?>> entries)
    {
        var keyPtrs = new List<IntPtr>();
        var valuePtrs = new List<IntPtr>();
        try
        {
            foreach (KeyValuePair<object, object?> entry in entries)
            {
                keyPtrs.Add(CreateNativeValue(entry.Key));
                valuePtrs.Add(CreateNativeValue(entry.Value));
            }

            if (keyPtrs.Count == 0)
            {
                throw new NotSupportedException("Cannot bind an empty MAP parameter; the engine cannot infer its key/value types.");
            }

            IntPtr[] keys = keyPtrs.ToArray();
            IntPtr[] values = valuePtrs.ToArray();
            LbugState state = Native.ValueCreateMap((ulong)keys.Length, keys, values, out IntPtr mapHandle);
            if (state != LbugState.Success || mapHandle == IntPtr.Zero)
            {
                throw new LadybugException("Failed to create a MAP parameter value.");
            }

            return mapHandle;
        }
        finally
        {
            foreach (IntPtr ptr in valuePtrs)
            {
                Native.ValueDestroy(ptr);
            }

            foreach (IntPtr ptr in keyPtrs)
            {
                Native.ValueDestroy(ptr);
            }
        }
    }

    private static IntPtr CreateNativeList(IEnumerable values)
    {
        var elementHandles = new List<IntPtr>();
        try
        {
            foreach (object? value in values)
            {
                elementHandles.Add(CreateNativeValue(value));
            }

            if (elementHandles.Count == 0)
            {
                return CreateNativeEmptyList(values.GetType());
            }

            IntPtr[] elements = elementHandles.ToArray();
            LbugState state = Native.ValueCreateList((ulong)elements.Length, elements, out IntPtr listHandle);
            if (state != LbugState.Success || listHandle == IntPtr.Zero)
            {
                throw new LadybugException("Failed to create a LIST parameter value.");
            }

            return listHandle;
        }
        finally
        {
            foreach (IntPtr handle in elementHandles)
            {
                Native.ValueDestroy(handle);
            }
        }
    }

    private static IntPtr CreateNativeEmptyList(Type sequenceType)
    {
        if (!TryGetElementDataTypeId(sequenceType, out LbugDataTypeId childTypeId))
        {
            throw new NotSupportedException(
                $"Cannot bind empty enumerable parameter type {sequenceType} because its element type is unknown or unsupported.");
        }

        Native.DataTypeCreate(childTypeId, IntPtr.Zero, 0, out LbugLogicalType childType);
        try
        {
            Native.DataTypeCreateWithChild(LbugDataTypeId.List, ref childType, 0, out LbugLogicalType listType);
            try
            {
                IntPtr handle = Native.ValueCreateDefault(ref listType);
                if (handle == IntPtr.Zero)
                {
                    throw new LadybugException("Failed to create an empty LIST parameter value.");
                }

                return handle;
            }
            finally
            {
                Native.DataTypeDestroy(ref listType);
            }
        }
        finally
        {
            Native.DataTypeDestroy(ref childType);
        }
    }

    private static bool TryGetElementDataTypeId(Type sequenceType, out LbugDataTypeId dataTypeId)
    {
        Type? elementType = GetEnumerableElementType(sequenceType);
        if (elementType is not null && Nullable.GetUnderlyingType(elementType) is { } nullableType)
        {
            elementType = nullableType;
        }

        if (elementType == typeof(bool))
        {
            dataTypeId = LbugDataTypeId.Bool;
            return true;
        }

        if (elementType == typeof(sbyte))
        {
            dataTypeId = LbugDataTypeId.Int8;
            return true;
        }

        if (elementType == typeof(short))
        {
            dataTypeId = LbugDataTypeId.Int16;
            return true;
        }

        if (elementType == typeof(int))
        {
            dataTypeId = LbugDataTypeId.Int32;
            return true;
        }

        if (elementType == typeof(long))
        {
            dataTypeId = LbugDataTypeId.Int64;
            return true;
        }

        if (elementType == typeof(byte))
        {
            dataTypeId = LbugDataTypeId.UInt8;
            return true;
        }

        if (elementType == typeof(ushort))
        {
            dataTypeId = LbugDataTypeId.UInt16;
            return true;
        }

        if (elementType == typeof(uint))
        {
            dataTypeId = LbugDataTypeId.UInt32;
            return true;
        }

        if (elementType == typeof(ulong))
        {
            dataTypeId = LbugDataTypeId.UInt64;
            return true;
        }

        if (elementType == typeof(float))
        {
            dataTypeId = LbugDataTypeId.Float;
            return true;
        }

        if (elementType == typeof(double))
        {
            dataTypeId = LbugDataTypeId.Double;
            return true;
        }

        if (elementType == typeof(string) || elementType == typeof(Guid))
        {
            dataTypeId = LbugDataTypeId.String;
            return true;
        }

        if (elementType == typeof(DateTime))
        {
            dataTypeId = LbugDataTypeId.Timestamp;
            return true;
        }

        if (elementType == typeof(DateTimeOffset))
        {
            dataTypeId = LbugDataTypeId.TimestampTz;
            return true;
        }

        if (elementType == typeof(Interval))
        {
            dataTypeId = LbugDataTypeId.Interval;
            return true;
        }

#if NET7_0_OR_GREATER
        if (elementType == typeof(DateOnly))
        {
            dataTypeId = LbugDataTypeId.Date;
            return true;
        }
#endif

        dataTypeId = default;
        return false;
    }

    private static Type? GetEnumerableElementType(Type sequenceType)
    {
        if (sequenceType.IsArray)
        {
            return sequenceType.GetElementType();
        }

        if (sequenceType.IsGenericType && sequenceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return sequenceType.GetGenericArguments()[0];
        }

        if (sequenceType.IsGenericType)
        {
            Type[] genericArguments = sequenceType.GetGenericArguments();
            if (genericArguments.Length == 1)
            {
                return genericArguments[0];
            }
        }

        return null;
    }

    private PreparedStatement BindValue(string name, IntPtr value)
    {
        try
        {
            return Do(name, Native.PreparedStatementBindValue(ref _handle, Name(name), value));
        }
        finally
        {
            Native.ValueDestroy(value);
        }
    }

    private PreparedStatement Do(string name, LbugState state)
    {
        if (state != LbugState.Success)
        {
            throw new LadybugException($"Failed to bind parameter '{name}'.");
        }

        return this;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PreparedStatement));
        }
    }
}
