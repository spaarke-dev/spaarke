/**
 * FetchXML Types for Universal DataGrid
 *
 * Type definitions for FetchXML execution and layout parsing.
 * Used by FetchXmlService and ViewService.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 */

/**
 * Result of executing a FetchXML query
 */
export interface IFetchXmlResult<T> {
  /** Retrieved entities */
  entities: T[];
  /** Total record count (if count was requested) */
  totalRecordCount?: number;
  /** Paging cookie for subsequent pages */
  pagingCookie?: string;
  /** Whether more records exist */
  moreRecords: boolean;
  /** FetchXML that was executed (for debugging) */
  fetchXml?: string;
}

/**
 * Column definition extracted from layoutxml
 */
export interface IColumnDefinition {
  /** Attribute logical name */
  name: string;
  /** Display width in pixels */
  width: number;
  /** Display label (from metadata or layoutxml) */
  label?: string;
  /** Whether this is the primary column (entity primary name) */
  isPrimary?: boolean;
  /** Data type for rendering */
  dataType?: ColumnDataType;
  /** For lookup columns - target entity logical name */
  lookupEntityLogicalName?: string;
  /** Sort order if column is sorted */
  sortOrder?: "asc" | "desc";
  /** Column index from layoutxml */
  index?: number;
}

/**
 * Supported column data types
 */
export type ColumnDataType =
  | "string"
  | "integer"
  | "decimal"
  | "money"
  | "datetime"
  | "date"
  | "boolean"
  | "lookup"
  | "optionset"
  | "status"
  | "owner"
  | "memo"
  | "uniqueidentifier"
  | "image"
  | "file";

/**
 * Parsed view definition from savedquery or sprk_gridconfiguration
 */
export interface IViewDefinition {
  /** View unique identifier */
  id: string;
  /** Display name */
  name: string;
  /** Entity logical name this view applies to */
  entityLogicalName: string;
  /** FetchXML query */
  fetchXml: string;
  /** Layout XML defining columns */
  layoutXml: string;
  /** Whether this is the default view */
  isDefault?: boolean;
  /** View type: system, personal, or custom */
  viewType: ViewType;
  /** Sort order for view selector dropdown */
  sortOrder?: number;
  /** Optional icon name for view selector */
  iconName?: string;
  /** Parsed column definitions (populated by parseLayoutXml) */
  columns?: IColumnDefinition[];
}

/**
 * View type indicating source
 */
export type ViewType =
  | "savedquery"      // System view from savedquery entity
  | "userquery"       // Personal view from userquery entity
  | "custom";         // Custom view from sprk_gridconfiguration

/**
 * Options for FetchXML execution
 */
export interface IFetchXmlOptions {
  /** Page size for pagination (default: 50) */
  pageSize?: number;
  /** Page number (1-based, default: 1) */
  pageNumber?: number;
  /** Paging cookie from previous page */
  pagingCookie?: string;
  /** Include total record count (impacts performance) */
  returnTotalRecordCount?: boolean;
  /** Maximum records to return (overrides pageSize if smaller) */
  maxPageSize?: number;
}

/**
 * Filter condition for merging into FetchXML
 */
export interface IFilterCondition {
  /** Attribute to filter on */
  attribute: string;
  /** Operator (eq, ne, gt, lt, ge, le, like, in, etc.) */
  operator: FetchXmlOperator;
  /** Filter value(s) */
  value?: string | number | boolean | Date;
  /** Multiple values for 'in' operator */
  values?: (string | number)[];
}

/**
 * FetchXML condition operators
 */
export type FetchXmlOperator =
  | "eq"
  | "ne"
  | "gt"
  | "ge"
  | "lt"
  | "le"
  | "like"
  | "not-like"
  | "in"
  | "not-in"
  | "between"
  | "not-between"
  | "null"
  | "not-null"
  | "on"
  | "on-or-before"
  | "on-or-after"
  | "today"
  | "yesterday"
  | "tomorrow"
  | "last-x-days"
  | "next-x-days"
  | "this-week"
  | "this-month"
  | "this-year";

/**
 * Filter group for merging (AND/OR logic)
 */
export interface IFilterGroup {
  /** Filter type: and/or */
  type: "and" | "or";
  /** Conditions in this group */
  conditions: IFilterCondition[];
  /** Nested filter groups */
  filters?: IFilterGroup[];
}
