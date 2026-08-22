# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-21 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none in progress** — Wave W1 complete (001 ✅ 002 ✅ 003 ✅ 005 ✅) |
| **Step** | Between waves. Next is **Wave W2**: 040, 004, 010 |
| **Status** | clean — worktree has 0 uncommitted changes; local HEAD == origin |
| **Next Action** | Say `continue`. Recommended order **040 → 004 → 010** (see "Why 040 first"). |

### Files Modified This Session

All committed and pushed to `work/sdap-SPE-admin-app-r2` (draft PR **#811**):

| Commit | Contents |
|---|---|
| `5b3ef6194` | **Task 001** — 60 SpeAdmin error sites routed; `Redact`/`Explain`/`ExtractRequestId`/`ClientStatusFor`; client `describeApiError`; 28 tests |
| `753c9ebc1` | **Build fix** — 4 undeclared deps + 2 vite aliases; SpeAdminApp code page builds again |
| `f3747646b` | **Task 002** — 70-site `catch (ODataError)` inventory; ADR-007 fix in `BulkOperationService` |
| `aa69ce941` | **Task 003** — `SyncHealth`/`ConcernOutcome`; Dataverse-outage-looks-like-OK fixed; 9 tests |
| `356001ee7` | docs refresh |
| `44a239aab` | **Task 005** — Audit Log read **and** write paths repaired; 19 tests |

⚠️ **Separate repo, NOT pushed**: `c:/code_files/spaarke-prototype` has **1 unpushed commit** `a53832a`
(the `spe-admin-r2-uat` harness + shared `_infra` mock fixes) on `feature/uat-harness-framework`. Left
unpushed deliberately — pushing another repo needs the operator's say-so.

### Critical Context

Every real defect found so far has the **same shape**, and **none was where its POML said to look**: a lower
layer collapses a failure into an absent/empty result that an upper layer reads as success. Verify a task's
premise before implementing to it — three of four premises were wrong.

---

## Full State

### Health at checkpoint

| Gate | Value |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors (7 pre-existing warnings) |
| Unit tests | **10,592 passed**, 0 failed, 97 skipped (+56 added this session) |
| ArchTests | 36/36 |
| Publish (compressed, framework-dependent linux-x64) | **43.68 MB** — under the ~44.96 MB baseline, ceiling 60 |
| New NuGet | none |
| CI | **deliberately not tracked** — operator said to disregard at this stage |

### 🔑 The recurring defect shape — three-for-three

| Task | Where the truth was lost |
|---|---|
| **003** | `LoadContainerTypeConfigsAsync` returned `Array.Empty<>()` on a Dataverse exception → indistinguishable from "none registered" → `SyncSucceeded = true` → green dashboard over a broken app |
| **005** | `SpeAuditService` swallowed every write failure → audit table silently **0 rows** for the life of the app |
| **002** | `BulkOperationService` caught raw `ODataError` (ADR-007 leak; fixed) |

**Look for this shape first in 004** — not for error swallowing in the Graph service.

### 🔑 Do not re-derive: the 70 `catch (ODataError)` sites are already correct

Two-layer design — inner `XAsync` catches **only 404** (`when`-filtered) → null/false; outer
`XForConfigAsync` translates everything else to `SpaarkeStorageException`. A 403/429/5xx is never swallowed.

An earlier task-001 note claimed *"28 of 70 swallow — those screens stay silent until 002 lands."* **Wrong**
(it never checked wrapper pairing). Corrected in [`notes/task-001-completion.md`](notes/task-001-completion.md)
and [`notes/odata-catch-inventory.md`](notes/odata-catch-inventory.md).

### Reusable mechanism — do not reinvent

| Helper | Use |
|---|---|
| `GraphErrorTranslator.ToProblemDetails(summary, errorCode, statusCode, traceId, title)` | Graph failures — code, upstream status, request id, traceId |
| `GraphErrorTranslator.ClientStatusFor(ex)` | Upstream→client status; Graph **401 → 502** so the client retry loop cannot swallow it |
| `ProblemDetailsHelper.Explain(summary, ex)` | Non-Graph failures — appends real type + message, redacted |
| `ProblemDetailsHelper.Redact(message)` | **Always** apply before putting upstream text in a payload |
| `GraphCallScope.Run(...)` / `.RunForConfig(...)` | Keeps `ODataError` inside `Infrastructure.Graph` (ADR-007 §1) |
| `SpeDashboardSyncService.DeriveHealth(concerns)` | "A failed concern can never report Healthy" |
| `SpeAuditService.MapCategory(text)` | Free text → `sprk_category` option-set int |
| `describeApiError(err, fallback)` (`speApiClient.ts`) | Client render sites — appends Graph code + request id |

> ⚠️ A summary passed to these **must not name a cause the caught exception did not establish.**

### Why 040 first in Wave W2

Spec §5 orders 004 before 040, but **040 (WireMock) is the unlock**: tasks 002 and 005 both had to record
empirical criteria as unverified because forcing an upstream failure needs a harness, and ADR-038 bans
`Mock<HttpMessageHandler>`. Doing 040 first lets those criteria actually be proven and protects 004's work.

### Standing gap — UI verification

`<ui-tests>` from tasks 001 and 003 are still **NOT DONE**. The code page now *builds*, and a local harness
exists, but neither substitutes for a **deployed** app + `--chrome`.

- **Harness** (`spaarke-prototype/projects/spe-admin-r2-uat`, `npm run dev`, port varies — was **5176**)
  render-verifies task 003's four sync-health scenarios against the *real* `DashboardPage`.
- It **cannot** verify task 001's `authenticatedFetch → ApiError → describeApiError` path: the harness
  aliases `@spaarke/auth` to a mock that always returns 200, so that would test the mock, not the product.

This debt compounds through Workstream C, which is heavily UI. Worth a decision before then.

### Carry-forward

1. **🔔 Task 010 can reopen the auth decision.** §6.5 gate resolved as **path C** (comply under ADR-028 E-1),
   but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently. It is Opus tier / `xhigh`,
   and an `UNWORKABLE` verdict blocks 011 and everything from 020 onward.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Task 004 is uncapped.** Search root cause not isolated; effort provisional.
4. **Live-tenant safety**: destructive tests need a dedicated throwaway container — existing containers hold
   real documents (signed NDAs, Compose drafts, matter files).
5. **A POML's premise can be wrong.** 001's `<relevant-files>` named 5 of 18 endpoint files (real scope 60
   sites, not 41). 002's premise did not hold at all. 003's held only one layer down. 005's pointed at the
   read path when the write path was equally broken. Under `mode="directional"` the `<goal>` binds.
6. **Residual ADR-007**: `BulkOperationService` still holds two `Microsoft.Graph.GraphServiceClient` locals —
   structural work for `speadmingraphservice-decomposition-r1`; recorded in the odata inventory.
7. **Dataverse MCP works** and is how 005's root cause was proven empirically. Reach for it before declaring
   something unverifiable against a live tenant.

### Session notes — key learnings

- **Two mistakes worth not repeating**: (a) `git stash push -- <path>` with nothing to stash creates no
  entry, so a following `git stash pop` pops *someone else's* stash — it dropped another project's WIP into
  this tree (reset, nothing lost); (b) pushing repeatedly cancels your own in-flight CI runs.
- **A confidently-worded wrong comment kept a bug alive for months.** `AuditLogEndpoints.cs:159` asserted
  lookup GUIDs "require single quotes"; 29 of the other 30 lookup filters in `src/` disagreed.
