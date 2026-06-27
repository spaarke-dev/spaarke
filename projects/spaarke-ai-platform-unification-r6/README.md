# Spaarke AI Platform Unification R6

> **Last Updated**: 2026-06-07
>
> **Status**: Ready for Implementation
>
> **Type**: Architecture convergence phase (not feature project)

## Overview

R6 is the convergence phase that aligns the conversational chat-agent with the production-mature playbook side of the Spaarke AI platform. After R6, R7+ feature work becomes "design a playbook in data + declare its output schema + reference scopes" while conversational primacy is preserved.

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | AI-optimized implementation specification (59 FRs, 16 NFRs across 9 pillars) |
| [Design](./design.md) | Comprehensive R6 design document (1457 lines) |
| [Plan](./plan.md) | Implementation WBS, phase breakdown, parallel groups |
| [Project Rules](./CLAUDE.md) | Project-scoped AI context and binding rules |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker with parallel execution groups |
| [Current Task State](./current-task.md) | Active work tracker (context-recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Implementation kickoff (Step 3 of project-pipeline) |
| **Progress** | 0% (artifacts generated; tasks generated; implementation not yet started) |
| **Target Completion** | ~6–7 weeks from Phase A start (Q7 scope-expansion adjusted) |
| **Completed Date** | — |
| **Owner** | Spaarke dev team |

## Problem Statement

R5 closed 2026-06-06 with known limitations after a single SC-18 walkthrough surfaced 9 systemic architecture gaps in the AI platform: persona text hardcoded in C# (not data-driven), tool registry ignored (12 hardcoded chat-tool classes instead of `sprk_analysistool` rows), playbook FK bypass (chat `/summarize` uses alternate-key lookup), schema-aware rendering missing (R5 walkthrough surfaced TL;DR + Entities rendering bugs), workspace ↔ assistant one-way (agent can't read tab state), execution opacity (no Claude-Code-like trace surface), memory utilization gap (`MatterMemoryService` exists but unwired), command-vocabulary ambiguity (`/summarize` not formally a command), widget visibility contract missing (agent can't query workspace widgets for compact state).

Continuing R5-style cycle-N patching would mean designing R7+ features around these gaps. R6 instead closes the gaps at their architectural roots so feature work after R6 becomes data-driven by default.

## Solution Summary

R6 ships 9 pillars across 4 phases + 1 parallel handler workstream:

1. **Persona as 5th Scope** — `sprk_aipersona` Dataverse entity (SYS-/CUST-/playbook inheritance)
2. **Tool Registry Convergence + 8 Typed Handlers** — `IToolHandler` generalization; data-driven chat tool discovery; 10 existing tools migrated; 8 missing handlers built (EntityExtractor, ClauseAnalyzer, RiskDetector, ClauseComparison, DateExtractor, FinancialCalculator, InvoiceExtraction, FinancialCalculation)
3. **Generic `invoke_playbook` Chat Tool** — `IInvokePlaybookAi` facade; specialized bridges removed
4. **Chat `/summarize` Through `PlaybookExecutionEngine`** — playbook FK fix; FK chain traversed
5. **Output Schema (Action) + Destination (Node Config) + Schema-Aware Widget + Dedup at CapabilityRouter** — fixes R5 rendering bugs structurally; duplicate-fire eliminated at routing layer
6. **Tri-directional Assistant ↔ Context ↔ Workspace State + Execution-Trace Widget** — workspace tabs typed, persisted, agent-readable; Context pane shows Claude-Code-like process stream
7. **Cross-Conversation Memory + Smart Recall + Full Memory Management UI** — summarization compression, pinned-context, selective recall, hierarchical composition, Pinned Memory CRUD UI (Q7 scope expansion this session)
8. **Command Router** — slash/hash/at vocabulary; 6 hard + 4 soft slashes
9. **Workspace Widget Visibility Contract** — `getAgentVisibleState()` per widget; agent prompt includes Assistant-visible tabs

## Graduation Criteria

The project is **complete** when:

- [ ] **Phase A exit (Pillars 1, 2, 3, 4)** — Chat-agent tool list driven by `sprk_analysistool` rows; persona data-driven; one generic `invoke_playbook` tool; `SessionSummarizeOrchestrator` routes through `PlaybookExecutionEngine`
- [ ] **Phase B exit (Pillar 5)** — Workspace renderer handles array + object fields correctly; `outputSchema` declared on action; duplicate-fire eliminated at `CapabilityRouter` (one user intent → one render)
- [ ] **Phase C exit (Pillars 6, 7, 9)** — Agent has accurate workspace awareness; "Update the summary in Tab 1" works; Send-to-Workspace + Add-to-Assistant + Pin-to-Matter functional; Context pane shows live execution trace; cross-conversation memory recalls prior-matter context; Pinned Memory UI ships
- [ ] **Phase D exit (Pillar 8 + integration)** — `/help` works; hard slashes bypass LLM (<100ms); soft slashes route via agent with prioritized intent; references resolve at parse time; lightweight eval baseline captured
- [ ] **8 Typed Handlers** — All 8 implemented as `IToolHandler`, registered via auto-discovery, dispatch from playbook + chat contexts
- [ ] **Vertical-Slice Validation** — Summarize playbook end-to-end exercises every pillar (per spec §Success Criteria)
- [ ] **NFR compliance** — BFF publish size ≤+5 MB total (NFR-02); pre-fill flow preserved (NFR-07); no Agent Framework references (NFR-04); 4-channel PaneEventBus + 4-stage shell lifecycle preserved
- [ ] **Quality gates** — All FULL-rigor tasks pass `code-review` + `adr-check`; integration test coverage for vertical slice; no HIGH-severity CVEs

## Scope

### In Scope

- 9 architectural pillars (above)
- 8 typed tool handlers (parallel workstream)
- Lightweight eval baseline (markdown transcripts per persona+playbook)
- Migration of 10 pre-R5 hardcoded chat tools to `IToolHandler` (single Phase A batch per Q9)
- Migration of 4 existing playbook actions to populate new `outputSchema` field (summarize-chat, summarize-workspace, matter-prefill, project-prefill)
- Full Memory Management UI in Context pane (Q7 scope expansion this session — was R7-deferred in spec)

### Out of Scope

- Microsoft Agent Framework adoption (binding exclusion per NFR-04)
- Replacement of any of the 11 production node executors
- Replacement of visual playbook canvas (`PlaybookBuilder` Code Page)
- Replacement of JPS authoring pipeline
- Modification of pre-fill flow signatures (`MatterPreFillService`, `ProjectPreFillService`, `useAiPrefill`, 45s timeout, `$choices` constraints — all binding per NFR-07)
- New ADRs (R6 operates within existing constraints)
- New top-level DI registrations (all R6 services register in existing modules per ADR-010)
- Full eval harness with CI integration (deferred R7)
- Custom Scope admin UI (deferred R7 per Q3)
- New PaneEventBus channels (4-channel contract closed per ADR-030 — additive event types only)
- New shell lifecycle stages (4-stage closed per ADR-031)

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| Persona = 5th scope entity (`sprk_aipersona`) | Most-specific-wins inheritance matches existing scope pattern; data-driven persona enables tenant override without code deploy | [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md), Q1+Q2 |
| `IToolHandler` generalized from `IAnalysisToolHandler` | Reuse auto-discovering registry pattern across playbook + chat contexts | Q11 |
| Single `IInvokePlaybookAi` facade | Cleaner than overloading `IWorkspacePrefillAi` per ADR-013 facade boundary | [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md), Q11 |
| `outputSchema` on action; destination on node config; widget schema-aware; dedup at CapabilityRouter | Clean separation of concerns; same handlers everywhere; existing 4 actions migrated to new model | Q5 (re-shaped 2026-06-07) |
| Q7 scope expansion: full Memory Management UI in R6 | Pinned Memory CRUD shipped with R6 instead of deferred R7 | Q7 |
| Q9 batch migration: all 10 chat tools at once | Faster delivery; demands comprehensive regression test + rollback plan | Q9 |
| Pillar 6 split into 6a/6b/6c | Internal sequencing: 6a state model gates 6c trace widget; 6b parallel with 6c | seq-2 |
| `context.tool_call_*` events: tool name + decision + timestamp ONLY | ADR-015 data governance | [ADR-015](../../.claude/adr/ADR-015-ai-data-governance.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Q9 batch tool migration breaks chat path | High | Medium | Comprehensive regression test covering every code path the 10 tools use; rollback plan: git revert migration commit; DI flag to reactivate hardcoded classes; staging validation before master merge |
| Pre-fill flow regression during Pillar 5 schema migration | High | Low | NFR-07 binding; before/after test of pre-fill flow in every Pillar 5 task; hook signatures + 45s timeout + `useAiPrefill` UNCHANGED |
| Q7 scope expansion makes Phase C tight | Medium | Medium | Flag at end of Phase B if calendar slipping; can re-defer Memory UI if needed |
| BFF publish-size budget breach (≤+5 MB) | Medium | Low | Per-task size verification per CLAUDE.md §10; current baseline ~45.65 MB; ceiling 60 MB hard |
| Pillar 6c trace widget surfaces ADR-015 violations | Medium | Low | Trace events log tool name + decision + timestamp ONLY; never user message content; reviewed in Pillar 6c tasks |
| Cross-pillar contract drift (Pillar 6 + 7 + 9 simultaneous) | Medium | Medium | Pillar 6a state model lands first; 7 + 9 consumers reference 6a interfaces; integration test at end of Phase C |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R5 closed with known limitations | Internal | ✅ 2026-06-06 | Closeout PR #364, lessons-learned PR #365 |
| R6 design.md merged to master | Internal | ✅ PR #367 | |
| R6 spec.md authored | Internal | ✅ PR #368 | Source for FR/NFR enumeration |
| Cosmos DB containers (`sessions`, `prompts`, `audit`, `memory`, `feedback`) | Azure | ✅ Production | Reused for memory + workspace tabs |
| Azure OpenAI (GPT-4o + GPT-4o-mini) | Azure | ✅ Production | CapabilityRouter Layer 2 |
| Azure AI Search indexes (`spaarke-knowledge-index-v2`, `spaarke-rag-references`, `spaarke-session-files`) | Azure | ✅ Production | RAG + session-files filter |
| Insights Engine R2 coordination | Cross-project | ⚠️ External | Wave F may impact Pillar 3 `invoke_playbook` for `insights.query` capability |
| Power Apps Dataverse forms for new entities | Internal | ⚠️ Will be created in tasks | Q3: no custom admin UI in R6 |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-07 | 1.0 | Initial implementation-phase README (replaces design-phase version); Q1–Q11 decisions baked in | project-pipeline session |

---

*See [plan.md](./plan.md) for implementation WBS and [CLAUDE.md](./CLAUDE.md) for project-scoped rules.*
