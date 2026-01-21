# CLAUDE.md - AI Semantic Search UI R2

> **Project**: AI Semantic Search UI R2
> **Last Updated**: 2026-01-20
> **Purpose**: Project-specific context for Claude Code

---

## Project Overview

Build a **PCF control for semantic search** with natural language query support, dynamic filters, infinite scroll results, and Dataverse-native navigation.

**Key Spec Reference**: [spec.md](spec.md)

---

## Critical Constraints

### ADR Compliance (MANDATORY)

| ADR | Requirement | Enforcement |
|-----|-------------|-------------|
| **ADR-006** | PCF over webresources | No legacy JS webresources |
| **ADR-012** | Shared component library | Import from `@spaarke/ui-components` where applicable |
| **ADR-021** | Fluent UI v9 design system | All UI from `@fluentui/react-components` |
| **ADR-022** | PCF platform libraries | React 16 APIs; `platform-library` declarations |

### React 16 APIs (CRITICAL)

```typescript
// CORRECT - React 16 pattern
import * as React from "react";
import * as ReactDOM from "react-dom";

public destroy(): void {
    if (this.container) {
        ReactDOM.unmountComponentAtNode(this.container);
        this.container = null;
    }
}

private renderComponent(): void {
    ReactDOM.render(
        React.createElement(FluentProvider, { theme: this.resolveTheme() },
            React.createElement(SemanticSearchControl, { ... })
        ),
        this.container
    );
}
```

**PROHIBITED**:
- `import { createRoot } from "react-dom/client"`
- `import ReactDOM from "react-dom/client"`
- Any React 18 APIs

### Fluent UI v9 Exclusively

```typescript
// CORRECT - Fluent v9
import { Button, Input, Dropdown } from "@fluentui/react-components";
import { makeStyles, tokens } from "@fluentui/react-components";

// PROHIBITED - Fluent v8
import { Button } from "@fluentui/react";  // DON'T USE
```

### Platform Library Configuration

```json
// featureconfig.json
{
  "pcfReactPlatformLibraries": true
}
```

```xml
<!-- ControlManifest.Input.xml -->
<platform-library name="React" version="16.14.0" />
<platform-library name="Fluent" version="9.46.2" />
```

---

## MUST Rules

- **MUST** use `@fluentui/react-components` (Fluent v9) exclusively
- **MUST** use React 16 APIs (`ReactDOM.render`, NOT `createRoot`)
- **MUST** declare `platform-library` for React and Fluent in manifest
- **MUST** wrap UI in `FluentProvider` with theme from context
- **MUST** use Fluent design tokens for all colors and spacing
- **MUST** support light, dark, and high-contrast modes
- **MUST** use `makeStyles` (Griffel) for custom styling
- **MUST** keep PCF bundle under 1MB (5MB absolute max)
- **MUST** follow Power Apps grid control patterns for infinite scroll
- **MUST** import icons from `@fluentui/react-icons`
- **MUST** use `Xrm.Navigation.navigateTo` for record navigation

## MUST NOT Rules

- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** use React 18 APIs (`createRoot`, `hydrateRoot`)
- **MUST NOT** hard-code colors (hex, rgb, named)
- **MUST NOT** bundle React/ReactDOM (use platform libraries)
- **MUST NOT** import from granular `@fluentui/react-*` packages
- **MUST NOT** use `window.open` for Dataverse records

---

## Reference Implementations

| Pattern | Reference Location |
|---------|-------------------|
| PCF control structure | `src/client/pcf/DocumentRelationshipViewer/` |
| MSAL auth provider | `src/client/pcf/DocumentRelationshipViewer/services/MsalAuthProvider.ts` |
| Theme handling | `src/client/pcf/DocumentRelationshipViewer/` (fluentDesignLanguage) |
| API service pattern | `src/client/pcf/*/services/*ApiService.ts` |
| Control initialization | `.claude/patterns/pcf/control-initialization.md` |
| Theme management | `.claude/patterns/pcf/theme-management.md` |

---

## External Domain Strategy

PCF controls can only call domains allowlisted in the manifest.

**Current Implementation** (Option B - Multi-Domain):
```xml
<external-service-usage enabled="true">
  <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

**Property Validation**: The `apiBaseUrl` property must be validated at runtime against the manifest allowlist.

---

## Infinite Scroll Contract

| Rule | Implementation |
|------|----------------|
| Pagination | offset/limit based (NOT page numbers) |
| Initial load | `offset=0, limit=25` |
| Load more | `offset=currentResults.length, limit=25` |
| Stop condition | `currentResults.length >= totalCount` |
| DOM cap | Maximum 200 items rendered |
| Over cap | Show "Showing first 200 of {totalCount} results. [View all]" |

---

## Navigation Patterns

| Action | Method |
|--------|--------|
| Open Record (Modal) | `Xrm.Navigation.navigateTo` with `target: 2` |
| Open Record (New Tab) | `Xrm.Navigation.navigateTo` with `target: 1` |
| Open File | `window.open(result.fileUrl, "_blank")` |
| View All | `Xrm.Navigation.navigateTo` with `pageType: "custom"` |

---

## Task Execution Protocol

### Rigor Level: FULL

This project involves code implementation (PCF, TypeScript). All tasks use FULL rigor:
- All 11 task-execute steps mandatory
- Checkpoint every 3 steps
- Quality gates (code-review + adr-check) at Step 9.5

### Multi-File Work Decomposition

When implementing tasks that modify 4+ files:
1. DECOMPOSE into independent sub-tasks
2. IDENTIFY parallel-safe work (no shared state)
3. DELEGATE to subagents (Task tool with subagent_type="general-purpose")
4. COORDINATE results

### Subagent Delegation Rules

| Work Type | Delegation Strategy |
|-----------|---------------------|
| Independent components | Parallel subagents |
| Shared state/types | Sequential (main agent) |
| Hook + component that uses it | Sequential (same agent) |
| Multiple independent services | Parallel subagents |

---

## Key Decisions Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-01-20 | Infinite scroll (not pagination) | Follows Fluent v9 and Power Apps patterns |
| 2026-01-20 | Explicit search click (not auto-search) | Simpler state management |
| 2026-01-20 | Modal + new tab for Open Record | User choice preserves context |
| 2026-01-20 | DOM cap at 200 (not virtualization) | Simpler implementation |
| 2026-01-20 | Multi-domain allowlist (not stable domain) | Current infrastructure supports this |
| 2026-01-20 | No custom keyboard shortcuts | Rely on Fluent built-in |
| 2026-01-20 | No custom ARIA implementation | Rely on Fluent v9 accessibility |

---

## Files Modified This Session

*Updated by Claude Code during task execution*

| File | Change | Task |
|------|--------|------|
| - | - | - |

---

## Quick Commands

```bash
# Build control
cd src/client/pcf/SemanticSearchControl && npm run build

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk

# Run tests
npm test

# Check bundle size
npm run build && du -h out/bundle.js
```

---

*Project-specific context for Claude Code. See root CLAUDE.md for repository-wide rules.*
