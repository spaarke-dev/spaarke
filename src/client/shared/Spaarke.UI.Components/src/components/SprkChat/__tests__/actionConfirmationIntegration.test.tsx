/**
 * SprkChat - action_confirmation Integration Tests (spaarke-modal-system task 042)
 *
 * THE CHANGE under test: `ActionConfirmationDialog` — a hand-rolled `position:absolute`
 * div (despite its own in-file comment wrongly claiming it used a Fluent Dialog) — is
 * retired (spec FR-13). SprkChat's HITL action-confirmation flow (`action_confirmation`
 * SSE event -> `pendingAction` state) now renders via the shared `ConfirmModal` preset
 * (`@spaarke/ui-components` SprkModal family, task 005). A11y (focus trap, ESC,
 * aria-modal/alertdialog) now comes from the Fluent `Dialog` inside `ConfirmModal`
 * instead of bespoke listeners.
 *
 * This is a full end-to-end render test: real fetch mock -> real SSE stream -> real
 * `useSseStream` consumer -> real `SprkChat` component tree -> real `ConfirmModal`
 * (Fluent `Dialog`) render — harness modeled on actionOutcomeIntegration.test.tsx.
 * It asserts on the DOM the actual `<ConfirmModal>`/`<SprkModal>` components produce,
 * not a hand-rolled substitute, so it fails if the re-route is ever reverted or the
 * gate-resolve wiring regresses.
 *
 * FULL-SUITE FLAKE FIX (2026-08-02) — two independent root causes, both load-only
 * (standalone was always green):
 *
 * 1. JEST'S DEFAULT 5s WHOLE-TEST TIMEOUT. Each test here mounts a Fluent `Dialog`
 *    portal (Tabster focus trap) on top of the full SprkChat tree and interacts with
 *    it (~0.5s standalone); under parallel-worker CPU contention cumulative wall time
 *    can stretch past 5s, killing the test mid-flight with `Exceeded timeout of
 *    5000 ms for a test` — an opaque failure that masquerades as "dialog never
 *    opened". Fix: explicit 30s per-`it` budget (`TEST_TIMEOUT_MS`); dialog-poll and
 *    waitFor timeouts (10s/3s) stay BELOW it so a genuine regression still fails
 *    fast with an assertion error + DOM dump.
 *
 * 2. A REACT-19 SCHEDULER RACE dropping the dialog leg of the mock stream's update
 *    cascade. The mocked SSE chain resolves entirely in microtasks; where its work
 *    lands is timing-dependent: inside a user-event/RTL act block it flushes
 *    synchronously (happy path), but when parallel-worker load shifts it into the
 *    gap where no act block is open, it goes to React 19's concurrent scheduler —
 *    which in this jsdom environment (no MessageChannel, no setImmediate →
 *    setTimeout fallback) drops work under contention. Instrumented failing runs
 *    pinned the break PRECISELY: the reader loop fully consumed the stream
 *    (getReader/read:chunk/read:done), TextDecoder yielded the full 269-char
 *    payload (events parsed, setStates called), the state BATCH itself COMMITTED
 *    (the composer re-enabled — isStreaming=false landed), no error banner, no
 *    act() warnings — yet the `pendingActionEvent` dispatch effect never fired,
 *    so `pendingAction`/the dialog never materialized. Because that effect's deps
 *    never change again, NOTHING can re-trigger it afterwards: a later
 *    microtask-only act(), a single post-send macrotask flush (FAIL/PASS/FAIL over
 *    3 runs), and even ~10s of REPEATED act+macrotask polling (FAIL/PASS/FAIL
 *    again) were all verified insufficient — rescue is impossible; only
 *    PREVENTION works. The same race can hit ANY async leg (the mount's
 *    setSession chain, the SSE batch, the dispatch effect, the gate-resolve
 *    chain), so prevention is applied to all of them:
 *      - the fetch router PARKS the `/messages` + `/gates/…/resolve` responses on
 *        a pending promise; `flushPendingWork` releases anything parked INSIDE an
 *        open act() block and yields a macrotask there, so the released chain
 *        (response → reader loop → state batch → passive-effect cascade → dialog
 *        render) runs entirely with the act queue open and never touches the real
 *        scheduler;
 *      - every wait (`findAlertDialogWithFlush`, `waitForWithFlush`) calls
 *        `flushPendingWork` per poll iteration, so even a fetch that reaches the
 *        router late is released in-act on the next iteration — no parked
 *        response can strand a wait;
 *      - the mount act() is HELD OPEN for two macrotask turns so the
 *        session-create chain's setSession lands in-act (a stranded setSession
 *        would leave handleSend's `!session` guard silently no-op'ing forever).
 *    Assertions unchanged throughout.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-050 - Canonical Modal Shell
 */
import * as React from 'react';
import { screen, waitFor, act, fireEvent, render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { SprkChat } from '../SprkChat';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream) — same as
// actionOutcomeIntegration.test.tsx / citationsIntegration.test.tsx.
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

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
  const fullText = events.map(evt => `data: ${JSON.stringify(evt)}\n\n`).join('');
  const stream = new (globalThis as any).ReadableStream({
    start(controller: any) {
      controller.enqueue(encoder.encode(fullText));
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
  sessionId: 'session-042',
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
let pendingGateResolveResponses: Response[] = [];
let gateResolveCalls: Array<{ url: string; body: unknown }> = [];
/**
 * Resolvers for DEFERRED `/messages` + `/gates/…/resolve` responses (see header
 * comment, root cause 2). The router parks these responses on a pending promise
 * instead of resolving in the ambient microtask flow; the test releases them
 * INSIDE an open act() block via `flushPendingWork`, so the ENTIRE
 * mock async chain (response → reader loop → state batch → passive-effect
 * cascade → dialog render) runs while the act queue is open and can never touch
 * React's real concurrent scheduler. Call-time behavior (mock.calls recording,
 * gateResolveCalls body capture) is unchanged — only RESOLUTION timing moves.
 */
let deferredNetworkReleases: Array<() => void> = [];

function setupFetchRouter() {
  pendingSseResponses = [];
  pendingGateResolveResponses = [];
  gateResolveCalls = [];
  deferredNetworkReleases = [];
  mockFetch.mockImplementation((url: string, options?: RequestInit) => {
    // Gate-resolve POST (`.../sessions/{id}/gates/{gateId}/resolve`) must be checked
    // BEFORE the generic `/sessions` branch below, since its URL also contains
    // `/sessions/`.
    if (typeof url === 'string' && url.includes('/gates/') && url.includes('/resolve')) {
      gateResolveCalls.push({
        url,
        body: typeof options?.body === 'string' ? JSON.parse(options.body) : undefined,
      });
      const response = pendingGateResolveResponses.shift() ?? createFetchResponse({ summary: 'Done.' });
      // DEFERRED — released inside act() by flushPendingWork.
      return new Promise<Response>(resolve => {
        deferredNetworkReleases.push(() => resolve(response));
      });
    }
    if (typeof url === 'string' && url.includes('/playbooks')) {
      return Promise.resolve(createFetchResponse(PLAYBOOKS_RESPONSE));
    }
    if (typeof url === 'string' && url.includes('/sessions') && !url.includes('/messages')) {
      return Promise.resolve(createFetchResponse(SESSION_RESPONSE));
    }
    if (typeof url === 'string' && url.includes('/messages')) {
      const response = pendingSseResponses.shift();
      if (response) {
        // DEFERRED — released inside act() by flushPendingWork.
        return new Promise<Response>(resolve => {
          deferredNetworkReleases.push(() => resolve(response));
        });
      }
      return Promise.resolve(createFetchResponse({ error: 'No mock SSE response queued' }, 500));
    }
    return Promise.resolve(createFetchResponse({ error: 'Unmocked URL' }, 404));
  });
}

const ACTION_CONFIRMATION_EVENTS = [
  {
    type: 'action_confirmation',
    content: null,
    data: {
      actionId: 'gate-001',
      actionName: 'Send Email',
      summary: 'Send an email to the matter contact.',
      parameters: { Recipient: 'john@example.com', Subject: 'Status update' },
    },
  },
  { type: 'done', content: null },
];

/**
 * Explicit per-test budget (see header comment). Jest only waits as long as the
 * test actually takes — this ceiling exists purely so parallel-worker contention
 * cannot kill an otherwise-passing test mid-flight.
 */
const TEST_TIMEOUT_MS = 30000;

/**
 * The message typed into the composer before each stream. Its CONTENT is never
 * asserted (the flow under test is SSE -> pendingAction -> ConfirmModal), so it
 * is deliberately short: each additional character is a full userEvent
 * keydown/keypress/input/keyup roundtrip with a macrotask yield — pure per-test
 * wall-time cost under loaded workers.
 */
const TYPED_MESSAGE = 'Go';

describe('SprkChat - action_confirmation Integration (spaarke-modal-system task 042)', () => {
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
      // Hold THIS act block open until the mount's session-create chain (effect ->
      // POST /sessions mock -> json -> setSession) has fully settled, so setSession
      // is SCHEDULED while the act queue is open and cannot strand in the
      // concurrent scheduler (header comment, root cause 2 — the same race one leg
      // earlier: a stranded setSession leaves handleSend's `!session` guard
      // permanently no-op'ing, so no /messages fetch ever happens).
      await new Promise(resolve => setTimeout(resolve, 0));
      await new Promise(resolve => setTimeout(resolve, 0));
    });

    await waitFor(
      () => {
        const textarea = screen.getByTestId('chat-input-textarea');
        const nativeTextarea = textarea.querySelector('textarea') || textarea;
        expect(nativeTextarea).not.toBeDisabled();
      },
      // Generous timeout — full-suite parallel workers can lag well past the 1s
      // default (matches actionOutcomeIntegration's convention).
      { timeout: 3000 }
    );

    return user;
  }

  /**
   * THE CORE DETERMINISM PRIMITIVE (see header comment, root cause 2). Inside
   * one act() block: (1) release any PARKED deferred network response(s) — so
   * the mock async chain (response -> reader loop -> state batch ->
   * passive-effect cascade -> dialog render) runs entirely while the act queue
   * is open and can never touch React's real concurrent scheduler; (2) yield
   * one real macrotask turn so the released chain (and any previously pending
   * scheduler callback) runs to completion and flushes at act exit. Every wait
   * loop calls this per iteration, so even a response whose fetch reached the
   * router late is still released in-act on the next poll — there is no window
   * in which a parked response can strand a wait.
   */
  async function flushPendingWork() {
    await act(async () => {
      deferredNetworkReleases.splice(0).forEach(release => release());
      await new Promise(resolve => setTimeout(resolve, 0));
    });
  }

  /**
   * Poll for the confirm dialog while ACTIVELY DRIVING the React 19 scheduler
   * each iteration — a bare findByRole can starve forever when the scheduler's
   * setTimeout-fallback batch is never picked up under loaded workers (see
   * header comment, root cause 2). Each iteration yields a real macrotask inside
   * act(), so any pending scheduler callback runs and its updates flush; the
   * loop keeps nudging until the state lands. Deterministic under arbitrary
   * load — it cannot miss the window the way a single post-send flush can.
   */
  async function findAlertDialogWithFlush(timeoutMs = 10000): Promise<HTMLElement> {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      const dialog = screen.queryByRole('alertdialog');
      if (dialog) return dialog;
      if (Date.now() > deadline) {
        // Let RTL produce its standard error + DOM dump for diagnosability.
        return screen.getByRole('alertdialog');
      }
      await flushPendingWork();
    }
  }

  /**
   * Release-capable equivalent of RTL's waitFor: retries `assertion` while
   * running {@link flushPendingWork} between attempts, so a parked deferred
   * response or pending scheduler work is actively released + driven in-act
   * rather than passively awaited. Throws the assertion's own error at the
   * deadline (same failure semantics + diagnosability as a bare expect).
   */
  async function waitForWithFlush(assertion: () => void, timeoutMs = 10000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    for (;;) {
      try {
        assertion();
        return;
      } catch (error) {
        if (Date.now() > deadline) throw error;
      }
      await flushPendingWork();
    }
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
    // Release the parked SSE response inside an open act() block — the entire
    // stream chain then runs deterministically (see flushPendingWork). If the
    // fetch has not reached the router yet, the dialog-wait poll releases it on
    // a subsequent iteration.
    await flushPendingWork();
  }

  // ─────────────────────────────────────────────────────────────────────
  // Test 1: action_confirmation renders ConfirmModal (Fluent alertdialog),
  // preserving the retired overlay's title/summary/parameters body copy.
  // ─────────────────────────────────────────────────────────────────────

  it('sseFlow_ActionConfirmationEvent_RendersConfirmModalWithSummaryAndParameters', async () => {
    const user = await renderChatWithSession();

    await sendMessageAndStream(user, TYPED_MESSAGE, ACTION_CONFIRMATION_EVENTS);

    // ConfirmModal (dismiss="alert") renders as an alertdialog — a real Fluent Dialog,
    // unlike the retired overlay's plain `role="dialog"` div.
    const dialog = await findAlertDialogWithFlush();
    expect(dialog).toHaveTextContent('Confirm Action: Send Email');
    expect(dialog).toHaveTextContent('Send an email to the matter contact.');

    // Parameters box — body copy the user needs before confirming a side-effecting
    // action; preserved from the retired overlay rather than dropped.
    expect(screen.getByTestId('action-parameters')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('Recipient:');
    expect(dialog).toHaveTextContent('john@example.com');
    expect(dialog).toHaveTextContent('Subject:');
    expect(dialog).toHaveTextContent('Status update');

    // Standard Cancel/Confirm footer.
    expect(screen.getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^confirm$/i })).toBeInTheDocument();
  }, TEST_TIMEOUT_MS);

  // ─────────────────────────────────────────────────────────────────────
  // Test 2: Confirm dispatches the approved gate-resolve call, closes the
  // dialog, and renders the completion message in the transcript.
  // ─────────────────────────────────────────────────────────────────────

  it('confirmClick_DispatchesApprovedGateResolveAndClosesDialog', async () => {
    const user = await renderChatWithSession();
    await sendMessageAndStream(user, TYPED_MESSAGE, ACTION_CONFIRMATION_EVENTS);
    await findAlertDialogWithFlush();

    pendingGateResolveResponses.push(createFetchResponse({ summary: 'Email sent.' }));
    await user.click(screen.getByRole('button', { name: /^confirm$/i }));
    // The confirm dispatch (gate-resolve fetch -> transcript message -> dialog
    // close) is the same async class as the send stream — release its parked
    // response inside act() (see flushPendingWork).
    await flushPendingWork();

    await waitForWithFlush(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });

    expect(gateResolveCalls).toHaveLength(1);
    expect(gateResolveCalls[0].url).toContain('/sessions/session-042/gates/gate-001/resolve');
    expect(gateResolveCalls[0].body).toEqual({ approved: true });

    await waitForWithFlush(() => {
      expect(screen.getByText(/Send Email completed\. Email sent\./)).toBeInTheDocument();
    });
  }, TEST_TIMEOUT_MS);

  // ─────────────────────────────────────────────────────────────────────
  // Test 3: Cancel dispatches the rejected gate-resolve call and closes the
  // dialog immediately WITHOUT executing the action — matches the retired
  // overlay's onCancel semantics (clears local state synchronously; the
  // server reject call is fire-and-forget).
  // ─────────────────────────────────────────────────────────────────────

  it('cancelClick_DispatchesRejectedGateResolveAndClosesDialogWithoutExecuting', async () => {
    const user = await renderChatWithSession();
    await sendMessageAndStream(user, TYPED_MESSAGE, ACTION_CONFIRMATION_EVENTS);
    await findAlertDialogWithFlush();

    await user.click(screen.getByRole('button', { name: /^cancel$/i }));

    // Cancel clears local state synchronously — the dialog must already be gone
    // BEFORE any async drain (that is the assertion of the retired overlay's
    // fire-and-forget cancel semantics).
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    // Release + drive the fire-and-forget reject POST through deterministically.
    await flushPendingWork();

    await waitForWithFlush(() => {
      expect(gateResolveCalls).toHaveLength(1);
    });
    expect(gateResolveCalls[0].body).toEqual({ approved: false });

    expect(screen.queryByText(/completed/i)).not.toBeInTheDocument();
  }, TEST_TIMEOUT_MS);

  // ─────────────────────────────────────────────────────────────────────
  // Test 4: ESC does not dismiss — dismiss="alert" (ConfirmModal's fixed
  // contract) blocks light-dismiss. The retired overlay's ESC handling was
  // incidental (an onKeyDown on a div with no tabIndex, only reachable when
  // focus already sat on a button inside it) — never a deliberate light-
  // dismiss guarantee, so this is a11y parity, not a regression.
  // ─────────────────────────────────────────────────────────────────────

  it('escapeKey_DoesNotDismiss_AlertModalBlocksLightDismiss', async () => {
    const user = await renderChatWithSession();
    await sendMessageAndStream(user, TYPED_MESSAGE, ACTION_CONFIRMATION_EVENTS);
    const dialog = await findAlertDialogWithFlush();

    fireEvent.keyDown(dialog, { key: 'Escape', code: 'Escape' });

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(gateResolveCalls).toHaveLength(0);
  }, TEST_TIMEOUT_MS);

  // ─────────────────────────────────────────────────────────────────────
  // Test 5: dark-theme parity (ADR-021) — semantic tokens only, renders
  // correctly under webDarkTheme with no crash.
  // ─────────────────────────────────────────────────────────────────────

  it('darkTheme_RendersActionConfirmationWithSummaryAndParameters', async () => {
    const user = userEvent.setup();

    await act(async () => {
      render(
        <FluentProvider theme={webDarkTheme}>
          <SprkChat {...defaultProps} />
        </FluentProvider>
      );
      // Same held-open mount act as renderChatWithSession: let the session-create
      // chain settle while the act queue is open (header comment, root cause 2).
      await new Promise(resolve => setTimeout(resolve, 0));
      await new Promise(resolve => setTimeout(resolve, 0));
    });

    await waitFor(
      () => {
        const textarea = screen.getByTestId('chat-input-textarea');
        const nativeTextarea = textarea.querySelector('textarea') || textarea;
        expect(nativeTextarea).not.toBeDisabled();
      },
      // Generous timeout — full-suite parallel workers can lag well past the 1s
      // default (matches actionOutcomeIntegration's convention).
      { timeout: 3000 }
    );

    await sendMessageAndStream(user, TYPED_MESSAGE, ACTION_CONFIRMATION_EVENTS);

    const dialog = await findAlertDialogWithFlush();
    expect(dialog).toHaveTextContent('Confirm Action: Send Email');
    expect(screen.getByTestId('action-parameters')).toBeInTheDocument();
  }, TEST_TIMEOUT_MS);
});
