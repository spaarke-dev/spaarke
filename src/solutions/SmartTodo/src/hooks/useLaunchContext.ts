/**
 * useLaunchContext — Parse the SmartTodo Code Page launch URL and return a
 * discriminated union describing what triggered the launch:
 *
 *   • `{ action: 'createTodo', initialRegarding }` — Outlook ribbon "Create To Do"
 *     flow (R3 task 070b). Triggers auto-open of `<CreateTodoWizard>` with the
 *     supplied regarding triple pre-filled.
 *   • `{ action: 'openTodos', regardingFilter }`  — Visual Host card drill-through
 *     (R4 FR-34 / task 034). Triggers the Kanban to pre-filter to the parent
 *     record.
 *   • `undefined` — Normal SmartTodo load (no launch context); Kanban renders
 *     unfiltered, no wizard auto-opens.
 *
 * Then clears the launch params from the URL so a refresh doesn't re-trigger the
 * same action.
 *
 * Why this exists
 * ──────────────
 * The Outlook add-in's "Create To Do" ribbon (R3 task 070) opens the SmartTodo
 * Code Page with a query string carrying a `createTodo` action and a
 * `sprk_communication` regarding triple. R3 task 070b shipped the parser. R4
 * task 034 extends it to also recognise Visual Host drill-through (FR-34): when
 * a chart-def-driven `Xrm.Navigation.navigateTo({pageType: 'webresource',
 * webresourceName: 'sprk_smarttodo.html', data: 'entityName=...&filterField=...
 * &filterValue=...&mode=dialog'}, {target: 2, ...})` opens this page, the
 * `data=` envelope is decoded and translated into an `openTodos` launch
 * context. The same hook handles BOTH flows so launch-context parsing stays in
 * one place.
 *
 * Contract (binding — read alongside)
 * ───────────────────────────────────
 *   • R3 createTodo launch contract: `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`
 *   • R4-003 drill-through spike:    `projects/smart-todo-r4/notes/drill-through-spike.md`
 *   • R4-004 hook-evolution decision: `projects/smart-todo-r4/notes/launch-context-decision.md`
 *   • R3 URL builder side:           `src/client/office-addins/shared/taskpane/services/createTodoLauncher.ts`
 *   • R4 Visual Host emitter:        `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx`
 *
 * URL params consumed
 * ───────────────────
 * **createTodo branch** (raw query, from Outlook ribbon):
 *   • `action=createTodo` — discriminator (REQUIRED)
 *   • `regardingType`     — Dataverse logical name (e.g., `sprk_communication`)
 *   • `regardingId`       — GUID (lowercased, no braces)
 *   • `regardingName`     — display name (typically the email subject)
 *
 * **openTodos branch** (two recognised wire formats):
 *
 *   1. Explicit raw query (e.g., direct test URL or future caller):
 *      `?action=openTodos&regardingType=sprk_matter&regardingId=<guid>&regardingName=<opt>`
 *
 *   2. Visual Host auto-inject keys (FR-34 — the production path), with or
 *      without `?data=<envelope>` wrapping:
 *      • `entityName=sprk_todo`           — ignored (target entity is implied)
 *      • `filterField=sprk_regarding<X>`  — drives `regardingFilter.entityType`
 *                                            (strip `sprk_regarding` prefix → `<X>`;
 *                                            then prepend `sprk_` → `sprk_<X>`)
 *      • `filterValue=<guid>`             — `regardingFilter.recordId`
 *      • `mode=dialog`                    — ignored (just signals VH-context)
 *
 *      No explicit `action` param is required for this format — the hook infers
 *      `openTodos` when `filterField` + `filterValue` are present.
 *
 * Behavior
 * ────────
 *   1. Reads `window.location.search` ONCE on mount via `parseDataParams`,
 *      which transparently handles both raw and `?data=<envelope>` wire
 *      formats (per Spaarke Code Page convention — ADR-026).
 *   2. Recognises the launch contract per priority order:
 *      a. Explicit `action=createTodo` + valid regarding triple → createTodo.
 *      b. Explicit `action=openTodos`  + regardingType/Id        → openTodos.
 *      c. VisualHost auto-inject (`filterField` + `filterValue` present;
 *         `action` absent)                                       → openTodos.
 *   3. If `action` is `createTodo` but regarding params are missing/blank:
 *        → returns `{ action: 'createTodo', initialRegarding: undefined }`
 *          (graceful degrade, logs a console warning). Wizard still auto-opens.
 *   4. If `openTodos` is inferred but regarding info is incomplete:
 *        → returns `undefined` (no graceful degrade — without a target record
 *          the filter is meaningless).
 *   5. Action is unknown (e.g., `?action=foo`): returns `undefined`.
 *   6. After the read, clears the recognised launch keys (raw + envelope-derived)
 *      via `history.replaceState`, preserving any other query string keys.
 *
 * Product-portability
 * ───────────────────
 * Pure browser APIs only (`window.location` / `URLSearchParams` /
 * `history.replaceState`) plus the `@spaarke/ui-components` `parseDataParams`
 * utility. No hardcoded URLs.
 *
 * @see projects/smart-todo-r4/tasks/034-B-extend-useLaunchContext.poml
 * @see projects/smart-todo-r4/notes/launch-context-decision.md
 */

import * as React from 'react';
import { parseDataParams } from '@spaarke/ui-components/utils';

// ---------------------------------------------------------------------------
// Constants — MUST match createTodoLauncher.ts (R3) + VisualHostRoot.tsx (R4)
// ---------------------------------------------------------------------------

/** Action discriminator value indicating the wizard should auto-open (R3). */
export const LAUNCH_ACTION_CREATE_TODO = 'createTodo';

/**
 * Action discriminator value indicating the Kanban should pre-filter to a
 * specific parent record (R4 FR-34 — Visual Host drill-through).
 */
export const LAUNCH_ACTION_OPEN_TODOS = 'openTodos';

/**
 * Query-parameter keys consumed by this hook for the explicit (raw) wire format.
 * Binding contract shared with `createTodoLauncher.ts` (R3 task 070).
 */
export const LAUNCH_PARAM_KEYS = {
  ACTION: 'action',
  REGARDING_TYPE: 'regardingType',
  REGARDING_ID: 'regardingId',
  REGARDING_NAME: 'regardingName',
} as const;

/**
 * Query-parameter keys auto-injected by VisualHost when its chart-def
 * `sprk_drillthroughtarget` points at a web resource (per R4-003 spike §3).
 * The hook recognises these in addition to the explicit keys above; they
 * arrive either as raw `?entityName=...` keys OR wrapped inside a `?data=
 * <urlencoded>` envelope (per ADR-026 Code Page convention).
 *
 * Binding contract shared with `src/client/pcf/VisualHost/control/components/
 * VisualHostRoot.tsx` (`handleExpandClick`).
 */
export const VISUAL_HOST_PARAM_KEYS = {
  /** Target entity (e.g., `'sprk_todo'`) — ignored by this hook (Kanban is implied). */
  ENTITY_NAME: 'entityName',
  /** Lookup field name on the target entity (e.g., `'sprk_regardingmatter'`). */
  FILTER_FIELD: 'filterField',
  /** Parent record GUID (no braces). */
  FILTER_VALUE: 'filterValue',
  /** VisualHost signal — always `'dialog'`; ignored by this hook. */
  MODE: 'mode',
} as const;

/**
 * Prefix used on VisualHost `filterField` values to encode the regarding entity
 * type. e.g., `sprk_regardingmatter` → strip prefix → `matter` → prepend
 * `sprk_` → `sprk_matter` (the Dataverse logical name).
 */
const REGARDING_FIELD_PREFIX = 'sprk_regarding';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Regarding triple for the `createTodo` branch — mirrors `AssociationResult`
 * from `@spaarke/ui-components` (the wizard's `initialRegarding` prop).
 * Inlined to avoid an unnecessary import since this hook is pure URL parsing.
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
 * Regarding filter for the `openTodos` branch — used by the Kanban consumer
 * (see R4 task 030) to scope `useTodoItems` to a specific parent record.
 *
 * Unlike `ILaunchRegarding`, `recordName` is OPTIONAL because VisualHost
 * doesn't have access to the parent record's display name at drill-through
 * time (chart-def context is field-only). The Kanban can render a fallback
 * header like "To Dos for [entityType] [recordId]" if it needs one.
 */
export interface ILaunchRegardingFilter {
  /** Dataverse logical name of the parent (e.g., `'sprk_matter'`). REQUIRED. */
  entityType: string;
  /** Parent record GUID. REQUIRED. */
  recordId: string;
  /** Display name — OPTIONAL; VisualHost may not have it. */
  recordName?: string;
}

/**
 * Launch context for the R3 Outlook ribbon "Create To Do" flow (UNCHANGED).
 */
export interface ICreateTodoLaunchContext {
  action: typeof LAUNCH_ACTION_CREATE_TODO;
  /**
   * Pre-filled regarding triple from the URL. `undefined` when the action is
   * present but the regarding params are missing/blank (graceful degrade —
   * wizard auto-opens with no pre-fill).
   */
  initialRegarding: ILaunchRegarding | undefined;
}

/**
 * Launch context for the R4 FR-34 Visual Host drill-through flow (NEW).
 */
export interface IOpenTodosLaunchContext {
  action: typeof LAUNCH_ACTION_OPEN_TODOS;
  /** Parent-record filter — Kanban scopes to this regarding lookup. */
  regardingFilter: ILaunchRegardingFilter;
}

/**
 * Parsed launch context returned by `useLaunchContext`. `undefined` indicates
 * the Code Page was loaded without a recognised launch action — SmartTodo
 * renders normally.
 */
export type ILaunchContext = ICreateTodoLaunchContext | IOpenTodosLaunchContext;

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Returns `true` when the string is non-null, non-undefined, and has non-whitespace content. */
function isNonBlank(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Derive the Dataverse regarding entity logical name from a VisualHost
 * `filterField` value.
 *
 * Convention (per R4-003 spike §5.4 + Visual Host `sprk_contextfieldname`):
 *   `sprk_regardingmatter`  → `sprk_matter`
 *   `sprk_regardingproject` → `sprk_project`
 *   `sprk_regardinginvoice` → `sprk_invoice`
 *   `sprk_regardingworkassignment` → `sprk_workassignment`
 *
 * Returns `undefined` if the input doesn't match the expected prefix
 * pattern — in which case the openTodos branch falls back to `undefined`.
 */
function deriveEntityTypeFromFilterField(filterField: string): string | undefined {
  const lowered = filterField.toLowerCase();
  if (!lowered.startsWith(REGARDING_FIELD_PREFIX)) return undefined;
  const tail = lowered.slice(REGARDING_FIELD_PREFIX.length).trim();
  if (!tail) return undefined;
  return `sprk_${tail}`;
}

/**
 * Strip the recognised launch keys from the current URL while preserving any
 * unrecognised ones. Uses `history.replaceState` so neither the SPA state nor
 * the browser history stack is disturbed.
 *
 * Cleared keys:
 *   • Raw: `action`, `regardingType`, `regardingId`, `regardingName`
 *   • Raw VisualHost: `entityName`, `filterField`, `filterValue`, `mode`
 *   • Envelope: the whole `data=` param (since its decoded contents are
 *     known launch keys; if a future caller needs to preserve non-launch
 *     keys inside the envelope, this can be refined to re-encode the
 *     residue)
 *
 * Wrapped in try/catch because `replaceState` can throw under some sandboxed
 * iframe + cross-origin configurations; failure is non-fatal.
 */
function clearLaunchParams(): void {
  try {
    const url = new URL(window.location.href);
    let mutated = false;

    const keysToClear = [
      ...Object.values(LAUNCH_PARAM_KEYS),
      ...Object.values(VISUAL_HOST_PARAM_KEYS),
      'data', // the envelope wrapper itself (R4 task 034)
    ];

    for (const key of keysToClear) {
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
    // Non-fatal — refresh-safety degrades but the wizard/filter already applied.
    console.warn('[SmartTodo] useLaunchContext: failed to clear URL params:', err);
  }
}

/**
 * Build the `createTodo` branch (R3 logic — preserved verbatim except for
 * the underlying param source, which now flows through `parseDataParams`).
 */
function buildCreateTodoContext(params: Record<string, string>): ICreateTodoLaunchContext {
  const entityType = params[LAUNCH_PARAM_KEYS.REGARDING_TYPE];
  const recordId = params[LAUNCH_PARAM_KEYS.REGARDING_ID];
  const recordName = params[LAUNCH_PARAM_KEYS.REGARDING_NAME];

  if (!isNonBlank(entityType) || !isNonBlank(recordId) || !isNonBlank(recordName)) {
    console.warn(
      '[SmartTodo] useLaunchContext: action=createTodo received but regarding params are missing/blank — wizard will open without pre-fill',
      {
        entityType,
        recordId,
        recordName: recordName ? `(len=${recordName.length})` : recordName,
      },
    );
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

/**
 * Build the `openTodos` branch. Recognises both:
 *   • Explicit raw form: `regardingType` + `regardingId` (+ optional `regardingName`)
 *   • VisualHost form:   `filterField` + `filterValue`
 *
 * Explicit raw values take precedence when both are present.
 *
 * Returns `undefined` when neither form yields a valid (entityType, recordId)
 * pair — in that case `openTodos` is not a viable launch action.
 */
function buildOpenTodosContext(
  params: Record<string, string>,
): IOpenTodosLaunchContext | undefined {
  // Explicit raw form first
  const rawType = params[LAUNCH_PARAM_KEYS.REGARDING_TYPE];
  const rawId = params[LAUNCH_PARAM_KEYS.REGARDING_ID];
  const rawName = params[LAUNCH_PARAM_KEYS.REGARDING_NAME];

  let entityType: string | undefined;
  let recordId: string | undefined;
  let recordName: string | undefined;

  if (isNonBlank(rawType) && isNonBlank(rawId)) {
    entityType = rawType.trim();
    recordId = rawId.trim();
    if (isNonBlank(rawName)) recordName = rawName.trim();
  } else {
    // VisualHost auto-inject form
    const filterField = params[VISUAL_HOST_PARAM_KEYS.FILTER_FIELD];
    const filterValue = params[VISUAL_HOST_PARAM_KEYS.FILTER_VALUE];

    if (isNonBlank(filterField) && isNonBlank(filterValue)) {
      const derived = deriveEntityTypeFromFilterField(filterField.trim());
      if (derived) {
        entityType = derived;
        recordId = filterValue.trim();
        // recordName intentionally absent — VisualHost doesn't provide it
      }
    }
  }

  if (!entityType || !recordId) {
    console.warn(
      '[SmartTodo] useLaunchContext: openTodos action recognised but regarding info incomplete — falling back to normal load',
      { entityType, recordId },
    );
    return undefined;
  }

  return {
    action: LAUNCH_ACTION_OPEN_TODOS,
    regardingFilter: {
      entityType,
      recordId,
      ...(recordName ? { recordName } : {}),
    },
  };
}

/**
 * Detect whether the parsed params look like a VisualHost auto-inject without
 * an explicit `action` param. `filterField` + `filterValue` are the marker
 * keys per the R4-003 spike.
 */
function looksLikeVisualHostAutoInject(params: Record<string, string>): boolean {
  return (
    isNonBlank(params[VISUAL_HOST_PARAM_KEYS.FILTER_FIELD]) &&
    isNonBlank(params[VISUAL_HOST_PARAM_KEYS.FILTER_VALUE])
  );
}

/**
 * Pure parser (extracted for testability). Reads from the supplied query string
 * and returns the launch context (or `undefined`). Does NOT touch `window`.
 *
 * Exported for unit tests; production code should call the `useLaunchContext`
 * hook instead.
 */
export function parseLaunchContextFromSearch(search: string): ILaunchContext | undefined {
  let params: Record<string, string>;
  try {
    params = parseDataParams(search);
  } catch {
    return undefined;
  }

  const action = params[LAUNCH_PARAM_KEYS.ACTION];

  if (action === LAUNCH_ACTION_CREATE_TODO) {
    return buildCreateTodoContext(params);
  }

  if (action === LAUNCH_ACTION_OPEN_TODOS) {
    return buildOpenTodosContext(params);
  }

  // No explicit action: VisualHost auto-inject form (FR-34) — infer openTodos.
  if (!isNonBlank(action) && looksLikeVisualHostAutoInject(params)) {
    return buildOpenTodosContext(params);
  }

  // Unknown action or no launch keys — caller renders normally.
  return undefined;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Read the SmartTodo launch URL once on mount, return the parsed launch
 * context (or `undefined`), and clear the launch params from the URL so a
 * page refresh doesn't re-trigger the same action.
 *
 * Returns `undefined` when no recognised launch action is present (normal
 * SmartTodo load — kanban renders unfiltered, no wizard auto-opens).
 *
 * @example
 * ```tsx
 * const launchContext = useLaunchContext();
 *
 * // createTodo branch (R3 — Outlook ribbon)
 * if (launchContext?.action === 'createTodo') {
 *   // launchContext.initialRegarding may be undefined (graceful degrade)
 *   openCreateWizard(launchContext.initialRegarding);
 * }
 *
 * // openTodos branch (R4 FR-34 — Visual Host drill-through)
 * if (launchContext?.action === 'openTodos') {
 *   // launchContext.regardingFilter is always populated
 *   applyKanbanFilter(launchContext.regardingFilter);
 * }
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
