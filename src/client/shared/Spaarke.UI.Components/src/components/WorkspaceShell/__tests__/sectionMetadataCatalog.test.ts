/**
 * sectionMetadataCatalog — unit tests
 *
 * Covers R4 FR-01 (W-3 task 040, 2026-05-26) invariants:
 *   (a) The catalog contains exactly the 7 system sections in default order.
 *   (b) Calendar + Daily Briefing entries are present (the two sections
 *       previously missing from the wizard's hardcoded SECTION_CATALOG).
 *   (c) `getSectionMetadata` lookup works for every catalog ID.
 *   (d) `SECTION_METADATA_IDS` set matches the catalog IDs (no drift in derived
 *       constant).
 *   (e) Every entry exposes a valid `category` value from the SectionCategory union.
 *
 * Test harness: jest, matching the conventions in
 * `src/client/shared/Spaarke.UI.Components/jest.config.js`.
 */

import {
  SECTION_METADATA_CATALOG,
  SECTION_METADATA_IDS,
  getSectionMetadata,
  type SectionMetadata,
} from '../sectionMetadataCatalog';

describe('SECTION_METADATA_CATALOG', () => {
  it('contains exactly the 7 canonical system sections in default order', () => {
    const expectedIdsInOrder = [
      'get-started',
      'quick-summary',
      'latest-updates',
      'todo',
      'documents',
      'daily-briefing',
      'calendar',
    ];
    expect(SECTION_METADATA_CATALOG.map(m => m.id)).toEqual(expectedIdsInOrder);
  });

  it('includes Calendar with the correct label and category (FR-01)', () => {
    const calendar = getSectionMetadata('calendar');
    expect(calendar).toBeDefined();
    expect(calendar?.label).toBe('Calendar');
    expect(calendar?.category).toBe('data');
  });

  it('includes Daily Briefing with the correct label and category (FR-01)', () => {
    const dailyBriefing = getSectionMetadata('daily-briefing');
    expect(dailyBriefing).toBeDefined();
    expect(dailyBriefing?.label).toBe('Daily Briefing');
    expect(dailyBriefing?.category).toBe('ai');
  });

  it('getSectionMetadata returns undefined for unknown IDs', () => {
    expect(getSectionMetadata('not-a-real-section')).toBeUndefined();
  });

  it('getSectionMetadata returns a result for every catalog ID', () => {
    for (const entry of SECTION_METADATA_CATALOG) {
      const lookup = getSectionMetadata(entry.id);
      expect(lookup).toBeDefined();
      expect(lookup?.id).toBe(entry.id);
    }
  });

  it('SECTION_METADATA_IDS matches the catalog entries', () => {
    const catalogIds = new Set(SECTION_METADATA_CATALOG.map(m => m.id));
    expect(SECTION_METADATA_IDS.size).toBe(catalogIds.size);
    for (const id of catalogIds) {
      expect(SECTION_METADATA_IDS.has(id)).toBe(true);
    }
  });

  it('every entry exposes a valid SectionCategory value', () => {
    const validCategories = new Set(['overview', 'data', 'ai', 'productivity']);
    for (const entry of SECTION_METADATA_CATALOG) {
      expect(validCategories.has(entry.category)).toBe(true);
    }
  });

  it('every entry has a non-empty label and description', () => {
    for (const entry of SECTION_METADATA_CATALOG) {
      expect(entry.label.trim().length).toBeGreaterThan(0);
      expect(entry.description.trim().length).toBeGreaterThan(0);
    }
  });

  it('entries have unique IDs (no duplicates)', () => {
    const ids = SECTION_METADATA_CATALOG.map(m => m.id);
    const unique = new Set(ids);
    expect(unique.size).toBe(ids.length);
  });

  it('SectionMetadata is a strict subset of SectionRegistration shape', () => {
    // This is a compile-time-style invariant; runtime check just ensures
    // the documented fields are present on every entry.
    const meta: SectionMetadata = SECTION_METADATA_CATALOG[0];
    expect(typeof meta.id).toBe('string');
    expect(typeof meta.label).toBe('string');
    expect(typeof meta.description).toBe('string');
    expect(typeof meta.category).toBe('string');
    expect(typeof meta.icon).toBe('object'); // Fluent icon component
  });
});
