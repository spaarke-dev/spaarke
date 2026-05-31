# Failure Inventory — Wave 1 Baseline (2026-05-31)

> **Source**: `baseline/test-baseline-2026-05-31.trx` (Sprk.Bff.Api.Tests, Release config)
> **Produced by**: Task 008 TRX reconciliation (Phase 0 Wave 0.2)
> **Method**: PowerShell XPath against TRX `<UnitTestResult outcome="Failed">` joined to `<UnitTest>` → `<TestMethod>` className. Exact count from XML — no estimation.

---

## Headline Numbers

```
Total tests:    6,021
Passed:         5,572  (92.5%)
Failed:           342  ( 5.7%)
Skipped:          107  ( 1.8%)
```

vs. **design.md §3** (2026-05-30 baseline): 5,215 / 4,844 / 269 / 102 → **+73 net failures** is the unit of analysis for THIS reconciliation.

All 342 failures cluster across **50 test classes**. Sum verified: 342 (parser exact).

---

## Failures by Test Class — Sorted Descending

| Test class (Sprk.Bff.Api.Tests.*) | Failures | Class total tests | Failure rate |
|---|---:|---:|---:|
| Integration.Workspace.WorkspaceEndpointsTests | 31 | 31 | 100.0% |
| Services.Communication.AssociationMappingTests | 29 | 29 | 100.0% |
| Integration.Workspace.WorkspaceLayoutEndpointTests | 23 | 23 | 100.0% |
| Services.Communication.DataverseRecordCreationTests | 23 | 23 | 100.0% |
| Api.Ai.PlaybookRunEndpointsTests | 20 | 20 | 100.0% |
| Api.Ai.StandaloneChatContextEndpointsTests | 18 | 18 | 100.0% |
| Api.Reporting.ReportingEndpointsTests | 12 | 29 | 41.4% |
| Api.Ai.HandlerEndpointsTests | 11 | 11 | 100.0% |
| Api.Ai.AnalysisChatContextEndpointsTests | 10 | 11 | 90.9% |
| Api.Office.OfficeEndpointsTests | 10 | 20 | 50.0% |
| Api.Ai.NodeEndpointsTests | 10 | 10 | 100.0% |
| UploadEndpointsTests (top-level) | 9 | 14 | 64.3% |
| Integration.CommunicationIntegrationTests | 9 | 27 | 33.3% |
| Services.Ai.Safety.CitationExtractorTests | 8 | 30 | 26.7% |
| Api.Ai.ModelEndpointsTests | 8 | 8 | 100.0% |
| Integration.SseStreamingIntegrationTests | 8 | 18 | 44.4% |
| SpeAdmin.SearchItemsTests | 7 | 20 | 35.0% |
| Services.Ai.Safety.PrivilegeLeakageTests | 7 | 29 | 24.1% |
| ListingEndpointsTests (top-level) | 6 | 9 | 66.7% |
| UserEndpointsTests (top-level) | 6 | 7 | 85.7% |
| FileOperationsTests (top-level) | 6 | 17 | 35.3% |
| Services.Ai.Sessions.SessionRestoreServiceTests | 5 | 27 | 18.5% |
| Api.Reporting.ReportingAuthorizationFilterTests | 5 | 18 | 27.8% |
| Api.Ai.ChatSessionPlanEndpointTests | 5 | 5 | 100.0% |
| Services.Ai.Nodes.CreateTaskNodeExecutorTests | 5 | 12 | 41.7% |
| Services.Ai.Feedback.FeedbackServiceTests | 4 | 8 | 50.0% |
| PipelineHealthTests (top-level) | 4 | 4 | 100.0% |
| Api.Ai.ChatRefineEndpointTests | 4 | 4 | 100.0% |
| HealthAndHeadersTests (top-level) | 4 | 4 | 100.0% |
| Api.Agent.AgentConversationServiceTests | 3 | 22 | 13.6% |
| Api.Agent.HandoffUrlBuilderTests | 3 | 26 | 11.5% |
| Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests | 3 | 15 | 20.0% |
| Services.Ai.RagServiceTests | 3 | 65 | 4.6% |
| EndpointGroupingTests (top-level) | 3 | 9 | 33.3% |
| Services.Ai.Capabilities.CapabilityRouterBenchmarkTests | 2 | 5 | 40.0% |
| Services.Ai.Safety.ConfidenceScoringServiceTests | 2 | 21 | 9.5% |
| Services.Ai.WorkingDocumentServiceTests | 2 | 14 | 14.3% |
| Api.Ai.DailyBriefingEndpointsTests | 2 | 2 | 100.0% |
| Integration.PlaybookExecutionTests | 1 | 18 | 5.6% |
| CorsAndAuthTests (top-level) | 1 | 2 | 50.0% |
| Api.Agent.AgentConfigurationServiceTests | 1 | 30 | 3.3% |
| Services.Ai.Chat.PlaybookChatContextProviderEnrichmentIntegrationTests | 1 | 9 | 11.1% |
| Services.Ai.Chat.OrchestratorPromptBuilderTests | 1 | 22 | 4.5% |
| Services.Ai.Chat.DirectOpenAiAgentTests | 1 | 16 | 6.3% |
| Services.Jobs.RecordSyncJobTests | 1 | 13 | 7.7% |
| Services.Ai.Safety.VerifyCitationsTests | 1 | 17 | 5.9% |
| Api.Ai.R2SseEventEmitterTests | 1 | 21 | 4.8% |
| Services.Ai.Chat.SseEventTypes.ChatSseEventFactoryTests | 1 | 23 | 4.3% |
| Services.Communication.ArchivalFlowTests | 1 | 4 | 25.0% |
| Services.Ai.Safety.CitationVerificationServiceTests | 1 | 9 | 11.1% |
| **Total** | **342** | — | — |

---

## Failures by Namespace Prefix — Roll-up

Computed by aggregating the 50 classes above by their first 3-5 segments (after `Sprk.Bff.Api.Tests.`):

| Namespace prefix | Failures | Notes |
|---|---:|---|
| Integration.Workspace.* | 54 | WorkspaceEndpoints (31) + WorkspaceLayoutEndpoints (23) — both 100% failure |
| Services.Communication.* | 53 | AssociationMapping (29) + DataverseRecordCreation (23) + ArchivalFlow (1) — three of the §3.2 compile-fix files |
| Api.Ai.* | 90 | 11 distinct AI endpoint test classes |
| Services.Ai.Safety.* | 19 | CitationExtractor (8) + PrivilegeLeakage (7) + ConfidenceScoring (2) + VerifyCitations (1) + CitationVerificationService (1) |
| Services.Ai.* (non-Safety) | 22 | Sessions.SessionRestore (5) + Nodes.CreateTask (5) + Feedback (4) + RagService (3) + Insights.Layer2 (3) + Chat.* (4) + WorkingDocument (2) + Capabilities.CapabilityRouterBenchmark (2) |
| Integration.* (non-Workspace) | 18 | CommunicationIntegration (9) + SseStreamingIntegration (8) + PlaybookExecution (1) |
| Api.Reporting.* | 17 | ReportingEndpoints (12) + ReportingAuthorizationFilter (5) |
| Api.Agent.* | 7 | AgentConversation (3) + HandoffUrlBuilder (3) + AgentConfiguration (1) |
| Top-level (Sprk.Bff.Api.Tests.*) | 39 | UploadEndpoints (9) + ListingEndpoints (6) + UserEndpoints (6) + FileOperations (6) + PipelineHealth (4) + HealthAndHeaders (4) + EndpointGrouping (3) + CorsAndAuth (1) |
| SpeAdmin.* | 7 | SearchItems (7) |
| Services.Jobs.* | 1 | RecordSyncJob (1) — one of the §3.2 compile-fix files |
| **TOTAL** | **342** | matches measured |

---

## Cross-reference: 17 compile-broken files from design.md §3.2

Of the 17 files listed in design.md §3.2 as compile-broken, **6 appear in the failure inventory** (now compile-clean but contributing runtime failures):

| §3.2 file | Class in inventory | Failures |
|---|---|---:|
| Integration/CommunicationIntegrationTests.cs | Integration.CommunicationIntegrationTests | 9 |
| Services/Ai/Sessions/SessionRestoreServiceTests.cs | Services.Ai.Sessions.SessionRestoreServiceTests | 5 |
| Services/Ai/WorkingDocumentServiceTests.cs | Services.Ai.WorkingDocumentServiceTests | 2 |
| Services/Communication/ArchivalFlowTests.cs | Services.Communication.ArchivalFlowTests | 1 |
| Services/Communication/AssociationMappingTests.cs | Services.Communication.AssociationMappingTests | 29 |
| Services/Communication/DataverseRecordCreationTests.cs | Services.Communication.DataverseRecordCreationTests | 23 |
| Services/Jobs/RecordSyncJobTests.cs | Services.Jobs.RecordSyncJobTests | 1 |

Subtotal of §3.2 compile-fixed files in current failures: **70 / 342** (20%). The remaining 11 §3.2 files now compile AND pass (their tests are absorbed silently into the +728 pass-rate delta). Confirms `baseline/README.md` "Phase 1 P1.A compile-recovery track may already be effectively complete" hypothesis.

---

## Data quality

- Parser exact: 342 failures grouped across 50 classes (sum = 342, no rounding).
- 0 failures classified as `<unknown>` (all `testId` values resolved to a class via the TRX TestDefinitions lookup).
- `ClassName` field comes from `<TestMethod className="...">` attribute — fully-qualified .NET name.
