/**
 * Type definitions for `RecordNavigationModalShell`.
 *
 * Public API:
 *   - `IRecordNavigationModalShellProps` — component props (FR-12).
 *
 * Cross-frame messaging protocol (FR-14):
 *   - `IDirtyCheckRequest` — parent → iframe.
 *   - `IDirtyCheckResponse` — iframe → parent.
 *   - `DIRTY_CHECK_REQUEST_TYPE` / `DIRTY_CHECK_RESULT_TYPE` — message-type discriminators.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 (semantic tokens, dark-mode parity)
 */

import type * as React from 'react';

// ---------------------------------------------------------------------------
// Cross-frame messaging protocol (FR-14)
// ---------------------------------------------------------------------------

/**
 * `request-dirty-check` — the outer shell posts this into the iframe's
 * `contentWindow` immediately before invoking `onNavigate`. The iframe-side
 * listener (not authored in this task; see smart-todo-r4 task 041) MUST
 * respond with an `IDirtyCheckResponse` carrying the same `correlationId`.
 */
export const DIRTY_CHECK_REQUEST_TYPE = 'request-dirty-check' as const;

/**
 * `dirty-check-result` — the iframe responds with this message after
 * inspecting its form-context dirty flag. The outer shell correlates the
 * response to the originating request via `correlationId`.
 */
export const DIRTY_CHECK_RESULT_TYPE = 'dirty-check-result' as const;

/**
 * Direction of a navigation request.
 */
export type RecordNavigationDirection = 'prev' | 'next';

/**
 * Outbound dirty-check request — posted by the shell to the iframe.
 */
export interface IDirtyCheckRequest {
  type: typeof DIRTY_CHECK_REQUEST_TYPE;
  /** Stable correlation id used to match the response to this request. */
  correlationId: string;
}

/**
 * Inbound dirty-check response — posted by the iframe back to the shell.
 */
export interface IDirtyCheckResponse {
  type: typeof DIRTY_CHECK_RESULT_TYPE;
  /** Echo of the originating request's `correlationId`. */
  correlationId: string;
  /** Whether the iframe's form has unsaved changes. */
  dirty: boolean;
}

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/**
 * Props for {@link RecordNavigationModalShell}.
 *
 * The shell renders modal chrome (header with `<` / `>` nav + "N of M"
 * counter + title; optional action bar; content area) and orchestrates the
 * cross-frame `request-dirty-check` / `dirty-check-result` protocol when
 * `dirtyCheckTargetWindow` is supplied.
 *
 * This component does NOT own the modal envelope (`Dialog` / `DialogSurface`).
 * Callers wrap it in their own modal surface (Fluent v9 `Dialog`,
 * `Xrm.Navigation.navigateTo` Code Page modal, etc.) per FR-13.
 */
export interface IRecordNavigationModalShellProps {
  /**
   * 0-based position of the currently-shown record inside the navigation set.
   * Used to render the "N of M" counter and gate the prev/next disabled state.
   */
  currentIndex: number;

  /**
   * Total record count in the navigation set. When &lt; 2, the prev/next
   * affordances are still rendered but disabled.
   */
  navigationTotal: number;

  /**
   * Invoked when the user clicks `<` or `>` (or presses the equivalent
   * keyboard shortcut) AND the dirty-check resolves clean (or the user
   * confirms discard). May be async — the shell awaits it before
   * re-enabling the nav buttons.
   */
  onNavigate: (direction: RecordNavigationDirection) => void | Promise<void>;

  /**
   * Title rendered in the header (typically the current record's display
   * name).
   */
  title: string;

  /**
   * Optional action bar slot — rendered to the right of the header (e.g.
   * download, close, open-in-new-tab). Callers compose Fluent v9 `Button`
   * elements here.
   */
  actionBar?: React.ReactNode;

  /**
   * Content area children — typically an iframe pointing at the OOB MDA
   * form for the current record (per FR-13).
   */
  children: React.ReactNode;

  /**
   * Optional iframe `contentWindow` to query for the dirty-check protocol.
   * When `null` or `undefined`, the dirty-check round-trip is skipped and
   * `onNavigate` is invoked immediately on prev/next click.
   */
  dirtyCheckTargetWindow?: Window | null;

  /**
   * Optional target origin for outbound dirty-check messages. Defaults to
   * `"*"` (any origin). For production use, callers SHOULD pin this to the
   * iframe's expected origin (e.g. `"https://contoso.crm.dynamics.com"`).
   * Inbound responses are validated against the {@link allowedOrigins} list
   * regardless of this value.
   */
  dirtyCheckTargetOrigin?: string;

  /**
   * Optional override of the inbound-message origin allow-list. Defaults to
   * `["https://*.dynamics.com", window.location.origin]`. Patterns may
   * include a single leading `*.` wildcard subdomain.
   */
  allowedOrigins?: ReadonlyArray<string>;

  /**
   * Timeout (ms) for the dirty-check round-trip. If no response arrives
   * within this window, the shell treats the iframe as clean and proceeds
   * with `onNavigate`. Defaults to `1000`.
   */
  dirtyCheckTimeout?: number;

  /**
   * Optional callback invoked when the user confirms the "Discard unsaved
   * changes?" prompt. Fires BEFORE `onNavigate`. Useful for caller-side
   * cleanup (e.g. clearing autosave drafts).
   */
  onDirtyDiscard?: () => void;

  /**
   * Additional CSS class applied to the root element. Callers may use this
   * to override sizing inside their modal surface.
   */
  className?: string;

  /** Test ID for automated testing of the root element. */
  'data-testid'?: string;
}
