# Dependency injection & extensions example

Uses the `LadybugDB.Extensions` package with a generic host:

- `AddLadybug` — registers `LadybugOptions`.
- `AddLadybugResilience` — decorates the executor with timeout + retry + circuit-breaker (no Polly).
- `AddLadybugHealthCheck` — an ASP.NET Core health check that runs a probe query.
- `ExecutorStreamingExtensions.StreamAsync` — `IAsyncEnumerable<IRowAccessor>` with typed `Get<T>`.
- `QueryResultExtensions` — `ToJson` / `ToCsv` (also `ToJsonArray` / `ToDataTable` / `ToDictionaries`).

The app owns the `Database`/`Connection` lifetime and registers an `ILadybugExecutor`
(`LadybugConnectionExecutor`) over it; the extension helpers build on that seam, which keeps the package
unit-testable with fakes.

```bash
dotnet run --project examples/di-extensions/DiExtensions.csproj
```
