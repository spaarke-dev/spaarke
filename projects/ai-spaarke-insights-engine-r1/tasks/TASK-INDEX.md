# Task Index — Spaarke Insights Engine, Phase 1

> **Generated**: 2026-05-28 via /project-pipeline Step 3
> **Source**: [SPEC.md](../SPEC.md) §3.1 D-P1..D-P17 + §8 phasing waves
> **Total tasks**: 24 (17 D-P deliverables + 1 W3.5 refactor + 1 Q5 side-quest + facade scaffold + deploy + wrap-up)
> **Universal task driver**: `task-execute` per root CLAUDE.md §4 (MANDATORY)

---

## Status legend

🔲 not-started · 🔄 in-progress · ✅ complete · 🚧 blocked · ⏭️ deferred (Phase 1.5+)

---

## Wave-organized index

### Wave 1 — Foundation types (parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [001](001-insight-artifact-envelope.poml) | D-P1 | InsightArtifact envelope POCOs (four-tier) | ✅ | 3h | ✅ | — |
| [002](002-iinsightgraph-interface.poml) | D-P17 | IInsightGraph interface + stub (Cosmos defers to Phase 1.5) | ✅ | 2h | ✅ | — |

### Wave 2 — Infrastructure provisioning (mostly parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [010](010-bicep-insights-index-and-shell.poml) | D-P2 | Bicep modules — spaarke-insights-index + Function App shell + single-tenant params | ✅ | 1d | ✅ | — |
| [011](011-sprk-precedent-entity.poml) | D-P3 (entity) | sprk_precedent Dataverse entity + relationship tables | ✅ | 4h | ✅ | — |
| [012](012-precedent-admin-endpoint.poml) | D-P3 (endpoint) | POST /api/insights/admin/precedents admin endpoint | ✅ | 4h | ❌ (needs 011) | 011 |

### Wave 3 — Platform primitives + node executors (parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [020](020-grounding-verifier.poml) | D-P9 | GroundingVerifier + GroundingVerifyNode | ✅ | 1d | ✅ | 001 |
| [021](021-confidence-gating-emission.poml) | D-P10 | Confidence threshold gating + per-field Observation emission | ✅ | 6h | ✅ | 001 |
| [022](022-insights-mode-node-executors.poml) | D-P12 | 5 Insights-mode node executors (LiveFact/IndexRetrieve/EvidenceSufficiency/DeclineToFind/ReturnInsightArtifact — GroundingVerify shipped in 020) | ✅ | 2d | ✅ | 001, 002, 020 |
| [023](023-insights-playbook-cache.poml) | D-P13 | Insights playbook execution cache (Redis wrap of PlaybookExecutionEngine) | ✅ | 6h | ✅ | — |
| [024](024-generic-cache-helper.poml) | Q5 side-quest | Generic IDistributedCacheExtensions.GetOrCreateAsync&lt;T&gt; helper | ✅ | 3h | ✅ | — |

### Wave 3.5 — Reuse refactor (serial; small)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [025](025-reference-indexing-parameterization.poml) | W3.5 | Parameterize ReferenceIndexingService for index name + schema mapper | ✅ | 4h | ✅ (vs other waves) | — |

### Wave 4 — Layer prompts (parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [030](030-layer1-classification-prompt.poml) | D-P5 | Layer 1 document classification node + classification@v1 prompt | ✅ | 4h | ✅ | 022 |
| [031](031-layer2-outcome-extraction-prompt.poml) | D-P6 | Layer 2 outcome extraction node + outcome-extraction@v1 prompt | ✅ | 1d | ✅ | 022 |

### Wave 5 — Ingest orchestration + Precedent projection + facade scaffold (parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [040](040-universal-ingest-playbook.poml) | D-P7 | Universal ingest playbook (Layer 1 → Layer 2 → gates → emission) | ✅ | 1d | ✅ | 020, 021, 022, 025, 030, 031, 042 |
| [041](041-precedent-projection-sync.poml) | D-P4 | Precedent → spaarke-insights-index projection sync | ✅ | 4h | ✅ | 011, 012, 025 |
| [042](042-iinsights-ai-facade.poml) | facade | IInsightsAi facade + InsightsOrchestrator (Zone A) | ✅ | 4h | ✅ | 022, 023 |

### Wave 6 — SPE consumer + review surface (mostly parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [050](050-spe-upload-consumer.poml) | D-P8 | SPE-upload event consumer (BackgroundService or Function) | ✅ | 1d | ✅ | 042 |
| [051](051-observation-mirror-sync.poml) | D-P11 (mirror) | Observation mirror sync to sprk_analysis polymorphic | ✅ | 4h | ✅ | 021, 025 |
| [052](052-observation-review-surface.poml) | D-P11 (view) | Dataverse model-driven view + disposition workflow | ✅ | 6h | ✅ | 051 |

### Wave 7 — Synthesis + endpoint (parallel)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [060](060-predict-matter-cost-playbook.poml) | D-P14 | predict-matter-cost synthesis playbook (end-to-end) | ✅ | 1d | ✅ | 020, 021, 022, 023, 041 |
| [061](061-insights-ask-endpoint.poml) | D-P15 | POST /api/insights/ask endpoint via IInsightsAi facade | ✅ | 4h | ✅ | 042 |

### Wave 8 — Smoke + eval baseline (serial)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [070](070-smoke-test-golden-dataset-eval.poml) | D-P16 | End-to-end smoke + golden dataset + eval harness baseline | ✅ | 1d | ❌ | 040, 050, 060, 061 |

### Wave 8.5 — Pre-deploy functional gap fix (serial)

| ID | D-P | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|---|
| [071](071-pre-deploy-functional-gap-fix.poml) | pre-deploy-fix | DataverseLiveFactResolver + DeclineResponse extraction from engine stream | ✅ | 6-8h | ❌ | 040, 042, 060, 061 |

### Deploy + wrap-up

| ID | Title | Status | Estimated | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| [080](080-deploy-to-spaarke-dev.poml) | Deploy Phase 1 to Spaarke Dev | 🔲 | 4h | ❌ | 070, 071 |
| [090](090-project-wrap-up.poml) | Project wrap-up + lessons-learned + Phase 1.5 outline | 🔲 | 3h | ❌ | 080 |

---

## Parallel execution groups (for `task-execute` parallel dispatch)

| Group | Tasks | Prerequisite | Max concurrency | Notes |
|---|---|---|---|---|
| W1 | 001, 002 | — | 2 | Pure types/interface; no shared files |
| W2 | 010, 011 | — | 2 | Bicep + entity — independent |
| W2-seq | 012 | 011 | 1 | Admin endpoint needs entity |
| W3 | 020, 021, 023, 024 | — for 023/024; 001 for 020/021 | 4 | All in different files; mostly independent |
| W3 (delayed) | 022 | 020 | 1 | Needs GroundingVerifyNode from 020 |
| W3.5 | 025 | — | 1 | Single refactor; blocks 041, 051 |
| W4 | 030, 031 | 022 | 2 | Different prompt templates |
| W5 | 040, 041, 042 | varies (see deps) | 3 | Can mostly run together once deps met |
| W6 | 050, 051 | 042 for 050; 021+025 for 051 | 2 | 052 follows 051 |
| W6-seq | 052 | 051 | 1 | Review view needs mirror sync |
| W7 | 060, 061 | varies (see deps) | 2 | Synthesis playbook + endpoint |
| W8 | 070 | 040, 050, 060, 061 | 1 | End-to-end; can't parallelize |
| W8.5 | 071 | 040, 042, 060, 061 | 1 | Pre-deploy gap fix; serial after W8 |
| Deploy | 080 | 070, 071 | 1 | Live deployment; serial |
| Wrap-up | 090 | 080 | 1 | Final task |

**Max concurrency per skill hard limit**: 6 agents per wave (tune only with evidence per project-pipeline skill).

---

## Critical path

001 → 020 → 022 → 030/031 → 040 → 050 → 060 → 061 → 070 → 080 → 090

Estimated critical path: **~10-12 working days** with parallelism.

---

## High-risk items

| Risk | Mitigation | Affected tasks |
|---|---|---|
| Layer 1 + Layer 2 prompts unreliable on real documents | D-P10 confidence gating + D-P11 review surface calibration loop | 030, 031, 052 |
| Live-ingest LLM cost projection unacceptable | First-step blocker on D-P8 requires admin sign-off | 050 |
| `spaarke-files-index` chunks don't expose verbatim text needed for GroundingVerifier | Verify against fixtures in W3 D-P9 acceptance | 020 |
| SPE-upload event mechanism not as expected | First-step blocker on D-P8; pause if doesn't match SPEC assumptions | 050 |
| `sprk_analysis` polymorphic pattern not in use for Observation mirroring | First-step blocker on D-P11; raise as design question if pattern missing | 051 |
| Coordination conflict with sdap-bff-api-remediation-fix Outcome E facade timing | DEP-7 coordination; manual grep gate at W7 acceptance until FR-C6 CI gate lands | 042, 050, 061, 070 |

---

## First-step blockers (resolved as task Step 0)

Per SPEC §6.1 — each task's POML resolves its blocker before proceeding to implementation:

| Blocker | Task | Method |
|---|---|---|
| spaarke-insights-index final name | 010 | SME confirmation |
| SME review of document-type taxonomy (Layer 1) | 030 | SME review |
| Confidence threshold starter values (Layer 2) | 031 | Product/SME confirmation |
| SPE-upload event source + dispatch shape + auth | 050 | Architecture confirmation |
| Production live-ingest cost projection | 050 | Admin/finance sign-off |
| sprk_analysis polymorphic source-type for Observations | 051 | mcp__dataverse__describe_table |
| Observation review sampling percentage | 052 | Admin confirmation |
| Phase 1 Precedent seeding count | 011 | SME engagement |

---

## Quick start

To begin Phase 1:

```
work on task 001
```

For parallel Wave 1 (recommended):

```
execute tasks 001 and 002 in parallel
```

The `task-execute` skill will load each POML, knowledge files, ADRs, and apply FULL rigor per root CLAUDE.md §8.

---

## Cross-references

- [SPEC.md](../SPEC.md) — canonical Phase 1 scope (D-P1..D-P17)
- [plan.md](../plan.md) — wave-organized implementation plan (this index is the per-task drill-down)
- [decisions.md](../decisions.md) — 63 numbered decisions (D-52..D-63 for 2026-05-28 direction)
- [SPEC-phase-1-minimum.md](../SPEC-phase-1-minimum.md) — rationale narrative + Precedent mockup + prompt templates
- [CLAUDE.md](../CLAUDE.md) — project-scoped instructions (boundaries, mandatory protocols)
- [current-task.md](../current-task.md) — active task state tracker
