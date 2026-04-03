/**
 * CardIcon — branded circle wrapper for card icons.
 *
 * Renders a Fluent icon inside a colored circle, used as the left
 * column of RecordCardShell. Supports custom colors for entity-specific
 * theming (e.g., file-type icons for documents).
 *
 * @see ADR-021 - Fluent UI v9 design system
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICardIconProps {
  /** Fluent icon element (e.g., <DocumentRegular />, <GavelRegular />). */
  children: React.ReactNode;

  /** Circle size in pixels. Default: 40. */
  size?: number;

  /** Background color. Default: brand background 2. */
  backgroundColor?: string;

  /** Icon color. Default: brand foreground 1. */
  iconColor?: string;

  /** Additional CSS class. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  circle: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: tokens.borderRadiusCircular,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// CardIcon
// ---------------------------------------------------------------------------

export const CardIcon: React.FC<ICardIconProps> = ({
  children,
  size = 40,
  backgroundColor,
  iconColor,
  className,
}) => {
  const styles = useStyles();

  return (
    <div
      className={mergeClasses(styles.circle, className)}
      style={{
        width: size,
        height: size,
        backgroundColor: backgroundColor ?? tokens.colorBrandBackground2,
        color: iconColor ?? tokens.colorBrandForeground1,
        fontSize: Math.round(size * 0.5),
      }}
      aria-hidden="true"
    >
      {children}
    </div>
  );
};

export default CardIcon;
