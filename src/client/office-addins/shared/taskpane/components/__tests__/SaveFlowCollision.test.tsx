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

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string> };
const textResponse = (ok: boolean, status: number, body: unknown): FakeResponse => ({
  ok,
  status,
  text: async () => JSON.stringify(body),
});

const COLLISION_PROBLEM = {
  type: 'https://spaarke.com/errors/office/conflict',
  title: 'File Already Exists',
  status: 409,
  detail: 'A file named "Brief.docx" already exists here. Nothing was uploaded or changed.',
  errorCode: 'OFFICE_020',
  fileName: 'Brief.docx',
  existingDocumentId: '11aed095-30da-f011-8406-7ced8d1dc988',
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

function renderPane() {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

/** Drives the pane to the collision state: renders, clicks Save, waits for the two-option choice. */
async function triggerCollision() {
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      return textResponse(false, 409, COLLISION_PROBLEM);
    }
    return textResponse(true, 200, {});
  });

  renderPane();
  await userEvent.click(await screen.findByRole('button', { name: 'Save' }, WAIT));
  await screen.findByRole('button', { name: 'Keep both' }, WAIT);
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
    'a refused CREATE surfaces "Keep both" and "Save as new version", stated as a non-alarming choice',
    async () => {
      await triggerCollision();

      expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save as new version' })).toBeInTheDocument();
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
    'a collision with no resolvable existing document offers ONLY "Keep both" (no version target to retry)',
    async () => {
      mockFetch.mockImplementation(async (url: string) => {
        if (String(url).includes('/api/office/save')) {
          return textResponse(false, 409, { ...COLLISION_PROBLEM, existingDocumentId: undefined });
        }
        return textResponse(true, 200, {});
      });

      renderPane();
      await userEvent.click(await screen.findByRole('button', { name: 'Save' }, WAIT));

      expect(await screen.findByRole('button', { name: 'Keep both' }, WAIT)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save as new version' })).not.toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );
});
