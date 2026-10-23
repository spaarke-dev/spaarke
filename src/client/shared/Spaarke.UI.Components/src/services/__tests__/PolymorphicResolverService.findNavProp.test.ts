/**
 * findNavProp — exact column first (ontology task 162, UAT round 1 of task 114).
 *
 * `sprk_document` has TWO lookups to each parent (`sprk_workassignment` + `sprk_relatedworkassignment`, and the same
 * for matter, project, invoice). Dataverse lists relationships in no stable order, and the old hint match was a
 * substring (`'workassignment'` is contained in BOTH column names), so a document was bound to whichever came first.
 * A hint equal to a column name must now win, in either order.
 */

import { findNavProp } from '../PolymorphicResolverService';
import type { INavPropEntry } from '../PolymorphicResolverService';

const PARENT: INavPropEntry = {
  columnName: 'sprk_workassignment',
  navPropName: 'sprk_WorkAssignment',
  referencedEntity: 'sprk_workassignment',
};
const RELATED: INavPropEntry = {
  columnName: 'sprk_relatedworkassignment',
  navPropName: 'sprk_RelatedWorkAssignment',
  referencedEntity: 'sprk_workassignment',
};

describe('findNavProp — exact column hint wins over a substring match', () => {
  it.each([
    ['parent column listed first', [PARENT, RELATED]],
    ['related column listed first', [RELATED, PARENT]],
  ])('returns the sprk_workassignment nav-prop for the hint "sprk_workassignment" (%s)', (_label, entries) => {
    expect(findNavProp(entries, 'sprk_workassignment', 'sprk_workassignment')).toBe('sprk_WorkAssignment');
  });

  it('matches the exact column case-insensitively', () => {
    expect(findNavProp([RELATED, PARENT], 'sprk_workassignment', 'SPRK_WorkAssignment')).toBe('sprk_WorkAssignment');
  });

  it('does not let an exact column of ANOTHER referenced entity win', () => {
    const other: INavPropEntry = { columnName: 'sprk_x', navPropName: 'sprk_X', referencedEntity: 'contact' };
    expect(findNavProp([other, RELATED, PARENT], 'sprk_workassignment', 'sprk_x')).toBe('sprk_RelatedWorkAssignment');
  });
});

describe('findNavProp — unchanged behaviour (must-not-fire)', () => {
  it('returns the only relationship to the entity whatever the hint', () => {
    expect(findNavProp([PARENT], 'sprk_workassignment', 'nonsense')).toBe('sprk_WorkAssignment');
    expect(findNavProp([PARENT], 'sprk_workassignment')).toBe('sprk_WorkAssignment');
  });

  it('still resolves a fragment hint by substring, first match', () => {
    // A fragment is not any column's full name, so there is no exact match: the old substring rule applies.
    expect(findNavProp([PARENT, RELATED], 'sprk_workassignment', 'related')).toBe('sprk_RelatedWorkAssignment');
    expect(findNavProp([RELATED, PARENT], 'sprk_workassignment', 'workassignment')).toBe('sprk_RelatedWorkAssignment');
  });

  it('falls back to the first relationship when the hint matches nothing, and returns undefined for no match', () => {
    expect(findNavProp([RELATED, PARENT], 'sprk_workassignment', 'zzz')).toBe('sprk_RelatedWorkAssignment');
    expect(findNavProp([RELATED, PARENT], 'sprk_matter', 'sprk_matter')).toBeUndefined();
  });
});
