# Lessons Learned — AI Semantic Search UI R3

> **Project**: AI Semantic Search UI R3
> **Duration**: 2026-02-24 to 2026-02-25
> **Scope**: 54 tasks across 8 phases

---

## What Worked Well

- **DocumentRelationshipViewer as reference**: Reusing the Code Page pattern (webpack, build-webresource.ps1, MSAL singleton, @xyflow/react) from the existing DocRelViewer saved significant discovery time. The webpack config, index.html shell, and PowerShell inline script were directly adapted.

- **Parallel task execution with subagents**: Running 4-7 independent tasks simultaneously (test suites, validation, analysis) via Task agents dramatically accelerated Phase 7 (Testing & Quality). All background agents completed successfully with minimal coordination overhead.

- **esbuild-loader over ts-loader**: Switching from ts-loader to esbuild-loader cut build times from ~21s to ~10s with no compatibility issues. Better ESM preservation also aids tree-shaking.

- **POML task decomposition**: The 54-task breakdown with explicit dependencies, knowledge files, and acceptance criteria in POML format allowed deterministic execution. Each task had clear scope and completion criteria.

- **ADR-021 enforcement via automated scanning**: The dark mode and accessibility validation agents (Tasks 064, 065) caught 19 violations across 11 files that manual review would likely have missed.

## What Could Be Improved

- **Phase 8 POML files created late**: The Phase 8 task POMLs (070-080) were created during project-pipeline but weren't found by initial glob searches due to path handling. This caused unnecessary investigation time during deployment.

- **Deploy-BffApi.ps1 health check timeout**: The 30-second retry window (6 attempts × 5s) is insufficient for Azure App Service cold starts. The deploy succeeded but the health check reported failure. Recommend increasing to 12 attempts or 90 seconds total.

- **PAC CLI lacks web resource upload**: The code-page-deploy skill documents this limitation, but it still caused a context switch during Task 070 when deployment was attempted programmatically. A workaround script using Dataverse WebAPI for web resource upload would eliminate this gap.

- **Pre-existing test failures in unrelated modules**: The BFF API test suite has ~100+ failing tests in unrelated areas (ClauseComparisonHandler, OfficeEndpoints, GraphApiWireMock). These create noise when verifying project-specific changes. Targeted test filters (`--filter "FullyQualifiedName~SemanticSearch|FullyQualifiedName~RecordSearch"`) were necessary.

- **GridView shared component not extensible**: The Universal DatasetGrid's `ColumnRendererService` has no extension points for custom cell renderers. This blocked the Task 051 migration goal. The shared library should expose a `customRenderers` prop or renderer registration API.

## Technical Learnings

- **Fluent UI v9 umbrella package is the bundle bottleneck**: At ~33-37% of the 1.13 MiB bundle, `@fluentui/react-components` umbrella is the largest contributor. Migrating to sub-package imports (`@fluentui/react-button`, `@fluentui/react-input`, etc.) could save 150-250 KiB but touches 12+ source files.

- **React.lazy/code-splitting not possible in Dataverse**: The single-file HTML constraint means all code must be in one bundle. Lazy loading, route-based splitting, and dynamic imports are architecturally incompatible with Code Pages.

- **d3-force clustering works well at <100 nodes**: The graph layout performs smoothly with the 100-node cap. Beyond that, the force simulation would need optimization (web worker offloading or pre-computed layouts).

- **ADR-021 token mapping for font sizes**: `fontSizeBase100` = 10px, `fontSizeBase200` = 12px, `fontSizeBase300` = 14px, `fontSizeBase400` = 16px. No Fluent tokens exist for layout dimensions (width, height, min-height), so pixel values are acceptable for structural layout.

- **ColumnRendererService architecture**: The shared grid's renderer selection is a static dispatch on `IDatasetColumn.dataType` → renderer function mapping. Adding custom column types requires modifying the shared library itself, not consumer code. This was the root cause of the Task 051 migration deviation.

## Recommendations for Future Projects

1. **Add renderer registration to GridView**: Extend `GridView` props with `customRenderers?: Record<string, ColumnRenderer>` that merges with the built-in `ColumnRendererService` map. This would unblock all "render custom columns in shared grid" use cases.

2. **Fluent UI sub-package migration**: Create a dedicated task to migrate `@fluentui/react-components` imports to sub-packages across all Code Pages. Expected savings: 150-250 KiB per code page. This is the single highest-impact bundle optimization available.

3. **Extend Deploy-BffApi.ps1 health check timeout**: Change `$maxRetries` from 6 to 12 and document that Azure cold starts can take 30-60 seconds.

4. **Create Dataverse WebAPI web resource upload script**: A PowerShell script that uses the Dataverse WebAPI to create/update web resources would eliminate the manual upload step in code-page-deploy.

5. **Isolate test suites in CI**: Pre-existing test failures in unrelated modules should be addressed or quarantined to avoid masking genuine regressions in project work.

---

*Completed 2026-02-25. All code tasks (52/54) delivered via automated execution. 2 tasks pending manual Dataverse deployment.*
