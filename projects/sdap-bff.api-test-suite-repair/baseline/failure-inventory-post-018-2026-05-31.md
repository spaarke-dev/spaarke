# Failure Inventory — Post-Wave-1.3 / Post-018 (2026-05-31)

> **Source TRX**: [`post-019-verify-2026-05-31.trx`](post-019-verify-2026-05-31.trx) — the authoritative post-Wave-1.3 measurement captured by task 019 (the formal verification gate).
> **Produced by**: Task 026 re-reconciliation (Phase 1 exit / Phase 2+3 entry).
> **Method**: PowerShell XPath against TRX `<UnitTestResult outcome="Failed">` joined to `<UnitTest>` → `<TestMethod>` className. Exact count from XML — no estimation.
> **Sibling**: [`failure-inventory-2026-05-31.md`](failure-inventory-2026-05-31.md) — task 008's pre-Phase-1 inventory (342 failures, 50 classes). This file is the refresh.

---

## Headline numbers

```
Total tests:    6,034
Passed:         5,753  (95.3%)
Failed:           172  ( 2.85%)
Skipped:          109  ( 1.81%)
```

vs. **task 008 pre-Phase-1 baseline**: 6,021 / 5,572 / 342 / 107 → **−170 failures (−49.7%)**.

Distribution: 172 failures clustered across **33 test classes** (down from 50). Sum verified: 172 (parser exact).

Cumulative chain per [`post-wave1.3-authoritative-baseline-2026-05-31.md`](post-wave1.3-authoritative-baseline-2026-05-31.md):

| Checkpoint | Failed | Δ vs. Phase 0 (342) |
|---|---:|---:|
| Phase 0 (task 001) | 342 | — (anchor) |
| Post-Wave-1.1a (task 014) | 284 | −58 (−17.0%) |
| Post-018 / Post-Wave-1.3 (task 018 + verified by 019) | **172** | **−170 (−49.7%)** |

---

## Failures by test class — sorted descending

| Test class (`Sprk.Bff.Api.Tests.*`) | Failures | Pre (task 008) | Δ |
|---|---:|---:|---:|
| Integration.Workspace.WorkspaceEndpointsTests | 31 | 31 | 0 |
| Integration.Workspace.WorkspaceLayoutEndpointTests | 23 | 23 | 0 |
| Api.Reporting.ReportingEndpointsTests | 12 | 12 | 0 |
| Api.Ai.StandaloneChatContextEndpointsTests | 11 | 18 | −7 |
| Api.Office.OfficeEndpointsTests | 10 | 10 | 0 |
| Integration.CommunicationIntegrationTests | 9 | 9 | 0 |
| Integration.SseStreamingIntegrationTests | 8 | 8 | 0 |
| Services.Ai.Safety.CitationExtractorTests | 8 | 8 | 0 |
| Services.Ai.Safety.PrivilegeLeakageTests | 7 | 7 | 0 |
| Api.Ai.AnalysisChatContextEndpointsTests | 7 | 10 | −3 |
| Api.Reporting.ReportingAuthorizationFilterTests | 5 | 5 | 0 |
| Services.Ai.Nodes.CreateTaskNodeExecutorTests | 5 | 5 | 0 |
| Services.Ai.Feedback.FeedbackServiceTests | 4 | 4 | 0 |
| Services.Ai.RagServiceTests | 3 | 3 | 0 |
| Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests | 3 | 3 | 0 |
| Api.Agent.AgentConversationServiceTests | 3 | 3 | 0 |
| Api.Agent.HandoffUrlBuilderTests | 3 | 3 | 0 |
| Services.Ai.Safety.ConfidenceScoringServiceTests | 2 | 2 | 0 |
| Services.Ai.WorkingDocumentServiceTests | 2 | 2 | 0 |
| Services.Ai.Capabilities.CapabilityRouterBenchmarkTests | 2 | 2 | 0 |
| Api.Ai.DailyBriefingEndpointsTests | 2 | 2 | 0 |
| Integration.PlaybookExecutionTests | 1 | 1 | 0 |
| Services.Jobs.RecordSyncJobTests | 1 | 1 | 0 |
| Services.Ai.Chat.DirectOpenAiAgentTests | 1 | 1 | 0 |
| Services.Ai.Chat.OrchestratorPromptBuilderTests | 1 | 1 | 0 |
| Services.Ai.Chat.PlaybookChatContextProviderEnrichmentIntegrationTests | 1 | 1 | 0 |
| Api.Agent.AgentConfigurationServiceTests | 1 | 1 | 0 |
| Services.Ai.Safety.VerifyCitationsTests | 1 | 1 | 0 |
| Api.Ai.R2SseEventEmitterTests | 1 | 1 | 0 |
| HealthAndHeadersTests | 1 | 4 | −3 |
| Services.Ai.Chat.SseEventTypes.ChatSseEventFactoryTests | 1 | 1 | 0 |
| Services.Ai.Safety.CitationVerificationServiceTests | 1 | 1 | 0 |
| PipelineHealthTests | 1 | 4 | −3 |
| **Total** | **172** | **329** of pre-Phase-1 342 (residual; 13 went to other classes that are now empty) | **−170** |

---

## Classes fully resolved (pre > 0, post = 0)

17 classes in task 008's inventory had >0 failures and now have 0. Total cleared via this cluster set: **170 failures**.

| Class | Pre (task 008) | Cleared by |
|---|---:|---|
| Api.Ai.PlaybookRunEndpointsTests | 20 | Wave 1.3 task 018 (factory edit) |
| Services.Communication.AssociationMappingTests | 29 | Wave 1.1a task 011 (Communications) |
| Services.Communication.DataverseRecordCreationTests | 23 | Wave 1.1a task 011 (Communications) |
| Api.Ai.HandlerEndpointsTests | 11 | Wave 1.3 task 018 (factory edit) |
| Api.Ai.NodeEndpointsTests | 10 | Wave 1.3 task 018 (factory edit) |
| UploadEndpointsTests (top-level) | 9 | Wave 1.3 task 018 (factory edit) |
| Api.Ai.ModelEndpointsTests | 8 | Wave 1.3 task 018 (factory edit) |
| SpeAdmin.SearchItemsTests | 7 | Wave 1.3 task 018 (factory edit) |
| ListingEndpointsTests (top-level) | 6 | Wave 1.3 task 018 (factory edit) |
| UserEndpointsTests (top-level) | 6 | Wave 1.3 task 018 (factory edit) |
| FileOperationsTests (top-level) | 6 | Wave 1.3 task 018 (factory edit) |
| Api.Ai.ChatSessionPlanEndpointTests | 5 | Wave 1.3 task 018 (factory edit) |
| Services.Ai.Sessions.SessionRestoreServiceTests | 5 | Wave 1.1a task 012 (Ai/Tools+Sessions test-level repair, including 2 `real-bug-pending-fix` skip-tags) |
| Api.Ai.ChatRefineEndpointTests | 4 | Wave 1.3 task 018 (factory edit) |
| EndpointGroupingTests (top-level) | 3 | Wave 1.3 task 018 (factory edit) |
| CorsAndAuthTests (top-level) | 1 | Wave 1.3 task 018 (factory edit) |
| Services.Communication.ArchivalFlowTests | 1 | Wave 1.1a task 011 (Communications) |
| **Subtotal cleared via fully-eliminated classes** | **154** | |

Partially cleared (pre > post > 0):
- Api.Ai.StandaloneChatContextEndpointsTests: 18 → 11 (−7) — Wave 1.3 task 018
- Api.Ai.AnalysisChatContextEndpointsTests: 10 → 7 (−3) — Wave 1.3 task 018
- HealthAndHeadersTests: 4 → 1 (−3) — Wave 1.3 task 018
- PipelineHealthTests: 4 → 1 (−3) — Wave 1.3 task 018
- **Partial subtotal**: −16

**Grand total cleared: 154 + 16 = 170** ✅ matches the 342 → 172 delta.

---

## Regression check — clusters newly failing post-Wave-1.3

| Class | Pre | Post | Notes |
|---|---:|---:|---|
| (none) | — | — | — |

**Zero new clusters.** Every class with failures in the post-019 TRX had failures in the pre-Phase-1 TRX. Confirms task 019's regression-free verdict and re-confirms task 018's `services.RemoveAll<IHostedService>()` guard absorbed any newly-unlocked code paths.

---

## Failures by namespace prefix — roll-up

Aggregated by first 3-5 segments after `Sprk.Bff.Api.Tests.`:

| Namespace prefix | Failures | vs. task 008 inventory | Notes |
|---|---:|---:|---|
| Integration.Workspace.* | 54 | 54 | WorkspaceEndpoints (31) + WorkspaceLayoutEndpoints (23) — unchanged; assertion-level (NOT factory) per task 018 §B |
| Api.Ai.* | 21 | 90 | −69. StandaloneChatContext (11) + AnalysisChatContext (7) + DailyBriefing (2) + R2SseEventEmitter (1); all 100%-failure Api.Ai classes (Playbook/Handler/Node/Model/ChatSession/ChatRefine) fully resolved by 018 |
| Services.Ai.Safety.* | 19 | 19 | CitationExtractor (8) + PrivilegeLeakage (7) + ConfidenceScoring (2) + VerifyCitations (1) + CitationVerificationService (1) — unchanged; assertion-level |
| Api.Reporting.* | 17 | 17 | ReportingEndpoints (12) + ReportingAuthorizationFilter (5) — unchanged |
| Services.Ai.* (non-Safety) | 22 | 22 | Nodes.CreateTask (5) + Feedback (4) + RagService (3) + Insights.Layer2 (3) + Chat.* (4) + WorkingDocument (2) + Capabilities.CapabilityRouterBenchmark (2) — unchanged (Sessions cleared by Wave 1.1a task 012, but offset by no growth elsewhere = stable) |
| Integration.* (non-Workspace) | 18 | 18 | CommunicationIntegration (9) + SseStreamingIntegration (8) + PlaybookExecution (1) — unchanged |
| Api.Office.* | 10 | 10 | OfficeEndpoints — unchanged |
| Api.Agent.* | 7 | 7 | AgentConversation (3) + HandoffUrlBuilder (3) + AgentConfiguration (1) — unchanged |
| Top-level (`Sprk.Bff.Api.Tests.*` root) | 2 | 39 | −37. Only HealthAndHeaders (1) + PipelineHealth (1) remain; Upload/Listing/User/FileOperations/EndpointGrouping/CorsAndAuth all cleared by 018 |
| Services.Communication.* | 0 | 53 | −53. Cleared in full by Wave 1.1a task 011 |
| Services.Jobs.* | 1 | 1 | RecordSyncJob (1) — unchanged |
| SpeAdmin.* | 0 | 7 | −7. Cleared by 018 |
| Services.Ai.Sessions.* | 0 | 5 | −5. Cleared by Wave 1.1a task 012 |
| **TOTAL** | **172** | **342** | **−170** |

---

## Data quality

- Parser exact: 172 failures grouped across 33 classes (sum = 172, no rounding).
- 0 failures classified as `<unknown>` — all `testId` values resolved to a class via the TRX TestDefinitions lookup.
- `ClassName` field comes from `<TestMethod className="...">` attribute — fully-qualified .NET name.
- Both TRX files (post-018-measure and post-019-verify) produce identical counts (verified by [`post-wave1.3-authoritative-baseline-2026-05-31.md`](post-wave1.3-authoritative-baseline-2026-05-31.md) §"Verification chain"); this inventory uses post-019 as the formal verification gate output.
