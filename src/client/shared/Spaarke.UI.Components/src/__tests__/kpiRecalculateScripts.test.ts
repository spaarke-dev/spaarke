/**
 * #1655 - the KPI recalculate form scripts build their BFF URL from sprk_BffApiBaseUrl, which in dev holds
 * "https://spaarke-bff-dev.azurewebsites.net/api", and append a route that carries its own /api. Before the fix
 * they POSTed ".../api/api/matters/{id}/recalculate-grades" (404). They now normalise the base with the shared
 * helper's `Spaarke.BffAuth.normalizeBaseUrl` (no fifth copy of the rule), and a bff_auth.js without it is treated
 * as not loaded.
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
const TOKEN = 'header.payload.sig';

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

beforeEach(() => {
  fetchMock = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}) }));
  (global as any).fetch = fetchMock;
  getToken = jest.fn(async () => TOKEN);
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe.each(CASES)('$file (#1655: one /api in the recalculate URL)', ({ file, ns, route, run }) => {
  beforeEach(() => {
    if (win.Spaarke) win.Spaarke[ns] = undefined;
    const script = document.createElement('script');
    script.textContent = fs.readFileSync(path.join(WEBRESOURCES, file), 'utf8');
    document.head.appendChild(script);
    win.Spaarke.BffAuth = { getToken, authenticatedFetch: jest.fn(), normalizeBaseUrl: jest.fn(normalizeBaseUrl) };
  });

  it.each([`${BFF}/api`, `${BFF}/api/`, `${BFF}/API`, BFF, `${BFF}/`])(
    'sprk_BffApiBaseUrl = %s: POSTs {BFF}' + route.replace(ID, '{id}') + ' with the Bearer token',
    async base => {
      await run(base);

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, init] = fetchMock.mock.calls[0];
      expect(url).toBe(BFF + route);
      expect(url).not.toMatch(/\/api\/api\//i);
      expect(init.method).toBe('POST');
      expect(init.headers.Authorization).toBe('Bearer ' + TOKEN);
      expect(getToken).toHaveBeenCalledWith(base); // the helper resolves the BFF origin itself
    }
  );

  it('treats a bff_auth.js without normalizeBaseUrl (pre-#1655) as not loaded: no request, no token', async () => {
    win.Spaarke.BffAuth = { getToken, authenticatedFetch: jest.fn() };

    await run(`${BFF}/api`);

    expect(fetchMock).not.toHaveBeenCalled();
    expect(getToken).not.toHaveBeenCalled();
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('Spaarke.BffAuth helper not loaded'));
  });

  it('no token: no request', async () => {
    getToken.mockResolvedValue(null);

    await run(`${BFF}/api`);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('the SpaarkeMaster copy is the same script', () => {
    const canonical = fs.readFileSync(path.join(WEBRESOURCES, file), 'utf8').replace(/\r\n/g, '\n');
    expect(fs.readFileSync(path.join(SOLUTION, file), 'utf8').replace(/\r\n/g, '\n')).toBe(canonical);
  });
});
