/**
 * Chart Definition Types - Spaarke Visuals Framework
 * Mirrors sprk_chartdefinition entity schema from Dataverse
 * Project: visualization-module
 */

/**
 * Visual type enumeration matching Dataverse option set values
 * @see projects/visualization-module/notes/entity-schema.md
 */
export enum VisualType {
  MetricCard = 100000000,
  BarChart = 100000001,
  LineChart = 100000002,
  AreaChart = 100000003,
  DonutChart = 100000004,
  StatusBar = 100000005,
  Calendar = 100000006,
  MiniTable = 100000007,
}

/**
 * Aggregation type enumeration matching Dataverse option set values
 */
export enum AggregationType {
  Count = 100000000,
  Sum = 100000001,
  Average = 100000002,
  Min = 100000003,
  Max = 100000004,
}

/**
 * Chart Definition interface matching sprk_chartdefinition entity
 */
export interface IChartDefinition {
  /** Primary key (GUID) */
  sprk_chartdefinitionid: string;

  /** Display name */
  sprk_name: string;

  /** Visual type to render */
  sprk_visualtype: VisualType;

  /** Target entity logical name */
  sprk_entitylogicalname: string;

  /** Base view GUID (SavedQuery or UserQuery) */
  sprk_baseviewid: string;

  /** Field to aggregate (optional for count-only) */
  sprk_aggregationfield?: string;

  /** Aggregation type (optional, default: Count) */
  sprk_aggregationtype?: AggregationType;

  /** Group by field (optional for single-value metrics) */
  sprk_groupbyfield?: string;

  /** Per-visual-type options JSON (optional) */
  sprk_optionsjson?: string;
}

/**
 * Parsed chart definition with typed options
 */
export interface IChartDefinitionParsed<TOptions = IChartOptionsBase>
  extends Omit<IChartDefinition, "sprk_optionsjson"> {
  /** Parsed options object */
  options: TOptions;
}

/**
 * Base interface for all chart options
 */
export interface IChartOptionsBase {
  /** Optional title override */
  title?: string;
  /** Optional subtitle */
  subtitle?: string;
}

/**
 * MetricCard visual options
 */
export interface IMetricCardOptions extends IChartOptionsBase {
  /** Show trend indicator */
  showTrend?: boolean;
  /** Trend comparison period */
  trendPeriod?: "day" | "week" | "month" | "quarter" | "year";
  /** Custom format string for value */
  valueFormat?: string;
  /** Icon name from Fluent icons */
  icon?: string;
}

/**
 * Bar/Column chart options
 */
export interface IBarChartOptions extends IChartOptionsBase {
  /** Chart orientation */
  orientation?: "vertical" | "horizontal";
  /** Show data labels on bars */
  showDataLabels?: boolean;
  /** Enable stacking for grouped data */
  stacked?: boolean;
  /** Maximum bars to display (top-N) */
  maxBars?: number;
}

/**
 * Line chart options
 */
export interface ILineChartOptions extends IChartOptionsBase {
  /** Show data points on line */
  showDataPoints?: boolean;
  /** Enable smooth curve interpolation */
  smoothCurve?: boolean;
  /** Fill area under line */
  fillArea?: boolean;
  /** Date grouping for time series */
  dateGrouping?: "day" | "week" | "month" | "quarter" | "year";
}

/**
 * Area chart options
 */
export interface IAreaChartOptions extends IChartOptionsBase {
  /** Enable stacking for multiple series */
  stacked?: boolean;
  /** Opacity of fill (0-1) */
  fillOpacity?: number;
  /** Date grouping for time series */
  dateGrouping?: "day" | "week" | "month" | "quarter" | "year";
}

/**
 * Donut/Pie chart options
 */
export interface IDonutChartOptions extends IChartOptionsBase {
  /** Show as pie instead of donut */
  showAsPie?: boolean;
  /** Inner radius percentage for donut (0-100) */
  innerRadius?: number;
  /** Show percentage labels */
  showPercentages?: boolean;
  /** Show legend */
  showLegend?: boolean;
  /** Maximum slices before grouping as "Other" */
  maxSlices?: number;
}

/**
 * Status Distribution Bar options
 */
export interface IStatusBarOptions extends IChartOptionsBase {
  /** Show count labels on segments */
  showCounts?: boolean;
  /** Show percentage labels */
  showPercentages?: boolean;
  /** Bar height in pixels */
  barHeight?: number;
}

/**
 * Calendar visual options
 */
export interface ICalendarOptions extends IChartOptionsBase {
  /** Date field to use for positioning */
  dateField?: string;
  /** End date field for ranges (optional) */
  endDateField?: string;
  /** Default view mode */
  defaultView?: "month" | "week" | "day";
  /** Field for item label */
  labelField?: string;
}

/**
 * Mini Table (Top-N) options
 */
export interface IMiniTableOptions extends IChartOptionsBase {
  /** Maximum rows to display */
  maxRows?: number;
  /** Columns to display (field logical names) */
  columns?: string[];
  /** Sort field */
  sortField?: string;
  /** Sort direction */
  sortDirection?: "asc" | "desc";
  /** Show row numbers */
  showRowNumbers?: boolean;
}

/**
 * Union type for all visual options
 */
export type ChartOptions =
  | IMetricCardOptions
  | IBarChartOptions
  | ILineChartOptions
  | IAreaChartOptions
  | IDonutChartOptions
  | IStatusBarOptions
  | ICalendarOptions
  | IMiniTableOptions;

/**
 * Map visual type to its options interface
 */
export interface VisualTypeOptionsMap {
  [VisualType.MetricCard]: IMetricCardOptions;
  [VisualType.BarChart]: IBarChartOptions;
  [VisualType.LineChart]: ILineChartOptions;
  [VisualType.AreaChart]: IAreaChartOptions;
  [VisualType.DonutChart]: IDonutChartOptions;
  [VisualType.StatusBar]: IStatusBarOptions;
  [VisualType.Calendar]: ICalendarOptions;
  [VisualType.MiniTable]: IMiniTableOptions;
}

/**
 * Helper to get typed options based on visual type
 */
export type OptionsForVisualType<T extends VisualType> =
  T extends keyof VisualTypeOptionsMap ? VisualTypeOptionsMap[T] : IChartOptionsBase;

/**
 * Aggregated data point for chart rendering
 */
export interface IAggregatedDataPoint {
  /** Category/group label */
  label: string;
  /** Aggregated value */
  value: number;
  /** Optional color override */
  color?: string;
  /** Original field value (for drill-through) */
  fieldValue: unknown;
}

/**
 * Chart data structure passed to chart components
 */
export interface IChartData {
  /** Data points for rendering */
  dataPoints: IAggregatedDataPoint[];
  /** Total record count (before aggregation) */
  totalRecords: number;
  /** Aggregation type used */
  aggregationType: AggregationType;
  /** Field that was aggregated */
  aggregationField?: string;
  /** Field used for grouping */
  groupByField?: string;
}

/**
 * Props common to all chart visual components
 */
export interface IChartComponentProps<TOptions extends IChartOptionsBase = IChartOptionsBase> {
  /** Chart data to render */
  data: IChartData;
  /** Parsed chart options */
  options: TOptions;
  /** Callback when chart element is clicked (for drill-through) */
  onDrillInteraction?: (interaction: import("./DrillInteractionTypes").DrillInteraction) => void;
  /** Width of the chart container */
  width?: number;
  /** Height of the chart container */
  height?: number;
  /** Loading state */
  isLoading?: boolean;
  /** Error message */
  error?: string;
}
