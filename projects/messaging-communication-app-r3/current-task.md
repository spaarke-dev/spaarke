# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-21 (Phase 2 complete — 013 + 014 done + gated)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Phase 2 ✅ · 020,023,024 ✅ (pushed) · **Task 021 (FR-04) COMPLETE ✅ + gated** (incl. backend Subject/To read-DTO enrichment). Next: 022 (forward, deps 020), 025 (title link). |
| **Step** | — |
| **Status** | 013,014,020,023,024 + 2 merges committed+pushed (PR #664). **021 done + gated, about to commit** (backend + client + frontend). 7 tasks this session. |
| **Next Action** | Commit 021, then `work on task 022` (FR-08 forward action → EmailComposer forward mode, deps 020). Then 025 (conversation title → record-scoped modal link, deps 011+012). ⚠️ **021 enriched the read DTO**: `ThreadMessageDto`/`IThreadMessageDto`/`TimelineMessage` now carry `subject`+`to` (email-in-flow block + word filter use them); recipients = access-filtered `sprk_to`, never BCC. `mapStateToSendRequest` lives in `EmailComposer.reducer.ts` (post-merge). ⚠️ worktree build gap: verify via scoped `tsc --noEmit` + `npm test`, NOT whole-package build. |

### Progress — 11 tasks ✅ (001–006, 010–014). 9 committed+pushed (PR #664); **013 + 014 committed? NO — uncommitted**
- **Phase 1 (backend spine) COMPLETE + pushed**: 001–006.
- **Phase 2 (shared conversation core) COMPLETE**: 010 characterize · 011 bubbles · 012 two-pane shell (all pushed) · **013 in-conversation compose · 014 additive filters (this session, gated, NOT yet committed)**.
- **013**: added a Teams-style chat input to `ConversationView` sending via existing `sendTimelineMessage({communicationType:'message', threadId, ...})` (ACS branch, ADR-045/046); `pollNow` on-send + on-demand refresh; ~5s poll retained. Gate found 1 Major (double-send on type-during-send) → FIXED (inFlightRef). See `notes/task-013-notes.md`.
- **014**: added additive filters (Email/Message toggles + word Dropdown) to `ConversationView`; presentational-only over the polled timeline; exported pure helpers `messagePassesFilters`/`extractWordOptions`. Gate verdict Ship (2 Minor fixed). See `notes/task-014-notes.md`.
- **Files changed this session (uncommitted)**: `ConversationView.tsx`, `ConversationView.types.ts`, 2 new tests (`ConversationView.compose.test.tsx`, `ConversationView.filters.test.tsx`), `notes/task-013-notes.md`, `notes/task-014-notes.md`, `notes/defer-issues.md` (added ISS-003), `tasks/TASK-INDEX.md`, POMLs 013/014, this file. **40 tests pass** for the ConversationView suite.

### Next Action (explicit)
1. **Commit** this session's Phase-2 work (013+014). 2. **File ISS-003** GitHub issue (`/defer` — push-to-github Step 1.6 blocks on the `{URL}` placeholder in `notes/defer-issues.md`). 3. `work on task 020` — FR-07 extend `SendEmailDialog`/`EmailComposer` (thread id + record link), **opus tier**, serial. Then Wave 11 = 023 + 024 (parallel-safe), 021, 022, 025.

### Critical Context (carry forward)
0. **ISS-003 filed → [#669](https://github.com/spaarke-dev/spaarke/issues/669)** — `useThreadPoll.pollNow` swallowed when a poll is in flight → task-013 on-send refresh can miss by ≤5s in a narrow race; fix belongs in the characterized core (NFR-06), deferred.
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
