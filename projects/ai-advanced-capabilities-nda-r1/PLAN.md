# PLAN — NDA Review & Analysis (Advisory Vertical)

> **Project**: `ai-advanced-capabilities-nda-r1` · **Source**: `spec.md` (43 FRs, 6 NFRs) · **Created**: 2026-07-25
> **Program**: ai-advanced-capabilities-development — first analysis/advisory vertical.
> **North star**: Claude/ChatGPT-level advisory output; relax deterministic guardrails (ADR-039 amendment) while staying cited + human-verified.
> **Status**: ✅ COMPLETE (2026-07-28) — all 22 tasks ✅, deployed to spaarkedev1, UAT-approved. See README "Build status (CLOSED 2026-07-28)".

---

## 1. Overview

A non-lawyer uploads an NDA in the SpaarkeAi Assistant and gets an advisory review inside **Compose (the single surface)**: risk rating + cited flagged-section summary (Assistant + in-Compose panel) → right-gutter advisory Comments → user-driven per-section Draft Alternative → Summary Page + comment-baked Word export → SPE save with versioning. Runs on the **Reasoning** model tier with a runtime picker. Net-new code is small and reuse-first; the load-bearing gate is the **ADR-039 deterministic-vs-advisory amendment (task 001)**.

## 2. Discovered Resources (Step 2)

### Applicable ADRs
| ADR | Relevance |
|---|---|
| **ADR-039** (grounded execution / closed catalogs) | Core dispatch; **AMENDED here** (deterministic vs advisory tiers) |
| **ADR-040** (session ledger) | Store-before-render for advisory output |
| **ADR-049** (Compose shadow-document) | OOXML source-of-truth, patch engine, comments, DOCX save/export — central to the Compose work |
| **ADR-041** (judgment/confirmation/completion) | Confirmation-as-deterministic + OutcomeCard gating |
| **ADR-043** (AI capability execution spine) | ContextBinder→envelope, DispositionRoutability registry |
| **ADR-030** (PaneEventBus) | Typed channel the `compose_advisory_comments` event rides |
| **ADR-033** (doc-stream SSE side-channel) | Streaming compose edits/redlines to client |
| **ADR-037** (multinode output composition) | Section-keyed composite streaming |
| **ADR-016** (rate limits / model tier) | Model-tier selection (Path C — complete the deferred wiring) |
| **ADR-013** (AI facade / PublicContracts) | Capability boundary for new BFF AI code |
| **ADR-007** (SpeFileStore) | SPE save + versioning |
| **ADR-032** (Null-Object kill-switch) | Any new conditional BFF service |
| **ADR-038** (testing strategy) | KEEP-path seam tests = DoD |
| **ADR-015/014** (governance/caching) | RAG grounding + ledger tiering; AI caching |
| **ADR-028** (auth) · **ADR-030/031** (pane/stage) · **ADR-006/021/012** (UI: Code Page, Fluent v9, shared lib) | Client + auth surfaces |

### Constraints
`.claude/constraints/bff-extensions.md` (BFF governance — binding) · `ai.md` · `api.md` · `auth.md` · `azure-deployment.md` (publish ≤60 MB) · `testing.md` · `theme-consistency.md`

### Patterns
`.claude/patterns/ai/`: `public-contracts-facade.md`, `endpoint-di-symmetry.md`, `node-executor-authoring.md`, `streaming-endpoints.md`, `analysis-scopes.md`, `indexing-pipeline.md`, `text-extraction.md` · `.claude/patterns/ui/`: `fluent-v9-component-authoring.md`, `embedded-widget-sizing.md`

### Knowledge / Standards
`docs/architecture/`: SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN, COMPOSE-REDLINE-DERIVED-VIEWS, rag-architecture, SPAARKEAI-WORKSPACE-ARCHITECTURE, SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN · `knowledge/`: azure-ai-search, agent-framework, sharepoint-embedded · `docs/standards/`: DATA-ACCESS-DECISION-CRITERIA, CHAT-ATTACHMENT-POLICY, MODAL-DECISION-CRITERIA, ASSISTANT-UI-ELEMENT-CRITERIA, CODING-STANDARDS, INTEGRATION-CONTRACTS, TEST-ARCHITECTURE

### Skills
`ai-procedure-maintenance` (ADR-039 amendment) · `jps-action-create` / `jps-validate` (NDA-REVIEW Action) · `add-reference-to-index` (seed standard) · `jps-scope-refresh` · `dataverse-deploy` / `dataverse-create-schema` · `bff-deploy` · `code-page-deploy` · `fluent-v9-component` · `ui-test` · `code-review` · `adr-check` · `task-execute`

### Scripts
`scripts/ai-search/Add-ReferenceToIndex.ps1` · `Index-AllReferences.ps1` · `scripts/Deploy-AnalysisAction.ps1` · `scripts/dataverse/Seed-PlaybookConsumers.ps1` · BFF/code-page deploy scripts · `scripts/Validate-TaskPoml.ps1`

### Canonical seeds to COPY (reuse-first)
- **Action seed**: `infra/dataverse/actions/compose-compare-to-playbook.action.json` (+ input/output schemas) → NDA-REVIEW; `compose-draft-alternative.action.json` (existing rewrite tool)
- **Binding rows**: `infra/dataverse/sprk_playbookconsumer-rows.json`
- **Prompted executor / model tier**: `Services/Ai/LinearConsumers/ActionRunner.cs`, `Services/Ai/PublicContracts/Binding.cs` (`AiModelTier`), `Configuration/DocumentIntelligenceOptions.cs`
- **Coded-workflow precedent (only if needed)**: `Services/Ai/Narrators/DailyBriefingNarrator.cs` + `CodedWorkflowRegistry.cs`
- **Compose server**: `Services/Compose/ComposeShadowPatchEngine.cs` (`ApplyComment`), `ComposeDocumentRenderer.cs`, `ComposeService.cs` — **`DocxAnnotationWriter.cs` RETIRED**
- **Compose client**: `Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`, `ComposeWorkspace.tsx`, `useComposeWorkspaceReceivers.ts`, `hooks/useComposeCommentThreads.ts`, `ComposeAiToolbar.tsx`
- **RAG**: `Services/Ai/ReferenceRetrievalService.cs`, `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`

## 3. Phase Breakdown (WBS)

> Numbering leaves 10-gaps for insertion. `<parallel-group>` / `<parallel-safe>` set at task-create. Model tier: Sonnet-5 @ high default; **Opus** for the ADR amendment + security/architecture tasks.

### Phase 0 — Governance gate (MERGE GATE, sequential, `.claude/`+`docs/adr/` = main-session-only)
- **001 — ADR-039 amendment**: author concise (`.claude/adr/`) + full (`docs/adr/`) deterministic-vs-advisory grounding tiers; update `.claude/CHANGELOG.md`; adr-check. **Blocks all advisory-tier tasks.** [ai-procedure-maintenance, adr-check] · Opus.

### Phase 1 — Platform enablers (model tier + grounding)
- **010 — Model-tier last-mile**: tier→deployment resolver + `StandardModel`/`ReasoningModel` in `DocumentIntelligenceOptions` + appsettings; `AnalysisAction.ModelTier` plumb; `ActionRunner` `model:null`→resolved. [BFF] · **§10 Placement Justification + publish-size**.
- **011 — Runtime model picker**: Assistant tier control → `sprk_modeltieroverride` via same resolver. [client]
- **012 — Seed NDA standard + grounding pin**: seed baseline Parts A–C into `spaarke-rag-references` (new source ID e.g. KNW-011, `documentType: legal`) via add-reference-to-index; **verify NFR-06 tenant pin** (`tenantId="system"` vs execution tenant). [add-reference-to-index]
- **013 — Reasoning deployment provisioning** (infra/appsettings; coordinate — may be external).

### Phase 2 — NDA review capability (Actions + Bindings + routing)
- **020 — NDA-REVIEW Action**: JPS prompt (baseline Parts A–B + Mike MIT), output schema `{overallRisk, flaggedSections[]}`, `sprk_modeltier=Reasoning`, advisory-tier temperature. [jps-action-create, jps-validate] · deps: 001, 010.
- **021 — NDA-STANDARD-SUMMARY** (UC3): prompted Fast-tier over the standard (or point a summary binding). deps: 012.
- **022 — Bindings + "Review NDA" card + classification**: `nda-review/default`, `nda-standard-summary`; resolve chip binding GUID; NDA doc classification on `document_uploaded`. [Seed-PlaybookConsumers] · deps: 020.
- **023 — Whole-doc review orchestration**: single prompted Action + disposition fan-out (card + comments); coded `NdaReviewWorkflow` only if orchestration demands (ADR-039 composite rule). deps: 020.

### Phase 3 — Compose review experience (client, single surface)
- **030 — Review-summary docked panel** in Compose (ComposeCommentThread convention). [fluent-v9-component] · deps: 023.
- **031 — Advisory-comments event + receiver**: `compose_advisory_comments` via **PaneEventBus (ADR-030)** + receiver reusing `createThread` + `resolveTargetSpans('strict')`. deps: 023.
- **032 — Right-gutter comment layout**: right-rail + `coordsAtPos` + live-pos resolution (fix `anchorText`→live pos) + collision/stacking. deps: 031.
- **033 — Draft Alternative + trace activation**: resolve `compose-draft-alternative` `bindingId:''`; execution-trace flow. deps: 022.

### Phase 4 — Export & persistence
- **040 — Comment-export wiring fix**: client sends `ComposeAnchoredComment` in `comments` (not `annotations`); `ApplyComment` bakes `w:comment`; round-trip via `DocxAnnotationReader`. [ADR-049] · deps: 031.
- **041 — Summary-Page DOCX writer**: section-insert + page break in `ComposeShadowPatchEngine`/`ComposeDocumentRenderer`. deps: 023.
- **042 — SPE save + versioning verification**: assert existing `ComposeService.SaveAsync`→SPE creates a version (test only). [ADR-007]

### Phase 5 — Eval & acceptance
- **050 — Eval harness + closed set**: `legal-eval-config.yaml` cases (6 NDAs) + negative/authorization + advisory-quality rubric (NFR-01); `metrics/citation_accuracy.py`. deps: 020.
- **051 — Golden-utterance dispatch eval** (ADR-039 gate): dispatch cases for the NDA card + NL intent. deps: 022.
- **052 — Grounding tenant-pin integration test** (NFR-06). deps: 012.

### Phase 6 — Deploy & wrap-up
- **060 — Deploy**: BFF (bff-deploy), code page (code-page-deploy), Dataverse Actions/Bindings (dataverse-deploy), AI Search index. Publish-size ≤60 MB check.
- **061 — UI tests**: review panel, gutter comments, "Review NDA" chip, dark-mode (ui-test).
- **090 — Wrap-up**: README status Complete, lessons-learned, `/test-diet`, archive.

## 4. Dependencies & critical path

```
001 (ADR-039 gate) ─── blocks all advisory-tier work
   └─► 010 model-tier ─► 020 NDA-REVIEW ─┬─► 022 bindings/card ─► 033 draft-alt
        012 seed+pin ───────────────────┤   023 orchestration ─┬─► 030 panel
        011 picker                        │                     ├─► 031 comments(event) ─► 032 gutter / 040 export
                                          │                     └─► 041 summary-page
   050/051/052 evals ◄────────────────────┘
   060 deploy ◄── all ── 061 UI ── 090 wrap-up
```
**Critical path**: 001 → 010 → 020 → 023 → 031 → 040. **Parallelizable**: 011/012 (with 010); 030/031/041 (after 023); evals alongside their features.

## 5. Governance
- **§10 BFF Hygiene**: tasks 010/023/040/041 add to `Sprk.Bff.Api` → Placement Justification + publish-size ≤60 MB each. No new NuGet expected.
- **§11**: NDA-REVIEW Action, advisory-comments wiring, model-tier last-mile, Summary-Page writer, review panel, gutter, comment-export fix, seeded source (see spec §11 table).
- **ADR-039 Path-B**: task 001 merges before any advisory-tier task (FR-00 gate).
- **Hot-path**: BFF=Y, SpaarkeAi=Y (design.md declaration).

## 6. Timeline (rough)
~30–40 tasks across 7 phases. Critical path ~6 sequential tasks. Phase 0 (gate) first; Phases 1–2 enable; 3–4 build the experience; 5 validates; 6 ships.
