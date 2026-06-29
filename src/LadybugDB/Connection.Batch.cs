using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LadybugDB;

/// <summary>
/// Prepare-once / bind-many batch surface for <see cref="Connection"/>. A single
/// <see cref="PreparedStatement"/> is prepared from the Cypher and reused across every parameter set —
/// re-binding a named parameter overwrites its previous value — so the query is planned only once and
/// the pooled-bind fast path lands on the hot repeated-shape loop (bulk insert/update, by-key delete,
/// scalar-per-key reads). Operations still serialize on the connection's gate.
/// </summary>
public sealed partial class Connection
{
    /// <summary>
    /// Prepares <paramref name="cypher"/> once, then for each parameter set re-binds every key on the
    /// same <see cref="PreparedStatement"/> and executes it, disposing each <see cref="QueryResult"/>
    /// immediately. Intended for write-path loops where no result is retained. An empty sequence is a
    /// no-op.
    /// </summary>
    /// <exception cref="LadybugQueryException">Thrown when preparation or any execution fails.</exception>
    public void ExecuteMany(string cypher, IEnumerable<IReadOnlyDictionary<string, object?>> parameterSets)
    {
        cypher = ThrowHelpers.ThrowIfNull(cypher, nameof(cypher));
        parameterSets = ThrowHelpers.ThrowIfNull(parameterSets, nameof(parameterSets));

        using PreparedStatement statement = Prepare(cypher);
        foreach (IReadOnlyDictionary<string, object?> parameters in parameterSets)
        {
            BindAll(statement, parameters);
            Execute(statement).Dispose();
        }
    }

    /// <summary>
    /// Prepares <paramref name="cypher"/> once, then for each parameter set re-binds every key on the
    /// same <see cref="PreparedStatement"/>, executes it, projects the <see cref="QueryResult"/> through
    /// <paramref name="selector"/>, and disposes the result — returning the projected values in input
    /// order. Intended for read loops that map one value per parameter set (e.g. rank-per-key). An empty
    /// sequence returns an empty list.
    /// </summary>
    /// <exception cref="LadybugQueryException">Thrown when preparation or any execution fails.</exception>
    public IReadOnlyList<T> ExecuteMany<T>(
        string cypher,
        IEnumerable<IReadOnlyDictionary<string, object?>> parameterSets,
        Func<QueryResult, T> selector)
    {
        cypher = ThrowHelpers.ThrowIfNull(cypher, nameof(cypher));
        parameterSets = ThrowHelpers.ThrowIfNull(parameterSets, nameof(parameterSets));
        selector = ThrowHelpers.ThrowIfNull(selector, nameof(selector));

        var results = new List<T>();
        using PreparedStatement statement = Prepare(cypher);
        foreach (IReadOnlyDictionary<string, object?> parameters in parameterSets)
        {
            BindAll(statement, parameters);
            using QueryResult result = Execute(statement);
            results.Add(selector(result));
        }

        return results;
    }

    /// <summary>Asynchronously prepares <paramref name="cypher"/> once and executes it against every
    /// parameter set (write path). The synchronous work is offloaded under the connection gate and the
    /// <paramref name="ct"/> is honored before and after the offload, consistent with the other async
    /// members.</summary>
    public Task ExecuteManyAsync(
        string cypher,
        IEnumerable<IReadOnlyDictionary<string, object?>> parameterSets,
        CancellationToken ct = default)
    {
        cypher = ThrowHelpers.ThrowIfNull(cypher, nameof(cypher));
        parameterSets = ThrowHelpers.ThrowIfNull(parameterSets, nameof(parameterSets));

        return RunWithCancellation<object?>(
            () =>
            {
                ExecuteMany(cypher, parameterSets);
                return null;
            },
            ct);
    }

    /// <summary>Asynchronously prepares <paramref name="cypher"/> once, executes it against every
    /// parameter set, and projects each result through <paramref name="selector"/> (read path). The
    /// synchronous work is offloaded under the connection gate and the <paramref name="ct"/> is honored
    /// before and after the offload, consistent with the other async members.</summary>
    public Task<IReadOnlyList<T>> ExecuteManyAsync<T>(
        string cypher,
        IEnumerable<IReadOnlyDictionary<string, object?>> parameterSets,
        Func<QueryResult, T> selector,
        CancellationToken ct = default)
    {
        cypher = ThrowHelpers.ThrowIfNull(cypher, nameof(cypher));
        parameterSets = ThrowHelpers.ThrowIfNull(parameterSets, nameof(parameterSets));
        selector = ThrowHelpers.ThrowIfNull(selector, nameof(selector));

        return RunWithCancellation(() => ExecuteMany(cypher, parameterSets, selector), ct);
    }

    private static void BindAll(PreparedStatement statement, IReadOnlyDictionary<string, object?> parameters)
    {
        parameters = ThrowHelpers.ThrowIfNull(parameters, nameof(parameters));
        foreach (KeyValuePair<string, object?> parameter in parameters)
        {
            statement.Bind(parameter.Key, parameter.Value);
        }
    }
}
