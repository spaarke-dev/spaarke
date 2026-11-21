/**
 * R4 task 022 (FR-14) — the optional size cap on `bffApiBlob`.
 *
 * The cap is additive: a call with no `limits` is exactly what it was (the document download and the invoice Files list), and
 * a call with `{ maxBytes }` never returns a body over it: a declared `Content-Length` over the cap aborts the request before
 * the body is read, and with no usable header the bytes are discarded when `blob.size` is over it.
 *
 * What runs for real: `bff-client.ts` (`bffApiBlob`, `authorizedFetch`, `executeFetch`). What is substituted: the token
 * acquirer, the active backend (host and URL builder), the invite-code store and `fetch`.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../src/auth/msal-auth', () => ({ acquireActiveBffToken: vi.fn(async () => 'test-token') }));
vi.mock('../src/auth/backend-selection', () => ({
  getActiveBackend: () => ({ bffBaseUrl: 'https://bff.example.test', bffScope: 'scope' }),
  bffRequestUrl: (backend: { bffBaseUrl: string }, path: string) => `${backend.bffBaseUrl}${path}`,
}));
vi.mock('../src/auth/invite-code', () => ({
  INVITE_CODE_HEADER: 'x-invite-code',
  clearInviteCode: vi.fn(),
  inviteCodeFor: () => null,
}));

import { ApiError } from '../src/types';
import { BlobTooLargeError, bffApiBlob } from '../src/auth/bff-client';

const PATH = '/api/v1/external/matters/m/documents/d/content';
const CAP = 26_214_400;

interface FakeResponse {
  ok: boolean;
  status: number;
  headers: Headers;
  blob: ReturnType<typeof vi.fn>;
  text: ReturnType<typeof vi.fn>;
}

function respond(init: {
  ok?: boolean;
  status?: number;
  headers?: Record<string, string>;
  size?: number;
}): FakeResponse {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    headers: new Headers(init.headers),
    blob: vi.fn(async () => new Blob([new Uint8Array(init.size ?? 4)])),
    text: vi.fn(async () => 'refused'),
  };
}

let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const sentSignal = (): AbortSignal | undefined => (fetchMock.mock.calls[0][1] as RequestInit).signal ?? undefined;

describe('bffApiBlob without limits (every existing caller)', () => {
  it('returns the body and sends the options as given: no signal of its own, no header read', async () => {
    const response = respond({ headers: { 'Content-Length': '999999999999' } });
    fetchMock.mockResolvedValue(response);

    const blob = await bffApiBlob(PATH);

    expect(blob.size).toBe(4);
    expect(sentSignal()).toBeUndefined();
    expect((fetchMock.mock.calls[0][1] as RequestInit).headers).toMatchObject({ Authorization: 'Bearer test-token' });
  });

  it('a non-2xx answer throws ApiError with the body text', async () => {
    fetchMock.mockResolvedValue(respond({ ok: false, status: 403 }));

    const err = await bffApiBlob(PATH).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).statusCode).toBe(403);
    expect((err as ApiError).message).toBe('refused');
  });
});

describe('bffApiBlob with limits', () => {
  it('a Content-Length over the cap aborts the request and the body is never read', async () => {
    const response = respond({ headers: { 'Content-Length': String(CAP + 1) } });
    fetchMock.mockResolvedValue(response);

    await expect(bffApiBlob(PATH, {}, { maxBytes: CAP })).rejects.toBeInstanceOf(BlobTooLargeError);

    expect(response.blob).not.toHaveBeenCalled();
    expect(sentSignal()?.aborted).toBe(true);
  });

  it('with no Content-Length, a body over the cap is read and then discarded: the call rejects, nothing is returned', async () => {
    const response = respond({ size: 64 });
    response.blob.mockResolvedValue({ size: CAP + 1 });
    fetchMock.mockResolvedValue(response);

    await expect(bffApiBlob(PATH, {}, { maxBytes: CAP })).rejects.toBeInstanceOf(BlobTooLargeError);

    expect(response.blob).toHaveBeenCalledTimes(1);
  });

  it('a Content-Length that is not a number does not decide: the real size does', async () => {
    fetchMock.mockResolvedValue(respond({ headers: { 'Content-Length': 'abc' }, size: 8 }));

    expect((await bffApiBlob(PATH, {}, { maxBytes: CAP })).size).toBe(8);
  });

  it('a file of exactly the cap is kept (the cap is inclusive); the request is not aborted', async () => {
    fetchMock.mockResolvedValue(respond({ headers: { 'Content-Length': String(CAP) }, size: 16 }));

    expect((await bffApiBlob(PATH, {}, { maxBytes: CAP })).size).toBe(16);
    expect(sentSignal()?.aborted).toBe(false);
  });

  it('a non-2xx answer is the same ApiError, whatever its Content-Length says', async () => {
    fetchMock.mockResolvedValue(respond({ ok: false, status: 404, headers: { 'Content-Length': String(CAP + 1) } }));

    const err = await bffApiBlob(PATH, {}, { maxBytes: CAP }).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).statusCode).toBe(404);
  });

  describe('a streamed body with no Content-Length (the content route answers chunked)', () => {
    /** A response whose body is a stream of `chunkCount` chunks of `chunkSize` bytes; `pulled` counts chunks handed out. */
    function streamed(chunkSize: number, chunkCount: number) {
      const state = { pulled: 0, cancelled: false };
      const body = new ReadableStream<Uint8Array>({
        pull(ctrl) {
          if (state.pulled >= chunkCount) {
            ctrl.close();
            return;
          }
          state.pulled += 1;
          ctrl.enqueue(new Uint8Array(chunkSize));
        },
        cancel() {
          state.cancelled = true;
        },
      });
      const response = { ...respond({}), body };
      return { response, state };
    }

    it('stops reading as soon as the running total passes the cap: the rest of the file is never pulled', async () => {
      const { response, state } = streamed(4, 1000);
      fetchMock.mockResolvedValue(response);

      await expect(bffApiBlob(PATH, {}, { maxBytes: 10 })).rejects.toBeInstanceOf(BlobTooLargeError);

      expect(state.pulled).toBeLessThan(10);
      expect(state.cancelled).toBe(true);
      expect(sentSignal()?.aborted).toBe(true);
      expect(response.blob).not.toHaveBeenCalled();
    });

    it('a body within the cap is returned whole, and the request is not aborted', async () => {
      const { response } = streamed(4, 3);
      fetchMock.mockResolvedValue(response);

      expect((await bffApiBlob(PATH, {}, { maxBytes: 12 })).size).toBe(12);
      expect(sentSignal()?.aborted).toBe(false);
    });
  });

  it("the caller's own signal aborts the request too, and an already-aborted one aborts it at once", async () => {
    fetchMock.mockResolvedValue(respond({}));
    const caller = new AbortController();
    await bffApiBlob(PATH, { signal: caller.signal }, { maxBytes: CAP });
    expect(sentSignal()?.aborted).toBe(false);

    caller.abort();
    // The listener was removed when the call settled; the request that is over is not touched.
    expect(sentSignal()?.aborted).toBe(false);

    fetchMock.mockClear();
    const inFlight = new AbortController();
    let release: (value: FakeResponse) => void = () => undefined;
    fetchMock.mockReturnValue(new Promise<FakeResponse>(resolve => (release = resolve)));
    const pending = bffApiBlob(PATH, { signal: inFlight.signal }, { maxBytes: CAP });
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    inFlight.abort();
    expect(sentSignal()?.aborted).toBe(true);
    release(respond({}));
    await pending;

    fetchMock.mockClear();
    fetchMock.mockResolvedValue(respond({}));
    await bffApiBlob(PATH, { signal: AbortSignal.abort() }, { maxBytes: CAP });
    expect(sentSignal()?.aborted).toBe(true);
  });
});
