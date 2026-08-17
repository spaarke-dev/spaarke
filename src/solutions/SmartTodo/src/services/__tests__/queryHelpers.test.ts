/**
 * queryHelpers.test.ts — `buildTodoItemsQuery` core coverage.
 *
 * smart-todo-r5 UAT 2026-08-17: task 021's structured Filter-pane predicate
 * (`ITodoFilterState` and its dedicated describe blocks) was removed from
 * `buildTodoItemsQuery` along with the Filter pane itself — see
 * `projects/smart-todo-r5/notes/uat-filter-text-search.md`. This file
 * replaces the task-021 `queryHelpers.test.ts` (which tested ONLY the now-
 * removed `filterState` branch) with coverage of the function's reverted,
 * pre-task-021 shape: contactId ownership scoping, the optional
 * `regardingFilter` parent-record scope, and the `includeCompleted` legacy
 * toggle.
 */

import { buildTodoItemsQuery } from '../queryHelpers';

const CONTACT_ID = '11111111-1111-1111-1111-111111111111';

describe('buildTodoItemsQuery — default shape (contactId only)', () => {
  it('call_ContactIdOnly_ScopesToAssignedToAndOpenInProgressStatuses', () => {
    const query = buildTodoItemsQuery(CONTACT_ID);
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
    expect(query).toContain('statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)');
    expect(query).not.toContain('statuscode eq 2');
  });

  it('call_ContactIdOnly_SortsByPriorityScoreDescThenDueDateAsc', () => {
    const query = buildTodoItemsQuery(CONTACT_ID);
    expect(decodeURIComponent(query)).toContain(
      '$orderby=sprk_priorityscore desc,sprk_duedate asc',
    );
  });
});

describe('buildTodoItemsQuery — regardingFilter (R4 FR-34 openTodos scope)', () => {
  it('call_WithRegardingFilter_AddsTheEntitySpecificRegardingClause', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, {
      entityType: 'sprk_matter',
      recordId: 'm-1',
    });
    expect(query).toContain('_sprk_regardingmatter_value eq m-1');
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
  });

  it('call_WithUnsupportedRegardingEntityType_SilentlyOmitsTheClause', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, {
      entityType: 'sprk_unknown_entity',
      recordId: 'x-1',
    });
    expect(query).not.toContain('x-1');
  });
});

describe('buildTodoItemsQuery — includeCompleted legacy toggle', () => {
  it('call_IncludeCompletedTrue_OrsStatuscode2IntoTheActiveClause', () => {
    const query = buildTodoItemsQuery(CONTACT_ID, undefined, true);
    expect(query).toContain(
      '(statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001) or statuscode eq 2)',
    );
  });

  it('call_IncludeCompletedOmitted_DefaultsToFalse_ExcludesStatuscode2', () => {
    const query = buildTodoItemsQuery(CONTACT_ID);
    expect(query).not.toContain('statuscode eq 2');
  });
});
