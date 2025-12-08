# MDA Dark Mode Theme Toggle

> **Last Updated**: December 7, 2025
>
> **Status**: ✅ Completed
>
> **Completed**: December 7, 2025

## Overview

This project adds a **Theme** flyout menu to the model-driven app command bar that allows users to select between Light, Dark, and Auto (system default) themes. The preference is persisted using `localStorage` and applied immediately to all PCF controls without page refresh.

## Quick Links

| Document | Description |
|----------|-------------|
| [Design Spec](./spec.md) | Technical design specification |
| [Project Plan](./plan.md) | Implementation plan and phased breakdown |
| [Tasks](./tasks/) | Task breakdown and status |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | ✅ Complete |
| **Progress** | 100% |
| **Est. Effort** | ~19 hours |
| **Owner** | Spaarke Engineering |

## Problem Statement

Users working in different lighting conditions need to switch between light and dark themes, but:
- No centralized theme control exists in the model-driven app
- Each PCF control has inconsistent or no theme support
- SpeFileViewer has an internal toggle, but it's per-control, not global
- No persistence of theme preference across sessions

## Solution Summary

Add a command bar flyout menu ("Theme >") with Auto/Light/Dark options that:
1. Persists user preference in `localStorage` (`spaarke-theme` key)
2. Broadcasts theme changes via custom events (same-tab) and storage events (cross-tab)
3. Updates all PCF controls immediately without page refresh
4. Uses shared theme utilities from `Spaarke.UI.Components` (ADR-012 compliance)

## Graduation Criteria

The project is considered **complete** when:

- [x] Shared `themeStorage.ts` utilities created in component library
- [x] Command bar flyout menu with Auto/Light/Dark options deployed
- [x] All PCF controls (SpeFileViewer, UniversalDatasetGrid, UniversalQuickCreate) use shared theme utilities
- [x] Theme changes apply immediately without page refresh
- [x] Theme preference persists across browser sessions
- [x] Cross-tab synchronization works correctly
- [x] PCF architecture documentation updated with theming standards
- [x] Deployed to production

## Scope

### In Scope

| Item | Description | Priority |
|------|-------------|----------|
| Shared theme utilities | `themeStorage.ts` in Spaarke.UI.Components | Must Have |
| Command bar flyout menu | Theme > Auto/Light/Dark in More Commands | Must Have |
| JavaScript web resource | Minimal `sprk_ThemeMenu.js` for ribbon invocation | Must Have |
| SVG icons | Menu, Auto, Light, Dark icons (16x16 and 32x32) | Must Have |
| SpeFileViewer update | Replace internal toggle with shared utilities | Must Have |
| UniversalDatasetGrid update | Add theme support using shared utilities | Must Have |
| UniversalQuickCreate update | Add theme support using shared utilities | Must Have |
| PCF documentation update | Add theming section to `sdap-pcf-patterns.md` | Should Have |

### Out of Scope

| Item | Reason | Future Consideration |
|------|--------|---------------------|
| SharePoint preview iframe theming | Microsoft API limitation (cross-origin) | Waiting on Microsoft |
| Model-driven app shell theming | Power Platform controls app chrome | No |
| Server-side preference sync | localStorage is simpler, faster | Low priority |
| Custom branded themes | Phase 1 focuses on standard themes | Yes - Phase 2 |
| High contrast theme | Accessibility enhancement | Yes - Phase 2 |

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Flyout submenu with Button elements | Matches MDA "Show As" pattern per Fluent V9; no checked states needed | - |
| Shared library for theme utilities | Code reuse across PCF controls; single source of truth | [ADR-012](../../docs/reference/adr/ADR-012-shared-component-library.md) |
| Minimal web resource | JavaScript only for ribbon invocation; logic in shared library | [ADR-006](../../docs/reference/adr/ADR-006-prefer-pcf-over-webresources.md) |
| localStorage over server storage | Immediate application; no API latency; browser privacy | - |
| Remove SpeFileViewer internal toggle | One global theme control, not per-control toggles | - |
| DOM navbar fallback | For Custom Pages where `fluentDesignLanguage` unavailable | - |
| `currentColor` in all icons | Dark mode compliance per spec Section 12 | - |

## Architecture

```
Command Bar (More Commands)
    └── Theme (Flyout) ────────────────────────┐
        ├── Auto (follows system)              │
        ├── Light                              │
        └── Dark                               │
                │                              │
                ▼                              │
        localStorage.setItem('spaarke-theme')  │
                │                              │
    ┌───────────┼───────────┐                  │
    │           │           │                  │
    ▼           ▼           ▼                  │
SpeFileViewer  DatasetGrid  QuickCreate        │
    │           │           │                  │
    └───────────┼───────────┘                  │
                │                              │
                ▼                              │
    Spaarke.UI.Components                      │
    └── utils/themeStorage.ts ─────────────────┘
        ├── getUserThemePreference()
        ├── setUserThemePreference()
        ├── getEffectiveDarkMode()
        └── setupThemeListener()
```

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| ~~Ribbon ToggleButton checked state~~ | ~~Low~~ | ~~Medium~~ | **Resolved**: Using Button elements per Fluent V9 pattern |
| Cross-browser localStorage timing | Low | Low | Test across browsers; use both storage and custom events |
| PCF control re-render performance | Low | Low | Efficient state updates; debounce if needed |
| Custom Pages lack fluentDesignLanguage | Medium | Medium | DOM navbar fallback detects via background color |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Spaarke.UI.Components library | Internal | Ready | Extend with themeStorage utilities |
| Fluent UI v9 | External | Ready | webLightTheme, webDarkTheme |
| PCF FluentProvider | External | Ready | All controls already use it |
| Ribbon Workbench | Tool | Ready | For flyout menu configuration |

## Known Limitations

### SharePoint Preview Cannot Be Themed

**Issue:** The SharePoint Embedded document preview iframe cannot be styled due to cross-origin security restrictions.

**Impact:** Low - The preview area will remain in its default light mode regardless of the selected theme.

**Resolution:** Waiting on Microsoft to add dark mode support to SharePoint Embedded preview URLs.

### Model-Driven App Shell Unchanged

**Issue:** The Power Platform model-driven app header, navigation, and form chrome cannot be themed by PCF controls.

**Impact:** Low - Only PCF control content areas will reflect the theme change.

**Resolution:** This is a platform limitation. Users will see themed content within a potentially light app shell.

### No Checked State in Menu (By Design)

**Issue:** Menu items don't show which option is currently selected.

**Impact:** None - This is by design per Fluent V9 pattern (matches "Show As" menu).

**Resolution:** Using Button elements instead of ToggleButton. The immediate theme change IS the visual feedback.

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-05 | 1.0 | Initial project setup from spec | AI Agent |
| 2025-12-05 | 1.1 | Updated for spec revisions: isDarkTheme property, DOM navbar fallback, Button elements (Fluent V9), icon compliance | AI Agent |
| 2025-12-07 | 2.0 | **Project Completed**: Theme flyout menu deployed to all entities (Document, Event, Invoice, Matter, Project), SpeFileViewer updated with logo switching and theme integration, ribbon icons displaying correctly with SVG best practices documented | AI Agent |

---

*Generated from spec.md via project initialization*
