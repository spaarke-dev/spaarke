/**
 * #1655 - the KPI recalculate form scripts (`sprk_matter_kpi_refresh.js`, `sprk_kpi_subgrid_refresh.js`,
 * `sprk_kpiassessment_quickcreate.js`):
 *
 *  - ONE /api: sprk_BffApiBaseUrl holds "https://spaarke-bff-dev.azurewebsites.net/api" in dev and the routes carry
 *    their own /api. Before the fix they POSTed ".../api/api/matters/{id}/recalculate-grades" (404). They now normalise
 *    the base with the shared helper's `Spaarke.BffAuth.normalizeBaseUrl` (no fifth copy of the rule).
 *  - Through the origin guard (ADR-028 A7): the call goes through `Spaarke.BffAuth.authenticatedFetch`, which attaches
 *    the token only for a URL on the BFF origin. The scripts no longer take a raw token (`getToken`) and attach
 *    "Bearer " themselves.
 *  - A bff_auth.js without `normalizeBaseUrl` or `authenticatedFetch` is treated as not loaded: no request.
 *
 * The REAL scripts are injected into the jsdom window, as the form runs them; only `Spaarke.BffAuth`, the form
 * context and `fetch` are replaced. The SpaarkeMaster copies must be the same scripts.
 */
import * as fs from 'fs';
import * as path from 'path';

const WEBRESOURCES = path.resolve(__dirname, '../../../../../solutions/webresources');
const SOLUTION = path.resolve(__dirname, '../../../../../dataverse/solutions/SpaarkeMaster/WebResources');
const BFF = 'https://bff.example.test';
const ID = '1b4e28ba-2fa1-11d2-883f-0016d3cca427';

/** The helper's rule (@spaarke/auth normalizeBffBaseUrl): drop trailing slashes, then one trailing /api. */
const normalizeBaseUrl = (raw: string): string =>
  String(raw ?? '')
    .trim()
    .replace(/\/+$/, '')
    .replace(/\/api$/i, '');

type Win = typeof window & { Spaarke?: any };
const win = window as Win;

const flush = async (): Promise<void> => {
  for (let i = 0; i < 8; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
};

const makeForm = () => ({ data: { refresh: jest.fn(() => Promise.resolve()) } });

interface ScriptCase {
  file: string;
  ns: string;
  route: string;
  /** Point the script at `base` and run its recalculate call. */
  run: (base: string) => Promise<void>;
}

const CASES: ScriptCase[] = [
  {
    file: 'sprk_matter_kpi_refresh.js',
    ns: 'MatterKpi',
    route: `/api/matters/${ID}/recalculate-grades`,
    run: async base => {
      win.Spaarke.MatterKpi._apiBaseUrl = base;
      win.Spaarke.MatterKpi._recalculateAndRefresh(makeForm(), ID);
      await flush();
    },
  },
  {
    file: 'sprk_kpi_subgrid_refresh.js',
    ns: 'KpiSubgrid',
    route: `/api/projects/${ID}/recalculate-grades`,
    run: async base => {
      win.Spaarke.KpiSubgrid._apiBaseUrl = base;
      win.Spaarke.KpiSubgrid._entityType = 'project';
      win.Spaarke.KpiSubgrid._recalculateAndRefresh(makeForm(), ID);
      await flush();
    },
  },
  {
    file: 'sprk_kpiassessment_quickcreate.js',
    ns: 'KpiAssessment',
    route: `/api/matters/${ID}/recalculate-grades`,
    run: async base => {
      win.Spaarke.KpiAssessment.Config.apiBaseUrl = base;
      win.Spaarke.KpiAssessment._refreshParentForm = jest.fn();
      await win.Spaarke.KpiAssessment._callCalculatorApi('matter', ID);
    },
  },
];

let fetchMock: jest.Mock;
let getToken: jest.Mock;
let authenticatedFetch: jest.Mock;

beforeEach(() => {
  fetchMock = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}) }));
  (global as any).fetch = fetchMock;
  getToken = jest.fn(async () => 'header.payload.sig');
  authenticatedFetch = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}) }));
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe.each(CASES)('$file (#1655)', ({ file, ns, route, run }) => {
  beforeEach(() => {
    if (win.Spaarke) win.Spaarke[ns] = undefined;
    const script = document.createElement('script');
    script.textContent = fs.readFileSync(path.join(WEBRESOURCES, file), 'utf8');
    document.head.appendChild(script);
    win.Spaarke.BffAuth = { getToken, authenticatedFetch, normalizeBaseUrl: jest.fn(normalizeBaseUrl) };
  });

  it.each([`${BFF}/api`, `${BFF}/api/`, `${BFF}/API`, BFF, `${BFF}/`])(
    'sprk_BffApiBaseUrl = %s: ONE call through Spaarke.BffAuth.authenticatedFetch to {BFF}' +
      route.replace(ID, '{id}') +
      ' (one /api); no token or Bearer of its own',
    async base => {
      await run(base);

      expect(authenticatedFetch).toHaveBeenCalledTimes(1);
      const [url, init, baseUrl] = authenticatedFetch.mock.calls[0];
      expect(url).toBe(BFF + route);
      expect(url).not.toMatch(/\/api\/api\//i);
      expect(init.method).toBe('POST');
      expect(init.headers.Authorization).toBeUndefined(); // the helper attaches it, BFF origin only
      expect(baseUrl).toBe(base);
      expect(getToken).not.toHaveBeenCalled();
      expect(fetchMock).not.toHaveBeenCalled(); // no request of its own
    }
  );

  it('no token (the helper resolves null): one attempt, no retry, no request of its own', async () => {
    authenticatedFetch.mockResolvedValue(null);

    await run(`${BFF}/api`);

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ['without normalizeBaseUrl (pre-#1655)', () => ({ getToken, authenticatedFetch })],
    ['without authenticatedFetch', () => ({ getToken, normalizeBaseUrl })],
  ])('treats a bff_auth.js %s as not loaded: no request, no token', async (_label, helper) => {
    win.Spaarke.BffAuth = helper();

    await run(`${BFF}/api`);

    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(getToken).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('Spaarke.BffAuth helper not loaded'));
  });

  it('attaches no Bearer token itself, and the SpaarkeMaster copy is the same script', () => {
    const canonical = fs.readFileSync(path.join(WEBRESOURCES, file), 'utf8').replace(/\r\n/g, '\n');
    expect(canonical).not.toMatch(/"Bearer "|BffAuth\.getToken\(/);
    expect(fs.readFileSync(path.join(SOLUTION, file), 'utf8').replace(/\r\n/g, '\n')).toBe(canonical);
  });
});
