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
  ReportCardMetric = 100000010,
  Gauge = 100000011,
  HorizontalStackedBar = 100000012,
}

/**
 * Value format enumeration matching Dataverse choice values
 * Controls how aggregated values are displayed on MetricCards
 */
export enum ValueFormat {
  ShortNumber = 100000000,
  LetterGrade = 100000001,
  Percentage = 100000002,
  WholeNumber = 100000003,
  Decimal = 100000004,
  Currency = 100000005,
}

/**
 * Color source enumeration matching Dataverse choice values
 * Controls how per-card colors are determined in MetricCard matrix
 */
export enum ColorSource {
  None = 100000000,
  OptionSetColor = 100000001,
  ValueThreshold = 100000002,
  SignBased = 100000003,
}

/**
 * Card shape enumeration matching Dataverse option set values (sprk_metriccardshape)
 * Controls the aspect ratio of cards in MetricCardMatrix
 */
export enum CardShape {
  Square = 100000000,
  VerticalRectangle = 100000001,
  HorizontalRectangle = 100000002,
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
  /** Advanced configuration (field pivot, card config, color thresholds).
   *  Mapped from the same Dataverse field (sprk_optionsjson) by ConfigurationLoader. */
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

  // Drill-through configuration
  sprk_drillthroughtarget?: string;

  // MetricCard enhancement fields (v1.2.33)
  sprk_valueformat?: ValueFormat;
  sprk_colorsource?: ColorSource;

  // Card shape (v1.2.44)
  sprk_metriccardshape?: CardShape;
}

/**
 * Drill interaction contract
 */
export type DrillOperator = 'eq' | 'in' | 'between';

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
  /** Option set sort order (for optionSetOrder sort) */
  sortOrder?: number;
  /** Per-data-point value format override (e.g., from field pivot config) */
  valueFormat?: ValueFormatType;
  /**
   * True when the underlying source field was null/undefined (not a real 0).
   * Used by visual components to distinguish "no data" from "real zero value"
   * and render a "No data available for this measure" placeholder instead.
   * Populated by FieldPivotService from the raw record value.
   */
  isNull?: boolean;
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

/**
 * Token set names for value-based color thresholds.
 * Each maps to a predefined set of Fluent UI v9 semantic tokens
 * that auto-adapt to light/dark mode.
 */
export type ColorTokenSet = 'brand' | 'warning' | 'danger' | 'success' | 'neutral';

/**
 * Color threshold rule — maps a value range to a Fluent token set
 */
export interface IColorThreshold {
  /** [min, max] inclusive range (values are 0-1 for grades, raw for others) */
  range: [number, number];
  /** Named token set to apply when value falls in range */
  tokenSet: ColorTokenSet;
}

/**
 * Card size controls responsive grid min-width and typography scale
 */
export type CardSize = 'small' | 'medium' | 'large';

/**
 * Sort order for cards in matrix layout
 */
export type CardSortBy = 'label' | 'value' | 'valueAsc' | 'optionSetOrder';

/**
 * Value format type as string (for config resolution)
 */
export type ValueFormatType =
  | 'shortNumber'
  | 'letterGrade'
  | 'percentage'
  | 'wholeNumber'
  | 'decimal'
  | 'currency'
  | 'signedPercentage';

/**
 * Color source type as string (for config resolution)
 */
export type ColorSourceType = 'none' | 'optionSetColor' | 'valueThreshold' | 'signBased';

/**
 * Badge tone — generic semantic tone for the optional MetricCard badge slot
 * (FR-VH-02). Maps to Fluent v9 `<Badge color={...}>` values via a local helper
 * in MetricCard.tsx:
 *  - "danger"  → Fluent v9 "danger"
 *  - "warning" → Fluent v9 "warning"
 *  - "success" → Fluent v9 "success"
 *  - "neutral" → Fluent v9 "subtle" (closest Fluent v9 neutral semantic)
 */
export type BadgeTone = 'danger' | 'warning' | 'success' | 'neutral';

/**
 * Description color tone — generic semantic foreground tone for the optional
 * MetricCard description sub-line (FR-VH-03). Maps to Fluent v9 semantic
 * foreground tokens via a local helper in MetricCard.tsx:
 *  - "brand"   → `tokens.colorBrandForeground1`
 *  - "neutral" → `tokens.colorNeutralForeground3` (existing/default — NFR-05 baseline)
 *  - "success" → `tokens.colorPaletteGreenForeground1`
 *  - "warning" → `tokens.colorPaletteDarkOrangeForeground1` (better WCAG AA contrast
 *    on light backgrounds than the yellow palette foreground)
 *  - "danger"  → `tokens.colorPaletteRedForeground1`
 */
export type DescriptionColorValue = 'brand' | 'neutral' | 'success' | 'warning' | 'danger';

/**
 * Badge configuration — optional decoration rendered inline next to a
 * MetricCard value (FR-VH-02). Position is reserved for future extension
 * (currently only "inline" is supported).
 */
export interface IBadgeConfig {
  /** Badge text content (e.g., "overdue", "new", "stale") */
  text: string;
  /** Semantic tone — mapped to Fluent v9 Badge color */
  tone: BadgeTone;
  /** Reserved for future placement options. Only "inline" is supported today. */
  position: 'inline';
}

/**
 * Chart legend configuration (v1.4.4 — currently consumed by DonutChart's
 * matrixRight path; schema is generic so future visual types may adopt it).
 *
 * All fields are optional; missing fields fall back to documented defaults.
 * See docs/guides/VISUALHOST-SETUP-GUIDE.md §"Chart Legend Configuration"
 * for authoring guidance.
 */
export interface IChartLegendConfig {
  /**
   * Where the legend sits relative to the visual.
   *  - "right" (default for back-compat with `donutLayout: "matrixRight"`)
   *  - "left"
   *  - "top"
   *  - "bottom"
   *  - "hidden" (renders the visual full-bleed, no legend)
   */
  placement?: 'right' | 'left' | 'top' | 'bottom' | 'hidden';

  /**
   * Layout of legend items.
   *  - "rows" (default for right/left placement): one item per row, vertical stack
   *  - "inline" (default for top/bottom placement): horizontal flow with wrap
   */
  orientation?: 'rows' | 'inline';

  /**
   * What each legend item shows.
   *  - "swatchLabelValue" (default): colored square + category label + measured value
   *  - "swatchLabel": colored square + category label only
   *  - "labelValue": category label + measured value (no swatch)
   *  - "labelOnly": category label only
   */
  itemFormat?: 'swatchLabelValue' | 'swatchLabel' | 'labelValue' | 'labelOnly';

  /**
   * Horizontal alignment of the value column relative to labels.
   * Applies only to `orientation: "rows"` with `itemFormat` that includes a value.
   *  - "near" (default, v1.4.3 behavior): values sit just to the right of the
   *    widest label (CSS grid `auto auto auto`)
   *  - "far": values pushed to the far edge of the legend cell (grid `auto auto 1fr`
   *    with value column right-aligned)
   */
  valueAlignment?: 'near' | 'far';

  /**
   * Override for the swatch size in pixels. Default: 10.
   * Ignored for itemFormats that don't render a swatch.
   */
  swatchSize?: number;
}

/**
 * Resolved card configuration — merged from Chart Definition fields,
 * Configuration JSON, and PCF property overrides.
 */
export interface ICardConfig {
  /** How to format the aggregated value */
  valueFormat: ValueFormatType;
  /** How per-card colors are determined */
  colorSource: ColorSourceType;
  /** Template for card description. Placeholders: {value}, {formatted}, {label}, {count} */
  cardDescription?: string;
  /** Display text when value is null/undefined */
  nullDisplay: string;
  /** Description when value is null */
  nullDescription?: string;
  /** Card size controlling min-width and typography */
  cardSize: CardSize;
  /** Sort order for cards */
  sortBy: CardSortBy;
  /** Fixed columns (null = auto-fill responsive) */
  columns?: number;
  /** Compact mode */
  compact: boolean;
  /** Show chart title above grid */
  showTitle: boolean;
  /** Max visible cards (null = all) */
  maxCards?: number;
  /** Use option set hex color as border accent and icon tint */
  accentFromOptionSet: boolean;
  /** Show the color-coded left accent bar on cards (default: false) */
  showAccentBar: boolean;
  /** Font size for the matrix title (e.g., "14px", "16px") */
  titleFontSize?: string;
  /** Map group labels to Fluent UI icon names */
  iconMap?: Record<string, string>;
  /** Value-based color threshold rules */
  colorThresholds?: IColorThreshold[];
  /** Card aspect ratio (e.g., "5 / 3", "1 / 1", "3 / 5") from sprk_metriccardshape */
  aspectRatio?: string;
  /** Content alignment within each card: left, left-center, center, right-center, right */
  dataJustification?: 'left' | 'left-center' | 'center' | 'right-center' | 'right';
  /** Invert sign-based coloring (negative=success, positive=danger) */
  invertSign?: boolean;
  /**
   * HorizontalStackedBar layout mode (FR-VH-04).
   *  - "default" (or undefined): current layout — total label (top-right), spent (bottom-left), remaining (bottom-right).
   *  - "headlineAboveBar": large headline + small sub-line ABOVE the bar; top-right total and bottom-right remaining labels are suppressed.
   * Backward-compat: omit or set to "default" to preserve byte-identical rendering for existing HSBar chart defs (NFR-05).
   */
  layoutMode?: 'default' | 'headlineAboveBar';
  /**
   * HSBar `headlineAboveBar` only — Dataverse field logical name whose value becomes the headline (e.g., `sprk_totalspendtodate`).
   * Resolved against `dataPoints[].fieldValue` (or `label`); falls back to `dataPoints[0]` (current/spent) when no match.
   * The value is formatted via the existing `valueFormat` (e.g., currency → `$50K`).
   */
  headlineFromField?: string;
  /**
   * HSBar `headlineAboveBar` only — template for the sub-line under the headline.
   * Supports placeholders `{remaining}`, `{percent}`, `{total}` (each pre-formatted via `valueFormat`,
   * except `{percent}` which is always rendered as integer percent).
   * Example: "{percent}% of {total}" → "33% of $150K".
   */
  subLineTemplate?: string;

  // ===== DonutChart-specific options (FR-VH-01 / task 020) =====
  // These are read by DonutChart.tsx only; other renderers ignore them.
  // Backward-compat: when all five are absent (and no fieldPivot is configured),
  // DonutChart renders byte-identically to the pre-FR-VH-01 behavior.

  /**
   * Donut layout variant.
   * - "standard" (default): Donut centered with optional legend — current behavior.
   * - "matrixRight": CSS grid with donut on the LEFT and breakdown rows on the RIGHT.
   *   Used by Matter Health Composite (FR-DV-01) and similar scorecard-style donuts.
   */
  donutLayout?: 'standard' | 'matrixRight';

  /**
   * What value to render in the donut center.
   * - "total" (default): Sum of all data point values — current behavior.
   * - "meanOfFields": Average of `fieldPivot.fields[].value`. Used with
   *   `valueFormat: "letterGrade"` to render a composite grade (e.g., "B+").
   */
  donutCenterMode?: 'total' | 'meanOfFields';

  /**
   * Optional center label override.
   * When set, replaces the auto-formatted value text in the donut center.
   * (Equivalent to the existing `centerLabel` config key, kept for parity.)
   */
  donutCenterLabel?: string;

  /**
   * When true and `donutLayout === "matrixRight"`, render a breakdown row per
   * field-pivot field on the right side of the donut. Each row shows the
   * uppercase small-caps label and the formatted value (per
   * `breakdownValueFormat`). Default: false.
   */
  showBreakdownRows?: boolean;

  /**
   * Format used for breakdown row values (right side of `matrixRight` layout).
   * - "score": Round to integer (e.g., 85)
   * - "scoreOver100": "85/100" style
   * - "percentage": "85%" (value is 0-1 decimal, multiplied by 100)
   * - "percentScore": "85%" (value is already on a 0-100 scale, no multiplication)
   * - "ratio": "0.85" (two decimals)
   * Default: "score".
   */
  breakdownValueFormat?: 'score' | 'scoreOver100' | 'percentage' | 'percentScore' | 'ratio';

  // ===== Chart legend configuration (v1.4.4) =====
  // Generic legend schema consumed by DonutChart's matrixRight path and
  // extensible to other visual types. Backward compat (NFR-05): when `legend`
  // is absent, DonutChart preserves its pre-v1.4.4 behavior — the existing
  // `donutLayout: "matrixRight"` + `showBreakdownRows: true` keys derive an
  // equivalent default `{ placement: "right", orientation: "rows",
  // itemFormat: "swatchLabelValue", valueAlignment: "near", swatchSize: 10 }`
  // internally, so every existing chart def renders identically.

  /**
   * Chart legend configuration. Today consumed by DonutChart's matrixRight
   * layout; the schema is intentionally generic so future visual types
   * (BarChart, HSBar, etc.) can adopt it.
   *
   * @see docs/guides/VISUALHOST-SETUP-GUIDE.md §"Chart Legend Configuration"
   */
  legend?: IChartLegendConfig;

  /**
   * Suppress the chart card's toolbar title text (v1.4.6).
   *
   * Default behavior (`undefined` or `true`): the legacy toolbar above the
   * chart renders the chart's `sprk_name` on the left side, alongside the
   * sparkle / expand icons on the right (v1.4.3 layout).
   *
   * When set to `false`: the toolbar renders ONLY the icons (right-aligned),
   * with no title text. Useful when the host form section already provides
   * the same name as a section heading and you want to avoid two visually
   * identical headers stacked. Has no effect when CardChrome is active
   * (`showTitle` PCF property = true) — CardChrome owns its own header.
   *
   * @example
   * ```json
   * { "showCardTitle": false }
   * ```
   */
  showCardTitle?: boolean;

  /**
   * Dataverse column on the chart's parent record (typically `sprk_matter`)
   * that holds the pre-computed AI summary text for this chart. When set,
   * VisualHostRoot renders an `AiSummaryPopover` (sparkle icon) in the
   * toolbar; clicking it shows the summary.
   *
   * Takes precedence over the PCF `aiSummaryField` property. This lets a
   * single chart definition map to its own summary column without each form
   * placement having to wire the property.
   *
   * Examples:
   *   - Matter Health Composite → "sprk_performancesummary"
   *   - Financial               → "sprk_financialsummary"
   *   - Task                    → "sprk_tasksummary"
   *
   * @see docs/guides/VISUALHOST-SETUP-GUIDE.md §"AI Summary Field Configuration"
   */
  aiSummaryField?: string;

  // ===== MetricCard badge slot (FR-VH-02 / task 021) =====
  // Generic addition consumed by MetricCard.tsx. Backward compat (NFR-05):
  // when `badge` is absent, MetricCard renders byte-identically to today.
  // In field-pivot mode, the per-field `IFieldPivotEntry.badge` takes
  // precedence over this top-level value for that field's card.

  /**
   * Optional badge rendered inline next to the MetricCard value
   * (e.g., `4 [overdue]` with a "danger"-toned Fluent v9 Badge).
   * Renderer: MetricCard.tsx single-card path.
   */
  badge?: IBadgeConfig;

  // ===== MetricCard description color (FR-VH-03 / task 022) =====
  // Generic addition consumed by MetricCard.tsx. Backward compat (NFR-05):
  // when `descriptionColor` is absent OR set to "neutral", the description
  // sub-line renders with `colorNeutralForeground3` — byte-identical to today.

  /**
   * Optional semantic foreground tone for the MetricCard description sub-line
   * (e.g., "events in last 7 days" in brand color on Matter Activity).
   * Renderer: MetricCard.tsx — the Description `<Text>` element only.
   * Default: "neutral" (resolves to existing `colorNeutralForeground3`).
   */
  descriptionColor?: DescriptionColorValue;
}

/**
 * Field Pivot Configuration
 *
 * Reads multiple fields from a single Dataverse record and presents each
 * as a separate data point in MetricCardMatrix. Generic — works for any entity
 * with multiple numeric fields that should display as a card row.
 *
 * Configured via sprk_optionsjson on the chart definition (mapped to sprk_configurationjson internally):
 * {
 *   "fieldPivot": {
 *     "fields": [
 *       { "field": "sprk_some_decimal_field", "label": "Label", "fieldValue": 1 }
 *     ]
 *   }
 * }
 */
export interface IFieldPivotConfig {
  fields: IFieldPivotEntry[];
}

export interface IFieldPivotEntry {
  /** Dataverse field logical name (e.g., "sprk_budgetcompliancegrade_current") */
  field: string;
  /** Display label for the card (e.g., "Budget") */
  label: string;
  /** Value passed to icon/color resolution (e.g., option set value for iconMap keys) */
  fieldValue?: unknown;
  /** Explicit sort order (default: array index) */
  sortOrder?: number;
  /** Per-field value format override (e.g., "currency", "letterGrade", "signedPercentage") */
  valueFormat?: ValueFormatType;
  /** For ratio-mode gauges: the total/denominator field logical name */
  totalField?: string;
  /**
   * Per-field badge slot (FR-VH-02). When present, MetricCard renders a Fluent v9
   * Badge inline next to the field's value. Takes precedence over the top-level
   * `ICardConfig.badge` for that specific field's card.
   */
  badge?: IBadgeConfig;
}
