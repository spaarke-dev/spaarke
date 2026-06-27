/**
 * InfoPopover — Fluent UI v9 Popover with an `Info16Regular` trigger Button.
 *
 * Phase G (Lookup-driven multi-index) — see spec §6.
 *
 * Used in the SearchFilterPane to replace inline-help labels (the "(i)" or
 * descriptive text below field labels) with a compact icon-triggered popover.
 * Keeps the side pane visually clean while preserving access to help content.
 *
 * Usage:
 * ```tsx
 * <div className={styles.labelRow}>
 *   <Label>Relevance Threshold</Label>
 *   <InfoPopover ariaLabel="About relevance threshold">
 *     Hide results scoring below this percentage.
 *   </InfoPopover>
 * </div>
 * ```
 *
 * @see ADR-021 — Fluent UI v9 design system + token-based styling
 */

import type { ReactNode } from 'react';
import { makeStyles, tokens, Button, Popover, PopoverTrigger, PopoverSurface } from '@fluentui/react-components';
import { Info16Regular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface InfoPopoverProps {
  /** Help body — React node rendered inside the PopoverSurface. */
  children: ReactNode;
  /** Accessible label for the trigger button. */
  ariaLabel?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: 'auto',
    paddingTop: 0,
    paddingBottom: 0,
    paddingLeft: tokens.spacingHorizontalXXS,
    paddingRight: tokens.spacingHorizontalXXS,
    color: tokens.colorNeutralForeground3,
  },
  surface: {
    maxWidth: '280px',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    lineHeight: '1.4',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const InfoPopover: React.FC<InfoPopoverProps> = ({ children, ariaLabel }) => {
  const styles = useStyles();

  return (
    <Popover positioning="above">
      <PopoverTrigger disableButtonEnhancement>
        <Button
          className={styles.trigger}
          appearance="transparent"
          size="small"
          icon={<Info16Regular />}
          aria-label={ariaLabel ?? 'More information'}
        />
      </PopoverTrigger>
      <PopoverSurface className={styles.surface}>{children}</PopoverSurface>
    </Popover>
  );
};

export default InfoPopover;
