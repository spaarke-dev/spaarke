# Spaarke Insights Engine (r1)

> **Status**: Design — pre-implementation
> **Created**: 2026-05-19
> **Owner**: Spaarke Engineering

The **Insights Engine** is the technical layer that realizes the Memory + Inference layers of the Legal IQ Stack. It is the component responsible for turning organizational signals (matters, documents, decisions, outcomes) into context that can be served honestly to agents and users across multiple surfaces (Context pane, form widgets, ribbon flyouts, Outlook add-ins, etc.).

## What this folder is

Project documentation for the Insights Engine — design, architecture, inventories, decisions, and phased plan.

This is **not** the implementation. Implementation lives in `src/server/api/Sprk.Bff.Api/` (BFF integration) and a new Azure Functions project (TBD; out-of-band sync and extraction).

## Why an "Engine"

"Engine" honestly scopes this component: signals come in, derived context comes out. Sources (Dataverse, SPE, sessions, AI Search) and surfaces (Context pane, ribbon, Outlook, forms) are **not** part of the Engine. They are upstream/downstream of it. This boundary is the load-bearing architectural decision.

## Core taxonomy: three artifact types

Every piece of context the Engine produces is one of three things, each with a different trust profile, store, and presentation rule:

| Type | Source | Confidence | Where it lives | How it's presented |
|---|---|---|---|---|
| **Fact** | Deterministic computation over systems of record | 1.0 always | Live query OR materialized feature view | Stated directly. No hedging. |
| **Observation** | Probabilistic extraction by playbook/LLM at a milestone | 0.0–1.0 | Insight Index (AI Search) + Insight Graph (Cosmos Gremlin) | Stated with confidence + evidence |
| **Inference** | Synthesized on demand by the Insights Agent over Facts + Observations | 0.0–1.0 | Never authoritatively stored (cached only) | Stated with confidence, comparable set, reasoning |

## Substrate

| Concern | Decision |
|---|---|
| Vector + semantic retrieval | **Azure AI Search** (existing) |
| Relationships (entities + typed edges) | **Cosmos DB Gremlin API** |
| Live Facts | Direct Dataverse queries |
| Synthesis layer | **Custom Insights Agent in BFF** (not Foundry-hosted) |
| Dataverse → indexes sync | **Azure Functions** + Dataverse webhooks + scheduled reconciliation |
| Tenant isolation | **Physical per-tenant** (own resources, deployed via Bicep) |

ADR-001 was updated in commit `84cec9f9` to permit Azure Functions for this kind of out-of-band integration work.

## Documents

| File | Status | Purpose |
|---|---|---|
| [decisions.md](decisions.md) | ✅ Current | Anchor doc — read first. ~38 numbered decisions with rationale + design.md refs. |
| [design.md](design.md) | ✅ Current | Comprehensive design (13 sections, 1268 lines). |
| [ai-inventory.md](ai-inventory.md) | ✅ Current | DI-anchored inventory of existing AI services + Insights Engine reusable foundations. |
| [azure-inventory.md](azure-inventory.md) | ✅ Initial | Azure resource inventory + cost-savings flags. Three additional subscriptions still to inventory. |
| `phase-1-sync-wiring/` | ⏳ Pending | Phase 1 sub-project: Dataverse → AI Search sync (foundation). Created when Phase 1 starts. |

## Related

- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + when Functions are permitted
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [Legal IQ Stack article](https://spaarke.com/why-spaarke/the-iq-stack) — marketing positioning this Engine must honestly deliver
- [Three-pane unification project (r2)](../spaarke-ai-platform-unification-r2/) — design context that surfaced the need for this Engine
