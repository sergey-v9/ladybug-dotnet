using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LadybugDB.Extensions;

/// <summary>
/// A materialized query result: column names and fully read-into-memory rows. This is the unit the
/// extension layer operates on, which keeps the package testable without the native engine.
/// </summary>
public sealed class LadybugExecutionResult
{
    /// <summary>Creates a materialized result.</summary>
    public LadybugExecutionResult(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    /// <summary>Column names in result order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Materialized rows; each row is aligned with <see cref="Columns"/>.</summary>
    public IReadOnlyList<object?[]> Rows { get; }

    /// <summary>An empty result with no columns and no rows.</summary>
    public static LadybugExecutionResult Empty { get; } =
        new(System.Array.Empty<string>(), System.Array.Empty<object?[]>());
}

/// <summary>
/// Minimal execution seam the extensions depend on. The production implementation
/// (<see cref="LadybugConnectionExecutor"/>) wraps a core <c>Connection</c>; tests supply a fake.
/// </summary>
public interface ILadybugExecutor
{
    /// <summary>A diagnostic name for the executor (surfaced by the health check).</summary>
    string Name { get; }

    /// <summary>Executes a Cypher query and returns its materialized result.</summary>
    Task<LadybugExecutionResult> ExecuteAsync(string cypher, CancellationToken cancellationToken = default);
}
