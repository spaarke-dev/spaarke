# Current Task — sdap-SPE-admin-app-r2

> **Purpose**: active-task state for context recovery. Tracks ONLY the current task.
> History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md); detail in each `.poml`.

---

## Active Task

| Field | Value |
|---|---|
| **Task** | **005** — Diagnose + fix Audit Log (last of Wave W1) |
| **Status** | not-started |
| **Phase** | 1 — Workstream A (Make failures visible) |
| **Rigor** | FULL (all three) |
| **Started** | — |

## Next Action

Dispatch **Wave W1**. 001 ✅ unblocked all three. Per `plan.md` §3 they can run together — different files:

| Task | Owns | ∥-safe |
|---|---|---|
| **002** — audit 70 `catch (ODataError)` sites | `SpeAdminGraphService.cs` (the wave's single GraphService task) | ❌ main session |
| 003 — Sync Status reflects real outcomes | DashboardSync + client | ✅ |
| 005 — diagnose + fix Audit Log | AuditService | ✅ |

Say **"continue"** or **"work on wave 1"**. ONE message with THREE `task-execute` invocations.

---

## Steps Completed

_(task 001 complete — see [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) and
[`notes/task-001-completion.md`](notes/task-001-completion.md))_

## Files Modified

_(reset for next task)_

## Decisions Made

_(reset — task 001's are recorded in [`notes/task-001-completion.md`](notes/task-001-completion.md))_

## Blockers

**None blocking W1.** One carried defect, not on the W1 path:

🔧 **The `SpeAdminApp` code page does not build** — `@microsoft/applicationinsights-web` is imported by the
shared `Spaarke.UI.Components/src/services/AppInsightsService.ts` but is not a declared dependency of
`SpeAdminApp` and is not installed. **Pre-existing** (reproduced on a clean tree), not caused by task 001.
Blocks all browser-based `<ui-tests>` and any "verify against Spaarke Dev" step. Task **003** is the first
W1 task that touches the client — fold the fix in there, or handle alongside task 060 (hygiene).

---

## Session Context

### Task 001 outcome (2026-08-21)

60 SpeAdmin error sites now report what Graph/Dataverse actually said. Zero misleading cause-assertions
remain; zero unredacted `ex.Message` payloads remain. Build 0 errors · 10,564 tests pass (+28 new) ·
ArchTests 36/36 · publish 43.68 MB compressed (under the ~44.96 MB baseline) · no new NuGet.

**What task 001 could NOT achieve, and why 002 matters:** 28 of the 70 `catch (ODataError)` sites in
`SpeAdminGraphService.cs` **swallow** the error (13 → `null`, 11 → empty/default, 4 rethrow-other). On those
paths there is no error to surface — the caller cannot distinguish *absent* from *failed*. Those screens stay
silent until 002 lands. "The app tells the truth" is currently true only for the 42 translating paths.

**Reusable mechanism 002–005 should build on** (do not reinvent):

| Helper | Use |
|---|---|
| `SpaarkeStorageException.GraphRequestId` | Graph request id, populated by `ToSpaarkeStorageException` |
| `GraphErrorTranslator.ToProblemDetails(summary, errorCode, statusCode, traceId, title)` | Graph failures — carries code, upstream status, request id, traceId |
| `GraphErrorTranslator.ClientStatusFor(ex)` | Upstream→client status; maps Graph 401→502 so the client retry loop cannot swallow it |
| `ProblemDetailsHelper.Explain(summary, ex)` | Non-Graph failures — appends real type+message, redacted |
| `ProblemDetailsHelper.Redact(message)` | **Always** apply before putting upstream text in a payload |
| `describeApiError(err, fallback)` (`speApiClient.ts`) | Client render sites — appends Graph code + request id |

> ⚠️ A summary passed to these helpers **must not name a cause the caught exception did not establish.**
> That is the defect the project exists to remove.

### Carry-forward — read before starting later tasks

1. **🔔 Task 010 can reopen the auth decision.** The §6.5 ADR gate is resolved as **path C** (comply under
   ADR-028 E-1), but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Tasks 004/005 are uncapped.** Search and Audit Log root causes are not isolated; effort is provisional.
4. **Task 040 was pulled forward** from Workstream D into Phase 2, so Phase 3's property fixes land
   protected. Deliberate deviation from spec §5 ordering — rationale in `plan.md` §4 Phase 2.
5. **Live-tenant safety**: destructive tests need a dedicated throwaway container. Existing containers hold
   real documents.
6. **A POML's `<relevant-files>` can be incomplete.** Task 001's named 5 endpoint files; there are 18, and
   the real scope was 60 sites, not 41. Under `mode="directional"` the `<goal>` binds, not the file list.
