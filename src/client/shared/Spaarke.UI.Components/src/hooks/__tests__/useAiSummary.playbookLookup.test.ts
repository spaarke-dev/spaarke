/**
 * useAiSummary playbook-lookup URL-shape tests (task 021 / spec FR-03).
 *
 * Asserts the hook's stable-ID Pattern B migration:
 *  - The playbook-resolution fetch hits `/api/ai/playbooks/by-id/{id}`
 *    NOT the legacy `/api/ai/playbooks/by-name/Document%20Profile` route.
 *  - The Document Profile playbook ID literal
 *    (`18cf3cc8-02ec-f011-8406-7c1e520aa4df`) is encoded into the URL path.
 *  - A ProblemDetails 404 response on the by-id endpoint surfaces a
 *    user-friendly error ("Playbook unavailable") via the document's
 *    `error` state — never the raw HTTP status or response body string,
 *    per ADR-019 + ADR-015.
 *
 * Notes:
 *  - We do NOT exercise the full SSE analysis pipeline here; that is covered
 *    elsewhere (and is unchanged by this task). The first fetch in
 *    `streamDocument` is the playbook lookup, so the first call's URL is
 *    the load-bearing assertion.
 *  - We use `act` + `waitFor` from `@testing-library/react` so the hook's
 *    state transitions are committed before assertions.
 */

import { act, renderHook, waitFor } from '@testing-library/react';
import { useAiSummary } from '../useAiSummary';

// ---------------------------------------------------------------------------
// Fetch + Response stand-ins
// ---------------------------------------------------------------------------

const BASE_URL = 'https://spe-api-test.example.com';

const DOC = {
  documentId: 'doc-1111',
  driveId: 'drive-aaaa',
  itemId: 'item-bbbb',
  fileName: 'contract.pdf',
};

/**
 * Minimal Response stand-in covering the surface `useAiSummary` touches on
 * the playbook-lookup branch: `ok`, `status`, `json()`. We don't need to
 * model `body.getReader()` because the test simulates a failure path OR
 * forces the SSE branch to abort immediately.
 */
function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: '',
    headers: new Headers({ 'content-type': 'application/json' }),
    json: jest.fn().mockResolvedValue(body),
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    // body intentionally undefined — `useAiSummary` SSE path will treat as
    // unreadable and short-circuit; we never reach the loop in these tests.
    body: null,
  } as unknown as Response;
}

function makeProblemDetails404(): Response {
  const problem = {
    type: 'https://spaarke.com/problems/playbook-not-found',
    title: 'Not Found',
    status: 404,
    detail: "Playbook with id '18cf3cc8-02ec-f011-8406-7c1e520aa4df' not found.",
    instance: '/api/ai/playbooks/by-id/18cf3cc8-02ec-f011-8406-7c1e520aa4df',
  };
  return {
    ok: false,
    status: 404,
    statusText: 'Not Found',
    headers: new Headers({ 'content-type': 'application/problem+json' }),
    json: jest.fn().mockResolvedValue(problem),
    text: jest.fn().mockResolvedValue(JSON.stringify(problem)),
    body: null,
  } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useAiSummary — Pattern B stable-ID playbook lookup (FR-03)', () => {
  const originalFetch = global.fetch;
  let fetchMock: jest.Mock;

  beforeEach(() => {
    fetchMock = jest.fn();
    // Cast through unknown to avoid the (non-load-bearing) Response generic
    // mismatch — we control the shape of what the hook reads in our tests.
    (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    (global as unknown as { fetch: typeof fetch }).fetch = originalFetch;
    jest.clearAllMocks();
  });

  it('fetches /api/ai/playbooks/by-id/{id} on the first call (NOT /by-name/)', async () => {
    fetchMock.mockResolvedValueOnce(makeJsonResponse(200, { playbookId: '18cf3cc8-02ec-f011-8406-7c1e520aa4df' }));

    const { result } = renderHook(() => useAiSummary({ apiBaseUrl: BASE_URL, autoStart: true }));

    act(() => {
      result.current.addDocuments([DOC]);
    });

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalled();
    });

    const [firstUrl] = fetchMock.mock.calls[0];
    expect(typeof firstUrl).toBe('string');
    expect(firstUrl as string).toContain('/api/ai/playbooks/by-id/');
    expect(firstUrl as string).toContain('18cf3cc8-02ec-f011-8406-7c1e520aa4df');
    expect(firstUrl as string).not.toContain('/by-name/');
    expect(firstUrl as string).not.toContain('Document%20Profile');
  });

  it('on 404 ProblemDetails, the document state shows a user-friendly error (not raw HTTP)', async () => {
    fetchMock.mockResolvedValueOnce(makeProblemDetails404());

    const { result } = renderHook(() => useAiSummary({ apiBaseUrl: BASE_URL, autoStart: true }));

    act(() => {
      result.current.addDocuments([DOC]);
    });

    await waitFor(() => {
      const doc = result.current.documents.find(d => d.documentId === DOC.documentId);
      expect(doc?.status).toBe('error');
    });

    const doc = result.current.documents.find(d => d.documentId === DOC.documentId);
    expect(doc?.error).toMatch(/playbook unavailable/i);
    // Acceptance criterion: raw HTTP status / "404" must not leak into the
    // user-facing error per ADR-019 + ADR-015 logging hygiene rules.
    expect(doc?.error ?? '').not.toMatch(/HTTP 404/);
    expect(doc?.error ?? '').not.toMatch(/\b404\b/);
  });
});
