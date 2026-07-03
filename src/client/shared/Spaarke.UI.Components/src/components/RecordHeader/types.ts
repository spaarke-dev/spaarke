/**
 * RecordHeader type contracts.
 *
 * FR-02 (record-header-and-notepad-r1): defines the shape of the
 * `RecordHeaderShell` prop surface. `IHeaderToolbarProps` is re-exported
 * from `../HeaderToolbar` intentionally, so per-entity thin PCFs can
 * import both `IRecordHeaderShellProps` and `IHeaderToolbarProps` from a
 * single barrel (`@spaarke/ui-components/components/RecordHeader`).
 *
 * @see ../../../../projects/record-header-and-notepad-r1/spec.md#fr-02
 */

import type * as React from 'react';
import type { IHeaderToolbarProps } from '../HeaderToolbar';

/**
 * Props for {@link RecordHeaderShell} (FR-02).
 *
 * - `toolbar` — REQUIRED fully-formed `IHeaderToolbarProps` (the shell does
 *   NOT re-transform the props — consumer supplies them directly, typically
 *   from `useRecordHeaderToolbarActions`). Rendered at the top of the card.
 * - `loading` — when `true`, the body renders a Fluent v9 `Skeleton` grid
 *   in place of `children`. Toolbar is still rendered at the top so the
 *   card chrome remains visually stable during load. Defaults to `false`.
 * - `children` — the body content, typically a `FieldGrid` composing 3–5
 *   field renderers. Not rendered while `loading === true`.
 */
export interface IRecordHeaderShellProps {
  toolbar: IHeaderToolbarProps;
  loading?: boolean;
  children: React.ReactNode;
  /**
   * When `true`, the shell renders WITHOUT a card border, background, or
   * padding — a bare vertical stack of the toolbar row + body slot. Useful
   * when the PCF is embedded directly in a form section that already provides
   * its own section chrome (matches the user's v1.0.2 feedback: "remove border").
   * Defaults to `false` (backward-compatible with the R1 card look).
   */
  borderless?: boolean;
}
