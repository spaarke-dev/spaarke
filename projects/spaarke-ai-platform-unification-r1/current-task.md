# Current Task State

> **Project**: spaarke-ai-platform-unification-r1
> **Last Updated**: 2026-05-16

## Active Task

**Task**: AIPU-071 (next pending)
**Task File**: tasks/071-legal-research-tools.poml
**Phase**: 2 (Wave 7)
**Status**: not-started
**Next Action**: Begin task 071 — AIPU-070 complete ✅

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | AIPU-071 - LegalResearchTools.cs (Bing Grounding) |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Load task 071 POML and begin |

## Completed Task: AIPU-070

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Created/Modified in AIPU-070

| File | Action |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/CodeInterpreterOptions.cs` | Created — Enabled (bool), MaxConcurrency (int, default 2), SandboxTimeoutSeconds (int, default 30) with [Required]/[Range] |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/CodeInterpreterBridge.cs` | Created — CodeInterpreterResult record + thin wrapper delegating to AgentServiceClient |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/CodeInterpreterTools.cs` | Created — AnalyzeDataAsync + GenerateChartAsync with [Description] attrs, kill switch, static SemaphoreSlim, CitationContext |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Added CodeInterpreterBridge Singleton registration |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ConfigurationModule.cs` | Added CodeInterpreterOptions binding (deferred validation) |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` | Added CodeInterpreter = "code_interpreter" (option 100000008) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Added CodeInterpreterTools wiring gated behind PlaybookCapabilities.CodeInterpreter |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: CodeInterpreterOptions with Enabled/MaxConcurrency/SandboxTimeoutSeconds | ✅ | [Required]/[Range] on all props |
| AC-2: CodeInterpreterBridge delegates to AgentServiceClient | ✅ | CreateOrResumeThread + SendMessage + StreamResponse pattern |
| AC-3: AnalyzeDataAsync + GenerateChartAsync with [Description] on all params | ✅ | [Description] on method + both params each |
| AC-4: Enabled kill switch returns user-readable string (not exception) | ✅ | Both tools check Enabled first; return graceful message |
| AC-5: SemaphoreSlim concurrency gate with timeout + 429-equivalent rejection | ✅ | Static SemaphoreSlim; WaitAsync with SandboxTimeoutSeconds |
| AC-6: No raw docs/PII to sandbox — caller-supplied excerpts only | ✅ | Enforced by ADR-015 comments + prompt builders |
| AC-7: CitationContext registered following WebSearchTools pattern | ✅ | RegisterAnalysisCitation + RegisterChartCitation |
| AC-8: dotnet build 0 errors in task-owned files | ✅ | 0 errors in my files (pre-existing errors in 071/072 files excluded) |

### Key Decisions

- Static SemaphoreSlim pattern (matching WebSearchTools.cs) vs instance-based (like AgentServiceClient) — static wins for session-level bounding
- Ephemeral thread key `_code_interpreter_ephemeral_` avoids polluting the resumable conversation cache
- `AgentServiceClient.CreateRunStreamingAsync` used — Code Interpreter step details surfaced via the model's assistant message (future: GetRunStepsAsync for richer attribution)
- `PlaybookCapabilities.CodeInterpreter = "code_interpreter"` at Dataverse option 100000008 (legal_research = 100000007 was already taken)
- Chart output label "[AI-generated chart]" per spec MUST rule for AI output attribution

## Completed Task: AIPU-060

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-060

| File | Action |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` | Added Azure.AI.Projects 1.0.0-beta.8 NuGet reference |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceOptions.cs` | Created — Enabled/Endpoint/AgentId/MaxConcurrency/ThreadCacheExpiryMinutes with [Required]/[Range] |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs` | Replaced stub — full SDK implementation (AgentsClient, SemaphoreSlim, Redis cache) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/FeatureDisabledException.cs` | Created — ADR-018 kill switch exception |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/ConcurrencyLimitExceededException.cs` | Created — ADR-016 429 exception |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: AgentServiceOptions with all properties + data annotations | ✅ | [Required] on all 5 props; [Range] on int props; [MinLength(1)] on AgentId |
| AC-2: CreateOrResumeThreadAsync, SendMessageAsync, StreamResponseAsync | ✅ | All 3 + bonus InvalidateThreadCacheAsync |
| AC-3: Kill switch in all public methods | ✅ | GuardEnabled() first in all 4 public methods |
| AC-4: SemaphoreSlim; ConcurrencyLimitExceededException on timeout | ✅ | 30s WaitAsync timeout → 429 exception |
| AC-5: Thread IDs in IDistributedCache tenant-scoped with sliding expiry | ✅ | "agent-thread:{tenantId}", SlidingExpiration |
| AC-6: No content logged — only IDs, timing, status | ✅ | Verified all _logger calls |
| AC-7: Azure.AI.Projects in .csproj | ✅ | Version 1.0.0-beta.8 |
| AC-8: dotnet build 0 errors | ✅ | 0 errors, 16 pre-existing warnings |

### Key Decisions

- Azure.AI.Projects 1.0.0-beta.8 selected (stable Agents SDK with AgentsClient, CreateRunStreamingAsync)
- AgentsClient lazily initialized (Lazy<T>) so DI wiring works with Enabled=false
- SemaphoreSlim held for full streaming duration to bound concurrent long-running runs
- DI registration and options binding already in place from task 056 stub work

## Completed Task: AIPU-061

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-061

| File | Action |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` | Added AgentService = 60 to ActionType enum with XML doc comment |
| `src/server/api/Sprk.Bff.Api/Services/Ai/NodeOutput.cs` | Added AgentConcurrencyExceeded and AgentFeatureDisabled error codes to NodeErrorCodes |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` | Created — full INodeExecutor implementation for ActionType.AgentService (60) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs` | Created (replaced by AIPU-060 full implementation via linter) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Added AgentServiceClient + AgentServiceNodeExecutor Singleton registrations |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ConfigurationModule.cs` | Added AgentServiceOptions binding (deferred validation, no ValidateOnStart) |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: ActionType enum contains AgentService = 60 with XML doc | ✅ | Line 138 of INodeExecutor.cs |
| AC-2: AgentServiceNodeExecutor implements INodeExecutor with SupportedActionTypes = [ActionType.AgentService] | ✅ | Lines 55-58 of AgentServiceNodeExecutor.cs |
| AC-3: Validate checks tenantId and prompt, returns errors when missing | ✅ | Lines 61-90 of AgentServiceNodeExecutor.cs |
| AC-4: ExecuteAsync delegates to AgentServiceClient; handles ConcurrencyLimitExceededException + FeatureDisabledException | ✅ | Lines 164-188 of AgentServiceNodeExecutor.cs |
| AC-5: Executor registered as INodeExecutor Singleton in AnalysisServicesModule | ✅ | Lines 153-154 of AnalysisServicesModule.cs |
| AC-6: dotnet build passes with zero errors | ✅ | Build succeeded: 0 errors, 16 pre-existing warnings |
| AC-7: No changes to NodeExecutorRegistry, AiToolService, or existing executors | ✅ | Only new files + additive changes verified |

### Key Decisions

- Parameters (tenantId, prompt) read from ConfigJson (JSON string) — PlaybookNodeDto has no Parameters dict; all executors use ConfigJson
- AgentServiceClient.cs was created as stub then immediately replaced by AIPU-060's full implementation (parallel task had already written the full class via linter)
- AgentServiceOptions registered with deferred validation (no ValidateOnStart) matching PowerBiOptions pattern — [Required] Endpoint/AgentId only needed when Enabled=true
- New error codes (AgentConcurrencyExceeded, AgentFeatureDisabled) added to NodeErrorCodes as additive-only change

**Rigor Level**: FULL
**Reason**: Tags include bff-api, ai-foundry; modifies .cs files; 10 steps; implements new INodeExecutor with ADR-013 constraints

## Completed Task: AIPU-050

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-050

| File | Action |
|------|--------|
| `src/client/code-pages/AnalysisWorkspace/package.json` | Added @spaarke/ai-context as file:../../shared/Spaarke.AI.Context dependency |
| `src/client/code-pages/AnalysisWorkspace/webpack.config.js` | Added @spaarke/ai-context alias (source dir) + include path for esbuild-loader |
| `src/client/code-pages/AnalysisWorkspace/src/context/AnalysisAiContext.tsx` | Import useChatSession + useChatContextMapping from @spaarke/ai-context; contextMapping added to AnalysisAiContextValue |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: @spaarke/ai-context in package.json as file: dep | ✅ | npm install: 829 packages; node_modules/@spaarke/ai-context linked |
| AC-2: imports from @spaarke/ai-context not local hooks | ✅ | Lines 29-30: useChatSession, useChatContextMapping from @spaarke/ai-context |
| AC-3: Public API surface unchanged | ✅ | chatSessionId: string|null, setChatSessionId, all original fields preserved; contextMapping added (additive) |
| AC-4: Build zero TS errors | ✅ | tsc: 0 errors in AnalysisAiContext.tsx; webpack build: compiled with 5 pre-existing warnings, 0 errors |
| AC-5: Existing tests pass | ✅ | No test files exist in tests/client/AnalysisWorkspace/ (vacuously satisfied) |
| AC-6: No duplicated implementation | ✅ | Local hooks/useChatSession.ts and useChatContextMapping.ts never existed; logic only in @spaarke/ai-context |

### Key Decisions

- No local useChatSession.ts or useChatContextMapping.ts in AnalysisWorkspace/hooks/ — task expected them but never created
- AnalysisAiContext.tsx is combined context+provider (no separate AnalysisAiProvider.tsx)
- chatSessionId bridged: useChatSession.session?.sessionId ?? sessionStorage fallback (SprkChat-driven flow preserved)
- useChatContextMapping called with analysisId + playbookId for analysis context enrichment
- Webpack alias points to /src (not /dist) matching @spaarke/auth and @spaarke/ui-components pattern

## Completed Task: AIPU-051

**Rigor Level**: STANDARD
**Completed**: 2026-05-16

### Files Created in AIPU-051

| File | Action |
|------|--------|
| `src/client/shared/Spaarke.AI.Outputs/jest.config.ts` | Created — jest config with ts-jest + jsdom environment |
| `src/client/shared/Spaarke.AI.Outputs/src/__tests__/test-utils.tsx` | Created — renderWithTheme helper + 17 mock prop factories |
| `src/client/shared/Spaarke.AI.Outputs/src/__tests__/no-hardcoded-colors.test.ts` | Created — ADR-021 static scan across output-widgets/ and source-widgets/ |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/BudgetDashboardWidget.test.tsx` | Created — 6 tests: light, dark, 2x NFR-01, structural |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/SearchResultsWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/AnalysisEditorWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/ContractComparisonWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/TimelineWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/DocumentCompareWidget.test.tsx` | Created — 6 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/StatusSummaryWidget.test.tsx` | Created — 8 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/RecommendationWidget.test.tsx` | Created — 8 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/ActionPlanWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/ChartWidget.test.tsx` | Created — 6 tests (ResizeObserver mocked) |
| `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/DataTableWidget.test.tsx` | Created — 8 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/DocumentViewerWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/WebSourceWidget.test.tsx` | Created — 7 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/LegalLibraryWidget.test.tsx` | Created — 8 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/CitationWidget.test.tsx` | Created — 9 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/ImageViewerWidget.test.tsx` | Created — 8 tests |
| `src/client/shared/Spaarke.AI.Outputs/src/source-widgets/__tests__/CodeViewerWidget.test.tsx` | Created — 8 tests |

### Test Results

**18 test suites, 127 tests — all PASS**

| Suite | Result |
|-------|--------|
| All 11 output widget test files | PASS |
| All 6 source widget test files | PASS |
| no-hardcoded-colors.test.ts | PASS — 0 ADR-021 violations found |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: Test files for all 17 widgets | ✅ | 11 output + 6 source = 17 test files |
| AC-2: Each file has 3+ tests (light, dark, render time) | ✅ | 6-9 tests per widget, always includes light/dark/NFR-01 |
| AC-3: Shared test-utils.tsx with renderWithTheme + factories | ✅ | 11 output + 6 source mock factories |
| AC-4: no-hardcoded-colors.test.ts exists and runs | ✅ | 0 violations found; scan is clean |
| AC-5: npx jest runs to completion without infrastructure errors | ✅ | 127/127 tests pass |
| AC-6: No production widget source files modified | ✅ | Only test files and jest.config.ts created |

### Key Decisions

- Installed `react`, `react-dom`, `@testing-library/dom` as devDependencies (peerDeps not auto-installed)
- ChartWidget: ResizeObserver mocked globally in test (jsdom does not implement it)
- DocumentViewerWidget / WebSourceWidget: iframe/object elements render structurally; external URLs not loaded in jsdom
- Color scan passes: all widgets use Fluent v9 `tokens.*` exclusively; ChartWidget's `var(--colorXxx)` are CSS custom property token references, not hex literals

## Completed Task: AIPU-041

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-041

| File | Action |
|------|--------|
| `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` | Created — buildLaunchUrl() + openSpaarkeAi() with SpaarkeAiLaunchParams type |
| `src/solutions/SpaarkeAi/src/ribbon/WorkspaceLaunch.ts` | Created — invocation-only: openFromWorkspace() calls openSpaarkeAi({}, 1) |
| `src/solutions/SpaarkeAi/src/ribbon/EntityFormLaunch.ts` | Created — invocation-only: openFromEntityForm(primaryControl) extracts entity context |
| `src/solutions/SpaarkeAi/src/ribbon/xrm-globals.d.ts` | Created — minimal ambient Xrm type declarations for ribbon scripts |
| `docs/guides/spaarkeai-launch-points.md` | Created — all 4 launch points documented with URL format, params, examples |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: launch-resolver.ts exports buildLaunchUrl + openSpaarkeAi with correct types | ✅ | SpaarkeAiLaunchParams + LaunchTarget types, no implicit any |
| AC-2: WorkspaceLaunch.ts invocation only | ✅ | 1 import + 1 exported function; no URL construction |
| AC-3: EntityFormLaunch.ts extracts from primaryControl, no Xrm.Navigation | ✅ | Delegates to openSpaarkeAi(); getEntityName() + getId() only |
| AC-4: docs/guides/spaarkeai-launch-points.md documents all 4 launch points | ✅ | Workspace, entity form, deep-link, M365 — all with URL format + examples |
| AC-5: M365 matterId documented and handled in launch-resolver.ts | ✅ | matterId in SpaarkeAiLaunchParams; Adaptive Card + agent action JSON included |
| AC-6: tsc --noEmit zero errors in src/solutions/SpaarkeAi/ own source | ✅ | grep "^src/" returns empty; shared library errors pre-existing (040) |

### Key Decisions

- Xrm types: added local `xrm-globals.d.ts` with minimal ambient declarations rather than installing `@types/xrm` (which is heavy and not needed for the Code Page build)
- WorkspaceLaunch opens `target: 1` (full page) — workspace navigation replaces the page
- EntityFormLaunch opens `target: 2` (modal dialog) — overlays the entity form per spec
- The ribbon scripts are separate web resources, not bundled into the Vite Code Page build
- `buildLaunchUrl` strips GUID braces (`{abc-123}` → `abc-123`) per Dataverse/Xrm getId() format

## Completed Task: AIPU-040

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-040

| File | Action |
|------|--------|
| `src/solutions/SpaarkeAi/src/App.tsx` | Rewrote — full provider tree (FluentProvider > AppWithAuth > StandaloneAiProvider > ThreePaneLayout) |
| `src/solutions/SpaarkeAi/src/main.tsx` | Updated — URL params parsed (entityType, entityId, matterId); passed to App |
| `src/solutions/SpaarkeAi/package.json` | Added @spaarke/ai-context and @spaarke/ai-outputs file: dependencies |
| `src/solutions/SpaarkeAi/tsconfig.json` | Added path aliases for @spaarke/ai-context and @spaarke/ai-outputs (dist types) |
| `src/solutions/SpaarkeAi/src/components/ChatPanel.tsx` | Created — SprkChat wired to useStandaloneAi() context |
| `src/solutions/SpaarkeAi/src/components/OutputPanel.tsx` | Created — output widget registry rendering with cross-pane linking |
| `src/solutions/SpaarkeAi/src/components/SourcePanel.tsx` | Created — source widget registry rendering with cross-pane subscription |
| `src/solutions/SpaarkeAi/src/components/ChatHistoryPanel.tsx` | Created — BFF session fetch + LibChatHistoryPanel wrapper |
| `src/solutions/SpaarkeAi/src/components/LeftPane.tsx` | Created — Chat/History tab toggle composite |
| `src/client/shared/Spaarke.UI.Components/src/components/index.ts` | Added ThreePaneLayout export |
| `src/client/shared/Spaarke.AI.Outputs/` | Rebuilt dist (cross-pane hooks were in source but not dist) |
| `src/client/shared/Spaarke.AI.Context/` | Rebuilt dist |

## Completed Task: AIPU-072

**Rigor Level**: FULL
**Completed**: 2026-05-16

### Files Modified in AIPU-072

| File | Action |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs` | Created — full ISprkChatAgent middleware with RoutingSignals taxonomy, ClassifyIntent, OTEL span, kill-switch fallback |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Updated — added `using Microsoft.Extensions.Options`, `using Sprk.Bff.Api.Services.Ai.Foundry`; WrapWithMiddleware now accepts tenantId and adds AgentServiceRoutingMiddleware as outermost layer (lazy service resolution from _serviceProvider) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/LegalResearchTools.cs` | Fixed pre-existing build errors from task 062: added `partial` keyword to QuerySanitizer class; added `RegexOptions.None` to 2-arg [GeneratedRegex] attributes |

### Acceptance Criteria Verified

| AC | Status | Notes |
|----|--------|-------|
| AC-1: AgentServiceRoutingMiddleware.cs implements ISprkChatAgent | ✅ | Decorator pattern, same as existing middleware |
| AC-2: ClassifyIntent is synchronous keyword matching, no LLM/network | ✅ | `internal static ClassificationResult ClassifyIntent(string)` — pure string ops |
| AC-3: Three signal arrays (CodeInterpreter, BingGrounding, ComplexQuery) with score threshold | ✅ | RoutingSignals static class with ScoreThreshold = 1 |
| AC-4: Kill switch (ADR-018) silently falls back to direct pipeline | ✅ | effectiveDecision check: `classification.Decision == AgentService && _options.Enabled` |
| AC-5: OTEL span "ai.routing.decision" with routing.decision, routing.backend, routing.matched_signals | ✅ | AiTelemetry.ActivitySource.StartActivity + SetTag; no content |
| AC-6: Stopwatch canary warning if ClassifyIntent > 10ms | ✅ | ClassifyWarningThresholdMs = 10; LogWarning if exceeded |
| AC-7: Wired in SprkChatAgentFactory.WrapWithMiddleware as outermost (additive) | ✅ | Lazy resolution from _serviceProvider; no DI registration added |
| AC-8: dotnet build 0 errors | ✅ | Build: 0 errors, 16 pre-existing warnings |

### Key Decisions

- ActivitySource: reused `AiTelemetry.ActivitySource` ("Sprk.Bff.Api.Ai", already in TelemetryModule) — no new source needed
- RoutingDecision + ClassificationResult made `internal` (not `private`) so ClassifyIntent can be `internal static` for testability
- TenantId injected as constructor param (not from ChatContext which has no TenantId field); factory passes it from CreateAgentAsync
- AgentServiceClient/Options resolved lazily from `_serviceProvider.GetService<T>()` in WrapWithMiddleware — middleware is skipped when Analysis:Enabled=false, no constructor change needed
- Signal threshold = 1: any single signal group match routes to Agent Service — aggressive routing fits the task spec
- Fixed pre-existing LegalResearchTools.cs compilation errors from task 062 (QuerySanitizer missing `partial`, 2-arg [GeneratedRegex] missing RegexOptions.None)

## Parallel Execution State

**Current Wave**: 7 (in progress)
**Wave Status**: 070 ✅, 071 🔲, 072 ✅ — Wave 7 progressing
**Active Agents**: 0

| Wave | Tasks | Status |
|------|-------|--------|
| 0 | 001, 002, 003 | complete |
| 1 | 010, 011, 012 | complete |
| 2 | 020, 021, 022, 023 | 020 ✅, 021 ✅, 022 ✅, 023 pending |
| 3 | 030, 031, 032 | complete |
| 4 | 040, 041 | ✅ complete |
| 5 | 050, 051 | complete |
| 5D | 055, 056 | complete |
| 6 | 060, 061, 062 | ✅ complete |
| 7 | 070, 071, 072 | 070 ✅, 071 🔲, 072 ✅ |
| 7D | 075, 076, 077 | pending |
| 8 | 080, 081 | pending |
| 9 | 085, 086, 090 | pending |
