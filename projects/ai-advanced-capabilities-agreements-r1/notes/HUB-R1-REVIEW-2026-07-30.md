# Hub-r1 Thorough Review — verified built state vs claims (2026-07-30)

> **Purpose**: Ground agreements-r1 re-planning in **verified fact**. A 7-agent adversarial review checked every
> load-bearing claim in the hub's reverse coordination doc
> (`projects/ai-advanced-capabilities-analysis-hub-r1/notes/COORDINATION-hub-r1-TO-agreements-r1.md`, commit `0370f4dee`
> — the untracked copy formerly in this notes/ dir was deleted post-merge as an identical duplicate) against origin/master code, the live spaarkedev1
> Dataverse environment (MCP), and git topology. All claims below carry file:line / SHA / env evidence.
> **Method**: parallel verifiers over (A) hub project state, (B) master-vs-branch delta, (C) BFF spine, (D) schema/registry,
> (E) client surface, (F) retirement, (G) the Part C.1 disposition proposal. ~850k tokens, 248 tool calls.
> **Full agent transcripts**: workflow `wf_787e4a86-b41` journal.

---

## 1. Headline verdicts

| Question | Verified answer |
|---|---|
| Is hub 001–070 shipped? | ✅ **Yes, and ALL of it is in origin/master** — `rev-list master..hub-branch` is EMPTY; hub tip `4347d14c2` is an ancestor of master tip `43a1b8f86`. Zero branch-only commits. |
| Is Phase 1 (NDA card → wizard-as-modal → editable Compose) shipped? | ✅ In master (`980e6bc21` Quick Start + `8b93ad9e2` Phase 1 + 3 UAT-fix commits) and deployed to spaarkedev1 — **awaiting owner UAT**. |
| What remains on hub? | 071 🔄 (**user-gated env work**: ribbon-button XML import + web-resource delete blocked by the `sprk_analysis` main form still referencing the retired HTML WR) · 072 e2e 🔲 · 090 wrap-up 🔲. No hidden commits behind these. |
| Is Phase 2 (bind session + auto-dispatch review on wizard finish) built? | ❌ **NOT built anywhere.** Design-only. |
| Is Phase 3 (durable recall — reopen restores review results) built? | ❌ **NOT built anywhere.** |
| ⚠️ Is OUR worktree current? | ❌ **No — HEAD `ef6af3246` is ~37–41 commits behind origin/master**, missing 4 of 5 Phase-1 commits (`7f842ecbd`, `b73127562`, `a84e28635`, `8b93ad9e2`), `HANDOFF-2026-07-31.md`, the tracked coordination doc (`0370f4dee`), and PRs #701–#704. **Merge master before planning.** |

**Phase 2 + Phase 3 are the owner's 3-phase MUST-HAVE remainder, and the hub explicitly routes them to agreements-r1**
(reverse doc Part C; `HANDOFF-2026-07-31 §4d` "coordinate before building durable recall"). agreements-r1 is now on the
critical path of that must-have.

---

## 2. A1–A6 verified (with corrections to the coordination doc)

| Ask | Doc said | Verified reality |
|---|---|---|
| **A1** wizard sub-domain picker | "Will build (small extension)" | **NOT built.** No picker code exists. 🚨 **PICKER LANDMINE**: all 3 seed rows have `sprk_isselectable = false` (incl. `nda`) — the doc's proposed filter would render an **empty picker**. Seeds wrong or semantics inverted; owner call needed. Slot options: wizard inline infoStep (`CreateAnalysisWizardWidget.tsx` 'analysis-details' renderContent) or the card layer (`QuickStartModal.tsx:211-216` / `AnalysisCardsWidget`). |
| **A2** sub-domain persistence | "Done (built)" | **PARTIAL — env-only.** `sprk_agreementtype` table EXISTS in spaarkedev1 with **3/10 seed rows** (general ✓fallback / nda / employment) and ALL 8 columns (identity + behavior; behavior values null, ours to fill via `update_record` — **no schema work needed**). 🚨 **Naming correction: the lookup's logical name is `sprk_agreementtype`** (OData `_sprk_agreementtype_value`), **NOT `sprk_agreementtypeid`** as the doc states 3×. **ZERO code references anywhere** — no TS type, no infra mirror, no seed JSON, no schema note. We must author the code mirror ourselves. `sprk_key` unique-constraint claim UNVERIFIED. Undocumented `sprk_description` column exists. |
| **A3** launch envelope | "Mostly built; finishing" | `activeWorkType` fully shipped end-to-end (launch-resolver → main.tsx → ThreePaneShell → ComposeEditor → `getToolsForSurface`, `ComposeAiToolbar.tsx:492-502`). **`subDomain` exists NOWHERE** — zero grep hits. Also: the `worktype` URL param is a **boolean "new-mode" flag** (doesn't pre-select a card), and the `regarding` URL param is **parsed then discarded** (`void regarding`, `main.tsx:592-596`) — the real regarding channel is `entityLogicalName`/`entityId` → `analysisInitialAssociation`. |
| **A4** fork/promote callable from classifier path | "Confirmed YES" | ✅ **CONFIRMED** — both endpoints live, zero wizard coupling, exact contracts in §3. **But two sharp caveats**: (1) 🚨 **silent-FK gap** — `PromoteSessionToAnalysisAsync` ignores `BindSessionToAnalysisAsync`'s bool (`ChatSessionManager.cs:527`); if the session's `sprk_aichatsummary` row was never created (Dataverse write is tolerated-failure), promote returns **201 with NO durable FK** → session invisible to `by-analysis` lookups/hub grid. (2) promote **requires a document** (400 for document-less sessions) and is **one-time bind** (400 on re-promote). Fully-expired (Redis+Cosmos-gone) sessions 404 — Dataverse cold path never reconstructs (`GetMessagesAsync` returns empty). Client "Promote…" dialog already exists (`HistoryOverlay.tsx:79-80`) — reuse. |
| **A5** `sprk_analysisoutput` memo target | "Confirmed stable" | ✅ CONFIRMED. `analysisId` flows via `session.HostContext.EntityId` into **every** dispatch envelope (`SessionDispatchOrchestrator.cs:483-485` → `HostEntityId`). The `AnalysisMetadata["analysisId"]` channel is **dead** — don't use. **Footgun confirmed + sharper than doc**: the sentinel `EntityType="sprk_analysisoutput"` (EntityId = an `sprk_analysis` GUID) is **triplicated** with comment-only coupling (`ChatDataverseRepository.cs:36`, `AnalysisEndpoints.cs:1102`, `ChatSessionManager.cs:473`); legacy resolvers (`AnalysisChatContextResolver.cs:264-284`, `SprkChatAgentFactory.cs:694-697`) query the output TABLE with that id and fail on fork/promote-bound sessions. |
| **A6** KEEP surfaces | "Confirmed" | ✅ 6/7 retirement claims CONFIRMED; KEEP set fully intact. **PARTIAL**: `sprk_chathistory` — chat-transcript semantics removed, but the **column is LIVE with second semantics** (Insights `ObservationMirrorMapper.cs:163` writes producer-context JSON on `sprk_analysis` mirror rows). Never delete/repurpose it. `AnalysisResponse.ChatHistory` wire field survives but is **always empty** — transcripts only via `GET /api/ai/chat/sessions/by-analysis/{id}` (Cosmos). Retirement not operationally done: 071 env deletion user-gated; 072 e2e never run. |

---

## 3. Exact contracts agreements-r1 will call (verified)

**`POST /api/ai/analysis/fork`** (`AnalysisEndpoints.cs:58,1136-1258`; rate-limit `ai-batch`)
Request `{ priorSessionId!, documentId! (Guid, 400 if empty), name!, playbookId?, hostContext? }` — client
`entityType`/`entityId` **ignored**, server sets sentinel + analysisId; only `workspaceType`/`pageType` survive.
Response 201 `{ analysisId, newSessionId, archivedSessionId }` (archivedSessionId = echoed input, not re-verified).
Archive-marker failure is non-fatal (still 201). Compensating Analysis delete if session mint throws.

**`POST /api/ai/analysis/promote`** (`AnalysisEndpoints.cs:77,1285-1404`)
Request `{ sessionId!, name!, documentId? (falls back to session.DocumentId; 400 if neither), playbookId? }`.
Response 201 `{ analysisId, sessionId (UNCHANGED — keep using it) }`. 400 already-bound (one-time). ⚠️ silent-FK gap above.

**`GET /api/ai/chat/sessions/by-analysis/{analysisId}`** (`ChatEndpoints.cs:173,1934-1959`) → most-recently-created
`{ sessionId, messageCount, isArchived, createdOn }`; 404 when none. (Archived sessions still appear in History.)

---

## 4. Part C.1 durable-recall re-route — verified SOUND but 4 changes, not 1 (this is our new scope)

All five factual claims CONFIRMED (`OutputRouter.cs:193-224,266-267` informational store-then-pass-through;
`ChatEndpoints.cs:1322` compose-only filter; `:1848-1856` restore has no Outputs; `useNdaReviewAdvisoryCommentsBridge.ts:128-155`
live-terminal-only projection; `ComposeWorkspace.tsx:1609-1616 → 1385-1484` FR-04 refresh-durability effect).
One mitigation the doc omits: **saved** documents DO retain advisory threads as native `w:comment` — it's the
review-STATE (badges, summary panel, gutter cards) + unsaved sessions that are lost.

**"A disposition problem" is 1 of 4 required changes:**
1. **Binding flip** `sprk_disposition` informational→compose (Dataverse data; server unchanged).
2. **Payload shape**: `{overallRisk, flaggedSections[]}` matches **NO** materialization branch. Either emit
   `comments:[{target_text,comment}]` (loses riskLevel/sectionRef/standardRef — `AnchoredAnnotation` has no such
   fields) **or extend the materializer with a findings branch calling `placeAdvisoryComments`** (which carries the
   metadata). ← **Aligns exactly with our FR-05 schema split — do them together.**
3. **Session routing (DEF-09)**: informational review dispatches on the **chat** session; compose-outputs are read
   from the **document** session (`ConversationPane.tsx:939-949` `sessionIdOverride`). Flip disposition alone → output
   lands where ComposeWorkspace never looks.
4. **Apply-leg gating**: compose outputs enter Accept/Reject/undo machinery (`emitComposeApplyLeg`,
   `ConversationPane.tsx:966-1002`); a comments-only payload hits `materializeComposeDraft` with no edits →
   needs an explicit comments-only/findings branch to avoid spurious redline staging.

**Risks to design around**: 128KB inline-payload cap (`SessionLedgerEntries.cs:50`) — big agreements with many
`quotedText` findings can exceed it → **truncation marker silently skipped** by the projection (`ChatEndpoints.cs:1326-1329`);
highest-turn-only re-materialization (`ComposeWorkspace.tsx:1422`) — findings + later draft-alternative coexisting →
only latest replays (FR-29 AnchoredAnnotations store is the second durability layer); supersede endpoint must not retract
findings; Cosmos write-through is **fire-and-forget** (swallowed exceptions) and `DELETE /sessions/{id}` **erases the
ledger** (GDPR) → **our FR-13 memo→`sprk_analysisoutput` is the only store surviving deletion** (strengthens its rationale).
`NdaReviewSummaryPanel` is fed **only** from the live event (`ComposeWorkspace.tsx:1577-1586`) — reopen restores gutter
comments but leaves the summary panel empty unless it too reads re-materialized state.

**Attribution fix**: the refresh-durability effect is **spaarkeai-compose-r2 task 016**, not a hub task.
**DEF-01 note**: `ComposeEditor.advisoryComments.test.tsx` has **no skip markers** — it's a **weakened assertion**,
not a skipped suite; diff against the original assertion.

---

## 5. Other verified facts that change our plan

- **Registry ≠ Action/Binding data.** Our design said the sub-domain registry is "Action/Binding data"; **reality: it's
  the `sprk_agreementtype` Dataverse table** (hub-built, env-only). Ownership split: hub = identity columns; **we own the
  behavior VALUES**: `sprk_knowledgepackref`, `sprk_classificationcue`, **`sprk_confidencethreshold`** (per-type override
  of our ≥0.85 baseline — the per-type mechanism already exists as a column!). We author the code mirror (TS type +
  seed/infra JSON) since none exists.
- **The hub card is "NDA Analysis" (`nda-analysis`), not "Agreement Analysis"** — Phase 1 deliberately scoped to NDA
  (only live card). Our generalization (classifier + general Action) is what turns it into the general card. (Worktree
  HEAD still shows the older `agreement-review` id — another merge-first reason.)
- **Wizard steps reordered by UAT** (`7f842ecbd`, master only): Step 1 Associate To → Step 2 Add file(s) (REQUIRED) →
  Step 3 Analysis Details (+2 contact lookups; Access step removed).
- **File-id impedance (Part C.4) confirmed on both sides**: wizard → durable `sprk_document` (`sprk_graphitemid/driveid`
  via `EntityCreationService`); dispatch → session-uploaded fileIds only, hard error "No session files were available"
  (`SessionDispatchOrchestrator.cs:769`). **No bridge exists.** The wizard creates NO session and runs NO review. Bridging = ours (Phase-2 wiring).
- **`100000000 → 'agreement-analysis'` mapping hard-coded in 3 places** (`main.tsx:605-607`, master `WorkspacePane:993`,
  master wizard `:829`) — consolidate when we generalize.
- **`sprk_agreement` entity**: env-only bare shell (name + `sprk_agreementtype` lookup). Assume nothing more.
- **Ribbon entry (2b/2d) not user-reachable**: launcher JS deployed, but ribbon-button XML never imported (071 user-gated).
- **Name collision hazard**: `sprk_confidencethreshold` also exists on `sprk_communicationrule` with live BFF semantics — don't conflate.
- **Task 031 grid row-click reopen was deliberately DROPPED** — grid row-click opens the OOB form; rehydration machinery
  survives only on the `analysisId` entry path (`openSpaarkeAi` / subgrid / 2c-2d).
- **PRs #701–#704** (email-r5, messaging-r3) churned shared files (`WorkspacePane` 579 lines, `EmailComposer`,
  `ConversationView`) — conflict awareness for our #6 email work.
- **Untracked duplicate**: our `notes/COORDINATION-hub-r1-TO-agreements-r1.md` is an untracked copy; the tracked original
  lives in the hub project's notes/ (arrives with the master merge). Delete the copy after merging.

---

## 6. Answers to the hub's 4 open asks (proposed)

1. **C.1 ownership** → **CONFIRM ours.** It composes with FR-05 (schema split feeds the findings-materializer branch),
   FR-06 (general Action), FR-13 (memo). Scope is the 4-change set + risks in §4 — bigger than "flip a flag," and we plan it as such.
2. **Behavior columns** → **CONFIRM ours** (values only; no schema work). We'll also author the missing code mirror + remaining 7 seed identity rows (or hand the row list to hub).
3. **A1 picker filter** → **BLOCKED on the landmine**: all seeds have `sprk_isselectable=false`. Owner must fix seed
   values (set selectable=true for live types) or invert the filter semantics before anyone builds the picker. Also decide **who builds the picker** — hub said "will build" but its remaining tasks are deploy/e2e/wrap-up only.
4. **A4 promote-vs-convenience-endpoint** → **promote fits our classifier path** (with documentId supplied). But ask hub
   (or file ourselves) a fix for the **silent-FK gap** (`ChatSessionManager.cs:527` ignoring the bind bool) — for a legal
   work product, a 201 that silently fails durable binding is unacceptable.

---

## 7. Immediate actions for agreements-r1

1. **Merge origin/master into this worktree** (37–41 commits behind; missing Phase 1 + handoff + coordination doc).
2. **Delete the untracked coordination-doc copy** after merge (tracked original arrives under hub notes/).
3. **Update `design.md` + `spec.md`** with the deltas: registry = `sprk_agreementtype` table (naming: lookup logical name
   `sprk_agreementtype`; `subDomain` ≡ `sprk_key`); NEW scope = Part C.1 durable-recall re-route (4 changes) + Phase-2
   file-id bridge/auto-run wiring; per-type threshold column; DEF-01 weakened-assertion wording; A1/A3 status corrections.
4. **Send §6 answers to the hub owner** (+ the picker landmine and silent-FK gap as coordination items).
5. Then `/project-pipeline`.
