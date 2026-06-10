/**
 * SmartTodoWidget — query-builder smoke tests.
 *
 * Run-time render tests (loading / empty / populated / error) require Jest +
 * jsdom + Fluent v9 test setup; the `@spaarke/smart-todo-components` peer
 * package ships type-check only (`build: tsc --noEmit`) — there is no Jest
 * config in this initial 0.1.0 release. These tests are kept in source as
 * pure-function exercises against `buildSmartTodoQuery`, so they can be
 * picked up later either by the peer package (when it gets Jest config) or
 * inlined into the LW shim's existing Jest run.
 *
 * R4 spec FR-02 acceptance:
 *   - Zero `sprk_event` references in the emitted query.
 *   - Zero `sprk_todoflag` references in the emitted query.
 *   - statuscode filter pinned to {Open=1, In Progress=659490001}.
 *   - statecode pinned to 0 (Active).
 *   - Regarding context filter, when supplied, takes precedence over the
 *     owner clause.
 *
 * Sample expected URL fragment (manual verification):
 *   `?$select=sprk_todoid,sprk_name,...&$filter=_sprk_regardingmatter_value
 *    %20eq%20<matterId>%20and%20statecode%20eq%200%20and%20(statuscode%20eq
 *    %201%20or%20statuscode%20eq%20659490001)&$orderby=...&$top=100`
 */

import {
  buildSmartTodoQuery,
  TODO_STATUSCODE_OPEN,
  TODO_STATUSCODE_IN_PROGRESS,
} from '../src/widgets/SmartTodoWidget/SmartTodoWidget';

// Lightweight assert helpers — kept dependency-free so these compile cleanly
// even before Jest is wired into this peer package.
function assert(cond: boolean, msg: string): void {
  if (!cond) {
    // eslint-disable-next-line no-console
    console.error(`[SmartTodoWidget.test] FAIL: ${msg}`);
    throw new Error(msg);
  }
}

export function runQueryBuilderSmokeTests(): void {
  // ── Test 1: zero legacy references ────────────────────────────────────────
  const q1 = buildSmartTodoQuery({ userId: 'u1' });
  assert(!q1.includes('sprk_event'), 'Query must not reference sprk_event');
  assert(!q1.includes('sprk_todoflag'), 'Query must not reference sprk_todoflag');

  // ── Test 2: statuscode + statecode pinned ─────────────────────────────────
  const decoded1 = decodeURIComponent(q1);
  assert(
    decoded1.includes(`statuscode eq ${TODO_STATUSCODE_OPEN}`),
    'Query must filter statuscode eq Open(1)'
  );
  assert(
    decoded1.includes(`statuscode eq ${TODO_STATUSCODE_IN_PROGRESS}`),
    'Query must filter statuscode eq InProgress(659490001)'
  );
  assert(decoded1.includes('statecode eq 0'), 'Query must filter statecode eq 0');

  // ── Test 3: regarding context filter takes precedence ─────────────────────
  const q2 = buildSmartTodoQuery({
    userId: 'u1',
    regardingContext: { entityLogicalName: 'sprk_matter', recordId: 'M1' },
  });
  const decoded2 = decodeURIComponent(q2);
  assert(
    decoded2.includes('_sprk_regardingmatter_value eq M1'),
    'Query must include regarding-matter filter when context is sprk_matter'
  );
  assert(
    !decoded2.includes('_ownerid_value'),
    'Owner clause must NOT be emitted when regarding context supplied'
  );

  // ── Test 4: owner clause when no regarding context ────────────────────────
  const q3 = buildSmartTodoQuery({ userId: 'u1' });
  const decoded3 = decodeURIComponent(q3);
  assert(
    decoded3.includes('_ownerid_value eq u1'),
    'Owner clause must be emitted when no regarding context'
  );

  // ── Test 5: scope=all uses business-unit OR ───────────────────────────────
  const q4 = buildSmartTodoQuery({
    userId: 'u1',
    scope: 'all',
    businessUnitId: 'bu1',
  });
  const decoded4 = decodeURIComponent(q4);
  assert(
    decoded4.includes('_owningbusinessunit_value eq bu1'),
    "Scope='all' must include business-unit OR clause"
  );

  // ── Test 6: select projection has the required fields ─────────────────────
  assert(q1.includes('sprk_todoid'), 'Select must include sprk_todoid');
  assert(q1.includes('sprk_name'), 'Select must include sprk_name');
  assert(q1.includes('sprk_duedate'), 'Select must include sprk_duedate');
  assert(q1.includes('statuscode'), 'Select must include statuscode');

  // eslint-disable-next-line no-console
  console.info('[SmartTodoWidget.test] All query-builder smoke tests passed.');
}

// Module-eval auto-run is gated to a Node.js env so test consumers can import
// this file without side effects. When the peer package gets Jest wired,
// replace this gate with `describe`/`it` blocks.
if (typeof process !== 'undefined' && process.env?.SMART_TODO_WIDGET_SMOKE === '1') {
  runQueryBuilderSmokeTests();
}
