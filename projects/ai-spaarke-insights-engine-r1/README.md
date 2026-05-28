# Spaarke Insights Engine (r1)

> **Status**: Design complete; pipeline-ready (2026-05-28)
> **Created**: 2026-05-19 · **Last Aligned**: 2026-05-28
> **Owner**: Spaarke Engineering

The **Insights Engine** is **Spaarke's context production service** — the system that produces, persists, and serves structured contextual claims about the organization's work, with provenance, confidence where applicable, and evidence-sufficiency rules where applicable. Context production includes (a) **deterministic claims** computed from source data, (b) **probabilistic claims** extracted from document content via LLM, and (c) **synthesized claims** combining the above. AI is one technique among several; the Engine orchestrates all of them uniformly through one envelope (`InsightArtifact`), one facade (`IInsightsAi`), and one execution path (Insights-mode playbooks on the existing `PlaybookExecutionEngine`).

## What this folder is

Project documentation for the Insights Engine — design, architecture, inventories, decisions, and the Phase 1 spec.

This is **not** the implementation. Implementation lives in `src/server/api/Sprk.Bff.Api/` (BFF integration) and a new SPE-upload consumer (per D-P8 — BackgroundService or Function per ADR-001).

## Why an "Engine"

"Engine" honestly scopes this component: signals come in, derived context comes out. The Engine **owns** the resolver (`InsightsOrchestrator`), synthesis layer, universal ingest playbook, `spaarke-insights-index` (derived AI Search index), and the Precedent layer. It **consumes — but does not own** source systems (Dataverse, SPE, AI Search operational substrate) as signal inputs. It **emits to — but does not own** presentation surfaces (which land in Phase 1.5+). This ownership boundary is the load-bearing architectural decision (D-01, D-02 in `decisions.md`).

## Core taxonomy: four artifact types

Every piece of context the Engine produces is one of four things, each with a different trust profile, store, and presentation rule. Phase 1 ships the envelope + active production of Facts, Observations, Precedents (via manual SME authoring), and Inferences end-to-end. Precedent lifecycle automation (Phase 1.5+ system-proposed Tentative mode) ships later. See `decisions.md` D-03 + D-46 + D-61, `SPEC.md` §4, and `SPEC-phase-1-minimum.md` §1 + §2 for full examples.

| Type | Source | Confidence | Where it lives | How it's presented |
|---|---|---|---|---|
| **Fact** | Deterministic computation over Dataverse (Live Facts on read via `LiveFactNode` per D-P12) | 1.0 always | Live query — no persistence in Phase 1 (no projection sync) | Stated directly. No hedging. |
| **Observation** | LLM extraction (D-P6 Layer 2 outcome extraction) from documents with verbatim quote, gated by confidence threshold (D-P10) and `GroundingVerifier` (D-P9) | 0.75–1.0 (above per-field thresholds; lower-confidence dropped) | `spaarke-insights-index` (AI Search) with `artifactType: "observation"` — discriminator | Stated with confidence + evidence quote + source-document link |
| **Precedent** | Phase 1: manual SME authoring via D-P3 admin endpoint. Phase 1.5+: system-proposed Tentative from nightly cluster job + SME refinement (D-61). | SME-confirmed (no probabilistic confidence) | `sprk_precedent` Dataverse entity (system of record) + `spaarke-insights-index` (read-optimized projection per D-P4) with `artifactType: "precedent"` | Stated as institutional rule with supporting-matter count + confirmation status + drill-through to basis matters |
| **Inference** | Synthesized on demand by `predict-matter-cost` Insights-mode playbook (D-P14) over Live Facts + Observations + Precedents | 0.0–1.0 | Never authoritatively stored (cached via D-P13) | Stated with confidence, comparable set, citable Precedents, reasoning. Insufficient → structured `DeclineResponse` per D-49 |

## Substrate

| Concern | Decision (2026-05-28 canonical) |
|---|---|
| Vector + structured retrieval | **Azure AI Search** — ONE new derived index (`spaarke-insights-index`) with `artifactType` discriminator (Observations + Precedents). D-53 revised from prior 5-index framing. Operational substrate (`spaarke-files-index`, `spaarke-records-index`, `spaarke-invoices-index`, `spaarke-rag-references`) consumed as-is — `spaarke-files-index` read by ingest (Layer 2) + grounding verification. |
| Graph (entities + typed edges) | **`IInsightGraph` interface ships in Phase 1 (D-P17 — stub)**; **`CosmosNoSqlInsightGraph` implementation is first Phase 1.5 deliverable**. Adjacency-list pattern per D-09; NOT Gremlin. |
| Precedent layer | `sprk_precedent` Dataverse entity (system of record) + projection sync to `spaarke-insights-index` on Confirmed (D-P4). Phase 1: manual SME authoring (D-P3). Phase 1.5+: system-proposed Tentative + lifecycle automation (decay, drift, promotion). |
| Live Facts | Direct Dataverse queries via `LiveFactResolverService` wrapped in `LiveFactNode` (D-P12). No projection writing in Phase 1 (Mode A removed per D-58 superseded). |
| Document extraction | **Universal layered ingest** on every SPE upload (D-P7 + D-P8). Layer 1 classification (D-P5 cheap, runs always); Layer 2 outcome extraction (D-P6, conditional on Layer 1 classification + confidence ≥ 0.7). Cheap layers gate expensive ones per D-59. |
| Synthesis layer | **Insights questions are Spaarke playbooks** executed by existing `PlaybookExecutionEngine` with Insights-mode metadata + caching (D-P13). NO parallel question-catalog system per D-54. |
| Honesty enforcement primitives | `ISanitizer` (D-A25/D-50 ingest) + `GroundingVerifier` (D-P9 mechanical citation check) + `EvidenceGuard.Validate` (D-A23/D-48 runtime guard) + `IDeclineToFindTool` (D-A24/D-49 deterministic decline, realized as `DeclineToFindNode` in D-P12) + confidence-threshold gating (D-P10/D-63). |
| Tenant isolation | **Single-tenant Phase 1** (D-52) — each customer = one Bicep parameter file = one full deployment unit. `tenantId` retained on derived index for partition keys + audit + future federation. |
| Observation review | **MANDATORY** Phase 1 (D-60) — Dataverse model-driven view + sample-based QA + disposition workflow per D-P11. Without it the honesty contract is performative. |

ADR-001 was updated in commit `84cec9f9` to permit Azure Functions for this kind of out-of-band integration work (D-P8 consumer).

## Documents

| File | Status | Purpose |
|---|---|---|
| [SPEC.md](SPEC.md) | ✅ **CANONICAL Phase 1 spec** (2026-05-28) | 17 deliverables D-P1..D-P17. Pipeline-ready. Read first for Phase 1 scope. |
| [decisions.md](decisions.md) | ✅ Current (2026-05-28) | 63 numbered decisions (D-01..D-38 baseline + D-39..D-45 r2 + D-46..D-51 LAVERN + D-52..D-63 spec-refinement). Read first for rationale. |
| [design.md](design.md) | ✅ Current; §0 refinement integration table | Comprehensive design (13 sections). §0 callout explains where text below conflicts with 2026-05-28 direction — SPEC.md wins. |
| [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) | ✅ Canonical rationale narrative for 2026-05-28 direction | Concrete examples (Precedent mockup, prompt templates, four-tier examples). SPEC.md cross-references this rather than duplicating. |
| [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) | ⚠️ HISTORICAL (partially superseded 2026-05-28) | 2026-05-27 refinement narrative. D-52 + D-54 stand; D-53 revised; D-55/D-56/D-57 deferred; D-58 superseded. Header on doc explains. Preserved for traceability. |
| [ai-inventory.md](ai-inventory.md) | ✅ Current | DI-anchored inventory of existing AI services. Informed Q5 duplication audit. |
| [azure-inventory.md](azure-inventory.md) | ✅ Initial | Azure resource inventory + cost-savings flags. |
| [lavern-pattern-assessment.md](lavern-pattern-assessment.md) | ✅ Current | LAVERN pattern-by-pattern analysis backing D-46..D-51. |
| [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) | ✅ Spaarke-wide architecture; §0 refinement integration callout | r2 body (2026-05-20) with 2026-05-28 §0 callout pointing readers at SPEC.md for current Phase 1 scope. |

## Where to start

- **For Phase 1 implementation scope**: read [SPEC.md](SPEC.md) §3 (deliverables) and §5 (acceptance).
- **For decisions and rationale**: read [decisions.md](decisions.md), especially D-52..D-63 for the 2026-05-28 narrowing.
- **For concrete examples** (Precedent mockup, prompt templates, four-tier examples): read [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) §1, §2, §3.3, §3.4.
- **For comprehensive design context**: read [design.md](design.md) §0 first (refinement integration table), then the body as architecture reference.
- **For Spaarke-wide architecture**: read [INSIGHTS-ENGINE-ARCHITECTURE.md](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0 first (refinement integration), then the r2 body.

## Related

- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + when Functions are permitted
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [Legal IQ Stack article](https://spaarke.com/why-spaarke/the-iq-stack) — marketing positioning this Engine must honestly deliver
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — AI facade source-of-truth (SPEC §3.5, DEP-7)
- [`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`](../ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md) — source of LAVERN patterns + ADR proposals 10.1, 10.3, 10.6
- [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) — sister project; consumer of `GroundingVerifier` (D-P9) and Precedents; canonical owner of `IGateResolver` primitive
