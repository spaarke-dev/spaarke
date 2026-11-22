/**
 * NewDirectMessageModal.private.test.tsx (fm task 056b)
 *
 * The modal in PRIVATE mode ("Send privately…" on a record): the same person picker with the record fixed as context and a
 * required first message. Submit is find-or-create of the pair thread, then the send with `privateContext`. Every refusal the
 * server can give is shown in the owner's wording, the dialog stays open and no second conversation is started on a retry; a
 * recipient refusal also offers "Send as a direct message instead" (same text, same pair thread, no record context).
 * The fetch double throws like `@spaarke/auth`'s production `authenticatedFetch`.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { NewDirectMessageModal, type INewDirectMessageModalProps } from '../NewDirectMessageModal';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BFF = 'https://bff.example.com';
const CONTEXT = { entityType: 'sprk_matter', id: 'matter-1' };

function json(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => 'application/problem+json' },
    json: async () => body,
  } as unknown as Response;
}

const isStartUrl = (url: string) => url.endsWith('/api/communications/threads/direct');
const isSendUrl = (url: string) => url.includes('/api/communications/send');

interface IRefusal {
  status: number;
  code?: string;
}

/** A send that refuses `refusals` in turn (one per attempt that carries `privateContext`), then succeeds. */
function makeFetch(refusals: IRefusal[] = []) {
  const queue = [...refusals];
  return throwingAuthenticatedFetch(async (url: string, init?: RequestInit) => {
    if (isStartUrl(url)) {
      return json({ threadId: 'pair-1', callerSystemUserId: 'me', otherParticipantSystemUserId: 'alice-1' });
    }
    if (isSendUrl(url)) {
      const body = JSON.parse(String(init?.body ?? '{}')) as { privateContext?: unknown };
      const refusal = body.privateContext ? queue.shift() : undefined;
      return refusal
        ? json(
            {
              title: 'Refused',
              status: refusal.status,
              detail: 'server detail',
              ...(refusal.code ? { code: refusal.code } : {}),
            },
            refusal.status
          )
        : json({ communicationId: 'comm-1' });
    }
    return json({ title: 'Not Found', status: 404 }, 404);
  });
}

function makeNav(): INavigationService {
  const alice: LookupResult = { id: '{ALICE-1}', name: 'Alice Archer', entityType: 'systemuser' };
  return { openLookup: jest.fn().mockResolvedValue([alice]) } as unknown as INavigationService;
}

function renderModal(overrides: Partial<INewDirectMessageModalProps> = {}, fetchImpl = makeFetch()) {
  const onConversationStarted = jest.fn();
  const onDismiss = jest.fn();
  const onError = jest.fn();
  const nav = makeNav();
  render(
    <FluentProvider theme={webLightTheme}>
      <NewDirectMessageModal
        open
        onDismiss={onDismiss}
        authenticatedFetch={fetchImpl as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        navigationService={nav}
        privateContext={CONTEXT}
        onConversationStarted={onConversationStarted}
        onError={onError}
        {...overrides}
      />
    </FluentProvider>
  );
  return { fetchMock: fetchImpl, nav, onConversationStarted, onDismiss, onError };
}

async function pickPerson(nav: INavigationService) {
  fireEvent.click(screen.getByRole('button', { name: 'Select To' }));
  await waitFor(() => expect(nav.openLookup as jest.Mock).toHaveBeenCalled());
  await screen.findByText('Alice Archer');
}

const typeMessage = (text: string) =>
  fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: text } });
const clickSend = () => fireEvent.click(screen.getByRole('button', { name: 'Send' }));
const calls = (fetchMock: jest.Mock, match: (url: string) => boolean) =>
  fetchMock.mock.calls.filter(c => match(String(c[0]))) as Array<[string, RequestInit]>;
const bodyOf = (call: [string, RequestInit]) => JSON.parse(call[1].body as string);

describe('NewDirectMessageModal - private mode (fm task 056b)', () => {
  it('titles the dialog for a private message about the record and names the record kind', () => {
    renderModal();

    expect(screen.getByRole('dialog', { name: 'Send privately' })).toBeInTheDocument();
    expect(screen.getByText(/About this matter/)).toBeInTheDocument();
  });

  it('finds the pair thread, then sends the message on it with the record as privateContext', async () => {
    const { fetchMock, nav, onConversationStarted, onDismiss } = renderModal();

    await pickPerson(nav);
    typeMessage('  The settlement figure  ');
    clickSend();

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer', true));
    const starts = calls(fetchMock, isStartUrl);
    expect(starts).toHaveLength(1);
    expect(bodyOf(starts[0])).toEqual({ otherParticipantSystemUserId: 'alice-1' });
    const sends = calls(fetchMock, isSendUrl);
    expect(sends).toHaveLength(1);
    expect(sends[0][0]).toBe(`${BFF}/api/communications/send`);
    expect(bodyOf(sends[0])).toMatchObject({
      communicationType: 'Message',
      threadId: 'pair-1',
      body: 'The settlement figure',
      bodyFormat: 'PlainText',
      to: [],
      privateContext: { entityType: 'sprk_matter', id: 'matter-1' },
    });
    expect(bodyOf(sends[0]).associations ?? []).toHaveLength(0);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('requires a message: nothing is sent and nothing is started without one', async () => {
    const { fetchMock, nav, onConversationStarted } = renderModal();

    await pickPerson(nav);
    clickSend();

    expect(await screen.findByRole('alert')).toHaveTextContent('Write the message to send.');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(onConversationStarted).not.toHaveBeenCalled();
  });

  it.each([
    [
      'recipient access',
      { status: 403, code: 'sdap.communication.private.recipient_access' },
      'Recipient does not have required record access',
    ],
    [
      'sender access',
      { status: 403, code: 'sdap.communication.private.sender_access' },
      'You do not have required record access',
    ],
    ['check unavailable', { status: 503, code: 'sdap.communication.private.check_unavailable' }, /try again/i],
    ['columns missing', { status: 409, code: 'sdap.communication.private.columns_missing' }, /not available yet/i],
    ['a 400', { status: 400 }, 'The private message could not be sent.'],
  ] as Array<[string, IRefusal, string | RegExp]>)(
    'shows the %s refusal, keeps the dialog open and hands nothing to the host',
    async (_name, refusal, expected) => {
      const { nav, onConversationStarted, onDismiss, onError } = renderModal({}, makeFetch([refusal]));

      await pickPerson(nav);
      typeMessage('Hello');
      clickSend();

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent(expected);
      expect(onDismiss).not.toHaveBeenCalled();
      expect(onConversationStarted).not.toHaveBeenCalled();
      expect(onError).toHaveBeenCalledTimes(1);
      expect(screen.getByRole('dialog', { name: 'Send privately' })).toBeInTheDocument();
      // The server's own words are never shown for the coded refusals.
      expect(screen.queryByText('server detail')).not.toBeInTheDocument();
    }
  );

  it('a retry after a refusal reuses the pair thread: no second conversation is started', async () => {
    const { fetchMock, nav, onConversationStarted } = renderModal(
      {},
      makeFetch([{ status: 403, code: 'sdap.communication.private.recipient_access' }])
    );

    await pickPerson(nav);
    typeMessage('Hello');
    clickSend();
    await screen.findByText('Recipient does not have required record access');

    clickSend();

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer', true));
    expect(calls(fetchMock, isStartUrl)).toHaveLength(1);
    expect(calls(fetchMock, isSendUrl)).toHaveLength(2);
  });

  it('a recipient refusal offers "Send as a direct message instead": same text, same pair thread, no record context', async () => {
    const { fetchMock, nav, onConversationStarted, onDismiss } = renderModal(
      {},
      makeFetch([{ status: 403, code: 'sdap.communication.private.recipient_access' }])
    );

    await pickPerson(nav);
    typeMessage('Hello Alice');
    clickSend();
    await screen.findByText('Recipient does not have required record access');

    fireEvent.click(screen.getByRole('button', { name: 'Send as a direct message instead' }));

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer', false));
    const sends = calls(fetchMock, isSendUrl);
    expect(sends).toHaveLength(2);
    expect(bodyOf(sends[1])).toMatchObject({ communicationType: 'Message', threadId: 'pair-1', body: 'Hello Alice' });
    expect(bodyOf(sends[1]).privateContext).toBeUndefined();
    expect(calls(fetchMock, isStartUrl)).toHaveLength(1);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('offers the direct-message fallback for the recipient refusal only', async () => {
    const { nav } = renderModal({}, makeFetch([{ status: 403, code: 'sdap.communication.private.sender_access' }]));

    await pickPerson(nav);
    typeMessage('Hello');
    clickSend();
    await screen.findByText('You do not have required record access');

    expect(screen.queryByRole('button', { name: 'Send as a direct message instead' })).not.toBeInTheDocument();
  });

  it('without privateContext the modal is the plain Direct flow: no privateContext on the send, no required message', async () => {
    const { fetchMock, nav, onConversationStarted } = renderModal({ privateContext: undefined });

    expect(screen.getByRole('dialog', { name: 'New message' })).toBeInTheDocument();
    await pickPerson(nav);
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer'));
    expect(calls(fetchMock, isSendUrl)).toHaveLength(0);
  });
});
