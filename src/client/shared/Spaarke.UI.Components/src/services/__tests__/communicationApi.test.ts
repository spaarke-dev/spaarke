/**
 * communicationApi.test.ts (task 023, W2)
 *
 * Behavior contract for the thin `sendCommunication()` client wrapper and its
 * typed `SendCommunicationError` (ADR-019 ProblemDetails parsing):
 *   - fromResponse parses RFC 7807 ProblemDetails (status/code/detail/correlationId)
 *   - fromResponse falls back cleanly on non-JSON / unparseable bodies (never throws)
 *   - fromResponse carries the BFF-authoritative canonical codes onto error.code
 *     (FROM_REQUIRED / FROM_NOT_APPROVED / ATTACHMENT_BLOCKED_TYPE)
 *   - sendCommunication throws SendCommunicationError on any non-2xx
 *   - argument guards throw before any network call
 *   - attachmentDocumentIds pass through unchanged (R4 W0: they carry
 *     sprk_document GUIDs; the wrapper does NO translation and NO rename)
 *
 * The mocked boundary is `authenticatedFetch` (the injected network seam) — the
 * banned `Mock<HttpMessageHandler>`-equivalent (a transport/wire-format mock) is
 * NOT used; the wrapper's own public contract is exercised.
 *
 * DOCUMENTED GAP: the POML also asked for a "one-shot deprecation warn (fires
 * once across multiple attachmentDocumentIds calls)". No such warning exists in
 * communicationApi.ts — the R4 W0 owner decision (2026-07-14) retracted the
 * `attachmentDocumentIds` rename premise, so task 022 never added a deprecation
 * path. Testing a non-existent warning would be fiction; instead this suite
 * asserts the field's correct pass-through semantics. See notes/023-*.md.
 */
import { sendCommunication, SendCommunicationError, type SendCommunicationOptions } from '../communicationApi';

// ---------------------------------------------------------------------------
// Fake Response builder (no wire/transport mock — a plain Response-shaped
// double supplying only the members fromResponse/sendCommunication read).
// ---------------------------------------------------------------------------

interface FakeResponseSpec {
  status: number;
  contentType?: string;
  jsonBody?: unknown;
  textBody?: string;
  jsonThrows?: boolean;
}

function fakeResponse(spec: FakeResponseSpec): Response {
  const { status, contentType, jsonBody, textBody, jsonThrows } = spec;
  return {
    status,
    ok: status >= 200 && status < 300,
    headers: { get: (name: string) => (name.toLowerCase() === 'content-type' ? (contentType ?? null) : null) },
    json: async () => {
      if (jsonThrows) throw new SyntaxError('Unexpected token');
      return jsonBody;
    },
    text: async () => textBody ?? '',
  } as unknown as Response;
}

function validOptions(overrides: Partial<SendCommunicationOptions> = {}): SendCommunicationOptions {
  return { to: ['a@example.com'], subject: 'Subject', body: 'Body', ...overrides };
}

// ---------------------------------------------------------------------------
// SendCommunicationError.fromResponse
// ---------------------------------------------------------------------------

describe('SendCommunicationError.fromResponse — ProblemDetails parsing', () => {
  it('parses errorCode / detail / correlationId from a problem+json body', async () => {
    const res = fakeResponse({
      status: 422,
      contentType: 'application/problem+json',
      jsonBody: { status: 422, errorCode: 'VALIDATION_FAILED', detail: 'Bad recipients', correlationId: 'corr-1' },
    });
    const err = await SendCommunicationError.fromResponse(res);

    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err.status).toBe(422);
    expect(err.code).toBe('VALIDATION_FAILED');
    expect(err.detail).toBe('Bad recipients');
    expect(err.correlationId).toBe('corr-1');
  });

  it('falls back code→body.code, detail→title, correlationId→traceId', async () => {
    const res = fakeResponse({
      status: 500,
      contentType: 'application/json',
      jsonBody: { status: 500, code: 'INTERNAL', title: 'Server error', traceId: 'trace-9' },
    });
    const err = await SendCommunicationError.fromResponse(res);
    expect(err.code).toBe('INTERNAL');
    expect(err.detail).toBe('Server error');
    expect(err.correlationId).toBe('trace-9');
  });

  it('uses HTTP_<status> code + instance correlation when errorCode/code absent', async () => {
    const res = fakeResponse({
      status: 403,
      contentType: 'application/json',
      jsonBody: { status: 403, detail: 'Forbidden', instance: '/req/42' },
    });
    const err = await SendCommunicationError.fromResponse(res);
    expect(err.code).toBe('HTTP_403');
    expect(err.detail).toBe('Forbidden');
    expect(err.correlationId).toBe('/req/42');
  });

  it('falls back to the raw text body on a non-JSON response (never throws)', async () => {
    const res = fakeResponse({ status: 502, contentType: 'text/plain', textBody: 'Bad Gateway' });
    const err = await SendCommunicationError.fromResponse(res);
    expect(err.status).toBe(502);
    expect(err.code).toBe('HTTP_502');
    expect(err.detail).toBe('Bad Gateway');
  });

  it('falls back to a minimal error when JSON parsing throws', async () => {
    const res = fakeResponse({ status: 400, contentType: 'application/json', jsonThrows: true });
    const err = await SendCommunicationError.fromResponse(res);
    expect(err.status).toBe(400);
    expect(err.code).toBe('HTTP_400');
    expect(err.detail).toBe('HTTP 400');
  });

  it('carries the BFF-authoritative canonical codes onto error.code', async () => {
    for (const code of ['FROM_REQUIRED', 'FROM_NOT_APPROVED', 'ATTACHMENT_BLOCKED_TYPE'] as const) {
      const res = fakeResponse({
        status: 422,
        contentType: 'application/problem+json',
        jsonBody: { status: 422, errorCode: code, detail: `${code} detail` },
      });
      const err = await SendCommunicationError.fromResponse(res);
      expect(err.code).toBe(code);
    }
  });

  it('is an Error subclass with a preserved prototype chain (instanceof works)', () => {
    const err = new SendCommunicationError(400, 'X', 'msg');
    expect(err).toBeInstanceOf(Error);
    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err.message).toContain('400');
  });
});

// ---------------------------------------------------------------------------
// sendCommunication
// ---------------------------------------------------------------------------

describe('sendCommunication — success path', () => {
  it('POSTs to /api/communications/send and returns the communicationId', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue(
        fakeResponse({ status: 200, contentType: 'application/json', jsonBody: { communicationId: 'comm-1' } })
      );

    const result = await sendCommunication(validOptions(), { authenticatedFetch });

    expect(result).toEqual({ communicationId: 'comm-1' });
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(url).toBe('/api/communications/send');
    expect(init.method).toBe('POST');
  });

  it('prefixes the request URL with bffBaseUrl (trailing slash trimmed)', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue(
        fakeResponse({ status: 200, contentType: 'application/json', jsonBody: { communicationId: 'c' } })
      );

    await sendCommunication(validOptions(), { authenticatedFetch, bffBaseUrl: 'https://bff.example.com/' });
    expect(authenticatedFetch.mock.calls[0][0]).toBe('https://bff.example.com/api/communications/send');
  });

  it('passes attachmentDocumentIds through unchanged (sprk_document GUIDs — no translation, no rename)', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue(
        fakeResponse({ status: 200, contentType: 'application/json', jsonBody: { communicationId: 'c' } })
      );
    const ids = ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'];

    await sendCommunication(validOptions({ attachmentDocumentIds: ids }), { authenticatedFetch });

    const body = JSON.parse(authenticatedFetch.mock.calls[0][1].body);
    expect(body.attachmentDocumentIds).toEqual(ids);
  });

  it('maps friendly send-mode/body-format strings to the BFF enum spellings', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue(
        fakeResponse({ status: 200, contentType: 'application/json', jsonBody: { communicationId: 'c' } })
      );

    await sendCommunication(validOptions({ sendMode: 'user', bodyFormat: 'text' }), { authenticatedFetch });
    const body = JSON.parse(authenticatedFetch.mock.calls[0][1].body);
    expect(body.sendMode).toBe('User');
    expect(body.bodyFormat).toBe('PlainText');
  });
});

describe('sendCommunication — error path', () => {
  it('throws SendCommunicationError on a non-2xx response', async () => {
    const authenticatedFetch = jest.fn().mockResolvedValue(
      fakeResponse({
        status: 422,
        contentType: 'application/problem+json',
        jsonBody: { status: 422, errorCode: 'FROM_NOT_APPROVED', detail: 'Sender not approved' },
      })
    );

    await expect(sendCommunication(validOptions(), { authenticatedFetch })).rejects.toMatchObject({
      name: 'SendCommunicationError',
      status: 422,
      code: 'FROM_NOT_APPROVED',
    });
  });

  it('throws when the success body lacks a communicationId', async () => {
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue(fakeResponse({ status: 200, contentType: 'application/json', jsonBody: {} }));
    await expect(sendCommunication(validOptions(), { authenticatedFetch })).rejects.toThrow(
      /did not include communicationId/
    );
  });
});

describe('sendCommunication — argument guards (throw before any network call)', () => {
  it('requires at least one recipient', async () => {
    const authenticatedFetch = jest.fn();
    await expect(sendCommunication(validOptions({ to: [] }), { authenticatedFetch })).rejects.toThrow(
      /at least one recipient/
    );
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('requires a non-empty subject', async () => {
    const authenticatedFetch = jest.fn();
    await expect(sendCommunication(validOptions({ subject: '  ' }), { authenticatedFetch })).rejects.toThrow(
      /subject is required/
    );
    expect(authenticatedFetch).not.toHaveBeenCalled();
  });

  it('requires authenticatedFetch to be provided', async () => {
    await expect(
      sendCommunication(validOptions(), { authenticatedFetch: undefined as unknown as jest.Mock })
    ).rejects.toThrow(/authenticatedFetch is required/);
  });
});
