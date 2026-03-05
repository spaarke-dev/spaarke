# AI Resource Activation & Integration (R3)

> **Status**: In Progress
> **Branch**: work/ai-resource-activation-r3
> **Created**: 2026-03-04
> **Estimated Effort**: ~47 hours across 5 phases

## Purpose

Activate the underutilized Azure AI resources built in R1 Phases 1-4. Create a tiered knowledge architecture with dedicated golden reference index, wire knowledge retrieval into playbook execution, fix hardcoded model selection, and establish embedding governance.

## Predecessor Projects

- **ai-spaarke-platform-enhancements-r1** (Phases 1-4 complete) — Built AI infrastructure
- **ai-json-prompt-schema** (complete) — Structured prompt authoring (JPS)
- **Related**: post-deployment-work.md — R1 Phase 5 validation (runs AFTER this project)

## Key Deliverables

1. `spaarke-rag-references` — Dedicated Azure AI Search index for golden reference knowledge
2. KNW-001–010 deployed and vectorized — 10 curated knowledge sources indexed
3. Knowledge-augmented playbook execution — L1/L2/L3 retrieval wired into action nodes
4. Model selection fix — `GenericAnalysisHandler` uses `ModelSelector` chain (no hardcoded `gpt-4o`)
5. Embedding governance — Legacy cleanup, change protocol documented

## Phases

| Phase | Focus | Est. |
|-------|-------|------|
| 1 | Index Architecture & Cleanup | 10h |
| 2 | Golden Reference Deployment | 12h |
| 3 | Knowledge-Augmented Execution | 14h |
| 4 | Model Selection Integration | 8h |
| 5 | Embedding Governance | 3h |

## Graduation Criteria

- [ ] `spaarke-rag-references` index exists with ~100 chunks from 10 knowledge sources
- [ ] 3 deprecated indexes removed
- [ ] All 10 playbook actions retrieve L1 knowledge during execution
- [ ] Analysis quality measurably improves with reference context
- [ ] `GenericAnalysisHandler` uses `ModelSelector` chain
- [ ] gpt-4o, gpt-4o-mini, o1-mini confirmed on Azure OpenAI
- [ ] `spaarke-records-index` populated
- [ ] 4 of 5 active indexes have data

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Document](design.md)
- [Post-Deployment Work](post-deployment-work.md)
