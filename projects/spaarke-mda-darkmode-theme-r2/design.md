# MDA Dark Mode Theme R2 — Unified Theme Consistency

> **Project**: spaarke-mda-darkmode-theme-r2
> **Status**: Design
> **Priority**: High
> **Prerequisite**: spaarke-mda-darkmode-theme-r1 (completed December 2025)
> **Last Updated**: March 30, 2026

---

## Executive Summary

Fix inconsistent light/dark mode switching across Spaarke surfaces by consolidating three separate theme utility implementations into a single authoritative module, eliminating OS `prefers-color-scheme` fallback from all surfaces, standardizing the localStorage key across modules, removing inlined duplicate theme code from PCF controls and code pages, and establishing a shared component library theme protocol that all new components must follow.

---

## Problem Statement

### The Inconsistency

When a user has OS set to **dark mode** and Spaarke theme preference set to **"Auto"** (or not set):

| Surface | Theme Rendered | Why |
|---------|---------------|-----|
| PCF controls (UniversalDatasetGrid, VisualHost, UniversalQuickCreate, EmailProcessingMonitor) | **Dark** | Falls back to OS `prefers-color-scheme` |
| Code Pages (LegalWorkspace, wizards, side panes) | **Light** | Ignores OS preference, falls back to light |
| Code Pages (AnalysisWorkspace, PlaybookBuilder) | **Dark** | Their `useThemeDetection.ts` also consults OS preference |
| LegalWorkspace (useTheme hook) | **Dark** | Uses different localStorage key AND consults OS preference |

This produces mixed themes on the same page.

### Root Causes

**1. Three core utility files with different fallback chains:**

| File | Used By | OS Fallback | Storage Key |
|------|---------|-------------|-------------|
| `codePageTheme.ts` | Most Code Pages | No ✅ | `spaarke-theme` |
| `themeStorage.ts` | PCF controls | Yes ❌ | `spaarke-theme` |
| `themeDetection.ts` | Legacy PCF pattern | N/A (context-based) | N/A |

**2. Five locations with inlined/duplicated theme code (not importing from shared library):**

| File | Lines | Issue |
|------|-------|-------|
| `pcf/UniversalQuickCreate/control/index.ts` | ~160 lines inlined | Marked "GitHub #234 — import when published" |
| `pcf/EmailProcessingMonitor/control/index.ts` | ~70 lines inlined | Same tracking comment |
| `pcf/VisualHost/control/providers/ThemeProvider.ts` | ~240 lines | Full reimplementation |
| `code-pages/AnalysisWorkspace/src/hooks/useThemeDetection.ts` | ~120 lines | Custom hook with OS fallback |
| `code-pages/PlaybookBuilder/src/hooks/useThemeDetection.ts` | ~120 lines | Copy of AnalysisWorkspace |

**3. Inconsistent localStorage keys:**

| Key | Used By | Issue |
|-----|---------|-------|
| `spaarke-theme` | Most surfaces | Standard ✅ |
| `spaarke-workspace-theme` | LegalWorkspace `useTheme.ts` hook | Different key — not synced ❌ |
| `spaarke-theme-preference` (sessionStorage) | Office Add-ins | Intentional isolation (OK) |

**4. Seven files actively violate ADR-021** by consulting `prefers-color-scheme`:

1. `pcf/VisualHost/control/providers/ThemeProvider.ts`
2. `pcf/UniversalQuickCreate/control/index.ts`
3. `pcf/EmailProcessingMonitor/control/index.ts`
4. `code-pages/PlaybookBuilder/src/hooks/useThemeDetection.ts`
5. `code-pages/AnalysisWorkspace/src/hooks/useThemeDetection.ts`
6. `solutions/LegalWorkspace/src/hooks/useTheme.ts`
7. `webresources/js/sprk_ThemeMenu.js` (lines 96-107)

---

## Full Component Inventory

### A. Core Theme Utility Files (Shared Library)

| File | Action | Notes |
|------|--------|-------|
| `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts` | **Modify** — becomes the single authority | Remove OS fallback, add Code Page functions |
| `src/client/shared/Spaarke.UI.Components/src/utils/codePageTheme.ts` | **Delete** — merge into themeStorage.ts | All consumers migrate |
| `src/client/shared/Spaarke.UI.Components/src/utils/themeDetection.ts` | **Evaluate** — deprecate if superseded | Legacy pattern, check if still needed |
| `src/client/shared/Spaarke.UI.Components/src/utils/index.ts` | **Update** — remove codePageTheme export | Update barrel |
| `src/client/shared/Spaarke.UI.Components/src/utils/__tests__/themeStorage.test.ts` | **Update** — fix OS fallback expectations | Tests currently assert wrong behavior |

### B. Code Page Solution ThemeProvider Wrappers (6 files — update imports)

| Solution | File | Current Import | Action |
|----------|------|----------------|--------|
| LegalWorkspace | `src/solutions/LegalWorkspace/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import to `themeStorage` |
| EventsPage | `src/solutions/EventsPage/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import |
| CalendarSidePane | `src/solutions/CalendarSidePane/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import |
| SpeAdminApp | `src/solutions/SpeAdminApp/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import |
| WorkspaceLayoutWizard | `src/solutions/WorkspaceLayoutWizard/src/providers/ThemeProvider.ts` | `codePageTheme` | Update import |

### C. Code Page Solution App.tsx / main.tsx (direct consumers — update imports)

| Solution | File | Current Pattern | Action |
|----------|------|----------------|--------|
| TodoDetailSidePane | `src/solutions/TodoDetailSidePane/src/App.tsx` | Imports `resolveCodePageTheme` | Update import path |
| SmartTodo | `src/solutions/SmartTodo/src/App.tsx` | Imports `resolveCodePageTheme` | Update import path |
| SummarizeFilesWizard | `src/solutions/SummarizeFilesWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| PlaybookLibrary | `src/solutions/PlaybookLibrary/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| DocumentUploadWizard | `src/solutions/DocumentUploadWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| CreateWorkAssignmentWizard | `src/solutions/CreateWorkAssignmentWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| CreateTodoWizard | `src/solutions/CreateTodoWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| CreateProjectWizard | `src/solutions/CreateProjectWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| CreateMatterWizard | `src/solutions/CreateMatterWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| CreateEventWizard | `src/solutions/CreateEventWizard/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |
| FindSimilarCodePage | `src/solutions/FindSimilarCodePage/src/main.tsx` | Imports `resolveCodePageTheme` | Update import path |

### D. Code Pages with Duplicate useThemeDetection Hooks (delete, migrate to shared)

| File | Lines | Action |
|------|-------|--------|
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useThemeDetection.ts` | ~120 | **Delete** — replace with shared utility import |
| `src/client/code-pages/PlaybookBuilder/src/hooks/useThemeDetection.ts` | ~120 | **Delete** — replace with shared utility import |
| `src/client/code-pages/AnalysisWorkspace/src/index.tsx` | | Update import |
| `src/client/code-pages/PlaybookBuilder/src/index.tsx` | | Update import |
| `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` | | Verify import |
| `src/client/code-pages/SemanticSearch/src/index.tsx` | | Verify import |
| `src/client/code-pages/SemanticSearch/src/providers/ThemeProvider.ts` | | **Replace** self-contained impl with shared import |

### E. PCF Controls with Duplicated Theme Code (replace inline with shared import)

| Control | File | Lines Inlined | Action |
|---------|------|---------------|--------|
| UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/control/index.ts` | ~160 | **Remove** inline code, import from `@spaarke/ui-components` |
| EmailProcessingMonitor | `src/client/pcf/EmailProcessingMonitor/control/index.ts` | ~70 | **Remove** inline code, import from `@spaarke/ui-components` |
| VisualHost | `src/client/pcf/VisualHost/control/providers/ThemeProvider.ts` | ~240 | **Replace** full reimplementation with shared import |

### F. PCF Controls with ThemeProvider Wrappers (verify, remove OS fallback)

| Control | File | Action |
|---------|------|--------|
| UniversalDatasetGrid | `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` | Verify uses shared lib; remove OS listener |
| SemanticSearchControl | `src/client/pcf/SemanticSearchControl/.../services/ThemeService.ts` | Verify; remove OS listener |
| RelatedDocumentCount | `src/client/pcf/RelatedDocumentCount/.../services/ThemeService.ts` | Verify; remove OS listener |
| ScopeConfigEditor | `src/client/pcf/ScopeConfigEditor/.../components/ScopeEditorShell.tsx` | Remove `prefers-color-scheme` CSS reference |

### G. PCF Controls with Theme Listener (verify pattern)

| Control | File | Action |
|---------|------|--------|
| AssociationResolver | `src/client/pcf/AssociationResolver/index.ts` | Verify `spaarke-theme` key usage |
| UpdateRelatedButton | `src/client/pcf/UpdateRelatedButton/index.ts` | Verify `spaarke-theme` key usage |
| DrillThroughWorkspace | `src/client/pcf/DrillThroughWorkspace/control/index.ts` | Verify theme listener pattern |

### H. LegalWorkspace Custom Hook (fix storage key + remove OS fallback)

| File | Issue | Action |
|------|-------|--------|
| `src/solutions/LegalWorkspace/src/hooks/useTheme.ts` | Uses `spaarke-workspace-theme` key (different!) + OS fallback | **Rewrite** to use standard `spaarke-theme` key via shared utility |
| `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` | Consumes useTheme | Update if hook signature changes |
| `src/solutions/LegalWorkspace/src/components/Shell/ThemeToggle.tsx` | Theme toggle component | Verify uses standard key |

### I. Web Resource (remove OS listener)

| File | Action |
|------|--------|
| `src/client/webresources/js/sprk_ThemeMenu.js` | **Remove** `prefers-color-scheme` listener (lines 96-107) |

### J. Office Add-ins (intentional exception — no changes)

| File | Notes |
|------|-------|
| `src/client/office-addins/shared/taskpane/hooks/useTheme.ts` | Uses `sessionStorage` + Office.context.officeTheme — intentionally isolated from MDA theme system. **No changes.** |

### K. Shared Component with Theme Reference

| File | Action |
|------|--------|
| `src/solutions/LegalWorkspace/src/components/RecordCards/DocumentCard.tsx` | Uses `getEffectiveDarkMode` — verify import source |
| `src/client/external-spa/src/App.tsx` | Verify theme import |

---

## Shared Component Library Theme Protocol

### New Standard: All Components Must Follow

Add to `.claude/constraints/` or `.claude/patterns/` as a mandatory protocol:

**Theme Resolution — Mandatory Pattern for All Spaarke UI Surfaces**

```
MUST import theme utilities from @spaarke/ui-components (themeStorage module)
MUST NOT inline or duplicate theme detection logic locally
MUST NOT consult OS prefers-color-scheme for theme resolution
MUST NOT use any localStorage key other than 'spaarke-theme' for theme preference
MUST use the unified priority chain:
  1. localStorage 'spaarke-theme' (user explicit)
  2. PCF context.fluentDesignLanguage.isDarkTheme (PCF only)
  3. Navbar DOM color detection
  4. Default: light theme

MUST listen for theme changes via setupThemeListener() from shared library
MUST use FluentProvider with theme from resolveThemeWithUserPreference() or resolveCodePageTheme()
MUST clean up theme listener on component destroy/unmount
```

**For PCF Controls:**
```typescript
import {
  setupThemeListener,
  resolveThemeWithUserPreference,
} from "@spaarke/ui-components/utils/themeStorage";

// In init()
this.cleanupThemeListener = setupThemeListener((isDark) => {
  this.notifyOutputChanged();
}, context);

// In updateView()
const theme = resolveThemeWithUserPreference(context);
return React.createElement(FluentProvider, { theme }, children);

// In destroy()
this.cleanupThemeListener?.();
```

**For Code Pages:**
```typescript
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";

const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    return setupCodePageThemeListener((newTheme) => setTheme(newTheme));
  }, []);

  return <FluentProvider theme={theme}>{children}</FluentProvider>;
};
```

**Exception: Office Add-ins** — Use `sessionStorage` + `Office.context.officeTheme`. Documented as intentional isolation from MDA theme system.

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

### What Does NOT Change

- localStorage key (`spaarke-theme`) — same
- Custom event name (`spaarke-theme-change`) — same
- Theme values (`auto`, `light`, `dark`) — same
- Cross-tab sync via `storage` event — same
- Navbar DOM detection algorithm — same
- Office Add-in theme behavior — intentional exception

---

## Design Decisions

### "Auto" Means App Theme, Not OS Theme

When a user selects "Auto":
1. Check navbar color → if detectable, use it (Power Apps controls the app theme)
2. Check PCF context → if available, use `isDarkTheme`
3. If neither is available → default to light

Users who want OS-following behavior can explicitly set dark/light to match their OS.

### Single Module, Not Three

Merging into `themeStorage.ts` because:
- It has the richest API (PCF context support, persistence functions)
- PCF controls are the harder surface to change
- Code Pages can easily consume any function signature

### Delete codePageTheme.ts, Don't Deprecate

Clean delete with import migration. Keeping deprecated re-exports adds confusion.

### Fix LegalWorkspace Storage Key

The `spaarke-workspace-theme` key in `useTheme.ts` is a bug — it means the LegalWorkspace theme doesn't sync with the ribbon menu or other surfaces. Replace with standard `spaarke-theme`.

### Remove All Inlined Theme Code

The tracking comment "GitHub #234 — Import from @spaarke/ui-components when published" in UniversalQuickCreate and EmailProcessingMonitor indicates these were always meant to be temporary. The shared library is published. Clean up now.

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
- Consolidate `themeStorage.ts`, `codePageTheme.ts`, and `themeDetection.ts` into single module
- Remove OS `prefers-color-scheme` fallback from ALL surfaces (7 files)
- Remove inlined/duplicated theme code from 5 locations
- Fix LegalWorkspace storage key (`spaarke-workspace-theme` → `spaarke-theme`)
- Update all Code Page ThemeProvider imports (6 wrappers + 11 App/main files)
- Update all PCF ThemeProvider imports (3 controls with inline code + 3 with ThemeService)
- Remove OS listener from `sprk_ThemeMenu.js`
- Update unit tests
- Update barrel exports
- Create shared component library theme protocol (constraint/pattern doc)

### Out of Scope
- New theme options (high contrast, custom branded themes)
- SharePoint preview iframe theming
- MDA app shell theming
- Dialog chrome dark mode
- Theme menu UX changes
- Office Add-in theme changes (intentional exception)

---

## Success Criteria

1. [ ] Single theme utility module — no `codePageTheme.ts`, no `themeDetection.ts` (if superseded)
2. [ ] OS dark mode + Spaarke "Auto" → ALL surfaces render light
3. [ ] Spaarke "Dark" → ALL surfaces render dark (PCF + Code Pages)
4. [ ] Spaarke "Light" → ALL surfaces render light
5. [ ] Cross-tab theme sync works
6. [ ] No `prefers-color-scheme` references in production code (only in test assertions documenting it's not used)
7. [ ] No inlined theme utilities — all consumers import from `@spaarke/ui-components`
8. [ ] Single localStorage key (`spaarke-theme`) across all MDA surfaces
9. [ ] Unit tests pass with updated expectations
10. [ ] Theme protocol documented in `.claude/patterns/` or `.claude/constraints/`

---

## Dependencies

### Prerequisites
- spaarke-mda-darkmode-theme-r1 (completed) — established the theme menu, localStorage pattern, and shared utilities

### Reused Components
- `sprk_ThemeMenu.js` — ribbon menu handler (minor fix: remove OS listener)
- `detectDarkModeFromNavbar()` — navbar color detection (no changes)
- `setupThemeListener()` — event listener pattern (remove OS listener only)

---

*Last updated: March 30, 2026*
