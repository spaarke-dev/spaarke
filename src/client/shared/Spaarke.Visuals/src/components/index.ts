/**
 * @spaarke/visuals — components barrel.
 *
 * Presentational visual components (moved from the VisualHost PCF in VHVU-041).
 * Each `export *` re-exports the component plus its public companion types
 * (props interfaces, `MatrixJustification`, `IMiniTableItem`/`IMiniTableColumn`,
 * `BarOrientation`, `ChartVariant`, `IStatusSegment`, `IEventDueDateCardProps`,
 * etc.).
 *
 * Note on `TrendDirection`: `VisualMetricCard` exports a local `TrendDirection`
 * (`'up' | 'down' | 'neutral'`) while `TrendCard` re-exports the canonical
 * `TrendDirection` (`'up' | 'down' | 'flat'`) from the `types` barrel. The two
 * collide under `export *`, so neither is surfaced through this components
 * barrel (not an error) — the canonical `TrendDirection` is available from
 * `@spaarke/visuals/types`.
 *
 * `VisualMetricCard` (renamed from `MetricCard` 2026-10-03, item C-3) is unrelated to
 * `MetricCard` in `@spaarke/ui-components` (`WorkspaceShell/MetricCard.tsx`) — the clickable
 * count-filter card. The rename removes the name collision; this package's component serves
 * the VisualHost PCF only.
 */

export * from './BarChart';
export * from './CalendarVisual';
export * from './DonutChart';
export * from './DueDateCard';
export * from './DueDateCardList';
export * from './ErrorBoundary';
export * from './EventDueDateCard';
export * from './GaugeVisual';
export * from './HorizontalStackedBar';
export * from './LineChart';
export * from './MetricCardMatrix';
export * from './MiniTable';
export * from './StatusDistributionBar';

// VisualMetricCard and TrendCard are re-exported explicitly (NOT `export *`) to avoid
// the `TrendDirection` name clash: VisualMetricCard has a local `TrendDirection`
// (`'up' | 'down' | 'neutral'`) and TrendCard re-exports the canonical
// `TrendDirection` (`'up' | 'down' | 'flat'`) from `types`. The canonical one
// is surfaced at the package root via the `types` barrel; VisualMetricCard's local
// variant stays internal to `./MetricCard`.
export { VisualMetricCard } from './MetricCard';
export type { IVisualMetricCardProps } from './MetricCard';
export { TrendCard } from './TrendCard';
export type { ITrendCardProps } from './TrendCard';
