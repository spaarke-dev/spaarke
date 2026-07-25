# NDA Review & Analysis (Advisory Vertical) — Design

> **Program**: ai-advanced-capabilities-development (use-case vertical #1)
> **Project**: `ai-advanced-capabilities-nda-r1`  ·  **Round**: r1
> **Date**: 2026-07-21  ·  **Scope refined**: 2026-07-25 (owner)  ·  **Owner**: ralph.schroeder@hotmail.com
> **Driver**: Use case (vertical). Defined by the NDA use case, not by a horizontal capability.
> **Produced by**: `use-case-to-design` (6-lens method); refined via `/design-to-spec` clarification. Next: `/design-to-spec ai-advanced-capabilities-nda-r1` → `spec.md`.
> **Capability verdicts** below reference the 2026-07-21 code audit ([`../ai-advanced-capabilities-development/PROGRAM-ROADMAP.md`](../ai-advanced-capabilities-development/PROGRAM-ROADMAP.md) §1) **and two 2026-07-24/25 code-validation passes** (recorded inline where they refine the original audit).

---

## 0. North Star (owner, 2026-07-25) — read first

This project is the **first repeatable "analysis / advisory" AI vertical**. Its defining goal:

> **Deliver Claude/ChatGPT-level reasoning and generative advisory output.** For these analysis/advisory use cases we deliberately **do not want to be overly limited by Spaarke's normal deterministic guardrails.** The user's bar: *"I expect better output than if I used Claude or ChatGPT online."*

The advisory information **must be accurate and never fabricated**, but it **benefits from the full depth and breadth of the LLM model** — not a temperature-0.2, verbatim-only comparison. NDA is the **template** for future advisory verticals (lease, employment, credit…).

Two structural consequences drive the rest of this design:

1. **ADR-039 amendment (this project carries it — CLAUDE.md §6.5 Path B).** ADR-039's "grounded execution" invariant must be refined to distinguish a **fact/deterministic tier** (assertions about document/data content — verbatim-grounded, citation-verified) from an **advisory/probabilistic tier** (reasoned recommendations — full model reasoning permitted, still Action-prompt-controlled, factual claims cited, marked "not legal advice", nothing fabricated). See §6 ADR Tensions.
2. **Model selection becomes real.** The advisory tier needs a **more capable model** than the global `gpt-4o-mini`. The catalog already carries the intent (`sprk_modeltier` on the Action) — this project **wires the dead-ended tier→deployment path** and points the NDA review Action at the **Reasoning** tier. See Lens 5.

---

## Lens 1 — Use Case Definition

**One-line goal**: A **non-lawyer** uploads an NDA and gets a full, standards-based advisory review — a concise cited summary, in-document advisory comments, and standards-based rewrite suggestions — without needing legal expertise.

**Personas**:
- Primary: **non-lawyer requester** (procurement, sales, ops, HR) who must know "is this OK to sign?" and "how do I fix it?"
- Secondary: **paralegal / junior associate** who triages the AI output and handles escalations.

**Jobs to be done** (r1 scope):

| Sub-UC | Job | Output |
|---|---|---|
| **UC1 — Review & advise** | Upload an NDA; analyze it against the **company NDA standard** with full advisory reasoning; return an **overall risk rating**, a concise **flagged-section summary** (in the Assistant), **in-document advisory Comments** on each flagged term, and standards-based **rewrite suggestions** applied per-section by the user | Concise cited summary (Assistant + Compose review-summary panel) · right-gutter advisory Comments in Compose · a generated **Summary Page** in the exported doc (comments baked into the DOCX) |
| **UC3 — Standard summary** | Summarize the **general required terms** of an NDA under the company standard (plain language) | Concise summary (chat / card) |

> **UC2 — Draft an NDA from parameters is DEFERRED to a separate project** (owner, 2026-07-25). The per-section "Draft Alternative" rewrite inside UC1 stays (it reuses the existing single-selection Compose tool); generating a *fresh* NDA from a parameter set is out of r1.

**Triggers**:
- From the **SpaarkeAi Assistant**: file upload → Assistant classifies as NDA-related → a **"Review an NDA" action card**, OR
- **Natural-language** request: "review this NDA" / "what are our required NDA terms?"

**Inputs → Outputs**:
- UC1: NDA file (DOCX/PDF) → risk rating + flagged-section summary (Assistant/Compose review panel) + right-gutter advisory Comments in Compose + user-driven per-section rewrites + Summary Page on export + SPE save with versioning.
- UC3: (no doc) → plain-language summary of required terms.

**Scope boundaries / non-goals (r1)**:
- Single NDA at a time. **Mass/tabular multi-NDA review is OUT** (future `-tabular` project).
- **UC2 fresh-draft-from-parameters is OUT** (deferred to a separate project).
- **Standard = the company's NDA standard.** Not general legal advice; every advisory output carries a "not legal advice" disclaimer.
- No negotiation-loop / counterparty round-tripping automation.
- No non-NDA contract types.
- **Memory (C12) is OUT of r1** — deferred to the program's Memory Activation project (see Assumptions).

**Done-criteria**:
- A non-lawyer can, unaided, upload an NDA and receive: an overall risk rating; a concise, cited flagged-section summary; advisory Comments anchored to the flagged clauses in the Compose document; the ability to rewrite each flagged section with company standards via the existing Draft Alternative tool; and an exported Word document that carries a **Summary Page** (TL;DR + flagged-section overview + recommendations).
- Every **factual finding cites the NDA section** it derives from (and, where applicable, the standard clause). Advisory reasoning is rich but tethered to those citations and marked "not legal advice."
- The review runs on the **Reasoning** model tier (not the global default), demonstrably producing deeper output than the baseline model.

**Business value**: Removes the paralegal bottleneck for routine NDA intake; standardizes NDA risk posture; produces an auditable, cited review trail; and establishes the **reusable advisory-vertical pattern** (surface + Action + model-tier + governance) for the rest of the program.

---

## Lens 2 — Surface / UX

**Target surfaces**: **SpaarkeAi workspace** (host) · **SprkChat / Assistant** (entry + concise summary) · **Compose/Tiptap editor** (the single work surface — review-summary panel + right-gutter advisory Comments + per-section Draft Alternative + Word I/O + SPE save) · **Context pane** (execution trace + citations). **No dedicated Analysis widget in r1** (Compose is the single surface). No DataGrid in r1 (single-doc).

**UC1 interaction walk-through** (owner-confirmed, 2026-07-25 — supersedes the original auto-redline walk-through):
```
User (Assistant): uploads Acme_NDA.docx
  → Assistant classifies the upload as NDA-related → offers a "Review an NDA" action card
  → User clicks "Review an NDA"
       • the file opens in the Compose tab
       • the NDA-review Action runs a WHOLE-DOCUMENT advisory analysis on the Reasoning model tier
       • Context pane streams execution-trace steps (read doc, retrieved standard, N clauses flagged, citations verified)
  → Assistant shows a SHORT, CONCISE, BULLET-POINT summary of key terms + overall risk,
       each bullet carrying a page/section/paragraph reference (NOT long prose)
  → Compose review-summary panel presents the fuller advisory: overall risk, the flagged-section list with
       advisory explanations + recommendations; clicking a finding jumps to that clause in Compose
  → Compose document: each flagged term is HIGHLIGHTED with an advisory Comment whose text is the
       AI's analysis/recommendation ("why this is flagged, what the standard wants")
  → User walks each highlighted section, selects it, and uses the embedded Compose AI tool
       "Draft Alternative" to rewrite it using company standards (existing single-selection tool)
  → On export, a SUMMARY PAGE is added to the Word doc (start or end): TL;DR + flagged-section
       overview + recommendations — concise, since the document body carries the detail
  → User saves / exports Word.
  → Disclaimer footer throughout: "AI can make mistakes. Not legal advice."
```

**UC3**: NL "what are our required NDA terms?" → plain-language summary card (retrieval + summarize over the standard).

**Reused UI**: SprkChat + file attachment + action chips; Compose editor + comment-anchor marks + Draft Alternative tool + Word shuttle; Context-pane execution-trace widget; citation badges/highlights.

**Net-new UI (r1)**:
- **Review-summary docked panel in Compose** (NEW — owner 2026-07-25, single-surface decision): hosts overall risk + flagged-section list + advisory reasoning inside Compose; click-to-navigate to the clause. Follows the `ComposeCommentThread`/`ComposeFindReplace` docked-panel convention (or a `ComposeWorkspace` `bannerStack` region). **No separate Analysis widget in r1** — fork one later only if needed.
- **Right-side comment gutter** (NEW — owner #6): advisory Comments render in a right-rail aligned to their anchored clauses (Tiptap/ProseMirror `coordsAtPos` + widget decorations), not a stacked top list.
- **"Review an NDA" action card** — wire the chip + resolve its Binding GUID (client chip stubs ship with `bindingId:''`).
- **Runtime model picker** (NEW, in r1) — a lightweight Assistant-side tier control (Fast / Standard / Reasoning) driving `sprk_modeltieroverride`.
- **Advisory-Comments materialization** into the live Compose doc (client wiring — see Lens 4 C4).
- **Summary Page** insertion into the exported DOCX (server writer addition — see Lens 4 C6b).

**Required states**:
- **Loading**: streamed steps in Context pane (no dead spinner).
- **Empty**: no NDA uploaded → prompt to upload or paste.
- **Error**: unreadable/oversized file, non-NDA document detected → clear message + how to proceed.
- **Uncertainty (critical for non-lawyers)**: low-confidence findings render as an explicit "couldn't confirm — recommend human review" state (decline path), not a false-confident green. Advisory suggestions the model is unsure about are marked for attorney review.

---

## Lens 3 — AI Capabilities Required

| # | Capability need | Primitive type | r1 |
|---|---|---|---|
| C1 | Ingest an uploaded NDA (DOCX/PDF) to text | Compose docx bridge (mammoth) | ✅ |
| C2 | **Whole-document** advisory review against the company NDA standard, producing `{overallRisk, flaggedSections[]}` with rich advisory reasoning on the **Reasoning** model tier | Prompted Action (JPS) + output schema + model-tier | ✅ |
| C3 | Ground the standard: retrieve company standard clauses + KNW clause libraries | Knowledge/RAG source + retrieval tool | ✅ |
| C4 | Render each finding as an **in-document advisory Comment** (highlight + explanation) across the whole doc | Compose comment-materialization wiring (new event + receiver) | ✅ |
| C5 | Verify each **factual** finding cites a real NDA section / standard clause | Citation verification / grounding | ✅ |
| C6 | User-driven **per-section rewrite** using company standards | Existing "Draft Alternative" tool (single selection) | ✅ |
| C6b | **Summary Page** (TL;DR + flagged overview + recommendations) inserted into the exported Word doc | DOCX writer addition | ✅ |
| C7 | Surface the concise cited summary in the **Assistant** + fuller advisory in a **Compose review-summary panel** (single surface) | Assistant render + Compose docked panel | ✅ |
| C8 | **Model-tier selection**: point the review Action at the Reasoning deployment (Dataverse-declared) **+ a runtime model picker in the Assistant** (`sprk_modeltieroverride`) | Wire dead-ended `sprk_modeltier`/`sprk_modeltieroverride` → deployment | ✅ |
| C9 | **Summarize** required NDA terms from the standard (UC3) | Prompted Action (Fast) + retrieval | ✅ |
| C10 | Route from the Assistant (file-upload "Review NDA" card **and** NL "review this NDA") to the review capability; classify the upload as NDA | Binding + intent routing (Text + Click) + doc classification | ✅ |
| C11 | Surface review steps live to the Context pane | Execution trace | ✅ |
| C12 | Remember company-standard deviations / counterparty patterns across NDAs | Memory (Record scope) | ⛔ deferred |
| C13 | Draft a **fresh** NDA from parameters | Prompted Action + Compose disposition | ⛔ deferred (UC2 → separate project) |

---

## Lens 4 — Have vs. Gap

> Precedence: **REUSE > ACTIVATE > COMPLETE > BUILD.** Verdicts below fold in the 2026-07-24/25 code-validation passes, which **corrected two original-audit assumptions** (noted ⚠).

| Cap | Verdict | Evidence | What's needed |
|---|---|---|---|
| C1 ingest DOCX/PDF | **REUSE** | `Spaarke.Compose.Components/.../utils/docxBridge.ts` (mammoth), `useChatFileAttachment.ts` | none |
| C2 whole-doc review Action | **BUILD (content) — CONFIG + model-tier** | `ActionRunner`, `PromptSchemaRenderer`; `compose-compare-to-playbook` exists but is **single-clause + `disposition:informational`** ⚠ (not whole-doc, not doc-annotating) | Author `NDA-REVIEW` Action: whole-doc advisory prompt (adapted from Mike `nda-review`, MIT + company standard), output schema `{overallRisk, flaggedSections:[{sectionRef, issue, severity, advisory, standardClauseRef}]}`, **`sprk_modeltier = Reasoning`** |
| C3 standard retrieval | **COMPLETE** | `ReferenceRetrievalService.cs`, `spaarke-rag-references` (93 docs incl. KNW clause libs); ingest via `ReferenceIndexingService` + `/add-reference-to-index` | Seed **company NDA standard** docs (content gap) |
| C4 in-doc advisory Comments | **COMPLETE — client wiring** ⚠ | Primitives exist: `CommentAnchorMark.ts`, `useComposeCommentThreads.createThread(text, range)` + `importThreads`, `resolveTargetSpans(...,'strict')`, `useComposeWorkspaceReceivers.ts` | **NEW**: a `compose_advisory_comments` pane/SSE event carrying `[{targetText, advisoryText, riskLabel}]` + a receiver branch that runs strict span-resolution then `createThread` per finding. No server-side doc mutation. **This is the main net-new wiring** (replaces the design's original "multi-edit batch redline" — which was moot: `ComposeParagraphRedlineSynthesizer` already batches, and redlines are now user-driven) |
| C5 citation verification | **REUSE** | `Services/Ai/CitationVerification/GroundingVerifier.cs` (zero-LLM mechanical), `CitationSafetyCheck` | none |
| C6 per-section Draft Alternative | **ACTIVATE** | `compose-draft-alternative.action.json` (single-edit, exactly the flow) + `ComposeAiToolbar.tsx` toolbar tool | Resolve `bindingId:''` stub → real GUID; point at company standard |
| C6b Summary Page in DOCX | **BUILD — small, no new package** | `ComposeShadowPatchEngine.cs`/`ComposeDocumentRenderer.cs` (live server DOCX authoring; OpenXML). **`DocxAnnotationWriter.cs` is RETIRED — do not target it** | **NEW** section-insert method: prepend/append a summary section + page break |
| C7 Assistant summary + Compose review panel | **ACTIVATE + BUILD (panel)** | Assistant render exists; `ComposeCommentThread`/`ComposeFindReplace` docked-panel pattern to copy | Concise cited-bullet summary in Assistant; add a review-summary docked panel in Compose (no widget) |
| C8 model-tier selection | **COMPLETE — wire dead-ended field** ⚠ | `AiModelTier{Fast,Standard,Reasoning}`, `sprk_modeltier`(Action)/`sprk_modeltieroverride`(Binding)/`EffectiveModelTier` all EXIST but unread; `ActionRunner.cs:132-139` hardcodes `model:null` → global `gpt-4o-mini`; per-Action **temperature** is the proven wired pattern to mirror | Add tier→deployment resolver + `StandardModel`/`ReasoningModel` in `DocumentIntelligenceOptions` + appsettings; add `ModelTier` to `AnalysisAction` record; change `ActionRunner` `model:null`→resolved. **Infra**: provision a reasoning-class Azure deployment |
| C9 standard-terms summary (UC3) | **REUSE** | `SUM-CHAT@v1` Action + `KnowledgeRetrievalHandler` | Point a summary binding at the standard |
| C10 routing (card + NL) + classify | **ACTIVATE** | `QuickActionChips.tsx`, `useChatFileAttachment.ts`, `SessionDispatchOrchestrator` (resolves Binding by id); `document_uploaded` event path + `chat-classify` exist | Add "Review NDA" card → resolve Binding GUID; NL intent → same binding; classify upload as NDA |
| C11 execution trace | **ACTIVATE** | `ExecutionTraceWidget.tsx`, `ComposeTraceHost.tsx` (built; empty) | Resolve compose action `bindingId:''` stubs so trace events flow |
| C12 memory | **DEFER** | `MemoryCompositionService.cs` dark | Out of r1 (Memory Activation project) |
| C13 draft NDA | **DEFER** | — | Out of r1 (separate project) |

**Net-new CODE, honestly scoped**:
1. **Advisory-Comments wiring (C4)** — one new pane/SSE event + one receiver branch; reuses existing comment primitives. *Client-side, low risk.*
2. **Model-tier last-mile (C8)** — resolver + payload plumbing + one `ActionRunner` line; catalog half already built (ADR-039-frozen). *Server, low risk; the real dependency is provisioning the deployment.*
3. **Summary Page writer (C6b)** — small OpenXML addition, no new package. *Server, low risk.*
4. **Review-summary docked panel (C7)** — small net-new panel inside Compose on the `ComposeCommentThread` convention. *Client.* Plus **right-gutter comment layout** (#6) and the **comment-export wiring fix** (#7).

**⚠ Two original-audit corrections captured here**: (a) the "extend `ComposeParagraphRedlineSynthesizer` to multi-edit" work **does not exist** — it already batches, and the refined flow doesn't auto-apply redlines at all; (b) `compose-compare-to-playbook` is **single-clause + informational**, so it is not the whole-doc, doc-annotating spine the original design implied — the review is authored as a whole-doc advisory Action + the new comment-materialization wiring.

---

## Lens 5 — Configuration

**Actions** (`sprk_analysisaction`):
- **`NDA-REVIEW`** — **prompted, advisory tier**; whole-document analysis prompt adapted from Mike `nda-review` (MIT) + the Spaarke Baseline NDA Standard (Parts A–B); output schema `{ overallRisk: enum(Low|Medium|High|Critical), flaggedSections: [{sectionRef, issue, severity, advisory, standardClauseRef}] }`; **`sprk_modeltier = Reasoning`**; temperature raised from the comparison default to permit advisory depth (still cited, not fabricated). Feeds: the Assistant summary, the Compose review-summary panel, and the `compose_advisory_comments` event.
- **`NDA-STANDARD-SUMMARY`** (or reuse `SUM-CHAT@v1`) — **prompted, Fast tier** over the standard (UC3).
- Per-section rewrite reuses the **existing `compose-draft-alternative`** Action (no new Action) — pointed at the company standard.

**Model-tier wiring** (the C8 enabler — this project pulls it through first for the whole program):
- `AiModelTier` deployments configured in `DocumentIntelligenceOptions` + appsettings (`StandardModel`, `ReasoningModel`); tier→deployment resolver; `AnalysisAction.ModelTier` plumbed; `ActionRunner` consumes it. **Assistant-runtime model picker (in r1, owner 2026-07-25)**: a user-facing tier control rides the same resolver via `sprk_modeltieroverride` (Binding), overriding the Action-declared tier for the next invocation; default = the Action's Reasoning tier when unset. No second routing surface (override travels the dispatch/Binding path).

**Bindings** (`sprk_playbookconsumer`):
- `nda-review/default` → `NDA-REVIEW`, disposition surfaces to Assistant + Compose review panel + `compose_advisory_comments`; risk **ConfirmWhenUncertain**; surfaces workspace + chat; event `document_uploaded` (conditional on NDA classification) + chip.
- `nda-standard-summary/default` → summary Action, disposition **Informational**.
- `compose-draft-alternative` binding → resolve the toolbar `bindingId:''` stub.

**Tools / capability grants**: `document.*`, `search.*` (reference retrieval), `verify_citations`; grant an `nda_review` capability to gate these bindings.

**Knowledge / reference docs**:
- **Spaarke Baseline NDA Standard v0.1** → [`notes/spaarke-nda-standard-baseline.md`](notes/spaarke-nda-standard-baseline.md) — 16-clause rubric (Part B) + required-terms (Part C) + plain-language summary (Part D). Seed Parts A–C into `spaarke-rag-references` via `/add-reference-to-index`; embed Parts A–B in the review Action prompt. **Baseline — counsel ratification pending.**
- Existing KNW clause libraries (KNW-001…010) already indexed — reuse for grounding.

**Review-summary panel**: a docked panel inside `ComposeEditor` (mirroring `ComposeCommentThread`), or a `ComposeWorkspace` `bannerStack` region — no separate widget.

**RAG grounding**: reuse `spaarke-rag-references` (populated; KNW-002 NDA checklist already indexed). Seed the baseline standard (Parts A–C) as a **new reference source with a stable non-colliding ID** (e.g. `KNW-011`, `documentType: legal`, NDA keywords) via `/add-reference-to-index`; retrieve at runtime via `ReferenceRetrievalService` → `AiAnalysisNodeExecutor` (`KnowledgeRetrievalConfig`). **PIN**: reference docs are seeded under `tenantId="system"`; NDA-REVIEW's execution tenant must match or grounding returns zero — verify first.

**SPE save + versioning**: inherited from Compose (`ComposeService.SaveAsync` → `SpeFileStore`, SharePoint version history) — no new save code.

**Export fidelity**: comments bake into the DOCX as native `w:comment` (`ComposeShadowPatchEngine.ApplyComment`) — fix the client save to send `ComposeAnchoredComment` in the `comments` field. Highlights = comments-as-highlight (no separate persistent highlight mark in r1).

**License attribution**: `NDA-REVIEW` prompt adapted from Mike OSS (`nda-review`, **MIT**) — retain attribution in Action metadata. Standard positions informed by Bonterms & Common Paper Mutual NDAs (**CC BY 4.0**).

---

## Lens 6 — Acceptance & Evaluation

**Closed review test set** (≥6 NDAs):
1. Compliant mutual NDA → **Low** risk, ≤1 flag, minimal advisory.
2. Unilateral NDA missing standard carve-outs → flagged "missing carve-outs" + advisory to insert them.
3. NDA with uncapped liability / non-standard indemnity → **High/Critical**, advisory to narrow.
4. NDA with overlong term / no return-destruction clause → flags + advisory.
5. NDA from the counterparty's perspective → correct perspective-aware findings.
6. Well-drafted NDA with only a drafting error (broken cross-ref) → catches the mechanical error.

**Negative / authorization cases** (≥1 each — required):
- **Wrong doc type**: upload a lease/invoice → classification detects non-NDA and declines (no fabricated review).
- **Insufficient/unreadable**: scanned image PDF with no extractable text → clear error, no hallucinated review.
- **Authorization**: user without the `nda_review` capability → capability-gated refusal.

**Advisory-quality bar (north-star)**: on the test set, the review output is judged **at least as useful/deep as a strong general LLM** (Claude/ChatGPT) given the same NDA + standard — the explicit success bar. Encode as a rubric-scored eval (usefulness + correctness + citation coverage), acknowledging its judgment nature.

**Summary-Page acceptance**: the exported DOCX contains a Summary Page (TL;DR + flagged overview + recommendations) that is concise and consistent with the in-document Comments.

**Eval harness**: encode the above in `legal-eval-config.yaml`; run `metrics/citation_accuracy.py` for citation grounding. **Every factual finding must cite its NDA section.** Per ADR-039, catalog/prompt changes must be covered by the golden-utterance eval suite (dispatch regressions block merge).

---

## Governance Seeds (for design-to-spec handoff)

### Hot-Path Declaration (CLAUDE.md §10)
```xml
<hot-path-declaration>
  <bff>Y</bff>          <!-- NDA-REVIEW Action, model-tier wiring in Services/Ai, Summary-Page writer in Services/Compose -->
  <spaarkeai>Y</spaarkeai> <!-- "Review NDA" card, Compose review-summary panel + gutter, advisory-Comments receiver, comment-export fix -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y → Placement Justification required per new server surface; ≤60 MB publish-size check per task. **No new NuGet expected** (reuses AI/Compose/OpenXML stack). The model-tier deployment is infra/appsettings, not a package.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `NDA-REVIEW` Action (row) | `infra/dataverse/actions/*.action.json`, `ActionRunner` | This IS an extension of the Action model (new data row, not new code) | Without it the NDA use case has no advisory review — the whole vertical fails |
| Advisory-Comments wiring (`compose_advisory_comments` event + receiver) | `useComposeWorkspaceReceivers.ts` (3 flows), `useComposeCommentThreads.createThread`, `resolveTargetSpans` | **Extend** the receiver + reuse comment primitives; add one event | Without it, review findings can't become in-document Comments; UC1's core Compose output fails |
| Model-tier last-mile (resolver + payload plumbing) | `AiModelTier`, `sprk_modeltier`, `EffectiveModelTier` (exist, unread); per-Action `temperature` (wired) | **Extend/wire** the existing dead-ended field; mirror the temperature path | Without it the advisory review runs on `gpt-4o-mini` — the north-star "better than Claude/ChatGPT" bar cannot be met |
| Runtime model picker (Assistant tier control) | `sprk_modeltieroverride` (exists, unread); Assistant chrome | **Extend** — reuse the same resolver + override field | Without it users can't dial reasoning depth per invocation (owner r1 requirement) |
| Summary-Page DOCX writer | `ComposeShadowPatchEngine.cs`/`ComposeDocumentRenderer.cs` (OpenXML; no page/section insert). **NOT** retired `DocxAnnotationWriter` | **Add** a section-insert method; reuse OpenXML | Without it the summary doesn't travel with the exported document (owner-requested deliverable) |
| Review-summary docked panel (in Compose) | `ComposeCommentThread`/`ComposeFindReplace` docked-panel pattern; `ComposeWorkspace` `bannerStack` | **Extend** — new sibling panel on the proven convention | Without it, risk rating + flagged list have nowhere to render on the single Compose surface |
| Right-gutter comment layout | `ComposeCommentThread` (stacked list), `CommentAnchorMark`, `coordsAtPos` (unused), `TrackChangesExtension` widget decorations | **Extend** the comment UI — right-rail + live-pos resolution | Comments stay a top list unaligned to clauses — poor review UX (owner #6) |
| Comment-export wiring fix | `ComposeShadowPatchEngine.ApplyComment` (works/tested), `SaveComposeDocumentBody.comments` | **Fix** the broken client field/shape | Advisory comments silently dropped on save — export loses them (owner #7) |
| Seeded NDA-standard reference source | `spaarke-rag-references`, `/add-reference-to-index`, KNW-002 | **Extend** — one new reference source (data) | No company-standard grounding → review can't compare against the standard |
| "Review an NDA" card | `QuickActionChips.tsx` (chip framework) | **Extend** (add card + resolve Binding GUID) | Without it, non-lawyers can't launch review from the Assistant (NL still works) |

*(No net-new services/entities. No coded workflow required: the review is one whole-doc Action; comments/summary-page are dispositions/writers, not orchestration — so ADR-039's "composites must be coded workflows" MUST is not triggered.)*

### Platform-Enabler Flag (demand-pull discipline)
This vertical **pulls through, first, three shared capabilities** later verticals inherit — adopt-and-harden here:
- **Model-tier selection** (roadmap #9 / r8) — wire the dead-ended `sprk_modeltier`→deployment path. First real consumer.
- **Tiered-governance ADR-039 amendment** (roadmap r8 / §6.5 Path B) — the deterministic-vs-advisory tier. This project authors it (see §6).
- **Advisory-Comments materialization** + **execution-trace activation** + compose `bindingId` stub resolution — shared Compose plumbing.
- **Memory (C12)** — **deferred** to the Memory Activation project; not pulled here.
- No scheduler / `IGateResolver` / `sprk_analysis` table needed for r1 (interactive, single-doc).

### ADR Tensions (CLAUDE.md §6.5)
| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039** (grounded execution invariant #3) | "every platform output is prompt-controlled + schema-validated (a) or cited tool-composed (b); free-form completion untethered from (a)/(b) has no code path" — read strictly as verbatim-grounded, low-temperature | The advisory tier needs **full-model reasoning depth** (higher temperature, generative recommendations) to beat a general LLM. Strict verbatim-grounded reading blocks the north star | **B — amendment** | Refine invariant #3 into **two grounding tiers**: **fact/deterministic** (assertions about doc/data content → verbatim-grounded + citation-verified, unchanged) and **advisory/probabilistic** (reasoned recommendations → full model reasoning permitted, still Action-**prompt-controlled** (so it stays within 3(a)), factual claims cited, marked "not legal advice", nothing fabricated). The advisory tier remains inside ADR-039's "prompt-controlled" spine — this **refines, does not break** the invariant. Authored by this project; generalized for the program (roadmap r8) |
| ADR-016 (model/budget) | model tier is a "deferred enhancement" | This project needs the Reasoning tier now | **C — comply by completing** | Wire the already-designed `sprk_modeltier` path (in-code comment already anticipates it); no ADR change, just the deferred implementation |
| — | UC1/UC2 auto-apply concerns | No tension: redlines are **user-driven**; factual findings cite | comply | — |

### Assumptions (proceeding unless corrected)
- **Company NDA standard**: `NDA-REVIEW` is bootstrapped from the **Spaarke Baseline NDA Standard v0.1** (`notes/spaarke-nda-standard-baseline.md`); counsel ratification refines it (checklist in that doc). Not a blocker.
- **Default posture**: mutual NDA; represented party = requester's company; residuals disfavored (B10); governing law/forum set at ratification.
- **Advisory surface**: **Compose is the single surface**; the review summary renders in an in-Compose docked panel (no separate widget).
- **Summary-Page placement**: default **end of document** (least disruptive to the original), configurable to start; owner said "either."
- **Model deployment**: a reasoning-class Azure OpenAI deployment will be provisioned + quota'd (infra dependency; the code wiring is in-scope regardless).
- **Memory (C12) deferred**; **UC2 draft deferred**.

## Resolved (owner, 2026-07-25)
- ✅ **ADR-039 amendment = task 001, a first merge gate** before any advisory-tier code (so downstream tasks don't trip adr-check/code-review on the relaxed-grounding output). Path-B mechanics: concise + full amendment authored, adr-check/code-review approved, `.claude/CHANGELOG.md` updated, merged before dependent code.
- ✅ **Runtime model picker is in r1** — Assistant-side tier control via `sprk_modeltieroverride`, with the Action-declared Reasoning tier as the default.

## Unresolved Questions (answer before/with implementation)
- [ ] **Counsel ratification of the baseline standard** — confirm per-clause positions + severities; set defaults (governing law/forum, term lengths, residuals). *Refinement, not a blocker.*
- [ ] **Grounding tenant strategy**: confirm NDA-REVIEW's execution tenant matches the `tenantId="system"` seeded reference docs (else zero grounding). *Confirm during the RAG task.*

---
*Design produced by use-case-to-design (6-lens); refined 2026-07-25 via design-to-spec. Next: `/design-to-spec ai-advanced-capabilities-nda-r1` → spec.md.*
