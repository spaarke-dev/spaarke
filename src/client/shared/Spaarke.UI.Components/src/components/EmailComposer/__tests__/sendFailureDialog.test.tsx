/**
 * sendFailureDialog.test.tsx — a failed send is never silent (owner decision 2026-10-09).
 *
 * Drives the REAL engine through its wrappers with the production-shaped fetch double
 * (`throwingAuthenticatedFetch`: `@spaarke/auth` THROWS ApiError/AuthError for a non-2xx) and asserts:
 *   - SendEmailPage (the Communication code page — no `onError`) shows a descriptive "Email not sent"
 *     dialog; the draft is untouched; OK closes it and returns focus to the composer.
 *   - A request that never reaches the server (fetch TypeError) → the dialog, and `onError` receives a
 *     SendCommunicationError (status 0, code NETWORK).
 *   - `sendFailureDisplay="host"` → `onError` only, no built-in dialog (the host shows its own UI).
 *   - No draft persistence wired → no Save Draft button; a wired-but-failing save → "Draft not saved" dialog.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SendEmailPage, type ISendEmailPageProps } from '../wrappers/SendEmailPage';
import { SendEmailDialog } from '../wrappers/SendEmailDialog';
import { SendCommunicationError } from '../../../services/communicationApi';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BFF = 'https://bff.example.com';
const SUBJECT = 'Documents for review';
const BODY = 'Please see the attached documents.';

function failingWith(status: number, body: Record<string, unknown> | null) {
  return throwingAuthenticatedFetch(
    async () => ({ ok: false, status, json: async () => body }) as unknown as Response
  ) as unknown as AuthenticatedFetchFn;
}

function renderPage(authenticatedFetch: AuthenticatedFetchFn, extra: Partial<ISendEmailPageProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SendEmailPage
        mode="compose"
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={BFF}
        initialTo={['alice@example.com']}
        initialSubject={SUBJECT}
        initialBody={BODY}
        {...extra}
      />
    </FluentProvider>
  );
}

function clickSend() {
  fireEvent.click(screen.getByRole('button', { name: /^send$/i }));
}

describe('SendEmailPage — a failed send shows a descriptive dialog (no onError on this wrapper)', () => {
  it('422 with a ProblemDetails detail → the server reason, what to do, the draft is kept, and the reference', async () => {
    const authenticatedFetch = failingWith(422, {
      title: 'One or more validation errors occurred.',
      status: 422,
      detail: 'The recipient "alice@example" is not a valid email address',
      errorCode: 'INVALID_RECIPIENT',
      correlationId: 'corr-7f3a',
    });
    renderPage(authenticatedFetch);
    clickSend();

    const dialog = await screen.findByRole('alertdialog', { name: 'Email not sent' });
    expect(
      within(dialog).getByText(
        'The recipient "alice@example" is not a valid email address. Correct it and send again. ' +
          'Your draft is still here — nothing was lost.'
      )
    ).toBeInTheDocument();
    expect(within(dialog).getByText('Reference: corr-7f3a')).toBeInTheDocument();
    // Never the raw status or JSON.
    expect(dialog.textContent).not.toMatch(/HTTP \d|[{}]/);

    // The draft is still there underneath.
    expect(screen.getByDisplayValue(SUBJECT)).toBeInTheDocument();
    expect(screen.getByText('alice@example.com')).toBeInTheDocument();

    // OK closes the dialog and returns to the composer, draft intact.
    fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog', { name: 'Email not sent' })).toBeNull());
    expect(screen.getByDisplayValue(SUBJECT)).toBeInTheDocument();
    const composer = screen.getByRole('region', { name: 'Email composer' });
    await waitFor(() => expect(composer.contains(document.activeElement)).toBe(true));
  });

  it('expired sign-in (AuthError) → the sign-in sentence', async () => {
    renderPage(failingWith(401, null));
    clickSend();
    const dialog = await screen.findByRole('alertdialog', { name: 'Email not sent' });
    expect(dialog.textContent).toContain('Your sign-in has expired. Refresh the page and sign in again, then resend.');
  });

  it('a 500 with no useful detail → "couldn\'t send this right now", never "HTTP 500"', async () => {
    renderPage(failingWith(500, null));
    clickSend();
    const dialog = await screen.findByRole('alertdialog', { name: 'Email not sent' });
    expect(dialog.textContent).toContain("The email service couldn't send this right now. Try again shortly.");
    expect(dialog.textContent).not.toMatch(/HTTP 500/);
  });

  it('a network failure (fetch TypeError) → "no response; check the connection", never the browser wording', async () => {
    const authenticatedFetch = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    renderPage(authenticatedFetch as unknown as AuthenticatedFetchFn);
    clickSend();
    const dialog = await screen.findByRole('alertdialog', { name: 'Email may not have been sent' });
    expect(dialog.textContent).toContain(
      "Couldn't get a response from the Spaarke server. Check your network connection."
    );
    expect(dialog.textContent).not.toContain('Failed to fetch');
    expect(screen.getByDisplayValue(SUBJECT)).toBeInTheDocument();
  });

  it('no draft persistence wired → no Save Draft button at all (owner decision 2026-10-09)', () => {
    renderPage(failingWith(500, null));
    expect(screen.queryByRole('button', { name: /save draft/i })).toBeNull();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('onSaveDraftRequest wired → Save Draft is present', () => {
    renderPage(failingWith(500, null), { onSaveDraftRequest: jest.fn() });
    expect(screen.getByRole('button', { name: /save draft/i })).toBeInTheDocument();
  });

  it('onSaveDraftRequest wired but failing → "Draft not saved" dialog', async () => {
    renderPage(failingWith(500, null), { onSaveDraftRequest: jest.fn().mockRejectedValue(new Error('boom')) });
    fireEvent.click(screen.getByRole('button', { name: /save draft/i }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Draft not saved' });
    expect(dialog.textContent).toContain("The draft couldn't be saved.");
  });
});

describe('SendEmailDialog — onError is a notification; sendFailureDisplay="host" hands the message to the host', () => {
  function renderDialog(props: { onError?: jest.Mock; sendFailureDisplay?: 'dialog' | 'host' }) {
    const authenticatedFetch = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={jest.fn()}
          authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
          bffBaseUrl={BFF}
          initialTo={['alice@example.com']}
          initialSubject={SUBJECT}
          initialBody={BODY}
          initialBodyFormat="PlainText"
          {...props}
        />
      </FluentProvider>
    );
  }

  it('network TypeError + onError → onError gets a SendCommunicationError (status 0, NETWORK) AND the dialog shows', async () => {
    const onError = jest.fn();
    renderDialog({ onError });
    clickSend();

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
    const err = onError.mock.calls[0][0];
    expect(err).toBeInstanceOf(SendCommunicationError);
    expect(err).toMatchObject({ status: 0, code: 'NETWORK' });
    expect(await screen.findByRole('alertdialog', { name: 'Email may not have been sent' })).toBeInTheDocument();
    // The composer dialog is still open underneath, draft intact.
    expect(screen.getByRole('alertdialog', { name: 'New Email' })).toBeInTheDocument();
    expect(screen.getByDisplayValue(BODY)).toBeInTheDocument();
  });

  it('sendFailureDisplay="host" + onError → onError only, no built-in dialog', async () => {
    const onError = jest.fn();
    renderDialog({ onError, sendFailureDisplay: 'host' });
    clickSend();

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
    // Give any (wrong) dialog a chance to render before asserting its absence.
    await new Promise(r => setTimeout(r, 0));
    expect(screen.queryByRole('alertdialog', { name: 'Email may not have been sent' })).toBeNull();
    expect(screen.getAllByRole('alertdialog')).toHaveLength(1); // only the composer itself
    expect(screen.getByRole('alertdialog', { name: 'New Email' })).toBeInTheDocument();
  });
});

describe('Save Draft always tells the user — sendFailureDisplay governs SEND only (review F2)', () => {
  it('sendFailureDisplay="host" → a failed Save Draft still shows "Draft not saved"', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={jest.fn()}
          authenticatedFetch={jest.fn() as unknown as AuthenticatedFetchFn}
          bffBaseUrl={BFF}
          initialTo={['alice@example.com']}
          initialSubject={SUBJECT}
          initialBody={BODY}
          initialBodyFormat="PlainText"
          onError={jest.fn()}
          sendFailureDisplay="host"
          onSaveDraftRequest={jest.fn().mockRejectedValue(new Error('boom'))}
        />
      </FluentProvider>
    );
    fireEvent.click(screen.getByRole('button', { name: /save draft/i }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Draft not saved' });
    expect(dialog.textContent).toContain("The draft couldn't be saved.");
  });
});

describe('a delivered email is never reported as failed (review K2)', () => {
  it('a host onSent that throws → no "Email not sent" dialog and no onError; the error is logged', async () => {
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const onError = jest.fn();
    const onSent = jest.fn(() => {
      throw new Error('host bookkeeping failed');
    });
    const authenticatedFetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ communicationId: 'comm-1' }),
    } as Response);
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={jest.fn()}
          authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
          bffBaseUrl={BFF}
          initialTo={['alice@example.com']}
          initialSubject={SUBJECT}
          initialBody={BODY}
          initialBodyFormat="PlainText"
          onSent={onSent}
          onError={onError}
        />
      </FluentProvider>
    );
    clickSend();

    await waitFor(() => expect(onSent).toHaveBeenCalledWith('comm-1'));
    await new Promise(r => setTimeout(r, 0));
    expect(onError).not.toHaveBeenCalled();
    expect(screen.queryByRole('alertdialog', { name: /not sent|may not have been sent/i })).toBeNull();
    expect(consoleError).toHaveBeenCalled();
    consoleError.mockRestore();
  });
});
