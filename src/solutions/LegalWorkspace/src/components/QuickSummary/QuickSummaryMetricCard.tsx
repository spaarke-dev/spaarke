import * as React from "react";
import {
  Text,
  Badge,
  Spinner,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import type { FluentIcon } from "@fluentui/react-icons";
import type { BadgeType } from "./quickSummaryConfig";

export interface IQuickSummaryMetricCardProps {
  /** Display title shown below the count number. */
  title: string;
  /** The count to display. undefined means the query failed. */
  count: number | undefined;
  /** When true, show a Spinner instead of the count. */
  isLoading: boolean;
  /** Called when the card is clicked or activated via keyboard. */
  onClick: () => void;
  /** Accessible label for the card button. */
  ariaLabel: string;
  /** Fluent v9 icon component for the card. */
  icon: FluentIcon;
  /** Badge type: "new" (green) or "overdue" (red). */
  badgeType?: BadgeType;
  /** Badge count. Shown only when > 0 and not loading. */
  badgeCount?: number;
}

const useStyles = makeStyles({
  card: {
    position: "relative",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minWidth: "120px",
    flex: "1 1 0",
    height: "120px",
    padding: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    cursor: "pointer",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth(tokens.strokeWidthThin),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: "box-shadow, background-color, border-color",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    ":focus-visible": {
      outlineWidth: "2px",
      outlineStyle: "solid",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "2px",
    },
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
      boxShadow: tokens.shadow4,
    },
    ":active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Pressed),
      boxShadow: tokens.shadow2,
    },
  },
  iconWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "32px",
    height: "32px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  count: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase600,
    marginBottom: tokens.spacingVerticalXXS,
  },
  title: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    lineHeight: tokens.lineHeightBase200,
  },
  spinnerWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "24px",
    marginBottom: tokens.spacingVerticalXXS,
  },
  badgeWrapper: {
    position: "absolute",
    top: "8px",
    right: "8px",
  },
});

/**
 * QuickSummaryMetricCard — interactive card displaying a single count metric
 * with icon, notification badge, and click-to-navigate.
 */
export const QuickSummaryMetricCard: React.FC<IQuickSummaryMetricCardProps> = ({
  title,
  count,
  isLoading,
  onClick,
  ariaLabel,
  icon: Icon,
  badgeType,
  badgeCount,
}) => {
  const styles = useStyles();

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        onClick();
      }
    },
    [onClick]
  );

  const showBadge = !isLoading && badgeType && badgeCount !== undefined && badgeCount > 0;

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={ariaLabel}
      onClick={onClick}
      onKeyDown={handleKeyDown}
      className={styles.card}
    >
      {/* Notification badge */}
      {showBadge && (
        <div className={styles.badgeWrapper}>
          <Badge
            appearance="filled"
            color={badgeType === "overdue" ? "danger" : "success"}
            size="small"
          >
            {badgeCount} {badgeType === "overdue" ? "Overdue" : "New"}
          </Badge>
        </div>
      )}

      {/* Icon */}
      <div className={styles.iconWrapper} aria-hidden="true">
        <Icon fontSize={16} />
      </div>

      {/* Count */}
      {isLoading ? (
        <div className={styles.spinnerWrapper}>
          <Spinner size="small" />
        </div>
      ) : (
        <Text size={600} weight="semibold" className={styles.count}>
          {count !== undefined ? count : "\u2014"}
        </Text>
      )}

      {/* Title */}
      <Text size={200} className={styles.title}>
        {title}
      </Text>
    </div>
  );
};

QuickSummaryMetricCard.displayName = "QuickSummaryMetricCard";
