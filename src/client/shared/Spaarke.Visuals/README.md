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

## Consumers

- `src/client/pcf/VisualHost/` — repointed in VHVU-060.
- Future code-page dashboards / report builder.
