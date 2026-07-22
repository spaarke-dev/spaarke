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

// ---------------------------------------------------------------------------
// Fetch fixtures
// ---------------------------------------------------------------------------

const ALL_THREADS = [
  { threadId: 't1', name: 'Acme Matter', threadType: 100000000, createdOn: '2026-07-19T12:00:00Z' },
  { threadId: 't2', name: 'Direct: Alice', threadType: 100000001, createdOn: '2026-07-18T09:00:00Z' },
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
}

function makeAuthenticatedFetch(options: IMockFetchOptions = {}) {
  const allThreads = options.allThreads ?? ALL_THREADS;
  const regardingResult = options.regardingResult ?? REGARDING_RESULT;
  const unreadByThread = options.unreadByThread ?? {};

  return jest.fn(async (url: string) => {
    if (url.includes('/by-regarding/')) {
      return jsonResponse(regardingResult);
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
    await waitFor(() => expect(screen.getByText('3 new messages')).toBeInTheDocument());
  });

  it('the word filter narrows the list (all mode, server-side search)', async () => {
    renderWorkspace();

    await waitFor(() => expect(screen.getByText('Direct: Alice')).toBeInTheDocument());

    fireEvent.change(screen.getByPlaceholderText('Filter threads'), { target: { value: 'Direct' } });

    await waitFor(() => expect(screen.queryByText('Acme Matter')).not.toBeInTheDocument(), { timeout: 2000 });
    expect(screen.getByText('Direct: Alice')).toBeInTheDocument();
  });

  it('the word filter narrows the list (record mode, client-side over the already-loaded set)', async () => {
    renderWorkspace({ regarding: { entityType: 'sprk_matter', id: 'rec1' } });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());

    fireEvent.change(screen.getByPlaceholderText('Filter threads'), { target: { value: 'zzz-no-match' } });

    await waitFor(() => expect(screen.getByText('No threads match your filter.')).toBeInTheDocument());
  });

  it('the ＋ New button invokes onCreateThread', async () => {
    const onCreateThread = jest.fn();
    renderWorkspace({ onCreateThread });

    await waitFor(() => expect(screen.getByText('Acme Matter')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));

    expect(onCreateThread).toHaveBeenCalledTimes(1);
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
