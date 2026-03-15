# Code Quality and Assurance R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-14
> **Source**: design.md (code-quality-and-assurance-r2)
> **Predecessor**: code-quality-and-assurance-r1 (PR #227, merged 2026-03-14)

## Executive Summary

R2 addresses structural code quality — God classes, memory leaks, dead code, interface bloat, no-op tests, and ADR violations discovered during deep codebase audit. These issues affect production stability (OOM from unbounded dictionaries), developer velocity (2,907-line OfficeService), and architectural integrity (ADR-002/ADR-022 violations). Target: B (85/100) → A- (98/100).

## Scope

### In Scope

**Domain A: Resource Safety (Quick Wins)**
- Fix 3 unbounded static dictionaries causing memory leaks
- Replace `new HttpClient()` with `IHttpClientFactory` in UploadSessionManager
- Fix 2 no-op architecture tests (`Assert.True(true)`) and add plugin assembly to ADR-002 test scope
- Delete 3 dead MsalAuthProvider.ts copies (2,321 lines) and establish shared logger utility

**Domain B: Backend Structural Decomposition**
- Decompose OfficeService.cs (2,907 lines → thin orchestrator + 4 focused services)
- Decompose AnalysisOrchestrationService.cs (20 DI deps → ~10, extract 3 services)
- Segregate IDataverseService (603 lines, 63 methods → 9 focused interfaces) and migrate all consumers to narrow interfaces

**Domain C: Frontend Structural Decomposition**
- Decompose AnalysisWorkspaceApp.tsx (1,564 lines, 29 useState → 7 custom hooks + ~300-line component)

**Domain D: Architecture Compliance**
- Fix ADR-022 violations: replace React 18 `createRoot` with React 16 `ReactDOM.render` in 3 PCF controls
- Document BaseProxyPlugin ADR-002 violations, mark as `[Obsolete]`, update arch tests (assessment only — no implementation)

### Out of Scope

- PowerShell script remediation (scripts are well-structured; PSScriptAnalyzer warnings are cosmetic)
- Accessibility audit (Fluent UI v9 handles ARIA; code pages already have good implementation)
- Bundle size optimization (pcf-scripts doesn't support tree-shaking)
- `ConfigureAwait(false)` (ASP.NET Core has no sync context)
- Base64 triple allocation on uploads (Graph SDK forces buffering)
- N+1 Dataverse patterns (intentional per code comments — throttle SPE)
- Unit test coverage improvement (separate dedicated testing project)
- ESLint code-level fixes (41 warnings set to warn for gradual adoption)
- Two parallel Dataverse implementations (architectural decision, not quality issue)
- E2E test placeholder GUIDs (environment-dependent by design)
- BaseProxyPlugin implementation fix (legacy/experimental, not in production)

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| BFF API Services | `src/server/api/Sprk.Bff.Api/Services/` | OfficeService + AnalysisOrchestrationService decomposition, memory leak fixes |
| BFF API DI | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | New service registrations, HttpClient factory |
| BFF API Graph | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | UploadSessionManager HttpClient fix |
| Shared Dataverse | `src/server/shared/Spaarke.Dataverse/` | IDataverseService interface segregation + consumer migration |
| PCF Controls | `src/client/pcf/` | AnalysisWorkspace decomposition, MsalAuthProvider deletion, React 18→16 fix |
| Shared UI | `src/client/shared/Spaarke.UI.Components/` | New shared logger utility |
| Architecture Tests | `tests/Spaarke.ArchTests/` | Fix no-op tests, add plugin assembly |
| Dataverse Plugin | `src/dataverse/plugins/` | Obsolete annotation, assessment note |

## Requirements

### Functional Requirements

1. **FR-01**: Replace `_analysisStore` static dictionary with slim DTO persisted to Redis via `IDistributedCache` with 2-hour TTL. Create `AnalysisCacheEntry` DTO containing `analysisId`, `documentId`, `documentText`, `status`. Rebuild streaming state on reload via existing `ReloadAnalysisFromDataverseAsync`. — Acceptance: No static `Dictionary<Guid, AnalysisInternalModel>` in AnalysisOrchestrationService. Redis key pattern: `sdap:ai:analysis:{analysisId}`.

2. **FR-02**: Replace `_links` static dictionary in ScopeManagementService with scoped lifecycle. Register service as `Scoped` so dictionary dies per-request. — Acceptance: ScopeManagementService registered as `AddScoped<>` in DI module. No static dictionary.

3. **FR-03**: Replace `_testResultStore` static dictionary in ProductionTestExecutor with `ConcurrentDictionary` + TTL-based eviction (30-minute expiry via background cleanup timer). — Acceptance: No unbounded growth. Entries auto-evicted after 30 minutes.

4. **FR-04**: Replace `new HttpClient()` in UploadSessionManager (lines 183, 411) with `IHttpClientFactory`. Register named client `"GraphUploadSession"` in GraphModule.cs. — Acceptance: Zero instances of `new HttpClient()` in UploadSessionManager. Named client registered in DI.

5. **FR-05**: Fix `ExpensiveResourcesShouldBeSingleton` arch test to use `WebApplicationFactory<Program>` for actual `ServiceLifetime` inspection. Fix `ServicesShouldBeConcreteUnlessSeamRequired` to use `Assert.Empty` with documented exception allow-list. — Acceptance: Both tests can actually fail when violations exist. No `Assert.True(true)` in ADR010_DITests.cs.

6. **FR-06**: Add plugin assembly (`Spaarke.Dataverse.CustomApiProxy`) to ADR002_PluginTests.cs scope. — Acceptance: Arch tests detect HTTP calls, Thread.Sleep, and HttpClient instantiation in plugin assembly.

7. **FR-07**: Delete 3 identical MsalAuthProvider.ts files (UniversalQuickCreate, UniversalDatasetGrid, AnalysisWorkspace PCF) and partial 4th copy in SemanticSearchControl. — Acceptance: Zero `MsalAuthProvider.ts` files remain. All auth uses `@spaarke/auth`.

8. **FR-08**: Create shared `createLogger(prefix)` utility in `@spaarke/ui-components` based on existing AnalysisWorkspace `logger.ts` pattern. Guard `logInfo`/`logWarn` behind `process.env.NODE_ENV === 'development'`. Migrate top-10 worst console.log offender files. — Acceptance: Shared logger exported from `@spaarke/ui-components`. Top-10 files converted.

9. **FR-09**: Decompose OfficeService.cs into 4 focused services: `OfficeEmailEnricher` (Graph+MimeKit), `OfficeDocumentPersistence` (Dataverse CRUD), `OfficeJobQueue` (Service Bus), `OfficeStorageUploader` (SPE). `OfficeService.SaveAsync` becomes thin orchestrator. Delete `[Obsolete] SimulateJobProgressAsync` (319 lines). — Acceptance: OfficeService.cs < 500 lines. 4 new service files. All existing functionality preserved. `dotnet build` passes.

10. **FR-10**: Decompose AnalysisOrchestrationService into 3 extracted services: `AnalysisDocumentLoader`, `AnalysisRagProcessor`, `AnalysisResultPersistence`. Constructor dependency count drops from 20 to ≤10. — Acceptance: Constructor has ≤10 parameters. 3 new service files. `dotnet build` passes.

11. **FR-11**: Segregate IDataverseService (63 methods) into 9 domain-specific interfaces: `IDocumentDataverseService`, `IAnalysisDataverseService`, `IGenericEntityService`, `IProcessingJobService`, `IEventDataverseService`, `IFieldMappingDataverseService`, `IKpiDataverseService`, `ICommunicationDataverseService`, `IDataverseHealthService`. Make `IDataverseService` a composite interface inheriting all 9. — Acceptance: 9 interface files created. `IDataverseService` inherits all 9. Both implementations updated.

12. **FR-12**: Migrate all consumers of `IDataverseService` to inject the narrowest applicable interface (e.g., services that only use document methods inject `IDocumentDataverseService`). — Acceptance: Each consumer injects only the interface(s) it actually uses. `dotnet build` and `dotnet test` pass.

13. **FR-13**: Decompose AnalysisWorkspaceApp.tsx (1,564 lines) into 7 custom hooks: `useAuth`, `useDocumentResolution`, `useAnalysisData`, `useAnalysisExecution`, `useWorkingDocumentSave`, `useChatState`, `usePanelResize`. Component drops to ~300 lines. — Acceptance: AnalysisWorkspaceApp.tsx < 400 lines. 7 hook files in `control/hooks/`. PCF build passes.

14. **FR-14**: Replace `createRoot` (React 18) with `ReactDOM.render` (React 16) in SpeFileViewer, SpeDocumentViewer, and UniversalDatasetGrid PCF controls. Remove `react-dom/client` imports. — Acceptance: Zero imports from `react-dom/client` in PCF controls. PCF build passes.

15. **FR-15**: Mark `BaseProxyPlugin` class as `[Obsolete("Violates ADR-002. Use BFF API + Service Bus pattern.")]`. Create assessment note documenting all 6 violations. — Acceptance: `[Obsolete]` attribute on class. Assessment note in project notes. Plugin assembly in arch test scope.

### Non-Functional Requirements

- **NFR-01**: All decompositions are behavior-preserving. Zero functional changes. Existing callers continue to work without modification (except FR-12 consumer migration which is an intentional change).
- **NFR-02**: `dotnet build` must pass with zero errors after each backend decomposition task.
- **NFR-03**: `dotnet test` must pass (all existing tests unchanged) after IDataverseService segregation and consumer migration.
- **NFR-04**: PCF build (`cd src/client/pcf && npm run build`) must pass with zero errors after frontend decomposition and React 18→16 fixes.
- **NFR-05**: New DI module registrations follow the `AddXxxModule` extension method pattern established in R1.
- **NFR-06**: New hooks follow `UseXxxOptions`/`UseXxxResult` type conventions from shared library. All returned functions wrapped in `useCallback`.

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint | Applies To |
|-----|-----------|---------------|-----------|
| ADR-001 | Minimal API runtime model | Single typed HttpClient per upstream service | FR-04 |
| ADR-002 | Plugin constraints | No HTTP/Graph calls; <200 LoC; <50ms p95 | FR-06, FR-15 |
| ADR-003 | Lean authorization seams | Use `IAuthorizationRule` chain; per-request UAC cache only | FR-11, FR-12 |
| ADR-007 | SpeFileStore facade | Route all SPE ops through facade; no Graph SDK type leaks | FR-09 |
| ADR-008 | Endpoint filters for auth | Move auth logic to filters, not services | FR-10 |
| ADR-009 | Redis-first caching | Use `IDistributedCache`; no hybrid L1 without profiling proof | FR-01 |
| ADR-010 | DI minimalism | Concrete registration; ≤15 non-framework lines; feature module extensions | FR-04, FR-09, FR-10 |
| ADR-012 | Shared component library | Use `@spaarke/ui-components`; React 16/17 compat for PCF hooks | FR-08, FR-13 |
| ADR-021 | Fluent UI v9 design system | Fluent v9 tokens only; no hard-coded colors; dark mode support | FR-13 |
| ADR-022 | PCF platform libraries | React 16 APIs; `ReactDOM.render()`; declare platform libs in manifest | FR-14 |

### MUST Rules (from ADRs)

- ✅ MUST use `IDistributedCache` for cross-request caching (ADR-009)
- ✅ MUST register concretes by default, not interfaces, unless genuine seam required (ADR-010)
- ✅ MUST use feature module extensions for DI registration (ADR-010)
- ✅ MUST route all SPE operations through `SpeFileStore` facade (ADR-007)
- ✅ MUST use `ReactDOM.render()` in PCF controls, not `createRoot` (ADR-022)
- ✅ MUST use Fluent v9 tokens for colors/spacing in UI components (ADR-021)
- ✅ MUST ensure new hooks are React 16/17 compatible for PCF consumption (ADR-012)
- ❌ MUST NOT add `IMemoryCache` hybrid caching without profiling proof (ADR-009)
- ❌ MUST NOT create interfaces without genuine seam requirement (ADR-010)
- ❌ MUST NOT make HTTP/Graph calls in plugins (ADR-002)
- ❌ MUST NOT import from `react-dom/client` in PCF controls (ADR-022)
- ❌ MUST NOT use React 18-only hooks (`useTransition`, `useDeferredValue`) in PCF hooks (ADR-012)

### Existing Patterns to Follow

| Pattern | Location | Usage |
|---------|----------|-------|
| DI module extension method | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | Template for new service registrations |
| Shared hook conventions | `src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` | `UseXxxOptions`/`UseXxxResult` types, `useCallback` wrapping |
| Logger utility | `src/client/pcf/AnalysisWorkspace/control/utils/logger.ts` | `logInfo`/`logWarn`/`logError` with component prefix |
| Auth migration | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/authInit.ts` | `@spaarke/auth` initialization pattern |
| Composite interface | (new pattern for this project) | `IDataverseService : IDocumentDataverseService, IAnalysisDataverseService, ...` |

## Success Criteria

1. [ ] Zero unbounded static dictionaries remain in BFF API — Verify: `grep -r "static.*Dictionary\|static.*ConcurrentDictionary" --include="*.cs" src/server/`
2. [ ] Zero `new HttpClient()` in DI-managed classes — Verify: `grep -rn "new HttpClient()" --include="*.cs" src/server/`
3. [ ] OfficeService.cs < 500 lines — Verify: `wc -l src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs`
4. [ ] AnalysisOrchestrationService constructor ≤ 10 parameters — Verify: count constructor parameters
5. [ ] IDataverseService composed of 9 focused interfaces — Verify: `IDataverseService` inherits 9 interfaces
6. [ ] All IDataverseService consumers inject narrow interfaces — Verify: `dotnet build` + `dotnet test` pass
7. [ ] AnalysisWorkspaceApp.tsx < 400 lines — Verify: `wc -l` on the file
8. [ ] Zero MsalAuthProvider.ts copies remain — Verify: `find . -name "MsalAuthProvider.ts" -not -path "*/node_modules/*"`
9. [ ] Zero no-op arch tests (`Assert.True(true)`) — Verify: `grep -rn "Assert.True(true" tests/`
10. [ ] Zero ADR-022 violations (`react-dom/client` in PCF) — Verify: `grep -rn "react-dom/client" src/client/pcf/`
11. [ ] `dotnet build`, `dotnet test`, and PCF `npm run build` all pass — Verify: run all three
12. [ ] Overall quality grade ≥ A- (98/100) — Verify: re-run quality scorecard

## Dependencies

### Prerequisites

- R1 branch (`feature/code-quality-and-assurance-r1`) merged to master — **Done** (PR #227 merged 2026-03-14)
- All pending BFF API PRs merged (avoid conflicts with OfficeService/AnalysisOrchestrationService decomposition)
- New worktree created from latest master

### External Dependencies

- None — all work is internal refactoring

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| _analysisStore Redis approach | Full model vs slim DTO vs in-memory eviction? | Slim DTO to Redis | Create `AnalysisCacheEntry` with essential fields. Rebuild streaming state on reload. Use `IDistributedCache` with 2h TTL. |
| IDataverseService consumer migration | Create interfaces only, or also migrate consumers? | Create + migrate consumers | All service constructors updated to inject narrow interfaces. More work but cleaner DI. |
| BaseProxyPlugin status | Active in production or legacy? | Legacy/experimental | Assessment only. Mark `[Obsolete]`. No implementation fix. |
| Design review workflow | Pipeline immediately or review first? | Review design.md first | User reviews design.md and spec.md before `/project-pipeline`. |

## Assumptions

- OfficeService decomposition does not change external API contracts (endpoint URLs, request/response DTOs unchanged)
- IDataverseService composite interface approach preserves full backward compatibility for any code not yet migrated
- AnalysisWorkspaceApp hook extraction does not change component behavior or UI rendering
- React 16 `ReactDOM.render` is functionally equivalent to `createRoot` for these PCF controls (no concurrent features used)
- The 3 MsalAuthProvider.ts copies are confirmed dead code (verified during audit — `@spaarke/auth` handles all auth)

## Unresolved Questions

- None — all blocking questions resolved during design-to-spec interview.

---

*AI-optimized specification. Original design: design.md*
