/**
 * Configuration Types for Grid Configuration Service
 *
 * Type definitions for sprk_gridconfiguration Dataverse entity.
 * Used by ConfigurationService and ViewService.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 */

/**
 * View type options from sprk_viewtype choice field
 */
export enum GridConfigViewType {
  /** Reference to existing savedquery view */
  SavedView = 1,
  /** Inline FetchXML and layout */
  CustomFetchXML = 2,
  /** Reference to another configuration (for reuse) */
  LinkedView = 3,
}

/**
 * Grid configuration record from sprk_gridconfiguration entity
 */
export interface IGridConfiguration {
  /** Primary key */
  id: string;
  /** Configuration display name */
  name: string;
  /** Target entity logical name */
  entityLogicalName: string;
  /** Configuration type */
  viewType: GridConfigViewType;
  /** GUID reference to savedquery (for SavedView type) */
  savedViewId?: string;
  /** Custom FetchXML query (for CustomFetchXML type) */
  fetchXml?: string;
  /** Column layout definition */
  layoutXml?: string;
  /** Additional JSON configuration */
  configJson?: IGridConfigJson;
  /** Whether this is the default view */
  isDefault: boolean;
  /** Display order in view selector */
  sortOrder: number;
  /** Fluent UI icon name */
  iconName?: string;
  /** Admin description/notes */
  description?: string;
  /** Record state (0 = Active, 1 = Inactive) */
  stateCode: number;
}

/**
 * Additional configuration stored in sprk_configjson
 */
export interface IGridConfigJson {
  /** Custom column overrides */
  columnOverrides?: IColumnOverride[];
  /** Default filter conditions */
  defaultFilters?: IDefaultFilter[];
  /** Row formatting rules */
  rowFormatting?: IRowFormattingRule[];
  /** Enable/disable features */
  features?: IGridFeatures;
  /** Custom CSS class names */
  cssClasses?: string[];
}

/**
 * Column override configuration
 */
export interface IColumnOverride {
  /** Attribute logical name */
  attributeName: string;
  /** Override display width */
  width?: number;
  /** Override display label */
  label?: string;
  /** Hide column */
  hidden?: boolean;
  /** Custom renderer name */
  renderer?: string;
  /** Renderer configuration */
  rendererConfig?: Record<string, unknown>;
}

/**
 * Default filter to apply
 */
export interface IDefaultFilter {
  /** Attribute to filter */
  attribute: string;
  /** Filter operator */
  operator: string;
  /** Filter value (can reference user context: @currentuser, @today) */
  value: string;
}

/**
 * Row formatting rule
 */
export interface IRowFormattingRule {
  /** Rule name for identification */
  name: string;
  /** Condition attribute */
  attribute: string;
  /** Condition operator */
  operator: "eq" | "ne" | "gt" | "lt" | "contains";
  /** Condition value */
  value: string | number | boolean;
  /** CSS class to apply */
  cssClass?: string;
  /** Background color */
  backgroundColor?: string;
  /** Text color */
  textColor?: string;
  /** Icon to show */
  icon?: string;
}

/**
 * Grid feature toggles
 */
export interface IGridFeatures {
  /** Enable row selection */
  enableSelection?: boolean;
  /** Enable multi-select */
  enableMultiSelect?: boolean;
  /** Enable sorting */
  enableSorting?: boolean;
  /** Enable column resizing */
  enableColumnResize?: boolean;
  /** Enable quick find search */
  enableQuickFind?: boolean;
  /** Enable export to Excel */
  enableExport?: boolean;
  /** Enable inline editing */
  enableInlineEdit?: boolean;
  /** Show row numbers */
  showRowNumbers?: boolean;
}

/**
 * Dataverse record structure for sprk_gridconfiguration
 */
export interface IGridConfigurationRecord {
  sprk_gridconfigurationid: string;
  sprk_name: string;
  sprk_entitylogicalname: string;
  sprk_viewtype: number;
  sprk_savedviewid?: string;
  sprk_fetchxml?: string;
  sprk_layoutxml?: string;
  sprk_configjson?: string;
  sprk_isdefault?: boolean;
  sprk_sortorder?: number;
  sprk_iconname?: string;
  sprk_description?: string;
  statecode: number;
}
