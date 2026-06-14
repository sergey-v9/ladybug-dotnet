using System;
using System.Collections.Generic;
using LadybugDB.Interop;

namespace LadybugDB;

public sealed partial class Connection
{
    /// <summary>Interrupts the query currently executing on this connection, if any. Safe to call
    /// from another thread while a query runs; it intentionally does not take the connection gate
    /// (which the running query holds), only honoring the disposal guard.</summary>
    public void Interrupt()
    {
        ThrowIfDisposed();
        Native.ConnectionInterrupt(ref _handle);
    }

    /// <summary>Sets the per-query execution timeout. The engine aborts a query that exceeds it.</summary>
    /// <param name="timeout">The timeout; rounded to whole milliseconds (the engine's unit).</param>
    public void SetQueryTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative.");
        }

        ulong ms = (ulong)Math.Max(0, (long)timeout.TotalMilliseconds);
        LbugState state = WithGate((ref LbugConnection h) => Native.ConnectionSetQueryTimeout(ref h, ms));
        if (state != LbugState.Success)
        {
            throw new LadybugException("Failed to set the query timeout.");
        }
    }

    /// <summary>Sets the maximum number of threads the engine may use to execute a query.</summary>
    public void SetMaxThreadsForExec(ulong numThreads)
    {
        LbugState state = WithGate((ref LbugConnection h) => Native.ConnectionSetMaxNumThreadForExec(ref h, numThreads));
        if (state != LbugState.Success)
        {
            throw new LadybugException("Failed to set the maximum execution threads.");
        }
    }

    /// <summary>Returns the maximum number of threads the engine may use to execute a query.</summary>
    public ulong GetMaxThreadsForExec()
    {
        return WithGate((ref LbugConnection h) =>
        {
            LbugState state = Native.ConnectionGetMaxNumThreadForExec(ref h, out ulong value);
            if (state != LbugState.Success)
            {
                throw new LadybugException("Failed to read the maximum execution threads.");
            }

            return value;
        });
    }

    /// <summary>
    /// Executes a (possibly multi-statement) Cypher query and returns every result set, walking the
    /// engine's result chain so no statement's output is truncated. Each returned
    /// <see cref="QueryResult"/> is the caller's to dispose.
    /// </summary>
    public IReadOnlyList<QueryResult> QueryAll(string cypher)
    {
        QueryResult first = Query(cypher);
        var results = new List<QueryResult> { first };
        try
        {
            QueryResult current = first;
            while (current.HasNextQueryResult())
            {
                current = current.GetNextQueryResult();
                results.Add(current);
            }

            return results;
        }
        catch
        {
            foreach (QueryResult r in results)
            {
                r.Dispose();
            }

            throw;
        }
    }
}
