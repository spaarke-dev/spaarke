# Agent findings — Insights Engine + Action Engine projects (2026-07-05)

Read at operator request (feedback pt 2): do the five umbrella projects claim the SpaarkeAi insights
renderer cluster, and what commitments must the target architecture honor?

## VERDICT on the ~14-file SpaarkeAi insights renderer cluster

**Genuinely dead — verified against project plans, not assumed.**
- **Provenance**: created by **R5** (`spaarke-ai-platform-unification-r5`), commit `01359b36f` / PR #345
  (2026-06-04, task 026 "D2-16 InsightsResponseRenderer"). NOT by any Insights/Action project.
- **Zero references** to the cluster files in any of the five projects' .md corpus.
- `ConversationPane.tsx` contains no reference to `insights` — never mounted.
- R5 closed "with known limitations", deferring renderer completion to **R6 Pillar 5** — and R6 built
  its OWN schema-aware renderer path without importing the R5 cluster (zero hits in R6 folder).
- insights-engine-r2 shipped only the SERVER contract the cluster would have consumed
  (`POST /api/insights/assistant/query` v1.0/v1.1 SSE + citations); r3 is paused with no tasks;
  widgets-r1 **explicitly renders via its own `InsightSummaryCard`** and lists "R6 Pillar 5
  schema-aware renderers" as NOT a dependency; action-engine-r1 unrelated.
- **Disposition for Step 3 Track B: DELETE** (cluster + its tests). The locked Assistant tool-call
  contract v1.1 (server side) is separate and STAYS.

## Project status snapshot

| Project | Status | Note |
|---|---|---|
| ai-spaarke-action-engine-r1 | **Not-started** (Phase 0 spike gate; dormant since 2026-05-29; 0/46 tasks) | Predates R5/R6; parked |
| ai-spaarke-insights-engine-r1 | **Complete + deployed** (Phase 1) | 17/17 D-P deliverables |
| ai-spaarke-insights-engine-r2 | **Complete** (closed 2026-06-04; PRs #330/334/336/337/339 + Wave F) | Built Assistant contract v1.0/v1.1 for R5 |
| ai-spaarke-insights-engine-r3 | **Paused** (2026-06-10; no tasks authored) | Resumes on: R6 ships P3+5+6 AND owner direction AND widgets-r1 proof-point |
| ai-spaarke-insights-engine-widgets-r1 | **Complete** (2026-06-11, PR #378) | InsightSummaryCard + sprk_aitopicregistry; deliberate isolation from R6/r3 |

## Commitments the umbrella target architecture MUST honor

### Insights Engine (r1/r2/widgets-r1 — shipped, load-bearing)
1. **Four-artifact `InsightArtifact` envelope** with distinct trust/store rules: Fact (1.0, live query),
   Observation (0.75-1.0, `spaarke-insights-index`, evidence quote + source link), Precedent
   (`sprk_precedent` SoR + index projection), Inference (never authoritatively stored; structured
   `DeclineResponse` when evidence insufficient).
2. **`IInsightsAi` facade is the single Zone-A entry point**; Zone-B namespace firewall
   (InsightsIngestJobHandler may import only IInsightsAi). Observation review MANDATORY (D-60);
   mirror to `sprk_analysis` polymorphic.
3. **Honesty primitives**: GroundingVerifier (mechanical citation check), EvidenceSufficiency,
   DeclineToFind, ISanitizer, confidence-threshold gating — implemented as node executors (70-120).
4. **r2 doctrine: "Insights IS a JPS application"** — NO parallel orchestrators; all workflows are
   JPS playbook DATA on `PlaybookExecutionEngine`; r2 explicitly RETIRED a code orchestrator
   (`IngestOrchestrator.cs`) in favor of the `universal-ingest@v1` playbook; prompts live in
   `sprk_analysisaction.sprk_systemprompt`. **⚠️ Direct counter-evidence for OQ-2 option (c)** —
   Insights moved TOWARD data-defined execution, opposite of the Wave 11/12 code-defined lesson.
5. **Locked Assistant tool-call contract v1.1** (`design-e3-tool-call-contract.md`): playbook
   inference / RAG answer / decline shapes + citations[].href + SSE. Hybrid consumption:
   `/api/insights/ask` (playbook) + `/api/insights/search` (RAG) + LLM intent classifier.
6. **widgets-r1 topic-registry pattern**: `sprk_aitopicregistry` (topic→playbook→display config +
   TTL cache) + `InsightSummaryCard` in `@spaarke/ai-widgets` + envelope persisted to host record
   (`sprk_matter.sprk_performancesummary`) + form-load pre-warm + `Sprk.Bff.Api.InsightWidgets`
   telemetry meter. The extensibility contract for future topics.
7. **Known debt r3 parked**: `InsightsIntentClassifier ↔ PlaybookDispatcher` reconciliation (~1 wk)
   — lands naturally inside the target single-dispatcher consolidation (canonical doc C-03);
   `spe://` href resolution; index rename; SC-15 SME calibration.

### Action Engine (r1 — NOT built; plan overlaps the target design heavily)
Planned (46 tasks, none started): `Services/Ai/ActionEngine/` + `IActionEngineFacade`; SIX new
Dataverse entities (`sprk_action`, `sprk_actiontemplate`, `sprk_actioninstance`, `sprk_actionrun`,
`sprk_toolregistry`, `sprk_gate_approval`); meta-tools FindResources/GetResourceDetail/InvokeResource;
`spaarke-resource-registry-index`; 4 `IGateResolver` impls + 5 gate types; `GateApprovalCard`;
scheduled dispatch handler; 3 starter templates. Commitments: canonical owner of `IGateResolver`;
Phase deny-tools at `IToolHandlerRegistry`; PublicContracts-only access.

**⚠️ Overlap alert for Step 3 / §8**: Action Engine R1's plan predates the target design and
duplicates much of it — `sprk_toolregistry` vs the extended `sprk_analysistool` (D-target Tool
catalog); gates/`IGateResolver` vs `ConfirmationGateService` (C-04/D12); meta-tool
FindResources/InvokeResource vs the L3 planner over the Tool catalog; `sprk_action*` entities vs
the extended Consumer catalog. Since R1 is at Phase 0 with zero code, the cheapest resolution is
to RE-BASE Action Engine R1's spec on the target architecture (its Phase-0 spike becomes "validate
the target design covers its FRs") rather than build its parallel vocabulary. Its genuinely novel
contributions to absorb: gate taxonomy (5 types + timeout + resolver plurality), action
templates/instances/runs lifecycle model, scheduled dispatch, resource-registry search.
