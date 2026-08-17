/**
 * Priority/effort card UI — value-mapping coverage (FR-02/FR-03,
 * smart-todo-r5 task 012).
 *
 * smart-todo-r5 task 040: converted from this file's original Jest-less
 * `assert()`-based smoke-test harness (no Jest runner existed in this
 * package before task 040 wired one — see `jest.config.cjs`) to real Jest
 * `describe`/`it`/`expect` (`it.each` for the per-choice-value tables). The
 * assertions below are UNCHANGED from the original harness — only the
 * execution mechanism changed.
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
 *
 * `@spaarke/ui-components` is mocked (module-boundary pattern, mirroring
 * `FilterPane.test.tsx`) because its barrel (`dist/index.js` ->
 * `services/index.js`) unconditionally requires `@spaarke/sdap-client`,
 * which is not built as a dist package in this worktree. The rich
 * `components/SmartToDo/KanbanCard.tsx` only imports `RecordCardShell` /
 * `CardIcon` (neither rendered by these pure-function tests) - the mock
 * keeps the suite hermetic to the `derivePriorityGlyph`/`deriveEffortBadge`
 * functions under test without pulling in the unrelated SDAP dependency
 * chain.
 */

jest.mock('@spaarke/ui-components', () => ({
  RecordCardShell: () => null,
  CardIcon: () => null,
}));

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

describe('priority glyph + PriorityScoreCard label — sprk_priority value mapping (FR-02/FR-03 UI)', () => {
  it.each(PRIORITY_CASES)(
    'renders label "$label" for sprk_priority=$value on the widget KanbanCard, the rich KanbanCard, and PriorityScoreCard',
    ({ value, label }) => {
      const widget = deriveWidgetPriorityGlyph(value);
      expect(widget).not.toBeUndefined();
      expect(widget?.label).toBe(label);
      expect(typeof widget?.color).toBe('string');
      expect(widget!.color.length).toBeGreaterThan(0);

      const rich = deriveRichPriorityGlyph(value);
      expect(rich).not.toBeUndefined();
      expect(rich?.label).toBe(label);

      expect(priorityChoiceLabel(value)).toBe(label);
    },
  );

  it('all 4 sprk_priority values map to distinct glyph colours (widget card) — acceptance: "distinctly colored"', () => {
    const distinctPriorityColors = new Set(PRIORITY_CASES.map(({ value }) => deriveWidgetPriorityGlyph(value)?.color));
    expect(distinctPriorityColors.size).toBe(PRIORITY_CASES.length);
  });
});

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

describe('effort badge + EffortScoreCard label — sprk_effort value mapping (FR-02/FR-03 UI)', () => {
  it.each(EFFORT_CASES)(
    'renders label "$label" for sprk_effort=$value on the widget KanbanCard, the rich KanbanCard, and EffortScoreCard',
    ({ value, label }) => {
      const widget = deriveWidgetEffortBadge(value);
      expect(widget).not.toBeUndefined();
      expect(widget?.label).toBe(label);
      expect(typeof widget?.style.backgroundColor).toBe('string');
      expect(typeof widget?.style.color).toBe('string');

      const rich = deriveRichEffortBadge(value);
      expect(rich).not.toBeUndefined();
      expect(rich?.label).toBe(label);

      expect(effortChoiceLabel(value)).toBe(label);
    },
  );

  it('all 5 sprk_effort values map to distinct badge tones (widget card)', () => {
    const distinctEffortColors = new Set(
      EFFORT_CASES.map(({ value }) => {
        const badge = deriveWidgetEffortBadge(value);
        return `${badge?.style.backgroundColor}|${badge?.style.color}`;
      }),
    );
    expect(distinctEffortColors.size).toBe(EFFORT_CASES.length);
  });
});

// ---------------------------------------------------------------------------
// Unset (null/undefined) and out-of-range values — neutral no-op, never a
// crash, never a misleading default colour (acceptance criterion 3).
// ---------------------------------------------------------------------------

describe('unset/out-of-range values — neutral no-op across all 6 lookup functions', () => {
  it.each([null, undefined, 999999999])(
    'is a no-op for %s — priority glyph, effort badge, and both score-card labels all return undefined, never throw',
    (value) => {
      expect(deriveWidgetPriorityGlyph(value)).toBeUndefined();
      expect(deriveRichPriorityGlyph(value)).toBeUndefined();
      expect(priorityChoiceLabel(value)).toBeUndefined();

      expect(deriveWidgetEffortBadge(value)).toBeUndefined();
      expect(deriveRichEffortBadge(value)).toBeUndefined();
      expect(effortChoiceLabel(value)).toBeUndefined();
    },
  );
});
