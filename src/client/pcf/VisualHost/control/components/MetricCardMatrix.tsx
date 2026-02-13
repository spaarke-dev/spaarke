/**
 * MetricCardMatrix Component
 * Renders multiple MetricCards in a responsive CSS Grid layout.
 * Used when a MetricCard visual type has a groupByField producing multiple data points.
 *
 * v1.2.33 Enhancement: Configurable icons, per-card colors (option set accent or
 * value-based token sets), descriptions, custom value formatting, and responsive
 * auto-fill grid layout. Replaces the need for separate GradeMetricCard component.
 *
 * Layout approach: Pure CSS Grid with auto-fill + minmax() for responsive behavior.
 *   - No hardcoded sizes or breakpoints — cards wrap naturally
 *   - Card size config controls the minmax() floor (140/200/280px)
 *   - Fixed columns mode available via config override
 *   - Parent container controls scrolling — component never scrolls itself
 */

import * as React from "react";
import {
  Card,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import {
  GavelRegular,
  MoneyRegular,
  TargetRegular,
  QuestionCircleRegular,
  TaskListSquareLtrRegular,
  CalendarClockRegular,
  AlertRegular,
  CheckmarkCircleRegular,
  DocumentRegular,
  PeopleRegular,
  StarRegular,
  ClipboardRegular,
} from "@fluentui/react-icons";
import type {
  IAggregatedDataPoint,
  DrillInteraction,
  ICardConfig,
  ColorTokenSet,
} from "../types";
import { formatValue } from "../utils/valueFormatters";

export type MatrixJustification = "left" | "center" | "right";

export interface IMetricCardMatrixProps {
  /** Title displayed above the card grid */
  title?: string;
  /** Data points to render as individual cards */
  dataPoints: IAggregatedDataPoint[];
  /** Number of cards per row (legacy — prefer cardConfig.columns) */
  columns?: number;
  /** Available width in pixels (from PCF property) — unused, kept for interface compat */
  width?: number;
  /** Minimum height in pixels (from PCF property) */
  height?: number;
  /** How to justify the card grid within the container */
  justification?: MatrixJustification;
  /** Callback for drill-through interaction */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interactions */
  drillField?: string;
  /** Resolved card configuration from cardConfigResolver */
  cardConfig?: ICardConfig;
}

/** Gap between cards in pixels */
const CARD_GAP = 8;

/** Card size → CSS min-width for auto-fill grid */
const CARD_SIZE_MAP: Record<string, string> = {
  small: "140px",
  medium: "200px",
  large: "280px",
};

// ============= Icon Resolution =============

/**
 * Map of icon name strings to Fluent UI icon components.
 * Keys are case-insensitive (resolved via lowercase lookup).
 */
const ICON_MAP: Record<string, React.ComponentType<{ className?: string }>> = {
  gavel: GavelRegular,
  money: MoneyRegular,
  target: TargetRegular,
  question: QuestionCircleRegular,
  task: TaskListSquareLtrRegular,
  tasklist: TaskListSquareLtrRegular,
  tasklistsquare: TaskListSquareLtrRegular,
  calendar: CalendarClockRegular,
  calendarclock: CalendarClockRegular,
  alert: AlertRegular,
  checkmark: CheckmarkCircleRegular,
  check: CheckmarkCircleRegular,
  document: DocumentRegular,
  people: PeopleRegular,
  star: StarRegular,
  clipboard: ClipboardRegular,
};

/**
 * Resolve icon component from config icon name string
 */
function resolveIcon(
  iconName: string | undefined
): React.ComponentType<{ className?: string }> | null {
  if (!iconName) return null;
  const normalized = iconName.toLowerCase().trim();
  return ICON_MAP[normalized] || null;
}

/**
 * Look up icon for a data point from the iconMap config.
 * Tries: formatted label → raw field value as string → null
 */
function getIconForDataPoint(
  dp: IAggregatedDataPoint,
  iconMap?: Record<string, string>
): React.ComponentType<{ className?: string }> | null {
  if (!iconMap) return null;
  // Try formatted label first (e.g., "Guidelines")
  const byLabel = iconMap[dp.label];
  if (byLabel) return resolveIcon(byLabel);
  // Try raw field value as string (e.g., "100000000")
  const rawKey = String(dp.fieldValue ?? "");
  const byRaw = iconMap[rawKey];
  if (byRaw) return resolveIcon(byRaw);
  return null;
}

// ============= Color Token Resolution =============

/**
 * Resolved color tokens for a single card
 */
interface ICardColorTokens {
  cardBackground?: string;
  borderAccent?: string;
  valueText?: string;
  iconColor?: string;
}

/**
 * Map a ColorTokenSet name to Fluent UI v9 semantic token values.
 * All tokens auto-adapt to light/dark mode via FluentProvider.
 */
function getTokenSetColors(tokenSet: ColorTokenSet): ICardColorTokens {
  switch (tokenSet) {
    case "brand":
      return {
        cardBackground: tokens.colorBrandBackground2,
        borderAccent: tokens.colorBrandBackground,
        valueText: tokens.colorBrandForeground1,
        iconColor: tokens.colorBrandForeground2,
      };
    case "warning":
      return {
        cardBackground: tokens.colorPaletteYellowBackground1,
        borderAccent: tokens.colorPaletteYellowBorderActive,
        valueText: tokens.colorPaletteYellowForeground2,
        iconColor: tokens.colorPaletteYellowForeground2,
      };
    case "danger":
      return {
        cardBackground: tokens.colorPaletteRedBackground1,
        borderAccent: tokens.colorPaletteRedBorderActive,
        valueText: tokens.colorPaletteRedForeground1,
        iconColor: tokens.colorPaletteRedForeground1,
      };
    case "success":
      return {
        cardBackground: tokens.colorPaletteGreenBackground1,
        borderAccent: tokens.colorPaletteGreenBorderActive,
        valueText: tokens.colorPaletteGreenForeground1,
        iconColor: tokens.colorPaletteGreenForeground1,
      };
    case "neutral":
    default:
      return {
        cardBackground: tokens.colorNeutralBackground3,
        borderAccent: tokens.colorNeutralStroke1,
        valueText: tokens.colorNeutralForeground3,
        iconColor: tokens.colorNeutralForeground3,
      };
  }
}

/**
 * Resolve per-card color tokens based on config and data point
 */
function resolveCardColors(
  dp: IAggregatedDataPoint,
  config?: ICardConfig
): ICardColorTokens {
  if (!config) return {};

  switch (config.colorSource) {
    case "optionSetColor": {
      // Use option set hex color as accent only (border + icon tint)
      // Card background and text remain neutral for dark mode compatibility
      if (dp.color) {
        return {
          borderAccent: dp.color,
          iconColor: dp.color,
        };
      }
      return {};
    }

    case "valueThreshold": {
      if (!config.colorThresholds || config.colorThresholds.length === 0) return {};
      // Find matching threshold for this data point's value
      const normalizedValue = dp.value;
      for (const threshold of config.colorThresholds) {
        const [min, max] = threshold.range;
        if (normalizedValue >= min && normalizedValue <= max) {
          return getTokenSetColors(threshold.tokenSet);
        }
      }
      // No match — use neutral
      return getTokenSetColors("neutral");
    }

    case "none":
    default:
      return {};
  }
}

// ============= Description Template =============

/**
 * Resolve description template with placeholders
 * Placeholders: {value} (raw), {formatted} (after formatting), {label}, {count}
 */
function resolveDescription(
  template: string | undefined,
  dp: IAggregatedDataPoint,
  formattedValue: string,
  _totalRecords?: number
): string | undefined {
  if (!template) return undefined;
  return template
    .replace(/\{value\}/g, String(dp.value))
    .replace(/\{formatted\}/g, formattedValue)
    .replace(/\{label\}/g, dp.label)
    .replace(/\{count\}/g, String(dp.value));
}

// ============= Styles =============

const useStyles = makeStyles({
  wrapper: {
    display: "flex",
    flexDirection: "column",
    width: "100%",
    boxSizing: "border-box",
  },
  title: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    paddingBottom: "4px",
    flexShrink: 0,
  },
  grid: {
    display: "grid",
    gap: `${CARD_GAP}px`,
  },
  card: {
    display: "flex",
    flexDirection: "row",
    boxSizing: "border-box",
    cursor: "default",
    transition: "box-shadow 0.2s ease-in-out, transform 0.2s ease-in-out",
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    gap: tokens.spacingHorizontalM,
    aspectRatio: "5 / 3",
    position: "relative",
    overflow: "hidden",
    alignItems: "center",
  },
  cardWithAccentBar: {
    paddingLeft: `calc(${tokens.spacingHorizontalS} + 4px)`,
  },
  cardCompact: {
    padding: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalXS,
    gap: tokens.spacingHorizontalS,
  },
  cardCompactWithAccentBar: {
    paddingLeft: `calc(${tokens.spacingHorizontalXS} + 4px)`,
  },
  cardInteractive: {
    cursor: "pointer",
    "&:hover": {
      boxShadow: tokens.shadow8,
      transform: "translateY(-2px)",
    },
    "&:active": {
      transform: "translateY(0)",
    },
  },
  borderAccent: {
    position: "absolute",
    left: "0",
    top: "0",
    bottom: "0",
    width: "4px",
  },
  cardContent: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    flexGrow: 1,
    minWidth: 0,
  },
  iconSlot: {
    fontSize: "28px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  cardLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase200,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  cardValue: {
    fontSize: tokens.fontSizeHero700,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightHero700,
    color: tokens.colorNeutralForeground1,
  },
  cardValueLarge: {
    fontSize: tokens.fontSizeHero900,
    lineHeight: tokens.lineHeightHero900,
  },
  cardDescription: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flexShrink: 0,
  },
});

// ============= Sort Utilities =============

/**
 * Sort data points according to config sortBy
 */
function sortDataPoints(
  points: IAggregatedDataPoint[],
  sortBy: string
): IAggregatedDataPoint[] {
  const sorted = [...points];
  switch (sortBy) {
    case "value":
      sorted.sort((a, b) => b.value - a.value);
      break;
    case "valueAsc":
      sorted.sort((a, b) => a.value - b.value);
      break;
    case "optionSetOrder":
      sorted.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
      break;
    case "label":
    default:
      sorted.sort((a, b) => a.label.localeCompare(b.label));
      break;
  }
  return sorted;
}

// ============= Component =============

/**
 * MetricCardMatrix - Renders grouped data as configurable metric cards in a responsive grid
 */
export const MetricCardMatrix: React.FC<IMetricCardMatrixProps> = ({
  title,
  dataPoints,
  columns: columnsProp,
  height,
  justification = "left",
  onDrillInteraction,
  drillField,
  cardConfig,
}) => {
  const styles = useStyles();

  // Resolve effective config with defaults
  const config = cardConfig;
  const effectiveValueFormat = config?.valueFormat ?? "shortNumber";
  const effectiveNullDisplay = config?.nullDisplay ?? "—";
  const effectiveCardSize = config?.cardSize ?? "medium";
  const effectiveSortBy = config?.sortBy ?? "label";
  const effectiveCompact = config?.compact ?? false;
  const effectiveColumns = config?.columns ?? columnsProp;
  const effectiveMaxCards = config?.maxCards;
  const effectiveShowAccentBar = config?.showAccentBar ?? false;
  const effectiveTitleFontSize = config?.titleFontSize;

  // Sort and limit data points
  let sortedPoints = sortDataPoints(dataPoints, effectiveSortBy);
  if (effectiveMaxCards && effectiveMaxCards > 0) {
    sortedPoints = sortedPoints.slice(0, effectiveMaxCards);
  }

  const count = sortedPoints.length;
  const isInteractive = !!onDrillInteraction && !!drillField;

  const handleCardClick = (dp: IAggregatedDataPoint) => {
    if (isInteractive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: dp.fieldValue ?? dp.label,
        label: dp.label,
      });
    }
  };

  // Grid layout: responsive auto-fill or fixed columns
  const cardMinWidth = CARD_SIZE_MAP[effectiveCardSize] || CARD_SIZE_MAP.medium;
  const gridStyle: React.CSSProperties = {
    gridTemplateColumns: effectiveColumns && effectiveColumns > 0
      ? `repeat(${Math.min(effectiveColumns, count)}, 1fr)`
      : `repeat(auto-fill, minmax(${cardMinWidth}, 1fr))`,
    justifyItems:
      justification === "center" ? "center"
        : justification === "right" ? "end"
          : "stretch",
  };

  // Wrapper min-height from Height prop
  const wrapperStyle: React.CSSProperties = height
    ? { minHeight: `${height}px` }
    : {};

  // Determine whether to show title from config
  const showTitle = config?.showTitle !== false;

  return (
    <div className={styles.wrapper} style={wrapperStyle}>
      {showTitle && title && (
        <Text
          className={styles.title}
          style={effectiveTitleFontSize ? { fontSize: effectiveTitleFontSize } : undefined}
        >
          {title}
        </Text>
      )}
      <div className={styles.grid} style={gridStyle}>
        {sortedPoints.map((dp, idx) => {
          const formattedVal = formatValue(dp.value, effectiveValueFormat, effectiveNullDisplay);
          const colorTokens = resolveCardColors(dp, config);
          const IconComponent = getIconForDataPoint(dp, config?.iconMap);
          const description = resolveDescription(
            dp.value == null ? config?.nullDescription : config?.cardDescription,
            dp,
            formattedVal
          );

          return (
            <Card
              key={`${dp.label}-${idx}`}
              appearance="subtle"
              className={mergeClasses(
                styles.card,
                effectiveShowAccentBar && styles.cardWithAccentBar,
                effectiveCompact && styles.cardCompact,
                effectiveCompact && effectiveShowAccentBar && styles.cardCompactWithAccentBar,
                isInteractive && styles.cardInteractive
              )}
              style={colorTokens.cardBackground ? { backgroundColor: colorTokens.cardBackground } : undefined}
              onClick={isInteractive ? () => handleCardClick(dp) : undefined}
              tabIndex={isInteractive ? 0 : undefined}
              role={isInteractive ? "button" : "region"}
              aria-label={
                isInteractive
                  ? `${dp.label}: ${formattedVal}. ${description || ""}Click to view details.`
                  : `${dp.label}: ${formattedVal}${description ? `. ${description}` : ""}`
              }
            >
              {/* Color-coded left border accent (conditional on showAccentBar) */}
              {effectiveShowAccentBar && colorTokens.borderAccent && (
                <div
                  className={styles.borderAccent}
                  style={{ backgroundColor: colorTokens.borderAccent }}
                />
              )}

              {/* Icon: left-aligned, vertically centered */}
              {IconComponent && (
                <span
                  className={styles.iconSlot}
                  style={colorTokens.iconColor ? { color: colorTokens.iconColor } : undefined}
                  aria-hidden="true"
                >
                  <IconComponent />
                </span>
              )}

              {/* Content: Label, Value, Description stacked vertically */}
              <div className={styles.cardContent}>
                <Text className={styles.cardLabel}>{dp.label}</Text>
                <Text
                  className={mergeClasses(
                    styles.cardValue,
                    effectiveCardSize === "large" && styles.cardValueLarge
                  )}
                  style={colorTokens.valueText ? { color: colorTokens.valueText } : undefined}
                  aria-live="polite"
                >
                  {formattedVal}
                </Text>
                {description && (
                  <Text
                    className={styles.cardDescription}
                    style={colorTokens.valueText ? { color: colorTokens.valueText, opacity: 0.8 } : undefined}
                  >
                    {description}
                  </Text>
                )}
              </div>
            </Card>
          );
        })}
      </div>
    </div>
  );
};
