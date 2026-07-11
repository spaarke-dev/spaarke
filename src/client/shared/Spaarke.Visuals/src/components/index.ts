/**
 * @spaarke/visuals — components barrel.
 *
 * Presentational visual components (moved from the VisualHost PCF in VHVU-041).
 * Each `export *` re-exports the component plus its public companion types
 * (props interfaces, `MatrixJustification`, `IMiniTableItem`/`IMiniTableColumn`,
 * `BarOrientation`, `ChartVariant`, `IStatusSegment`, `IEventDueDateCardProps`,
 * etc.).
 *
 * Note on `TrendDirection`: `MetricCard` exports a local `TrendDirection`
 * (`'up' | 'down' | 'neutral'`) while `TrendCard` re-exports the canonical
 * `TrendDirection` (`'up' | 'down' | 'flat'`) from the `types` barrel. The two
 * collide under `export *`, so neither is surfaced through this components
 * barrel (not an error) — the canonical `TrendDirection` is available from
 * `@spaarke/visuals/types`.
 */

export * from './BarChart';
export * from './DonutChart';
export * from './ErrorBoundary';
export * from './EventDueDateCard';
export * from './GaugeVisual';
export * from './GradeMetricCard';
export * from './HorizontalStackedBar';
export * from './LineChart';
export * from './MetricCardMatrix';
export * from './MiniTable';
export * from './StatusDistributionBar';

// MetricCard and TrendCard are re-exported explicitly (NOT `export *`) to avoid
// the `TrendDirection` name clash: MetricCard has a local `TrendDirection`
// (`'up' | 'down' | 'neutral'`) and TrendCard re-exports the canonical
// `TrendDirection` (`'up' | 'down' | 'flat'`) from `types`. The canonical one
// is surfaced at the package root via the `types` barrel; MetricCard's local
// variant stays internal to `./MetricCard`.
export { MetricCard } from './MetricCard';
export type { IMetricCardProps } from './MetricCard';
export { TrendCard } from './TrendCard';
export type { ITrendCardProps } from './TrendCard';
