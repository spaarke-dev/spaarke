# Project Plan: MDA Dark Mode Theme Toggle

> **Last Updated**: December 5, 2025
>
> **Status**: Not Started
>
> **Related**: [Project README](./README.md) | [Design Spec](./spec.md)

---

## 1. Executive Summary

### 1.1 Purpose

Add a centralized theme control to the model-driven app command bar that allows users to switch between Light, Dark, and Auto (system default) themes. The preference persists in localStorage and applies immediately to all PCF controls without page refresh.

### 1.2 Business Value

- **Better User Experience**: Users can work comfortably in different lighting conditions
- **Consistency**: Single global theme control instead of per-control toggles
- **Persistence**: Theme preference remembered across sessions
- **Cross-Tab Sync**: Theme changes reflect in all open tabs

### 1.3 Success Criteria

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| Theme options | Auto, Light, Dark available | Functional testing |
| Immediate application | No page refresh required | Visual testing |
| Persistence | Preference survives browser restart | Functional testing |
| Cross-tab sync | Other tabs update automatically | Functional testing |

---

## 2. Background & Context

### 2.1 Current State

- SpeFileViewer has an internal theme toggle button (per-control)
- UniversalDatasetGrid has `resolveTheme()` but uses local ThemeProvider
- UniversalQuickCreate has no theme detection
- No centralized theme control exists
- No persistence of theme preference

### 2.2 Desired State

- Single "Theme" flyout menu in command bar (More Commands)
- All PCF controls use shared `themeStorage` utilities from component library
- Theme changes apply immediately via event listeners
- Preference persists in localStorage (`spaarke-theme` key)
- Cross-tab synchronization via storage events

### 2.3 Gap Analysis

| Area | Current State | Desired State | Gap |
|------|--------------|---------------|-----|
| Theme control | Per-control or none | Global command bar menu | New web resource + ribbon config |
| PCF theming | Inconsistent | Shared utilities | Update all PCF controls |
| Persistence | None | localStorage | New shared utility |
| Event system | None | Custom + storage events | New shared utility |

---

## 3. Solution Overview

### 3.1 Approach

1. **Shared Library**: Create `themeStorage.ts` in Spaarke.UI.Components with theme utilities
2. **Web Resource**: Create minimal `sprk_ThemeMenu.js` for ribbon command invocation
3. **Ribbon Config**: Add flyout submenu to command bar via Ribbon Workbench (Button elements per Fluent V9)
4. **PCF Updates**: Update all controls to use shared theme utilities
5. **Icon Compliance**: Ensure all icons use `currentColor` for light/dark theme support
6. **Documentation**: Update PCF architecture standards with theming requirements

### 3.2 Architecture Impact

```
Command Bar (JS Web Resource - Button elements per Fluent V9)
         │
         ▼
    localStorage
    spaarke-theme
         │
    ┌────┴────┐
    │ Events  │
    └────┬────┘
         │
    ┌────┴────────────────────────────────────────────────┐
    │        Spaarke.UI.Components                        │
    │        └── utils/themeStorage.ts                    │
    │            ├── getUserThemePreference()             │
    │            ├── getEffectiveDarkMode()               │
    │            │   └── Priority: localStorage →         │
    │            │       context.isDarkTheme →            │
    │            │       DOM navbar → system pref         │
    │            └── setupThemeListener()                 │
    └────┬────────────────────────────────────────────────┘
         │
    ┌────┼────────────┬────────────┐
    ▼    ▼            ▼            ▼
SpeFile  Dataset     QuickCreate  (Future PCF)
Viewer   Grid
```

No backend changes required. All client-side implementation.

### 3.3 Key Technical Decisions

| Decision | Options Considered | Selected | Rationale |
|----------|-------------------|----------|-----------|
| Menu type | Toggle button, Flyout menu | Flyout submenu | Matches MDA pattern (like "Show As"); supports future themes |
| Menu items | ToggleButton, Button | Button | Fluent V9 pattern; no checked states needed |
| Storage | localStorage, sessionStorage, server | localStorage | Persistent; no latency; privacy |
| Event system | storage only, custom only, both | Both | Same-tab (custom) + cross-tab (storage) |
| Shared lib | Duplicate per control, shared utility | Shared utility | ADR-012 compliance |
| Context property | isDarkMode, isDarkTheme | isDarkTheme | Matches Power Platform API |
| Custom Pages | Skip detection, DOM fallback | DOM navbar fallback | Enables theme detection where context unavailable |

---

## 4. Scope Definition

### 4.1 In Scope

| Item | Description | Priority |
|------|-------------|----------|
| `themeStorage.ts` | Theme utilities in shared library | Must Have |
| `sprk_ThemeMenu.js` | Minimal ribbon handler | Must Have |
| SVG icons (4) | Menu, Auto, Light, Dark | Must Have |
| Ribbon flyout menu | Command bar configuration | Must Have |
| SpeFileViewer update | Replace internal toggle | Must Have |
| UniversalDatasetGrid update | Add shared theme support | Must Have |
| UniversalQuickCreate update | Add theme support | Must Have |
| Documentation update | PCF theming standards | Should Have |

### 4.2 Out of Scope

| Item | Reason | Future Consideration |
|------|--------|---------------------|
| SharePoint preview theming | Microsoft limitation | Waiting on MS |
| App shell theming | Platform limitation | No |
| Custom themes | Phase 1 scope | Yes - Phase 2 |
| Server sync | Complexity vs value | Low priority |

### 4.3 Assumptions

- Spaarke.UI.Components library exists and is importable by PCF controls
- Ribbon Workbench is available for ribbon configuration
- All PCF controls use FluentProvider with theme prop
- localStorage is available in the model-driven app context

### 4.4 Constraints

- Must use shared component library (ADR-012)
- Web resource must be minimal - logic in shared library (ADR-006)
- Must support keyboard accessibility
- Cannot theme cross-origin iframes (SharePoint preview)

---

## 5. Work Breakdown Structure

### Phase 1: Shared Infrastructure (4h)

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 001 | Create themeStorage.ts utilities | 2h | None |
| 002 | Create sprk_ThemeMenu.js web resource | 1h | 001 |
| 003 | Create SVG icons (4 icons) | 1h | None |

### Phase 2: PCF Control Updates (7h)

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 010 | Update SpeFileViewer to use shared theme | 3h | 001 |
| 011 | Update UniversalDatasetGrid theme support | 2h | 001 |
| 012 | Update UniversalQuickCreate theme support | 2h | 001 |

### Phase 3: Ribbon Configuration (3h)

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 020 | Configure flyout menu via Ribbon Workbench | 3h | 002, 003 |

### Phase 4: Documentation & Testing (5h)

| ID | Task | Estimate | Dependencies |
|----|------|----------|--------------|
| 030 | Update sdap-pcf-patterns.md with theming | 1h | 001 |
| 031 | Integration testing (all controls) | 3h | 010, 011, 012, 020 |
| 032 | Deploy to DEV environment | 1h | 031 |

---

## 6. Timeline & Milestones

### 6.1 Estimated Timeline

| Phase | Estimate |
|-------|----------|
| Phase 1: Shared Infrastructure | 4 hours |
| Phase 2: PCF Control Updates | 7 hours |
| Phase 3: Ribbon Configuration | 3 hours |
| Phase 4: Documentation & Testing | 5 hours |
| **Total** | **~19 hours** |

### 6.2 Key Milestones

| Milestone | Criteria | Status |
|-----------|----------|--------|
| M1: Shared Library Complete | themeStorage.ts tested and exported | Not Started |
| M2: Web Resources Ready | JS + SVG icons in solution | Not Started |
| M3: PCF Controls Updated | All 3 controls use shared theme | Not Started |
| M4: Ribbon Configured | Flyout menu working in MDA | Not Started |
| M5: Testing Complete | All integration tests pass | Not Started |
| M6: DEV Deployment | Live in DEV environment | Not Started |

---

## 7. Risk Management

### 7.1 Risk Register

| ID | Risk | Impact | Likelihood | Mitigation |
|----|------|--------|------------|------------|
| R1 | ~~Ribbon ToggleButton doesn't show checked state~~ | ~~Low~~ | ~~Medium~~ | **Resolved**: Using Button elements per Fluent V9 pattern |
| R2 | localStorage unavailable in some contexts | Medium | Low | Fallback to 'auto' mode |
| R3 | PCF re-render causes flicker | Low | Low | Use efficient state updates |
| R4 | Cross-tab sync timing issues | Low | Low | Test thoroughly; use debounce if needed |
| R5 | Custom Pages don't have fluentDesignLanguage | Medium | Medium | DOM navbar fallback detects via background color |
| R6 | Icons not visible in opposite theme | Medium | Low | Use `currentColor` in all SVGs (Section 12 compliance) |

---

## 8. ADR Alignment

| ADR | Requirement | How Addressed |
|-----|-------------|---------------|
| ADR-006 | PCF over web resources | Web resource minimal (invocation only); logic in shared lib |
| ADR-012 | Shared component library | themeStorage.ts added to Spaarke.UI.Components |

---

## 9. Acceptance Criteria

### 9.1 Functional Requirements

| ID | Requirement | Acceptance Test |
|----|-------------|-----------------|
| FR1 | Theme flyout in command bar | Menu visible in "More Commands" |
| FR2 | Auto option follows system | When system is dark, controls render dark |
| FR3 | Light option forces light | Controls render light regardless of system |
| FR4 | Dark option forces dark | Controls render dark regardless of system |
| FR5 | No page refresh needed | Theme applies immediately |
| FR6 | Preference persists | Survives browser restart |
| FR7 | Cross-tab sync | Other tabs update automatically |

### 9.2 Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR1 | Keyboard accessible | Tab/Arrow/Enter navigation |
| NFR2 | No performance degradation | Theme change < 100ms |
| NFR3 | WCAG 2.1 AA contrast | All themes pass contrast checks |

---

## 10. Files to Create

```
src/client/
├── shared/Spaarke.UI.Components/
│   └── src/utils/
│       ├── themeStorage.ts              # NEW
│       └── __tests__/themeStorage.test.ts  # NEW
├── webresources/js/
│   └── sprk_ThemeMenu.js                # NEW
└── assets/icons/
    ├── sprk_ThemeMenu16.svg             # NEW
    ├── sprk_ThemeAuto16.svg             # NEW
    ├── sprk_ThemeLight16.svg            # NEW
    └── sprk_ThemeDark16.svg             # NEW
```

## 11. Files to Modify

```
src/client/pcf/SpeFileViewer/control/
├── index.ts          # Add theme listener
├── FilePreview.tsx   # Remove internal toggle
└── package.json      # Verify shared lib dependency

src/client/pcf/UniversalDatasetGrid/control/
├── index.ts          # Add theme listener
└── providers/ThemeProvider.ts  # Use shared themeStorage

src/client/pcf/UniversalQuickCreate/control/
└── index.ts          # Add FluentProvider with theme

docs/ai-knowledge/architecture/
└── sdap-pcf-patterns.md  # Add theming section
```

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-05 | 1.0 | Initial plan from spec | AI Agent |
| 2025-12-05 | 1.1 | Updated for spec revisions: isDarkTheme property, DOM navbar fallback, Button elements (Fluent V9), icon compliance | AI Agent |

---

*Generated from spec.md via project initialization*
