/**
 * NewThreadModal.test.tsx (task 024 / R3 UAT 2026-07-23 item 9 redesign)
 *
 * Drives the redesigned create-thread modal end-to-end against a fake
 * `authenticatedFetch` + a fake `navigationService` (ADR-028/ADR-038 — no
 * `@spaarke/auth` import, no transport-level mock). The modal now creates a
 * NAMED, RECORD-ANCHORED thread (no participant): a `Name` field + an
 * "Associate to record" picker (`<AssociateToStep />`) + an OPTIONAL plain-text
 * first message. Covers:
 *   1. Create with a pre-seeded (locked) regarding → `POST /api/communications/threads`
 *      with { name?, regardingEntityType, regardingRecordId, regardingRecordName },
 *      and the returned thread id is handed to the selection callback.
 *   2. Pick a record via the AssociateToStep lookup → the picked record drives the create.
 *   3. Validation blocks submit with no record association.
 *   4. An optional first message is posted via the EXISTING send path with the regarding association.
 *   5. A create failure is surfaced inline (fatal); dialog stays open; no selection.
 *   6. A create SUCCESS whose first-message send FAILS still selects the thread (non-fatal).
 *   7. Dark-theme render (ADR-021) — no hand-authored inline colors.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { NewThreadModal, type INewThreadModalProps } from '../NewThreadModal';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';

const BFF = 'https://bff.example.com';

interface IFakeFetchOptions {
  threadId?: string;
  /** When set, the create endpoint responds non-OK with this ProblemDetails. */
  createError?: { status: number; detail: string };
  /** When set, the send endpoint responds non-OK (first-message failure). */
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

function isCreateUrl(url: string): boolean {
  return url.endsWith('/api/communications/threads');
}
function isSendUrl(url: string): boolean {
  return url.includes('/api/communications/send');
}

function makeFetch(opts: IFakeFetchOptions = {}) {
  const threadId = opts.threadId ?? 'thread-abc';
  return jest.fn((url: string) => {
    if (isCreateUrl(url)) {
      if (opts.createError) {
        return Promise.resolve(problem(opts.createError.status, opts.createError.detail, 'CREATE_THREAD_FAILED'));
      }
      return Promise.resolve({ ok: true, json: async () => ({ threadId }) } as Response);
    }
    if (isSendUrl(url)) {
      if (opts.sendError) {
        return Promise.resolve(problem(opts.sendError.status, opts.sendError.detail, 'SEND_FAILED'));
      }
      return Promise.resolve({ ok: true, json: async () => ({ communicationId: 'comm-1' }) } as Response);
    }
    return Promise.resolve({
      ok: false,
      status: 404,
      headers: { get: () => '' },
      json: async () => ({}),
    } as unknown as Response);
  });
}

/** Fake navigation service whose lookup resolves to a fixed record (or empty = cancel). */
function makeNav(picked?: LookupResult): INavigationService {
  return {
    openLookup: jest.fn().mockResolvedValue(picked ? [picked] : []),
  } as unknown as INavigationService;
}

function renderModal(
  overrides: Partial<INewThreadModalProps> = {},
  fetchImpl?: ReturnType<typeof makeFetch>,
  navigationService?: INavigationService,
  theme: typeof webLightTheme = webLightTheme
) {
  const authenticatedFetch = fetchImpl ?? makeFetch();
  const nav = navigationService ?? makeNav();
  const onThreadCreated = jest.fn();
  const onDismiss = jest.fn();
  const onError = jest.fn();

  const utils = render(
    <FluentProvider theme={theme}>
      <NewThreadModal
        open
        onDismiss={onDismiss}
        authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        navigationService={nav}
        onThreadCreated={onThreadCreated}
        onError={onError}
        {...overrides}
      />
    </FluentProvider>
  );

  return { ...utils, authenticatedFetch, nav, onThreadCreated, onDismiss, onError };
}

const MATTER = { entityType: 'sprk_matter', id: 'matter-1', name: 'Acme Matter' };
const clickCreate = () => fireEvent.click(screen.getByRole('button', { name: 'Create' }));

function createCalls(fetchMock: jest.Mock): unknown[][] {
  return fetchMock.mock.calls.filter(c => isCreateUrl(String(c[0])));
}
function sendCalls(fetchMock: jest.Mock): unknown[][] {
  return fetchMock.mock.calls.filter(c => isSendUrl(String(c[0])));
}

describe('NewThreadModal — create a record-anchored thread', () => {
  it('POSTs /api/communications/threads with the regarding + name and hands the returned id to the selection callback', async () => {
    const { authenticatedFetch, onThreadCreated, onDismiss } = renderModal({ regarding: MATTER });

    fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Discovery strategy' } });
    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));

    const create = createCalls(authenticatedFetch as jest.Mock);
    expect(create).toHaveLength(1);
    const [url, init] = create[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/threads`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toMatchObject({
      name: 'Discovery strategy',
      regardingEntityType: 'sprk_matter',
      regardingRecordId: 'matter-1',
      regardingRecordName: 'Acme Matter',
    });

    // No body → no first-message send.
    expect(sendCalls(authenticatedFetch as jest.Mock)).toHaveLength(0);
    expect(onDismiss).toHaveBeenCalledTimes(1);
  });

  it('picks a record via the AssociateToStep lookup and uses it to create the thread', async () => {
    const nav = makeNav({ id: '{REC-99}', name: 'Picked Matter', entityType: 'sprk_matter' });
    const { authenticatedFetch, onThreadCreated } = renderModal({}, undefined, nav);

    // Drive the record picker (default type = Matter → Select Record → resolves to the fixed record).
    fireEvent.click(screen.getByRole('button', { name: /select record/i }));
    await waitFor(() => expect(nav.openLookup as jest.Mock).toHaveBeenCalled());

    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));
    const [, init] = createCalls(authenticatedFetch as jest.Mock)[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toMatchObject({
      regardingEntityType: 'sprk_matter',
      regardingRecordId: 'rec-99', // cleaned + lowercased by AssociateToStep
      regardingRecordName: 'Picked Matter',
    });
  });
});

describe('NewThreadModal — validation', () => {
  it('blocks submit with no record association and does not call the endpoint', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal();

    clickCreate();

    expect(await screen.findByText(/choose a record to associate/i)).toBeInTheDocument();
    expect((authenticatedFetch as jest.Mock).mock.calls).toHaveLength(0);
    expect(onThreadCreated).not.toHaveBeenCalled();
  });
});

describe('NewThreadModal — optional first message via the existing send path', () => {
  it('when a plain-text body is authored, posts a thread Message with the regarding association', async () => {
    const { authenticatedFetch, onThreadCreated } = renderModal({ regarding: MATTER });

    fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: 'Hello there' } });
    clickCreate();

    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));

    const sends = sendCalls(authenticatedFetch as jest.Mock);
    expect(sends).toHaveLength(1);
    const [url, init] = sends[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/send`);
    expect(JSON.parse(init.body as string)).toMatchObject({
      communicationType: 'Message',
      threadId: 'thread-abc',
      body: 'Hello there',
      associations: [{ entityType: 'sprk_matter', entityId: 'matter-1' }],
    });
  });
});

describe('NewThreadModal — error handling', () => {
  it('surfaces a create failure inline, keeps the dialog open, and does not select a thread', async () => {
    const fetchImpl = makeFetch({ createError: { status: 400, detail: 'Regarding record is required.' } });
    const { onThreadCreated, onDismiss, onError } = renderModal({ regarding: MATTER }, fetchImpl);

    clickCreate();

    expect(await screen.findByText(/regarding record is required/i)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onThreadCreated).not.toHaveBeenCalled();
    expect(onDismiss).not.toHaveBeenCalled();
    await waitFor(() => expect(onError).toHaveBeenCalled());
  });

  it('a created thread is still selected when its first message fails to send (non-fatal)', async () => {
    const fetchImpl = makeFetch({ sendError: { status: 500, detail: 'Chat backend unavailable.' } });
    const { authenticatedFetch, onThreadCreated, onDismiss, onError } = renderModal({ regarding: MATTER }, fetchImpl);

    fireEvent.change(screen.getByRole('textbox', { name: 'Message' }), { target: { value: 'Hello there' } });
    clickCreate();

    // Thread was created → selected despite the send failure.
    await waitFor(() => expect(onThreadCreated).toHaveBeenCalledWith('thread-abc'));
    expect(createCalls(authenticatedFetch as jest.Mock)).toHaveLength(1);
    expect(sendCalls(authenticatedFetch as jest.Mock)).toHaveLength(1);
    // Non-fatal notice shown; dialog stays open (a send failure is not a create failure).
    expect(await screen.findByText(/couldn't be sent — try again in the thread/i)).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onDismiss).not.toHaveBeenCalled();
    await waitFor(() => expect(onError).toHaveBeenCalled());
  });
});

describe('NewThreadModal — Dark Mode Compliance (ADR-021)', () => {
  it('renders under a dark-theme FluentProvider without hand-authored inline colors', () => {
    const { container } = renderModal({ regarding: MATTER }, undefined, undefined, webDarkTheme);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    const styledEls = container.querySelectorAll<HTMLElement>('[style]');
    for (const el of Array.from(styledEls)) {
      expect(el.style.color).toBe('');
      expect(el.style.backgroundColor).toBe('');
    }
  });
});
