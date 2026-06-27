/**
 * Unit tests for `targetEntityNormalize.ts`.
 *
 * Coverage:
 *   - `normalizeTargetEntityLabel` — each known label maps to its expected
 *     wire form; empty/whitespace inputs return null.
 *   - `buildSearchRequestFragment` — "All" row produces `scope: 'all'`,
 *     entity rows produce `scope: 'entity' + entityType`. Both flavors
 *     include the `searchIndexName`.
 *   - `ALL_SENTINEL` value is the string literal `"all"`.
 */

import {
  ALL_SENTINEL,
  buildSearchRequestFragment,
  normalizeTargetEntityLabel,
} from '../../services/targetEntityNormalize';
import type { AiSearchIndexRow } from '../../services/aiSearchIndexService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeRow(overrides: Partial<AiSearchIndexRow> = {}): AiSearchIndexRow {
  return {
    sprk_aisearchindexid: '00000000-0000-0000-0000-000000000000',
    sprk_displayname: 'Test Row',
    sprk_searchindexname: 'spaarke-test-index',
    sprk_targetentitytype: 100000000,
    sprk_targetentitytypeLabel: 'All',
    sprk_isdefault: false,
    sprk_displayorder: 10,
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('targetEntityNormalize', () => {
  describe('ALL_SENTINEL', () => {
    it('is the string literal "all"', () => {
      expect(ALL_SENTINEL).toBe('all');
    });
  });

  describe('normalizeTargetEntityLabel', () => {
    describe('known Choice labels', () => {
      it('"All" → "all"', () => {
        expect(normalizeTargetEntityLabel('All')).toBe('all');
      });

      it('"Matter" → "matter"', () => {
        expect(normalizeTargetEntityLabel('Matter')).toBe('matter');
      });

      it('"Project" → "project"', () => {
        expect(normalizeTargetEntityLabel('Project')).toBe('project');
      });

      it('"Invoice" → "invoice"', () => {
        expect(normalizeTargetEntityLabel('Invoice')).toBe('invoice');
      });

      it('"Event" → "event"', () => {
        expect(normalizeTargetEntityLabel('Event')).toBe('event');
      });

      it('"Work Assignment" → "workassignment" (strips space)', () => {
        expect(normalizeTargetEntityLabel('Work Assignment')).toBe('workassignment');
      });

      it('"Document" → "document"', () => {
        expect(normalizeTargetEntityLabel('Document')).toBe('document');
      });
    });

    describe('case insensitivity', () => {
      it('"MATTER" → "matter"', () => {
        expect(normalizeTargetEntityLabel('MATTER')).toBe('matter');
      });

      it('"matter" → "matter"', () => {
        expect(normalizeTargetEntityLabel('matter')).toBe('matter');
      });

      it('"mAtTeR" → "matter"', () => {
        expect(normalizeTargetEntityLabel('mAtTeR')).toBe('matter');
      });
    });

    describe('whitespace handling', () => {
      it('strips internal spaces', () => {
        expect(normalizeTargetEntityLabel('Work Assignment')).toBe('workassignment');
      });

      it('strips multiple consecutive spaces', () => {
        expect(normalizeTargetEntityLabel('Work   Assignment')).toBe('workassignment');
      });

      it('strips tabs and newlines (treated as whitespace)', () => {
        expect(normalizeTargetEntityLabel('Work\tAssignment\n')).toBe('workassignment');
      });

      it('strips leading and trailing whitespace', () => {
        expect(normalizeTargetEntityLabel('  Matter  ')).toBe('matter');
      });
    });

    describe('boundary cases', () => {
      it('null input → null', () => {
        expect(normalizeTargetEntityLabel(null)).toBeNull();
      });

      it('undefined input → null', () => {
        expect(normalizeTargetEntityLabel(undefined)).toBeNull();
      });

      it('empty string → null', () => {
        expect(normalizeTargetEntityLabel('')).toBeNull();
      });

      it('whitespace-only string → null', () => {
        expect(normalizeTargetEntityLabel('   ')).toBeNull();
      });

      it('tab + newline only → null', () => {
        expect(normalizeTargetEntityLabel('\t\n')).toBeNull();
      });

      it('non-string input → null (defensive)', () => {
        // Cast through unknown so the test demonstrates the runtime guard.
        expect(normalizeTargetEntityLabel(42 as unknown as string)).toBeNull();
      });
    });

    describe('future-proofing', () => {
      it('handles new labels without code change ("Contract" → "contract")', () => {
        expect(normalizeTargetEntityLabel('Contract')).toBe('contract');
      });

      it('handles multi-word new labels ("Account Plan" → "accountplan")', () => {
        expect(normalizeTargetEntityLabel('Account Plan')).toBe('accountplan');
      });
    });
  });

  describe('buildSearchRequestFragment', () => {
    describe('"All" sentinel row', () => {
      it('produces scope="all" with searchIndexName and NO entityType', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'All',
          sprk_searchindexname: 'spaarke-records-index',
        });

        const result = buildSearchRequestFragment(row);

        expect(result).toEqual({
          scope: 'all',
          searchIndexName: 'spaarke-records-index',
        });
        expect(result.entityType).toBeUndefined();
      });

      it('treats "ALL" (uppercase) as the All sentinel too', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'ALL',
          sprk_searchindexname: 'spaarke-records-index',
        });

        const result = buildSearchRequestFragment(row);

        expect(result.scope).toBe('all');
        expect(result.entityType).toBeUndefined();
      });
    });

    describe('entity-specific rows', () => {
      it('Matter row → scope="entity", entityType="matter"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Matter',
          sprk_searchindexname: 'spaarke-records-index',
        });

        const result = buildSearchRequestFragment(row);

        expect(result).toEqual({
          scope: 'entity',
          entityType: 'matter',
          searchIndexName: 'spaarke-records-index',
        });
      });

      it('Project row → scope="entity", entityType="project"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Project',
          sprk_searchindexname: 'spaarke-records-index',
        });

        expect(buildSearchRequestFragment(row)).toEqual({
          scope: 'entity',
          entityType: 'project',
          searchIndexName: 'spaarke-records-index',
        });
      });

      it('Invoice row → scope="entity", entityType="invoice"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Invoice',
          sprk_searchindexname: 'spaarke-records-index',
        });

        expect(buildSearchRequestFragment(row)).toEqual({
          scope: 'entity',
          entityType: 'invoice',
          searchIndexName: 'spaarke-records-index',
        });
      });

      it('Work Assignment row → scope="entity", entityType="workassignment"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Work Assignment',
          sprk_searchindexname: 'spaarke-records-index',
        });

        expect(buildSearchRequestFragment(row)).toEqual({
          scope: 'entity',
          entityType: 'workassignment',
          searchIndexName: 'spaarke-records-index',
        });
      });

      it('Event row → scope="entity", entityType="event"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Event',
          sprk_searchindexname: 'spaarke-records-index',
        });

        expect(buildSearchRequestFragment(row)).toEqual({
          scope: 'entity',
          entityType: 'event',
          searchIndexName: 'spaarke-records-index',
        });
      });

      it('Document row → scope="entity", entityType="document"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: 'Document',
          sprk_searchindexname: 'spaarke-file-index',
        });

        expect(buildSearchRequestFragment(row)).toEqual({
          scope: 'entity',
          entityType: 'document',
          searchIndexName: 'spaarke-file-index',
        });
      });
    });

    describe('edge cases', () => {
      it('empty label → defensive default of scope="all" with searchIndexName preserved', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: '',
          sprk_searchindexname: 'spaarke-records-index',
        });

        const result = buildSearchRequestFragment(row);

        expect(result.scope).toBe('all');
        expect(result.searchIndexName).toBe('spaarke-records-index');
        expect(result.entityType).toBeUndefined();
      });

      it('whitespace-only label → defensive default of scope="all"', () => {
        const row = makeRow({
          sprk_targetentitytypeLabel: '   ',
          sprk_searchindexname: 'spaarke-records-index',
        });

        const result = buildSearchRequestFragment(row);

        expect(result.scope).toBe('all');
        expect(result.entityType).toBeUndefined();
      });
    });
  });
});
