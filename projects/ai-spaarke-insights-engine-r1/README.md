# Spaarke Insights Engine (r1)

> **Status**: Design — pre-implementation
> **Created**: 2026-05-19
> **Owner**: Spaarke Engineering

The **Insights Engine** is the technical layer that realizes the Memory + Inference layers of the Legal IQ Stack. It is the component responsible for turning organizational signals (matters, documents, decisions, outcomes) into context that can be served honestly to agents and users across multiple surfaces (Context pane, form widgets, ribbon flyouts, Outlook add-ins, etc.).

## What this folder is

Project documentation for the Insights Engine — design, architecture, inventories, decisions, and phased plan.

This is **not** the implementation. Implementation lives in `src/server/api/Sprk.Bff.Api/` (BFF integration) and a new Azure Functions project (TBD; out-of-band sync and extraction).

## Why an "Engine"

"Engine" honestly scopes this component: signals come in, derived context comes out. The Engine **owns** the resolver, synthesis layer, sync/extraction Functions, Insight Index, Insight Graph, and Precedent Board. It **consumes — but does not own** source systems (Dataverse, SPE, sessions, AI Search) as signal inputs; data flows from them into the Engine via well-defined interfaces, but their schemas/lifecycles are not the Engine's responsibility. It **emits to — but does not own** presentation surfaces (Context pane, ribbon, Outlook, forms) as downstream consumers. This ownership boundary is the load-bearing architectural decision (D-01, D-02 in `decisions.md`).

## Core taxonomy: four artifact types

Every piece of context the Engine produces is one of four things, each with a different trust profile, store, and presentation rule. Phase 1 ships architecture + scaffold for all four tiers; Precedent lifecycle automation ships in Phase 1.5. See `decisions.md` D-03 + D-46, `INSIGHTS-ENGINE-ARCHITECTURE.md` §3, and `lavern-pattern-assessment.md` §3.1 for full rationale.

| Type | Source | Confidence | Where it lives | How it's presented |
|---|---|---|---|---|
| **Fact** | Deterministic computation over systems of record | 1.0 always | Live query OR materialized feature view | Stated directly. No hedging. |
| **Observation** | Probabilistic extraction by playbook/LLM at a milestone | 0.0–1.0 | Insight Index (AI Search) + Insight Graph references | Stated with confidence + evidence |
| **Precedent** | Cross-matter pattern that has accumulated supporting Observations and (in Phase 1.5+) been SME-confirmed as a firm-level rule | 0.0–1.0, refined over time | `sprk_precedent` entity + `insight-precedents` AI Search index + `Precedent` vertices in graph; lifecycle: Tentative → Confirmed → UnderDriftReview → Deprecated → Retired | Stated as institutional rule with evidence count, confirmation status, supporting matters, last-reviewed date |
| **Inference** | Synthesized on demand by the Insights Agent over Facts + Observations + Precedents | 0.0–1.0 | Never authoritatively stored (cached only) | Stated with confidence, comparable set, citable Precedents, reasoning |

## Substrate

| Concern | Decision |
|---|---|
| Vector + semantic retrieval | **Azure AI Search** (existing) — 5 indexes: `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`, `insight-precedents` |
| Relationships (entities + typed edges, with Precedent vertex + edges) | **Cosmos NoSQL adjacency-list** (decisions.md D-09; NOT Gremlin) |
| Precedent Board | `sprk_precedent` Dataverse entity + relationship tables + Cosmos `Precedent` vertex + `insight-precedents` AI Search index. Phase 1 = scaffold; Phase 1.5 = lifecycle automation (decay, promotion, drift). |
| Live Facts | Direct Dataverse queries |
| Synthesis layer | **Custom Insights Agent in BFF** (not Foundry-hosted) |
| Dataverse → indexes sync | **Azure Functions** + Dataverse webhooks + scheduled reconciliation |
| Honesty enforcement primitives | `ISanitizer` (ingest) + `GroundingVerifier` (post-Agent citation check) + `EvidenceGuard.Validate` (runtime non-empty guard) + `IDeclineToFindTool` (deterministic decline path) — LAVERN-derived; see `lavern-pattern-assessment.md` |
| Tenant isolation | **Physical per-tenant** (own resources, deployed via Bicep) |

ADR-001 was updated in commit `84cec9f9` to permit Azure Functions for this kind of out-of-band integration work.

## Documents

| File | Status | Purpose |
|---|---|---|
| [decisions.md](decisions.md) | ✅ Current | Anchor doc — read first. Numbered decisions with rationale: D-01–D-38 (baseline) + D-46–D-51 (LAVERN-derived). Note: D-39–D-45 are claimed by architecture-doc r2 expansion and pending back-propagation (see DEF-15). |
| [design.md](design.md) | ✅ Current | Comprehensive design (13 sections, 1268 lines). |
| [SPEC.md](SPEC.md) | ✅ Current | Phase 1 spec; deliverables D-A1–D-A14 (baseline) + D-A15–D-A21 (architecture-doc r2 evaluation/surfacing/MCP) + D-A22–D-A27 (LAVERN-derived primitives + Precedent scaffold). |
| [ai-inventory.md](ai-inventory.md) | ✅ Current | DI-anchored inventory of existing AI services + Insights Engine reusable foundations. |
| [azure-inventory.md](azure-inventory.md) | ✅ Initial | Azure resource inventory + cost-savings flags. Three additional subscriptions still to inventory. |
| [lavern-pattern-assessment.md](lavern-pattern-assessment.md) | ✅ New (2026-05-22) | LAVERN pattern-by-pattern analysis + decision basis for Phase 1 expansion. Captures verdicts on all 12 patterns from `ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`. |
| `phase-1-sync-wiring/` | ⏳ Pending | Phase 1 sub-project: Dataverse → AI Search sync (foundation). Created when Phase 1 starts. |

## Related

- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + when Functions are permitted
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [Legal IQ Stack article](https://spaarke.com/why-spaarke/the-iq-stack) — marketing positioning this Engine must honestly deliver
- [Three-pane unification project (r2)](../spaarke-ai-platform-unification-r2/) — design context that surfaced the need for this Engine
- [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) — source of LAVERN patterns + ADR proposals 10.1–10.6
- [`projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md`](../ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md) — six user-interaction modes the Engine + Action Engine support
- [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) — sister project; consumer of Insights signals + Precedents; canonical owner of `IGateResolver` primitive
- [`projects/ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md`](../ai-spaarke-action-engine-r1/coordination-assessment-with-insights-engine.md) — joint decisions (§4.1–§4.8 including LAVERN-derived §4.6, §4.7, §4.8)
