/**
 * openEmailCompose.test.ts
 *
 * Focused unit tests for the compose launcher (`openEmailCompose`). Asserts the
 * `Xrm.Navigation.navigateTo` `data` envelope per mode (`compose=<mode>` +
 * optional `&id=<source>`), the modal navigation options, and the best-effort
 * no-op paths (reply/forward without a source; no MDA host).
 */
import { openEmailCompose, composeRegardingParams, parseComposeRegarding } from '../openEmailCompose';
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
    // `createForm` OOB size (70%×80%) — spaarke-modal-system P7 task 090
    // (FR-11/FR-18); was 60%×80%.
    expect(navOptions).toEqual({
      target: 2,
      position: 1,
      width: { value: 70, unit: '%' },
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

  describe('"Related to" seed (#2908)', () => {
    const MATTER_ID = '22222222-2222-4222-8222-222222222222';

    it('New with a regarding record appends regardingType / regardingId / regardingName, normalised', async () => {
      installXrm();

      await openEmailCompose({
        mode: 'new',
        regarding: { entityType: 'SPRK_Matter', id: `{${MATTER_ID.toUpperCase()}}`, name: 'Acme v. Beta & Co' },
      });

      const [pageInput] = mockNavigateTo.mock.calls[0];
      const data = new URLSearchParams(pageInput.data as string);
      expect(data.get('compose')).toBe('new');
      expect(data.get('regardingType')).toBe('sprk_matter');
      expect(data.get('regardingId')).toBe(MATTER_ID);
      expect(data.get('regardingName')).toBe('Acme v. Beta & Co');
      // The receiving page reads the same three parameters back.
      expect(parseComposeRegarding(data)).toEqual({
        entityType: 'sprk_matter',
        id: MATTER_ID,
        name: 'Acme v. Beta & Co',
      });
    });

    it('a reply ignores regarding (its associations come from the source email)', async () => {
      installXrm();

      await openEmailCompose({
        mode: 'reply',
        sourceCommunicationId: SOURCE_ID,
        regarding: { entityType: 'sprk_matter', id: MATTER_ID },
      });

      expect(mockNavigateTo.mock.calls[0][0].data).toBe(`compose=reply&id=${SOURCE_ID}`);
    });

    it('an invalid regarding is dropped with a warning and the composer still opens', async () => {
      installXrm();
      const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

      await openEmailCompose({ mode: 'new', regarding: { entityType: 'sprk_matter', id: 'not-a-guid' } });

      expect(mockNavigateTo.mock.calls[0][0].data).toBe('compose=new');
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('invalid "Related to"'), expect.anything());
    });

    it('composeRegardingParams omits an empty name; parseComposeRegarding refuses a bad table name', () => {
      expect(composeRegardingParams({ entityType: 'sprk_matter', id: MATTER_ID, name: '  ' })).toBe(
        `regardingType=sprk_matter&regardingId=${MATTER_ID}`
      );
      expect(
        parseComposeRegarding(new URLSearchParams(`regardingType=sprk_matter'x&regardingId=${MATTER_ID}`))
      ).toBeUndefined();
      expect(parseComposeRegarding(new URLSearchParams('compose=new'))).toBeUndefined();
    });
  });

  it('no-ops (console.warn) outside an MDA host (no Xrm)', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await expect(openEmailCompose({ mode: 'new' })).resolves.toBeUndefined();

    expect(warn).toHaveBeenCalled();
  });
});
