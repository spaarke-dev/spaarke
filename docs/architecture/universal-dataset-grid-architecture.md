# Universal Dataset Grid Architecture

> **Status**: Draft
> **Created**: 2026-02-05
> **Domain**: UI Components / PCF / React Code Pages
> **Related ADRs**: [ADR-012](../adr/ADR-012-shared-components.md), [ADR-021](../adr/ADR-021-fluent-ui-design-system.md), [ADR-022](../adr/ADR-022-pcf-platform-libraries.md)

---

## Executive Summary

This document defines the architecture for **universal dataset grid components** that provide consistent, Power Apps-native grid experiences across both PCF controls (form-embedded, React 16/17) and React Code Pages (standalone HTML web resources, React 18).

---

## Problem Statement

### Current Gaps

1. **No View Selector** — Custom Pages cannot select different views like OOB grids
2. **Inconsistent appearance** — Custom grids don't match Power Apps native styling
3. **Missing page chrome** — No command bar or view toolbar in Custom Pages
4. **Code duplication** — Grid logic repeated across PCF and Custom Page implementations
5. **Limited configuration** — Views and FetchXML are hardcoded, not runtime-configurable

---

## Goals

| Goal | Description |
|------|-------------|
| **Visual parity** | Grid indistinguishable from OOB Power Apps grid |
| **Theme support** | Automatic light/dark mode following user preference |
| **View selection** | Dropdown to switch between saved views |
| **Configuration table** | Admin-configurable views without deployment (`sprk_gridconfiguration`) |
| **Code reuse** | Single component library for PCF and Custom Pages (>80% shared code) |

---

## Design Decisions

### Shared Library with React 16 API Compatibility

The shared library (`@spaarke/ui-components`) uses **React 16 API only** to support both PCF controls (platform-provided React 16/17, per ADR-022) and Custom Pages (bundled React 18). Using React 18 APIs in the shared library would break PCF controls.

**APIs used (React 16 compatible)**: `useState`, `useEffect`, `useMemo`, `useCallback`, `ReactDOM.render()`, `unmountComponentAtNode()`.

**APIs avoided (React 18 only)**: `createRoot()`, `useId`, `useSyncExternalStore`, `startTransition`, Suspense for data.

PCF controls reference the shared library's React 16 declarations; Custom Pages use their own bundled React 18.

### Config-Driven Views via sprk_gridconfiguration

Views can come from `savedquery` (system/public views) or `sprk_gridconfiguration` (custom admin-defined FetchXML). The Dataverse table stores complex FetchXML queries that cannot be expressed in the standard view builder (linked entity filters, aggregate queries, grouped results). This allows admins to update views without solution deployments.

Priority order: `sprk_gridconfiguration` entries first, then `savedquery` system views, then `userquery` personal views (future).

### OOB Appearance Parity

The grid replicates the Power Apps native grid structure: Command Bar (ribbon-style), View Toolbar (view dropdown + filters), and Data Grid (Fluent UI v9 DataGrid with checkbox column, sortable headers, status pills). All styling uses Fluent UI v9 semantic tokens per ADR-021 — no hard-coded colors. FluentProvider wraps all UI with theme detected from the host context.

### PCF vs Code Page Selection

See ADR-006 for the authoritative rule. Summary:
- **Field-bound subgrid on entity form** → PCF (only option)
- **Standalone list/browse dialog or entity homepage** → React Code Page
- **Multi-step wizard** → React Code Page with WizardDialog component
- **Canvas app embedding** → PCF

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        @spaarke/ui-components                               │
│                    peerDependency: "react": ">=16.14.0"                    │
│              Services are framework-agnostic (receive Xrm as constructor arg)│
└─────────────────────────────────────────────────────────────────────────────┘
                    │                               │
         ┌──────────┴──────────┐         ┌─────────┴──────────┐
         │   PCF Controls      │         │   Custom Pages (HTML)│
         │   React 16.14       │         │   React 18.x        │
         │   (platform lib)    │         │   (bundled)         │
         │   Subgrids on forms │         │   Entity homepages  │
         └─────────────────────┘         └─────────────────────┘
```

### Data Source Modes

**Saved Views** (`savedquery`): Standard Dataverse views. ViewService retrieves FetchXML and layoutXml. Used when views are manageable in the OOB view builder.

**Custom FetchXML** (`sprk_gridconfiguration`): Admin-configured complex queries. ConfigurationService reads from the Dataverse table. Used for linked entity filters, aggregates, and fallback views.

**Personal Views** (`userquery`): Future — personal views per user.

---

## Theme Compliance

Per ADR-021:
- Fluent UI v9 only (`@fluentui/react-components`)
- No hard-coded colors — use `tokens.colorNeutralBackground1`, etc.
- FluentProvider wrapper required around all UI
- Dark mode detection: `Xrm.Utility.getGlobalContext().userSettings.isDarkMode` → fallback to system preference
- High contrast: use `teamsHighContrastTheme` when detected

---

## Implementation Roadmap

| Phase | Tasks | Priority |
|-------|-------|----------|
| **1 — Core Services** | FetchXmlService, ViewService, getXrm() utility, peerDependency update | High |
| **2 — ViewSelector** | ViewSelector component, useViewSelector hook, OOB-matching style | High |
| **3 — Config Table** | sprk_gridconfiguration entity, ConfigurationService, admin form | Medium |
| **4 — Page Chrome** | CommandBar, ViewToolbar components matching OOB styling | High |
| **5 — Entity Integration** | Refactor EventsPage to use shared components, validate visual parity | High |

---

## Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| **Visual parity** | Side-by-side comparison indistinguishable from OOB |
| **Theme support** | Light, dark, high-contrast all render correctly |
| **View selection** | Dropdown shows all saved views + custom configs |
| **Performance** | <500ms initial load, <100ms view switch |
| **Bundle size** | PCF bundle <1MB (uses platform libraries) |
| **Code reuse** | >80% shared code between PCF and Custom Page |
| **Accessibility** | WCAG 2.1 AA compliant |

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [ADR-012: Shared Components](../../.claude/adr/ADR-012-shared-components.md) | Shared component library architecture |
| [ADR-021: Fluent Design System](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 requirements |
| [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | React 16 API requirements |
| [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../standards/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) | Detailed design standards |

---

*Document Version: 1.0*
*Last Updated: 2026-02-05*
