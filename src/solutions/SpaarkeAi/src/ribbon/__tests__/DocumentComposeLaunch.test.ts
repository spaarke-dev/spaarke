/**
 * DocumentComposeLaunch.test.ts — spaarkeai-compose-r1 task 046 ribbon-handler tests.
 *
 * Verifies the `Sprk.SpaarkeAi.DocumentComposeLaunch.openInCompose` ribbon
 * handler does its three jobs:
 *
 *   1. Reads `sprk_documentid` from the open record's FormContext.
 *   2. Fetches `sprk_graphitemid` (+ optional drive id + display name) via
 *      `Xrm.WebApi.retrieveRecord`.
 *   3. Delegates to `openSpaarkeAiCompose` with the assembled params, NEVER
 *      throws (even on failure paths — graceful empty-state fallback).
 *
 * Banned-pattern compliance (ADR-038):
 *   - No `Mock<HttpMessageHandler>` — Xrm.WebApi is the platform boundary,
 *     mocked here legitimately (SDK is not under our control).
 *   - No DI-registration tests.
 *   - No constructor null-checks (this is a functions module).
 *
 * Test category: **Boundary contract test** (KEEP per ADR-038 §7 — verifies
 * a public seam where the ribbon SDK meets our code).
 *
 * @see src/solutions/SpaarkeAi/src/ribbon/DocumentComposeLaunch.ts
 * @see projects/spaarkeai-compose-r1/tasks/046-frontend-wire-modal-launch.poml
 */

import '@testing-library/jest-dom';

// Mock the launch-resolver so we can assert on its inputs without firing
// Xrm.Navigation.navigateTo.
const mockOpenSpaarkeAiCompose = jest.fn();
jest.mock('../../utils/launch-resolver', () => ({
  openSpaarkeAiCompose: (params: unknown) => mockOpenSpaarkeAiCompose(params),
}));

import { openInCompose } from '../DocumentComposeLaunch';

// ---------------------------------------------------------------------------
// Xrm.WebApi mock
// ---------------------------------------------------------------------------

interface MockWebApi {
  retrieveRecord: jest.Mock<Promise<Record<string, unknown>>, [string, string, string?]>;
}
interface MockXrm {
  WebApi: MockWebApi;
}

function installXrmMock(webApi?: Partial<MockWebApi>): MockWebApi {
  const wa: MockWebApi = {
    retrieveRecord: webApi?.retrieveRecord
      ?? jest.fn().mockResolvedValue({}),
  };
  (globalThis as unknown as { Xrm: MockXrm }).Xrm = { WebApi: wa };
  return wa;
}
function uninstallXrmMock(): void {
  delete (globalThis as Partial<{ Xrm: unknown }>).Xrm;
}

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

function makeFormContext(id: string): MockFormContext {
  return {
    data: {
      entity: {
        getId: () => id,
        getEntityName: () => 'sprk_document',
      },
    },
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('DocumentComposeLaunch.openInCompose', () => {
  beforeEach(() => {
    mockOpenSpaarkeAiCompose.mockClear();
  });
  afterEach(() => {
    uninstallXrmMock();
  });

  test('happy path: fetches SPE pointer + delegates to openSpaarkeAiCompose with full document context', async () => {
    installXrmMock({
      retrieveRecord: jest.fn().mockResolvedValue({
        sprk_documentid: 'doc-guid-1',
        sprk_graphitemid: '01DRIVEITEM',
        sprk_driveid: 'b!XYZ',
        sprk_displayname: 'Acme MSA.docx',
      }),
    });

    await openInCompose(makeFormContext('{doc-guid-1}') as unknown as Xrm.FormContext);

    expect(mockOpenSpaarkeAiCompose).toHaveBeenCalledTimes(1);
    expect(mockOpenSpaarkeAiCompose).toHaveBeenCalledWith({
      entityLogicalName: 'sprk_document',
      entityId: 'doc-guid-1',
      sprkDocumentId: 'doc-guid-1',
      speDriveItemId: '01DRIVEITEM',
      speDriveId: 'b!XYZ',
      speFileName: 'Acme MSA.docx',
    });
  });

  test('missing sprk_graphitemid: opens Compose in empty-state with sprkDocumentId only', async () => {
    installXrmMock({
      retrieveRecord: jest.fn().mockResolvedValue({
        sprk_documentid: 'doc-guid-1',
        sprk_graphitemid: undefined,
      }),
    });

    await openInCompose(makeFormContext('doc-guid-1') as unknown as Xrm.FormContext);

    expect(mockOpenSpaarkeAiCompose).toHaveBeenCalledWith({
      entityLogicalName: 'sprk_document',
      entityId: 'doc-guid-1',
      sprkDocumentId: 'doc-guid-1',
    });
  });

  test('WebApi failure: opens Compose with sprkDocumentId only (graceful fallback, no throw)', async () => {
    installXrmMock({
      retrieveRecord: jest.fn().mockRejectedValue(new Error('Record not found')),
    });
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    await expect(
      openInCompose(makeFormContext('doc-guid-1') as unknown as Xrm.FormContext),
    ).resolves.not.toThrow();

    expect(mockOpenSpaarkeAiCompose).toHaveBeenCalledWith({
      entityLogicalName: 'sprk_document',
      entityId: 'doc-guid-1',
      sprkDocumentId: 'doc-guid-1',
    });

    errorSpy.mockRestore();
  });

  test('empty form id (unsaved record): opens Compose with no params (handler defensive guard)', async () => {
    installXrmMock();

    await openInCompose(makeFormContext('') as unknown as Xrm.FormContext);

    expect(mockOpenSpaarkeAiCompose).toHaveBeenCalledWith({});
  });

  test('strips braces from record GUID before WebApi call', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      sprk_graphitemid: '01ITEM',
    });
    installXrmMock({ retrieveRecord });

    await openInCompose(
      makeFormContext('{F1A2B3C4-0000-1111-2222-333344445555}') as unknown as Xrm.FormContext,
    );

    // First arg is entity name, second is the GUID (no braces).
    expect(retrieveRecord).toHaveBeenCalledWith(
      'sprk_document',
      'F1A2B3C4-0000-1111-2222-333344445555',
      expect.any(String),
    );
  });
});
