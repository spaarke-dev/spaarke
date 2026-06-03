# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Status**: COMPLETE — Task 035 (Wave D6 / D-G4) — Index Scope Shape Migration ✅ 2026-06-03

---

## 🔄 Active — Task 035 (Wave D6 / D-G4)

| Field | Value |
|---|---|
| **Task** | 035 — D6 spaarke-insights-index scope shape generalization (hybrid: keep matterId + add entityType/entityId) |
| **POML** | `projects/ai-spaarke-insights-engine-r2/tasks/035-index-scope-shape-migration.poml` |
| **Rigor Level** | FULL (index schema migration + BFF writer/reader changes + NFR-08 backward-compat invariant) |
| **Estimated effort** | 2d |
| **Wave-item** | D-G4 (parallel with 032, 034) |
| **Dependencies** | 015 ✅ (Wave A6 design) |
| **Started** | 2026-06-03 |

### Approach
- **In-place schema additions** (cheaper than v2/back-fill since no actual Phase 1 Observations have been written to spaarke-insights-index yet — writer was scaffolded but no live ingest run completed). PUT operations on the index are idempotent and Azure AI Search supports adding new filterable string fields in place.
- Add top-level `scope` ComplexType with `matterId`, `entityType`, `entityId` fields.
- Writer (`ObservationIndexUpserter`) projects `Observation.Scope` to the new index `scope` field, **dual-writing** `matterId` for matter-subject observations per design-a6 §4.4.
- Reader (`IndexRetrieveNode`) accepts a new optional `subjectScope` config so callers can use the hybrid OR-filter from design-a6 §4.5.
- Feature flag `Insights:Index:DualWriteScope` (default `true`) toggles dual-write behavior for rollback per design-a6 §5.3.

### Decisions tracking
- D1: in-place schema add vs v2 index → **in-place**, because (a) no Phase 1 data persisted, (b) Azure AI Search supports adding new filterable fields in place, (c) avoids back-fill complexity, (d) Bicep PUT is idempotent.
- D2: top-level `scope` ComplexType vs `value/raw/scope` → **top-level**, matching design-a6 §4.5 filter notation `scope/matterId eq '...'`. The existing `value/raw/scope/{matterType,opposingCounsel}` is preserved for backward compat with any callers using those fields (currently zero).
- D3: feature flag name → `Insights:Index:DualWriteScope` (bool, default true). Controls writer emission of legacy `scope.matterId` for matter subjects.
- D4: deploy ordering documented in final report for owner-coordinated infra apply.

### Retirement scope
- DELETE: `IngestOrchestrator.cs` + `IIngestOrchestrator.cs` + `IInsightsPromptLoader.cs` + `InsightsPromptLoader.cs` + 3 .txt prompts + 2 .schema.json files + `InsightsIngestOptions.cs` (Decision 1 = delete flag entirely)
- DELETE: `tests/.../Ingest/IngestOrchestratorTests.cs` (14 tests for retired type)
- MODIFY: `InsightsOrchestrator.cs` (drop 2 ctor params 8→6; remove legacy fallback)
- MODIFY: `InsightsFacadeModule.cs` (remove options binding)
- MODIFY: `InsightsIngestModule.cs` (remove IIngestOrchestrator + IInsightsPromptLoader registrations)
- MODIFY: `InsightsOrchestratorTests.cs` (prune fallback tests, update ctor sigs)
- MODIFY: `PredictMatterCostPlaybookTests.cs` (update ctor sigs)
- MODIFY: csproj + comments in `Sprk.Bff.Api.csproj` (remove .txt + .schema.json content includes)

### Decisions
- **D1 (feature flag)**: A — Delete entirely. Kill-switch happens at playbook level (deploy a no-op playbook); not at orchestrator. Aligns D-P15-02 ("ONE canonical universal-ingest playbook").
- **D2 (EventIds)**: 8060 (routed-to-playbook) → DELETE (always-taken now, noise). 8061 (routed-to-legacy) → DELETE (no legacy path). 8062 (adapter-mismatch) → KEEP. 8063 (playbook-failed) → KEEP, reword as hard failure.
- **D3 (deploy ordering)**: Document in final report.

---

## ✅ Closed — Task 023 (Wave C-G3) — COMPLETE 2026-06-02

[See prior commits for details — facade rewire landed at 5d9b1eeb.]

---

## Wave sequencing (per owner direction WB-1)

Wave B FIRST ✅ → A ✅ → C 🔄 (022 active; closes wave) → D → E → wrap-up.

| Wave | Tasks | Status |
|---|---|---|
| **B** (Unblock synthesis) | 001–006 | ✅ COMPLETE |
| **A** (Foundations) | 010–015 | ✅ COMPLETE |
| **C** (JPS compliance) | 020–024 | 🔄 — 020/021/023/024 ✅; 022 in progress |
| **D** (2D taxonomy + multi-entity) | 030–036 | 🔲 |
| **E** (Hybrid + Assistant) | 040–043 | 🔲 |
| Wrap-up | 090 | 🔲 |
