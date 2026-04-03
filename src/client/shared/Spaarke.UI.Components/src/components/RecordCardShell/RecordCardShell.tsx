/**
 * RecordCardShell — shared card shell for all entity record cards.
 *
 * Provides consistent layout, sizing, hover/focus states, and accessibility
 * across all card types (Documents, Matters, Projects, Todos, Events, etc.).
 * Entity-specific cards are thin wrappers that pass content + tools.
 *
 * Layout:
 *   ┌─ accent border ──────────────────────────────────────────────┐
 *   │ [icon]  Row 1: title + primary fields     [tools] [menu]   │
 *   │         Row 2: secondary content                             │
 *   └─────────────────────────────────────────────────────────────┘
 *
 * The `tools` slot renders inline action buttons (preview, pin, summary,
 * etc.) — different per entity type. The `overflowMenu` slot renders the
 * ⋮ overflow menu. Both are optional.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRecordCardShellProps {
  /** Left icon element — typically a Fluent icon in a colored circle. */
  icon: React.ReactNode;

  /** Row 1 content — title, badges, primary fields. */
  primaryContent: React.ReactNode;

  /** Row 2 content — status badges, description, metadata. Optional. */
  secondaryContent?: React.ReactNode;

  /**
   * Inline action buttons rendered in the right column (e.g., preview,
   * pin, AI summary). Different per entity type. Rendered before
   * the overflow menu.
   */
  tools?: React.ReactNode;

  /** Overflow menu (⋮) — rendered after tools. Optional. */
  overflowMenu?: React.ReactNode;

  /** Left accent border color. Default: brand. Set to "none" to hide. */
  accentColor?: string;

  /** Click handler (single click). Makes the card interactive. */
  onClick?: (e: React.MouseEvent | React.KeyboardEvent) => void;

  /** Double-click handler (e.g., open in new tab). */
  onDoubleClick?: (e: React.MouseEvent) => void;

  /** Accessible label for the card. */
  ariaLabel?: string;

  /** Shows a subtle loading overlay (e.g., while navigating). */
  isLoading?: boolean;

  /** Additional CSS class applied to the root element. */
  className?: string;

  /** Test ID for automated testing. */
  'data-testid'?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow2,
    cursor: 'default',
    position: 'relative',
    transitionProperty: 'background-color, box-shadow',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },

  interactive: {
    cursor: 'pointer',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      boxShadow: tokens.shadow4,
    },
    '&:active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
    '&:focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '-2px',
    },
  },

  accent: {
    borderLeftWidth: '3px',
    borderLeftStyle: 'solid',
  },

  iconColumn: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'flex-start',
    paddingTop: '2px',
  },

  contentColumn: {
    flex: '1 1 0',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },

  primaryRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },

  secondaryRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
  },

  toolsColumn: {
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },

  loadingOverlay: {
    position: 'absolute',
    inset: 0,
    backgroundColor: 'rgba(255, 255, 255, 0.6)',
    borderRadius: tokens.borderRadiusMedium,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    pointerEvents: 'none',
  },
});

// ---------------------------------------------------------------------------
// RecordCardShell
// ---------------------------------------------------------------------------

export const RecordCardShell: React.FC<IRecordCardShellProps> = ({
  icon,
  primaryContent,
  secondaryContent,
  tools,
  overflowMenu,
  accentColor,
  onClick,
  onDoubleClick,
  ariaLabel,
  isLoading,
  className,
  'data-testid': testId,
}) => {
  const styles = useStyles();

  const isInteractive = !!onClick || !!onDoubleClick;
  const effectiveAccent = accentColor ?? tokens.colorBrandStroke1;
  const showAccent = accentColor !== 'none';

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (onClick && (e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        onClick(e);
      }
    },
    [onClick],
  );

  const handleToolsClick = React.useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  return (
    <div
      className={mergeClasses(
        styles.root,
        isInteractive && styles.interactive,
        showAccent && styles.accent,
        className,
      )}
      style={showAccent ? { borderLeftColor: effectiveAccent } : undefined}
      role={isInteractive ? 'button' : 'listitem'}
      tabIndex={isInteractive ? 0 : undefined}
      aria-label={ariaLabel}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
      onKeyDown={isInteractive ? handleKeyDown : undefined}
      data-testid={testId}
    >
      {/* Icon column */}
      <div className={styles.iconColumn}>{icon}</div>

      {/* Content column */}
      <div className={styles.contentColumn}>
        <div className={styles.primaryRow}>{primaryContent}</div>
        {secondaryContent && (
          <div className={styles.secondaryRow}>{secondaryContent}</div>
        )}
      </div>

      {/* Tools + overflow menu column */}
      {(tools || overflowMenu) && (
        // eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions
        <div className={styles.toolsColumn} onClick={handleToolsClick}>
          {tools}
          {overflowMenu}
        </div>
      )}

      {/* Loading overlay */}
      {isLoading && <div className={styles.loadingOverlay} />}
    </div>
  );
};

export default RecordCardShell;
