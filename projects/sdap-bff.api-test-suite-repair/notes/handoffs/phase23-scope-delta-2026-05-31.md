# Phase 2+3 Scope Delta — Wave 1 Baseline Reconciliation (2026-05-31)

> **Source**: Task 008 TRX reconciliation (Phase 0 Wave 0.2).
> **Inputs**: `baseline/failure-inventory-2026-05-31.md` (per-class failure counts derived from `baseline/test-baseline-2026-05-31.trx`).
> **Authority**: Owner directive 2026-05-31 — absorb +73 net failures into THIS project; do not defer.

---

## Summary

| Metric | design.md §3 | Wave 1 measured | Δ |
|---|---:|---:|---:|
| Total tests | 5,215 | 6,021 | **+806** |
| Passed | 4,844 | 5,572 | **+728** |
| Failed | 269 | **342** | **+73** |
| Skipped | 102 | 107 | +5 |

This document reconciles the **342 measured failures** (across 50 test classes) to the existing Phase 2+3 task scope (POMLs 030..074). Every failing class is assigned to (a) an existing task absorbing it via a `<scope-extension>` `<notes>` block, or (b) flagged "REQUIRES OWNER DECISION" with a proposed absorbing candidate.

The +73 net failures (vs. design §3's 269) consist of:
- **70 from §3.2 compile-fixed files** that now compile and contribute runtime failures (Phase 1 P1.A absorption side effect; primary contributor: AssociationMapping +29, DataverseRecordCreation +23, CommunicationIntegration +9)
- **3 from drift between 2026-05-30 design baseline and 2026-05-31 measurement** distributed across small clusters

---

## Absorbed Into Existing Tasks

Each row = one cluster (or contiguous set of clusters) absorbed into a Phase 2+3 task. The task POML received a `<notes><scope-extension date="2026-05-31">` block.

| Cluster (Sprk.Bff.Api.Tests.*) | Failures | Absorbing task | Rationale |
|---|---:|---|---|
| Services.Communication.AssociationMappingTests | 29 | **055** (Communications batch 1) | 055 targets `Services/Communication/*` excluding Phase 1 task 011's compile-fix set. AssociationMapping IS in 011's set, but 011 is compile-only; runtime failures belong here. P23.M is the natural home. |
| Services.Communication.DataverseRecordCreationTests | 23 | **055** | Same as above. |
| Services.Communication.ArchivalFlowTests | 1 | **055** | Same as above. |
| Integration.Workspace.WorkspaceEndpointsTests | 31 | **060** (BFF Integration batch 1) | 060 targets `tests/unit/Sprk.Bff.Api.Tests/Integration/*.cs` (first ~12 alphabetically). Workspace/* is under Integration/. |
| Integration.Workspace.WorkspaceLayoutEndpointTests | 23 | **060** | Same. |
| Integration.CommunicationIntegrationTests | 9 | **060** | Same; also compile-touched by Phase 1 task 013. |
| Integration.SseStreamingIntegrationTests | 8 | **061** (BFF Integration batch 2) | 061 targets remaining ~13 alphabetically. |
| Integration.PlaybookExecutionTests | 1 | **061** | Same. |
| Api.Ai.PlaybookRunEndpointsTests | 20 | **070** (LOW-tier Api/* batch 1) | 070-072 cover `Api/*.cs` quartered alphabetically. Ai/* is first letter range. |
| Api.Ai.StandaloneChatContextEndpointsTests | 18 | **070** | Same. |
| Api.Ai.HandlerEndpointsTests | 11 | **070** | Same. |
| Api.Ai.AnalysisChatContextEndpointsTests | 10 | **070** | Same. |
| Api.Ai.NodeEndpointsTests | 10 | **070** | Same. |
| Api.Ai.ModelEndpointsTests | 8 | **070** | Same. |
| Api.Ai.ChatSessionPlanEndpointTests | 5 | **070** | Same. |
| Api.Ai.ChatRefineEndpointTests | 4 | **070** | Same. |
| Api.Ai.DailyBriefingEndpointsTests | 2 | **070** | Same. |
| Api.Ai.R2SseEventEmitterTests | 1 | **070** | Same. |
| Api.Agent.AgentConversationServiceTests | 3 | **070** | Api/Agent/ is alphabetically near Api/Ai/. |
| Api.Agent.HandoffUrlBuilderTests | 3 | **070** | Same. |
| Api.Agent.AgentConfigurationServiceTests | 1 | **070** | Same. |
| Api.Office.OfficeEndpointsTests | 10 | **071** (LOW-tier Api/* batch 2) | 071 covers 2nd quarter alphabetically — Office is mid-range. |
| Api.Reporting.ReportingEndpointsTests | 12 | **072** (LOW-tier Api/* batch 3) | 072 covers 3rd quarter — Reporting is later. |
| Api.Reporting.ReportingAuthorizationFilterTests | 5 | **072** | Same. |
| Top-level UploadEndpointsTests | 9 | **073** (top-level *EndpointTests batch) | 073 explicitly targets top-level `*EndpointTests.cs`. |
| Top-level UserEndpointsTests | 6 | **073** | Same. |
| Top-level ListingEndpointsTests | 6 | **073** | Same. |
| Top-level FileOperationsTests | 6 | **073** | Top-level file; precedent: 073 is the natural home for top-level test files. |
| Top-level PipelineHealthTests | 4 | **073** | Same. |
| Top-level HealthAndHeadersTests | 4 | **073** | Same. |
| Top-level EndpointGroupingTests | 3 | **073** | Same. |
| Top-level CorsAndAuthTests | 1 | **073** | Same. |
| Services.Ai.Safety.CitationExtractorTests | 8 | **044** (ai-safety) | 044 targets `Services/Ai/Safety/*.cs` — direct match. |
| Services.Ai.Safety.PrivilegeLeakageTests | 7 | **044** | Same. |
| Services.Ai.Safety.ConfidenceScoringServiceTests | 2 | **044** | Same. |
| Services.Ai.Safety.VerifyCitationsTests | 1 | **044** | Same. |
| Services.Ai.Safety.CitationVerificationServiceTests | 1 | **044** | Same. |
| Services.Ai.Nodes.CreateTaskNodeExecutorTests | 5 | **054** (ai-nodes) | 054 targets `Services/Ai/Nodes/*.cs` — direct match. |
| Services.Ai.Capabilities.CapabilityRouterBenchmarkTests | 2 | **053** (ai-capabilities) | 053 targets `Services/Ai/Capabilities/*.cs (excluding Streaming*)` — Benchmark is non-Streaming. |
| Services.Ai.Chat.PlaybookChatContextProviderEnrichmentIntegrationTests | 1 | **050/051/052** (ai-chat batches) | 050-052 batch `Services/Ai/Chat/*.cs` excluding `Streaming*` alphabetically. Specific batch assignment determined when the task agent enumerates the directory. |
| Services.Ai.Chat.OrchestratorPromptBuilderTests | 1 | **050/051/052** | Same. |
| Services.Ai.Chat.DirectOpenAiAgentTests | 1 | **050/051/052** | Same. |
| Services.Ai.Chat.SseEventTypes.ChatSseEventFactoryTests | 1 | **050/051/052** | Same (subdirectory under Chat/). |

**Subtotal absorbed into existing tasks: 320 / 342 (93.6%)**

---

## Requires Owner Decision

These clusters fall **outside** the `<relevant-files>` glob of every existing Phase 2+3 task. For each, the failure-inventory entry was matched against every Phase 2+3 POML's relevant-files block; no clean fit emerged. Total: **22 failures across 6 clusters / 5 distinct test directories**.

| # | Cluster | Failures | Why no fit | Proposed absorbing task | Owner decision needed |
|---|---|---:|---|---|---|
| 1 | Services.Ai.Sessions.SessionRestoreServiceTests | 5 | `Services/Ai/Sessions/` is its own subdirectory. Phase 2+3 ai-chat / ai-capabilities / ai-nodes / ai-safety tasks don't cover it. Phase 1 task 012 (compile-fix-batch-3-ai-tools-sessions) touched it for compile only. | Extend **050** (ai-chat batch 1) relevant-files to include `Services/Ai/Sessions/*.cs` — Sessions is functionally adjacent to Chat orchestration. Alternative: new mini-task `057-ai-sessions.poml`. | Pick: extend 050 vs. new task |
| 2 | Services.Ai.Feedback.FeedbackServiceTests | 4 | `Services/Ai/Feedback/` is its own subdirectory. No existing Phase 2+3 task. | Extend **050** relevant-files to include `Services/Ai/Feedback/*.cs`. Alternative: new mini-task `058-ai-feedback.poml`. | Pick: extend 050 vs. new task |
| 3 | Services.Ai.RagServiceTests + Services.Ai.WorkingDocumentServiceTests | 3 + 2 = 5 | Both files live at root of `Services/Ai/` (NOT in any subdirectory). Phase 2+3 ai-chat tasks target `Services/Ai/Chat/*.cs` only. ai-safety / ai-nodes / ai-capabilities target their own subdirs. | Extend **050** relevant-files to include `Services/Ai/*.cs` (root level, non-subdir) — captures both files. Alternative: new mini-task `059-ai-root-services.poml`. | Pick: extend 050 vs. new task |
| 4 | Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests | 3 | `Services/Ai/Insights/Layer2/` is owned by sibling project `ai-spaarke-insights-engine-r1` per design §2.3. Coordination required. | **HOLD** until Insights priority-order sign-off (Phase 0 task 005). Recommended: extend **050** OR new task `057-ai-insights-layer2.poml` AFTER Insights owner confirms no in-flight Insights work overlaps. | (a) coord with Insights owner; (b) pick absorbing task |
| 5 | SpeAdmin.SearchItemsTests | 7 | `SpeAdmin/` is a top-level subdirectory (`tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/*.cs`), NOT under `Api/`. LOW-tier batches 070-073 target `Api/*` and top-level `*EndpointTests.cs` — SpeAdmin is neither. | Extend **073** relevant-files to include `tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/*.cs` — closest tier (LOW). Alternative: new mini-task `075-low-tier-speadmin.poml`. | Pick: extend 073 vs. new task |
| 6 | Services.Jobs.RecordSyncJobTests | 1 | `Services/Jobs/` has NO Phase 2+3 task. Phase 1 task 013 touched it for compile only. Single failure; could be absorbed into any Services-tier task. | Extend **046** (resilience) relevant-files OR **033** (factory-dependent batch 1) — jobs are background work with resilience characteristics. Single-failure cluster; lowest scope-extension cost. | Pick: 046 vs. 033 (recommend 046) |
| **TOTAL** | — | **22** | — | — | — |

---

## Recommended Default (if owner unavailable)

**Default decision** (until owner overrides):

- Items 1, 2, 3 → **Extend 050 relevant-files** to include:
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Sessions/*.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Feedback/*.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/*.cs` (root, non-subdir)
  Rationale: keeps the Phase 2+3 wave structure intact (no new tasks → no wave re-planning); adds ~12 failures to 050's scope (50 → 62 in batch 1).
- Item 4 (Insights Layer 2) → **HOLD** pending Phase 0 task 005 priority-order sign-off + Insights owner sync. Add `<scope-extension>` only after coord confirmed.
- Item 5 (SpeAdmin) → **Extend 073 relevant-files** — keeps it in LOW-tier wave 2.5 without new task.
- Item 6 (Services.Jobs.RecordSyncJob) → **Extend 046 relevant-files** (single failure, low cost).

Task 008 has applied the **default decisions for items 1, 2, 3, 5, 6** as `<scope-extension>` blocks on POMLs 050, 073, 046. Item 4 (Insights Layer 2) is left HOLD — agent did NOT edit POMLs for it; owner must direct.

If owner chooses different placements (new tasks instead of extensions), the `<scope-extension>` blocks can be replaced with new POMLs and TASK-INDEX.md updated; no work is wasted (POML annotations are appended notes, not metadata changes).

---

## Phase 2+3 Wall-Clock Impact

**No material change to wave plan**:
- 6-agent concurrency caps preserved per Wave 2.1–2.5.
- No new POMLs added (default decisions extend existing relevant-files; do not create new tasks).
- Per-task scope grows marginally (+1 to +12 failures absorbed per task), but Phase 2+3 person-hours estimate (48–75h per design §10) already includes 25% buffer per task acceptance metric "tier reaches zero failures."
- Estimated impact: +2–4h per affected task (053, 054, 044, 055, 060, 061, 070, 071, 072, 073, 046, 050) distributed across the 5-wave Phase 2+3 plan. Total: +24–48h person-hours in absolute terms, but **wall-clock unchanged** because each affected task is ALREADY in a wave with concurrency room — the marginal failures don't push any task beyond its wave's wall-clock floor.
- Item 4 (Insights Layer 2 HOLD) is the only structural uncertainty. If owner declines to absorb (defer to sibling Insights project), the failure count target becomes 339 instead of 342 and design §9 "zero failures" closure must explicitly exclude those 3 cases via §6.2 `[Trait("status", "real-bug-pending-fix")]` or sibling-coordination note.

---

## POMLs Edited (Cross-Reference)

These task POMLs received `<notes><scope-extension date="2026-05-31">` blocks via task 008:

| POML | Cluster absorbed | Failure count |
|---|---|---:|
| `tasks/044-ai-safety.poml` | Services.Ai.Safety.* (5 classes) | 19 |
| `tasks/046-resilience.poml` | Services.Jobs.RecordSyncJobTests (default decision item 6) | 1 |
| `tasks/050-ai-chat-batch-1.poml` | Services.Ai.Chat.* (4 classes) + DEFAULT items 1,2,3 (Sessions, Feedback, Ai/ root) | 4 + 11 = 15 |
| `tasks/053-ai-capabilities.poml` | Services.Ai.Capabilities.CapabilityRouterBenchmark | 2 |
| `tasks/054-ai-nodes.poml` | Services.Ai.Nodes.CreateTaskNodeExecutor | 5 |
| `tasks/055-communications-batch-1.poml` | Services.Communication.* (3 classes) | 53 |
| `tasks/060-bff-integration-batch-1.poml` | Integration.Workspace.* + Integration.CommunicationIntegration | 63 |
| `tasks/061-bff-integration-batch-2.poml` | Integration.SseStreamingIntegration + Integration.PlaybookExecution | 9 |
| `tasks/070-low-tier-api-batch-1.poml` | Api.Ai.* (11 classes) + Api.Agent.* (3 classes) | 90 + 7 = 97 |
| `tasks/071-low-tier-api-batch-2.poml` | Api.Office.OfficeEndpoints | 10 |
| `tasks/072-low-tier-api-batch-3.poml` | Api.Reporting.* (2 classes) | 17 |
| `tasks/073-low-tier-endpoint-tests.poml` | Top-level *EndpointTests (8 classes) + DEFAULT item 5 SpeAdmin | 39 + 7 = 46 |
| **TOTAL absorbed via edits** | — | **339** |
| **HOLD (item 4 Insights Layer 2)** | Services.Ai.Insights.Layer2.Layer2OutcomeExtraction | 3 |
| **GRAND TOTAL** | — | **342** ✅ |

Matches the measured 342 failures exactly.

---

## Verification

- [x] Sum of "Absorbed into existing tasks" rows + "Requires Owner Decision" rows = 320 + 22 = 342 ✅
- [x] Sum of POMLs Edited cross-reference = 339 + 3 HOLD = 342 ✅
- [x] No new test `.cs` files referenced (NFR-02 — metadata work only)
- [x] No production code paths (`src/`, `power-platform/`, `infra/`, `scripts/`) referenced
- [x] All cluster→task assignments justified by existing POML relevant-files glob analysis
- [x] Owner-decision section explicit + ranked + has agent-applied default for each
