/**
 * HeaderToolbar type contract
 *
 * FR-01 (record-header-and-notepad-r1): defines the shape of a single
 * icon slot and the toolbar-level props. Every field here is part of the
 * external contract consumed by `RecordHeaderShell`, `MatterHeaderPcf`,
 * and future per-entity thin PCFs; changes are BREAKING and require a
 * shared-lib major version bump.
 *
 * @see ../../../../projects/record-header-and-notepad-r1/spec.md#fr-01
 */

import type * as React from 'react';
import type { ISummaryData } from '../AiSummaryPopover/AiSummaryPopover';

/**
 * A single right-aligned icon slot in the HeaderToolbar.
 *
 * - `key` — stable React key for reconciliation.
 * - `icon` — Fluent v9 icon element (from `@fluentui/react-icons`). Typed
 *   as `React.ReactElement` so it composes into the Fluent v9 `Button` icon
 *   slot without a cast (v9 slots reject `ReactNode` unions like `bigint`).
 * - `onClick` — sync or async handler; awaited exceptions are the caller's
 *   responsibility. The button ignores clicks when `disabled === true`.
 * - `tooltip` — REQUIRED a11y label. Rendered inside a Fluent v9 `Tooltip`
 *   with `relationship="label"` so screen readers announce it and it also
 *   populates `aria-label` on the icon-only button.
 * - `badge` — optional count overlay. Rendered as a Fluent v9 `CounterBadge`
 *   ONLY when the value is a positive integer (`> 0`). `0` or `undefined`
 *   suppresses the badge entirely (per FR-01 acceptance criterion).
 * - `disabled` — when true, the underlying `Button` is disabled and does
 *   not fire `onClick`.
 */
export interface IHeaderToolbarSlot {
  key: string;
  icon: React.ReactElement;
  onClick: () => void | Promise<void>;
  tooltip: string;
  badge?: number;
  disabled?: boolean;
  /**
   * Optional forwarded ref for the button element. Consumers that need to
   * anchor a Popover, Menu, or Dialog against a specific icon (e.g. the
   * sparkle → AI summary popover) attach a ref via this field and read
   * `.current` at open time. Added v1.0.2 fix: the sparkle popover in
   * `useRecordHeaderToolbarActions` was previously unanchored because the
   * hook could not reach the trigger button through the toolbar layer.
   */
  buttonRef?: React.Ref<HTMLButtonElement>;
}

/**
 * Props for the HeaderToolbar shared component (FR-01).
 *
 * - `title` — optional left-aligned text. Truncated with ellipsis on
 *   overflow so the icon slots stay right-aligned regardless of title
 *   length. Omit for icons-only toolbars.
 * - `iconSlots` — zero or more right-aligned icon slots. Empty array
 *   renders a valid (empty) toolbar.
 */
export interface IHeaderToolbarProps {
  title?: string;
  iconSlots: IHeaderToolbarSlot[];
  /**
   * Optional AI-summary popover, rendered as the FIRST element in the icon
   * strip via the shared `<AiSummaryPopover>` component (same one used by
   * VisualHost, SemanticSearchControl, DocumentCard, etc.). When provided,
   * `HeaderToolbar` builds the sparkle trigger internally so callers don't
   * have to direct-import `@fluentui/react-icons` from a virtual PCF (that
   * import path breaks webpack's griffel/src resolution — see the v1.0.4
   * and v1.0.10 build regressions).
   *
   * The `onFetchSummary` callback is invoked lazily on first open — return
   * `{ summary, tldr }` (either may be `null`). Retained on this shared
   * contract because AI summary is a first-class record-header surface
   * across every entity's header PCF (spec FR-08).
   */
  aiSummary?: {
    onFetchSummary: () => Promise<ISummaryData>;
  };
}
