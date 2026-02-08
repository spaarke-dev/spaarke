/**
 * Visual Host PCF Types
 * Local type definitions to avoid bundling shared library components
 * Note: These mirror the types in @spaarke/ui-components
 */

/**
 * Visual type enumeration matching Dataverse option set values
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
  DueDateCard = 100000008,
  DueDateCardList = 100000009,
}

/**
 * Click action type enumeration matching Dataverse option set values
 */
export enum OnClickAction {
  None = 100000000,
  OpenRecordForm = 100000001,
  OpenSidePane = 100000002,
  NavigateToPage = 100000003,
  OpenDatasetGrid = 100000004,
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
  sprk_chartdefinitionid: string;
  sprk_name: string;
  sprk_description?: string;
  sprk_visualtype: VisualType;
  sprk_sourceentity?: string;
  sprk_entitylogicalname?: string;
  sprk_baseviewid?: string;
  sprk_aggregationfield?: string;
  sprk_aggregationtype?: AggregationType;
  sprk_groupbyfield?: string;
  sprk_optionsjson?: string;
  sprk_configurationjson?: string;

  // FetchXML fields (existing in Dataverse)
  sprk_fetchxmlquery?: string;
  sprk_fetchxmlparams?: string;

  // Click action fields
  sprk_onclickaction?: OnClickAction;
  sprk_onclicktarget?: string;
  sprk_onclickrecordfield?: string;

  // Card list configuration fields
  sprk_contextfieldname?: string;
  sprk_viewlisttabname?: string;
  sprk_maxdisplayitems?: number;
}

/**
 * Drill interaction contract
 */
export type DrillOperator = "eq" | "in" | "between";

export interface DrillInteraction {
  field: string;
  operator: DrillOperator;
  value: unknown;
  label?: string;
}

/** Alias for backwards compatibility */
export type IDrillInteraction = DrillInteraction;

/**
 * Aggregated data point for chart rendering
 */
export interface IAggregatedDataPoint {
  label: string;
  value: number;
  color?: string;
  fieldValue: unknown;
}

/**
 * Chart data structure passed to chart components
 */
export interface IChartData {
  dataPoints: IAggregatedDataPoint[];
  totalRecords: number;
  aggregationType: AggregationType;
  aggregationField?: string;
  groupByField?: string;
}

/**
 * Visual Host specific configuration
 */
export interface IVisualHostConfig {
  showToolbar: boolean;
  height?: number;
  enableDrillThrough: boolean;
}

/**
 * Default Visual Host configuration
 */
export const DEFAULT_VISUAL_HOST_CONFIG: IVisualHostConfig = {
  showToolbar: true,
  enableDrillThrough: true,
};
