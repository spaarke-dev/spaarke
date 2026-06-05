# Current Task — Spaarke AI Platform Unification R5

> **Purpose**: Active task state tracker. Managed by `task-execute` skill per CLAUDE.md §7.
> **Status**: 🔴 **PHASE 2 CLOSEOUT REQUIRED** — SC-18 walkthrough surfaced Sev-1 integration gap; remediation tasks 032-035 added.
> **Last updated**: 2026-06-04 (post-walkthrough, remediation plan authored)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **State** | 🔴 BLOCKED on Phase 2 closeout. Backend + frontend integration gap identified during SC-18 walkthrough on Spaarke Dev. 4 new tasks (032-035) added to TASK-INDEX. |
| **PR #345** | ✅ MERGED (commit `01359b36` on master, 2026-06-04 17:49 UTC) |
| **PR #349** | ✅ MERGED (CD platform-test fixes — `Uri.TryCreate` + `Path.GetInvalidFileNameChars`) |
| **R5 deployed to Spaarke Dev** | ✅ via `scripts/Deploy-BffApi.ps1` (manual deploy from operator's workstation). PLUS live-patched `ChatDocumentEndpoints.cs` tid-claim fix (NOT yet on master — needs follow-up PR). |
| **SC-18 walkthrough verdict** | ❌ NOT-USABLE for Phase 1.5 acceptance. Sev-1 root cause: `ChatDocumentEndpoints.UploadDocumentAsync` writes to Redis only; never calls R5's `IndexSessionFileAsync` or populates `ChatSession.UploadedFiles[]`. Chat agent then doesn't see uploaded files. |
| **Phase 2 closeout** | 4 tasks: **032** (wire upload→session-files), **033** (chat context surfaces UploadedFiles), **034** (frontend auto-trigger on upload), **035** (re-run walkthrough + signoff). Tasks 032+033 can run in parallel; 034 needs both deployed first; 035 needs all three. |
| **Next action when resuming** | Execute task 032 (or 032+033 in parallel) via `task-execute` skill. See remediation plan for full context. |

---

## Walkthrough findings (Sev-2 fixed + Sev-1 deferred to closeout)

### ✅ Sev-2 #1: tid claim-mapping fix in ChatDocumentEndpoints (LIVE-PATCHED on Dev)

Root cause: `ChatDocumentEndpoints.cs:146` only checked `tid` claim form; Microsoft.Identity.Web renames `tid` → `http://schemas.microsoft.com/identity/claims/tenantid` on the ClaimsPrincipal in this environment. Other endpoints (e.g., `ChatEndpoints.cs:2012`) check both forms.

**Fix applied** to lines 151-153 + 443-445: added schema URL fallback. Deployed via `Deploy-BffApi.ps1` mid-session. **MUST be promoted to master via small follow-up PR** (otherwise next deploy overwrites). Tracked as a non-numbered follow-up in [`notes/summarize-vertical-remediation-plan.md`](notes/summarize-vertical-remediation-plan.md) §4.

### 🔴 Sev-1 #2: Summarize-vertical integration gap (REMEDIATION TASKS 032-035 FILED)

R5 deployed the **complete backend** (orchestrator + endpoint + agent tool + telemetry + session-files index + indexing pipeline method) AND the deployed playbook (`summarize-document-for-chat@v1`). User-facing UX is broken because the wiring between three components was never authored. See [`notes/summarize-vertical-remediation-plan.md`](notes/summarize-vertical-remediation-plan.md) for full root-cause analysis + 4-task decomposition.

---

## Resume protocol (when ready to continue)

1. **Pull master** (it has R5 + PR #349 platform fixes). Stay on `work/spaarke-ai-platform-unification-r5` branch for the closeout tasks (or create a new closeout branch from master).
2. **Promote the tid-claim fix** to master via small PR. Files: `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs` lines 151-153 + 443-445 — replace the `tid`-only lookup with the dual-form lookup matching `ChatEndpoints.cs:2012`. See [`notes/summarize-vertical-remediation-plan.md`](notes/summarize-vertical-remediation-plan.md) §3.1 for exact diff context.
3. **Execute closeout tasks** via `task-execute` skill — parallel-safe groups per `TASK-INDEX.md`:
   - **Wave P2-G9-CLOSEOUT (parallel)**: spawn sub-agents for tasks 032 + 033 in ONE message
   - Wait for both to commit; deploy via `scripts/Deploy-BffApi.ps1`
   - **Wave P2-G10-CLOSEOUT (serial)**: execute task 034 (frontend auto-trigger) — needs 032+033 backend behavior to test against
   - Deploy frontend via the appropriate code-page deploy script
   - **Wave P2-G11-CLOSEOUT (serial)**: execute task 035 (SC-18 walkthrough re-run + signoff)
4. **Capture signoff** in [`notes/task-030-summarize-walkthrough.md`](notes/task-030-summarize-walkthrough.md) §7.
5. **Flip tasks 030, 031, 032, 033, 034, 035 → ✅** in `tasks/TASK-INDEX.md`.
6. **Update plan.md** Phase 2 → ✅.
7. **Start Phase 3** — Wave 8 from project-pipeline plan:
   - 040 D3-01 `/analyze` proof point
   - 041 D3-02 Get Started welcome card "Summarize a Document"
   - 042 D3-03 Telemetry dashboards
   - 043 D3-04 Operator-led end-to-end testing
   - 044 D3-05 Lessons-learned + R6 backlog
8. **Wrap-up task 090** — README → Complete; coordination doc §8 entry; final R5 merge ceremony.

---

## Pre-resume verification (validates fix expectations)

When resuming, before executing any task:

```bash
# 1. Verify R5 backend endpoints are still live on Dev (with bearer)
curl -sS -w "summarize: HTTP %{http_code}\n" -X POST \
  "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/00000000-0000-0000-0000-000000000000/summarize" \
  -H "Authorization: Bearer x"
# Expected: 401 (auth middleware rejects fake bearer; route exists)
# NOTE: bearer is required — without it, BFF returns 404 for AI routes as a security pattern

# 2. Verify upload endpoint accepts authenticated requests (tid claim fix)
curl -sS -X POST \
  "https://spaarke-bff-dev.azurewebsites.net/api/ai/chat/sessions/<real-session-id>/documents" \
  -H "Authorization: Bearer <real-jwt-from-DevTools>" \
  --form "file=@/path/to/small.pdf"
# Expected: 202 Accepted + DocumentUploadResponse JSON
# If 401 with detail "Tenant identity not found in token claims" → live-patch was lost; re-apply per resume protocol step 2
```

---

## Key commits this session

| Commit | Description |
|---|---|
| `a9c7900f` | Pre-PR-#345 checkpoint (R5 work) |
| `b4584774` | PR #345: Prettier fix + asymmetric-registration fix (`NullSessionSummarizeOrchestrator`) |
| `6a8c96da` | PR #345: Code Quality whitespace + CRLF normalization |
| **`01359b36`** | **PR #345 squash-merge to master** (R5 ships) |
| `eeb5c929` | PR #349: 2 Linux/Windows platform-test fixes (Uri.TryCreate + GetInvalidFileNameChars) |
| **(merged)** | **PR #349 squash-merge to master** (PROD CD unblocked) |
| **(LIVE-PATCH, NO COMMIT)** | `ChatDocumentEndpoints.cs` tid-claim fallback — deployed via `Deploy-BffApi.ps1`; needs follow-up PR to master |

---

## Reference materials

- **PR #345** (R5 main): https://github.com/spaarke-dev/spaarke/pull/345
- **PR #349** (CD platform fixes): https://github.com/spaarke-dev/spaarke/pull/349
- **REMEDIATION PLAN**: [`projects/spaarke-ai-platform-unification-r5/notes/summarize-vertical-remediation-plan.md`](notes/summarize-vertical-remediation-plan.md) ← read this first
- **Walkthrough findings**: [`notes/task-030-summarize-walkthrough.md`](notes/task-030-summarize-walkthrough.md)
- **Insights walkthrough (deferred to Phase 3)**: [`notes/task-030-sme-walkthrough.md`](notes/task-030-sme-walkthrough.md)
- **New task POMLs**: [`tasks/032-upload-endpoint-wire-session-files.poml`](tasks/032-upload-endpoint-wire-session-files.poml), [`tasks/033-chat-context-surfaces-uploaded-files.poml`](tasks/033-chat-context-surfaces-uploaded-files.poml), [`tasks/034-frontend-auto-trigger-summarize-on-upload.poml`](tasks/034-frontend-auto-trigger-summarize-on-upload.poml), [`tasks/035-sc18-walkthrough-rerun.poml`](tasks/035-sc18-walkthrough-rerun.poml)
- **Spec**: [`spec.md`](spec.md) FR-01/FR-02/FR-04/FR-08/NFR-02/SC-08/SC-18
- **CLAUDE**: [`CLAUDE.md`](CLAUDE.md)
- **TASK-INDEX**: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)

---

*Checkpoint authored 2026-06-04 immediately after SC-18 walkthrough surfaced Sev-1 integration gap. Resume by executing tasks 032+033 in parallel via `task-execute` skill.*
