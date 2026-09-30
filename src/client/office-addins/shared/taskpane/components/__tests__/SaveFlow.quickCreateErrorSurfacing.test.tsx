/**
 * Task 053 (spaarkeai-word-add-in-r1): end-to-end proof that a failed quick-create or entity search no
 * longer vanishes. Before this task, `SaveFlow.tsx` discarded every non-OK response from these two fetches
 * (`createRelatedRecord`'s `if (!res.ok) return null`, `relatedSearch`'s `if (!res.ok) return []`) — so
 * task 031's newly load-bearing Project/Matter owner 403 (`errorCode: "owner_unresolved"`, an actionable
 * `detail`, no row written) reached the user as nothing happening at all.
 *
 * Unlike `SaveFlow.matterTypeQuickCreate.test.tsx` (which proves the OUTBOUND POST body shape),
 * this suite proves the INBOUND failure path: a mocked `fetch` returning the real BFF response shape
 * (top-level `errorCode`/`detail` — confirmed via `OfficeQuickCreateContractTests`/
 * `OfficeQuickCreateProjectContractTests`, ASP.NET's `Results.Problem` extensions-flattening, NOT the
 * `extensions.code` nesting specific to the separate global-exception-handler path task 050 fixed) all the
 * way to visible, server-sourced text in the rendered pane. `RelatedToPicker.errorSurfacing.test.tsx`
 * covers the same contract at the narrower `RelatedToPicker` boundary with mocked callbacks instead of a
 * mocked `fetch`.
 *
 * NEW FILE — ADR-038 (new tests in new files); mirrors `SaveFlow.matterTypeQuickCreate.test.tsx`'s
 * mocking/render conventions (real `SaveFlow` + real `RelatedToPicker`, `DocumentProfileSection` +
 * `SseClient` mocked as unrelated to this path).
 */
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

// Same budget rationale as the sibling matter-type-quickcreate suite: real-timer typing under parallel
// jest load can approach the package's default 10s testTimeout. Every fetch here is mocked.
const TEST_TIMEOUT_MS = 60000;
const WAIT = { timeout: 10000 };

// ResizeObserver (Fluent v9 MessageBar reflow detection) is polyfilled globally in jest.setup.js
// (task 071) — removed the per-file copy that used to live here.

type FakeResponse = { ok: boolean; status: number; json: () => Promise<unknown> };
const json = (ok: boolean, status: number, body: unknown): FakeResponse => ({ ok, status, json: async () => body });
const nonJson = (ok: boolean, status: number): FakeResponse => ({
  ok,
  status,
  json: async () => {
    throw new SyntaxError('Unexpected end of input');
  },
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

describe('SaveFlow — a refused Project quick-create surfaces the server detail (task 031 owner_unresolved / task 053)', () => {
  it(
    'shows the exact server `detail` text for a 403 owner_unresolved response, not a generic message',
    async () => {
      const serverDetail =
        'Your account could not be matched to a Dataverse user, so the new project could not be assigned ' +
        'to you and was not created. Ask an administrator to check that your user is provisioned in this environment.';
      mockFetch.mockImplementation(async (url: string) => {
        const u = String(url);
        if (u.includes('/api/office/quickcreate/project')) {
          // The REAL wire shape: errorCode/detail/correlationId/entityType as TOP-LEVEL JSON properties
          // (OfficeEndpoints.QuickCreateAsync's own catch block, via Results.Problem's extensions
          // flattening) — not nested under an "extensions" object.
          return json(false, 403, {
            type: 'https://spaarke.com/errors/office/owner_unresolved',
            title: 'Project Not Created',
            status: 403,
            detail: serverDetail,
            errorCode: 'owner_unresolved',
            correlationId: 'corr-1',
            entityType: 'project',
          });
        }
        return json(true, 200, {});
      });

      renderPane();

      await userEvent.click(screen.getByRole('radio', { name: 'Project' }));
      await userEvent.click(await screen.findByRole('button', { name: 'New' }, WAIT));
      await userEvent.type(screen.getByLabelText('New Project name'), 'Acme Expansion');
      await userEvent.click(screen.getByRole('button', { name: 'Create' }));

      const error = await screen.findByText(serverDetail, {}, WAIT);
      expect(error).toHaveAttribute('role', 'alert');
      // The pre-053 generic replacement must be gone for a failure the server explained.
      expect(screen.queryByText("Couldn't create the Project.")).toBeNull();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'a non-ProblemDetails 502 (infrastructure failure, no parseable body) still produces a sensible message and never crashes the pane',
    async () => {
      mockFetch.mockImplementation(async (url: string) => {
        const u = String(url);
        if (u.includes('/api/office/quickcreate/project')) {
          return nonJson(false, 502);
        }
        return json(true, 200, {});
      });

      renderPane();

      await userEvent.click(screen.getByRole('radio', { name: 'Project' }));
      await userEvent.click(await screen.findByRole('button', { name: 'New' }, WAIT));
      await userEvent.type(screen.getByLabelText('New Project name'), 'Acme Expansion');
      await userEvent.click(screen.getByRole('button', { name: 'Create' }));

      // A sensible, non-empty message renders — the pane is not left unchanged with no feedback, and
      // no unhandled rejection/error boundary trips (jsdom would otherwise surface it as a test failure).
      await waitFor(() => {
        const alerts = screen.getAllByRole('alert');
        expect(alerts.length).toBeGreaterThan(0);
        expect(alerts.some(el => (el.textContent ?? '').length > 0)).toBe(true);
      }, WAIT);
    },
    TEST_TIMEOUT_MS
  );
});

describe('SaveFlow — a failed entity search is distinct from "no matches" (task 053)', () => {
  it(
    'a 500 from /api/office/search/entities shows an error + Retry, not an empty result list',
    async () => {
      mockFetch.mockImplementation(async (url: string) => {
        const u = String(url);
        if (u.includes('/api/office/search/entities')) {
          return json(false, 500, {
            type: 'https://spaarke.com/errors/office/internal_error',
            title: 'Internal Server Error',
            status: 500,
            detail: 'An unexpected error occurred while searching.',
            errorCode: 'OFFICE_INTERNAL',
          });
        }
        return json(true, 200, {});
      });

      renderPane();

      await userEvent.type(await screen.findByLabelText('Search Matter records', {}, WAIT), 'Acme');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));

      const error = await screen.findByText('An unexpected error occurred while searching.', {}, WAIT);
      expect(error).toHaveAttribute('role', 'alert');
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    },
    TEST_TIMEOUT_MS
  );

  it(
    'a 200 with zero results shows no error banner — a genuinely empty search stays quiet',
    async () => {
      mockFetch.mockImplementation(async (url: string) => {
        const u = String(url);
        if (u.includes('/api/office/search/entities')) {
          return json(true, 200, { results: [] });
        }
        return json(true, 200, {});
      });

      renderPane();

      await userEvent.type(await screen.findByLabelText('Search Matter records', {}, WAIT), 'Nonexistent');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));

      await waitFor(() => expect(mockFetch).toHaveBeenCalled(), WAIT);
      expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    },
    TEST_TIMEOUT_MS
  );
});
