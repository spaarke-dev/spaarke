/**
 * configResolution.availableViews.test — unit tests for `filterAvailableViews`.
 *
 * Covers spaarke-dataset-grid-framework-r2 task 002 (FR-05) acceptance criteria:
 *   - `availableViews` unset (undefined) → all views returned (back-compat).
 *   - `availableViews` set with a non-empty allowlist → intersection returned.
 *   - `availableViews` set with 0 matching IDs → empty array returned.
 *   - `availableViews` set as an empty array → treated as "no filter"
 *     (documented decision — safer default to avoid the empty-picker footgun).
 *   - GUID comparison is case-insensitive and tolerates braces.
 *
 * MAINTAIN-class per ADR-038 § 7 (framework-contract test on a pure function).
 * Does NOT mock HTTP, DI, or any React surface — the pure filter helper is
 * exercised directly.
 *
 * @see configResolution.ts `filterAvailableViews`
 * @see DataGridConfiguration.ts `SourceSavedQuery.availableViews`
 * @see spec.md § FR-05
 */

import { filterAvailableViews } from '../configResolution';
import type { SavedQuerySummary } from '../../../services/IDataverseClient';

/** Test fixture: 5 sibling savedqueries returned by Dataverse. */
const fiveViews: SavedQuerySummary[] = [
  { id: 'view-1', name: 'Active', isDefault: true, queryType: 0 },
  { id: 'view-2', name: 'Inactive', isDefault: false, queryType: 0 },
  { id: 'view-3', name: 'My Records', isDefault: false, queryType: 0 },
  { id: 'view-4', name: 'Team Records', isDefault: false, queryType: 0 },
  { id: 'view-5', name: 'All Records', isDefault: false, queryType: 0 },
];

describe('filterAvailableViews — FR-05 config-level allowlist', () => {
  describe('absent allowlist (back-compat)', () => {
    it('returns views verbatim when allowlist is undefined', () => {
      const result = filterAvailableViews(fiveViews, undefined);
      expect(result).toEqual(fiveViews);
    });

    it('returns the same reference (or an equal list) — no filter applied', () => {
      const result = filterAvailableViews(fiveViews, undefined);
      expect(result).toHaveLength(5);
      expect(result.map(v => v.id)).toEqual(['view-1', 'view-2', 'view-3', 'view-4', 'view-5']);
    });
  });

  describe('empty-array allowlist (documented as "no filter")', () => {
    it('returns views verbatim when allowlist is an empty array', () => {
      // Empty-array semantics decision (FR-05 owner note 2026-07-02):
      // treat `[]` as "no filter" (identical to undefined) rather than
      // "hide all". This is a safer default that avoids the accidental
      // empty-picker footgun. Makers who genuinely want to hide all
      // sibling views should omit the entire savedquery source or use
      // a `savedquery` type with only the primary view (no ViewSelector).
      const result = filterAvailableViews(fiveViews, []);
      expect(result).toEqual(fiveViews);
    });
  });

  describe('non-empty allowlist (allowlist applied)', () => {
    it('returns only the intersection when 2 IDs match out of 5', () => {
      const result = filterAvailableViews(fiveViews, ['view-1', 'view-3']);
      expect(result).toHaveLength(2);
      expect(result.map(v => v.id)).toEqual(['view-1', 'view-3']);
    });

    it('preserves Dataverse ordering (not allowlist ordering)', () => {
      // Allowlist declared in reverse order — result should still follow
      // the Dataverse ordering (view-1 before view-3).
      const result = filterAvailableViews(fiveViews, ['view-3', 'view-1']);
      expect(result.map(v => v.id)).toEqual(['view-1', 'view-3']);
    });

    it('returns an empty array when no allowlist IDs match', () => {
      const result = filterAvailableViews(fiveViews, ['nonexistent-1', 'nonexistent-2']);
      expect(result).toEqual([]);
    });

    it('returns a single-element list when only one allowlist ID matches', () => {
      const result = filterAvailableViews(fiveViews, ['view-4']);
      expect(result).toHaveLength(1);
      expect(result[0].id).toBe('view-4');
    });

    it('handles a partial-match allowlist (some match, some do not)', () => {
      const result = filterAvailableViews(fiveViews, ['view-2', 'nonexistent', 'view-5']);
      expect(result.map(v => v.id)).toEqual(['view-2', 'view-5']);
    });
  });

  describe('GUID comparison is case-insensitive and brace-tolerant', () => {
    it('matches when allowlist uses uppercase GUIDs and views use lowercase', () => {
      const upper: SavedQuerySummary[] = [
        { id: 'abc-123', name: 'A', isDefault: true, queryType: 0 },
        { id: 'def-456', name: 'B', isDefault: false, queryType: 0 },
      ];
      const result = filterAvailableViews(upper, ['ABC-123']);
      expect(result).toHaveLength(1);
      expect(result[0].id).toBe('abc-123');
    });

    it('strips braces from allowlist GUIDs before comparing', () => {
      const views: SavedQuerySummary[] = [
        { id: '11111111-1111-1111-1111-111111111111', name: 'A', isDefault: true, queryType: 0 },
      ];
      const result = filterAvailableViews(views, ['{11111111-1111-1111-1111-111111111111}']);
      expect(result).toHaveLength(1);
    });
  });

  describe('edge cases', () => {
    it('returns an empty array when views is empty and allowlist is undefined', () => {
      expect(filterAvailableViews([], undefined)).toEqual([]);
    });

    it('returns an empty array when views is empty and allowlist is set', () => {
      expect(filterAvailableViews([], ['view-1'])).toEqual([]);
    });
  });
});
