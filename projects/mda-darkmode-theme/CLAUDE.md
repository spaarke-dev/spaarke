# CLAUDE.md - MDA Dark Mode Theme Project

> **Project**: mda-darkmode-theme
> **Status**: Not Started
> **Last Updated**: December 5, 2025

---

## Project Context

This project implements a dark mode theme menu for the Spaarke model-driven app using localStorage persistence and shared component library utilities.

## Quick Links

| Document | Purpose |
|----------|---------|
| [spec.md](./spec.md) | Complete technical specification |
| [plan.md](./plan.md) | Implementation plan with phases |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task breakdown and status tracking |

## Key Design Decisions

1. **Flyout menu over toggle button** - Matches MDA command bar pattern (like "Show As"); supports future theme additions
2. **localStorage over URL parameters** - Chosen for immediate application without page refresh
3. **Custom event for same-tab sync** - Standard `storage` event only fires in other tabs
4. **Three-state + extensible** - Auto/Dark/Light with room for custom themes
5. **PCF-level theming only** - Cannot theme app shell or SharePoint preview iframes
6. **Shared library (ADR-012)** - Theme utilities in `Spaarke.UI.Components`, not duplicated
7. **Minimal web resource (ADR-006)** - JS file is invocation only; logic in shared library
8. **No alert dialogs** - Visual change is the feedback; dialogs are disruptive
9. **DOM navbar fallback** - Custom Pages don't have `fluentDesignLanguage`; detect via navbar color
10. **Theme-aware icons** - All SVG icons use `currentColor` for light/dark compatibility

## ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-006 | PCF over web resources | Web resource is minimal (invocation only, <50 lines) |
| ADR-012 | Shared component library | Theme utilities in Spaarke.UI.Components |

## Implementation Notes

### localStorage Key
```javascript
const STORAGE_KEY = 'spaarke-theme';
// Values: 'auto' | 'dark' | 'light' | <future custom themes>
```

### Shared Library Location
```
src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts
```

### Theme Detection Priority (implemented in shared library)
1. localStorage (`spaarke-theme`) - User's explicit preference
2. Power Platform context (`context.fluentDesignLanguage?.isDarkTheme`) - App setting
3. DOM navbar detection - Custom Pages fallback (checks navbar background color)
4. System preference (`prefers-color-scheme`) - OS/browser setting

### PCF Theme Integration Pattern
```typescript
// Required imports
import { setupThemeListener, resolveThemeWithUserPreference } from '@spaarke/ui-components/utils/themeStorage';

// In init()
this.cleanupThemeListener = setupThemeListener((isDark) => {
    // Re-render with new theme
}, context);

// In destroy()
if (this.cleanupThemeListener) {
    this.cleanupThemeListener();
}
```

## Files to Create

```
src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts
src/client/shared/Spaarke.UI.Components/src/utils/__tests__/themeStorage.test.ts
src/client/webresources/js/sprk_ThemeMenu.js
src/client/assets/icons/sprk_ThemeMenu16.svg
src/client/assets/icons/sprk_ThemeAuto16.svg
src/client/assets/icons/sprk_ThemeLight16.svg
src/client/assets/icons/sprk_ThemeDark16.svg
```

## Files to Modify

| File | Action |
|------|--------|
| `src/client/pcf/SpeFileViewer/control/index.ts` | Add theme listener |
| `src/client/pcf/SpeFileViewer/control/FilePreview.tsx` | **REMOVE** theme toggle button |
| `src/client/pcf/UniversalDatasetGrid/control/index.ts` | Add theme listener |
| `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` | Update to use shared library |
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | Add theme support |
| `docs/ai-knowledge/architecture/sdap-pcf-patterns.md` | Add theming section |

## PCF Controls Requiring Updates

| Control | Current State | Action Required |
|---------|---------------|-----------------|
| SpeFileViewer | Has internal toggle | Remove toggle; use shared library |
| UniversalDatasetGrid | Has local ThemeProvider | Update to use shared library |
| UniversalQuickCreate | No theme support | Add theme support |

## Breaking Changes

- **SpeFileViewer**: Internal theme toggle button is removed
- All users must use the global command bar menu for theme selection

## Known Limitations

1. **SharePoint preview iframe cannot be themed** - Cross-origin security
2. **Model-driven app shell unchanged** - Only PCF content areas themed
3. **No checked state in menu** - By design per Fluent V9 pattern; theme change is the feedback

## Task Execution Order

Start with tasks that have no dependencies:
1. **001** - themeStorage.ts (unlocks most other tasks)
2. **003** - SVG icons (independent)

Then follow the critical path:
```
001 → 002 → 020 → 031 → 032
  ↘ 010 ↗
  ↘ 011 ↗
  ↘ 012 ↗
003 → 020
```

## Useful Commands

```bash
# Build shared library
cd src/client/shared/Spaarke.UI.Components && npm run build

# Build PCF control
cd src/client/pcf/SpeFileViewer && npm run build

# Build PCF solution
"/c/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe" \
  SpeFileViewerSolution/SpeFileViewerSolution.cdsproj /t:Rebuild /p:Configuration=Release

# Import solution
pac solution import --path [solution.zip] --async true --publish-changes true
```

## Related Files (Reference)

- `/src/client/shared/Spaarke.UI.Components/src/utils/themeDetection.ts` - Existing theme utilities
- `/src/client/pcf/SpeFileViewer/control/FilePreview.tsx` - Has theme toggle to remove
- `/src/client/pcf/SpeFileViewer/control/index.ts` - Needs theme listener
- `/src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` - Needs update

---

*Project-specific context for AI agents working on this feature*
