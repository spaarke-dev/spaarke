# Current Task — sdap-SPE-admin-app-r2

> **Purpose**: active-task state for context recovery. Tracks ONLY the current task.
> History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md); detail in each `.poml`.

---

## Active Task

| Field | Value |
|---|---|
| **Task** | **005** — Diagnose + fix Audit Log ([`tasks/005-fix-audit-log.poml`](tasks/005-fix-audit-log.poml)) |
| **Status** | not-started |
| **Phase** | 1 — Workstream A · Wave W1 (last of three) |
| **Rigor** | FULL · sonnet @ **xhigh** · owns `SpeAuditService` |
| **Started** | — |

## Next Action

Run task **005**. It is the last W1 task; 002 ✅ and 003 ✅ are done. Then Wave W2 (004, 010, 040).

⚠️ **005 is uncapped** — the Audit Log root cause is not isolated. Its `<escalation>` trigger is a legitimate
stop if the cause turns out to be outside the task's scope.

---

## Steps Completed

_(W1: 002 ✅ · 003 ✅ — see [`TASK-INDEX.md`](tasks/TASK-INDEX.md) and the per-task notes)_

## Files Modified

_(reset for task 005)_

## Decisions Made

_(reset — recorded per task in `notes/task-00N-completion.md`)_

## Blockers

_(none)_

✅ **Resolved**: the `SpeAdminApp` code-page build was broken by four undeclared shared-library dependencies
(`@microsoft/applicationinsights-web`, `@hello-pangea/dnd`, `mammoth`, `pdfjs-dist`) plus two missing vite
aliases. Fixed in `753c9ebc1`. The page builds (`✓ built in 23.24s`).

> ⚠️ Building is **not** the same as being deployed. Every `<ui-tests>` block still needs a **deployed** app
> (and a `--chrome` session) to run. Tasks 001 and 003 both recorded their UI criteria as NOT DONE for that
> reason. This will keep accruing until someone deploys — worth deciding before Workstream C.

---

## Session Context

### 🔑 The load-bearing correction from task 002

**Do not re-derive this.** The 70 `catch (ODataError)` sites in `SpeAdminGraphService.cs` are **already
correct**. They implement a two-layer design: the inner `XAsync` catches **only 404** (`when`-filtered) and
returns null/false; the outer `XForConfigAsync` translates everything else to `SpaarkeStorageException`. A
403/429/5xx is never swallowed.

An earlier note in task 001 claimed *"28 of 70 swallow the error — those screens stay silent until 002
lands."* **That was wrong** (it did not check wrapper pairing) and is corrected in
[`notes/task-001-completion.md`](notes/task-001-completion.md) and
[`notes/odata-catch-inventory.md`](notes/odata-catch-inventory.md).

**Implication for 004/005**: the empty-grid / silent-failure symptom does **not** come from error swallowing
in the Graph service. Look elsewhere — the pattern found twice so far is a **lower layer collapsing a failure
into an empty/absent result** that an upper layer reads as success:

| Task | Where the truth was lost |
|---|---|
| 003 | `LoadContainerTypeConfigsAsync` returned `Array.Empty<>()` on a Dataverse exception → indistinguishable from "none registered" → `SyncSucceeded = true` |
| 002 | `BulkOperationService` caught raw `ODataError` (ADR-007 leak, since fixed) |

**Start 004/005 by looking for that same shape**, not by re-auditing catch blocks.

### Reusable mechanism — do not reinvent

| Helper | Use |
|---|---|
| `GraphErrorTranslator.ToProblemDetails(summary, errorCode, statusCode, traceId, title)` | Graph failures — carries code, upstream status, request id, traceId |
| `GraphErrorTranslator.ClientStatusFor(ex)` | Upstream→client status; maps Graph **401 → 502** so the client retry loop cannot swallow it |
| `ProblemDetailsHelper.Explain(summary, ex)` | Non-Graph failures — appends real type + message, redacted |
| `ProblemDetailsHelper.Redact(message)` | **Always** apply before putting upstream text in a payload |
| `GraphCallScope.Run(...)` / `.RunForConfig(...)` | Keeps `ODataError` inside `Infrastructure.Graph` (ADR-007 §1) |
| `SpeDashboardSyncService.DeriveHealth(concerns)` | The "a failed concern can never report Healthy" rule |
| `describeApiError(err, fallback)` (`speApiClient.ts`) | Client render sites — appends Graph code + request id |

> ⚠️ A summary passed to these helpers **must not name a cause the caught exception did not establish.**
> That is the defect this project exists to remove.

### Carry-forward

1. **🔔 Task 010 can reopen the auth decision.** The §6.5 gate is resolved as **path C** (comply under
   ADR-028 E-1), but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Tasks 004/005 are uncapped.** Root causes not isolated; effort is provisional.
4. **Task 040 was pulled forward** into Phase 2 — it is also the harness that would let 002's and 003's
   deferred empirical criteria actually be proven.
5. **Live-tenant safety**: destructive tests need a dedicated throwaway container. Existing containers hold
   real documents.
6. **A POML's premise can be wrong.** Task 001's `<relevant-files>` named 5 of 18 endpoint files (real scope:
   60 sites, not 41). Task 002's premise did not hold at all. Task 003's held only one layer down. Under
   `mode="directional"` the `<goal>` binds — **verify the premise before implementing to it.**
7. **Residual ADR-007**: `BulkOperationService` still holds two `Microsoft.Graph.GraphServiceClient` locals.
   Structural work for `speadmingraphservice-decomposition-r1`; recorded in the odata inventory.
