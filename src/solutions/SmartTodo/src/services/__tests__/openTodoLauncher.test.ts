/**
 * openTodoLauncher — OPEN existing sprk_todo + post-close refetch unit tests
 * (task 033, spec FR-14).
 *
 * Covers the closed acceptance set from
 * `projects/smart-todo-r5/tasks/033-saveclose-dismiss-refresh.poml` step 6 for
 * the Code Page OPEN call site:
 *   - `launchOpenTodoForm` calls `navigateToEntityRecordSurfaceAsync` with
 *     `entityName: 'sprk_todo'` and the given `entityId` (OPEN mode).
 *   - On a real dialog close (`outcome.launched === true`), the `onClose`
 *     refetch is invoked UNCONDITIONALLY — because an existing-record OPEN
 *     never resolves with `savedEntityReference` (CREATE-only, per MS Learn),
 *     so save cannot be distinguished from cancel; a redundant refetch on
 *     cancel is the documented, tolerable trade-off (missing-refetch-on-save
 *     is the failure to avoid).
 *   - When no Xrm host is reachable (`launched === false`), `onClose` is NOT
 *     invoked — nothing opened, so nothing needs refreshing.
 *
 * Test harness note: mirrors `SmartTodoApp.test.tsx` — `@spaarke/ui-components`
 * is mocked so the test imports only the small service module rather than
 * `SmartTodoApp.tsx`'s full import graph (Header, SmartToDo, SearchFilter,
 * Toolbar, TodoContext, `@spaarke/auth`, ...). This is exactly why the OPEN
 * wrapper was extracted from `SmartTodoApp.tsx` into `openTodoLauncher.ts`
 * (task 033), matching the CREATE path's `newTaskLauncher.ts` precedent.
 *
 * Classification (ADR-038 §7): MAINTAIN-class — a behavioral contract test for
 * the save-refresh wiring, the load-bearing outcome of FR-14. No
 * `Mock<HttpMessageHandler>`, no DI-registration checks, no ctor null-checks,
 * no coverage-as-gate.
 */

const mockNavigateToEntityRecordSurfaceAsync = jest.fn();

jest.mock('@spaarke/ui-components', () => ({
  navigateToEntityRecordSurfaceAsync: mockNavigateToEntityRecordSurfaceAsync,
}));

import { launchOpenTodoForm } from '../openTodoLauncher';

beforeEach(() => {
  mockNavigateToEntityRecordSurfaceAsync.mockReset();
});

describe('launchOpenTodoForm (SmartTodoApp OPEN call site)', () => {
  it('calls navigateToEntityRecordSurfaceAsync with entityName "sprk_todo" and the given entityId (OPEN mode)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true });

    await launchOpenTodoForm('todo-guid-1', jest.fn());

    expect(mockNavigateToEntityRecordSurfaceAsync).toHaveBeenCalledTimes(1);
    const callArgs = mockNavigateToEntityRecordSurfaceAsync.mock.calls[0][0];
    expect(callArgs.entityName).toBe('sprk_todo');
    expect(callArgs.entityId).toBe('todo-guid-1');
    // UAT 2026-08-18 #1 — uniform dialog title (not the record's sprk_name).
    expect(callArgs.title).toBe('Smart To Do Item');
    // UAT 2026-08-18 #3 — one size down: record (85%×85%), not fullCover (100%).
    expect(callArgs.size).toEqual({ width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' } });
  });

  it('invokes onClose (refetch) unconditionally on a clean dialog close — the OPEN Save & Close case', async () => {
    // OPEN resolves with a plain { launched: true } (no savedEntityReference,
    // no cancelled flag) whether the user saved or cancelled — refetch fires
    // regardless so a Save & Close reflects without a manual reload.
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true });
    const onClose = jest.fn();

    await launchOpenTodoForm('todo-guid-2', onClose);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('still invokes onClose on the dialog-error resolve shape ({ launched: true, cancelled: true })', async () => {
    // The shared launcher maps a rejected/erroring navigateTo promise to
    // { launched: true, cancelled: true }. OPEN gates only on `launched`, so
    // the refetch still fires (tolerable redundant refetch, never a miss).
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true, cancelled: true });
    const onClose = jest.fn();

    await launchOpenTodoForm('todo-guid-3', onClose);

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does NOT invoke onClose when navigateTo could not launch (non-host environment)', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: false });
    const onClose = jest.fn();

    await launchOpenTodoForm('todo-guid-4', onClose);

    expect(onClose).not.toHaveBeenCalled();
  });

  it('does not throw when onClose is omitted', async () => {
    mockNavigateToEntityRecordSurfaceAsync.mockResolvedValue({ launched: true });

    await expect(launchOpenTodoForm('todo-guid-5')).resolves.toBeUndefined();
  });
});
