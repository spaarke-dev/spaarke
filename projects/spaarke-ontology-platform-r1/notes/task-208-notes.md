# Task 208 notes — decision commit reports signal_changed when an item changes twice while it is being closed (#3028, DI-167)

Branch `work/ontology-208-signal-changed` from origin/master `e70ff38894`. Rigor FULL (declared by the POML; `bff-api`, `.cs`). Model sonnet @ high.

## Before → after

| Case (one item's close) | origin/master | This change |
|---|---|---|
| Mismatch on attempt 0, success on attempt 1 | closes | unchanged |
| Mismatch on attempt 0 and on attempt 1 | `signal_close_failed`, metered `decision_commit_signal_close_failed` | `signal_changed` ("The item kept changing while it was being closed; it was left as it is."), logged, **not** metered |
| Mismatch on attempt 0, other exception on attempt 1 | `signal_close_failed`, metered | unchanged |
| Other exception on attempt 0 | `signal_close_failed`, metered | unchanged |
| Cancellation | propagates | unchanged |

Retry count stays one retry. No new outcome code, no response-shape change, no DI or package change. Other items' outcomes and the task 205 per-item semantics are untouched.

## Change

`src/server/api/Sprk.Bff.Api/Services/Signals/DecisionCommitService.cs`, `CloseSignalAsync`:
- a second catch `when (IsVersionMismatch(ex))` (reached only on the last attempt, because the first catch takes attempt 0) `break`s out of the loop, so the existing post-loop `signal_changed` return is now reachable;
- `Warn` gained `countAsFailure` (default true, so every other warning still meters); `signal_changed` passes false. This is needed for the acceptance criterion "telemetry records no decision_commit_signal_close_failed": the post-loop `Warn` call alone would still have incremented `ontology.writer.failures`.

## Tests (ADR-038, `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.RowBatching.cs`)

- `T208_AnItemThatChangesTwiceWhileItIsClosed_ReportsSignalChanged_AndIsNotCountedAsAWriterFailure` — FAILS on master code, passes now (asserts the outcome, `Warnings`, item left open, no update written, and no `ontology.writer.failures` reason via a `MeterListener`).
- `T205_OneItemsRowVersionKeepsChanging_...` (task 205 pinning test) — expectation changed from `signal_close_failed` to `signal_changed`; FAILS on master code, passes now.
- Must-not-fire controls (pass on both): `T208_OneMismatchThenSuccess_StillClosesTheItem_AndMetersNothing`; `T208_AMismatchThenAnotherKindOfFailure_IsStillASignalCloseFailed_AndCounted`.
- Failure-before evidence: master source + new tests = 2 failed / 2 passed (4 run).

Results after the change:
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Signals|FullyQualifiedName~DecisionCommit"`: 5409 passed, 0 failed, 6 skipped (includes `SignalRoutesContractBaselineTests`; no baseline moved, no baseline pinned this race).
- `tests/Spaarke.ArchTests` (built explicitly first): 2793 passed, 0 failed.

## Publish size (BFF hygiene §10 item 4)

`dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/`, PDBs included, `Compress-Archive -CompressionLevel Optimal` over the publish folder; both from fresh short-path worktrees (`C:\wm208` = origin/master `e70ff38894`, `C:\wb208` = this branch head):

| | files | zip bytes |
|---|---|---|
| master | 194 | 43,573,370 (41.55 MB) |
| branch | 194 | 43,573,123 (41.55 MB) |
| delta | 0 | -247 |

No package added or changed, so no CVE re-run (no new HIGH CVE possible from this diff).

## Consumers of the outcome codes

- `src/client/shared/Spaarke.UI.Components/src/components/DecisionWizard/decisionDisplay.ts` `WARNING_TEXT` already has user text for both `signal_changed` and `signal_close_failed`; its tests (`decisionRecording.test.ts`, `DecisionWizard.test.tsx`) already reference them. No client change needed.
- A built copy sits in `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_spaarkeai` (generated bundle; not edited).
- No MCP or other consumer of these codes outside this repo's shared lib was found (`git grep` over `src`, `tests`, docs).

## Review (Step 9.5)

Self-review of the 4-line source diff plus tests: no F-class finding. ADR-038 (test fails before, controls present), no new service/interface/registration (§11), no package change (no CVE re-run needed). Class grep (`attempt == 0` with a retry): `ApExportService`, `ApExportDeliveryService`, `InquiryDispositionService`, `ContactIdentityBinder` are different loops that handle their second attempt on purpose; no same-class defect found.

rework: none task 208
