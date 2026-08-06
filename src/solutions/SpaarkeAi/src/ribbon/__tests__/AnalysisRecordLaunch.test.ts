/**
 * AnalysisRecordLaunch.test.ts — ai-advanced-capabilities-analysis-hub-r1 task 052
 * ribbon-handler tests.
 *
 * Verifies the two `Sprk.SpaarkeAi.AnalysisRecordLaunch` ribbon handlers:
 *
 *   1. `openNewAnalysisFromRecord` — reads entityId/entityLogicalName from the open
 *      record's FormContext, then delegates to `openSpaarkeAi` with
 *      worktype + regarding=parent (entry case 2b), NEVER throws.
 *   2. `openExistingAnalysis` — delegates to `openSpaarkeAi` with analysisId
 *      (entry case 2d), NEVER throws.
 *
 * Banned-pattern compliance (ADR-038):
 *   - No `Mock<HttpMessageHandler>` (no fetch in this file).
 *   - No DI-registration tests.
 *   - No constructor null-check tests (this is a functions module).
 *
 * Test category: **Boundary contract test** (KEEP per ADR-038 §7 — verifies a
 * public seam where the ribbon SDK meets our code).
 *
 * @see src/solutions/SpaarkeAi/src/ribbon/AnalysisRecordLaunch.ts
 * @see projects/ai-advanced-capabilities-analysis-hub-r1/tasks/052-extend-openspaarkeai-ribbon-launcher.poml
 */

import '@testing-library/jest-dom';

// Mock the launch-resolver so we can assert on its inputs without firing
// Xrm.Navigation.navigateTo.
const mockOpenSpaarkeAi = jest.fn();
jest.mock('../../utils/launch-resolver', () => ({
  openSpaarkeAi: (params: unknown, target?: unknown) => mockOpenSpaarkeAi(params, target),
}));

import { openNewAnalysisFromRecord, openExistingAnalysis } from '../AnalysisRecordLaunch';

// ---------------------------------------------------------------------------
// FormContext mock factory
// ---------------------------------------------------------------------------

interface MockFormContext {
  data: {
    entity: {
      getId(): string;
      getEntityName(): string;
    };
  };
}

function makeFormContext(id: string, entityLogicalName = 'sprk_matter'): MockFormContext {
  return {
    data: {
      entity: {
        getId: () => id,
        getEntityName: () => entityLogicalName,
      },
    },
  };
}

describe('AnalysisRecordLaunch.openNewAnalysisFromRecord', () => {
  beforeEach(() => {
    mockOpenSpaarkeAi.mockClear();
  });

  test('ribbon new-in-record opens modal with worktype + regarding=parent (POML ui-test #1)', () => {
    openNewAnalysisFromRecord(
      makeFormContext('{matter-guid-1}', 'sprk_matter') as unknown as Xrm.FormContext,
      '100000000',
    );

    expect(mockOpenSpaarkeAi).toHaveBeenCalledTimes(1);
    expect(mockOpenSpaarkeAi).toHaveBeenCalledWith(
      {
        entityLogicalName: 'sprk_matter',
        entityId: 'matter-guid-1',
        worktype: '100000000',
        regarding: 'matter-guid-1',
      },
      2,
    );
  });

  test('works identically for sprk_project records', () => {
    openNewAnalysisFromRecord(
      makeFormContext('project-guid-1', 'sprk_project') as unknown as Xrm.FormContext,
      '100000001',
    );

    expect(mockOpenSpaarkeAi).toHaveBeenCalledWith(
      {
        entityLogicalName: 'sprk_project',
        entityId: 'project-guid-1',
        worktype: '100000001',
        regarding: 'project-guid-1',
      },
      2,
    );
  });

  test('strips braces from the record GUID', () => {
    openNewAnalysisFromRecord(
      makeFormContext('{F1A2B3C4-0000-1111-2222-333344445555}') as unknown as Xrm.FormContext,
      '100000000',
    );

    const [params] = mockOpenSpaarkeAi.mock.calls[0];
    expect(params.entityId).toBe('F1A2B3C4-0000-1111-2222-333344445555');
    expect(params.regarding).toBe('F1A2B3C4-0000-1111-2222-333344445555');
  });

  test('unsaved-record guard: empty record id (no GUID) logs a warning and does NOT throw, does not open (POML ui-test #3)', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() =>
      openNewAnalysisFromRecord(
        makeFormContext('') as unknown as Xrm.FormContext,
        '100000000',
      ),
    ).not.toThrow();

    expect(mockOpenSpaarkeAi).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('[AnalysisRecordLaunch]'),
    );

    warnSpy.mockRestore();
  });

  test('empty worktype guard: falsy worktype logs a warning and does NOT throw, does not open (avoids silently degrading to default cold-load)', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() =>
      openNewAnalysisFromRecord(
        makeFormContext('matter-guid-1') as unknown as Xrm.FormContext,
        '',
      ),
    ).not.toThrow();

    expect(mockOpenSpaarkeAi).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('[AnalysisRecordLaunch]'),
    );

    warnSpy.mockRestore();
  });

  test('defensive guard: getId() throwing logs a warning and does NOT throw, does not open', () => {
    const throwingFormContext = {
      data: {
        entity: {
          getId: () => {
            throw new Error('boom');
          },
          getEntityName: () => 'sprk_matter',
        },
      },
    };
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() =>
      openNewAnalysisFromRecord(
        throwingFormContext as unknown as Xrm.FormContext,
        '100000000',
      ),
    ).not.toThrow();

    expect(mockOpenSpaarkeAi).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalled();

    warnSpy.mockRestore();
  });
});

describe('AnalysisRecordLaunch.openExistingAnalysis', () => {
  beforeEach(() => {
    mockOpenSpaarkeAi.mockClear();
  });

  test('open existing passes analysisId (POML ui-test #2)', () => {
    openExistingAnalysis('analysis-guid-1');

    expect(mockOpenSpaarkeAi).toHaveBeenCalledTimes(1);
    expect(mockOpenSpaarkeAi).toHaveBeenCalledWith({ analysisId: 'analysis-guid-1' }, 2);
  });

  test('strips braces from the analysis GUID', () => {
    openExistingAnalysis('{A1B2C3D4-0000-1111-2222-333344445555}');

    expect(mockOpenSpaarkeAi).toHaveBeenCalledWith(
      { analysisId: 'A1B2C3D4-0000-1111-2222-333344445555' },
      2,
    );
  });

  test('empty analysisId (no selection) logs a warning and does NOT throw, does not open', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() => openExistingAnalysis('')).not.toThrow();

    expect(mockOpenSpaarkeAi).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('[AnalysisRecordLaunch]'),
    );

    warnSpy.mockRestore();
  });
});
