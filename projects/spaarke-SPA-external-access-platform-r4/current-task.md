# Current Task State

> **Last Updated**: 2026-11-11
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave 1 (P1): 001 + 002, both ✅ done by sub-agents |
| **Step** | Post-wave: build verified (BFF build 0 errors; shared-lib `tsc --noEmit` exit 0) → file defer issues → commit + push |
| **Status** | in-progress (main-session wrap-up of wave 1) |
| **Next Action** | 1) File GitHub issues for D-1, D-2 and D-3 in `notes/defer-issues.md` (label `spaarke-SPA-external-access-platform-r4`) and replace PENDING with the issue numbers. 2) Commit all wave-1 changes (message file + `git commit -F`), push. 3) Tell ebilling-r1 about the overlap (below). 4) Next wave: 003 (sequential, opus) → 005 → 004 → 006 → 007. |

### Uncommitted (all wave-1 work)
- Shared lib: `src/client/shared/Spaarke.UI.Components/src/components/RecordCommandBar/` (new), `components/index.ts`
- BFF: `DocumentOperationsEndpoints.cs`, `Api/ExternalAccess/{ExternalProjectDataEndpoints,ExternalMessageEndpoints,WorkforceRoleCeilingGate}.cs`, `Dtos/ExternalProjectDtos.cs`, `Infrastructure/ExternalAccess/ExternalDataService.cs`
- Tests: ArchTests ledger/census, integration (new `ExternalRootEventCreateContractTests.cs`, `regression/Issue2271_DeleteLockedDocumentProblemDetailsTests.cs`, others modified), unit Events/ExternalAccess
- Project: POMLs 001/002 (completed), TASK-INDEX, `notes/task-001-notes.md`, `notes/task-002-notes.md`, `notes/defer-issues.md`

### Critical Context
- Owner 2026-11-11: "you can use auto fix; continue" → Auto-fix is ON for draft PR #2681; execution started; 002's widened scope (list `canCreate`) accepted.
- 002 results: 1656/1656 filtered BFF tests, 2145/2145 ArchTests; publish +2,459 B; **UAC review PENDING** (four items in `notes/task-002-notes.md`). Must go to uac-r2 before the P1 deploy request (task 018).
- **Hard overlap:** the unmerged `origin/work/ebilling-r1` task-130 batch heavily changes `ExternalProjectDataEndpoints.cs` and `ExternalDataService.cs` (and three more of 002's files). Merge order needs coordinating with ebilling-r1.
- 001: RecordCommandBar, 14/14 tests, full shared-lib suite 6112/6112; ADR-012 Path C approved; second consumer = `Spaarke.Communication.Components/.../EmailToolbar.tsx`. Browser UI tests deferred to task 003.
- D-1 is a real user-visible pre-existing defect (external events are missing the ADR-024 resolver fields), filed rather than fixed. The owner decides whether R4 adds a follow-up task.

## Open owner questions
- D-1: add an R4 follow-up task, or hand it to the event/write-path owner?
- D-2: the `Location` header contract choice.

## Quick Reference
- Project CLAUDE.md, tasks/TASK-INDEX.md, plan.md, spec.md
- PR #2681 (draft, Auto-fix on); Project issue #2680; Epic #2099
