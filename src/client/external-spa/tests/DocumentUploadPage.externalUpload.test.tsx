// DocumentUploadPage uploads through the external plane on both planes (spaarke-SPA-external-access-platform-r3 task 042,
// FR-18, #1566). The real page, WizardShell, FileUploadZone, bff-client and backend selection run; fetch and the token
// acquirer (the seam each plane's bootstrap points at its own strategy) are recording fakes, and useMsal is a fake whose
// loginRedirect is recorded. Every assertion is about what goes over the wire and what the page shows.
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

const h = vi.hoisted(() => ({ loginRedirect: vi.fn(async () => undefined) }));

// teams-js fetches its valid-domains list when first loaded; a fake keeps the recorded traffic to the SPA's own calls.
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
  useMsal: () => ({ instance: { loginRedirect: h.loginRedirect }, accounts: [], inProgress: 'none' }),
}));

const RECORD_ID = '3f2c9a10-5b6d-4e7f-8a9b-0c1d2e3f4a5b';
const CONTOSO = {
  customerKey: 'contoso',
  displayName: 'Contoso',
  bffBaseUrl: 'https://contoso-bff.test',
  bffAppId: 'aaaaaaaa-0000-0000-0000-00000000000a',
};
const SLOW = { timeout: 60000 };

interface Sent {
  url: string;
  method: string | undefined;
  authorization: string | undefined;
  contentType: string | undefined;
  fileName: string | null;
  formKeys: string[];
}

let sent: Sent[];
let statuses: number[];

beforeEach(() => {
  sent = [];
  statuses = [];
  h.loginRedirect.mockClear();
  localStorage.clear();
  sessionStorage.clear();
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      const body = init?.body;
      const form = body instanceof FormData ? body : null;
      const file = form?.get('file');
      sent.push({
        url,
        method: init?.method,
        authorization: headers.Authorization,
        contentType: headers['Content-Type'],
        fileName: file instanceof File ? file.name : null,
        formKeys: form ? Array.from(form.keys()) : [],
      });
      const status = statuses.length > 0 ? (statuses.shift() as number) : 201;
      return new Response(status < 300 ? JSON.stringify({ id: 'doc-1' }) : JSON.stringify({ detail: 'refused' }), {
        status,
        headers: { 'Content-Type': 'application/json' },
      });
    })
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

/** Fresh module graph; the plane's backend is activated and its acquirer installed, as the plane bootstrap does. */
async function loadForPlane(plane: 'workforce' | 'ciam' | null) {
  vi.resetModules();
  const selection = await import('../src/auth/backend-selection');
  const msalAuth = await import('../src/auth/msal-auth');
  const scopes: Array<string | undefined> = [];
  const acquirer = vi.fn(async (scope?: string) => {
    scopes.push(scope);
    return `${plane}-token`;
  });
  msalAuth.setActiveBffTokenAcquirer(acquirer);
  if (plane) selection.activateBackend(plane, { ...CONTOSO });
  const { DocumentUploadPage } = await import('../src/pages/DocumentUploadPage');
  return { selection, msalAuth, acquirer, scopes, DocumentUploadPage };
}

function renderAt(Page: React.FC<{ teamsHost?: boolean }>, search: string, teamsHost = false) {
  return render(
    React.createElement(
      MemoryRouter,
      { initialEntries: [`/upload${search}`] },
      React.createElement(
        Routes,
        null,
        React.createElement(Route, { path: '/upload', element: React.createElement(Page, { teamsHost }) }),
        React.createElement(Route, { path: '*', element: React.createElement('div', null, 'left the page') })
      )
    )
  );
}

/** Select files in the zone, go to the review step and press Upload. */
async function uploadFiles(...names: string[]) {
  const input = await waitFor(() => {
    const el = document.querySelector('input[type="file"]');
    if (!el) throw new Error('no file input yet');
    return el as HTMLInputElement;
  });
  const files = names.map(n => new File(['%PDF-1.4 test'], n, { type: 'application/pdf' }));
  fireEvent.change(input, { target: { files } });
  fireEvent.click(await screen.findByRole('button', { name: 'Next' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Upload' }));
}

const ROOTS: Array<[string, string]> = [
  ['sprk_project', 'projects'],
  ['sprk_matter', 'matters'],
  ['sprk_workassignment', 'workassignments'],
  ['sprk_servicerequest', 'servicerequests'],
];

describe('DocumentUploadPage — external document routes (task 031) via bffApiCall', () => {
  it.each(['workforce', 'ciam'] as const)(
    '%s plane: uploads through POST /api/v1/external/{root}/{id}/documents on the active backend with its token',
    async plane => {
      const { DocumentUploadPage, scopes } = await loadForPlane(plane);
      renderAt(DocumentUploadPage, `?entityType=sprk_project&entityId=${RECORD_ID}`);
      await uploadFiles('brief.pdf');

      expect(await screen.findByText('1 file uploaded successfully')).toBeTruthy();
      expect(sent).toHaveLength(1);
      expect(sent[0]).toMatchObject({
        url: `https://contoso-bff.test/api/v1/external/projects/${RECORD_ID}/documents`,
        method: 'POST',
        authorization: `Bearer ${plane}-token`,
        contentType: undefined, // multipart boundary is the browser's
        fileName: 'brief.pdf',
        formKeys: ['file'],
      });
      // The token came through the active-plane seam for this backend's user_impersonation scope.
      expect(scopes).toEqual([`api://${CONTOSO.bffAppId}/user_impersonation`]);
    },
    SLOW
  );

  it.each(ROOTS)(
    '%s → /api/v1/external/%s/{id}/documents (the only request; no other route)',
    async (entityType, segment) => {
      const { DocumentUploadPage } = await loadForPlane('workforce');
      renderAt(DocumentUploadPage, `?entityType=${entityType}&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf', 'b.pdf');

      expect(await screen.findByText('2 files uploaded successfully')).toBeTruthy();
      expect(sent.map(s => s.url)).toEqual([
        `https://contoso-bff.test/api/v1/external/${segment}/${RECORD_ID}/documents`,
        `https://contoso-bff.test/api/v1/external/${segment}/${RECORD_ID}/documents`,
      ]);
      expect(sent.map(s => s.fileName)).toEqual(['a.pdf', 'b.pdf']);
    },
    SLOW
  );

  it(
    'a containerId in the link is never sent — the server derives the container from the record',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}&containerId=b!evil-container`);
      await uploadFiles('a.pdf');

      await screen.findByText('1 file uploaded successfully');
      expect(sent).toHaveLength(1);
      expect(sent[0].url).not.toContain('evil');
      expect(sent[0].formKeys).toEqual(['file']);
    },
    SLOW
  );
});

describe('DocumentUploadPage — refusals', () => {
  it(
    '403 from the external route → the denied state; no further file is sent and no user is shown',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [403];
      renderAt(DocumentUploadPage, `?entityType=sprk_workassignment&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf', 'b.pdf');

      expect(await screen.findByTestId('bff-access-denied')).toBeTruthy();
      expect(screen.getByText(/permission to upload documents to this work assignment/)).toBeTruthy();
      expect(sent).toHaveLength(1);
      expect(document.body.textContent).not.toMatch(/Mock|Jane Smith|Sam Rivera|Dana Okafor/);
    },
    SLOW
  );

  it(
    '401 (after the one fresh-token retry) → the sign-in state; "Sign in again" redirects for the active login scope',
    async () => {
      const { DocumentUploadPage, msalAuth } = await loadForPlane('workforce');
      msalAuth.setActiveLoginScope(`api://${CONTOSO.bffAppId}/user_impersonation`);
      statuses = [401, 401];
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf');

      expect(await screen.findByTestId('bff-access-signin')).toBeTruthy();
      expect(sent).toHaveLength(2); // the request and bff-client's single retry
      fireEvent.click(screen.getByRole('button', { name: 'Sign in again' }));
      expect(h.loginRedirect).toHaveBeenCalledWith({ scopes: [`api://${CONTOSO.bffAppId}/user_impersonation`] });
      expect(document.body.textContent).not.toMatch(/Mock|Jane Smith|Sam Rivera|Dana Okafor/);
    },
    SLOW
  );

  it(
    'in Teams, "Sign in again" never redirects (NFR-04) — it reloads the tab to re-run the Teams sign-in',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('workforce');
      const reload = vi.fn();
      const realLocation = window.location;
      Object.defineProperty(window, 'location', { configurable: true, value: { ...realLocation, reload } });
      try {
        statuses = [401, 401];
        renderAt(DocumentUploadPage, `?entityType=sprk_project&entityId=${RECORD_ID}`, true);
        await uploadFiles('a.pdf');
        fireEvent.click(await screen.findByRole('button', { name: 'Sign in again' }));
        expect(reload).toHaveBeenCalledTimes(1);
        expect(h.loginRedirect).not.toHaveBeenCalled();
      } finally {
        Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
      }
    },
    SLOW
  );

  it(
    'no active backend → the upload fails closed: no token, no request, the file is reported failed',
    async () => {
      const { DocumentUploadPage, acquirer } = await loadForPlane(null);
      renderAt(DocumentUploadPage, `?entityType=sprk_project&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf');

      expect(await screen.findByText('0 of 1 files uploaded')).toBeTruthy();
      expect(screen.getByText(/a\.pdf: Upload failed\./)).toBeTruthy();
      expect(acquirer).not.toHaveBeenCalled();
      expect(sent).toHaveLength(0);
    },
    SLOW
  );

  it(
    '409 / 422 are per-file outcomes (the other files still upload)',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [409, 201, 422];
      renderAt(DocumentUploadPage, `?entityType=sprk_servicerequest&entityId=${RECORD_ID}`);
      await uploadFiles('dup.pdf', 'ok.pdf', 'nostore.pdf');

      expect(await screen.findByText('1 of 3 files uploaded')).toBeTruthy();
      expect(screen.getByText(/dup\.pdf: A file named "dup\.pdf" already exists/)).toBeTruthy();
      expect(screen.getByText(/nostore\.pdf: This service request has no document storage/)).toBeTruthy();
      expect(sent).toHaveLength(3);
    },
    SLOW
  );

  it.each([
    [
      'an unsupported record type',
      `?entityType=sprk_invoice&entityId=${RECORD_ID}`,
      /cannot be uploaded to this record type/,
    ],
    [
      'a malformed record id',
      '?entityType=sprk_project&entityId=..%2F..%2Fapi%2Fobo',
      /record id in the link is not valid/,
    ],
    ['missing parameters', '?entityType=sprk_project', /Missing required parameters/],
  ])(
    '%s is refused before any request',
    async (_label, search, message) => {
      const { DocumentUploadPage, acquirer } = await loadForPlane('ciam');
      renderAt(DocumentUploadPage, search);
      expect(await screen.findByText(message)).toBeTruthy();
      expect(document.querySelector('input[type="file"]')).toBeNull();
      expect(acquirer).not.toHaveBeenCalled();
      expect(sent).toHaveLength(0);
    },
    SLOW
  );
});
