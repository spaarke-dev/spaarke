/**
 * Unit tests for FindView (spaarkeai-word-add-in-r1 task 033, FR-16b).
 *
 * Covers:
 * - `resolveFindState` (pure): every `DocumentIdentityState` outcome × the tri-state
 *   `sprk_searchindexed` (true / false / null / undefined-while-loading), including the four richer
 *   identity outcomes (conflict / indeterminate / denied / error) the POML's naive "exists or not"
 *   model does not cover, and `documentIdentity === undefined` (Outlook — identity does not apply).
 * - State 1 (no `sprk_document`): save prompt, "Go to Save" control, and NO profile/index read call.
 * - State 2 (resolved, not indexed — false and null render identically): Run Index button; a click
 *   sends `POST /api/ai/rag/send-to-index` with `DocumentIds` containing the LOWERCASED, BRACE-FREE
 *   document GUID and `TenantId` from `authService.getAccount().tenantId` — asserted on the actual
 *   captured request body, not by inspection.
 * - A 200 response carrying `ChunksIndexed = 0` (or a per-document `Success: false`) is treated as a
 *   FAILURE: an explicit failure message renders and the view does NOT transition to the results state.
 * - A successful Run Index re-polls (not writes) `sprk_searchindexed` and transitions to the results
 *   state.
 * - State 3 (indexed): a D-032-2 `PARTIAL_RESULTS` warning on `GraphMetadata.warnings` is surfaced.
 * - The identity-conflict / identity-indeterminate / identity-denied / identity-error states each
 *   render distinct, honest copy (never the save prompt), with retry wired to `onRetryDocumentIdentity`
 *   where offered.
 * - A Run Index network failure (e.g. an unauthenticated 401) surfaces an error state rather than an
 *   unhandled exception.
 */

import React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FindView, resolveFindState, type FindState } from '../FindView';
import { apiClient, ApiClientError } from '@shared/services';
import type { DocumentIdentityState } from '../../../services/documentIdentityService';
import { openRecord } from '../../../services/openRecordLauncher';

// Task 092 (UAT-3): opening wiring — same pattern as `SaveFlow.openRecord.test.tsx`.
jest.mock('../../../services/openRecordLauncher');
const mockOpenRecord = openRecord as jest.MockedFunction<typeof openRecord>;

// ResizeObserver (Fluent v9 MessageBar reflow detection) is polyfilled globally in jest.setup.js
// (task 071) — removed the per-file copy that used to live here.

// jsdom doesn't implement IntersectionObserver either — FindResultsList's useLazyResults uses one for
// its sentinel-driven, NEVER-refetching reveal (task 034, Path 2). This mock captures every
// constructed observer's callback so a test can simulate the sentinel intersecting the viewport.
let intersectionCallbacks: IntersectionObserverCallback[] = [];
class IntersectionObserverMock {
  constructor(callback: IntersectionObserverCallback) {
    intersectionCallbacks.push(callback);
  }
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IntersectionObserver = IntersectionObserverMock;

function fireSentinelIntersection(): void {
  const entry = { isIntersecting: true } as IntersectionObserverEntry;
  intersectionCallbacks.forEach(cb => cb([entry], {} as IntersectionObserver));
}

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
    authService: {
      ...actual.authService,
      getAccount: jest.fn(),
    },
  };
});

// eslint-disable-next-line @typescript-eslint/no-var-requires
const { authService } = jest.requireMock('@shared/services') as {
  authService: { getAccount: jest.Mock };
};

const mockGet = apiClient.get as jest.Mock;
const mockPost = apiClient.post as jest.Mock;
const mockGetAccount = authService.getAccount;

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

// An intentionally UPPERCASE, braced GUID — proves cleanGuid runs on the way into the request body
// rather than assuming task 013 always hands back an already-clean value.
const DOCUMENT_ID_RAW = '{AAAAAAAA-1111-2222-3333-444444444444}';
const DOCUMENT_ID_CLEAN = 'aaaaaaaa-1111-2222-3333-444444444444';
const TENANT_ID = 'tttttttt-5555-6666-7777-888888888888';

const RESOLVED: DocumentIdentityState = {
  kind: 'resolved',
  documentId: DOCUMENT_ID_RAW,
  documentName: 'Master Services Agreement',
  fileName: 'MSA.docx',
  relatedRecord: null,
};

function profileEnvelope(fields: {
  searchIndexed?: boolean | null;
  searchIndexName?: string | null;
  summaryStatus?: number | null;
  keywords?: string | null;
  tldr?: string | null;
  summary?: string | null;
}) {
  return { data: fields };
}

function relatedDocumentsEnvelope(
  totalResults: number,
  warnings?: { code: string; message: string }[],
  nodes: unknown[] = []
) {
  return { nodes, metadata: { totalResults, warnings: warnings ?? null } };
}

function resultNode(id: string, label: string, similarity = 0.9) {
  return { id, type: 'related', data: { label, documentType: 'Contract', similarity } };
}

function matterHubNode(id: string, label: string) {
  return { id, type: 'matter', data: { label } };
}

describe('resolveFindState (pure)', () => {
  it('checking -> checking', () => {
    expect(resolveFindState('checking', undefined, undefined)).toEqual<FindState>({ kind: 'checking' });
  });

  it('undefined (Outlook — identity does not apply) -> no-document', () => {
    expect(resolveFindState(undefined, undefined, undefined)).toEqual<FindState>({ kind: 'no-document' });
  });

  it("kind 'new' -> no-document", () => {
    expect(resolveFindState({ kind: 'new', reason: 'not_cloud_document' }, undefined, undefined)).toEqual<FindState>({
      kind: 'no-document',
    });
  });

  it("kind 'conflict' -> identity-conflict", () => {
    expect(resolveFindState({ kind: 'conflict' }, undefined, undefined)).toEqual<FindState>({
      kind: 'identity-conflict',
    });
  });

  it("kind 'indeterminate' (unavailable) -> identity-indeterminate", () => {
    expect(resolveFindState({ kind: 'indeterminate', reason: 'unavailable' }, undefined, undefined)).toEqual<FindState>(
      { kind: 'identity-indeterminate', reason: 'unavailable' }
    );
  });

  it("kind 'indeterminate' (system_failure) -> identity-indeterminate", () => {
    expect(
      resolveFindState({ kind: 'indeterminate', reason: 'system_failure' }, undefined, undefined)
    ).toEqual<FindState>({ kind: 'identity-indeterminate', reason: 'system_failure' });
  });

  it("kind 'denied' -> identity-denied", () => {
    expect(resolveFindState({ kind: 'denied' }, undefined, undefined)).toEqual<FindState>({
      kind: 'identity-denied',
    });
  });

  it("kind 'error' -> identity-error, carrying the message", () => {
    expect(resolveFindState({ kind: 'error', message: 'boom' }, undefined, undefined)).toEqual<FindState>({
      kind: 'identity-error',
      message: 'boom',
    });
  });

  it('resolved + searchIndexed undefined (still loading) -> loading-index-status', () => {
    expect(resolveFindState(RESOLVED, undefined, undefined)).toEqual<FindState>({ kind: 'loading-index-status' });
  });

  it('resolved + searchIndexed true -> indexed', () => {
    expect(resolveFindState(RESOLVED, true, undefined)).toEqual<FindState>({
      kind: 'indexed',
      documentId: DOCUMENT_ID_RAW,
    });
  });

  it('resolved + searchIndexed false -> not-indexed', () => {
    expect(resolveFindState(RESOLVED, false, undefined)).toEqual<FindState>({
      kind: 'not-indexed',
      documentId: DOCUMENT_ID_RAW,
    });
  });

  it('resolved + searchIndexed null -> not-indexed (same as false — spec FR-16 does not distinguish)', () => {
    expect(resolveFindState(RESOLVED, null, undefined)).toEqual<FindState>({
      kind: 'not-indexed',
      documentId: DOCUMENT_ID_RAW,
    });
  });

  it('resolved + an index-status read error -> index-status-error, regardless of searchIndexed', () => {
    expect(resolveFindState(RESOLVED, true, 'network down')).toEqual<FindState>({
      kind: 'index-status-error',
      message: 'network down',
    });
  });

  // Task 077 gaps (b) + (c): a completed save is an identity source of last resort.
  describe('a completed save (savedDocumentId) — task 077', () => {
    it('overrides the two "we do not know of a record" outcomes: new, and undefined (Outlook)', () => {
      expect(
        resolveFindState({ kind: 'new', reason: 'not_spaarke_document' }, false, undefined, DOCUMENT_ID_CLEAN)
      ).toEqual<FindState>({ kind: 'not-indexed', documentId: DOCUMENT_ID_CLEAN });
      expect(resolveFindState(undefined, true, undefined, DOCUMENT_ID_CLEAN)).toEqual<FindState>({
        kind: 'indexed',
        documentId: DOCUMENT_ID_CLEAN,
      });
      expect(resolveFindState(undefined, undefined, undefined, DOCUMENT_ID_CLEAN)).toEqual<FindState>({
        kind: 'loading-index-status',
      });
    });

    it.each<[string, DocumentIdentityState, FindState['kind']]>([
      ['conflict', { kind: 'conflict' }, 'identity-conflict'],
      ['indeterminate', { kind: 'indeterminate', reason: 'unavailable' }, 'identity-indeterminate'],
      ['denied', { kind: 'denied' }, 'identity-denied'],
      ['error', { kind: 'error', message: 'boom' }, 'identity-error'],
    ])('does NOT override %s — an honest refusal must not be papered over by a save id', (_, identity, expected) => {
      expect(resolveFindState(identity, true, undefined, DOCUMENT_ID_CLEAN).kind).toBe(expected);
    });
  });
});

describe('FindView (component)', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockGetAccount.mockReset();
    mockGetAccount.mockReturnValue({ tenantId: TENANT_ID });
    intersectionCallbacks = [];
    mockOpenRecord.mockReset();
    mockOpenRecord.mockReturnValue({ opened: true });
  });

  describe('state 1 — no sprk_document', () => {
    it('shows the save prompt, a Save control, and issues no index-status read', async () => {
      const onGoToSave = jest.fn();
      renderWithProvider(
        <FindView documentIdentity={{ kind: 'new', reason: 'not_spaarke_document' }} onGoToSave={onGoToSave} />
      );

      // Regex, not the full string: task 077 appended a sentence telling the user what Find will show
      // once the item is saved. The save prompt itself — what this asserts — is unchanged.
      expect(
        screen.getByText(/Save this document to Spaarke so it can be indexed for AI similarity search\./)
      ).toBeTruthy();

      const goToSave = screen.getByRole('button', { name: /go to save/i });
      await userEvent.click(goToSave);
      expect(onGoToSave).toHaveBeenCalledTimes(1);

      await Promise.resolve();
      expect(mockGet).not.toHaveBeenCalled();
      expect(screen.queryByRole('button', { name: /run index/i })).toBeNull();
    });

    it('undefined documentIdentity (Outlook) renders the same save prompt', async () => {
      renderWithProvider(<FindView />);
      // Regex, not the full string: task 077 appended a sentence telling the user what Find will show
      // once the item is saved. The save prompt itself — what this asserts — is unchanged.
      expect(
        screen.getByText(/Save this document to Spaarke so it can be indexed for AI similarity search\./)
      ).toBeTruthy();
    });
  });

  describe('state 2 — resolved, not indexed', () => {
    it('shows the not-indexed copy and an enabled Run Index button', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: null, summaryStatus: null }));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText(/this document isn.t indexed yet\./i)).toBeTruthy());

      const runIndex = screen.getByRole('button', { name: /run index/i });
      expect(runIndex).toBeEnabled();
    });

    it('Run Index sends the lowercased, brace-free document id and the tenant id to the existing route', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: false }));
      mockPost.mockResolvedValueOnce({
        totalRequested: 1,
        successCount: 1,
        failedCount: 0,
        results: [{ documentId: DOCUMENT_ID_CLEAN, success: true, chunksIndexed: 5, indexName: 'idx' }],
      });
      // The re-poll after a successful Run Index.
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(0));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      const runIndex = await screen.findByRole('button', { name: /run index/i });
      await userEvent.click(runIndex);

      await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));

      expect(mockPost).toHaveBeenCalledWith('/api/ai/rag/send-to-index', {
        DocumentIds: [DOCUMENT_ID_CLEAN],
        TenantId: TENANT_ID,
      });
    });

    it('a 200 response with ChunksIndexed = 0 is a FAILURE — no transition to results', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: false }));
      mockPost.mockResolvedValueOnce({
        totalRequested: 1,
        successCount: 1,
        failedCount: 0,
        results: [{ documentId: DOCUMENT_ID_CLEAN, success: true, chunksIndexed: 0, indexName: 'idx' }],
      });

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      const runIndex = await screen.findByRole('button', { name: /run index/i });
      await userEvent.click(runIndex);

      await waitFor(() => expect(screen.getByText('Indexing failed')).toBeTruthy());

      // Only the one read that rendered state 2 — no re-poll happened, because the pipeline did not
      // actually succeed.
      expect(mockGet).toHaveBeenCalledTimes(1);
      expect(screen.getByText(/this document isn.t indexed yet\./i)).toBeTruthy();
    });

    it('a per-document Success: false is also a failure, surfacing the server error message', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: false }));
      mockPost.mockResolvedValueOnce({
        totalRequested: 1,
        successCount: 0,
        failedCount: 1,
        results: [
          { documentId: DOCUMENT_ID_CLEAN, success: false, chunksIndexed: 0, errorMessage: 'Document not found' },
        ],
      });

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      const runIndex = await screen.findByRole('button', { name: /run index/i });
      await userEvent.click(runIndex);

      await waitFor(() => expect(screen.getByText('Document not found')).toBeTruthy());
    });

    it('a network/auth failure (e.g. 401) surfaces an error state, not an unhandled exception', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: false }));
      mockPost.mockRejectedValueOnce(
        new ApiClientError({ type: 'about:blank', title: 'Unauthorized', status: 401, detail: 'Unauthorized' })
      );

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      const runIndex = await screen.findByRole('button', { name: /run index/i });
      // Must not throw out of the click handler.
      await expect(userEvent.click(runIndex)).resolves.not.toThrow();

      await waitFor(() => expect(screen.getByText('Indexing failed')).toBeTruthy());
      expect(screen.getByText('Unauthorized')).toBeTruthy();
    });
  });

  describe('state 3 — indexed (task 034, Path 2 — FindResultsList)', () => {
    it('renders the results list and surfaces the D-032-2 PARTIAL_RESULTS warning', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(
        relatedDocumentsEnvelope(
          3,
          [{ code: 'PARTIAL_RESULTS', message: 'Some related documents were not evaluated…' }],
          [resultNode('doc-1', 'MSA Draft')]
        )
      );

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      // Task 092: the "Most similar documents" heading is now ALWAYS present (even while loading), so
      // it's no longer a usable proxy for "the fetch resolved" — wait on the actual row text instead.
      await waitFor(() => expect(screen.getByText('MSA Draft')).toBeTruthy());
      expect(screen.getByText('Results may be incomplete')).toBeTruthy();
      expect(screen.getByText('Some related documents were not evaluated…')).toBeTruthy();

      expect(mockGet).toHaveBeenLastCalledWith(`/api/ai/visualization/related/${DOCUMENT_ID_CLEAN}`);
    });

    it('renders no warning when the graph metadata carries none', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(1, undefined, [resultNode('doc-1', 'MSA Draft')]));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('MSA Draft')).toBeTruthy());
      expect(screen.queryByText('Results may be incomplete')).toBeNull();
    });

    it('renders the explicit, announced empty state when there are no similar documents', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(0));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('No similar documents found')).toBeTruthy());
    });

    it('hub nodes render distinctly from ranked results, never as similarity matches', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(
        relatedDocumentsEnvelope(1, undefined, [
          resultNode('doc-1', 'MSA Draft'),
          matterHubNode('matter-1', 'Smith v Smith'),
        ])
      );

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('MSA Draft')).toBeTruthy());
      expect(screen.getByText(/Matter: Smith v Smith/)).toBeTruthy();
      // Task 092: the hub section now shares the ONE scroll container with the ranked list (by
      // design — a single scroll container is the whole point), so the scope that matters is the
      // RANKED LIST itself, not the shared scroller around it.
      const rankedList = screen.getByRole('list', { name: 'Similar Documents' });
      expect(rankedList.textContent).not.toContain('Smith v Smith');
    });

    it('no pager control of any kind renders, even with many results', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      const nodes = Array.from({ length: 45 }, (_, i) => resultNode(`doc-${i}`, `Document ${i}`));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(45, undefined, nodes));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('Document 0')).toBeTruthy());
      expect(screen.queryByText(/load more/i)).toBeNull();
      expect(screen.queryByText(/next page/i)).toBeNull();
      expect(screen.queryByRole('navigation')).toBeNull();
    });

    it('scrolling (sentinel-driven reveal) never issues a second call to the similarity route', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      const nodes = Array.from({ length: 45 }, (_, i) => resultNode(`doc-${i}`, `Document ${i}`));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(45, undefined, nodes));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('Document 0')).toBeTruthy());

      const relatedCallsBefore = mockGet.mock.calls.filter(call =>
        String(call[0]).includes('/api/ai/visualization/related/')
      ).length;
      expect(relatedCallsBefore).toBe(1);

      // Simulate the sentinel entering the viewport twice (reveals two more chunks).
      await act(async () => {
        fireSentinelIntersection();
      });
      await act(async () => {
        fireSentinelIntersection();
      });

      const relatedCallsAfter = mockGet.mock.calls.filter(call =>
        String(call[0]).includes('/api/ai/visualization/related/')
      ).length;
      expect(relatedCallsAfter).toBe(1); // still exactly one — reveal is DOM-only, never a re-fetch
    });
  });

  // Task 092 (UAT-3): Matching records must not wait on the documents request.
  describe('Matching records render independently of the Documents section (task 092, UAT-3)', () => {
    it('renders while the documents request is still loading', async () => {
      mockGet.mockResolvedValueOnce(
        profileEnvelope({ searchIndexed: true, summaryStatus: 100000002, keywords: 'indemnity' })
      );
      // The visualization GET stays pending until resolved below — documents stay in `loading` for
      // the assertions, then settled explicitly so no promise is left dangling for jest's teardown.
      let resolveDocs!: (value: unknown) => void;
      const docsPromise = new Promise(resolve => {
        resolveDocs = resolve;
      });
      mockGet.mockImplementationOnce(() => docsPromise);
      mockPost.mockResolvedValueOnce({
        results: [{ recordId: 'm-1', recordType: 'sprk_matter', recordName: 'Acme v. Globex', confidenceScore: 0.8 }],
      });

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('Finding similar documents…')).toBeTruthy());
      await waitFor(() => expect(screen.getByText('Acme v. Globex')).toBeTruthy());

      await act(async () => {
        resolveDocs(relatedDocumentsEnvelope(0));
      });
    });

    it('renders when the documents request fails', async () => {
      mockGet.mockResolvedValueOnce(
        profileEnvelope({ searchIndexed: true, summaryStatus: 100000002, keywords: 'indemnity' })
      );
      mockGet.mockRejectedValueOnce(
        new ApiClientError({ type: 'about:blank', title: 'Server Error', status: 500, detail: 'Boom' })
      );
      mockPost.mockResolvedValueOnce({
        results: [{ recordId: 'm-1', recordType: 'sprk_matter', recordName: 'Acme v. Globex', confidenceScore: 0.8 }],
      });

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('Boom')).toBeTruthy());
      await waitFor(() => expect(screen.getByText('Acme v. Globex')).toBeTruthy());
    });

    it('shows a confidence percentage and up to 3 match reasons end-to-end', async () => {
      mockGet.mockResolvedValueOnce(
        profileEnvelope({ searchIndexed: true, summaryStatus: 100000002, keywords: 'indemnity' })
      );
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(0));
      mockPost.mockResolvedValueOnce({
        results: [
          {
            recordId: 'm-1',
            recordType: 'sprk_matter',
            recordName: 'Acme v. Globex',
            confidenceScore: 0.876,
            matchReasons: ['Name match: <em>Acme</em>', 'Description match: indemnity clause'],
          },
        ],
      });

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('Acme v. Globex')).toBeTruthy());
      expect(screen.getByText(/88% match/)).toBeTruthy();
      expect(screen.getByText('Acme').tagName).toBe('STRONG');
      expect(screen.getByText('Description match: indemnity clause')).toBeTruthy();
    });
  });

  // Task 092 (UAT-3, NFR-10): opening a row — gated on `canOpenRecord` + `ORG_URL`, wired to the SAME
  // `openRecord` launcher `SaveFlow` uses (`SaveFlow.openRecord.test.tsx` is the precedent this mirrors).
  describe('opening a row (task 092, UAT-3, NFR-10)', () => {
    const ORG_URL = 'https://contoso.crm.dynamics.com';
    const originalOrgUrl = process.env.ORG_URL;

    afterAll(() => {
      if (originalOrgUrl === undefined) {
        delete process.env.ORG_URL;
      } else {
        process.env.ORG_URL = originalOrgUrl;
      }
    });

    it('document row: calls openRecord with sprk_document + the cleaned id when canOpenRecord + ORG_URL are both set', async () => {
      process.env.ORG_URL = ORG_URL;
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(1, undefined, [resultNode('doc-1', 'MSA Draft')]));

      renderWithProvider(<FindView documentIdentity={RESOLVED} canOpenRecord />);

      const row = await screen.findByRole('button', { name: /MSA Draft/ });
      await userEvent.click(row);

      expect(mockOpenRecord).toHaveBeenCalledWith({ orgUrl: ORG_URL, entityType: 'sprk_document', recordId: 'doc-1' });
    });

    it('a parent/hub row: calls openRecord with sprk_matter + its real id', async () => {
      process.env.ORG_URL = ORG_URL;
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(
        relatedDocumentsEnvelope(1, undefined, [
          resultNode('doc-1', 'MSA Draft'),
          matterHubNode('matter-bbbbbbbb-1111-2222-3333-444444444444', 'Smith v Smith'),
        ])
      );

      renderWithProvider(<FindView documentIdentity={RESOLVED} canOpenRecord />);

      const row = await screen.findByRole('button', { name: /Matter: Smith v Smith/ });
      await userEvent.click(row);

      expect(mockOpenRecord).toHaveBeenCalledWith({
        orgUrl: ORG_URL,
        entityType: 'sprk_matter',
        recordId: 'bbbbbbbb-1111-2222-3333-444444444444',
      });
    });

    it('renders every row as plain text (no button role) when canOpenRecord is false (default)', async () => {
      process.env.ORG_URL = ORG_URL;
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(1, undefined, [resultNode('doc-1', 'MSA Draft')]));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await screen.findByText('MSA Draft');
      expect(screen.queryByRole('button', { name: /MSA Draft/ })).toBeNull();
      expect(mockOpenRecord).not.toHaveBeenCalled();
    });

    it('renders every row as plain text when ORG_URL is not configured, even with canOpenRecord true', async () => {
      delete process.env.ORG_URL;
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(1, undefined, [resultNode('doc-1', 'MSA Draft')]));

      renderWithProvider(<FindView documentIdentity={RESOLVED} canOpenRecord />);

      await screen.findByText('MSA Draft');
      expect(screen.queryByRole('button', { name: /MSA Draft/ })).toBeNull();
      expect(mockOpenRecord).not.toHaveBeenCalled();
    });
  });

  // Task 077 gaps (b) + (c).
  describe('after a save in this session (task 077)', () => {
    it('a document saved in this session leaves the save prompt and reaches Run Index — without reopening it', async () => {
      mockGet.mockResolvedValue(profileEnvelope({ searchIndexed: false, summaryStatus: null }));
      const identity: DocumentIdentityState = { kind: 'new', reason: 'not_spaarke_document' };

      const { rerender } = renderWithProvider(<FindView documentIdentity={identity} />);
      expect(screen.getByText('Save this document to Spaarke')).toBeTruthy();
      expect(mockGet).not.toHaveBeenCalled();

      // The save completes: App threads SaveView.onComplete's id through as savedDocumentId.
      rerender(
        <FluentProvider theme={webLightTheme}>
          <FindView documentIdentity={identity} savedDocumentId={DOCUMENT_ID_CLEAN} />
        </FluentProvider>
      );

      expect(await screen.findByRole('button', { name: /run index/i })).toBeTruthy();
      expect(screen.queryByText('Save this document to Spaarke')).toBeNull();
      expect(mockGet).toHaveBeenCalledWith(`/api/v1/documents/${DOCUMENT_ID_CLEAN}`);
    });

    it('Outlook: before a save the prompt names the EMAIL; after a save, Find is reachable (FR-16 not amended)', async () => {
      mockGet.mockResolvedValue(profileEnvelope({ searchIndexed: false, summaryStatus: null }));

      // Outlook: identity resolution never applies (documentIdentity undefined).
      const { rerender } = renderWithProvider(<FindView itemNoun="email" />);
      expect(screen.getByText('Save this email to Spaarke')).toBeTruthy();

      rerender(
        <FluentProvider theme={webLightTheme}>
          <FindView itemNoun="email" savedDocumentId={DOCUMENT_ID_CLEAN} />
        </FluentProvider>
      );

      expect(await screen.findByRole('button', { name: /run index/i })).toBeTruthy();
      expect(screen.getByText(/This email isn.t indexed yet\./)).toBeTruthy();
    });
  });

  describe('the four richer identity outcomes never show the save prompt', () => {
    it('conflict', async () => {
      renderWithProvider(<FindView documentIdentity={{ kind: 'conflict' }} />);
      expect(screen.getByText(/can.t check this document for indexing/i)).toBeTruthy();
      expect(screen.queryByText(/save this document to spaarke/i)).toBeNull();
    });

    it('indeterminate offers a retry wired to onRetryDocumentIdentity', async () => {
      const onRetry = jest.fn();
      renderWithProvider(
        <FindView
          documentIdentity={{ kind: 'indeterminate', reason: 'unavailable' }}
          onRetryDocumentIdentity={onRetry}
        />
      );
      expect(screen.getByText(/couldn.t check this document/i)).toBeTruthy();
      await userEvent.click(screen.getByRole('button', { name: /try again/i }));
      expect(onRetry).toHaveBeenCalledTimes(1);
    });

    it('denied', async () => {
      renderWithProvider(<FindView documentIdentity={{ kind: 'denied' }} />);
      expect(screen.getByText(/you can.t check this document/i)).toBeTruthy();
    });

    it('error', async () => {
      renderWithProvider(<FindView documentIdentity={{ kind: 'error', message: 'Something broke' }} />);
      expect(screen.getByText('Something broke')).toBeTruthy();
    });
  });
});
