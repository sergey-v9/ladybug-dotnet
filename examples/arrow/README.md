# Apache Arrow example

Uses the `LadybugDB.Arrow` package for zero-copy Arrow interop:

- `QueryResult.ReadSchema()` / `ReadBatches()` — export a result to `Apache.Arrow` `RecordBatch`es.
- `Connection.CreateArrowTable(name, batch)` — ingest a `RecordBatch` as a node table (and
  `CreateArrowRelTable(...)` for relationship tables).

`Apache.Arrow` comes transitively from `LadybugDB.Arrow`, so the core package and non-Arrow consumers
never carry that dependency.

```bash
dotnet run --project examples/arrow/ArrowExample.csproj
```
