# Capability Lenses — have-vs-gap checklist (Lens 4 support)

> Use this to score each required capability as **REUSE / ACTIVATE / COMPLETE / BUILD**. Verdicts here are a **2026-07-21 snapshot** — always re-verify against code (these files move) or the current `PROGRAM-ROADMAP.md` §1. When a capability isn't listed, spawn an Explore audit; do not guess.
>
> Precedence when designing: **REUSE > ACTIVATE > COMPLETE > BUILD.**

## How to verify a verdict

1. Read `projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md` §1 (current-state inventory) — the authoritative reconciled list.
2. If not covered there, `Grep`/`Glob` the entry points below.
3. If still uncertain, launch an `Explore` agent scoped to the capability. Mark the verdict PROVISIONAL until confirmed.

## The capability primitives (name your Lens-3 needs against these)

- **Action** (prompted/coded) — a `sprk_analysisaction` row; prompted = JPS prompt + output schema via `ActionRunner`/`PromptSchemaRenderer`; coded = `ICodedWorkflow` via `CodedWorkflowRegistry`.
- **Skill** — a `sprk_analysisskill` prompt fragment injected into an Action's prompt (NOT independently dispatchable).
- **Tool** — a `sprk_analysistool` row + auto-discovered handler, projected to the loop by `AgentToolCatalogProjector` (closed catalog, capability-gated).
- **Knowledge/RAG** — an AI Search index queried via `RagService`/`ReferenceRetrievalService` + `KnowledgeRetrievalHandler`.
- **Binding** — a `sprk_playbookconsumer` row = the routing identity; carries disposition (Informational/WorkProduct/Overlay/Email/Record/Notification/Compose/SurfaceLaunch), risk, surfaces, events.
- **Memory** — `MemoryItem` (Record/User scope, Cosmos) + pinned context + session ledger.
- **Grounding** — citation verification + evidence enforcement + decline.

## Snapshot verdicts (2026-07-21 — verify before relying)

### REUSE (wired end-to-end — use as-is)
| Capability | Entry points |
|---|---|
| Action model (12 prompted + 1 coded) | `Services/Ai/LinearConsumers/ActionRunner.cs`, `CodedWorkflowRegistry.cs`, `infra/dataverse/actions/*.action.json` |
| Tool catalog (~37 tools, closed-catalog projection) | `Chat/AgentToolCatalogProjector.cs`, `Services/Ai/Handlers/**`, `ToolFrameworkExtensions.cs:92` |
| Bindings + dispatch spine | `Binding.cs`, `Chat/SessionDispatchOrchestrator.cs`, `DispositionRoutability.cs`, `OutputRouter.cs` |
| Hybrid RAG (5 indexes, privilege-filtered) | `RagService.cs`, `Configuration/AiSearchOptions.cs`, `ReferenceRetrievalService.cs` |
| Reference index populated (93 docs, KNW clause libraries) | `spaarke-rag-references`, `scripts/ai-search/Index-AllReferences.ps1` |
| Mechanical citation verification | `Services/Ai/CitationVerification/GroundingVerifier.cs`, `Safety/Citations/CitationSafetyCheck.cs` |
| Execution-trace UI (Context pane) | `SpaarkeAi/.../context/ComposeTraceHost.tsx`, `Spaarke.AI.Widgets/.../ExecutionTraceWidget.tsx` |
| decline_to_find + evidence-sufficiency | `Services/Ai/Nodes/DeclineToFindNode.cs`, `EvidenceSufficiencyNode.cs` |
| Evidence-required enforcement | `Chat/AgentTurnCitationEnforcer.cs` (repairs + telemeters) |
| Inline redline / tracked-changes | `Spaarke.Compose.Components/.../marks/{InsertionMark,DeletionMark}.ts`, `Services/Compose/DocxAnnotationWriter.cs` |
| External case-law (via Bing grounding) | `Services/Ai/Handlers/LegalResearchHandler.cs` |
| Memory capture + recall + Record/User read-into-prompt | `Services/Ai/Memory/MemoryItemStore.cs`, `MemoryWriteHandler.cs`, `RecallSessionFileHandler.cs`, `Context/ContextBinder.cs:454,512` |
| Precedent Board entity + lifecycle (manual) | `Services/Ai/Insights/Precedents/DataversePrecedentBoard.cs`, `sprk_precedent` |
| Streaming chat surface | `SprkChat.tsx`, `useSseStream.ts`, `ChatEndpoints.cs` |
| Compose editor + DOCX round-trip | `Spaarke.Compose.Components/.../ComposeEditor.tsx`, `utils/docxBridge.ts` |
| Config-driven DataGrid | `Spaarke.UI.Components/.../DataGrid/`, `sprk_gridconfiguration`, `ConfigurationService.ts` |
| Service Bus job dispatch | `Services/Jobs/ServiceBusJobProcessor.cs` |

### ACTIVATE (built + DI-registered but DARK — wire it, don't build it)
| Capability | Why dark / what to wire |
|---|---|
| Hierarchical memory composition (pinned + similarity recall + compression) | `MemoryCompositionService.ComposeAsync` has **zero callers** → pinned memory never reaches the LLM. Wire into `PlaybookChatContextProvider` prompt assembly. |
| Pinned-context injection | written from 3 surfaces (`ManagePinnedContextHandler`, widget, dialog); only reader is the dark composer. |
| ADR-040 session ledger readers | model + 3-tier persistence landed with "zero production readers". |
| Compose cross-pane flows 3/4/6 | `COMPOSE_FLOW_RECEIVER_MATRIX` — context↔workspace↔knowledge-graph receivers are stubs. |
| Execution-trace population | client-side compose action `bindingId:''` stubs → resolve to real GUIDs so trace events flow. |

### COMPLETE (partial — finish/assemble)
| Capability | Gap |
|---|---|
| Human approval gate (multi-surface) | Confirmation Gate + `GateDecisionV2` built; multi-surface `IGateResolver` (queue/webhook/auto/timeout) planned-only. |
| Citation source viewer | blocks exist (`CitationBadge.tsx`, `context_highlight` SSE, Tiptap highlight) — not assembled into click→open→jump-to-passage. |
| Ingest sanitization | control-char + injection + PII done; **no zero-width/homoglyph** normalization. |
| Model-tier abstraction | multi-provider clients exist; deployment catalog is a stub (`Api/Ai/ModelEndpoints.cs` `StubModelDeployments`); no semantic tier selector. |
| Reference corpus content | pipeline + KNW golden docs live; **no CUAD/MAUD/case-law seed**; `spaarke-insights-index` empty (0 docs, ingest job pending). |

### BUILD (absent — net-new; each needs §11 justification)
| Capability | Note |
|---|---|
| EvaluatorGate (bounded re-eval loop, model-tier separation) | genuine greenfield. |
| Phase deny-tools | gating today is capability/context-based, not per-phase. |
| Tabular doc×question review grid | DataGrid has no AI-column renderer (AI only as row/bulk secondary actions); needs column→Action binding + coded fan-out. |
| Automated precedent reinforce/decay/promotion scoring | board exists; automation absent (manual SME only). |
| Native CourtListener API client | only Bing grounding today (upgrade, optional). |
| Cron/durable scheduler | only in-process `PeriodicTimer`; needed for decay/digest/triage. |
| `sprk_analysis` durable results table | outputs persist to host record (`sprk_aitopicregistry`) + insights index + session ledger; no queryable results/history entity. |

## Notes
- **Skills ≠ dispatchable**: 30+ `sprk_analysisskill` rows are prompt fragments, not tools. If a use case needs a "skill" as a callable unit, that's an Action or Tool.
- **Untested prompts**: existing Actions/JPS were LLM-authored and never eval'd — "exists" ≠ "works" (Lens 6 must eval).
- **Vocabulary**: ignore node-graph "playbook engine" framing in pre-2026-07 docs; it is frozen. Use Actions/Bindings/Tools.
