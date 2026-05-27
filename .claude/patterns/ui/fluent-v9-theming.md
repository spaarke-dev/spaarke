# Fluent v9 Theming

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Choosing between platform-provided theme vs custom theme; designing a Spaarke brand-palette theme; adding dark-mode support; wiring `FluentProvider` for a new surface.

## Read These Files

1. `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` — Spaarke's reference theme-detection + FluentProvider wiring
2. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Theme/ThemeColors.stories.tsx` — full token grid
3. Drill-down only if needed: `knowledge/fluent-ui-v9/docs/theming.md` (custom brand ramp, extending tokens), `knowledge/fluent-ui-v9/docs/community/birkelbach-standard-custom-theming.md` (tinycolor2 brand-variant generation)

## Constraints

- **ADR-021**: Hard-coded colors forbidden. Dark mode required for any Spaarke surface that renders in MDA.
- For PCF surfaces: MUST consume `context.fluentDesignLanguage.tokenTheme` so customer-tenant themes propagate.

## Decision Table

| Surface | Theme source | Fallback |
|---|---|---|
| PCF (Canvas or MDA, virtual or non-virtual) | `context.fluentDesignLanguage?.tokenTheme` | `webLightTheme` if undefined (older MDA) |
| Code Page | `webLightTheme` / `webDarkTheme` via Spaarke shared provider | n/a — Code Pages have no platform theme |
| Office Add-in | Office host theme bridge → mapped to `webLightTheme` / `webDarkTheme` | `webLightTheme` |
| External SPA | Spaarke brand theme (custom `createLightTheme(brandRamp)`) | n/a |
| MCP App widget | `useThemeColors` host-bridge resolution | see `knowledge/mcp-apps/trey-research/` reference |

## Key Rules

- ❌ NEVER use `webLightTheme` directly in PCFs — always prefer `tokenTheme` from PCF context. Falling back to `webLightTheme` is fine ONLY when `fluentDesignLanguage` is unavailable.
- ❌ NEVER use `teamsHighContrastTheme` or other hard-coded high-contrast themes. Windows High Contrast mode is handled automatically by Fluent v9.
- ✅ Custom Spaarke brand theme: build via `createLightTheme(brandVariants)` + `createDarkTheme(brandVariants)`. Brand ramp lives in ONE place in `Spaarke.UI.Components` and is exported.
- ✅ Dark-mode detection: `context.fluentDesignLanguage?.isDarkTheme` (PCF) or media query / Office bridge (other surfaces).
- ✅ Token-name pattern: read tokens through the imported `tokens` object (`tokens.colorNeutralForeground1`). Never reference `var(--...)` directly — token names may change without notice.

## Code Pattern (PCF theme wiring)

```tsx
// In control/index.ts
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
  const theme = context.fluentDesignLanguage?.tokenTheme ?? webLightTheme; // ← platform first
  return React.createElement(FluentProvider, { theme }, React.createElement(MyComponent, { ... }));
}
```

## See Also

- [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md) — portals don't inherit FluentProvider styles
- [`../pcf/fluent-v9-modern-theming.md`](../pcf/fluent-v9-modern-theming.md) — the four PCF integration approaches
