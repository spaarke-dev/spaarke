/**
 * useKanbanColumns — Completed-item bucketing coverage (FR-07 / F-2 Show-
 * Completed toggle, smart-todo-r5 task 022).
 *
 * Jest-less pure-function smoke tests, matching this package's existing
 * `SmartTodoWidget.test.tsx` pattern — `@spaarke/smart-todo-components`
 * ships type-check only (`build: tsc --noEmit`); there is no Jest config
 * in this peer package yet (wiring it is tracked separately as
 * smart-todo-r5 task 040). These tests exercise the exported pure function
 * `bucketTodoItems` directly (no React renderer needed — the hook itself
 * wraps this pure function in `useMemo`/`useState`, so testing the pure
 * function covers the bucketing behavior under test here) so they compile
 * cleanly today and can be picked up as-is (or trivially wrapped in
 * `describe`/`it`) once Jest is wired into this package.
 *
 * Scope: this file verifies the KANBAN RENDER-SIDE guarantee that FR-07
 * requires — that a Completed (statuscode=2) item, once the query layer
 * starts returning it (see `buildTodoItemsQuery`'s `includeCompleted` param
 * in `src/solutions/SmartTodo/src/services/queryHelpers.ts`), is bucketed
 * into its normal score/due-date-based Today/Tomorrow/Future column with NO
 * special-case exclusion and NO 4th "Completed" column introduced. It does
 * NOT test `buildTodoItemsQuery` itself (that lives in a different package —
 * `src/solutions/SmartTodo` — and importing across that package boundary
 * would violate ADR-012's "no src/solutions/… reach-in" rule for this
 * shared-lib package).
 */

import { bucketTodoItems } from '../src/hooks/useKanbanColumns';
import type { IKanbanCardTodo } from '../src/types/kanban';

// Lightweight assert helpers — kept dependency-free so these compile cleanly
// even before Jest is wired into this peer package.
function assert(cond: boolean, msg: string): void {
  if (!cond) {
    // eslint-disable-next-line no-console
    console.error(`[useKanbanColumns.test] FAIL: ${msg}`);
    throw new Error(msg);
  }
}

// ---------------------------------------------------------------------------
// Fixture helpers
// ---------------------------------------------------------------------------

/** ISO date string N days offset from "now" (negative = past / overdue). */
function isoDaysFromNow(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString();
}

function makeTodo(overrides: Partial<IKanbanCardTodo> & { sprk_todoid: string }): IKanbanCardTodo {
  return {
    sprk_name: 'Untitled',
    sprk_priorityscore: 50,
    sprk_effortscore: 50,
    sprk_todopinned: false,
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Test 1: default-hidden regression — a set with no Completed items buckets
// exactly as before (3 columns, no unexpected 4th bucket, counts preserved).
// ---------------------------------------------------------------------------

export function runDefaultHiddenRegressionTest(): void {
  const items: IKanbanCardTodo[] = [
    makeTodo({ sprk_todoid: 't1', statuscode: 1, sprk_duedate: isoDaysFromNow(0) }), // Open, due today
    makeTodo({ sprk_todoid: 't2', statuscode: 659490001, sprk_duedate: isoDaysFromNow(1) }), // In Progress, due tomorrow
  ];

  const columns = bucketTodoItems(items);

  assert(columns.length === 3, 'bucketTodoItems must always return exactly 3 columns (Today/Tomorrow/Future)');
  assert(
    columns.map(c => c.id).join(',') === 'Today,Tomorrow,Future',
    'Column ids/order must be Today, Tomorrow, Future'
  );
  const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);
  assert(totalItems === 2, 'All non-Completed items must be present — no item silently dropped');
  assert(columns[0].items.some(i => i.sprk_todoid === 't1'), 'Open item due today must land in Today');
  assert(columns[1].items.some(i => i.sprk_todoid === 't2'), 'In Progress item due tomorrow must land in Tomorrow');

  // eslint-disable-next-line no-console
  console.info('[useKanbanColumns.test] Default-hidden regression test passed.');
}

// ---------------------------------------------------------------------------
// Test 2: a Completed (statuscode=2) item flows through the SAME score/due-
// date bucketing as any other item — no special-case exclusion, no 4th
// column. This is the core FR-07 render-side guarantee.
// ---------------------------------------------------------------------------

export function runCompletedItemBucketingTest(): void {
  const items: IKanbanCardTodo[] = [
    makeTodo({ sprk_todoid: 'open-today', statuscode: 1, sprk_duedate: isoDaysFromNow(0) }),
    makeTodo({ sprk_todoid: 'completed-today', statuscode: 2, sprk_duedate: isoDaysFromNow(-1) }), // overdue → Today
    makeTodo({ sprk_todoid: 'completed-future', statuscode: 2, sprk_duedate: isoDaysFromNow(10) }),
    makeTodo({ sprk_todoid: 'completed-undated', statuscode: 2 }), // no due date → Future
  ];

  const columns = bucketTodoItems(items);

  assert(columns.length === 3, 'Including Completed items must NOT introduce a 4th column');

  const today = columns.find(c => c.id === 'Today')!;
  const future = columns.find(c => c.id === 'Future')!;

  assert(
    today.items.some(i => i.sprk_todoid === 'completed-today'),
    'Completed item with an overdue due-date must bucket into Today, same as a non-completed item would'
  );
  assert(
    today.items.some(i => i.sprk_todoid === 'open-today'),
    'Completed item bucketing into Today must NOT displace a non-completed Today item (mixed-column rendering)'
  );
  assert(
    future.items.some(i => i.sprk_todoid === 'completed-future'),
    'Completed item due far out must bucket into Future, same as a non-completed item would'
  );
  assert(
    future.items.some(i => i.sprk_todoid === 'completed-undated'),
    'Completed item with no due date must default to Future, same as a non-completed item would'
  );

  const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);
  assert(totalItems === items.length, 'Every Completed item must be present in exactly one column — none dropped');

  // eslint-disable-next-line no-console
  console.info('[useKanbanColumns.test] Completed-item bucketing test passed.');
}

// ---------------------------------------------------------------------------
// Test 3: a pinned Completed item still respects its pinned column override
// (pin behavior is uniform across statuscode — no special-case guard).
// ---------------------------------------------------------------------------

export function runCompletedPinnedItemTest(): void {
  const items: IKanbanCardTodo[] = [
    makeTodo({
      sprk_todoid: 'completed-pinned-future-column',
      statuscode: 2,
      sprk_duedate: isoDaysFromNow(0), // would compute to Today by date...
      sprk_todopinned: true,
      sprk_todocolumn: 100000002, // ...but pinned explicitly to Future
    }),
  ];

  const columns = bucketTodoItems(items);
  const future = columns.find(c => c.id === 'Future')!;
  const today = columns.find(c => c.id === 'Today')!;

  assert(
    future.items.some(i => i.sprk_todoid === 'completed-pinned-future-column'),
    'Pinned Completed item must honor its pinned column override, same as a non-completed item would'
  );
  assert(
    !today.items.some(i => i.sprk_todoid === 'completed-pinned-future-column'),
    'Pinned Completed item must NOT also appear in its date-computed column'
  );

  // eslint-disable-next-line no-console
  console.info('[useKanbanColumns.test] Completed-pinned-item test passed.');
}

// ---------------------------------------------------------------------------
// Test 4: negative regression — a Dismissed-shaped item (statuscode
// 659490002) is bucketed with the same no-special-case logic. Dismissed
// items are fetched via the separate `buildDismissedTodoQuery` /
// `DismissedSection` path today and never actually reach this function in
// production, but this proves the hook applies zero statuscode
// discrimination — so the Dismissed lane cannot be broken by this task's
// change (it doesn't touch statuscode handling in this file at all).
// ---------------------------------------------------------------------------

export function runDismissedShapeUnaffectedTest(): void {
  const items: IKanbanCardTodo[] = [
    makeTodo({ sprk_todoid: 'dismissed-1', statuscode: 659490002, sprk_duedate: isoDaysFromNow(1) }),
  ];

  const columns = bucketTodoItems(items);
  const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);

  assert(totalItems === 1, 'bucketTodoItems applies no statuscode filtering — a Dismissed-shaped item still buckets');
  assert(
    columns.find(c => c.id === 'Tomorrow')!.items.some(i => i.sprk_todoid === 'dismissed-1'),
    'Dismissed-shaped item due tomorrow must bucket into Tomorrow — confirms zero statuscode special-casing'
  );

  // eslint-disable-next-line no-console
  console.info('[useKanbanColumns.test] Dismissed-shape-unaffected test passed.');
}

// Module-eval auto-run is gated to a Node.js env so test consumers can import
// this file without side effects. When the peer package gets Jest wired,
// replace this gate with `describe`/`it` blocks.
if (typeof process !== 'undefined' && process.env?.USE_KANBAN_COLUMNS_SMOKE === '1') {
  runDefaultHiddenRegressionTest();
  runCompletedItemBucketingTest();
  runCompletedPinnedItemTest();
  runDismissedShapeUnaffectedTest();
}
