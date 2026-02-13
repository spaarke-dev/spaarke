/**
 * @deprecated v1.2.33 — Use MetricCardMatrix with ICardConfig instead.
 * The ReportCardMetric visual type now routes through the enhanced MetricCard pipeline
 * with configurable icons, colors, descriptions, and value formatting.
 * This component is retained for reference only and will be removed in a future version.
 *
 * GradeMetricCard Component (DEPRECATED)
 * Displays a grade metric with icon, letter grade, color-coded border,
 * and contextual text for matter performance assessment areas.
 * Supports click-to-drill for viewing underlying records.
 */

import * as React from "react";
import {
  Card,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import type { DrillInteraction } from "../types";
import {
  type IColorRule,
  gradeValueToLetter,
  resolveGradeColorScheme,
  getGradeColorTokens,
  resolveContextTemplate,
  resolveAreaIcon,
} from "../utils/gradeUtils";

export interface IGradeMetricCardProps {
  /** Performance area name (e.g., "Guidelines", "Budget", "Outcomes") */
  areaName: string;
  /** Icon key for the area (e.g., "guidelines", "budget", "outcomes") */
  areaIcon: string;
  /** Grade value as decimal 0-1, or null if no data */
  gradeValue: number | null;
  /** Template string for contextual text (supports {grade} and {area} placeholders) */
  contextTemplate?: string;
  /** Custom color rules for grade-to-color mapping */
  colorRules?: IColorRule[];
  /** Callback when card is clicked for drill-through */
  onDrillInteraction?: (interaction: DrillInteraction) => void;
  /** Field name for drill interaction */
  drillField?: string;
  /** Value to filter by when drilling */
  drillValue?: unknown;
  /** Whether the card should be interactive */
  interactive?: boolean;
  /** Fill container width */
  fillContainer?: boolean;
  /** Explicit width in pixels */
  explicitWidth?: number;
  /** Explicit height in pixels */
  explicitHeight?: number;
}

const useStyles = makeStyles({
  card: {
    minWidth: "200px",
    minHeight: "120px",
    cursor: "default",
    position: "relative",
    overflow: "hidden",
    transition: "box-shadow 0.2s ease-in-out, transform 0.2s ease-in-out",
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
  cardFillContainer: {
    width: "100%",
    minWidth: "unset",
    minHeight: "unset",
    aspectRatio: "5 / 3",
  },
  borderAccent: {
    position: "absolute",
    left: "0",
    top: "0",
    bottom: "0",
    width: "4px",
  },
  content: {
    display: "flex",
    flexDirection: "column",
    padding: tokens.spacingVerticalM,
    paddingLeft: `calc(${tokens.spacingHorizontalM} + 4px)`,
    gap: tokens.spacingVerticalS,
    height: "100%",
    boxSizing: "border-box",
  },
  header: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  icon: {
    fontSize: "20px",
    flexShrink: 0,
  },
  label: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
  gradeContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexGrow: 1,
  },
  gradeText: {
    fontSize: tokens.fontSizeHero900,
    fontWeight: tokens.fontWeightBold,
    lineHeight: tokens.lineHeightHero900,
  },
  contextText: {
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
});

const DEFAULT_CONTEXT_TEMPLATE = "You have a {grade}% in {area} compliance";

/**
 * GradeMetricCard - Displays a grade metric with color-coded styling
 */
export const GradeMetricCard: React.FC<IGradeMetricCardProps> = ({
  areaName,
  areaIcon,
  gradeValue,
  contextTemplate,
  colorRules,
  onDrillInteraction,
  drillField,
  drillValue,
  interactive = false,
  fillContainer = false,
  explicitWidth,
  explicitHeight,
}) => {
  const styles = useStyles();

  const colorScheme = resolveGradeColorScheme(gradeValue, colorRules);
  const colorTokens = getGradeColorTokens(colorScheme);
  const letterGrade = gradeValueToLetter(gradeValue);
  const AreaIcon = resolveAreaIcon(areaIcon);

  const template = contextTemplate || DEFAULT_CONTEXT_TEMPLATE;
  const contextMessage = resolveContextTemplate(template, gradeValue, areaName);

  const hasExplicitDimensions = explicitWidth != null && explicitHeight != null;
  const isInteractive = interactive && onDrillInteraction && drillField;

  const handleClick = () => {
    if (isInteractive && onDrillInteraction && drillField) {
      onDrillInteraction({
        field: drillField,
        operator: "eq",
        value: drillValue ?? areaName,
        label: areaName,
      });
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (isInteractive && (e.key === "Enter" || e.key === " ")) {
      e.preventDefault();
      handleClick();
    }
  };

  return (
    <Card
      className={mergeClasses(
        styles.card,
        isInteractive && styles.cardInteractive,
        fillContainer && !hasExplicitDimensions && styles.cardFillContainer
      )}
      style={{
        backgroundColor: colorTokens.cardBackground,
        ...(hasExplicitDimensions
          ? {
              width: `${explicitWidth}px`,
              height: `${explicitHeight}px`,
              minWidth: "unset",
              minHeight: "unset",
            }
          : undefined),
      }}
      onClick={isInteractive ? handleClick : undefined}
      onKeyDown={isInteractive ? handleKeyDown : undefined}
      tabIndex={isInteractive ? 0 : undefined}
      role={isInteractive ? "button" : "region"}
      aria-label={
        isInteractive
          ? `${areaName}: Grade ${letterGrade}. Click to view details.`
          : `${areaName}: Grade ${letterGrade}`
      }
    >
      {/* Color-coded left border accent */}
      <div
        className={styles.borderAccent}
        style={{ backgroundColor: colorTokens.borderAccent }}
      />

      <div className={styles.content}>
        {/* Header: Icon + Area Label */}
        <div className={styles.header}>
          <span className={styles.icon} style={{ color: colorTokens.iconColor }}>
            <AreaIcon />
          </span>
          <Text
            className={styles.label}
            style={{ color: colorTokens.labelColor }}
          >
            {areaName}
          </Text>
        </div>

        {/* Center: Large letter grade */}
        <div className={styles.gradeContainer}>
          <Text
            className={styles.gradeText}
            style={{ color: colorTokens.gradeText }}
            aria-live="polite"
          >
            {letterGrade}
          </Text>
        </div>

        {/* Bottom: Contextual text */}
        <Text
          className={styles.contextText}
          style={{ color: colorTokens.contextText }}
        >
          {contextMessage}
        </Text>
      </div>
    </Card>
  );
};
