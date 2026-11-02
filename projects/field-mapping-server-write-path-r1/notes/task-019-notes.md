# Task 019 — W2 gate: notes

> Rigor FULL. Tags: deploy, bff-api, security, review. Started 2026-11-02.
> **Deviation from the POML (standing rule):** step 4's deploy does NOT run from this session. The dev BFF is deployed
> by R3 in a batched candidate, with the owner's go in R3's session. This session runs steps 1–3, then the live checks
> (5–8) once dev carries the W2 commits, with the owner's OK for the test-row creates.

## W2 change set reviewed

Every W2 merge on master, up to `origin/master` 1c919560c3:
- #2282, #2287, #2288 (1fa0cf5cb8..1d8cf7b794);
- #2366 073, #2367 070, #2370 037, #2376 038;
- #2392 14b9335a6c, #2435 6b0c1e2fed;
- #2442 071, #2459 072, #2383 074;
- #2486 017 b083d62183;
- #2504 039 0e3962069b.

## Adversarial pass 1 (Opus, fresh agent, 2026-11-02, over 1c919560c3)

The reviewer got the NFR-01 focus list and the ADR-008 path-A note.

**Result: 0 BLOCKING and no authorization bypass or existence oracle.** It ran 2509 tests (2509 passed, 3 skipped) and an ArchTests subset (75 passed).

| # | Finding | Where | Class | Disposition |
|---|---|---|---|---|
| P1-1 | The chat create drops the index columns with the UPDATE helper. It logs `client_search_index_dropped` at once, with no record id and the applied value "unchanged", even when the dropped value equals the one step 8 applies. That breaks FR-38's rule to log only when the values differ, and the log has the wrong shape. | `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:394` | F2 (NIT) | **Fixing:** branch `fix/fm-019-chat-drop-log`. The handler drops the columns without logging and passes the dropped values to the service's step-12 log (`RecordCreationService.cs:1462`). |
| P1-2 | A system principal's step 2 checks only the document reason code of `RefuseAppOnlyWrite`. | `RecordCreationService.cs:398` | K2 | Dormant: no app-only caller reaches the service with another table today. Revisit when 033/034 move the system creators onto the service. |
| P1-3 | Repair has no go-live cutoff, so FR-25's "never write a row created before go-live" is not enforced in code yet. | `RecordCreationService.Repair.cs:74` | K2 | Owned by task 052 (the scheduled pass that drives Repair). Repair is not wired to any caller today. **052's brief must carry this item.** |
| P1-4 | An unreadable intermediate parent gives 409 `ParentUndetermined`, not 503. | `RecordCreationService.cs:1051-1055` | K1 | Pre-existing (the deleted core behaved the same). Fails closed; the status code is a known limit. |
| P1-5 | 3 skipped tests: `OfficeEndpointsContractTests.Post_OfficeQuickCreate_*`. | `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs:253,276,298` | K4 | Pre-existing skips from 2026-01 and 2026-03, reason "Requires fully mocked Dataverse services for quick create". The W2 quick-create path is covered by `OfficeRecordOwnershipTests`, `CrossSurfaceOwnerTests` and `SecureRootCreateRouteTests.Office*`. |

## Adversarial pass 2

Pending. It needs a fresh Opus agent over the P1-1 fix diff plus the whole W2 diff.

## Step 1: publish size and CVE

Pending. Measure against a fresh master build that carries the P1-1 fix.

## Live checks A–C and App Insights

Pending. Two things must happen first:
- dev carries 0e3962069b (039) and the P1-1 fix, through R3's batch;
- the owner gives an OK in this session for the `FMSWP-019` test-row creates.
