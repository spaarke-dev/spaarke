# NDA Review, Analysis & Drafting — Design

> **Program**: ai-advanced-capabilities-development (use-case vertical #1)
> **Project**: `ai-advanced-capabilities-nda-r1`  ·  **Round**: r1
> **Date**: 2026-07-21  ·  **Owner**: ralph.schroeder@hotmail.com
> **Driver**: Use case (vertical). Defined by the NDA use case, not by a horizontal capability.
> **Produced by**: `use-case-to-design` (6-lens method). Next: `/design-to-spec ai-advanced-capabilities-nda-r1`.
> **Capability verdicts** below reference the 2026-07-21 code audit captured in [`../ai-advanced-capabilities-development/PROGRAM-ROADMAP.md`](../ai-advanced-capabilities-development/PROGRAM-ROADMAP.md) §1 and the skill's `capability-lenses.md`.

---

## Lens 1 — Use Case Definition

**One-line goal**: A **non-lawyer** uploads an NDA and gets a full, standards-based review and a compliant NDA in **Word** — without needing legal expertise.

**Personas**:
- Primary: **non-lawyer requester** (procurement, sales, ops, HR) who receives or needs an NDA and must know "is this OK to sign?" / "give me a clean one."
- Secondary: **paralegal / junior associate** who triages the AI output and handles escalations.

**Jobs to be done** (all three in r1 scope — "review / analysis / drafting"):

| Sub-UC | Job | Output |
|---|---|---|
| **UC1 — Review & redline** | Upload an NDA; analyze whether it follows the **company's NDA standard**; return an overall summary + **overall risk rating**, a concise list of **flagged sections**, and **redline revisions** with rationale in the redline comments | Findings card + inline redlined NDA → **Word** (tracked changes + comments) via Compose |
| **UC2 — Draft** | Generate a compliant NDA **filled in for the requester's parameters** (parties, description of purpose, effective date, term, mutual/unilateral…) | Drafted NDA in Compose → **Word** |
| **UC3 — Standard summary** | Summarize the **general required terms** of an NDA under the company standard (plain language) | Concise summary (chat/card) |

**Triggers**:
- From the **SpaarkeAi Assistant**: file upload + a **"Review NDA" action card**, OR
- **Natural-language** request: "review this NDA" / "draft an NDA for…" / "what are our required NDA terms?"

**Inputs → Outputs**:
- UC1: NDA file (DOCX/PDF) → findings (risk + flagged sections) + redlined Word doc.
- UC2: parameter set (parties, purpose, effective date, term, direction) → drafted Word NDA.
- UC3: (no doc) → plain-language summary of required terms.

**Scope boundaries / non-goals (r1)**:
- Single NDA at a time. **Mass/tabular multi-NDA review is OUT** (future `-tabular-r2`).
- **Standard = the company's NDA standard.** Not general legal advice; the output carries a "not legal advice" disclaimer.
- No negotiation-loop / counterparty round-tripping automation in r1.
- No non-NDA contract types (lease, employment…) — those are their own verticals.

**Done-criteria**:
- A non-lawyer can, unaided, upload an NDA and receive: an overall risk rating, a flagged-section list, and a redlined Word document with rationale comments; and can request a compliant draft NDA and a standard-terms summary.
- Every finding cites the NDA section (and, where applicable, the standard clause) it derives from.

**Business value**: Removes the paralegal/associate bottleneck for routine NDA intake and issuance; standardizes NDA risk posture across the company; produces an auditable, cited review trail.

---

## Lens 2 — Surface / UX

**Target surfaces**: **SpaarkeAi workspace** (host) · **SprkChat / Assistant** (entry + conversation) · **Compose/Tiptap editor** (redline + draft, Word I/O) · **Context pane** (execution trace + citations). No DataGrid in r1 (single-doc).

**UC1 interaction walk-through**:
```
User (Assistant): uploads Acme_NDA.docx → clicks "Review NDA" card   (or types "review this NDA")
  → Assistant routes to the NDA review capability (Click path binding, or NL intent → same binding)
  → Context pane streams review steps (execution trace):
       • Read Acme_NDA.docx
       • Applied NDA Standard Review
       • Checked 15 standard clauses … 3 flagged
       • Verified citations (n/n quotes found)
  → Compose editor opens the NDA with inline redlines (insertions/deletions) + comment bubbles ("why")
  → Findings card in the conversation: Overall Risk = MEDIUM; flagged sections list (each links to the clause)
  → User reviews redlines (accept/reject per change) → exports Word (tracked changes + comments)
  → Disclaimer footer: "AI can make mistakes. Not legal advice."
```

**UC2 interaction walk-through**:
```
User: "draft an NDA" → Assistant collects parameters (parties, purpose, effective date, term, mutual/unilateral)
   (conversational slot-filling; missing params requested, never invented)
  → Draft capability generates the NDA from the company template into Compose → user edits → export Word
```

**UC3**: NL "what are our required NDA terms?" → plain-language summary card (retrieval + summarize over the standard).

**Reused UI**: SprkChat + file attachment + action chips; Compose editor + redline marks + comment anchors + Word shuttle; Context-pane execution-trace widget; citation badges/highlights.

**Net-new UI**: none required for r1 beyond wiring a **"Review NDA" action card** and assembling the citation source-viewer (see Lens 4). Confirm during design-to-spec.

**Required states**:
- **Loading**: streamed steps in Context pane (no dead spinner).
- **Empty**: no NDA uploaded → prompt to upload or paste.
- **Error**: unreadable/oversized file, non-NDA document detected → clear message + how to proceed.
- **Uncertainty (critical for non-lawyers)**: low-confidence findings render as an explicit "couldn't confirm — recommend human review" state (decline path), not a false-confident green. Redlines the model is unsure about are marked for attorney review.

---

## Lens 3 — AI Capabilities Required

For the NDA service to work, the AI service MUST be able to:

| # | Capability need | Primitive type |
|---|---|---|
| C1 | Ingest an uploaded NDA (DOCX/PDF) to text | Tool / Compose docx bridge |
| C2 | Review the NDA against the **company NDA standard**, producing structured `{overallRisk, flaggedSections[], recommendedEdits[]}` | Prompted Action (JPS) + output schema |
| C3 | Ground the standard: retrieve company standard clauses + KNW clause libraries | Knowledge/RAG source + retrieval tool |
| C4 | Apply `recommendedEdits[]` as **inline redlines with rationale comments** across the doc | Compose redline synthesizer + comment anchors + Binding (Compose disposition) |
| C5 | Verify each finding cites a real NDA section / standard clause | Citation verification / grounding |
| C6 | Export the redlined NDA to **Word** (tracked changes + comments) | Compose Word shuttle / DOCX annotation writer |
| C7 | **Draft** an NDA from the company template + collected parameters | Prompted Action + Compose disposition |
| C8 | Collect draft parameters conversationally (ask for missing, never invent) | Input-collection / slot-filling |
| C9 | **Summarize** required NDA terms from the standard | Prompted Action (Fast) + retrieval |
| C10 | Route from the Assistant (file-upload "Review NDA" card **and** NL "review this NDA") to the review capability | Binding + intent routing (Text + Click paths) |
| C11 | Surface review steps live to the Context pane | Execution trace |
| C12 | (optional) Remember company-standard deviations / counterparty patterns across NDAs | Memory (Record scope) |

---

## Lens 4 — Have vs. Gap

> Precedence applied: **REUSE > ACTIVATE > COMPLETE > BUILD.** Evidence per `PROGRAM-ROADMAP.md` §1 / `capability-lenses.md` (2026-07-21 audit).

| Cap | Verdict | Evidence | What's needed |
|---|---|---|---|
| C1 ingest DOCX/PDF | **REUSE** | `Spaarke.Compose.Components/.../utils/docxBridge.ts` (mammoth in), Compose service | none |
| C2 NDA review Action | **BUILD (content) — CONFIG** | Action model exists (`ActionRunner`, `PromptSchemaRenderer`); no NDA-review Action row yet | Author `NDA-REVIEW@v1` prompted Action from Mike `nda-review` (MIT) + company standard; define output schema |
| C3 standard retrieval | **COMPLETE** | `ReferenceRetrievalService.cs`, `spaarke-rag-references` (93 docs incl. KNW clause libs) | Seed **company NDA standard** docs into the reference index (content gap, not code) |
| C4 apply redlines + rationale | **COMPLETE** | redline marks + `ComposeParagraphRedlineSynthesizer.cs`, `compose-draft-alternative` binding, `CommentAnchorMark.ts` | Extend the draft-alternative pipeline to apply **N review-driven edits across the whole doc** with per-edit "why" comments (single-selection today) |
| C5 citation verification | **REUSE** | `Services/Ai/CitationVerification/GroundingVerifier.cs`, `CitationSafetyCheck` | none |
| C6 Word export (tracked changes) | **REUSE** | `Services/Compose/DocxAnnotationWriter.cs`, `useComposeWordShuttle.ts` | none |
| C7 draft NDA Action | **BUILD (content) — CONFIG** | Compose draft disposition + `compose-draft-document` binding exist | Author `NDA-DRAFT@v1` from Mike `draft-from-template` (MIT) + company template |
| C8 parameter slot-filling | **COMPLETE (verify)** | `BindingInputSchemaValidator.cs`, input schemas; Mike `ask_inputs` pattern | Confirm conversational collection of missing params exists or needs a light input step |
| C9 standard-terms summary | **REUSE** | `SUM-CHAT@v1` Action + `KnowledgeRetrievalHandler` | Point a summary binding at the standard |
| C10 Assistant routing (card + NL) | **ACTIVATE** | `QuickActionChips.tsx`, `useChatFileAttachment.ts`, `SessionDispatchOrchestrator`; client `bindingId:''` chip stubs | Add "Review NDA" card → resolve its binding GUID; NL intent → same binding |
| C11 execution trace | **ACTIVATE** | `ExecutionTraceWidget.tsx`, `ComposeTraceHost.tsx` (built; empty in practice) | Resolve compose action `bindingId:''` stubs so trace events flow |
| C12 memory (optional) | **ACTIVATE** | `MemoryCompositionService.cs` dark; `MemoryItemStore` live | Optional in r1; if included, wire pinned/record memory into prompt path (demand-pull note below) |
| C13 citation source viewer | **COMPLETE** | `CitationBadge.tsx` + `context_highlight` SSE + Tiptap `QaHighlightExtension` | Assemble click→open-source→jump-to-passage (nice-to-have for r1) |

**Net-new BUILD components: none.** The two "BUILD" cells (C2, C7) are **authoring/config** (Action rows + prompts), not new code components. Everything else is REUSE / ACTIVATE / COMPLETE. This is why NDA is the right first vertical.

**The one real code extension**: C4 — apply multiple review-driven edits across the whole document (today's redline path targets a single selection). Decide in design-to-spec: **(preferred)** extend `ComposeParagraphRedlineSynthesizer` to accept an `edits[]` batch from the review Action's structured output; **(alt)** a coded workflow `NdaReviewWorkflow` (`ICodedWorkflow`, à la `DailyBriefingNarrator`) if orchestration (review → map findings→edits → apply → assemble card) proves too complex for prompted-Action + deterministic apply.

---

## Lens 5 — Configuration

**Actions** (`sprk_analysisaction`):
- `NDA-REVIEW@v1` — **prompted**; JPS system prompt adapted from Mike `nda-review` SKILL.md (MIT) + company NDA standard; output schema `{ overallRisk: enum(Low|Medium|High|Critical), flaggedSections: [{sectionRef, issue, severity, standardClauseRef}], recommendedEdits: [{find, replace, contextBefore, contextAfter, reason}] }`; model tier **Standard/Premium**.
- `NDA-DRAFT@v1` — **prompted**; adapted from Mike `draft-from-template` (MIT) + company NDA template; inputs {parties, purpose, effectiveDate, term, direction}; disposition **Compose**; model tier Standard.
- `NDA-STANDARD-SUMMARY@v1` — **prompted** (or reuse `SUM-CHAT@v1`) over the standard; model tier **Fast**.

**Bindings** (`sprk_playbookconsumer`):
- `nda-review/default` → `NDA-REVIEW@v1`, disposition **WorkProduct** (+ redline via Compose), risk **ConfirmWhenUncertain**, surfaces: workspace + chat, event: `document_uploaded` (conditional on NDA classification) + chip.
- `nda-draft/default` → `NDA-DRAFT@v1`, disposition **Compose**.
- `nda-standard-summary/default` → summary Action, disposition **Informational**.

**Tools / capability grants**: project `document.*`, `search.*` (reference retrieval), `verify_citations` into the NDA context; grant the `nda_review` capability to gate these bindings.

**Knowledge / reference docs**:
- **Spaarke Baseline NDA Standard v0.1** → [`notes/spaarke-nda-standard-baseline.md`](notes/spaarke-nda-standard-baseline.md) — synthesized 2026-07-22 from open sources (Mike `nda-review` MIT; Bonterms & Common Paper Mutual NDAs CC BY 4.0; practitioner guides). 16-clause compliance rubric (Part B) + required-terms set (Part C) + plain-language summary (Part D). Seed Parts A–C into `spaarke-rag-references` via `/add-reference-to-index`; embed Parts A–B in the review Action prompt. **Baseline — counsel ratification pending (see checklist in that doc).**
- **Company NDA template** (for UC2 draft): interim = a standards-compliant mutual NDA structured per Bonterms/Common Paper (CC BY 4.0); replace with the firm's actual template when supplied.
- Existing KNW clause libraries (KNW-001 Contract Terms Glossary, KNW-002 NDA Review Checklist, etc.) already indexed — reuse for grounding.

**Grid config**: n/a (r1 single-doc).

**License attribution**: `NDA-REVIEW@v1` and `NDA-DRAFT@v1` prompts adapted from Mike OSS `mike-workflows` (`nda-review`, `draft-from-template`), **MIT** — retain attribution in the Action metadata/notes.

---

## Lens 6 — Acceptance & Evaluation

**Closed test set** (≥6 NDAs):
1. Compliant mutual NDA → expect **Low** risk, ≤1 flag, minimal redlines.
2. Unilateral NDA missing standard carve-outs → expect flagged "missing carve-outs" + redline inserting them.
3. NDA with uncapped liability / non-standard indemnity → **High/Critical**, redline narrowing.
4. NDA with overlong term / no return-destruction clause → flags + redlines.
5. NDA from represented-party's *counterparty* perspective → correct perspective-aware findings.
6. Well-drafted NDA with only a drafting error (broken cross-ref) → catches the mechanical error.

**Negative / authorization cases** (≥1 each — required):
- **Wrong doc type**: upload a lease/invoice → system detects non-NDA and declines (no fabricated NDA review).
- **Insufficient/unreadable**: scanned image PDF with no extractable text → clear error, no hallucinated review.
- **Authorization**: user without the `nda_review` capability → capability-gated refusal.

**Draft (UC2) acceptance**: given a parameter set, produces a Word NDA with all parameters correctly substituted and **no invented facts** (missing params → placeholder or ask).

**Eval harness**: encode the above as cases in `legal-eval-config.yaml`; run `metrics/citation_accuracy.py` for citation grounding. **Every finding must cite its NDA section.** Success targets: citation accuracy ≥ target; flagged-section recall on the seeded standard; redline acceptance rate in UAT.

---

## Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>Y</bff>          <!-- new Actions/Bindings + redline-apply extension in Services/Ai + Services/Compose -->
  <spaarkeai>Y</spaarkeai> <!-- Assistant "Review NDA" card, workspace/Compose/Context-pane wiring -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y → Placement Justification required per new server surface; ≤60 MB publish-size check per task. No new NuGet expected (reuses existing AI/Compose stack).

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `NDA-REVIEW@v1` / `NDA-DRAFT@v1` Actions | `infra/dataverse/actions/*.action.json` (compose-*), `ActionRunner` | These ARE extensions of the existing Action model (new data rows, not new code) | Without them the NDA use case has no review/draft capability — the entire use case fails |
| Multi-edit redline apply | `ComposeParagraphRedlineSynthesizer.cs`, `compose-draft-alternative` | **Yes — extend** to accept an `edits[]` batch | Without it, review findings can't become inline redlines; UC1's core Word output fails |
| "Review NDA" action card | `QuickActionChips.tsx` (chip framework) | **Yes — extend** (add card + resolve binding) | Without it, non-lawyers can't launch review from the Assistant (NL still works) |

*(No net-new services/entities. If the review orchestration forces a coded workflow, add `NdaReviewWorkflow : ICodedWorkflow` as a §11 row at design-to-spec.)*

### Platform-Enabler Flag (demand-pull discipline)
- **Execution-trace activation** and **compose `bindingId` stub resolution** are shared capabilities this use case **pulls through first** — adopt-and-harden here; later verticals inherit.
- **Memory (C12)** — optional in r1; if included, wire `MemoryCompositionService`/pinned read into the prompt path (minimal increment for NDA: remember standard deviations). This is the first demand-pull of the dark memory layer; keep it minimal, don't build the full hierarchical composer unless NDA needs it.
- No scheduler / `IGateResolver` / `sprk_analysis` table needed for r1 (interactive, single-doc).

### Candidate ADR Tensions (CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Likely path | Rationale |
|---|---|---|---|---|
| ADR-039 (grounded/closed) | every output grounded | UC3 summary + some review narrative are generative, not doc-grounded | **A (exception)** | Non-lawyer summary is standard-grounded, human-verified, low-stakes; scope a documented exception (candidate for the tiered-governance amendment) |
| — | — | No hard tension for UC1/UC2 (both cite + human-accept) | comply | Review findings cite; redlines are human-accepted |

### Assumptions (proceeding unless corrected)
- **Company NDA standard**: `NDA-REVIEW@v1` is bootstrapped from the **Spaarke Baseline NDA Standard v0.1** (`notes/spaarke-nda-standard-baseline.md`) — synthesized from Mike (MIT) + Bonterms & Common Paper open standards (CC BY 4.0) + practitioner guidance. Review quality tracks this baseline; counsel ratification refines it (checklist in the doc). No longer a hard blocker.
- **Company NDA template**: UC2 draft uses an interim standards-compliant mutual NDA (Bonterms/Common Paper structure, CC BY 4.0) until the firm's template is supplied.
- **Default posture**: mutual NDA; represented party = requester's company; residuals disfavored (B10); governing law/forum to be set at ratification.
- All three sub-UCs (review, draft, summary) are in r1; tabular/mass review deferred to r2.

## Unresolved Questions (answer before/with design-to-spec)
- [ ] **Counsel ratification of the baseline standard** (`notes/spaarke-nda-standard-baseline.md`) — confirm/override per-clause positions + severities; set company defaults (governing law/forum, term lengths, residuals posture). *Refinement, not a blocker to start.*
- [ ] Provide the **firm's actual NDA template** for `NDA-DRAFT@v1` fidelity (interim CC BY template works meanwhile).
- [ ] Draft parameter collection: conversational slot-filling vs. a small structured input form? Blocks: C8 UX.
- [ ] Redline orchestration: prompted-Action + deterministic multi-edit apply (preferred) vs. `NdaReviewWorkflow` coded workflow? Blocks: C4 §11 component decision.
- [ ] Include memory (C12) in r1, or defer? Blocks: demand-pull scope.

---
*Design produced by use-case-to-design (6-lens). Next: `/design-to-spec ai-advanced-capabilities-nda-r1`.*
