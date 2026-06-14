# WS-K — netstandard2.0 dependency feasibility (Phase 1)

Goal: confirm every third-party dependency the new packages take has an acceptable
`netstandard2.0` asset, so all packages can multi-target `net10.0;netstandard2.0`
(design §4.1). If any dependency lacks ns2.0, the fallback is to keep the package
multi-targeted and `#if NET`-gate **only** the affected type — never to drop ns2.0
for the whole package.

## Verification commands

```bash
# lib/ TFMs inside a given package version:
curl -s "https://api.nuget.org/v3-flatcontainer/<id-lower>/<ver>/<id-lower>.<ver>.nupkg" -o pkg.nupkg
unzip -l pkg.nupkg | grep -i 'lib/'
```

## Results (verified 2026-06-14)

| Dependency (package) | Used by | ns2.0 asset? | Chosen floor | Notes |
|---|---|---|---|---|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | Extensions (WS-G) | yes | 8.0.0 | `IServiceCollection`; `lib/netstandard2.0/` verified. Latest stable line 10.0.x. |
| `Microsoft.Extensions.Options` | Extensions (WS-G) | yes | 8.0.0 | `lib/netstandard2.0/` verified. |
| `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` | Extensions health (WS-G) | yes | 8.0.0 | **`IHealthCheck` lives in `.Abstractions`, which HAS `lib/netstandard2.0/`** (verified 8.0.0). No `#if NET` gate needed. |
| `Microsoft.Extensions.Diagnostics.HealthChecks` (impl) | Extensions health (WS-G) | yes | 8.0.0 | Only needed if registration sugar requires the concrete builder; `.Abstractions` alone covers `IHealthCheck`. |
| `Microsoft.Bcl.AsyncInterfaces` | Extensions (`IAsyncEnumerable` on ns2.0) | yes | 8.0.0 | Provides `IAsyncEnumerable<T>` for ns2.0; net10 has it intrinsically. Reference only `Condition="'$(TargetFramework)'=='netstandard2.0'"`. |
| `System.Text.Json` | Extensions (`ToJson`) | yes | 8.0.0 | `lib/netstandard2.0/` verified. |
| `System.Runtime.Numerics` (`BigInteger`) | core DECIMAL (WS-C) | yes | built-in | ns2.0-safe (design §5.2). |
| `Apache.Arrow` | Arrow package (WS-E) | yes | 18.0.0 | `lib/netstandard2.0/Apache.Arrow.dll` verified present in 18.0.0. |
| `System.Diagnostics.DiagnosticSource` | core OTel (WS-H) | yes | 8.0.0 | `ActivitySource`/`Meter` on ns2.0; `lib/netstandard2.0/` verified (design §5.4). |
| `Microsoft.CodeAnalysis.CSharp` | SourceGen analyzer (WS-I) | n/a | 4.8.0 | Analyzer itself targets ns2.0; referenced `PrivateAssets="all"`. |
| `BenchmarkDotNet` | Benchmarks (WS-J) | n/a | 0.14.0 | Benchmark host is `net10.0` only; not packed. |

## Conclusion

**No `#if NET` gate is required for any planned dependency** — including HealthChecks,
because `IHealthCheck` is in `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`
which ships `netstandard2.0`. All new library packages multi-target `net10.0;netstandard2.0`.

All eight third-party `lib/netstandard2.0/<id>.dll` assets were re-confirmed against
`api.nuget.org` on 2026-06-14 (DI.Abstractions, Options, HealthChecks.Abstractions,
Bcl.AsyncInterfaces, System.Text.Json, Apache.Arrow 18.0.0, DiagnosticSource).

## Documented fallback (if a future dependency lacks ns2.0)

Keep the package multi-targeted; wrap the single affected public type in
`#if NET` … `#endif`, and reference the dependency with
`Condition="'$(TargetFramework)' != 'netstandard2.0'"`. Dropping ns2.0 from a whole
package is a flagged decision, not a default.
