/**
 * Unit tests for documentIdentityService.
 *
 * Covers spaarkeai-word-add-in-r1 task 013 (FR-01 client) acceptance criteria, mapping every
 * outcome documented in
 * projects/spaarkeai-word-add-in-r1/notes/012-identity-resolver-decisions.md §2:
 *   - 200 resolved:true → 'resolved'
 *   - 200 resolved:false, reason not_cloud_document / not_resolvable / not_spaarke_document → 'new'
 *   - 200 resolved:false, reason identity_conflict → 'conflict' (NEVER 'new')
 *   - 503 → 'indeterminate' (reason: 'unavailable') — NEVER 'new'
 *   - 403 reasonCode sdap.access.error.system_failure → 'indeterminate' (reason: 'system_failure')
 *   - any other 403 → 'denied'
 *   - network / 5xx / unexpected → 'error', never a thrown exception
 *   - empty / non-absolute documentUrl → 'new' (not_cloud_document) with NO network call
 *
 * Also covers `applyDocumentIdentityOutcome` — the pure merge-into-savedContext function App.tsx
 * uses to thread a resolution outcome into state (acceptance criteria 6-8). App.tsx has no
 * render-based test harness in this codebase (no App.test.tsx), so this is the only automated
 * proof of that merge behavior.
 */

import {
  resolveDocumentIdentity,
  applyDocumentIdentityOutcome,
  type DocumentIdentityContext,
} from '../documentIdentityService';
import { apiClient, ApiClientError } from '@shared/services';

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
  };
});

const mockPost = apiClient.post as jest.Mock;

describe('resolveDocumentIdentity', () => {
  beforeEach(() => {
    mockPost.mockReset();
  });

  describe('local guard — no network call', () => {
    it('returns new/not_cloud_document without a network call for an empty url', async () => {
      const outcome = await resolveDocumentIdentity('');
      expect(outcome).toEqual({ kind: 'new', reason: 'not_cloud_document' });
      expect(mockPost).not.toHaveBeenCalled();
    });

    it('returns new/not_cloud_document without a network call for undefined', async () => {
      const outcome = await resolveDocumentIdentity(undefined);
      expect(outcome).toEqual({ kind: 'new', reason: 'not_cloud_document' });
      expect(mockPost).not.toHaveBeenCalled();
    });

    it('returns new/not_cloud_document without a network call for whitespace', async () => {
      const outcome = await resolveDocumentIdentity('   ');
      expect(outcome).toEqual({ kind: 'new', reason: 'not_cloud_document' });
      expect(mockPost).not.toHaveBeenCalled();
    });

    it('returns new/not_cloud_document without a network call for a relative/schemeless string', async () => {
      // A bare filename/relative path has no URI scheme at all and is rejected by the URL
      // constructor — this is the "not absolute" half of task 012's notes §2 rule 3. It is NOT
      // realistic input from Office.context.document.url (which is either empty or a real URI),
      // but the guard covers it defensively.
      const outcome = await resolveDocumentIdentity('brief.docx');
      expect(outcome).toEqual({ kind: 'new', reason: 'not_cloud_document' });
      expect(mockPost).not.toHaveBeenCalled();
    });

    it('DOES send a file:// URI to the server rather than blocking it locally', async () => {
      // Task 012's live run (notes §8 row #4) fed the server `file:///C:/…/brief.docx` and got
      // `resolved:false, reason:not_cloud_document` back WITHOUT a Graph call — the server, not the
      // client, makes that determination. `file://` is a real, absolute URI scheme, so this client
      // guard must NOT intercept it — doing so would diverge from the documented live contract.
      mockPost.mockResolvedValueOnce({ resolved: false, reason: 'not_cloud_document' });

      const outcome = await resolveDocumentIdentity('file:///C:/Users/me/Documents/brief.docx');

      expect(outcome).toEqual({ kind: 'new', reason: 'not_cloud_document' });
      expect(mockPost).toHaveBeenCalledWith('/api/documents/resolve-identity', {
        documentUrl: 'file:///C:/Users/me/Documents/brief.docx',
      });
    });
  });

  describe('resolved', () => {
    it('maps a resolved response, canonicalizing GUIDs (ADR-044)', async () => {
      mockPost.mockResolvedValueOnce({
        resolved: true,
        documentId: '{8C135B45-5DA8-F111-AAAB-7CED8DDC4A05}',
        documentName: 'Examiner report draft',
        fileName: 'Examiner report draft.docx',
        relatedRecord: { entityType: 'sprk_matter', id: '{11111111-2222-3333-4444-555555555555}', name: 'PAT-191111' },
        reason: null,
      });

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/sites/legal/x.docx');

      expect(outcome).toEqual({
        kind: 'resolved',
        documentId: '8c135b45-5da8-f111-aaab-7ced8ddc4a05',
        documentName: 'Examiner report draft',
        fileName: 'Examiner report draft.docx',
        relatedRecord: {
          entityType: 'sprk_matter',
          id: '11111111-2222-3333-4444-555555555555',
          name: 'PAT-191111',
        },
      });
      expect(mockPost).toHaveBeenCalledWith('/api/documents/resolve-identity', {
        documentUrl: 'https://contoso.sharepoint.com/sites/legal/x.docx',
      });
    });

    it('maps a resolved response with no related record (unassociated document)', async () => {
      mockPost.mockResolvedValueOnce({
        resolved: true,
        documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        documentName: 'Untitled matter memo',
        fileName: 'memo.docx',
        relatedRecord: null,
        reason: null,
      });

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/sites/legal/memo.docx');

      expect(outcome).toEqual({
        kind: 'resolved',
        documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        documentName: 'Untitled matter memo',
        fileName: 'memo.docx',
        relatedRecord: null,
      });
    });

    it('tolerates a relatedRecord with no name', async () => {
      mockPost.mockResolvedValueOnce({
        resolved: true,
        documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        documentName: 'doc',
        fileName: 'doc.docx',
        relatedRecord: { entityType: 'sprk_project', id: 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff' },
      });

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toMatchObject({
        kind: 'resolved',
        relatedRecord: { entityType: 'sprk_project', id: 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff', name: null },
      });
    });
  });

  describe('new document (resolved: false, non-conflict reasons) — normal, not an error', () => {
    it.each(['not_cloud_document', 'not_resolvable', 'not_spaarke_document'] as const)(
      'maps reason=%s to kind=new',
      async reason => {
        mockPost.mockResolvedValueOnce({ resolved: false, reason });

        const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

        expect(outcome).toEqual({ kind: 'new', reason });
      }
    );

    it('falls back to not_resolvable for an unrecognized reason rather than throwing', async () => {
      mockPost.mockResolvedValueOnce({ resolved: false, reason: 'some_future_reason' });

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'new', reason: 'not_resolvable' });
    });
  });

  describe('identity_conflict — NOT new (never offer save-as-new)', () => {
    it('maps reason=identity_conflict to kind=conflict', async () => {
      mockPost.mockResolvedValueOnce({ resolved: false, reason: 'identity_conflict' });

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'conflict' });
    });
  });

  describe('indeterminate — never treated as new (never mints a duplicate row)', () => {
    it('maps a 503 to kind=indeterminate, reason=unavailable', async () => {
      mockPost.mockRejectedValueOnce(
        new ApiClientError({
          type: 'about:blank',
          title: 'Service Unavailable',
          status: 503,
          detail: 'identity_resolution_unavailable',
        })
      );

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'indeterminate', reason: 'unavailable' });
    });

    it('maps a 403 with reasonCode sdap.access.error.system_failure to kind=indeterminate, reason=system_failure', async () => {
      const error = new ApiClientError({
        type: 'about:blank',
        title: 'Forbidden',
        status: 403,
        detail: 'Dataverse unavailable during authorization check',
      });
      // reasonCode is a ProblemDetails extension not in the shared ApiError type — attach at runtime
      // the way the real fetch response would.
      (error.error as unknown as { reasonCode: string }).reasonCode = 'sdap.access.error.system_failure';
      mockPost.mockRejectedValueOnce(error);

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'indeterminate', reason: 'system_failure' });
    });
  });

  describe('denied — a plain 403 (caller may reach the file, not the record)', () => {
    it('maps a 403 with no reasonCode to kind=denied', async () => {
      mockPost.mockRejectedValueOnce(new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 }));

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'denied' });
    });

    it('maps a 403 with an unrelated reasonCode to kind=denied (not indeterminate)', async () => {
      const error = new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403 });
      (error.error as unknown as { reasonCode: string }).reasonCode = 'sdap.access.error.insufficient_rights';
      mockPost.mockRejectedValueOnce(error);

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'denied' });
    });
  });

  describe('error — the save flow must not be blocked', () => {
    it('maps a 500 to kind=error, never throws', async () => {
      mockPost.mockRejectedValueOnce(
        new ApiClientError({ type: 'about:blank', title: 'Internal Server Error', status: 500, detail: 'oops' })
      );

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome.kind).toBe('error');
      expect((outcome as { kind: 'error'; message: string }).message).toContain('oops');
    });

    it('maps an unexpected 400 to kind=error, never throws', async () => {
      mockPost.mockRejectedValueOnce(
        new ApiClientError({ type: 'about:blank', title: 'Bad Request', status: 400, detail: 'malformed' })
      );

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome.kind).toBe('error');
    });

    it('maps a network failure (non-ApiClientError) to kind=error, never throws', async () => {
      mockPost.mockRejectedValueOnce(new TypeError('network blew up'));

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'error', message: 'network blew up' });
    });

    it('maps a non-Error rejection to kind=error with a generic message, never throws', async () => {
      mockPost.mockRejectedValueOnce('some string rejection');

      const outcome = await resolveDocumentIdentity('https://contoso.sharepoint.com/x.docx');

      expect(outcome).toEqual({ kind: 'error', message: 'Document identity resolution failed.' });
    });
  });
});

describe('applyDocumentIdentityOutcome', () => {
  const toFriendlyRegardingType = (entity: string): string => {
    const map: Record<string, string> = { sprk_matter: 'Matter', sprk_project: 'Project', sprk_invoice: 'Invoice' };
    return map[entity] ?? entity;
  };

  it('merges a resolved outcome with a related record into prev (not a replace)', () => {
    // App.savedContext is DocumentIdentityContext intersected with other fields (SavedTodoContext);
    // this stands in for that wider shape to prove prev's OTHER fields survive the merge.
    const prev: DocumentIdentityContext & { communicationId?: string } = { communicationId: 'demo-123' };

    const next = applyDocumentIdentityOutcome(
      prev,
      {
        kind: 'resolved',
        documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        documentName: 'Examiner report draft',
        fileName: 'Examiner report draft.docx',
        relatedRecord: { entityType: 'sprk_matter', id: '11111111-2222-3333-4444-555555555555', name: 'PAT-191111' },
      },
      toFriendlyRegardingType
    );

    expect(next).toEqual({
      communicationId: 'demo-123', // preserved from prev, not clobbered
      documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      documentName: 'Examiner report draft',
      fileName: 'Examiner report draft.docx',
      relatedRecord: { entityType: 'sprk_matter', id: '11111111-2222-3333-4444-555555555555', name: 'PAT-191111' },
      regardingEntity: 'Matter', // friendly type, seeded for Create To Do
      regardingRecordId: '11111111-2222-3333-4444-555555555555',
      regardingName: 'PAT-191111',
    });
  });

  it('merges a resolved outcome with NO related record — no regarding fields are set', () => {
    const next = applyDocumentIdentityOutcome(
      undefined,
      {
        kind: 'resolved',
        documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        documentName: 'Untitled memo',
        fileName: 'memo.docx',
        relatedRecord: null,
      },
      toFriendlyRegardingType
    );

    expect(next).toEqual({
      documentId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      documentName: 'Untitled memo',
      fileName: 'memo.docx',
      relatedRecord: null,
    });
    expect(next).not.toHaveProperty('regardingEntity');
    expect(next).not.toHaveProperty('regardingRecordId');
  });

  it('omits regardingName when the related record has no name', () => {
    const next = applyDocumentIdentityOutcome(
      undefined,
      {
        kind: 'resolved',
        documentId: 'a',
        documentName: 'doc',
        fileName: 'doc.docx',
        relatedRecord: { entityType: 'sprk_project', id: 'b', name: null },
      },
      toFriendlyRegardingType
    );

    expect(next?.regardingEntity).toBe('Project');
    expect(next).not.toHaveProperty('regardingName');
  });

  it.each(['new', 'conflict', 'indeterminate', 'denied', 'error'] as const)(
    "returns prev BY REFERENCE, unchanged, for a '%s' outcome",
    kind => {
      const prev = { regardingEntity: 'Matter', regardingRecordId: 'x' };
      const outcome =
        kind === 'new'
          ? ({ kind: 'new', reason: 'not_cloud_document' } as const)
          : kind === 'conflict'
            ? ({ kind: 'conflict' } as const)
            : kind === 'indeterminate'
              ? ({ kind: 'indeterminate', reason: 'unavailable' } as const)
              : kind === 'denied'
                ? ({ kind: 'denied' } as const)
                : ({ kind: 'error', message: 'x' } as const);

      const next = applyDocumentIdentityOutcome(prev, outcome, toFriendlyRegardingType);

      expect(next).toBe(prev); // same reference — a React setState updater can bail out
    }
  );

  it('returns undefined, unchanged, when prev is undefined and the outcome is not resolved', () => {
    const next = applyDocumentIdentityOutcome(
      undefined,
      { kind: 'new', reason: 'not_cloud_document' },
      toFriendlyRegardingType
    );

    expect(next).toBeUndefined();
  });
});
