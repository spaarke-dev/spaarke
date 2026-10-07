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
 * (`'up' | 'down' | 'neutral'`) that would collide with the canonical
 * `TrendDirection` (`'up' | 'down' | 'flat'`) surfaced at the package root via
 * the `types` barrel, so it is not surfaced through this components barrel.
 * (`TrendCard`, which re-exported the canonical one, had zero consumers and was
 * deleted 2026-10-03 — reuse audit C-14.)
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

// MetricCard is re-exported explicitly (NOT `export *`) to avoid the
// `TrendDirection` name clash: MetricCard has a local `TrendDirection`
// (`'up' | 'down' | 'neutral'`); the canonical one (`'up' | 'down' | 'flat'`) is
// surfaced at the package root via the `types` barrel. MetricCard's local
// variant stays internal to `./MetricCard`.
export { MetricCard } from './MetricCard';
export type { IMetricCardProps } from './MetricCard';
