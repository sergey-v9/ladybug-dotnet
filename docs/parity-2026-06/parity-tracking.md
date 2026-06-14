# C-API Parity Tracking (2026-06)

Maps each upstream C-API gtest source under `test/c_api/` to its C# port in
`test/LadybugDB.Tests.Parity/`. Ports are native-gated (`ParityEnvironment.NativeAvailable`)
and rebuild a small in-process person graph instead of loading the engine's `dataset/tinysnb`
CSVs (unavailable to a managed NuGet consumer).

**Upstream pin:** `LadybugDB/ladybug` tag **`v0.17.1`** (matches the native bump in `version.txt`
→ `0.17.1.0`, driven by WS-K; see `04-coordination-decisions.md` §D1 — `v0.17.2` is not a
published GitHub release, so the effort ships on `v0.17.1`). Re-port when the pin moves; re-sync
expected values against that tag's `src/include/c_api/lbug.h` and `test/c_api/*.cpp`.

Legend: ✅ ported · 🟡 partially ported (constructor-only or env-specific cases intentionally
skipped) · ⬜ not ported (with reason).

| Upstream file | C# port | Status | Notes |
|---|---|:--:|---|
| `version_test.cpp` | `VersionParityTests.cs` | ✅ | On-disk magic-header check replaced by storage-version > 0 (managed surface exposes no file path). Version non-empty + stable. |
| `database_test.cpp` | `DatabaseParityTests.cs` | 🟡 | Ported: open/in-memory/read-only/use-after-destroy. Skipped: HomeDir (`~`), EnableMultiWrites (no managed accessor), C-pointer close-after-destroy ordering. |
| `connection_test.cpp` | `ConnectionParityTests.cs` | ✅ | Query/threads/timeout/interrupt/prepare/execute. Null-handle C cases are not expressible (managed ctor throws). |
| `query_result_test.cpp` | `QueryResultParityTests.cs` | ✅ | Columns/types/summary/iterator/reset/multi-result via the WS-B surface. The Arrow-schema accessor is commented-out upstream; covered by WS-E, not here. |
| `data_type_test.cpp` | `DataTypeParityTests.cs` | 🟡 | Observed via `QueryResult.GetColumnType` (no public logical-type constructor). Id / child-type / fixed-array-size / ToString covered. |
| `flat_tuple_test.cpp` | `FlatTupleParityTests.cs` | ✅ | Typed value access, out-of-range error, pipe-delimited to-string. |
| `prepared_statement_test.cpp` | `PreparedStatementParityTests.cs` | 🟡 | Scalar binds + IsReadOnly + prepare-error ported; DATE/INT128/BLOB use the WS-C overloads (BLOB round-trips via `CAST($p AS BLOB)` per §D7). INTERVAL/explicit timestamp-precision binds tracked as follow-ups. |
| `value_test.cpp` | `ValueParityTests.cs` | 🟡 | Read/accessor cases ported (primitives, temporal, list/struct/map, node/rel, decimal-as-text). `lbug_value_create_*` constructor cases are out of scope (no managed value-construction surface). |

All 8 upstream `test/c_api/*.cpp` files have a corresponding port; none are left untracked.

## Benchmarks + differential parity

- `benchmarks/LadybugDB.Benchmarks` — BenchmarkDotNet `QueryBenchmarks` (Query/Prepare/Execute) plus
  a `--ci-gate <baseline.json> <candidate.json>` mode that fails (exit 1) when any candidate/baseline
  mean ratio exceeds the ceiling from `LADYBUG_BENCH_RATIO_MAX` (default 1.25; legacy
  `LADYBUG_BENCH_MAX_RATIO` is also accepted). The gate math lives in the pure, native-free
  `CiGate` class and is unit-tested by `benchmarks/LadybugDB.Benchmarks.Tests` (non-gated `[Fact]`s).
- `DifferentialRunner` drives equivalent public-surface paths and asserts they agree; reachable from
  the CLI (`--diff`) and from the native-gated `DifferentialSmokeTests` in the parity suite.

## Follow-ups
- INTERVAL and explicit `timestamp_ns/ms/sec` bind ports once WS-C lands those overloads.
- UNION first-class read parity once the engine exposes a non-best-effort tagged-union read shape
  (the current materializer recovers the active member name heuristically; see `GraphTypes.Union`).
- Arrow differential parity lives with WS-E (`LadybugDB.Arrow`); wire it into the diff runner once
  that package's read/write surface is finalized.
