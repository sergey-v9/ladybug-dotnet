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

        // Common widenings the engine does not pre-coerce (e.g. an INT64 cell into an int member).
        object converted = System.Convert.ChangeType(cell, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        return (T)converted;
    }
}
