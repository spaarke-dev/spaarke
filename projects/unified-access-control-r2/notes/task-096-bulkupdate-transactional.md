# Task 096 — `BulkUpdateAsync` is genuinely all-or-nothing

> **Date**: 2026-09-10 (session 7) · **Commits**: `3570d24e4` (fix) + review follow-up · **Issue**: ISS-005 / GitHub #970
> **Design**: `notes/decisions/external-grant-expiry-mandatory.md` §12.2 · **Consumed by**: task 098
> **Follow-ups filed**: ISS-006 / #971 (SDK `LastException` race, system-wide) · ISS-007 / #972 (layout caller)

## What changed

`IGenericEntityService.BulkUpdateAsync` sent an `ExecuteMultipleRequest` with `ContinueOnError = false`
under the comment *"Stop on first error for transactional behavior"*. `ExecuteMultiple` is **not**
transactional: it stopped at the first fault with every earlier update already committed, and its error
message implied nothing had happened. It now sends **one `ExecuteTransactionRequest`**; Dataverse rolls
the whole set back on any fault.

| | Before | After |
|---|---|---|
| Message | `ExecuteMultipleRequest` (ContinueOnError=false) | `ExecuteTransactionRequest` |
| Partial failure | earlier rows committed, silently | impossible — all or none |
| Call | `Task.Run(() => _serviceClient.Execute(...), ct)` | `_serviceClient.ExecuteAsync(..., ct)` (as `UpsertAsync` and the KPI batch in the same file) |
| Fault naming an in-range request (`ExecuteTransactionFault`) | `"Failed to bulk update X records: <msg>"` — implies nothing happened | names the **request index** and **record**; says **NO update was applied** |
| Any other Dataverse fault | same message | the atomicity guarantee only — all or none, never partial (see #971 for why no stronger claim is safe) |
| No fault at all (timeout) | same message | **outcome unknown — all or none, never partial** (a timeout can land after the commit) |
| Cancellation | wrapped in `InvalidOperationException` | propagates as `OperationCanceledException` (so `DailySendCountResetService`'s shutdown catch now matches) |
| `DBNull.Value` in a field | reached serialization, failed client-side, reported as a failed update | `ArgumentException` before anything is sent, pointing to `UpdateAsync` |
| Signature | — | unchanged |

Both callers compile unchanged: `CommunicationAccountService.cs:239` (daily `sprk_sendstoday` reset) and
`WorkspaceLayoutService.cs:946` (clearing a user's other default layouts). ⚠️ The layout caller catches
the failure and writes the new default anyway, so **it can still end up with two defaults** — atomicity
fixed the helper, not that caller's policy. Filed as ISS-007 / #972 (the design note's claim is corrected).

## Escalation — fired at step 1, resolved by the owner

The POML's trigger: *"If the request sent by BulkUpdateAsync cannot be observed at a substitutable seam
without reflection (B8) or transport mocking (B1), STOP and escalate."* It fired:

- `DataverseServiceClientImpl` builds its `ServiceClient` **inside the constructor** from configuration
  (`Lazy<ServiceClient>`); there is no injection point.
- `ServiceClient.Execute` is `virtual final` — an interface implementation that cannot be overridden
  (verified by reflection on `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32).

**Owner decision (2026-09-10): "pure builder".** Two `public static`, no-I/O functions —
`BuildBulkUpdateTransaction` and `DescribeBulkUpdateFailure` — tested directly under
`tests/unit/domain/Dataverse/`. This is the class's own precedent (`StageAnalysisRegardingFields`, tested
by `AnalysisRegardingWriteTests` for the same reason). Rejected: a constructor seam taking
`IOrganizationServiceAsync2` (a design change to a shared base-layer class — `_serviceClient` is typed as
the concrete `ServiceClient` at ~40 call sites and in the public `OrganizationService` property), and a
source-scan guard alone (cannot test the failure-wording criterion).

**The accepted gap**: the wiring in `BulkUpdateAsync` (build → `ExecuteAsync` → describe the failure) is
covered by review, not a test. Reverting *inside* `BulkUpdateAsync` while bypassing the builder would fail
no test — by construction, and by the owner's explicit acceptance. The review read those lines closely.

## Conflict check (hot path)

`/conflict-check` run at step 0: **no open PR and no other worktree** touches
`DataverseServiceClientImpl.cs` or `IGenericEntityService.cs`. Silent pass.

## Perturbation (mandatory)

Committed first (`3570d24e4`), then each perturbation applied and reverted with `git checkout`:

| # | Perturbation | Result |
|---|---|---|
| P1 | Failure wording: "The transaction was rolled back — NO updates were applied." → "Some updates may have been applied." | **2 of 5 tests fail** (`..._WhenTheTransactionFaults_...`, `..._WhenTheFaultArrivesWrapped_...`) |
| P2 | Builder reverted to `ExecuteMultipleRequest` (ContinueOnError=false) | **test project fails to compile** — 4 × CS1061 in `BulkUpdateTransactionTests.cs` (`OrganizationRequest` has no `Requests`). Detected at build time: the concrete return type `ExecuteTransactionRequest` makes the regression unrepresentable without a visible signature change |

Tree restored after each; `git diff` vs HEAD empty.

## Review (Step 9.5) — code-review + adr-check, and what was done

No Critical findings; **no ADR violations** (ADR-010: no new interface or registration; ADR-038: KEEP path,
no B1–B17 bans). The reviewer read the SDK 1.1.32 source and Microsoft Learn.

| # | Finding | Decision |
|---|---|---|
| W1 | Docs said the transaction "cannot be nested inside ExecuteMultiple" — **backwards**. Learn: an `ExecuteMultiple` may contain transactions; a transaction may not contain `ExecuteMultiple`/`ExecuteTransaction`. The error came from this project's own design note | **Fixed** in both docs **and** the design note (§12.2) |
| W2 | The SDK throws the client-wide `LastException`; with the singleton client, a failed call can throw **another request's** exception. Verified in source (`if (resp == null) throw LastException;`; a plain logger property reset per call). "NO update was applied" could then be false | **Fixed for this method**: the definite claim is made only for an `ExecuteTransactionFault` naming an in-range request; any other fault gets the atomicity guarantee alone. The race is **system-wide** (e.g. `AssociateAsync` treats another request's "duplicate" error as success) → **filed ISS-006 / #971**, not fixed here |
| W3 | Atomicity does not prevent two default layouts — the caller catches and proceeds | **Filed ISS-007 / #972**; claim corrected in these notes and the design note |
| S1–S5 | Interface wording overclaimed (unconditional index naming; unverified "1,000 rejected"; "ONE round trip" though the SDK retries; `DBNull` consequence unstated; "ran as ONE transaction" for failures before sending) | **Fixed** (wording made exact; `DBNull` now rejected) |
| S6 | Cancellation wrapped as `InvalidOperationException` (pre-existing) | **Fixed** — `catch … when (ex is not OperationCanceledException)` |
| S7 | Structured `{RecordCount}` / `{EntityLogicalName}` dropped from the error log | **Fixed** |
| S8 | Interface contract cross-referenced one implementation's helpers | **Fixed** — mechanism moved to the impl remarks |
| S9 | A null `fields` dictionary threw a raw `NullReferenceException` | **Fixed** — `ArgumentException` with the index |
| S10 | Null checks on non-nullable parameters | Accepted — kept for parity with the old public behaviour |
| T1 | The "fault names no request" branch was untested | **Added** a test (also asserts it does NOT claim nothing was applied) |
| T2 | `ContainInOrder` + count → `Equal` | **Done** |
| P1 | No recorded `/conflict-check` result | **Recorded** above |

## Publish size (CLAUDE.md §10, hazards 1–4)

Both sides from **fresh worktrees at short paths** (`C:\wt096m` = `origin/master`, `C:\wt096b` =
`3570d24e4`; branch 0 behind master), `dotnet publish -c Release`, zipped with **`Compress-Archive`**
(the `Deploy-BffApi.ps1` method), incl. PDBs:

| Side | Files | Zip |
|---|---|---|
| master | 214 | **45.35 MB** |
| branch @ `3570d24e4` | 214 | **45.40 MB** |
| delta | equal file counts ✅ | **+0.05 MB** — the whole project's cumulative delta; identical to the session-6 branch figure, so task 096 itself contributes ≈ 0.00 MB |

≤ 60 MB ceiling ✅. No package added, so no new CVE surface. (The review follow-up changes only method
bodies and docs; it cannot move the publish size measurably.)

## Verification

| Run | Result |
|---|---|
| Targeted (`BulkUpdateTransactionTests` + both callers + the precedent) @ `35dd301be` | 74 / 74 |
| Full `Sprk.Bff.Api.Tests` @ `3570d24e4` | 12,265 passed / 0 failed / 58 skipped |
| Full `Sprk.Bff.Api.Tests` @ `35dd301be` | **12,267 passed / 0 failed / 58 skipped** (+2 = the new tests) |
| `Spaarke.ArchTests` (both commits) | 197 / 197 |
| `Sprk.Bff.Api.IntegrationTests` (holds fakes implementing the interface) | builds |
| `scripts/check-task-status-drift.ps1` | 98 POMLs = 98 index rows, no drift |

## Found in passing (not fixed — out of scope)

- **A test helper's doc is wrong** — `tests/integration/.../Helpers/MockServiceClientFactory.cs` says
  `ServiceClient` is *sealed*. It is not (`IsSealed = False`); what makes it unmockable is that `Execute`
  is `virtual final` and the constructors connect. The conclusion is right; the reason is not.
  Docs-vs-reality mismatch #10 for this project (#11 is W1 above — this project's own design note).
- The KPI batch read in the same file (`ExecuteMultipleRequest`, `ContinueOnError = true`) is a correct
  use of `ExecuteMultiple` — independent reads, per-request faults handled. Left alone.
