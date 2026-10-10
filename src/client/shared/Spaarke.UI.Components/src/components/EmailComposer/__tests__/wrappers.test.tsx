/**
 * wrappers.test.tsx (task 023, W2)
 *
 * The three semantic wrappers (task 021) are "thin by contract" (ADR-045): they
 * add no business logic, only lock the `mount` shape and map host callbacks.
 * These tests assert exactly that contract:
 *   - SendEmailStep  → mount='inline', forwards the composer handle, default mode 'compose'
 *   - SendEmailDialog → mount='dialog', open-gated Dialog chrome, Cancel → onClose
 *   - SendEmailPage  → mount='page', requires mode, Cancel → onCancel(onClose)
 *   - SendEmailPane  → mount='inline' with its own Send, no chrome, forwards sendMode/onSent/onError (task 096)
 */
import * as React from 'react';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SendEmailStep } from '../wrappers/SendEmailStep';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import { SendEmailPage } from '../wrappers/SendEmailPage';
import { SendEmailPane } from '../wrappers/SendEmailPane';
import type { IEmailComposerHandle } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const authenticatedFetch = jest.fn() as unknown as AuthenticatedFetchFn;
const BFF = 'https://bff.example.com';

const COMPOSER_ACTIONS = { name: 'Composer actions' } as const;

describe('SendEmailStep', () => {
  it('locks mount to inline and forwards the composer handle', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(<SendEmailStep ref={ref} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);

    expect(ref.current).not.toBeNull();
    expect(typeof ref.current!.send).toBe('function');
    const state = ref.current!.getState();
    expect(state.mount).toBe('inline');
    // Defaults mode to 'compose' when not supplied.
    expect(state.mode).toBe('compose');
  });

  it('renders no internal action bar (inline — the wizard frame owns navigation)', () => {
    renderWithProviders(<SendEmailStep authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);
    expect(screen.queryByRole('region', COMPOSER_ACTIONS)).toBeNull();
  });

  it('passes initial* props through to the engine', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(
      <SendEmailStep
        ref={ref}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        initialTo={['a@example.com']}
        initialSubject="Hi"
      />
    );
    const state = ref.current!.getState();
    expect(state.to.map(r => r.email)).toEqual(['a@example.com']);
    expect(state.subject).toBe('Hi');
  });
});

describe('SendEmailDialog', () => {
  it('renders the composer inside Dialog chrome (with action bar) when open', () => {
    renderWithProviders(
      <SendEmailDialog open onClose={jest.fn()} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    // modalType="alert" (item 12 — no light dismiss) renders role="alertdialog", not "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    // dialog mount renders the engine's own action bar.
    expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();
  });

  it('does not render composer content when closed', () => {
    renderWithProviders(
      <SendEmailDialog open={false} onClose={jest.fn()} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    expect(screen.queryByRole('region', COMPOSER_ACTIONS)).toBeNull();
  });

  it('maps the composer Cancel action to onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <SendEmailDialog open onClose={onClose} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('SendEmailPage', () => {
  it('locks mount to page and renders the required mode', () => {
    renderWithProviders(<SendEmailPage mode="compose" authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);
    // page mount renders the engine action bar + the compose header.
    expect(screen.getByRole('region', COMPOSER_ACTIONS)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'New Email' })).toBeInTheDocument();
  });

  it('maps onClose onto the engine Cancel action', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <SendEmailPage mode="compose" onClose={onClose} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />
    );
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // Task 103 (UAT R2 D3): the host-supplied `onSearchRecipients` seam must reach
  // the engine's To/Cc/Bcc RecipientField so typing looks up contacts. These two
  // tests assert the wrapper forwards the seam end-to-end (type → suggest → select
  // → resolved recipient) and that free-typed addresses still work when NO search
  // is supplied (autocomplete is additive, not mandatory).
  it('forwards onSearchRecipients to the To field — a directory match becomes a resolved recipient', async () => {
    const onSearchRecipients = jest
      .fn()
      .mockResolvedValue([{ id: 'contact-1', name: 'Fiona Contact (fiona@example.com)' }]);
    renderWithProviders(
      <SendEmailPage
        mode="compose"
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        onSearchRecipients={onSearchRecipients}
      />
    );

    const toInput = screen.getByRole('textbox', { name: 'To' }) as HTMLInputElement;
    fireEvent.change(toInput, { target: { value: 'fio' } });

    // Debounced search (300 ms) → suggestion listbox renders the contact.
    const option = await screen.findByRole('option', { name: /Fiona Contact/ }, { timeout: 2000 });
    expect(onSearchRecipients).toHaveBeenCalledWith('fio');

    fireEvent.click(option);

    // The resolved contact is added as a To recipient chip carrying the email.
    const toGroup = screen.getByRole('group', { name: 'To' });
    await waitFor(() => expect(within(toGroup).getByText(/fiona@example\.com/)).toBeInTheDocument());
  });

  it('still accepts a free-typed address when no onSearchRecipients is supplied', () => {
    renderWithProviders(<SendEmailPage mode="compose" authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);

    const toInput = screen.getByRole('textbox', { name: 'To' }) as HTMLInputElement;
    fireEvent.change(toInput, { target: { value: 'greg@example.com' } });
    fireEvent.keyDown(toInput, { key: 'Enter' });

    const toGroup = screen.getByRole('group', { name: 'To' });
    expect(within(toGroup).getByText('greg@example.com')).toBeInTheDocument();
  });
});

describe('SendEmailPane (spaarkeai-word-add-in-r1 task 096 — a chromeless side pane that owns its send)', () => {
  function jsonResponse(status: number, body: unknown): Response {
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: { get: () => 'application/json' },
      json: async () => body,
      text: async () => JSON.stringify(body),
    } as unknown as Response;
  }

  it('locks mount to inline, defaults mode to compose, and draws no chrome or action bar', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(<SendEmailPane ref={ref} authenticatedFetch={authenticatedFetch} bffBaseUrl={BFF} />);

    expect(ref.current!.getState().mount).toBe('inline');
    expect(ref.current!.getState().mode).toBe('compose');
    expect(screen.queryByRole('heading', { name: 'New Email' })).toBeNull();
    expect(screen.queryByRole('region', COMPOSER_ACTIONS)).toBeNull();
  });

  it('keeps the engine Send button and forwards a host-locked sendMode (no From switcher)', () => {
    const ref = React.createRef<IEmailComposerHandle>();
    renderWithProviders(
      <SendEmailPane
        ref={ref}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        sendMode="user"
        fromMailbox="me@example.com"
      />
    );

    const from = screen.getByRole('group', { name: 'From' });
    expect(within(from).getByRole('button', { name: /send/i })).toBeInTheDocument();
    expect(within(from).getByText('me@example.com')).toBeInTheDocument();
    expect(within(from).queryByRole('button', { name: 'From mailbox' })).toBeNull();
    expect(ref.current!.getState().sendMode).toBe('user');
  });

  it('forwards onSent with the communication id after a send', async () => {
    const fetchFn = jest.fn().mockResolvedValue(jsonResponse(200, { communicationId: 'c-1' }));
    const onSent = jest.fn();
    renderWithProviders(
      <SendEmailPane
        authenticatedFetch={fetchFn as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        initialTo={['a@example.com']}
        initialSubject="Hi"
        initialBody="<p>Body</p>"
        onSent={onSent}
      />
    );

    fireEvent.click(within(screen.getByRole('group', { name: 'From' })).getByRole('button', { name: /send/i }));

    await waitFor(() => expect(onSent).toHaveBeenCalledWith({ communicationId: 'c-1' }));
    expect(fetchFn).toHaveBeenCalledWith(`${BFF}/api/communications/send`, expect.objectContaining({ method: 'POST' }));
  });

  it('forwards onError with the server reason on a refused send', async () => {
    // Production shape: `@spaarke/auth`'s authenticatedFetch THROWS an ApiError for a non-2xx.
    const fetchFn = throwingAuthenticatedFetch(async () => jsonResponse(403, { status: 403, detail: 'Not allowed' }));
    const onError = jest.fn();
    renderWithProviders(
      <SendEmailPane
        authenticatedFetch={fetchFn as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        initialTo={['a@example.com']}
        initialSubject="Hi"
        initialBody="<p>Body</p>"
        onError={onError}
      />
    );

    fireEvent.click(within(screen.getByRole('group', { name: 'From' })).getByRole('button', { name: /send/i }));

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
    expect(onError.mock.calls[0][0]).toMatchObject({ status: 403, detail: 'Not allowed' });
  });

  it('control: a host whose fetch RETURNS the 403 still forwards onError with the server reason', async () => {
    const fetchFn = jest.fn().mockResolvedValue(jsonResponse(403, { detail: 'Not allowed' }));
    const onError = jest.fn();
    renderWithProviders(
      <SendEmailPane
        authenticatedFetch={fetchFn as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        initialTo={['a@example.com']}
        initialSubject="Hi"
        initialBody="<p>Body</p>"
        onError={onError}
      />
    );

    fireEvent.click(within(screen.getByRole('group', { name: 'From' })).getByRole('button', { name: /send/i }));

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
    expect(onError.mock.calls[0][0]).toMatchObject({ status: 403, detail: 'Not allowed' });
  });
});
