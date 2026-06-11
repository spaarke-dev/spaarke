# Spaarke Insights Engine — Widgets r1

> **Status**: ✅ COMPLETE (2026-06-11) — 14 of 15 success criteria PASS; SC-12 (feedback affordance) DEFERRED to r2+ per Q-U3 owner resolution; SC-15 (owner walkthrough) ready for operator sign-off per `notes/handoffs/production-deploy.md` §7. See [`notes/lessons-learned.md`](notes/lessons-learned.md).
> **Predecessors**: [`ai-spaarke-insights-engine-r2`](../ai-spaarke-insights-engine-r2/) (substrate ✅ on master), [`bff-ai-architecture-audit-r1`](../bff-ai-architecture-audit-r1/) (canonical patterns ✅ on master), R5 Summarize (placeholder fields on `sprk_matter` ✅ on master)
> **Parallel projects**: [`ai-spaarke-insights-engine-r3`](../ai-spaarke-insights-engine-r3/) (PAUSED pending R6), [`spaarke-ai-platform-unification-r6`](../spaarke-ai-platform-unification-r6/) (in design)

---

## 1. Purpose

Deliver a **reusable, topic-scoped, subject-aware Insight Summary widget framework** with **Matter Health single-mode** as the first proven topic. Each insight invocation produces an AI-generated narrative with citations that is **persisted to the host record** for downstream consumption (reports, emails, notifications) and **pre-warmed on form load** for low-latency UX. The framework establishes the pattern; r2+ extend to additional topics, modes, and record types.

Product surface project, not platform.

## 2. What r1 ships

| Layer | Deliverable |
|---|---|
| **Framework** | Reusable `InsightSummaryCard` component in `@spaarke/ai-widgets` (5+ states: idle / loading / loaded / error / decline / stale) |
| **Registry** | `sprk_aitopicregistry` Dataverse entity mapping topics → playbooks → display config |
| **First topic** | **Matter Health single-mode** JPS playbook (`matter-health-single`) — 7-dimension diagnostic narrative |
| **Persistence** | JSON envelope written to existing `sprk_matter.sprk_performancesummary` (longtext) — no new fields |
| **Caching** | Per-topic TTL via `sprk_aitopicregistry.sprk_cachettlminutes` (default 60 min); reuses `IInsightsPlaybookExecutionCache` |
| **Pre-warm** | Matter form OnLoad handler triggers fire-and-forget invocation when stored summary is stale |
| **Telemetry** | New meter `Sprk.Bff.Api.InsightWidgets` emitting `widget.insightcard.invoked` events |
| **UAT** | End-to-end against real dev Matter + decline rendering + kill-switch + degraded mode |
| **Tutorial** | `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` for authoring next topic |

## 3. Hard out of scope (r2+ candidates)

- Workspace narrative widgets (cross-record aggregation)
- Topics other than `matter-health`
- Analysis modes other than `single` (portfolio + comparative)
- Record types other than Matter
- Actionable citations (depends on r3 Tier 2.4)
- Multi-turn / bidirectional clarification (depends on r3 Tier 2.1)
- Real-time push-based insight updates (SSE for mid-stream UI)
- Scheduled batch refresh
- **Feedback affordance (thumbs up/down + free text)** — DEFERRED to r2+ pending AIPU2 Cosmos `feedback` container per ADR-015 (Q-U3 resolution 2026-06-10)
- New BFF PublicContracts facade (uses existing `IInsightsAi` directly)
- New node executor types (uses existing executors)
- New ADRs (operate within audit-codified constraints)

## 4. Status

| Phase | Status |
|---|---|
| Project initialized | ✅ 2026-06-10 |
| design.md owner iteration | ✅ 2026-06-10 |
| spec.md generated via `/design-to-spec` | ✅ 2026-06-10 |
| 8 open questions resolved (Q-U1..Q-U8) | ✅ 2026-06-10 (`/project-pipeline` constrained run) |
| README.md / CLAUDE.md regenerated | ✅ 2026-06-10 |
| plan.md (8-phase WBS + Discovered Resources) | ✅ 2026-06-10 |
| Task POMLs | ✅ 2026-06-10 (see [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)) |
| Master rebased + branch synced | ✅ 2026-06-10 |
| Implementation (task-execute) — Phases 0–7 (Tasks 001–080) | ✅ 2026-06-11 |
| UAT (SC-05/06/07/08/10/11/14) | ✅ 2026-06-11 (see [`notes/uat-results/`](notes/uat-results/)) |
| Production-ready packaging (Task 080) | ✅ 2026-06-11 (45.99 MB / 14.01 MB NFR-01 headroom — see [`notes/handoffs/production-deploy.md`](notes/handoffs/production-deploy.md)) |
| Lessons-learned + project wrap-up (Task 090) | ✅ 2026-06-11 (see [`notes/lessons-learned.md`](notes/lessons-learned.md)) |
| Owner walkthrough sign-off (SC-15) | 🟡 Ready — operator gate per [`notes/handoffs/production-deploy.md`](notes/handoffs/production-deploy.md) §7 |
| Feedback affordance (SC-12) | ⏭️ DEFERRED to r2+ per Q-U3 (AIPU2 Cosmos `feedback` per ADR-015) |
| Visible card render (FR-19) | 🟡 PARTIAL — IIFE bundle deferred to r2 P1 retrofit per [`notes/lessons-learned.md`](notes/lessons-learned.md) §3.1 |

**Overall**: r1 framework + first topic delivered. 14 of 15 SCs PASS; 1 DEFERRED (Q-U3 owner decision); 1 OPERATOR GATE (SC-15).

## 5. Working artifacts

| File | Purpose | Status |
|---|---|---|
| [`design.md`](design.md) | Framework + Matter Health design (source of truth) | ✅ stable |
| [`spec.md`](spec.md) | Implementation specification with FRs / NFRs / SCs + Resolution Decisions | ✅ stable (edits applied 2026-06-10) |
| [`plan.md`](plan.md) | 8-phase WBS + Discovered Resources | ✅ stable |
| [`CLAUDE.md`](CLAUDE.md) | Project-scoped AI context (mandatory `task-execute` protocol + discovered resources) | ✅ stable |
| [`current-task.md`](current-task.md) | Active task tracker (`task-execute` updates this) | ✅ reset to `none` (project complete) |
| [`tasks/`](tasks/) | Task POMLs + TASK-INDEX with parallel groups | ✅ 41 of 41 in-scope tasks complete (SC-12 deferred → not generated; SC-15 operator-gated) |
| [`notes/`](notes/) | Spikes, handoffs, drafts; ~14 handoffs + 6 UAT walkthroughs + lessons-learned | ✅ populated |
| [`decisions/`](decisions/) | Decision records (DR-001 component reuse) | ✅ DR-001 authored |

## 6. Dependencies

### Hard dependencies (must be healthy)

| Dependency | Source | Status |
|---|---|---|
| Insights Engine substrate: `IInsightsAi.AnswerQuestionAsync` (playbook path) | r2 Wave E ([PR #337](https://github.com/spaarke-dev/spaarke/pull/337)) | ✅ on master |
| Multi-entity subject scheme (`matter:GUID`) | r2 Wave D ([PR #336](https://github.com/spaarke-dev/spaarke/pull/336)) | ✅ on master |
| PublicContracts facade pattern + Null peers | Audit PR #351 + #360 | ✅ on master |
| Existing R5 placeholder field `sprk_performancesummary` on `sprk_matter` | R5 Summarize | ✅ on master |
| `Deploy-Playbook.ps1` deployment tooling | r2 Wave B | ✅ proven |
| `sprk_playbook` + `sprk_playbooknode` tables | Existing platform | ✅ exists |
| 18 existing node executors (`UpdateRecord`, `AiAnalysis`, `GroundingVerify`, etc.) | Audit | ✅ exists |
| `sprk_kpiassessment` entity + Matter roll-up fields | Existing platform | ✅ exists |
| `Spaarke.AI.Widgets` package (v0.1.0) — destination for `InsightSummaryCard` | Existing | ✅ exists |
| `AiSummaryPopover` extant pattern (reuse reference) | Existing in `Spaarke.UI.Components` | ✅ exists |

### Soft dependencies (improve UAT realism)

| Dependency | Source | Notes |
|---|---|---|
| `spaarke-files-index` → `spaarke-insights-index` pipeline healthy | Current debugging stream | Insights narratives degrade gracefully to "insufficient evidence" Decline when Observations index is sparse. r1 ships with degraded-mode UAT (FR-26). |
| `AiProcessingOptions:InsightsIngest=true` env config | DevOps | Same — D-P8 SPE-upload consumer must be enabled in target env for end-to-end UAT |

### Explicitly NOT dependencies

- R6 Pillar 3 `IInvokePlaybookAi` facade — r1 uses `IInsightsAi.AnswerQuestionAsync` directly
- R6 Pillar 5 schema-aware renderers — r1 defines its own rendering
- R6 Pillar 6 tri-directional workspace state model — r1 is per-record
- R6 Pillar 9 workspace widget visibility contract — r1 is per-record
- r3 Wave 2 InsightsIntentClassifier reconciliation — r1 uses playbook path directly
- r3 Tier 2.4 actionable citations — r1 ships display-only citations
- AIPU2 Cosmos `feedback` container — feedback affordance deferred to r2+ per Q-U3

This isolation is deliberate: r1 ships before R6/r3/AIPU2 close.

## 7. Graduation criteria

r1 is complete when **all 15 success criteria from [spec.md](spec.md#success-criteria) pass** (except SC-12 which is DEFERRED to r2+ per Q-U3). Owner walkthrough sign-off (SC-15) is the final gate.

---

*Project initiated 2026-06-10 per owner direction "pivot to widget delivery; keep r1 scope tight to one surface." Plan + tasks generated 2026-06-10 via `/project-pipeline` constrained run. Project closed 2026-06-11 via Task 090 wrap-up. r1 graduated to r2+ backlog feed; see [`notes/lessons-learned.md`](notes/lessons-learned.md) §3.*
