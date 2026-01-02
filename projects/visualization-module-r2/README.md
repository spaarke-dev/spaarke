# Universal Dataset Grid R2

> **Status**: Future Project - Not Started
> **Priority**: Medium
> **Blocked By**: React 16 compatibility fix required

---

## Overview

The UniversalDatasetGrid PCF control (v2.1.4) is currently **non-functional in Dataverse** due to React version incompatibility. This project will fix the control and deploy a working version.

## Current State

| Aspect | Status |
|--------|--------|
| **Source Location** | `src/client/pcf/UniversalDatasetGrid/` |
| **Current Version** | 2.1.4 |
| **Deployment Status** | Broken - React 18 incompatible with Dataverse |
| **Last Working Version** | Unknown (pre-React 18 migration) |

## Root Cause: React 18 vs Platform React 16

### The Problem

The UniversalDatasetGrid was migrated to React 18 in Sprint 5B (v2.0.5-2.0.7), using:

```typescript
// Current code (BROKEN in Dataverse)
import ReactDOM from 'react-dom/client';
this.root = ReactDOM.createRoot(container);
this.root.render(<App />);
```

**Dataverse provides React 16.14.0** via platform libraries. When a PCF uses `platform-library name="React"`, Dataverse injects React 16 at runtime. The `createRoot()` API does not exist in React 16.

### ADR-022 Compliance

Per [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022.md), all PCF controls MUST:
- Use `ReactDOM.render()` (React 16 API)
- NOT use `createRoot()` (React 18 API)
- Declare platform libraries in manifest

**VisualHost and DrillThroughWorkspace** were fixed to comply with ADR-022. UniversalDatasetGrid was not.

## Features (When Working)

The control provides:
- Document management grid with SDAP integration
- Fluent UI v9 styling
- Multi-select with Power Apps sync
- Column sorting
- Command bar (Add, Remove, Update, Download, Refresh)
- Error boundary and structured logging
- Theme detection (light/dark mode)

### Known Limitations (Pre-existing)

| Feature | Status |
|---------|--------|
| Virtualization | Deferred - alignment issues |
| File Operations | Placeholder implementations |
| Server-side Paging | Not implemented |
| Record Limit | ~1000 records (non-virtualized) |

## Required Fixes

### Phase 1: React 16 Compatibility (Critical)

**File**: `control/index.ts`

Change from:
```typescript
import ReactDOM from 'react-dom/client';
private root: ReactDOM.Root | null = null;

public init(context, notifyOutputChanged, state, container) {
    this.root = ReactDOM.createRoot(container);
    // ...
}

public updateView(context) {
    this.root?.render(<App />);
}

public destroy() {
    this.root?.unmount();
}
```

To:
```typescript
import * as ReactDOM from 'react-dom';
private container: HTMLDivElement | null = null;

public init(context, notifyOutputChanged, state, container) {
    this.container = container;
    // ...
}

public updateView(context) {
    ReactDOM.render(<App />, this.container);
}

public destroy() {
    if (this.container) {
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
```

**File**: `ControlManifest.Input.xml`

Add platform libraries:
```xml
<resources>
  <code path="index.ts" order="1" />
  <css path="styles.css" order="2" />
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

**File**: `package.json`

Move React to devDependencies (type-checking only):
```json
{
  "devDependencies": {
    "react": "^16.14.0",
    "react-dom": "^16.14.0",
    "@types/react": "^16.14.0",
    "@types/react-dom": "^16.14.0"
  }
}
```

### Phase 2: Verify File Operations

Test and fix placeholder implementations:
- `FileDownloadService.ts` - Download via SDAP API
- `FileDeleteService.ts` - Delete with confirmation
- `FileReplaceService.ts` - Version replacement
- Add File - Upload flow

### Phase 3: Integration Testing

- Deploy to SPAARKE DEV 1
- Test with Documents entity
- Verify selection sync with Power Apps
- Test file operations end-to-end

## Deployment Notes

### Central Package Management

The workspace uses `Directory.Packages.props`. Before `pac pcf push`:

```bash
# Disable CPM
mv /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props.disabled

# Deploy
cd src/client/pcf/UniversalDatasetGrid
npm run build:prod
pac pcf push --publisher-prefix sprk

# Restore CPM
mv /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props.disabled /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props
```

### File Lock Workaround

If `pac pcf push` fails with file lock error:
```bash
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

## Related Controls

| Control | React Pattern | Status |
|---------|--------------|--------|
| **VisualHost** | `ReactDOM.render()` | Working (v1.1.11) |
| **DrillThroughWorkspace** | `ReactDOM.render()` | Working (v1.1.1) |
| **UniversalDatasetGrid** | `createRoot()` | **Broken** |

## Estimated Effort

| Phase | Effort |
|-------|--------|
| Phase 1: React 16 fix | 2-4 hours |
| Phase 2: File operations | 8-16 hours |
| Phase 3: Testing | 4-8 hours |
| **Total** | 14-28 hours |

## References

- [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022.md)
- [UniversalDatasetGrid Source](../../src/client/pcf/UniversalDatasetGrid/)
- [UniversalDatasetGrid Docs](../../src/client/pcf/UniversalDatasetGrid/docs/)
- [CHANGELOG](../../src/client/pcf/UniversalDatasetGrid/docs/CHANGELOG.md)
- [VisualHost React 16 Pattern](../../src/client/pcf/VisualHost/control/index.ts)

---

*Created: 2026-01-02*
*Project: universal-dataset-grid-r2*
