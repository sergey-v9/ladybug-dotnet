using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LadybugDB;

namespace LadybugDB.Benchmarks;

/// <summary>
/// Differential smoke runner: drives the public surface over an in-memory database along multiple
/// equivalent paths and asserts they agree. A divergence (e.g. <c>Query</c> + <c>Rows()</c> vs
/// <c>Execute</c> of the same Cypher) indicates a surface regression. Native-dependent; the caller
/// must guarantee the engine is loadable.
/// </summary>
public static class DifferentialRunner
{
    /// <summary>Runs all checks, writing one line per check. Returns true when all agree.</summary>
    public static bool Run(TextWriter log)
    {
        var checks = new List<(string Name, Func<bool> Check)>
        {
            ("scalar_query_vs_execute", ScalarQueryVsExecute),
            ("rows_match_columncount", RowsMatchColumnCount),
            ("ordered_read_is_stable", OrderedReadIsStable),
        };

        bool allOk = true;
        foreach ((string name, Func<bool> check) in checks)
        {
            bool ok;
            try { ok = check(); }
            catch (Exception ex) { ok = false; log.WriteLine($"ERROR {name}: {ex.Message}"); }
            if (!ok) { allOk = false; }
            log.WriteLine($"{(ok ? "OK" : "MISMATCH")} {name}");
        }

        return allOk;
    }

    private static bool ScalarQueryVsExecute()
    {
        using var db = new Database(":memory:");
        using var conn = new Connection(db);

        object? viaQuery;
        using (QueryResult r = conn.Query("RETURN 1 + 1")) { viaQuery = r.Rows().Single()[0]; }

        object? viaExecute;
        using (PreparedStatement p = conn.Prepare("RETURN 1 + 1"))
        using (QueryResult r = conn.Execute(p)) { viaExecute = r.Rows().Single()[0]; }

        return Equals(viaQuery, viaExecute) && Equals(viaQuery, 2L);
    }

    private static bool RowsMatchColumnCount()
    {
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        using QueryResult r = conn.Query("RETURN 1 AS a, 'x' AS b, true AS c");
        object?[] row = r.Rows().Single();
        return row.Length == (int)r.ColumnCount && r.ColumnCount == 3UL;
    }

    private static bool OrderedReadIsStable()
    {
        using var db = new Database(":memory:");
        using var conn = new Connection(db);
        conn.Query("CREATE NODE TABLE N(id INT64, PRIMARY KEY(id))").Dispose();
        for (int i = 0; i < 5; i++) { conn.Query($"CREATE (:N {{id: {i}}})").Dispose(); }

        long[] first;
        using (QueryResult r = conn.Query("MATCH (n:N) RETURN n.id ORDER BY n.id"))
        { first = r.Rows().Select(x => (long)x[0]!).ToArray(); }

        long[] second;
        using (QueryResult r = conn.Query("MATCH (n:N) RETURN n.id ORDER BY n.id"))
        { second = r.Rows().Select(x => (long)x[0]!).ToArray(); }

        return first.SequenceEqual(second) && first.SequenceEqual(new long[] { 0, 1, 2, 3, 4 });
    }
}
