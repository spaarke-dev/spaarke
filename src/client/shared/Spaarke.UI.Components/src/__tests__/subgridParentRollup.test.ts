/**
 * #1655 - `sprk_subgrid_parent_rollup.js` 3.0.0 gets its BFF token from the shared `Spaarke.BffAuth` helper
 * (sprk_/scripts/bff_auth.js = @spaarke/auth) instead of its own inlined MSAL copy. The REAL script is injected into the
 * jsdom window, as the form runs it; only `Xrm`, the form context and `Spaarke.BffAuth` are replaced.
 *
 *  - a subgrid row-count change POSTs the recalculate route through Spaarke.BffAuth.authenticatedFetch, then refreshes;
 *  - no token (a null Response) -> no refresh, nothing thrown;
 *  - Spaarke.BffAuth not registered on the form -> no call, an error logged, nothing thrown;
 *  - the script carries no MSAL code of its own (no CDN load, no PublicClientApplication, no display-name hint).
 */
import * as fs from 'fs';
import * as path from 'path';

const SCRIPT = path.resolve(__dirname, '../../../../../solutions/webresources/sprk_subgrid_parent_rollup.js');
const SOLUTION_COPY = path.resolve(
  __dirname,
  '../../../../../dataverse/solutions/SpaarkeMaster/WebResources/sprk_subgrid_parent_rollup.js'
);
const BFF = 'https://bff.example.test';
const MATTER_ID = '1b4e28ba-2fa1-11d2-883f-0016d3cca427';

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

const flush = async (): Promise<void> => {
  for (let i = 0; i < 8; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
};

function inject(): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(SCRIPT, 'utf8');
  document.head.appendChild(script);
}

function installXrm(): void {
  win.Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(async (entity: string) => {
        if (entity === 'environmentvariabledefinition') {
          return { entities: [{ environmentvariabledefinitionid: 'def-1', defaultvalue: BFF }] };
        }
        return { entities: [] };
      }),
    },
  };
}

function makeForm() {
  let rowCount = 2;
  let onSubgridLoad: (() => void) | null = null;
  const refresh = jest.fn(() => Promise.resolve());
  const subgrid = {
    getGrid: () => ({ getTotalRecordCount: () => rowCount }),
    addOnLoad: (fn: () => void) => {
      onSubgridLoad = fn;
    },
  };
  const form = {
    refresh,
    setRows: (n: number) => {
      rowCount = n;
    },
    fireSubgridLoad: () => onSubgridLoad?.(),
    getControl: (name: string) => (name === 'subgrid_invoice' ? subgrid : null),
    data: {
      refresh,
      entity: {
        getEntityName: () => 'sprk_matter',
        getId: () => `{${MATTER_ID.toUpperCase()}}`,
      },
    },
  };
  return form;
}

const CONFIG = JSON.stringify({
  subgridName: 'subgrid_invoice',
  apiPathTemplate: '/api/finance/{entityPath}/{entityId}/recalculate',
  entityPathMap: { sprk_matter: 'matters' },
  refreshDelayMs: 1,
});

let authenticatedFetch: jest.Mock;
let fetchSpy: jest.Mock;

beforeEach(() => {
  if (win.Spaarke) {
    win.Spaarke.SubgridRollup = undefined;
    win.Spaarke.BffAuth = undefined;
  }
  inject();
  installXrm();
  authenticatedFetch = jest.fn(async () => ({ ok: true, status: 200 }));
  win.Spaarke.BffAuth = { authenticatedFetch, getToken: jest.fn() };
  fetchSpy = jest.fn();
  (global as any).fetch = fetchSpy;
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

async function loadAndChangeRows() {
  const form = makeForm();
  win.Spaarke.SubgridRollup.onLoad({ getFormContext: () => form }, CONFIG);
  await flush();
  form.setRows(3);
  form.fireSubgridLoad();
  await flush();
  await new Promise(resolve => setTimeout(resolve, 5)); // refreshDelayMs
  await flush();
  return form;
}

describe('sprk_subgrid_parent_rollup.js 3.0.0 (#1655)', () => {
  it('POSTs the recalculate route through Spaarke.BffAuth.authenticatedFetch, then refreshes the form', async () => {
    const form = await loadAndChangeRows();

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, options, baseUrl] = authenticatedFetch.mock.calls[0];
    expect(url).toBe(`${BFF}/api/finance/matters/${MATTER_ID.toUpperCase()}/recalculate`); // braces stripped, case kept (unchanged)
    expect(options.method).toBe('POST');
    expect(options.headers.Authorization).toBeUndefined(); // the helper attaches it
    expect(baseUrl).toBe(BFF);
    expect(fetchSpy).not.toHaveBeenCalled(); // no request of its own
    expect(form.refresh).toHaveBeenCalledWith(false);
  });

  it('does not refresh, and throws nothing, when no token could be acquired (null Response)', async () => {
    authenticatedFetch.mockResolvedValue(null);

    const form = await loadAndChangeRows();

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(form.refresh).not.toHaveBeenCalled();
  });

  it('skips the call, logs an error and throws nothing when bff_auth.js is not registered on the form', async () => {
    win.Spaarke.BffAuth = undefined;

    const form = await loadAndChangeRows();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(form.refresh).not.toHaveBeenCalled();
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('Spaarke.BffAuth is not loaded'));
  });

  it('carries no MSAL flow of its own, and the SpaarkeMaster copy is the same script', () => {
    const text = fs.readFileSync(SCRIPT, 'utf8');
    expect(text).not.toMatch(/alcdn\.msauth\.net|PublicClientApplication|ssoSilent|acquireTokenSilent|userSettings/);
    expect(fs.readFileSync(SOLUTION_COPY, 'utf8').replace(/\r\n/g, '\n')).toBe(text.replace(/\r\n/g, '\n'));
  });
});
