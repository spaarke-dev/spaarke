# PCF React 16 Remediation Project

> **Status**: Active
> **Created**: 2025-12-30
> **Priority**: High (Performance Impact)

## Executive Summary

Standardize all Spaarke PCF controls to use Microsoft's platform-provided React library (React 16.14.0 / 17.0.2 at runtime) instead of bundling React 18. This reduces bundle sizes by ~20x and improves form load performance.

## Problem Statement

Some PCF controls bundle React 18 (~10 MB each), while others use the platform-provided React 16 (~500 KB each). A typical Dataverse form with 5 PCF controls could load 50 MB of JavaScript if all controls bundle React 18.

## Solution

Migrate all PCF controls to:
1. Use `<platform-library name="React" version="16.14.0" />` in manifest
2. Use React 16-compatible APIs (`ReactDOM.render`, not `createRoot`)
3. Configure `featureconfig.json` to externalize ReactDOM

## Scope

| Control | Current State | Bundle Size | Action Required |
|---------|---------------|-------------|-----------------|
| VisualHost | ✅ Platform React 16 | 455 KB | None (template) |
| UniversalDatasetGrid | ❌ Bundles React 18 | 10.2 MB | Migrate |
| UniversalQuickCreate | ⚠️ Platform lib but React 18 API | ~500 KB | Fix API |
| AnalysisBuilder | ⚠️ Platform lib declared | TBD | Verify |
| AnalysisWorkspace | ⚠️ Platform lib declared | TBD | Verify |
| DrillThroughWorkspace | ⚠️ Platform lib but React 18 API | TBD | Fix API (uses createRoot) |
| SpeDocumentViewer | ⚠️ Platform lib declared | TBD | Verify |
| SpeFileViewer | ❌ No platform lib | TBD | Add |

## Key Artifacts

- [spec.md](spec.md) - Full technical specification
- [notes/migration-guide.md](notes/migration-guide.md) - Step-by-step migration instructions
- [tasks/](tasks/) - Individual migration tasks

## Related ADRs

- [ADR-022: PCF Platform Libraries](.claude/adr/ADR-022-pcf-platform-libraries.md) - React 16 constraint

## Success Criteria

1. All PCF controls use platform-provided React
2. No PCF control bundle exceeds 1 MB
3. All controls work correctly in Dataverse model-driven apps
4. Shared component library (`@spaarke/ui-components`) uses React 16-compatible APIs
