/**
 * SprkChat — one-shot host→send seam tests
 * (spaarkeai-assistant-enhancements-r2 task 042c-fr-c4 / FR-C4).
 *
 * Verifies the additive `pendingOutboundMessage` / `onOutboundConsumed` prop
 * pair (mirrors the existing `injectLocalMessage` / `onLocalMessageInjected`
 * opt-in pattern):
 *   - arming `pendingOutboundMessage` invokes SprkChat's OWN `handleSend`
 *     exactly ONCE (a real POST /messages turn) and acks via
 *     `onOutboundConsumed`;
 *   - re-arming (after the host clears the slot) sends again;
 *   - never passing the prop ⇒ ZERO sends (existing consumers unaffected).
 *
 * @see FR-C4 — deterministic host-driven send seam (no SprkChat fork; reuses
 *      the existing send path so onDecorateOutboundBody still runs)
 * @see ADR-012 (shared-lib wiring) / ADR-028 (authenticatedFetch)
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
import { waitFor, act } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SprkChat } from '../SprkChat';
import type { ISprkChatProps } from '../types';

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

const mockAuthenticatedFetch = (url: string, init?: RequestInit) =>
  mockFetch(url, {
    ...init,
    headers: { ...(init?.headers ?? {}), Authorization: 'Bearer test-access-token' },
  });
const mockGetAccessToken = () => Promise.resolve('test-access-token');

const apiProps = {
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

function messagesCalls(): unknown[][] {
  return mockFetch.mock.calls.filter(
    ([url]) => typeof url === 'string' && url.includes('/sessions/session-hostsend-1/messages')
  );
}

/**
 * Host harness that owns the one-slot `pendingOutboundMessage` state and clears
 * it on `onOutboundConsumed` — the exact contract a real host (ConversationPane)
 * implements. The test drives `arm` to set the slot.
 */
let armSlot: (value: string | null) => void = () => undefined;
function Harness(props: Partial<ISprkChatProps>): React.JSX.Element {
  const [pending, setPending] = React.useState<string | null>(null);
  armSlot = setPending;
  return (
    <SprkChat {...apiProps} {...props} pendingOutboundMessage={pending} onOutboundConsumed={() => setPending(null)} />
  );
}

describe('SprkChat — one-shot host→send seam (task 042c-fr-c4 / FR-C4)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    armSlot = () => undefined;
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      if (typeof url === 'string' && url.includes('/sessions/') && url.endsWith('/messages')) {
        return Promise.resolve(emptySseResponse());
      }
      if (typeof url === 'string' && url.endsWith('/sessions') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse({ sessionId: 'session-hostsend-1', createdAt: '2026-08-06T00:00:00Z' }));
      }
      return Promise.resolve(jsonResponse({}));
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  async function mountAndAwaitSession(props: Partial<ISprkChatProps> = {}): Promise<void> {
    await act(async () => {
      renderWithProviders(<Harness {...props} />);
    });
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' })
      );
    });
  }

  it('arming pendingOutboundMessage sends exactly ONCE and acks (slot cleared)', async () => {
    const onOutboundConsumedSpy = jest.fn();
    // Wire a spy INTO the harness's own ack so we can assert it fired, while the
    // harness still clears its slot.
    await mountAndAwaitSession({
      onDecorateOutboundBody: b => {
        onOutboundConsumedSpy();
        return b;
      },
    });

    expect(messagesCalls()).toHaveLength(0);

    await act(async () => {
      armSlot('Summarize this email');
    });

    await waitFor(() => expect(messagesCalls()).toHaveLength(1));

    const body = JSON.parse((messagesCalls()[0][1] as RequestInit).body as string);
    expect(body.message).toBe('Summarize this email');

    // A few extra render/microtask ticks must NOT produce a second send — the
    // slot was consumed + cleared by the ack (onOutboundConsumed → setPending(null)).
    await act(async () => {
      await new Promise(r => setTimeout(r, 10));
    });
    expect(messagesCalls()).toHaveLength(1);
  });

  it('re-arming after the ack sends AGAIN (one send per arm)', async () => {
    await mountAndAwaitSession();

    await act(async () => {
      armSlot('Summarize this email');
    });
    await waitFor(() => expect(messagesCalls()).toHaveLength(1));

    // Let the first stream settle (isStreaming returns false), then re-arm with
    // the SAME string — the null-reset between arms means it sends again.
    await act(async () => {
      await new Promise(r => setTimeout(r, 10));
    });

    await act(async () => {
      armSlot('Summarize this email');
    });
    await waitFor(() => expect(messagesCalls()).toHaveLength(2));
  });

  it('never passing pendingOutboundMessage produces ZERO host-driven sends', async () => {
    // Render bare SprkChat WITHOUT the prop pair — existing-consumer parity.
    await act(async () => {
      renderWithProviders(<SprkChat {...apiProps} />);
    });
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.example.com/api/ai/chat/sessions',
        expect.objectContaining({ method: 'POST' })
      );
    });

    await act(async () => {
      await new Promise(r => setTimeout(r, 10));
    });
    expect(messagesCalls()).toHaveLength(0);
  });
});
