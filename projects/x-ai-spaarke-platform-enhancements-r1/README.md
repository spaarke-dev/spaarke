# AI Platform Foundation — Phase 1

> **Portfolio**: [Project #1265](https://github.com/spaarke-dev/spaarke/issues/1265) · [Board #2](https://github.com/users/spaarke-dev/projects/2) — _auto-registered 2026-10-04 (existence only; Epic/Task Count/Start Date not yet set)_

> **Last Updated**: 2026-02-22
>
> **Status**: In Progress

## Overview

Phase 1 delivers the AI platform foundation required for the June 2026 product launch. The platform has substantial working infrastructure but critical gaps: zero seed data, no clause-aware chunking, hand-rolled chat that doesn't scale, no evaluation harness, and no scope management UX. This project closes all four gaps across four parallel workstreams.

## Quick Links

| Document | Description |
|----------|-------------|
| [Specification](./spec.md) | AI-optimized technical specification |
| [Implementation Plan](./plan.md) | Phase breakdown and task dependencies |
| [Task Index](./tasks/TASK-INDEX.md) | All tasks with status and parallel groups |
| [Current Task](./current-task.md) | Active task state (context recovery) |
| [AI Architecture](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md) | Full AI architecture reference |
| [AI Strategy](../../docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md) | Product roadmap context |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Target Date** | 2026-06-01 (product launch gate) |
| **Completed Date** | — |
| **Owner** | Spaarke AI Team |

## Problem Statement

The AI platform has working infrastructure (Azure OpenAI, AI Search, PlaybookExecutionEngine, 10+ tool handlers, RagService, AnalysisWorkspace PCF, completed scope resolution + semantic search) but four critical gaps block the June 2026 launch: (1) zero seed data means the platform ships empty, (2) no clause-aware chunking degrades retrieval quality on legal documents, (3) hand-rolled chat in AnalysisWorkspace doesn't scale to multi-context conversations, and (4) no evaluation harness means quality is unmeasured.

## Solution Summary

Four parallel workstreams address these gaps. Workstream A builds the retrieval foundation with LlamaParse dual-parser and semantic chunking. Workstream B seeds the Scope Library with 8 Actions, 10 Skills, 10 Knowledge Sources, 8 Tools, 10 Playbooks, and deploys the ScopeConfigEditor PCF. Workstream C replaces the hand-rolled chat with Agent Framework-powered SprkChat. Workstream D validates everything end-to-end with an evaluation harness and establishes a reproducible quality baseline.

## Graduation Criteria

The project is considered **complete** when:

- [ ] 10 pre-built playbooks ship with product — selectable in AnalysisWorkspace, each executes against test document
- [ ] 8 Actions, 10 Skills, 10 Knowledge sources, 8 Tools seeded — Dataverse records exist, ScopeResolverService resolves all
- [ ] Clause-aware chunking deployed with LlamaParse option — chunks respect boundaries, parser router selects correctly
- [ ] Knowledge base management operational — admin CRUD works, documents indexed, search returns results
- [ ] Hybrid retrieval measurably improves over baseline — evaluation harness shows Recall@10 >= 0.7
- [ ] End-to-end playbook workflow passes for all 10 playbooks — automated test suite green
- [ ] Scope editor PCF deployed — admins configure scopes with validation on all 4 entity forms
- [ ] SprkChat deployed in AnalysisWorkspace — context switching, tool use, predefined prompts, highlight-and-refine work
- [ ] LlamaParse dual-parser evaluated — complex legal documents parse with >90% structural accuracy
- [ ] Quality baseline established — evaluation report generated for all 10 playbooks with reproducible scores

## Workstreams

| Workstream | Focus | Items | Blocks |
|------------|-------|-------|--------|
| **A: Retrieval Foundation** | LlamaParse, chunking, indexing pipeline, KB API | A1–A7 | D3, D4 |
| **B: Scope Library** | Seed data, ScopeConfigEditorPCF | B1–B7 | D1, D2 |
| **C: SprkChat** | Agent Framework, chat API, UI component | C1–C7 | D6 |
| **D: Validation** | Test corpus, E2E tests, evaluation harness | D1–D6 | Launch gate |

## Scope

### In Scope

- Workstream A: RagQueryBuilder, SemanticDocumentChunker, DocumentParserRouter + LlamaParseClient, RagIndexingPipeline, KnowledgeBaseEndpoints, two-index architecture, retrieval instrumentation
- Workstream B: 8 system Actions, 10 system Skills, 10 system Knowledge Sources, 8+ system Tools, 10 pre-built Playbooks, ScopeConfigEditorPCF control, handler discovery verification
- Workstream C: Agent Framework integration, SprkChatAgent + supporting services, chat tools, ChatEndpoints (SSE), SprkChat React component, AnalysisWorkspace integration, agent middleware
- Workstream D: Test document corpus, E2E playbook tests, evaluation harness, quality baseline, negative testing, SprkChat evaluation

### Out of Scope

- SprkChat on Workspace, Document Studio, Word Add-in, Matter Page surfaces (Phase 2)
- Deviation detection / Ambiguity detection / Multi-user annotations (Phase 2–4)
- Document version comparison / Prompt library management (Phase 2)
- Multi-agent orchestration (Phase 3)
- AI Foundry Prompt Flow activation (Phase 5)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Extend BFF, not separate AI service | Avoids microservice complexity; BFF already has auth, rate limiting | ADR-013 |
| Agent Framework RC (not wait for GA) | RC shipped Feb 19, 2026; API surface stable; enables Phase 1 | — |
| CodeMirror over Monaco for ScopeConfigEditor | Bundle size constraint < 1MB; Monaco is too heavy | ADR-022 |
| Redis for chat sessions, Dataverse for persistence | Hot cache for 24h idle; audit trail forever | ADR-009 |
| Two-index architecture (knowledge + discovery) | Different chunk sizes and population strategies for different query types | ADR-013 |
| LlamaParse dual-parser with fallback | Complex docs need LlamaParse; simple docs use cheaper Azure Doc Intel | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Agent Framework RC breaking changes before GA | High | Low | Pin to RC version; upgrade path in plan |
| LlamaParse API unavailable during dev | Medium | Low | Fallback to Azure Doc Intel; optional dependency |
| DI registration count exceeds 15 (ADR-010) | Medium | Medium | Feature modules; audit count after each phase |
| Dataverse chat entity schema ambiguity | Medium | High | Define schema in task 001; validate before C2 |
| SprkChat bundle > 500KB | Low | Medium | Code splitting; defer non-critical deps |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| ai-scope-resolution-enhancements | Internal | ✅ Complete | ScopeResolverService, handler discovery, GenericAnalysisHandler shipped |
| ai-semantic-search-ui-r2 | Internal | ✅ Complete | SemanticSearchControl PCF shipped |
| Azure AI Search S1 capacity | External | ✅ Ready | Two indexes fit within S1 tier |
| LlamaParse API key in Key Vault | External | 🔲 Needed | Must be provisioned before A3 |
| Microsoft Agent Framework RC NuGet | External | ✅ Available | Shipped Feb 19, 2026 |
| Dataverse scope entities | Internal | ✅ Exist | sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content, sprk_aiplaybook |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-22 | 1.0 | Project initialized from spec.md | AI Agent |

---

*AI Platform Foundation Phase 1 | Target: June 2026 product launch*
