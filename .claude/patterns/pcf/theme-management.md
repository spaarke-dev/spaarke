# Theme Management Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Implementing dark mode support or theme-aware styling in PCF controls.

## Read These Files
1. `src/client/pcf/VisualHost/control/providers/ThemeProvider.ts` — Theme detection and FluentProvider setup
2. `src/client/pcf/VisualHost/control/index.ts` — Theme wiring in control lifecycle
   <!-- Corrected 2026-09-01: both pointed at the DELETED UniversalDatasetGrid. VisualHost holds the same
        two files at the same relative paths, so the pattern below transfers unchanged. Lighter
        alternative if you only need detection: SemanticSearchControl/*/services/ThemeService.ts. -->

## Constraints
- **ADR-021**: All UI must use Fluent UI v9 — no hard-coded colors; dark mode required
- MUST use `context.fluentDesignLanguage?.tokenTheme` for theme tokens
- MUST NOT hard-code any color values — use Fluent v9 design tokens

## Key Rules
- Get theme from `context.fluentDesignLanguage` in `updateView` — pass to `FluentProvider`
- Fallback: `webLightTheme` if `fluentDesignLanguage` unavailable (older MDA versions)
- CSS: use `var(--colorNeutralBackground1)` tokens, never `#fff` or `rgb()` literals
- Test both light and dark mode — MDA dark mode toggle changes tokens at runtime
