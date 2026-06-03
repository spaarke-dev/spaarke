# Current Task — Spaarke Insights Engine Phase 1.5 (r2)

> **Purpose**: Active task state tracker. Managed by `task-execute` skill.
> **Status**: IN PROGRESS — Task 022 (Wave C-G4) — Retire IngestOrchestrator.cs

---

## 🔄 Active — Task 022 (Wave C3 / C-G4)

| Field | Value |
|---|---|
| **Task** | 022 — C3 Retire IngestOrchestrator.cs + orphaned interfaces |
| **POML** | `projects/ai-spaarke-insights-engine-r2/tasks/022-retire-ingest-orchestrator.poml` |
| **Rigor Level** | FULL (BFF code deletion + ctor change + DI cleanup + test pruning) |
| **Estimated effort** | 4h |
| **Wave-item** | C-G4 (serial; closes Wave C) |
| **Dependencies** | 020 ✅, 023 ✅ |
| **Started** | 2026-06-02 |

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
