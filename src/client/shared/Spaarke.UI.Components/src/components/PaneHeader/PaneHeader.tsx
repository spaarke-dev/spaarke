/**
 * PaneHeader.tsx
 *
 * Shared pane-header primitive for the SpaarkeAi three-pane shell
 * (Assistant / Workspace / Context). Provides the canonical visual treatment:
 * a left icon (rendered in brand-foreground color), a semibold title, and an
 * optional right-aligned slot for stage labels, action buttons, etc.
 *
 * Visual style mirrors the existing `ContextPaneController.tsx` header at
 * lines 142-171 (styles) and 691-700 (markup) — that styling is now the
 * single source of truth, lifted here per ADR-012.
 *
 * Standards:
 *   - ADR-012  Shared component library (context-agnostic, no solution imports)
 *   - ADR-021  Fluent v9 tokens only — zero hex / rgba literals
 *   - ADR-022  React 19 compatible (makeStyles + functional component)
 *   - ADR-025  Icons come from `@fluentui/react-icons` via the `icon` prop
 *
 * Usage:
 *   <PaneHeader
 *     title="Context"
 *     icon={<DocumentRegular />}
 *     rightSlot={<Text size={100}>Stage 2</Text>}
 *   />
 */

import * as React from 'react';
import { Text, makeStyles, tokens } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Header bar — flex row matching ContextPaneController.tsx:142-156.
   * 40px min-height keeps it consistent with the canonical reference and
   * provides comfortable touch-target headroom.
   */
  root: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
    minHeight: '40px',
  },

  /**
   * Icon wrapper — brand-foreground color so any v9 icon passed via the
   * `icon` prop inherits the canonical accent treatment. `flexShrink: 0`
   * prevents the icon from collapsing when the title is long.
   */
  icon: {
    color: tokens.colorBrandForeground1,
    fontSize: '16px',
    flexShrink: 0,
    display: 'inline-flex',
    alignItems: 'center',
  },

  /**
   * Title — semibold, neutral-foreground-1, grows to fill available space
   * so the right slot is pushed to the trailing edge of the header.
   */
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    flexGrow: 1,
  },

  /**
   * Right slot — trailing-edge container for stage labels, buttons, etc.
   * `flexShrink: 0` so contents are never compressed; alignment inherits
   * from the parent's `alignItems: 'center'`.
   */
  rightSlot: {
    flexShrink: 0,
    display: 'inline-flex',
    alignItems: 'center',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PaneHeaderProps {
  /** Visible title text. Rendered with semibold neutral-foreground-1. */
  title: string;
  /**
   * Optional leading icon. Pass any Fluent v9 icon node — it will be wrapped
   * in a span that applies `colorBrandForeground1` and a 16px font-size.
   */
  icon?: React.ReactNode;
  /**
   * Optional right-aligned content (stage label, action button, etc.).
   * Rendered after the title in a non-shrinking flex container.
   */
  rightSlot?: React.ReactNode;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Canonical pane-header primitive consumed by ConversationPane,
 * WorkspacePane, and ContextPaneController. Uses the semantic
 * `<header>` element for accessibility — screen readers announce it as
 * a landmark region without needing an explicit `role`.
 */
export const PaneHeader: React.FC<PaneHeaderProps> = ({ title, icon, rightSlot }) => {
  const styles = useStyles();

  return (
    <header className={styles.root} data-testid="pane-header">
      {icon ? (
        <span className={styles.icon} aria-hidden="true" data-testid="pane-header-icon">
          {icon}
        </span>
      ) : null}
      <Text className={styles.title} size={300}>
        {title}
      </Text>
      {rightSlot ? (
        <span className={styles.rightSlot} data-testid="pane-header-right-slot">
          {rightSlot}
        </span>
      ) : null}
    </header>
  );
};
