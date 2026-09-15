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
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FindView, resolveFindState, type FindState } from '../FindView';
import { apiClient, ApiClientError } from '@shared/services';
import type { DocumentIdentityState } from '../../../services/documentIdentityService';

// Fluent v9 MessageBar uses ResizeObserver for reflow detection; jsdom doesn't provide one.
class ResizeObserverMock {
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
(globalThis as any).ResizeObserver = (globalThis as any).ResizeObserver ?? ResizeObserverMock;

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
}) {
  return { data: fields };
}

function relatedDocumentsEnvelope(totalResults: number, warnings?: { code: string; message: string }[]) {
  return { nodes: [], metadata: { totalResults, warnings: warnings ?? null } };
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
});

describe('FindView (component)', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockPost.mockReset();
    mockGetAccount.mockReset();
    mockGetAccount.mockReturnValue({ tenantId: TENANT_ID });
  });

  describe('state 1 — no sprk_document', () => {
    it('shows the save prompt, a Save control, and issues no index-status read', async () => {
      const onGoToSave = jest.fn();
      renderWithProvider(
        <FindView documentIdentity={{ kind: 'new', reason: 'not_spaarke_document' }} onGoToSave={onGoToSave} />
      );

      expect(
        screen.getByText('Save this document to Spaarke so it can be indexed for AI similarity search.')
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
      expect(
        screen.getByText('Save this document to Spaarke so it can be indexed for AI similarity search.')
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

  describe('state 3 — indexed', () => {
    it('renders a results container and surfaces the D-032-2 PARTIAL_RESULTS warning', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(
        relatedDocumentsEnvelope(3, [
          { code: 'PARTIAL_RESULTS', message: 'Some related documents were not evaluated…' },
        ])
      );

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('3 similar documents found.')).toBeTruthy());
      expect(screen.getByText('Results may be incomplete')).toBeTruthy();
      expect(screen.getByText('Some related documents were not evaluated…')).toBeTruthy();

      expect(mockGet).toHaveBeenLastCalledWith(`/api/ai/visualization/related/${DOCUMENT_ID_CLEAN}`);
    });

    it('renders no warning when the graph metadata carries none', async () => {
      mockGet.mockResolvedValueOnce(profileEnvelope({ searchIndexed: true }));
      mockGet.mockResolvedValueOnce(relatedDocumentsEnvelope(1));

      renderWithProvider(<FindView documentIdentity={RESOLVED} />);

      await waitFor(() => expect(screen.getByText('1 similar document found.')).toBeTruthy());
      expect(screen.queryByText('Results may be incomplete')).toBeNull();
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
