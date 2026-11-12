// The shell path of "Submit invoice" (R4 task 007 fix round 1, F1 / F3): `RecordDeepLink` (the app-wide `?record=` deep link,
// mounted on every route), the record opener with the router's `navigate` (the production `RouterRecordOpener` App.tsx
// mounts), the REAL record modal, and the `/invoices/submit` route with the real
// `InvoiceSubmitPage` + shared wizard all run in one MemoryRouter. The network seam is the same as InvoiceSubmitPage.test.tsx:
// `fetch` is a URL-routed fake BFF and the token acquirer is a fake.
//
// What this pins: the page's own parameter (`invoiceRecord`) is NOT the app-wide `record` deep link, so landing on
// `/invoices/submit?invoiceRecord=matter:{id}` opens no record dialog and the wizard shows the matter chosen; and when
// BOTH parameters are present each does only its own job.
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { installLayout, uninstallLayout } from './jsdomLayout';

vi.mock('@microsoft/teams-js', () => ({
  app: {
    initialize: vi.fn(),
    getContext: vi.fn(),
    notifyAppLoaded: vi.fn(),
    notifySuccess: vi.fn(),
    notifyFailure: vi.fn(),
  },
  nestedAppAuth: { isNAAChannelRecommended: vi.fn(() => false) },
  authentication: { getAuthToken: vi.fn() },
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect: vi.fn(async () => undefined) }, accounts: [], inProgress: 'none' }),
}));

const SLOW = { timeout: 60000 };
// Waits inside a test are shorter than the test, so a stuck step fails on its own line.
const WAIT = { timeout: 20000 };
const MATTER_ID = '3f2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5b';
const PROJECT_ID = '4a2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5c';
/** A matter the deep link opens, which is NOT the one the invoice link preselects (and not on the firm's list). */
const OTHER_MATTER_ID = '7d2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a77';
const FIRM = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};

interface Call {
  url: string;
  method: string;
  body: string | null;
}
let calls: Call[];

const json = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/** Record names the fake serves for the record view's single-record read (by id); the wizard's list read has no id filter. */
const NAMES: Record<string, string> = {
  [MATTER_ID]: 'Acme v. Roadrunner',
  [OTHER_MATTER_ID]: 'Deep-linked matter',
};

beforeEach(() => {
  installLayout();
  calls = [];
  localStorage.clear();
  sessionStorage.clear();
  window.history.replaceState(null, '', '/');
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? init.body : null;
      calls.push({ url, method, body });
      const p = new URL(url).pathname;
      if (method === 'GET' && p === '/api/v1/external/invoice-submissions') return json(200, []);
      if (method === 'GET' && p === '/api/v1/external/me') {
        return json(200, {
          contactId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
          email: 'ann@firm.example',
          projects: [],
          firms: [FIRM],
        });
      }
      if (method === 'GET' && p === '/api/v1/external/invoice-submissions/firm-records') {
        return json(200, {
          firmId: FIRM.firmId,
          matters: [MATTER_ID],
          projects: [PROJECT_ID],
          workAssignments: [],
          partial: false,
          truncated: false,
        });
      }
      if (method === 'GET' && p === '/api/v1/external/projects') {
        return json(200, {
          value: [{ sprk_projectid: PROJECT_ID, sprk_name: 'Patent portfolio', sprk_referencenumber: 'P-77' }],
        });
      }
      if (method === 'POST' && p === '/api/v1/external/api/dataverse/fetch') {
        const request = JSON.parse(String(body)) as { entityName: string; fetchXml: string };
        if (request.entityName !== 'sprk_matter') return json(200, { entities: [], moreRecords: false });
        const id = /operator="eq" value="([^"]+)"/.exec(request.fetchXml)?.[1];
        // The record view's read (one id) or the wizard's list (the firm's matters).
        const rows = id ? [id] : [MATTER_ID];
        return json(200, {
          entities: rows.map(rowId => ({
            sprk_matterid: rowId,
            sprk_mattername: NAMES[rowId] ?? 'A matter',
            sprk_matternumber: 'M-1001',
          })),
          moreRecords: false,
        });
      }
      return json(404, { status: 404, title: 'unexpected route in test', detail: `${method} ${p}` });
    })
  );
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  uninstallLayout();
});

/** Fresh module graph, with the plane's backend and token acquirer as the plane bootstrap leaves them. */
async function load() {
  vi.resetModules();
  const selection = await import('../src/auth/backend-selection');
  const msalAuth = await import('../src/auth/msal-auth');
  msalAuth.setActiveBffTokenAcquirer(vi.fn(async () => 'ciam-token'));
  selection.activateBackend('ciam', { ...CONTOSO });
  const { InvoiceSubmitPage } = await import('../src/pages/InvoiceSubmitPage');
  const { RecordDeepLink } = await import('../src/components/detail/RecordDeepLink');
  const { useOpenRecord } = await import('../src/components/detail/RecordOpener');
  const { RouterRecordOpener } = await import('../src/components/detail/RouterRecordOpener');
  return { InvoiceSubmitPage, RecordDeepLink, RouterRecordOpener, useOpenRecord };
}

type Loaded = Awaited<ReturnType<typeof load>>;

/** Shows the router's current URL, so a test sees what the deep link left in the address bar. */
const Where: React.FC = () => {
  const location = useLocation();
  return <output data-testid="where">{location.pathname + location.search}</output>;
};

/** A stand-in for a grid row: opens the matter's record view through the shell's opener. */
const Home: React.FC<{ useOpenRecord: Loaded['useOpenRecord'] }> = ({ useOpenRecord }) => {
  const openRecord = useOpenRecord();
  return (
    <button onClick={() => openRecord({ entityName: 'sprk_matter', recordId: MATTER_ID })}>Open the matter</button>
  );
};

async function renderShell(entry: string) {
  const loaded = await load();
  // The production bridge App.tsx mounts (the opener given the router's navigate), not a copy.
  const { InvoiceSubmitPage, RecordDeepLink, RouterRecordOpener, useOpenRecord } = loaded;
  return render(
    <FluentProvider theme={webLightTheme}>
      <MemoryRouter initialEntries={[entry]}>
        <RouterRecordOpener>
          <RecordDeepLink teamsHost={false} />
          <Where />
          <Routes>
            <Route path="/invoices/submit" element={<InvoiceSubmitPage />} />
            <Route path="*" element={<Home useOpenRecord={useOpenRecord} />} />
          </Routes>
        </RouterRecordOpener>
      </MemoryRouter>
    </FluentProvider>
  );
}

/** The fetchXml of a Dataverse read, or null for any other call. */
const fetchXmlOf = (c: Call): { entityName: string; fetchXml: string } | null =>
  c.body !== null && c.url.endsWith('/api/dataverse/fetch')
    ? (JSON.parse(c.body) as { entityName: string; fetchXml: string })
    : null;
/** The record view's read of ONE record (filtered on its id); the wizard's lists carry no id filter. */
const recordViewReads = (): Call[] => calls.filter(c => /operator="eq" value="/.test(fetchXmlOf(c)?.fetchXml ?? ''));
/** The wizard's matter list. */
const matterListReads = (): Call[] =>
  calls.filter(c => {
    const read = fetchXmlOf(c);
    return read?.entityName === 'sprk_matter' && !/operator="eq" value="/.test(read.fetchXml);
  });

const where = (): string => screen.getByTestId('where').textContent ?? '';
const isChecked = (el: HTMLElement) => (el as HTMLInputElement).checked;
const isEnabled = (el: HTMLElement) => !(el as HTMLButtonElement).disabled;
/** The wizard's matter radio, found even while a modal dialog has the rest of the page aria-hidden. */
const matterRadio = (hidden: boolean) => screen.findByRole('radio', { name: /Acme v\. Roadrunner/, hidden }, WAIT);
const settle = () => new Promise<void>(resolve => setTimeout(resolve, 150));

describe('the invoice page under the shell: invoiceRecord is not the record deep link', () => {
  it(
    '/invoices/submit?invoiceRecord=matter:{id} opens no record dialog and the wizard shows the matter chosen',
    async () => {
      await renderShell(`/invoices/submit?invoiceRecord=${encodeURIComponent(`matter:${MATTER_ID}`)}`);

      const matter = await matterRadio(false);
      expect(isChecked(matter)).toBe(true);
      expect(isEnabled(await screen.findByRole('button', { name: /^next$/i }))).toBe(true);
      await settle();
      // No record view was opened, and the link is still in the address (nothing stripped it).
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(where()).toContain('invoiceRecord=');
      expect(where()).toContain(MATTER_ID);
      // The record view's own read never happened (its read filters on one id).
      expect(recordViewReads()).toHaveLength(0);
    },
    SLOW
  );

  it(
    "a URL with BOTH parameters: record= opens that record's dialog and is stripped, invoiceRecord= is kept and still preselects its own record",
    async () => {
      await renderShell(
        `/invoices/submit?record=${encodeURIComponent(`matter:${OTHER_MATTER_ID}`)}` +
          `&invoiceRecord=${encodeURIComponent(`matter:${MATTER_ID}`)}`
      );

      // The app-wide deep link did its own job: the record view of matter A (the OTHER one) is open.
      const dialog = await screen.findByRole('dialog', undefined, WAIT);
      await within(dialog).findByRole('heading', { name: 'Deep-linked matter' }, WAIT);
      // ...and was stripped from the address once it settled, while the page's own parameter stayed.
      await waitFor(() => expect(where()).not.toMatch(/[?&]record=/), WAIT);
      expect(where()).toContain('invoiceRecord=');
      expect(where()).toContain(MATTER_ID);
      expect(where()).not.toContain(OTHER_MATTER_ID);

      // The page did its own job, unaffected by `record` being stripped: the wizard has matter B chosen (queried through
      // the aria-hidden the open dialog puts on the page behind it).
      expect(isChecked(await matterRadio(true))).toBe(true);

      // Closing the dialog leaves the page as it was: still B chosen, still enabled, nothing re-read.
      // The footer Close (the header × has the same name).
      fireEvent.click(
        within(dialog)
          .getAllByRole('button', { name: /^close$/i })
          .find(b => b.textContent === 'Close')!
      );
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull(), WAIT);
      expect(isChecked(await matterRadio(false))).toBe(true);
      expect(isEnabled(await screen.findByRole('button', { name: /^next$/i }))).toBe(true);
      // The page's read-ahead ran once and was not cancelled or redone by the strip (the record view's own firm-records
      // read for its Submit invoice action is a separate read).
      expect(calls.filter(c => new URL(c.url).pathname === '/api/v1/external/projects')).toHaveLength(1);
      expect(matterListReads()).toHaveLength(1);
      expect(where()).toContain('invoiceRecord=');
    },
    SLOW
  );

  it(
    'the full click path: open the matter record view, click Submit invoice, land on the page with the matter chosen and no dialog open',
    async () => {
      await renderShell('/');

      fireEvent.click(await screen.findByRole('button', { name: 'Open the matter' }));
      const dialog = await screen.findByRole('dialog', undefined, WAIT);
      await within(dialog).findByRole('heading', { name: 'Acme v. Roadrunner' }, WAIT);
      const submit = await within(dialog).findByRole('button', { name: /submit invoice/i }, WAIT);
      fireEvent.click(submit);

      // The record view closes and the router goes to the page, with the page's parameter (not `record`).
      await waitFor(() => expect(where()).toMatch(/^\/invoices\/submit\?invoiceRecord=/), WAIT);
      expect(where()).not.toMatch(/[?&]record=/);
      expect(isChecked(await matterRadio(false))).toBe(true);
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull(), WAIT);
      // The app-wide deep link did not re-open anything over the page.
      await settle();
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(isChecked(await matterRadio(false))).toBe(true);
    },
    SLOW
  );
});
