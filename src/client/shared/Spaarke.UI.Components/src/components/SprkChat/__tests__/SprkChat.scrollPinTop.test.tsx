/**
 * SprkChat — "pin new user message to top" scroll behavior (spaarkeai-assistant-
 * enhancements-r2 Phase 0 FIX 1, owner request).
 *
 * Desired: when the user sends a message, the message list scrolls so THAT user
 * message sits at the TOP of the viewport (ChatGPT/Claude pattern) instead of the
 * legacy scroll-to-bottom — so the full streaming response is readable without the
 * user scrolling.
 *
 * jsdom does not implement layout (`offsetTop`/`scrollHeight`/`clientHeight` are
 * always 0), so this test stubs `HTMLElement.prototype.offsetTop` to a fixed,
 * non-zero value (mirrors the `stubOverflow` pattern in
 * `RecordHeader/__tests__/fields.test.tsx`) and asserts the WIRING: after send, the
 * message-list container's `scrollTop` is set to the pinned user message's
 * `offsetTop` (both resolve to the same stubbed value since every element shares
 * the same prototype getter in this test — the assertion is on the wiring, not on
 * real pixel geometry, which jsdom cannot provide).
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 (dark mode compliance)
 */

// jsdom v30 omits TextDecoder/TextEncoder — the SSE reader path needs them.
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
import { SprkChat } from '../SprkChat';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

const mockAuthenticatedFetch = (url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: { ...(init?.headers ?? {}), Authorization: 'Bearer test-access-token' },
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

/** A minimal SSE response that emits a single `done` event and closes. */
function emptySseResponse(): Response {
  const sseBytes = Uint8Array.from(Buffer.from('data: {"type":"done","content":null}\n\n', 'utf-8'));
  let readCount = 0;
  const reader = {
    read: jest.fn(() => {
      readCount += 1;
      return readCount === 1
        ? Promise.resolve({ done: false, value: sseBytes })
        : Promise.resolve({ done: true, value: undefined });
    }),
    cancel: jest.fn().mockResolvedValue(undefined),
    releaseLock: jest.fn(),
  };
  return {
    ok: true,
    status: 200,
    body: { getReader: () => reader },
    text: jest.fn().mockResolvedValue(''),
    headers: new Headers({ 'content-type': 'text/event-stream' }),
  } as unknown as Response;
}

/**
 * Stub `offsetTop` at the `HTMLElement.prototype` level to a fixed non-zero value
 * — jsdom has no layout engine so every element's real `offsetTop` is 0. Mirrors
 * `RecordHeader/__tests__/fields.test.tsx`'s `stubOverflow` pattern.
 */
function stubOffsetTop(value: number): () => void {
  const orig = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetTop');
  Object.defineProperty(HTMLElement.prototype, 'offsetTop', {
    configurable: true,
    get() {
      return value;
    },
  });
  return () => {
    if (orig) Object.defineProperty(HTMLElement.prototype, 'offsetTop', orig);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).offsetTop;
  };
}

describe('SprkChat — pin new user message to top on send (Phase 0 FIX 1)', () => {
  let restoreOffsetTop: () => void;

  beforeEach(() => {
    jest.clearAllMocks();
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/sessions/') && url.endsWith('/messages')) {
        return Promise.resolve(emptySseResponse());
      }
      if (typeof url === 'string' && url.endsWith('/sessions') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse({ sessionId: 'session-scrolltop-1', createdAt: '2026-08-07T00:00:00Z' }));
      }
      return Promise.resolve(jsonResponse({}));
    });
    restoreOffsetTop = stubOffsetTop(555);
  });

  afterEach(() => {
    restoreOffsetTop();
    jest.restoreAllMocks();
  });

  it('sets the message-list scrollTop to the new user message offsetTop (pin-to-top, not scroll-to-bottom)', async () => {
    const user = userEvent.setup();

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      const textarea = screen.getByTestId('chat-input-textarea');
      const nativeTextarea = textarea.querySelector('textarea') || textarea;
      expect(nativeTextarea).not.toBeDisabled();
    });

    const messageList = screen.getByTestId('chat-message-list') as HTMLDivElement;
    // Sanity: nothing has pinned yet (no send has happened).
    expect(messageList.scrollTop).toBe(0);

    const textarea = screen.getByTestId('chat-input-textarea');
    const nativeTextarea = (textarea.querySelector('textarea') || textarea) as HTMLTextAreaElement;
    await user.type(nativeTextarea, 'Summarize this document');
    await user.click(screen.getByTestId('chat-send-button'));

    // The pin-to-top effect runs once the user + placeholder-assistant messages
    // render (React effect after the `messages` state update) — assert it lands
    // on the stubbed offsetTop value rather than scrollHeight (the old
    // scroll-to-bottom behavior, which jsdom always reports as 0 here too, so a
    // false pass on the old behavior is NOT possible via that avenue — the
    // meaningful signal is that scrollTop becomes exactly the stubbed offsetTop).
    await waitFor(() => {
      expect(messageList.scrollTop).toBe(555);
    });

    // The user's message text is actually in the transcript (not just the composer).
    expect(screen.getByText('Summarize this document')).toBeInTheDocument();
  });

  it('does not pin-to-top when no send has occurred (initial mount stays at scrollTop 0)', async () => {
    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      const textarea = screen.getByTestId('chat-input-textarea');
      const nativeTextarea = textarea.querySelector('textarea') || textarea;
      expect(nativeTextarea).not.toBeDisabled();
    });

    const messageList = screen.getByTestId('chat-message-list') as HTMLDivElement;
    expect(messageList.scrollTop).toBe(0);
  });
});
