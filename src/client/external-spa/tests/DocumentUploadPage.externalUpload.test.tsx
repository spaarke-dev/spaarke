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

/** A scripted response: a bare status (a ProblemDetails body saying `refused` for an error), or status plus body. */
type Scripted = number | { status: number; body: string };

let sent: Sent[];
let statuses: Scripted[];

/** A ProblemDetails body as the BFF writes it. */
const problem = (status: number, detail: string): Scripted => ({
  status,
  body: JSON.stringify({ status, title: 'refused', detail }),
});

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
      const scripted = statuses.length > 0 ? (statuses.shift() as Scripted) : 201;
      const status = typeof scripted === 'number' ? scripted : scripted.status;
      const responseBody =
        typeof scripted === 'number'
          ? status < 300
            ? JSON.stringify({ id: 'doc-1' })
            : JSON.stringify({ detail: 'refused' })
          : scripted.body;
      return new Response(responseBody, { status, headers: { 'Content-Type': 'application/json' } });
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
  const acquirer = vi.fn(async (scope?: string, _options?: { forceRefresh?: boolean }) => {
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
      // select_account: the IdP must not silently hand back the session whose token was just refused.
      expect(h.loginRedirect).toHaveBeenCalledWith({
        scopes: [`api://${CONTOSO.bffAppId}/user_impersonation`],
        prompt: 'select_account',
      });
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
      // Nothing uploaded: a warning, not the green success checkmark.
      expect(screen.getByTestId('upload-result-warning-icon')).toBeTruthy();
      expect(screen.queryByTestId('upload-result-success-icon')).toBeNull();
      expect(acquirer).not.toHaveBeenCalled();
      expect(sent).toHaveLength(0);
    },
    SLOW
  );

  it(
    '409 / 422 are per-file outcomes (the other files still upload); the collision 409 still names the file',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [
        problem(
          409,
          "A file named 'dup.pdf' already exists in this service request. Nothing was uploaded or changed - rename the file and try again."
        ),
        201,
        problem(
          422,
          'This service request has no storage container configured, so the document cannot be uploaded. Please contact the service request owner.'
        ),
      ];
      renderAt(DocumentUploadPage, `?entityType=sprk_servicerequest&entityId=${RECORD_ID}`);
      await uploadFiles('dup.pdf', 'ok.pdf', 'nostore.pdf');

      expect(await screen.findByText('1 of 3 files uploaded')).toBeTruthy();
      expect(screen.getByText(/dup\.pdf: A file named 'dup\.pdf' already exists/)).toBeTruthy();
      expect(screen.getByText(/nostore\.pdf: This service request has no storage container configured/)).toBeTruthy();
      expect(sent).toHaveLength(3);
    },
    SLOW
  );

  it(
    'an owner-refusal 409 shows the server detail - it is NOT reported as "already exists"',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('workforce');
      const detail =
        'The document cannot be filed because no owning team could be resolved for this project. Please contact the project team.';
      statuses = [problem(409, detail)];
      renderAt(DocumentUploadPage, `?entityType=sprk_project&entityId=${RECORD_ID}`);
      await uploadFiles('brief.pdf');

      expect(await screen.findByText('0 of 1 files uploaded')).toBeTruthy();
      expect(screen.getByText(`brief.pdf: ${detail}`)).toBeTruthy();
      expect(document.body.textContent).not.toMatch(/already exists/);
    },
    SLOW
  );

  it.each([
    [503, 'The service request could not be read just now. Please try again shortly.'],
    [400, 'A non-empty file is required'],
    [413, 'The file is larger than the upload limit.'],
    [502, 'The document could not be stored. Please try again.'],
  ])(
    'a %s shows the server detail for that file, not a generic "Upload failed."',
    async (status, detail) => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [problem(status, detail)];
      renderAt(DocumentUploadPage, `?entityType=sprk_servicerequest&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf');

      expect(await screen.findByText('0 of 1 files uploaded')).toBeTruthy();
      expect(screen.getByText(`a.pdf: ${detail}`)).toBeTruthy();
      expect(document.body.textContent).not.toMatch(/Upload failed\./);
    },
    SLOW
  );

  it(
    'with no ProblemDetails detail (an empty or non-JSON body) each status falls back to its own sensible text',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [
        { status: 409, body: '' },
        { status: 413, body: '<html>too big</html>' },
        { status: 422, body: '' },
        { status: 503, body: '' },
        { status: 400, body: '' },
      ];
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('c.pdf', 'big.pdf', 'nostore.pdf', 'down.pdf', 'bad.pdf');

      expect(await screen.findByText('0 of 5 files uploaded')).toBeTruthy();
      expect(screen.getByText(/c\.pdf: "c\.pdf" could not be uploaded because of a conflict/)).toBeTruthy();
      expect(screen.getByText(/big\.pdf: "big\.pdf" is too large to upload\./)).toBeTruthy();
      expect(screen.getByText(/nostore\.pdf: This matter has no document storage configured yet\./)).toBeTruthy();
      expect(screen.getByText(/down\.pdf: Upload failed\. Please try again later\./)).toBeTruthy();
      expect(screen.getByText(/bad\.pdf: Upload failed\./)).toBeTruthy();
    },
    SLOW
  );

  it(
    'nothing uploaded is a warning, not the green checkmark; a partial batch keeps the checkmark',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [problem(409, 'x'), 201, problem(409, 'y')];
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf', 'b.pdf', 'c.pdf');
      expect(await screen.findByText('1 of 3 files uploaded')).toBeTruthy();
      expect(screen.getByTestId('upload-result-success-icon')).toBeTruthy();
      expect(screen.queryByTestId('upload-result-warning-icon')).toBeNull();
    },
    SLOW
  );

  it(
    'a 401 after some files uploaded shows which files went through, beside the sign-in state',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('workforce');
      statuses = [201, 201, 401, 401]; // a, b uploaded; c refused, and so is bff-client's retry
      renderAt(DocumentUploadPage, `?entityType=sprk_project&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf', 'b.pdf', 'c.pdf', 'd.pdf');

      expect(await screen.findByTestId('bff-access-signin')).toBeTruthy();
      expect(screen.getByTestId('upload-batch-uploaded').textContent).toContain('a.pdf, b.pdf');
      expect(screen.getByTestId('upload-batch-not-uploaded').textContent).toContain('c.pdf, d.pdf');
      expect(screen.getByTestId('upload-batch-not-uploaded').textContent).not.toContain('a.pdf');
      expect(sent.map(s => s.fileName)).toEqual(['a.pdf', 'b.pdf', 'c.pdf', 'c.pdf']); // d.pdf was never sent
    },
    SLOW
  );

  it(
    'a 403 after a file uploaded shows the uploaded file beside the denied state; a first-file 403 shows no "uploaded" line',
    async () => {
      const { DocumentUploadPage } = await loadForPlane('ciam');
      statuses = [201, 403];
      const first = renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf', 'b.pdf');
      expect(await screen.findByTestId('bff-access-denied')).toBeTruthy();
      expect(screen.getByTestId('upload-batch-uploaded').textContent).toContain('a.pdf');
      expect(screen.getByTestId('upload-batch-not-uploaded').textContent).toContain('b.pdf');
      first.unmount();

      statuses = [403];
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('c.pdf');
      expect(await screen.findByTestId('bff-access-denied')).toBeTruthy();
      expect(screen.queryByTestId('upload-batch-uploaded')).toBeNull();
      expect(screen.getByTestId('upload-batch-not-uploaded').textContent).toContain('c.pdf');
    },
    SLOW
  );

  it(
    'the 401 retry forces a network refresh; the first attempt calls the acquirer with the scope alone (the 040 shape)',
    async () => {
      const { DocumentUploadPage, acquirer } = await loadForPlane('ciam');
      const scope = `api://${CONTOSO.bffAppId}/user_impersonation`;
      statuses = [401, 201];
      renderAt(DocumentUploadPage, `?entityType=sprk_matter&entityId=${RECORD_ID}`);
      await uploadFiles('a.pdf');

      expect(await screen.findByText('1 file uploaded successfully')).toBeTruthy();
      expect(acquirer.mock.calls).toEqual([[scope], [scope, { forceRefresh: true }]]);
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
