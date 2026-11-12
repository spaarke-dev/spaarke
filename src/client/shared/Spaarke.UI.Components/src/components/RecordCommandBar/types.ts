/**
 * RecordCommandBar type contract.
 *
 * FR-04 (spaarke-SPA-external-access-platform-r4): a record-level action
 * descriptor carries exactly `id`, `label`, `icon`, `onClick`, `visible`,
 * `tooltip` and an optional `overflow`. There is deliberately NO `disabled`
 * field: an action the caller may not use is hidden, never shown disabled
 * (owner decision 2026-11-08, "hide, don't disable").
 *
 * The component is data-free (ADR-012): the host decides visibility from its
 * own access data and passes plain descriptors in.
 */

import type * as React from 'react';

/**
 * One record-level action rendered by {@link RecordCommandBar}.
 */
export interface RecordCommandBarAction {
  /**
   * Stable identifier, unique within one bar. Used as the React key.
   * For icon lookup, use a key of `RECORD_ACTION_ICONS` (e.g. `refresh`).
   */
  id: string;

  /**
   * Visible text of the action when it renders as an item in the
   * "More actions" overflow menu. Inline actions are icon-only, so the label
   * is not shown for them; their accessible name comes from `tooltip`.
   */
  label: string;

  /**
   * Fluent v9 icon element, normally `<RECORD_ACTION_ICONS.refresh />` etc.
   * Typed as `React.ReactElement` so it drops into the Fluent `Button` /
   * `MenuItem` icon slot without a cast.
   */
  icon: React.ReactElement;

  /**
   * Invoked when the action is activated (click, Enter or Space).
   * Sync or async; a returned promise is not awaited and its errors are the
   * caller's to handle (same contract as `IHeaderToolbarSlot.onClick`).
   */
  onClick: () => void | Promise<void>;

  /**
   * Whether the caller may use this action. Only `true` renders it; any other
   * value means the action is not rendered at all (not in the DOM, no tab
   * stop). Hide, don't disable.
   */
  visible: boolean;

  /**
   * REQUIRED accessible label. Shown as the Fluent `Tooltip` and used as the
   * element's `aria-label`, for inline buttons and overflow menu items alike.
   * Keep the `label` text inside it (WCAG 2.5.3 "label in name"), e.g.
   * label "Download", tooltip "Download file".
   */
  tooltip: string;

  /**
   * When `true`, the action renders inside the "More actions" overflow menu
   * instead of as an inline icon button. Default `false`.
   */
  overflow?: boolean;
}

/**
 * Props for {@link RecordCommandBar}.
 */
export interface RecordCommandBarProps {
  /**
   * The record's actions, in display order. Actions with `visible !== true`
   * are dropped. An empty (or all-hidden) list renders an empty bar.
   */
  actions: RecordCommandBarAction[];

  /**
   * Accessible name of the bar (`role="toolbar"`), e.g. "Record actions".
   */
  ariaLabel: string;

  /**
   * Optional class name merged LAST onto the bar root so callers can adjust
   * layout (margins, width). Use Fluent tokens in the caller's styles.
   */
  className?: string;
}
