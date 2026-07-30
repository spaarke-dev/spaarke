# Agreement Analysis — Review Depth & Output Deliverables (agreements-r1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-30
> **Source**: `design.md` (+ `design-discussion.md`, `notes/HANDOFF-from-compose-fidelity-r4.5.md`, `notes/word-comment-export-gap.md`, `notes/COORDINATION-with-analysis-hub-r1.md`)

## Executive Summary

Generalize the shipped NDA advisory-review vertical into a **type-agnostic Agreement Analysis review capability** that
works for any agreement type, and make the review deeper to work with and exportable as a business deliverable. The
core new intelligence is a **document-driven classifier + orientation** (detect *is-this-an-agreement* + *which type* →
bind that type's knowledge, scope tools, focus the Assistant), backed by **one general agreement-review Action** whose
value comes from **per-type knowledge packs** (NDA is the shipped exemplar; other types register later via sibling
projects). On top of that: multi-select batch actions, bidirectional summary↔note↔document highlighting, cleaner
Assistant confirmations, a first-class **Review Summary Memo** (generate-docx / email), and **Word-comment export
fidelity** — plus two folded-in R4.5 items (the DEF-01 clause-anchoring correctness fix and the nda→agreements
rename/WS-4 wiring).

## Scope

### In Scope
- **Generalization**: confirm the Advisory Review runs for all agreement types (remove NDA gating); rename
  `ndaClauseLocation.ts` → `clauseLocation.ts` (+ `NdaReviewSummaryPanel` naming); consume the WS-4 reference layer.
- **One general `agreement-review` Action** (type-agnostic method prompt), with NDA's clause taxonomy/rubric moved into
  the **NDA knowledge pack**; a data-driven **sub-domain registry** (owned here); a **general/fallback** pack.
- **Document classifier + orientation** (two-level: work-type + sub-domain), Reasoning-tier, reusing the Insights
  Layer-1 node *contract* (typed enum + confidence); orients `activeWorkType`, knowledge, tool palette, discussion.
- **Confirmation gate** fired on **uncertainty** (below ≥0.85) *or* **multiplicity** (composite doc → choice of lens,
  incl. **"both" = multiple packs**).
- **Two co-equal human-present entry modes**: explicit (consume the hub wizard's type selection) + interactive
  (Assistant chat-upload → classifier).
- **Review-depth UX**: #2 bidirectional highlight · #3 multi-select + sequential batch AI action with progress · #4
  separated + location-labelled Assistant confirmations.
- **DEF-01**: advisory-comment placement ambiguity fix + re-enable `ComposeEditor.advisoryComments.test.tsx`.
- **Output deliverables**: schema split (`flaggedClause`/`assessment`) · #5 Review Summary Memo (assemble + persist to
  `sprk_analysisoutput`) · #6 memo toolbar dropdown (docx download + email via `EmailComposer`) · #7 Word-comment export
  fidelity + configurable comment author.

### Out of Scope
- **PDF ingest** (#1) → deferred to a `compose-r5` platform project. r1 builds/validates on **DOCX**.
- **Per-type knowledge packs** (lease/employment/asset-purchase grounding) → separate per-type sibling projects. Only
  the NDA pack (exemplar) is exercised here.
- **The Analysis hub widget + Create Agreement Analysis wizard + `sprk_analysis` spine + session binding** → owned by
  `analysis-hub-r1` (consumed here; see Coordination doc).
- **The autonomous / no-human (email-intake) review path** → future email sibling. Classifier is architected not to
  *preclude* headless invocation, but r1 builds only human-present paths.
- **Tabular doc×question review grid** → deferred (hub §11.7).

### Affected Areas
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — DEF-01 placement fix; configurable
  comment author (`:2146`); export-string composition for #7.
- `src/client/shared/Spaarke.Compose.Components/src/**` — `ComposeCommentGutter` (multi-select #3, reverse-highlight
  #2), `ComposeCommentThread.types.ts` export mapping (`:256-262`, `:89` scope), `ndaClauseLocation.ts`→`clauseLocation.ts` rename.
- `src/solutions/SpaarkeAi/src/components/conversation/**` — `ConversationPane` confirmation formatting (#4), batch
  dispatch loop (#3), classifier orientation + confirmation UX, `activeWorkType` handling.
- `src/server/api/Sprk.Bff.Api/Services/Ai/**` — generalized `agreement-review` Action + output-schema split;
  Reasoning-tier classifier node (reuse Layer-1 contract); sub-domain registry data.
- `src/server/api/Sprk.Bff.Api/Services/Compose/**` — memo docx generation via `ComposeDocumentRenderer` /
  `ComposeShadowPatchEngine` (no `ApplyComment` change).
- `infra/dataverse/actions/*.action.json` + `infra/dataverse/outputschemas/*.schema.json` — generalized Action +
  schema split; `sprk_analysisoutput` memo persistence.
- Eval: `tests/integration/contract/Eval/**` (golden-utterance / output-schema coverage for the schema + prompt changes).

## Requirements

### Functional Requirements

1. **FR-01 — Generalize the Advisory Review trigger (nda→agreements)** — Acceptance: the Advisory Review runs for a
   non-NDA agreement (employment/lease) and is not gated to an NDA `consumerType`; a code audit shows no NDA-only branch
   on the trigger path.
2. **FR-02 — Rename `ndaClauseLocation.ts` → `clauseLocation.ts`** (+ `NdaReviewSummaryPanel` as appropriate) —
   Acceptance: no `ndaClauseLocation`-named symbol remains; imports in `ComposeCommentGutter.tsx`/`ComposeEditor.tsx`/
   tests resolve; pure rename (behavior unchanged; all touched tests pass).
3. **FR-03 — Consume WS-4 reference layer** — Acceptance: review-note anchoring + citations use
   `ComposeDocxProjection.ParaIdMap[].ComputedNumber` and `CitationResolver`; the #2 join key is a stable computed
   clause number.
4. **FR-04 — DEF-01 advisory-comment placement fix** — Acceptance: a target matching >1 location (or below the
   confidence bar) is reported `ambiguous`/`not_found`, never silently placed; `ComposeEditor.advisoryComments.test.tsx`
   is re-enabled **with the original assertion** (`placed=1`) and passes.
5. **FR-05 — Split the Action output schema** into discrete `flaggedClause` / `assessment` (+ existing `standardRef`) —
   Acceptance: the Action emits the discrete fields; neither memo (#5) nor export (#7) string-parses markers; the
   golden-utterance / output-schema eval suite is updated and green. **Foundational — sequence before FR-12/FR-14.**
6. **FR-06 — One general `agreement-review` Action + sub-domain registry** — Acceptance: a single type-agnostic Action
   (method prompt: role + advisory grounding rules + "compare against retrieved standard") runs the review; NDA's
   B1–B16 taxonomy/rubric live in the NDA **knowledge pack**, not the prompt; a data-driven registry
   (`{subDomain, displayName, knowledgePackRef, classificationCue}`) has `nda` + `general` entries; adding a stub entry
   routes with no code change.
7. **FR-07 — Document classifier + orientation** — Acceptance: on the interactive path, a dropped file is classified on
   the **Reasoning tier** (work-type = agreement? + sub-domain) reusing the Layer-1 node contract (typed enum +
   confidence); on success the session **orients** (`activeWorkType='agreement-analysis'`, knowledge pack bound, tool
   palette scoped via `getToolsForSurface`, follow-on discussion focused). Not `gpt-4o-mini`.
8. **FR-08 — Confirmation gate (disambiguation)** — Acceptance: below **≥0.85** confidence → a user confirmation
   (proposed type + pick-another) blocks the run until confirmed; a composite doc offers a **choice of lens**
   (e.g. "employment · just the NDA · both"), and **"both" binds multiple packs** (1-to-many routing). No silent
   wrong-grounding run.
9. **FR-09 — Two entry modes** — Acceptance: **explicit** — a wizard-supplied `subDomain` binds that pack
   deterministically (no classifier guess; optional mismatch sanity-check); **interactive** — the classifier path
   (FR-07/08) drives it. Both set `activeWorkType`.
10. **FR-10 — #2 Bidirectional highlight** — Acceptance: selecting a Review Summary row highlights **both** the document
    location **and** the matching gutter Review Note (join key = computed clause number).
11. **FR-11 — #3 Multi-select Review Notes + batch AI action** — Acceptance: a per-note checkbox + a sub-toolbar
    AI-action dropdown (work-type-scoped); running executes **per selected note, sequentially** (ADR-016) with a
    **progress bar**; each note's outcome surfaces in the Assistant exactly as an individual run.
12. **FR-12 — #4 Separated Assistant confirmations** — Acceptance: each "What I changed" confirmation shows a **bold
    location** header + clear inter-entry separation; a batch reads as distinct entries.
13. **FR-13 — #5 Review Summary Memo** — Acceptance: a generated memo lists each changed section with
    {location, before(original), after(final change-disposition), why, golden-ref}; persists to `sprk_analysisoutput`
    (structured fields + JSON body for the section array); no new entity; before/after derived from final dispositions
    (no per-accept event capture).
14. **FR-14 — #6 Memo toolbar dropdown** — Acceptance: a toolbar control offers **Generate memo** (downloadable `.docx`
    via `ComposeDocumentRenderer`) and **Email memo** (opens `EmailComposer` with body + subject prefilled).
15. **FR-15 — #7 Word-comment export fidelity + configurable author** — Acceptance: a saved-and-opened-in-Word agreement
    shows each comment as configurable **Author** · **"Flagged clause"** (not "Grounded fact") · **"Assessment says: …"**
    · **"Standard: …"** (citation, ideally full clause text) — mirroring the gutter; server `ApplyComment` unchanged.

### Non-Functional Requirements
- **NFR-01 — BFF publish ≤60 MB** compressed (per BFF-touching task; no new NuGet expected; report absolute + diff).
- **NFR-02 — ADR-016 rate limits** — batch (#3) runs **sequentially**; classifier + review honor budgets/rate limits.
- **NFR-03 — ADR-021 Fluent v9** — all new UI (checkboxes, sub-toolbar, memo dropdown, confirmation UX) uses semantic
  tokens + dark mode.
- **NFR-04 — Classifier accuracy-first** — Reasoning tier; near-certain threshold **≥0.85 baseline as configuration**
  (tunable via UAT, not a hardcoded constant); biased toward confirming over mis-running.
- **NFR-05 — Headless-capable architecture** — the classifier + review must not *require* a UI confirmation to
  function (so the future email path is not precluded), even though r1 builds only human-present paths.
- **NFR-06 — Eval coverage (ADR-039)** — any Action output-schema/prompt change (FR-05/FR-06) is covered by the
  golden-utterance suite; dispatch regressions block merge.

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution; advisory tier — amended in nda-r1, inherited here; output-schema eval obligation)
- **ADR-016** (budgets/rate-limits — sequential batch; tier resolution)
- **ADR-021** (Fluent v9 semantic tokens + dark mode)
- **ADR-040** (ledger — change dispositions the memo reads), **ADR-041** (confirmation/OutcomeCard)
- **ADR-049** (Compose shadow document), **ADR-033/037** (doc-stream SSE / composite streaming), **ADR-030** (PaneEventBus)
- **ADR-007** (SpeFileStore), **ADR-013** (AI facade — CRUD uses `Services/Ai/PublicContracts/`), **ADR-028** (auth)
- **ADR-038** (testing strategy — KEEP categories; re-enable, don't weaken, the DEF-01 test)

### MUST Rules
- ✅ MUST route CRUD→AI through `Services/Ai/PublicContracts/` facade; MUST NOT inject AI-internal types into CRUD (ADR-013).
- ✅ MUST run batch actions sequentially (ADR-016); MUST NOT parallel-burst.
- ✅ MUST re-enable the DEF-01 test with its original assertion (ADR-038); MUST NOT weaken it to pass.
- ✅ MUST cover the schema/prompt change with golden-utterance evals (ADR-039).
- ✅ MUST keep classifier on the Reasoning tier (NFR-04); MUST NOT use `gpt-4o-mini` for sub-domain routing.
- ✅ MUST own the sub-domain registry as data (Action/Binding), extensible with zero code (§11).
- ❌ MUST NOT build per-type Actions or per-type UX branches (one general capability; grounding varies only).
- ❌ MUST NOT build the wizard/spine/session binding (hub-owned) or the autonomous email path.

### Existing Patterns
- Per-note dispatch: `ComposeCommentGutter.noteTools`/`onRunNoteTool` → `ConversationPane.dispatchComposeAction` →
  `makeComposeEditControlsMessage` / `COMPOSE_EDIT_CONFIRMATION`.
- Classifier contract: `Services/Ai/Insights/Playbooks/layer1-classification.node.json` (typed enum + confidence) — reuse the shape, change the tier.
- Memo engines: `ComposeDocumentRenderer` / `ComposeShadowPatchEngine` (nda-r1 Summary-Page); email: `EmailComposer`.
- Export seam: `composeSessionCommentThreadsToAnchoredComments` (`ComposeCommentThread.types.ts:256-262`; lift never-export scope `:89`).
- Tool scoping: `getToolsForSurface(surface, activeWorkType)` (Contextual AI Tool Library).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>          <!-- generalized agreement-review Action + output-schema split; Reasoning-tier classifier node; memo docx-gen + sprk_analysisoutput persistence -->
  <spaarkeai>Y</spaarkeai> <!-- Compose review surface: multi-select gutter, sub-toolbar, bidirectional highlight, confirmation formatting, classifier orientation + confirmation UX, memo toolbar dropdown, comment-export mirror, clause-location rename -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**BFF=Y** → Placement Justification per new server surface; ≤60 MB publish-size check per BFF task; no new NuGet
expected (reuses AI/Compose/OpenXML stack). Most work is client-side; the BFF surface is the generalized Action + schema
split (data/prompt), the Reasoning-tier classifier node, and memo docx-gen + `sprk_analysisoutput` persistence.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Document classifier + orientation + sub-domain registry | Insights Layer-1 node (enum+confidence) pattern; `nda-review` inline scope-guard (declines only); grounding/RAG binding; `activeWorkType`/`getToolsForSurface` | **Extend** — reuse Layer-1 contract on Reasoning tier; add data-driven registry + orientation wiring | "Review this document" can't identify the agreement or its type → no orientation, no type-specific knowledge; the type-agnostic promise + interactive path fail |
| One general `agreement-review` Action | `nda-review.action.json` (NDA-specific prompt/taxonomy) | **Extend/generalize** — type-agnostic method prompt; taxonomy→pack | Staying NDA-specific means per-type Action forks (violates "one capability"); or no non-NDA review at all |
| Confirmation-gate (disambiguation) UX | `ConversationPane` chips/confirmations; OutcomeCard (ADR-041) | **Extend** — a confirm affordance on uncertainty/multiplicity | Composite/uncertain docs silently run the wrong lens — wasted, inaccurate reviews the user must redo |
| Multi-select selection model + sub-toolbar (#3) | `ComposeCommentGutter` per-note tools | **Extend** the gutter — selection state + one sub-toolbar; loop shipped single-note dispatch | Reviewer runs each note one-by-one — the batch-a-review-pass ask fails |
| Summary↔note reverse-highlight (#2) | `NdaReviewSummaryPanel`, `ComposeCommentGutter`, anchor/highlight resolution | **Extend** — reverse scroll+highlight on the existing join key | Triage stays one-directional |
| Review Summary Memo (assembly + persistence) (#5) | `sprk_analysisoutput` (exists), Action output schema | **Extend/compose** — assemble from dispositions; persist to existing entity | No exportable deliverable of what changed + why |
| Memo toolbar dropdown (#6) | Compose toolbar, `ComposeDocumentRenderer`, `EmailComposer` | **Extend** — one dropdown wiring shipped renderers + EmailComposer | Memo has no generate/email affordance |
| Action output-schema split (`flaggedClause`/`assessment`) (#5/#7) | `nda-review.schema.json` (`explanation`, `standardRef`) | This IS an extension of the Action data (two output fields) | Both consumers string-parse one prose blob — brittle, duplicated |
| Comment-export mirror (#7) | `composeSessionCommentThreadsToAnchoredComments`, display-only relabel `ComposeCommentGutter.tsx:343-347` | **Fix/extend** the export mapping — relabel + append `standardRef` | Saved-in-Word comments stay raw prose + hardcoded author |
| Configurable comment author (#7a) | Hardcoded literal `ComposeEditor.tsx:2146` | **Extend** — one prop/config | Author stuck as "AI Advisory Review" |

*(No net-new services or Dataverse entities. Memo reuses `sprk_analysisoutput`; batch reuses shipped dispatch; the
schema split + registry are data/prompt. The one Dataverse column need — `sprk_subdomain` — is specified to and owned
by `analysis-hub-r1`, see Coordination A2.)*

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-016** (rate limits) | batch could burst concurrent calls | #3 runs an action per selected note | **C — comply** | Sequential-with-progress (Decision #2); progress covers the wait; no ADR change |
| **ADR-039** (output = schema-validated; grounded execution) | changing an Action's output schema; classifier as a new AI call | FR-05 schema split; FR-07 classifier | **C — comply** | Update golden-utterance evals alongside (NFR-06); the classifier is prompt-controlled + schema-validated (typed enum + confidence), fully inside invariant (a) |
| **§10 BFF Hygiene** | adding to `Sprk.Bff.Api` | generalized Action, classifier node, memo docx-gen + persistence | comply | Placement Justification (reuse Compose renderers + `sprk_analysisoutput` + Layer-1 contract); publish-size check; no new NuGet |

> The advisory-tier grounding **amendment** was authored in nda-r1 and is **inherited** — no new amendment anticipated.
> All other listed ADRs apply without exception.

## Success Criteria
1. [ ] Advisory Review runs on a non-NDA agreement with correct clause-location labels — Verify: manual + integration test (FR-01/02/03).
2. [ ] DEF-01 test re-enabled with original assertion and green — Verify: `ComposeEditor.advisoryComments.test.tsx` (FR-04).
3. [ ] Action emits `flaggedClause`/`assessment`; evals green — Verify: schema validation + golden-utterance suite (FR-05, NFR-06).
4. [ ] One general Action + registry (`nda`+`general`); stub entry routes with no code — Verify: registry test (FR-06).
5. [ ] Interactive classifier detects agreement + type on Reasoning tier and orients — Verify: classifier integration test (FR-07).
6. [ ] Below ≥0.85 → confirmation blocks run; composite → choice-of-lens incl. "both"=multi-pack — Verify: confirmation UX tests (FR-08).
7. [ ] Explicit wizard `subDomain` binds deterministically — Verify: hand-off contract test (FR-09) *(needs hub A1/A3)*.
8. [ ] #2/#3/#4 behaviors per acceptance — Verify: Compose UI tests (FR-10/11/12).
9. [ ] Memo assembles {location,before,after,why,golden-ref}, persists to `sprk_analysisoutput` — Verify: memo test (FR-13).
10. [ ] Memo dropdown → downloadable `.docx` + `EmailComposer` prefilled — Verify: UI test (FR-14).
11. [ ] Word comments mirror gutter (author/Flagged clause/Assessment/Standard) — Verify: save→open-in-Word manual + export-mapping test (FR-15).
12. [ ] BFF publish ≤60 MB — Verify: `dotnet publish` size check per BFF task (NFR-01).

## Dependencies

### Prerequisites
- nda-r1 shipped surface (Compose review, NDA-REVIEW Action, advisory tier, model-tier resolver, RAG) — present.
- R4.5 WS-3 numbering + WS-4 `CitationResolver`/`ComputedNumber` — deployed to dev 2026-07-28.

### External / Cross-Project
- **`analysis-hub-r1`** (near-deploy) — the wizard (explicit entry), `sprk_analysis` spine + **`sprk_subdomain`**
  column, `activeWorkType`/`subDomain` launch envelope, session↔Analysis binding. **Full contract + 3 time-sensitive
  asks (A1 wizard type-picker, A2 `sprk_subdomain` column, A3 launch envelope) in
  [`notes/COORDINATION-with-analysis-hub-r1.md`](notes/COORDINATION-with-analysis-hub-r1.md).** agreements-r1 ships
  value on the interactive/chat path regardless of hub timing; only the explicit-wizard path + durable classifier-
  started Analysis depend on the hub.
- **`compose-r5`** (future) — PDF ingest; r1 validates on DOCX and inherits PDF later.
- A Reasoning-class Azure OpenAI deployment (provisioned in nda-r1) for the classifier + review.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Coordination | Is this coordinated with analysis-hub-r1? | Yes — siblings, decoupled; two shared contracts (storage model + work-type model); no build-order dependency | Coordination doc authored; wizard/spine consumed, not built |
| Type support | Support full range of agreement types with type-specific knowledge? | Yes — via the knowledge sub-domain axis; UX/tools/memo stay general | One general capability; grounding varies only |
| Per-type packs | Build per-type grounding here? | **Option (a)** — no; separate per-type sibling projects (nda-r1 pattern) author packs | r1 builds the machine + registry; packs plug in later |
| Classifier scope | Is the classifier in scope, and how central? | **First-class, co-equal** — powers document-driven orientation ("review this" → identify agreement + type → orient) | Classifier is a core deliverable, not a fallback |
| Classifier quality | Cheap classifier or robust? | **Accuracy-first** — Reasoning tier, reuse Layer-1 *contract* not its cheap model | A wrong route wastes a full analysis; robustness > classifier cost |
| Confirmation | When to confirm vs auto-run? | Confirm on **uncertainty** *or* **multiplicity**; hint (wizard) authoritative; composite → choice-of-lens, "both"=multi-pack | 1-to-many routing; ≥0.85 baseline gate |
| Threshold | Set the near-certain value? | **≥0.85 baseline**, configurable, tune via UAT | Config not constant (NFR-04) |
| Action model | One general Action or per-type? | **One general Action** (method); type-specific **knowledge packs** = the value; general pack = fallback | NDA taxonomy → pack, not prompt |
| Wizard | Does this project build the Agreement Analysis wizard? | **No** — hub-owned (option A); coordinate | Wizard = hub; machine = here |
| Autonomous path | Is email/no-human intake in scope? | **No** — illustrative only; out of scope for both | Build human-present paths; keep headless-capable (NFR-05) |
| Schema split | Split `explanation` or parse client-side? | **Split once** at the Action layer (`flaggedClause`/`assessment`) | Foundational task; eval obligation |

## Assumptions

- **Batch max-selection (#3)**: assuming a **soft cap ~25** with a confirm above it (sequential runs can be slow),
  tunable like the classifier threshold — affects the #3 sub-toolbar. *(Owner did not confirm; see Unresolved.)*
- **`Standard:` depth (#7/#5)**: assuming **full clause text when the grounding layer supplies it, else the citation
  label** — affects export/memo assembly + grounding retrieval. *(Owner note "ideally full clause text".)*
- **Explicit-path sanity-check**: assuming the classifier **optionally** validates a wizard-supplied type and only
  *warns* on a mismatch (the user's explicit choice wins) — affects FR-09 wiring.
- **`sprk_subdomain` lands with the hub schema** (Coordination A2); if not, agreements-r1 persists sub-domain via the
  existing `sprk_analysis` surface or defers persistence — affects FR-06/FR-13 persistence wiring.

## Unresolved Questions

- [ ] **Batch max-selection cap** (soft ~25 vs none) — Blocks: final #3 sub-toolbar behavior (assumption in place).
- [ ] **`Standard:` reference depth** (citation vs full clause text) + whether grounding reliably supplies full text —
  Blocks: nothing (assumption in place); confirm during the RAG/export task.
- [ ] **Hub decisions A1/A2/A4** (wizard type-picker timing · `sprk_subdomain` column · classifier-path session
  binding callability) — Blocks: the explicit-wizard path + durable classifier-started Analysis (not the interactive
  path). Track via the Coordination doc.
- [ ] **Rename blast radius** — confirm no consumer outside Compose imports `ndaClauseLocation`/`NdaReviewSummaryPanel`
  by name (grep at task time) — Blocks: FR-02 (pure-rename risk only).

---
*AI-optimized specification. Original design: `design.md`. Coordination: `notes/COORDINATION-with-analysis-hub-r1.md`.*
