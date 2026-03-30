# MDA Dark Mode Theme R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-30
> **Source**: design.md

## Executive Summary

Eliminate inconsistent light/dark mode rendering across Spaarke by consolidating three theme utility files into a single authoritative module, removing OS `prefers-color-scheme` fallback from all 7 violating surfaces, deploying the theme flyout ribbon to all entity forms and grids, and adding Dataverse-backed cross-device theme persistence. Establish a mandatory shared component library theme protocol.

## Scope

### In Scope
- Consolidate `themeStorage.ts`, `codePageTheme.ts`, and `themeDetection.ts` into single module
- Remove OS `prefers-color-scheme` fallback from 7 files
- Remove inlined/duplicated theme code from 5 locations (~710 lines)
- Fix LegalWorkspace storage key (`spaarke-workspace-theme` → `spaarke-theme`)
- Update 6 Code Page ThemeProvider wrappers
- Update 11 Code Page App.tsx / main.tsx direct consumers
- Delete 2 duplicate `useThemeDetection.ts` hooks
- Replace 3 PCF controls with inlined theme code
- Verify 3 PCF ThemeService files
- Remove OS listener from `sprk_ThemeMenu.js`
- Deploy theme flyout ribbon to 6 missing entities
- Add Form ribbon location to 3 existing entities (currently HomepageGrid only)
- Update "Auto" label from "follows system" to "follows app"
- Add `ThemePreference` (100000001) to `sprk_preferencetype` option set
- Implement hybrid localStorage + Dataverse sync for cross-device persistence
- Create theme protocol in `.claude/patterns/` or `.claude/constraints/`
- Update unit tests

### Out of Scope
- High contrast or custom branded themes
- SharePoint Embedded preview iframe theming
- MDA app shell theming (header, nav, form chrome)
- Custom Page dialog chrome dark mode
- Office Add-in theme changes (intentional isolation via sessionStorage)

### Affected Areas
- `src/client/shared/Spaarke.UI.Components/src/utils/` — core theme utilities (consolidate)
- `src/solutions/*/src/providers/ThemeProvider.ts` — 6 Code Page wrappers (update imports)
- `src/solutions/*/src/main.tsx` and `src/solutions/*/src/App.tsx` — 11 entry points (update imports)
- `src/client/code-pages/AnalysisWorkspace/src/hooks/` — delete useThemeDetection.ts
- `src/client/code-pages/PlaybookBuilder/src/hooks/` — delete useThemeDetection.ts
- `src/client/code-pages/SemanticSearch/src/providers/` — replace self-contained ThemeProvider
- `src/client/pcf/UniversalQuickCreate/control/index.ts` — remove ~160 inline lines
- `src/client/pcf/EmailProcessingMonitor/control/index.ts` — remove ~70 inline lines
- `src/client/pcf/VisualHost/control/providers/ThemeProvider.ts` — replace ~240 line reimplementation
- `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` — remove OS listener
- `src/client/pcf/SemanticSearchControl/.../services/ThemeService.ts` — remove OS listener
- `src/client/pcf/RelatedDocumentCount/.../services/ThemeService.ts` — remove OS listener
- `src/solutions/LegalWorkspace/src/hooks/useTheme.ts` — fix storage key + remove OS fallback
- `src/client/webresources/js/sprk_ThemeMenu.js` — remove OS listener, add Dataverse persist call
- `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` — add 6 entities, update 3 existing
- `.claude/patterns/` or `.claude/constraints/` — new theme protocol document

## Requirements

### Functional Requirements

1. **FR-01**: Unified theme utility — consolidate `codePageTheme.ts`, `themeStorage.ts`, and `themeDetection.ts` into a single `themeStorage.ts` module. Acceptance: Only one theme utility file exists; barrel export updated; all consumers compile.

2. **FR-02**: Remove OS preference fallback — delete `getSystemThemePreference()` function and all `window.matchMedia('(prefers-color-scheme: dark)')` listeners from production code. Acceptance: `grep -r "prefers-color-scheme" src/` returns zero hits in production `.ts`/`.tsx`/`.js` files (test files excepted).

3. **FR-03**: Unified priority chain — all surfaces resolve theme via: (1) localStorage `spaarke-theme`, (2) PCF `context.fluentDesignLanguage.isDarkTheme` (when available), (3) navbar DOM color detection, (4) default light. Acceptance: User with OS dark mode + no Spaarke preference sees light theme on ALL surfaces.

4. **FR-04**: Code Page functions in unified module — export `resolveCodePageTheme()` and `setupCodePageThemeListener()` from `themeStorage.ts`. Acceptance: All Code Pages import from `themeStorage` (not `codePageTheme`); `codePageTheme.ts` deleted.

5. **FR-05**: Remove inlined theme code from PCF controls — replace inline implementations in UniversalQuickCreate (~160 lines), EmailProcessingMonitor (~70 lines), and VisualHost ThemeProvider (~240 lines) with imports from `@spaarke/ui-components`. Acceptance: No inline `getUserThemePreference` or `getEffectiveDarkMode` functions in PCF control files.

6. **FR-06**: Delete duplicate useThemeDetection hooks — remove `useThemeDetection.ts` from AnalysisWorkspace and PlaybookBuilder code pages; replace with shared utility import. Acceptance: Files deleted; `index.tsx` updated; both code pages build.

7. **FR-07**: Fix LegalWorkspace storage key — change `useTheme.ts` from `spaarke-workspace-theme` to `spaarke-theme`. Acceptance: `grep -r "spaarke-workspace-theme" src/` returns zero hits.

8. **FR-08**: Standardize localStorage key — all MDA surfaces use `spaarke-theme` exclusively. Acceptance: `grep -r "spaarke-theme" src/` shows only the standard key (no variants except Office Add-ins using sessionStorage).

9. **FR-09**: Deploy theme flyout to missing entities — add `CustomAction` ribbon XML for: `sprk_workassignment`, `sprk_analysisplaybook`, `sprk_analysisoutput`, `sprk_communication`, `sprk_eventtodo`, `sprk_eventtype`. Acceptance: Theme flyout visible on all 6 entity HomepageGrid views.

10. **FR-10**: Add Form ribbon location — add Form `CustomAction` for `sprk_project`, `sprk_invoice`, `sprk_event` (currently HomepageGrid only). Acceptance: Theme flyout visible on entity form command bars for all 3 entities.

11. **FR-11**: Update "Auto" label — change "Auto (follows system)" to "Auto (follows app)" in all ribbon menu labels and tooltips. Acceptance: No ribbon XML contains "follows system".

12. **FR-12**: Dataverse theme persistence — add preference type `100000001 = ThemePreference` to `sprk_preferencetype` option set. Store user's theme choice as `"dark"`, `"light"`, or `"auto"` in `sprk_preferencevalue`. Acceptance: Theme change creates/updates `sprk_userpreference` record.

13. **FR-13**: Hybrid sync on page load — read localStorage first (instant render), then async-fetch Dataverse preference; if different, update localStorage and re-render. Acceptance: No flash-of-wrong-theme on page load; Dataverse value wins if it differs.

14. **FR-14**: Cross-device theme sync — user sets dark mode on Device A; logs into Device B; dark mode loads after first page load. Acceptance: New device renders correct theme after initial Dataverse sync.

15. **FR-15**: Theme protocol document — create `.claude/patterns/theme-management.md` or `.claude/constraints/theme.md` with mandatory MUST/MUST NOT rules for all new components. Acceptance: Document exists with PCF and Code Page patterns.

16. **FR-16**: Update unit tests — remove test cases asserting OS preference fallback; add tests for: no preference → light theme; Code Page functions; Dataverse sync stubs. Acceptance: All tests pass.

### Non-Functional Requirements

- **NFR-01**: Theme switch latency — theme change renders within one React render cycle (<100ms perceived). Dataverse write is async and does not block UI.
- **NFR-02**: No flash-of-wrong-theme — localStorage cache ensures correct theme renders before Dataverse async fetch completes.
- **NFR-03**: Graceful degradation — if Dataverse is unavailable, fall back to localStorage. If localStorage is empty, default to light. Never throw.
- **NFR-04**: Bundle size — removing ~710 lines of inlined duplicate code across 5 PCF/code page files should reduce total bundle size.

## Technical Constraints

### Applicable ADRs
- **ADR-021**: Fluent UI v9 design system — all UI must use Fluent v9; Spaarke theme system controls all surfaces (NOT OS preference)
- **ADR-012**: Shared component library — theme utilities must live in `@spaarke/ui-components`; no local duplication
- **ADR-006**: No legacy JS web resources — `sprk_ThemeMenu.js` is invocation-only (all logic in shared library)
- **ADR-022**: PCF platform libraries — React 16 APIs; PCF controls use platform-provided React

### MUST Rules
- MUST import theme utilities from `@spaarke/ui-components` (themeStorage module)
- MUST NOT inline or duplicate theme detection logic locally
- MUST NOT consult OS `prefers-color-scheme` for theme resolution
- MUST NOT use any localStorage key other than `spaarke-theme` for theme preference (Office Add-ins excepted)
- MUST use unified priority chain: localStorage → PCF context → navbar → light default
- MUST clean up theme listener on component destroy/unmount
- MUST use `FluentProvider` with theme from `resolveThemeWithUserPreference()` (PCF) or `resolveCodePageTheme()` (Code Pages)

### Existing Patterns to Follow
- See `src/solutions/LegalWorkspace/src/hooks/useUserPreferences.ts` for `sprk_userpreference` read/write pattern
- See `src/solutions/LegalWorkspace/src/services/DataverseService.ts` lines 545-618 for `getUserPreference`/`setUserPreference`
- See `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` for per-entity ribbon XML pattern
- See `.claude/patterns/pcf/theme-management.md` for PCF theme listener pattern

## Success Criteria

1. [ ] Single theme utility module — no `codePageTheme.ts`, no inline duplicates — Verify: `find src/ -name "codePageTheme*"` returns nothing
2. [ ] OS dark mode + Spaarke "Auto" → ALL surfaces render light — Verify: manual test on OS dark mode machine
3. [ ] Spaarke "Dark" → ALL surfaces render dark (PCF + Code Pages) — Verify: manual test
4. [ ] Spaarke "Light" → ALL surfaces render light — Verify: manual test
5. [ ] Cross-tab theme sync works — Verify: change theme in Tab A, Tab B updates
6. [ ] No `prefers-color-scheme` in production code — Verify: `grep -r "prefers-color-scheme" src/ --include="*.ts" --include="*.tsx" --include="*.js"` (excluding test files)
7. [ ] No inlined theme utilities — Verify: no `getUserThemePreference` function definitions outside shared library
8. [ ] Single localStorage key — Verify: `grep -r "spaarke-.*-theme" src/` returns zero hits
9. [ ] Unit tests pass — Verify: `npm test` in shared library
10. [ ] Theme protocol documented — Verify: file exists in `.claude/patterns/` or `.claude/constraints/`
11. [ ] Dataverse persistence works — Verify: check `sprk_userpreference` record after theme change
12. [ ] Cross-device sync — Verify: set dark on Device A, open Device B, dark loads
13. [ ] Theme flyout on ALL entities — Verify: navigate to each entity grid/form, flyout visible
14. [ ] "Auto (follows app)" label — Verify: inspect ribbon menu text on any entity

## Dependencies

### Prerequisites
- spaarke-mda-darkmode-theme-r1 (completed) — theme menu, localStorage pattern, shared utilities
- `sprk_userpreference` entity exists in Dataverse (confirmed — used for Kanban thresholds)

### External Dependencies
- Dataverse admin access to add option set value to `sprk_preferencetype`
- Solution import access to deploy ribbon XML changes

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| OS preference | Should "Auto" follow OS or app theme? | Follow app (Power Platform), not OS | Removed all `prefers-color-scheme` references |
| Dataverse storage | Should theme persist cross-device? | Yes, via `sprk_userpreference` | Added hybrid localStorage + Dataverse sync |
| Storage field | Add field to User table or use sprk_userpreference? | Use existing `sprk_userpreference` with new preference type | No schema changes needed, just option set value |
| Ribbon approach | Global button vs per-entity? | Per-entity — deploy to all main entities | Add 6 missing entities + Form locations for 3 existing |
| Office Add-ins | Include in unified theme? | No — intentional isolation (sessionStorage + Office.context) | Excluded from scope |

## Assumptions

- **Option set value**: Assuming `100000001` is available for `ThemePreference` in `sprk_preferencetype` — verify before implementation
- **Ribbon import**: Assuming all entity solutions accept `CustomAction` additions without conflicts with existing ribbon customizations
- **Dataverse latency**: Assuming async Dataverse read completes within 500ms in dev environment — acceptable for background sync

## Unresolved Questions

None — all questions resolved during design session.

---

*AI-optimized specification. Original: design.md*
