/**
 * NewThreadModal.test.tsx (task 024, FR-11)
 *
 * Drives the create-thread modal end-to-end against a fake `authenticatedFetch`
 * (ADR-028/ADR-038 — no `@spaarke/auth` import, no transport-level mock). Covers:
 *   1. Create a record-less thread → `POST /api/communications/threads/direct`
 *      (find-or-create) is called with the resolved participant's systemuserid,
 *      and the returned thread id is handed to the selection callback.
 *   2. Find-or-create returns an EXISTING thread → the server-returned id is used
 *      verbatim; exactly ONE POST to /threads/direct fires (no client-side
 *      duplicate-create path).
 *   3. Validation blocks submit with no participants / with a non-Spaarke-user
 *      free-text participant, with >1 resolved Spaarke user (M3), and with a
 *      regarding + empty body (M2).
 *   4. A create failure is surfaced inline (fatal); the dialog stays open; no
 *      selection.
 *   5. A create SUCCESS whose first-message send FAILS still selects the thread
 *      (M1, non-fatal) and surfaces a non-fatal notice.
 *   6. With a body (+ regarding), the first message is posted via the EXISTING
 *      send path (`communicationType: 'Message'`, thread-pinned, associations).
 *   7. Dark-theme render (ADR-021) — no hand-authored inline colors.
 *
 * Participants are added through the REUSED `RecipientField` directory-search
 * flow (type → resolve via `onSearchRecipients` → click the option), exactly as
 * a user would, so the test exercises the real resolved-systemuser mapping.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { NewThreadModal, type INewThreadModalProps } from '../NewThreadModal';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ILookupItem } from '../../../types/LookupTypes';

const BFF = 'https://bff.example.com';

const ADA: ILookupItem = { id: 'user-1', name: 'Ada Lovelace (ada@example.com)', entityType: 'systemuser' };
const GRACE: ILookupItem = { id: 'user-2', name: 'Grace Hopper (grace@example.com)', entityType: 'systemuser' };
const DIRECTORY: ILookupItem[] = [ADA, GRACE];

interface IFakeFetchOptions {
  /** Thread id the /threads/direct endpoint returns (find-or-create result). */
  threadId?: string;
  /** When set, /threads/direct responds non-OK with this ProblemDetails. */
  directError?: { status: number; detail: string };
  /** When set, /send responds non-OK with this ProblemDetails (first-message failure). */
  sendError?: { status: number; detail: string };
}

function problem(status: number, detail: string, code: string): Response {
  return {
    ok: false,
    status,
    headers: { get: () => 'application/problem+json' },
    json: async () => ({ title: 'Error', detail, errorCode: code }),
  } as unknown as Response;
}

function makeFetch(opts: IFakeFetchOptions = {}) {
  const threadId = opts.threadId ?? 'thread-abc';
  return jest.fn((url: string) => {
    if (url.includes('/threads/direct')) {
      if (opts.directError) {
        return Promise.resolve(problem(opts.directError.status, opts.directError.detail, 'DIRECT_THREAD_FAILED'));
      }
      return Promise.resolve({
        ok: true,
        json: async () => ({ threadId, callerSystemUserId: 'me', otherParticipantSystemUserId: 'user-1' }),
      } as Response);
    }
    if (url.includes('/api/communications/send')) {
      if (opts.sendError) {
        return Promise.resolve(problem(opts.sendError.status, opts.sendError.detail, 'SEND_FAILED'));
      }
      return Promise.resolve({ ok: true, json: async () => ({ communicationId: 'comm-1' }) } as Response);
    }
    return Promise.resolve({ ok: false, status: 404, headers: { get: () => '' }, json: async () => ({}) } as unknown as Response);
  });
}

function renderModal(
  overrides: Partial<INewThreadModalProps> = {},
  fetchImpl?: ReturnType<typeof makeFetch>,
  theme: typeof webLightTheme = webLightTheme
) {
  const authenticatedFetch = fetchImpl ?? makeFetch();
  const onThreadCreated = jest.fn();
  const onDismiss = jest.fn();
  const onError = jest.fn();
  const onSearchRecipients = jest.fn(async (q: string) =>
    DIRECTORY.filter(u => u.name.toLowerCase().includes(q.toLowerCase()))
  );

  const utils = render(
    <FluentProvider theme={theme}>
      <NewThreadModal
        open
        onDismiss={onDismiss}
        authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        onSearchRecipients={onSearchRecipients as unknown as (q: string) => Promise<ILookupItem[]>}
        onThreadCreated={onThreadCreated}
        onError={onError}
        {...overrides}
      />
    </FluentProvider>
  );

  return { ...utils, authenticatedFetch, onThreadCreated, onDismiss, onError, onSearchRecipients };
}

/** Adds a resolved Spaarke-user participant via the reused RecipientField search flow. */
async function addSpaarkeUser(query = 'Ada', chipText = 'Ada Lovelace (ada@example.com)') {
  const input = screen.getByRole('textbox', { name: 'Participant' });
  fireEvent.change(input, { target: { value: query } });
  const option = await screen.findByRole('option', { name: new RegExp(query, 'i') });
  fireEvent.click(option);
  await screen.findByText(chipText); // chip rendered
}

const clickCreate = () => fireEvent.click(screen.getByRole('button', { name: 'Create' }));

function directCalls(fetchMock: jest.Mock): unknown[][] {
  return fetchMock.mock.calls.filter(c => String(c[0]).includes('/threads/direct'));
}
function sendCalls(fetchMock: jest.Mock): unknown[][] {
  return fetchMock.mock.calls.filter(c => String(c[0]).includes('/api/communications/send'));
}

describe('NewThreadModal — create a record-less thread', () => {
  it('POSTs /threads/direct with the resolved participant and hands the returned thread id to the selection callback', async () => {
    const { authenticatedFetch, onThreadCreated, onDismiss } = renderModal();

    await addSpaarkeUser();
    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));

    const direct = directCalls(authenticatedFetch as jest.Mock);
    expect(direct).toHaveLength(1);
    const [url, init] = direct[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/threads/direct`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ otherParticipantSystemUserId: 'user-1' });

    // Record-less + no body → NO first-message send fired.
    expect(sendCalls(authenticatedFetch as jest.Mock)).toHaveLength(0);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });
});

describe('NewThreadModal — find-or-create returns an existing thread', () => {
  it('uses the server-returned thread id verbatim with exactly one create call (no client-side duplicate path)', async () => {
    const fetchImpl = makeFetch({ threadId: 'existing-thread-777' });
    const { authenticatedFetch, onThreadCreated } = renderModal({}, fetchImpl);

    await addSpaarkeUser();
    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('existing-thread-777'));
    expect(directCalls(authenticatedFetch as jest.Mock)).toHaveLength(1);
    expect(onThreadCreated).toHaveBeenCalledTimes(1);
  });
});

describe('NewThreadModal — validation', () => {
  it('blocks submit with no participants and does not call the endpoint', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal();

    clickCreate();

    expect(await screen.findByText(/add a participant/i)).toBeInTheDocument();
    expect((authenticatedFetch as jest.Mock).mock.calls).toHaveLength(0);
    expect(onThreadCreated).not.toHaveBeenCalled();
  });

  it('blocks submit when the participant is free-text (not a resolved Spaarke user the endpoint can consume)', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal();

    const input = screen.getByRole('textbox', { name: 'Participant' });
    fireEvent.change(input, { target: { value: 'bob@example.com' } });
    fireEvent.keyDown(input, { key: 'Enter' }); // commit free-text recipient (no systemuserid)
    await screen.findByText('bob@example.com'); // chip rendered

    clickCreate();

    expect(await screen.findByText(/select a spaarke user/i)).toBeInTheDocument();
    expect(directCalls(authenticatedFetch as jest.Mock)).toHaveLength(0);
    expect(onThreadCreated).not.toHaveBeenCalled();
  });

  it('M3: blocks submit when more than one resolved Spaarke user is present (Direct threads are 1:1)', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal();

    await addSpaarkeUser('Ada', 'Ada Lovelace (ada@example.com)');
    await addSpaarkeUser('Grace', 'Grace Hopper (grace@example.com)');
    clickCreate();

    expect(await screen.findByText(/1:1 — remove the extra participants/i)).toBeInTheDocument();
    expect(directCalls(authenticatedFetch as jest.Mock)).toHaveLength(0);
    expect(onThreadCreated).not.toHaveBeenCalled();
  });

  it('M2: blocks submit when a regarding is supplied but the first-message body is empty', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal({
      regarding: { entityType: 'sprk_matter', id: 'matter-1', name: 'Smith v. Jones' },
    });

    await addSpaarkeUser();
    clickCreate();

    expect(
      await screen.findByText(/add a first message to link this conversation to smith v\. jones/i)
    ).toBeInTheDocument();
    expect(directCalls(authenticatedFetch as jest.Mock)).toHaveLength(0);
    expect(onThreadCreated).not.toHaveBeenCalled();
  });
});

describe('NewThreadModal — error handling', () => {
  it('surfaces a create failure inline, keeps the dialog open, and does not select a thread', async () => {
    const fetchImpl = makeFetch({ directError: { status: 400, detail: 'Cannot start a direct thread with yourself.' } });
    const { onThreadCreated, onDismiss, onError } = renderModal({}, fetchImpl);

    await addSpaarkeUser();
    clickCreate();

    expect(await screen.findByText(/cannot start a direct thread with yourself/i)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onThreadCreated).not.toHaveBeenCalled();
    expect(onDismiss).not.toHaveBeenCalled();
    await waitFor(() => expect(onError).toHaveBeenCalled());
  });

  it('M1: a created thread is still selected when its first message fails to send (non-fatal)', async () => {
    const fetchImpl = makeFetch({ sendError: { status: 500, detail: 'Chat backend unavailable.' } });
    const { authenticatedFetch, onThreadCreated, onDismiss, onError } = renderModal(
      { regarding: { entityType: 'sprk_matter', id: 'matter-1', name: 'Smith v. Jones' } },
      fetchImpl
    );

    await addSpaarkeUser();
    fireEvent.change(screen.getByRole('textbox', { name: 'Message body' }), { target: { value: 'Hello there' } });
    clickCreate();

    // Thread was created → selected despite the send failure.
    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));
    // Both endpoints were hit (create + failed send), each once.
    expect(directCalls(authenticatedFetch as jest.Mock)).toHaveLength(1);
    expect(sendCalls(authenticatedFetch as jest.Mock)).toHaveLength(1);
    // Non-fatal notice shown; dialog stays open (a send failure is not a create failure).
    expect(await screen.findByText(/couldn't be sent — try again in the thread/i)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onDismiss).not.toHaveBeenCalled();
    await waitFor(() => expect(onError).toHaveBeenCalled());
  });
});

describe('NewThreadModal — first message via the existing send path (reused BodyEditor + regarding)', () => {
  it('when a body is authored, posts a thread-pinned Message with the regarding association', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal({
      regarding: { entityType: 'sprk_matter', id: 'matter-1', name: 'Smith v. Jones' },
    });

    await addSpaarkeUser();
    fireEvent.change(screen.getByRole('textbox', { name: 'Message body' }), {
      target: { value: 'Hello there' },
    });
    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));

    const sends = sendCalls(authenticatedFetch as jest.Mock);
    expect(sends).toHaveLength(1);
    const [url, init] = sends[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/send`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toMatchObject({
      communicationType: 'Message',
      threadId: 'thread-abc',
      body: 'Hello there',
      associations: [{ entityType: 'sprk_matter', entityId: 'matter-1' }],
    });
  });
});

describe('NewThreadModal — Dark Mode Compliance (ADR-021)', () => {
  it('renders under a dark-theme FluentProvider without hand-authored inline colors', () => {
    const { container } = renderModal({}, undefined, webDarkTheme);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    const styledEls = container.querySelectorAll<HTMLElement>('[style]');
    for (const el of Array.from(styledEls)) {
      expect(el.style.color).toBe('');
      expect(el.style.backgroundColor).toBe('');
    }
  });
});
