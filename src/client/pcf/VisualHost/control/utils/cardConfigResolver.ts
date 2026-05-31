/**
 * Card Configuration Resolver
 * Merges configuration from three tiers into a single ICardConfig:
 *   1. Chart Definition fields (Dataverse columns — sprk_valueformat, sprk_colorsource)
 *   2. Configuration JSON (sprk_optionsjson in Dataverse, mapped to sprk_configurationjson internally)
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
  type IBadgeConfig,
  type IChartLegendConfig,
  type BadgeTone,
  type DescriptionColorValue,
  type ValueFormatType,
  type ColorSourceType,
  type IColorThreshold,
  ValueFormat,
  ColorSource,
  CardShape,
  VisualType,
} from '../types';

/**
 * Default card configuration — sensible defaults for all MetricCards
 */
const DEFAULT_CONFIG: ICardConfig = {
  valueFormat: 'shortNumber',
  colorSource: 'none',
  nullDisplay: '—',
  cardSize: 'medium',
  sortBy: 'label',
  compact: false,
  showTitle: false,
  accentFromOptionSet: false,
  showAccentBar: false,
};

/**
 * Default color thresholds for grade-based coloring (ReportCardMetric preset)
 */
const GRADE_COLOR_THRESHOLDS: IColorThreshold[] = [
  { range: [0.85, 1.0], tokenSet: 'brand' },
  { range: [0.7, 0.84], tokenSet: 'warning' },
  { range: [0.0, 0.69], tokenSet: 'danger' },
];

/**
 * Default icon map for ReportCardMetric preset (from GradeMetricCard legacy)
 */
const GRADE_ICON_MAP: Record<string, string> = {
  Guidelines: 'Gavel',
  Budget: 'Money',
  Outcomes: 'Target',
};

/**
 * Map ValueFormat enum to ValueFormatType string
 */
function valueFormatEnumToString(enumVal: ValueFormat): ValueFormatType {
  switch (enumVal) {
    case ValueFormat.LetterGrade:
      return 'letterGrade';
    case ValueFormat.Percentage:
      return 'percentage';
    case ValueFormat.WholeNumber:
      return 'wholeNumber';
    case ValueFormat.Decimal:
      return 'decimal';
    case ValueFormat.Currency:
      return 'currency';
    case ValueFormat.ShortNumber:
    default:
      return 'shortNumber';
  }
}

/**
 * Map ColorSource enum to ColorSourceType string
 */
function colorSourceEnumToString(enumVal: ColorSource): ColorSourceType {
  switch (enumVal) {
    case ColorSource.OptionSetColor:
      return 'optionSetColor';
    case ColorSource.ValueThreshold:
      return 'valueThreshold';
    case ColorSource.SignBased:
      return 'signBased';
    case ColorSource.None:
    default:
      return 'none';
  }
}

/**
 * Map CardShape enum to CSS aspect-ratio string
 */
function cardShapeToAspectRatio(shape?: CardShape): string | undefined {
  switch (shape) {
    case CardShape.Square:
      return '1 / 1';
    case CardShape.VerticalRectangle:
      return '3 / 5';
    case CardShape.HorizontalRectangle:
      return '5 / 3';
    default:
      return undefined;
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
    showTitle?: boolean;
    titleFontSize?: string;
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
  } else if (typeof json.valueFormat === 'string') {
    valueFormat = json.valueFormat as ValueFormatType;
  } else if (isReportCardMetric) {
    valueFormat = 'letterGrade';
  }

  // --- Color Source Resolution ---
  let colorSource: ColorSourceType = DEFAULT_CONFIG.colorSource;
  if (chartDef.sprk_colorsource != null) {
    colorSource = colorSourceEnumToString(chartDef.sprk_colorsource);
  } else if (typeof json.colorSource === 'string') {
    colorSource = json.colorSource as ColorSourceType;
  } else if (isReportCardMetric) {
    colorSource = 'valueThreshold';
  }

  // --- Config JSON fields with defaults ---
  const cardDescription =
    (json.cardDescription as string) ??
    (isReportCardMetric ? ((json.contextTemplate as string) ?? '{formatted} — {value}% compliance') : undefined);

  const nullDisplay = (json.nullDisplay as string) ?? (isReportCardMetric ? 'N/A' : DEFAULT_CONFIG.nullDisplay);

  const nullDescription =
    (json.nullDescription as string) ?? (isReportCardMetric ? 'No data available for {label}' : undefined);

  const cardSize = (json.cardSize as ICardConfig['cardSize']) ?? DEFAULT_CONFIG.cardSize;

  const sortBy =
    (json.sortBy as ICardConfig['sortBy']) ?? (isReportCardMetric ? 'optionSetOrder' : DEFAULT_CONFIG.sortBy);

  const columns = pcfOverrides?.columns ?? (json.columns as number | undefined) ?? undefined;

  const compact = (json.compact as boolean) ?? DEFAULT_CONFIG.compact;

  const showTitle = pcfOverrides?.showTitle ?? (json.showTitle as boolean) ?? DEFAULT_CONFIG.showTitle;

  const maxCards = (json.maxCards as number | undefined) ?? undefined;

  const accentFromOptionSet =
    (json.accentFromOptionSet as boolean) ?? (isReportCardMetric ? true : DEFAULT_CONFIG.accentFromOptionSet);

  const showAccentBar = (json.showAccentBar as boolean) ?? DEFAULT_CONFIG.showAccentBar;

  const titleFontSize = pcfOverrides?.titleFontSize ?? (json.titleFontSize as string | undefined) ?? undefined;

  // --- Icon Map ---
  const iconMap =
    (json.iconMap as Record<string, string> | undefined) ?? (isReportCardMetric ? GRADE_ICON_MAP : undefined);

  // --- Color Thresholds ---
  const colorThresholds =
    (json.colorThresholds as IColorThreshold[] | undefined) ??
    (isReportCardMetric && colorSource === 'valueThreshold' ? GRADE_COLOR_THRESHOLDS : undefined);

  // --- Aspect Ratio (from Chart Definition shape field, then JSON fallback) ---
  const aspectRatio =
    cardShapeToAspectRatio(chartDef.sprk_metriccardshape) ?? (json.aspectRatio as string | undefined) ?? undefined;

  // --- Data Justification (from Config JSON) ---
  const dataJustification = (json.dataJustification as ICardConfig['dataJustification']) ?? undefined;

  // --- Invert Sign (for signBased coloring) ---
  const invertSign = (json.invertSign as boolean | undefined) ?? undefined;

  // --- HorizontalStackedBar headline layout (FR-VH-04) ---
  // Generic addition. Backward compat (NFR-05): when `layoutMode` is absent (or "default"),
  // HSBar renders identically to today.
  const layoutModeRaw = json.layoutMode as string | undefined;
  const layoutMode: ICardConfig['layoutMode'] | undefined =
    layoutModeRaw === 'headlineAboveBar' || layoutModeRaw === 'default' ? layoutModeRaw : undefined;
  const headlineFromField = (json.headlineFromField as string | undefined) ?? undefined;
  const subLineTemplate = (json.subLineTemplate as string | undefined) ?? undefined;

  // --- DonutChart options (FR-VH-01 / task 020) ---
  // Generic addition. Backward compat (NFR-05): when all five donut keys are absent,
  // DonutChart renders byte-identically to today.
  const donutLayoutRaw = json.donutLayout as string | undefined;
  const donutLayout: ICardConfig['donutLayout'] | undefined =
    donutLayoutRaw === 'matrixRight' || donutLayoutRaw === 'standard' ? donutLayoutRaw : undefined;

  const donutCenterModeRaw = json.donutCenterMode as string | undefined;
  const donutCenterMode: ICardConfig['donutCenterMode'] | undefined =
    donutCenterModeRaw === 'meanOfFields' || donutCenterModeRaw === 'total' ? donutCenterModeRaw : undefined;

  const donutCenterLabel = (json.donutCenterLabel as string | undefined) ?? undefined;
  const showBreakdownRows = (json.showBreakdownRows as boolean | undefined) ?? undefined;

  const breakdownValueFormatRaw = json.breakdownValueFormat as string | undefined;
  const breakdownValueFormat: ICardConfig['breakdownValueFormat'] | undefined =
    breakdownValueFormatRaw === 'score' ||
    breakdownValueFormatRaw === 'scoreOver100' ||
    breakdownValueFormatRaw === 'percentage' ||
    breakdownValueFormatRaw === 'percentScore' ||
    breakdownValueFormatRaw === 'ratio'
      ? breakdownValueFormatRaw
      : undefined;

  // --- MetricCard badge slot (FR-VH-02 / task 021) ---
  // Generic addition. Backward compat (NFR-05): when `badge` is absent in
  // sprk_optionsjson, ICardConfig.badge is undefined and MetricCard renders
  // byte-identically to today.
  const badge = parseBadge(json.badge);

  // --- MetricCard description color (FR-VH-03 / task 022) ---
  // Generic addition. Backward compat (NFR-05): when `descriptionColor` is
  // absent (or invalid), the field is undefined and MetricCard falls back to
  // `colorNeutralForeground3` — byte-identical to today. An explicit "neutral"
  // resolves to the same token, also byte-identical.
  const descriptionColor = parseDescriptionColor(json.descriptionColor);

  // --- Chart legend configuration (v1.4.4) ---
  // Generic addition. Backward compat (NFR-05): when `legend` is absent,
  // DonutChart derives a `placement: "right"` default from the existing
  // `donutLayout: "matrixRight"` + `showBreakdownRows: true` keys, so every
  // existing chart def renders identically.
  const legend = parseLegend(json.legend);

  // --- AI Summary field (v1.4.4) ---
  // Chart-def-driven mapping for the toolbar's sparkle icon. When set,
  // VisualHostRoot wires the `AiSummaryPopover` to read this Dataverse column
  // from the parent record. Takes precedence over the PCF `aiSummaryField`
  // property (placement-level override stays available for ad-hoc forms).
  const aiSummaryField =
    typeof json.aiSummaryField === 'string' && json.aiSummaryField.length > 0
      ? json.aiSummaryField
      : undefined;

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
    aspectRatio,
    dataJustification,
    invertSign,
    layoutMode,
    headlineFromField,
    subLineTemplate,
    donutLayout,
    donutCenterMode,
    donutCenterLabel,
    showBreakdownRows,
    breakdownValueFormat,
    badge,
    descriptionColor,
    legend,
    aiSummaryField,
  };
}

/**
 * Parse + validate an opaque badge value from `sprk_optionsjson`.
 * Accepts `{ text, tone, position }` shape with the constrained `BadgeTone` +
 * "inline" position; returns `undefined` for any malformed input so the card
 * renders unchanged (NFR-05).
 */
function parseBadge(raw: unknown): IBadgeConfig | undefined {
  if (!raw || typeof raw !== 'object') return undefined;
  const obj = raw as Record<string, unknown>;
  const text = obj.text;
  const tone = obj.tone;
  const position = obj.position;
  if (typeof text !== 'string' || text.length === 0) return undefined;
  if (tone !== 'danger' && tone !== 'warning' && tone !== 'success' && tone !== 'neutral') {
    return undefined;
  }
  if (position !== 'inline') return undefined;
  return { text, tone: tone as BadgeTone, position };
}

/**
 * Parse + validate an opaque `descriptionColor` value from `sprk_optionsjson`
 * (FR-VH-03). Accepts the five-value whitelist; returns `undefined` for any
 * malformed / unknown input so MetricCard falls back to the default
 * `colorNeutralForeground3` (NFR-05 byte-identical baseline).
 */
function parseDescriptionColor(raw: unknown): DescriptionColorValue | undefined {
  if (raw !== 'brand' && raw !== 'neutral' && raw !== 'success' && raw !== 'warning' && raw !== 'danger') {
    return undefined;
  }
  return raw;
}

/**
 * Parse + validate an opaque `legend` config object from `sprk_optionsjson`
 * (v1.4.4). Each subfield is independently whitelisted; unknown values are
 * dropped (set to undefined) so DonutChart's per-field defaults apply.
 *
 * Backward compat (NFR-05): when `legend` is absent or every subfield is
 * invalid, the returned object is undefined and DonutChart derives a
 * `placement: "right"` default from the existing `donutLayout: "matrixRight"`
 * + `showBreakdownRows: true` keys, so every existing chart def renders
 * identically.
 */
function parseLegend(raw: unknown): IChartLegendConfig | undefined {
  if (!raw || typeof raw !== 'object') return undefined;
  const obj = raw as Record<string, unknown>;

  const placement =
    obj.placement === 'right' ||
    obj.placement === 'left' ||
    obj.placement === 'top' ||
    obj.placement === 'bottom' ||
    obj.placement === 'hidden'
      ? obj.placement
      : undefined;

  const orientation =
    obj.orientation === 'rows' || obj.orientation === 'inline'
      ? obj.orientation
      : undefined;

  const itemFormat =
    obj.itemFormat === 'swatchLabelValue' ||
    obj.itemFormat === 'swatchLabel' ||
    obj.itemFormat === 'labelValue' ||
    obj.itemFormat === 'labelOnly'
      ? obj.itemFormat
      : undefined;

  const valueAlignment =
    obj.valueAlignment === 'near' || obj.valueAlignment === 'far'
      ? obj.valueAlignment
      : undefined;

  const swatchSize =
    typeof obj.swatchSize === 'number' && obj.swatchSize > 0 && obj.swatchSize < 100
      ? obj.swatchSize
      : undefined;

  // Skip the object entirely if every field is invalid/absent — caller
  // falls back to the legacy `matrixRight`/`showBreakdownRows` derivation.
  if (
    placement === undefined &&
    orientation === undefined &&
    itemFormat === undefined &&
    valueAlignment === undefined &&
    swatchSize === undefined
  ) {
    return undefined;
  }

  return { placement, orientation, itemFormat, valueAlignment, swatchSize };
}
