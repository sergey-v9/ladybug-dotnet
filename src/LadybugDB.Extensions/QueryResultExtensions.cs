using System;
using System.Collections.Generic;
using System.Data;
using LadybugDB;

namespace LadybugDB.Extensions;

/// <summary>
/// Export and projection helpers over a core <see cref="QueryResult"/>: dictionaries, LINQ projection,
/// scalar extraction, JSON, <see cref="DataTable"/>, and CSV. Each method eagerly materializes the
/// result's rows once and delegates to the shared, native-free helpers.
/// </summary>
public static class QueryResultExtensions
{
    /// <summary>Projects each row into a dictionary keyed by column name.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(this QueryResult r)
        => ResultHelpers.ToDictionaries(View(r));

    /// <summary>Projects each row through <paramref name="selector"/> with an <see cref="IRowAccessor"/>.</summary>
    public static IEnumerable<T> Select<T>(this QueryResult r, Func<IRowAccessor, T> selector)
        => ResultHelpers.Select(View(r), selector ?? throw new ArgumentNullException(nameof(selector)));

    /// <summary>Reads the first row's value at <paramref name="column"/>, converted to <typeparamref name="T"/>.</summary>
    public static T Scalar<T>(this QueryResult r, int column = 0)
        => ResultHelpers.Scalar<T>(View(r), column);

    /// <summary>Serializes the whole result (columns + rows) to JSON.</summary>
#if NET
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(ResultHelpers.JsonReflectionMessage)]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(ResultHelpers.JsonReflectionMessage)]
#endif
    public static string ToJson(this QueryResult r)
        => ResultHelpers.ToJson(View(r));

    /// <summary>Serializes the rows as a JSON array of column-keyed objects.</summary>
#if NET
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(ResultHelpers.JsonReflectionMessage)]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(ResultHelpers.JsonReflectionMessage)]
#endif
    public static string ToJsonArray(this QueryResult r)
        => ResultHelpers.ToJsonArray(View(r));

    /// <summary>Converts the result to a <see cref="DataTable"/> (nulls become <see cref="DBNull"/>).</summary>
    public static DataTable ToDataTable(this QueryResult r)
        => ResultHelpers.ToDataTable(View(r));

    /// <summary>Converts the result to a CSV string using <paramref name="separator"/>.</summary>
    public static string ToCsv(this QueryResult r, char separator = ',')
        => ResultHelpers.ToCsv(View(r), separator);

    private static IResultData View(QueryResult r)
        => new QueryResultData(r ?? throw new ArgumentNullException(nameof(r)));
}
