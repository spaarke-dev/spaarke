/**
 * SmartTodoWidget — query-builder tests + orientation-default test.
 *
 * smart-todo-r5 task 040: converted from this file's original Jest-less
 * `assert()`-based smoke-test harness (no Jest runner existed in this
 * package before task 040 wired one — see `jest.config.cjs`) to real Jest
 * `describe`/`it`/`expect`. Running the ORIGINAL assertions verbatim for the
 * first time (they had never actually executed) surfaced real drift: the
 * original harness called `buildSmartTodoQuery({ userId: 'u1' })` and
 * expected an `_ownerid_value eq u1` "owner clause". The function's actual
 * current signature (post UAT 2026-06-20 assignedto-contact migration, see
 * this file's own docblock above `buildSmartTodoQuery`) takes `contactId`,
 * not `userId`, and emits `_sprk_assignedto_value eq <contactId>`, not
 * `_ownerid_value`. With the stale `userId` key, `opts.contactId` was always
 * `undefined`, so every one of these test calls actually exercised the
 * SECURITY zero-row fallback branch (`sprk_todoid eq
 * 00000000-0000-0000-0000-000000000000`) instead of the intended path —
 * silently, since nothing ever ran it. This file corrects the input key to
 * `contactId` and the expected field to `_sprk_assignedto_value`, matching
 * current code (root CLAUDE.md §2: code wins, docs/tests lag) — the
 * BEHAVIORAL intent of each assertion (zero legacy refs, statuscode/
 * statecode pinning, regarding-precedence, assignee clause, scope='all' BU
 * clause, select projection) is unchanged from the original harness; only
 * the stale parameter name and the (also stale) `_ownerid_value` field name
 * are corrected. See projects/smart-todo-r5/notes/task-040-test-infra.md.
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
 *
 * smart-todo-r5 task 024 (U-2 / FR-09, 2026-08-15): the widget's default
 * orientation changed from 'vertical' (stacked rows) to 'horizontal'
 * (side-by-side columns — left/center/right), matching the mockup intent
 * and the Code Page's existing default. This is a pure-VALUE check of the
 * single-source-of-truth constant `SmartTodoWidget`'s `useState<Orientation>`
 * seeds from — a true render-level assertion (mount + inspect the
 * `KanbanBoard` region) is a separate, larger render-test surface this task
 * does not add (would require Fluent v9 provider + `@hello-pangea/dnd` mount
 * scaffolding beyond this file's existing pure-function scope); the
 * underlying flip mechanism itself is already covered by
 * `Spaarke.UI.Components/.../Kanban/__tests__/KanbanBoard.test.tsx`'s
 * "keeps the same column structure across orientation flips".
 *
 * `@spaarke/ui-components` is mocked (module-boundary pattern, mirroring
 * `FilterPane.test.tsx`) because its barrel (`dist/index.js` ->
 * `services/index.js`) unconditionally requires `@spaarke/sdap-client`,
 * which is not built as a dist package in this worktree. `SmartTodoWidget`
 * only imports `OrientationToggle` / `MicrosoftToDoIcon` (both unrendered by
 * these pure-function tests) - the mock keeps the suite hermetic to
 * `buildSmartTodoQuery` and `SMART_TODO_WIDGET_DEFAULT_ORIENTATION` without
 * pulling in the unrelated SDAP dependency chain.
 */

jest.mock('@spaarke/ui-components', () => ({
  OrientationToggle: () => null,
  MicrosoftToDoIcon: () => null,
}));

import {
  buildSmartTodoQuery,
  TODO_STATUSCODE_OPEN,
  TODO_STATUSCODE_IN_PROGRESS,
  SMART_TODO_WIDGET_DEFAULT_ORIENTATION,
} from '../src/widgets/SmartTodoWidget/SmartTodoWidget';

describe('buildSmartTodoQuery — query-builder tests (FR-02 acceptance)', () => {
  it('emits zero legacy sprk_event/sprk_todoflag references', () => {
    const query = buildSmartTodoQuery({ contactId: 'c1' });
    expect(query).not.toContain('sprk_event');
    expect(query).not.toContain('sprk_todoflag');
  });

  it('pins statuscode to {Open, In Progress} and statecode to Active', () => {
    const query = buildSmartTodoQuery({ contactId: 'c1' });
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain(`statuscode eq ${TODO_STATUSCODE_OPEN}`);
    expect(decoded).toContain(`statuscode eq ${TODO_STATUSCODE_IN_PROGRESS}`);
    expect(decoded).toContain('statecode eq 0');
  });

  it('regarding context filter takes precedence over the assignee clause', () => {
    const query = buildSmartTodoQuery({
      contactId: 'c1',
      regardingContext: { entityLogicalName: 'sprk_matter', recordId: 'M1' },
    });
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain('_sprk_regardingmatter_value eq M1');
    // `_sprk_assignedto_value` still appears in $select (it's always
    // projected) — check the FILTER clause specifically doesn't use it.
    expect(decoded).not.toContain('_sprk_assignedto_value eq');
  });

  it('emits the assignee clause when no regarding context is supplied', () => {
    const query = buildSmartTodoQuery({ contactId: 'c1' });
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain('_sprk_assignedto_value eq c1');
  });

  it('with no contactId and no regarding context, falls back to the SECURITY zero-row guard (never all active todos)', () => {
    // Per this file's buildSmartTodoQuery docblock: showing other people's
    // todos to an unresolved user is a data-isolation breach — the function
    // must never silently fall back to "all active sprk_todos system-wide".
    const query = buildSmartTodoQuery({});
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain('sprk_todoid eq 00000000-0000-0000-0000-000000000000');
    expect(decoded).not.toContain('_sprk_assignedto_value eq');
  });

  it("scope='all' includes the business-unit OR clause", () => {
    const query = buildSmartTodoQuery({ contactId: 'c1', scope: 'all', businessUnitId: 'bu1' });
    const decoded = decodeURIComponent(query);
    expect(decoded).toContain('_owningbusinessunit_value eq bu1');
    expect(decoded).toContain('_sprk_assignedto_value eq c1');
  });

  it('the $select projection includes the required fields', () => {
    const query = buildSmartTodoQuery({ contactId: 'c1' });
    expect(query).toContain('sprk_todoid');
    expect(query).toContain('sprk_name');
    expect(query).toContain('sprk_duedate');
    expect(query).toContain('statuscode');
  });
});

describe('SmartTodoWidget — default orientation (FR-09 / U-2)', () => {
  it("SMART_TODO_WIDGET_DEFAULT_ORIENTATION is 'horizontal' (side-by-side columns: Today/Tomorrow/Future left/center/right)", () => {
    expect(SMART_TODO_WIDGET_DEFAULT_ORIENTATION).toBe('horizontal');
  });
});
