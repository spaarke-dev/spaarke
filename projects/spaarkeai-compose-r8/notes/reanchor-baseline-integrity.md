# Task 014 — Re-anchor baseline integrity (FR-S07)

> Completed 2026-08-20. BFF-only: `ComposeService.cs`, `IComposeService.cs`, `ComposeEndpoints.cs`,
> `ComposeSaveTelemetry.cs`, one seam test, one waiver re-baseline.

---

## The defect, and why it is the worst one in Track S

`ReanchorStaleSaveAsync` re-downloads the CURRENT bytes so the operation log can be rebased onto them.
When that download failed — either by throwing or by returning `null` — it returned `originalBaseline`,
the **load-time** bytes, and the caller assigned them straight to `contentToPersist` and saved.

This method is only ever reached when `baseMoved` is true, i.e. when we have **already observed** that
the stored version is newer than ours. So the fallback bytes were, by construction, older than the
version they were about to replace. The save then reported HTTP 200.

Its own comment claimed the fallback "fails closed: every op/comment surfaces as ORPHAN". That was
true — **of the ops**. It was the **bytes** that were wrong. Reporting the operations honestly while
writing a stale document is exactly the Half-A/Half-B confusion this project exists to remove, and it
is why this survived a release whose other nine save defects are all client-contract issues.

`If-Match` did not protect against it either: the precondition carries `preWriteETag`, the LIVE etag
read at save time, so the PUT matched and landed. If-Match closes the read-to-write window; it cannot
catch a save that deliberately decided to write stale content.

## Empirical reproduction BEFORE the fix (bff-extensions.md § F.3)

The seam test was written first and run against the unfixed code. Both arrangements failed with a real
`.docx` captured at the SPE boundary:

```
Failed ... Save_StaleBase_ReanchorDownloadFails_WritesNothing_RefusesStale_ThroughTheWire(downloadThrows: True)
  Expected persistedAfterSeed to be <null> ..., but found {0x50, 0x4B, 0x03, 0x04, ... 1286 more}
Failed ... (downloadThrows: False)   — same
Failed! - Failed: 2, Passed: 5, Total: 7
```

`0x50 0x4B 0x03 0x04` is the ZIP magic — a whole Word document was written over a version already known
to be newer. Not hypothetical.

## The fix

The fallback is **deleted**, not guarded. A conditional destructive path is still a destructive path,
and a re-anchor with no current bytes cannot produce a correct save under any condition, so there is no
version of it worth keeping. Both failure modes now throw
`ComposeStaleBaselineUnavailableException(documentSpeId, reason)` with a bounded `reason`
(`download-faulted` / `download-empty`).

The endpoint maps it to **HTTP 409** + `refused-stale` + telemetry cause `baseline-download`.

### Why `refused-stale` and not `storage-failed`

The failing call is a storage READ, which pulls toward `storage-failed`. But that member's defining
property is "the storage ATTEMPT failed" — and no write was attempted here. The stored version is
provably untouched. Telling the user their document may be damaged when it is intact would be its own
dishonest outcome, of exactly the class FR-S06 exists to prevent.

`refused-stale`'s defining property is "refused because the base moved; nothing written, nothing
overwritten" — which is precisely this. The fact that a failed read caused it rides the telemetry
**cause** dimension, which is what the second dimension is for. The enum's doc comment was corrected to
list both producers, because it previously described only the task-011 one and asserted "NOT a storage
fault", which is untrue of producer (2). That is a documentation correction, not a widening of the
closed set — no member was added (FR-S06 discipline).

### Why 409 and not 412

Nothing about the CALLER's state is stale in a way reloading fixes, and a re-download failure is
usually transient. The honest instruction is "try again", which is 409's semantics, and it matches the
sibling precondition refusal task 011 already maps to 409.

## Step 4 — every source of the bytes the writer sees

`contentToPersist` has exactly **two** assignment sites that introduce a SOURCE; every other assignment
is an in-place transform of bytes already resolved.

| Line | Assignment | Source or transform | Stale-safe? |
|---|---|---|---|
| 1105 | `ResolveSaveBaselineAsync(...)` | SOURCE | Yes — retained client bytes, a version fetched by id, or a freshly rendered model. Runs before any staleness is known; a moved base is handled downstream. |
| 1273 | `patchedBytes` from `ReanchorStaleSaveAsync` | SOURCE | **Was the defect.** Now either the freshly-downloaded current bytes or a terminal refusal — never the load-time baseline. |
| 1300 | `_baselineParaIdStamper.Stamp(contentToPersist, …)` | transform | n/a |
| 1319 | `_patchEngine.Apply(contentToPersist, …)` | transform | n/a |
| 1360 | `bestEffortBytes` (partial-apply recovery) | transform | n/a |
| 1399 | `_documentRenderer.AppendSection(contentToPersist, …)` | transform | n/a |

The two surviving `BuildAllOrphanSummary` returns inside `ReanchorStaleSaveAsync` (the
paragraph-corpus read failure, and the normal end) both return `currentBytes` — the fresh download — so
the invariant holds: **the bytes are either freshly downloaded or the save terminates.**

## Escalation trigger — checked, did NOT fire

The trigger asked whether deleting the fallback breaks a legitimate non-destructive scenario, e.g. a
first save of an Authored document with no prior version to download. It cannot: this branch requires
`baseMoved`, which requires a successful live-metadata read AND a differing baseline etag. A brand-new
item has no moved base and never enters here. A download that fails immediately after a successful
metadata read on the same item is a genuine fault, not an absent version.

## Verification

- Seam suite (`ConcurrencySaveSeamTests`): **7/7 pass**, including test #1 — a stale-base re-anchor
  whose download SUCCEEDS still saves and applies the AUTO-band op. That is the no-regression criterion.
- All Compose tests in `Sprk.Bff.Api.Tests`: **1133/1133 pass**.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 7 pre-existing warnings.
- **Publish size: 43.68 MB compressed incl. PDBs** (4 PDBs), measured by zipping the publish output.
  Identical to the task-012-era measurement — **0.00 MB delta**, and 16.3 MB under the 60 MB ceiling.
  (Measure compressed; raw bytes read ~137 MB and look catastrophic.)
- `dotnet list package --vulnerable --include-transitive`: **no vulnerable packages**. No NuGet added.
- No new DI registration (ADR-010 budget untouched). `AnnotationReanchorService` **unmodified** — the
  ADR-sanctioned fuzzy re-anchor is KEEP; this task changed what happens when the DOWNLOAD fails.
- No HTTP 422 introduced (ADR-049).

## Finding: the god-class ratchet was ALREADY RED before this task

`dotnet test tests/Spaarke.ArchTests` fails on **three** files. Measured on clean HEAD (`eeac5e0c1`) by
stashing, so this is not an assertion:

| File | Frozen | At HEAD | After 014 | Whose |
|---|---|---|---|---|
| `Services/Compose/ComposeService.cs` | 3,573 | **3,769** | 3,785 | compose-r8 tasks 011/013, +16 here |
| `Api/ComposeEndpoints.cs` | 2,651 | **2,819** | 2,845 | compose-r8 tasks 011/013, +26 here |
| `Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 2,864 | **2,975** | 2,975 | **NOT this project** — `a76e7e714` / `e3e72af91` (Dataverse MI migration) |

Two were already breached by this project's earlier tasks and nobody noticed, because the CI job that
runs NetArchTest (`code-quality`) carries `continue-on-error: true` — the same "gate that enforces
nothing" pattern task 018 exists to close, showing up in a second place.

**Action taken**: the two Compose waivers are RE-BASELINED with a documented reason in
`GodClassGuardTests.cs` (never silently — the pattern's rule). Decomposition is not deferred
indefinitely: Track D tasks **070** and **073** own the split and DELETE these waivers. Doing it now
would churn the exact methods tasks 015/016 still have to edit, mid-P0-track.

**Left alone deliberately**: `DataverseServiceClientImpl.cs`. It is another project's breach, and
re-baselining another team's waiver is their call, not mine. **The ArchTest therefore still fails on
that one file** — it failed before this task and it fails after, unchanged. It needs an owner.

## Files changed

| File | Change |
|---|---|
| `Services/Compose/ComposeService.cs` | the two destructive `return (originalBaseline, …)` fallbacks DELETED, replaced by a typed refusal throw, with the rationale recorded inline |
| `Services/Compose/IComposeService.cs` | NEW `ComposeStaleBaselineUnavailableException`; `RefusedStale` doc corrected to list both producers |
| `Api/ComposeEndpoints.cs` | catch → telemetry(`refused-stale`, `baseline-download`) → 409 ProblemDetails |
| `Telemetry/ComposeSaveTelemetry.cs` | NEW bounded cause `baseline-download` |
| `tests/integration/seam/Compose/ConcurrencySaveSeamTests.cs` | NEW `[Theory]` 1c — both download-failure modes; asserts no write, 409, honest detail |
| `tests/Spaarke.ArchTests/GodClassGuardTests.cs` | two Compose waivers re-baselined WITH reasons pointing at Track D 070/073 |
