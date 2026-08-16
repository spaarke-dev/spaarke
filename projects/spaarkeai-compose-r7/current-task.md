# Current Task State — spaarkeai-compose-r7

> **Last Updated**: 2026-08-16 (task-execute — task 030 in progress)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: master MERGED (HEAD `9aaf695c8` merge; resolved DataverseWebApiService.cs conflict — RED-4 dead-code deletion took master side, my UpsertAsync lives in DataverseServiceClientImpl/interface; 1124 Compose tests green post-merge). Branch current with master.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **040 ✅ COMPLETE** (commit pending this turn) — next: **041** (save-state indicator + beforeunload + invariant/test flip) |
| **Step** | 040 implemented + gated (code-review + adr-check PASS); trackers updated |
| **Next Action** | Execute **041**. Then 050→051→060→061→070→071→072→074→090. |

### Task-041 carried boundary (task 040 DELIBERATELY deferred these — they are 041's scope, not misses)
- **Flip the "no autosave" invariant comments** at `ComposeWorkspace.tsx:34` + `:2966` — 040 left them untouched (they assert *no automatic SERVER flush*, still TRUE; the client draft store never calls `triggerSave`/BFF). 041 reconciles the wording as the documented ADR-Tensions **Path-A** change.
- **Update the `unmountFlush` test** (`ComposeWorkspace.unmountFlush.test.tsx`) to the new intended behavior (coupled with the comment flip — one coherent Path-A change).
- **Save-state indicator** (Saving…/Saved/Unsaved + Auto Save On/Off) in `ComposeFormatToolbar.tsx`.
- **`beforeunload`/modal-close guard** on unsaved work.
- **040 hooks 041 can reuse**: `autoSaveEnabled` state (task 020), `getComposeDraft`/`saveComposeDraft`/`clearComposeDraft` (`composeDraftStore.ts`), `ComposeEditorHandle.getDraftHtml()`, `draftAutosaveMirrorRef`, `COMPOSE_DRAFT_AUTOSAVE_INTERVAL_MS`.

### Task 040 — what shipped (see notes/task-040-notes.md)
NEW `composeDraftStore.ts` (client-only localStorage, single-slot, id-match-gated) + `ComposeEditor.getDraftHtml()` + ComposeWorkspace autosave effect (~15s dirty-only, `autoSaveEnabled`-gated, **no BFF**), clear-on-save, and non-destructive recovery-on-mount via the existing `mountDraftHtml` path (key = task-010 `getComposeLogicalIdentity`). **Ripple was SMALLER than predicted** — zero editor-handle mocks needed changing (inferred stub types + optional-chained consumer). 10/10 store tests standalone; 3 CI-only workspace tests; full suite 615 pass / 0 fail; NFR-03 escalation trigger NOT fired (draft path calls no fetch).

**11 of 20 tasks done: 001, 010, 011, 012, 013, 020, 030, 040, 073, 075.** Phases 1–3 + Phase 4a (draft store) COMPLETE.

### Task-040 design (investigated 2026-08-16 — execute this)

**Files**: NEW `src/client/shared/Spaarke.Compose.Components/src/widgets/composeDraftStore.ts` (best-effort localStorage, try/catch, like `composeIdentity.ts`); MODIFY `ComposeWorkspace.tsx`; MODIFY `ComposeEditor.tsx` (`ComposeEditorHandle` + impl).

**The draft KEY** = `getComposeLogicalIdentity(state.documentRef)` (task-010 accessor `sprkDocumentId ?? speDriveItemId ?? composeLogicalId`, `''`-guarded) — `src/types/compose-contracts.ts:694`. Reuse it; do NOT derive a second key.

**Content serialization — RECOMMENDED Option B (reuse existing recovery path)**: `ComposeEditorHandle` has `buildContentModel()` (structured model) but **NO plain HTML getter**. Add `getDraftHtml(): string | null` to the handle (`ComposeEditor.tsx:843` interface + its `useImperativeHandle` impl — TipTap `editor.getHTML()`). Serialize `{ logicalId, fileName, html, savedAt }` to localStorage. Recover on mount by seeding via the EXISTING `mountDraftHtml` reducer path (the same one blank/template/AI-draft use). ⚠️ **Ripple (task-013 pattern)**: adding a handle method breaks every test that mocks `ComposeEditorHandle` (the editor stubs in `ComposeWorkspace.*.test.tsx`, `bornInEditorSave`, `renderOnSave`, etc.) — add `getDraftHtml: () => '<p/>'` to each stub. Run the FULL runnable jest suite.

**Wiring in ComposeWorkspace.tsx**:
- `autoSaveEnabled` useState(true) already exists (task 020) — gate the autosave effect on it.
- **Autosave effect**: `setInterval` ~15s while `state.status==='loaded'`; on tick, if `editorRef.current?.isDirty()` (the editor's OWN authoritative flag, read fresh — see `:1494`) AND a logical id exists → `composeDraftStore.set(logicalId, {...})`. **NO network call** (NFR-03 — the escalation trigger). Use a ref-mirror for the draft-capture closure (same convention as `triggerSaveRef`/`hasUnsavedWorkRef`) so the interval need not re-subscribe.
- **Clear on save**: in the `saveSucceeded` path (~`:1840`/`setIsDirty(false)` ~`:1902`) → `composeDraftStore.clear(logicalId)`. Note the logical id may ROTATE on promotion (transient→promoted adopts the new sprkDocumentId) — clear the OLD key (pre-save identity) there; task 030's `clearActiveComposeLogicalId` precedent.
- **Recovery on mount**: after a mount resolves a documentRef with a logical id, check `composeDraftStore.get(logicalId)`; if a draft exists AND is newer than the mounted content → recover it (seed via `mountDraftHtml`). For 040, a straightforward recover-on-reopen for born-in-editor/transient drafts satisfies criterion 2; the recovery-vs-server-content PROMPT/indicator is task 041's job — keep 040's recovery minimal + non-destructive (don't silently clobber a loaded server doc without the 041 guard; simplest safe scope = recover when the mounted doc has no server content newer than the draft).

**Boundaries**: CLIENT-ONLY (no BFF). Autosave dirty-only, ~15s (tunable). Do NOT touch the "no autosave" invariant comment (@34/@2789) or the `unmountFlush` test — those are **task 041** (the invariant flip is 041's documented Path-A change). NEVER delete `docxBridge.ts`.

**Escalation trigger (NFR-03)**: if dirty-tracking cannot separate local-draft persistence from a server save (version-per-tick risk) → STOP + escalate. (Design above keeps them fully separate: the draft path calls only `composeDraftStore`, never `authenticatedFetch`.)

**10 of 20 tasks done: 001, 010, 011, 012, 013, 020, 030, 073, 075.** Phase 1 (Save-Identity) + Phase 2 (Save dropdown) + Phase 3 (Name modal) COMPLETE.

**030 complete** (commit pending this turn): `ComposeSaveNameDialog.tsx` (FormModal preset) + `requestSave` interception in ComposeWorkspace. **No BFF change** (displayName plumbing already existed — task 100/013). 13/13 dialog tests green; full runnable jest 605 pass. See `notes/task-030-notes.md`.

**Carried decisions for 040**:
- `autoSaveEnabled` useState(true) lives in ComposeWorkspace (task 020) — 040 connects it to real client-only autosave behavior.
- `getComposeLogicalIdentity(ref)` = `sprkDocumentId ?? speDriveItemId ?? composeLogicalId` (task 010) = the FR-03 draft-recovery key.
- Task 030 added `requestSave`/`saveNeedsName`/`isUntitledDraftName`/`autoNameForUnnamedDraft`/`UNTITLED_DOC_NAME` in ComposeWorkspace — 040's autosave sits alongside these (new docs are named-first before explicit server save; the local draft protects pre-name work).
- **050/051 still must wire the FR-11 PDF-intake cause-specific message** (deferred from 073).
- Task-013 lesson still applies to any future BFF write-path change: run the FULL BFF suite.

**9 of 20 tasks done: 001, 010, 011, 012, 013, 020, 073, 075.** **Phase 1 (Save-Identity) + Phase 2 (Save dropdown) COMPLETE.**

Recent commits: 013 (`c3f646504` — server atomic upsert; full suite 10,421 passed), 020 (`df57361da` — Save/Save As dropdown + Auto Save toggle; `autoSaveEnabled` state lives in ComposeWorkspace for Phase 4 to consume).

**Carried decisions/context**:
- `composeLogicalId` (getComposeLogicalIdentity accessor) = FR-03/FR-07 shared key; `autoSaveEnabled` useState(true) in ComposeWorkspace = the Auto Save toggle state 040/041 must connect to real autosave.
- **050/051 must wire the FR-11 PDF-intake cause-specific message** (deferred from 073 — see task-073-notes.md).
- **013 lesson**: changing a promote-write primitive (CreateAsync→UpsertAsync) ripples to every test that mocks it — expect similar test-migration when touching ComposeService write paths. Run the FULL BFF suite for BFF data-path changes, not just the targeted folder.
- NFR-01 baseline = 44.96 MB incl PDBs. Branch 0 behind master (merged this session; only docs(quality) commits since).
- 030 is BFF-touching (ComposeEndpoints/ComposeService name threading) → /conflict-check + publish + CVE gates.

**Completed this session** (all committed + pushed-pending):
- 001 ✅ gate (`3f5cbfe02`) — baseline **44.96 MB incl PDBs net10**; conflict-check CLEAR; DI-gate verified; PRs #690/#266 OPEN.
- 010 ✅ (`2dde88f3c`) — `composeLogicalId` + `getComposeLogicalIdentity` accessor + `composeIdentity.ts` (localStorage single-slot). Shared key for 040/011.
- 073 ✅ (`fd0b8e4da`, cherry-picked from Group-B subagent) — PDF-intake cause discrimination. **FR-11 end-to-end surfacing deferred to 050/051** (see task-073-notes.md — avoids r2 PublicContracts change + downcast).
- 011 ✅ (`23793f4e9`) — id-less assistant-insert door now carries dedup identity.

- 075 ✅ (`57cf4b865`, cherry-picked from Group-B subagent) — test-hygiene batch (FakeTimeProvider flake, 4 jest suites, nda fixture, seam-test tighten). Subagent-verified 10,402 xUnit + 960 jest green.

**Group B complete** (073 ∥ 075 both integrated). **6 of 20 tasks done: 001, 010, 011, 073, 075** (+012 analyzed).

**Remaining (14)**: 012 → 013 (BFF spine, serialize — both edit ComposeService.cs) → 020 → 030 → 040 → 041 → 050 → 051 → 060 → 061 → 070 → 071 → 072 → 074 → 090.

**Key carried decisions**: composeLogicalId is the FR-03/FR-07 shared key; localStorage single active-draft slot; 050/051 must wire the PDF-intake cause-specific message (FR-11 rider); 012 fix = Graph `conflictBehavior=rename` for forkNew create (plan in `notes/task-012-analysis.md`). Baseline for NFR-01 deltas = **44.96 MB incl PDBs**.

**⚠️ Branch is 4 behind master (growing).** Before the 012/013 BFF spine, consider `git merge origin/master` (INDEX.md conflict expected) so the BFF data-integrity edits don't hit a large late conflict — master may have touched ComposeService.cs.

### Files Modified This Session (all COMMITTED — nothing uncommitted)
This session implemented 9 tasks (001, 010, 011, 012, 013, 020, 073, 075 + checkpoints). Each task is its own commit. Product surfaces touched:
- **Client** (`Spaarke.Compose.Components`): `composeIdentity.ts` (new — logical-id + fork-name helpers), `compose-contracts.ts` (`composeLogicalId` + `getComposeLogicalIdentity`), `ComposeWorkspace.tsx` + `.types.ts` (identity plumbing, id-less-mount fix, Save-As uniquify, autoSave state), `ComposeFormatToolbar.tsx` (+test) (Save/Save As dropdown + Auto Save), `ComposeEditor.tsx` (autoSave prop drill), `index.ts` (exports).
- **BFF/shared-lib**: `IGenericEntityService`/`DataverseServiceClientImpl`/`DataverseWebApiService` (new `UpsertAsync`), `ComposeService.cs` (promote atomic upsert), `Services/Ai/ComposePdfIntakeSource.cs` (+test) (FR-11 discrimination).
- **Tests**: 12 BFF test files migrated `CreateAsync`→`UpsertAsync`; test-hygiene batch (075). Full BFF suite **10,421 passed / 0 failed**.
- **Docs**: `notes/task-0{10,11,12,13,20,73,75}-notes.md` + `task-012/013-analysis.md`.

### Critical Context
Project is fully initialized and **execution-ready**: spec (13 FRs / 6 NFRs), 20 validated POML tasks (8 phases), branch `work/spaarkeai-compose-r7` @ `6486c52ea`, **0 behind master, clean, pushed**. The branch is **net10-ready** (master is net10 as of 2026-08-14; BFF Release build clean) and **re-aligned** to the code-quality-and-assurance-r3 + dotnet-10-COMPLETE master (anchors re-verified 2026-08-15). Nothing blocks starting task 001. Do **not** re-run the pipeline — go straight to execution.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none (next: 001) |
| **Task File** | `tasks/001-coordination-gate-baseline.poml` |
| **Title** | Coordination gate + publish-size baseline + env verification |
| **Phase** | 0: Coordination Gate + Baseline |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps (initialization, not task execution)
- [x] `/design-to-spec` → `spec.md` (owner Q&A resolved: client-only draft store; candidate adds; D1 hygiene deferred)
- [x] Both open questions resolved (unified stable-logical-id for FR-03+FR-07; fidelity-wideners home → wrap-up)
- [x] `/project-pipeline` → README, plan, CLAUDE.md, 20 POML tasks + TASK-INDEX (validator clean 20/20)
- [x] `projects/INDEX.md` row appended (BFF=Y, SpaarkeAi=Y)
- [x] net10 readiness: merged net10 master, BFF Release build clean, task 001 + CLAUDE.md updated
- [x] Re-sync to code-quality-r3 + dotnet-10-COMPLETE master; anchors re-verified; baseline → net10 ~44.96 MB

### Current Step
*No active task — awaiting go-ahead for task 001.*

### Files Modified (All Task)
*No implementation files yet — R7 has written no product code.*

### Decisions Made (project-level, carried into execution)
- 2026-08-13 — Autosave draft store = **client-only** (no BFF surface; NFR-03 satisfied structurally).
- 2026-08-13 — FR-03 draft recovery + FR-07 client dedup **unified on one stable logical id** (`sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`); FR-07(b) introduces it (none exists today).
- 2026-08-13 — Fidelity wideners deferred; **home named at wrap-up** (task 090). D1 dev-data hygiene = leave/defer.
- 2026-08-14 — Branch retargeted **net10** (master is net10; never deploy BFF from a net8 tree → 503).
- 2026-08-15 — Publish baseline is **net10 ~44.96 MB incl PDBs** (supersedes net8 ~46.94 MB); task 001 re-measures empirically.

---

## Next Action

**Next Step**: `task-execute` on **task 001** (coordination gate). It runs `/conflict-check`, measures the **net10** publish baseline, verifies the PDF DI compound gate is ON in the target env, checks watched PRs, and writes `notes/coordination-baseline.md`.

**Pre-conditions**:
- Branch clean + 0 behind master ✅ (already true at handoff)
- Confirm net10-readiness in task 001 step 0.5 (SDK 10.0.1xx, Release build clean — already verified at init)

**Recommended execution order** (from TASK-INDEX):
1. **001** (gate) → 2. **010** (stable logical id — opus; blocks Phase 4) → 3. **011→012→013** (save-identity vectors) → 4. **020** (Save dropdown), **030** (name modal) → 5. **040→041** (autosave) → 6. **050→051** (PDF parity) → 7. **060→061** (hotkeys) → 8. **Group B: 073 ∥ 075** (parallel) + **070,071,072,074** (sequential) → 9. **090** (wrap-up).

**Key Context**:
- Critical path: `001 → 010 → 040 → 041 → 090`.
- opus tasks: 010, 013, 050. xhigh: 011, 012, 071.
- **parallel-safe:false on ALL Compose-spine tasks**; only Group B (073, 075) is parallel.
- Coordination: `/conflict-check` before every BFF PR; 061 vs assistant-r3 (`ConversationPane`/`SprkChatInput`); 073 consume `Services/Ai/PublicContracts/` (no fork); 075 watch PR #690.
- Anchor caveat (post-2026-08-15 merge): `AnalysisServicesModule.cs` DI gate shifted to ~L145/165 (grep the symbols, don't trust exact lines); all other anchors intact.

**Expected Output of task 001**: `notes/coordination-baseline.md` with net10-readiness, conflict-check result, net10 publish baseline (MB + PDB convention), PDF DI gate ON/OFF, PR #690/#266 status.

---

## Blockers

**Status**: None. Execution is owner-gated (deliberate), not blocked.

---

## Session Notes

### Current Session
- Focus: `/design-to-spec` → `/project-pipeline` initialization + net10 migration + code-quality-r3 re-alignment. All committed/pushed.

### Key Learnings
- No non-rotating document identity exists today — FR-07(b) must introduce one; it is the shared key for draft recovery (FR-03) + client dedup (FR-07).
- Master went net10 (2026-08-14) and absorbed the code-quality-r3 BFF refactor (2026-08-15); R7's only anchor drift was `AnalysisServicesModule.cs` — everything else intact.

### Handoff Notes (for the fresh post-compact session)
1. Read this file + `projects/spaarkeai-compose-r7/CLAUDE.md` (constraints + 2026-08-15 re-sync note) + `spec.md` (FR/NFR closed sets).
2. Confirm branch clean + 0 behind master (`git status`, `git rev-list --count HEAD..origin/master`). If behind, `git merge origin/master` first (INDEX.md is the usual conflict).
3. Start with **"work on task 001"** — do NOT re-run the pipeline; tasks already exist and validate clean.
4. Portfolio: project is NOT registered on the board (no README portfolio pointer) — run `/devops-project-register` if desired (optional).
5. Held decisions still stand: merge-to-master deferred to wrap-up; fidelity-wideners home named at task 090.

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r7 · **Branch**: `work/spaarkeai-compose-r7` @ `6486c52ea`
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Spec**: [`spec.md`](./spec.md) · **Plan**: [`plan.md`](./plan.md)

### Applicable ADRs
- ADR-049 (save path) · ADR-050 (name modal) · ADR-032 (PDF gate) · ADR-007/013 (`ProjectForMount` async tension) · ADR-021 (Fluent dark-mode) · ADR-038 (tests)

### Knowledge Files
- `spec.md`, `plan.md`, `CLAUDE.md`, `notes/r6-defer-register-consolidated.md`, `tasks/TASK-INDEX.md`

---

## Recovery Instructions

1. **Quick Recovery**: read the section at top (<30s).
2. **Confirm sync**: branch clean + 0 behind master; merge master if behind (INDEX.md conflict expected).
3. **Begin**: "work on task 001" → `task-execute`.
4. **Full protocol**: [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md).

**Commands**: `/project-continue` (full reload + sync) · "where was I?" (quick recovery).

---

*This file is the primary source of truth for active work state. Keep it updated.*
