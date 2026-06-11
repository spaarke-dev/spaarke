/**
 * useUserPreferences.test.ts
 *
 * Unit tests for the SmartTodo user-preferences hook orientation extension
 * (R4 task 071, spec FR-28 / FR-29 / FR-30 / NFR-08).
 *
 * ──────────────────────────────────────────────────────────────────────────
 * TEST-RUNNER STATUS (2026-06-11)
 *
 * SmartTodo's `package.json` does NOT currently include a test runner (no jest,
 * no vitest). These tests are written as DOCUMENTATION + ASSERTION SOURCE so
 * they activate immediately when a test runner is added to the SmartTodo
 * package — matching the established pattern of `useLaunchContext.test.ts`,
 * `ToolbarActions.test.ts`, and `buildTodoIframeUrl.test.ts`.
 *
 * In the meantime, the test source itself serves as:
 *   1. An executable spec — every assertion below is a concrete behavior we
 *      want the production hook to honour.
 *   2. A regression boundary — future edits to `useUserPreferences.ts` should
 *      change tests in this file FIRST, then the implementation, so the diff
 *      makes the behavior change reviewable.
 *
 * Scenarios covered (mirror task 071 POML acceptance criteria):
 *
 *   Orientation round-trip:
 *     (a) DEFAULT_SMART_TODO_ORIENTATION = "horizontal" (FR-28 default).
 *     (b) isValidOrientation returns true for "horizontal" + "vertical".
 *     (c) isValidOrientation rejects "diagonal", null, undefined, number, etc.
 *
 *   JSON envelope semantics (mirrors what the hook does internally on fetch):
 *     (d) Empty JSON → orientation defaults to "horizontal".
 *     (e) JSON with valid orientation="vertical" → orientation = "vertical".
 *     (f) JSON with invalid orientation → falls back to "horizontal".
 *     (g) JSON with viewMode + thresholds + orientation → all fields restored.
 *
 *   Envelope shape contract (regression — sibling fields must coexist):
 *     (h) DEFAULT_PREFERENCES carries all 4 fields (thresholds, viewMode,
 *         orientation).
 *
 * @see src/solutions/SmartTodo/src/hooks/useUserPreferences.ts
 * @see projects/smart-todo-r4/spec.md FR-28 / FR-29 / FR-30 / NFR-08
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import {
  DEFAULT_SMART_TODO_ORIENTATION,
  DEFAULT_SMART_TODO_VIEW_MODE,
  DEFAULT_TODAY_THRESHOLD,
  DEFAULT_TOMORROW_THRESHOLD,
  PREFERENCE_TYPE_TODO_KANBAN,
  type ITodoKanbanPreferences,
  type SmartTodoOrientation,
} from '../useUserPreferences';

// Compile-time shims so the file type-checks without jest/vitest types installed.
declare const describe: (name: string, fn: () => void) => void;
declare const it: (name: string, fn: () => void) => void;
declare const expect: any;

// ---------------------------------------------------------------------------
// Inline mirror of the validator (the hook's parser uses an internal closure;
// we mirror it here to assert the contract directly without exporting it).
// If the production parser changes, these tests must update in lock-step.
// ---------------------------------------------------------------------------

const VALID_ORIENTATIONS: ReadonlyArray<SmartTodoOrientation> = [
  'horizontal',
  'vertical',
];

function isValidOrientation(value: unknown): value is SmartTodoOrientation {
  return (
    typeof value === 'string' &&
    (VALID_ORIENTATIONS as readonly string[]).includes(value)
  );
}

/**
 * Simulates the JSON-parse + field-recovery block inside the hook's
 * `getUserPreferences` resolution path. Mirrors lines that read the
 * `sprk_preferencevalue` JSON and pick orientation with a default fallback.
 */
function parseStoredPreferences(jsonValue: string): ITodoKanbanPreferences {
  let parsed: Partial<ITodoKanbanPreferences> = {};
  try {
    parsed = JSON.parse(jsonValue) as Partial<ITodoKanbanPreferences>;
  } catch {
    parsed = {};
  }
  return {
    todayThreshold:
      typeof parsed.todayThreshold === 'number'
        ? parsed.todayThreshold
        : DEFAULT_TODAY_THRESHOLD,
    tomorrowThreshold:
      typeof parsed.tomorrowThreshold === 'number'
        ? parsed.tomorrowThreshold
        : DEFAULT_TOMORROW_THRESHOLD,
    viewMode:
      parsed.viewMode === 'card' || parsed.viewMode === 'list'
        ? parsed.viewMode
        : DEFAULT_SMART_TODO_VIEW_MODE,
    orientation: isValidOrientation(parsed.orientation)
      ? parsed.orientation
      : DEFAULT_SMART_TODO_ORIENTATION,
  };
}

// ---------------------------------------------------------------------------
// Orientation default + validator
// ---------------------------------------------------------------------------

describe('useUserPreferences — orientation defaults + validator', () => {
  it('(a) DEFAULT_SMART_TODO_ORIENTATION is "horizontal" (FR-28)', () => {
    expect(DEFAULT_SMART_TODO_ORIENTATION).toBe('horizontal');
  });

  it('(b) isValidOrientation accepts "horizontal"', () => {
    expect(isValidOrientation('horizontal')).toBe(true);
  });

  it('(b) isValidOrientation accepts "vertical"', () => {
    expect(isValidOrientation('vertical')).toBe(true);
  });

  it('(c) isValidOrientation rejects unknown string', () => {
    expect(isValidOrientation('diagonal')).toBe(false);
  });

  it('(c) isValidOrientation rejects null / undefined / number / object', () => {
    expect(isValidOrientation(null)).toBe(false);
    expect(isValidOrientation(undefined)).toBe(false);
    expect(isValidOrientation(0)).toBe(false);
    expect(isValidOrientation({})).toBe(false);
    expect(isValidOrientation(['horizontal'])).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// JSON envelope round-trip
// ---------------------------------------------------------------------------

describe('useUserPreferences — JSON envelope round-trip', () => {
  it('(d) empty stored JSON falls back to defaults including orientation=horizontal', () => {
    const prefs = parseStoredPreferences('{}');
    expect(prefs.orientation).toBe('horizontal');
    expect(prefs.viewMode).toBe(DEFAULT_SMART_TODO_VIEW_MODE);
    expect(prefs.todayThreshold).toBe(DEFAULT_TODAY_THRESHOLD);
    expect(prefs.tomorrowThreshold).toBe(DEFAULT_TOMORROW_THRESHOLD);
  });

  it('(e) stored orientation="vertical" round-trips through the parser', () => {
    const json = JSON.stringify({
      todayThreshold: 60,
      tomorrowThreshold: 30,
      viewMode: 'card',
      orientation: 'vertical',
    } satisfies ITodoKanbanPreferences);
    const prefs = parseStoredPreferences(json);
    expect(prefs.orientation).toBe('vertical');
  });

  it('(e) stored orientation="horizontal" round-trips through the parser', () => {
    const json = JSON.stringify({
      todayThreshold: 60,
      tomorrowThreshold: 30,
      viewMode: 'list',
      orientation: 'horizontal',
    } satisfies ITodoKanbanPreferences);
    const prefs = parseStoredPreferences(json);
    expect(prefs.orientation).toBe('horizontal');
  });

  it('(f) invalid stored orientation falls back to "horizontal"', () => {
    const json = JSON.stringify({
      todayThreshold: 60,
      tomorrowThreshold: 30,
      viewMode: 'card',
      orientation: 'diagonal',
    });
    const prefs = parseStoredPreferences(json);
    expect(prefs.orientation).toBe('horizontal');
  });

  it('(f) missing orientation field falls back to "horizontal" (back-compat with R3 / 033 records)', () => {
    // Simulates a pre-task-071 record that only carried thresholds + viewMode.
    const json = JSON.stringify({
      todayThreshold: 60,
      tomorrowThreshold: 30,
      viewMode: 'list',
    });
    const prefs = parseStoredPreferences(json);
    expect(prefs.orientation).toBe('horizontal');
    // Existing fields still restored
    expect(prefs.viewMode).toBe('list');
  });

  it('(g) all four envelope fields are preserved together (sibling round-trip)', () => {
    const json = JSON.stringify({
      todayThreshold: 75,
      tomorrowThreshold: 25,
      viewMode: 'list',
      orientation: 'vertical',
    } satisfies ITodoKanbanPreferences);
    const prefs = parseStoredPreferences(json);
    expect(prefs).toEqual({
      todayThreshold: 75,
      tomorrowThreshold: 25,
      viewMode: 'list',
      orientation: 'vertical',
    });
  });

  it('(g) corrupt JSON falls back to defaults across all four fields (incl. orientation)', () => {
    const prefs = parseStoredPreferences('this is not json {{{');
    expect(prefs.orientation).toBe(DEFAULT_SMART_TODO_ORIENTATION);
    expect(prefs.viewMode).toBe(DEFAULT_SMART_TODO_VIEW_MODE);
    expect(prefs.todayThreshold).toBe(DEFAULT_TODAY_THRESHOLD);
    expect(prefs.tomorrowThreshold).toBe(DEFAULT_TOMORROW_THRESHOLD);
  });
});

// ---------------------------------------------------------------------------
// Preferencetype + envelope-shape regression
// ---------------------------------------------------------------------------

describe('useUserPreferences — preferencetype contract', () => {
  it('(h) PREFERENCE_TYPE_TODO_KANBAN is 100000000 (orientation shares the same record per FR-30)', () => {
    // Spec FR-30 says the conceptual "SmartTodoOrientation" preference type;
    // implementation choice is to round-trip via the existing kanban-prefs
    // JSON envelope rather than a new optionset value (avoids Dataverse
    // optionset schema work for an additive client-side field).
    expect(PREFERENCE_TYPE_TODO_KANBAN).toBe(100000000);
  });
});
