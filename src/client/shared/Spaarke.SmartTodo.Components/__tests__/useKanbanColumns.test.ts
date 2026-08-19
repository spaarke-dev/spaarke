/**
 * useKanbanColumns — Completed-item bucketing coverage (FR-07 / F-2 Show-
 * Completed toggle, smart-todo-r5 task 022) + subtle-coloring token mapping
 * (FR-08 / U-1+F-1, task 023).
 *
 * smart-todo-r5 task 040: converted from this file's original Jest-less
 * `assert()`-based smoke-test harness (no Jest runner existed in this
 * package before task 040 wired one — see `jest.config.cjs`) to real Jest
 * `describe`/`it`/`expect`. The assertions below are UNCHANGED from the
 * original harness — only the execution mechanism changed.
 *
 * These tests exercise the exported pure function `bucketTodoItems` directly
 * (no React renderer needed — the hook itself wraps this pure function in
 * `useMemo`/`useState`, so testing the pure function covers the bucketing
 * behavior under test here).
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

import { tokens } from '@fluentui/react-components';
import { bucketTodoItems } from '../src/hooks/useKanbanColumns';
import type { IKanbanCardTodo } from '../src/types/kanban';

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

describe('bucketTodoItems — default-hidden regression (no Completed items in the set)', () => {
  it('buckets a mixed Open/InProgress set into exactly Today/Tomorrow/Future with no item dropped', () => {
    const items: IKanbanCardTodo[] = [
      makeTodo({ sprk_todoid: 't1', statuscode: 1, sprk_duedate: isoDaysFromNow(0) }), // Open, due today
      makeTodo({ sprk_todoid: 't2', statuscode: 659490001, sprk_duedate: isoDaysFromNow(1) }), // In Progress, due tomorrow
    ];

    const columns = bucketTodoItems(items);

    expect(columns).toHaveLength(3);
    expect(columns.map(c => c.id)).toEqual(['Today', 'Tomorrow', 'Future']);
    const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);
    expect(totalItems).toBe(2);
    expect(columns[0].items.some(i => i.sprk_todoid === 't1')).toBe(true);
    expect(columns[1].items.some(i => i.sprk_todoid === 't2')).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Test 2: a Completed (statuscode=2) item flows through the SAME score/due-
// date bucketing as any other item — no special-case exclusion, no 4th
// column. This is the core FR-07 render-side guarantee.
// ---------------------------------------------------------------------------

describe('bucketTodoItems — Completed-item bucketing (FR-07 core guarantee)', () => {
  it('buckets Completed items into their score/due-date column exactly like any other item, with no 4th column introduced', () => {
    const items: IKanbanCardTodo[] = [
      makeTodo({ sprk_todoid: 'open-today', statuscode: 1, sprk_duedate: isoDaysFromNow(0) }),
      makeTodo({ sprk_todoid: 'completed-today', statuscode: 2, sprk_duedate: isoDaysFromNow(-1) }), // overdue → Today
      makeTodo({ sprk_todoid: 'completed-future', statuscode: 2, sprk_duedate: isoDaysFromNow(10) }),
      makeTodo({ sprk_todoid: 'completed-undated', statuscode: 2 }), // no due date → Future
    ];

    const columns = bucketTodoItems(items);

    expect(columns).toHaveLength(3);

    const today = columns.find(c => c.id === 'Today')!;
    const future = columns.find(c => c.id === 'Future')!;

    expect(today.items.some(i => i.sprk_todoid === 'completed-today')).toBe(true);
    expect(today.items.some(i => i.sprk_todoid === 'open-today')).toBe(true);
    expect(future.items.some(i => i.sprk_todoid === 'completed-future')).toBe(true);
    expect(future.items.some(i => i.sprk_todoid === 'completed-undated')).toBe(true);

    const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);
    expect(totalItems).toBe(items.length);
  });
});

// ---------------------------------------------------------------------------
// Test 3: a pinned Completed item still respects its pinned column override
// (pin behavior is uniform across statuscode — no special-case guard).
// ---------------------------------------------------------------------------

describe('bucketTodoItems — pinned Completed item', () => {
  it('honors the pinned column override for a Completed item exactly like a non-completed item', () => {
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

    expect(future.items.some(i => i.sprk_todoid === 'completed-pinned-future-column')).toBe(true);
    expect(today.items.some(i => i.sprk_todoid === 'completed-pinned-future-column')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Test 4: negative regression — a Dismissed-shaped item (statuscode
// 659490002) is bucketed with the same no-special-case logic. Dismissed
// items are fetched via the separate `buildDismissedTodoQuery` /
// `DismissedSection` path today and never actually reach this function in
// production, but this proves the hook applies zero statuscode
// discrimination — so the Dismissed lane cannot be broken by this task's
// change (it doesn't touch statuscode handling in this file at all).
// ---------------------------------------------------------------------------

describe('bucketTodoItems — Dismissed-shape unaffected (negative regression)', () => {
  it('applies zero statuscode filtering — a Dismissed-shaped item still buckets by score/due-date', () => {
    const items: IKanbanCardTodo[] = [
      makeTodo({ sprk_todoid: 'dismissed-1', statuscode: 659490002, sprk_duedate: isoDaysFromNow(1) }),
    ];

    const columns = bucketTodoItems(items);
    const totalItems = columns.reduce((sum, c) => sum + c.items.length, 0);

    expect(totalItems).toBe(1);
    expect(columns.find(c => c.id === 'Tomorrow')!.items.some(i => i.sprk_todoid === 'dismissed-1')).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// Test 5: subtle channel coloring (smart-todo-r5 task 023, FR-08 / U-1 + F-1,
// 2026-08-16) — the semantic red=Today / yellow=Tomorrow / green=Future
// color mapping must be preserved, using ONLY Fluent v9 semantic tokens
// (ADR-021 — zero hex/rgb literals). This test does NOT assert on how
// `KanbanBoard.tsx` renders `tintColor` (full-column vs header-only) — that
// is a render-side concern covered by the component itself, not this pure
// bucketing function. It asserts the DATA CONTRACT this hook still owns:
// the right token family per column, and the yellow-contrast fix
// (`countTextColor`) staying in place.
// ---------------------------------------------------------------------------

describe('bucketTodoItems — subtle-coloring token mapping (FR-08 data contract)', () => {
  it('preserves the semantic red/yellow/green token mapping and the yellow-contrast countTextColor fix, with zero hex/rgb literals', () => {
    const columns = bucketTodoItems([]);
    const today = columns.find(c => c.id === 'Today')!;
    const tomorrow = columns.find(c => c.id === 'Tomorrow')!;
    const future = columns.find(c => c.id === 'Future')!;

    // Semantic mapping preserved: red=Today, yellow=Tomorrow, green=Future.
    expect(today.accentColor).toBe(tokens.colorPaletteRedBorder2);
    expect(tomorrow.accentColor).toBe(tokens.colorPaletteYellowBorder2);
    expect(future.accentColor).toBe(tokens.colorPaletteGreenBorder2);

    // tintColor still sourced from the lightest (`Background1`) semantic token
    // tier per column — KanbanBoard.tsx now scopes its application to the
    // column header strip only (not the full column body), but the token
    // VALUES this hook emits are unchanged (ADR-021: semantic tokens only).
    expect(today.tintColor).toBe(tokens.colorPaletteRedBackground1);
    expect(tomorrow.tintColor).toBe(tokens.colorPaletteYellowBackground1);
    expect(future.tintColor).toBe(tokens.colorPaletteGreenBackground1);

    // F-1 yellow-contrast fix: the Tomorrow column's count pill still forces a
    // dark neutral foreground (WCAG-safe against the saturated yellow
    // Border2/accentColor pill background) — Today/Future keep the default
    // (colorNeutralForegroundOnBrand, applied by KanbanBoard.tsx when
    // `countTextColor` is undefined).
    expect(tomorrow.countTextColor).toBe(tokens.colorNeutralForeground1);
    expect(today.countTextColor).toBeUndefined();
    expect(future.countTextColor).toBeUndefined();

    // No hex/rgb literals anywhere in the emitted values (ADR-021) — every
    // token resolves to a Fluent v9 CSS custom-property reference at runtime
    // (e.g. "var(--colorPaletteRedBorder2)"), never a literal color.
    const hexOrRgbPattern = /^#|^rgb/i;
    for (const col of [today, tomorrow, future]) {
      expect(hexOrRgbPattern.test(col.accentColor ?? '')).toBe(false);
      expect(hexOrRgbPattern.test(col.tintColor ?? '')).toBe(false);
    }
  });
});
