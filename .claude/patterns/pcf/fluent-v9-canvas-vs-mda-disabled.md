# PCF Canvas vs MDA — Disabled-State Handling

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Building a PCF that ships to BOTH Canvas Apps and Model-Driven Apps; user reports "disabled state looks wrong / unstyled / shows native browser disabled appearance" in one app type.

## Read These Files

1. `src/client/pcf/UniversalDatasetGrid/control/index.ts` — Spaarke's current Canvas/MDA detection
2. Drill-down: `knowledge/fluent-ui-v9/docs/community/birkelbach-style-fluent-ui-9-pcfs.md` — full pattern with code

## Constraints

- **ADR-021**: Fluent v9 only. Disabled state must follow Fluent 2 visual language.

## The Problem

Fluent v9's native `disabled` prop renders correctly in **Canvas Apps** but produces visually wrong output in **Model-Driven Apps** (washed out / browser-default look). The fix is to use `readOnly` in MDA with a theme override.

## Decision Table

| App type | Disabled prop | Theme override |
|---|---|---|
| Canvas App | `disabled={isDisabled}` | NONE |
| Model-Driven App | `readOnly={isDisabled}` | YES — `colorCompoundBrandStroke*` → `colorNeutralStroke1*` |

## Detection

```ts
// Detect Canvas via manifest parameter (Birkelbach pattern — explicit, reliable)
//
// In ControlManifest.Input.xml:
//   <property name="isCanvas" of-type="Enum" usage="input" required="true" pfx-default-value="'YES'">
//     <value name="Yes" display-name-key="Yes">YES</value>
//     <value name="No"  display-name-key="No" default="true">NO</value>
//   </property>
//
// In control:
const isCanvasApp = context.parameters.isCanvas.raw === 'YES';
```

Alternative: detect MDA via `context.fluentDesignLanguage` presence (per Rivard 2023-10) — but undocumented and brittle. Prefer the explicit manifest property.

## Code Pattern (Universal — works for both)

```tsx
export const MyInput: React.FC<Props> = ({ name, isDisabled, theme, isCanvasApp }) => {
  const myTheme = isDisabled && !isCanvasApp
    ? {
        ...theme,
        colorCompoundBrandStroke:         theme.colorNeutralStroke1,
        colorCompoundBrandStrokeHover:    theme.colorNeutralStroke1Hover,
        colorCompoundBrandStrokePressed:  theme.colorNeutralStroke1Pressed,
        colorCompoundBrandStrokeSelected: theme.colorNeutralStroke1Selected,
      }
    : theme;

  return (
    <FluentProvider theme={myTheme}>
      <Input
        value={name}
        appearance="filled-darker"           // ← matches modern Power Apps look
        readOnly={isDisabled}                // ← MDA path
        disabled={isDisabled && isCanvasApp} // ← Canvas path
      />
    </FluentProvider>
  );
};
```

## Key Rules

- ✅ Use `appearance="filled-darker"` on Input/Combobox/Dropdown — closest match to modern Power Apps controls.
- ✅ Combobox specifically: in MDA disabled state, render an `<Input readOnly>` instead — Combobox's disabled rendering is worse than Input's.
- ❌ Don't apply the neutral-stroke theme override in Canvas — the native disabled rendering is correct there.

## See Also

- [`fluent-v9-modern-theming.md`](./fluent-v9-modern-theming.md) — overall PCF theme integration
- [`../ui/fluent-v9-theming.md`](../ui/fluent-v9-theming.md) — token reference
