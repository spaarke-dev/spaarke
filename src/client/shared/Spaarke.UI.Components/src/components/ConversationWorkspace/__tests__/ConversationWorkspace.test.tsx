/**
 * ConversationWorkspace.test.tsx (task 012, FR-01/10)
 *
 * Behavior contract for the shared two-pane conversation shell:
 *  - mount-agnostic (renders identically with/without `regarding`)
 *  - all-mode consumes FR-16 `GET /api/communications/threads`; record mode
 *    routes to the existing `GET /api/communications/by-regarding/{type}/{id}`
 *    (see `communicationThreadListApi.ts` header for why FR-16 itself cannot
 *    express the regarding filter)
 *  - rows show name + unread only; word filter narrows; ＋ invokes onCreateThread
 *  - default-select + the `renderConversation` right-pane seam
 *  - empty / loading / error states; ARIA list semantics + keyboard selection
 *  - both Fluent v9 themes (ADR-021)
 */
import * as React from 'react';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { ConversationWorkspace } from '../ConversationWorkspace';
import type { ConversationWorkspaceProps, IConversationRendererProps } from '../ConversationWorkspace';
import type { INavigationService } from '../../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Fetch fixtures
// ---------------------------------------------------------------------------

const ALL_THREADS = [
  { threadId: 't1', name: 'Acme Matter', threadType: 100000000, createdOn: '2026-07-19T12:00:00Z', isPinned: false },
  { threadId: 't2', name: 'Direct: Alice', threadType: 100000001, createdOn: '2026-07-18T09:00:00Z', isPinned: false },
];

const REGARDING_RESULT = {
  entityType: 'sprk_matter',
  recordId: 'rec1',
  threads: [{ threadId: 't1', name: 'Acme Matter' }],
  threadCount: 1,
  messageCount: 0,
};

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/json' },
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

interface IMockFetchOptions {
  allThreads?: typeof ALL_THREADS;
  regardingResult?: typeof REGARDING_RESULT;
  unreadByThread?: Record<string, number>;
  /** task 041: when true, every PATCH .../pin request resolves 500 (rollback test). */
  pinShouldFail?: boolean;
}

function makeAuthenticatedFetch(options: IMockFetchOptions = {}) {
  const allThreads = options.allThreads ?? ALL_THREADS;
  const regardingResult = options.regardingResult ?? REGARDING_RESULT;
  const unreadByThread = options.unreadByThread ?? {};

  return jest.fn(async (url: string, init?: RequestInit) => {
    if (url.includes('/by-regarding/')) {
      return jsonResponse(regardingResult);
    }
    const pinMatch = url.match(/\/threads\/([^/]+)\/pin$/);
    if (pinMatch && init?.method === 'PATCH') {
      if (options.pinShouldFail) {
        return jsonResponse({ title: 'Server error', detail: 'Write failed' }, 500);
      }
      const threadId = pinMatch[1];
      const body = JSON.parse((init.body as string) ?? '{}') as { pinned: boolean };
      return jsonResponse({ threadId, isPinned: body.pinned });
    }
    const unreadMatch = url.match(/\/threads\/([^/]+)\/unread-count/);
    if (unreadMatch) {
      const threadId = unreadMatch[1];
      return jsonResponse({ threadId, since: null, unreadCount: unreadByThread[threadId] ?? 0 });
    }
    if (/\/api\/communications\/threads(\?|$)/.test(url)) {
      const parsed = new URL(url, 'https://bff.example.com');
      const search = parsed.searchParams.get('search');
      const threads = search ? allThreads.filter(t => t.name.toLowerCase().includes(search.toLowerCase())) : allThreads;
      return jsonResponse({ threads, count: threads.length, nextPageToken: null, hasMore: false });
    }
    return jsonResponse({ title: 'Not Found' }, 404);
  });
}

function renderWorkspace(
  overrides: Partial<ConversationWorkspaceProps> = {},
  theme: typeof webLightTheme = webLightTheme
) {
  const authenticatedFetch = overrides.authenticatedFetch ?? makeAuthenticatedFetch();
  const utils = render(
    <FluentProvider theme={theme}>
      <ConversationWorkspace authenticatedFetch={authenticatedFetch} {...overrides} />
    </FluentProvider>
  );
  return { authenticatedFetch: authenticatedFetch as jest.Mock, ...utils };
}

// ---------------------------------------------------------------------------
// Mount-agnostic + regarding filter
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — mount-agnostic (FR-01)', () => {
  it('all mode (no regarding): lists every thread, incl. record-less, via FR-16 /threads', async () => {
    const { authenticatedFetch } = renderWorkspace();

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    expect(screen.getByText('Direct: Alice')).toBeInTheDocument();

    expect(authenticatedFetch.mock.calls.some(([u]) => /\/api\/communications\/threads(\?|$)/.test(u))).toBe(true);
    expect(authenticatedFetch.mock.calls.some(([u]) => u.includes('/by-regarding/'))).toBe(false);
  });

  it("record mode (regarding present): lists ONLY that record's threads via by-regarding, never FR-16 /threads", async () => {
    const { authenticatedFetch } = renderWorkspace({ regarding: { entityType: 'sprk_matter', id: 'rec1' } });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    expect(screen.queryByText('Direct: Alice')).not.toBeInTheDocument();

    expect(authenticatedFetch.mock.calls.some(([u]) => u.includes('/by-regarding/sprk_matter/rec1'))).toBe(true);
    const listCalls = authenticatedFetch.mock.calls.filter(([u]) => /\/api\/communications\/threads(\?|$)/.test(u));
    expect(listCalls).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// Thread list content (name + unread only; word filter; create)
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — thread list content (FR-10)', () => {
  it('rows show the thread name and an unread indicator when unread > 0', async () => {
    const authenticatedFetch = makeAuthenticatedFetch({ unreadByThread: { t1: 3 } });
    renderWorkspace({ authenticatedFetch });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    // R3 UAT 2026-07-22 item 5b: the "N new messages" text row + inline
    // "Mark as read" button were replaced by a compact unread dot (the
    // mark-as-read action moved to the message toolbar, item 5c). The dot
    // carries the count in its accessible name.
    await waitFor(() => expect(screen.getByLabelText('3 unread messages')).toBeInTheDocument());
    expect(screen.queryByText('3 new messages')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /mark as read/i })).not.toBeInTheDocument();
  });

  it('has NO thread-list text filter (task 062 / §B4 — removed in the Teams-style redesign)', async () => {
    renderWorkspace();

    await waitFor(() => expect(screen.getByText('Direct: Alice')).toBeInTheDocument());

    // The "Filter threads" input was removed; the full access-filtered set shows.
    expect(screen.queryByPlaceholderText('Filter threads')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: /filter threads/i })).not.toBeInTheDocument();
    expect(screen.getByText('Acme Matter')).toBeInTheDocument();
  });

  it('the icon-only ＋ (create) button invokes onCreateThread', async () => {
    const onCreateThread = jest.fn();
    renderWorkspace({ onCreateThread });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    // Icon-only now (task 062 / §B5) — identified by its "New thread" aria-label.
    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));

    expect(onCreateThread).toHaveBeenCalledTimes(1);
  });

  it('opens the built-in New-conversation modal when a navigationService is supplied (item 5a / item 9)', async () => {
    const navigationService = { openLookup: jest.fn().mockResolvedValue([]) } as unknown as INavigationService;
    renderWorkspace({ navigationService });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    // No modal until the ＋ is clicked.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));

    // The shell owns the create surface: NewThreadModal opens in-place.
    expect(await screen.findByRole('dialog', { name: /New conversation/i })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Thread-pane header + collapse (R3 UAT 2026-07-23 items 3/5/8)
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — thread pane header + collapse', () => {
  it('renders a "Threads" title and collapses/expands the pane on the header affordance', async () => {
    renderWorkspace();

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    // Title present (item 5); create ＋ present (item 8).
    expect(screen.getByText('Threads')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument();

    // Collapse (item 3) — the thread list content disappears, a re-expand rail appears.
    fireEvent.click(screen.getByRole('button', { name: 'Collapse threads pane' }));
    await waitFor(() => expect(screen.queryByText('Acme Matter')).not.toBeInTheDocument());
    const expandRail = screen.getByRole('button', { name: 'Expand threads pane' });
    expect(expandRail).toBeInTheDocument();

    // Expand — the thread list returns.
    fireEvent.click(expandRail);
    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
  });
});

// ---------------------------------------------------------------------------
// Selection + renderConversation seam
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — selection + renderConversation seam', () => {
  it('default-selects the first/most-recent thread and renders it via renderConversation', async () => {
    const renderConversation = jest.fn((props: IConversationRendererProps) => (
      <div data-testid="conv">{props.threadId}</div>
    ));
    renderWorkspace({ renderConversation });

    await waitFor(() => expect(screen.getByTestId('conv')).toHaveTextContent('t1'));
    expect(renderConversation).toHaveBeenCalledWith(expect.objectContaining({ threadId: 't1', bffBaseUrl: undefined }));
  });

  it('renders the built-in placeholder pane when renderConversation is omitted', async () => {
    renderWorkspace();
    await waitFor(() => expect(screen.getByText(/Conversation placeholder for thread t1/)).toBeInTheDocument());
  });

  it('selecting a different row updates the right pane and fires onThreadSelected', async () => {
    const onThreadSelected = jest.fn();
    const renderConversation = jest.fn((props: IConversationRendererProps) => (
      <div data-testid="conv">{props.threadId}</div>
    ));
    renderWorkspace({ onThreadSelected, renderConversation });

    await waitFor(() => expect(screen.getByTestId('conv')).toHaveTextContent('t1'));
    fireEvent.click(screen.getByText('Direct: Alice'));

    await waitFor(() => expect(screen.getByTestId('conv')).toHaveTextContent('t2'));
    expect(onThreadSelected).toHaveBeenCalledWith('t2');
  });
});

// ---------------------------------------------------------------------------
// Empty / loading / error states + ARIA + keyboard (NFR-05)
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — empty / loading / error states (NFR-05)', () => {
  it('renders a loading state before the list resolves', () => {
    const authenticatedFetch = jest.fn(() => new Promise<Response>(() => {})); // never resolves
    renderWorkspace({ authenticatedFetch });

    expect(screen.getByText('Loading threads…')).toBeInTheDocument();
  });

  it('renders an empty state when there are no threads', async () => {
    const authenticatedFetch = makeAuthenticatedFetch({
      allThreads: [],
      regardingResult: { ...REGARDING_RESULT, threads: [], threadCount: 0 },
    });
    renderWorkspace({ authenticatedFetch });

    await waitFor(() => expect(screen.getByText('No threads yet.')).toBeInTheDocument());
  });

  it('renders an error state (and fires onError) when the list fetch fails', async () => {
    const authenticatedFetch = jest.fn(async () => jsonResponse({ title: 'Boom', detail: 'Server error' }, 500));
    const onError = jest.fn();
    renderWorkspace({ authenticatedFetch, onError });

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(onError).toHaveBeenCalledTimes(1);
  });

  it('exposes ARIA list/listitem semantics and supports keyboard selection', async () => {
    const onThreadSelected = jest.fn();
    renderWorkspace({ onThreadSelected });

    await waitFor(() => expect(screen.getByRole('list', { name: 'Conversation threads' })).toBeInTheDocument());
    expect(screen.getAllByRole('listitem')).toHaveLength(2);

    const list = screen.getByRole('list', { name: 'Conversation threads' });
    fireEvent.keyDown(list, { key: 'ArrowDown' });
    fireEvent.keyDown(list, { key: 'Enter' });

    await waitFor(() => expect(onThreadSelected).toHaveBeenLastCalledWith('t2'));
  });
});

// ---------------------------------------------------------------------------
// Dark mode (ADR-021)
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — dark mode (ADR-021)', () => {
  it('renders under webDarkTheme without hardcoded-color regressions', async () => {
    renderWorkspace({}, webDarkTheme);
    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    expect(screen.getByText('Direct: Alice')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// Pin/unpin (task 041, FR-24)
// ---------------------------------------------------------------------------

describe('ConversationWorkspace — pin/unpin (task 041, FR-24)', () => {
  it('reflects the persisted pin state on load (survives a reload) and sorts the pinned thread to the top', async () => {
    // t2 arrives from the server already pinned (isPinned:true) — simulates a fresh mount after a page reload,
    // NOT an in-session optimistic update. t1 (createdon-desc's natural first row) is unpinned.
    const allThreads = [ALL_THREADS[0], { ...ALL_THREADS[1], isPinned: true }];
    renderWorkspace({ authenticatedFetch: makeAuthenticatedFetch({ allThreads }) });

    await waitFor(() => expect(screen.getByText('Direct: Alice')).toBeInTheDocument());

    const pinnedButton = screen.getByRole('button', { name: 'Unpin Direct: Alice' });
    const unpinnedButton = screen.getByRole('button', { name: 'Pin Acme Matter' });
    expect(pinnedButton).toHaveAttribute('aria-pressed', 'true');
    expect(unpinnedButton).toHaveAttribute('aria-pressed', 'false');

    // Sorted to the top: the pinned thread's listitem precedes the unpinned one in DOM order, even though the
    // server returned t1 (unpinned) first.
    const items = screen.getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Direct: Alice');
    expect(items[1]).toHaveTextContent('Acme Matter');
  });

  it('pinning a thread optimistically sets aria-pressed and PATCHes the pin field', async () => {
    const authenticatedFetch = makeAuthenticatedFetch();
    renderWorkspace({ authenticatedFetch });
    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());

    const pinButton = screen.getByRole('button', { name: 'Pin Acme Matter' });
    fireEvent.click(pinButton);

    // Optimistic — flips immediately, before the PATCH resolves.
    expect(screen.getByRole('button', { name: 'Unpin Acme Matter' })).toHaveAttribute('aria-pressed', 'true');

    await waitFor(() =>
      expect(
        authenticatedFetch.mock.calls.some(
          ([u, init]) => /\/threads\/t1\/pin$/.test(u) && (init as RequestInit)?.method === 'PATCH'
        )
      ).toBe(true)
    );
    const [, init] = authenticatedFetch.mock.calls.find(([u]) => /\/threads\/t1\/pin$/.test(u))!;
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({ pinned: true });
  });

  it('unpinning clears the pinned marker and PATCHes pinned:false', async () => {
    const allThreads = [{ ...ALL_THREADS[0], isPinned: true }, ALL_THREADS[1]];
    const authenticatedFetch = makeAuthenticatedFetch({ allThreads });
    renderWorkspace({ authenticatedFetch });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Unpin Acme Matter' })).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Unpin Acme Matter' }));

    expect(screen.getByRole('button', { name: 'Pin Acme Matter' })).toHaveAttribute('aria-pressed', 'false');
    await waitFor(() => {
      const call = authenticatedFetch.mock.calls.find(([u]) => /\/threads\/t1\/pin$/.test(u));
      expect(call).toBeDefined();
      expect(JSON.parse((call![1] as RequestInit).body as string)).toEqual({ pinned: false });
    });
  });

  it('rolls back the optimistic pin state when the PATCH fails', async () => {
    const authenticatedFetch = makeAuthenticatedFetch({ pinShouldFail: true });
    const onError = jest.fn();
    renderWorkspace({ authenticatedFetch, onError });
    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Pin Acme Matter' }));

    // Optimistic flip happens immediately...
    expect(screen.getByRole('button', { name: 'Unpin Acme Matter' })).toHaveAttribute('aria-pressed', 'true');

    // ...then rolls back once the PATCH rejects.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Pin Acme Matter' })).toHaveAttribute('aria-pressed', 'false')
    );
    expect(onError).toHaveBeenCalled();
  });

  it('never renders an archive/mute/tag control (FR-24 is pin-only)', async () => {
    renderWorkspace();
    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());

    const buttons = screen.getAllByRole('button');
    const forbidden = buttons.filter(b =>
      /archive|mute|tag/i.test(b.getAttribute('aria-label') ?? b.textContent ?? '')
    );
    expect(forbidden).toHaveLength(0);
  });
});
