/**
 * Priority/effort card UI — value-mapping coverage (FR-02/FR-03,
 * smart-todo-r5 task 012).
 *
 * Jest-less pure-function smoke tests, matching this package's existing
 * `SmartTodoWidget.test.tsx` / `useKanbanColumns.test.ts` pattern —
 * `@spaarke/smart-todo-components` ships type-check only (`build: tsc
 * --noEmit`); there is no Jest config in this peer package yet (wiring it
 * is tracked separately as smart-todo-r5 task 040). A full render test
 * (mount `<KanbanCard>` / `<PriorityScoreCard>` / `<EffortScoreCard>` and
 * assert the glyph/badge DOM) requires Jest + jsdom + Fluent v9 provider
 * setup and is documented here as a render-test gap (needs Jest — task
 * 040), not faked with a test that can't execute.
 *
 * These tests exercise the exported PURE functions that decide what the
 * priority glyph / effort badge renders for a given `sprk_priority` /
 * `sprk_effort` raw Choice value (task 010: sprk_priority Urgent=100000000,
 * High=100000001, Medium=100000002, Low=100000003; sprk_effort
 * None=100000000, Very High=100000001, High=100000002, Medium=100000003,
 * Low=100000004) — covering:
 *   - the widget `KanbanCard` (`components/KanbanCard/KanbanCard.tsx`)
 *   - the rich `SmartToDoKanbanCard` (`components/SmartToDo/KanbanCard.tsx`)
 *   - the `PriorityScoreCard` / `EffortScoreCard` "Selected:" choice badge
 *     label lookup (`components/SmartToDo/{Priority,Effort}ScoreCard.tsx`)
 *
 * Both KanbanCard files export identically-named `derivePriorityGlyph` /
 * `deriveEffortBadge` functions (intentional local duplication — see each
 * file's header comment; each already independently duplicates its own
 * `DUE_BADGE_STYLE` map, so this task follows the same established
 * per-file convention rather than introducing new shared surface). They
 * are imported here under aliases to avoid a name collision.
 */

import {
  derivePriorityGlyph as deriveWidgetPriorityGlyph,
  deriveEffortBadge as deriveWidgetEffortBadge,
} from '../src/components/KanbanCard/KanbanCard';
import {
  derivePriorityGlyph as deriveRichPriorityGlyph,
  deriveEffortBadge as deriveRichEffortBadge,
} from '../src/components/SmartToDo/KanbanCard';
import { priorityChoiceLabel } from '../src/components/SmartToDo/PriorityScoreCard';
import { effortChoiceLabel } from '../src/components/SmartToDo/EffortScoreCard';

// Lightweight assert helpers — kept dependency-free so these compile cleanly
// even before Jest is wired into this peer package.
function assert(cond: boolean, msg: string): void {
  if (!cond) {
    // eslint-disable-next-line no-console
    console.error(`[priorityEffortCardUi.test] FAIL: ${msg}`);
    throw new Error(msg);
  }
}

function assertEqual<T>(actual: T, expected: T, msg: string): void {
  assert(actual === expected, `${msg} (expected ${String(expected)}, got ${String(actual)})`);
}

// ---------------------------------------------------------------------------
// sprk_priority (Urgent=100000000, High=100000001, Medium=100000002,
// Low=100000003) — each defined value on BOTH KanbanCard variants.
// ---------------------------------------------------------------------------

const PRIORITY_CASES: Array<{ value: number; label: string }> = [
  { value: 100000000, label: 'Urgent' },
  { value: 100000001, label: 'High' },
  { value: 100000002, label: 'Medium' },
  { value: 100000003, label: 'Low' },
];

for (const { value, label } of PRIORITY_CASES) {
  const widget = deriveWidgetPriorityGlyph(value);
  assert(widget !== undefined, `widget KanbanCard priority glyph should render for sprk_priority=${value}`);
  assertEqual(widget?.label, label, `widget KanbanCard priority glyph label for sprk_priority=${value}`);
  assert(typeof widget?.color === 'string' && widget.color.length > 0, `widget KanbanCard priority glyph colour is a non-empty token string for sprk_priority=${value}`);

  const rich = deriveRichPriorityGlyph(value);
  assert(rich !== undefined, `rich KanbanCard priority glyph should render for sprk_priority=${value}`);
  assertEqual(rich?.label, label, `rich KanbanCard priority glyph label for sprk_priority=${value}`);

  assertEqual(priorityChoiceLabel(value), label, `PriorityScoreCard choice-badge label for sprk_priority=${value}`);
}

// Each of the 4 priority values must resolve to a visually distinct colour
// on the widget card (acceptance criterion: "distinctly colored").
const distinctPriorityColors = new Set(PRIORITY_CASES.map(({ value }) => deriveWidgetPriorityGlyph(value)?.color));
assertEqual(distinctPriorityColors.size, PRIORITY_CASES.length, 'all 4 sprk_priority values map to distinct glyph colours (widget card)');

// ---------------------------------------------------------------------------
// sprk_effort (None=100000000, Very High=100000001, High=100000002,
// Medium=100000003, Low=100000004) — each defined value on BOTH cards.
// ---------------------------------------------------------------------------

const EFFORT_CASES: Array<{ value: number; label: string }> = [
  { value: 100000000, label: 'None' },
  { value: 100000001, label: 'Very High' },
  { value: 100000002, label: 'High' },
  { value: 100000003, label: 'Medium' },
  { value: 100000004, label: 'Low' },
];

for (const { value, label } of EFFORT_CASES) {
  const widget = deriveWidgetEffortBadge(value);
  assert(widget !== undefined, `widget KanbanCard effort badge should render for sprk_effort=${value}`);
  assertEqual(widget?.label, label, `widget KanbanCard effort badge label for sprk_effort=${value}`);
  assert(
    typeof widget?.style.backgroundColor === 'string' && typeof widget.style.color === 'string',
    `widget KanbanCard effort badge has both a background and foreground token for sprk_effort=${value}`
  );

  const rich = deriveRichEffortBadge(value);
  assert(rich !== undefined, `rich KanbanCard effort badge should render for sprk_effort=${value}`);
  assertEqual(rich?.label, label, `rich KanbanCard effort badge label for sprk_effort=${value}`);

  assertEqual(effortChoiceLabel(value), label, `EffortScoreCard choice-badge label for sprk_effort=${value}`);
}

// Each of the 5 effort values must resolve to a visually distinct
// background/foreground pair on the widget card.
const distinctEffortColors = new Set(
  EFFORT_CASES.map(({ value }) => {
    const badge = deriveWidgetEffortBadge(value);
    return `${badge?.style.backgroundColor}|${badge?.style.color}`;
  })
);
assertEqual(distinctEffortColors.size, EFFORT_CASES.length, 'all 5 sprk_effort values map to distinct badge tones (widget card)');

// ---------------------------------------------------------------------------
// Unset (null/undefined) and out-of-range values — neutral no-op, never a
// crash, never a misleading default colour (acceptance criterion 3).
// ---------------------------------------------------------------------------

for (const value of [null, undefined, 999999999]) {
  assertEqual(deriveWidgetPriorityGlyph(value), undefined, `widget KanbanCard priority glyph is a no-op for ${String(value)}`);
  assertEqual(deriveRichPriorityGlyph(value), undefined, `rich KanbanCard priority glyph is a no-op for ${String(value)}`);
  assertEqual(priorityChoiceLabel(value), undefined, `PriorityScoreCard choice-badge label is a no-op for ${String(value)}`);

  assertEqual(deriveWidgetEffortBadge(value), undefined, `widget KanbanCard effort badge is a no-op for ${String(value)}`);
  assertEqual(deriveRichEffortBadge(value), undefined, `rich KanbanCard effort badge is a no-op for ${String(value)}`);
  assertEqual(effortChoiceLabel(value), undefined, `EffortScoreCard choice-badge label is a no-op for ${String(value)}`);
}

// eslint-disable-next-line no-console
console.log('[priorityEffortCardUi.test] All assertions passed.');
