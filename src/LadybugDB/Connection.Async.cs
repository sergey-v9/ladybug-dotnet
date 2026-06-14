using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LadybugDB;

/// <summary>
/// Asynchronous surface for <see cref="Connection"/>. Engine calls are synchronous and are offloaded
/// with <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>; the work still takes the
/// per-connection <c>_gate</c> lock inside the wrapped synchronous method, so a single connection stays
/// serialized. A <see cref="CancellationToken"/> registers <see cref="Interrupt"/> (native
/// <c>lbug_connection_interrupt</c>) for the duration of the call and is honored before and after the
/// offload; an interrupt-induced <see cref="LadybugQueryException"/> is normalized to
/// <see cref="OperationCanceledException"/> when the token is cancelled.
/// </summary>
/// <remarks>
/// By design (CONC-5), the async methods do not add real I/O concurrency to a single connection: the
/// wrapped synchronous engine call still acquires the per-connection serialization gate. If several
/// async calls are launched on the same <see cref="Connection"/> without being awaited in turn, all but
/// the one holding the gate block a thread-pool thread until it is released. This mirrors Python's
/// pool model. For genuine concurrency, use one <see cref="Connection"/> per concurrent operation or a
/// connection pool (see the LadybugDB.Extensions pooling helpers) rather than sharing one connection.
/// </remarks>
public sealed partial class Connection
{
    /// <summary>Executes a Cypher query asynchronously. Cancellation interrupts the running query.</summary>
    /// <remarks>Concurrent un-awaited calls on a single connection serialize on the internal gate and
    /// block a thread-pool thread (CONC-5); use one connection per concurrent operation or a pool.</remarks>
    public Task<QueryResult> QueryAsync(string cypher, CancellationToken ct = default)
    {
        if (cypher is null)
        {
            throw new ArgumentNullException(nameof(cypher));
        }

        return RunWithCancellation(() => Query(cypher), ct);
    }

    /// <summary>Executes a multi-statement Cypher query asynchronously, returning every result set.</summary>
    public Task<IReadOnlyList<QueryResult>> QueryAllAsync(string cypher, CancellationToken ct = default)
    {
        if (cypher is null)
        {
            throw new ArgumentNullException(nameof(cypher));
        }

        return RunWithCancellation<IReadOnlyList<QueryResult>>(() => QueryAll(cypher), ct);
    }

    /// <summary>Prepares a parameterized Cypher statement asynchronously.</summary>
    public Task<PreparedStatement> PrepareAsync(string cypher, CancellationToken ct = default)
    {
        if (cypher is null)
        {
            throw new ArgumentNullException(nameof(cypher));
        }

        return RunWithCancellation(() => Prepare(cypher), ct);
    }

    /// <summary>Executes a previously prepared statement asynchronously.</summary>
    public Task<QueryResult> ExecuteAsync(PreparedStatement statement, CancellationToken ct = default)
    {
        if (statement is null)
        {
            throw new ArgumentNullException(nameof(statement));
        }

        return RunWithCancellation(() => Execute(statement), ct);
    }

    /// <summary>
    /// Streams the rows of a Cypher query as an async sequence. Each yielded <see cref="FlatTuple"/>
    /// shares the engine's reused buffer, so consume (and dispose) it before requesting the next.
    /// Cancellation interrupts the underlying query.
    /// </summary>
    public async IAsyncEnumerable<FlatTuple> StreamAsync(
        string cypher,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (cypher is null)
        {
            throw new ArgumentNullException(nameof(cypher));
        }

        QueryResult result = await QueryAsync(cypher, ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                bool hasNext = await Task.Run(() => result.HasNext(), ct).ConfigureAwait(false);
                if (!hasNext)
                {
                    yield break;
                }

                yield return result.GetNext();
            }
        }
        finally
        {
            result.Dispose();
        }
    }

    // Honors ct before offload, registers Interrupt() for the duration of the native call, and honors
    // ct after the offload so a cancellation that lands as an engine error surfaces as
    // OperationCanceledException.
    private async Task<T> RunWithCancellation<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using (ct.Register(static state => ((Connection)state!).InterruptForCancellation(), this))
        {
            try
            {
                return await Task.Run(work, ct).ConfigureAwait(false);
            }
            catch (LadybugQueryException ex) when (ct.IsCancellationRequested)
            {
                // The interrupt surfaced as an engine error; normalize to cancellation but keep the
                // original engine error as the inner exception so a genuine failure is not masked
                // (CONC-3) -- e.g. when the token cancelled for an unrelated reason.
                throw NormalizeCancellationException(ex, ct);
            }
        }
    }

    // Wraps an engine error that surfaced under a cancelled token as an OperationCanceledException
    // while preserving the original as InnerException (CONC-3). Internal so the contract is unit-tested.
    internal static OperationCanceledException NormalizeCancellationException(
        LadybugQueryException inner, CancellationToken ct)
        => new("The query was canceled.", inner, ct);

    // Best-effort interrupt invoked from a cancellation callback: a disposed connection (or a race
    // with disposal) must never throw out of the registration, so swallow ObjectDisposedException.
    private void InterruptForCancellation()
    {
        try
        {
            Interrupt();
        }
        catch (ObjectDisposedException)
        {
            // The connection was disposed concurrently; nothing to interrupt.
        }
    }

    // Native-free test hook for the cancellation contract: honors ct before and after the offload with
    // no native interaction, so the cancellation semantics can be verified without the engine.
    internal static async Task<T> RunWithCancellationForTests<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        T result = await Task.Run(work, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
