/**
 * ConversationWorkspace.private.test.tsx (fm task 056b)
 *
 * Private messages in a record's conversation (hosted with `regarding`):
 *  - "Send privately…" exists only when the workspace is hosted for a matter / project / work assignment AND can open the
 *    person picker; the dialog flow is picker -> startDirectThread -> send with privateContext, then the pair's entry is
 *    selected;
 *  - the list gains one "Private · you and {name}" entry per counterpart from `GET /threads?kind=direct&about=…`, kept
 *    apart from the record's own threads, with no pin or delete control;
 *  - opening an entry hands the renderer `about` (so the thread read is narrowed to the record), and "Open full
 *    conversation" shows the same pair thread unnarrowed.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ConversationWorkspace } from '../ConversationWorkspace';
import type { ConversationWorkspaceProps, IConversationRendererProps } from '../ConversationWorkspace';
import { ConversationView } from '../../ConversationView';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { INavigationService } from '../../../types/serviceInterfaces';

const MATTER = { entityType: 'sprk_matter', id: 'rec1' };

const PRIVATE_ROWS = [
  {
    threadId: 'p1',
    name: 'Direct Conversation',
    threadType: 100000001,
    createdOn: '2026-11-19T09:00:00Z',
    isPinned: false,
    counterpartName: 'Alice Archer',
  },
  {
    threadId: 'p2',
    name: 'Direct Conversation',
    threadType: 100000001,
    createdOn: '2026-11-18T09:00:00Z',
    isPinned: false,
    counterpartName: 'Bob Baker',
  },
];

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/json' },
    json: async () => body,
  } as unknown as Response;
}

interface IFetchOptions {
  privateRows?: unknown[];
  privateListFails?: boolean;
  /** What the list returns once a send has happened. */
  afterSend?: unknown[];
}

function makeFetch(options: IFetchOptions = {}) {
  const privateRows = options.privateRows ?? PRIVATE_ROWS;
  // The server lists a pair once a private message about the record exists.
  let sentPrivately = false;
  return throwingAuthenticatedFetch(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase();
    if (url.includes('/by-regarding/')) {
      return jsonResponse({
        entityType: 'sprk_matter',
        recordId: 'rec1',
        threads: [{ threadId: 't1', name: 'Acme Matter' }],
        threadCount: 1,
      });
    }
    if (method === 'POST' && url.endsWith('/api/communications/threads/direct')) {
      return jsonResponse({ threadId: 'p1', callerSystemUserId: 'me', otherParticipantSystemUserId: 'alice-1' });
    }
    if (method === 'POST' && url.includes('/api/communications/send')) {
      sentPrivately = true;
      return jsonResponse({ communicationId: 'c1' });
    }
    if (/\/threads\/[^/]+\/unread-count/.test(url)) return jsonResponse({ unreadCount: 0 });
    if (/\/threads\/[^/]+\/messages/.test(url)) {
      return jsonResponse({ threadId: 'p1', name: null, messages: [], count: 0 });
    }
    if (/\/api\/communications\/threads(\?|$)/.test(url)) {
      if (options.privateListFails) return jsonResponse({ title: 'Error', status: 500, detail: 'boom' }, 500);
      const threads = options.afterSend && sentPrivately ? options.afterSend : privateRows;
      return jsonResponse({ threads, count: threads.length, nextPageToken: null, hasMore: false });
    }
    return jsonResponse({ title: 'Not Found', status: 404 }, 404);
  });
}

function makeNav(): INavigationService {
  return {
    openLookup: jest.fn().mockResolvedValue([{ id: '{ALICE-1}', name: 'Alice Archer', entityType: 'systemuser' }]),
  } as unknown as INavigationService;
}

function renderWorkspace(overrides: Partial<ConversationWorkspaceProps> = {}, fetchImpl = makeFetch()) {
  const renderConversation = jest.fn((p: IConversationRendererProps) => (
    <div>{`open:${p.threadId}:${p.threadName}:${p.about ? 'about' : 'full'}`}</div>
  ));
  render(
    <FluentProvider theme={webLightTheme}>
      <ConversationWorkspace
        authenticatedFetch={fetchImpl}
        regarding={MATTER}
        navigationService={makeNav()}
        renderConversation={renderConversation}
        {...overrides}
      />
    </FluentProvider>
  );
  return { fetchMock: fetchImpl as jest.Mock, renderConversation };
}

const privateListUrls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.map(c => String(c[0])).filter(u => /\/api\/communications\/threads\?/.test(u));
const lastRender = (renderConversation: jest.Mock) =>
  renderConversation.mock.calls[renderConversation.mock.calls.length - 1][0] as IConversationRendererProps;

describe('ConversationWorkspace - "Send privately…" action (fm task 056b)', () => {
  it('is offered on a matter, project and work assignment', async () => {
    for (const entityType of ['sprk_matter', 'sprk_project', 'sprk_workassignment']) {
      const { unmount } = (() => {
        const utils = render(
          <FluentProvider theme={webLightTheme}>
            <ConversationWorkspace
              authenticatedFetch={makeFetch()}
              regarding={{ entityType, id: 'rec1' }}
              navigationService={makeNav()}
            />
          </FluentProvider>
        );
        return utils;
      })();
      expect(await screen.findByRole('button', { name: 'Send privately…' })).toBeInTheDocument();
      unmount();
    }
  });

  it('is not offered when the workspace is not hosted for a record', async () => {
    renderWorkspace({ regarding: undefined });

    await screen.findByText('Private · you and Alice Archer').catch(() => undefined);
    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Send privately…' })).not.toBeInTheDocument();
  });

  it('is not offered on a record type the server does not accept', async () => {
    renderWorkspace({ regarding: { entityType: 'contact', id: 'c1' } });

    await waitFor(() => expect(screen.getByRole('button', { name: 'New thread' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Send privately…' })).not.toBeInTheDocument();
  });

  it('is not offered without a person picker (no navigation service)', async () => {
    renderWorkspace({ navigationService: undefined });

    await screen.findByText('Acme Matter');
    expect(screen.queryByRole('button', { name: 'Send privately…' })).not.toBeInTheDocument();
  });

  it('runs picker -> startDirectThread -> send with privateContext, then selects the private entry', async () => {
    const { fetchMock, renderConversation } = renderWorkspace(
      {},
      makeFetch({ privateRows: [], afterSend: [PRIVATE_ROWS[0]] })
    );
    await screen.findByText('Acme Matter');

    fireEvent.click(screen.getByRole('button', { name: 'Send privately…' }));
    await screen.findByRole('dialog', { name: 'Send privately' });
    fireEvent.click(screen.getByRole('button', { name: 'Select To' }));
    await screen.findByText('Alice Archer');
    fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: 'About the filing' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    await waitFor(() => expect(lastRender(renderConversation)).toMatchObject({ threadId: 'p1', about: MATTER }));
    const posts = fetchMock.mock.calls.filter(c => (c[1] as RequestInit | undefined)?.method === 'POST');
    expect(String(posts[0][0])).toMatch(/\/threads\/direct$/);
    expect(JSON.parse((posts[0][1] as RequestInit).body as string)).toEqual({
      otherParticipantSystemUserId: 'alice-1',
    });
    expect(String(posts[1][0])).toMatch(/\/api\/communications\/send$/);
    expect(JSON.parse((posts[1][1] as RequestInit).body as string)).toMatchObject({
      communicationType: 'Message',
      threadId: 'p1',
      body: 'About the filing',
      privateContext: { entityType: 'sprk_matter', id: 'rec1' },
    });
    expect(lastRender(renderConversation).threadName).toBe('Private · you and Alice Archer');
  });
});

describe("ConversationWorkspace - the record's private entries (fm task 056b)", () => {
  it('asks for kind=direct about this record and lists one "Private · you and …" entry per counterpart after the record threads', async () => {
    const { fetchMock } = renderWorkspace();

    await screen.findByText('Private · you and Alice Archer');
    expect(screen.getByText('Private · you and Bob Baker')).toBeInTheDocument();
    expect(screen.getByText('Acme Matter')).toBeInTheDocument();
    const urls = privateListUrls(fetchMock);
    expect(urls).toHaveLength(1);
    const params = new URL(urls[0], 'https://bff.example.com').searchParams;
    expect(params.get('kind')).toBe('direct');
    expect(params.get('about')).toBe('sprk_matter:rec1');
    const names = screen.getAllByRole('listitem').map(li => li.textContent);
    expect(names.findIndex(t => t?.includes('Acme Matter'))).toBeLessThan(
      names.findIndex(t => t?.includes('Private · you and Alice Archer'))
    );
  });

  it('never mixes an entry into a record thread: the record row keeps its own thread, the entry its pair thread, and entries have no pin or delete', async () => {
    const { renderConversation } = renderWorkspace();
    await screen.findByText('Private · you and Alice Archer');

    await waitFor(() => expect(lastRender(renderConversation)).toMatchObject({ threadId: 't1', about: undefined }));
    expect(screen.queryByRole('button', { name: /Delete Private/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Pin Private/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete Acme Matter' })).toBeInTheDocument();
  });

  it('opening an entry selects its pair thread narrowed to the record; "Open full conversation" shows the same thread unnarrowed', async () => {
    const { renderConversation } = renderWorkspace();

    fireEvent.click(await screen.findByText('Private · you and Alice Archer'));

    await waitFor(() =>
      expect(lastRender(renderConversation)).toMatchObject({
        threadId: 'p1',
        threadName: 'Private · you and Alice Archer',
        about: MATTER,
      })
    );
    const full = lastRender(renderConversation).onOpenFullConversation;
    expect(full).toBeDefined();

    React.act(() => full?.());

    await waitFor(() => expect(lastRender(renderConversation)).toMatchObject({ threadId: 'p1', about: undefined }));
    expect(lastRender(renderConversation).onOpenFullConversation).toBeUndefined();
  });

  it('with a real ConversationView the thread read carries about=…, and the full-conversation link drops it', async () => {
    const fetchMock = makeFetch();
    render(
      <FluentProvider theme={webLightTheme}>
        <ConversationWorkspace
          authenticatedFetch={fetchMock}
          regarding={MATTER}
          navigationService={makeNav()}
          renderConversation={p => (
            <ConversationView
              threadId={p.threadId}
              authenticatedFetch={p.authenticatedFetch}
              currentUserSystemUserId="me"
              about={p.about}
              onOpenFullConversation={p.onOpenFullConversation}
              pollIntervalMs={60_000}
            />
          )}
        />
      </FluentProvider>
    );

    fireEvent.click(await screen.findByText('Private · you and Bob Baker'));
    const link = await screen.findByRole('button', { name: 'Open full conversation' });
    const reads = () =>
      (fetchMock as jest.Mock).mock.calls.map(c => String(c[0])).filter(u => /\/threads\/p2\/messages/.test(u));
    await waitFor(() => expect(reads().length).toBeGreaterThan(0));
    expect(
      reads().every(u => new URL(u, 'https://bff.example.com').searchParams.get('about') === 'sprk_matter:rec1')
    ).toBe(true);

    fireEvent.click(link);

    await waitFor(() => expect(reads().some(u => !u.includes('about='))).toBe(true));
    expect(screen.queryByRole('button', { name: 'Open full conversation' })).not.toBeInTheDocument();
  });

  it("a failed private list leaves the record's own threads untouched", async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderWorkspace({}, makeFetch({ privateListFails: true }));

    await screen.findByText('Acme Matter');
    await waitFor(() => expect(console.warn).toHaveBeenCalled());
    expect(screen.queryByText(/Private · you and/)).not.toBeInTheDocument();
    (console.warn as jest.Mock).mockRestore();
  });

  it('in all mode (no record) no private entries are asked for', async () => {
    const fetchMock = makeFetch();
    render(
      <FluentProvider theme={webLightTheme}>
        <ConversationWorkspace authenticatedFetch={fetchMock} navigationService={makeNav()} />
      </FluentProvider>
    );

    await waitFor(() => expect(privateListUrls(fetchMock as jest.Mock).length).toBeGreaterThan(0));
    expect(privateListUrls(fetchMock as jest.Mock).every(u => !u.includes('about='))).toBe(true);
  });
});
