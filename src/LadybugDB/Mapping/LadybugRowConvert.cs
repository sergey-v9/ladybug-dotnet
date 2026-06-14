using System;
using System.Globalization;

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

        // The fast path above missed, so the engine's CLR type differs from the member type. Unwrap
        // Nullable<T> to the underlying so boxing the converted underlying back into T still produces
        // a valid Nullable<T> (e.g. INT64 cell -> int? member). Convert(cell, underlying) below.
        Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Coerce(cell, target);
    }

    /// <summary>
    /// Coerces <paramref name="cell"/> (a non-null boxed CLR result value) to <paramref name="target"/>
    /// (already Nullable-unwrapped). Shared by <see cref="To{T}"/> and the Extensions RowAccessor so
    /// both mapping paths handle enum / Guid / DateOnly &lt;-&gt; DateTime identically.
    /// </summary>
    internal static object Coerce(object cell, Type target)
    {
        // Enums: the engine surfaces the column as its integral type (e.g. INT64 -> long). Convert to
        // the enum's underlying integral first, then box as the enum via Enum.ToObject (AOT-safe; no
        // Enum.Parse string reflection). Handles both Enum members and Nullable<Enum>.
        if (target.IsEnum)
        {
            object integral = System.Convert.ChangeType(cell, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture);
            return Enum.ToObject(target, integral);
        }

        // Guid: UUID columns surface as Guid (already caught by the fast path), but a UUID that failed
        // upstream Guid.TryParse — or a STRING column mapped to Guid — arrives as a string.
        if (target == typeof(Guid))
        {
            return cell switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ => throw new InvalidCastException($"Cannot convert {cell.GetType()} to System.Guid."),
            };
        }

#if NET7_0_OR_GREATER
        // DateOnly <-> DateTime cross-conversion: on net7+ the engine returns DATE as DateOnly;
        // neither type implements IConvertible against the other, so Convert.ChangeType would throw.
        // (On netstandard2.0 DateOnly/TimeOnly don't exist and DATE already arrives as DateTime.)
        if (target == typeof(DateTime) && cell is DateOnly dateOnly)
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue);
        }

        if (target == typeof(DateOnly) && cell is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }
#endif

        // Common widenings the engine does not pre-coerce (e.g. an INT64 cell into an int member,
        // DOUBLE into float, DECIMAL into decimal).
        return System.Convert.ChangeType(cell, target, CultureInfo.InvariantCulture);
    }
}
