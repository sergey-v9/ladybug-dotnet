using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LadybugDB.Extensions;

/// <summary>
/// <see cref="IAsyncEnumerable{T}"/> sugar over <see cref="ILadybugExecutor"/>. The executor returns a
/// materialized result (the core async path streams under the hood via WS-D's
/// <c>Connection.StreamAsync</c>); these helpers expose rows one-at-a-time as <see cref="IRowAccessor"/>.
/// </summary>
public static class ExecutorStreamingExtensions
{
    /// <summary>Executes <paramref name="cypher"/> and yields one <see cref="IRowAccessor"/> per row.</summary>
    public static async IAsyncEnumerable<IRowAccessor> StreamAsync(
        this ILadybugExecutor executor,
        string cypher,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new ArgumentNullException(nameof(executor));
        }

        cancellationToken.ThrowIfCancellationRequested();
        LadybugExecutionResult result =
            await executor.ExecuteAsync(cypher, cancellationToken).ConfigureAwait(false);
        Dictionary<string, int> index = ResultHelpers.BuildColumnIndex(result.Columns);

        foreach (object?[] row in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RowAccessor(row, index);
        }
    }

    /// <summary>Executes <paramref name="cypher"/> and yields each row projected by <paramref name="selector"/>.</summary>
    public static async IAsyncEnumerable<T> StreamAsync<T>(
        this ILadybugExecutor executor,
        string cypher,
        Func<IRowAccessor, T> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new ArgumentNullException(nameof(executor));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        await foreach (IRowAccessor row in executor.StreamAsync(cypher, cancellationToken).ConfigureAwait(false))
        {
            yield return selector(row);
        }
    }
}
