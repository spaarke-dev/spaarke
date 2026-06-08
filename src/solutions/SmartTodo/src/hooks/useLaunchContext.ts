/**
 * useLaunchContext — Parse the SmartTodo Code Page launch URL for `action=createTodo`
 * and an optional pre-filled regarding triple, then clear the URL params so a refresh
 * doesn't re-trigger the wizard.
 *
 * Why this exists
 * ──────────────
 * The Outlook add-in's "Create To Do" ribbon (task 070) opens the SmartTodo Code Page
 * with a query string carrying a `createTodo` action and the `sprk_communication`
 * regarding triple. SmartTodo had no parser, so the wizard opened (when opened) at
 * its default state without pre-fill. This hook closes that contract gap (task 070b).
 *
 * Contract (binding — read alongside)
 * ───────────────────────────────────
 *   • Launch contract: `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`
 *   • URL builder side: `src/client/office-addins/shared/taskpane/services/createTodoLauncher.ts`
 *
 * URL params consumed (matches `CREATE_TODO_LAUNCH_PARAMS` from the builder):
 *   • `action`        — must equal `'createTodo'` to trigger
 *   • `regardingType` — Dataverse logical name (e.g., `sprk_communication`)
 *   • `regardingId`   — GUID (lowercased, no braces)
 *   • `regardingName` — display name (typically the email subject)
 *
 * Behavior
 * ────────
 *   1. Reads `window.location.search` ONCE on mount (no re-reads — refs guard).
 *   2. If `action !== 'createTodo'` → returns `undefined`; SmartTodo renders normally.
 *   3. If `action === 'createTodo'` AND all three regarding params are present and non-empty:
 *        → returns `{ action: 'createTodo', initialRegarding: { entityType, recordId, recordName } }`.
 *   4. If `action === 'createTodo'` but regarding params are missing/blank:
 *        → returns `{ action: 'createTodo', initialRegarding: undefined }` (graceful degrade,
 *          logs a console warning). The wizard still auto-opens but without pre-fill.
 *   5. After the read, clears the launch params via `history.replaceState`, preserving
 *      any other query string keys (e.g., `data=…` envelope from Xrm.Navigation).
 *
 * Product-portability
 * ───────────────────
 * Pure browser APIs only (`window.location` / `URLSearchParams` / `history.replaceState`).
 * No hardcoded URLs.
 *
 * @see task 070b POML for the full requirement
 * @see projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md (binding)
 */

import * as React from 'react';

// ---------------------------------------------------------------------------
// Constants — MUST match createTodoLauncher.ts on the Outlook side
// ---------------------------------------------------------------------------

/** Action discriminator value indicating the wizard should auto-open. */
export const LAUNCH_ACTION_CREATE_TODO = 'createTodo';

/**
 * Query-parameter keys consumed by this hook. The launch contract (see header)
 * binds these names verbatim — changing them is a breaking change shared with
 * `createTodoLauncher.ts`.
 */
export const LAUNCH_PARAM_KEYS = {
  ACTION: 'action',
  REGARDING_TYPE: 'regardingType',
  REGARDING_ID: 'regardingId',
  REGARDING_NAME: 'regardingName',
} as const;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Regarding triple shape — mirrors `AssociationResult` from `@spaarke/ui-components`
 * (the wizard's `initialRegarding` prop). Inlined to avoid an unnecessary import
 * since this hook is pure URL parsing.
 */
export interface ILaunchRegarding {
  /** Dataverse logical name (e.g., `'sprk_communication'`). */
  entityType: string;
  /** GUID (lowercased, no braces — normalised by the URL builder). */
  recordId: string;
  /** Display name (drives the AssociateToStep selected-record card). */
  recordName: string;
}

/**
 * Parsed launch context returned by `useLaunchContext`. `undefined` indicates the
 * Code Page was loaded without a launch action — SmartTodo renders normally.
 */
export interface ILaunchContext {
  /** Action discriminator. Currently only `'createTodo'` is recognised. */
  action: typeof LAUNCH_ACTION_CREATE_TODO;
  /**
   * Pre-filled regarding triple from the URL. `undefined` when the action is
   * present but the regarding params are missing/blank (graceful degrade —
   * wizard auto-opens with no pre-fill).
   */
  initialRegarding: ILaunchRegarding | undefined;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Returns `true` when the string is non-null, non-undefined, and has non-whitespace content. */
function isNonBlank(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Strip the four launch params from the current URL while preserving any others
 * (e.g., the Xrm `data=…` envelope). Uses `history.replaceState` so neither the
 * SPA state nor the browser history stack is disturbed.
 *
 * Wrapped in try/catch because `replaceState` can throw under some sandboxed
 * iframe + cross-origin configurations; failure is non-fatal (the only
 * downside is a refresh re-triggers the wizard).
 */
function clearLaunchParams(): void {
  try {
    const url = new URL(window.location.href);
    let mutated = false;
    for (const key of Object.values(LAUNCH_PARAM_KEYS)) {
      if (url.searchParams.has(key)) {
        url.searchParams.delete(key);
        mutated = true;
      }
    }
    if (!mutated) return;

    // Reconstruct the URL (preserves path + any remaining params + hash).
    const newSearch = url.searchParams.toString();
    const newUrl = `${url.pathname}${newSearch ? `?${newSearch}` : ''}${url.hash}`;
    window.history.replaceState(null, '', newUrl);
  } catch (err) {
    // Non-fatal — refresh-safety degrades but the wizard already opened.
    console.warn('[SmartTodo] useLaunchContext: failed to clear URL params:', err);
  }
}

/**
 * Pure parser (extracted for testability). Reads from the supplied query string
 * and returns the launch context (or `undefined`). Does NOT touch `window`.
 *
 * Exported for unit tests; production code should call the `useLaunchContext`
 * hook instead.
 */
export function parseLaunchContextFromSearch(search: string): ILaunchContext | undefined {
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(search);
  } catch {
    return undefined;
  }

  const action = params.get(LAUNCH_PARAM_KEYS.ACTION);
  if (action !== LAUNCH_ACTION_CREATE_TODO) {
    return undefined;
  }

  const entityType = params.get(LAUNCH_PARAM_KEYS.REGARDING_TYPE);
  const recordId = params.get(LAUNCH_PARAM_KEYS.REGARDING_ID);
  const recordName = params.get(LAUNCH_PARAM_KEYS.REGARDING_NAME);

  // All three regarding params must be present and non-blank for a valid pre-fill.
  // Missing/blank → action still recognised (auto-open) but without initialRegarding.
  if (!isNonBlank(entityType) || !isNonBlank(recordId) || !isNonBlank(recordName)) {
    if (action === LAUNCH_ACTION_CREATE_TODO) {
      console.warn(
        '[SmartTodo] useLaunchContext: action=createTodo received but regarding params are missing/blank — wizard will open without pre-fill',
        { entityType, recordId, recordName: recordName ? `(len=${recordName.length})` : recordName },
      );
    }
    return { action: LAUNCH_ACTION_CREATE_TODO, initialRegarding: undefined };
  }

  return {
    action: LAUNCH_ACTION_CREATE_TODO,
    initialRegarding: {
      entityType: entityType.trim(),
      recordId: recordId.trim(),
      recordName: recordName.trim(),
    },
  };
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Read the SmartTodo launch URL once on mount, return the parsed context, and
 * clear the launch params from the URL so a page refresh doesn't re-trigger the
 * wizard.
 *
 * Returns `undefined` when no `action=createTodo` query param is present (normal
 * SmartTodo load — kanban renders as usual).
 *
 * Returns `{ action, initialRegarding }` when the Outlook ribbon launched the
 * page; `initialRegarding` is `undefined` if regarding params were malformed.
 *
 * @example
 * ```tsx
 * const launchContext = useLaunchContext();
 * const [wizardOpen, setWizardOpen] = React.useState(launchContext?.action === 'createTodo');
 * // ...
 * <CreateTodoWizard
 *   open={wizardOpen}
 *   initialRegarding={launchContext?.initialRegarding}
 *   onClose={() => setWizardOpen(false)}
 *   // ...
 * />
 * ```
 */
export function useLaunchContext(): ILaunchContext | undefined {
  // Compute once on first render — useMemo with empty deps so subsequent renders
  // return the same reference and the URL is not re-read. We intentionally do
  // NOT useState here because the value is a function of the initial URL, not
  // something that changes during the component's lifetime.
  //
  // We also need to clear the URL on first render — done via useEffect so it
  // runs after commit (SSR-safe, and avoids tearing the initial render).
  const launchContext = React.useMemo<ILaunchContext | undefined>(() => {
    if (typeof window === 'undefined') return undefined;
    return parseLaunchContextFromSearch(window.location.search);
  }, []);

  React.useEffect(() => {
    if (launchContext) {
      clearLaunchParams();
    }
    // Run ONCE — launchContext is memoised on first render and won't change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return launchContext;
}
