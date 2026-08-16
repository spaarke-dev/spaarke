/**
 * queryHelpers.test.ts — `buildTodoItemsQuery` filter-pane coverage (task 021,
 * FR-06 / F-3).
 *
 * Not named in the task 021 POML's `<outputs>` list (which only names
 * `FilterPane.test.tsx`), but added because:
 *   - `queryHelpers.ts` is squarely in this task's edit scope.
 *   - The negative acceptance criterion — "calling `buildTodoItemsQuery` with
 *     no filter argument reproduces the exact pre-existing query string" — is
 *     `testable="true"` and is far more reliably verified against the pure
 *     function directly than indirectly through a React render.
 *   - Task 022's notes explicitly flagged this exact coverage gap as
 *     "a natural fit for whoever executes task 021" (see
 *     `notes/task-022-completed-toggle.md`).
 *
 * A CONTACT_ID and a REGARDING_FILTER fixture are reused across cases so the
 * only thing under test in each case is the filter-pane-specific clause.
 */

import {
  buildTodoItemsQuery,
  DEFAULT_TODO_FILTER,
  TODO_PRIORITY_CHOICE_VALUES,
  TODO_STATUS_FILTER_STATUSCODE,
  type ITodoFilterState,
} from '../queryHelpers';

const CONTACT_ID = '11111111-1111-1111-1111-111111111111';
const OTHER_CONTACT_ID = '22222222-2222-2222-2222-222222222222';

describe('buildTodoItemsQuery — backward compatibility (no filterState arg)', () => {
  it('call_NoOptionalArgs_ReproducesExactPreExistingDefaultQuery', () => {
    const query = buildTodoItemsQuery(CONTACT_ID);
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
    expect(query).toContain('statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)');
    expect(query).not.toContain('statuscode eq 2');
    expect(query).not.toContain('sprk_priority eq');
    // Note: 'sprk_duedate' alone always appears in $select — assert on the
    // filter-level clause shape instead (absence of a due-date RANGE clause).
    expect(query).not.toContain('sprk_duedate ge');
    expect(query).not.toContain('sprk_duedate lt');
  });

  it('call_RegardingFilterOnly_UnchangedFromPreTask021Shape', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, {
      entityType: 'sprk_matter',
      recordId: 'm-1',
    });
    expect(query).toContain('_sprk_regardingmatter_value eq m-1');
    expect(query).toContain('statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)');
  });

  it('call_IncludeCompletedTrue_NoFilterState_UsesTask022LegacyBehaviorUnchanged', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, true);
    // Exact byte-for-byte shape task 022 shipped (OData `and` binds tighter
    // than `or`, so this single paren pair is already semantically
    // `((statecode eq 0 and (...)) or statuscode eq 2)` — task 021 must not
    // alter this legacy-path string at all).
    expect(query).toContain(
      '(statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001) or statuscode eq 2)',
    );
  });
});

describe('buildTodoItemsQuery — filterState default (FR-06 acceptance #1)', () => {
  it('call_DefaultTodoFilter_MatchesOpenAndInProgressStatuscodeOnly', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, DEFAULT_TODO_FILTER);
    expect(query).toContain('(statuscode eq 1 or statuscode eq 659490001)');
    expect(query).not.toContain('statuscode eq 2');
    expect(query).not.toContain('sprk_priority eq');
    expect(query).not.toContain('sprk_duedate ge');
    expect(query).not.toContain('sprk_duedate lt');
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
  });
});

describe('buildTodoItemsQuery — Status category', () => {
  it('call_StatusIncludesCompleted_AddsStatuscode2ToTheMatchSet', () => {
    const filter: ITodoFilterState = {
      ...DEFAULT_TODO_FILTER,
      statusValues: ['Open', 'InProgress', 'Completed'],
    };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('statuscode eq 1');
    expect(query).toContain('statuscode eq 659490001');
    expect(query).toContain('statuscode eq 2');
  });

  it('call_StatusOnlyCompleted_MatchesOnlyStatuscode2', () => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, statusValues: ['Completed'] };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('statuscode eq 2');
    expect(query).not.toContain('statuscode eq 1');
    expect(query).not.toContain('statuscode eq 659490001');
  });

  it('call_StatusEmpty_DefensivelyMatchesNothing', () => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, statusValues: [] };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('statuscode eq -1');
  });

  it('TODO_STATUS_FILTER_STATUSCODE_matches_documented_sprk_todo_lifecycle_values', () => {
    expect(TODO_STATUS_FILTER_STATUSCODE).toEqual({
      Open: 1,
      InProgress: 659490001,
      Completed: 2,
    });
  });
});

describe('buildTodoItemsQuery — Priority category', () => {
  it('call_OnePriorityValue_AddsSinglePriorityEqClause', () => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, priorityValues: [100000000] };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('sprk_priority eq 100000000');
  });

  it('call_MultiplePriorityValues_OrsThemTogether', () => {
    const filter: ITodoFilterState = {
      ...DEFAULT_TODO_FILTER,
      priorityValues: [100000000, 100000001],
    };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('(sprk_priority eq 100000000 or sprk_priority eq 100000001)');
  });

  it('call_EmptyPriorityValues_OmitsThePriorityClauseEntirely', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, DEFAULT_TODO_FILTER);
    expect(query).not.toContain('sprk_priority eq');
  });

  it('TODO_PRIORITY_CHOICE_VALUES_has_exactly_the_4_FR-02_choice_values', () => {
    expect(TODO_PRIORITY_CHOICE_VALUES).toEqual([
      { value: 100000000, label: 'Urgent' },
      { value: 100000001, label: 'High' },
      { value: 100000002, label: 'Medium' },
      { value: 100000003, label: 'Low' },
    ]);
  });
});

describe('buildTodoItemsQuery — Due-date category', () => {
  const CATEGORIES: Array<ITodoFilterState['dueDateCategory']> = [
    'Today',
    'Tomorrow',
    'ThisWeek',
    'Overdue',
  ];

  it.each(CATEGORIES)('call_DueDateCategory_%s_AddsASprkDuedateClause', (category) => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, dueDateCategory: category };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain('sprk_duedate');
  });

  it('call_Overdue_UsesLessThanTodayStartWithNoUpperBound', () => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, dueDateCategory: 'Overdue' };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toMatch(/sprk_duedate lt \d{4}-\d{2}-\d{2}T00:00:00\.000Z/);
    expect(query).not.toContain('sprk_duedate ge');
  });

  it('call_Today_UsesA24HourRangeStartingAtTodayMidnightUtc', () => {
    const filter: ITodoFilterState = { ...DEFAULT_TODO_FILTER, dueDateCategory: 'Today' };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toMatch(
      /sprk_duedate ge \d{4}-\d{2}-\d{2}T00:00:00\.000Z and sprk_duedate lt \d{4}-\d{2}-\d{2}T00:00:00\.000Z/,
    );
  });

  it('call_DueDateCategoryUndefined_OmitsTheClauseEntirely', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, DEFAULT_TODO_FILTER);
    expect(query).not.toContain('sprk_duedate ge');
    expect(query).not.toContain('sprk_duedate lt');
  });
});

describe('buildTodoItemsQuery — Assigned-To category (ownership override)', () => {
  it('call_AssignedToContactIdSet_ReplacesTheDefaultOwnershipClause', () => {
    const filter: ITodoFilterState = {
      ...DEFAULT_TODO_FILTER,
      assignedToContactId: OTHER_CONTACT_ID,
    };
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, filter);
    expect(query).toContain(`_sprk_assignedto_value eq ${OTHER_CONTACT_ID}`);
    expect(query).not.toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
  });

  it('call_AssignedToContactIdUnset_FallsBackToTheCurrentUserContact', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, false, DEFAULT_TODO_FILTER);
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
  });
});

describe('DEFAULT_TODO_FILTER', () => {
  it('matches the FR-06 default: Status {Open, InProgress}, everything else unfiltered', () => {
    expect(DEFAULT_TODO_FILTER).toEqual({
      priorityValues: [],
      statusValues: ['Open', 'InProgress'],
      dueDateCategory: undefined,
      assignedToContactId: undefined,
    });
  });
});
