# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-21 (by context-handoff — Phase 3 at 5/6; tasks 013,014,020,021,022,023,024 done)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Phase 1-4 ✅ (on master). **Phase 5 Round 1 ✅** — 043 (FR-21 privilege/privacy: DTO markers + permitted-recipient display, over-disclosure seam test, gate clean), 040 (FR-24 `sprk_ispinned` field CREATED live in spaarkedev1 — **041 must treat null as unpinned**), 044 (FR-23 search config — PREPARED; operator Maker-Portal steps in notes; cosmetic `description="test"` left on a Quick-Find view for operator cleanup). Verified: BFF build 0 err, shared-UI 157/157, tsc 0. **Round 2 IN FLIGHT: 042** (attachments, opus). Then Round 3: 041 (pin UI, uses `sprk_ispinned`). 045 ⛔, 046 ⏸, #675 blocked. |
| **Step** | Wave 15 done. All 4 surfaces built + Step 9.5-gated by their agents; **main-session cross-surface build-verify GREEN** (Communication.Components jest 4/4 + tsc clean; AI.Widgets registry 40/40; LegalWorkspace dual-use consumer typechecks — only unrelated Events.Components tsc noise). No escalations. Committed locally; **needs push**. |
| **Status** | 030 new PCF `CommunicationConversationPanel` (build:prod pass, 29 jest); 031 `CommunicationsWorkspaceWidget` body-swap to shared two-pane (identity `communications-list`/`communications` byte-unchanged; added jest infra + `@spaarke/auth` dep); 032 new Vite page `sprk_communicationconversationpage` (blocking auth bootstrap); 033 Email&Messages tab via DataGrid `hostFilters` + form onLoad (fail-CLOSED scoping fix). Solution packaging/version-footer bump/live ui-tests all deferred to **034**. |
| **Next Action** | **`/push-to-github`** then **task 034** (deploy Phase 4 surfaces — Matter pilot; `<steps mode="prescriptive">`, STANDARD rigor). 034 deps 030-033 (all ✅). Cite in deploy PR: ADR-026 Path-A (030 PCF), ADR-006 Path-C (033 grid web-resource), the SpaarkeAi hot-path republish (031, coordinate per projects/INDEX.md), and the fail-closed regarding-scope hardening (033). **Known Phase-4 wiring gap** (not a blocker; see Critical Context #11): `ConversationWorkspace.renderConversation` forwards only `{threadId, authenticatedFetch, bffBaseUrl}`, so FR-12 title-link only lights up where the host knows the regarding (030 PCF record-mode wires it; 031/032 all-mode leave it unwired by design). |

### Progress this session — 7 implementation tasks + backend enrichment, ALL gated + committed + PUSHED (PR #664)
- **Phase 1 (001–006)** + **Phase 2 core (010,011,012)**: done in a PRIOR session, pushed.
- **013** FR-06 in-conversation compose (ConversationView chat input via existing `sendTimelineMessage`, ACS branch; `pollNow` refresh). Gate fixed a Major double-send (`inFlightRef`). `notes/task-013-notes.md`.
- **014** FR-09 additive filters (Email/Message toggles + word Dropdown; presentational). Ship. `notes/task-014-notes.md`.
- **020** FR-07/19 SendEmailDialog thread-pin + auto-associate (opus). Gate fixed a Major stale-contract doc + 4 minors. `notes/task-020-notes.md`.
- **021** FR-04 email-in-flow block **+ correctness-critical backend read-DTO Subject/To enrichment** (owner-approved escalation resolution). Recipient-disclosure gate-verified SAFE. `notes/task-021-notes.md`.
- **022** FR-08 forward → SendEmailDialog forward mode. Ship (regarding-in-forward doc-mitigated). `notes/task-022-notes.md`.
- **023** FR-05 MessageQuickView + ConversationView `scrollToMessage` handle. Ship. `notes/task-023-notes.md`.
- **024** FR-11 NewThreadModal find-or-create. Fixed 3 Majors; §6.5 Path-A (name/desc omitted) accepted. `notes/task-024-notes.md`.
- **025** FR-12 conversation title → record link. Added `ConversationView` header (`title`/`regarding`/`onOpenRecord` props); title is a Fluent `Link as="button" type="button"` when regarding+callback present, else plain `Text`; delegates open to host (no `Xrm`/iframe in shared lib — ADR-012/MODAL-DECISION-CRITERIA). Gate fixed 2 Minors (`type="button"` form-submit guard; `role="heading"` a11y), accepted 1 (parity `name`). 63/63 CV tests. `notes/task-025-notes.md`. **← Phase 3 complete.**
- **Merges reconciled**: 54-commit master merge (email-r4 moved `mapStateToSendRequest` → `EmailComposer.reducer.ts` + added attachment body-links) + 2 CI-Prettier merges + **16-commit master merge this session** (1 conflict `SendEmailDialog.tsx` → union resolve `201a4e607`). All conflict-resolved, tests green.

### Critical Context (carry forward — CURRENT)
1. **Read DTO now carries `subject` + `to`** (task 021): `ThreadMessageDto` (backend) / `IThreadMessageDto` / `TimelineMessage` + `buildTimeline` mapping. Recipients = access-filtered `sprk_to` on the visible row (NEVER fabricated, NEVER BCC — `sprk_bcc` is separate, never selected). The email-in-flow block + the word filter use them.
2. **Identity contract (FR-02/18)**: bubble alignment keys on `senderSystemUserId` (`SentBy`), NEVER email-string. **Access model (NFR-01)**: impersonated + shared access-filter; NO membership-union; client renders exactly what server returns.
3. **`mapStateToSendRequest` lives in `EmailComposer.reducer.ts`** now (email-r4 moved it in the merged master) — Phase-4/future send-shaping edits go there, not `EmailComposer.tsx`. R3's `threadId` arg was re-applied there.
4. **Decoupled host seams (ADR-012)** on ConversationView: `renderConversation` (012), `currentUserSystemUserId`, `scrollToMessage` handle via `forwardRef` (023), `onOpenEmail` (021), `onForwardMessage` (022). Phase-4 hosts (030 PCF / 031 widget / 032 code page) mount `ConversationView` into `ConversationWorkspace`'s `renderConversation` + wire these callbacks (open/forward → the extended `SendEmailDialog`; the enriched message builds the view/forward `sourceRecord`).
5. **regarding-in-forward (ISS-005 #672)**: in `mode="forward"` the composer derives `associations` from `sourceRecord.associations`, dropping the dialog's `regarding` fold. Host MUST include regarding in `sourceRecord.associations` (documented in the `onForwardMessage`/`sourceRecord` JSDocs). `threadId` DOES survive forward.
6. **§6.5 Path exceptions on record** (cite in deploy PRs): task 012 Path-A (`ConversationWorkspace` record-mode via `by-regarding`); task 024 Path-A (NewThreadModal name/desc omitted — endpoint has no field, ISS-004 #670); ADR-006 (task 033 DataGrid web-resource) + ADR-026 (task 030 Path-A PCF) both Path C.
7. **⚠️ Worktree build gap**: sibling `@spaarke/auth`/`@spaarke/sdap-client` `dist/` unbuilt → exactly 2 unrelated tsc errors (`EntityCreationService.ts`/`useWizardPageBootstrap.ts`). Verify client work via **scoped `tsc --noEmit` (expect exactly those 2) + `npm test`**, NOT whole-package `npm run build`.
8. **CI is now triggering** on PR #664 (8 checks). CI runs Prettier and pushes `style: auto-format` commits back — to avoid the reject/merge cycle, run `npx prettier --write` on changed client files BEFORE pushing.
9. **Flaky-dialog tests**: the 2 Fluent-`Dialog`-open integration tests (`ConversationView.emailInFlow` open→dialog, `ConversationView.forward` forward→dialog) were hardened with `findByRole('dialog', {}, { timeout: 4000 })` (jsdom/tabster timing flake — not a logic bug).
10. **Filed follow-ups**: #666/#667 (participant escaping, roll-up OrderBy), #669 (ISS-003 `useThreadPoll.pollNow` swallowed race), #670 (ISS-004 named-direct-threads Path-B), #672 (ISS-005 regarding-in-forward engine-union). All in `notes/defer-issues.md` with URLs (push-to-github Step 1.6 clean).
11. **FR-12 title-link wiring boundary (Phase 4)**: `ConversationView` has the FR-12 props (`title`/`regarding`/`onOpenRecord`, task 025), but `ConversationWorkspace.renderConversation` forwards only `{threadId, authenticatedFetch, bffBaseUrl}` to the seam. So the title link lights up ONLY where the host itself knows the regarding: **030 PCF (record mode)** wires it (closes the Fluent dialog then `navigateTo` Layout 1); **031 widget + 032 code page (all mode)** correctly leave it unwired (no per-thread regarding/name at the seam — §11 dead-code avoidance). If the workspace/widget surfaces should later show record-linked titles, the enhancement is to have `ConversationWorkspace` thread per-thread regarding+name into `renderConversation` (own follow-up; not in R3 scope).
12. **Phase 4 dual-use consequence (031)**: the `CommunicationsWorkspaceWidget` body-swap also changes what `LegalWorkspace/src/sections/communications.registration.ts` renders (same shared widget, Pattern-D dual-use) — intended NFR-06 "one shared widget" behavior, not a regression. Verified LegalWorkspace's registration still typechecks.

### Files Modified This Session
None uncommitted — **all work is committed + pushed**. Per-task detail in `notes/task-0NN-notes.md` (013,014,020,021,022,023,024); commits: `git log --oneline aff99a072..HEAD` (Wave-11 checkpoint → HEAD).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 025 |
| **Task File** | `tasks/025-conversation-title-record-link.poml` |
| **Title** | FR-12 conversation title → record-scoped modal link |
| **Phase** | 3 (email-in-flow + quick-view + new-thread) |
| **Status** | in-progress — merged origin/master first (SendEmailDialog conflict resolved as union: R3 `regarding`-fold + master R6-4 760px dialog); implementing header title link |
| **Started** | 2026-07-21 |

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
