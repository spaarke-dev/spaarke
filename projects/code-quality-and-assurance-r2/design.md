# Code Quality and Assurance R2 — Design Document

> **Version**: 1.0
> **Author**: Ralph Schroeder + Claude Code
> **Created**: 2026-03-14
> **Status**: Draft
> **Predecessor**: code-quality-and-assurance-r1 (completed 2026-03-13, PR #227 merged)

---

## 1. Problem Statement

R1 established surface-level quality infrastructure: Prettier formatting, ESLint linting, pre-commit hooks, CI quality gates, TODO/FIXME resolution, and Program.cs decomposition. Overall quality grade moved from **C (74/100) to B (85/100)**.

The remaining 15 points come from **structural code quality** — issues that affect production stability, developer velocity, and architectural integrity:

- **3 unbounded static dictionaries** that grow forever and will cause OOM under sustained load
- **`new HttpClient()` created per chunk upload** — socket exhaustion risk under concurrency
- **2,907-line OfficeService.cs** — God class with 7 distinct responsibilities, merge-conflict magnet
- **890-line ExecutePlaybookAsync method** with 20 constructor dependencies — untestable
- **603-line IDataverseService interface** with 63 methods — violates interface segregation
- **1,564-line AnalysisWorkspaceApp.tsx** with 29 useState hooks — God component
- **2,321 lines of dead code** (3 identical MsalAuthProvider.ts copies)
- **2 no-op arch tests** that always pass (`Assert.True(true)`)
- **3 PCF controls violating ADR-022** by using React 18 `createRoot` instead of platform-provided React 16
- **~977 raw console.log calls** in production client code with no structured logging

These are not cosmetic — they are the issues a senior full-stack developer would flag in a code review.

**Goal**: Address all 10 findings to move from B (85/100) to A- (98/100).

---

## 2. Design Principles

1. **Decompose, don't rewrite** — Extract methods into focused services/hooks. Preserve behavior exactly. No feature changes.
2. **Zero breaking changes** — Interface segregation uses composite inheritance. Service decomposition is internal. All existing callers continue to work.
3. **Follow established patterns** — New DI modules follow the `AddXxxModule` pattern from R1. New hooks follow the `UseXxxOptions/UseXxxResult` convention from the shared library.
4. **Verify with builds and tests** — Every decomposition must pass `dotnet build`, `dotnet test`, or `npm run build` before proceeding.
5. **Delete dead code** — Don't preserve deprecated files "just in case." The MsalAuthProvider copies are confirmed dead. Delete them.

---

## 3. Scope

### 3.1 In Scope — 10 Items Across 4 Phases

#### Phase 1: Quick Wins (all parallelizable)

**Item 1: Fix 3 Unbounded Static Dictionaries (Memory Leaks)**

| Location | What It Stores | Fix |
|----------|---------------|-----|
| `AnalysisOrchestrationService.cs:73` | `Dictionary<Guid, AnalysisInternalModel>` — full analysis records with document text | Replace with Redis (`IDistributedCache`) using `sdap:ai:analysis:{id}` key, 2-hour TTL. Infrastructure already exists (`_cache` field). |
| `ScopeManagementService.cs:400` | `Dictionary<(Guid,ScopeType), HashSet<Guid>>` — playbook scope links | Change service lifecycle to `Scoped` (dies per-request). This is scaffolding pending Dataverse migration. |
| `ProductionTestExecutor.cs:31` | `Dictionary<Guid, ProductionTestResult>` — test execution results | Replace with `ConcurrentDictionary` + TTL-based eviction (30-minute expiry). Background cleanup timer. |

**Item 2: Replace `new HttpClient()` with IHttpClientFactory**

Two instances in `UploadSessionManager.cs` (lines 183, 411) create `new HttpClient()` per chunk upload. `IHttpClientFactory` is already used elsewhere in the project (`GraphClientFactory.cs`, `AiModule.cs`).

Fix: Register named client `"GraphUploadSession"` in `GraphModule.cs`. Inject `IHttpClientFactory` into `UploadSessionManager`. Replace both `new HttpClient()` with `_httpClientFactory.CreateClient("GraphUploadSession")`.

**Item 3: Fix No-Op Arch Tests + Add Plugin Assembly Coverage**

In `ADR010_DITests.cs`:
- `ExpensiveResourcesShouldBeSingleton` — always passes via `Assert.True(true)`. Fix: Use `WebApplicationFactory<Program>` to inspect actual `ServiceLifetime` registrations.
- `ServicesShouldBeConcreteUnlessSeamRequired` — logs but never fails. Fix: Change to `Assert.Empty(unnecessaryInterfaces)` with documented exception allow-list.

In `ADR002_PluginTests.cs`:
- Currently only tests the BFF assembly (`typeof(Program).Assembly`). The actual plugin assembly (`BaseProxyPlugin`) is not in scope. Fix: Add plugin assembly to test scope.

**Item 4: Delete Dead MsalAuthProvider.ts + Establish Shared Logger**

Three identical 775-line `MsalAuthProvider.ts` files exist in:
- `pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts`
- `pcf/UniversalDatasetGrid/control/services/auth/MsalAuthProvider.ts`
- `pcf/AnalysisWorkspace/control/services/auth/MsalAuthProvider.ts`

All three are confirmed dead — `@spaarke/auth` already handles authentication in these controls. The UniversalDatasetGrid and AnalysisWorkspace copies are already marked `@deprecated`. Delete all three plus the 4th partial copy in SemanticSearchControl.

Additionally, create a shared `createLogger(prefix)` utility in `@spaarke/ui-components` based on the existing `AnalysisWorkspace/control/utils/logger.ts` pattern. Migrate the top-10 worst console.log offender files to use it.

---

#### Phase 2: Backend Structural Decomposition

**Item 5: Decompose OfficeService.cs (2,907 lines)**

Current `OfficeService` has 7 distinct responsibility groups:

| Responsibility | Methods | Lines | Extract To |
|---------------|---------|-------|-----------|
| Email enrichment (Graph API) | `EnrichEmailFromGraphAsync`, `EnrichAttachmentFromGraphAsync` | ~220 | `OfficeEmailEnricher` |
| EML construction (MimeKit) | `BuildEmlFromMetadata`, `GenerateEmlFileName`, `SanitizeFileName` | ~180 | `OfficeEmailEnricher` |
| SPE upload | `UploadToSpeAsync` | ~50 | `OfficeStorageUploader` |
| Dataverse persistence | `CreateDocumentWithSpePointersAsync`, `UpdateJobStatusInDataverseAsync`, `CheckForExistingJobAsync` | ~300 | `OfficeDocumentPersistence` |
| Service Bus queuing | `QueueUploadFinalizationAsync` | ~120 | `OfficeJobQueue` |
| Job status | `GetJobStatusAsync`, mapping helpers | ~160 | `OfficeJobStatusService` |
| Dead code | `SimulateJobProgressAsync` [Obsolete] | ~319 | Delete |

After extraction, `OfficeService.SaveAsync` becomes a ~200-line thin orchestrator that injects and calls the 4 focused services. Register new services in the existing `OfficeModule.cs`.

**Item 6: Decompose AnalysisOrchestrationService.cs (2,430 lines, 20 DI deps)**

Constructor injects 20 dependencies — a clear signal for decomposition. Extract 3 services:

| Extract To | Responsibilities | Dependencies Absorbed |
|-----------|-----------------|----------------------|
| `AnalysisDocumentLoader` | `ExtractDocumentTextAsync`, `ReloadAnalysisFromDataverseAsync`, `GetOrReloadFromDataverseAsync` | `IDataverseService`, `ISpeFileOperations`, `ITextExtractor`, `IDistributedCache`, `IHttpContextAccessor` |
| `AnalysisRagProcessor` | `ProcessRagKnowledgeAsync`, `GetOrSearchRagCacheAsync`, `ComputeRagCacheKey` | `IRagService`, `RagQueryBuilder`, `IDistributedCache`, `CacheMetrics` |
| `AnalysisResultPersistence` | `StoreDocumentProfileOutputsAsync`, `EnqueueRagIndexingJobAsync`, working doc finalization | `IDataverseService`, `IWorkingDocumentService`, `IStorageRetryPolicy`, `JobSubmissionService` |

After extraction, `AnalysisOrchestrationService` retains only streaming coordination. Constructor drops from 20 to ~9-10 dependencies. `ExecutePlaybookAsync` stays in the orchestrator but delegates phases to the extracted services.

**Item 7: Segregate IDataverseService (603 lines, 63 methods)**

Create 9 focused interfaces by domain:

| Interface | Methods | Domain |
|----------|---------|--------|
| `IDocumentDataverseService` | 13 | Document CRUD + relationship queries |
| `IAnalysisDataverseService` | 4 | Analysis + outputs |
| `IGenericEntityService` | 9 | Generic CRUD + metadata |
| `IProcessingJobService` | 8 | Office processing jobs |
| `IEventDataverseService` | 9 | Events + logs + types |
| `IFieldMappingDataverseService` | 7 | Field mapping + record ops |
| `IKpiDataverseService` | 2 | KPI assessments |
| `ICommunicationDataverseService` | 9 | Communication + lookups |
| `IDataverseHealthService` | 2 | Health checks |

`IDataverseService` becomes a composite interface inheriting all 9. Both implementations (`DataverseServiceClientImpl`, `DataverseWebApiService`) implement the composite. **Zero breaking changes** — existing consumers continue injecting `IDataverseService`.

---

#### Phase 3: Frontend Structural Decomposition

**Item 8: Decompose AnalysisWorkspaceApp.tsx (1,564 lines, 29 useState)**

Extract 7 custom hooks following shared library conventions:

| Hook | State Absorbed | Logic Absorbed |
|------|---------------|---------------|
| `useAuth` | `isAuthInitialized`, `sprkChatAccessToken`, `sprkChatSessionId` | Auth check effect, token refresh interval |
| `useDocumentResolution` | `resolvedDocumentId/ContainerId/FileId/Name`, `playbookId` | Dataverse ID resolution from PCF context |
| `useAnalysisData` | `isLoading`, `error`, `_analysis`, `pendingExecution` | `loadAnalysis()` async function |
| `useAnalysisExecution` | `isExecuting`, `executionProgress` | `executeAnalysis()` + inline SSE reader |
| `useWorkingDocumentSave` | `isDirty`, `isSaving`, `lastSaved` | Auto-save timer (3s debounce), dirty tracking |
| `useChatState` | `chatMessages`, `isChatDirty`, `streamingResponse`, `isSessionResumed`, `isResumingSession`, `showResumeDialog`, `pendingChatHistory` | Chat message management, session resume dialog |
| `usePanelResize` | `isConversationPanelVisible`, `isDocumentPanelVisible`, `leftPanelWidth`, `centerPanelWidth` + 5 refs | Mouse event handlers, drag resize, cleanup |

After extraction, `AnalysisWorkspaceApp.tsx` drops to ~300 lines: hook composition + layout JSX.

---

#### Phase 4: Architecture Compliance

**Item 9: Fix ADR-022 Violations — React 18 createRoot in 3 PCF Controls**

| Control | Violation | Fix |
|---------|-----------|-----|
| `SpeFileViewer/control/index.ts` | `import { createRoot } from 'react-dom/client'` | Replace with `ReactDOM.render()` from `react-dom` |
| `SpeDocumentViewer/control/index.ts` | Same | Same |
| `UniversalDatasetGrid/control/index.ts` | `ReactDOM.createRoot(container)` | Same |

PCF controls must use platform-provided React 16/17. Using `createRoot` bundles React 18 into the PCF, doubling the React footprint.

**Item 10: Document BaseProxyPlugin ADR-002 Violations (Assessment Only)**

`BaseProxyPlugin.cs` (391 lines) violates ADR-002 in 6 ways: HTTP calls, `Thread.Sleep`, `new HttpClient()`, OAuth token acquisition, audit logging with 5+ Dataverse round-trips. The plugin is legacy/experimental and not actively used in production.

Scope: Document violations in an assessment note. Mark class as `[Obsolete]` with ADR-002 reference. Update `ADR002_PluginTests.cs` to flag the assembly. **No implementation** — full architectural inversion deferred unless plugin is reactivated.

---

### 3.2 Out of Scope

| Item | Reason for Exclusion |
|------|---------------------|
| PowerShell script remediation | Scripts are well-structured. PSScriptAnalyzer warnings are cosmetic. |
| Accessibility audit | Fluent UI v9 handles most ARIA. Code pages already have good ARIA. |
| Bundle size optimization | pcf-scripts doesn't support tree-shaking. Can't control without changing build tooling. |
| ConfigureAwait(false) | ASP.NET Core has no sync context. Not needed. |
| Base64 triple allocation on uploads | Graph SDK forces buffering. Can't stream from Dataverse forms. |
| N+1 Dataverse patterns | Intentional per code comments (throttle SPE). Revisit when batching API available. |
| Unit test coverage improvement | Separate dedicated testing project recommended. |
| ESLint code-level fixes | 41 warnings set to warn for gradual adoption. |
| Two parallel Dataverse implementations | Architectural decision, not quality issue. |
| E2E test placeholder GUIDs | Environment-dependent by design. |

---

## 4. Affected Areas

| Area | Path | Changes |
|------|------|---------|
| BFF API Services | `src/server/api/Sprk.Bff.Api/Services/` | OfficeService + AnalysisOrchestrationService decomposition, memory leak fixes |
| BFF API DI | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | New service registrations, HttpClient factory |
| BFF API Graph | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | UploadSessionManager HttpClient fix |
| Shared Dataverse | `src/server/shared/Spaarke.Dataverse/` | IDataverseService interface segregation |
| PCF Controls | `src/client/pcf/` | AnalysisWorkspace decomposition, MsalAuthProvider deletion, React 18 fix |
| Shared UI | `src/client/shared/Spaarke.UI.Components/` | New shared logger utility |
| Architecture Tests | `tests/Spaarke.ArchTests/` | Fix no-op tests, add plugin assembly |
| Dataverse Plugin | `src/dataverse/plugins/` | Obsolete annotation, assessment note |

---

## 5. Success Criteria

| Metric | Before (R1 Exit) | After (R2 Target) |
|--------|-------------------|-------------------|
| Unbounded static dictionaries | 3 | 0 |
| `new HttpClient()` in DI context | 2 instances | 0 |
| OfficeService.cs lines | 2,907 | <500 (orchestrator) |
| AnalysisOrchestrationService DI deps | 20 | ≤10 |
| IDataverseService methods in single interface | 63 | 63 across 9 focused interfaces |
| AnalysisWorkspaceApp.tsx lines | 1,564 | <400 |
| Dead MsalAuthProvider copies | 3 (2,321 lines) | 0 |
| No-op arch tests | 2 | 0 |
| ADR-022 violations (React 18 in PCF) | 3 controls | 0 |
| Raw console.log in top-10 files | ~300 calls | 0 (shared logger) |
| **Overall quality grade** | **B (85/100)** | **A- (98/100)** |

---

## 6. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| OfficeService decomposition breaks upload flow | Medium | High | Behavior-preserving extraction only. Verify with existing integration tests. |
| IDataverseService segregation breaks DI resolution | Low | High | Composite interface preserves backward compatibility. `dotnet build` + `dotnet test` after. |
| React 16 downgrade breaks PCF rendering | Medium | Medium | Test each control in Dataverse form after fix. SemanticSearchControl is the reference (already React 16). |
| AnalysisWorkspace hook extraction breaks state flow | Medium | High | Extract one hook at a time, verify PCF build after each. Keep all state transitions identical. |
| R1 branch merge conflicts with other BFF API projects | Low | Medium | R2 should be started on a fresh worktree from master after all pending PRs are merged. |

---

## 7. Estimated Effort

| Phase | Items | Parallel? | Wall-Clock Hours |
|-------|-------|-----------|-----------------|
| Phase 1: Quick Wins | #1, #2, #3, #4 | All 4 parallel | ~4-5h |
| Phase 2: Backend Decomposition | #5, #6, #7 | #5+#6 parallel, #7 after | ~8h |
| Phase 3: Frontend Decomposition | #8 | Sequential | ~8-10h |
| Phase 4: Architecture Compliance | #9, #10 | Parallel | ~3h |
| **Total** | **10 items** | | **~23-26h wall-clock** |

---

*Design document for code-quality-and-assurance-r2. Created 2026-03-14.*
