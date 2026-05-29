# UI Patterns Index

> **Last Reviewed**: 2026-05-26
> **Status**: Current

> Pointer-based pattern files for Spaarke UI work using Fluent UI v9 across PCF, Code Pages, External SPA, Office Add-ins, and MCP App widgets.

| Pattern | When to Load | Last Reviewed | Status |
|---|---|---|---|
| [choice-dialog-pattern.md](choice-dialog-pattern.md) | Implementing choice/selection dialog UI | 2026-04-05 | Verified |
| [fluent-v9-component-authoring.md](fluent-v9-component-authoring.md) | Authoring/modifying any Fluent v9 React component | 2026-05-26 | Current |
| [fluent-v9-theming.md](fluent-v9-theming.md) | Theme decisions, FluentProvider wiring, dark mode, Spaarke brand theme | 2026-05-26 | Current |
| [fluent-v9-portal-gotcha.md](fluent-v9-portal-gotcha.md) | Using Popover / Tooltip / Toast / Dialog / Menu / Combobox dropdown | 2026-05-26 | Current |
| [fluent-v9-react-version-boundaries.md](fluent-v9-react-version-boundaries.md) | Authoring in Spaarke.UI.Components OR bumping React versions | 2026-05-26 | Current |
| [fluent-v9-host-visual-fit.md](fluent-v9-host-visual-fit.md) | Surface-by-surface theme-source matrix; "make it look native" inside MDA / Canvas / Code Pages / Office Add-ins | 2026-05-28 | Current |

## Critical Constraint (ADR-021 + ADR-022)

All UI: Fluent UI v9 only. NO `@fluentui/react` (v8). NO hard-coded colors — use `tokens.*`. Spaarke.UI.Components must be React-16.14-safe (consumed by PCF).

## Related

- [`../pcf/fluent-v9-modern-theming.md`](../pcf/fluent-v9-modern-theming.md) + [`../pcf/fluent-v9-canvas-vs-mda-disabled.md`](../pcf/fluent-v9-canvas-vs-mda-disabled.md) — PCF-specific Fluent v9 patterns
- [`../../skills/fluent-v9-component/SKILL.md`](../../skills/fluent-v9-component/SKILL.md) — skill that loads these patterns on UI tasks
- [`../../../knowledge/fluent-ui-v9/docs/INDEX.md`](../../../knowledge/fluent-ui-v9/docs/INDEX.md) — verbose Microsoft + MVP reference archive
