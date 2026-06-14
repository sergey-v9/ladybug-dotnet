using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LadybugDB;

namespace LadybugDB.Extensions;

/// <summary>
/// A read-only view over a query result: column names plus a (re-)enumerable row stream. Implemented
/// by an internal adapter over core's <see cref="QueryResult"/> and by test fakes, so the export
/// helpers below run with or without the native engine.
/// </summary>
public interface IResultData
{
    /// <summary>Column names in result order.</summary>
    IReadOnlyList<string> Columns { get; }

    /// <summary>Enumerates the rows; each call may stream afresh.</summary>
    IEnumerable<object?[]> EnumerateRows();
}

/// <summary>Pure, native-free helpers backing <see cref="QueryResultExtensions"/>.</summary>
internal static class ResultHelpers
{
    internal const string JsonReflectionMessage =
        "JSON serialization of arbitrary query-result values uses reflection and may not be " +
        "trim- or AOT-safe. Project the result to known types before serializing if you need AOT.";

    private static readonly JsonSerializerOptions JsonDefaults = new()
    {
        WriteIndented = false,
    };

    internal static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> columns)
    {
        var index = new Dictionary<string, int>(columns.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
        {
            index[columns[i]] = i;
        }

        return index;
    }

    internal static IReadOnlyList<IReadOnlyDictionary<string, object?>> ToDictionaries(IResultData data)
    {
        IReadOnlyList<string> columns = data.Columns;
        var list = new List<IReadOnlyDictionary<string, object?>>();
        foreach (object?[] row in data.EnumerateRows())
        {
            var dict = new Dictionary<string, object?>(columns.Count);
            for (int i = 0; i < columns.Count; i++)
            {
                dict[columns[i]] = i < row.Length ? row[i] : null;
            }

            list.Add(dict);
        }

        return list;
    }

    internal static IEnumerable<T> Select<T>(IResultData data, Func<IRowAccessor, T> selector)
    {
        Dictionary<string, int> index = BuildColumnIndex(data.Columns);
        foreach (object?[] row in data.EnumerateRows())
        {
            yield return selector(new RowAccessor(row, index));
        }
    }

    internal static T Scalar<T>(IResultData data, int column)
    {
        Dictionary<string, int> index = BuildColumnIndex(data.Columns);
        foreach (object?[] row in data.EnumerateRows())
        {
            return new RowAccessor(row, index).Get<T>(column);
        }

        throw new InvalidOperationException("The query result contains no rows.");
    }

#if NET
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(JsonReflectionMessage)]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(JsonReflectionMessage)]
#endif
    internal static string ToJson(IResultData data)
    {
        var payload = new Dictionary<string, object?>
        {
            ["columns"] = data.Columns,
            ["rows"] = MaterializeRows(data),
        };

        return JsonSerializer.Serialize(payload, JsonDefaults);
    }

#if NET
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(JsonReflectionMessage)]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(JsonReflectionMessage)]
#endif
    internal static string ToJsonArray(IResultData data)
        => JsonSerializer.Serialize(ToDictionaries(data), JsonDefaults);

    internal static DataTable ToDataTable(IResultData data)
    {
        var table = new DataTable();
        foreach (string column in data.Columns)
        {
            table.Columns.Add(column, typeof(object));
        }

        foreach (object?[] row in data.EnumerateRows())
        {
            var values = new object?[data.Columns.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (i < row.Length ? row[i] : null) ?? DBNull.Value;
            }

            table.Rows.Add(values);
        }

        return table;
    }

    internal static string ToCsv(IResultData data, char separator)
    {
        var sb = new StringBuilder();
        string sep = separator.ToString();
        AppendCsvLine(sb, data.Columns, sep, separator);
        foreach (object?[] row in data.EnumerateRows())
        {
            var cells = new string[data.Columns.Count];
            for (int i = 0; i < cells.Length; i++)
            {
                object? value = i < row.Length ? row[i] : null;
                cells[i] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            AppendCsvLine(sb, cells, sep, separator);
        }

        return sb.ToString();
    }

    private static List<object?[]> MaterializeRows(IResultData data)
    {
        var rows = new List<object?[]>();
        foreach (object?[] row in data.EnumerateRows())
        {
            rows.Add(row);
        }

        return rows;
    }

    private static void AppendCsvLine(StringBuilder sb, IReadOnlyList<string> cells, string sep, char separator)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(sep);
            }

            sb.Append(CsvEscape(cells[i], separator));
        }

        sb.Append("\r\n");
    }

    private static string CsvEscape(string value, char separator)
    {
        if (value.IndexOf(separator) >= 0 || value.IndexOf('"') >= 0 ||
            value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

/// <summary>Real <see cref="IResultData"/> over a core <see cref="QueryResult"/>, eagerly materialized.</summary>
internal sealed class QueryResultData : IResultData
{
    private readonly IReadOnlyList<object?[]> _rows;

    public QueryResultData(QueryResult result)
    {
        Columns = result.ColumnNames;

        // Rewind first so each extension call materializes the full result independently, even when
        // several are invoked on the same QueryResult (the engine's tuple iterator is forward-only).
        result.ResetIterator();

        var rows = new List<object?[]>();
        foreach (object?[] row in result.Rows())
        {
            rows.Add(row);
        }

        _rows = rows;
    }

    public IReadOnlyList<string> Columns { get; }

    public IEnumerable<object?[]> EnumerateRows() => _rows;
}
