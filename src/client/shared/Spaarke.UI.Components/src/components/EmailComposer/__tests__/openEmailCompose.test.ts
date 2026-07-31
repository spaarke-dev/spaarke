/**
 * openEmailCompose.test.ts
 *
 * Focused unit tests for the compose launcher (`openEmailCompose`). Asserts the
 * `Xrm.Navigation.navigateTo` `data` envelope per mode (`compose=<mode>` +
 * optional `&id=<source>`), the modal navigation options, and the best-effort
 * no-op paths (reply/forward without a source; no MDA host).
 */
import { openEmailCompose } from '../openEmailCompose';
import { EMAIL_PAGE_WEBRESOURCE_NAME } from '../openEmailRecord';

type XrmLike = {
  WebApi: { retrieveMultipleRecords: jest.Mock };
  Navigation: { navigateTo: jest.Mock };
};

let mockNavigateTo: jest.Mock;

function installXrm(): void {
  mockNavigateTo = jest.fn().mockResolvedValue(undefined);
  const xrm: XrmLike = {
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

const SOURCE_ID = '11111111-1111-1111-1111-111111111111';

describe('openEmailCompose (compose launcher)', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
    jest.restoreAllMocks();
  });

  it('New (default): opens sprk_emailpage with compose=new and no id, as a centered modal', async () => {
    installXrm();

    await openEmailCompose();

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'webresource',
      webresourceName: EMAIL_PAGE_WEBRESOURCE_NAME,
      data: 'compose=new',
    });
    expect(navOptions).toEqual({
      target: 2,
      position: 1,
      width: { value: 60, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });

  it.each(['reply', 'replyAll', 'forward'] as const)(
    '%s: folds the normalised source id into the data envelope',
    async mode => {
      installXrm();

      await openEmailCompose({ mode, sourceCommunicationId: `{${SOURCE_ID.toUpperCase()}}` });

      const [pageInput] = mockNavigateTo.mock.calls[0];
      expect(pageInput.data).toBe(`compose=${mode}&id=${SOURCE_ID}`);
    }
  );

  it('no-ops (console.warn) for reply/forward without a source id', async () => {
    installXrm();
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await openEmailCompose({ mode: 'reply' });

    expect(mockNavigateTo).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalled();
  });

  it('no-ops (console.warn) outside an MDA host (no Xrm)', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await expect(openEmailCompose({ mode: 'new' })).resolves.toBeUndefined();

    expect(warn).toHaveBeenCalled();
  });
});
