# Adversarial Review — Findings & Fix Plan (2026-06-14)

> **STATUS: all 13 confirmed findings FIXED and verified (255/255 tests green).**
> Commits: FIX-MAP `c0b0bfb`+`57be6fa` (SG-1/AOT-1/SG-2/MAP-1/SG-3); FIX-CONC `e256f7a`/`7e87106`/`c3bed23`/`ef8da57`
> (CONC-1/CONC-2/CONC-3/CONC-5/MAP-2/UNION-1); FIX-ARROW `8dc2a4c` (ARROW-1); FIX-MISC `b761f9b` (PORT-1/NAT-5).
> FIX-ARROW and FIX-MISC were finished by the orchestrator after the authoring agents hit the session
> usage limit mid-run — FIX-ARROW's work was complete-but-uncommitted (recovered & committed); FIX-MISC
> was redone inline.

5-lens adversarial review of `main..HEAD` (~8,400 LOC). Each finding was verified by an
independent skeptic prompted to refute it. **23 raised → 13 confirmed real, 2 uncertain
(near-unreachable), 8 false-positives.** Verifiers also corrected several over-claims (the
UNION *value* read is correct; only the multi-member tag *label* is best-effort).

## Confirmed — to fix

| ID | Sev | Issue | Fix | Group |
|---|---|---|---|---|
| SG-1 / AOT-1 | **high** | `LadybugRowConvert.To<T>` (source-gen Map path) throws on nullable-widening / enum / Guid / DateOnly→DateTime targets (`Convert.ChangeType` without unwrapping) | Unwrap `Nullable.GetUnderlyingType`; `Enum.ToObject` via underlying; Guid parse; DateOnly/DateTime. Share one helper. | FIX-MAP |
| SG-2 / MAP-1 | med | `RowAccessor.Convert<T>` (Extensions) throws on enum/Guid (unwraps Nullable but not enum/Guid) | Same shared conversion helper | FIX-MAP |
| SG-3 | low | Core ships a real `LadybugRowMappers` partial → `CS0436` in consumers that name the type directly (error under TWAE) | Remove core `LadybugRowMappers.cs`; generator emits the container unconditionally; add a two-assembly test | FIX-MAP |
| CONC-2 | **high** | `Interrupt()` / `InterruptForCancellation` can call `lbug_connection_interrupt` on a freed handle during concurrent `Dispose` (use-after-free) | Dedicated lifetime lock so Interrupt and `ConnectionDestroy` are mutually exclusive; Interrupt checks `_disposed` inside it | FIX-CONC |
| CONC-1 | med | `Value.Dispose()` uses a plain `bool _disposed` (not idempotent/thread-safe) — violates design §7 (FlatTuple was converted, Value missed) | `int _disposed` + `Interlocked.Exchange` + `Volatile.Read`, matching the other handles | FIX-CONC |
| CONC-3 | low | `RunWithCancellation` masks a genuine `LadybugQueryException` as `OperationCanceledException` when the token cancels for an unrelated reason | Preserve the inner exception; only normalize when the failure was the interrupt | FIX-CONC |
| CONC-5 | low | Un-awaited concurrent `QueryAsync` on one connection blocks a thread-pool thread on the sync `_gate` (inherent to Task.Run-over-sync-lock) | Document on the async API (by design; same as Python's pool model) | FIX-CONC (doc) |
| ARROW-1 | med | Arrow ingest hands the engine a pinned *copy* of the C-Data struct; the `Create()`-allocated outer shells (~152 B/call) leak (no UAF — engine frees inner buffers) | Pass the original `cSchema`/`cArray` `IntPtr`s straight through (interop → `IntPtr`); free the outer shells in a `finally` | FIX-ARROW |
| PORT-1 | med | `byte[]` nested in LIST/STRUCT/MAP binds as an escaped string literal (no CAST opportunity) → silent wrong type | Throw `NotSupportedException` for nested `byte[]` (engine has no blob value-creator); document the top-level-only limitation | FIX-MISC |
| NAT-5 | low | `dlopen` P/Invoke marshals the path as `CharSet.Ansi` → non-ASCII bundled paths corrupted (net10.0 **and** ns2.0) | `LPUTF8Str` on net7+, `byte[]`+`ToUtf8` on ns2.0 | FIX-MISC |

## Uncertain — document, optional hardening (no behavior change required)

| ID | Issue | Decision |
|---|---|---|
| NAT-4 / UNION-1 | UNION multi-member **tag label** is best-effort (active member *value* is correct; the engine C API exposes no tag-discriminator accessor) | Already documented (D7 / parity-tracking). Add a multi-member native-gated test pinning value-correctness + the tag caveat. Consider upstream request for a `lbug_value` union-tag accessor. |
| MAP-2 | `ReadMap` silently drops a null map key (near-unreachable: Ladybug map keys aren't nullable; `Dictionary<object,…>` can't hold a null key anyway) | Optional: throw `LadybugException` instead of silent drop (defense-in-depth). |

## False-positives (refuted by verification) — 8
Not listed individually; the verifiers found the cited code already correct or the bug
unreachable. Full reasoning in the review transcript.

## Fix groups (TDD, file-disjoint, sequential)
- **FIX-MAP** — `Mapping/LadybugRowConvert.cs`, `LadybugDB.Extensions/IRowAccessor.cs`, remove `Mapping/LadybugRowMappers.cs` + `LadybugDB.SourceGen` emitter, tests. (SG-1, AOT-1, SG-2, MAP-1, SG-3)
- **FIX-CONC** — `Connection.cs`, `Connection.Control.cs`, `Connection.Async.cs`, `Value.cs`, tests. (CONC-2, CONC-1, CONC-3, CONC-5 doc, MAP-2 hardening, UNION test)
- **FIX-ARROW** — `Connection.Arrow.cs`, `Interop/Native.LibraryImport.cs` + `Native.DllImport.cs`, `LadybugDB.Arrow/LadybugArrow.cs`, Arrow tests. (ARROW-1)
- **FIX-MISC** — `PreparedStatement.cs`, `Interop/UnixNativeMethods.cs` + `Native.cs`, tests. (PORT-1, NAT-5)
