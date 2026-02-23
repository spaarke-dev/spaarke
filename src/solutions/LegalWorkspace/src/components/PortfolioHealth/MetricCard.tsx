import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Card,
  CardHeader,
  Skeleton,
  SkeletonItem,
} from "@fluentui/react-components";
import { ArrowUpRegular, ArrowDownRegular } from "@fluentui/react-icons";

export interface IMetricCardProps {
  /** Card label / title */
  label: string;
  /** Formatted metric value (string or number) */
  value: string | number;
  /** Optional icon element from @fluentui/react-icons */
  icon?: React.ReactElement;
  /** Optional trend direction */
  trend?: "up" | "down" | "flat";
  /** Optional numeric delta for trend (e.g. +3 or -2) */
  trendDelta?: number;
  /**
   * Optional severity tint applied to the value text.
   * danger = red, warning = amber, success = green, info = neutral accent
   */
  severity?: "success" | "warning" | "danger" | "info";
  /** When true, renders a skeleton placeholder instead of content */
  isLoading?: boolean;
  /** Optional content rendered below the metric value (e.g. utilization bar) */
  children?: React.ReactNode;
}

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalXS,
    minHeight: "96px",
  },
  headerRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  iconWrapper: {
    display: "flex",
    alignItems: "center",
    color: tokens.colorNeutralForeground3,
    fontSize: "20px",
  },
  label: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightRegular,
  },
  valueRow: {
    display: "flex",
    alignItems: "baseline",
    gap: tokens.spacingHorizontalXS,
  },
  value: {
    fontWeight: tokens.fontWeightSemibold,
  },
  valueSeveritySuccess: {
    color: tokens.colorPaletteGreenForeground1,
  },
  valueSeverityWarning: {
    color: tokens.colorPaletteMarigoldForeground1,
  },
  valueSeverityDanger: {
    color: tokens.colorPaletteCranberryForeground2,
  },
  valueSeverityInfo: {
    color: tokens.colorBrandForeground1,
  },
  trendWrapper: {
    display: "flex",
    alignItems: "center",
    gap: "2px",
  },
  trendUp: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: "12px",
    lineHeight: "1",
  },
  trendDown: {
    color: tokens.colorPaletteCranberryForeground2,
    fontSize: "12px",
    lineHeight: "1",
  },
  trendFlat: {
    color: tokens.colorNeutralForeground3,
    fontSize: "12px",
    lineHeight: "1",
  },
  skeletonCard: {
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalS,
    minHeight: "96px",
  },
});

function getSeverityClass(
  severity: IMetricCardProps["severity"],
  styles: ReturnType<typeof useStyles>
): string | undefined {
  switch (severity) {
    case "success":
      return styles.valueSeveritySuccess;
    case "warning":
      return styles.valueSeverityWarning;
    case "danger":
      return styles.valueSeverityDanger;
    case "info":
      return styles.valueSeverityInfo;
    default:
      return undefined;
  }
}

/** Loading placeholder for a single metric card */
const MetricCardSkeleton: React.FC = () => {
  const styles = useStyles();
  return (
    <div className={styles.skeletonCard} aria-busy="true" aria-label="Loading metric">
      <Skeleton>
        <SkeletonItem size={12} style={{ width: "60%" }} />
        <SkeletonItem size={20} style={{ width: "40%", marginTop: tokens.spacingVerticalXS }} />
      </Skeleton>
    </div>
  );
};

/**
 * MetricCard — reusable card that displays a single KPI metric.
 * Uses Fluent UI v9 tokens exclusively; no hardcoded colors.
 */
export const MetricCard: React.FC<IMetricCardProps> = ({
  label,
  value,
  icon,
  trend,
  trendDelta,
  severity,
  isLoading = false,
  children,
}) => {
  const styles = useStyles();

  if (isLoading) {
    return <MetricCardSkeleton />;
  }

  const severityClass = getSeverityClass(severity, styles);

  return (
    <div className={styles.card} role="region" aria-label={label}>
      {/* Label row with optional icon */}
      <div className={styles.headerRow}>
        {icon && (
          <span className={styles.iconWrapper} aria-hidden="true">
            {icon}
          </span>
        )}
        <Text size={200} className={styles.label}>
          {label}
        </Text>
      </div>

      {/* Value row with optional trend */}
      <div className={styles.valueRow}>
        <Text
          size={600}
          className={`${styles.value}${severityClass ? ` ${severityClass}` : ""}`}
          aria-label={`${label}: ${value}`}
        >
          {value}
        </Text>

        {trend && trend !== "flat" && (
          <div className={styles.trendWrapper} aria-label={`Trend: ${trend}`}>
            {trend === "up" ? (
              <span className={styles.trendUp}>
                <ArrowUpRegular />
              </span>
            ) : (
              <span className={styles.trendDown}>
                <ArrowDownRegular />
              </span>
            )}
            {trendDelta !== undefined && (
              <Text size={100} className={trend === "up" ? styles.trendUp : styles.trendDown}>
                {trendDelta > 0 ? `+${trendDelta}` : trendDelta}
              </Text>
            )}
          </div>
        )}
      </div>

      {/* Optional slot — used by spend card to host UtilizationBar */}
      {children}
    </div>
  );
};
