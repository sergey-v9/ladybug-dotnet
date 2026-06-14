# WS-L: Docs + Examples Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Update `README.md` and `MAINTAINING.md` to document the new packages (`LadybugDB.Extensions`, `LadybugDB.Arrow`), the async API, engine-extension usage plus the global-symbol-visibility loader behavior, OpenTelemetry signals, POCO mapping, and the `0.17.2` native bump — and add five runnable examples (`examples/async`, `examples/di-extensions`, `examples/arrow`, `examples/engine-extensions`, `examples/poco-mapping`) whose code matches the pinned §4 public-API contracts exactly.

**Architecture:** This is the last (Phase 4) workstream; it owns documentation and example consumer apps only — it adds no library code. Each new example is a standalone `Microsoft.NET.Sdk` exe targeting `net10.0`, referencing the published-style package versions through `$(LadybugVersion)` in `examples/Directory.Build.props` (bumped to `0.17.2`), exactly as the existing four database examples do. New examples that depend on other workstreams' surface (`LadybugDB.Extensions`, `LadybugDB.Arrow`, async partials, `[LadybugRow]`/`Map<T>()`, `InstallExtension`/`LoadExtension`) call only the §4-pinned signatures.

**Tech Stack:** .NET 10 console apps, NuGet `PackageReference`, Markdown docs, `Examples.slnx` solution, MSBuild `Directory.Build.props`. Examples reference published packages, so they are validated by `dotnet build examples/Examples.slnx` against an artifacts feed (or a local `nuget.config`), not by the repo's main `dotnet test`.

---

## Files

**Modify**
- `W:\code\ladybug\tools\csharp_api\README.md` — document new packages, async, engine extensions + loader behavior, OTel, POCO mapping, version bump to 0.17.2.
- `W:\code\ladybug\tools\csharp_api\MAINTAINING.md` — package family list (add Extensions + Arrow), versioning bump, examples section (add five new examples).
- `W:\code\ladybug\tools\csharp_api\examples\Directory.Build.props` — bump `LadybugVersion` default `0.17.0.1` → `0.17.2`.
- `W:\code\ladybug\tools\csharp_api\examples\README.md` — add the five new examples to the tables.
- `W:\code\ladybug\tools\csharp_api\examples\Examples.slnx` — add the five new projects.

**Create — examples/async**
- `W:\code\ladybug\tools\csharp_api\examples\async\Async.csproj`
- `W:\code\ladybug\tools\csharp_api\examples\async\Program.cs`
- `W:\code\ladybug\tools\csharp_api\examples\async\README.md`

**Create — examples/di-extensions**
- `W:\code\ladybug\tools\csharp_api\examples\di-extensions\DiExtensions.csproj`
- `W:\code\ladybug\tools\csharp_api\examples\di-extensions\Program.cs`
- `W:\code\ladybug\tools\csharp_api\examples\di-extensions\README.md`

**Create — examples/arrow**
- `W:\code\ladybug\tools\csharp_api\examples\arrow\Arrow.csproj`
- `W:\code\ladybug\tools\csharp_api\examples\arrow\Program.cs`
- `W:\code\ladybug\tools\csharp_api\examples\arrow\README.md`

**Create — examples/engine-extensions**
- `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\EngineExtensions.csproj`
- `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\Program.cs`
- `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\README.md`

**Create — examples/poco-mapping**
- `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\PocoMapping.csproj`
- `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\Program.cs`
- `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\README.md`

**"Test" strategy for this workstream.** Examples reference *published* packages, so they cannot
be added to `LadybugDB.slnx` / `dotnet test`. The verification gate for each example is a
**restore + build** of its `.csproj` against a NuGet feed that has the `0.17.2` family (the local
`./artifacts` feed produced by `cake Pack`, or nuget.org once published). Because Phase 4 runs after
WS-K bumps the family to `0.17.2` and the other workstreams ship their public API, the gate command
is `dotnet build <example>.csproj` with a `nuget.config` pointing at the artifacts feed. Where the
`0.17.2` packages are not yet restorable on the build host, the fallback gate is **C# compile of the
`Program.cs` against the merged source tree** via a throwaway project reference (documented per-task),
so the example code is proven to match the pinned API before the packages exist. Each task states the
exact command and the expected result for both situations.

---

## Pre-flight (read once before starting)

- [ ] Read the pinned public-API contract: `W:\code\ladybug\tools\csharp_api\docs\parity-2026-06\03-high-level-plan.md` §3 (interop) and §4 (public API). Example code MUST use these exact names/signatures.
- [ ] Read the design spec decisions: `W:\code\ladybug\tools\csharp_api\docs\superpowers\specs\2026-06-14-csharp-parity-extensions-design.md` §5 (async honesty, DECIMAL, POCO source-gen, OTel, Arrow raw/friendly split, engine extensions + global symbol visibility).
- [ ] Confirm WS-K has bumped `version.txt` to `0.17.2` and the new `.csproj`s for `LadybugDB.Extensions` / `LadybugDB.Arrow` exist and pack. If `version.txt` still reads `0.17.0.1`, STOP — this plan runs last, after WS-K.
- [ ] Decide the build host situation: run `dotnet nuget locals all --list` and confirm whether `./artifacts` (from `cake Pack`) has `LadybugDB.Extensions.0.17.2.nupkg` and `LadybugDB.Arrow.0.17.2.nupkg`. This selects the "published feed" vs "source-tree compile" gate per task.

---

## Task 1 — Bump the examples package version to 0.17.2

- [ ] Read `W:\code\ladybug\tools\csharp_api\examples\Directory.Build.props` to confirm the current default is `0.17.0.1`.
- [ ] Edit `W:\code\ladybug\tools\csharp_api\examples\Directory.Build.props`: change the `<LadybugVersion>` default from `0.17.0.1` to `0.17.2`. Final content:

```xml
<Project>

  <PropertyGroup>
    <!-- Published package version used by the examples. Override with:
         dotnet run -p:LadybugVersion=<version> -->
    <LadybugVersion Condition="'$(LadybugVersion)' == ''">0.17.2</LadybugVersion>
  </PropertyGroup>

</Project>
```

- [ ] Verify the four existing examples still restore against the new version (artifacts feed present):

```powershell
dotnet restore W:\code\ladybug\tools\csharp_api\examples\quickstart\Quickstart.csproj --source W:\code\ladybug\tools\csharp_api\artifacts --source https://api.nuget.org/v3/index.json
```

Expected PASS: restore succeeds and reports `LadybugDB 0.17.2`. If the `0.17.2` packages are not on the feed yet, expected (acceptable) outcome is a restore error naming `LadybugDB 0.17.2` not found — proving the version flowed through; do not proceed to publish-dependent build until WS-K's `Pack` has staged them.
- [ ] Commit:

```
git add examples/Directory.Build.props
git commit -m "docs(examples): pin examples to the 0.17.2 package family"
```

---

## Task 2 — examples/async: ExecuteAsync / StreamAsync / CancellationToken

Pinned contract (§4.3): `Task<QueryResult> QueryAsync(string, CancellationToken=default)`,
`Task<IReadOnlyList<QueryResult>> QueryAllAsync(...)`, `Task<PreparedStatement> PrepareAsync(...)`,
`Task<QueryResult> ExecuteAsync(PreparedStatement, CancellationToken=default)`,
`IAsyncEnumerable<FlatTuple> StreamAsync(string, CancellationToken=default)`. `CancellationToken`
maps to `Interrupt()`.

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\async\Async.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>LadybugDB.Examples.Async</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LadybugDB" Version="$(LadybugVersion)" />
    <!-- All-platform native engine. For a slim single-RID app, drop this and reference one runtime, e.g.:
         <PackageReference Include="LadybugDB.Native.win-x64" Version="$(LadybugVersion)" /> -->
    <PackageReference Include="LadybugDB.Native" Version="$(LadybugVersion)" />
  </ItemGroup>

</Project>
```

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\async\Program.cs` (uses only §4.3 signatures; `Value` materialization via `FlatTuple.GetValue` per `QueryResult.Rows()`):

```csharp
using LadybugDB;

// Async: run queries off the calling thread, stream rows with IAsyncEnumerable, and cancel
// an in-flight query via a CancellationToken (which interrupts the engine).
//
// The async surface is "honest": each call is offloaded onto the connection's serialized
// work over the existing per-connection lock, and a CancellationToken registers Interrupt().

using Database database = new();
using Connection connection = new(database);

// DDL/insert through the async query path. QueryAsync returns a Task<QueryResult>.
(await connection.QueryAsync(
    "CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))")).Dispose();

// QueryAllAsync walks a multi-statement chain and returns every result set.
IReadOnlyList<QueryResult> seeded = await connection.QueryAllAsync(
    "CREATE (:Person {name: 'Alice', age: 30});" +
    "CREATE (:Person {name: 'Bob', age: 42});" +
    "CREATE (:Person {name: 'Carol', age: 25});");
foreach (QueryResult r in seeded)
{
    r.Dispose();
}

Console.WriteLine("== QueryAsync ==");
using (QueryResult result = await connection.QueryAsync(
    "MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY p.name"))
{
    foreach (object?[] row in result.Rows())
    {
        Console.WriteLine($"  {row[0]} ({row[1]})");
    }
}

Console.WriteLine();

// PrepareAsync + ExecuteAsync: compile once, execute off-thread with bound parameters.
Console.WriteLine("== PrepareAsync + ExecuteAsync ==");
using (PreparedStatement select = await connection.PrepareAsync(
    "MATCH (p:Person) WHERE p.age >= $minAge RETURN p.name ORDER BY p.name"))
{
    select.Bind("minAge", 30L);
    using QueryResult result = await connection.ExecuteAsync(select);
    foreach (object?[] row in result.Rows())
    {
        Console.WriteLine($"  {row[0]}");
    }
}

Console.WriteLine();

// StreamAsync: an IAsyncEnumerable<FlatTuple>. Each tuple shares the engine's reusable buffer,
// so read the values you need before the loop advances.
Console.WriteLine("== StreamAsync ==");
await foreach (FlatTuple tuple in connection.StreamAsync(
    "MATCH (p:Person) RETURN p.name, p.age ORDER BY p.name"))
{
    using Value name = tuple.GetValue(0);
    using Value age = tuple.GetValue(1);
    Console.WriteLine($"  {name.GetValue()} ({age.GetValue()})");
}

Console.WriteLine();

// CancellationToken -> Interrupt(): cancelling the token interrupts the running query.
Console.WriteLine("== CancellationToken cancels a query ==");
using var cts = new CancellationTokenSource();
cts.Cancel();
try
{
    using QueryResult _ = await connection.QueryAsync(
        "UNWIND range(1, 1000000000) AS x RETURN count(x)", cts.Token);
    Console.WriteLine("  (completed before cancellation took effect)");
}
catch (OperationCanceledException)
{
    Console.WriteLine("  query was cancelled");
}
```

- [ ] Compile-gate (published feed available):

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\async\Async.csproj
```

Expected PASS: build succeeds. Expected (acceptable) FAIL before WS-K Pack: `NU1102` ("Unable to find package LadybugDB version 0.17.2"). A compile error referencing a missing `QueryAsync`/`StreamAsync`/`ExecuteAsync`/`PrepareAsync`/`QueryAllAsync` member means the example diverges from §4.3 — fix the example, not the contract.
- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\async\README.md`:

```markdown
# async

Runs queries asynchronously, streams rows, and cancels an in-flight query.

It demonstrates:

- `Connection.QueryAsync(...)` / `QueryAllAsync(...)` — `Task`-returning query execution; `QueryAllAsync`
  returns every result set of a multi-statement query.
- `Connection.PrepareAsync(...)` + `Connection.ExecuteAsync(stmt, ...)` — prepare and execute off-thread.
- `Connection.StreamAsync(...)` — an `IAsyncEnumerable<FlatTuple>` for row-by-row streaming.
- A `CancellationToken` that interrupts the running query (it calls `Connection.Interrupt()`), surfacing
  as `OperationCanceledException`.

The async surface is honest: calls are offloaded over the connection's internal serialization, not
wrapped in `Task.FromResult`.

## Run

```bash
dotnet run
```

Expected output:

```
== QueryAsync ==
  Alice (30)
  Bob (42)
  Carol (25)

== PrepareAsync + ExecuteAsync ==
  Alice
  Bob

== StreamAsync ==
  Alice (30)
  Bob (42)
  Carol (25)

== CancellationToken cancels a query ==
  query was cancelled
```
```

- [ ] Commit:

```
git add examples/async
git commit -m "docs(examples): add async example (ExecuteAsync/StreamAsync/CancellationToken)"
```

---

## Task 3 — examples/di-extensions: ServiceCollection + health check + resilience + export helpers

Pinned contract (§4.8): `AddLadybug(IServiceCollection, Action<LadybugOptions>)`,
`AddLadybugHealthCheck(IServiceCollection, string name="ladybug")`,
`AddLadybugResilience(IServiceCollection, Action<LadybugResilienceOptions>?)`;
`IRowAccessor { T Get<T>(int); T Get<T>(string); T GetOrDefault<T>(string, T) }`;
`QueryResultExtensions`: `ToDictionaries`, `Select<T>(Func<IRowAccessor,T>)`, `Scalar<T>(int column=0)`,
`ToJson`, `ToCsv(char separator=',')`, `ToDataTable`. `LadybugOptions` is configured via its
`Action<LadybugOptions>` delegate (WS-G owns its members — the example uses the DI registration and
the resolved `Connection`/`Database`, plus the export helpers, all of which are pinned).

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\di-extensions\DiExtensions.csproj` (adds `LadybugDB.Extensions` plus the DI/health-check host abstractions the consumer needs):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>LadybugDB.Examples.DiExtensions</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LadybugDB" Version="$(LadybugVersion)" />
    <PackageReference Include="LadybugDB.Extensions" Version="$(LadybugVersion)" />
    <!-- All-platform native engine. For a slim single-RID app, drop this and reference one runtime, e.g.:
         <PackageReference Include="LadybugDB.Native.win-x64" Version="$(LadybugVersion)" /> -->
    <PackageReference Include="LadybugDB.Native" Version="$(LadybugVersion)" />
    <!-- DI host + health-check abstractions the consumer wires up. -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="9.0.0" />
  </ItemGroup>

</Project>
```

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\di-extensions\Program.cs` (uses only §4.8-pinned registration + export helpers; resolves a `Connection` from DI):

```csharp
using LadybugDB;
using LadybugDB.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// DI + Extensions: register LadybugDB in a ServiceCollection with a health check and the
// resilience executor, resolve a Connection, then use the export helpers (ToDictionaries,
// Select, Scalar, ToJson, ToCsv, ToDataTable) over a QueryResult.

var services = new ServiceCollection();

// AddLadybug registers a Database + Connection from LadybugOptions (configured via the delegate).
services.AddLadybug(options =>
{
    // In-memory database for the sample. LadybugOptions members are owned by LadybugDB.Extensions.
    options.DatabasePath = string.Empty;
});

// A health check named "ladybug" that runs a trivial probe query.
services.AddLadybugHealthCheck("ladybug");

// The resilience executor (timeout / retry / circuit-breaker; no Polly). Defaults are fine here.
services.AddLadybugResilience();

using ServiceProvider provider = services.BuildServiceProvider();

// Resolve a Connection from the container and seed some data.
var connection = provider.GetRequiredService<Connection>();
connection.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
connection.Query("CREATE (:Person {name: 'Alice', age: 30})").Dispose();
connection.Query("CREATE (:Person {name: 'Bob', age: 42})").Dispose();

// Run the registered health check.
var healthService = provider.GetRequiredService<HealthCheckService>();
HealthReport report = await healthService.CheckHealthAsync();
Console.WriteLine($"Health: {report.Status}");
Console.WriteLine();

// Export helpers over a QueryResult.
Console.WriteLine("== ToDictionaries ==");
using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    foreach (IReadOnlyDictionary<string, object?> rowDict in result.ToDictionaries())
    {
        Console.WriteLine($"  name={rowDict["name"]} age={rowDict["age"]}");
    }
}

Console.WriteLine();
Console.WriteLine("== Select<T> with IRowAccessor ==");
using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name, p.age ORDER BY p.name"))
{
    foreach (string line in result.Select(row => $"{row.Get<string>(0)} is {row.Get<long>("p.age")}"))
    {
        Console.WriteLine($"  {line}");
    }
}

Console.WriteLine();
Console.WriteLine("== Scalar / ToJson / ToCsv / ToDataTable ==");
using (QueryResult result = connection.Query("MATCH (p:Person) RETURN count(p) AS n"))
{
    Console.WriteLine($"  Scalar<long>() = {result.Scalar<long>()}");
}

using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    Console.WriteLine($"  ToJson()  = {result.ToJson()}");
}

using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    Console.WriteLine("  ToCsv():");
    foreach (string csvLine in result.ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Console.WriteLine($"    {csvLine.TrimEnd('\r')}");
    }
}

using (QueryResult result = connection.Query("MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    System.Data.DataTable table = result.ToDataTable();
    Console.WriteLine($"  ToDataTable(): {table.Rows.Count} rows x {table.Columns.Count} columns");
}
```

- [ ] Compile-gate:

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\di-extensions\DiExtensions.csproj
```

Expected PASS: build succeeds. Acceptable pre-Pack FAIL: `NU1102` for `LadybugDB.Extensions 0.17.2`. A compile error on `AddLadybug`/`AddLadybugHealthCheck`/`AddLadybugResilience`/`ToDictionaries`/`Select`/`Scalar`/`ToJson`/`ToCsv`/`ToDataTable`/`IRowAccessor` means the example diverges from §4.8 — fix the example.
- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\di-extensions\README.md`:

```markdown
# di-extensions

Wires LadybugDB into a `Microsoft.Extensions.DependencyInjection` container and uses the
`LadybugDB.Extensions` ecosystem layer.

It demonstrates:

- `AddLadybug(options => ...)` to register a `Database` + `Connection` from `LadybugOptions`.
- `AddLadybugHealthCheck("ladybug")` and running it through `HealthCheckService`.
- `AddLadybugResilience()` — the built-in timeout/retry/circuit-breaker executor (no Polly).
- Export helpers over a `QueryResult`: `ToDictionaries()`, `Select(row => ...)` with `IRowAccessor`,
  `Scalar<T>()`, `ToJson()`, `ToCsv()`, and `ToDataTable()`.

`LadybugDB.Extensions` is a separate NuGet package layered on top of the managed `LadybugDB` core; it
depends on the `Microsoft.Extensions.*` abstractions and `System.Text.Json`.

## Run

```bash
dotnet run
```

Expected output:

```
Health: Healthy

== ToDictionaries ==
  name=Alice age=30
  name=Bob age=42

== Select<T> with IRowAccessor ==
  Alice is 30
  Bob is 42

== Scalar / ToJson / ToCsv / ToDataTable ==
  Scalar<long>() = 2
  ToJson()  = [{"name":"Alice","age":30},{"name":"Bob","age":42}]
  ToCsv():
    name,age
    Alice,30
    Bob,42
  ToDataTable(): 2 rows x 2 columns
```
```

- [ ] Commit:

```
git add examples/di-extensions
git commit -m "docs(examples): add di-extensions example (DI + health check + resilience + export)"
```

---

## Task 4 — examples/arrow: RecordBatch round-trip

Pinned contract (§4.4 `LadybugDB.Arrow`): `Schema ReadSchema(this QueryResult)`,
`IEnumerable<RecordBatch> ReadBatches(this QueryResult, long chunkSize=1_000_000)`,
`void CreateArrowTable(this Connection, string name, RecordBatch)`. Apache.Arrow lives only in
the `LadybugDB.Arrow` package, never in core.

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\arrow\Arrow.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>LadybugDB.Examples.Arrow</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LadybugDB" Version="$(LadybugVersion)" />
    <PackageReference Include="LadybugDB.Arrow" Version="$(LadybugVersion)" />
    <!-- All-platform native engine. For a slim single-RID app, drop this and reference one runtime, e.g.:
         <PackageReference Include="LadybugDB.Native.win-x64" Version="$(LadybugVersion)" /> -->
    <PackageReference Include="LadybugDB.Native" Version="$(LadybugVersion)" />
  </ItemGroup>

</Project>
```

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\arrow\Program.cs` (uses only §4.4 helpers; builds a `RecordBatch` with the Apache.Arrow builders to feed `CreateArrowTable`, then reads results back as batches):

```csharp
using Apache.Arrow;
using LadybugDB;
using LadybugDB.Arrow;

// Arrow: ingest an Apache.Arrow RecordBatch into LadybugDB, then read query results back out
// as Arrow batches and schema. The friendly Apache.Arrow layer lives in the LadybugDB.Arrow
// package, layered over the engine's raw Arrow C-Data-Interface export in core.

using Database database = new();
using Connection connection = new(database);

// 1) Build a RecordBatch in memory: a "Person" table with name + age columns.
StringArray names = new StringArray.Builder()
    .Append("Alice").Append("Bob").Append("Carol").Build();
Int64Array ages = new Int64Array.Builder()
    .Append(30).Append(42).Append(25).Build();

var schema = new Schema(
    new[]
    {
        new Field("name", StringType.Default, nullable: false),
        new Field("age", Int64Type.Default, nullable: false),
    },
    metadata: null);

using var batch = new RecordBatch(schema, new IArrowArray[] { names, ages }, length: 3);

// 2) Ingest it as an Arrow-backed node table. CreateArrowTable consumes the batch.
connection.CreateArrowTable("Person", batch);
Console.WriteLine("Ingested 3 rows via Arrow RecordBatch.");
Console.WriteLine();

// 3) Read query results back as an Arrow schema + batches (the round-trip).
using QueryResult result = connection.Query(
    "MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name");

Schema outSchema = result.ReadSchema();
Console.WriteLine("== Arrow schema ==");
foreach (Field field in outSchema.FieldsList)
{
    Console.WriteLine($"  {field.Name}: {field.DataType.TypeId}");
}

Console.WriteLine();
Console.WriteLine("== Arrow batches ==");
foreach (RecordBatch outBatch in result.ReadBatches())
{
    var nameCol = (StringArray)outBatch.Column("name");
    var ageCol = (Int64Array)outBatch.Column("age");
    for (int i = 0; i < outBatch.Length; i++)
    {
        Console.WriteLine($"  {nameCol.GetString(i)} ({ageCol.GetValue(i)})");
    }

    outBatch.Dispose();
}
```

- [ ] Compile-gate:

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\arrow\Arrow.csproj
```

Expected PASS: build succeeds. Acceptable pre-Pack FAIL: `NU1102` for `LadybugDB.Arrow 0.17.2`. A compile error on `ReadSchema`/`ReadBatches`/`CreateArrowTable` means the example diverges from §4.4 — fix the example. (If the Apache.Arrow `RecordBatch`/builder surface differs in the package version WS-E picked, adjust the *builder* calls only, never the LadybugDB.Arrow extension signatures.)
- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\arrow\README.md`:

```markdown
# arrow

Round-trips data through Apache Arrow: ingest a `RecordBatch` into LadybugDB, then read query
results back out as Arrow batches.

It demonstrates:

- `Connection.CreateArrowTable(name, batch)` — ingest an Apache.Arrow `RecordBatch` as a node table.
- `QueryResult.ReadSchema()` — the result's Arrow `Schema`.
- `QueryResult.ReadBatches()` — the result as a stream of Arrow `RecordBatch` chunks.

Apache.Arrow types are exposed only by the `LadybugDB.Arrow` package, which layers the friendly
`RecordBatch`/schema API over the engine's raw Arrow C-Data-Interface export in core. The managed
`LadybugDB` core has no Apache.Arrow dependency.

## Run

```bash
dotnet run
```

Expected output:

```
Ingested 3 rows via Arrow RecordBatch.

== Arrow schema ==
  name: String
  age: Int64

== Arrow batches ==
  Alice (30)
  Bob (42)
  Carol (25)
```
```

- [ ] Commit:

```
git add examples/arrow
git commit -m "docs(examples): add arrow example (RecordBatch round-trip)"
```

---

## Task 5 — examples/engine-extensions: INSTALL/LOAD a real extension

Pinned contract (§4.5): `void Connection.InstallExtension(string name)` runs `"INSTALL <name>"`;
`void Connection.LoadExtension(string name)` runs `"LOAD EXTENSION <name>"`. The loader change
(global symbol visibility on Linux/macOS) is what makes a dynamically loaded extension `.so`
resolve engine symbols.

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\EngineExtensions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>LadybugDB.Examples.EngineExtensions</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LadybugDB" Version="$(LadybugVersion)" />
    <!-- All-platform native engine. For a slim single-RID app, drop this and reference one runtime, e.g.:
         <PackageReference Include="LadybugDB.Native.win-x64" Version="$(LadybugVersion)" /> -->
    <PackageReference Include="LadybugDB.Native" Version="$(LadybugVersion)" />
  </ItemGroup>

</Project>
```

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\Program.cs` (uses the `InstallExtension`/`LoadExtension` helpers against the real `json` extension; needs network on first install):

```csharp
using LadybugDB;

// Engine extensions: INSTALL and LOAD a real engine extension, then use it.
//
// INSTALL downloads the extension's shared library; LOAD EXTENSION dynamically loads it into the
// running engine. On Linux/macOS this only works because the binding loads liblbug with global
// symbol visibility (dlopen RTLD_NOW | RTLD_GLOBAL), so the extension .so can resolve engine
// symbols at load time. (Installing requires network access the first time.)

using Database database = new();
using Connection connection = new(database);

// Install + load the official "json" extension via the helper sugar (runs "INSTALL json" then
// "LOAD EXTENSION json" through the normal query path).
connection.InstallExtension("json");
connection.LoadExtension("json");
Console.WriteLine("Loaded the 'json' extension.");
Console.WriteLine();

// Use a function the extension provides. cast(... AS JSON) and json_extract come from the extension.
using QueryResult result = connection.Query(
    "RETURN json_extract(cast('{\"name\": \"Alice\", \"age\": 30}' AS JSON), 'name') AS name");

foreach (object?[] row in result.Rows())
{
    Console.WriteLine($"  json_extract -> {row[0]}");
}
```

- [ ] Compile-gate:

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\engine-extensions\EngineExtensions.csproj
```

Expected PASS: build succeeds. Acceptable pre-Pack FAIL: `NU1102` for `LadybugDB 0.17.2`. A compile error on `InstallExtension`/`LoadExtension` means the example diverges from §4.5 — fix the example.
- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\engine-extensions\README.md`:

```markdown
# engine-extensions

Installs and loads a real engine extension (the official `json` extension), then calls a function
it provides.

It demonstrates:

- `Connection.InstallExtension("json")` — runs `INSTALL json` (downloads the extension; needs network
  on first run).
- `Connection.LoadExtension("json")` — runs `LOAD EXTENSION json` (dynamically loads the extension).
- Calling an extension-provided function (`json_extract`) from Cypher.

## Why this needs the loader fix

A dynamically loaded extension `.so`/`.dylib` must resolve the engine's exported symbols at load
time. The binding loads `liblbug` with **global symbol visibility** on Linux/macOS — its native
resolver uses `dlopen(..., RTLD_NOW | RTLD_GLOBAL)` — so the extension can bind against the
already-loaded engine. This mirrors what the Java/Node/Rust bindings do via `RTLD_GLOBAL` / `-rdynamic`.
Without it, `LOAD EXTENSION` fails on Linux with unresolved-symbol errors even though the engine is
present. On Windows the engine DLL's exports are visible to loaded extensions by default.

## Run

```bash
dotnet run
```

Requires network access the first time so `INSTALL json` can download the extension.

Expected output:

```
Loaded the 'json' extension.

  json_extract -> "Alice"
```
```

- [ ] Commit:

```
git add examples/engine-extensions
git commit -m "docs(examples): add engine-extensions example (INSTALL/LOAD json)"
```

---

## Task 6 — examples/poco-mapping: [LadybugRow] + Map<T>()

Pinned contract (§4.7): `[LadybugRow]` attribute (`AttributeTargets.Class | Struct`);
generated `IReadOnlyList<T> QueryResult.Map<T>()` and
`IAsyncEnumerable<T> QueryResult.MapAsync<T>(CancellationToken ct=default)`. Reflection-free,
AOT-safe — the mapper is source-generated by the analyzer shipped inside the core package.

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\PocoMapping.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>LadybugDB.Examples.PocoMapping</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LadybugDB" Version="$(LadybugVersion)" />
    <!-- All-platform native engine. For a slim single-RID app, drop this and reference one runtime, e.g.:
         <PackageReference Include="LadybugDB.Native.win-x64" Version="$(LadybugVersion)" /> -->
    <PackageReference Include="LadybugDB.Native" Version="$(LadybugVersion)" />
  </ItemGroup>

</Project>
```

- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\Program.cs` (annotates a record with `[LadybugRow]` and uses generated `Map<T>()` / `MapAsync<T>()`; column names match property names):

```csharp
using LadybugDB;

// POCO mapping: annotate a type with [LadybugRow] and the source generator produces a reflection-free
// mapper, surfaced as QueryResult.Map<T>() and MapAsync<T>(). No runtime reflection, so it is AOT-safe.

using Database database = new();
using Connection connection = new(database);

connection.Query("CREATE NODE TABLE Person(name STRING, age INT64, PRIMARY KEY(name))").Dispose();
connection.Query("CREATE (:Person {name: 'Alice', age: 30})").Dispose();
connection.Query("CREATE (:Person {name: 'Bob', age: 42})").Dispose();
connection.Query("CREATE (:Person {name: 'Carol', age: 25})").Dispose();

// Synchronous Map<T>(): materialize every row into a Person. Result column names must match the
// target's property names (here: name, age).
Console.WriteLine("== Map<Person>() ==");
using (QueryResult result = connection.Query(
    "MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    foreach (Person person in result.Map<Person>())
    {
        Console.WriteLine($"  {person.Name} ({person.Age})");
    }
}

Console.WriteLine();

// Asynchronous MapAsync<T>(): the same mapping over the async streaming path.
Console.WriteLine("== MapAsync<Person>() ==");
using (QueryResult result = connection.Query(
    "MATCH (p:Person) RETURN p.name AS name, p.age AS age ORDER BY name"))
{
    await foreach (Person person in result.MapAsync<Person>())
    {
        Console.WriteLine($"  {person.Name} ({person.Age})");
    }
}

// [LadybugRow] marks this record for the source generator. Property names map to result columns.
[LadybugRow]
public sealed record Person(string Name, long Age);
```

- [ ] Compile-gate:

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\poco-mapping\PocoMapping.csproj
```

Expected PASS: build succeeds and the generator emits the `Map<Person>` mapper. Acceptable pre-Pack FAIL: `NU1102` for `LadybugDB 0.17.2`. A compile error on `[LadybugRow]`, `Map<T>`, or `MapAsync<T>` (or a generator diagnostic) means the example diverges from §4.7 — fix the example. (If the generator requires the `[LadybugRow]` type to be top-level rather than after top-level statements, move `Person` into its own file `Person.cs`; do not change the attribute/method names.)
- [ ] Create `W:\code\ladybug\tools\csharp_api\examples\poco-mapping\README.md`:

```markdown
# poco-mapping

Maps query results straight into your own types with a source-generated, reflection-free mapper.

It demonstrates:

- `[LadybugRow]` on a `record`/`class` — the analyzer shipped inside the `LadybugDB` package generates
  a mapper for it at compile time.
- `QueryResult.Map<T>()` — materialize all rows into `T` synchronously.
- `QueryResult.MapAsync<T>()` — the same over the async path (`IAsyncEnumerable<T>`).

Result column names must match the target type's property names (use `AS` aliases in Cypher to line
them up). Mapping is reflection-free, so it works under Native AOT and trimming.

## Run

```bash
dotnet run
```

Expected output:

```
== Map<Person>() ==
  Alice (30)
  Bob (42)
  Carol (25)

== MapAsync<Person>() ==
  Alice (30)
  Bob (42)
  Carol (25)
```
```

- [ ] Commit:

```
git add examples/poco-mapping
git commit -m "docs(examples): add poco-mapping example ([LadybugRow] + Map<T>())"
```

---

## Task 7 — Register the five new examples in Examples.slnx + examples/README.md

- [ ] Edit `W:\code\ladybug\tools\csharp_api\examples\Examples.slnx` to add the five projects:

```xml
<Solution>
  <Project Path="quickstart/Quickstart.csproj" />
  <Project Path="demo-graph/DemoGraph.csproj" />
  <Project Path="prepared-statements/PreparedStatements.csproj" />
  <Project Path="result-values/ResultValues.csproj" />
  <Project Path="async/Async.csproj" />
  <Project Path="di-extensions/DiExtensions.csproj" />
  <Project Path="arrow/Arrow.csproj" />
  <Project Path="engine-extensions/EngineExtensions.csproj" />
  <Project Path="poco-mapping/PocoMapping.csproj" />
</Solution>
```

- [ ] Edit `W:\code\ladybug\tools\csharp_api\examples\README.md` — update the intro to note that some examples reference the additional packages, and add a second table. Change the "Every project references two packages" paragraph to acknowledge the new packages, then add this section after the existing "Database usage examples" table (before "## Package / deployment example"):

```markdown
## Feature examples

These showcase the parity/async/ecosystem additions. Some reference additional packages
(`LadybugDB.Extensions`, `LadybugDB.Arrow`) on top of the managed core.

| Example | What it shows | Extra package |
| --- | --- | --- |
| [`async`](async/) | `QueryAsync`/`QueryAllAsync`/`PrepareAsync`/`ExecuteAsync`, `StreamAsync` (`IAsyncEnumerable`), and `CancellationToken` → `Interrupt`. | — |
| [`di-extensions`](di-extensions/) | `AddLadybug` + health check + resilience in a `ServiceCollection`, and the `QueryResult` export helpers (`ToDictionaries`/`Select`/`Scalar`/`ToJson`/`ToCsv`/`ToDataTable`). | `LadybugDB.Extensions` |
| [`arrow`](arrow/) | Apache Arrow round-trip: `CreateArrowTable` ingest + `ReadSchema`/`ReadBatches` export. | `LadybugDB.Arrow` |
| [`engine-extensions`](engine-extensions/) | `InstallExtension`/`LoadExtension` for a real engine extension (`json`), and the global-symbol-visibility loader behavior. | — |
| [`poco-mapping`](poco-mapping/) | `[LadybugRow]` + source-generated `Map<T>()`/`MapAsync<T>()` (reflection-free, AOT-safe). | — |
```

Also update the "open all" line so the `Examples.slnx` build covers all nine projects (no list of four).
- [ ] Compile-gate (whole example solution, if the `0.17.2` feed is staged):

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\examples\Examples.slnx
```

Expected PASS: all nine projects build. Acceptable pre-Pack FAIL: `NU1102` for the `0.17.2` family on the example host — proves the wiring, not a code error.
- [ ] Commit:

```
git add examples/Examples.slnx examples/README.md
git commit -m "docs(examples): register async/di-extensions/arrow/engine-extensions/poco-mapping in the example solution"
```

---

## Task 8 — README.md: version line, packages, async, engine extensions, OTel, POCO

This task makes the user-facing README reflect the new surface. Read the current
`W:\code\ladybug\tools\csharp_api\README.md` first; it ends after the "How the packages are built"
section at line 99.

- [ ] Edit the version blockquote (line 7): replace

```markdown
> Current package family: `0.17.0.1`, built against the Ladybug `v0.17.0` engine.
```

with

```markdown
> Current package family: `0.17.2`, built against the Ladybug `v0.17.2` engine.
```

- [ ] Edit the Installation section to document the new managed packages. After the "Available native packages" paragraph (around line 37), add:

```markdown
### Optional companion packages

The managed core is `LadybugDB`. Two optional packages add ecosystem features on top of it (each
shares the family version and targets `net10.0;netstandard2.0`):

- **`LadybugDB.Extensions`** — `Microsoft.Extensions`-style integration: `AddLadybug` DI registration,
  a health check, a built-in resilience executor (timeout/retry/circuit-breaker, no Polly),
  `IAsyncEnumerable` streaming sugar, an `IRowAccessor`, and `QueryResult` export helpers
  (`ToDictionaries`/`Select`/`Scalar`/`ToJson`/`ToCsv`/`ToDataTable`).
- **`LadybugDB.Arrow`** — Apache Arrow interop: read results as `RecordBatch` and ingest a
  `RecordBatch`/CSR back into the engine. Apache.Arrow is referenced only by this package, never by core.

```bash
dotnet add package LadybugDB.Extensions
dotnet add package LadybugDB.Arrow
```
```

- [ ] Add an "Async" subsection after the "Quick start" code block (after line 55). Insert:

```markdown
### Async

The core ships first-class async on `Connection`. Calls are offloaded over the connection's internal
serialization (not `Task.FromResult` wrappers), and a `CancellationToken` interrupts the running query.

```csharp
using var result = await conn.QueryAsync("MATCH (p:Person) RETURN p.name, p.age");

await foreach (var tuple in conn.StreamAsync("MATCH (p:Person) RETURN p.name"))
{
    using var name = tuple.GetValue(0);
    Console.WriteLine(name.GetValue());
}
```

`QueryAsync`, `QueryAllAsync`, `PrepareAsync`, `ExecuteAsync`, and `StreamAsync` are available; see
[`examples/async`](examples/async/).

### Engine extensions

Install and load engine extensions (for example `json` or `fts`) through helper methods:

```csharp
conn.InstallExtension("json");   // INSTALL json   (downloads on first run)
conn.LoadExtension("json");      // LOAD EXTENSION json
```

On Linux/macOS the binding loads the engine with **global symbol visibility**
(`dlopen(..., RTLD_NOW | RTLD_GLOBAL)`), so a dynamically loaded extension `.so`/`.dylib` can resolve the
engine's symbols — matching the Java/Node/Rust bindings. See [`examples/engine-extensions`](examples/engine-extensions/).

### Observability (OpenTelemetry)

The core emits OpenTelemetry signals from a static `ActivitySource` and `Meter`, both named
`"LadybugDB"`, with **zero cost when no listener is attached**. The meter publishes `db.query.count`,
`db.query.errors`, and `db.query.duration.ms`. Subscribe with the OpenTelemetry SDK:

```csharp
using var tracer = Sdk.CreateTracerProviderBuilder().AddSource("LadybugDB").Build();
using var meter  = Sdk.CreateMeterProviderBuilder().AddMeter("LadybugDB").Build();
```

(`LadybugDB.Extensions` adds registration sugar for wiring these into a host.)

### POCO mapping

Annotate a type with `[LadybugRow]` and a source generator (shipped inside the `LadybugDB` package)
produces a reflection-free mapper, surfaced as `QueryResult.Map<T>()` / `MapAsync<T>()` — AOT- and
trim-safe:

```csharp
[LadybugRow]
public sealed record Person(string Name, long Age);

using var result = conn.Query("MATCH (p:Person) RETURN p.name AS Name, p.age AS Age");
foreach (var person in result.Map<Person>())
{
    Console.WriteLine($"{person.Name} ({person.Age})");
}
```

See [`examples/poco-mapping`](examples/poco-mapping/).
```

- [ ] Update the versioning paragraph in "How the packages are built" (lines 81-82) so the worked example uses `0.17.2`:

```markdown
All packages in the family share one version. The first three numeric segments track the upstream engine
release, and the optional fourth segment is the .NET package revision for binding-only releases. For
example, package `0.17.2` wraps the Ladybug `v0.17.2` engine; a binding-only fix over the same engine
would be `0.17.2.1`. Prerelease suffixes are reserved for preview builds.
```

- [ ] Verify the README renders without broken relative links (the five new `examples/<name>/` dirs exist from Tasks 2-6):

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
```

Expected PASS: the core solution still builds (README edits don't affect it; this confirms the tree is intact). The README link targets are real directories created earlier in this plan.
- [ ] Commit:

```
git add README.md
git commit -m "docs: document new packages, async, engine extensions, OTel, POCO mapping, and 0.17.2 bump"
```

---

## Task 9 — MAINTAINING.md: package family, versioning, examples

- [ ] Read the current `W:\code\ladybug\tools\csharp_api\MAINTAINING.md` (already read in pre-flight). Edit the Versioning Policy package list (lines 26-32) to add the two new managed packages above the native packages:

```markdown
All packages in the family share one package version:

- `LadybugDB`
- `LadybugDB.Extensions`
- `LadybugDB.Arrow`
- `LadybugDB.Native`
- `LadybugDB.Native.win-x64`
- `LadybugDB.Native.linux-x64`
- `LadybugDB.Native.linux-arm64`
- `LadybugDB.Native.osx-x64`
- `LadybugDB.Native.osx-arm64`
```

- [ ] Update the Versioning Policy examples block (lines 39-43) to lead with the current `0.17.2` family:

```markdown
- `0.17.2` - stable .NET package family for engine `v0.17.2`.
- `0.17.2.1` - binding/package-only release that still uses engine `v0.17.2`.
- `0.17.0.1` - an earlier binding/package-only release over engine `v0.17.0`.
- `0.18.0-preview.1` - preview package family for a future engine `v0.18.0`.
```

- [ ] Update the "Package Family" section (lines 156-163) so it names the managed companion packages and notes the expanded `Pack`/`VerifyPackages` scope. Replace the section body with:

```markdown
`LadybugDB` is the managed core. Two optional managed companion packages layer on top of it:

- `LadybugDB.Extensions` - DI / health check / resilience / streaming / row-access / export. Depends on
  the `Microsoft.Extensions.*` abstractions, `Microsoft.Bcl.AsyncInterfaces` (for ns2.0), and
  `System.Text.Json`.
- `LadybugDB.Arrow` - Apache Arrow `RecordBatch`/CSR interop. Depends on `Apache.Arrow`. Apache.Arrow is
  not referenced by the core or by `LadybugDB.Extensions`.

Native libraries ship separately in one package per RID, and `LadybugDB.Native` is a meta-package that
depends on every per-RID native package. Consumers reference:

- `LadybugDB` plus `LadybugDB.Native` for all supported platforms, or
- `LadybugDB` plus one `LadybugDB.Native.<rid>` package for a slim single-platform app,
- optionally adding `LadybugDB.Extensions` and/or `LadybugDB.Arrow` for the ecosystem features.

The shipped RIDs are `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. `Pack` builds and
`VerifyPackages` checks the full family, including the two managed companion packages.
```

- [ ] Add a note about the engine-extension loader behavior to the "ABI Update Checklist" rules list (after line 153, in the "Rules that should not change without deliberate review" block):

```markdown
- On Linux/macOS the native resolver loads `liblbug` with global symbol visibility
  (`dlopen(..., RTLD_NOW | RTLD_GLOBAL)`) so dynamically loaded engine extensions can resolve engine
  symbols. Do not narrow this to local-only visibility without verifying `LOAD EXTENSION` still works
  against a real extension under the native-gated tests.
```

- [ ] Replace the "Examples" section (lines 165-177) to enumerate all categories including the five new feature examples:

```markdown
## Examples

`examples/` contains three categories, all consuming published NuGet packages and sharing the example
package version in `examples/Directory.Build.props`:

- Database-usage examples: `quickstart`, `demo-graph`, `prepared-statements`, and `result-values`.
- Feature examples for the parity/async/ecosystem additions: `async` (async query/stream/cancel),
  `di-extensions` (DI + health check + resilience + export helpers; references `LadybugDB.Extensions`),
  `arrow` (Apache Arrow round-trip; references `LadybugDB.Arrow`), `engine-extensions` (INSTALL/LOAD a
  real extension), and `poco-mapping` (`[LadybugRow]` + `Map<T>()`).
- `native-loading/`: a deployment/package-loading example showing bundled native NuGet vs.
  system-installed native library behavior.

Examples are not currently part of CI because their package-restore behavior depends on a published
package version being available. Bump `examples/Directory.Build.props` to the published family version
when releasing.
```

- [ ] Verify the doc edits left the tree buildable (docs-only change; confirms nothing else broke):

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
```

Expected PASS: core solution builds.
- [ ] Commit:

```
git add MAINTAINING.md
git commit -m "docs(maintaining): add Extensions/Arrow packages, 0.17.2 versioning, loader note, and new examples"
```

---

## Task 10 — Final consistency sweep

- [ ] Grep the repo for stale `0.17.0.1` / `0.17.0` references in docs and example props that should now be `0.17.2`, and confirm only intentional historical mentions remain:

```powershell
dotnet build W:\code\ladybug\tools\csharp_api\LadybugDB.slnx -c Debug
```

Use Grep (pattern `0\.17\.0\.1`, globs `README.md`, `MAINTAINING.md`, `examples/**`) to list remaining hits. The only acceptable remaining `0.17.0.1` mentions are the historical examples in `MAINTAINING.md`'s versioning list. Every package-version *default* and the README "current family" line must read `0.17.2`.
- [ ] Confirm every new example folder has exactly three files (`*.csproj`, `Program.cs`, `README.md`) and is listed in `Examples.slnx`. Use Glob `examples/{async,di-extensions,arrow,engine-extensions,poco-mapping}/*`.
- [ ] If any stale reference is found, fix it and commit:

```
git add -A
git commit -m "docs: sweep stale version references to 0.17.2"
```

---

## Self-review / done criteria

Tie-out to WS-L's Done column ("docs/examples updated") and the spec's Definition of Done (§10).

- [ ] `README.md` documents: the `0.17.2` family/engine line, `LadybugDB.Extensions` + `LadybugDB.Arrow` packages, the async API (`QueryAsync`/`QueryAllAsync`/`PrepareAsync`/`ExecuteAsync`/`StreamAsync`), engine extensions (`InstallExtension`/`LoadExtension`) **with** the global-symbol-visibility loader behavior, OpenTelemetry (`ActivitySource`/`Meter` named `LadybugDB`, the three metrics), and POCO mapping (`[LadybugRow]` + `Map<T>()`/`MapAsync<T>()`).
- [ ] `MAINTAINING.md` lists `LadybugDB.Extensions` and `LadybugDB.Arrow` in the package family, bumps the versioning examples to `0.17.2`, documents the loader rule, and enumerates all example categories including the five new ones.
- [ ] `examples/Directory.Build.props` default `LadybugVersion` is `0.17.2`.
- [ ] Five new runnable examples exist, each with `.csproj` + `Program.cs` + `README.md`:
  - `examples/async` (ExecuteAsync/StreamAsync/CancellationToken) — code uses only §4.3 signatures.
  - `examples/di-extensions` (ServiceCollection + health check + resilience + export helpers) — references `LadybugDB.Extensions`, code uses only §4.8 signatures.
  - `examples/arrow` (RecordBatch round-trip) — references `LadybugDB.Arrow`, code uses only §4.4 signatures.
  - `examples/engine-extensions` (INSTALL/LOAD `json`) — code uses only §4.5 signatures.
  - `examples/poco-mapping` (`[LadybugRow]` + `Map<T>()`) — code uses only §4.7 signatures.
- [ ] Each new example references the family via `$(LadybugVersion)` (no hardcoded versions), consistent with `examples/Directory.Build.props`, and follows the existing csproj convention (the all-platform `LadybugDB.Native` reference + the slim-RID comment).
- [ ] All nine projects are registered in `examples/Examples.slnx`; `examples/README.md` lists every example in its tables.
- [ ] No example uses an invented API name — every called member matches the §4 pinned contract exactly. (Verified by the per-task compile gate: a member-not-found compile error fails the gate.)
- [ ] `dotnet build examples/Examples.slnx` succeeds once the `0.17.2` package family is restorable (artifacts feed or nuget.org); before then, the only failures are `NU1102` package-not-found, never `CS`-level API mismatches.
- [ ] `dotnet build LadybugDB.slnx -c Debug` still green (docs/examples changes don't touch core sources).
- [ ] No stale `0.17.0.1` version defaults remain outside the historical versioning examples in `MAINTAINING.md`.
