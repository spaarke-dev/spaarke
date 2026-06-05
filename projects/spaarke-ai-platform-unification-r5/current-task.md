# Current Task — Spaarke AI Platform Unification R5

> **Purpose**: Active task state tracker. Managed by `task-execute` skill per CLAUDE.md §7.
> **Status**: 🟡 **TASK 036 IN-PROGRESS** — frontend UX rework: inline file holding + extensible intent matcher + deterministic Summarize promotion.
> **Last updated**: 2026-06-05 (task 036 started; FULL rigor; ~13 steps)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 036 — P2-CLOSEOUT-05 Inline file holding + intent matcher + Summarize promote-and-execute |
| **Task file** | `projects/spaarke-ai-platform-unification-r5/tasks/036-inline-holding-intent-matcher-summarize-promote.poml` |
| **Step** | Step 1 of 13 (starting Step 1: notes/task-036-design-2026-06-05.md) |
| **Status** | in-progress |
| **Rigor** | FULL |
| **Phase** | Phase 2 — Vertical Slice (CLOSEOUT) |
| **Branch** | `fix/r5-session-files-schema-conformance` (working on top of master after PR #361 merge) — may need to branch to `work/r5-task-036-inline-holding` |
| **Next Action** | Step 1: Write `notes/task-036-design-2026-06-05.md` capturing SC-18 cycle-4 evidence + operator UX clarification + design choices (deterministic-vs-LLM, inline-vs-promoted, chip-in-thread vs above-input). |
| **Pre-reqs** | ✅ Task 032 (upload→session-files), ✅ Task 033 (ChatContext.UploadedFiles), ✅ PR #361 (schema-conformance fix). Backend is correct; this is frontend-only work. |

---

## Phase 2 closeout status (post-walkthrough remediation)

| Task | Status | What | When |
|---|---|---|---|
| 032 | ✅ | Wire `ChatDocumentEndpoints.UploadDocumentAsync` to call `IndexSessionFileAsync` + populate `ChatSession.UploadedFiles[]` | PR #354 merged 2026-06-04 |
| 033 | ✅ | `PlaybookChatContextProvider` surfaces `UploadedFiles` in `ChatContext`; `SprkChatAgentFactory` system-prompt suffix | PR #354 merged 2026-06-04 |
| 034 (frontend auto-trigger) | ⏭️ Deferred | Original "auto-trigger on upload" — REPLACED by tasks 036/037/038 with cleaner intent-driven design (operator decision 2026-06-05) | n/a |
| 035 | ⏭️ Deferred until 036/037/038 | SC-18 walkthrough re-run + signoff | After 036/037/038 ship |
| **036** | 🔄 in-progress | Inline holding + intent matcher + Summarize promote-and-execute | Now |
| 037 | 🔲 | Context-pane execution-trace widget | After 036 |
| 038 | 🔲 | Workspace-pane Summary tab registration | After 036 (parallel with 037) |

---

## SC-18 cycle log (for posterity)

| Cycle | Symptom | Root cause | Fix |
|---|---|---|---|
| 1 (2026-06-04) | Upload 401 "Tenant identity not found in token claims" | `ChatDocumentEndpoints.cs` only checked `tid` short claim form | PR #354 — schema URL fallback |
| 2 (2026-06-04) | Upload 200 but agent says "I don't see the document" | Upload endpoint wrote only to Redis; never indexed | PR #354 tasks 032 + 033 |
| 3 (2026-06-04) | After cycle-2 deploy: Azure Search 400 on session-files index — `tags` null | `BuildKnowledgeDocuments` left `Tags = null` | PR #359 — `Tags = Array.Empty<string>()` |
| 4 (2026-06-05) | After cycle-3 deploy: Azure Search 400 — `deploymentId` does not exist in schema | 7 customer-corpus-only fields serialize when null; same root cause as cycle 3 | PR #361 — `[JsonIgnore(WhenWritingNull)]` on all 8 affected fields + regression test |
| 5 (2026-06-05) | Indexing now succeeds; UX itself is broken: two upload paths producing two session states; summary in chat pane not workspace | Frontend architecture gap (paperclip uses FR-07 inline; `[action:upload]` button uses server-side path). Not a bug — a design problem. | **Tasks 036 + 037 + 038** (this task and its siblings) |

---

## Knowledge Files Loaded (Step 4)

(To be populated during execution.)

## Constraints Loaded (Step 4a)

(To be populated.)

## Patterns Loaded (Step 4b)

(To be populated.)

## Applicable ADRs (Step 5)

(To be populated.)

## Files Modified This Session

(To be populated during execution.)

## Completed Steps

(To be populated.)

## Decisions Made

(To be populated.)

---

## Critical Context for Resume

1. **Backend is correct**. All bug fixes from cycles 1-4 are merged and deployed. PR #354, PR #359, PR #361 all on master + Spaarke Dev. Hash verified 2026-06-05 14:54 UTC. This task is frontend-only.

2. **Two upload paths exist in the chat shell, by accident not design**. Paperclip → `useChatFileAttachment` (client-side text extraction, FR-07 inline attachments). `[action:upload]` button → server-side `POST /api/ai/chat/sessions/{id}/documents` (indexes to session-files). Operator's intent-driven design (per 2026-06-05 chat) converges: file held inline until intent matches, THEN promoted server-side and executed deterministically.

3. **Concordance-table for free-form-chat playbook routing is R6 backlog**, not in this task. For now: deterministic frontend matcher for slash + keyword + button. LLM fallback for ambiguous chat (existing tool registration).

4. **Bypass LLM for playbook SELECTION** (operator explicit). LLM is only involved in playbook EXECUTION (generation). Slash `/summarize` + ready chip → direct POST `/summarize`.

5. **PaneEventBus channels are CLOSED at 4 per ADR-030**. Only additive event types within existing channels (`workspace.streaming_*`, `context.files_staged` — both already registered per task 016).

6. **Worktree is `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r5`**. Master is checked out in another worktree (`spaarke-wt-ai-spaarke-insights-engine-r2`) — cannot `git checkout master` here.

---

## Resume protocol

To resume: read this file's Quick Recovery section, then re-invoke task-execute on the same POML.

```
task-execute projects/spaarke-ai-platform-unification-r5/tasks/036-inline-holding-intent-matcher-summarize-promote.poml
```

---

*Maintained by task-execute skill. Resets on task transition per protocol §11.*
