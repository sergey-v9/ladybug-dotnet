using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LadybugDB;

namespace LadybugDB.Extensions;

/// <summary>
/// Production <see cref="ILadybugExecutor"/> over a core <see cref="Connection"/>. Uses WS-D's async
/// surface (<c>Connection.QueryAsync</c>) and eagerly materializes each result so callers receive a
/// detached, thread-safe snapshot.
/// </summary>
public sealed class LadybugConnectionExecutor : ILadybugExecutor
{
    private readonly Connection _connection;

    public LadybugConnectionExecutor(Connection connection, string name = "ladybug")
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public async Task<LadybugExecutionResult> ExecuteAsync(
        string cypher,
        CancellationToken cancellationToken = default)
    {
        using QueryResult result = await _connection.QueryAsync(cypher, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> columns = result.ColumnNames;
        var rows = new List<object?[]>();
        foreach (object?[] row in result.Rows())
        {
            rows.Add(row);
        }

        return new LadybugExecutionResult(columns, rows);
    }
}
