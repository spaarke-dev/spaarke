# Fluent v9 + React Version Boundaries

> **Last Reviewed**: 2026-05-26
> **Status**: Current

## When

Authoring code in `Spaarke.UI.Components` (must run in both PCF + Code Pages); bumping `react` / `react-dom` / `@fluentui/react-components` in any surface; debugging "works in Code Pages, breaks in PCF" issues.

## Read These Files

1. `package.json` of the target surface — check the actual React version pinned
2. Drill-down only if bumping React majors: `knowledge/fluent-ui-v9/docs/react-version-support.md`

## Constraints

- **ADR-022**: PCF runtime = React 16 (platform-provided). Code Pages = React 18 (bundled). NEVER mix.
- **ADR-021**: Fluent v9 only — but the v9 package must match the React version it supports.

## Compatibility Matrix

| Surface | React | Min Fluent v9 | Notes |
|---|---|---|---|
| PCF Canvas (virtual + platform libraries) | **16.14.0** | 9.46.2 | Pinned by platform; can't bump. `pac pcf init -fw react` produces this combo. |
| PCF non-virtual (legacy) | 16.x bundled | 9.x | Stick with 16.14 baseline for compatibility. |
| Code Pages | 18.x | 9.66.0+ | React 18 support added in Fluent v9.66.0. |
| External SPA | 18.x or 19.x | 9.66.0 / 9.72.2 | React 19 support added in Fluent v9.72.2. |
| Office Add-ins | 18.x | 9.66.0+ | Same as Code Pages. |
| MCP App widgets | 18.x | 9.66.0+ | Trey-research reference. |

## Key Rules

- ✅ `Spaarke.UI.Components` is consumed by BOTH PCF (React 16.14) and Code Pages (React 18). Therefore it MUST be 16.14-safe:
  - NO `createRoot` from `react-dom/client` (React 18 only)
  - NO `useId`, `useTransition`, `useDeferredValue` (React 18 only)
  - NO `<StrictMode>` toggling at component level
- ✅ Code Pages and External SPA may use React 18-only features freely.
- ❌ NEVER bump React in a PCF surface without checking that `@fluentui/react-components` of the same version still ships React-16-compatible types.
- ❌ NEVER add `@types/react@19` if any consuming surface is still on React 17/18 — `Slot` children typing changes break across versions.
- If you ship a Spaarke library targeting all surfaces, follow Fluent's own rule: use `JSXElement` / `JSXIntrinsicElementKeys` / `JSXIntrinsicElement<K>` from `@fluentui/react-components` instead of `JSX.Element` (removed from global in React 19).

## See Also

- [`fluent-v9-component-authoring.md`](./fluent-v9-component-authoring.md) — module conventions
- `src/client/pcf/CLAUDE.md` — PCF-module-specific guidance
