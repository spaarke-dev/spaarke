/**
 * SendEmailFollowOnStep.composer.test.tsx - spaarke-ontology-platform-r1 task 182 (owner D-219; UAT round 4 F-R4-3).
 *
 * The create wizards' Send Email step is the shared EmailComposer, compose-only, with the Send Email dialog's features: From
 * (My mailbox / Spaarke shared mailbox), To, Cc, Bcc, attachments, the template picker and AI draft, and no Send button. The
 * wizard sends at Finish through `sendComposedEmail` (the composer's request path) with the created record as association.
 *
 * Covers: what the step renders; no network call while composing; Back then Next restores every field; a host change while
 * the step is open (the Summarize Files toggle) reaches the composer; the Finish request (From, To, Cc, Bcc, attachments,
 * association, plain-text line breaks); a failed send is a warning, not a throw; Bcc is never dropped; the moved
 * `toPlainText` is the one the decision wizard uses.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { SendEmailFollowOnStep } from '../SendEmailFollowOnStep';
import { EMPTY_COMPOSED_EMAIL, isComposedEmailReady, sendComposedEmail } from '../../../EmailComposer/composeOnlySend';
import type { IComposedEmail, IEmailComposeHandlers } from '../../../EmailComposer/composeOnlySend';
import { toPlainText } from '../../../EmailComposer/composeOnlyText';
import type { AuthenticatedFetchFn } from '../../../../services/EntityCreationService';

const COMPOSED: IComposedEmail = {
  ...EMPTY_COMPOSED_EMAIL,
  to: [
    { email: 'client@acme.example', resolved: false },
    { email: 'cfo@acme.example', resolved: false },
  ],
  cc: [{ email: 'partner@firm.example', resolved: false }],
  bcc: [{ email: 'records@firm.example', resolved: false }],
  subject: 'New project: Acme',
  body: 'Dear Client,\n\nThe project is set up.\n\nKind regards',
  bodyFormat: 'PlainText',
  attachments: [{ id: 'doc:d-1', source: 'related', fileName: 'brief.pdf', sizeBytes: 10, documentId: 'd-1' }],
  sendMode: 'user',
};

function handlers(): Required<IEmailComposeHandlers> {
  return {
    onLookupRecipients: jest.fn().mockResolvedValue(null),
    recordLookupCatalog: [{ logicalName: 'sprk_document', displayName: 'Document' }],
    onLookupRecord: jest.fn().mockResolvedValue(null),
    onUploadLocalAttachment: jest.fn().mockResolvedValue({ documentId: 'd-up' }),
    onResolveShareLink: jest.fn().mockResolvedValue(null),
    onListEmailTemplates: jest.fn().mockResolvedValue([{ id: 't-1', name: 'Welcome Letter' }]),
    onRenderEmailTemplate: jest.fn().mockResolvedValue({ subject: 'S', body: 'B', isHtml: false }),
    onDraftWithAi: jest.fn().mockResolvedValue({ text: 'drafted' }),
  };
}

/** A wizard-like host: owns the composed email; `show` stands in for the wizard shell mounting the step (Back / Next). */
const Host: React.FC<{
  initial: IComposedEmail;
  composeHandlers?: IEmailComposeHandlers;
  onValue?: (v: IComposedEmail) => void;
  hostRef?: React.MutableRefObject<{ setShow: (s: boolean) => void; setValue: (v: IComposedEmail) => void } | null>;
  headerContent?: React.ReactNode;
}> = ({ initial, composeHandlers, onValue, hostRef, headerContent }) => {
  const [value, setValue] = React.useState(initial);
  const [show, setShow] = React.useState(true);
  if (hostRef) hostRef.current = { setShow, setValue };
  return show ? (
    <SendEmailFollowOnStep
      value={value}
      onChange={next => {
        setValue(next);
        onValue?.(next);
      }}
      composeHandlers={composeHandlers}
      headerContent={headerContent}
    />
  ) : (
    <div>other step</div>
  );
};

/** Text of the composer's rich-text body (the step opens in rich text, where the template, AI and attach tools are). */
const bodyText = (): string =>
  document.querySelector('[data-testid="send-email-followon-step"] [contenteditable="true"]')?.textContent ?? '';

describe('SendEmailFollowOnStep: the shared EmailComposer, compose-only (task 182)', () => {
  let fetchSpy: jest.SpyInstance;
  beforeEach(() => {
    // jsdom may lack a global fetch; give it one so "no network call" is observable.
    if (!('fetch' in globalThis))
      (globalThis as unknown as { fetch: unknown }).fetch = () => Promise.reject(new Error('x'));
    fetchSpy = jest.spyOn(globalThis, 'fetch');
  });
  afterEach(() => fetchSpy.mockRestore());

  it('renders From (both mailboxes), To, Cc, Bcc, attachments, template picker and AI draft, and no Send button', async () => {
    const h = handlers();
    renderWithProviders(<Host initial={COMPOSED} composeHandlers={h} />);

    const from = screen.getByRole('group', { name: 'From' });
    expect(within(from).getByText('From:')).toBeInTheDocument();
    fireEvent.click(within(from).getByRole('button', { name: 'From mailbox' }));
    expect(await screen.findByRole('menuitemradio', { name: 'My mailbox' })).toBeInTheDocument();
    expect(screen.getByRole('menuitemradio', { name: 'Spaarke shared mailbox' })).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /^send/i })).toBeNull();
    expect(screen.getByRole('group', { name: 'To' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Cc' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Bcc' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Attach' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /apply template/i })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Draft with AI' }).length).toBeGreaterThan(0);
  });

  it('says when and how the email is sent, never "draft"', () => {
    renderWithProviders(<Host initial={COMPOSED} />);
    const step = screen.getByTestId('send-email-followon-step');
    const heading = step.firstElementChild as HTMLElement;
    expect(heading).toHaveTextContent(/sent when you finish/i);
    expect(heading.textContent).not.toMatch(/draft/i);
  });

  it('makes no network call while composing: no fetch and no host handler runs until the user invokes it', async () => {
    const h = handlers();
    const onValue = jest.fn();
    renderWithProviders(<Host initial={COMPOSED} composeHandlers={h} onValue={onValue} />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Changed' } });
    await waitFor(() => expect(onValue).toHaveBeenCalled());

    expect(fetchSpy).not.toHaveBeenCalled();
    for (const fn of Object.values(h)) {
      if (typeof fn === 'function') expect(fn).not.toHaveBeenCalled();
    }

    // Invoked by the user: the template list is the host handler, still no direct fetch.
    fireEvent.click(screen.getByRole('button', { name: /apply template/i }));
    await waitFor(() => expect(h.onListEmailTemplates).toHaveBeenCalledTimes(1));
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('mounting over the starting value lifts nothing', async () => {
    const onValue = jest.fn();
    renderWithProviders(<Host initial={COMPOSED} onValue={onValue} />);
    await waitFor(() => expect(bodyText()).toContain('Kind regards')); // the rich-text editor has loaded the pre-fill
    await act(async () => Promise.resolve());
    expect(onValue).not.toHaveBeenCalled();
  });

  it('opens a plain-text pre-fill in rich text and keeps it plain text while only other fields change', async () => {
    const onValue = jest.fn();
    renderWithProviders(<Host initial={COMPOSED} onValue={onValue} />);
    await waitFor(() => expect(bodyText()).toContain('The project is set up.'));

    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Edited subject' } });
    await waitFor(() => expect(onValue).toHaveBeenCalled());
    const last = onValue.mock.calls[onValue.mock.calls.length - 1][0] as IComposedEmail;
    // The pre-filled body is untouched, so it stays the wizard's plain text (the Work Assignment wizard still refreshes it).
    expect(last.bodyFormat).toBe('PlainText');
    expect(last.body).toBe(COMPOSED.body);
    expect(last.subject).toBe('Edited subject');
  });

  it('the real editor collapsing repeated, leading or trailing spaces and tabs in a pre-fill is not an edit', async () => {
    const spaced: IComposedEmail = {
      ...COMPOSED,
      body: ['Name:  Acme   Corp', '  - item', 'a\ttab', 'Hello ', 'World'].join('\n'),
    };
    const onValue = jest.fn();
    renderWithProviders(<Host initial={spaced} onValue={onValue} />);
    await waitFor(() => expect(bodyText()).toContain('World'));
    await act(async () => Promise.resolve());
    expect(onValue).not.toHaveBeenCalled();

    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Edited subject' } });
    await waitFor(() => expect(onValue).toHaveBeenCalled());
    const last = onValue.mock.calls[onValue.mock.calls.length - 1][0] as IComposedEmail;
    expect(last.bodyFormat).toBe('PlainText');
    expect(last.body).toBe(spaced.body);
  });

  it('lifts the whole email, including the From choice, Bcc and attachments', async () => {
    const onValue = jest.fn();
    renderWithProviders(<Host initial={{ ...COMPOSED, sendMode: 'sharedMailbox' }} onValue={onValue} />);

    fireEvent.click(screen.getByRole('button', { name: 'From mailbox' }));
    fireEvent.click(await screen.findByRole('menuitemradio', { name: 'My mailbox' }));

    await waitFor(() => expect(onValue).toHaveBeenCalled());
    const last = onValue.mock.calls[onValue.mock.calls.length - 1][0] as IComposedEmail;
    expect(last.sendMode).toBe('user');
    expect(last.bcc.map(r => r.email)).toEqual(['records@firm.example']);
    expect(last.cc.map(r => r.email)).toEqual(['partner@firm.example']);
    expect(last.attachments).toEqual([expect.objectContaining({ documentId: 'd-1', selected: true })]);
  });

  it('Back then Next restores From, To, Cc, Bcc, subject, body and attachments', async () => {
    const hostRef: React.MutableRefObject<{
      setShow: (s: boolean) => void;
      setValue: (v: IComposedEmail) => void;
    } | null> = { current: null };
    const onValue = jest.fn();
    renderWithProviders(<Host initial={COMPOSED} hostRef={hostRef} onValue={onValue} />);

    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Edited subject' } });
    await waitFor(() => expect(onValue).toHaveBeenCalled());

    act(() => hostRef.current!.setShow(false)); // Next: the step unmounts
    expect(screen.queryByRole('textbox', { name: 'Subject' })).toBeNull();
    act(() => hostRef.current!.setShow(true)); // Back: it mounts again from the wizard's value

    expect(screen.getByRole('textbox', { name: 'Subject' })).toHaveValue('Edited subject');
    await waitFor(() => expect(bodyText()).toContain('The project is set up.'));
    expect(screen.getByRole('button', { name: 'From mailbox' })).toHaveTextContent('My mailbox');
    for (const address of ['client@acme.example', 'cfo@acme.example', 'partner@firm.example', 'records@firm.example']) {
      expect(screen.getByText(address)).toBeInTheDocument();
    }

    // The restored composer still holds the attachment, kept: the next change lifts it unchanged.
    onValue.mockClear();
    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Again' } });
    await waitFor(() => expect(onValue).toHaveBeenCalled());
    const last = onValue.mock.calls[onValue.mock.calls.length - 1][0] as IComposedEmail;
    expect(last.attachments).toEqual([expect.objectContaining({ documentId: 'd-1', selected: true })]);
    expect(last.bcc.map(r => r.email)).toEqual(['records@firm.example']);
  });

  it('a host change while the step is open reaches the composer (the Summarize Files toggle rewrites the body)', async () => {
    const hostRef: React.MutableRefObject<{
      setShow: (s: boolean) => void;
      setValue: (v: IComposedEmail) => void;
    } | null> = { current: null };
    renderWithProviders(
      <Host
        initial={COMPOSED}
        hostRef={hostRef}
        headerContent={<input type="checkbox" aria-label="Include only short summary" />}
      />
    );
    expect(screen.getByRole('checkbox', { name: 'Include only short summary' })).toBeInTheDocument();

    act(() => hostRef.current!.setValue({ ...COMPOSED, body: 'Short summary only' }));
    await waitFor(() => expect(bodyText()).toBe('Short summary only'));
  });

  it('shows a pending upload and isComposedEmailReady keeps Next off until it is uploaded', () => {
    const pending: IComposedEmail = {
      ...COMPOSED,
      attachments: [{ id: 'local:x', source: 'local', fileName: 'notes.docx', sizeBytes: 5 }],
    };
    renderWithProviders(<Host initial={pending} />);
    expect(screen.getByTestId('attachments-pending')).toHaveTextContent('notes.docx');
    expect(isComposedEmailReady(pending)).toBe(false);
    expect(isComposedEmailReady(COMPOSED)).toBe(true);
    expect(isComposedEmailReady({ ...COMPOSED, bcc: [{ email: 'not-an-address' }] })).toBe(false);
    expect(isComposedEmailReady({ ...COMPOSED, bodyFormat: 'HTML', body: '<p></p>' })).toBe(false);
  });
});

describe('sendComposedEmail: the Finish send through the composer request path (task 182)', () => {
  const okFetch = () =>
    jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: () => 'application/json' },
      json: async () => ({ communicationId: 'comm-1' }),
    });
  const bodyOf = (fetch: jest.Mock) => JSON.parse(fetch.mock.calls[0][1].body as string) as Record<string, unknown>;
  const association = [{ entityType: 'sprk_project', entityId: 'p-1', entityName: 'Acme' }];

  it('posts From, To, Cc, Bcc, attachments and the created record, with plain-text line breaks intact', async () => {
    const fetch = okFetch();
    const result = await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
      bffBaseUrl: 'https://bff.example',
    });

    expect(result).toEqual({ success: true, communicationId: 'comm-1' });
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toBe('https://bff.example/api/communications/send');
    const body = bodyOf(fetch);
    expect(body.sendMode).toBe('User');
    expect(body.to).toEqual(['client@acme.example', 'cfo@acme.example']);
    expect(body.cc).toEqual(['partner@firm.example']);
    expect(body.bcc).toEqual(['records@firm.example']);
    expect(body.attachmentDocumentIds).toEqual(['d-1']);
    expect(body.associations).toEqual([{ entityType: 'sprk_project', entityId: 'p-1', entityName: 'Acme' }]);
    expect(body.bodyFormat).toBe('PlainText');
    expect(body.body).toBe('Dear Client,\n\nThe project is set up.\n\nKind regards');
  });

  it('negative: Bcc is never dropped, even with no Cc', async () => {
    const fetch = okFetch();
    await sendComposedEmail(
      { ...COMPOSED, cc: [] },
      { associations: association, authenticatedFetch: fetch as unknown as AuthenticatedFetchFn }
    );
    expect(bodyOf(fetch).bcc).toEqual(['records@firm.example']);
  });

  it('the shared mailbox stays the default From', async () => {
    const fetch = okFetch();
    await sendComposedEmail(
      { ...COMPOSED, sendMode: EMPTY_COMPOSED_EMAIL.sendMode },
      { associations: association, authenticatedFetch: fetch as unknown as AuthenticatedFetchFn }
    );
    expect(bodyOf(fetch).sendMode).toBe('SharedMailbox');
  });

  it('archives like the Send Email dialog by default; a record-less send (Summarize Files) can opt out', async () => {
    const fetch = okFetch();
    await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
    });
    expect(bodyOf(fetch).archiveToSpe).toBe(true);

    const noArchive = okFetch();
    await sendComposedEmail(COMPOSED, {
      associations: [],
      authenticatedFetch: noArchive as unknown as AuthenticatedFetchFn,
      archiveToSpe: false,
    });
    expect(bodyOf(noArchive).archiveToSpe).toBe(false);
  });

  it('a failed send is a warning, never a throw (the record stays)', async () => {
    const fetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 502,
      headers: { get: () => 'application/json' },
      json: async () => ({ title: 'Bad gateway', detail: 'Graph refused the send', correlationId: 'corr-9' }),
    });
    const result = await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
    });
    expect(result.success).toBe(false);
    expect(result.warning).toMatch(/not sent.*Graph refused the send.*corr-9/);
  });

  it('a send the server accepted without confirming it says the email may have been sent (no blind resend)', async () => {
    const fetch = jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });
    const result = await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
    });
    expect(result.success).toBe(false);
    expect(result.warning).toMatch(/^The email may have been sent/);
    expect(result.warning).toMatch(/Check your Sent Items or the record's Messages before sending it again\./);
    expect(result.warning).not.toMatch(/sendCommunication|communicationId|not sent|draft/i);
  });

  it('a server error with no detail is described in words, never "HTTP 500", and never mentions a draft', async () => {
    const fetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 500,
      headers: { get: () => 'text/plain' },
      json: async () => {
        throw new Error('not json');
      },
      text: async () => '',
    });
    const result = await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
    });
    expect(result.success).toBe(false);
    expect(result.warning).toBe("The email was not sent. The email service couldn't send it right now.");
    expect(result.warning).not.toMatch(/HTTP|draft/i);
  });

  it('an expired sign-in says to sign in and send it from the record (the composer is gone)', async () => {
    const fetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 401,
      headers: { get: () => 'application/json' },
      json: async () => ({ title: 'Unauthorized', status: 401 }),
    });
    const result = await sendComposedEmail(COMPOSED, {
      associations: association,
      authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
    });
    expect(result.warning).toBe(
      'The email was not sent. Your sign-in had expired. Sign in again, then send it from the record.'
    );
  });

  describe('the next step a failure names follows where the user is (task 183, fix round 1)', () => {
    const expired = () =>
      jest.fn().mockResolvedValue({
        ok: false,
        status: 401,
        headers: { get: () => 'application/json' },
        json: async () => ({ title: 'Unauthorized', status: 401 }),
      });
    const unconfirmed = () => jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });

    it('an expired sign-in with the composer still open says to select the button that sends again', async () => {
      const result = await sendComposedEmail(COMPOSED, {
        associations: association,
        authenticatedFetch: expired() as unknown as AuthenticatedFetchFn,
        retryLabel: 'Finish',
      });
      expect(result.warning).toBe(
        'The email was not sent. Your sign-in had expired. Sign in again, then select Finish to send it.'
      );
      expect(result.warning).not.toMatch(/from the record/);
    });

    it('an expired sign-in after the wizard closed, with no record, does not point at a record', async () => {
      const result = await sendComposedEmail(COMPOSED, {
        associations: [],
        authenticatedFetch: expired() as unknown as AuthenticatedFetchFn,
      });
      expect(result.warning).toBe(
        'The email was not sent. Your sign-in had expired. Sign in again, then send the email again.'
      );
      expect(result.warning).not.toMatch(/record/);
    });

    it('an unconfirmed send with no record says to check Sent Items only', async () => {
      const result = await sendComposedEmail(COMPOSED, {
        associations: [],
        authenticatedFetch: unconfirmed() as unknown as AuthenticatedFetchFn,
      });
      expect(result.warning).toBe(
        'The email may have been sent: Spaarke accepted it but did not confirm it. Check your Sent Items before sending it again.'
      );
    });

    it('a send that got no response with no record says to check Sent Items only', async () => {
      const noResponse = jest.fn().mockRejectedValue(new TypeError('Failed to fetch'));
      const result = await sendComposedEmail(COMPOSED, {
        associations: [],
        authenticatedFetch: noResponse as unknown as AuthenticatedFetchFn,
      });
      expect(result.warning).toMatch(/^The email may not have been sent/);
      expect(result.warning).toMatch(/Check your Sent Items before sending it again\./);
      expect(result.warning).not.toMatch(/record/);
    });

    it("with a record the unconfirmed warning still names the record's Messages", async () => {
      const result = await sendComposedEmail(COMPOSED, {
        associations: association,
        authenticatedFetch: unconfirmed() as unknown as AuthenticatedFetchFn,
        retryLabel: 'Send',
      });
      expect(result.warning).toMatch(/so it may not appear on the record/);
      expect(result.warning).toMatch(/Sent Items or the record's Messages/);
    });
  });

  it('refuses, with a warning, an attachment that never uploaded (never sent without it)', async () => {
    const fetch = okFetch();
    const result = await sendComposedEmail(
      { ...COMPOSED, attachments: [{ id: 'local:x', source: 'local', fileName: 'notes.docx', sizeBytes: 5 }] },
      { associations: association, authenticatedFetch: fetch as unknown as AuthenticatedFetchFn }
    );
    expect(result.success).toBe(false);
    expect(result.warning).toMatch(/notes\.docx/);
    expect(fetch).not.toHaveBeenCalled();
  });

  it('sends nothing when there is no To', async () => {
    const fetch = okFetch();
    expect(
      await sendComposedEmail(EMPTY_COMPOSED_EMAIL, {
        associations: association,
        authenticatedFetch: fetch as unknown as AuthenticatedFetchFn,
      })
    ).toEqual({ success: true });
    expect(fetch).not.toHaveBeenCalled();
  });

  it('toPlainText (moved from EmailMessageFields) keeps line breaks of a rich-text body', () => {
    expect(toPlainText('<p>Line one</p><p>Line two<br>three</p>', 'HTML')).toBe('Line one\nLine two\nthree');
    expect(toPlainText('a\nb', 'PlainText')).toBe('a\nb');
  });
});
