/**
 * todoSearchUtils.test.ts — `matchesTodoSearchQuery` coverage (smart-todo-r5
 * UAT 2026-08-17 — expanding text search).
 *
 * Exercises the exact predicate `SmartToDo.tsx`'s `displayItems` memo uses:
 * case-insensitive substring match on NAME, DESCRIPTION, and ASSIGNED-TO
 * (per the UAT acceptance — search by "the To Do's NAME + DESCRIPTION +
 * ASSIGNED-TO"), plus the pre-existing regarding-record name/number match
 * (DEF-11 Part 3) this task preserves unchanged.
 */

import { matchesTodoSearchQuery } from '../todoSearchUtils';
import type { ITodo } from '../../types/entities';

function makeTodo(overrides: Partial<ITodo> = {}): ITodo {
  return {
    sprk_todoid: 't-1',
    sprk_name: 'Review NDA',
    sprk_description: 'Confidentiality clause review',
    assignedToName: 'Jordan Rivera',
    sprk_regardingrecordname: 'Smith v Jones',
    sprk_regardingrecordnumber: 'MAT-2026-01234',
    createdon: '2026-08-01T00:00:00.000Z',
    modifiedon: '2026-08-01T00:00:00.000Z',
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

describe('matchesTodoSearchQuery — name', () => {
  it('call_SubstringOfName_CaseInsensitive_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'nda')).toBe(true);
    expect(matchesTodoSearchQuery(makeTodo(), 'NDA')).toBe(true);
  });
});

describe('matchesTodoSearchQuery — description', () => {
  it('call_SubstringOfDescription_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'confidentiality')).toBe(true);
  });
});

describe('matchesTodoSearchQuery — assigned-to (smart-todo-r5 UAT gap closed)', () => {
  it('call_SubstringOfAssignedToName_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'jordan')).toBe(true);
    expect(matchesTodoSearchQuery(makeTodo(), 'rivera')).toBe(true);
  });

  it('call_AssignedToNameMissing_DoesNotThrow_NoFalseMatch', () => {
    const item = makeTodo({ assignedToName: undefined });
    expect(matchesTodoSearchQuery(item, 'jordan')).toBe(false);
  });
});

describe('matchesTodoSearchQuery — regarding-record (DEF-11 Part 3, preserved)', () => {
  it('call_SubstringOfRegardingRecordName_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'smith v jones')).toBe(true);
  });

  it('call_SubstringOfRegardingRecordNumber_Matches', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'mat-2026-01234')).toBe(true);
  });
});

describe('matchesTodoSearchQuery — no match', () => {
  it('call_QueryNotPresentInAnyField_ReturnsFalse', () => {
    expect(matchesTodoSearchQuery(makeTodo(), 'nonexistent-term-xyz')).toBe(false);
  });
});

describe('matchesTodoSearchQuery — blended due-date search (UAT 2026-08-17)', () => {
  const dueAug18 = makeTodo({ sprk_duedate: '2026-08-18', dueDateFormatted: '8/18/2026' });

  it('call_MonthNameLong_Matches', () => {
    expect(matchesTodoSearchQuery(dueAug18, 'august')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, 'August 18')).toBe(true);
  });

  it('call_MonthNameShort_Matches', () => {
    expect(matchesTodoSearchQuery(dueAug18, 'aug 18')).toBe(true);
  });

  it('call_Numeric_And_Iso_And_Formatted_Match', () => {
    expect(matchesTodoSearchQuery(dueAug18, '8/18')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, '8/18/2026')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, '2026-08-18')).toBe(true);
  });

  it('call_DifferentMonth_DoesNotMatch', () => {
    expect(matchesTodoSearchQuery(dueAug18, 'september')).toBe(false);
    expect(matchesTodoSearchQuery(dueAug18, 'july')).toBe(false);
  });

  it('call_TimezoneSafe_DayNotShifted', () => {
    // A DATE-ONLY 2026-08-18 must match "18", never "17" (no local-tz Date() shift).
    expect(matchesTodoSearchQuery(dueAug18, 'august 18')).toBe(true);
    expect(matchesTodoSearchQuery(dueAug18, 'august 17')).toBe(false);
  });

  it('call_NoDueDate_NoThrow_NoFalseMatch', () => {
    const noDate = makeTodo({ sprk_duedate: undefined, dueDateFormatted: undefined });
    expect(matchesTodoSearchQuery(noDate, 'august')).toBe(false);
  });
});
