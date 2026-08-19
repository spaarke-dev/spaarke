/**
 * openTodoLauncher — OPEN existing sprk_todo OOB main-form wiring + post-close
 * refetch (spec FR-14, task 033).
 *
 * Sibling of `newTaskLauncher.ts` (task 030, the CREATE path). Extracted from
 * `SmartTodoApp.tsx`'s module-scope `openSprkTodoAsLayout1` wrapper for the same
 * reason `launchNewTaskCreateForm` was extracted: so the post-close refetch
 * decision is unit-testable WITHOUT pulling `SmartTodoApp.tsx`'s full import
 * graph (Header, SmartToDo, SearchFilter, Toolbar, TodoContext, `@spaarke/auth`,
 * ...) into the test module graph. Task 033 gave this wrapper real branching
 * worth testing in isolation (the launched-vs-not gate + the unconditional
 * on-close refetch), which is what justifies the separate file per CLAUDE.md
 * §11 (it is now more than a trivial pass-through).
 *
 * ADR-050 Path A exception (per spec.md ADR Tensions + CLAUDE.md §6.5): this
 * reuses the OOB `Xrm.Navigation.navigateTo` main-form OPEN surface via the
 * existing `navigateToEntityRecordSurfaceAsync()` launcher — NOT a proprietary
 * `SprkModal`/`FormModal`. `SprkModal` does not govern OOB `navigateTo` dialogs
 * (see `docs/standards/MODAL-DECISION-CRITERIA.md`).
 *
 * Save-vs-cancel signal (task 031 step-2 finding, baked into the shared
 * launcher's doc comment — do NOT re-derive): an existing-record OPEN
 * `entityrecord` `navigateTo` NEVER resolves with `savedEntityReference` (that
 * is CREATE-only, per Microsoft Learn). So the resolve value cannot distinguish
 * Save & Close from Cancel/dismiss. Per the task's step-4 decision this launcher
 * therefore invokes `onClose` UNCONDITIONALLY once the dialog closes
 * (`outcome.launched === true`): a redundant refetch on cancel is tolerable, a
 * MISSING refetch on save is not (FR-14). The `navigateTo` promise resolves
 * AFTER Save & Close commits the write, so the refetch reads committed data — no
 * read-after-write race for the standard target:2 dialog flow. `onClose` is NOT
 * called when no Xrm host is reachable (`launched === false`) — nothing opened,
 * so nothing needs refreshing.
 *
 * @see projects/smart-todo-r5/tasks/033-saveclose-dismiss-refresh.poml
 * @see projects/smart-todo-r5/notes/task-033-saveclose-refresh.md
 */

import { navigateToEntityRecordSurfaceAsync, getOobModalSize } from '@spaarke/ui-components';

/** Entity logical name for the sprk_todo OOB main form (spec FR-11). */
const TODO_ENTITY_NAME = 'sprk_todo';

/**
 * Uniform dialog chrome title for every To Do modal (smart-todo-r5 UAT
 * 2026-08-18 item #1) — replaces the record's `sprk_name` so all To Do
 * modals read the same. Also used by the CREATE path (newTaskLauncher).
 */
const TODO_DIALOG_TITLE = 'Smart To Do Item';

/**
 * Open the sprk_todo OOB main form in OPEN-EXISTING mode as a modal (spec
 * FR-11), and invoke `onClose` after the dialog closes so the caller can
 * refetch the Kanban (spec FR-14). Refetch is UNCONDITIONAL on a real close —
 * see the module doc comment for the save-vs-cancel signal rationale.
 *
 * REUSES `navigateToEntityRecordSurfaceAsync` (per CLAUDE.md §11 / the task's
 * "MUST reuse" constraint) — no second, parallel `Xrm.Navigation.navigateTo`
 * call site.
 */
export async function launchOpenTodoForm(
  todoId: string,
  onClose?: () => void,
): Promise<void> {
  const outcome = await navigateToEntityRecordSurfaceAsync({
    entityName: TODO_ENTITY_NAME,
    entityId: todoId,
    // UAT 2026-08-18 #1 — uniform dialog title (not the record's sprk_name).
    title: TODO_DIALOG_TITLE,
    // UAT 2026-08-18 — down to createForm (70%×80%): fullCover(100%) → record(85%,
    // "not smaller enough") → createForm. (Two steps down from the original.)
    size: getOobModalSize('createForm'),
  });
  if (!outcome.launched) {
    // eslint-disable-next-line no-console
    console.warn('[SmartTodoApp] Xrm.Navigation.navigateTo unavailable; open aborted.');
    return;
  }
  // Dialog closed — refetch unconditionally (OPEN mode has no reliable
  // save-vs-cancel signal; missing-refetch-on-save is the failure to avoid).
  onClose?.();
}
