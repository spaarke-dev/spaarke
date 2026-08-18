/**
 * ensureNavigatorSidePane.test.ts (spaarke-side-pane-navigation-history-r1)
 *
 * Unit coverage for the SHARED code-page registrar that every Spaarke code
 * page imports from `@spaarke/ui-components` to auto-dock the Navigator
 * pane on mount. Verifies the createPane contract (paneId, canClose:false,
 * alwaysRender:true, imageSrc), idempotency across repeat calls, and that
 * the function never throws when `Xrm.App.sidePanes` is unavailable.
 *
 * The module keeps a module-level singleton guard (`_started`), so each
 * scenario that needs a fresh guard state re-imports the module via
 * `jest.resetModules()` + `require(...)` rather than the static `import`.
 *
 * @see ../ensureNavigatorSidePane.ts
 * @see docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md
 */

type MockXrm = {
  createPane: jest.Mock;
  getPane: jest.Mock;
  navigate: jest.Mock;
};

function installMockXrm(): MockXrm {
  const navigate = jest.fn().mockResolvedValue(undefined);
  const pane = { navigate };
  const createPane = jest.fn().mockResolvedValue(pane);
  const getPane = jest.fn().mockReturnValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = {
    WebApi: {},
    App: {
      sidePanes: {
        createPane,
        getPane,
        getAllPanes: jest.fn().mockReturnValue([]),
        getSelectedPane: jest.fn().mockReturnValue(undefined),
      },
    },
  };
  return { createPane, getPane, navigate };
}

function flushPromises(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, 0));
}

describe('ensureNavigatorSidePane', () => {
  afterEach(() => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    jest.resetModules();
    jest.useRealTimers();
  });

  it('creates the Navigator pane once with the required contract, then navigates it to the webresource', async () => {
    const mock = installMockXrm();

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const { ensureNavigatorSidePane } = require('../ensureNavigatorSidePane');
    ensureNavigatorSidePane();
    await flushPromises();

    expect(mock.createPane).toHaveBeenCalledTimes(1);
    expect(mock.createPane).toHaveBeenCalledWith(
      expect.objectContaining({
        paneId: 'sprk-navigator',
        title: 'Navigator',
        canClose: false,
        alwaysRender: true,
        isSelected: false,
        imageSrc: 'WebResources/sprk_navigatorstar.svg',
      })
    );
    expect(mock.navigate).toHaveBeenCalledWith({
      pageType: 'webresource',
      webresourceName: 'sprk_NavigatorPane.html',
    });
  });

  it('is idempotent — a second call in the same module instance does not create a second pane', async () => {
    const mock = installMockXrm();

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const { ensureNavigatorSidePane } = require('../ensureNavigatorSidePane');
    ensureNavigatorSidePane();
    await flushPromises();
    ensureNavigatorSidePane();
    await flushPromises();

    expect(mock.createPane).toHaveBeenCalledTimes(1);
  });

  it('does not create a pane when one already exists (getPane returns it)', async () => {
    const mock = installMockXrm();
    mock.getPane.mockReturnValue({ paneId: 'sprk-navigator' });

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const { ensureNavigatorSidePane } = require('../ensureNavigatorSidePane');
    ensureNavigatorSidePane();
    await flushPromises();

    expect(mock.createPane).not.toHaveBeenCalled();
  });

  it('never throws when Xrm.App.sidePanes is absent, even through the retry backoff', () => {
    jest.useFakeTimers();
    delete (window as unknown as { Xrm?: unknown }).Xrm;

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const { ensureNavigatorSidePane } = require('../ensureNavigatorSidePane');
    expect(() => ensureNavigatorSidePane()).not.toThrow();
    expect(() => jest.runAllTimers()).not.toThrow();
  });
});
