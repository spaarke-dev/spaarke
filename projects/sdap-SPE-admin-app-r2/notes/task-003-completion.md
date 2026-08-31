# Task 003 — completion record

> **Completed**: 2026-08-21 · **Rigor**: FULL · **Spec**: FR-A03

## The defect, precisely

The POML said Sync Status *"reads OK regardless of whether the underlying concerns returned data."* That was
**partly** wrong and **partly** right, and the distinction is the whole task.

**Already worked**: the service tracked `failedConfigs`, set `SyncSucceeded`, wrote a `SyncStatus` line; the
endpoint passed both through; the UI rendered `syncSucceeded ? "OK" : "Partial"`. A *Graph* failure on one
config did already show "Partial".

**The real defect** was one layer down, at `LoadContainerTypeConfigsAsync`:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to load container type configs from Dataverse.");
    return Array.Empty<SpeAdminGraphService.ContainerTypeConfig>();   // ← indistinguishable
}
```

An empty list from a **Dataverse outage** was indistinguishable from an empty list meaning **"no configs
registered"** — and the caller mapped the empty case to `SyncSucceeded = true` with the message
*"No container type configs registered."*

**So: Dataverse down → Sync Status "OK".** A green dashboard over an app that could not read its own
configuration. That is spec §2.4's systemic defect exactly, and it was invisible to every existing signal.

Two smaller instances of the same shape, also fixed:

- Config records skipped as incomplete were a `LogWarning` only — the dashboard silently covered fewer
  configs than the operator had registered, and still read OK.
- A per-config Graph failure incremented a counter, so the UI could say *"1 failed"* but not **which** config
  or **why**. The reason existed only in the server log.

## What changed

| File | Change |
|---|---|
| `Services/SpeAdmin/SpeDashboardSyncService.cs` | + `SyncHealth` enum (Healthy/Degraded/Failed) · + `ConcernOutcome` record · + `ConfigLoadResult` (separates "load failed" from "none registered") · + **`DeriveHealth`** (public pure domain rule) · + `Summarize` (single construction point) |
| `Api/SpeAdmin/DashboardEndpoints.cs` | unchanged — already passed metrics through wholesale |
| `types/spe.ts` | + `SyncHealth`, `ConcernOutcome`; `DashboardMetrics` extended |
| `components/dashboard/DashboardPage.tsx` | health-driven tile (value + semantic colour + icon) · named failing-concern list in a `MessageBar` · `resolveSyncHealth` back-compat guard |
| `tests/unit/domain/SpeAdmin/DashboardSyncHealthTests.cs` | **new** — 9 tests |

`SyncSucceeded` is retained as a derived mirror of `SyncHealth == Healthy`, so nothing consuming the old
field breaks.

### The stale-cache trap

`DashboardMetrics` is cached in Redis for **2× the sync interval**. Immediately after deploy, cached payloads
have no `syncHealth` field. Treating that `undefined` as "Healthy" would reintroduce the exact optimistic
default this task removes — so `resolveSyncHealth` falls back to the legacy boolean and degrades an
unrecognised value to **"Degraded", never "OK"**.

## Verification

| Gate | Result |
|---|---|
| `dotnet build` | ✅ 0 errors |
| Unit tests | ✅ **10,573 passed**, 0 failed (+9 new) |
| ArchTests | ✅ 36/36 |
| Code page build | ✅ `✓ built in 23.24s` |
| Publish size | ✅ **43.68 MB** compressed (unchanged; ceiling 60) |
| ADR-021 — no hex literals | ✅ Fluent semantic palette tokens only (`colorPaletteGreen/Yellow/RedForeground1`) |
| ADR-019 — failures as ProblemDetails | ✅ endpoint already compliant; concern reasons pass through task-001 `Explain`/`Redact` |
| No optimistic default remains | ✅ `SyncSucceeded` is derived in `Summarize`, never assigned literally |

**Placement Justification (CLAUDE.md §10)**: modify-only within existing BFF files, plus two nested types on
the existing service. No new service, endpoint, DI registration, or package.

## ⚠️ Not verified — POML step 7 and the three `<ui-tests>`

The code page now **builds** (fixed in `753c9ebc1`), but the `<ui-tests>` require a **deployed** SPE Admin app
plus the ability to force a concern to fail against Spaarke Dev. Neither is available here, and there is no
`--chrome` session.

- POML **step 7** ("with a concern deliberately failing, the dashboard does not read OK") — **NOT DONE**.
- Acceptance criteria 1–3 and 5 are verified at the **logic and type** layer (9 tests over the health rule,
  token-only colours) but **not** rendered in a browser.

`DeriveHealth` was deliberately made a public pure function precisely so the load-bearing rule is protected by
executable tests rather than resting entirely on a manual browser check.
