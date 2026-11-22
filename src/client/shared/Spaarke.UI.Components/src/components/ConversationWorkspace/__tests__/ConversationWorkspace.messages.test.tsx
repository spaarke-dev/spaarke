/**
 * ConversationWorkspace.messages.test.tsx (fm task 055)
 *
 * The All / Messages switch of the all-mode shell: Messages asks the server for the caller's own Direct conversations
 * (`GET /api/communications/threads?kind=direct`), names each row by the other person, and its ＋ opens the
 * new-direct-message modal; All is unchanged and keeps the record-anchored modal; record mode shows no switch.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ConversationWorkspace } from '../ConversationWorkspace';
import type { ConversationWorkspaceProps, IConversationRendererProps } from '../ConversationWorkspace';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { INavigationService } from '../../../types/serviceInterfaces';

const ALL_THREADS = [
  { threadId: 't1', name: 'Acme Matter', threadType: 100000000, createdOn: '2026-11-20T12:00:00Z', isPinned: false },
  {
    threadId: 'p1',
    name: 'Direct Conversation',
    threadType: 100000001,
    createdOn: '2026-11-19T09:00:00Z',
    isPinned: false,
  },
];

const DIRECT_THREADS = [
  { ...ALL_THREADS[1], counterpartName: 'Alice Archer' },
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

function makeFetch(direct: unknown[] = DIRECT_THREADS) {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/by-regarding/')) {
      return jsonResponse({
        entityType: 'sprk_matter',
        recordId: 'rec1',
        threads: [{ threadId: 't1', name: 'Acme Matter' }],
        threadCount: 1,
      });
    }
    if (/\/threads\/[^/]+\/unread-count/.test(url)) {
      return jsonResponse({ unreadCount: 0 });
    }
    if (/\/api\/communications\/threads(\?|$)/.test(url)) {
      const kind = new URL(url, 'https://bff.example.com').searchParams.get('kind');
      const threads = kind === 'direct' ? direct : ALL_THREADS;
      return jsonResponse({ threads, count: threads.length, nextPageToken: null, hasMore: false });
    }
    return jsonResponse({ title: 'Not Found', status: 404 }, 404);
  });
}

function renderWorkspace(overrides: Partial<ConversationWorkspaceProps> = {}, fetchImpl = makeFetch()) {
  const renderConversation = jest.fn((p: IConversationRendererProps) => (
    <div>{`open:${p.threadId}:${p.threadName}`}</div>
  ));
  render(
    <FluentProvider theme={webLightTheme}>
      <ConversationWorkspace authenticatedFetch={fetchImpl} renderConversation={renderConversation} {...overrides} />
    </FluentProvider>
  );
  return { fetchMock: fetchImpl as jest.Mock, renderConversation };
}

const listUrls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.map(c => String(c[0])).filter(u => /\/api\/communications\/threads(\?|$)/.test(u));

const openMessages = () => fireEvent.click(screen.getByRole('tab', { name: 'Messages' }));

describe('ConversationWorkspace — Messages view (fm task 055)', () => {
  it('all mode shows the All / Messages switch on All, and lists with no kind', async () => {
    const { fetchMock } = renderWorkspace();

    await screen.findByText('Acme Matter');
    expect(screen.getByRole('tab', { name: 'All', selected: true })).toBeInTheDocument();
    expect(listUrls(fetchMock).every(u => !u.includes('kind='))).toBe(true);
  });

  it('record mode shows no switch', async () => {
    renderWorkspace({ regarding: { entityType: 'sprk_matter', id: 'rec1' } });

    await screen.findByText('Acme Matter');
    expect(screen.queryByRole('tab', { name: 'Messages' })).not.toBeInTheDocument();
  });

  it('Messages asks for kind=direct, names each row by the other person, and opens the first with that name', async () => {
    const { fetchMock, renderConversation } = renderWorkspace();
    await screen.findByText('Acme Matter');

    openMessages();

    await screen.findByText('Alice Archer');
    expect(screen.getByText('Bob Baker')).toBeInTheDocument();
    expect(screen.queryByText('Acme Matter')).not.toBeInTheDocument();
    expect(screen.queryByText('Direct Conversation')).not.toBeInTheDocument();
    expect(listUrls(fetchMock).some(u => u.includes('kind=direct'))).toBe(true);
    await waitFor(() =>
      expect(renderConversation).toHaveBeenLastCalledWith(
        expect.objectContaining({ threadId: 'p1', threadName: 'Alice Archer' })
      )
    );
  });

  it('switching back to All lists every thread again and opens its first row', async () => {
    const { renderConversation } = renderWorkspace();
    await screen.findByText('Acme Matter');
    openMessages();
    await screen.findByText('Bob Baker');
    fireEvent.click(screen.getByText('Bob Baker'));

    fireEvent.click(screen.getByRole('tab', { name: 'All' }));

    await screen.findByText('Acme Matter');
    await waitFor(() =>
      expect(renderConversation).toHaveBeenLastCalledWith(
        expect.objectContaining({ threadId: 't1', threadName: 'Acme Matter' })
      )
    );
  });

  it('an empty Messages list says so', async () => {
    renderWorkspace({}, makeFetch([]));
    await screen.findByText('Acme Matter');

    openMessages();

    expect(await screen.findByText('No messages yet.')).toBeInTheDocument();
  });

  it('the ＋ opens the new-direct-message modal in Messages and the record-anchored modal in All', async () => {
    const navigationService = { openLookup: jest.fn().mockResolvedValue([]) } as unknown as INavigationService;
    renderWorkspace({ navigationService });
    await screen.findByText('Acme Matter');

    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));
    expect(await screen.findByRole('dialog', { name: /New conversation/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    openMessages();
    await screen.findByText('Alice Archer');
    fireEvent.click(screen.getByRole('button', { name: 'New message' }));
    expect(await screen.findByRole('dialog', { name: /New message/i })).toBeInTheDocument();
  });
});

/** The list double above, plus the create endpoints answering and the first-message send failing (500). */
function makeSendFailingFetch() {
  const list = makeFetch();
  return throwingAuthenticatedFetch(async (url: string, init?: RequestInit) => {
    const post = (init?.method ?? 'GET').toUpperCase() === 'POST';
    if (post && url.endsWith('/api/communications/threads/direct')) {
      return jsonResponse({ threadId: 'pair-9', callerSystemUserId: 'me', otherParticipantSystemUserId: 'carol-1' });
    }
    if (post && url.endsWith('/api/communications/threads')) {
      return jsonResponse({ threadId: 'thread-new' });
    }
    if (url.includes('/api/communications/send')) {
      return jsonResponse({ title: 'Error', status: 500, detail: 'send failed' }, 500);
    }
    return (list as unknown as (u: string, i?: RequestInit) => Promise<Response>)(url, init);
  });
}

describe('ConversationWorkspace — create modals keep a send-failure warning (fm task 055, review F1)', () => {
  it('the new-direct-message modal stays open with its warning when the first message fails, and the pair is selected', async () => {
    const navigationService = {
      openLookup: jest.fn().mockResolvedValue([{ id: '{CAROL-1}', name: 'Carol Chen', entityType: 'systemuser' }]),
    } as unknown as INavigationService;
    const { renderConversation } = renderWorkspace({ navigationService }, makeSendFailingFetch());
    await screen.findByText('Acme Matter');
    openMessages();
    await screen.findByText('Alice Archer');

    fireEvent.click(screen.getByRole('button', { name: 'New message' }));
    await screen.findByRole('dialog', { name: /New message/i });
    fireEvent.click(screen.getByRole('button', { name: 'Select To' }));
    await screen.findByText('Carol Chen');
    fireEvent.change(await screen.findByRole('textbox', { name: 'Message' }), { target: { value: 'Hi Carol' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));

    expect(await screen.findByText(/couldn't be sent/i)).toBeInTheDocument();
    // A failed submit must hand focus back to the message box: otherwise Fluent's focus manager marks the open dialog
    // aria-hidden and the warning is invisible to assistive technology (the dialog is still mounted and on screen).
    expect(await screen.findByRole('dialog', { name: /New message/i })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Message' })).toHaveFocus());
    await waitFor(() =>
      expect(renderConversation).toHaveBeenLastCalledWith(expect.objectContaining({ threadId: 'pair-9' }))
    );
  });

  it('the record-anchored modal stays open with its warning when the first message fails, and the thread is selected', async () => {
    const navigationService = {
      openLookup: jest.fn().mockResolvedValue([{ id: '{REC-99}', name: 'Picked Matter', entityType: 'sprk_matter' }]),
    } as unknown as INavigationService;
    const { renderConversation } = renderWorkspace({ navigationService }, makeSendFailingFetch());
    await screen.findByText('Acme Matter');

    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));
    await screen.findByRole('dialog', { name: /New conversation/i });
    fireEvent.click(screen.getByRole('button', { name: /select record/i }));
    await waitFor(() => expect(navigationService.openLookup as jest.Mock).toHaveBeenCalled());
    fireEvent.change(await screen.findByRole('textbox', { name: 'Message' }), { target: { value: 'Hello there' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create' }));

    expect(await screen.findByText(/couldn't be sent — try again in the thread/i)).toBeInTheDocument();
    // A failed submit must hand focus back to the message box: otherwise Fluent's focus manager marks the open dialog
    // aria-hidden and the warning is invisible to assistive technology (the dialog is still mounted and on screen).
    expect(await screen.findByRole('dialog', { name: /New conversation/i })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Message' })).toHaveFocus());
    await waitFor(() =>
      expect(renderConversation).toHaveBeenLastCalledWith(expect.objectContaining({ threadId: 'thread-new' }))
    );
  });
});

describe('ConversationWorkspace — host create surface and the Messages view (fm task 055, review F5)', () => {
  it('with only a host onCreateThread, All enables the ＋ and calls the host; in Messages the ＋ is disabled', async () => {
    const onCreateThread = jest.fn();
    renderWorkspace({ onCreateThread });
    await screen.findByText('Acme Matter');

    fireEvent.click(screen.getByRole('button', { name: 'New thread' }));
    expect(onCreateThread).toHaveBeenCalledTimes(1);

    openMessages();
    await screen.findByText('Alice Archer');
    expect(screen.getByRole('button', { name: 'New message' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'New message' }));
    expect(onCreateThread).toHaveBeenCalledTimes(1);
  });

  it('with both, the Messages ＋ opens the direct modal and does not call the host', async () => {
    const onCreateThread = jest.fn();
    const navigationService = { openLookup: jest.fn().mockResolvedValue([]) } as unknown as INavigationService;
    renderWorkspace({ onCreateThread, navigationService });
    await screen.findByText('Acme Matter');
    openMessages();
    await screen.findByText('Alice Archer');

    fireEvent.click(screen.getByRole('button', { name: 'New message' }));

    expect(await screen.findByRole('dialog', { name: /New message/i })).toBeInTheDocument();
    expect(onCreateThread).not.toHaveBeenCalled();
  });
});
