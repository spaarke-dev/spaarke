/**
 * sectionMetadataCatalog — `widthPreference` field (FR-04 schema half)
 *
 * Covers task 014 (spaarke-dataset-grid-framework-r2 FR-04):
 *   (a) `SectionMetadata` accepts `widthPreference: 'full' | 'half' | 'any'` (compile-time).
 *   (b) 6 entity-list widgets have `widthPreference: 'full'` per owner clarification 2026-07-02:
 *       communications, documents, invoices, matters, projects, work-assignments.
 *   (c) Entries omitting the field are still valid (backward-compat).
 *
 * Runtime dev-guard + wizard placement warnings ship in task 015.
 */

import { SECTION_METADATA_CATALOG, getSectionMetadata, type SectionMetadata } from '../sectionMetadataCatalog';

describe('SectionMetadata.widthPreference (FR-04)', () => {
  // Type-level asserts: TypeScript compilation IS the test. If the field type is
  // wrong these `satisfies`/typed literals will fail `tsc`.
  it('accepts all three union values on the SectionMetadata type', () => {
    // Compile-time proof: these variables are valid SectionMetadata objects.
    const full: SectionMetadata = {
      id: '__type-test-full',
      label: 'x',
      description: 'x',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      icon: (() => null) as any,
      category: 'data',
      widthPreference: 'full',
    };
    const half: SectionMetadata = {
      id: '__type-test-half',
      label: 'x',
      description: 'x',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      icon: (() => null) as any,
      category: 'data',
      widthPreference: 'half',
    };
    const any_: SectionMetadata = {
      id: '__type-test-any',
      label: 'x',
      description: 'x',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      icon: (() => null) as any,
      category: 'data',
      widthPreference: 'any',
    };
    const omitted: SectionMetadata = {
      id: '__type-test-omitted',
      label: 'x',
      description: 'x',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      icon: (() => null) as any,
      category: 'data',
      // widthPreference intentionally omitted — backward-compat
    };
    expect(full.widthPreference).toBe('full');
    expect(half.widthPreference).toBe('half');
    expect(any_.widthPreference).toBe('any');
    expect(omitted.widthPreference).toBeUndefined();
  });

  it('sets widthPreference="full" on all 6 entity-list widgets (owner clarification 2026-07-02)', () => {
    const expectedFullIds = [
      'communications',
      'documents',
      'invoices',
      'matters',
      'projects',
      'work-assignments',
    ] as const;
    for (const id of expectedFullIds) {
      const entry = getSectionMetadata(id);
      expect(entry).toBeDefined();
      expect(entry?.widthPreference).toBe('full');
    }
  });

  it('leaves non-entity-list sections with widthPreference undefined (defaults to "any")', () => {
    // Non-entity-list sections should NOT have widthPreference set (defaults to 'any' semantics).
    const nonEntityListIds = ['get-started', 'quick-summary', 'latest-updates', 'todo', 'daily-briefing', 'calendar'];
    for (const id of nonEntityListIds) {
      const entry = getSectionMetadata(id);
      if (entry) {
        expect(entry.widthPreference).toBeUndefined();
      }
    }
  });

  it('every widthPreference value present in the catalog is one of the valid union members', () => {
    const validValues = new Set(['full', 'half', 'any']);
    for (const entry of SECTION_METADATA_CATALOG) {
      if (entry.widthPreference !== undefined) {
        expect(validValues.has(entry.widthPreference)).toBe(true);
      }
    }
  });
});
