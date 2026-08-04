/**
 * openEmailRecord.test.ts
 *
 * Focused unit tests for the Pattern B launcher (`openEmailRecord`). Installs a
 * global Xrm shim (mirroring the fixture shape used across the hooks suite —
 * `getXrm()` returns Xrm only when `Xrm.WebApi` is present) and asserts the
 * exact `Xrm.Navigation.navigateTo` args: web-resource name + `data` envelope +
 * modal navigation options. Also covers the best-effort no-op paths (no host /
 * blank id).
 */
import { openEmailRecord, EMAIL_PAGE_WEBRESOURCE_NAME } from '../openEmailRecord';

type XrmLike = {
  WebApi: { retrieveMultipleRecords: jest.Mock };
  Navigation: { navigateTo: jest.Mock };
};

let mockNavigateTo: jest.Mock;

function installXrm(): void {
  mockNavigateTo = jest.fn().mockResolvedValue(undefined);
  const xrm: XrmLike = {
    // getXrm() gates on `Xrm.WebApi` being truthy — include it.
    WebApi: { retrieveMultipleRecords: jest.fn() },
    Navigation: { navigateTo: mockNavigateTo },
  };
  (globalThis as unknown as { Xrm?: XrmLike }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmLike }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: XrmLike }).Xrm;
  delete (window as unknown as { Xrm?: XrmLike }).Xrm;
}

const COMMUNICATION_ID = '11111111-1111-1111-1111-111111111111';

describe('openEmailRecord (Pattern B launcher)', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
    jest.restoreAllMocks();
  });

  it('opens the sprk_emailpage web resource as a centered modal, carrying the id in the data envelope', async () => {
    installXrm();

    await openEmailRecord(COMMUNICATION_ID);

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'webresource',
      webresourceName: EMAIL_PAGE_WEBRESOURCE_NAME,
      data: COMMUNICATION_ID,
    });
    expect(EMAIL_PAGE_WEBRESOURCE_NAME).toBe('sprk_emailpage');
    // `record` OOB size (85%×85%) — spaarke-modal-system P7 task 090
    // (FR-11/FR-18); was 80%×80%.
    expect(navOptions).toEqual({
      target: 2,
      position: 1,
      width: { value: 85, unit: '%' },
      height: { value: 85, unit: '%' },
    });
  });

  it('folds a `&single=1` flag into the data envelope when { single: true }', async () => {
    installXrm();

    await openEmailRecord(COMMUNICATION_ID, { single: true });

    const [pageInput] = mockNavigateTo.mock.calls[0];
    expect(pageInput.webresourceName).toBe('sprk_emailpage');
    expect(pageInput.data).toBe(`${COMMUNICATION_ID}&single=1`);
  });

  it('normalises the id (strips braces, lowercases) before building the data envelope', async () => {
    installXrm();

    await openEmailRecord('{1111AAAA-1111-1111-1111-111111111111}');

    const [pageInput] = mockNavigateTo.mock.calls[0];
    expect(pageInput.data).toBe('1111aaaa-1111-1111-1111-111111111111');
  });

  it('no-ops (console.warn) with a blank id and never calls navigateTo', async () => {
    installXrm();
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await openEmailRecord('   ');

    expect(mockNavigateTo).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalled();
  });

  it('no-ops (console.warn) outside an MDA host (no Xrm)', async () => {
    // No installXrm() — getXrm() returns null.
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await expect(openEmailRecord(COMMUNICATION_ID)).resolves.toBeUndefined();

    expect(warn).toHaveBeenCalled();
  });
});
