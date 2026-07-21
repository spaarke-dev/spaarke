# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-21 (by context-handoff)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave 7 COMPLETE ✅ (011 ConversationView + 012 ConversationWorkspace) — next: Wave 8 (013 in-conversation compose) |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | `work on task 013` (FR-06 in-conversation compose) — serial, edits ConversationView from 011. Then Wave 9 (014 filters). **Phase-4 hosts wire `ConversationView` into `ConversationWorkspace`'s `renderConversation` seam + supply currentUserSystemUserId.** ⚠️ pre-existing worktree build gap: sibling `@spaarke/auth`/`@spaarke/sdap-client` dist unbuilt (2 unrelated tsc errors) — verify via scoped tsc/jest. |

### Progress this session — 9 tasks ✅ (all gated + committed + PUSHED to PR #664)
- **Phase 1 (backend spine) COMPLETE**: 001 characterize · 002 FR-18 DTO enrichment · 003 FR-16 list-all-threads · 004 FR-17 naming+rename · 005 FR-19 email ThreadId · 006 doc-drift.
- **Phase 2 core**: 010 characterize · 011 ConversationView (bubbles) · 012 ConversationWorkspace (two-pane shell).
- Working tree **CLEAN** — nothing uncommitted. 8 task/chore commits + init/portfolio on `origin/work/messaging-communication-app-r3`. Portfolio Tasks Completed = 9.

### Next Action (explicit)
`work on task 013` — FR-06 in-conversation compose (chat input sends via existing send path; on-demand + on-send refresh; ~5s polling retained). Serial (edits `ConversationView` from 011, `parallel-safe:false`). Then Wave 9 = task 014 (additive filters). After Phase 2: Phase 3 (020 extend SendEmailDialog, 021 email-in-flow, 022 forward, 023 quick-view, 024 new-thread, 025 title link).

### Critical Context (carry forward)
1. **Identity contract (FR-02/18)**: bubble alignment keys on `senderSystemUserId` (backend `SentBy`, shipped by task 002; plumbed to client by 011 via `IThreadMessageDto`/`TimelineMessage`/`buildTimeline` mapping). NEVER align on email-string.
2. **Access model (NFR-01)**: all reads impersonated + shared access-filter; **NO membership-union** (retired). Client renders exactly what server returns; no client-side thread filtering.
3. **§6.5 Path A exception (task 012, on record for review)**: `ConversationWorkspace` record-mode lists via existing `GET /by-regarding/{entityType}/{id}` (FR-16 `/threads` has no regarding param by design so record-less threads surface in all-mode). Both server-access-filtered.
4. **Renderer seam**: Phase-4 hosts (030 PCF / 031 widget / 032 code page) inject `ConversationView` into `ConversationWorkspace`'s `renderConversation` prop + supply `currentUserSystemUserId`.
5. **⚠️ Worktree build gap**: sibling `@spaarke/auth`/`@spaarke/sdap-client` `dist/` unbuilt → 2 tsc errors in `EntityCreationService.ts`/`useWizardPageBootstrap.ts` (UNRELATED to R3). Verify client work via scoped `tsc --noEmit` (expect exactly those 2) + `jest`. Whole-package `npm run build` will fail on them until siblings are built.
6. **Filed issues**: #666 (ISS-001 participant= escaping), #667 (ISS-002 roll-up OrderBy). Pre-existing HIGH CVE `System.Security.Cryptography.Xml 8.0.3` — NOT introduced by R3.
7. **CI**: `sdap-ci.yml` shows no checks on this branch (not triggering on these pushes — verify when PR #664 → ready).

### Files Modified This Session
None uncommitted — all work across 001–006 + 010–012 is committed. See `git log --oneline origin/master..HEAD` and per-task `notes/task-0NN-notes.md`.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 003 |
| **Task File** | `tasks/003-list-all-threads-endpoint.poml` |
| **Title** | FR-16 `GET /communications/threads` + `ListThreadsAsync` + seam tests |
| **Phase** | 1 (backend read/thread spine) |
| **Status** | implemented — build/test/publish/CVE green (see `notes/task-003-notes.md`); pending PR + `/conflict-check` |
| **Started** | 2026-07-20 |

### Approach (decided)
- Impersonated `sprk_communicationthreads` query, NO regarding filter (record-less inclusion via impersonation).
- `$select` = id, name, threadtype, createdon; `$orderby=createdon desc` (deterministic, stable paging).
- Name search: `contains(sprk_name,'<escaped>')` — single-quote doubled (OData injection safe).
- Paging: keyset/cursor on `createdon` (Dataverse Web API has no `$skip`; seam drops `@odata.nextLink`).
  Opaque base64 `pageToken` = last row `createdon`; next-page filter `createdon lt <cursor>` (non-overlapping).
- No new DI dependency (service already scoped); NO membership seam (retired union stays retired).

---

## Progress

### Completed Steps

*No steps completed yet*

### Current Step

*No active task*

### Files Modified (All Task)

*No files modified yet*

### Decisions Made

*No decisions recorded yet*

---

## Next Action

**Next Step**: Execute task 001 (Phase 1 — backend read/thread spine)

**Pre-conditions**:
- Tasks generated in `tasks/` (pipeline Step 3)
- R2 participant junction confirmed applied in target env

**Key Context**:
- Refer to `spec.md` FR-16/17/18/19 for the backend increment
- ADR-038 seam tests are DoD; no membership-union (NFR-01)

**Expected Output**:
- Phase 1 backend endpoints + enriched DTO + seam tests

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-20 18:30
- Focus: Project initialization (artifacts generated)

### Key Learnings

- Notification spine (`communication-arrived`, FR-22) is NOT yet in master — keep FR-22 late.

### Handoff Notes

*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: messaging-communication-app-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-045, ADR-046, ADR-024, ADR-026, ADR-028, ADR-038, ADR-021, ADR-006

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md`, `docs/standards/CHAT-ATTACHMENT-POLICY.md`

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
