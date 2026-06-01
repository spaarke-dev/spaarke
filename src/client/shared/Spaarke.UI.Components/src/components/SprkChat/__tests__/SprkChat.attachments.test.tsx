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

import * as React from 'react';
import { screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { AttachmentChip, ChatAttachment, IUseChatFileAttachmentResult } from '../hooks/useChatFileAttachment';

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
 * Build a Response whose body is an SSE stream that immediately closes,
 * matching what useSseStream expects from POST /messages.
 */
function emptySseResponse(): Response {
  const encoder = new TextEncoder();
  const stream = new ReadableStream({
    start(controller) {
      controller.enqueue(encoder.encode('event: done\ndata: {}\n\n'));
      controller.close();
    },
  });
  return {
    ok: true,
    status: 200,
    body: stream,
    text: jest.fn().mockResolvedValue(''),
    headers: new Headers({ 'content-type': 'text/event-stream' }),
  } as unknown as Response;
}

function makeReadyChip(id: string, filename: string, mimeType: string, textContent: string): AttachmentChip {
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
        return Promise.resolve(jsonResponse({ sessionId: 'session-attach-1', createdAt: '2026-05-20T00:00:00Z' }));
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
        expect.objectContaining({ method: 'POST' })
      );
    });

    const textarea = screen.getByTestId('chat-input-textarea').querySelector('textarea') as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'hello with no attachments');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    await waitFor(() => {
      const messagesCall = mockFetch.mock.calls.find(
        ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages')
      );
      expect(messagesCall).toBeDefined();
    });

    const messagesCall = mockFetch.mock.calls.find(
      ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages')
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
        expect.objectContaining({ method: 'POST' })
      );
    });

    const textarea = screen.getByTestId('chat-input-textarea').querySelector('textarea') as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'summarize attached files');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    await waitFor(() => {
      const messagesCall = mockFetch.mock.calls.find(
        ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages')
      );
      expect(messagesCall).toBeDefined();
    });

    const messagesCall = mockFetch.mock.calls.find(
      ([url]) => typeof url === 'string' && url.includes('/sessions/session-attach-1/messages')
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
        typeof url === 'string' && url.includes('/documents') && (init as RequestInit | undefined)?.method === 'POST'
    );
    expect(documentsPosts).toHaveLength(0);
  });

  it('calls clearAll on successful stream completion', async () => {
    const chips: AttachmentChip[] = [makeReadyChip('a', 'note.txt', 'text/plain', 'content')];
    setHookResult([{ filename: 'note.txt', contentType: 'text/plain', textContent: 'content' }], chips);

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' })
      );
    });

    const textarea = screen.getByTestId('chat-input-textarea').querySelector('textarea') as HTMLTextAreaElement;
    const sendButton = screen.getByTestId('chat-send-button');

    await act(async () => {
      await userEvent.type(textarea, 'send with attachment');
    });
    await act(async () => {
      await userEvent.click(sendButton);
    });

    // The mocked SSE stream emits a single `event: done` and closes, which
    // flips streamDone to true → the streamDone effect calls clearAll.
    await waitFor(() => {
      expect(mockClearAll).toHaveBeenCalled();
    });
  });
});
