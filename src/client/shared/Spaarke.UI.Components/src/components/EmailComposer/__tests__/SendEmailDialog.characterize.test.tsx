/**
 * SendEmailDialog.characterize.test.tsx (task 010, NFR-08)
 *
 * Characterization tests for `<SendEmailDialog/>`'s SEND-PATH contract — the
 * genuine coverage gap `wrappers.test.tsx` (task 023) leaves: that suite
 * covers render/open-close/"Cancel maps to onClose" (the task 021 "thin
 * wrapper" contract) but never drives an actual send. This file:
 *
 *   1. Captures the CURRENT prop contract (`ISendEmailDialogProps`) so task
 *      020 (R3 thread-id + record-link extension) can extend it additively
 *      without silently breaking an untested prop.
 *   2. Drives a real Send click and asserts the exact request payload
 *      `sendCommunication()` (via the injected `authenticatedFetch`) builds
 *      from the composer state.
 *   3. Asserts the engine's `onSent(result)` maps to
 *      `props.onSent?.(communicationId)` THEN `onClose()` — untested today.
 *   4. Dark-theme render (ADR-021) — the wrapper renders under a
 *      `webDarkTheme` FluentProvider without hand-authored inline colors.
 *
 * Uses PlainText body format (`initialBodyFormat: 'PlainText'`) so the body
 * editor renders a plain `<Textarea>` rather than the Lexical rich-text
 * editor (`BodyEditor.tsx`) — avoids depending on Lexical's HTML
 * parse/normalize timing for a payload-shape assertion that is not about
 * rich-text behavior.
 *
 * Injected fake `authenticatedFetch` per ADR-028/ADR-038 — no
 * `@spaarke/auth` import, no `Mock<HttpMessageHandler>`-style transport mock.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog, type ISendEmailDialogProps } from '../wrappers/SendEmailDialog';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';

const BFF = 'https://bff.example.com';

function renderDialog(overrides: Partial<ISendEmailDialogProps> = {}, theme: typeof webLightTheme = webLightTheme) {
  const authenticatedFetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ communicationId: 'comm-123' }),
  } as Response);
  const onClose = jest.fn();

  const utils = render(
    <FluentProvider theme={theme}>
      <SendEmailDialog
        open
        onClose={onClose}
        authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
        bffBaseUrl={BFF}
        initialTo={['alice@example.com']}
        initialSubject="Documents for review"
        initialBody="Please see attached."
        initialBodyFormat="PlainText"
        {...overrides}
      />
    </FluentProvider>
  );

  return { ...utils, authenticatedFetch, onClose };
}

describe('SendEmailDialog — renders composer (baseline)', () => {
  it('renders the dialog with the composer recipient + subject fields populated from initial* props', () => {
    renderDialog();
    // modalType="alert" (item 12 — no light dismiss) renders role="alertdialog", not "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument();
    expect(screen.getByText('alice@example.com')).toBeInTheDocument(); // RecipientField chip text
    expect(screen.getByDisplayValue('Documents for review')).toBeInTheDocument();
  });
});

describe('SendEmailDialog — send-path invocation (NOT covered by wrappers.test.tsx)', () => {
  it('clicking Send calls authenticatedFetch with the expected sendCommunication() payload shape', async () => {
    const { authenticatedFetch } = renderDialog();

    fireEvent.click(screen.getByRole('button', { name: /send/i }));

    await waitFor(() => expect(authenticatedFetch).toHaveBeenCalledTimes(1));

    const [url, init] = authenticatedFetch.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BFF}/api/communications/send`);
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json');

    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({
      to: ['alice@example.com'],
      subject: 'Documents for review',
      body: 'Please see attached.',
      bodyFormat: 'PlainText',
      communicationType: 'Email',
      sendMode: 'SharedMailbox',
      // EmailComposer's own default (EmailComposer.reducer.ts `initialState`: `props.archiveToSpe ?? true`) is
      // TRUE — this differs from the lower-level `sendCommunication()`/BFF wire default of `false`
      // (`communicationApi.ts`: `opts.archiveToSpe ?? false`), which only applies when a caller bypasses the
      // composer engine's own state. Pinned here as current behavior; see task-010-notes.md.
      archiveToSpe: true,
    });
  });

  it('onSent maps the engine result to props.onSent(communicationId) THEN onClose() — untested by wrappers.test.tsx', async () => {
    const onSent = jest.fn();
    const { onClose } = renderDialog({ onSent });

    fireEvent.click(screen.getByRole('button', { name: /send/i }));

    await waitFor(() => expect(onSent).toHaveBeenCalledWith('comm-123'));
    expect(onClose).toHaveBeenCalledTimes(1);
    // onSent fires before onClose (host reads the communicationId before the dialog unmounts).
    const sentOrder = onSent.mock.invocationCallOrder[0];
    const closeOrder = onClose.mock.invocationCallOrder[0];
    expect(sentOrder).toBeLessThan(closeOrder);
  });

  it('onError is invoked (not onSent/onClose) when the send fails — dialog stays open', async () => {
    const authenticatedFetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 400,
      headers: { get: () => 'application/problem+json' },
      json: async () => ({ title: 'Bad request', detail: 'Invalid recipient', errorCode: 'INVALID_RECIPIENT' }),
    } as unknown as Response);
    const onError = jest.fn();
    const onClose = jest.fn();
    const onSent = jest.fn();

    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailDialog
          open
          onClose={onClose}
          authenticatedFetch={authenticatedFetch as unknown as AuthenticatedFetchFn}
          bffBaseUrl={BFF}
          initialTo={['alice@example.com']}
          initialSubject="Subject"
          initialBody="Body"
          initialBodyFormat="PlainText"
          onSent={onSent}
          onError={onError}
        />
      </FluentProvider>
    );

    fireEvent.click(screen.getByRole('button', { name: /send/i }));

    await waitFor(() => expect(onError).toHaveBeenCalled());
    expect(onSent).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    // modalType="alert" (item 12 — no light dismiss) renders role="alertdialog", not "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});

describe('SendEmailDialog — prop contract (task 020 extends additively)', () => {
  it('accepts and forwards associations + attachmentSources + onSearchRecipients without changing the compose-mode render shape', () => {
    const onSearchRecipients = jest.fn().mockResolvedValue([]);
    renderDialog({
      associations: [{ entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'Smith v. Jones' }],
      attachmentSources: [],
      onSearchRecipients,
    });
    // modalType="alert" (item 12 — no light dismiss) renders role="alertdialog", not "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Composer actions' })).toBeInTheDocument();
  });

  it('defaults mode to "compose" when mode is omitted (Send + Save Draft + Cancel, not Close/Reply/Forward)', () => {
    renderDialog();
    expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save Draft' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull();
  });
});

describe('SendEmailDialog — Dark Mode Compliance (ADR-021)', () => {
  it('renders under a dark-theme FluentProvider without hand-authored inline colors', () => {
    const { container } = renderDialog({}, webDarkTheme);
    // modalType="alert" (item 12 — no light dismiss) renders role="alertdialog", not "dialog".
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    // Fluent tokens apply via CSS classes (Griffel/makeStyles), not inline styles —
    // pin the absence of hardcoded inline color/background-color per ADR-021.
    const styledEls = container.querySelectorAll<HTMLElement>('[style]');
    for (const el of Array.from(styledEls)) {
      expect(el.style.color).toBe('');
      expect(el.style.backgroundColor).toBe('');
    }
  });
});
