/**
 * SprkChat - action_outcome Integration Tests (spaarke-ai-architecture-redesign-r2 task 044c)
 *
 * THE GAP (G-R2-A client-finisher, verified 2026-07-09): the server has always emitted an
 * `action_outcome` ChatSseEvent on the AUTO-EXECUTE (no-dialog) gate leg (task 044,
 * SideEffectGateAIFunction.cs ~line 490) — carrying the Completion Engine's OutcomeCard — but no
 * SpaarkeAi/shared client consumed it. A clear low-risk create (e.g. "create a follow-up task…")
 * executed with no dialog, but the ✅ OutcomeCard + record chip + Undo chip never rendered — only
 * grounded text. This fails G-R2-A UAT scenario S1.
 *
 * THE FIX under test: `action_outcome` is routed to the SAME `outcome_card` render path the
 * gate-RESUME leg already uses (SprkChat.tsx `handleActionConfirm` ~line 761 / OutcomeCard.tsx) —
 * no new card component, no second render path.
 *
 * This is a full end-to-end render test: real fetch mock -> real SSE stream -> real
 * `useSseStream` consumer -> real `SprkChat` component tree -> real `OutcomeCard` render. It
 * asserts on the DOM produced by the actual `<OutcomeCard>` component (`data-testid`s baked into
 * OutcomeCard.tsx), not a hand-rolled substitute — so it FAILS if the `action_outcome` branch is
 * ever removed from the dispatcher (hooks/useSseStream.ts `processEvent`), the SprkChat switch
 * case is removed, or `actionOutcomeDataToCard` stops mapping the wire shape correctly.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import { screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SprkChat } from '../SprkChat';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream)
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

/**
 * Async-delivering ReadableStream polyfill — same as citationsIntegration.test.tsx. The async
 * delay between chunks matters: React batches state updates, and SprkChat's effects only react
 * once a render cycle observes the intermediate state.
 */
if (typeof globalThis.ReadableStream === 'undefined') {
  (globalThis as any).ReadableStream = class ReadableStream {
    private _source: any;
    constructor(source: any) {
      this._source = source;
    }
    getReader() {
      const chunks: Uint8Array[] = [];
      const controller = {
        enqueue: (chunk: Uint8Array) => chunks.push(chunk),
        close: () => {},
      };
      this._source.start(controller);
      let index = 0;
      return {
        read: async () => {
          if (index < chunks.length) {
            if (index > 0) {
              await new Promise(r => setTimeout(r, 5));
            }
            return { done: false, value: chunks[index++] };
          }
          return { done: true, value: undefined };
        },
        cancel: async () => {},
      };
    }
  };
}

// ---------------------------------------------------------------------------
// Mock fetch
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

function createFetchResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function createSseStreamResponse(events: Array<Record<string, unknown>>, status = 200): Response {
  const encoder = new TextEncoder();

  const tokenEvents = events.filter(e => e.type === 'token');
  const otherEvents = events.filter(e => e.type !== 'token');

  const tokenChunk = encoder.encode(tokenEvents.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join(''));
  const otherChunk = encoder.encode(otherEvents.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join(''));

  const fullText = events.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join('');

  const stream = new (globalThis as any).ReadableStream({
    start(controller: any) {
      if (tokenChunk.length > 0) {
        controller.enqueue(tokenChunk);
      }
      if (otherChunk.length > 0) {
        controller.enqueue(otherChunk);
      }
      controller.close();
    },
  });

  return {
    ok: status >= 200 && status < 300,
    status,
    body: stream,
    text: jest.fn().mockResolvedValue(fullText),
    headers: new Headers(),
  } as unknown as Response;
}

const SESSION_RESPONSE = {
  sessionId: 'session-123',
  createdAt: '2026-02-23T10:00:00Z',
};

const PLAYBOOKS_RESPONSE = { playbooks: [] };

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

let pendingSseResponses: Response[] = [];

function setupFetchRouter() {
  pendingSseResponses = [];
  mockFetch.mockImplementation((url: string, _options?: any) => {
    if (typeof url === 'string' && url.includes('/playbooks')) {
      return Promise.resolve(createFetchResponse(PLAYBOOKS_RESPONSE));
    }
    if (typeof url === 'string' && url.includes('/sessions') && !url.includes('/messages')) {
      return Promise.resolve(createFetchResponse(SESSION_RESPONSE));
    }
    if (typeof url === 'string' && url.includes('/messages')) {
      const response = pendingSseResponses.shift();
      if (response) {
        return Promise.resolve(response);
      }
      return Promise.resolve(createFetchResponse({ error: 'No mock SSE response queued' }, 500));
    }
    return Promise.resolve(createFetchResponse({ error: 'Unmocked URL' }, 404));
  });
}

describe('SprkChat - action_outcome Integration (task 044c)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setupFetchRouter();
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  async function renderChatWithSession() {
    const user = userEvent.setup();

    await act(async () => {
      renderWithProviders(<SprkChat {...defaultProps} />);
    });

    await waitFor(() => {
      const textarea = screen.getByTestId('chat-input-textarea');
      const nativeTextarea = textarea.querySelector('textarea') || textarea;
      expect(nativeTextarea).not.toBeDisabled();
    });

    return user;
  }

  async function sendMessageAndStream(
    user: ReturnType<typeof userEvent.setup>,
    messageText: string,
    sseEvents: Array<Record<string, unknown>>
  ) {
    pendingSseResponses.push(createSseStreamResponse(sseEvents));

    const textarea = screen.getByTestId('chat-input-textarea');
    const nativeTextarea = textarea.querySelector('textarea') || textarea;
    await user.type(nativeTextarea, messageText);
    await user.click(screen.getByTestId('chat-send-button'));
  }

  // ─────────────────────────────────────────────────────────────────────
  // Test 1: the headline G-R2-A scenario — auto-execute create renders the
  // OutcomeCard with the record link chip + the Undo next-step chip.
  // ─────────────────────────────────────────────────────────────────────

  it('sseFlow_ActionOutcomeEvent_RendersOutcomeCardWithRecordLinkAndUndoChip', async () => {
    const user = await renderChatWithSession();

    // Exact server wire shape (ChatSseActionOutcomeData, Api/Ai/ChatEndpoints.cs;
    // emitted by SideEffectGateAIFunction.cs ~line 490 — camelCase per
    // JsonNamingPolicy.CamelCase).
    const sseEvents = [
      {
        type: 'action_outcome',
        content: null,
        data: {
          actionName: 'sprk_create_todo',
          status: 'succeeded',
          userSummary: 'Follow-up task "Review NDA" was created.',
          linkUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_todo&id=abc-123&pagetype=entityrecord',
          linkLabel: 'Open record',
          nextSteps: ['Undo'],
          ledgerOutputKey: 'binding-42@t1',
        },
      },
      {
        type: 'token',
        content: 'ACTION EXECUTED: Follow-up task "Review NDA" was created. An Undo affordance is available.',
      },
      { type: 'done', content: null },
    ];

    await sendMessageAndStream(user, 'Create a follow-up task to review the NDA', sseEvents);

    // The OutcomeCard component (OutcomeCard.tsx) renders its root with this testid.
    // Before task 044c this NEVER appears for the auto-execute leg — only the plain
    // grounded-text token content would render.
    await waitFor(
      () => {
        expect(screen.getByTestId('outcome-card')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    // User-facing summary rendered verbatim.
    expect(screen.getByText('Follow-up task "Review NDA" was created.')).toBeInTheDocument();

    // Record chip: the server-composed link renders as a primary button labeled
    // with the server's linkLabel.
    const recordButton = screen.getByRole('button', { name: /open record/i });
    expect(recordButton).toBeInTheDocument();

    // Undo chip: the declared next-step affordance chip.
    expect(screen.getByTestId('outcome-card-chips')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /undo/i })).toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Test 2: a failed auto-execute outcome still renders the card (status
  // tolerant reader) without a link, and without fabricating an Undo chip.
  // ─────────────────────────────────────────────────────────────────────

  it('sseFlow_ActionOutcomeFailedNoLink_RendersCardWithoutLinkOrChips', async () => {
    const user = await renderChatWithSession();

    const sseEvents = [
      {
        type: 'action_outcome',
        content: null,
        data: {
          actionName: 'sprk_create_todo',
          status: 'failed',
          userSummary: 'The follow-up task could not be created.',
          linkUrl: null,
          linkLabel: null,
          nextSteps: [],
          ledgerOutputKey: 'binding-9@t2',
        },
      },
      { type: 'done', content: null },
    ];

    await sendMessageAndStream(user, 'Create a follow-up task', sseEvents);

    await waitFor(
      () => {
        expect(screen.getByTestId('outcome-card')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    expect(screen.getByText('The follow-up task could not be created.')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /open record/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId('outcome-card-chips')).not.toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Test 3: non-regression — an ordinary token/done stream with NO
  // action_outcome frame must not render an OutcomeCard.
  // ─────────────────────────────────────────────────────────────────────

  it('sseFlow_NoActionOutcomeEvent_NoOutcomeCardRendered', async () => {
    const user = await renderChatWithSession();

    const sseEvents = [
      { type: 'token', content: 'Just a normal grounded answer.' },
      { type: 'done', content: null },
    ];

    await sendMessageAndStream(user, 'A normal question', sseEvents);

    await waitFor(
      () => {
        expect(screen.getByText('Just a normal grounded answer.')).toBeInTheDocument();
      },
      { timeout: 3000 }
    );

    expect(screen.queryByTestId('outcome-card')).not.toBeInTheDocument();
  });
});
