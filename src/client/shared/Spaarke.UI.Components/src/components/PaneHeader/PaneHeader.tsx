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
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

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
   * Clickable variant — applied when `onCollapse` is wired so the header acts
   * as a collapse trigger. Cursor is `pointer` everywhere except over icon
   * buttons in `rightSlot` (those buttons stop propagation themselves).
   * Mirrors SmartToDo KanbanBoard.tsx column-header pattern (Task 094).
   */
  rootClickable: {
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
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
   *
   * When `onCollapse` is wired, interactive elements inside `rightSlot`
   * (Buttons, Menus, etc.) MUST call `e.stopPropagation()` in their own
   * `onClick` handlers so clicking them does not trigger the collapse.
   * The shared PaneHeader applies `stopPropagation` on the rightSlot
   * container's `onClick` automatically, so consumers do NOT have to wire
   * it on every internal button — the container guard catches bubbling
   * clicks from any descendant. This mirrors the SmartToDo KanbanBoard
   * column-header pattern (Task 094).
   */
  rightSlot?: React.ReactNode;
  /**
   * Optional collapse-toggle callback (Task 094 — SpaarkeAi three-pane
   * collapse/expand). When provided, the header becomes a clickable surface
   * — clicking anywhere on the header (except inside `rightSlot` icon
   * buttons, which stop propagation) invokes this callback. Used by the
   * three-pane shell to toggle a pane's collapsed state.
   *
   * Pattern matches SmartToDo KanbanBoard.tsx column-header click-to-collapse
   * (`onToggleCollapse` prop). Keyboard accessibility: when wired, the
   * header is `tabIndex=0`, `role="button"`, `aria-expanded={expanded}`,
   * and responds to Enter / Space.
   */
  onCollapse?: () => void;
  /**
   * When `onCollapse` is wired, indicates whether the pane is currently
   * expanded. Drives `aria-expanded` on the clickable header for screen
   * readers. Defaults to `true` so cold-load state is reported correctly
   * before consumer state initialises.
   */
  expanded?: boolean;
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
export const PaneHeader: React.FC<PaneHeaderProps> = ({
  title,
  icon,
  rightSlot,
  onCollapse,
  expanded = true,
}) => {
  const styles = useStyles();

  // ── Collapse-toggle wiring (Task 094) ──────────────────────────────────
  //
  // When `onCollapse` is provided, the header becomes a clickable / focusable
  // surface that toggles the pane's collapsed state. Keyboard accessibility
  // is wired via tabIndex / role / onKeyDown so the same affordance is
  // reachable without a mouse.
  //
  // The rightSlot container stops propagation so any interactive content
  // inside (Buttons, Menus, Tooltips) does not bubble its click up to the
  // header. This is the same guard SmartToDo KanbanBoard.tsx uses for its
  // column headers (KanbanBoard.tsx:212 `onClick={onToggleCollapse}` on the
  // outer header, with the count badge / icon buttons inside the header
  // never propagating).
  const collapsible = typeof onCollapse === 'function';

  const handleHeaderClick = collapsible ? () => onCollapse?.() : undefined;

  const handleHeaderKeyDown = collapsible
    ? (e: React.KeyboardEvent<HTMLElement>): void => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onCollapse?.();
        }
      }
    : undefined;

  const handleRightSlotClick = collapsible
    ? (e: React.MouseEvent<HTMLSpanElement>): void => {
        // Prevent rightSlot children (icon buttons, menus) from bubbling
        // their click up to the header and accidentally triggering collapse.
        e.stopPropagation();
      }
    : undefined;

  const handleRightSlotKeyDown = collapsible
    ? (e: React.KeyboardEvent<HTMLSpanElement>): void => {
        // Block Enter / Space inside rightSlot from reaching the header's
        // keyboard handler. Buttons inside Fluent components handle their
        // own Enter / Space natively; we just stop the bubble here.
        if (e.key === 'Enter' || e.key === ' ') {
          e.stopPropagation();
        }
      }
    : undefined;

  return (
    <header
      className={mergeClasses(styles.root, collapsible && styles.rootClickable)}
      data-testid="pane-header"
      onClick={handleHeaderClick}
      onKeyDown={handleHeaderKeyDown}
      role={collapsible ? 'button' : undefined}
      tabIndex={collapsible ? 0 : undefined}
      aria-expanded={collapsible ? expanded : undefined}
      aria-label={collapsible ? `${title} pane — click to ${expanded ? 'collapse' : 'expand'}` : undefined}
    >
      {icon ? (
        <span className={styles.icon} aria-hidden="true" data-testid="pane-header-icon">
          {icon}
        </span>
      ) : null}
      <Text className={styles.title} size={300}>
        {title}
      </Text>
      {rightSlot ? (
        <span
          className={styles.rightSlot}
          data-testid="pane-header-right-slot"
          onClick={handleRightSlotClick}
          onKeyDown={handleRightSlotKeyDown}
        >
          {rightSlot}
        </span>
      ) : null}
    </header>
  );
};
