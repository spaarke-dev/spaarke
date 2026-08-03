/**
 * ensureAssociationColumns.test.ts
 *
 * Unit tests for the FetchXML augmentation helper that keeps the left-list
 * association status dot (🔴/🟡/🟢) resolvable regardless of a maker's column
 * set (email-communication-solution-r5, owner UAT 2026-08-03 Item 3). jsdom
 * supplies `DOMParser`/`XMLSerializer`. Covers: injection into a view that
 * omits the columns, idempotence when a column is already present, the
 * `<all-attributes/>` no-op, and the malformed-XML degrade-to-original path.
 */
import { ensureAssociationColumns } from '../ensureAssociationColumns';

/** Count non-overlapping occurrences of `needle` in `haystack`. */
function countOccurrences(haystack: string, needle: string): number {
  return haystack.split(needle).length - 1;
}

describe('ensureAssociationColumns', () => {
  it('injects the association columns into a view that omits them', () => {
    const fetchXml = `<fetch><entity name="sprk_communication"><attribute name="sprk_subject" /><attribute name="sprk_from" /></entity></fetch>`;

    const result = ensureAssociationColumns(fetchXml);

    // Denormalized name + raw status + at least one typed regarding lookup.
    expect(result).toContain('sprk_regardingrecordname');
    expect(result).toContain('sprk_associationstatus');
    expect(result).toContain('sprk_regardingmatter');
    // Original columns are preserved.
    expect(result).toContain('sprk_subject');
    expect(result).toContain('sprk_from');
  });

  it('does not duplicate an association column that is already selected', () => {
    const fetchXml = `<fetch><entity name="sprk_communication"><attribute name="sprk_regardingrecordname" /></entity></fetch>`;

    const result = ensureAssociationColumns(fetchXml);

    expect(countOccurrences(result, 'name="sprk_regardingrecordname"')).toBe(1);
  });

  it('returns an <all-attributes/> fetch unchanged (every column already selected)', () => {
    const fetchXml = `<fetch><entity name="sprk_communication"><all-attributes /></entity></fetch>`;

    expect(ensureAssociationColumns(fetchXml)).toBe(fetchXml);
  });

  it('returns malformed XML unchanged (degrade to the maker view, never throw)', () => {
    const malformed = `<fetch><entity name="sprk_communication"><attribute name="sprk_subject"></fetch>`;

    expect(ensureAssociationColumns(malformed)).toBe(malformed);
  });
});
