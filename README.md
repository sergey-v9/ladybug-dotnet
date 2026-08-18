# LadybugDB for .NET

Official C# binding for the [Ladybug](https://github.com/ladybugdb/ladybug) embedded graph database.
It wraps the native Ladybug C API via P/Invoke and ships prebuilt native libraries for supported
platforms, so you can run Cypher queries against an embedded graph database directly from .NET.

> Current package family: `0.19.1`, built against the Ladybug `v0.19.1` engine.

## Target frameworks

- `net10.0` (primary, AOT/trim friendly, source-generated `LibraryImport`)
- `netstandard2.0` (broad reach, including .NET Framework)

## Packages

| Package | Purpose | Extra dependencies |
|---|---|---|
| `LadybugDB` | Managed binding: `Database`/`Connection`/`QueryResult`/`Value`/`PreparedStatement`, async, engine-extension loading, the POCO source generator, and lightweight OpenTelemetry. | none |
| `LadybugDB.Native` (+ `LadybugDB.Native.<rid>`) | Prebuilt native engine, one package per RID plus an all-platform meta-package. | — |
| `LadybugDB.Extensions` | ASP.NET/host integration: DI registration, health check, resilience (timeout/retry/circuit-breaker), `IAsyncEnumerable` streaming, typed row access, and result export (JSON/CSV/`DataTable`). | `Microsoft.Extensions.*` |
| `LadybugDB.Arrow` | Apache Arrow interop: export a `QueryResult` to `RecordBatch`es and ingest `RecordBatch`es as tables. | `Apache.Arrow` |

`LadybugDB.Extensions` and `LadybugDB.Arrow` are optional and isolated, so the core package and its
consumers stay dependency-free and AOT-friendly.

## Installation

Reference the managed package **plus** a native package. For an app that should run on any supported
platform, add the native meta-package (it pulls in every per-platform native package):

```bash
dotnet add package LadybugDB
dotnet add package LadybugDB.Native
```

For a slim, single-platform app, reference just the native package for that platform instead:

```bash
dotnet add package LadybugDB
dotnet add package LadybugDB.Native.win-x64
```

Available native packages: `LadybugDB.Native.win-x64`, `LadybugDB.Native.linux-x64`,
`LadybugDB.Native.linux-arm64`, `LadybugDB.Native.osx-x64`, `LadybugDB.Native.osx-arm64`. The
`LadybugDB.Native` meta-package depends on all of them.

## Quick start

```csharp
using LadybugDB;

using var db = new Database("./demo.db");   // empty path => in-memory
using var conn = new Connection(db);

conn.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
conn.Query("CREATE (:Person {name: 'Alice', age: 30})").Dispose();

using var result = conn.Query("MATCH (p:Person) RETURN p.name, p.age");
foreach (var row in result.Rows())
{
    Console.WriteLine($"{row[0]} is {row[1]} years old");
}
```

Runnable samples live in [`examples/`](examples). Below is a tour of the capabilities beyond the basics.

## Connection control & result metadata

```csharp
conn.SetQueryTimeout(TimeSpan.FromSeconds(5));   // abort long queries
conn.SetMaxThreadsForExec(4);
// conn.Interrupt();   // cancel the in-flight query from another thread

using var r = conn.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age");
foreach (var col in r.Columns)
    Console.WriteLine($"{col.Name}: {col.Type}");      // typed logical types
Console.WriteLine($"compiled in {r.Summary.CompilingTimeMs} ms, ran in {r.Summary.ExecutionTimeMs} ms");

// Multi-statement queries return every result set (no truncation):
foreach (var rs in conn.QueryAll("RETURN 1; RETURN 2;"))
    using (rs) { /* ... */ }
```

`QueryResult` also exposes `ResetIterator()`, `HasNextQueryResult()` / `GetNextQueryResult()`.

## Async

`LadybugDB` offers a first-class async surface (the synchronous engine call is offloaded, honoring the
connection's internal serialization), with `CancellationToken` wired to the engine interrupt:

```csharp
using var result = await conn.QueryAsync("MATCH (p:Person) RETURN p.name", cancellationToken);

await foreach (var tuple in conn.StreamAsync("MATCH (p:Person) RETURN p.name", cancellationToken))
    using (tuple)
        Console.WriteLine(tuple.GetValue(0).GetValue());
```

Also: `QueryAllAsync`, `PrepareAsync`, `ExecuteAsync`. A single `Connection` serializes operations, so
use one connection per concurrent operation (or the connection pool guidance in `LadybugDB.Extensions`).

## Parameter binding & types

`PreparedStatement.Bind(...)` covers all scalar types plus `decimal`/`LadybugDecimal`, `BigInteger`
(INT128), `byte[]` (BLOB, top-level), `IReadOnlyDictionary<string,object?>` (STRUCT), and `BindMap`
(MAP). DECIMAL reads return a real `decimal` when it fits and a lossless `LadybugDecimal` otherwise —
never a bare string.

### Bulk: prepare once, bind many

For a hot loop that runs the **same** Cypher with different parameters (bulk insert/update, by-key
delete, scalar-per-key reads), `Connection.ExecuteMany` prepares the statement **once** and re-binds
each parameter set on it — so the query is planned a single time and the pooled-bind fast path runs
on every iteration. Each `QueryResult` is disposed for you. An empty sequence is a no-op.

```csharp
// Write path: one prepare, N executes; results disposed internally.
conn.ExecuteMany(
    "CREATE (:Person {name: $name, age: $age})",
    people.Select(p => new Dictionary<string, object?> { ["name"] = p.Name, ["age"] = p.Age }));

// Read path: project one value per parameter set, in input order.
IReadOnlyList<long?> ages = conn.ExecuteMany(
    "MATCH (p:Person) WHERE p.name = $name RETURN p.age",
    names.Select(n => new Dictionary<string, object?> { ["name"] = n }),
    r => (long?)r.Rows().FirstOrDefault()?[0]);

// Async equivalents honor a CancellationToken: ExecuteManyAsync(...) / ExecuteManyAsync(..., selector, ct).
```

## Engine extensions

Ladybug extensions (e.g. `json`, `fts`, `vector`, `httpfs`) install and load through the query engine.
The binding adds convenience helpers and — importantly — loads the native engine with **global symbol
visibility** on Linux/macOS so a dynamically loaded extension resolves the engine's symbols:

```csharp
conn.InstallExtension("json");   // INSTALL json
conn.LoadExtension("json");      // LOAD EXTENSION json
```

## Apache Arrow (`LadybugDB.Arrow`)

```csharp
using LadybugDB.Arrow;

using var r = conn.Query("MATCH (p:Person) RETURN p.name, p.age");
foreach (RecordBatch batch in r.ReadBatches())   // zero-copy export
    Console.WriteLine($"{batch.Length} rows");

conn.CreateArrowTable("People", someRecordBatch); // ingest a RecordBatch as a node table
```

## Dependency injection, health, resilience, export (`LadybugDB.Extensions`)

```csharp
services.AddSingleton(_ => new Connection(database));
services.AddSingleton<ILadybugExecutor>(sp => new LadybugConnectionExecutor(sp.GetRequiredService<Connection>()));
services.AddLadybug(o => { o.DatabasePath = "./app.db"; o.MaxThreads = 4; });
services.AddLadybugResilience();      // timeout + retry + circuit breaker (no Polly)
services.AddLadybugHealthCheck();     // ASP.NET Core health check

// Stream typed rows:
await foreach (var row in executor.StreamAsync("MATCH (p:Person) RETURN p.name AS name"))
    Console.WriteLine(row.Get<string>("name"));

// Export a QueryResult:
string json = result.ToJson();
string csv  = result.ToCsv();
DataTable table = result.ToDataTable();
```

## OpenTelemetry

The core binding emits an `ActivitySource` and `Meter` named **`LadybugDB`** (instruments
`db.query.count`, `db.query.errors`, `db.query.duration.ms`). They are zero-cost when no listener is
attached; subscribe via your OpenTelemetry pipeline to observe query activity.

## POCO mapping (source-generated)

Annotate a record/class with `[LadybugRow]` and map result rows with a reflection-free, AOT-safe
generated mapper:

```csharp
using LadybugDB.Mapping;

[LadybugRow]
public sealed record Person(string Name, long Age);

foreach (Person p in result.Map<Person>())   // generated; also MapAsync<Person>()
    Console.WriteLine($"{p.Name} ({p.Age})");
```

## Inspecting pushed-down SQL

`Connection.GetPushedSql(cypher)` prepares and optimizes a query, then returns the SQL emitted by a
pushdown-capable table function. It returns `null` when the optimized plan contains no pushed-down SQL
and throws `LadybugQueryException` with the native diagnostic when the query cannot be analyzed.

## Building / testing

```powershell
dotnet build LadybugDB.slnx -c Release
dotnet test  LadybugDB.slnx -c Release
```

The binding needs the native Ladybug shared library at runtime (`lbug_shared.dll` /
`liblbug.so` / `liblbug.dylib`). When the native library is not available, native round-trip tests skip
(the ABI/struct-layout guards still run). Set `LADYBUG_REQUIRE_NATIVE=1` to require a native load.

## How the packages are built

This repo does not contain the engine source; the native libraries come from the upstream
[Ladybug](https://github.com/ladybugdb/ladybug) engine. Packaging is driven by a Cake Frosting build
project under [`cake/`](cake) (run it with the `build.ps1` / `build.sh` bootstrap):

```bash
./build.sh --target Test     # build + stage the host native + run the suite
./build.sh --target Pack     # build the full package family into ./artifacts
```

All packages in the family share one version. The first three numeric segments track the upstream engine
release, and the optional fourth segment is the .NET package revision for binding-only releases. For
example, package `0.19.1` wraps the Ladybug `v0.19.1` engine; a future binding-only fix over the same
engine would be `0.19.1.1`. Prerelease suffixes are reserved for preview builds.

The package version is defined once in `version.txt` at the repo root. Override it with
`--package-version <v>` (the release workflow uses the git tag). The engine release defaults to the first
three numeric package-version segments; override with `--engine-version` when needed.

- **`Pack`** stages the prebuilt `liblbug-*` assets for every shipped RID (downloaded from an upstream
  `LadybugDB/ladybug` GitHub Release, pinned to the engine version derived from `version.txt`),
  packs `LadybugDB`, `LadybugDB.Extensions`, `LadybugDB.Arrow`, one `LadybugDB.Native.<rid>` package per
  RID, and the `LadybugDB.Native` meta-package, then verifies every package's contents.
- **CI / release** (`.github/workflows/`) invoke the same pipeline; the release workflow gates on the
  linux-x64 suite against the real engine and publishes all packages to nuget.org via OIDC.
- **Local development** is easiest when this repo is checked out as the `tools/csharp_api` submodule
  inside the Ladybug monorepo: `scripts/build-native-and-test.ps1` builds `lbug_shared` from the parent
  engine tree, stages it into `lib/runtimes/win-x64/native/`, and runs the suite. With a native already
  staged there, `--target Test` reuses it instead of downloading.

See [`MAINTAINING.md`](MAINTAINING.md) for the maintainer guide (versioning, ABI updates, release flow).
