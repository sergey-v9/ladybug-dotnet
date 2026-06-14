# Async example

Demonstrates the asynchronous surface of `LadybugDB`:

- `QueryAsync` — a `Task`-returning one-shot query.
- `StreamAsync` — `IAsyncEnumerable<FlatTuple>` row-by-row streaming.
- `CancellationToken` — wired to the engine interrupt, so cancelling the token aborts an in-flight query.

A single `Connection` serializes its operations internally, so the async calls are offloaded while
honoring that gate. For genuine concurrency, use one connection per concurrent operation.

```bash
dotnet run --project examples/async/AsyncExample.csproj
```
