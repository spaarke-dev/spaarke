/**
 * SprkChat Attachment Payload Tests (Task 026 — FR-07)
 *
 * Verifies the SprkChat handleSend handler correctly wires `chatAttachments`
 * (from `useChatFileAttachment`) into the outbound POST body sent to
 * `/api/ai/chat/sessions/{sessionId}/messages`. The backend DTO contract
 * (camelCase: attachments[], filename, contentType, textContent) is per
 * spike 001 + Phase E task 050 (see
 * `projects/spaarke-ai-platform-unification-r3/notes/spikes/001-fr07-attachments-payload.md`).
 *
 * @see FR-07 (chat-message attachments)
 * @see ADR-012 (shared component wiring lives in shared lib)
 * @see ADR-028 (auth via authenticatedFetch / getAccessToken — no token snapshots)
 */

// Task 077: Polyfill TextDecoder / TextEncoder in jsdom global scope BEFORE
// any module imports. The production `useSseStream` hook constructs
// `new TextDecoder()` to decode the SSE stream bytes (production-correct under
// browsers, but jsdom v30 omits both `TextDecoder` and `TextEncoder` from the
// test global — verified via `typeof TextDecoder === 'undefined'`). Without
// this polyfill, the SSE reader path throws "TextDecoder is not defined" and
// the `done` event never reaches the streamDone effect → clearAttachments.
//
// We use Node's `util` module (always available under jest-environment-jsdom
// running on Node) rather than installing a polyfill dependency. This is the
// canonical pattern from the jsdom maintainers' README.
//
// Reusable pattern for future SprkChat tests touching SSE / streaming: place
// these two assignments at the top of the test file, before any production
// imports. (Putting them in jest.setup.js would be cleaner, but the R4 task
// 077 scope is test-file-only.)
import { TextDecoder as NodeTextDecoder, TextEncoder as NodeTextEncoder } from 'util';
if (typeof (globalThis as { TextDecoder?: unknown }).TextDecoder === 'undefined') {
  (globalThis as { TextDecoder: unknown }).TextDecoder = NodeTextDecoder;
}
if (typeof (globalThis as { TextEncoder?: unknown }).TextEncoder === 'undefined') {
  (globalThis as { TextEncoder: unknown }).TextEncoder = NodeTextEncoder;
}

import * as React from 'react';
import { screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type {
  AttachmentChip,
  ChatAttachment,
  IUseChatFileAttachmentResult,
} from '../hooks/useChatFileAttachment';

// ---------------------------------------------------------------------------
// Mock the attachment hook so we can inject deterministic attachments
// without driving the real File → extraction pipeline (PDF.js / mammoth).
// ---------------------------------------------------------------------------

let mockHookResult: IUseChatFileAttachmentResult;
const mockClearAll = jest.fn();
const mockAddFiles = jest.fn().mockResolvedValue(undefined);
const mockRemoveFile = jest.fn();

jest.mock('../hooks/useChatFileAttachment', () => {
  const actual = jest.requireActual('../hooks/useChatFileAttachment');
  return {
    ...actual,
    useChatFileAttachment: () => mockHookResult,
  };
});

// Import after mock is registered.
// eslint-disable-next-line import/first
import { SprkChat } from '../SprkChat';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

const mockAuthenticatedFetch = (url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: {
      ...(init?.headers ?? {}),
      Authorization: 'Bearer test-access-token',
    },
  });
const mockGetAccessToken = () => Promise.resolve('test-access-token');

const defaultProps = {
  playbookId: 'test-playbook-id',
  apiBaseUrl: 'https://api.example.com',
  authenticatedFetch: mockAuthenticatedFetch,
  getAccessToken: mockGetAccessToken,
};

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

/**
 * Build a Response whose body is an SSE "stream" that immediately closes,
 * matching what useSseStream expects from POST /messages.
 *
 * Task 077: previously used `new TextEncoder().encode(...)` inside
 * `new ReadableStream({ start(controller) { ... } })`, but BOTH `TextEncoder`
 * AND `ReadableStream` are NOT defined in the jest-environment-jsdom global
 * scope (verified via `typeof TextEncoder === 'undefined'` and
 * `typeof ReadableStream === 'undefined'`). The constructor error was being
 * caught by useSseStream's fetchStream() outer try/catch and rendered as a
 * chat error banner ("ReadableStream is not defined"), so the `done` event
 * never reached processEvent → onDone → setIsDone(true) → streamDone effect
 * → clearAttachments.
 *
 * Why the boundary tests still passed pre-077: they only assert on the
 * outbound POST body via `mockFetch.mock.calls` (recorded BEFORE the
 * ReadableStream is consumed). They never await the streaming completion.
 * Only the `clearAll on successful stream completion` test exercises the
 * streamDone effect.
 *
 * Fix (per task 077 POML step 4 — "mock the SSE reader output directly"):
 * Bypass jsdom's missing `ReadableStream` entirely by hand-rolling a minimal
 * reader that satisfies the `response.body.getReader()` contract used by
 * useSseStream (`{ read(): Promise<{ done, value }> }`). We yield the
 * canonical `done` event on the first `read()` call and `{ done: true }`
 * on the second. `Uint8Array` IS defined in jsdom, so we use it directly.
 *
 * Reusable pattern for future SprkChat SSE tests: prefer this hand-rolled
 * reader over `new ReadableStream(...)`. jsdom v30 lacks both
 * `ReadableStream` and `TextEncoder` in the test global. This approach is
 * also more deterministic — the reader resolves synchronously with each
 * `await reader.read()` tick, so the SSE → streamDone → clearAttachments
 * cascade completes in O(microtasks) rather than depending on jsdom's
 * scheduling of real-stream events.
 */
function emptySseResponse(): Response {
  // Task 071: parseSseEvent only consumes `data:` lines that contain a JSON
  // payload with a `type` field. The previous `event: done\ndata: {}\n\n`
  // form was silently skipped (no `type` → null), so streamDone never flipped
  // to true and clearAll was never invoked. Use the canonical SSE format.
  const sseBytes = Uint8Array.from(
    Buffer.from('data: {"type":"done","content":null}\n\n', 'utf-8'),
  );

  let readCount = 0;
  const reader = {
    read: jest.fn(() => {
      readCount += 1;
      if (readCount === 1) {
        return Promise.resolve({ done: false, value: sseBytes });
      }
      return Promise.resolve({ done: true, value: undefined });
    }),
    cancel: jest.fn().mockResolvedValue(undefined),
    releaseLock: jest.fn(),
  };

  return {
    ok: true,
    status: 200,
    body: {
      getReader: () => reader,
    },
    text: jest.fn().mockResolvedValue(''),
    headers: new Headers({ 'content-type': 'text/event-stream' }),
  } as unknown as Response;
}

function makeReadyChip(
  id: string,
  filename: string,
  mimeType: string,
  textContent: string,
): AttachmentChip {
  return {
    id,
    filename,
    sizeBytes: textContent.length,
    mimeType,
    status: 'ready',
    textContent,
  };
}

function setHookResult(attachments: ChatAttachment[], chips: AttachmentChip[]): void {
  mockHookResult = {
    files: chips,
    attachments,
    errors: [],
    addFiles: mockAddFiles,
    removeFile: mockRemoveFile,
    clearAll: mockClearAll,
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SprkChat — attachments payload wiring (task 026, FR-07)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setHookResult([], []);

    // Default fetch behaviour:
    //  - POST /sessions                            → 200 with sessionId
    //  - POST /sessions/{id}/messages              → empty SSE stream
    //  - All other GETs (playbooks, slash, etc.)   → 200 with empty payloads
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/sessions/') && url.endsWith('/messages')) {
        return Promise.resolve(emptySseResponse());
      }
      if (typeof url === 'string' && url.endsWith('/sessions') && init?.method === 'POST') {
        return Promise.resolve(
          jsonResponse({ sessionId: 'session-attach-1', createdAt: '2026-05-20T00:00:00Z' }),
        );
      }
      // Generic fallback for discovery / context-mapping / dynamic-slash GETs.
      return Promise.resolve(jsonResponse({}));
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('omits the attachments field when no chips are present', async () => {
    setHookResult([], []);

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    // Wait for mount-time session POST to settle.
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // Task 071: Fluent v9 Textarea forwards data-testid to either the wrapper
    // div OR (in some configurations) the textarea itself. Fall back to the
    // matched element if `.querySelector('textarea')` returns null.
    const textareaContainer = screen.getByTestId('chat-input-textarea') as HTMLElement;
    const textarea = (textareaContainer.querySelector('textarea') ?? textareaContainer) as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'hello with no attachments');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    await waitFor(() => {
      const messagesCall = mockFetch.mock.calls.find(
        ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages'),
      );
      expect(messagesCall).toBeDefined();
    });

    const messagesCall = mockFetch.mock.calls.find(
      ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages'),
    )!;
    const body = JSON.parse((messagesCall[1] as RequestInit).body as string);

    expect(body).toEqual({ message: 'hello with no attachments', documentId: undefined });
    expect(body.attachments).toBeUndefined();
  });

  it('includes attachments[] with camelCase fields when chips are ready', async () => {
    const chips: AttachmentChip[] = [
      makeReadyChip('a', 'sky.txt', 'text/plain', 'the sky is blue'),
      makeReadyChip('b', 'fish.txt', 'text/plain', 'fish are gold'),
    ];
    const attachments: ChatAttachment[] = chips.map(c => ({
      filename: c.filename,
      contentType: c.mimeType,
      textContent: c.textContent ?? '',
    }));
    setHookResult(attachments, chips);

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // Task 071: Fluent v9 Textarea forwards data-testid to either the wrapper
    // div OR (in some configurations) the textarea itself. Fall back to the
    // matched element if `.querySelector('textarea')` returns null.
    const textareaContainer = screen.getByTestId('chat-input-textarea') as HTMLElement;
    const textarea = (textareaContainer.querySelector('textarea') ?? textareaContainer) as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'summarize attached files');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    await waitFor(() => {
      const messagesCall = mockFetch.mock.calls.find(
        ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages'),
      );
      expect(messagesCall).toBeDefined();
    });

    const messagesCall = mockFetch.mock.calls.find(
      ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages'),
    )!;
    const body = JSON.parse((messagesCall[1] as RequestInit).body as string);

    expect(body.message).toBe('summarize attached files');
    expect(body.attachments).toEqual([
      { filename: 'sky.txt', contentType: 'text/plain', textContent: 'the sky is blue' },
      { filename: 'fish.txt', contentType: 'text/plain', textContent: 'fish are gold' },
    ]);
    // OC-02: NO Dataverse Document entity creation — attachments are in-memory only.
    // Verify the payload path didn't trigger a separate /documents POST.
    const documentsPosts = mockFetch.mock.calls.filter(
      ([url, init]) =>
        typeof url === 'string' &&
        url.includes('/documents') &&
        (init as RequestInit | undefined)?.method === 'POST',
    );
    expect(documentsPosts).toHaveLength(0);
  });

  it('calls clearAll on successful stream completion', async () => {
    const chips: AttachmentChip[] = [
      makeReadyChip('a', 'note.txt', 'text/plain', 'content'),
    ];
    setHookResult(
      [{ filename: 'note.txt', contentType: 'text/plain', textContent: 'content' }],
      chips,
    );

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' }),
      );
    });

    // Task 071: Fluent v9 Textarea forwards data-testid to either the wrapper
    // div OR (in some configurations) the textarea itself. Fall back to the
    // matched element if `.querySelector('textarea')` returns null.
    const textareaContainer = screen.getByTestId('chat-input-textarea') as HTMLElement;
    const textarea = (textareaContainer.querySelector('textarea') ?? textareaContainer) as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'send with attachment');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    // Task 077: SSE stream reader is microtask-async; flush microtasks so the
    // `done` event lands and streamDone flips to true before assertion.
    // The real root cause of the prior failure was the missing TextDecoder /
    // TextEncoder polyfills + the unmockable `new ReadableStream(...)` in jsdom
    // v30. With both addressed (polyfill at top of file + hand-rolled reader
    // in `emptySseResponse()`), the cascade reader.read() → setIsDone(true) →
    // streamDone effect → clearAttachments completes within a few microtask
    // ticks. The 10 ms flush below is conservative — empirically it completes
    // in well under that.
    await act(async () => {
      await new Promise(r => setTimeout(r, 10));
    });

    // The mocked SSE stream emits a single `data: {"type":"done"...}` and closes,
    // which flips streamDone to true → the streamDone effect calls clearAll.
    await waitFor(
      () => {
        expect(mockClearAll).toHaveBeenCalled();
      },
      { timeout: 3000 },
    );
  });
});
