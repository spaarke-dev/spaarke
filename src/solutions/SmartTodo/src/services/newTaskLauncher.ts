/**
 * newTaskLauncher — "+ New Task" OOB create-form wiring (spec FR-10, task 030).
 *
 * Extracted from `SmartTodoApp.tsx` into its own service module (mirrors the
 * existing `queryHelpers.ts` / `TodoRegardingUpdateBuilder.ts` service-layer
 * convention) so it can be unit-tested WITHOUT pulling `SmartTodoApp.tsx`'s
 * full import graph (Header, SmartToDo, SearchFilter, Toolbar, TodoContext,
 * `@spaarke/auth`, ...) into the test module graph. `openSprkTodoAsLayout1`
 * (the sibling "open" launcher) stays inline in `SmartTodoApp.tsx` because it
 * has no non-trivial branching to unit-test in isolation; this launcher DOES
 * (the regarding-context → defaultValues mapping), which is what justifies
 * the separate file per CLAUDE.md §11.
 *
 * ADR-050 Path A exception (per spec.md ADR Tensions + CLAUDE.md §6.5): this
 * reuses the OOB `Xrm.Navigation.navigateTo` main-form CREATE surface via the
 * existing `navigateToEntityRecordSurfaceAsync()` launcher
 * (`@spaarke/ui-components` WorkspaceShell/wizardLaunchers.ts) — NOT a
 * proprietary `SprkModal`/`FormModal` config. `SprkModal` does not govern OOB
 * `navigateTo` dialogs (see `docs/standards/MODAL-DECISION-CRITERIA.md`).
 *
 * @see projects/smart-todo-r5/tasks/030-new-task-oob-mainform-modal.poml
 * @see projects/smart-todo-r5/notes/task-030-new-task-modal.md — defaultValues
 *      mapping decision + documented pre-seed gap.
 */

import {
  navigateToEntityRecordSurfaceAsync,
  TODO_REGARDING_CATALOG,
} from '@spaarke/ui-components';
import type { ILaunchContext } from '../hooks/useLaunchContext';

/** Entity logical name for the sprk_todo OOB create form (spec FR-10). */
const TODO_ENTITY_NAME = 'sprk_todo';

/** Dialog title passed to `navigateToEntityRecordSurfaceAsync`. */
const NEW_TASK_DIALOG_TITLE = 'New To Do';

/**
 * Narrow shape shared by the two launch-context branches this task reads
 * (`openTodos`'s `regardingFilter` and `createTodo`'s `initialRegarding`) —
 * both carry `{entityType, recordId, recordName?}`.
 */
interface IRegardingSource {
  entityType: string;
  recordId: string;
  recordName?: string;
}

/**
 * Resolve the regarding context the Code Page was launched with, preferring
 * the `openTodos` branch's `regardingFilter` (an ACTIVE Kanban filter — the
 * more likely scenario when the user is mid-session and clicks "+ New Task")
 * over the `createTodo` branch's `initialRegarding` (present only on the very
 * first render, before `LaunchCreateTodoWizardHost` consumes it for the
 * Outlook/parent-ribbon wizard flow this task does NOT touch).
 */
function resolveRegardingSource(
  launchContext: ILaunchContext | undefined,
): IRegardingSource | undefined {
  if (launchContext?.action === 'openTodos') {
    return launchContext.regardingFilter;
  }
  if (launchContext?.action === 'createTodo') {
    return launchContext.initialRegarding;
  }
  return undefined;
}

/**
 * Build the `defaultValues` pre-seed map for the sprk_todo OOB create form,
 * best-effort, from the Code Page's current launch/regarding context.
 *
 * Mirrors `TodoRegardingUpdateBuilder.ts`'s `TODO_REGARDING_CATALOG` for the
 * entity-specific lookup attribute name (e.g. `sprk_regardingmatter`) — does
 * NOT invent a new attribute name. Pre-seeds:
 *   1. The entity-specific single-valued lookup, using the Microsoft Learn
 *      documented `navigateTo` `data`-param lookup-default shape:
 *      `JSON.stringify([{id, entityType, name}])` (a JSON-stringified
 *      single-element array — the same shape Microsoft's own `customerid`
 *      pre-seed example uses).
 *   2. `sprk_regardingrecordid` / `sprk_regardingrecordname` — the two plain
 *      TEXT resolver fields (ADR-024) — pre-seeded directly as strings, the
 *      unambiguous case for the `data` param's documented scalar-attribute
 *      default-value support.
 *
 * DOCUMENTED GAP (per the task's escalation trigger — NOT hacked around):
 * `sprk_regardingrecordtype` (the 4th resolver field — a lookup to the
 * `sprk_recordtype_ref` reference table) is deliberately NOT pre-seeded here.
 * Its target GUID is a reference-table row keyed by entity-type NAME, not the
 * regarding record's own GUID — resolving it requires an extra Web API query
 * this client-side pre-seed step does not perform. The RegardingResolver
 * control already wired onto the sprk_todo form (task 013) is where the user
 * completes/reconciles the full regarding association after the form opens;
 * this pre-seed only gives the form a head start, it does not need to be
 * complete. The entity-specific-lookup `data`-param shape above is applied
 * per the documented Microsoft convention but has NOT been empirically
 * verified against the live `sprk_todo` form in this environment — the
 * project's UI-smoke step (POML step 5) is the verification point; if it
 * does not pre-fill, that is a documentation update, not a silent failure,
 * because the plain-create path (no defaultValues) still works unconditionally.
 *
 * Returns `undefined` when there is no regarding context, or the context's
 * `entityType` is not one of the 12 canonical sprk_todo regarding targets —
 * in both cases the caller falls back to a plain create (no defaultValues).
 */
export function buildNewTaskDefaultValues(
  launchContext: ILaunchContext | undefined,
): Record<string, unknown> | undefined {
  const regarding = resolveRegardingSource(launchContext);
  if (!regarding) return undefined;

  const catalogEntry = TODO_REGARDING_CATALOG.find((c) => c.entityType === regarding.entityType);
  if (!catalogEntry) return undefined;

  const defaultValues: Record<string, unknown> = {
    [catalogEntry.lookupAttribute]: JSON.stringify([
      { id: regarding.recordId, entityType: regarding.entityType, name: regarding.recordName ?? '' },
    ]),
    sprk_regardingrecordid: regarding.recordId,
  };
  if (regarding.recordName) {
    defaultValues.sprk_regardingrecordname = regarding.recordName;
  }
  return defaultValues;
}

/**
 * Open the sprk_todo OOB main form in CREATE mode as a modal (spec FR-10),
 * pre-seeded with `buildNewTaskDefaultValues(launchContext)`, and invoke
 * `onSaved` when the user actually saves (never on cancel/dismiss).
 *
 * REUSES `navigateToEntityRecordSurfaceAsync` (per CLAUDE.md §11 / the task's
 * "MUST reuse" constraint) — no second, parallel `Xrm.Navigation.navigateTo`
 * call site.
 */
export async function launchNewTaskCreateForm(
  launchContext: ILaunchContext | undefined,
  onSaved: () => void,
): Promise<void> {
  const defaultValues = buildNewTaskDefaultValues(launchContext);
  const outcome = await navigateToEntityRecordSurfaceAsync({
    entityName: TODO_ENTITY_NAME,
    title: NEW_TASK_DIALOG_TITLE,
    defaultValues,
  });
  if (outcome.savedEntityReference) {
    onSaved();
  }
}
