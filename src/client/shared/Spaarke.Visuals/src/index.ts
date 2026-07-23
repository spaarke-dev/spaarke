/**
 * @spaarke/visuals
 *
 * Presentational chart/visual React components — KPI/metric cards, charts,
 * gauges, distribution bars, mini-tables, calendar visuals.
 *
 * Architecture (ADR-012 amended boundary — governed canonical sibling):
 *  - **Presentational only.** No `Xrm` / `WebAPI` / `ComponentFramework` /
 *    FetchXML. The host (VisualHost PCF today, code-page dashboards later)
 *    fetches + shapes data and passes it in via props.
 *  - Fluent UI v9 only (ADR-021); `@fluentui/react-charting` for charts.
 *  - React declared as a peer (ADR-022) — no bundled React.
 *  - **Zero internal Spaarke dependencies.** Card chrome, the tool registry,
 *    drill-through, and data services stay host-side (in the PCF).
 *
 * Consumers:
 *  - `src/client/pcf/VisualHost/` (repoint in VHVU-060)
 *  - future code-page dashboard / report builder surfaces
 */

export * from './components';
export type * from './types';
export * from './utils';
