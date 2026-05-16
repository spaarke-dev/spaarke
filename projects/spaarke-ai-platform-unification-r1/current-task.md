# Current Task State

> **Project**: spaarke-ai-platform-unification-r1
> **Last Updated**: 2026-05-16

## Active Task

**Task**: AIPU-055
**Task File**: tasks/055-build-deploy-code-page.poml
**Phase**: 5D
**Status**: not-started
**Next Action**: Begin task 055 — depends on 050 ✅ and 051 ✅ (both Wave 5 complete)

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | AIPU-055 - Build + deploy Code Page + BFF |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Load task 055 POML and begin |

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

## Parallel Execution State

**Current Wave**: 5
**Wave Status**: 040 ✅, 041 ✅ complete; Wave 5 (050, 051) pending
**Active Agents**: 0

| Wave | Tasks | Status |
|------|-------|--------|
| 0 | 001, 002, 003 | complete |
| 1 | 010, 011, 012 | complete |
| 2 | 020, 021, 022, 023 | 020 ✅, 021 ✅, 022 ✅, 023 pending |
| 3 | 030, 031, 032 | complete |
| 4 | 040, 041 | ✅ complete |
| 5 | 050, 051 | pending (parallel-safe) |
| 5D | 055, 056 | pending |
| 6 | 060, 061, 062 | pending |
| 7 | 070, 071, 072 | pending |
| 7D | 075, 076, 077 | pending |
| 8 | 080, 081 | pending |
| 9 | 085, 086, 090 | pending |
