/**
 * findNavProp — exact column first (ontology task 162, UAT round 1 of task 114).
 *
 * `sprk_document` has TWO lookups to each parent (`sprk_workassignment` + `sprk_relatedworkassignment`, and the same
 * for matter, project, invoice). The document-bind bug was in the CALLERS: they passed a fragment (`'workassignment'`)
 * that both column names contain, or no column at all, so the bind went to whichever relationship Dataverse listed
 * first. The callers now pass the full column name, and `workAssignmentService.documentBind.test.ts` /
 * `invoiceService.documentBind.test.ts` prove that end to end.
 *
 * What THIS file pins is the helper's own rule: an entry whose column EQUALS the hint wins over an earlier entry whose
 * column merely CONTAINS it (`sprk_workassignment_x` before `sprk_workassignment`), case-insensitively. Note the full
 * hint `sprk_workassignment` is NOT a substring of `sprk_relatedworkassignment`, so the plain parent/related ordering
 * cases below pass on the old substring code too; they document the caller contract, and the `_x` case is the one that
 * proves exact-first.
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

  it('prefers the column that EQUALS the hint over an earlier column that merely contains it', () => {
    // Fails on the old substring-only rule: `sprk_workassignment_x` contains the hint and is listed first.
    const lookalike: INavPropEntry = {
      columnName: 'sprk_workassignment_x',
      navPropName: 'X',
      referencedEntity: 'sprk_workassignment',
    };
    expect(findNavProp([lookalike, PARENT], 'sprk_workassignment', 'sprk_workassignment')).toBe('sprk_WorkAssignment');
    expect(findNavProp([PARENT, lookalike], 'sprk_workassignment', 'sprk_workassignment')).toBe('sprk_WorkAssignment');
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
