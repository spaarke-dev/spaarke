/**
 * Card Configuration Resolver
 * Merges configuration from three tiers into a single ICardConfig:
 *   1. Chart Definition fields (Dataverse columns — sprk_valueformat, sprk_colorsource)
 *   2. Configuration JSON (sprk_configurationjson — flexible, complex settings)
 *   3. PCF Property overrides (per-form-placement overrides)
 *
 * Priority: PCF Override > Chart Definition Field > Config JSON > Defaults
 *
 * Also provides the ReportCardMetric preset for backward compatibility:
 * when sprk_visualtype === ReportCardMetric, grade-specific defaults are applied.
 */

import {
  type IChartDefinition,
  type ICardConfig,
  type ValueFormatType,
  type ColorSourceType,
  type IColorThreshold,
  ValueFormat,
  ColorSource,
  VisualType,
} from "../types";

/**
 * Default card configuration — sensible defaults for all MetricCards
 */
const DEFAULT_CONFIG: ICardConfig = {
  valueFormat: "shortNumber",
  colorSource: "none",
  nullDisplay: "—",
  cardSize: "medium",
  sortBy: "label",
  compact: false,
  showTitle: true,
  accentFromOptionSet: false,
  showAccentBar: false,
};

/**
 * Default color thresholds for grade-based coloring (ReportCardMetric preset)
 */
const GRADE_COLOR_THRESHOLDS: IColorThreshold[] = [
  { range: [0.85, 1.00], tokenSet: "brand" },
  { range: [0.70, 0.84], tokenSet: "warning" },
  { range: [0.00, 0.69], tokenSet: "danger" },
];

/**
 * Default icon map for ReportCardMetric preset (from GradeMetricCard legacy)
 */
const GRADE_ICON_MAP: Record<string, string> = {
  Guidelines: "Gavel",
  Budget: "Money",
  Outcomes: "Target",
};

/**
 * Map ValueFormat enum to ValueFormatType string
 */
function valueFormatEnumToString(enumVal: ValueFormat): ValueFormatType {
  switch (enumVal) {
    case ValueFormat.LetterGrade: return "letterGrade";
    case ValueFormat.Percentage: return "percentage";
    case ValueFormat.WholeNumber: return "wholeNumber";
    case ValueFormat.Decimal: return "decimal";
    case ValueFormat.Currency: return "currency";
    case ValueFormat.ShortNumber:
    default: return "shortNumber";
  }
}

/**
 * Map ColorSource enum to ColorSourceType string
 */
function colorSourceEnumToString(enumVal: ColorSource): ColorSourceType {
  switch (enumVal) {
    case ColorSource.OptionSetColor: return "optionSetColor";
    case ColorSource.ValueThreshold: return "valueThreshold";
    case ColorSource.None:
    default: return "none";
  }
}

/**
 * Parse configuration JSON from chart definition.
 * Returns empty object if parsing fails or input is empty.
 */
function parseConfigJson(jsonString?: string): Record<string, unknown> {
  if (!jsonString) return {};
  try {
    return JSON.parse(jsonString) as Record<string, unknown>;
  } catch {
    return {};
  }
}

/**
 * Resolve a complete ICardConfig from chart definition + PCF overrides.
 *
 * @param chartDef - Chart definition from Dataverse
 * @param pcfOverrides - Per-placement PCF property overrides
 * @returns Fully resolved card configuration
 */
export function resolveCardConfig(
  chartDef: IChartDefinition,
  pcfOverrides?: {
    valueFormatOverride?: string;
    columns?: number;
  }
): ICardConfig {
  const json = parseConfigJson(chartDef.sprk_configurationjson);
  const isReportCardMetric = chartDef.sprk_visualtype === VisualType.ReportCardMetric;

  // --- Value Format Resolution ---
  // Priority: PCF override > Chart Def field > Config JSON > preset default > default
  let valueFormat: ValueFormatType = DEFAULT_CONFIG.valueFormat;
  if (pcfOverrides?.valueFormatOverride) {
    valueFormat = pcfOverrides.valueFormatOverride as ValueFormatType;
  } else if (chartDef.sprk_valueformat != null) {
    valueFormat = valueFormatEnumToString(chartDef.sprk_valueformat);
  } else if (typeof json.valueFormat === "string") {
    valueFormat = json.valueFormat as ValueFormatType;
  } else if (isReportCardMetric) {
    valueFormat = "letterGrade";
  }

  // --- Color Source Resolution ---
  let colorSource: ColorSourceType = DEFAULT_CONFIG.colorSource;
  if (chartDef.sprk_colorsource != null) {
    colorSource = colorSourceEnumToString(chartDef.sprk_colorsource);
  } else if (typeof json.colorSource === "string") {
    colorSource = json.colorSource as ColorSourceType;
  } else if (isReportCardMetric) {
    colorSource = "valueThreshold";
  }

  // --- Config JSON fields with defaults ---
  const cardDescription = (json.cardDescription as string) ??
    (isReportCardMetric ? (json.contextTemplate as string) ?? "{formatted} — {value}% compliance" : undefined);

  const nullDisplay = (json.nullDisplay as string) ??
    (isReportCardMetric ? "N/A" : DEFAULT_CONFIG.nullDisplay);

  const nullDescription = (json.nullDescription as string) ??
    (isReportCardMetric ? "No data available for {label}" : undefined);

  const cardSize = (json.cardSize as ICardConfig["cardSize"]) ?? DEFAULT_CONFIG.cardSize;

  const sortBy = (json.sortBy as ICardConfig["sortBy"]) ??
    (isReportCardMetric ? "optionSetOrder" : DEFAULT_CONFIG.sortBy);

  const columns = pcfOverrides?.columns ?? (json.columns as number | undefined) ?? undefined;

  const compact = (json.compact as boolean) ?? DEFAULT_CONFIG.compact;

  const showTitle = (json.showTitle as boolean) ?? DEFAULT_CONFIG.showTitle;

  const maxCards = (json.maxCards as number | undefined) ?? undefined;

  const accentFromOptionSet = (json.accentFromOptionSet as boolean) ??
    (isReportCardMetric ? true : DEFAULT_CONFIG.accentFromOptionSet);

  const showAccentBar = (json.showAccentBar as boolean) ?? DEFAULT_CONFIG.showAccentBar;

  const titleFontSize = (json.titleFontSize as string | undefined) ?? undefined;

  // --- Icon Map ---
  const iconMap = (json.iconMap as Record<string, string> | undefined) ??
    (isReportCardMetric ? GRADE_ICON_MAP : undefined);

  // --- Color Thresholds ---
  const colorThresholds = (json.colorThresholds as IColorThreshold[] | undefined) ??
    (isReportCardMetric && colorSource === "valueThreshold" ? GRADE_COLOR_THRESHOLDS : undefined);

  return {
    valueFormat,
    colorSource,
    cardDescription,
    nullDisplay,
    nullDescription,
    cardSize,
    sortBy,
    columns,
    compact,
    showTitle,
    maxCards,
    accentFromOptionSet,
    showAccentBar,
    titleFontSize,
    iconMap,
    colorThresholds,
  };
}
