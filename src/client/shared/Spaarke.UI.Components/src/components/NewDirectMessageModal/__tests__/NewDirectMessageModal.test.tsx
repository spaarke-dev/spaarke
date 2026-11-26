/**
 * NewDirectMessageModal.test.tsx (fm task 055)
 *
 * Drives the new-direct-message modal against a fetch double in `@spaarke/auth`'s production shape (a non-2xx throws
 * ApiError) and a fake navigation service. Covers: the person picker asks the `systemuser` side pane with the internal-user
 * filter; Start calls find-or-create (`POST /threads/direct`) and hands back the thread and the person's name; an optional
 * first message goes through the existing send path with no record association; validation; the server's neutral refusal
 * shown inline; a send failure is non-fatal.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { NewDirectMessageModal, type INewDirectMessageModalProps } from '../NewDirectMessageModal';
import { INTERNAL_USER_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BFF = 'https://bff.example.com';
const NEUTRAL_400 = 'The other participant must be an active internal user.';

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

function makeFetch(opts: { startStatus?: number; sendFails?: boolean } = {}) {
  return throwingAuthenticatedFetch(async (url: string) => {
    if (isStartUrl(url)) {
      return opts.startStatus
        ? json({ title: 'Validation Error', status: opts.startStatus, detail: NEUTRAL_400 }, opts.startStatus)
        : json({ threadId: 'pair-1', callerSystemUserId: 'me', otherParticipantSystemUserId: 'alice-1' });
    }
    if (isSendUrl(url)) {
      return opts.sendFails
        ? json({ title: 'Error', status: 500, detail: 'send failed' }, 500)
        : json({ communicationId: 'comm-1' });
    }
    return json({ title: 'Not Found', status: 404 }, 404);
  });
}

function makeNav(picked?: LookupResult): INavigationService {
  return { openLookup: jest.fn().mockResolvedValue(picked ? [picked] : []) } as unknown as INavigationService;
}

const ALICE: LookupResult = { id: '{ALICE-1}', name: 'Alice Archer', entityType: 'systemuser' };

function renderModal(
  overrides: Partial<INewDirectMessageModalProps> = {},
  fetchImpl = makeFetch(),
  nav: INavigationService = makeNav(ALICE)
) {
  const onConversationStarted = jest.fn();
  const onDismiss = jest.fn();
  const onError = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <NewDirectMessageModal
        open
        onDismiss={onDismiss}
        authenticatedFetch={fetchImpl as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        navigationService={nav}
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

const clickStart = () => fireEvent.click(screen.getByRole('button', { name: 'Start' }));
const calls = (fetchMock: jest.Mock, match: (url: string) => boolean) =>
  fetchMock.mock.calls.filter(c => match(String(c[0])));

describe('NewDirectMessageModal', () => {
  it('asks the systemuser side pane with the internal-user filter', async () => {
    const { nav } = renderModal();

    await pickPerson(nav);

    expect(nav.openLookup).toHaveBeenCalledWith({ entityType: 'systemuser', filters: INTERNAL_USER_LOOKUP_FILTERS });
  });

  it('starts the pair conversation and hands back its thread and the person, with no send when there is no message', async () => {
    const { fetchMock, nav, onConversationStarted, onDismiss } = renderModal();

    await pickPerson(nav);
    clickStart();

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer'));
    const [url, init] = calls(fetchMock, isStartUrl)[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/threads/direct`);
    expect(JSON.parse(init.body as string)).toEqual({ otherParticipantSystemUserId: 'alice-1' });
    expect(calls(fetchMock, isSendUrl)).toHaveLength(0);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('posts a first message on the conversation through the send path, with no record association', async () => {
    const { fetchMock, nav, onConversationStarted } = renderModal();

    await pickPerson(nav);
    fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: '  Hi Alice  ' } });
    clickStart();

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer'));
    const sends = calls(fetchMock, isSendUrl);
    expect(sends).toHaveLength(1);
    const body = JSON.parse((sends[0] as [string, RequestInit])[1].body as string);
    expect(body).toMatchObject({ communicationType: 'Message', threadId: 'pair-1', body: 'Hi Alice' });
    expect(body.associations ?? []).toHaveLength(0);
  });

  it('blocks Start until a person is chosen', async () => {
    const { fetchMock, onConversationStarted } = renderModal();

    clickStart();

    expect(await screen.findByRole('alert')).toHaveTextContent('Choose the person to message.');
    expect(fetchMock).not.toHaveBeenCalled();
    expect(onConversationStarted).not.toHaveBeenCalled();
  });

  it("shows the server's refusal inline and stays open when the person cannot take part", async () => {
    const { nav, onConversationStarted, onDismiss, onError } = renderModal({}, makeFetch({ startStatus: 400 }));

    await pickPerson(nav);
    clickStart();

    expect(await screen.findByText(NEUTRAL_400)).toBeInTheDocument();
    expect(onConversationStarted).not.toHaveBeenCalled();
    expect(onDismiss).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledTimes(1);
  });

  it('still opens the conversation when the first message fails, and says so', async () => {
    const { nav, onConversationStarted, onDismiss } = renderModal({}, makeFetch({ sendFails: true }));

    await pickPerson(nav);
    fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: 'Hi' } });
    clickStart();

    await waitFor(() => expect(onConversationStarted).toHaveBeenCalledWith('pair-1', 'Alice Archer'));
    expect(await screen.findByText(/couldn't be sent/i)).toBeInTheDocument();
    expect(onDismiss).not.toHaveBeenCalled();
  });
});
