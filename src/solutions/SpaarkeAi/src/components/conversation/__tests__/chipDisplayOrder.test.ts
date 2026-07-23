/**
 * chipDisplayOrder tests — task 043 / FR-G1 (ADR-039 display-layer reorder).
 *
 * Covers:
 *   - deterministic: same input → same output, repeatedly
 *   - preference-keyed: sort key comes ONLY from `preferredBindingOrder`,
 *     never from chip labels/content
 *   - no-preference fallback: returns the server-declared order UNCHANGED
 *     (the documented client-preference-source GAP)
 *   - never adds/removes/mutates chips — pure reorder of the SAME array
 *   - stable tiebreak for unranked/tied chips (original relative order)
 */

import type { ConsumerChip } from '@spaarke/ui-components';
import { reorderChipsForDisplay } from '../chipDisplayOrder';

const chips: ConsumerChip[] = [
  { bindingId: 'b-summarize', label: 'Summarize all?' },
  { bindingId: 'b-create-matter', label: 'Create matter' },
  { bindingId: 'b-draft-email', label: 'Draft an email' },
];

describe('reorderChipsForDisplay', () => {
  it('returns chips UNCHANGED (server-declared order) when no preference is supplied', () => {
    expect(reorderChipsForDisplay(chips)).toEqual(chips);
    expect(reorderChipsForDisplay(chips, null)).toEqual(chips);
    expect(reorderChipsForDisplay(chips, {})).toEqual(chips);
    expect(reorderChipsForDisplay(chips, { preferredBindingOrder: [] })).toEqual(chips);
  });

  it('is deterministic: the SAME chips + preference always produce the SAME order', () => {
    const preference = { preferredBindingOrder: ['b-draft-email', 'b-summarize'] };
    const first = reorderChipsForDisplay(chips, preference);
    const second = reorderChipsForDisplay(chips, preference);
    expect(first.map((c) => c.bindingId)).toEqual(second.map((c) => c.bindingId));
    expect(first.map((c) => c.bindingId)).toEqual(['b-draft-email', 'b-summarize', 'b-create-matter']);
  });

  it('is preference-KEYED: ranks strictly by bindingId position in preferredBindingOrder, not by label content', () => {
    // Deliberately reversed alphabetical/semantic expectation — proves the
    // sort key is the preference list, not any re-derived heuristic.
    const preference = { preferredBindingOrder: ['b-create-matter'] };
    const result = reorderChipsForDisplay(chips, preference);
    expect(result[0].bindingId).toBe('b-create-matter');
    // Unranked chips keep their original relative order, appended after.
    expect(result.slice(1).map((c) => c.bindingId)).toEqual(['b-summarize', 'b-draft-email']);
  });

  it('never adds, removes, or grants a chip — output is a permutation of the SAME input chips', () => {
    const preference = { preferredBindingOrder: ['not-a-real-binding-id', 'b-summarize'] };
    const result = reorderChipsForDisplay(chips, preference);
    expect(result).toHaveLength(chips.length);
    expect([...result].sort((a, b) => a.bindingId.localeCompare(b.bindingId))).toEqual(
      [...chips].sort((a, b) => a.bindingId.localeCompare(b.bindingId))
    );
  });

  it('stably tiebreaks unranked/tied chips by their original relative order', () => {
    const preference = { preferredBindingOrder: ['b-draft-email'] };
    const result = reorderChipsForDisplay(chips, preference);
    expect(result.map((c) => c.bindingId)).toEqual(['b-draft-email', 'b-summarize', 'b-create-matter']);
  });

  it('does not mutate the input array', () => {
    const original = [...chips];
    reorderChipsForDisplay(chips, { preferredBindingOrder: ['b-draft-email'] });
    expect(chips).toEqual(original);
  });
});
