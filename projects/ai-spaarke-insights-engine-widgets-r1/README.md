# Spaarke Insights Engine — Widgets r1

> **Status**: 🆕 DESIGN PHASE (initiated 2026-06-10)
> **Predecessors**: [`ai-spaarke-insights-engine-r2`](../ai-spaarke-insights-engine-r2/) (Phase 1.5 — ✅ Complete; r2 shipped the Insights Engine substrate this project builds on), [`bff-ai-architecture-audit-r1`](../bff-ai-architecture-audit-r1/) (Phase 4 — ✅ Complete; locked the PublicContracts facade + Endpoint↔DI Symmetry patterns this project follows)
> **Parallel projects**: [`ai-spaarke-insights-engine-r3`](../ai-spaarke-insights-engine-r3/) (Insights Engine Phase 2 cleanup — PAUSED pending R6), [`spaarke-ai-platform-unification-r6`](../spaarke-ai-platform-unification-r6/) (architectural convergence — in design)

---

## 1. Purpose

Surface the Spaarke Insights Engine to end users through a **reusable, topic-scoped, subject-aware** Insight Summary widget pattern. r1 establishes the framework with one proven topic — **Matter Health** — and one proven analysis scope — **single-matter narrative**. r2+ extend to additional topics, additional analysis modes, and additional record types using the same pattern.

This is a **product surface** project, not a platform project. r2 (Insights Engine), the audit (BFF AI architecture), and R5 (Summarize integration) shipped the substrate. r1 builds on top.

## 2. What r1 ships

| Layer | Deliverable |
|---|---|
| **Framework** | Reusable `InsightSummaryCard` component pattern (sparkle icon → playbook invocation → narrative + citations) in `@spaarke/ai-widgets` |
| **First topic** | **Matter Health** insights driven by `sprk_kpiassessment` substrate + matter roll-up scores |
| **First subject scope** | **Single-matter** (`matter:GUID`) — diagnostic narrative explaining the matter's health grade |
| **JPS playbooks** | `matter-health-single` (and possibly `matter-health-portfolio` / `matter-health-comparative` if scope permits) |
| **End-to-end UAT** | Sparkle icon on Matter record's Matter Health card → narrative with citations to specific assessments + Notes excerpts |

## 3. Hard out of scope (r2+ candidates)

- Workspace narrative widgets (cross-record / portfolio aggregation in workspace context)
- Other record types (Project, Invoice, Communication) — Matter only in r1
- Other Matter topics (Budget Performance, Outcomes Success, Upcoming Tasks) — framework supports them, but only Matter Health authored in r1
- Actionable citations (`citations[].action`) — r1 ships display-only `citations[].href`
- Multi-turn / bidirectional clarification — r1 is one-shot
- Real-time / push-based insight updates — r1 is pull-on-sparkle-click + cache

## 4. Status

| Phase | Status |
|---|---|
| Design discussion (design.md) | 🔄 in flight |
| spec.md | 🔲 derives from design.md |
| plan.md (wave breakdown) | 🔲 derives from spec |
| Task POMLs | 🔲 not started |
| Implementation | 🔲 not started |

## 5. Working artifacts

| File | Purpose | Status |
|---|---|---|
| [`design.md`](design.md) | Framework + Matter Health design | 🔄 initial draft for iteration |
| [`CLAUDE.md`](CLAUDE.md) | Project-scoped AI context | 🔄 skeleton |
| [`current-task.md`](current-task.md) | Active task tracker | 🔄 initial |
| `spec.md` | Implementation specification | 🔲 deferred until design solidifies |
| `plan.md` | Wave structure | 🔲 deferred until spec exists |
| [`tasks/`](tasks/) | Task POMLs | 🔲 empty |
| [`notes/`](notes/) | Spikes, handoffs, decisions, drafts | 🔲 empty |
| [`decisions/`](decisions/) | Decision records | 🔲 empty |

## 6. Dependencies

### Hard dependencies (must be healthy)

| Dependency | Source | Status |
|---|---|---|
| Insights Engine substrate: `IInsightsAi.AnswerQuestionAsync` (playbook path) | r2 Wave E ([PR #337](https://github.com/spaarke-dev/spaarke/pull/337)) | ✅ on master |
| Insights Engine substrate: `IInsightsAi.SearchAsync` (RAG path, fallback option) | r2 Wave E ([PR #337](https://github.com/spaarke-dev/spaarke/pull/337)) | ✅ on master |
| `IInsightsAi.AssistantQueryStreamAsync` (SSE option) | r2 Wave F ([PR #339](https://github.com/spaarke-dev/spaarke/pull/339)) | ✅ on master |
| Multi-entity subject scheme (`matter:GUID`) | r2 Wave D ([PR #336](https://github.com/spaarke-dev/spaarke/pull/336)) | ✅ on master |
| PublicContracts facade pattern + Null peers | Audit PR #351 (LATENT BUG fix) | ✅ on master |
| Spaarke Public-Contracts Facade DI Fascia pattern | Audit PR #360 — [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) | ✅ on master |
| Endpoint↔DI Registration Conditionality Symmetry Rule | Audit PR #360 — [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) | ✅ on master |
| `sprk_kpiassessment` entity + Matter roll-up fields | Existing Spaarke platform (not r1 scope) | ✅ exists |

### Soft dependencies (improve UX but not blocking)

| Dependency | Source | Notes |
|---|---|---|
| Files-index → Insights-index pipeline working | Current debugging stream + D-P8 SPE-upload consumer config | Insights narratives degrade gracefully to "insufficient evidence" Decline when Observations index is sparse; widget r1 can demo with KPI roll-up data alone |
| R6 Pillar 3 `IInvokePlaybookAi` facade | R6 (in design) | r1 uses `IInsightsAi.AnswerQuestionAsync` directly; can switch to `IInvokePlaybookAi` in r2 if R6 ships first |

### Explicitly NOT dependencies

- R6 Pillar 5 (Output Schema / Schema-Aware Renderers) — r1 widgets define their own rendering
- R6 Pillar 6 (Tri-directional Workspace State Model) — r1 widgets render in record context, not workspace context
- R6 Pillar 9 (Workspace Widget Visibility Contract) — r1 widgets are per-record, not workspace-scoped
- r3 Wave 2 (InsightsIntentClassifier reconciliation) — r1 uses playbook path directly (no classifier needed)

This isolation is deliberate: it lets r1 ship before R6/r3 close, and lets R6/r3 progress without blocking on widget delivery.

---

*Project initiated 2026-06-10 in response to owner direction "pivot to widget delivery; keep r1 scope tight to one surface."*
