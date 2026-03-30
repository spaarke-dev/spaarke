# MDA Dark Mode Theme R2 — Unified Theme Consistency

> **Project**: spaarke-mda-darkmode-theme-r2
> **Status**: Design
> **Priority**: High
> **Prerequisite**: spaarke-mda-darkmode-theme-r1 (completed December 2025)
> **Last Updated**: March 30, 2026

---

## Executive Summary

Fix inconsistent light/dark mode switching across Spaarke surfaces by consolidating two separate theme utility files into a single module with a consistent fallback chain. The root cause is that PCF controls fall back to OS `prefers-color-scheme` while Code Pages do not — causing the same page to render mixed themes when a user's OS is dark but no Spaarke preference is set.

---

## Problem Statement

### The Inconsistency

When a user has:
- OS set to **dark mode**
- Spaarke theme preference set to **"Auto"** (or not set at all)

They see:

| Surface | Theme Rendered | Why |
|---------|---------------|-----|
| PCF controls (UniversalDatasetGrid, VisualHost) | **Dark** | Falls back to OS `prefers-color-scheme` |
| Code Pages (LegalWorkspace, wizards, side panes) | **Light** | Ignores OS preference, falls back to light |

This produces a jarring split on pages like the Corporate Workspace where PCF grids (dark) sit alongside Code Page sections like SmartToDo or ActivityFeed (light).

### Root Cause: Two Utility Files with Different Fallback Chains

**`codePageTheme.ts`** (used by Code Pages):
```
1. localStorage 'spaarke-theme'     → user preference
2. URL flags parameter              → Xrm.Navigation dark flag
3. Navbar DOM color detection       → background-color check
4. DEFAULT: light theme             ✅ Correct per ADR-021
```

**`themeStorage.ts`** (used by PCF controls):
```
1. localStorage 'spaarke-theme'     → user preference
2. PCF context.fluentDesignLanguage → Power Platform setting
3. Navbar DOM color detection       → background-color check
4. OS prefers-color-scheme          ❌ Violates ADR-021
5. DEFAULT: light theme
```

Step 4 in the PCF chain (`getSystemThemePreference()`) consults `window.matchMedia('(prefers-color-scheme: dark)')`. This was likely a safety net for edge cases where no other signal is available, but it violates ADR-021's requirement that "the Spaarke theme system (not the OS) controls all UI surfaces."

### Additional Issues

1. **Duplicated ThemeProvider.ts wrappers** — 6+ Code Page solutions have local ThemeProvider files that wrap `codePageTheme.ts`. These can drift from the shared implementation.
2. **OS preference listener in PCF** — `setupThemeListener()` in `themeStorage.ts` actively monitors `prefers-color-scheme` changes, causing PCF controls to reactively switch themes when the OS theme changes — even when the user has set "Auto" expecting app-level behavior.
3. **Unit tests encode the wrong behavior** — `themeStorage.test.ts` lines 168-178 explicitly test and assert that `getEffectiveDarkMode()` falls back to system preference, making this a tested (but incorrect) behavior.

---

## Solution: Consolidate to Single Theme Utility

### Unified Theme Priority Chain

```
1. localStorage 'spaarke-theme'        → User's explicit choice (dark/light)
2. PCF context.fluentDesignLanguage     → Power Platform setting (PCF only)
3. Navbar DOM color detection           → Reads navbar background-color
4. DEFAULT: LIGHT THEME                 → Never consults OS preference
```

When the user selects **"Auto"** in the theme menu, it means "follow the Power Platform app theme" (detected via navbar color or PCF context), NOT "follow the OS."

### What Changes

| Change | File | Action |
|--------|------|--------|
| Remove OS fallback | `themeStorage.ts` | Delete `getSystemThemePreference()`, remove `matchMedia` listener |
| Add Code Page functions | `themeStorage.ts` | Export `resolveCodePageTheme()` and `setupCodePageThemeListener()` |
| Delete duplicate | `codePageTheme.ts` | Remove file entirely |
| Update imports | 6× `ThemeProvider.ts` | Point to unified `themeStorage.ts` |
| Verify PCF providers | 2× PCF `ThemeProvider.ts` | Confirm no local OS fallback |
| Fix tests | `themeStorage.test.ts` | Remove OS fallback expectations, add Code Page tests |
| Update exports | Barrel `index.ts` | Export unified functions |

### What Does NOT Change

- localStorage key (`spaarke-theme`) — same
- Custom event name (`spaarke-theme-change`) — same
- Ribbon menu JS (`sprk_ThemeMenu.js`) — same
- Theme values (`auto`, `light`, `dark`) — same
- Cross-tab sync via `storage` event — same
- Navbar DOM detection algorithm — same

---

## Design Decisions

### "Auto" Means App Theme, Not OS Theme

When a user selects "Auto":
1. Check navbar color → if detectable, use it (Power Apps controls the app theme)
2. Check PCF context → if available, use `isDarkTheme`
3. If neither is available → default to light

This is the correct interpretation because:
- The user is inside a Power Platform app that has its own theme
- The app theme may differ from the OS theme
- ADR-021 states the Spaarke theme system controls all surfaces
- Users who want OS-following behavior can explicitly set dark/light to match their OS

### Single Module, Not Two

Merging into `themeStorage.ts` (not `codePageTheme.ts`) because:
- `themeStorage.ts` has the richer API (PCF context support, more functions)
- PCF controls are the harder surface to change (embedded in forms)
- Code Pages can easily consume any function signature
- Avoids renaming the module that PCF controls already import

### Delete codePageTheme.ts, Don't Deprecate

The file is small, has only 3 consumers that are easy to update, and keeping it as a deprecated re-export adds confusion. Clean delete with import migration.

---

## Migration Checklist

### Code Page Solutions to Update

| Solution | ThemeProvider Path | Current Import |
|----------|-------------------|----------------|
| LegalWorkspace | `src/solutions/LegalWorkspace/src/providers/ThemeProvider.ts` | `codePageTheme.ts` |
| EventsPage | `src/solutions/EventsPage/src/providers/ThemeProvider.ts` | `codePageTheme.ts` |
| CalendarSidePane | `src/solutions/CalendarSidePane/src/providers/ThemeProvider.ts` | `codePageTheme.ts` |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/src/providers/ThemeProvider.ts` | `codePageTheme.ts` |
| SpeAdminApp | `src/solutions/SpeAdminApp/src/providers/ThemeProvider.ts` | `codePageTheme.ts` |
| TodoDetailSidePane | Check if exists | TBD |

### PCF Controls to Verify

| Control | ThemeProvider Path | Expected Import |
|---------|-------------------|-----------------|
| UniversalDatasetGrid | `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` | `themeStorage.ts` |
| VisualHost | `src/client/pcf/VisualHost/control/providers/ThemeProvider.ts` | `themeStorage.ts` |

### Other Consumers

| File | Check For |
|------|-----------|
| `src/client/shared/Spaarke.UI.Components/src/utils/index.ts` | Re-exports of `codePageTheme` |
| Any `App.tsx` importing `resolveCodePageTheme` directly | Update import path |

---

## Known Platform Limitations (Carried Forward from R1)

| Limitation | Impact | Status |
|------------|--------|--------|
| SharePoint Embedded preview iframe cannot be themed | Low — preview stays light | Waiting on Microsoft |
| MDA app shell (header, nav, form chrome) not controllable | Low — shell follows platform theme | Platform limitation |
| Custom Page dialog chrome (title bar, close button) stays white in dark mode | Medium — content is themed, chrome is not | Microsoft working on support |

---

## Scope

### In Scope
- Consolidate `themeStorage.ts` and `codePageTheme.ts` into single module
- Remove OS `prefers-color-scheme` fallback from all surfaces
- Update all Code Page ThemeProvider imports
- Verify all PCF ThemeProvider imports
- Update unit tests
- Update barrel exports

### Out of Scope
- New theme options (high contrast, custom branded themes)
- SharePoint preview iframe theming
- MDA app shell theming
- Dialog chrome dark mode
- Theme menu UX changes

---

## Success Criteria

1. [ ] Single theme utility module — no `codePageTheme.ts`
2. [ ] OS dark mode + Spaarke "Auto" → ALL surfaces render light
3. [ ] Spaarke "Dark" → ALL surfaces render dark (PCF + Code Pages)
4. [ ] Spaarke "Light" → ALL surfaces render light
5. [ ] Cross-tab theme sync works
6. [ ] No OS `prefers-color-scheme` references in codebase (except tests documenting it's not used)
7. [ ] Unit tests pass with updated expectations

---

## Dependencies

### Prerequisites
- spaarke-mda-darkmode-theme-r1 (completed) — established the theme menu, localStorage pattern, and shared utilities

### Reused Components
- `sprk_ThemeMenu.js` — ribbon menu handler (no changes)
- `detectDarkModeFromNavbar()` — navbar color detection (no changes)
- `setupThemeListener()` — event listener pattern (remove OS listener only)

---

*Last updated: March 30, 2026*
