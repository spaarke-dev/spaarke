# Implementation Plan — Spaarke Insights Engine, Phase 1

> **Status**: Pipeline-ready (2026-05-28)
> **Source**: Derived from [SPEC.md](SPEC.md) §8 phasing
> **Companion**: [README.md](README.md) (project overview), [SPEC.md](SPEC.md) (canonical scope), [decisions.md](decisions.md) (rationale)

---

## Overview

Phase 1 ships the **Insights Engine context production service** end-to-end against real documents: universal layered ingest produces Observations from SPE-uploaded documents; SME-authored Precedents enrich the synthesis; `predict-matter-cost` synthesis playbook composes Live Facts + Observations + Precedents into a grounded Inference with `GroundingVerifier` enforcement. **17 deliverables (D-P1..D-P17)** across **8 waves**, executable in parallel where dependencies allow.

**Acceptance bar** (per SPEC §1): A single API call returns either a structurally-honest grounded Inference or a structured `DeclineResponse` — composed from Observations extracted from actual SPE documents by Phase 1's ingest pipeline. Not infrastructure with mock data.

---

## Phase 1 goals (from SPEC §2)

1. Provision the new derived `insights-index` (one index, discriminator-based) + Bicep infrastructure (single-tenant per D-52)
2. Implement `InsightArtifact` envelope POCOs (four-tier per D-03/D-46)
3. Implement universal layered ingest playbook (Layer 1 classification + conditional Layer 2 outcome extraction; cheap-gates-expensive per D-59)
4. Implement three mechanical post-processing gates: `GroundingVerifier`, confidence threshold gating, per-field Observation emission
5. Ship Observation review surface (mandatory per D-60)
6. Implement `sprk_precedent` entity + admin endpoint for manual SME authoring (Phase 1 mode of D-61)
7. Implement `LiveFactResolverService` + new Insights-mode node executors (D-P12)
8. Implement `predict-matter-cost` synthesis playbook end-to-end with evidence-sufficiency + insufficient-evidence paths
9. Expose `POST /api/insights/ask` through `IInsightsAi` facade (Zone A/B boundary per SPEC §3.5)
10. Smoke test + golden dataset + eval harness baseline against real Observations

---

## Phase breakdown

Waves are sequential; tasks within a wave run in parallel where dependencies allow. Each task is a separate POML file; the universal task driver is `task-execute` per root CLAUDE.md §4.

### Wave 1 — Foundation types (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 001 | D-P1 | `InsightArtifact` envelope POCOs (Fact / Observation / Precedent / Inference) in `Models/Insights/` |
| 002 | D-P17 | `IInsightGraph` interface + DTOs + `NotImplementedException` stub in `Services/Insights/Graph/` |

**Gates**: pure POCOs/interface; no external deps. **Acceptance**: types compile, registered in DI.

### Wave 2 — Infrastructure provisioning (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 010 | D-P2 | Bicep modules for `insights-index` + Function App shell + single-tenant parameter file pattern (D-A28) |
| 011 | D-P3 (entity) | `sprk_precedent` Dataverse entity + relationship tables via `dataverse-create-schema` skill |
| 012 | D-P3 (endpoint) | `POST /api/insights/admin/precedents` admin endpoint (JWT auth per ADR-008) |

**First-step blockers**: 010 → confirm `insights-index` final name; 011 → confirm Phase 1 Precedent seeding count from SME. **Acceptance**: Bicep deploys clean to Spaarke Dev; `sprk_precedent` queryable; admin endpoint creates Precedent.

### Wave 3 — Platform primitives + node executors (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 020 | D-P9 | `GroundingVerifier` mechanical citation check in `Services/Ai/CitationVerification/` + `GroundingVerifyNode` wrapper |
| 021 | D-P10 | Confidence-threshold gating + per-field Observation emission post-processor in `Services/Ai/Insights/Extraction/` |
| 022 | D-P12 | 6 new Insights-mode node executors in `Services/Ai/Nodes/` (LiveFactNode, IndexRetrieveNode, EvidenceSufficiencyNode, DeclineToFindNode, GroundingVerifyNode, ReturnInsightArtifactNode) + DI registration |
| 023 | D-P13 | Insights playbook execution caching in `Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` (wraps `PlaybookExecutionEngine`) + ADR-009 compliance |
| 024 | Q5 side-quest | Optional: extract generic `IDistributedCacheExtensions.GetOrCreateAsync<T>` helper (parallel-safe; benefits whole codebase) |

**Acceptance**: each primitive unit-tested in isolation; node executors registered and discoverable.

### Wave 3.5 — Refactor for reuse (serial; small)

| Task | D-P ID | Deliverable |
|---|---|---|
| 025 | (refactor) | Parameterize `ReferenceIndexingService` for index name + schema mapper so both `spaarke-rag-references` and `insights-index` use one code path (~half day) |

**Acceptance**: existing reference indexing behavior unchanged; new parameterized API ready for D-P2 + D-P4 + D-P11.

### Wave 4 — Layer prompts (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 030 | D-P5 | Layer 1 document-classification node config — playbook node authored against existing `AiAnalysisNodeExecutor` + `classification@v1` prompt template per SPEC-phase-1-minimum.md §3.3 |
| 031 | D-P6 | Layer 2 outcome-extraction node config — playbook node + `outcome-extraction@v1` prompt template per SPEC-phase-1-minimum.md §3.4 |

**First-step blockers**: 030 → SME review of document-type taxonomy; 031 → product/SME confirmation of confidence thresholds. **Acceptance**: each prompt produces typed JSON output against fixture documents.

### Wave 5 — Ingest orchestration + Precedent projection (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 040 | D-P7 | Universal ingest playbook authoring (Layer 1 → conditional Layer 2 → gates → emission); playbook definition + `Services/Ai/Insights/IngestOrchestrator.cs` |
| 041 | D-P4 | Precedent → `insights-index` projection sync (small job, fires on `sprk_precedent` status → Confirmed); uses W3.5 parameterized `ReferenceIndexingService` |
| 042 | (facade) | `IInsightsAi` facade scaffold at `Services/Ai/PublicContracts/IInsightsAi.cs` with `AnswerQuestionAsync` + `RunIngestAsync` method stubs; impl in `Services/Ai/Insights/InsightsOrchestrator.cs` (Zone A) |

**Acceptance**: ingest playbook executes against a fixture document (mock SPE event); Precedent projection round-trips.

### Wave 6 — SPE consumer + review surface (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 050 | D-P8 | SPE-upload event consumer (BackgroundService or Function per ADR-001) in `Services/Jobs/SpeUploadConsumer.cs` — calls `IInsightsAi.RunIngestAsync` |
| 051 | D-P11 (mirror) | Observation mirror sync — writes one `sprk_analysis` row per Observation as side-effect of ingest playbook |
| 052 | D-P11 (view) | Dataverse model-driven view for Observation review surface + disposition workflow (Correct/Incorrect/Unclear) |

**First-step blockers**: 050 → confirm SPE-upload event source + dispatch shape + auth context; production live-ingest cost projection; 051 → confirm `sprk_analysis` polymorphic source-type via Dataverse MCP; 052 → confirm sampling percentage. **Acceptance**: end-to-end ingest smoke (fixture closing-letter upload → Observations in `insights-index` + mirrored to `sprk_analysis`).

### Wave 7 — Synthesis + API endpoint (parallel)

| Task | D-P ID | Deliverable |
|---|---|---|
| 060 | D-P14 | `predict-matter-cost` synthesis playbook authoring — Insights-mode metadata (tier=inference, evidence rule `comparableMatters.min: 12`, insufficient-evidence template, cache TTL); node graph composing LiveFactNode + IndexRetrieveNode (cohort + Precedents) + EvidenceSufficiencyNode + AiAnalysis + GroundingVerifyNode + ReturnInsightArtifactNode; decline path via DeclineToFindNode |
| 061 | D-P15 | `POST /api/insights/ask` endpoint in `Api/Insights/InsightEndpoints.cs` — calls `IInsightsAi.AnswerQuestionAsync` (Zone B; no direct `IChatClient` / `PlaybookExecutionEngine`); endpoint filter (ADR-008) + rate limiting (ADR-016) + ProblemDetails (ADR-019) |

**Acceptance**: end-to-end synthesis smoke (`POST /api/insights/ask` returns Inference or `DeclineResponse`); §3.5 facade grep passes (zero Zone-B → AI-internal imports).

### Wave 8 — Smoke + eval baseline

| Task | D-P ID | Deliverable |
|---|---|---|
| 070 | D-P16 | Smoke test (3–5 fixture closing-letters → ingest → review → synthesize cycle) + 10–15 golden tuples for `predict-matter-cost` + eval harness baseline run (RAG-triad metrics permissive Phase 1 thresholds) |

**Acceptance**: all 5.1 SPEC acceptance criteria pass; `GroundingVerifier` strips one synthetic bad citation; `DeclineToFindNode` produces structured `DeclineResponse` on low-evidence path.

### Deployment + wrap-up

| Task | Deliverable |
|---|---|
| 080 | Deploy Phase 1 to Spaarke Dev (Bicep + BFF redeploy + Dataverse solution import); verify all 5.1 acceptance criteria pass against deployed environment |
| 090 | Project wrap-up — update README to "Phase 1 complete"; create `notes/lessons-learned.md`; archive intermediate artifacts; outline Phase 1.5 spec (Cosmos as first deliverable) |

---

## Parallel execution groups (high-level)

Detailed groups in `tasks/TASK-INDEX.md`. Headline:

| Wave | Parallel? | Notes |
|---|---|---|
| W1 (001, 002) | ✅ Yes | Both pure types/interfaces; no shared files |
| W2 (010, 011, 012) | ⚠️ Partial | 011 + 012 can parallel; 010 (Bicep) independent. 012 needs 011 complete. |
| W3 (020, 021, 022, 023, 024) | ✅ Yes | All in different files; node executors in 022 may need iteration but each executor file is separate |
| W3.5 (025) | Serial | Single refactor; blocks W5 (D-P4) and W6 (D-P11 mirror) |
| W4 (030, 031) | ✅ Yes | Different prompt templates, separate playbook node configs |
| W5 (040, 041, 042) | ✅ Yes | Different files; 042 facade scaffold needed before W6 (D-P8) |
| W6 (050, 051, 052) | ⚠️ Partial | 051 + 052 can parallel; 050 needs 042 facade complete |
| W7 (060, 061) | ✅ Yes | Different files; 061 endpoint can scaffold against facade while 060 playbook authoring iterates |
| W8 (070) | Serial | End-to-end test; needs all prior waves complete |

**Max concurrency**: 6 agents per wave (skill hard limit; tune only with evidence).

---

## Dependencies summary

- **DEP-3**: `accessibleMatterSet` source resolution (within-tenant trimming) — needed for W7 D-P14 + W3 D-P13 cache-key scope hash
- **DEP-5**: LAVERN ADRs 10.1 (Precedent), 10.6 (Sanitization + Citation Verification) ratification — needed for W3 D-P9 design freeze
- **DEP-7**: `Services/Ai/PublicContracts/` facade scaffold from `sdap-bff-api-remediation-fix` Outcome E task 046 — needed for W5 D-P15 facade compliance; interim manual grep gate at W7 acceptance until FR-C6 CI gate lands (Phase 6 task 082)

Per SPEC §6.1: DEP-1, DEP-2, DEP-4 (Phase C auth, closure-extraction format) are **dissolved in Phase 1** under the D-P architecture. They return as Phase 1.5 dependencies if/when system-proposed Tentative Precedent workflow needs Dataverse change-tracking.

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Layer 1 + Layer 2 prompts produce unreliable extractions on real documents (false positives / missed signals) | D-P10 confidence-threshold gating + D-P11 mandatory review surface (sample-based QA) is the calibration loop; prompts are versioned (D-62) so iteration is mechanical |
| R2 | Production live-ingest LLM cost exceeds budget at projected document volumes | First-step blocker on D-P8 requires cost projection; D-59 cheap-gates-expensive economics bound the cost (Layer 1 cheap; Layer 2 conditional on outcome-bearing classifications) |
| R3 | `spaarke-files-index` chunk shape doesn't expose the verbatim text needed for `GroundingVerifier` substring/sliding-window match | Substring + sliding-window matching is designed for chunked content; verify against fixture documents in W3 D-P9 acceptance |
| R4 | SPE-upload event mechanism not what we expect (different auth model than Service Bus; different dispatch shape) | First-step blocker on D-P8 — pause if event mechanism doesn't match SPEC §3.1 D-P8 assumptions |
| R5 | `sprk_analysis` polymorphic pattern not yet wired for Observation mirroring | First-step blocker on D-P11 — confirm via Dataverse MCP before mirror sync work; raise back as design question if pattern not in use |
| R6 | Coordination conflict with `sdap-bff-api-remediation-fix` Outcome E facade scaffold timing | Both projects coordinate via DEP-7; facade scaffold (task 046) lands post-Phase-3 baseline closure; manual grep gate at W7 acceptance covers interim period |

---

## Cross-references

- [SPEC.md](SPEC.md) §3 (deliverables), §5 (acceptance), §6 (dependencies), §8 (phasing), §9 (references)
- [decisions.md](decisions.md) D-52..D-63 (2026-05-28 spec-refinement)
- [SPEC-phase-1-minimum.md](SPEC-phase-1-minimum.md) — rationale narrative for D-59..D-63 + Precedent mockup + prompt templates
- [design.md](design.md) §0 — refinement-integration table for understanding stale design-doc text vs current direction
- [docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §0 — Spaarke-wide architecture context
