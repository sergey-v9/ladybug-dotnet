# Feature Parity Matrix (2026-06)

Legend: ✅ yes · 🟡 partial · ❌ no. Columns: **Ours** = `tools/csharp_api` ·
**Ref** = `reference/ladybug.net` · Py/Java/Node/Rust = official sibling bindings.
Derived from the 7-agent analysis. The **C API** column marks whether the capability
exists in `lbug.h` at all (the ceiling for any binding).

| # | Capability | C API | Ours | Ref | Py | Java | Node | Rust |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| 1 | Database open w/ full SystemConfig | ✅ | ✅ | 🟡¹ | ✅ | 🟡² | 🟡² | ✅ |
| 2 | `Query` one-shot | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | Prepare + execute w/ bound params | ✅ | ✅ | 🟡¹ | ✅ | ✅ | ✅ | ✅ |
| 4 | Set/get max threads for exec | ✅ | ❌ | ✅ | 🟡 | ✅ | 🟡 | ✅ |
| 5 | **Interrupt / cancel query** | ✅ | ❌ | ✅ | ✅ | ✅ | ❌ | ✅ |
| 6 | **Query timeout** | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | Scalar param binding (all types) | ✅ | 🟡³ | 🟡 | ✅ | 🟡 | 🟡 | ✅ |
| 8 | Complex binding (LIST/STRUCT/MAP) | ✅ | 🟡⁴ | ✅ | ✅ | ✅ | ✅ | 🟡 |
| 9 | Column metadata + **logical type** | ✅ | 🟡⁵ | ✅ | ✅ | ✅ | 🟡 | ✅ |
| 10 | Iteration (hasNext/getNext/**reset**) | ✅ | 🟡⁶ | ✅ | ✅ | ✅ | ✅ | 🟡 |
| 11 | **Query summary (timings)** | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | **Multiple result sets** | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ❌ |
| 13 | `ToString` / display | ✅ | ✅ | ✅ | 🟡 | ✅ | ❌ | ✅ |
| 14 | **Arrow interop** | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ |
| 15 | Read BOOL/INT/UINT/INT128/DBL/FLT | ✅ | ✅ | 🟡⁷ | ✅ | ✅ | ✅ | ✅ |
| 16 | Read DATE/TIMESTAMP*/INTERVAL | ✅ | ✅ | 🟡⁷ | ✅ | ✅ | ✅ | ✅ |
| 17 | Read STRING/BLOB/UUID/DECIMAL | ✅ | 🟡⁸ | 🟡⁷ | ✅ | ✅ | ✅ | ✅ |
| 18 | Read LIST/ARRAY/STRUCT/MAP/UNION | ✅ | 🟡⁹ | 🟡⁷ | ✅ | 🟡 | ✅ | ✅ |
| 19 | Read NODE/REL/RECURSIVE_REL/ID | ✅ | ✅ | 🟡⁷ | ✅ | ✅ | ✅ | ✅ |
| 20 | Value accessors | ✅ | ✅ | 🟡⁷ | ✅ | ✅ | 🟡 | ✅ |
| 21 | **Engine extension install/load** | 🟡ᴬ | 🟡ᴮ | ❌ | ✅ | 🟡 | 🟡 | ✅ |
| 22 | Async / future-based query | — | ❌ | ✅ᶜ | ✅ | ❌ | 🟡 | ❌ |
| 23 | Cancellation tokens | — | ❌ | ✅ | 🟡 | ❌ | ❌ | ❌ |
| 24 | Query progress callback | 🟡ᴰ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| 25 | Object/POCO mapping | — | ❌ | 🟡ᴱ | 🟡 | ❌ | 🟡 | ❌ |
| 26 | Streaming/cursor over results | ✅ | 🟡⁶ | ✅ | 🟡 | ✅ | 🟡 | 🟡 |
| 27 | Explicit disposal model | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 28 | Error/exception model | ✅ | 🟡ᶠ | ✅ | 🟡 | 🟡 | ✅ | ✅ |
| 29 | DI / framework integration | — | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |
| 30 | Connection pooling/guidance | — | ❌ | 🟡 | ✅ | 🟡 | 🟡 | 🟡 |

## Notes

1. **Ref** declares the full P/Invoke surface but its live adapter reads values via `to_string` and `LadybugOptions` config isn't wired into the adapter yet → effectively partial.
2. **Java/Node** put `max_num_threads` only on the connection, not the DB config object; config is positional args, not an object.
3. **Ours** binds bool/all int+uint widths/float/double/string/Guid/DateTime/DateTimeOffset/Interval/DateOnly — but **no DECIMAL, BLOB, Int128, or explicit timestamp_ns/ms/sec** bind.
4. **Ours** binds LIST (incl. empty/null) but **not STRUCT or MAP** parameters (`create_struct`/`create_map` unwrapped).
5. **Ours** exposes column **names** only; `get_column_data_type` (per-column logical type) is unwrapped.
6. **Ours** has `HasNext`/`GetNext`/`RowCount` + a materializing `Rows()` enumerator, but **no `reset_iterator`** and **no `IAsyncEnumerable`/lazy streaming abstraction**.
7. **Ref** can technically read all types but its live path stringifies everything; typed extraction is declared-but-unused.
8. **Ours** DECIMAL is lossy (falls back to raw string on `decimal` overflow); UUID falls back to string on parse failure.
9. **Ours** approximates fixed-size ARRAY via the LIST reader and UNION via the STRUCT reader.

### Capability-existence footnotes (C API ceiling)
- **A** — No engine-extension C API; install/load is done through the normal Cypher query path (`INSTALL x; LOAD EXTENSION x;`). "Parity" = an ergonomic helper + native global-symbol-visibility on load.
- **B** — **Ours**: `Query("INSTALL …")` works syntactically, but native global-symbol-visibility on Linux is **unverified/likely broken** (no RTLD_GLOBAL handling, unlike Java/Node/Rust). Marked partial pending verification.
- **C** — Reference async is over a synchronous engine (async wrapper/streaming seam), not engine-level async.
- **D** — Progress callback is a C++-main-API feature surfaced by Node, not part of the stable C ABI; lowest priority for us.
- **E** — Reference `IRowAccessor` gives typed by-name/by-index access (closest to POCO), but no class/source-gen mapping.
- **F** — **Ours** has `LadybugException`/`LadybugQueryException` only (2 types, no `get_last_error`); shallow but typed.

## Our prioritized gap list (what to close)

**P0 — correctness / must-fix**
- netstandard2.0 Unix native loading (no resolver) — load failure for that audience.
- Multi-statement result truncation (`has_next_query_result`/`get_next_query_result`).
- Engine-extension native symbol visibility on Linux (verify + fix load flags).
- DECIMAL fidelity (stop the silent `decimal`↔`string` ambiguity).

**P1 — core C-API parity (every sibling has these)**
- Query timeout · interrupt/cancel · set/get max threads.
- Query summary (compiling/execution time).
- Per-column logical type (`get_column_data_type`).
- `reset_iterator`.
- DECIMAL/BLOB/Int128/struct/map **parameter binding** (value creators).

**P2 — high-value capability**
- The `LadybugDB.Extensions` .NET package (DI, health, resilience, streaming, row mapping, export helpers).
- First-class async (`Task`-returning execute + `IAsyncEnumerable` streaming + `CancellationToken`→interrupt).
- Arrow interop (export + ingest).

**P3 — differentiators / nice-to-have**
- OpenTelemetry instrumentation.
- Source-generated POCO mapping.
- Differential parity harness + upstream-ported parity tests + BenchmarkDotNet CI gate.
- Engine-extension ergonomic helpers + connection pooling guidance.
