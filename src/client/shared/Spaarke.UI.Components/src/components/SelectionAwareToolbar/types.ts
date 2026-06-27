/**
 * SelectionAwareToolbar — types
 *
 * Shared toolbar primitive that becomes visible only when ≥1 row/card is
 * selected. Consumers pass an arbitrary list of actions via a slot-based API.
 *
 * Initial consumers: smart-todo-r4 SmartTodo Code Page (tasks 030–033, 070).
 *
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-012 Shared component library
 * @see smart-todo-r4 spec FR-08
 */
import type * as React from 'react';

/**
 * A single action rendered in the selection-aware toolbar.
 *
 * - `id`: Stable React key (must be unique within an `actions[]`).
 * - `label`: Visible button label + accessible name.
 * - `icon`: Optional leading icon (Fluent v9 react-icons element).
 * - `onClick`: Click handler. Receives no args — the parent already knows the
 *   selection set.
 * - `disabled`: When `true`, button is rendered but non-interactive.
 * - `appearance`: Fluent v9 Button `appearance`. Defaults to `'subtle'`.
 */
export interface ToolbarAction {
  /** Stable React key. */
  id: string;
  /** Visible label / accessible name. */
  label: string;
  /** Optional leading icon (Fluent v9 react-icon element). */
  icon?: React.ReactNode;
  /** Click handler. */
  onClick: () => void;
  /** When true, the button renders disabled. */
  disabled?: boolean;
  /**
   * Fluent v9 Button appearance. Defaults to `'subtle'`.
   * Allowed: `'primary'`, `'subtle'`, `'outline'`.
   */
  appearance?: 'primary' | 'subtle' | 'outline';
}

/** Props for {@link SelectionAwareToolbar}. */
export interface SelectionAwareToolbarProps {
  /**
   * Number of items currently selected. The component renders `null` when
   * this is `0`.
   */
  selectedCount: number;
  /** Actions to render as ToolbarButtons (left-to-right). */
  actions: ToolbarAction[];
  /**
   * When `true` (default), the toolbar prefixes the actions with
   * "N selected".
   */
  showCountLabel?: boolean;
  /** Optional extra className applied to the toolbar container. */
  className?: string;
}
