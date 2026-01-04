# CLAUDE.md - PCF React 16 Remediation

> **Project**: pcf-react-16-remediation
> **Created**: 2025-12-30
> **Status**: Active

---

## Project Context

This project standardizes all Spaarke PCF controls to use Microsoft's platform-provided React library instead of bundling React 18.

## Key Decisions

1. **Use React 16.14.0 in manifest** - Loads React 17.0.2 at runtime in model-driven apps
2. **Use React 16 APIs** - `ReactDOM.render()` not `createRoot()`
3. **Add featureconfig.json** - Required for ReactDOM externalization

## Critical Files

| File | Purpose |
|------|---------|
| `featureconfig.json` | Enables ReactDOM externalization |
| `ControlManifest.Input.xml` | Platform library declarations |
| `index.ts` | React 16 render pattern |

## Template Control

**VisualHost** (`src/client/pcf/VisualHost/`) is the reference implementation with correct React 16 configuration.

## Controls to Migrate

| Control | Priority | Notes |
|---------|----------|-------|
| UniversalDatasetGrid | High | Bundles React 18 (10 MB) |
| UniversalQuickCreate | High | Has platform-lib but uses React 18 API |
| SpeFileViewer | Medium | Missing platform-library |
| AnalysisBuilder | Low | Verify configuration |
| AnalysisWorkspace | Low | Verify configuration |
| DrillThroughWorkspace | Low | Verify configuration |
| SpeDocumentViewer | Low | Verify configuration |

## Related ADRs

- **ADR-022**: PCF Platform Libraries - React 16 constraint

## Quick Commands

```bash
# Check bundle size after migration
ls -lh out/controls/*/bundle.js

# Expected: ~400-600 KB (not 5+ MB)

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk
```

## Success Criteria

- All PCF bundles < 1 MB
- No React-related console errors
- All controls render correctly in Dataverse
