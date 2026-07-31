# CLAUDE.md — `ai-advanced-capabilities-agreements-r1` (project context)

> Loads with every task in this project. Root `CLAUDE.md` rules still apply. Code wins over docs.

## 🚨 Task execution protocol (MANDATORY)
Execute every task via the **`task-execute`** skill (root CLAUDE.md §4). Do NOT read POML files and implement manually. Declare rigor level at task start.

## What this project is
The **type-agnostic Agreement Analysis review machine** on the shipped nda-r1 Compose surface + hub-r1 platform:
document-driven **classifier + orientation** (Reasoning tier, ≥0.85 confirm gate, composite "both"=multi-pack) over the
**`sprk_agreementtype` registry**; ONE general `agreement-review` Action (per-type knowledge packs = the value, general
= fallback; packs authored by sibling projects, zero code here); review-depth UX (multi-select batch, bidirectional
highlight, separated confirmations); **durable review** (FR-16 compose-disposition zero-LLM reopen + FR-17 wizard
auto-run — the hub's Phase-2/3 remainder, accepted); memo + Word-export fidelity; DEF-01 + rename + WS-4 fidelity.
See `spec.md` (17 FRs) + `notes/HUB-R1-REVIEW-2026-07-30.md` (the verified seam map — read before Phase 2/3 tasks).

## Binding gates for this project
- **§10 BFF Hygiene** — BFF-touching tasks (020/030/033/050/051/060): Placement Justification (cite
  `.claude/constraints/bff-extensions.md`), publish ≤60 MB (baseline ~49.63 MB incl. PDBs), no new HIGH CVE.
  Hot-path: **BFF=Y, SpaarkeAi=Y**. `/conflict-check` before every BFF PR.
- **§11 Reuse-first** — batch loops the SHIPPED single-note dispatch; memo uses `sprk_analysisoutput` + existing
  renderers (`DocxAnnotationWriter` is RETIRED); registry = the ONE `sprk_agreementtype` table (no parallel lists);
  findings re-materialize via `placeAdvisoryComments` (NOT `registerAiReviewComments` — metadata loss).
- **TEST-MODIFYING override** — 002 (evals), 012 (assertion restore), 061: unconditional code-review + adr-check.
- **DEF-01 contract** — ambiguous/not_found targets are REPORTED, never silently placed; restore the ORIGINAL
  assertion (weakened, not skipped — diff git history).
- **Sentinel footgun** — `HostContext.EntityType="sprk_analysisoutput"` carries an **`sprk_analysis` GUID** as
  EntityId (triplicated constant: `ChatDataverseRepository.cs:36`, `AnalysisEndpoints.cs:1102`,
  `ChatSessionManager.cs:473`); never query the output table with it; never re-type the literal.
- **Registry naming** — `subDomain` ≡ `sprk_agreementtype.sprk_key`; the `sprk_analysis` lookup's logical name is
  **`sprk_agreementtype`** (`_sprk_agreementtype_value`), NOT `sprk_agreementtypeid` (that's the PK).
- **Hot files** — `ConversationPane.tsx` (021→031→041→042), `ComposeEditor.tsx` (010→052→011→012),
  `ComposeWorkspace.tsx` (030→032): sequence with a commit between; never parallel.

## Applicable ADRs (load per task by tag)
**AI/dispatch**: ADR-039 (grounded execution; advisory amendment inherited from nda-r1 — no new amendment), ADR-043
(execution spine + DispositionRoutability single-source), ADR-040 (ledger; 128KB cap), ADR-041 (confirmation/outcomes),
ADR-016 (rate limits — sequential batch), ADR-013 (facade; redesign-r2 ownership lock RETIRED — architecture rule
stands), ADR-015 (governance — memo survives ledger deletion) · **Compose**: ADR-049 (shadow doc + WS-4; concise-only),
ADR-037/033 (streaming), ADR-030 (PaneEventBus), ADR-031 (stage lifecycle) · **Data/auth**: ADR-007 (SPE facade),
ADR-044 (GUID canonicalization), ADR-027 (solution/data mgmt), ADR-028 (auth), ADR-008/010/019 (endpoint hygiene) ·
**UI**: ADR-021 (Fluent v9 + dark mode), ADR-012 (shared lib), ADR-026 (code page), ADR-045 (EmailComposer for memo
email) · **Testing**: ADR-038 (**full version only**: `docs/adr/ADR-038-testing-strategy.md` — no concise file exists) ·
**Deploy**: ADR-029 (publish hygiene).

## Key constraints / patterns
`.claude/constraints/`: bff-extensions (BINDING), ai, data, testing, api, azure-deployment ·
`.claude/patterns/ai/`: node-executor-authoring, public-contracts-facade, analysis-scopes, streaming-endpoints ·
`.claude/patterns/ui/`: fluent-v9-component-authoring, fluent-v9-portal-gotcha ·
`.claude/patterns/dataverse/`: entity-operations.

## Entry points (verified seam map — full detail in notes/HUB-R1-REVIEW-2026-07-30.md)
- Registry: `sprk_agreementtype` (env; GUIDs in the 001 seed JSON) + mirror `Spaarke.UI.Components/src/types/sprkAnalysis.ts`
- Action/Binding: `infra/dataverse/actions/` + `sprk_playbookconsumer-rows.json`; classifier contract:
  `Services/Ai/Insights/Playbooks/layer1-classification.node.json`
- Dispatch: `Services/Ai/Chat/SessionDispatchOrchestrator.cs` (:483 HostEntityId; :689-769 file resolution)
- Bind: `POST /api/ai/analysis/fork` (:58) / `promote` (:77; FK gap `ChatSessionManager.cs:527`); read
  `GET /api/ai/chat/sessions/by-analysis/{id}`
- Durable recall: `OutputRouter.cs` → `ChatEndpoints.ProjectComposeOutputs` (:1312, compose-only, skips truncation) →
  `ComposeWorkspace.materializeComposeDraftFromLedger` (:1385) + FR-04 effect (:1609); DEF-09 `sessionIdOverride`
  (`ConversationPane.tsx:939-949`)
- Review UX: `ComposeCommentGutter` (noteTools) → `ConversationPane.dispatchComposeAction` → `makeComposeEditControlsMessage`
- Export: `composeSessionCommentThreadsToAnchoredComments` (`ComposeCommentThread.types.ts:256`; never-export scope :89);
  author literal `ComposeEditor.tsx:2146`; server `ApplyComment` NO change
- Launch: `launch-resolver.ts` (`SpaarkeAiLaunchParams`; subDomain per hub bd64a69d4) → `main.tsx` parse → wizard
  `CreateAnalysisWizardWidget.tsx` (A1 picker 1e1a6579b)

## Task summary (generated by /project-pipeline 2026-07-31)
23 tasks / 7 phases. Critical path: `002 → 030 → 031 → 032 → 060 → 061 → 090`. See `tasks/TASK-INDEX.md` for waves.
- **Phase 0** (001–003): registry mirror+seeds · Action generalization+schema split (opus) · knowledge packs.
- **Phase 1** (010–012): rename · WS-4 anchoring · DEF-01 fix.
- **Phase 2** (020–023): classifier (opus) · orientation+gate · subDomain envelope · explicit-path bind.
- **Phase 3** (030–033): disposition flip+findings materializer (opus) · DEF-09 routing+gating · panel restore+caps · auto-run bridge (opus).
- **Phase 4** (040–042): bidirectional highlight · multi-select batch · confirmations.
- **Phase 5** (050–052): memo assembly+persistence · toolbar docx/email · Word-export mirror.
- **Phase 6** (060/061/090): deploy · e2e (zero-LLM reopen assert) · wrap-up+test-diet+registration recipe.

## Cross-project (live)
- **hub-r1**: A1+A3-core shipped; 022 finishes the deferred envelope legs (coordinate — their standing offer); 033
  checks Phase-1 UAT first. Open Qs doc: `notes/COORDINATION-agreements-r1-ANSWERS-and-QUESTIONS-to-hub-r1.md`.
- **compose-r5**: most-active branch on OUR Compose files — rebase early. **notification-spine-r1**: same
  DispositionRoutability surface as 030. **PR #690** (LFS fixtures): merge before Compose seam/eval CI.

## Current state
Next action: execute **Wave 0** (001 ∥ 002 ∥ 010) via `task-execute`. See `current-task.md`.
