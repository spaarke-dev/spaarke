/**
 * ConversationView.private.test.tsx (fm task 056b)
 *
 * Private messages in the conversation view:
 *  - the "About {name}" chip renders only for a message with a non-null `privateContext` (null = no reference, or a record
 *    the viewer cannot read, resolved server-side);
 *  - `about` (a record's "Private · you and …" entry) narrows the thread read (`?about=type:id`), shows the narrowing notice
 *    with the "Open full conversation" link, sends the typed message privately about the same record and shows the owner's
 *    wording when the server refuses; without `about` none of that appears.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import type { ConversationViewProps } from '../ConversationView.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const ABOUT = { entityType: 'sprk_matter', id: 'matter-1' };

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000000,
    communicationType: 100000004,
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

function res(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/json' },
    json: async () => body,
  } as unknown as Response;
}

function makeFetch(messages: IThreadMessageDto[], sendRefusal?: { status: number; code: string }) {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (url.includes('/send')) {
      return sendRefusal
        ? res({ title: 'Refused', status: sendRefusal.status, code: sendRefusal.code }, sendRefusal.status)
        : res({ communicationId: 'new-1' });
    }
    if (url.includes('/unread-count')) return res({ threadId: 't1', since: null, unreadCount: 0 });
    return res({ threadId: 't1', name: null, messages, count: messages.length });
  });
}

function renderView(fetchMock: jest.Mock, overrides: Partial<ConversationViewProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ConversationView
        threadId="t1"
        currentUserSystemUserId={USER_1}
        authenticatedFetch={fetchMock as unknown as ConversationViewProps['authenticatedFetch']}
        pollIntervalMs={60_000}
        {...overrides}
      />
    </FluentProvider>
  );
}

const readUrls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.map(c => String(c[0])).filter(u => u.includes('/messages'));
const sendBodies = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls
    .filter(c => String(c[0]).includes('/send'))
    .map(c => JSON.parse(String((c[1] as RequestInit).body)) as Record<string, unknown>);

describe('ConversationView - the "About" chip (fm task 056b)', () => {
  it('shows "About {name}" on a message with a privateContext, and nothing on a null or absent one', async () => {
    const fetchMock = makeFetch([
      dto('with', {
        body: 'about the matter',
        privateContext: { entityType: 'sprk_matter', id: 'm1', name: 'Acme v. Roe' },
      }),
      dto('null-ctx', {
        body: 'no reference',
        createdOn: '2026-07-20T10:01:00Z',
        sentAt: '2026-07-20T10:01:00Z',
        privateContext: null,
      }),
      dto('absent', { body: 'old server', createdOn: '2026-07-20T10:02:00Z', sentAt: '2026-07-20T10:02:00Z' }),
    ]);

    renderView(fetchMock);

    await screen.findByText('about the matter');
    await screen.findByText('no reference');
    await screen.findByText('old server');
    const chips = screen.getAllByText(/^About /);
    expect(chips).toHaveLength(1);
    expect(chips[0]).toHaveTextContent('About Acme v. Roe');
  });
});

describe("ConversationView - a record's private entry (about) (fm task 056b)", () => {
  it('reads the thread narrowed to the record and says so, with a link to the full conversation', async () => {
    const onOpenFullConversation = jest.fn();
    const fetchMock = makeFetch([dto('m1', { body: 'only about the matter' })]);

    renderView(fetchMock, { about: ABOUT, onOpenFullConversation });

    await screen.findByText('only about the matter');
    const urls = readUrls(fetchMock);
    expect(urls.length).toBeGreaterThan(0);
    for (const u of urls) {
      expect(new URL(u, 'https://bff.example.com').searchParams.get('about')).toBe('sprk_matter:matter-1');
    }
    expect(screen.getByText(/only the private messages about this record/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Open full conversation' }));
    expect(onOpenFullConversation).toHaveBeenCalledTimes(1);
  });

  it('sends what is typed privately about the same record and hides the Internal Only tags', async () => {
    const user = userEvent.setup();
    const fetchMock = makeFetch([dto('m1')]);
    renderView(fetchMock, { about: ABOUT });
    await screen.findByText('body-m1');

    expect(screen.queryByText('Internal Only')).not.toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'A reply');
    await user.click(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(sendBodies(fetchMock)).toHaveLength(1));
    expect(sendBodies(fetchMock)[0]).toMatchObject({
      communicationType: 'Message',
      threadId: 't1',
      body: 'A reply',
      privateContext: { entityType: 'sprk_matter', id: 'matter-1' },
    });
    expect(sendBodies(fetchMock)[0].isInternalOnly).toBeUndefined();
  });

  it("shows the owner's wording when the server refuses the private reply", async () => {
    const user = userEvent.setup();
    const fetchMock = makeFetch([dto('m1')], { status: 403, code: 'sdap.communication.private.recipient_access' });
    renderView(fetchMock, { about: ABOUT });
    await screen.findByText('body-m1');

    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'A reply');
    await user.click(screen.getByRole('button', { name: 'Send message' }));

    expect(await screen.findByText('Recipient does not have required record access')).toBeInTheDocument();
  });

  it('without about: the whole thread is read, no notice, and a message goes out with no privateContext', async () => {
    const user = userEvent.setup();
    const fetchMock = makeFetch([dto('m1')]);
    renderView(fetchMock);
    await screen.findByText('body-m1');

    for (const u of readUrls(fetchMock)) {
      expect(new URL(u, 'https://bff.example.com').searchParams.has('about')).toBe(false);
    }
    expect(screen.queryByText(/only the private messages about this record/i)).not.toBeInTheDocument();
    expect(screen.getByText('Internal Only')).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Plain');
    await user.click(screen.getByRole('button', { name: 'Send message' }));

    await waitFor(() => expect(sendBodies(fetchMock)).toHaveLength(1));
    expect(sendBodies(fetchMock)[0].privateContext).toBeUndefined();
  });
});
