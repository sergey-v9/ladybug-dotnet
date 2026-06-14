using System.Linq;
using BenchmarkDotNet.Attributes;
using LadybugDB;

namespace LadybugDB.Benchmarks;

/// <summary>
/// Latency benchmarks over an in-memory database. Names here must match the names used by the
/// baseline JSON consumed by the <c>--ci-gate</c> path (see <see cref="CiGate"/>).
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private Database _db = null!;
    private Connection _conn = null!;
    private PreparedStatement _prepared = null!;

    [GlobalSetup]
    public void Setup()
    {
        _db = new Database(":memory:");
        _conn = new Connection(_db);
        _conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
        for (int i = 0; i < 1000; i++)
        {
            using PreparedStatement ins = _conn.Prepare("CREATE (:Person {name: $n, age: $a})");
            ins.Bind("n", "p" + i).Bind("a", (long)i);
            ins.Execute().Dispose();
        }

        _prepared = _conn.Prepare("MATCH (p:Person) WHERE p.age >= $a RETURN p.name");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _prepared.Dispose();
        _conn.Dispose();
        _db.Dispose();
    }

    [Benchmark]
    public int Query()
    {
        using QueryResult r = _conn.Query("MATCH (p:Person) RETURN p.name, p.age");
        return r.Rows().Count();
    }

    [Benchmark]
    public int Prepare()
    {
        using PreparedStatement p = _conn.Prepare("MATCH (p:Person) WHERE p.age >= $a RETURN p.name");
        return p is null ? 0 : 1;
    }

    [Benchmark]
    public int Execute()
    {
        _prepared.Bind("a", 500L);
        using QueryResult r = _conn.Execute(_prepared);
        return r.Rows().Count();
    }
}
