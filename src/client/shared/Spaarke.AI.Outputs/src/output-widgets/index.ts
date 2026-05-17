/**
 * @spaarke/ai-outputs — Output Widgets barrel export
 *
 * Output pane widget components added wave by wave.
 * Default exports are re-exported as named exports for consumers that need
 * direct component references without going through the lazy registry.
 */

// Wave 2 (task 020): widgets 1-4
export { default as BudgetDashboardWidget } from './BudgetDashboardWidget';
export type { BudgetDashboardData, BudgetDashboardWidgetProps, BudgetLineItem } from './BudgetDashboardWidget';

export { default as SearchResultsWidget } from './SearchResultsWidget';
export type { SearchResultsData, SearchResultsWidgetProps, SearchResultItem } from './SearchResultsWidget';

export { default as AnalysisEditorWidget } from './AnalysisEditorWidget';
export type { AnalysisEditorData, AnalysisEditorWidgetProps, AnalysisSection } from './AnalysisEditorWidget';

export { default as ContractComparisonWidget } from './ContractComparisonWidget';
export type {
  ContractComparisonData,
  ContractComparisonWidgetProps,
  ContractClausePair,
} from './ContractComparisonWidget';

// Wave 2 (task 021): widgets 5-8
export { default as TimelineWidget } from './TimelineWidget';
export type { TimelineData, TimelineWidgetProps, TimelineEvent } from './TimelineWidget';

export { default as DocumentCompareWidget } from './DocumentCompareWidget';
export type {
  DocumentCompareData,
  DocumentCompareWidgetProps,
  DocumentCompareLine,
  DocumentChangeType,
} from './DocumentCompareWidget';

export { default as DataTableWidget } from './DataTableWidget';
export type { DataTableData, DataTableWidgetProps, DataTableColumn, DataTableRowValue } from './DataTableWidget';

export { default as ChartWidget } from './ChartWidget';
export type { ChartData, ChartWidgetProps, ChartSeries, ChartPoint } from './ChartWidget';

// Wave 3 (task 031): widgets 9-11
export { default as StatusSummaryWidget } from './StatusSummaryWidget';
export type { StatusSummaryData, StatusSummaryWidgetProps, StatusCategory, StatusLevel } from './StatusSummaryWidget';

export { default as RecommendationWidget } from './RecommendationWidget';
export type {
  RecommendationData,
  RecommendationWidgetProps,
  Recommendation,
  RecommendationPriority,
} from './RecommendationWidget';

export { default as ActionPlanWidget } from './ActionPlanWidget';
export type { ActionPlanData, ActionPlanWidgetProps, ActionPlanStep } from './ActionPlanWidget';
