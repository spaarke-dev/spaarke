# @spaarke/visuals

Presentational chart/visual React components — KPI/metric cards, charts, gauges, distribution bars, mini-tables, calendar visuals.

## Design contract

- **Presentational only.** No `Xrm`, `WebAPI`, `ComponentFramework`, or FetchXML. The host fetches and shapes data and passes it in via props. This is the boundary that let VisualHost stop bundling shared-lib `src` (see the VisualHost decoupling project).
- **Fluent UI v9 only** (ADR-021); charts via `@fluentui/react-charting`.
- **React is a peer** (ADR-022) — nothing here bundles React.
- **Zero internal Spaarke dependencies.** Card chrome (`CardChrome`), the tool registry, drill-through (`ClickActionHandler`), and data services stay host-side in the PCF.

## Governance

This is a **governed canonical sibling** package sanctioned by the ADR-012 amendment (VHVU-070). It exists to be reused across the VisualHost PCF and future code-page dashboard / report-builder surfaces without the raw-`src`-consumption anti-pattern.

## Layout (source-only, mirrors `@spaarke/events-components`)

```
src/
├── index.ts          # barrel
├── components/       # visual components (populated in VHVU-041)
├── types/            # view-model types incl. the canonical VisualType
└── utils/            # chart/token color + formatting helpers
```

`main`/`types` point at `src/index.ts`; consumers import the TypeScript source directly (`"build": "tsc --noEmit"`), same as the other shared sibling packages.

### Why `@types/react@18` (not 19)

Dev-typed against **React 18** on purpose. React 18's `ReactNode` is a subset of React 19's (`19` *added* `Promise<ReactNode>` to the union), so an `@18`-typed component is assignable into **both** an `@18` host (the VisualHost PCF platform-library React) **and** an `@19` host (future code pages) with no JSX types skew. Typing against `@19` instead would reproduce the `TS2786: cannot be used as a JSX component` error in the R18 PCF — the exact skew this whole decoupling project exists to eliminate (see the retained `AiSummaryPopover` casts, which stem from `@spaarke/ui-components` being `@19`-typed). `@18` is the forward-compatible lower bound; the `peerDependencies` still accept React 16–19 at runtime.

## Consumers

- `src/client/pcf/VisualHost/` — repointed in VHVU-060.
- Future code-page dashboards / report builder.
