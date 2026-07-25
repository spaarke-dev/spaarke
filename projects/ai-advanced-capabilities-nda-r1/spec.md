# NDA Review & Analysis (Advisory Vertical) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-25
> **Source**: `design.md` (use-case-to-design 6-lens; scope refined 2026-07-25 by owner)
> **Program**: ai-advanced-capabilities-development — use-case vertical #1 (first repeatable advisory pattern)

## Executive Summary

A non-lawyer uploads an NDA into the SpaarkeAi Assistant and receives a full, standards-based **advisory** review: an overall risk rating, a concise cited flagged-section summary in the Assistant, in-document advisory Comments on each flagged clause in the Compose editor, user-driven per-section rewrites using company standards, and an exported Word document carrying a generated Summary Page. This is the program's **first "analysis/advisory" vertical** and its **north star is Claude/ChatGPT-level generative advisory output** — deliberately less constrained by Spaarke's deterministic guardrails, while remaining accurate, cited, and never fabricated. It carries two program-wide enablers: the **ADR-039 deterministic-vs-advisory tier amendment** and the **model-tier selection** wiring.

## Scope

### In Scope
- **UC1 — Review & advise**: whole-document NDA analysis against the company standard on the **Reasoning** model tier → risk rating + flagged-section summary (Assistant + Compose review-summary panel) + in-document advisory Comments (right-gutter) + user-driven per-section rewrites + Summary Page + comment-baked Word export.
- **UC3 — Standard summary**: plain-language summary of required NDA terms (Fast tier).
- **ADR-039 amendment (Path B)**: deterministic (fact) vs advisory (probabilistic-but-accurate) grounding tiers.
- **Model-tier selection wiring**: activate the dead-ended `sprk_modeltier` → Azure deployment path; NDA-REVIEW runs on Reasoning; **plus a runtime model picker in the Assistant** (`sprk_modeltieroverride`) — in r1.
- **Advisory-Comments materialization**: new `compose_advisory_comments` event + receiver rendering Comments into the live Compose doc.
- **Summary-Page DOCX writer**: prepend/append a concise summary section (TL;DR + flagged overview + recommendations).
- **Compose is the single surface** for the whole review → revise → draft flow. The review summary + flagged-section list render in a **review-summary docked panel inside Compose** (following the existing `ComposeCommentThread`/`ComposeFindReplace` docked-panel convention). **No dedicated Analysis widget in r1** (fork one later only if needed).
- **Right-side comment gutter** — advisory comments render in a right-rail gutter aligned to their anchored clauses (Tiptap/ProseMirror positioning), not a stacked list on top.
- **Export bakes native Word comments** onto flagged clause ranges (comments-as-highlight); saved documents inherit **SPE save + SharePoint versioning** (existing Compose behavior).
- **"Review an NDA" card** + NDA upload classification + routing (Click + Text paths).
- **NDA standard content** seeded into `spaarke-rag-references` + embedded in the review prompt.

### Out of Scope
- **UC2 — draft a fresh NDA from parameters** → deferred to a separate project. (The per-section "Draft Alternative" rewrite inside UC1 stays — it reuses the existing single-selection tool.)
- **Memory (C12)** cross-NDA recall → deferred to the program Memory Activation project.
- Mass/tabular multi-NDA review → future `-tabular` project.
- Non-NDA contract types; negotiation-loop automation; general legal advice (every output marked "not legal advice").
- AI **auto-applied** batch redlines (superseded — redlines are user-driven per-section).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs` — model-tier last-mile (replace `model:null` ~L132-139).
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` (+ `AnalysisAction` record) — add/populate `ModelTier`.
- `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` (+ appsettings) — `StandardModel`/`ReasoningModel` deployments + tier→deployment resolver.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeShadowPatchEngine.cs` + `ComposeDocumentRenderer.cs` — Summary-Page insertion (OpenXML page break + prepend/append section). **NOTE: `DocxAnnotationWriter.cs` is RETIRED — do not target it.** These two engines are the live server DOCX authoring path; `ComposeShadowPatchEngine.ApplyComment` (`:664`) is the native `w:comment` writer, `DocxAnnotationReader.cs` the round-trip reader.
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` (`SaveComposeDocumentBody` ~`:1854-1902`) — the save body reads `comments` as `ComposeAnchoredComment`; the client comment-export wiring fix targets this contract.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` + `ComposeEditor.tsx` — review-summary docked panel, right-gutter comment layout, and the comment-export save fix (send `ComposeAnchoredComment` in `comments`, not `annotations`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/useComposeWorkspaceReceivers.ts` — new `compose_advisory_comments` branch (reuse `useComposeCommentThreads.createThread` + `resolveTargetSpans('strict')`).
- `infrastructure/ai-search/spaarke-rag-references.json` (index) + `/add-reference-to-index` skill / `scripts/ai-search/Add-ReferenceToIndex.ps1` — seed the NDA standard as a new reference source; `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` (`KnowledgeRetrievalConfig`, `RetrieveReferenceKnowledgeAsync`) — runtime grounding.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/QuickActionChips.tsx` + chip stubs — "Review an NDA" card.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeAiToolbar.tsx` — resolve `bindingId:''` for `compose-draft-alternative`.
- `infra/dataverse/actions/` + `infra/dataverse/sprk_playbookconsumer-rows.json` + input/output schemas — `NDA-REVIEW`, `NDA-STANDARD-SUMMARY`, bindings.
- `docs/adr/ADR-039-*.md` + `.claude/adr/ADR-039-*.md` — the tier amendment.
- `spaarke-rag-references` index — NDA standard docs.

## Requirements

### Functional Requirements

> **FR-00 (ADR-039 amendment — FIRST GATE, BINDING SEQUENCING)**: The ADR-039 deterministic-vs-advisory tier amendment (concise `.claude/adr/ADR-039-*.md` + full `docs/adr/ADR-039-*.md`) MUST be authored, `adr-check`/`code-review`-approved, and merged **before any advisory-tier code task begins**. It is **task 001** and a hard merge gate — no NDA-REVIEW Action, model-tier, advisory-Comments, or Analysis-widget task may merge ahead of it. — Acceptance: both ADR files carry the two-tier grounding refinement; `.claude/CHANGELOG.md` updated; adr-check treats advisory-tier output as compliant only under the amended invariant. Rationale: without it, every downstream advisory-tier task trips ADR-039's strict grounded-output rule at review (owner directive 2026-07-25).

1. **FR-01 (Classification + card)**: When a user uploads a document to the Assistant, the system classifies whether it is an NDA and, if so, offers a "Review an NDA" action card. — Acceptance: uploading an NDA surfaces the card; uploading a lease/invoice does NOT and yields a non-NDA decline (no fabricated review).
2. **FR-02 (Routing, both paths)**: "Review an NDA" (Click) and NL "review this NDA" (Text) both resolve to the same `nda-review/default` Binding. — Acceptance: both entry paths dispatch the NDA-REVIEW Action via `SessionDispatchOrchestrator`; ledger records `{bindingId}@t{n}`.
3. **FR-03 (Open in Compose + whole-doc review)**: Selecting the card opens the file in the Compose tab and runs a **whole-document** advisory analysis. — Acceptance: the review consumes the full document text (not a single selection) and returns `{overallRisk, flaggedSections[]}`.
4. **FR-04 (Reasoning model tier)**: NDA-REVIEW runs on the **Reasoning** deployment, declared via `sprk_modeltier` on the Action row. — Acceptance: the request to Azure OpenAI uses the Reasoning deployment, not the global `gpt-4o-mini`; verified by a resolver unit test + an integration assertion.
4b. **FR-04b (Runtime model picker — in r1)**: The Assistant exposes a user-facing control to select/change the model tier at runtime, overriding the Action-declared tier via `sprk_modeltieroverride` (Binding) through the same tier→deployment resolver. — Acceptance: selecting a tier in the Assistant changes the deployment used for the next review invocation; the override rides the dispatch/Binding path (no second routing surface); default remains the Action-declared Reasoning tier when the user makes no selection.
5. **FR-05 (Concise cited Assistant summary)**: The Assistant shows a short, bullet-point key-terms summary with an overall risk rating; **each bullet carries a page/section/paragraph reference**; no long prose. — Acceptance: summary renders as bullets with section refs; every factual bullet cites a section.
6. **FR-06 (In-document advisory Comments — client-event path)**: Each flagged clause is annotated in the Compose doc with a Comment whose text is the AI advisory explanation. The ledgered review Action output is emitted as a lightweight client pane event (`compose_advisory_comments`, modeled on `compose_qa_highlight`) and a receiver branch in `useComposeWorkspaceReceivers.ts` materializes comments by reusing `useComposeCommentThreads.createThread` + `resolveTargetSpans(...,'strict')`. No new routable server disposition. — Acceptance: N findings → N comment threads anchored to the correct spans via strict span-resolution; a finding whose text can't be strictly located is reported, not mis-anchored (do-not-guess); the Action output is present in the ADR-040 ledger.
7. **FR-07 (Review-summary panel — inside Compose, single surface)**: A **review-summary docked panel in Compose** (following the `ComposeCommentThread`/`ComposeFindReplace` docked-panel convention, or a `ComposeWorkspace` `bannerStack` region) presents overall risk + the flagged-section list + advisory reasoning; clicking a finding navigates to that clause in the doc. **No separate Analysis widget in r1.** — Acceptance: the panel renders findings within the Compose surface; click-to-navigate scrolls/selects the target clause; review, comments, and Draft Alternative all operate on the one Compose surface.
8. **FR-08 (Per-section Draft Alternative)**: The user selects a flagged section and uses the existing embedded "Draft Alternative" Compose tool to rewrite it using company standards. — Acceptance: the `compose-draft-alternative` toolbar action is enabled (Binding GUID resolved) and produces a standards-based rewrite for the selection.
9. **FR-09 (Summary Page)**: On export, a Summary Page (TL;DR + flagged-section overview + recommendations) is inserted into the Word document. — Acceptance: exported DOCX contains a distinct summary section (default at end, configurable to start) with a page break; content is concise and consistent with the in-doc Comments.
10. **FR-10 (UC3 standard summary)**: NL "what are our required NDA terms?" returns a plain-language summary of the standard on the Fast tier. — Acceptance: summary reflects the standard's required-terms set (Part C/D).
11. **FR-11 (Citation verification)**: Every factual finding is citation-verified against the source document. — Acceptance: `GroundingVerifier` runs; unverifiable factual claims are flagged/declined, not asserted.
12. **FR-12 (Execution trace)**: Review steps stream live to the Context pane. — Acceptance: trace shows read/retrieve/flag/verify steps (compose `bindingId:''` stubs resolved so events flow).
13. **FR-13 (Authorization)**: The `nda-review` capability gates the bindings. — Acceptance: a user without the capability receives a capability-gated refusal + `dispatch_refused` telemetry.
14. **FR-14 (Uncertainty state)**: Low-confidence findings render as an explicit "couldn't confirm — recommend human review" state, not a false-confident pass. — Acceptance: a low-confidence case produces a decline/review marker rather than a green.
15. **FR-15 (Save to SPE + versioning — inherited)**: Saving the reviewed NDA persists to SharePoint Embedded with SharePoint version history (existing Compose behavior — `ComposeService.SaveAsync` → `SpeFileStore`). — Acceptance: a save creates a new SPE driveItem version and prior versions remain addressable; the NDA feature uses the existing `/api/compose/documents/{id}/save` path unchanged (verify, no new save code). Note: concurrency is the existing Compose Redis save-stamp + re-anchoring, not Graph `If-Match`.
16. **FR-16 (Right-gutter comments aligned to edits)**: Advisory comments render in a right-side gutter aligned to their anchored clauses (Word/Google-Docs style), leveraging Tiptap/ProseMirror positioning — not a stacked list on top. — Acceptance: each comment card sits at the vertical position of its anchored clause via live-position resolution (`coordsAtPos`) with collision/stacking; anchors resolve from the live `commentAnchor` mark (not the stored `anchorText` alone); reuses the proven widget-decoration approach (`TrackChangesExtension`).
17. **FR-17 (Export bakes native comments)**: Exporting/saving the document bakes the advisory comments into the DOCX as native `w:comment` on the flagged clause ranges, so they survive export and round-trip back on reopen. — Acceptance: the client save sends comments as `ComposeAnchoredComment` in the `comments` field (fixing the current broken path that sends `annotations`/`DocxAnnotationInput` and is silently dropped); `ComposeShadowPatchEngine.ApplyComment` writes them; `DocxAnnotationReader` reads them back on reopen. **Highlights = comments-as-highlight** (the commented range is the highlight; no separate persistent highlight mark in r1).

### Non-Functional Requirements
- **NFR-01 (Advisory quality — north star)**: On the closed test set, review output is judged at least as useful and deep as a strong general LLM (Claude/ChatGPT) given the same NDA + standard. — Verify via rubric-scored eval (usefulness + correctness + citation coverage).
- **NFR-02 (Grounded-but-advisory)**: Factual assertions are verbatim-grounded + cited (deterministic tier); advisory reasoning may use full model depth but stays Action-prompt-controlled, cited, and marked "not legal advice"; nothing fabricated. — Verify per ADR-039 amendment + citation eval.
- **NFR-03 (Publish size)**: BFF publish output stays ≤60 MB compressed; report absolute + delta per BFF-touching task. No new NuGet expected.
- **NFR-04 (Eval gate)**: Catalog/prompt changes are covered by the golden-utterance eval suite (dispatch regressions block merge, ADR-039/ADR-038).
- **NFR-05 (No new intent mechanism / routing surface)**: Routing stays on the Binding table + the three entry paths; no second intent detector, no config-file routing (ADR-039 MUST NOT).
- **NFR-06 (Grounding tenant pin — verify FIRST)**: Golden reference docs in `spaarke-rag-references` are seeded under `tenantId = "system"`, but `ReferenceRetrievalService` filters by the execution `context.TenantId`. NDA-REVIEW MUST resolve a tenant value that matches the seeded docs, or grounding silently returns **zero** chunks. — Verify: an integration test asserts non-empty reference retrieval for a seeded NDA-standard query under NDA-REVIEW's actual execution tenant; the tenant strategy is documented before relying on grounding. (Also: filter on `documentType`, not `domain`.)

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution & closed catalogs) — **amended by this project** (deterministic vs advisory tiers); three entry paths, Binding-only routing, eval-gated.
- **ADR-040** (session ledger) — write output before render; `{bindingId}@t{n}`.
- **ADR-016** (model/budget) — model-tier is the "deferred enhancement" this project completes.
- **ADR-013** (AI facade boundary) — CRUD↔AI via `Services/Ai/PublicContracts/`.
- **ADR-038** (testing strategy) — eval suite is KEEP-class `tests/integration/contract/**`; no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests.
- **ADR-032** (kill-switch) — if any capability is feature-gated, use Null-Object.

### MUST Rules
- ✅ MUST route the review via the Click and Text entry paths only; resolve by Binding id.
- ✅ MUST cite every **factual** finding to a document section; citation-verify before asserting.
- ✅ MUST keep advisory output **prompt-controlled** (via the Action) and marked "not legal advice"; MUST NOT fabricate facts/citations.
- ✅ MUST declare the model tier on the Action row (`sprk_modeltier`) and resolve it to a deployment at execution.
- ✅ MUST classify uploads and decline non-NDA documents (no fabricated review).
- ✅ MUST cover catalog/prompt changes with golden-utterance eval cases.
- ❌ MUST NOT add a second intent-detection mechanism or a routing surface outside the Binding table.
- ❌ MUST NOT auto-apply redlines — redlines are user-driven via Draft Alternative.
- ❌ MUST NOT inject AI-internal types into CRUD code (use the PublicContracts facade).

### Existing Patterns to Follow
- Per-Action **temperature** wiring (`sprk_temperature` → `AnalysisAction.Temperature` → `ActionRunner` → client) is the **proven pattern to mirror for model tier**.
- `useComposeCommentThreads.createThread(text, range)` + `importThreads` + `resolveTargetSpans(editor, sourceText, 'strict')` for comment materialization; `useComposeWorkspaceReceivers.ts` for the receiver branch (existing flows: `compose_context_insert`, `compose_assistant_insert`, `compose_qa_highlight`).
- `ComposeCommentThread`/`ComposeFindReplace` docked-panel convention in `ComposeEditor.tsx` for the review-summary panel; `TrackChangesExtension` widget-decoration + `coordsAtPos` for the right-gutter comment layout.
- `ComposeShadowPatchEngine.cs` / `ComposeDocumentRenderer.cs` OpenXML authoring (native `w:comment` via `ApplyComment`, page break + prepend/append) for the Summary Page and comment baking — **NOT** the retired `DocxAnnotationWriter.cs`.
- Save path: `ComposeService.SaveAsync` → `SpeFileStore` (SPE + SharePoint versioning) — inherited; do not add a new save path.
- RAG grounding: `spaarke-rag-references` index + `/add-reference-to-index` ingest (`.md` metadata-header contract, `sprk_analysisknowledges` RagIndex record) + `ReferenceRetrievalService` → `AiAnalysisNodeExecutor` `KnowledgeRetrievalConfig`; `KNW-002` NDA checklist already indexed.
- `DailyBriefingNarrator` is the coded-workflow precedent **only if** orchestration proves necessary (not expected — see §11).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y → each BFF-touching task states a Placement Justification citing `.claude/constraints/bff-extensions.md` and reports publish size (≤60 MB). No new NuGet expected (reuses AI/Compose/OpenXML stack; the Reasoning deployment is infra/appsettings).

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `NDA-REVIEW` Action (row) | `infra/dataverse/actions/*.action.json`, `ActionRunner` | IS an extension of the Action model (data row) | No advisory review → entire vertical fails |
| Advisory-Comments wiring (`compose_advisory_comments` event + receiver branch) | `useComposeWorkspaceReceivers.ts`, `useComposeCommentThreads`, `resolveTargetSpans` | Extend receiver + reuse comment primitives; add one event | Findings can't become in-doc Comments → UC1 core Compose output fails |
| Model-tier last-mile (resolver + `AnalysisAction.ModelTier` + `ActionRunner` line) | `AiModelTier`, `sprk_modeltier`, `EffectiveModelTier` (exist, unread); per-Action temperature (wired) | Wire the dead-ended field; mirror temperature | Review runs on `gpt-4o-mini` → north-star quality bar unreachable |
| Runtime model picker (Assistant tier control → `sprk_modeltieroverride`) | `sprk_modeltieroverride` (exists, unread); Assistant chrome | Extend — reuse the same resolver + override field | Users can't dial reasoning depth per invocation (owner r1 requirement) |
| Summary-Page DOCX writer | `ComposeShadowPatchEngine.cs`/`ComposeDocumentRenderer.cs` (OpenXML; no page/section insert) — **NOT** retired `DocxAnnotationWriter` | Add a section-insert method; reuse OpenXML | Summary doesn't travel with exported doc (owner deliverable) |
| Review-summary docked panel (in Compose) | `ComposeCommentThread`/`ComposeFindReplace` docked-panel pattern; `ComposeWorkspace` `bannerStack` | **Extend** — new sibling panel on the proven convention; no new surface | Without an in-Compose summary the risk rating + flagged list have nowhere to render on the single surface |
| Right-gutter comment layout | `ComposeCommentThread` (stacked list), `CommentAnchorMark`, `coordsAtPos` (unused), `TrackChangesExtension` widget decorations | **Extend** the comment UI — add right-rail + live-pos resolution | Comments stay a top list unaligned to clauses — poor review UX (owner requirement #6) |
| Comment-export wiring fix | `ComposeShadowPatchEngine.ApplyComment` (works/tested), `SaveComposeDocumentBody.comments` | **Fix** the broken client field/shape (not new server code) | Advisory comments are silently dropped on save — exported DOCX loses them (owner requirement #7) |
| Seeded NDA-standard reference source | `spaarke-rag-references`, `/add-reference-to-index`, KNW-002 | **Extend** — add one new reference source (data, not code) | No company-standard grounding → review can't compare against the standard |
| "Review an NDA" card | `QuickActionChips.tsx` | Extend (card + resolve Binding GUID) | Non-lawyers can't launch review from the Assistant |

*No net-new services/entities. **No coded workflow required**: the review is a single whole-doc Action; comments and the summary page are dispositions/writers, not multi-Action orchestration — so ADR-039's "composites MUST be coded workflows" rule is not triggered. If review orchestration later grows (e.g., segment→multi-Action→assemble), promote to `NdaReviewWorkflow : ICodedWorkflow` per ADR-039 and re-file this row.*

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039** invariant #3 (grounded execution) | "every output is prompt-controlled+schema-validated (a) or cited tool-composed (b); free-form untethered completion has no code path" — strict verbatim/low-temp reading | The advisory tier needs full-model reasoning depth to beat a general LLM; a strict verbatim reading blocks the north star | **B — amendment** | Refine invariant #3 into **two grounding tiers**: **fact/deterministic** (assertions about doc/data → verbatim-grounded + citation-verified; unchanged) and **advisory/probabilistic** (reasoned recommendations → full model reasoning; still Action-**prompt-controlled** ⇒ stays inside 3(a); factual claims cited; marked "not legal advice"; nothing fabricated). Refines, does not break, the invariant. This project authors the concise + full amendment; the program (r8) generalizes it. |
| **ADR-016** | model tier a "deferred enhancement" | Reasoning tier needed now | **C — comply by completing** | The `sprk_modeltier` path is already designed (an in-code comment anticipates it); this project implements the deferred last-mile — no ADR change. |
| — | Auto-apply redlines | None | comply | Redlines are user-driven; factual findings cite. |

## Success Criteria
1. [ ] Non-lawyer flow works end-to-end unaided: upload → classify → card → Compose open → review → Assistant cited summary → in-doc Comments → per-section Draft Alternative → Summary Page → export. — Verify: UAT walkthrough on the test set.
2. [ ] NDA-REVIEW runs on the Reasoning deployment. — Verify: resolver unit test + integration assertion on the deployment name.
3. [ ] Advisory quality ≥ strong general LLM on the closed set. — Verify: rubric-scored eval (NFR-01).
4. [ ] Every factual finding cites its NDA section; unverifiable claims declined. — Verify: `metrics/citation_accuracy.py` + `GroundingVerifier`.
5. [ ] N findings → N correctly-anchored Comments; un-locatable findings reported, not mis-anchored. — Verify: comment-materialization test.
6. [ ] Exported DOCX carries a concise Summary Page with a page break. — Verify: OpenXML export test + visual check.
7. [ ] Negative cases: non-NDA declines; unreadable-PDF errors cleanly; unauthorized user refused with telemetry. — Verify: eval negative/authorization cases.
8. [ ] ADR-039 amendment (concise + full) merged before/with the advisory-tier code. — Verify: adr-check + code-review sign-off.
9. [ ] BFF publish ≤60 MB; golden-utterance eval green. — Verify: publish measurement + `eval-gate` CI job.
10. [ ] Grounding returns non-zero reference chunks under NDA-REVIEW's execution tenant (NFR-06). — Verify: integration test on seeded NDA-standard retrieval.
11. [ ] Save creates a new SPE/SharePoint version; export retains native comments and they round-trip on reopen. — Verify: save-version test + comment round-trip test.
12. [ ] Advisory comments render in a right-side gutter aligned to their anchored clauses. — Verify: UI test on gutter positioning.

## Dependencies

### Prerequisites
- Spaarke Baseline NDA Standard v0.1 (`notes/spaarke-nda-standard-baseline.md`) seeded into `spaarke-rag-references` (Parts A–C) as a **new reference source with a stable, non-colliding source ID** (e.g. `KNW-011`; `documentType: legal`, NDA keywords) via `/add-reference-to-index`; Parts A–B embedded in the review prompt. `KNW-002` (NDA Review Checklist) is already indexed and reused.
- **Grounding tenant pinned** (NFR-06): NDA-REVIEW's execution tenant must match the seeded docs' `tenantId="system"`, or retrieval returns zero — verify before relying on grounding.
- A **reasoning-class Azure OpenAI deployment** provisioned + quota'd (infra); appsettings deployment names.

### External Dependencies
- Mike OSS `nda-review` prompt (MIT — attribution retained); standard positions informed by Bonterms & Common Paper Mutual NDAs (CC BY 4.0 — attribution retained).
- Counsel ratification of the baseline standard (refinement, not a start-blocker).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| North star | How constrained should advisory output be? | Claude/ChatGPT-level generative advisory; deliberately relax deterministic guardrails; accurate + never fabricated | Drives the ADR-039 amendment + Reasoning tier |
| ADR-039 | Exception or amendment? | **Amend** ADR-039 for deterministic vs advisory tiers; this project carries it | Path B; blocks advisory-tier code sign-off |
| Model | How to select the model? | Surface in Dataverse (Action tier) **and** a **runtime picker in the Assistant** — both in r1 | Wire dead-ended `sprk_modeltier` + `sprk_modeltieroverride`; NDA-REVIEW=Reasoning |
| Sequencing | When does the ADR-039 amendment land? | **First — task 001, a merge gate before any advisory-tier code** | FR-00; unblocks all downstream advisory tasks |
| Surface | New Analysis widget, or one surface? | **Compose is the single surface** (review/revise/draft one end-to-end process); fork an Analysis widget later only if needed | Review-summary docked panel in Compose; no widget (FR-07) |
| SPE save | Save + versioning enabled? | Yes — confirm it's standard existing Compose behavior | Inherited via `ComposeService.SaveAsync`→SPE; no new code (FR-15) |
| RAG reuse | Are we reusing existing RAG/golden resources? | Yes — `spaarke-rag-references` + KNW-002 + existing ingest/grounding; seed the standard as content | Reuse-first; pin the `tenantId="system"` grounding gotcha (NFR-06) |
| Comment UI | Where do comments show? | **Right-side gutter aligned to the edits**, leveraging Tiptap/ProseMirror — not on top | Right-rail gutter layout (FR-16) |
| Export fidelity | Keep highlights + comments on export? | Yes — comments-as-highlight (native `w:comment` on the range); no separate highlight mark in r1 | Fix client comment-export wiring (FR-17) |
| Redlines | Auto-applied or user-driven? | AI adds advisory Comments; user drives per-section rewrites via existing Draft Alternative | Removes batch-redline; adds comment-materialization |
| UC2 draft | In r1? | **Deferred to a separate project** | Draft Action/slot-filling out of r1 |
| UC3 summary | In r1? | **Yes** | Fast-tier summary retained |
| Summary Page | New? Placement? | Yes — TL;DR + flagged overview + recommendations; start or end | New DOCX writer; default end, configurable |
| Memory C12 | In r1? | Not raised for inclusion → deferred to Memory Activation project | Out of r1 scope |

## Assumptions
- **Summary-Page placement**: default end of document, configurable to start.
- **Standard**: baseline v0.1 governs review quality until counsel ratification; mutual-NDA default posture.
- **Model deployment** will be provisioned; code wiring is in-scope regardless.
- **Grounding tenant**: NDA-REVIEW runs under (or is pinned to) a tenant value that matches the `tenantId="system"` seeded reference docs (NFR-06); the exact strategy is confirmed during the RAG task.
- **Review-summary panel placement**: a docked panel in `ComposeEditor` (mirroring `ComposeCommentThread`) is the default; a `ComposeWorkspace` `bannerStack` region is the fallback if layout demands.

## Resolved (owner, 2026-07-25)
- ✅ **ADR-039 amendment is task 001, a first merge gate** (FR-00) before any advisory-tier code.
- ✅ **Runtime model picker is in r1** (FR-04b) — Assistant tier control via `sprk_modeltieroverride`, plus the Action-declared Reasoning default.
- ✅ **Compose is the single surface** — review/revise/draft in Compose; review-summary docked panel, not a separate Analysis widget (FR-07).
- ✅ **Advisory-comments = lightweight client-event path** (ledgered Action output → pane event → receiver), not a new routable disposition (FR-06).
- ✅ **Right-gutter comments aligned to edits** are in r1 (FR-16).
- ✅ **Export bakes native comments; highlights = comments-as-highlight**, no separate persistent highlight mark in r1 (FR-17).
- ✅ **SPE save + versioning inherited** (FR-15) — no new save code.
- ✅ **RAG reuse** — `spaarke-rag-references` + existing ingest/grounding; content-seed only; `DocxAnnotationWriter` retired (use `ComposeShadowPatchEngine`/`ComposeDocumentRenderer`).
- ⚠️ **Counsel ratification NOT an r1 blocker** — r1 ships on baseline v0.1 under disclaimer + human-in-the-loop; **owner to confirm business risk posture at review** (§6).

## Unresolved Questions
- [ ] Counsel ratification of per-clause positions/severities + company defaults. — Blocks: standard v1.0 (not the r1 start); owner to confirm r1 risk posture (§6).
- [ ] Grounding tenant strategy for `tenantId="system"` reference docs (NFR-06) — confirm during the RAG task.

---
*AI-optimized specification. Original design: `design.md`. First advisory vertical for ai-advanced-capabilities-development.*
