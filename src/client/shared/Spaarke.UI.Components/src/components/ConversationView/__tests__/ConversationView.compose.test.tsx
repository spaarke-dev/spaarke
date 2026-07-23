/**
 * ConversationView.compose.test.tsx (task 013, FR-06)
 *
 * Coverage for the in-conversation compose bar layered onto the bubble view.
 * The single highest-value assertions (per the task acceptance criteria):
 *   1. Compose submit invokes the EXISTING send path — POST /api/communications/send
 *      with `communicationType: 'Message'` + the active `threadId`, no new
 *      send function/endpoint (ADR-045/046).
 *   2. On send the thread refreshes immediately (the new message appears
 *      WITHOUT advancing the ~5s interval), and the ~5s interval polling stays
 *      active independently.
 *
 * Per ADR-038/ADR-028 a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same pattern as the
 * sibling `ConversationView.test.tsx`.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import type { ConversationViewProps } from '../ConversationView.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000000, // PlainText
    communicationType: 100000004, // Message
    from: 'someone@example.com',
    direction: null,
    sentBy: USER_1,
    sentByName: 'Me',
    sentAt: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    inReplyTo: null,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

function jsonResponse(body: unknown): Response {
  return { ok: true, json: async () => body } as unknown as Response;
}

/**
 * A stateful fake fetch: reads return whatever is currently in `store.messages`
 * (so a send that pushes into the store surfaces on the very next read), and
 * `/send` appends a Message row + returns a communicationId. `store.reads`
 * counts read polls so tests can assert immediate-refresh + interval-alive.
 */
function buildStatefulFetch(initial: IThreadMessageDto[] = []) {
  const store = {
    messages: [...initial],
    reads: 0,
    sends: [] as Array<Record<string, unknown>>,
  };
  const fetchMock = jest.fn(async (url: string, init?: RequestInit) => {
    if (url.includes('/send')) {
      const payload = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>;
      store.sends.push(payload);
      store.messages.push(
        dto(`sent-${store.sends.length}`, {
          body: String(payload.body ?? ''),
          createdOn: `2026-07-20T10:0${store.sends.length}:00Z`,
          sentAt: `2026-07-20T10:0${store.sends.length}:00Z`,
        })
      );
      return jsonResponse({ communicationId: `new-${store.sends.length}` });
    }
    if (url.includes('/unread-count')) {
      return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
    }
    store.reads += 1;
    // Honor the incremental `since` cursor exactly like the BFF read does
    // (exclusive: only rows strictly newer than `since`), so tests genuinely
    // exercise the `sinceCursorRef` path rather than always full-loading.
    const sinceMatch = /[?&]since=([^&]+)/.exec(url);
    const since = sinceMatch ? decodeURIComponent(sinceMatch[1]) : undefined;
    const visible = since ? store.messages.filter(m => (m.createdOn ?? '') > since) : store.messages;
    return jsonResponse({ threadId: 't1', name: null, messages: visible, count: visible.length });
  });
  return { fetchMock, store };
}

function buildFailingSendFetch() {
  const fetchMock = jest.fn(async (url: string) => {
    if (url.includes('/send')) {
      return {
        ok: false,
        status: 500,
        headers: { get: () => 'application/json' },
        json: async () => ({ detail: 'Send failed' }),
      } as unknown as Response;
    }
    if (url.includes('/unread-count')) {
      return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
    }
    return jsonResponse({ threadId: 't1', name: null, messages: [], count: 0 });
  });
  return fetchMock;
}

function renderView(overrides: Partial<ConversationViewProps>, theme = webLightTheme) {
  const props: ConversationViewProps = {
    threadId: 't1',
    currentUserSystemUserId: USER_1,
    // Large interval so any mid-test refresh must come from `pollNow`, not the interval.
    pollIntervalMs: 100000,
    authenticatedFetch: undefined as unknown as ConversationViewProps['authenticatedFetch'],
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <ConversationView {...props} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Compose bar
// ---------------------------------------------------------------------------

describe('ConversationView — in-conversation compose (FR-06)', () => {
  it('Send is disabled on empty input and enabled once text is entered', async () => {
    const { fetchMock } = buildStatefulFetch();
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    const sendButton = screen.getByRole('button', { name: 'Send message' });
    expect(sendButton).toBeDisabled();

    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'hello');
    expect(sendButton).toBeEnabled();
  });

  it('submit invokes the EXISTING send path — POST /send with communicationType Message + active threadId', async () => {
    const { fetchMock, store } = buildStatefulFetch();
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'ship it');
    await userEvent.click(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(store.sends).toHaveLength(1));
    const sendCall = fetchMock.mock.calls.find(([url]) => String(url).includes('/send'));
    expect(sendCall).toBeDefined();
    const [url, init] = sendCall as [string, RequestInit];
    // Reuses the canonical POST /api/communications/send — NO new endpoint.
    expect(url).toContain('/api/communications/send');
    expect(init.method).toBe('POST');
    expect(store.sends[0]).toMatchObject({
      communicationType: 'Message', // ACS Message branch (ADR-046)
      threadId: 't1', // stamped onto the active thread (FR-06)
      body: 'ship it',
    });
  });

  it('Enter sends (Shift+Enter does not) via the existing path', async () => {
    const { fetchMock, store } = buildStatefulFetch();
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    const input = screen.getByRole('textbox', { name: 'Message' });
    await userEvent.type(input, 'line one{Shift>}{Enter}{/Shift}still typing');
    expect(store.sends).toHaveLength(0); // Shift+Enter inserts a newline, does not send

    await userEvent.type(input, '{Enter}');
    await waitFor(() => expect(store.sends).toHaveLength(1));
    expect(String(store.sends[0].body)).toContain('line one');
  });

  it('refreshes immediately on send — the new message appears without advancing the ~5s interval', async () => {
    const { fetchMock, store } = buildStatefulFetch();
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    // Let the initial full load settle (empty thread).
    expect(await screen.findByText('No messages yet.')).toBeInTheDocument();
    const readsBeforeSend = store.reads;

    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'hi there');
    await userEvent.click(screen.getByRole('button', { name: 'Send message' }));

    // The sent bubble appears WITHOUT the interval firing (interval is 100s here) —
    // proving the on-send `pollNow` refresh, and that a read happened after the send.
    await waitFor(() => expect(screen.getByText('hi there')).toBeInTheDocument());
    expect(store.reads).toBeGreaterThan(readsBeforeSend);
  });

  it('the manual refresh control forces a poll (on-demand refresh)', async () => {
    const { fetchMock, store } = buildStatefulFetch([dto('m1', { body: 'existing' })]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(store.reads).toBeGreaterThan(0));
    const readsBefore = store.reads;

    await userEvent.click(screen.getByRole('button', { name: 'Refresh conversation' }));
    await waitFor(() => expect(store.reads).toBeGreaterThan(readsBefore));
  });

  it('keeps the ~5s interval polling active (interval keeps firing independently of send)', async () => {
    const { fetchMock, store } = buildStatefulFetch();
    // Short interval: prove the poll loop is intact and NOT disabled by the compose feature.
    renderView({
      authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'],
      pollIntervalMs: 30,
    });

    await waitFor(() => expect(store.reads).toBeGreaterThan(3));
  });

  it('does NOT double-send when the user keeps typing during an in-flight send', async () => {
    // A send whose resolution we control, to hold the in-flight window open.
    let resolveSend!: (r: Response) => void;
    const gate = new Promise<Response>(res => {
      resolveSend = res;
    });
    const sends: Array<Record<string, unknown>> = [];
    const fetchMock = jest.fn(async (url: string, init?: RequestInit) => {
      if (url.includes('/send')) {
        sends.push(JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>);
        return gate; // stays pending — keeps the send "in flight"
      }
      if (url.includes('/unread-count')) {
        return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
      }
      return jsonResponse({ threadId: 't1', name: null, messages: [], count: 0 });
    });
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    const input = screen.getByRole('textbox', { name: 'Message' });
    await userEvent.type(input, 'first');
    await userEvent.click(screen.getByRole('button', { name: 'Send message' }));
    await waitFor(() => expect(sends).toHaveLength(1));

    // While the first send is still pending, keep typing and hit Enter again.
    await userEvent.type(input, ' more{Enter}');
    // The in-flight guard blocks the second send — still exactly ONE.
    expect(sends).toHaveLength(1);

    // Resolve so the async continuation settles (avoids act warnings).
    resolveSend(jsonResponse({ communicationId: 'x' }));
    await screen.findByText('Sent');
  });

  it('failed send shows a retryable alert and retains the draft', async () => {
    const fetchMock = buildFailingSendFetch();
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    const input = screen.getByRole('textbox', { name: 'Message' });
    await userEvent.type(input, 'will fail');
    await userEvent.click(screen.getByRole('button', { name: 'Send message' }));

    // Failed state surfaces as an alert; the draft is retained so Send can retry.
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(input).toHaveValue('will fail');
    expect(screen.getByRole('button', { name: 'Send message' })).toBeEnabled();
  });

  it('renders the compose input under dark mode via the host FluentProvider (ADR-021)', () => {
    const { fetchMock } = buildStatefulFetch();
    renderView(
      { authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] },
      webDarkTheme
    );
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send message' })).toBeInTheDocument();
  });
});
