# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-08
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | W0 ✅ + W1 ✅ + **016/030/032 FRONTEND INTEGRATION ✅** (2026-07-08). A0 seams in worktree. |
| **Step** | — |
| **Status** | 13 tasks done + integration wave **MERGED TO MASTER** (2026-07-09, master @ `978333245`; merged origin/master DailyBriefing fix #584 en route). **016/030/032 IMPLEMENTED + INTEGRATED + verified.** All 3 of 016's hooks landed; FR-18 near-side threaded; jest enabled for Compose.Components. |
| **Next Action** | Dispatch **031** (custom ProseMirror marks — edits ComposeEditor.tsx, upgrades `materializeComposeDraft` to positioned pending-redline). |

### ✅ Integration wave complete (016/030/032) — 2026-07-08
Committed the frontend+BFF integration wave. Verification:
- **Compose.Components typecheck**: clean (against built `@spaarke/*` dists — built via `scripts/Build-AllClientComponents.ps1 -Component SharedLibs`).
- **jest** (now enabled — first config for the package): `ComposeAiToolbar.test.tsx` **10/10 green**.
- **BFF**: builds clean; Compose+ADR-013 suite **169/169 green** (incl. 4 new `ChatComposeOutputsProjectionTests`). Publish size **46.46 MB compressed incl PDBs** (< 60 MB ceiling; ~0 delta — no new packages).

**What landed:**
- **016 HOOK #1** (BFF read endpoint): `GET /api/ai/chat/sessions/{sessionId}/compose-outputs` in `ChatEndpoints.cs` — reads the existing `session.Outputs` ledger surface (ADR-040), projects `compose`-disposition entries via new pure `ProjectComposeOutputs` (skips truncation markers). New `ComposeLedgerOutputDto` in `SessionLedgerEntries.cs`. §10/§11: extends ChatEndpoints + reuses `session.Outputs`; no new service/DI/package.
- **016 HOOK #2** (editor materialize): `materializeComposeDraft(draft, provenance)` added to `ComposeEditorHandle` + implemented (clean cursor insertion of `new_text` as escaped paragraphs; positioned `target_text` replace + pending-redline marks + provenance badge are **task 031**). Shared `ComposeDraftPayload`/`ComposeDraftProvenance` types now owned by ComposeEditor + imported by ComposeWorkspace (removed the local mirror + `ComposeDraftMaterializeCapable` hack).
- **016 HOOK #3** (contract): additive `ledgerRef?` on shared `ComposeAssistantToWorkspaceFlow` (compose-contracts.ts); ComposeWorkspace's local `ComposeAssistantInsertLedgerSignal` hack removed.
- **FR-18 seam (near-side)**: optional `enqueueComposeAction?` threaded toolbar → editor → workspace (`ComposeActionEnqueue` type). When a host supplies it, toolbar routes dispatch through 032's serial queue; else falls back to its own bound dispatcher.

**Deferred (with rationale):**
- **FR-18 far-side host wiring** — delivering ConversationPane's `dispatchComposeAction` across panes to `ComposeWorkspace.enqueueComposeAction` is a host/shared-context decision, and no toolbar action can dispatch until Phase-4 catalog (core-gated) wires real `bindingId`s. Near-side seam is ready; host wires it at Phase 4.
- **SpaarkeAi solution typecheck** — the 032 files (`ConversationPane.tsx`, `useSerialActionQueue.ts` + test) are prior-session WIP UNCHANGED this session; my edits are confined to the shared Compose lib (typechecks clean). Full SpaarkeAi typecheck needs a full solution `npm install` — deferred (unmodified-by-this-session files).
- **Core follow-up**: the compose WRITE path (`BindingDisposition.Compose` + `OutputRouter` case) is core task 010, not present — so the read endpoint returns `[]` until then (render-follows-store path is correctly dormant end-to-end).

### Files Modified This Session
- `notes/spikes/spike-0-dispatch-path.md` - Created - Spike 0 (dispatch seam confirmed; `compose_action_request` correction)
- `notes/spikes/spike-2-edit-validator.md` (+prototype) - Created - adeu match_mode + ambiguity errors VALIDATED (ran headless)
- `notes/spikes/spike-3-edit-batch.md` (+prototype) - Created - 4-phase batch + rollback VALIDATED (ran headless)
- `notes/spikes/spike-4-semantic-appendix.md` - Created - design-confirmed; hallucination measurement deferred
- `notes/spikes/spike-5-openxml-write.md` (+sample-annotated.docx) - Created - Open XML w:ins/w:comment writer VALIDATED (real .docx, 0 errors)
- `notes/spikes/spike-7-checkout-collision.md` - Created - checkout=Dataverse advisory lock; conflict UX from 423/412
- `design.md` - Modified - Spike 0 dispatch-contract correction (§2.1/§3/§5/§7.2/§13 + revision log)
- `tasks/000/002/003/004/005/007-*.poml` - Modified - status→completed
- `tasks/TASK-INDEX.md` - Modified - 000/002/003/004/005/007 🔲→✅

### Critical Context
Planning complete; execution started. **Spike 0 result**: the ADR-039 session-dispatch seam is
confirmed (static trace) end-to-end for a Compose Binding — ZERO new BFF dispatch routes.
**Correction for Phase 1/3/4**: the design's `compose_action_request` event does NOT exist;
use the R1-shipped six-flow contract (`compose-contracts.ts`) — a selection emits
`conversation.compose_selection_offer` (Flow 2), dispatch is a direct `dispatchConsumer(bindingId,
{slots})` call (useConsumerChips pattern), editor insertion is `workspace.compose_assistant_insert`
(Flow 5). Tasks **016/030/046** must be authored against this. The parallel Compose action endpoint
is confirmed deleted (design §2.1/§7.2 holds). Remaining split unchanged: independent tracks
startable now; core-gated tracks (⛔) wait on core R2 Phase A0.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 031 |
| **Task File** | tasks/031-custom-marks.poml |
| **Title** | FR-15 custom ProseMirror marks (insertion/deletion/commentAnchor) |
| **Phase** | 3 Inline Editing UX |
| **Status** | ✅ completed (2026-07-09) |
| **Started** | 2026-07-09 |
| **Rigor** | FULL · sonnet@high (session on Opus) · directional |

**031 done**: 3 `Mark.create` marks (`src/widgets/marks/`) registered additively in ComposeEditor; span+`data-compose-mark` render with provenance attrs + token-based redline classes (ADR-021). Code-review caught + fixed a StarterKit-Strike parse collision. jest 19/19; build green; zero new deps. Materialization from ledger = FR-16 / task 033.

---

## Progress

### Completed Steps
*No steps completed yet — task decomposition pending*

### Files Modified (All Task)
*No task files yet*

### Decisions Made
*Project-level decisions recorded in CLAUDE.md §Decisions Made*

---

## Next Action

**Next Step**: `/task-create projects/spaarkeai-compose-r2`

**Pre-conditions**:
- plan.md phase breakdown reviewed (done)
- Worktree synced to master (done — 0 behind)

**Key Context**:
- Refer to `plan.md` §4 for phase deliverables + core-A0 gating markers
- Refer to `spec.md` for FR/NFR acceptance criteria
- ADR-039/040 govern the AI dispatch + ledger surface

**Expected Output**:
- `tasks/*.poml` files + `tasks/TASK-INDEX.md` with dependency graph + `blocked-on: core-A0` markers + parallel groups

---

## Blockers

**Status**: None (planning) — note: several implementation phases are gated on core R2 Phase A0 (see CLAUDE.md §Core Phase A0 dependency)

---

## Session Notes

### Current Session
- Started: 2026-07-08
- Focus: project initialization (design refinement → spec → adr-check → planning artifacts)

### Key Learnings
- Entry-point state verified in code: 1c works; 1a/1b are build items; mount seam (`docxBytes`) + `PromoteIfEphemeralAsync` already exist (shrinks scope)
- Core R2 authored this project's initial design.md; core setup being finalized — dependency is real but coordinated

### Handoff Notes

**W0 spike-surfaced corrections (fold into design/spec + task authoring before the affected tasks run):**
- **Spike 0** → design.md ALREADY corrected (`compose_action_request`/`compose_edit_apply_request` don't exist; real contract = Flow 2 `compose_selection_offer` + direct `dispatchConsumer` + Flow 5 `compose_assistant_insert`). Affects tasks 016/030/046.
- **Spike 2** → task 020 (FR-19): adopt adeu `match_mode` + structured ambiguity errors verbatim; **fuzzy/typo matching is Phase-2 deferred** (not in validator); task 020 must state which document projection the offsets are relative to.
- **Spike 3** → ✅ APPLIED design §6.1: overlap (non-fatal skip-and-report) vs validation-failure (fatal whole-batch rollback) are **two separate code paths**. Tasks 021/022 must model both.
- **Spike 4** → stale "defer defined-terms to R3" superseded by 2026-07-08 scope-lock; `SemanticAppendixGenerator` (deterministic pre-scan) ≠ `compose-defined-terms` Action (LLM checker) — keep distinct. Cross-refs need OOXML (Phase-2 reader), not flat text. (No design edit needed — captured for task 004/060/044 authoring.)
- **Spike 5** → ✅ APPLIED design §14 + publish-size: `DocumentFormat.OpenXml` 3.4.1 already a BFF dep (`Sprk.Bff.Api.csproj:128`) → **zero package/size delta** for the writer; `Codeuctivity.OpenXmlPowerTools` only needed IF diff/redline is built (reader/compare), NOT the writer. Task 050 gotcha: `w:del` text = `DeletedText` not `Text`.
- **Spike 7** → ✅ APPLIED spec FR-24: `If-Match`/ETag is a NEW capability task 050 must ADD to the SPE write facade (not existing); catch 423/412 → typed conflict. Remaining 4 gaps for tasks 054/055 (Word-open signal via webhook/delta; pending-annotation durability decoupled from lock; wire checkout stubs; defer path). Conflict UX driven by write-back outcomes (423/412), not checkout state.

**Runtime-deferred verifications** (need deployed env / live LLM — recipes in each note): Spike 0 §6 (SSE+ledger live), Spike 4 (hallucination A/B measurement), Spike 5 (Word-for-Web native render), Spike 7 (423/412 status + Word-for-Web UX).

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r2
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (pending)

### Applicable ADRs
- ADR-039 (dispatch/catalogs), ADR-040 (ledger), ADR-013 (AI facade) — the load-bearing three; full list in CLAUDE.md §Resources

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above
2. **If more context needed**: Read CLAUDE.md + plan.md §4
3. **Load task file**: (none yet — run task-create first)
4. **Resume**: from the "Next Action" section

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
