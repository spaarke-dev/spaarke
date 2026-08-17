/**
 * todoSearchUtils.test.ts — `matchesTodoSearchQuery` coverage for the SHARED
 * predicate now used by BOTH the SmartTodo Code Page and SmartTodoWidget
 * (§11 convergence, smart-todo-r5 follow-up 2026-08-17 — moved here from
 * `src/solutions/SmartTodo/src/utils/__tests__/`).
 *
 * Matches: NAME, DESCRIPTION, ASSIGNED-TO, regarding-record name/number, and the
 * blended DATE search — the last being the capability the widget previously
 * lacked (its old inline predicate matched only name + description).
 */

import { matchesTodoSearchQuery, type TodoSearchableFields } from '../src/utils/todoSearchUtils';

function makeTodo(overrides: Partial<TodoSearchableFields> = {}): TodoSearchableFields {
  return {
    sprk_name: 'Review NDA',
    sprk_description: 'Confidentiality clause review',
    assignedToName: 'Jordan Rivera',
    sprk_regardingrecordname: 'Smith v Jones',
    sprk_regardingrecordnumber: 'MAT-2026-01234',
    ...overrides,
  };
}

describe('matchesTodoSearchQuery — empty query', () => {
  it('call_EmptyString_MatchesEveryItem', () => {
    expect(matchesTodoSearchQuery(makeTodo(), '')).toBe(true);
  });
  it('call_WhitespaceOnly_MatchesEveryItem', () => {
    expect(matchesTodoSearchQuery(makeTodo(), '   ')).toBe(true);
  });
});

describe('matchesTodoSearchQuery — name / description / assigned-to / regarding', () => {
  it('call_SubstringOfName_CaseInsensitive_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'nda')).toBe(true);
    expect(matchesTodoSearchQuery(makeTodo(), 'NDA')).toBe(true);
  });
  it('call_SubstringOfDescription_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'confidentiality')).toBe(true);
  });
  it('call_SubstringOfAssignedToName_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'jordan')).toBe(true);
    expect(matchesTodoSearchQuery(makeTodo(), 'rivera')).toBe(true);
  });
  it('call_AssignedToNameMissing_DoesNotThrow_NoFalseMatch', () => {
    expect(matchesTodoSearchQuery(makeTodo({ assignedToName: undefined }), 'jordan')).toBe(false);
  });
  it('call_SubstringOfRegardingRecordNameOrNumber_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'smith v jones')).toBe(true);
    expect(matchesTodoSearchQuery(makeTodo(), 'mat-2026-01234')).toBe(true);
  });
  it('call_QueryNotPresentInAnyField_ReturnsFalse', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'nonexistent-term-xyz')).toBe(false);
  });
});

describe('matchesTodoSearchQuery — blended due-date search (the widget-gap this closes)', () => {
  const dueAug18 = makeTodo({ sprk_duedate: '2026-08-18', dueDateFormatted: '8/18/2026' });

  it('call_MonthNameLongOrShort_Matches', () => {
    expect(matchesTodoSearchQuery(dueAug18, 'august')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, 'August 18')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, 'aug 18')).toBe(true);
  });
  it('call_Numeric_And_Iso_And_Formatted_Match', () => {
    expect(matchesTodoSearchQuery(dueAug18, '8/18')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, '8/18/2026')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, '2026-08-18')).toBe(true);
  });
  it('call_DateOnly_TimezoneSafe_DayNotShifted', () => {
    // A DATE-ONLY 2026-08-18 must match "18", never "17" (no local-tz Date() shift).
    expect(matchesTodoSearchQuery(dueAug18, 'august 18')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, 'august 17')).toBe(false);
  });
  it('call_DifferentMonth_DoesNotMatch', () => {
    expect(matchesTodoSearchQuery(dueAug18, 'september')).toBe(false);
    expect(matchesTodoSearchQuery(dueAug18, 'july')).toBe(false);
  });
  it('call_NoDueDate_NoThrow_NoFalseMatch', () => {
    const noDate = makeTodo({ sprk_duedate: undefined, dueDateFormatted: undefined });
    expect(matchesTodoSearchQuery(noDate, 'august')).toBe(false);
  });
});
