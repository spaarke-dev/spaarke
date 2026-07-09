# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-09 (by context-handoff — pre-compact, parallel-wave plan captured)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Phase 3+4. **DONE + MERGED TO MASTER** (`7f5e592c4`, 2026-07-09): 031 marks, catalog 040/041/043/044, compose-disposition routing promotion + task-035 OutcomeCard reconciliation. Branch clean, 0 ahead of master, main repo synced. |
| **Step** | — (between tasks) |
| **Status** | **All prior work merged to master.** redesign-r2 coordination CLOSED: they APPROVED the routing promotion + I applied their one required fix (compose case returns `Outcome = outcome`, task 035). Also merged core Wave K (038 trace view + 035 completion engine). 200 unit tests + 23 router tests green. |
| **Next Action** | **DISPATCH PARALLEL WAVE** (user-requested): 042 + 033 + others. See "Parallel wave plan" below. Then serialize 034 after 033. |

### ▶️ NEXT: Parallel wave plan (user asked to run 042 + 033 + any safe parallels)
Analysis of newly-unblocked tasks (parallel-safe flag / deps / files / build-system):
| Task | Tier | parallel-safe | Files (footprint) | Build | Notes |
|---|---|---|---|---|---|
| **042** draft-alternative | opus | **true** | `infra/dataverse/actions|inputschemas|outputschemas/compose-draft-alternative.*` + Binding row in `sprk_playbookconsumer-rows.json` | none (JSON) | 5th catalog row. **Binding now declares `disposition=compose` (100000006)** — routing live. Follow the compose catalog template (see 040 `.action.json`). Output schema per POML. Owner hygiene: no `@v1`. |
| **033** pending-redline | opus | **false** | `ComposeEditor.tsx` + NEW `widgets/hooks/usePendingRedline.ts` | npm | Materialize pending redlines from ledger using the 031 marks + compose-outputs read. parallel-safe=false (shares ComposeEditor) → **run in MAIN SESSION**. |
| **034** undo/replace | opus | false | `usePendingRedline.ts` (shared w/033) + `useEditSupersession.ts` | npm | **dep 033 → serialize AFTER 033.** |
| **050** DocxAnnotationWriter | opus/xhigh | true | BFF `Services/Compose` (C#) | dotnet | Word writer (hard). Disjoint. |
| **051** DocxAnnotationReader | sonnet | true | BFF (C#) | dotnet | Disjoint. |
| **060** anchored annotations | sonnet | true | (verify files before adding — likely ComposeWorkspace/BFF) | ? | deps none. Check footprint. |
| **061** action history via ledger | sonnet | true | BFF (C#) | dotnet | deps none. Disjoint. |
| **064** context-pane trace | sonnet | true | frontend Context pane | npm | core 038 landed → unblocked. |

**Build-contention rule** (shared worktree): NO concurrent `dotnet build`; NO concurrent `npm build`. So a safe wave = at most 1 dotnet-building task + 1 npm-building task + unlimited no-build (catalog JSON) tasks, running as sub-agents that write DISJOINT files; MAIN SESSION runs the consolidated build/test after.

**RECOMMENDED SAFE WAVE (3-way):**
- **Sub-agent A → 042** (catalog JSON, no build, parallel-safe=true)
- **Sub-agent B → 061** (action-history-ledger, BFF/dotnet, sonnet, deps none, disjoint) *(or 050 writer if you prefer the Word push next)*
- **MAIN SESSION → 033** (pending-redline, ComposeEditor/npm, opus, parallel-safe=false)
Then **034** after 033 (shares usePendingRedline.ts). Each task STILL runs via `task-execute` (FULL rigor; 042/033 also test-touching/opus). Cap 6 agents. After the wave: main session `dotnet build` + `npm run build`/typecheck + jest.

### Merged-to-master commit trail (2026-07-09)
`540760eac` routing promotion · (Wave K merge) · `7f5e592c4` task-035 OutcomeCard reconciliation (HEAD/master). Earlier: `978333245` 016/030/032 · catalog wave · gating.

### Still core-gated (not startable)
**042/033/034/064/060/061 NOW unblocked.** 071 needs 070 (070 deps 030/031 done → 070 startable but parallel-safe=false). Still blocked: **063** (core 057 memory.write — LAST A0 seam before the 017 "Compose UNBLOCKED" milestone). 045 (eval) needs 042; 047 (deploy) needs 045; 034 needs 033.

### ⬇️ Prior-session history below (016/030/032 integration wave — 2026-07-08)

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
| **Task ID** | 040/041/043/044 (Phase 4 catalog wave) |
| **Task File** | tasks/04{0,1,3,4}-*.poml |
| **Title** | FR-07/08/10/11 compose Action + Binding catalog rows |
| **Phase** | 4 Catalog |
| **Status** | ✅ completed (2026-07-09) — 042 DEFERRED (core 010) |
| **Rigor** | FULL · sonnet@high (session on Opus) · directional |

**Phase-4 catalog wave (040/041/043/044) done — mirror-first, ADR-039.** Each capability = action-only seed (`infra/dataverse/actions/{code}.action.json`, systemPrompt home) + input mirror (`inputschemas/`) + output mirror (`outputschemas/`) + Binding row (`sprk_playbookconsumer-rows.json`). 13 files valid JSON; no banned property-level `required:true`; OptionSet codes verified vs `Binding.cs` (disposition=Informational/risk=None/captureMode=LoopElicitation = 100000000). Deploy = task 047 (`Deploy-AnalysisAction.ps1` + `Seed-PlaybookConsumers.ps1`); eval cases = task 045.
- **SystemPrompt-home decision** (was the open review question): lives on `sprk_analysisaction.sprk_systemprompt` in an **action-only** seed file (no playbook — engine frozen); grounded in the R5 rule "sprk_systemprompt IS the JPS prompt primitive."
- **042 (draft-alternative) DEFERRED**: its Binding declares the `compose` disposition = core task **010** (`BindingDisposition.Compose` + OutputRouter case), not landed. TASK-INDEX 042 → 🔴/⛔ (was wrongly unblocked).
- **In-file REVIEW FLAGS** (non-blocking, for deploy/seed validation): 044 `surfaces="context"` (renders in Context pane — confirm the surface value is recognized); 043 input `changesText` upstream wiring finalizes with tasks 051/054; `ucid` left null pending a compose use-case id; span fields authored as text snippets (LLM-reliable).
- **031 (prior) done**: 3 custom marks; jest 19/19; on branch.

**Next**: 045 (eval cases ≥5 golden + ≥5 dispatch per row — FULL, modifies tests/) then 047 (deploy). 046 (dispatch wiring) also startable. 042 waits on core 010.

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
