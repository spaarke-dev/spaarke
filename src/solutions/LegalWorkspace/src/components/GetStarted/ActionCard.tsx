import * as React from "react";
import {
  Text,
  makeStyles,
  shorthands,
  tokens,
  mergeClasses,
} from "@fluentui/react-components";
import type { FluentIcon } from "@fluentui/react-icons";

export interface IActionCardProps {
  /** Fluent v9 icon component to render above the label. */
  icon: FluentIcon;
  /** Short label displayed below the icon. */
  label: string;
  /** Accessible description for the card button. */
  ariaLabel: string;
  /** Called when the card is clicked. Stub until tasks 024/025 wire it. */
  onClick?: () => void;
  /** When true the card is rendered in a non-interactive disabled state. */
  disabled?: boolean;
}

const useStyles = makeStyles({
  card: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minWidth: "120px",
    maxWidth: "148px",
    width: "136px",
    flexShrink: 0,
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    cursor: "pointer",
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth(tokens.strokeWidthThin),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    transitionProperty: "box-shadow, background-color, border-color",
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    // Focus ring via outline (keyboard navigation)
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
  cardDisabled: {
    cursor: "not-allowed",
    opacity: "0.5",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1,
      ...shorthands.borderColor(tokens.colorNeutralStroke2),
      boxShadow: "none",
    },
    ":active": {
      backgroundColor: tokens.colorNeutralBackground1,
      ...shorthands.borderColor(tokens.colorNeutralStroke2),
      boxShadow: "none",
    },
  },
  iconWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "40px",
    height: "40px",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground2,
    marginBottom: tokens.spacingVerticalS,
    color: tokens.colorBrandForeground1,
  },
  label: {
    textAlign: "center",
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase200,
  },
});

/**
 * ActionCard — interactive card with icon + label used in the Get Started row.
 *
 * Renders a Fluent v9 Card with:
 *   - Hover elevation (shadow4)
 *   - Focus ring (2px solid colorBrandStroke1) for keyboard navigation
 *   - Disabled state with reduced opacity
 *   - Semantic tokens throughout (zero hardcoded colors)
 */
export const ActionCard: React.FC<IActionCardProps> = ({
  icon: Icon,
  label,
  ariaLabel,
  onClick,
  disabled = false,
}) => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    if (!disabled && onClick) {
      onClick();
    }
  }, [disabled, onClick]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (!disabled && (e.key === "Enter" || e.key === " ")) {
        e.preventDefault();
        onClick?.();
      }
    },
    [disabled, onClick]
  );

  return (
    <div
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-label={ariaLabel}
      aria-disabled={disabled}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      className={mergeClasses(styles.card, disabled && styles.cardDisabled)}
    >
      <div className={styles.iconWrapper} aria-hidden="true">
        <Icon fontSize={20} />
      </div>
      <Text size={200} weight="semibold" className={styles.label}>
        {label}
      </Text>
    </div>
  );
};

ActionCard.displayName = "ActionCard";
