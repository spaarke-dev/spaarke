# Advanced AI Capabilities — Program Roadmap

> **Date**: 2026-07-21
> **Status**: Program umbrella roadmap (re-based on current architecture)
> **Owner**: ralph.schroeder@hotmail.com
> **Supersedes framing of**: [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md) (2026-05-20) — that doc predates the 2026-07 AI-architecture redesign and is written in the retired JPS-playbook vocabulary. It remains valid as pattern research; this roadmap re-bases its plan onto the shipped Action/Binding/Tool/Ledger model and folds in the Mike OSS findings.
> **Companion inputs**: [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md) (6 interaction modes) · [`TEST-DATA-REQUIREMENTS.md`](TEST-DATA-REQUIREMENTS.md) · Mike OSS assessment (memory: `reference_mike-oss-legal-ai-assessment`)

---

## 0. The headline finding

A four-front code review (memory · Action/Skill/Tool/Knowledge/Binding catalog · Lavern+Mike pattern coverage · retrieval/UI surfaces) found that **~70% of the "advanced AI capabilities" proposed by the Lavern plan and the Mike findings already exist in the codebase** — much of it wired, some of it built-but-dark.

This changes the program's character. It is **not** "build a legal-AI platform." It is, in priority order:

1. **ACTIVATE** capability that is built + DI-registered but has no runtime caller (biggest, cheapest ROI).
2. **COMPLETE** partials and assemble existing building blocks into product features.
3. **BUILD** the genuinely-absent net-new features (a short list).
4. **GOVERN** — add the quality/governance primitives and the tiered-governance ADR amendment.

The re-basing rule for every child project: **the current model is Actions + Bindings + Tools over the ADR-039 closed catalog / ADR-040 ledger dispatch spine — NOT the frozen node-graph playbook engine.** Any Lavern pattern expressed as "new JPS playbook node" must be re-expressed as a prompted/coded Action, a `sprk_analysistool` handler, a Binding disposition, or a ledger reader.

---

## 1. Current-state capability inventory (what already exists — credit before building)

Verdicts: **DONE** = wired end-to-end · **DARK** = built + DI-registered, zero runtime callers · **PARTIAL** = real but incomplete · **ABSENT** = net-new.

### 1.1 The action-based AI model (Actions · Skills · Tools · Knowledge · Bindings) — DONE

| Concept | State | Evidence |
|---|---|---|
| **Actions** | 12 prompted (`SUM-CHAT`, `CLS-CHAT`, `REF-CHAT`, `DRAFT-CORR`, `CREATE-TASK`, `CREATE-MATTER`, 7× compose) via `ActionRunner` + `PromptSchemaRenderer`; **1 coded composite** (`DAILY-BRIEFING@v1` → `DailyBriefingNarrator`) via `CodedWorkflowRegistry` | `Services/Ai/PublicContracts/Binding.cs:212`, `LinearConsumers/ActionRunner.cs`, `Services/Ai/Narrators/DailyBriefingNarrator.cs`, `infra/dataverse/actions/*.action.json` |
| **Skills** | 30+ prompt-fragment skills (Citation Extraction, Risk Flagging, Defined Terms…) injected into Action prompts — real but **not independently dispatchable** (prompt modifiers, predate ADR-039) | `sprk_analysisskill`, `AnalysisSkillService.cs`, `PromptSchemaRenderer.RenderSkillSection` (`:694`) |
| **Tools** | ~37 `sprk_analysistool` rows / ~25 handlers, auto-discovered + projected to the loop as the **closed catalog** (document/search, dataverse.\*, memory, notify/email, verify/legal-research, deterministic text/finance/clause) | `ToolFrameworkExtensions.cs:92`, `Chat/AgentToolCatalogProjector.cs:23`, `Services/Ai/Handlers/**` |
| **Knowledge** | Hybrid RAG (vector+BM25, 3072-dim, privilege-filtered); `spaarke-rag-references` **populated (93 docs incl. KNW-001..010 legal clause libraries)**; `ReferenceRetrievalService` parallel golden-reference path | `RagService.cs`, `ReferenceRetrievalService.cs:16`, `Configuration/AiSearchOptions.cs` (5 indexes), `config/spaarke-resources.yaml:344` |
| **Bindings** | ~18 full-contract bindings (dispositions: Informational/WorkProduct/Overlay/Email/Record/Notification/**Compose**/SurfaceLaunch); single routing surface; ledger keys `{bindingId}@t{n}` | `Binding.cs:39,134`, `Chat/SessionDispatchOrchestrator.cs`, `DispositionRoutability.cs`, `OutputRouter.cs`, `infra/dataverse/sprk_playbookconsumer-rows.json` |

### 1.2 Memory — DONE core, DARK sophisticated layer (your flagged area — confirmed)

- **DONE**: structured cross-session `MemoryItem` store (Record + User scope, Cosmos `memory-items`, upsert-by-key) with `memory.write` capture, user-seed, Compose defined-term capture, and **read-into-prompt** for both scopes (`ContextBinder.cs:454,512`, `PlaybookChatContextProvider.cs:758,800`); `recall_session_file` (session AI-Search index); Pinned Memory **CRUD UI** + `/api/memory/pins`; GDPR governance + Tier-2 audit.
- **DARK (built, DI-registered, zero callers — the under-leveraged capability)**:
  - `MemoryCompositionService.ComposeAsync` (the 4-layer hierarchical memory: recent-verbatim + compressed-mid + retrieved-old + always-on pinned) — **never invoked in production**.
  - Consequently **pinned memory never reaches the LLM** — pins are written from 3 surfaces (widget, voice `ManagePinnedContextHandler`, dialog) but the only prompt-path reader is the dark composer.
  - `PinnedContextRecallService` (embedding similarity recall) and `SummarizationCompressionService` (mid-window compression) — complete, tested, reachable only through the dark composer.
  - **Session Ledger (ADR-040)** — model + 3-tier persistence (Redis→Cosmos→Dataverse) landed with *"zero production readers"* per its own contract.

### 1.3 Precedent Board (Lavern's "crown jewel") — ALREADY EXISTS, manual-only

- **DONE**: `IPrecedentBoard`/`DataversePrecedentBoard` over Dataverse `sprk_precedent`; **full lifecycle** `Tentative→Confirmed→UnderDriftReview→Deprecated→Retired`; cross-matter N:N `sprk_precedent_matter`; confirm endpoint projects to `spaarke-insights-index` (`artifactType=precedent`); Observations feed it via `ObservationEmitterNodeExecutor`.
- **ABSENT**: automated reinforce/decay/salience scoring; automatic Observation→Precedent promotion (manual SME only); `spaarke-insights-index` is **0 docs** (ingest job pending). So the *board* exists; the *automation and content* do not.

### 1.4 Lavern + Mike pattern coverage

| # | Pattern | Verdict | Note |
|---|---|---|---|
| 1 | Mechanical citation verification | **DONE** | `GroundingVerifier.cs` + `CitationSafetyCheck` (node + post-LLM) |
| 2 | Execution-trace / step-flow UI | **DONE** | `ExecutionTraceWidget`/`ComposeTraceHost` (ledger-backed) — but empty in practice (see r2) |
| 3 | Evaluator gate (bounded re-eval loop) | **ABSENT** | net-new |
| 4 | Human approval gate | **PARTIAL** | Confirmation Gate + `GateDecisionV2` built; multi-surface `IGateResolver` planned-only |
| 5 | decline_to_find + evidence-sufficiency | **DONE** | `DeclineToFindNode`, `EvidenceSufficiencyNode` |
| 6 | Evidence-required enforcement | **DONE** | `AgentTurnCitationEnforcer` (repairs + telemeters) |
| 7 | Phase deny-tools | **ABSENT** | gating is capability/context-based, not per-phase |
| 8 | Ingest sanitization | **PARTIAL** | control-char+injection+PII done; no zero-width/homoglyph |
| 9 | Provider/model tier abstraction | **PARTIAL** | multi-provider clients exist; deployment catalog is a stub; no semantic tier selector |
| 10 | Tabular extraction (doc×question grid) | **ABSENT** | net-new |
| 11 | Reference legal corpus / seed data | **PARTIAL** | pipeline + KNW golden docs live; no CUAD/MAUD/case-law seed; insights-index empty |
| 12 | External case-law retrieval | **DONE (via Bing)** | `LegalResearchHandler` Bing-grounded; no native CourtListener API client |
| 13 | Inline redline / tracked-changes | **DONE** | Tiptap marks + `DocxAnnotationWriter` |
| 14 | Citation source viewer | **PARTIAL** | badge + `context_highlight` SSE + Tiptap highlight exist, not assembled |

### 1.5 Surfaces + infra

- **DONE**: mature streaming chat (`SprkChat`/`useSseStream`), Compose editor + DOCX round-trip, config-driven DataGrid (`sprk_gridconfiguration`), Service Bus job dispatch (13+ handlers), 5-index hybrid RAG.
- **DARK/PARTIAL**: Compose cross-pane flows 3/4/6 (stub receivers — context↔workspace↔knowledge-graph); `ScheduledRagIndexingService` ships disabled; **no cron/durable scheduler** (only `PeriodicTimer`); **no `sprk_analysis` durable results table** (outputs persist to host record via `sprk_aitopicregistry` + insights index + session ledger); **no AI-driven DataGrid columns** (AI only as row/bulk secondary actions).

---

## 2. How Lavern and Mike relate (complementary, not overlapping)

- **Lavern** contributes governance/quality/memory scaffolding (evaluator, gates, evidence, phase-deny, sanitization) + academic corpora (CUAD/MAUD) + the Precedent Board pattern + the 6 interaction-mode framing. **Apache-2.0** — patterns portable, code not reused.
- **Mike** contributes shippable product surface + **MIT-licensed prompt/workflow library** (nda-review, lease, employment, draft-from-template, tabular `table-columns.yaml`) + CourtListener retrieval + inline redline contract. **MIT workflows directly adaptable with attribution.**

Where they overlap (citation verify, step-flow UI, tabular, seed data) they corroborate the bet. Net: Lavern = trust/memory scaffolding (mostly already built); Mike = product content + external retrieval (the cheap authoring wins).

---

## 3. Program structure — umbrella + sequenced child projects

**Decision (2026-07-21):** keep `projects/ai-advanced-capabilities-development/` as the **program umbrella**; decompose into independently-shippable child worktree projects, sequenced by dependency, organized under four theme-waves (ACTIVATE → COMPLETE → BUILD → GOVERN).

Each child project is a normal Spaarke project (design.md → project-pipeline → task-execute), registered in [`projects/INDEX.md`](../INDEX.md), with a Placement Justification per CLAUDE.md §10/§11.

### Wave 1 — ACTIVATE (wire built-but-dark; near-zero net-new; highest ROI)

| ID | Project | Scope | Sources | Primary status |
|---|---|---|---|---|
| **r1** | **Memory Activation** | Wire `MemoryCompositionService.ComposeAsync` (or at minimum `IPinnedContextRepository` read) into `PlaybookChatContextProvider` prompt assembly so pinned memory, similarity recall, and compression actually reach the model; decide ADR-040 ledger reader strategy | Existing (dark) | Wiring, not building |
| **r2** | **Context Pane completion** | Surface/seed the execution trace; resolve client-side compose inline-action `bindingId:''` stubs to real GUIDs; implement Compose cross-pane flows 3/4/6; coordinate Assistant↔Workspace↔Context; **decide ledger-only vs. reasoning-narration** (central product+ADR-040 question) | Existing (partial) + Mike UX | Complete + one decision |

### Wave 2 — COMPLETE product capability on existing rails (author / seed / assemble)

| ID | Project | Scope | Sources | Primary status |
|---|---|---|---|---|
| **r3** | **Legal Analysis Capability Pack** | Port Mike's MIT prompts (nda-review, lease, employment, credit, shareholder, draft-from-template…) into prompted Actions + Bindings on the Compose/Analysis surface; run the dormant eval harness (`legal-eval-config.yaml`, `metrics/citation_accuracy.py`) to validate the never-tested Action prompts | Mike (MIT) | Authoring + seeding |
| **r4** | **Citation Source Viewer** | Assemble existing blocks — `CitationBadge` + `context_highlight` SSE + Tiptap `QaHighlightExtension` + source pane — into one click→open-source→jump-to-passage viewer with an end-of-answer citation block (Mike screenshots 3–6) | Existing (partial) + Mike | Assembly + small UI |
| **r5** | **Reference Corpus Seed** | Populate the empty `spaarke-insights-index`; extend `spaarke-rag-references` with CUAD/MAUD (Lavern #9, CC BY 4.0 clean) and optionally case-law; legal-review gate before any CC BY-SA (UNFAIR-ToS/LEDGAR); optional native CourtListener API client to upgrade #12 from Bing | Lavern #9 + Mike | Seed data (pipeline exists) |

### Wave 3 — BUILD genuine net-new features

| ID | Project | Scope | Sources | Primary status |
|---|---|---|---|---|
| **r6** | **AI Tabular Review** | doc×question review grid: AI-driven DataGrid columns bound to Action `bindingId`s + a coded fan-out (`ICodedWorkflow`) iterating documents×columns with cited cells; Mike `table-columns.yaml` schemas as seed. Depends on r3 (column Actions) | Lavern #12 + Mike | Net-new (uses existing DataGrid + Actions) |
| **r7** | **Precedent Board Automation** | The board exists — add automated reinforce/decay/salience scoring + Observation→Precedent promotion + drift-detection job + SME curation UI; populate the Observation→insights-index pipeline. Depends on r5 (corpus) + scheduler + insights-index activation | Lavern #1 | Automate existing board |

### Wave 4 — GOVERN (platform primitives + ADR work)

| ID | Project | Scope | Sources | Primary status |
|---|---|---|---|---|
| **r8** | **AI Quality & Governance Primitives** | EvaluatorGate (#3, absent); phase deny-tools (#7, absent); zero-width/homoglyph sanitization (#8); real model-tier abstraction (#9); generalize the Confirmation Gate into multi-surface `IGateResolver` (#4). Carries the **tiered-governance ADR amendment** (§6.5 Path B): a lighter envelope for interactive, human-verified read-only paths | Lavern #2,#4,#7,#8,#9 | Net-new platform + ADR |

### Enabling infra (fold into the first dependent project, or a small standalone)

- **Native cron/durable scheduler** (only `PeriodicTimer` today) — needed by r7 (decay/promotion) and any digest/triage mode. Fold into r7 or spin a small infra project.
- **`sprk_analysis` durable results table** — needed if triage queues / analysis history / precedent-decay ledgers require independently-queryable records (today: host-record columns + insights index + session ledger). Evaluate in r7.

---

## 4. Dependency order & rationale

```
Wave 1 (activate)      r1 Memory ─┐        r2 Context Pane ─┐
                                  │                         │
Wave 2 (complete)   r3 Capability Pack ──► r4 Citation Viewer   r5 Reference Corpus
                                  │                                   │
Wave 3 (build)          r6 Tabular Review (needs r3)      r7 Precedent Automation (needs r5 + scheduler)
                                  │
Wave 4 (govern)                 r8 Quality & Governance (benefits all; gates nothing in W1)
```

- **Front-load ACTIVATE**: r1/r2 turn already-paid-for capability into visible product value with almost no new ADRs — the cheapest wins and the two you flagged.
- **Wave 2 is authoring/assembly** on shipped rails (RAG, Actions, Compose, citation blocks) — low architectural risk, high product payoff, mostly Mike-derived content.
- **Wave 3 is the real net-new**, but each item stands on existing substrate (DataGrid+Actions; the existing Precedent Board).
- **Wave 4 is deferrable** — nothing in Waves 1–2 is blocked on it; it raises trust and unlocks the tiered-governance cost relief.

---

## 5. Cross-cutting

- **ADRs**: r8 carries the tiered-governance amendment (ADR-039/043, §6.5 Path B). r5 needs a legal-review checkpoint for CC BY-SA datasets. Lavern's proposed ADRs 10.1–10.5 are largely **already realized** (Precedent Board = `sprk_precedent`; citation verifier shipped; gate contract = `GateDecisionV2`) — re-scope them as "amend/extend," not "ratify new."
- **Licensing**: Mike workflows MIT (attribution); Lavern Apache-2.0 (patterns only, no code reuse); CUAD/MAUD CC BY 4.0 (attribution); UNFAIR-ToS/LEDGAR CC BY-SA (legal review).
- **BFF hygiene (§10)**: every child project touching `Sprk.Bff.Api` writes a Placement Justification + hot-path declaration + publish-size check.
- **Don't-rebuild list** (reviewer enforce): citation verify, execution-trace widget, decline/evidence nodes, redline marks, RAG stack, MemoryItem store, Precedent Board entity/lifecycle, Confirmation Gate. Extend these; do not fork parallel implementations.

---

## 6. Next actions

1. Confirm the r1–r8 decomposition (or adjust slicing).
2. Kick off **r1 Memory Activation** and **r2 Context Pane** (Wave 1) — via `/devops-project-start` or `/design-to-spec` per project.
3. For r2, decide the **ledger-only vs. reasoning-narration** question (forks the spec).
4. Open the legal-review ticket for CC BY-SA datasets ahead of r5.
