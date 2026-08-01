# Agreement Analysis — Review Depth & Output Deliverables (agreements-r1) — Design

> **Project**: `ai-advanced-capabilities-agreements-r1` · **Round**: r1 (the "nda-r2" successor)
> **Date**: 2026-07-29 · **Owner**: ralph.schroeder
> **Status**: 🟢 Design — decisions locked; ready for `/design-to-spec`
> **Spawned from**: `ai-advanced-capabilities-nda-r1` (shipped NDA advisory-review vertical). This round is the
> **work-type generalization + review-depth + output successor**: it makes the review machine agreement-type-agnostic
> and adds batch actions, bidirectional highlighting, cleaner confirmations, and a first-class **Review Summary Memo**.
> **Sibling (decoupled)**: `ai-advanced-capabilities-analysis-hub-r1` — the platform (spine + sessions + hub widget).
> **Source material**: `design-discussion.md` (7 enhancements, decisions §4) + `notes/HANDOFF-from-compose-fidelity-r4.5.md`
> (DEF-01 + nda→agreements generalization) + `notes/word-comment-export-gap.md` (#7 root cause).

---

## 0. North Star (owner, 2026-07-28) — read first

Turn the NDA advisory vertical into a **general Agreement Analysis review capability** that works for **any** agreement
type, and make the review *deeper to work with* and *exportable as a business deliverable*. Two intents:

1. **Generalize** — the review surface, tools, clause-location, and memo are **type-agnostic**. Agreement-type-specific
   knowledge (NDA · real-estate lease · employment · asset-purchase · …) varies **grounding only** — it is authored in
   **separate per-type sibling projects** (the nda-r1 pattern), not here.
2. **Deepen + deliver** — multi-select batch AI actions, bidirectional summary↔note↔document highlighting, cleaner
   per-section Assistant confirmations, and a **Review Summary Memo** (generate-as-docx / email), plus **Word-comment
   export fidelity** so a saved-and-opened agreement mirrors the on-screen review structure.

**Scope discipline**: 6 of 7 enhancements refine shipped nda-r1 Compose primitives; the 7th (PDF ingest) is deferred
to a `compose-r5` platform project. Two R4.5 handoff items (a clause-anchoring correctness bug + the nda→agreements
rename) fold in as one fidelity workstream. Build almost nothing net-new (Lens 4).

---

## Relationship to `analysis-hub-r1` (siblings, deliberately decoupled)

Both projects abstract NDA → Agreement Analysis, at **different layers**, and neither blocks the other:

| | **analysis-hub-r1** (platform) | **agreements-r1** (this — review depth + output) |
|---|---|---|
| Owns | `sprk_analysis` spine (`sprk_worktype`, regarding field-set, subgrids), session persistence/binding, hub widget + creation wizard, entry matrix, retiring the old `AnalysisWorkspace` code page | On-surface review execution: batch actions, bidirectional highlight, cleaner confirmations, Review Summary Memo, Word-export fidelity, DEF-01 fix, nda→agreements generalization |
| Layer | Entry / spine / session / tool-scoping | Review execution / clause-location / output deliverables |

**Two shared contracts both honor** (locked, no build-order dependency):
- **Storage model** (hub §11.7, "shared with agreements-r1"): full chat transcript → **Cosmos**; business anchor +
  structured deliverables (the memo, outputs) → **Dataverse** (`sprk_analysis` / `sprk_analysisoutput`). The memo (#5)
  lands in `sprk_analysisoutput`, which exists today; the hub formalizes that store but does **not** gate this project.
- **Three-level work-type model** + Contextual AI Tool Library (`workTypes` × `surfaces`, `getToolsForSurface`) —
  shipped in nda-r1, consumed by both.

**⚡ HUB BUILT-STATE UPDATE (2026-07-31, verified — supersedes the "near-deploy" framing above/below).** Hub-r1 has
**shipped tasks 001–070 + a post-plan Phase 1, all merged to origin/master** (verified: zero branch-only commits): the
spine (`sprk_worktype`, regarding field-set, subgrids), session↔Analysis FK, **`POST /api/ai/analysis/fork` +
`/promote`** (both callable from a non-wizard/classifier trigger), archive durability, hub widget + tabbed Quick Start,
**Create Analysis wizard as a modal** (steps: Associate To → Add file(s) → Analysis Details), `activeWorkType`
end-to-end tool-palette scoping, entry matrix 2a–2d, `openSpaarkeAi` analysis params + ribbon launcher JS, legacy
retirement, and Phase 1 (**"NDA Analysis" card (`nda-analysis`) → wizard → open doc in EDITABLE Compose** seeding
`activeWorkType='agreement-analysis'`). Remaining hub work: 071 (user-gated env: ribbon-button XML + web-resource
delete) · 072 e2e · 090 wrap-up. **Hub Phases 2 (bind session + auto-dispatch review on wizard finish) and 3 (durable
recall of review results on reopen) are NOT built and are routed to agreements-r1** — see the scope additions in
Lens 3(e)/(f). Authoritative detail + corrections: **[`notes/HUB-R1-REVIEW-2026-07-30.md`](notes/HUB-R1-REVIEW-2026-07-30.md)**
(7-agent verified) and the hub's reverse coordination doc
(`projects/ai-advanced-capabilities-analysis-hub-r1/notes/COORDINATION-hub-r1-TO-agreements-r1.md`).

**Independence (revised)**: the substrate we depend on (fork/promote, FK binding, `activeWorkType`, wizard, editable-
Compose open) is **built and in master** — no degraded fallback needed. What we now *owe the composition*: the review
machine's auto-run + durable-recall legs (hub Phases 2/3) are ours, making agreements-r1 the critical path of the
owner's 3-phase must-have.

**Where "pick Agreement Analysis from the hub → classify + call agreement knowledge" lives** (the owner's critical
flow): the hub **card sets the work type** (level 1 → `sprk_worktype=agreement-analysis`; surface + tool palette) — that
launch is hub-owned and decoupled. The subsequent **classify-the-sub-domain → bind-its-grounding** step (level 1 →
level 2) is **this project's machine** (Lens 3d), triggered whenever an analysis runs under that work type — from the
hub *or* today's nda-r1-style entry. So the flow spans both projects at the seam, but the routing intelligence is
agreements-r1's, and it does not depend on the hub existing to function.

---

## Lens 1 — Use Case Definition

**Actor**: a legal reviewer (lawyer or trained non-lawyer) working an agreement inside the SpaarkeAi/Compose three-pane.

**Primary flow** (any agreement type):
1. Load an agreement (**DOCX** for r1 — see Lens 4 PDF note) via one of two co-equal entry modes: **explicit** — the
   user picks the agreement type in the hub's Create Agreement Analysis wizard (deterministic); or **interactive** — a
   file dropped into the Assistant chat with "review this," which the **classifier** orients (Lens 3d).
2. Run the **AI Advisory Review** — general, not NDA-gated. The session runs under work-type `agreement-analysis` with
   the agreement type's **knowledge pack bound** (explicit from the wizard, or classifier-inferred + confirmed when
   uncertain/composite). Type-specific knowledge is where the value is; a general pack is the fallback. Findings render
   as summary rows + gutter Review Notes (shipped nda-r1 primitives).
3. **Triage faster** — selecting a summary row highlights **both** the document location **and** its matching Review
   Note (#2); Review Notes are **multi-selectable** with a **batch AI action** run per-selected note, with a progress
   bar and per-section Assistant outcomes exactly as an individual run produces today (#3); those Assistant
   confirmations are **clearly separated + location-labelled** (#4).
4. **Deliver** — generate a **Review Summary Memo** listing each changed section with {location, before, after, why,
   golden-reference} (#5); export it as a **.docx** or **email** via a toolbar dropdown (#6); and when the agreement is
   saved and opened in **Word**, each comment **mirrors the on-screen gutter** (Author / "Flagged clause" / "Assessment
   says" / "Standard") rather than raw prose (#7).

**Correctness invariant** (folded from R4.5 handoff): an advisory Review Note must anchor to the **right** clause. A
should-be-ambiguous target must be reported, not silently placed on the wrong clause (DEF-01).

**Type-agnosticism is the deliverable**: the flow above is identical for a lease, an employment agreement, or an asset
purchase agreement. Only the *knowledge pack* differs — and the **classifier + orientation + routing that selects it is
in scope** (Lens 3d); the per-type knowledge *packs* are out of scope (separate sibling projects). r1 proves the
machine is type-agnostic (removes NDA gating, renames NDA-named-but-general code, builds the classifier + router + one
general Action, validated on NDA + a general fallback).

**Out of scope**: PDF ingest (#1 → `compose-r5`); per-type knowledge packs (separate sibling projects); the hub
spine/session/**wizard** work (analysis-hub-r1 — see [`notes/COORDINATION-with-analysis-hub-r1.md`](../notes/COORDINATION-with-analysis-hub-r1.md));
the **autonomous / no-human (email-intake) path** (future email sibling); a tabular doc×question review grid (hub §11.7, deferred).

---

## Lens 2 — Surface / UX (Compose review surface — the UX-depth bucket)

The single surface stays the SpaarkeAi/Compose three-pane. Three refinements + one correctness fix:

**#2 — Bidirectional highlight (summary row → document section AND its Review Note).**
Today a summary row highlights the document section only. Add the reverse link: selecting a row also scrolls+highlights
the matching gutter Review Note. Join key = the finding's section/anchor id (now stably a **computed clause number**
via WS-4, see Lens 3). *Reuse*: `NdaReviewSummaryPanel` (rows already resolve `docPosition`) + `ComposeCommentGutter`
+ existing anchor/highlight resolution. *Size*: small–moderate, client-only.

**#3 — Multi-select Review Notes + batch AI action.**
Checkbox at the upper-left of each Review Note; select one / many / all. When ≥1 selected, a sub-toolbar exposes the
AI-action dropdown (work-type-scoped from the Tool Library); running it executes the action **per selected note,
sequentially** (Decision #2 — respects ADR-016 rate limits), with a **progress bar**. Each note's outcome surfaces in
the Assistant exactly as an individual run does. *Reuse*: the per-note dispatch is shipped
(`ComposeCommentGutter.noteTools`/`onRunNoteTool` → `ConversationPane.dispatchComposeAction` →
`makeComposeEditControlsMessage`); batch = a selection model + sub-toolbar + a loop over the existing single-note
dispatch; progress reuses `NdaReviewProgressModal` / `AiProgressStepper`. *Size*: moderate–large, client-heavy.

**#4 — Clearer, separated Assistant confirmations.**
Each "What I changed" confirmation gets a **bold location indicator** in its header + more whitespace between entries
(pairs naturally with #3, which emits many at once). *Reuse*: `makeComposeEditControlsMessage` /
`COMPOSE_EDIT_CONFIRMATION` rendering in `ConversationPane`. *Size*: small, client-only.

**DEF-01 (correctness, folded from R4.5) — advisory-comment placement.**
`placeAdvisoryComments` materializes a comment for a target that should have surfaced as ambiguous (`placed=2` vs
expected `1`). Fix the match/ambiguity precision: a target matching >1 location (or below a confidence bar) must be
reported `not_found`/`ambiguous`, not placed. Anchor by **computed clause number** (WS-4) where the model supplies a
section reference — deterministic, likely eliminating the ambiguity class. *Exit criterion*: re-enable
`ComposeEditor.advisoryComments.test.tsx` **without weakening the assertion**. *Location*: `ComposeEditor.tsx:~2519`.

**ADR-021**: all new UI (checkboxes, sub-toolbar, memo dropdown) uses Fluent v9 semantic tokens + dark mode.

---

## Lens 3 — AI Capabilities Required

**(a) Generalize the review trigger + clause-location (nda→agreements).** *(folded from R4.5 handoff ITEM 2)*
- **Confirm the AI Advisory Review runs for all agreement types** — not gated to an NDA `consumerType`. This is the
  core generalization deliverable. The location-label logic is **already document-agnostic** (`deriveClauseLocationLabel`
  / `findGoverningHeading` / `computedNumberAt` have zero NDA branching); the work is confirming the *trigger* is
  general + removing NDA gating where present.
- **Rename for clarity** — `ndaClauseLocation.ts` → `clauseLocation.ts` (and `NdaReviewSummaryPanel` naming as
  appropriate), updating imports in `ComposeCommentGutter.tsx`, `ComposeEditor.tsx`, and tests. Pure rename, no logic
  change — removes the recurring "is this NDA-only?" confusion permanently.

**(b) Consume the WS-4 reference layer** *(shipped in R4.5, "not yet wired to a consumer" — agreements is the consumer)*.
Wire review-note anchoring + citations to `ComposeDocxProjection.ParaIdMap[].ComputedNumber` and `CitationResolver`
(citation string ↔ `paraId`, covering "Section 4.2", "4.2(b)(iii)", "Sections 4–7"). This is what makes DEF-01
fixable deterministically and gives #2 a stable join key.

**(c) Shared enabler — split the Action output schema** *(Decision: split once)*.
The memo (#5) and Word-comment export (#7) both need structured {Flagged clause / Assessment / Standard} data. Today
that lives as one `explanation` string both consumers would string-parse. **Split the Action output schema into discrete
`flaggedClause` / `assessment` fields** (`standardRef` already exists) so neither consumer parses markers. This is a
**JPS/playbook-layer change** (Action `.action.json` + `outputschemas/*.schema.json`) and is a **foundational,
early-sequenced task** — #5 and #7 both depend on it. It carries an **eval-case obligation** (per the consumer-wiring
guide + ADR-039 golden-utterance suite — any Action output-schema change must be covered).

**(d) Document-driven classification + orientation — the Assistant's core intelligence (in scope, first-class).**
The classifier turns a raw document + a vague instruction ("review this") into an **oriented, grounded
agreement-analysis session**. It performs **intent inference from the document itself**, at two levels, then orients
the whole surface:
1. **Is this an agreement?** (work-type detection) — distinguishes an agreement from a pleading, correspondence,
   invoice, memo. Decides whether the agreement-analysis machine activates.
2. **Which agreement type?** (sub-domain) — NDA · lease · employment · asset-purchase → selects the knowledge pack.
3. **Orient** — set `activeWorkType='agreement-analysis'` (scopes the Tool Library palette via `getToolsForSurface`),
   **bind the type's knowledge**, and **focus the follow-on actions + discussion** on that agreement type. This is the
   Assistant getting smart about *what the user handed it*, rather than waiting to be told.

**Two co-equal entry modes** (not primary/secondary — different entry contexts):
- **Explicit (wizard)** — the hub's Create Agreement Analysis wizard passes an **explicit, user-selected agreement
  type** (authoritative). Routing is **deterministic**; no classifier guess needed (it may optionally sanity-check for
  a mismatch). *(Wizard is `analysis-hub-r1`'s deliverable — see Coordination, below.)*
- **Interactive (Assistant chat-upload)** — a file dropped into the chat with no type given. The classifier infers
  work-type + sub-domain and orients. Human is present → it **confirms when helpful** (see confirmation gate).

**Value model — type-specific knowledge is the point; general is the fallback.** One **general agreement-review Action**
(type-agnostic method: role + advisory grounding rules + "compare against the retrieved standard"). The **value lives in
the per-type knowledge packs** — NDA's B1–B16 taxonomy/rubric/standards, a lease pack, an employment pack — which move
**into the pack**, not the prompt. A **general/broad pack is the graceful fallback** (unknown type, or user chooses
"general"), explicitly the lower-value path. The classifier routes to a **pack**, not to a different Action.

**Confirmation = intent-disambiguation** (fires on uncertainty *or* multiplicity), owner 2026-07-30:
- **Uncertainty** — below the near-certain bar on a single type → confirm the proposed type / let the user pick.
- **Multiplicity** — a composite/multi-type doc (e.g. employment terms + an NDA addendum) → offer a **choice of lens**:
  *"This looks like an employment agreement that also contains an NDA. Review it as: employment · just the NDA · both?"*
  **"Both" applies multiple knowledge packs** — routing is therefore **1-to-many** (a doc reviewed under >1 sub-domain).
- Rationale: confirmation avoids **investing in the wrong path** — cheaper than an inaccurate review the user must
  discover and redo.

**Classifier quality (owner 2026-07-29) — accuracy-first, not cheapest.** A misclassification spins up a full, slow,
expensive review against the **wrong grounding**, so robustness dwarfs classifier cost. We **reuse the Insights
**Layer-1 classification node** *contract*** (`layer1-classification.node.json`: typed enum + confidence) but run it on
the **Reasoning tier** — not the `gpt-4o-mini` its cheap-gates-expensive economics prescribes (inverted for us).
Ground truth: **no sub-domain classifier exists today** — `nda-review` only has an inline prompt SCOPE GUARD that
*declines* non-NDAs; this router is net-new.

- **Near-certain threshold = ≥0.85 (baseline, configurable)** — biased toward confirming over mis-running; tune
  per-sub-domain via UAT. **Per-type override mechanism already exists**: `sprk_agreementtype.sprk_confidencethreshold`
  (null → global baseline).
- **Bootstrapping** — r1 builds + validates the classifier + router with **NDA as the first registered pack** + the
  general fallback. Routing to lease/employment/asset-purchase **activates automatically** as each per-type sibling
  project **registers** its pack — no change to this machine.
- **Registration seam (CORRECTED 2026-07-31 per hub built state)** — the registry **IS the `sprk_agreementtype`
  Dataverse table** (hub-built; NOT Action/Binding data as earlier drafted). `subDomain` ≡ the row's **`sprk_key`** slug;
  the `sprk_analysis` lookup's logical name is **`sprk_agreementtype`** (OData `_sprk_agreementtype_value` — the doc'd
  `sprk_agreementtypeid` is wrong). Ownership: hub owns identity columns (`sprk_key`/`sprk_name`/`sprk_isfallback`/
  `sprk_isselectable`/`sprk_sortorder`); **agreements-r1 owns the behavior VALUES** (`sprk_knowledgepackref`,
  `sprk_classificationcue`, `sprk_confidencethreshold`) — filled via `update_record`, no schema work. New type = new
  row, zero code (§11 goal preserved). ⚠️ Current env state: **3/10 seed rows loaded; ALL `sprk_isselectable=false`**
  (picker landmine — owner must fix seeds/semantics); **zero code mirror exists** — agreements-r1 authors the TS type +
  seed/infra JSON. The wizard type-picker reads this table.

**(e) Durable-recall re-route — hub Part C.1, ACCEPTED scope (the review must survive reopen).**
Today `nda-review` results are ledgered durably (Cosmos, ADR-040) but with `informational` disposition — so
`compose-outputs` (compose-only filter) skips them, `/restore` omits Outputs, and the advisory gutter is a live-turn
client projection lost on reload; reopening re-derives only by **re-dispatching** (cost/latency/non-determinism —
unacceptable for legal work product). Fix (verified sound; **4 changes, not 1**): (1) Binding `sprk_disposition` →
`compose` (data); (2) **payload shape** — `flaggedSections[]` matches no materializer branch → extend the FR-04
refresh-durability materializer with a **findings branch** calling `placeAdvisoryComments` (keeps riskLevel/sectionRef/
standardRef; composes with the (c) schema split — do together); (3) **DEF-09 session routing** — dispatch must target
the document session (`sessionIdOverride`) or the output lands where ComposeWorkspace never looks; (4) **apply-leg
gating** for findings-only outputs (no spurious redline staging/Accept-Reject). Design-around risks: 128KB inline-payload
cap (truncation markers silently skipped — chunk or budget findings), highest-turn-only re-materialization (FR-29
annotations store is the second layer), supersede must not retract findings, `NdaReviewSummaryPanel` must also restore
from re-materialized state (today live-event-fed only). `DELETE /sessions` erases the ledger → the **memo (#5) in
`sprk_analysisoutput` is the only deletion-surviving store** (strengthens #5's rationale).

**(f) Wizard→review auto-run bridge — hub Phase 2 remainder, ACCEPTED scope.**
The wizard produces a **durable `sprk_document`** (SPE `sprk_graphitemid`/`driveid`) but creates **no chat session and
runs no review**; dispatch consumes **session-uploaded fileIds only** (hard error otherwise). agreements-r1 wires:
register the durable doc as session file context (bridge the impedance) → bind the session (fork/promote — both live,
non-wizard-callable; promote requires a documentId; its **silent-FK gap is fixed hub-side** — their Q2 closeout, we verify) →
auto-dispatch the review on wizard-finish → advisory comments render in Compose. This is the "Phase 2" leg of the
owner's 3-phase must-have; (e) is the "Phase 3" leg.

**Boundary (option a).** r1 builds the **machine + classifier + router + confirmation + the general Action + the
registry**; it does **not** author per-type knowledge *packs* — each agreement type gets its own sibling project that
authors + registers its pack. NDA is the shipped exemplar that validates the machine end to end.

**Out of scope (both projects): the autonomous / no-human path** (e.g. email intake). The classifier is architected not
to *preclude* headless invocation later, but r1 builds only the human-present **explicit** + **interactive** paths;
low-confidence therefore always resolves to a **user confirmation** (never a headless hold-for-triage decision).

**Coordination with `analysis-hub-r1`.** The wizard (explicit path), the `sprk_analysis` spine + `sprk_subdomain`
persistence, `activeWorkType`/`subDomain` launch envelope, and session↔Analysis binding are hub deliverables this
machine consumes. Full contract + time-sensitive asks: **[`notes/COORDINATION-with-analysis-hub-r1.md`](../notes/COORDINATION-with-analysis-hub-r1.md)**.

**No new reasoning capability** beyond nda-r1 — the advisory tier (ADR-039 amended in nda-r1), model-tier resolver, and
RAG grounding are all shipped. r1 reuses them.

---

## Lens 4 — Have vs. Gap

**Reuse inventory (§11 — build almost nothing net-new):**

| Need | Reuse (shipped) |
|---|---|
| Per-note AI action dispatch (#3 batch unit) | ✅ `ComposeCommentGutter.noteTools`/`onRunNoteTool` + `ConversationPane.dispatchComposeAction` |
| Per-section Assistant outcome (#3/#4) | ✅ `makeComposeEditControlsMessage` / `COMPOSE_EDIT_CONFIRMATION` (#4 refines its formatting) |
| Summary rows + doc highlight (#2) | ✅ `NdaReviewSummaryPanel` (+ `docPosition` resolution) |
| Review Notes gutter (#2/#3) | ✅ `ComposeCommentGutter` |
| Progress UI (#3) | ✅ `NdaReviewProgressModal` / `AiProgressStepper` |
| Clause-location / computed number (#2, DEF-01) | ✅ `clauseLocation.ts` (rename of `ndaClauseLocation.ts`) + WS-4 `ComputedNumber`/`CitationResolver` |
| Memo content (#5) | ✅ NDA-REVIEW output schema (location/before/why/golden-ref) — only after-text + the schema split are net-new |
| Memo docx export (#6) | ✅ `ComposeDocumentRenderer` / `ComposeShadowPatchEngine` (nda-r1 Summary-Page engines) |
| Memo email (#6) | ✅ `EmailComposer` form |
| Memo persistence (#5) | ✅ Dataverse `sprk_analysisoutput` (exists) — per the shared storage model |
| Word-comment export (#7) | ✅ server `ComposeShadowPatchEngine.ApplyComment` (no change) + export mapping seam `composeSessionCommentThreadsToAnchoredComments` |
| Batch action palette | ✅ Contextual AI Tool Library (`getToolsForSurface`, work-type-scoped) |

**Gaps (net-new / net-changed), grouped into two workstreams:**

**W-Fidelity** (generalization + correctness — folds the R4.5 handoff):
- Confirm review trigger is general + remove NDA gating (Lens 3a) · rename `ndaClauseLocation.ts`→`clauseLocation.ts` ·
  consume WS-4 `ComputedNumber`/`CitationResolver` (Lens 3b) · DEF-01 placement-ambiguity fix + test re-enable (Lens 2).

**W-Output** (deliverables — all downstream of the schema split):
- The **schema split** (`flaggedClause`/`assessment`, Lens 3c) — foundational, first · **memo assembly** from final
  change-disposition state (before=original, after=accepted/rejected result — Decision #4) + **persistence** to
  `sprk_analysisoutput` (Decision #3) · **memo toolbar dropdown** (generate-docx download + email via `EmailComposer`,
  #6) · **Word-comment export mirror** (#7: relabel + append `standardRef` when composing export `commentText`).

**W-Durable-Review** (hub Phases 2/3 remainder, accepted 2026-07-31 — Lens 3e/3f):
- The **compose-disposition re-route + findings materializer branch** (4-change set, Lens 3e) so reopen restores the
  review deterministically without re-dispatch · the **summary-panel restore** from re-materialized state · the
  **wizard→session file-id bridge + auto-run dispatch on wizard-finish** (Lens 3f; the promote **silent-FK fix is
  hub-side** — their Q2 closeout, we verify + regression-test) · the `sprk_agreementtype` **code mirror** (TS type +
  seed/infra JSON) + behavior-column values + remaining seed rows.

**UX-depth** (Lens 2): the multi-select selection model + sub-toolbar (#3) · the summary↔note reverse-highlight link
(#2) · the confirmation formatting (#4).

**PDF (#1) — GAP, deferred**: Compose is DOCX-native end to end; **no PDF path exists**. PDF ingest is a substantial
platform effort (extraction → paraId projection, non-round-trip-editable) → **`compose-r5`** (Decision #1). agreements-r1
builds/validates on DOCX and inherits PDF when compose-r5 lands. It IS a hard prerequisite for the real agreement use
case → sequence compose-r5 as a focused precursor, but not in this project.

---

## Lens 5 — Configuration

- **Comment author** — make the advisory-comment author **configurable** (fixes #7a: hardcoded literal `"AI Advisory
  Review"` at `ComposeEditor.tsx:2146`). Owner questioned whether that's the right name — one prop/config, not a literal.
- **Memo shape** (Decision #3) — reuse `sprk_analysisoutput` (child of `sprk_analysis`): structured field-level data +
  a **JSON body** for the section array. **No new entity.** Working copy may live in Cosmos alongside the session; the
  committed memo lands in Dataverse.
- **Memo before/after semantics** (Decision #4) — derived from the document's **final change-disposition state** at
  generation time (before = original text; after = accepted result / rejected outcome). **No per-accept event capture,
  no timestamp.**
- **Batch action set** (#3) — drawn from the shipped `getToolsForSurface` registry, **work-type-scoped** to
  `agreement-analysis`. No new tool authoring.
- **Sub-domain registry** (Lens 3d) — agreements-r1 owns the canonical `{ subDomain, displayName, knowledgePackRef,
  classificationCue }` list as **data** (Action/Binding, not code); the hub wizard's type-picker reads it. Initial
  entries: `nda` (exemplar) + `general` (fallback).
- **Near-certain threshold = ≥0.85** — baseline, **configuration not a hardcoded constant** so it's tunable via UAT
  without redeploy; may become per-sub-domain later.
- **`activeWorkType` wiring** — the launcher passes `agreement-analysis` (+ `subDomain`) so the tool palette scopes and
  the Assistant orients (hub coordination — Coordination doc A3).
- **Per-type knowledge packs — OUT (option a)** — NDA/lease/employment/asset-purchase grounding is authored in separate
  per-type sibling projects, not configured here. r1 leaves the knowledge-sub-domain seam open and type-agnostic.

---

## Lens 6 — Acceptance & Evaluation

**Type-agnosticism (core):**
- The Advisory Review runs on a **non-NDA agreement** (e.g. an employment agreement or lease) and produces summary rows
  + gutter notes with correct clause-location labels — proving the trigger + location logic are not NDA-gated.
- No `ndaClauseLocation`-named symbol remains; imports resolve to `clauseLocation.ts`; all touched tests pass unchanged
  in behavior (pure rename).

**Classifier + orientation (Lens 3d — document-driven intelligence):**
- **Explicit path** — a wizard-supplied agreement type binds that pack **deterministically** (no classifier guess);
  `activeWorkType='agreement-analysis'` scopes the tool palette.
- **Interactive path** — a file dropped into the Assistant chat with "review this" is **classified on the Reasoning
  tier** (not `gpt-4o-mini`): work-type detected as *agreement* + sub-domain, then the session **orients** (tool
  palette scoped, knowledge bound, follow-on discussion focused). Near-certain (≥0.85) NDA → auto-binds the NDA pack.
- **Confirmation on uncertainty** — below ≥0.85 → **user confirmation** (proposed type + pick-another); does **not**
  run until confirmed. **No silent wrong-grounding run.**
- **Confirmation on multiplicity** — a composite doc (employment + NDA addendum) offers **"employment · just the NDA ·
  both"**, and **"both" binds multiple packs** (routing is 1-to-many).
- **Value/fallback** — one general agreement-review Action; a matched type uses its rich pack; unknown/`general` routes
  to the broad fallback pack (lower value, still runs).
- **Registry** — data-driven; adding a stub second sub-domain entry routes to it with **no code change** (test the seam).

**UX depth:**
- **#2** — selecting a summary row highlights BOTH the document section AND its matching gutter Review Note.
- **#3** — selecting N notes + running a batch action executes sequentially with a visible progress bar; each note's
  Assistant outcome matches an individually-run note; ADR-016 rate limits respected (no parallel burst).
- **#4** — each Assistant confirmation shows a **bold location** header + clear separation; a batch of confirmations is
  readable as distinct entries.

**Correctness (DEF-01):**
- `ComposeEditor.advisoryComments.test.tsx` asserts the **original strict behavior** (`placed=1` for the ambiguous case)
  and passes. *(Verified 2026-07-31: the suite has no skip markers — the assertion was **weakened**, not skipped; the
  task is diff-against-original + restore, not un-skip.)* A target matching >1 location is reported
  `ambiguous`/`not_found`, never silently placed.

**Durable recall (Lens 3e/3f — the reopen guarantee):**
- Reopening an analysis (from the grid entry path or `openSpaarkeAi analysisId`) **restores the review deterministically
  with ZERO LLM calls**: gutter Review Notes re-materialize from the compose-disposition ledger (findings branch) AND
  the Review Summary panel repopulates — not just comments.
- Wizard-finish (with the durable `sprk_document`) **auto-runs the review**: file registered as session context, session
  bound (fork/promote), review dispatched, comments render in Compose — no manual re-upload.
- A promoted classifier-path session is **durably bound** (visible in `by-analysis` lookups) — the silent-FK gap does
  not ship as-is.
- An over-cap (>128KB) findings payload does **not** silently vanish on reopen (chunked/budgeted or explicitly surfaced).

**Output deliverables:**
- **Schema split** — the Action emits discrete `flaggedClause` / `assessment` (+ existing `standardRef`); the
  golden-utterance / output-schema eval suite is updated and green (ADR-039 obligation).
- **#5 memo** — a generated memo lists each changed section with {location, before(original), after(final disposition),
  why, golden-ref}; it persists to `sprk_analysisoutput` (structured + JSON body).
- **#6 export** — the toolbar dropdown produces a downloadable `.docx` AND opens `EmailComposer` with the memo body +
  subject prefilled.
- **#7 Word fidelity** — a saved-and-opened-in-Word agreement shows each comment as: configurable **Author** · label
  **"Flagged clause"** (not "Grounded fact") · **"Assessment says: …"** · **"Standard: …"** (citation, ideally full
  clause text) — mirroring the on-screen gutter. Server `ApplyComment` unchanged (renders whatever `commentText`/`Author`
  it's given).

**Eval obligation**: the schema split is an Action output-schema change → **must** be covered by the golden-utterance
eval suite (ADR-039); dispatch regressions block merge.

---

## Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>Y</bff>          <!-- memo docx-generation + persistence (Services/Compose renderers, sprk_analysisoutput write); Action output-schema split (Services/Ai) -->
  <spaarkeai>Y</spaarkeai> <!-- Compose review surface: multi-select gutter, sub-toolbar, bidirectional highlight, confirmation formatting, memo toolbar dropdown, comment-export mirror, clause-location rename -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y → Placement Justification per new server surface; ≤60 MB publish-size check per BFF-touching task. **No new NuGet
expected** (reuses AI/Compose/OpenXML stack). Most work is client-side (Compose); the BFF surface is memo docx-gen +
`sprk_analysisoutput` persistence + the Action-schema split (data/prompt, not packages).

### New Components (§11 three-question gate)
| New component | Existing overlap (grep before claiming none) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Multi-select selection model + sub-toolbar (#3) | `ComposeCommentGutter` (per-note tools), `noteTools`/`onRunNoteTool` | **Extend** the gutter — add selection state + one sub-toolbar; reuse the shipped single-note dispatch in a loop | Without it, a reviewer must run each note one-by-one — the core "batch a review pass" ask fails |
| Summary↔note reverse-highlight (#2) | `NdaReviewSummaryPanel`, `ComposeCommentGutter`, existing anchor/highlight resolution | **Extend** — add the reverse scroll+highlight on the existing join key | Selecting a summary row leaves the matching note un-highlighted — the triage loop stays one-directional |
| Review Summary Memo (assembly + persistence) (#5) | `sprk_analysisoutput` (exists), NDA-REVIEW output schema | **Extend/compose** — assemble from final dispositions; persist to the existing output entity (no new entity) | No exportable deliverable of what changed + why — the owner-requested business artifact doesn't exist |
| Memo toolbar dropdown (#6) | Compose toolbar, `ComposeDocumentRenderer`, `EmailComposer` | **Extend** — one dropdown wiring shipped renderers + EmailComposer to the #5 content | The memo has no user-facing generate/email affordance |
| Action output-schema split (`flaggedClause`/`assessment`) (#7/#5) | `nda-review.action.json` + `outputschemas/nda-review.schema.json` (`explanation`, `standardRef`) | This IS an extension of the Action data (two new output fields) | Both memo + export must string-parse markers out of one prose blob — brittle, duplicated in two consumers |
| Comment-export mirror (#7) | `composeSessionCommentThreadsToAnchoredComments` (`ComposeCommentThread.types.ts:256-262`), the display-only relabel in `ComposeCommentGutter.tsx:343-347` | **Fix/extend** the export mapping — apply the same relabel + append `standardRef` (lift the "never-export" scope at `:89`) | Saved-in-Word comments stay raw prose + hardcoded author — export loses the review structure (owner UAT #7) |
| Configurable comment author (#7a) | Hardcoded literal `ComposeEditor.tsx:2146` | **Extend** — one prop/config | Author is stuck as "AI Advisory Review" with no way to set the right name |
| Sub-domain classify-and-route + pack registry (Lens 3d) | Insights **Layer-1 classification node** (typed enum + confidence + gate) pattern; the grounding/RAG binding path; nda-review's inline scope-guard (declines only) | **Extend** — reuse the Layer-1 node *contract* on the **Reasoning tier** (accuracy-first); add a data-driven pack registry + a below-threshold confirmation | Picking "Agreement Analysis" can't select agreement-specific knowledge — every agreement gets NDA grounding or a bare decline; the type-agnostic promise fails |

*(No net-new services or Dataverse entities. Memo reuses `sprk_analysisoutput`; batch reuses the shipped dispatch;
the schema split is data/prompt, not code. §11 satisfied: every gap is an extension of a shipped surface.)*

### Platform-Enabler Flag (demand-pull discipline)
- **The nda→agreements generalization + `clauseLocation.ts` rename + WS-4 consumption** are shared enablers every future
  per-type project inherits — this project adopts-and-hardens them (first general consumer of WS-4).
- **The sub-domain classify-and-route mechanism + pack registry** (Lens 3d) is the platform seam every per-type project
  plugs into — authored once here, validated on NDA + a general fallback; per-type projects register a pack, no code.
- **The Action output-schema split** (`flaggedClause`/`assessment`) is a shared enabler for both the memo and the export,
  and for any future consumer of structured review data — authored once here.
- **Deferred / not pulled**: PDF ingest (→ compose-r5); the `sprk_analysis` spine + session binding + hub widget (→
  analysis-hub-r1); per-type grounding **packs** (→ per-type sibling projects — the **router** that selects them IS
  built here, Lens 3d); the tabular doc×question grid (hub, deferred).

### ADR Tensions (CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-016** (rate limits / budgets) | batch AI actions could burst concurrent calls | #3 runs an action per selected note | **C — comply** | Run **sequentially with a progress bar** (Decision #2); the progress UI covers the wait; no ADR change |
| **ADR-039** (Action output = schema-validated) | changing an Action's output schema | The schema split adds `flaggedClause`/`assessment` | **C — comply** | Update the golden-utterance eval suite alongside the schema (eval-case obligation); dispatch regressions gate merge |
| **§10 BFF Hygiene** | adding to `Sprk.Bff.Api` | memo docx-gen + persistence touch the BFF | comply | Placement Justification (reuse Compose renderers + `sprk_analysisoutput`); publish-size check; no new NuGet |

*(No ADR **amendment** anticipated — the advisory-tier grounding amendment was authored in nda-r1 and is inherited.)*

### Assumptions (proceeding unless corrected)
- **DOCX-only for r1** — the review surface, memo, and export are validated on DOCX agreements; PDF arrives with
  compose-r5. Owner confirmed (Decision #1).
- **Grounding is supplied externally** — r1 does not author per-type golden references; it validates the type-agnostic
  machine against whatever sub-domain grounding is bound (NDA today; others via sibling projects). Option (a), owner-confirmed.
- **`sprk_analysisoutput` is the memo store** — no new entity; JSON body for the section array (Decision #3).
- **Server `ApplyComment` needs no change** for #7 — it renders whatever `commentText`/`Author` it's given; the fix is
  client-side export-string composition.
- **The schema split is preferred over client-side parsing** — owner-confirmed; done once at the Action layer so both
  memo and export consume discrete fields.

---

## Resolved (owner, 2026-07-28 / 2026-07-29)
- ✅ **PDF (#1) → `compose-r5`**, out of agreements-r1. Build/validate on DOCX; inherit PDF later.
- ✅ **#3 batch = sequential-with-progress** (ADR-016).
- ✅ **Memo → `sprk_analysisoutput`** + JSON body; no new entity.
- ✅ **Memo before/after = final change-disposition state** (before=original, after=accepted/rejected); no timestamp capture.
- ✅ **Work-type scope = general across ALL agreement types**; sub-domains vary grounding only; no per-type UX branches.
- ✅ **Both R4.5 handoff items folded in as scoped work** (DEF-01 correctness fix + nda→agreements generalization/rename/WS-4).
- ✅ **Schema split done once** at the Action output layer (`flaggedClause`/`assessment`), serving both memo + export.
- ✅ **Per-type knowledge PACKS = separate sibling projects** (option a; the nda-r1 pattern). But the **classify-and-route
  mechanism** (pick Agreement Analysis → classify sub-domain → bind agreement-specific grounding) **IS in scope here**
  (Lens 3d), validated on NDA; per-type projects register packs into it with no code change. *(owner, 2026-07-29 —
  refines the earlier "builds no routing" phrasing.)*
- ✅ **Classifier = accuracy-first, not cheapest** (owner, 2026-07-29). Reuse the Insights **Layer-1 node contract**
  (typed sub-domain enum + confidence) but run it on the **Reasoning tier** as the pre-review step — a wrong route
  wastes a full analysis, so robustness beats classifier cost. (nda-review has no real classifier today, only an inline
  scope-guard.)
- ✅ **Hint + near-certain-else-confirm** (owner, 2026-07-29). User may hint the type (authoritative). Auto-classify
  auto-proceeds only when **near-certain**; otherwise **confirm with the user** before running — no silent wrong-grounding
  run, no silent general fallback.
- ✅ **Near-certain threshold = ≥0.85 confidence (baseline)** (owner, 2026-07-29). A single global default, biased toward
  confirming over mis-running; **review/tune per-sub-domain via UAT + user feedback**. Encode as configuration, not a
  hardcoded constant, so it's adjustable without a redeploy. *(2026-07-31: per-type override home =
  `sprk_agreementtype.sprk_confidencethreshold`, already exists.)*
- ✅ **Hub Part C.1 + Phase-2/3 remainder ACCEPTED as agreements-r1 scope** (owner "proceed as recommended",
  2026-07-31): the durable-recall re-route (Lens 3e, 4-change set) + the wizard→review auto-run bridge (Lens 3f).
  Verified against code in [`notes/HUB-R1-REVIEW-2026-07-30.md`](notes/HUB-R1-REVIEW-2026-07-30.md).
- ✅ **Registry = the `sprk_agreementtype` Dataverse table** (2026-07-31, supersedes "Action/Binding data"): hub owns
  identity columns/rows; agreements-r1 owns behavior values + the code mirror. `subDomain` ≡ `sprk_key`; the
  `sprk_analysis` lookup's logical name is `sprk_agreementtype`.

## Unresolved Questions (answer before/with implementation)
- [ ] **Batch scope for #3** — confirm the batch AI-action set is exactly the work-type-scoped `getToolsForSurface`
  palette (no batch-only actions), and whether "select all → run" caps at a max selection count for UX/rate-limit sanity.
- [ ] **`standardRef` depth in the export/memo** — citation only, or citation + full standard clause text? (Owner note
  #7: "ideally full clause text.") Confirm whether the grounding layer reliably supplies the full clause text.
- [ ] **Rename blast radius** — confirm no external consumer (outside Compose) imports `ndaClauseLocation`/
  `NdaReviewSummaryPanel` by name before the rename (grep at task time; pure-rename risk only).
- [ ] **Hub ↔ machine hand-off shape** — *(largely resolved 2026-07-31)*: `activeWorkType` is shipped end-to-end; the
  **`subDomain` param does not exist yet** — adding it to `SpaarkeAiLaunchParams` + `buildLaunchUrl` + `main.tsx` parse
  is a small task (ours or hub's; coordinate). Note the `worktype` URL param is a boolean "new-mode" flag and the
  `regarding` URL param is dead (`void regarding`) — the live regarding channel is `entityLogicalName`/`entityId`.
- [x] ~~Picker landmine + A1 ownership~~ — **RESOLVED 2026-07-31**: owner fixed the seeds (all `sprk_isselectable=Yes`,
  verified); hub SHIPPED the picker (`1e1a6579b`) + A3-core envelope (`bd64a69d4`); the deep-threading slice is ours
  (task 022, hub Q1 answer — "do NOT rebuild A1 or A3-core").
- [x] ~~Promote silent-FK gap ownership~~ — **RESOLVED: hub fixes** (their bug; Q2 answer, tracked closeout). We verify
  + keep the durable-bind acceptance (Lens 6) — 033 does not re-implement.
- [ ] **`sprk_key` uniqueness** — owner action in flight (hub Q4); task 001 verifies + coordinates-once before code
  keys on it.

---

**Next step**: `/project-pipeline` (spec.md updated 2026-07-31 against hub built state; hub substrate merged to master).
