/**
 * useLaunchContext — Parse the Notepad Code Page launch URL and return the
 * regarding context (entity + record id) or a structured error state.
 *
 * Contract (spec FR-13, NFR-09 — BINDING)
 * ───────────────────────────────────────
 *   • URL parameter names `regardingEntity` and `regardingId` are external API
 *     surface. Any rename is a breaking change requiring a migration plan.
 *   • When invoked via `Xrm.Navigation.navigateTo({pageType: 'webresource',
 *     webresourceName: 'sprk_notepad_page', data: '...' })`, Power Apps wraps
 *     the payload inside a `?data=<urlencoded>` envelope. Direct browser
 *     navigation (`?regardingEntity=foo&regardingId=bar`) is also supported
 *     (test / dev harness path).
 *   • Both wire formats are handled transparently by `parseDataParams` from
 *     `@spaarke/ui-components` (per ADR-006 Code Page convention).
 *
 * Behavior
 * ────────
 *   1. Reads `window.location.search` ONCE on mount (memoised).
 *   2. Parses via `parseDataParams` — handles both the `?data=<envelope>` wire
 *      format used by Power Apps AND the raw `?k=v&k=v` form used in dev.
 *   3. Extracts `regardingEntity` + `regardingId`.
 *   4. Loose validation — both fields present, non-blank, and `regardingId`
 *      has ≥32 characters (a GUID is 36 with hyphens or 32 without, and Xrm
 *      sometimes returns braced GUIDs — we accept whatever the caller passed).
 *      Strict format validation is intentionally deferred to the repository
 *      layer (`useSprkMemoRepository`, task 033) per task guidance.
 *   5. If validation fails, returns `{ valid: false, error, regardingEntity,
 *      regardingId }` — NotepadShell renders the FR-13 MessageBar.
 *
 * Why no Xrm.Utility fallback
 * ───────────────────────────
 * SmartTodo's `useLaunchContext` does NOT use `Xrm.Utility` either — it relies
 * purely on URL parameters. The Notepad Code Page is invoked via
 * `Xrm.Navigation.navigateTo({ data: '...' })` from `HeaderToolbar` (task 012),
 * which ALWAYS populates the URL. No known scenario reaches the Code Page
 * without URL params, so a `Xrm.Utility.getGlobalContext()` fallback would be
 * dead code. FR-13 specifies the missing-context handling explicitly as a
 * user-facing MessageBar, not a silent recovery attempt.
 *
 * Testing
 * ───────
 * `parseLaunchContext(search: string)` is exported as a pure function so tests
 * don't need `@testing-library/react` (not currently in the Notepad devDeps).
 * Production code should call `useLaunchContext()` (the hook).
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-13, NFR-09
 * @see src/solutions/SmartTodo/src/hooks/useLaunchContext.ts (reference impl)
 */

import * as React from 'react';
import { parseDataParams } from '@spaarke/ui-components/utils';

// ---------------------------------------------------------------------------
// Constants — external API surface per NFR-09
// ---------------------------------------------------------------------------

/**
 * URL parameter keys consumed by this hook. Binding contract shared with
 * `useRecordHeaderToolbarActions` (task 012) — any change is a breaking API
 * bump per NFR-09.
 */
export const LAUNCH_PARAM_KEYS = {
  REGARDING_ENTITY: 'regardingEntity',
  REGARDING_ID: 'regardingId',
} as const;

/**
 * Minimum character length to consider a value plausibly a GUID. A canonical
 * GUID is 36 chars with hyphens (`8-4-4-4-12`); braced form is 38 chars; some
 * callers strip hyphens (32 chars). We accept anything ≥32 to stay tolerant of
 * all three shapes without doing full format validation (the repository layer
 * will reject truly malformed values at query time). Prevents obvious garbage
 * (empty string, `?regardingId=abc`) from being treated as valid.
 */
const MIN_GUID_LENGTH = 32;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Parsed launch context. Discriminated by `valid`:
 *   • `valid: true`  — both fields present and pass loose validation; safe to
 *     use as query keys for `useSprkMemoRepository`.
 *   • `valid: false` — at least one field missing or malformed; NotepadShell
 *     renders the FR-13 MessageBar with `error` text and a Close button.
 *
 * `regardingEntity` and `regardingId` are ALWAYS returned (even on invalid
 * state) so error messages can show what WAS received — useful for debugging
 * misconfigured toolbar launches.
 */
export interface ILaunchContext {
  regardingEntity: string | null;
  regardingId: string | null;
  valid: boolean;
  error?: string;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Non-null, non-undefined, and has non-whitespace content. */
function isNonBlank(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Loose GUID plausibility check. Accepts `abc-123-...-xyz`, `{abc-123-...}`,
 * and hyphenless `abc123...` — anything ≥32 chars. Strict format validation is
 * the repository's responsibility. Prevents truly-empty and obvious-garbage
 * values from reaching the CRUD layer.
 */
function looksLikeGuid(value: string): boolean {
  return value.trim().length >= MIN_GUID_LENGTH;
}

// ---------------------------------------------------------------------------
// Pure parser (exported for testability)
// ---------------------------------------------------------------------------

/**
 * Pure parser that reads a search string (e.g., `?regardingEntity=...`) and
 * returns an `ILaunchContext`. Handles both the raw `?k=v&k=v` and the
 * `?data=<urlencoded>` envelope formats transparently via `parseDataParams`.
 *
 * Exported for unit tests; production code should call `useLaunchContext()`.
 *
 * @param search Query string starting with `?` (or empty). Typically
 *   `window.location.search`.
 */
export function parseLaunchContext(search: string): ILaunchContext {
  let params: Record<string, string>;
  try {
    params = parseDataParams(search);
  } catch {
    return {
      regardingEntity: null,
      regardingId: null,
      valid: false,
      error: 'Missing regarding context: failed to parse URL parameters.',
    };
  }

  const rawEntity = params[LAUNCH_PARAM_KEYS.REGARDING_ENTITY];
  const rawId = params[LAUNCH_PARAM_KEYS.REGARDING_ID];

  const regardingEntity = isNonBlank(rawEntity) ? rawEntity.trim() : null;
  const regardingId = isNonBlank(rawId) ? rawId.trim() : null;

  // Enumerate missing / invalid fields for a specific error message.
  const missing: string[] = [];
  if (!regardingEntity) missing.push('regardingEntity');
  if (!regardingId) {
    missing.push('regardingId');
  } else if (!looksLikeGuid(regardingId)) {
    // Present but too short to be a GUID — treat as invalid.
    missing.push('regardingId');
  }

  if (missing.length > 0) {
    const which = missing.join(' and ');
    return {
      regardingEntity,
      regardingId,
      valid: false,
      error: `Missing regarding context: ${which}`,
    };
  }

  return {
    regardingEntity,
    regardingId,
    valid: true,
  };
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Read `window.location.search` once on mount and return the parsed launch
 * context. Result is memoised — subsequent renders return the same reference
 * and the URL is not re-parsed.
 *
 * @example
 * ```tsx
 * const ctx = useLaunchContext();
 * if (!ctx.valid) {
 *   return <MessageBar intent="error">{ctx.error}</MessageBar>;
 * }
 * return <NotepadInner entity={ctx.regardingEntity} id={ctx.regardingId} />;
 * ```
 */
export function useLaunchContext(): ILaunchContext {
  return React.useMemo<ILaunchContext>(() => {
    if (typeof window === 'undefined') {
      return {
        regardingEntity: null,
        regardingId: null,
        valid: false,
        error: 'Missing regarding context: no browser environment.',
      };
    }
    return parseLaunchContext(window.location.search);
  }, []);
}
