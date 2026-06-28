using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using LadybugDB;

namespace LadybugDB.Benchmarks;

/// <summary>Source-generated row shape for measuring typed mapping.</summary>
[LadybugRow]
public sealed record BenchmarkPersonRow(string Name, long Age);

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
    private PreparedStatement _blobPrepared = null!;
    private PreparedStatement _structPrepared = null!;
    private PreparedStatement _mapPrepared = null!;
    private PreparedStatement _listPrepared = null!;
    private byte[] _blob = null!;
    private Dictionary<string, object?> _struct = null!;
    private Dictionary<object, object?> _map = null!;
    private List<float> _vector = null!;

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
        _blobPrepared = _conn.Prepare("RETURN CAST($b AS BLOB)");
        _structPrepared = _conn.Prepare("RETURN $s");
        _mapPrepared = _conn.Prepare("RETURN $m");
        _listPrepared = _conn.Prepare("RETURN $v");
        _blob = new byte[512];
        for (int i = 0; i < _blob.Length; i++)
        {
            _blob[i] = (byte)i;
        }

        _struct = new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["age"] = 42L,
            ["score"] = 12.5d,
            ["active"] = true,
            ["city"] = "Paris",
            ["role"] = "admin",
            ["label"] = "cafe",
            ["notes"] = "ready"
        };

        _map = new Dictionary<object, object?>
        {
            ["name"] = "Alice",
            ["age"] = 42L,
            ["score"] = 12.5d,
            ["active"] = true,
            ["city"] = "Paris",
            ["role"] = "admin",
            ["label"] = "cafe",
            ["notes"] = "ready"
        };

        _vector = new List<float>(128);
        for (int i = 0; i < 128; i++)
        {
            _vector.Add(i / 128f);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _listPrepared.Dispose();
        _mapPrepared.Dispose();
        _structPrepared.Dispose();
        _blobPrepared.Dispose();
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
    public int MapRows()
    {
        using QueryResult r = _conn.Query("MATCH (p:Person) RETURN p.name AS Name, p.age AS Age");
        return r.Map<BenchmarkPersonRow>().Count;
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

    [Benchmark]
    public int BindBlob()
    {
        _blobPrepared.Bind("b", _blob);
        return _blob.Length;
    }

    [Benchmark]
    public int BindStruct()
    {
        _structPrepared.Bind("s", _struct);
        return _struct.Count;
    }

    [Benchmark]
    public int BindMap()
    {
        _mapPrepared.BindMap("m", _map);
        return _map.Count;
    }

    [Benchmark]
    public int BindList()
    {
        _listPrepared.Bind("v", _vector);
        return _vector.Count;
    }
}
