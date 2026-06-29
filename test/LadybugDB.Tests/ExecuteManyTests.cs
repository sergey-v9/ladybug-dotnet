using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LadybugDB;
using Xunit;

namespace LadybugDB.Tests;

/// <summary>
/// Prepare-once / bind-many <see cref="Connection.ExecuteMany(string, IEnumerable{IReadOnlyDictionary{string, object?}})"/>
/// surface (Graphiti wish #4). Native-gated round-trips plus the always-on empty-sequence contract.
/// </summary>
public sealed class ExecuteManyTests
{
    [SkippableFact]
    public void ExecuteMany_BulkInsert_PersistsEveryRow()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();

            const int n = 50;
            IEnumerable<IReadOnlyDictionary<string, object?>> rows = Enumerable.Range(0, n)
                .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["name"] = "P" + i.ToString("D3"),
                    ["age"] = (long)i,
                });

            conn.ExecuteMany("CREATE (:Person {name: $name, age: $age})", rows);

            using QueryResult count = conn.Query("MATCH (p:Person) RETURN count(p)");
            Assert.Equal((long)n, count.Rows().Single()[0]);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void ExecuteMany_Projection_ReturnsOrderedScalars()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
            conn.ExecuteMany(
                "CREATE (:Person {name: $name, age: $age})",
                new[] { ("Alice", 30L), ("Bob", 42L), ("Carol", 25L) }
                    .Select(t => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                    {
                        ["name"] = t.Item1,
                        ["age"] = t.Item2,
                    }));

            // Rank-per-key shape: one scalar projected per parameter set, in input order.
            string[] lookup = { "Carol", "Alice", "Bob", "Missing" };
            IReadOnlyList<long?> ages = conn.ExecuteMany(
                "MATCH (p:Person) WHERE p.name = $name RETURN p.age",
                lookup.Select(name => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["name"] = name,
                }),
                result =>
                {
                    object?[]? row = result.Rows().FirstOrDefault();
                    return (long?)(row?[0]);
                });

            Assert.Equal(new long?[] { 25L, 30L, 42L, null }, ages);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void ExecuteMany_EmptySequence_IsNoOp()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            conn.Query("CREATE NODE TABLE Person(name STRING, PRIMARY KEY(name))").Dispose();

            // Void overload: no rows inserted.
            conn.ExecuteMany(
                "CREATE (:Person {name: $name})",
                Enumerable.Empty<IReadOnlyDictionary<string, object?>>());
            using (QueryResult count = conn.Query("MATCH (p:Person) RETURN count(p)"))
            {
                Assert.Equal(0L, count.Rows().Single()[0]);
            }

            // Projecting overload: empty input -> empty list (and selector never invoked).
            IReadOnlyList<long> projected = conn.ExecuteMany<long>(
                "MATCH (p:Person) WHERE p.name = $name RETURN p.name",
                Enumerable.Empty<IReadOnlyDictionary<string, object?>>(),
                _ => throw new InvalidOperationException("selector must not run on an empty sequence"));
            Assert.Empty(projected);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public async Task ExecuteManyAsync_RoundTrip_WritesThenProjects()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            (await conn.QueryAsync("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))")).Dispose();

            await conn.ExecuteManyAsync(
                "CREATE (:Person {name: $name, age: $age})",
                new[] { ("Alice", 30L), ("Bob", 42L) }
                    .Select(t => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                    {
                        ["name"] = t.Item1,
                        ["age"] = t.Item2,
                    }));

            IReadOnlyList<long?> ages = await conn.ExecuteManyAsync(
                "MATCH (p:Person) WHERE p.name = $name RETURN p.age",
                new[] { "Bob", "Alice" }.Select(name => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["name"] = name,
                }),
                result => (long?)(result.Rows().FirstOrDefault()?[0]));

            Assert.Equal(new long?[] { 42L, 30L }, ages);
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }

    [SkippableFact]
    public void ExecuteMany_NullArguments_Throw()
    {
        Skip.IfNot(TestEnvironment.NativeAvailable, "Native Ladybug library is not available.");

        string dbPath = TestEnvironment.NewTempDbPath();
        try
        {
            using var db = new Database(dbPath);
            using var conn = new Connection(db);

            IEnumerable<IReadOnlyDictionary<string, object?>> empty =
                Enumerable.Empty<IReadOnlyDictionary<string, object?>>();

            Assert.Throws<ArgumentNullException>(() => conn.ExecuteMany(null!, empty));
            Assert.Throws<ArgumentNullException>(() => conn.ExecuteMany("RETURN 1", null!));
            Assert.Throws<ArgumentNullException>(() => conn.ExecuteMany<int>("RETURN 1", empty, null!));
        }
        finally
        {
            TestEnvironment.TryDelete(dbPath);
        }
    }
}
