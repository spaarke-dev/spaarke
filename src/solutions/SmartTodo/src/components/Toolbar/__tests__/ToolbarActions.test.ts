/**
 * ToolbarActions.test.ts
 *
 * Unit tests for the SmartTodo selection-aware toolbar action factory (R4 task 032).
 *
 * ──────────────────────────────────────────────────────────────────────────
 * TEST-RUNNER STATUS (2026-06-10)
 *
 * SmartTodo's `package.json` does NOT currently include a test runner (no jest,
 * no vitest) — see `hooks/__tests__/useLaunchContext.test.ts` for the same
 * pre-existing situation (R4-034 outcome). These tests are written as
 * DOCUMENTATION + ASSERTION SOURCE so they activate immediately once a runner
 * is wired up.
 *
 * In the meantime, the test source itself serves as:
 *   1. An executable spec — every assertion below is a concrete behavior we
 *      want the production action factory to honour.
 *   2. A regression boundary — future edits to `ToolbarActions.ts` should
 *      change tests in this file FIRST, then the implementation, so the diff
 *      makes the behavior change reviewable.
 *
 * To run these once a test runner is wired up:
 *   • Add `vitest` (preferred) or `jest` + jsdom env to
 *     `src/solutions/SmartTodo/package.json` devDependencies.
 *   • Add a `"test"` script.
 *   • These tests use the `jest` / `vitest` global API (`describe`/`it`/`expect`)
 *     which both runners expose by default.
 * ──────────────────────────────────────────────────────────────────────────
 *
 * Scenarios covered (mirror the POML acceptance criteria):
 *
 *   Open:
 *     (a) Empty selection → succeeded:0, no event dispatched.
 *     (b) ≥1 selected → dispatches `OPEN_TODOS_EVENT` with selectedIds + firstId.
 *
 *   Delete:
 *     (a) Empty selection → noop.
 *     (b) User cancels confirm → noop (no deleteRecord calls).
 *     (c) User confirms → all selected records deleted in parallel.
 *     (d) Partial failure → partial succeeded/failed counts; onAfterMutate
 *         still invoked; onClearSelection still invoked.
 *     (e) webApi null → fails gracefully without throwing.
 *
 *   Email:
 *     (a) Empty selection → noop.
 *     (b) ≥1 selected → sets window.location.href to a `mailto:` with
 *         encoded subject + body containing each todo's name + due date.
 *
 *   Pin:
 *     (a) Empty selection → noop.
 *     (b) All selected pinned → unpins all (sprk_todopinned: false).
 *     (c) Any selected unpinned → pins all (sprk_todopinned: true).
 *     (d) webApi null → fails gracefully without throwing.
 *
 * @see ../ToolbarActions.ts — the implementation under test
 * @see smart-todo-r4 spec FR-08
 */

import {
  createToolbarActions,
  resolveSelectedTodos,
  OPEN_TODOS_EVENT,
  type ITodoActionWebApi,
  type ToolbarActionContext,
} from '../ToolbarActions';
import type { ITodo } from '../../../types/entities';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const makeTodo = (overrides: Partial<ITodo> = {}): ITodo =>
  ({
    sprk_todoid: 'todo-1',
    sprk_name: 'Sample To-Do',
    statuscode: 1,
    statecode: 0,
    sprk_todopinned: false,
    ...overrides,
  }) as ITodo;

const makeMockWebApi = (): jest.Mocked<ITodoActionWebApi> & {
  deleteRecord: jest.Mock;
  updateRecord: jest.Mock;
} => ({
  deleteRecord: jest.fn().mockResolvedValue(undefined),
  updateRecord: jest.fn().mockResolvedValue(undefined),
}) as jest.Mocked<ITodoActionWebApi> & { deleteRecord: jest.Mock; updateRecord: jest.Mock };

const makeCtx = (overrides: Partial<ToolbarActionContext> = {}): ToolbarActionContext => ({
  webApi: makeMockWebApi(),
  getSelectedTodos: () => [],
  onAfterMutate: jest.fn(),
  onClearSelection: jest.fn(),
  confirm: () => true, // auto-confirm in tests unless overridden
  ...overrides,
});

// ---------------------------------------------------------------------------
// resolveSelectedTodos
// ---------------------------------------------------------------------------

describe('resolveSelectedTodos', () => {
  it('returns empty array when selectedIds is empty', () => {
    const items = [makeTodo({ sprk_todoid: 'a' }), makeTodo({ sprk_todoid: 'b' })];
    const result = resolveSelectedTodos(new Set(), items);
    expect(result).toEqual([]);
  });

  it('returns only the items matching selectedIds', () => {
    const items = [
      makeTodo({ sprk_todoid: 'a', sprk_name: 'A' }),
      makeTodo({ sprk_todoid: 'b', sprk_name: 'B' }),
      makeTodo({ sprk_todoid: 'c', sprk_name: 'C' }),
    ];
    const result = resolveSelectedTodos(new Set(['a', 'c']), items);
    expect(result).toHaveLength(2);
    expect(result.map(r => r.sprk_todoid)).toEqual(['a', 'c']);
  });

  it('skips selection ids that aren\'t in items (stale selection tolerance)', () => {
    const items = [makeTodo({ sprk_todoid: 'a' })];
    const result = resolveSelectedTodos(new Set(['a', 'b-stale']), items);
    expect(result).toHaveLength(1);
  });
});

// ---------------------------------------------------------------------------
// Open
// ---------------------------------------------------------------------------

describe('handleOpen', () => {
  it('returns succeeded:0 and dispatches no event when no items selected', () => {
    const dispatchSpy = jest.spyOn(window, 'dispatchEvent');
    const { handleOpen } = createToolbarActions(makeCtx({ getSelectedTodos: () => [] }));
    const result = handleOpen();
    expect(result.succeeded).toBe(0);
    expect(result.failed).toBe(0);
    expect(dispatchSpy).not.toHaveBeenCalled();
    dispatchSpy.mockRestore();
  });

  it('dispatches OPEN_TODOS_EVENT with selectedIds + firstId when ≥1 selected', () => {
    const dispatched: Event[] = [];
    const dispatchSpy = jest
      .spyOn(window, 'dispatchEvent')
      .mockImplementation(e => {
        dispatched.push(e);
        return true;
      });
    const selected = [
      makeTodo({ sprk_todoid: 'todo-a' }),
      makeTodo({ sprk_todoid: 'todo-b' }),
    ];
    const { handleOpen } = createToolbarActions(makeCtx({ getSelectedTodos: () => selected }));
    const result = handleOpen();
    expect(result.succeeded).toBe(2);
    expect(result.failed).toBe(0);
    expect(dispatched).toHaveLength(1);
    expect(dispatched[0].type).toBe(OPEN_TODOS_EVENT);
    const detail = (dispatched[0] as CustomEvent<{ selectedIds: string[]; firstId: string }>).detail;
    expect(detail.selectedIds).toEqual(['todo-a', 'todo-b']);
    expect(detail.firstId).toBe('todo-a');
    dispatchSpy.mockRestore();
  });
});

// ---------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------

describe('handleDelete', () => {
  it('returns noop when no items selected', async () => {
    const ctx = makeCtx({ getSelectedTodos: () => [] });
    const { handleDelete } = createToolbarActions(ctx);
    const result = await handleDelete();
    expect(result.succeeded).toBe(0);
    expect(result.failed).toBe(0);
    expect((ctx.webApi as jest.Mocked<ITodoActionWebApi>).deleteRecord).not.toHaveBeenCalled();
  });

  it('returns noop and does not delete when user cancels confirm', async () => {
    const selected = [makeTodo({ sprk_todoid: 'todo-a' })];
    const ctx = makeCtx({
      getSelectedTodos: () => selected,
      confirm: () => false,
    });
    const { handleDelete } = createToolbarActions(ctx);
    const result = await handleDelete();
    expect(result.succeeded).toBe(0);
    expect((ctx.webApi as jest.Mocked<ITodoActionWebApi>).deleteRecord).not.toHaveBeenCalled();
    expect(ctx.onClearSelection).not.toHaveBeenCalled();
  });

  it('deletes all selected records in parallel + refreshes + clears selection on full success', async () => {
    const selected = [
      makeTodo({ sprk_todoid: 'a' }),
      makeTodo({ sprk_todoid: 'b' }),
      makeTodo({ sprk_todoid: 'c' }),
    ];
    const webApi = makeMockWebApi();
    const onAfterMutate = jest.fn();
    const onClearSelection = jest.fn();
    const ctx = makeCtx({
      webApi,
      getSelectedTodos: () => selected,
      onAfterMutate,
      onClearSelection,
    });
    const { handleDelete } = createToolbarActions(ctx);
    const result = await handleDelete();
    expect(result.succeeded).toBe(3);
    expect(result.failed).toBe(0);
    expect(webApi.deleteRecord).toHaveBeenCalledTimes(3);
    expect(webApi.deleteRecord).toHaveBeenCalledWith('sprk_todo', 'a');
    expect(webApi.deleteRecord).toHaveBeenCalledWith('sprk_todo', 'b');
    expect(webApi.deleteRecord).toHaveBeenCalledWith('sprk_todo', 'c');
    expect(onClearSelection).toHaveBeenCalled();
    expect(onAfterMutate).toHaveBeenCalled();
  });

  it('reports partial failure and still invokes onAfterMutate + onClearSelection', async () => {
    const selected = [
      makeTodo({ sprk_todoid: 'ok' }),
      makeTodo({ sprk_todoid: 'fail' }),
    ];
    const webApi: ITodoActionWebApi = {
      deleteRecord: jest.fn().mockImplementation((_e: string, id: string) =>
        id === 'fail' ? Promise.reject(new Error('boom')) : Promise.resolve(undefined),
      ),
      updateRecord: jest.fn(),
    };
    const onAfterMutate = jest.fn();
    const onClearSelection = jest.fn();
    const ctx = makeCtx({
      webApi,
      getSelectedTodos: () => selected,
      onAfterMutate,
      onClearSelection,
    });
    const { handleDelete } = createToolbarActions(ctx);
    const result = await handleDelete();
    expect(result.succeeded).toBe(1);
    expect(result.failed).toBe(1);
    expect(result.message).toContain('boom');
    expect(onClearSelection).toHaveBeenCalled();
    expect(onAfterMutate).toHaveBeenCalled();
  });

  it('fails gracefully when webApi is null', async () => {
    const selected = [makeTodo({ sprk_todoid: 'a' })];
    const ctx = makeCtx({ webApi: null, getSelectedTodos: () => selected });
    const { handleDelete } = createToolbarActions(ctx);
    const result = await handleDelete();
    expect(result.succeeded).toBe(0);
    expect(result.failed).toBe(1);
    expect(result.message).toMatch(/not available/i);
  });
});

// ---------------------------------------------------------------------------
// Email
// ---------------------------------------------------------------------------

describe('handleEmail', () => {
  it('returns noop when no items selected', () => {
    const { handleEmail } = createToolbarActions(makeCtx({ getSelectedTodos: () => [] }));
    const result = handleEmail();
    expect(result.succeeded).toBe(0);
    expect(result.failed).toBe(0);
  });

  it('composes a mailto: with encoded subject + body containing names + due dates', () => {
    // Stub window.location.href assignment via Object.defineProperty.
    let assignedHref = '';
    const originalLocation = window.location;
    delete (window as unknown as { location?: Location }).location;
    (window as unknown as { location: Partial<Location> }).location = {
      set href(v: string) {
        assignedHref = v;
      },
      get href() {
        return assignedHref;
      },
    };

    try {
      const selected = [
        makeTodo({ sprk_todoid: 'a', sprk_name: 'Review draft', sprk_duedate: '2026-06-12T00:00:00Z' }),
        makeTodo({ sprk_todoid: 'b', sprk_name: 'Call client', sprk_duedate: undefined }),
      ];
      const { handleEmail } = createToolbarActions(makeCtx({ getSelectedTodos: () => selected }));
      const result = handleEmail();
      expect(result.succeeded).toBe(2);
      expect(result.failed).toBe(0);
      expect(assignedHref).toMatch(/^mailto:\?/);
      expect(assignedHref).toContain(encodeURIComponent('To-Dos: 2 selected'));
      expect(assignedHref).toContain(encodeURIComponent('Review draft'));
      expect(assignedHref).toContain(encodeURIComponent('Call client'));
      // The second todo has no due date — should NOT have a "(due …)" suffix.
      expect(assignedHref).toContain(encodeURIComponent('- Call client'));
    } finally {
      (window as unknown as { location: Location }).location = originalLocation;
    }
  });
});

// ---------------------------------------------------------------------------
// Pin
// ---------------------------------------------------------------------------

describe('handlePin', () => {
  it('returns noop when no items selected', async () => {
    const { handlePin } = createToolbarActions(makeCtx({ getSelectedTodos: () => [] }));
    const result = await handlePin();
    expect(result.succeeded).toBe(0);
  });

  it('PINS all when any selected is unpinned (mixed → all-pinned)', async () => {
    const selected = [
      makeTodo({ sprk_todoid: 'a', sprk_todopinned: true }),
      makeTodo({ sprk_todoid: 'b', sprk_todopinned: false }),
    ];
    const webApi = makeMockWebApi();
    const ctx = makeCtx({ webApi, getSelectedTodos: () => selected });
    const { handlePin } = createToolbarActions(ctx);
    const result = await handlePin();
    expect(result.succeeded).toBe(2);
    expect(webApi.updateRecord).toHaveBeenCalledWith('sprk_todo', 'a', { sprk_todopinned: true });
    expect(webApi.updateRecord).toHaveBeenCalledWith('sprk_todo', 'b', { sprk_todopinned: true });
  });

  it('UNPINS all when every selected is already pinned', async () => {
    const selected = [
      makeTodo({ sprk_todoid: 'a', sprk_todopinned: true }),
      makeTodo({ sprk_todoid: 'b', sprk_todopinned: true }),
    ];
    const webApi = makeMockWebApi();
    const ctx = makeCtx({ webApi, getSelectedTodos: () => selected });
    const { handlePin } = createToolbarActions(ctx);
    const result = await handlePin();
    expect(result.succeeded).toBe(2);
    expect(webApi.updateRecord).toHaveBeenCalledWith('sprk_todo', 'a', { sprk_todopinned: false });
    expect(webApi.updateRecord).toHaveBeenCalledWith('sprk_todo', 'b', { sprk_todopinned: false });
  });

  it('refreshes after mutate even on partial failure', async () => {
    const selected = [
      makeTodo({ sprk_todoid: 'ok', sprk_todopinned: false }),
      makeTodo({ sprk_todoid: 'fail', sprk_todopinned: false }),
    ];
    const webApi: ITodoActionWebApi = {
      deleteRecord: jest.fn(),
      updateRecord: jest.fn().mockImplementation((_e: string, id: string) =>
        id === 'fail' ? Promise.reject(new Error('boom')) : Promise.resolve(undefined),
      ),
    };
    const onAfterMutate = jest.fn();
    const ctx = makeCtx({ webApi, getSelectedTodos: () => selected, onAfterMutate });
    const { handlePin } = createToolbarActions(ctx);
    const result = await handlePin();
    expect(result.succeeded).toBe(1);
    expect(result.failed).toBe(1);
    expect(onAfterMutate).toHaveBeenCalled();
  });

  it('fails gracefully when webApi is null', async () => {
    const selected = [makeTodo()];
    const ctx = makeCtx({ webApi: null, getSelectedTodos: () => selected });
    const { handlePin } = createToolbarActions(ctx);
    const result = await handlePin();
    expect(result.succeeded).toBe(0);
    expect(result.failed).toBe(1);
    expect(result.message).toMatch(/not available/i);
  });
});
