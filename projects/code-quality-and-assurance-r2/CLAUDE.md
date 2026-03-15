# CLAUDE.md — Code Quality and Assurance R2

> **Project**: code-quality-and-assurance-r2
> **Branch**: feature/code-quality-and-assurance-r2
> **Created**: 2026-03-14

## Project Context

Structural code quality improvements: decompose God classes, fix memory leaks, eliminate dead code, segregate interfaces, fix ADR violations. Move from B (85/100) to A- (98/100).

## Applicable ADRs

| ADR | Key Constraint | When |
|-----|---------------|------|
| ADR-001 | Single typed HttpClient per upstream service | HttpClient factory fix |
| ADR-002 | No HTTP/Graph calls in plugins; <200 LoC; <50ms | BaseProxyPlugin assessment |
| ADR-007 | Route all SPE ops through SpeFileStore facade | OfficeService decomposition |
| ADR-009 | Use IDistributedCache; no hybrid L1 without profiling | Memory leak fixes |
| ADR-010 | Concrete registration; ≤15 non-framework DI; feature modules | All service decompositions |
| ADR-012 | Use @spaarke/ui-components; React 16/17 compat for PCF | Hook extraction |
| ADR-021 | Fluent v9 tokens only; no hard-coded colors | Frontend decomposition |
| ADR-022 | ReactDOM.render() in PCF; no react-dom/client | React 18→16 fix |

## Critical Constraints

- **Zero breaking changes**: All decompositions are behavior-preserving
- **IDataverseService**: Composite interface inherits all 9 — existing callers unchanged
- **Memory leak fix**: Use slim DTO to Redis (not full model). Create AnalysisCacheEntry DTO.
- **Consumer migration**: Update all IDataverseService consumers to inject narrow interfaces
- **Hooks**: Follow UseXxxOptions/UseXxxResult convention. React 16/17 compatible.
- **Existing patterns**: DI modules use AddXxxModule extension methods

## Owner Decisions (Captured)

1. **_analysisStore**: Slim DTO to Redis with 2h TTL (not full model, not in-memory eviction)
2. **IDataverseService consumers**: Migrate ALL consumers to narrow interfaces (not just create interfaces)
3. **BaseProxyPlugin**: Legacy/experimental — assessment only, mark [Obsolete]

## Key File Paths

### Backend Targets
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` (2,907 lines → decompose)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` (2,430 lines → decompose)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs` (HttpClient fix)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Testing/ProductionTestExecutor.cs` (memory leak)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Scopes/ScopeManagementService.cs` (memory leak)
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` (603 lines → segregate)

### Frontend Targets
- `src/client/pcf/AnalysisWorkspace/control/components/AnalysisWorkspaceApp.tsx` (1,564 lines → decompose)
- `src/client/pcf/SpeFileViewer/control/index.ts` (React 18→16)
- `src/client/pcf/SpeDocumentViewer/control/index.ts` (React 18→16)
- `src/client/pcf/UniversalDatasetGrid/control/index.ts` (React 18→16)

### Patterns to Follow
- DI module: `Infrastructure/DI/AnalysisServicesModule.cs`
- Shared hook: `Spaarke.UI.Components/src/hooks/useSseStream.ts`
- Logger: `AnalysisWorkspace/control/utils/logger.ts`
- Auth: `SemanticSearchControl/authInit.ts`

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing project tasks, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

See root CLAUDE.md for full task execution protocol, trigger phrases, and enforcement checklist.
