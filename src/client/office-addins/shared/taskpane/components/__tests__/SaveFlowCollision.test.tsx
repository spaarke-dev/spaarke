/**
 * Task 025 (spaarkeai-word-add-in-r1): the pane's two-option choice after a refused CREATE save
 * (OFFICE_020 name collision) — "Keep both" (an AllowRename retry), "Save as new version" (a retry
 * through the ALREADY-SHIPPED FR-11 version-save path, using the existingDocumentId the refusal
 * resolved), and dismissing (no further request — the refusal itself already left no bytes and no
 * sprk_document row, so there is nothing for a dismiss to undo).
 *
 * NEW FILE (not an edit to the existing, unrelated-red `SaveFlow.test.tsx`). Modeled on the sibling
 * `SaveFlow.matterTypeQuickCreate.test.tsx`'s proven `renderPane()` shape (word host,
 * `documentContentBase64` set, no `documentIdentity` — `isValid` is unconditionally `true` in
 * `useSaveFlow.ts`, and the create `saveMode.target` resolves without one).
 *
 * The mock fetch responses use `text()`, not `json()`: `useSaveFlow.ts`'s own save handler reads
 * `response.text()` first and JSON-parses it manually (the sibling quick-create test never exercises
 * this path, so its `json()`-only fake would NOT work here — verified by reproduction, root CLAUDE.md
 * §10/F.3).
 */
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

// ResizeObserver (Fluent v9 Dropdown/MessageBar reflow) is polyfilled globally in jest.setup.js
// (task 071) — removed the per-file copy that used to live here.

// Mock crypto.subtle for idempotency key computation (useSaveFlow.ts computeIdempotencyKey).
Object.defineProperty(global.crypto, 'subtle', {
  value: {
    digest: jest.fn().mockImplementation(async () => new Uint8Array(32).buffer),
  },
  configurable: true,
});

const TEST_TIMEOUT_MS = 30000;
const WAIT = { timeout: 10000 };

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string>; json: () => Promise<unknown> };
const textResponse = (ok: boolean, status: number, body: unknown): FakeResponse => ({
  ok,
  status,
  text: async () => JSON.stringify(body),
  // Task 088: the open-links read (and describeFetchFailure) parse with json(), not text().
  json: async () => body,
});

// Readable AND filed to the record this save targets — the server's `canSaveAsVersion: true` case (task 088
// AC6), i.e. exactly the payload that offered "Save as new version" before task 088.
const COLLISION_PROBLEM = {
  type: 'https://spaarke.com/errors/office/conflict',
  title: 'File Already Exists',
  status: 409,
  detail: 'A file named "Brief.docx" already exists here. Nothing was uploaded or changed.',
  errorCode: 'OFFICE_020',
  fileName: 'Brief.docx',
  existingDocumentId: '11aed095-30da-f011-8406-7ced8d1dc988',
  existingDocumentName: 'Brief',
  canSaveAsVersion: true,
};

// Task 088 (AC5): readable, but filed to a DIFFERENT record — identified (for Open), no version retry (#1005).
const UNFILED_COLLISION_PROBLEM = {
  ...COLLISION_PROBLEM,
  existingDocumentName: 'Unrelated Matter — Draft',
  canSaveAsVersion: false,
};

const OPEN_LINKS = {
  desktopUrl: 'ms-word:https://contoso.sharepoint.com/contentstorage/x/Brief.docx',
  webUrl: 'https://contoso.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc=x',
  mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  fileName: 'Brief.docx',
};

const ACCEPTED_SAVE_RESPONSE = (jobId: string) => ({
  jobId,
  statusUrl: `/office/jobs/${jobId}`,
  streamUrl: `/office/jobs/${jobId}/stream`,
  status: 'Queued',
  duplicate: false,
  correlationId: 'corr-1',
});

const mockFetch = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  global.fetch = mockFetch as unknown as typeof fetch;
});

function renderPane(props: { canOpenRecord?: boolean; canOpenDesktopWord?: boolean } = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        {...props}
      />
    </FluentProvider>
  );
}

/** Drives the pane to the collision state: renders, clicks Save, waits for the collision choices. */
async function triggerCollision(
  problem: Record<string, unknown> = COLLISION_PROBLEM,
  props: { canOpenRecord?: boolean; canOpenDesktopWord?: boolean } = {}
) {
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      return textResponse(false, 409, problem);
    }
    return textResponse(true, 200, {});
  });

  renderPane(props);
  await userEvent.click(await screen.findByRole('button', { name: 'Save' }, WAIT));
  await screen.findByRole('button', { name: 'Keep both' }, WAIT);
}

function openLinksCalls(): string[] {
  return mockFetch.mock.calls.map(([url]) => String(url)).filter(url => url.includes('/open-links'));
}

function latestSaveRequestBody(): Record<string, unknown> {
  const call = mockFetch.mock.calls.find(([url]) => String(url).includes('/api/office/save'));
  if (!call) {
    throw new Error('No /api/office/save request was made');
  }
  const [, init] = call as [string, RequestInit];
  return JSON.parse(String(init.body)) as Record<string, unknown>;
}

describe('SaveFlow — collision two-option choice (task 025)', () => {
  it(
    'a refused CREATE on a readable document filed to the target surfaces Keep both, Save as new version, Open and Dismiss (task 088 AC6)',
    async () => {
      await triggerCollision();

      expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save as new version' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Open in browser' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
      // Non-destructive framing: "Nothing was saved", never presented as a hard failure with no way
      // forward — the server's own detail text surfaces verbatim. Appears TWICE by design (the visible
      // MessageBar body, and the visually-hidden aria-live="assertive" announcer for screen readers).
      expect(screen.getAllByText(/nothing was uploaded or changed/i).length).toBeGreaterThan(0);
    },
    TEST_TIMEOUT_MS
  );

  it(
    '"Keep both" retries with document.allowRename, without re-sending existingDocumentId/isNewVersion',
    async () => {
      await triggerCollision();
      mockFetch.mockClear();
      mockFetch.mockImplementation(async () => textResponse(true, 202, ACCEPTED_SAVE_RESPONSE('job-rename')));

      await userEvent.click(screen.getByRole('button', { name: 'Keep both' }));

      await waitFor(() => expect(mockFetch).toHaveBeenCalled(), WAIT);
      const body = latestSaveRequestBody();
      const document = body.document as Record<string, unknown>;
      expect(document.allowRename).toBe(true);
      expect(document.existingDocumentId).toBeUndefined();
      expect(document.isNewVersion).toBeUndefined();
    },
    TEST_TIMEOUT_MS
  );

  it(
    '"Save as new version" retries through the FR-11 version-save path (existingDocumentId + isNewVersion)',
    async () => {
      await triggerCollision();
      mockFetch.mockClear();
      mockFetch.mockImplementation(async () => textResponse(true, 202, ACCEPTED_SAVE_RESPONSE('job-version')));

      await userEvent.click(screen.getByRole('button', { name: 'Save as new version' }));

      await waitFor(() => expect(mockFetch).toHaveBeenCalled(), WAIT);
      const body = latestSaveRequestBody();
      const document = body.document as Record<string, unknown>;
      expect(document.existingDocumentId).toBe(COLLISION_PROBLEM.existingDocumentId);
      expect(document.isNewVersion).toBe(true);
      expect(document.allowRename).toBeUndefined();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'dismissing sends no further request, creates nothing, and returns to the editable save form',
    async () => {
      await triggerCollision();
      mockFetch.mockClear();

      await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

      expect(screen.queryByRole('button', { name: 'Keep both' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save as new version' })).not.toBeInTheDocument();
      // The editable form is back — the same "Save" affordance the user started from.
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
      // No bytes moved, no document created: literally no request was sent for a dismiss to undo.
      expect(mockFetch).not.toHaveBeenCalled();
    },
    TEST_TIMEOUT_MS
  );

  // ── Task 055 (#1005): the prompt must name the document it would write into ──────────────────────

  it(
    'the collision NAMES the document "Save as new version" would write into',
    async () => {
      // #1005: the pane offered this retry against an opaque GUID, so a user could not see the target was
      // an unrelated document that merely shared Word's default filename. It versioned a patent report onto
      // a stranger's row. Naming the target is what makes declining possible.
      mockFetch.mockImplementation(async (url: string) => {
        if (String(url).includes('/api/office/save')) {
          return textResponse(false, 409, {
            ...COLLISION_PROBLEM,
            existingDocumentName: 'Examiner Report — Canadian Application',
          });
        }
        return textResponse(true, 200, {});
      });

      renderPane();
      await userEvent.click(await screen.findByRole('button', { name: 'Save' }, WAIT));
      await screen.findByRole('button', { name: 'Keep both' }, WAIT);

      // The name appears in the prompt — not merely in a tooltip or the raw detail string.
      expect(screen.getAllByText(/Examiner Report — Canadian Application/).length).toBeGreaterThan(0);
    },
    TEST_TIMEOUT_MS
  );

  it(
    'a collision whose document was withheld (unreadable, or none) offers ONLY Keep both and Dismiss — no Open, no version (task 088 AC7)',
    async () => {
      // The server's exact payload for a caller who cannot read the other document: no id, no name, and the
      // canSaveAsVersion key absent altogether (it is written only alongside an id).
      const {
        existingDocumentId: _id,
        existingDocumentName: _name,
        canSaveAsVersion: _flag,
        ...withheld
      } = COLLISION_PROBLEM;
      await triggerCollision(withheld, { canOpenRecord: true });

      expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save as new version' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Open in browser' })).not.toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );

  // ── Task 088 (UAT-5): the version retry is the server's FLAG; "Open" reaches the other file ────────────

  it(
    'a readable document filed to ANOTHER record shows Keep both, Open and Dismiss — never Save as new version (AC5)',
    async () => {
      await triggerCollision(UNFILED_COLLISION_PROBLEM);

      expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Open in browser' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save as new version' })).not.toBeInTheDocument();
      // The prompt names which document holds the name (task 055), so Open is not a blind click.
      expect(screen.getAllByText(/Unrelated Matter — Draft/).length).toBeGreaterThan(0);
    },
    TEST_TIMEOUT_MS
  );

  it(
    'an id WITHOUT canSaveAsVersion never offers Save as new version — the offer comes from the flag, not the id (#1005)',
    async () => {
      const { canSaveAsVersion: _flag, ...idOnly } = COLLISION_PROBLEM;
      await triggerCollision(idOnly);

      expect(screen.queryByRole('button', { name: 'Save as new version' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Open in browser' })).toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'Open calls GET /api/documents/{id}/open-links and launches its webUrl in a browser window (canOpenBrowserWindow host)',
    async () => {
      const openBrowserWindow = Office.context.ui.openBrowserWindow as jest.Mock;
      openBrowserWindow.mockClear();
      await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: true });
      mockFetch.mockClear();
      mockFetch.mockImplementation(async () => textResponse(true, 200, OPEN_LINKS));

      await userEvent.click(screen.getByRole('button', { name: 'Open in browser' }));

      await waitFor(() => expect(openBrowserWindow).toHaveBeenCalledTimes(1), WAIT);
      expect(openLinksCalls()).toEqual([
        `https://bff/api/documents/${COLLISION_PROBLEM.existingDocumentId}/open-links`,
      ]);
      expect(openBrowserWindow).toHaveBeenCalledWith(OPEN_LINKS.webUrl);
      // The ms-word: desktop link is never launched: openBrowserWindow accepts http/https only (notes/088 §3).
      expect(openBrowserWindow).not.toHaveBeenCalledWith(OPEN_LINKS.desktopUrl);
      // Opening the other file is not a save: nothing else was sent.
      expect(mockFetch.mock.calls.some(([url]) => String(url).includes('/api/office/save'))).toBe(false);
    },
    TEST_TIMEOUT_MS
  );

  // ── Task 094: "Open in Word" (PC/Mac trial) alongside "Open in browser" ──────────────────────────────

  it(
    'canOpenDesktopWord false (the default — Office on the web, or no desktop evidence) renders "Open in browser" only',
    async () => {
      await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: true });

      expect(screen.getByRole('button', { name: 'Open in browser' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Open in Word' })).not.toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'canOpenDesktopWord true (PC/Mac) also renders "Open in Word", which anchor-clicks the desktopUrl — never openBrowserWindow/window.open',
    async () => {
      const clickSpy = jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      const openBrowserWindow = Office.context.ui.openBrowserWindow as jest.Mock;
      openBrowserWindow.mockClear();
      const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => null);
      try {
        await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: true, canOpenDesktopWord: true });
        mockFetch.mockClear();
        mockFetch.mockImplementation(async () => textResponse(true, 200, OPEN_LINKS));

        await userEvent.click(screen.getByRole('button', { name: 'Open in Word' }));

        await waitFor(() => expect(clickSpy).toHaveBeenCalledTimes(1), WAIT);
        const anchor = clickSpy.mock.instances[0] as unknown as HTMLAnchorElement;
        expect(anchor.getAttribute('href')).toBe(OPEN_LINKS.desktopUrl);
        expect(openBrowserWindow).not.toHaveBeenCalled();
        expect(windowOpen).not.toHaveBeenCalled();
        // Opening the other file is not a save: nothing else was sent.
        expect(mockFetch.mock.calls.some(([url]) => String(url).includes('/api/office/save'))).toBe(false);
      } finally {
        clickSpy.mockRestore();
        windowOpen.mockRestore();
      }
    },
    TEST_TIMEOUT_MS
  );

  it(
    '"Open in Word" without a desktopUrl in the response shows a reason and opens nothing',
    async () => {
      const clickSpy = jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      try {
        await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: true, canOpenDesktopWord: true });
        mockFetch.mockClear();
        mockFetch.mockImplementation(async () => textResponse(true, 200, { ...OPEN_LINKS, desktopUrl: null }));

        await userEvent.click(screen.getByRole('button', { name: 'Open in Word' }));

        const reasons = await screen.findAllByText(/did not return a desktop link/i);
        expect(reasons.some(element => element.closest('[aria-live]') === null)).toBe(true);
        expect(clickSpy).not.toHaveBeenCalled();
      } finally {
        clickSpy.mockRestore();
      }
    },
    TEST_TIMEOUT_MS
  );

  it(
    'Open on a host WITHOUT openBrowserWindow (Office on the web) launches the same webUrl with window.open — decided by capability',
    async () => {
      const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => ({ opener: {} }) as unknown as Window);
      const openBrowserWindow = Office.context.ui.openBrowserWindow as jest.Mock;
      openBrowserWindow.mockClear();
      try {
        await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: false });
        mockFetch.mockClear();
        mockFetch.mockImplementation(async () => textResponse(true, 200, OPEN_LINKS));

        await userEvent.click(screen.getByRole('button', { name: 'Open in browser' }));

        await waitFor(() => expect(windowOpen).toHaveBeenCalledWith(OPEN_LINKS.webUrl, '_blank'), WAIT);
        expect(openBrowserWindow).not.toHaveBeenCalled();
      } finally {
        windowOpen.mockRestore();
      }
    },
    TEST_TIMEOUT_MS
  );

  it(
    'a failed open-links call shows its reason in the prompt and opens nothing — never a client-built URL',
    async () => {
      const openBrowserWindow = Office.context.ui.openBrowserWindow as jest.Mock;
      openBrowserWindow.mockClear();
      const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => null);
      try {
        await triggerCollision(UNFILED_COLLISION_PROBLEM, { canOpenRecord: true });
        mockFetch.mockClear();
        mockFetch.mockImplementation(async () =>
          textResponse(false, 409, {
            type: 'https://spaarke.com/errors/file',
            title: 'Document Has No File',
            status: 409,
            detail: 'Document 11aed095 has no file in storage.',
          })
        );

        await userEvent.click(screen.getByRole('button', { name: 'Open in browser' }));

        // Visible in the prompt itself — not only in the sr-only live region the announcement also uses.
        const reasons = await screen.findAllByText('Document 11aed095 has no file in storage.');
        expect(reasons.some(element => element.closest('[aria-live]') === null)).toBe(true);
        expect(openBrowserWindow).not.toHaveBeenCalled();
        expect(windowOpen).not.toHaveBeenCalled();
        // The prompt is still there to act on.
        expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument();
      } finally {
        windowOpen.mockRestore();
      }
    },
    TEST_TIMEOUT_MS
  );
});
