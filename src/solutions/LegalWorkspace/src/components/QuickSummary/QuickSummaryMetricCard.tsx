import * as React from "react";
import {
  Text,
  Spinner,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";

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
}

const useStyles = makeStyles({
  card: {
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
    // Focus ring for keyboard navigation
    ":focus-visible": {
      outlineWidth: "2px",
      outlineStyle: "solid",
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: "2px",
    },
    // Hover elevation
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Hover),
      boxShadow: tokens.shadow4,
    },
    // Active / pressed state
    ":active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
      ...shorthands.borderColor(tokens.colorNeutralStroke1Pressed),
      boxShadow: tokens.shadow2,
    },
  },
  count: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase700,
    marginBottom: tokens.spacingVerticalXS,
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
    height: "28px",
    marginBottom: tokens.spacingVerticalXS,
  },
});

/**
 * QuickSummaryMetricCard — interactive card displaying a single count metric.
 *
 * Renders a Fluent v9 styled card with:
 *   - Large count number (fontSizeBase700, semibold)
 *   - Title label below (fontSizeBase200, foreground3)
 *   - Hover elevation (shadow4) and focus ring (2px colorBrandStroke1)
 *   - Loading state with Spinner
 *   - Error state showing em-dash
 *   - Full keyboard support (Enter / Space)
 *   - Semantic tokens throughout (zero hardcoded colors, dark mode compatible)
 */
export const QuickSummaryMetricCard: React.FC<IQuickSummaryMetricCardProps> = ({
  title,
  count,
  isLoading,
  onClick,
  ariaLabel,
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

  const renderCount = (): React.ReactNode => {
    if (isLoading) {
      return (
        <div className={styles.spinnerWrapper}>
          <Spinner size="small" />
        </div>
      );
    }
    return (
      <Text size={700} weight="semibold" className={styles.count}>
        {count !== undefined ? count : "\u2014"}
      </Text>
    );
  };

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={ariaLabel}
      onClick={onClick}
      onKeyDown={handleKeyDown}
      className={styles.card}
    >
      {renderCount()}
      <Text size={200} className={styles.title}>
        {title}
      </Text>
    </div>
  );
};

QuickSummaryMetricCard.displayName = "QuickSummaryMetricCard";
