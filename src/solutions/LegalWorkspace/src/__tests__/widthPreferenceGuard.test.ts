/**
 * FR-04 (task 015) — Runtime dev-guard unit tests
 *
 * Covers the runtime half of FR-04 (spaarke-dataset-grid-framework-r2):
 * `warnOnWidthPreferenceViolations` in `sectionRegistry.ts` — fires
 * `console.warn` when a section metadata has `widthPreference: 'full'` but
 * appears in a multi-column row of the parsed LayoutJson.
 *
 * Acceptance criteria from the POML:
 *
 *   (a) 'full' widget in a multi-column row (columns='1fr 1fr') → warn called
 *   (b) 'full' widget in a single-column row (columns='1fr')  → NO warn
 *   (c) 'half' or 'any' widget in any row                     → NO warn
 *   (d) NODE_ENV === 'production' → NO warn regardless of layout
 *   (e) Unknown section ID (not in SECTION_METADATA_CATALOG)  → NO warn
 *   (f) SectionInstance-shaped entry (object with `id`)       → same guard applies
 *
 * NOTE — deferred test-runner setup: `src/solutions/LegalWorkspace/` does NOT
 * yet have jest configured. This file is scaffolded to follow the same pattern
 * as `src/solutions/WorkspaceLayoutWizard/src/__tests__/rowHeight.test.tsx`
 * (task 011 / task 013 precedent) so it runs unchanged once a runner is wired.
 * The POML's output path `WorkspaceShell/__tests__/widthPreferenceGuard.test.ts`
 * is not viable because the guard code lives in LegalWorkspace, not the shared
 * lib — the test is placed adjacent to the code it tests instead. Documented
 * as a minor POML-deviation in the task 015 completion report.
 *
 * @see ../sectionRegistry.ts — warnOnWidthPreferenceViolations (unit under test)
 * @see docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md § FR-04
 */

import {
  warnOnWidthPreferenceViolations,
  type LayoutJsonLike,
} from '../sectionRegistry';

// ---------------------------------------------------------------------------
// Fixtures — 'documents' (widthPreference: 'full') + 'get-started' (no pref)
// are canonical entries in SECTION_METADATA_CATALOG per task 014. Using them
// avoids the need to stub the catalog, so the test exercises the real lookup.
// ---------------------------------------------------------------------------

const FULL_PREF_SECTION_ID = 'documents';    // widthPreference: 'full' per task 014
const NEUTRAL_SECTION_ID   = 'get-started';  // widthPreference omitted → 'any'
const UNKNOWN_SECTION_ID   = '__unknown-section-x__'; // not in catalog

/** Build a minimal LayoutJsonLike with one row, N columns, N section entries. */
function makeLayout(
  columns: string,
  sections: ReadonlyArray<string | { id: string }>,
  rowId = 'row-1',
): LayoutJsonLike {
  return {
    rows: [
      {
        id: rowId,
        columns,
        sections,
      },
    ],
  };
}

// Preserve original NODE_ENV so we can safely mutate + restore per-test.
const ORIGINAL_NODE_ENV = process.env.NODE_ENV;

describe('warnOnWidthPreferenceViolations — FR-04 runtime dev-guard (task 015)', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    // Reset to a known dev-mode baseline before each test.
    process.env.NODE_ENV = 'development';
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    warnSpy.mockRestore();
    process.env.NODE_ENV = ORIGINAL_NODE_ENV;
  });

  // -------------------------------------------------------------------------
  // (a) 'full' section in multi-column row → warn
  // -------------------------------------------------------------------------

  it('fullSectionInMultiColumnRow_DevMode_LogsConsoleWarn', () => {
    const layout = makeLayout('1fr 1fr', [FULL_PREF_SECTION_ID, NEUTRAL_SECTION_ID]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).toHaveBeenCalledTimes(1);
    const msg = warnSpy.mock.calls[0][0] as string;
    expect(msg).toContain('[Spaarke DataGrid]');
    expect(msg).toContain(FULL_PREF_SECTION_ID);
    expect(msg).toContain('widthPreference:full');
    expect(msg).toContain('multi-column');
  });

  it('fullSectionInThreeColumnRow_DevMode_LogsSlotCountInWarning', () => {
    const layout = makeLayout('1fr 1fr 1fr', [FULL_PREF_SECTION_ID, '', '']);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0][0]).toContain('3 columns');
  });

  it('fullSectionInRepeatShorthandRow_DevMode_LogsCorrectSlotCount', () => {
    const layout = makeLayout('repeat(4, 1fr)', [FULL_PREF_SECTION_ID, '', '', '']);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0][0]).toContain('4 columns');
  });

  // -------------------------------------------------------------------------
  // (b) 'full' section in single-column row → NO warn
  // -------------------------------------------------------------------------

  it('fullSectionInSingleColumnRow_DevMode_DoesNotWarn', () => {
    const layout = makeLayout('1fr', [FULL_PREF_SECTION_ID]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // (c) Non-'full' widthPreference → NO warn
  // -------------------------------------------------------------------------

  it('neutralPrefSectionInMultiColumnRow_DevMode_DoesNotWarn', () => {
    const layout = makeLayout('1fr 1fr', [NEUTRAL_SECTION_ID, NEUTRAL_SECTION_ID]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // (d) NODE_ENV === 'production' → NO warn regardless
  // -------------------------------------------------------------------------

  it('fullSectionInMultiColumnRow_ProductionMode_DoesNotWarn', () => {
    process.env.NODE_ENV = 'production';
    const layout = makeLayout('1fr 1fr', [FULL_PREF_SECTION_ID, NEUTRAL_SECTION_ID]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // (e) Unknown section ID → NO warn (skip, not error)
  // -------------------------------------------------------------------------

  it('unknownSectionId_DevMode_DoesNotWarn', () => {
    const layout = makeLayout('1fr 1fr', [UNKNOWN_SECTION_ID, UNKNOWN_SECTION_ID]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // (f) SectionInstance-shaped entry → same guard applies
  // -------------------------------------------------------------------------

  it('sectionInstanceEntry_FullPrefInMultiColumnRow_DevMode_LogsConsoleWarn', () => {
    // FR-03 SectionInstance shape: `{ id: string; ... }`. The guard extracts the
    // ID and treats it identically to a bare-string entry.
    const layout = makeLayout('1fr 1fr', [
      { id: FULL_PREF_SECTION_ID },
      NEUTRAL_SECTION_ID,
    ]);
    warnOnWidthPreferenceViolations(layout);

    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0][0]).toContain(FULL_PREF_SECTION_ID);
  });

  // -------------------------------------------------------------------------
  // Defensive input handling
  // -------------------------------------------------------------------------

  it('nullLayout_DevMode_DoesNotThrowOrWarn', () => {
    expect(() => warnOnWidthPreferenceViolations(null)).not.toThrow();
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('undefinedLayout_DevMode_DoesNotThrowOrWarn', () => {
    expect(() => warnOnWidthPreferenceViolations(undefined)).not.toThrow();
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('emptyRowsArray_DevMode_DoesNotWarn', () => {
    warnOnWidthPreferenceViolations({ rows: [] });
    expect(warnSpy).not.toHaveBeenCalled();
  });

  it('emptyStringSectionEntry_DevMode_DoesNotWarn', () => {
    // Empty sections (from task 091 tolerance) should be skipped, not looked up.
    const layout = makeLayout('1fr 1fr', ['', '']);
    warnOnWidthPreferenceViolations(layout);
    expect(warnSpy).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // Multi-row layout — warn fires per-violation, one warn per bad section
  // -------------------------------------------------------------------------

  it('multipleFullSectionsInSameMultiColumnRow_LogsOneWarnPerSection', () => {
    // A hypothetical layout with two 'full' widgets in a 2-column row would fire
    // once per section. Documents + Documents-like case doesn't repeat the same
    // ID (rare), but we simulate with two distinct 'full' entities.
    const layout = makeLayout('1fr 1fr', ['documents', 'communications']);
    warnOnWidthPreferenceViolations(layout);
    // Both 'documents' and 'communications' have widthPreference: 'full' per task 014.
    expect(warnSpy).toHaveBeenCalledTimes(2);
  });

  it('multipleRowsWithMixedViolations_WarnsOnlyForActualViolations', () => {
    const layout: LayoutJsonLike = {
      rows: [
        // Row 1: violation (full in multi-col)
        { id: 'row-1', columns: '1fr 1fr', sections: [FULL_PREF_SECTION_ID, NEUTRAL_SECTION_ID] },
        // Row 2: OK (full in single-col)
        { id: 'row-2', columns: '1fr', sections: [FULL_PREF_SECTION_ID] },
        // Row 3: OK (neutral in multi-col)
        { id: 'row-3', columns: '1fr 1fr', sections: [NEUTRAL_SECTION_ID, NEUTRAL_SECTION_ID] },
      ],
    };
    warnOnWidthPreferenceViolations(layout);
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0][0]).toContain('row-1');
  });
});
