# Theme Management Pattern

## When
Implementing dark mode support or theme-aware styling in PCF controls.

## Read These Files
1. `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` — Theme detection and FluentProvider setup
2. `src/client/pcf/UniversalDatasetGrid/control/index.ts` — Theme wiring in control lifecycle

## Constraints
- **ADR-021**: All UI must use Fluent UI v9 — no hard-coded colors; dark mode required
- MUST use `context.fluentDesignLanguage?.tokenTheme` for theme tokens
- MUST NOT hard-code any color values — use Fluent v9 design tokens

## Key Rules
- Get theme from `context.fluentDesignLanguage` in `updateView` — pass to `FluentProvider`
- Fallback: `webLightTheme` if `fluentDesignLanguage` unavailable (older MDA versions)
- CSS: use `var(--colorNeutralBackground1)` tokens, never `#fff` or `rgb()` literals
- Test both light and dark mode — MDA dark mode toggle changes tokens at runtime
