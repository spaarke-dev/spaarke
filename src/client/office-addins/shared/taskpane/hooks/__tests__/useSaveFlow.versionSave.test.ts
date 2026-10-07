/**
 * FR-11 client (spaarkeai-word-add-in-r1 task 024): what `useSaveFlow` SENDS for a version save versus a
 * create, how its idempotency key behaves, and how a refused version save is described.
 *
 * `crypto.subtle.digest` is backed by a REAL SHA-256 (node:crypto) here — unlike the constant digest mock in
 * `useSaveFlow.test.ts` — because the key-distinctness assertions are about the actual hash.
 */
import { renderHook, act, waitFor } from '@testing-library/react';
import { createHash } from 'crypto';
import {
  useSaveFlow,
  buildIdempotencyCanonical,
  computeIdempotencyKey,
  type SaveFlowContext,
  type SaveRequest,
} from '../useSaveFlow';
import type { EntitySearchResult } from '../useEntitySearch';

jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: {
    digest: jest.fn(async (_algorithm: string, data: ArrayLike<number>) => {
      const digest = createHash('sha256')
        .update(Buffer.from(Array.from(data)))
        .digest();
      // Built in THIS realm so `new Uint8Array(buffer)` in the hook reads it.
      const out = new Uint8Array(digest.length);
      out.set(digest);
      return out.buffer;
    }),
  },
});

const DOCUMENT_ID = '8c135b45-5da8-f111-aaab-7ced8ddc4a05';
const DOCUMENT_ID_BRACED_UPPER = '{8C135B45-5DA8-F111-AAAB-7CED8DDC4A05}';
const CONTENT_A = 'UEsDBBQAAAAIAAAAIQA=';
const CONTENT_B = 'UEsDBBQAAAAIAAAAIQB=';
const DOCUMENT_URL = 'https://contoso.sharepoint.com/contentstorage/CSP_x/Document%20Library/Brief.docx';

const MATTER: EntitySearchResult = {
  id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Acme v. Beta',
};

function wordContext(overrides: Partial<SaveFlowContext> = {}): SaveFlowContext {
  return {
    hostType: 'word',
    itemId: DOCUMENT_URL,
    itemName: 'Brief',
    attachments: [],
    documentUrl: DOCUMENT_URL,
    documentContentBase64: CONTENT_A,
    ...overrides,
  };
}

const versionTarget = (id = DOCUMENT_ID) => ({ mode: 'version' as const, existingDocumentId: id });

// ── fetch doubles ────────────────────────────────────────────────────────────────────────────────

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string>; json: () => Promise<unknown> };

const accepted = (): FakeResponse => {
  const body = {
    jobId: 'job-1',
    statusUrl: '/api/office/jobs/job-1',
    streamUrl: '/api/office/jobs/job-1/stream',
    status: 'Queued',
    duplicate: false,
    correlationId: 'corr-1',
  };
  return { ok: true, status: 202, text: async () => JSON.stringify(body), json: async () => body };
};

const problem = (status: number, extensions: Record<string, unknown>): FakeResponse => {
  const body = {
    type: 'https://spaarke.com/errors/office',
    title: 'Refused',
    status,
    detail: 'Refused.',
    ...extensions,
  };
  return { ok: false, status, text: async () => JSON.stringify(body), json: async () => body };
};

let saveResponses: Array<() => FakeResponse>;

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  saveResponses = [];
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const next = saveResponses.shift();
      if (!next) {
        throw new Error('Unexpected save request');
      }
      return next();
    }
    // Job polling after an accepted save — keep it "running" so nothing completes mid-test.
    const status = { jobId: 'job-1', status: 'Running', completedPhases: [] };
    return { ok: true, status: 200, text: async () => JSON.stringify(status), json: async () => status };
  });
});

const getAccessToken = jest.fn().mockResolvedValue('token');

function renderSaveFlow() {
  return renderHook(() => useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff' }));
}

function saveCalls(): Array<[string, RequestInit]> {
  return mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save')) as Array<
    [string, RequestInit]
  >;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the raw JSON wire body, asserted field by field
function bodyOf(call: [string, RequestInit]): Record<string, any> {
  return JSON.parse(String(call[1].body));
}

function headerOf(call: [string, RequestInit], name: string): string | null {
  const headers = call[1].headers;
  if (!headers) return null;
  if (typeof (headers as Headers).get === 'function') return (headers as Headers).get(name);
  const record = headers as Record<string, string>;
  const key = Object.keys(record).find(k => k.toLowerCase() === name.toLowerCase());
  return key !== undefined ? (record[key] ?? null) : null;
}

const sha256Hex = (text: string) => createHash('sha256').update(text, 'utf8').digest('hex');

// ── What is sent ─────────────────────────────────────────────────────────────────────────────────

describe('useSaveFlow — FR-11 version save payload (task 024)', () => {
  it('a resolved identity sends existingDocumentId (canonical bare-lowercase) with isNewVersion:true, and no targetEntity', async () => {
    saveResponses.push(accepted);
    const { result } = renderSaveFlow();

    // A remembered "Related to" selection must NOT ride along on a version save.
    act(() => result.current.setSelectedEntity(MATTER));
    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget(DOCUMENT_ID_BRACED_UPPER) }));
    });

    expect(saveCalls()).toHaveLength(1);
    const body = bodyOf(saveCalls()[0]!);
    expect(body.contentType).toBe('Document');
    expect(body.document.existingDocumentId).toBe(DOCUMENT_ID);
    expect(body.document.isNewVersion).toBe(true);
    expect(body.document).not.toHaveProperty('versionComment');
    expect(body).not.toHaveProperty('targetEntity');
    // The version key rides in the body (the server's persistent dedupe reads it) AND the header.
    expect(body.idempotencyKey).toMatch(/^[0-9a-f]{64}$/);
    expect(headerOf(saveCalls()[0]!, 'X-Idempotency-Key')).toBe(body.idempotencyKey);
    expect(result.current.error).toBeNull();
  });

  it('without a save target (an unidentified document) sends no version fields and creates exactly as before', async () => {
    saveResponses.push(accepted);
    const { result } = renderSaveFlow();

    act(() => result.current.setSelectedEntity(MATTER));
    await act(async () => {
      await result.current.startSave(wordContext());
    });

    const call = saveCalls()[0]!;
    const body = bodyOf(call);
    expect(body.document).not.toHaveProperty('existingDocumentId');
    expect(body.document).not.toHaveProperty('isNewVersion');
    expect(body).not.toHaveProperty('idempotencyKey');
    expect(body.targetEntity).toEqual({ entityType: 'Matter', entityId: MATTER.id, displayName: MATTER.name });

    // The key is the SHA-256 of the pre-task-024 canonical string, byte for byte.
    const legacyCanonical = JSON.stringify({
      sourceType: 'WordDocument',
      associationType: 'Matter',
      associationId: MATTER.id,
      emailId: DOCUMENT_URL,
      attachmentIds: [],
      includeBody: false,
      documentUrl: DOCUMENT_URL,
    });
    expect(headerOf(call, 'X-Idempotency-Key')).toBe(sha256Hex(legacyCanonical));
  });

  it('the "Save as new document" override (target: create) sends no version fields', async () => {
    saveResponses.push(accepted);
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: { mode: 'create' } }));
    });

    const body = bodyOf(saveCalls()[0]!);
    expect(body.contentType).toBe('Document');
    expect(body.document).not.toHaveProperty('existingDocumentId');
    expect(body.document).not.toHaveProperty('isNewVersion');
  });

  it('an Outlook save never carries version fields, whatever the context says', async () => {
    saveResponses.push(accepted);
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave({
        hostType: 'outlook',
        itemId: '<message@contoso.com>',
        itemName: 'Re: brief',
        attachments: [],
        saveTarget: versionTarget(),
      });
    });

    const body = bodyOf(saveCalls()[0]!);
    expect(body.contentType).toBe('Email');
    expect(body).not.toHaveProperty('document');
    expect(body).not.toHaveProperty('idempotencyKey');
  });

  it('a version target that is not a GUID is refused client-side and nothing is sent', async () => {
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget('not-a-guid') }));
    });

    expect(saveCalls()).toHaveLength(0);
    expect(result.current.flowState).toBe('error');
  });
});

// ── The idempotency key ──────────────────────────────────────────────────────────────────────────

describe('useSaveFlow — idempotency key (task 024)', () => {
  const legacyRequest = (): SaveRequest => ({
    sourceType: 'WordDocument',
    associationType: '',
    associationId: '',
    content: { emailId: DOCUMENT_URL, includeBody: false, attachmentIds: [], documentUrl: DOCUMENT_URL },
    processing: { profileSummary: true, ragIndex: true, deepAnalysis: false },
  });

  it('a version save and a non-version save of the same file name produce different keys', async () => {
    const version = {
      existingDocumentId: DOCUMENT_ID,
      fileName: 'Brief.docx',
      contentSha256: sha256Hex(CONTENT_A),
      failedAttempts: 0,
    };

    expect(buildIdempotencyCanonical(legacyRequest(), version)).not.toBe(buildIdempotencyCanonical(legacyRequest()));
    const versionKey = await computeIdempotencyKey(legacyRequest(), version);
    const createKey = await computeIdempotencyKey(legacyRequest());
    expect(versionKey).toMatch(/^[0-9a-f]{64}$/);
    expect(createKey).toMatch(/^[0-9a-f]{64}$/);
    expect(versionKey).not.toBe(createKey);
  });

  it('the non-version canonical string is byte-identical to the pre-task-024 format', () => {
    expect(buildIdempotencyCanonical(legacyRequest())).toBe(
      JSON.stringify({
        sourceType: 'WordDocument',
        associationType: '',
        associationId: '',
        emailId: DOCUMENT_URL,
        attachmentIds: [],
        includeBody: false,
        documentUrl: DOCUMENT_URL,
      })
    );
  });

  it('two revisions of one document get different keys; an identical re-send gets the same key', async () => {
    saveResponses.push(accepted, accepted, accepted);
    const { result } = renderSaveFlow();

    for (const content of [CONTENT_A, CONTENT_A, CONTENT_B]) {
      await act(async () => {
        await result.current.startSave(wordContext({ documentContentBase64: content, saveTarget: versionTarget() }));
      });
    }

    const [first, resend, revision] = saveCalls().map(c => bodyOf(c).idempotencyKey as string);
    expect(resend).toBe(first);
    expect(revision).not.toBe(first);
  });

  it('a retry after a refused version save (423, locked) sends a NEW key, so it is not answered with the failed job', async () => {
    saveResponses.push(() => problem(423, { errorCode: 'OFFICE_019' }), accepted);
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget() }));
    });
    expect(result.current.flowState).toBe('error');
    expect(result.current.error?.recoverable).toBe(true);
    expect(result.current.error?.offerSaveAsNew).toBe(true);

    await act(async () => {
      result.current.retry();
    });

    const [refused, retried] = saveCalls();
    expect(bodyOf(retried!).document.existingDocumentId).toBe(DOCUMENT_ID);
    expect(bodyOf(retried!).idempotencyKey).not.toBe(bodyOf(refused!).idempotencyKey);
    expect(headerOf(retried!, 'X-Idempotency-Key')).not.toBe(headerOf(refused!, 'X-Idempotency-Key'));
  });
});

// ── Completion ───────────────────────────────────────────────────────────────────────────────────

describe('useSaveFlow — version save completion (task 024)', () => {
  it('completes on the EXISTING document when the job reports Completed without an artifact', async () => {
    // The synchronous save path marks the job Completed with no Result.Artifact (only the AI workers set one,
    // later). A version save's document is, by contract, the one it named — so the pane can finish on it
    // instead of stopping at a "Completed" card with no way forward.
    saveResponses.push(accepted);
    mockFetch.mockImplementation(async (url: string) => {
      if (String(url).includes('/api/office/save')) {
        return saveResponses.shift()!();
      }
      const status = { jobId: 'job-1', status: 'Completed', completedPhases: [] };
      return { ok: true, status: 200, text: async () => JSON.stringify(status), json: async () => status };
    });
    const onComplete = jest.fn();
    const { result } = renderHook(() =>
      useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete })
    );

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget(DOCUMENT_ID_BRACED_UPPER) }));
    });

    await waitFor(() => expect(result.current.flowState).toBe('complete'));
    expect(result.current.savedDocumentId).toBe(DOCUMENT_ID);
    expect(onComplete).toHaveBeenCalledWith(DOCUMENT_ID, '');
  });

  it('a completed job artifact, when present, still wins over the target id', async () => {
    const artifactId = '11111111-2222-3333-4444-555555555555';
    saveResponses.push(accepted);
    mockFetch.mockImplementation(async (url: string) => {
      if (String(url).includes('/api/office/save')) {
        return saveResponses.shift()!();
      }
      const status = {
        jobId: 'job-1',
        status: 'Completed',
        completedPhases: [],
        result: { artifact: { type: 'Document', id: artifactId, webUrl: 'https://contoso/doc' } },
      };
      return { ok: true, status: 200, text: async () => JSON.stringify(status), json: async () => status };
    });
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget() }));
    });

    await waitFor(() => expect(result.current.flowState).toBe('complete'));
    expect(result.current.savedDocumentId).toBe(artifactId);
    expect(result.current.savedDocumentUrl).toBe('https://contoso/doc');
  });
});

// ── Refused version saves ────────────────────────────────────────────────────────────────────────

describe('useSaveFlow — refused version saves (task 024)', () => {
  it.each([
    [404, 'OFFICE_016'],
    [409, 'OFFICE_017'],
    [400, 'OFFICE_018'],
  ])('HTTP %i %s → not retryable, and "Save as new document" is offered', async (status, errorCode) => {
    saveResponses.push(() => problem(status, { errorCode }));
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget() }));
    });

    expect(result.current.flowState).toBe('error');
    expect(result.current.error?.recoverable).toBe(false);
    expect(result.current.error?.offerSaveAsNew).toBe(true);
  });

  it('a 403 from the version-save filter (no errorCode: not authorized OR unknown id) offers save-as-new and is not retryable', async () => {
    saveResponses.push(() => problem(403, { reasonCode: 'sdap.access.deny.insufficient_rights' }));
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget() }));
    });

    expect(result.current.error?.title).toBe('Cannot Add a Version');
    expect(result.current.error?.recoverable).toBe(false);
    expect(result.current.error?.offerSaveAsNew).toBe(true);
  });

  it('a 403 whose reasonCode is the authorization system failure is retryable and does not push to a new document', async () => {
    saveResponses.push(() => problem(403, { reasonCode: 'sdap.access.error.system_failure' }));
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext({ saveTarget: versionTarget() }));
    });

    expect(result.current.error?.recoverable).toBe(true);
    expect(result.current.error?.offerSaveAsNew).toBeUndefined();
  });

  it('a refused CREATE save never offers save-as-new', async () => {
    saveResponses.push(() => problem(403, { errorCode: 'OFFICE_009' }));
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext());
    });

    expect(result.current.flowState).toBe('error');
    expect(result.current.error?.offerSaveAsNew).toBeUndefined();
  });
});
