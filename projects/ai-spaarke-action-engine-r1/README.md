# AI Spaarke Action Engine R1

> **Last Updated**: 2026-05-29
>
> **Status**: In Progress

## Overview

The Action Engine is the agent creation, management, and execution surface for the Spaarke platform — the management plane around the existing JPS Playbook engine. It enables technical and non-technical legal-ops users to define, configure, and run Actions across three invocation paths: conversational (via the Spaarke AI Assistant), explicit UI (ribbon buttons, command-bar tiles), and system-triggered (cron, Insights signals, Dataverse webhooks).

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan, WBS, discovered resources |
| [Design Spec](./design.md) | Multi-surface Assistant model, conceptual model, component surfaces |
| [Specification](./spec.md) | Functional + non-functional requirements, scope, acceptance gates |
| [Task Index](./tasks/TASK-INDEX.md) | All POML task files with parallel groups |
| [Current Task](./current-task.md) | Active task state (auto-updated by task-execute) |
| [Project AI Context](./CLAUDE.md) | Per-project ADRs, constraints, task execution protocol |
| [Coordination with Insights Engine](./coordination-assessment-with-insights-engine.md) | Signal envelope, Tool Registry stewardship, gate primitive sharing |
| [LAVERN Pattern Assessment](./lavern-pattern-assessment.md) | External reference (patterns adopted inline; no new ADR ratification) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Phase 0: Architecture Spike (task 001) |
| **Progress** | 5% — artifacts initialized; implementation pending task 001 outcome |
| **Branch** | `work/ai-spaarke-action-engine-r1` |
| **Target Date** | TBD (gated on Phase 0 spike outcome) |
| **Owner** | spaarke-dev |

## Problem Statement

Spaarke needs a unified surface for **authoring, discovering, and executing Actions** — small composable units of work that may be deterministic (Dataverse queries, email sends) or probabilistic (multi-step LLM agents). Today, AI capabilities are scattered: the JPS Playbook engine handles analysis but lacks invocation triggers; the Assistant exists in multiple surfaces but has no formal mechanism to discover or invoke arbitrary Tools; deterministic operations are hand-coded per use case. Users — particularly non-technical legal-ops — cannot author or schedule reusable Actions without engineering effort. The result is feature drift, duplicated effort across surfaces, and no consistent control plane for safety (gates, phase enforcement, audit).

## Solution Summary

The Action Engine introduces a unified Tool Registry that treats deterministic and probabilistic Tools as first-class peers, exposes Actions through three invocation paths (conversational, explicit, system), and enforces side-effect safety through a canonical `IGateResolver` primitive and mechanical Phase deny-tools enforcement at the dispatch layer. MVP delivers: BFF surface under `Services/Ai/ActionEngine/`; six new Dataverse entities; three meta-tools (`FindResources`, `GetResourceDetail`, `InvokeResource`) registered with every Assistant session; four `IGateResolver` implementations; a shared `GateApprovalCard` Fluent v9 component; and three starter Action Templates (Summarize Matter, Weekly Task Digest, Find Similar Matters). Pro-code authoring only in MVP; Visual Builder and Conversational Builder Agent defer to R2/R3.

## Graduation Criteria

The project is **complete** when all criteria from [spec.md §Success Criteria](./spec.md) are met:

- [ ] **G1**: Three starter Templates author + execute end-to-end via manual and scheduled invocation (integration test: `ActionEngine.IntegrationTests.StarterTemplates`)
- [ ] **G2**: Conversational invocation via SpaarkeAi resolves user intent → Tool (E2E test: "Summarize Matter X")
- [ ] **G3**: `IGateResolver` routes EthicsCritical / MeaningCritical / FinalDelivery gates; approval resumes; rejection terminates; 5-min timeout auto-rejects
- [ ] **G4**: Phase deny-tools throws `PhaseToolDeniedException` when an execute Tool is dispatched during authoring phase
- [ ] **G5**: `FindResources` p95 < 200ms over semantic index (NFR-01); verified via load test + App Insights metric
- [ ] **G6**: Every Tool dispatch writes audit per ADR-015 (Tier 2 hash + Tier 3 work history)
- [ ] **G7**: All 8 hallucination guardrails present and tested
- [ ] **G8**: BFF publish-size delta ≤ 5 MB (NFR-10)
- [ ] **G9**: No new HIGH-severity CVEs (`dotnet list package --vulnerable --include-transitive`)
- [ ] **G10**: Hybrid D runtime topology validated; scheduler choice documented in runtime ADR
- [ ] **G11**: Endpoint-filter authorization applied on every Action Engine endpoint
- [ ] **G12**: No CRUD-side caller injects Action Engine internals — all reach via `Services/Ai/PublicContracts/IActionEngineFacade.cs` (ADR-013 refined)

## Scope

### In Scope (MVP)

- BFF surface: `src/server/api/Sprk.Bff.Api/Services/Ai/ActionEngine/` + `AddActionEngineModule()` + `Services/Ai/PublicContracts/IActionEngineFacade.cs`
- Dataverse entities: `sprk_action`, `sprk_actiontemplate`, `sprk_actioninstance`, `sprk_actionrun`, `sprk_toolregistry`, `sprk_gate_approval`; extension of `sprk_aichatcontextmap`
- Endpoints: `ActionEndpoints.cs`, `ToolRegistryEndpoints.cs` (endpoint-filter auth per ADR-008)
- Background job: `ScheduledActionDispatchJobHandler.cs` (ADR-004 `IJobHandler<T>`)
- Azure AI Search index: `spaarke-resource-registry-index` on existing `spaarke-search-dev` service
- Meta-tools: `FindResources`, `GetResourceDetail`, `InvokeResource`
- Always-on tools: `SearchDocuments`, `QueryDataverse`, `GetCurrentEntityContext`
- IGateResolver: interface + 4 implementations + 5 gate types + 5-min timeout
- Phase deny-tools: mechanical enforcement at `IToolHandlerRegistry` dispatch layer
- Audit: extend `AuditEnrichmentMiddleware` for Tool dispatch (ADR-015 Tier 2 + Tier 3)
- Shared UI: `GateApprovalCard` in `Spaarke.UI.Components` (Fluent v9, ADR-021 dark-mode)
- SpaarkeAi extensions: `ChatLaunchContext` URL-param parsing; ribbon launchers on Matter/Project/Account/Contact
- Three starter Templates: Summarize Matter / Weekly Task Digest / Find Similar Matters
- Default `Spaarke Assistant — General` playbook (seed data with system prompt §7.4.3 of design.md)

### Out of Scope (deferred R2/R3)

- Visual Builder for non-technical Action authoring
- Conversational Builder Agent (Assistant-authored Actions from natural language)
- Microsoft Agent Framework as separate runtime (Hybrid D uses existing `SprkChatAgent` + `UseFunctionInvocation`)
- Hard rate-limit caps (MVP uses soft enforcement per ADR-016)
- Cross-solution layering rules for Templates (system → ISV → customer)
- M365 Copilot agent surface in MVP (declarative agent manifest stubs only)

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| Action Engine lives in BFF (not extracted as separate service) | ADR-013 decision criteria; latency budget <500ms; BFF audit/session state lifecycle | [bff-ai-extraction-assessment-2026-05-20.md](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) |
| CRUD-side consumers reach Action Engine via `PublicContracts/` facade | ADR-013 refined (2026-05-20) | spec.md §Placement Justification |
| Hybrid D runtime: BFF + Azure-native scheduler | Same pattern as Insights Engine Track B; no new runtime in MVP | spec.md §Technical Approach |
| Scheduler choice deferred to task 001 | Three viable options; needs empirical validation before commit | task 001 acceptance criteria |
| Adopt LAVERN patterns inline (GateResolver, Phase deny-tools, Tool Registry metadata) | External reference; no separate ADR ratification needed | [lavern-pattern-assessment.md](./lavern-pattern-assessment.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Publish-size delta exceeds 5 MB cap (NFR-10) | High (blocks deployment) | Medium | Task 001 spike measures empirically; consume AI Search via existing client; reuse `SprkChatAgent` not new runtime |
| Scheduler choice wrong; rework Phase 4 | Medium | Medium | Task 001 architecture spike validates choice before main MVP build |
| Tool Registry classification taxonomy not aligned with Insights Engine | Medium | Medium | Coordination doc tracks shared taxonomy; signal envelope ownership clarified |
| `IGateResolver` 5-min timeout misfires in long Actions | Medium | Low | Configurable per-Action; task 035 covers semantics |
| Audit volume from per-Tool-dispatch logging exceeds Cosmos quota | Medium | Low | Tier 2 (hash only) carries most volume; Tier 3 (work history) tenant-partitioned with GDPR erasure |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Existing `SprkChatAgent` + `UseFunctionInvocation` | Internal | Production | Reuse; extension point for multi-step probabilistic agent loops |
| Existing `ServiceBusJobProcessor` (ADR-004) | Internal | Production | Slot for `ScheduledActionDispatchJobHandler` |
| Existing `spaarke-search-dev` Azure AI Search service | External (Azure) | Production | Add `spaarke-resource-registry-index` |
| Insights Engine `InsightArtifact` signal envelope | Cross-project | In progress on `work/ai-spaarke-insights-engine-r1` | Joint ownership; coordinate before Phase 5 |
| JPS Playbook entities (`sprk_playbook`, `sprk_aichatcontextmap`) | Internal | Production | Action sits above (FK relationship); extend `sprk_aichatcontextmap` |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | spaarke-dev | Overall accountability |
| AI Agent | Claude Opus 4.7 | Implementation via task-execute |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-05-29 | 1.0 | Initial project artifacts generated via /project-pipeline | Claude Opus 4.7 |

---

*Initialized via `/project-pipeline projects/ai-spaarke-action-engine-r1`. Next: execute task 001 (architecture spike).*
