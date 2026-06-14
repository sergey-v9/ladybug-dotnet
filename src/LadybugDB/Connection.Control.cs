using System;
using System.Collections.Generic;
using LadybugDB.Interop;

namespace LadybugDB;

public sealed partial class Connection
{
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
}
