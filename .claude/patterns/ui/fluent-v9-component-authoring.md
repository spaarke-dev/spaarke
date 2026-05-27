# Fluent v9 Component Authoring

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Authoring a new component in `src/client/shared/Spaarke.UI.Components/` OR modifying any Fluent v9 React component in `src/client/{code-pages,external-spa,office-addins,pcf}/**/components/`.

## Read These Files

1. `src/client/shared/Spaarke.UI.Components/src/index.ts` — existing exports; check before creating new
2. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Button/ButtonAppearance.stories.tsx` — slot composition pattern
3. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Provider/FluentProviderDefault.stories.tsx` — root mount
4. Drill-down only if needed: `knowledge/fluent-ui-v9/docs/slots-architecture.md` (when authoring novel composition), `knowledge/fluent-ui-v9/docs/styling-griffel.md` (when debugging style precedence)

## Constraints

- **ADR-021**: All UI Fluent v9 only. No `@fluentui/react` (v8). No hard-coded colors.
- **ADR-012**: If a component fits >1 surface, it belongs in `Spaarke.UI.Components` — do NOT duplicate.

## Key Rules

- `makeStyles` at **module scope** (never inside the component body — re-creates styles every render).
- All colors / spacing / radius via `tokens.*` from `@fluentui/react-components`. NEVER raw hex / `var(--...)` / rgb literals.
- `mergeClasses(componentClasses, props.className)` — **props.className LAST** so callers override.
- Use `shorthands.border()` / `shorthands.padding()` / `shorthands.margin()` — Griffel rejects CSS shorthand properties.
- Default to slot composition (`<Component icon={...} />`). Fall back to the hooks API only when slots can't express it.
- `FluentProvider` mounts ONCE at the surface root — never per-component (except portal re-wrap, see [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md)).
- If shipping in `Spaarke.UI.Components` (consumed by PCF + Code Pages), the component MUST be React-16.14-safe — no `createRoot`, no React-18-only hooks. See [`fluent-v9-react-version-boundaries.md`](./fluent-v9-react-version-boundaries.md).

## Code Pattern

```tsx
import {
  makeStyles, mergeClasses, tokens, shorthands,  // ← always import tokens + shorthands
  Button, FluentProvider, webLightTheme,
} from '@fluentui/react-components';

const useStyles = makeStyles({                   // ← module scope, NOT inside component
  root: {
    color: tokens.colorNeutralForeground1,      // ← tokens, not hex
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },
});

export const MyControl: React.FC<MyControlProps> = ({ className, label }) => {
  const styles = useStyles();
  return (
    <Button
      className={mergeClasses(styles.root, className)}  // ← props.className LAST
      appearance="primary"
    >
      {label}
    </Button>
  );
};
```

## See Also

- [`fluent-v9-theming.md`](./fluent-v9-theming.md) — FluentProvider setup + Spaarke theming convention
- [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md) — `Popover` / `Tooltip` / `Dialog` / `Menu` / `Toast` re-wrap
- [`../../knowledge/fluent-ui-v9/NOTES.md`](../../../knowledge/fluent-ui-v9/NOTES.md) — Spaarke-specific commentary (stub, fill from real use)
