# CLAUDE.md - Events Workspace Apps UX R1

> **Last Updated**: 2026-02-04
>
> **Purpose**: AI context file for Events Workspace Apps UX R1 project.

---

## Project Context

This project delivers UX components for Event management in Spaarke:
- **DueDatesWidget** - Card-based upcoming events on Overview tabs
- **EventCalendarFilter** - Date-based navigation/filtering
- **UniversalDatasetGrid Enhancement** - Calendar-aware grid with side pane
- **EventDetailSidePane** - Context-preserving Event editing
- **Events Custom Page** - System-level Events view

## Applicable ADRs

| ADR | Key Constraint | Load When |
|-----|----------------|-----------|
| [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) | PCF controls only | Creating any PCF |
| [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) | Dataset PCF over subgrids | Grid work |
| [ADR-012](../../.claude/adr/ADR-012-shared-components.md) | Shared components | EventTypeService |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9, dark mode | All UI work |
| [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | React 16 APIs | All PCF work |

## Critical Rules

### MUST:
- ✅ Use `ReactDOM.render()` and `unmountComponentAtNode()` (React 16)
- ✅ Use `@fluentui/react-components` (Fluent v9) exclusively
- ✅ Declare `platform-library` in ControlManifest.Input.xml
- ✅ Use Fluent design tokens (no hard-coded colors)
- ✅ Support light, dark, and high-contrast themes
- ✅ Include version footer in all PCF controls
- ✅ Match Power Apps grid look/feel exactly

### MUST NOT:
- ❌ Use `createRoot()` or `react-dom/client` (React 18)
- ❌ Use `@fluentui/react` (Fluent v8)
- ❌ Bundle React/Fluent in PCF output
- ❌ Hard-code colors (use tokens)
- ❌ Reuse VisualHost CalendarVisual directly (React 18)
- ❌ Brand as "My Events" - use "Events" only

## Key Patterns

### PCF Initialization
```typescript
// React 16 pattern - see .claude/patterns/pcf/control-initialization.md
import * as ReactDOM from "react-dom";  // NOT react-dom/client

public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);  // NOT root.unmount()
}

private renderComponent(): void {
    ReactDOM.render(
        React.createElement(FluentProvider, { theme },
            React.createElement(RootComponent, { context })
        ),
        this.container
    );
}
```

### Theme Management
```typescript
// See .claude/patterns/pcf/theme-management.md
import { webLightTheme, webDarkTheme } from "@fluentui/react-components";

function resolveTheme(context): Theme {
    // 1. User preference (localStorage)
    // 2. URL flags
    // 3. PCF context.fluentDesignLanguage
    // 4. Navbar detection
    // 5. System preference
}
```

### Calendar-Grid Communication (Form)
```json
// Hidden field: sprk_calendarfilter
{"type":"range","start":"2026-02-01","end":"2026-02-07"}
{"type":"single","date":"2026-02-10"}
{"type":"clear"}
```

### Side Pane Opening
```typescript
Xrm.App.sidePanes.createPane({
    title: "Event Details",
    paneId: "eventDetailPane",
    canClose: true,
    width: 400,
    webResourceParams: { eventId, eventType }
});
```

## File Locations

| Component | Location |
|-----------|----------|
| New PCF Controls | `src/client/pcf/{ControlName}/` |
| Shared Services | `src/client/shared/Spaarke.UI.Components/src/services/` |
| Custom Pages | `src/solutions/` |
| Existing Grid | `src/client/pcf/UniversalDatasetGrid/` |
| Field Visibility | `src/client/pcf/EventFormController/handlers/FieldVisibilityHandler.ts` |
| Calendar Reference | `src/client/pcf/VisualHost/control/components/CalendarVisual.tsx` |

## Data Model

### Event Fields
| Field | Schema Name | Notes |
|-------|-------------|-------|
| Event Name | `sprk_eventname` | Primary name |
| Due Date | `sprk_duedate` | Main date for filtering |
| Status | `statecode` | 0=Active, 1=Inactive |
| Status Reason | `statuscode` | Draft(1), Planned(2), Open(3), On Hold(4), Completed(5), Cancelled(6) |
| Event Type | `sprk_eventtype` | Lookup for field visibility config |
| Owner | `ownerid` | Assigned user |

### Event Type Config
```typescript
// New field: sprk_fieldconfigjson on sprk_eventtype
interface EventTypeFieldConfig {
    visibleFields?: string[];
    hiddenFields?: string[];
    requiredFields?: string[];
    sectionDefaults?: {
        dates?: "expanded" | "collapsed";
        relatedEvent?: "expanded" | "collapsed";
        description?: "expanded" | "collapsed";
    };
}
```

## Deployment

### PCF Deployment Checklist
1. Build: `npm run build:prod`
2. Update version in 5 locations (see `docs/guides/PCF-DEPLOYMENT-GUIDE.md`)
3. Copy bundle.js, ControlManifest.xml, styles.css to Solution/
4. Pack: `powershell -File pack.ps1`
5. Import: `pac solution import --path {zip} --publish-changes`
6. For Custom Pages: Save/Publish in make.powerapps.com

### Scripts
| Script | Purpose |
|--------|---------|
| `scripts/Deploy-PCFWebResources.ps1` | PCF deployment |
| `scripts/Deploy-CustomPage.ps1` | Custom Page deployment |
| `scripts/query-pcf-controls.ps1` | Verify controls |

---

## 🚨 MANDATORY: Task Execution Protocol

**When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill.**

### Trigger Phrases

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Invoke task-execute with task X |
| "continue" | Find next pending task in TASK-INDEX.md, invoke task-execute |
| "next task" | Find next pending task, invoke task-execute |
| "resume task X" | Invoke task-execute with task X |

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files loaded (ADRs, patterns)
- ✅ Context tracked in current-task.md
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates (code-review, adr-check)
- ✅ PCF version bumping protocol

**DO NOT** read POML files directly and implement manually. Always use task-execute.

---

## Quick Reference

```bash
# Start work
work on task 001

# Check status
/project-status events-workspace-apps-UX-r1

# Save progress
/checkpoint

# Build PCF
cd src/client/pcf/{ControlName} && npm run build:prod

# Deploy PCF
cd Solution && powershell -File pack.ps1
pac solution import --path bin/*.zip --publish-changes
```

---

*Project-specific context for Claude Code. See root CLAUDE.md for repository-wide standards.*
