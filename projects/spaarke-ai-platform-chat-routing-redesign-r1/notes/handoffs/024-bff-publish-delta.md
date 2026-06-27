# Task 024 — BFF Publish Delta

> **Generated**: 2026-06-22
> **Task**: 024 — Deprecation telemetry on `/api/ai/playbooks/by-name/{name}` (FR-03)
> **Wave**: 1-H (parallel-safe with task 023; different files)
> **Phase 0 baseline**: 44.75 MB compressed
> **Prior wave baseline (task 022)**: 44.75 MB compressed

---

## Measurement

| Metric | Value |
|---|---|
| Source | `deploy/api-publish-024/` |
| Compressed archive | `c:/tmp/api-publish-024-l6.zip` (zip DEFLATE level 6 — matches PowerShell `Compress-Archive -CompressionLevel Optimal` used by prior handoffs) |
| Raw bytes | **46,930,068** |
| MB (1024²) | **44.76 MB** |
| Δ vs Phase 0 baseline | **+0.01 MB** (within ±0.02 MB measurement noise) |
| Δ vs prior wave (post-022) | **+0.01 MB** (within noise) |
| NFR-01 ceiling | 60 MB (HARD STOP) |
| NFR-01 architecture-review trigger | 55 MB |
| NFR-01 escalation trigger | +5 MB single-task delta |

**Verdict**: ✅ **WITHIN BUDGET**. Effectively zero delta. The only changes are a few source lines (telemetry calls on existing types + a `using System.Diagnostics` directive); no new NuGet packages, no new DI registrations, no new endpoints, no new public-contract surface.

---

## Why effectively zero delta

This task is additive telemetry on an existing endpoint:
- Added `using System.Diagnostics` (already a transitive dep of ASP.NET Core — no new package)
- Added 3 telemetry calls (1 warning log + 2 Activity tags) inside the existing `GetPlaybookByName` handler
- Added `HttpContext httpContext` parameter to the handler signature so it can read `User.FindFirst("tid")` + `Request.Headers.UserAgent`
- Added XML-doc remarks explaining the deprecation + ADR-015 tier-1 audit

No allocations beyond what the existing handler already does. The IL output for `PlaybookEndpoints.cs` grew by ~150 bytes (one log call with 3 args, two SetTag calls); the `+0.01 MB` zip delta is measurement noise rather than meaningful growth.

---

## Compression-level note

Prior handoffs (010, 011, …, 022) used PowerShell `Compress-Archive -CompressionLevel Optimal` (DEFLATE level 6). PowerShell is not directly available from the agent shell on this run, so the same compression characteristics were reproduced via Python's `zipfile.ZipFile(..., ZIP_DEFLATED)` at the default level (also DEFLATE level 6). Cross-validation:

| Level | Bytes | MB |
|---|---|---|
| `ZIP_DEFLATED` level 9 (max) | 46,738,672 | 44.57 |
| `ZIP_DEFLATED` default (level 6) | 46,930,068 | 44.76 |
| Prior baseline (PS `Optimal`, task 022) | 46,929,510 | 44.75 |

The level-6 Python measurement (46,930,068) is within 558 bytes of the prior PowerShell measurement (46,929,510), confirming the compression is comparable. **Reported value for the project trajectory uses the level-6 measurement (44.76 MB).**

---

## Cumulative project trajectory

| Wave | Compressed | Δ vs prior |
|---|---|---|
| Phase 0 baseline | 44.75 MB | — |
| 010 | 44.75 MB | 0.00 |
| 011 | 44.75 MB | 0.00 |
| 012 | 44.75 MB | 0.00 |
| 013 | 44.75 MB | 0.00 |
| 015 | 44.75 MB | 0.00 |
| 016 | 44.75 MB | 0.00 |
| 017 | 44.75 MB | 0.00 |
| 018 | 44.75 MB | 0.00 |
| 019 | 44.75 MB | 0.00 |
| 020 | 44.75 MB | 0.00 |
| 022 | 44.75 MB | 0.00 |
| **024** | **44.76 MB** | **+0.01** (noise) |

Headroom vs NFR-01 ceiling: **60.00 − 44.76 = 15.24 MB** (25.4% of budget remains).

---

## Notes

- Telemetry adds ZERO runtime allocations on the deprecated endpoint when the deprecation window closes (the endpoint will be deleted; cf. task 024 note: deletion is later stabilization-window action).
- Activity tags (`deprecated.endpoint`, `deprecated.name`) leverage the request-scoped `Activity.Current` from the ASP.NET Core hosting pipeline — no new `ActivitySource` instance is registered.
- Tier-1 safe per ADR-015 — payload contains only stable identifiers (playbook name, tenant id, endpoint marker, User-Agent). Per-call audit confirmed in `PlaybookByNameDeprecationTests.ByName_EmitsExactlyOneWarning_PerCall_WithTier1SafePayload`.
