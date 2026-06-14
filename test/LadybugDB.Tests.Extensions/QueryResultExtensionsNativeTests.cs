using System.Collections.Generic;
using System.Data;
using LadybugDB;
using LadybugDB.Extensions;
using Xunit;

namespace LadybugDB.Tests.Extensions;

public sealed class QueryResultExtensionsNativeTests
{
    private static bool NativeAvailable { get; } = Probe();

    [SkippableFact]
    public void ToDictionaries_over_real_query_result()
    {
        Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);
        using QueryResult result = conn.Query("RETURN 1 AS one, 'hi' AS greeting");

        IReadOnlyList<IReadOnlyDictionary<string, object?>> dicts = result.ToDictionaries();

        Assert.Single(dicts);
        Assert.Equal(1L, dicts[0]["one"]);
        Assert.Equal("hi", dicts[0]["greeting"]);
    }

    [SkippableFact]
    public void Scalar_and_datatable_over_real_query_result()
    {
        Skip.IfNot(NativeAvailable, "Native Ladybug library is not available.");

        using var db = new Database(string.Empty);
        using var conn = new Connection(db);
        using QueryResult result = conn.Query("RETURN 7 AS n");

        // Two extension calls on the SAME result: each must materialize the full result, which only
        // works because QueryResultData rewinds the forward-only engine iterator before reading.
        Assert.Equal(7, result.Scalar<int>());
        DataTable table = result.ToDataTable();
        Assert.Equal(1, table.Rows.Count);
        Assert.Equal(7, result.Scalar<int>());
    }

    private static bool Probe()
    {
        try
        {
            _ = LadybugVersion.StorageVersion;
            return true;
        }
        catch (System.DllNotFoundException) { return false; }
        catch (System.TypeInitializationException) { return false; }
        catch (System.EntryPointNotFoundException) { return false; }
    }
}
