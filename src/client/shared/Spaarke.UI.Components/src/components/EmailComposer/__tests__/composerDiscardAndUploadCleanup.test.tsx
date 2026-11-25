/**
 * composerDiscardAndUploadCleanup.test.tsx — spaarke-ontology-platform-r1 task 220 (#2946 DI-158, #2813 DI-154).
 *
 * 1. Closing a draft with typed content asks before discarding it ("Discard this email?"); Keep editing keeps it; an
 *    untouched draft closes without asking.
 * 2. A local file is uploaded as a document at once. When the email is not sent (confirmed discard, Cancel after a
 *    failed send) or the file is removed from the draft, the composer deletes the documents IT uploaded, through the
 *    delete the upload handed back (`discard`). Observed on one fake delete:
 *      - a document picked from existing records is never deleted;
 *      - a successful send deletes nothing;
 *      - a failed send that may have gone out (no response, 5xx) deletes nothing (a sent email's attachment row would be
 *        unlinked, not refused: `sprk_communicationattachment_Document_1n` is RemoveLink);
 *      - an upload whose handler hands back no delete behaves as before.
 * 3. A send that got no response at all is worded as a connection failure, never "on this page"; an HTTP 500 keeps its
 *    wording.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SendEmailDialog, type ISendEmailDialogProps } from '../wrappers/SendEmailDialog';
import { SendEmailPage } from '../wrappers/SendEmailPage';
import { EmailComposer } from '../EmailComposer';
import type { IEmailComposerHandle, IEmailComposerProps, IUploadedLocalAttachment } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BFF = 'https://bff.example.com';
const PDF = 'application/pdf';

function pdf(name: string): File {
  return new File(['%PDF-1.4'], name, { type: PDF });
}

/** A fetch that answers the send with `status` (2xx → a communication id), shaped like `@spaarke/auth`'s. */
function sendAnswering(status: number): AuthenticatedFetchFn {
  return throwingAuthenticatedFetch(
    async () =>
      ({
        ok: status >= 200 && status < 300,
        status,
        json: async () => (status < 300 ? { communicationId: 'comm-1' } : null),
      }) as unknown as Response
  ) as unknown as AuthenticatedFetchFn;
}

/** A 2xx whose body carries no communication id: the server may have sent it (SENT_UNCONFIRMED). */
function sendAnsweringWithoutId(): AuthenticatedFetchFn {
  return throwingAuthenticatedFetch(
    async () => ({ ok: true, status: 200, json: async () => ({}) }) as unknown as Response
  ) as unknown as AuthenticatedFetchFn;
}

function sendThrowing(err: unknown): AuthenticatedFetchFn {
  return jest.fn().mockRejectedValue(err) as unknown as AuthenticatedFetchFn;
}

/**
 * The fake document store: `upload` creates `doc-N` and hands back a `discard` that deletes exactly that document via
 * `deleteDocument` (the one place a delete is observed).
 */
function fakeDocuments(opts: { withDiscard?: boolean } = {}) {
  const withDiscard = opts.withDiscard ?? true;
  const deleteDocument = jest.fn(async (_documentId: string) => undefined);
  let n = 0;
  const upload = jest.fn(async (_file: File): Promise<IUploadedLocalAttachment> => {
    n += 1;
    const documentId = `doc-${n}`;
    return withDiscard ? { documentId, discard: () => deleteDocument(documentId) } : { documentId };
  });
  return { upload, deleteDocument, deleted: () => deleteDocument.mock.calls.map(c => c[0]) };
}

function renderDialog(props: Partial<ISendEmailDialogProps> = {}) {
  const onClose = jest.fn();
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <SendEmailDialog
        open
        onClose={onClose}
        authenticatedFetch={sendAnswering(200)}
        bffBaseUrl={BFF}
        attachmentSources={[{ kind: 'local' }]}
        {...props}
      />
    </FluentProvider>
  );
  return { onClose, ...utils };
}

const SEEDED = {
  initialTo: ['alice@example.com'],
  initialSubject: 'Engagement terms',
  initialBody: 'Please see the attached file.',
};

function fileInput(): HTMLInputElement {
  return document.querySelector('input[type="file"]') as HTMLInputElement;
}

async function attach(name: string, expectedDocumentCalls: number, upload: jest.Mock) {
  fireEvent.change(fileInput(), { target: { files: [pdf(name)] } });
  await waitFor(() => expect(upload).toHaveBeenCalledTimes(expectedDocumentCalls));
  // Let the upload settle and the item get its document.
  await act(async () => {
    await Promise.resolve();
  });
}

/**
 * A button in a nested SprkModal. The dialog itself is found accessibly (`findByRole` above); its buttons are read with
 * `hidden: true` because in jsdom the Fluent/tabster modal stack marks a nested SprkModal surface `aria-hidden` a moment
 * after it opens — the existing "Email not sent" dialog does the same (checked on origin/master), so a slow run would
 * otherwise flake. Not a claim about browsers.
 */
function dialogButton(dialog: HTMLElement, name: string): HTMLElement {
  return within(dialog).getByRole('button', { name, hidden: true });
}

function clickClose() {
  // The shell's ×.
  fireEvent.click(screen.getByRole('button', { name: 'Close' }));
}

async function confirmDiscard() {
  const ask = await screen.findByRole('alertdialog', { name: 'Discard this email?' });
  fireEvent.click(dialogButton(ask, 'Discard'));
}

function removeAttachment(name: string) {
  const heading = screen.getAllByRole('button', { name: /^Attachments/ })[0];
  if (heading.getAttribute('aria-expanded') !== 'true') fireEvent.click(heading);
  fireEvent.click(screen.getByRole('button', { name: `Remove ${name}` }));
}

// ---------------------------------------------------------------------------------------------------------------------
// 1. Asking before a typed draft is discarded (#2946)
// ---------------------------------------------------------------------------------------------------------------------

describe('EmailComposer — closing a typed draft asks first (#2946)', () => {
  it('× after typing a subject asks "Discard this email?" and does not close', async () => {
    const { onClose } = renderDialog();
    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Fee change' } });

    clickClose();

    const ask = await screen.findByRole('alertdialog', { name: 'Discard this email?' });
    expect(dialogButton(ask, 'Keep editing')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('Keep editing keeps the draft open and intact', async () => {
    const { onClose } = renderDialog();
    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Fee change' } });
    clickClose();
    const ask = await screen.findByRole('alertdialog', { name: 'Discard this email?' });

    fireEvent.click(dialogButton(ask, 'Keep editing'));

    await waitFor(() => expect(screen.queryByRole('alertdialog', { name: 'Discard this email?' })).toBeNull());
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByDisplayValue('Fee change')).toBeInTheDocument();
  });

  it('Discard closes', async () => {
    const { onClose } = renderDialog();
    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Fee change' } });
    clickClose();
    await confirmDiscard();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('the Cancel button asks too (page mount)', async () => {
    const onClose = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <SendEmailPage mode="compose" authenticatedFetch={sendAnswering(200)} bffBaseUrl={BFF} onClose={onClose} />
      </FluentProvider>
    );
    fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'Fee change' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await screen.findByRole('alertdialog', { name: 'Discard this email?' });
    expect(onClose).not.toHaveBeenCalled();
  });

  // Must-not-fire controls.
  it('an empty draft closes without asking', () => {
    const { onClose } = renderDialog();
    clickClose();
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alertdialog', { name: 'Discard this email?' })).toBeNull();
  });

  it('an untouched reply prefill with &quot; / &#39; entities and <style> text closes without asking (K3)', async () => {
    const { onClose } = renderDialog({
      mode: 'reply',
      sourceRecord: {
        communicationId: 'comm-0',
        from: 'alice@example.com',
        to: ['me@example.com'],
        subject: 'Fees',
        body: '<style>p { color: red; }</style><p>She said &quot;fine&quot; and it&#39;s agreed &amp; signed.</p>',
        bodyFormat: 'HTML',
      },
    } as Partial<ISendEmailDialogProps>);
    // Let the editor mount and re-serialise the quoted body.
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 50));
    });

    clickClose();

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alertdialog', { name: 'Discard this email?' })).toBeNull();
  });

  it('a draft the host pre-filled and the user did not touch closes without asking', () => {
    const { onClose } = renderDialog(SEEDED);
    clickClose();
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alertdialog', { name: 'Discard this email?' })).toBeNull();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// 2. Cleaning up what this composer uploaded (#2813)
// ---------------------------------------------------------------------------------------------------------------------

describe('EmailComposer — documents this session uploaded are deleted when the email is not sent (#2813)', () => {
  it('confirmed discard deletes the session upload', async () => {
    const docs = fakeDocuments();
    const { onClose } = renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload });
    await attach('fees.pdf', 1, docs.upload);

    clickClose();
    await confirmDiscard();

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Cancel after a send the server refused (422) deletes the session upload', async () => {
    const docs = fakeDocuments();
    renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload, authenticatedFetch: sendAnswering(422) });
    await attach('fees.pdf', 1, docs.upload);

    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));
    const failure = await screen.findByRole('alertdialog', { name: 'Email not sent' });
    // The failure itself deletes nothing: the draft (and its upload) stays for a retry.
    expect(docs.deleteDocument).not.toHaveBeenCalled();
    fireEvent.click(dialogButton(failure, 'OK'));

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await confirmDiscard();

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
  });

  it('remove then re-attach deletes the removed upload at once, then the re-attached one on discard', async () => {
    const docs = fakeDocuments();
    renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload });
    await attach('fees.pdf', 1, docs.upload);

    removeAttachment('fees.pdf');
    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));

    await attach('fees.pdf', 2, docs.upload);
    clickClose();
    await confirmDiscard();

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1', 'doc-2']));
  });

  it('an upload that finishes after the composer was discarded is deleted', async () => {
    const deleteDocument = jest.fn(async (_id: string) => undefined);
    let finish: (v: IUploadedLocalAttachment) => void = () => undefined;
    const upload = jest.fn(
      () =>
        new Promise<IUploadedLocalAttachment>(resolve => {
          finish = resolve;
        })
    );
    renderDialog({ ...SEEDED, onUploadLocalAttachment: upload });
    fireEvent.change(fileInput(), { target: { files: [pdf('late.pdf')] } });
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(1));

    clickClose();
    await confirmDiscard();
    await act(async () => {
      finish({ documentId: 'doc-late', discard: () => deleteDocument('doc-late') });
      await Promise.resolve();
    });

    await waitFor(() => expect(deleteDocument).toHaveBeenCalledWith('doc-late'));
  });

  it('an upload that finishes after an inline composer unmounted is deleted (its id never reached the host)', async () => {
    const deleteDocument = jest.fn(async (_id: string) => undefined);
    let finish: (v: IUploadedLocalAttachment) => void = () => undefined;
    const upload = jest.fn(
      () =>
        new Promise<IUploadedLocalAttachment>(resolve => {
          finish = resolve;
        })
    );
    const { unmount } = render(
      <FluentProvider theme={webLightTheme}>
        <EmailComposer
          mode="compose"
          mount="inline"
          authenticatedFetch={sendAnswering(200) as unknown as IEmailComposerProps['authenticatedFetch']}
          attachmentSources={[{ kind: 'local' }]}
          onUploadLocalAttachment={upload}
        />
      </FluentProvider>
    );
    fireEvent.change(fileInput(), { target: { files: [pdf('late.pdf')] } });
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(1));
    unmount();

    await act(async () => {
      finish({ documentId: 'doc-late', discard: () => deleteDocument('doc-late') });
      await Promise.resolve();
    });

    await waitFor(() => expect(deleteDocument).toHaveBeenCalledWith('doc-late'));
  });

  // Never-delete cases.
  it('a document picked from existing records is never deleted', async () => {
    const docs = fakeDocuments();
    const onLookupRecord = jest.fn().mockResolvedValue({
      entityType: 'sprk_document',
      id: 'doc-existing',
      name: 'Engagement letter.pdf',
    });
    renderDialog({
      ...SEEDED,
      onUploadLocalAttachment: docs.upload,
      onLookupRecord,
      initialAttachments: [
        { id: 'carried', source: 'related', fileName: 'Prior memo.pdf', sizeBytes: 10, documentId: 'doc-carried' },
      ],
    });
    fireEvent.click(screen.getByRole('button', { name: 'Attach' }));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Link documents' }));
    await waitFor(() => expect(onLookupRecord).toHaveBeenCalledWith('sprk_document'));
    await attach('fees.pdf', 1, docs.upload);

    // Removing the picked document deletes nothing.
    await act(async () => {
      await Promise.resolve();
    });
    removeAttachment('Engagement letter.pdf');
    removeAttachment('Prior memo.pdf');
    expect(docs.deleteDocument).not.toHaveBeenCalled();

    clickClose();
    await confirmDiscard();

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
  });

  it('a successful send deletes nothing', async () => {
    const docs = fakeDocuments();
    const onSent = jest.fn();
    const { onClose } = renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload, onSent });
    await attach('fees.pdf', 1, docs.upload);

    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));

    await waitFor(() => expect(onSent).toHaveBeenCalledWith('comm-1'));
    expect(onClose).toHaveBeenCalled();
    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it.each([
    [
      'no response (the email may have gone out)',
      sendThrowing(new TypeError('Failed to fetch')),
      'Email may not have been sent',
    ],
    ['a 500 (the email may have gone out)', sendAnswering(500), 'Email not sent'],
    ['a success without an id (SENT_UNCONFIRMED)', sendAnsweringWithoutId(), 'Email may have been sent'],
  ])('Cancel after %s deletes nothing', async (_label, authenticatedFetch, failureTitle) => {
    const docs = fakeDocuments();
    renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload, authenticatedFetch });
    await attach('fees.pdf', 1, docs.upload);

    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));
    const failure = await screen.findByRole('alertdialog', { name: failureTitle });
    fireEvent.click(dialogButton(failure, 'OK'));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // F4-4: once an attempt may have sent the email, the prompt does not claim it "will not be sent".
    const ask = await screen.findByRole('alertdialog', { name: 'Discard this email?' });
    expect(ask).toHaveTextContent('What you have written will be lost.');
    expect(ask).not.toHaveTextContent(/will not be sent/);
    fireEvent.click(dialogButton(ask, 'Discard'));

    // Nothing to wait for: give any stray delete a chance to run, then check none did.
    await act(async () => {
      await Promise.resolve();
    });
    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it('a document a saved draft carried is never deleted, even when the draft is then discarded', async () => {
    const docs = fakeDocuments();
    const ref = React.createRef<IEmailComposerHandle>();
    const onCancel = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <EmailComposer
          ref={ref}
          mode="compose"
          mount="dialog"
          authenticatedFetch={sendAnswering(200) as unknown as IEmailComposerProps['authenticatedFetch']}
          attachmentSources={[{ kind: 'local' }]}
          onUploadLocalAttachment={docs.upload}
          onSaveDraftRequest={async () => ({ communicationId: 'draft-1' })}
          onCancel={onCancel}
        />
      </FluentProvider>
    );
    await attach('fees.pdf', 1, docs.upload);

    await act(async () => {
      await ref.current!.saveDraft();
    });
    act(() => ref.current!.requestCancel());
    await confirmDiscard();

    expect(onCancel).toHaveBeenCalledTimes(1);
    await act(async () => {
      await Promise.resolve();
    });
    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it('closing after a successful send neither asks nor deletes', async () => {
    const docs = fakeDocuments();
    const ref = React.createRef<IEmailComposerHandle>();
    const onCancel = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <EmailComposer
          ref={ref}
          mode="compose"
          mount="dialog"
          authenticatedFetch={sendAnswering(200) as unknown as IEmailComposerProps['authenticatedFetch']}
          bffBaseUrl={BFF}
          initialTo={SEEDED.initialTo}
          initialSubject={SEEDED.initialSubject}
          initialBody={SEEDED.initialBody}
          attachmentSources={[{ kind: 'local' }]}
          onUploadLocalAttachment={docs.upload}
          onCancel={onCancel}
        />
      </FluentProvider>
    );
    await attach('fees.pdf', 1, docs.upload);

    await act(async () => {
      await ref.current!.send();
    });
    act(() => ref.current!.requestCancel());

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('alertdialog', { name: 'Discard this email?' })).toBeNull();
    await act(async () => {
      await Promise.resolve();
    });
    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it('an upload sent only as a body link (Attach off, Link on) is kept when the send may have gone out (F1-1)', async () => {
    const deleteDocument = jest.fn(async (_id: string) => undefined);
    const { onClose } = renderDialog({
      ...SEEDED,
      authenticatedFetch: sendThrowing(new TypeError('Failed to fetch')),
      initialAttachments: [
        {
          id: 'local:fees',
          source: 'local',
          fileName: 'fees.pdf',
          sizeBytes: 1,
          documentId: 'doc-linked',
          discard: () => deleteDocument('doc-linked'),
          selected: false,
          linkSelected: true,
          linkUrl: 'https://org.example.com/main.aspx?etn=sprk_document&id=doc-linked',
        },
      ],
    });

    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));
    const failure = await screen.findByRole('alertdialog', { name: 'Email may not have been sent' });
    fireEvent.click(dialogButton(failure, 'OK'));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    const ask = screen.queryByRole('alertdialog', { name: 'Discard this email?' });
    if (ask) fireEvent.click(dialogButton(ask, 'Discard'));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
    await act(async () => {
      await Promise.resolve();
    });
    expect(deleteDocument).not.toHaveBeenCalled();
  });

  it('an upload whose handler hands back no delete behaves as before (discard closes, nothing thrown)', async () => {
    const docs = fakeDocuments({ withDiscard: false });
    const { onClose } = renderDialog({ ...SEEDED, onUploadLocalAttachment: docs.upload });
    await attach('fees.pdf', 1, docs.upload);
    removeAttachment('fees.pdf');
    await attach('fees.pdf', 2, docs.upload);

    clickClose();
    await confirmDiscard();

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------------------------------------------------
// 3. A send that got no response is worded as a connection failure (#2946)
// ---------------------------------------------------------------------------------------------------------------------

describe('EmailComposer — a send with no response says the connection failed (#2946)', () => {
  it.each([
    ['WebKit offline wording', new TypeError('The Internet connection appears to be offline.')],
    [
      'a TypeError from another realm (not instanceof TypeError here)',
      { name: 'TypeError', message: 'Failed to fetch' },
    ],
    ['a host fetch that throws its own error', new Error('socket hang up')],
  ])('%s → connection wording, never "on this page"', async (_label, thrown) => {
    renderDialog({ ...SEEDED, authenticatedFetch: sendThrowing(thrown) });
    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));

    const failure = await screen.findByRole('alertdialog', { name: 'Email may not have been sent' });
    expect(failure.textContent).toContain('The connection to the Spaarke server failed');
    expect(failure.textContent).toContain('Your draft is still here');
    expect(failure.textContent).not.toMatch(/on this page/);
  });

  it('an HTTP 500 keeps the existing wording', async () => {
    renderDialog({ ...SEEDED, authenticatedFetch: sendAnswering(500) });
    fireEvent.click(screen.getByRole('button', { name: /^send$/i }));

    const failure = await screen.findByRole('alertdialog', { name: 'Email not sent' });
    expect(failure.textContent).toContain("The email service couldn't send this right now. Try again shortly.");
    expect(failure.textContent).not.toMatch(/connection/);
  });
});
