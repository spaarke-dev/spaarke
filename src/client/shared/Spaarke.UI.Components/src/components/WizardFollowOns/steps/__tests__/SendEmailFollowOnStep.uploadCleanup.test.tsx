/**
 * SendEmailFollowOnStep.uploadCleanup.test.tsx — spaarke-ontology-platform-r1 task 220, fix round 1 (#2813).
 *
 * The create wizards' Send Email step uploads a local file as a `sprk_document` at once. When the wizard's email is not
 * sent, the documents the step uploaded are deleted through the delete the upload handed back (`discard`):
 *   (a) the wizard closes without Finish (also after Next / Back remounted the step, and on close — not on reopen);
 *   (b) Finish's send provably did not go out (a 4xx refusal), then the wizard closes;
 *   (c) a file removed inside the step (remove then re-attach), also after Back / Next restored the item.
 * Never deleted: a document the wizard passed in (no `discard`); anything after a successful send; anything a Finish send
 * carried that may have gone out (no response, a host fetch error, 5xx).
 *
 * The harness is what every wizard host does: hold the `IComposedEmail`, render the step while it is shown, send it at
 * Finish with `sendComposedEmail`, and call `useDiscardComposedEmailUploadsOnClose(open, emailRef)`. The step, the composer
 * and `sendComposedEmail` are the real ones; the uploader and the fetch are fakes.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { SendEmailFollowOnStep } from '../SendEmailFollowOnStep';
import { EMPTY_COMPOSED_EMAIL, sendComposedEmail } from '../../../EmailComposer/composeOnlySend';
import type { IComposedEmail, ISendComposedEmailResult } from '../../../EmailComposer/composeOnlySend';
import { useDiscardComposedEmailUploadsOnClose } from '../../../EmailComposer/uploadDiscard';
import type { IUploadedLocalAttachment } from '../../../EmailComposer/EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../../services/EntityCreationService';

const BFF = 'https://bff.example.com';

const READY: IComposedEmail = {
  ...EMPTY_COMPOSED_EMAIL,
  to: [{ email: 'client@example.com', resolved: false }],
  subject: 'New matter',
  body: 'The matter is set up.',
};

function fakeDocuments() {
  const deleteDocument = jest.fn(async (_documentId: string) => undefined);
  let n = 0;
  const upload = jest.fn(async (_file: File): Promise<IUploadedLocalAttachment> => {
    n += 1;
    const documentId = `doc-${n}`;
    return { documentId, discard: () => deleteDocument(documentId) };
  });
  return { upload, deleteDocument, deleted: () => deleteDocument.mock.calls.map(c => c[0]) };
}

function answering(status: number): AuthenticatedFetchFn {
  return jest.fn(
    async () =>
      ({
        ok: status >= 200 && status < 300,
        status,
        json: async () => (status < 300 ? { communicationId: 'comm-1' } : null),
      }) as unknown as Response
  ) as unknown as AuthenticatedFetchFn;
}

function throwing(err: unknown): AuthenticatedFetchFn {
  return jest.fn().mockRejectedValue(err) as unknown as AuthenticatedFetchFn;
}

interface IWizardApi {
  showStep: (show: boolean) => void;
  finish: () => Promise<ISendComposedEmailResult>;
}

/** A wizard host as the real ones are wired (CreateRecordWizard, Work Assignment, Summarize Files, …). */
const Wizard: React.FC<{
  open: boolean;
  initial: IComposedEmail;
  upload: (file: File) => Promise<IUploadedLocalAttachment>;
  fetch: AuthenticatedFetchFn;
  api: React.MutableRefObject<IWizardApi | null>;
}> = ({ open, initial, upload, fetch, api }) => {
  const [email, setEmail] = React.useState(initial);
  const emailRef = React.useRef(email);
  emailRef.current = email;
  const [shown, setShown] = React.useState(true);
  useDiscardComposedEmailUploadsOnClose(open, emailRef);
  api.current = {
    showStep: setShown,
    finish: () =>
      sendComposedEmail(emailRef.current, {
        associations: [{ entityType: 'sprk_matter', entityId: 'm-1', entityName: 'Matter 1' } as never],
        authenticatedFetch: fetch,
        bffBaseUrl: BFF,
      }),
  };
  if (!open) return null;
  return shown ? (
    <SendEmailFollowOnStep value={email} onChange={setEmail} composeHandlers={{ onUploadLocalAttachment: upload }} />
  ) : (
    <div>another step</div>
  );
};

function renderWizard(opts: { initial?: IComposedEmail; fetch?: AuthenticatedFetchFn } = {}) {
  const docs = fakeDocuments();
  const api: React.MutableRefObject<IWizardApi | null> = { current: null };
  const props = {
    initial: opts.initial ?? READY,
    upload: docs.upload,
    fetch: opts.fetch ?? answering(200),
    api,
  };
  const utils = renderWithProviders(<Wizard open {...props} />);
  const setOpen = (open: boolean) => utils.rerender(<Wizard open={open} {...props} />);
  return { docs, api, setOpen };
}

async function attach(name: string, uploads: number, upload: jest.Mock) {
  const input = document.querySelector(
    '[data-testid="send-email-followon-step"] input[type="file"]'
  ) as HTMLInputElement;
  fireEvent.change(input, { target: { files: [new File(['%PDF'], name, { type: 'application/pdf' })] } });
  await waitFor(() => expect(upload).toHaveBeenCalledTimes(uploads));
  await act(async () => {
    await Promise.resolve();
  });
}

function removeAttachment(name: string) {
  const heading = screen.getAllByRole('button', { name: /^Attachments/ })[0];
  if (heading.getAttribute('aria-expanded') !== 'true') fireEvent.click(heading);
  fireEvent.click(screen.getByRole('button', { name: `Remove ${name}` }));
}

/** Let any stray delete run, so "not called" means not called. */
async function settle() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

describe('Send Email follow-on: the documents the step uploaded are deleted when the email is not sent (#2813)', () => {
  // (a) close without Finish
  it('closing the wizard without Finish deletes the upload', async () => {
    const { docs, setOpen } = renderWizard();
    await attach('fees.pdf', 1, docs.upload);
    expect(docs.deleteDocument).not.toHaveBeenCalled();

    setOpen(false);

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
  });

  it('Next and Back (the step unmounts and remounts) delete nothing; closing then deletes the upload once; reopening deletes nothing more', async () => {
    const { docs, api, setOpen } = renderWizard();
    await attach('fees.pdf', 1, docs.upload);

    act(() => api.current!.showStep(false));
    act(() => api.current!.showStep(true));
    await settle();
    expect(docs.deleteDocument).not.toHaveBeenCalled();

    setOpen(false);
    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
    setOpen(true);
    setOpen(false);
    await settle();
    expect(docs.deleted()).toEqual(['doc-1']);
  });

  // (b) Finish that provably did not send
  it('a Finish the server refused (422), then closing, deletes the upload', async () => {
    const { docs, api, setOpen } = renderWizard({ fetch: answering(422) });
    await attach('fees.pdf', 1, docs.upload);

    let result: ISendComposedEmailResult | undefined;
    await act(async () => {
      result = await api.current!.finish();
    });
    expect(result!.success).toBe(false);
    await settle();
    expect(docs.deleteDocument).not.toHaveBeenCalled(); // the failure itself deletes nothing

    setOpen(false);
    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
  });

  // (c) remove then re-attach inside the step
  it('remove then re-attach deletes the removed upload at once and the re-attached one on close', async () => {
    const { docs, setOpen } = renderWizard();
    await attach('fees.pdf', 1, docs.upload);
    removeAttachment('fees.pdf');
    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));

    await attach('fees.pdf', 2, docs.upload);
    setOpen(false);
    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1', 'doc-2']));
  });

  it('a file removed after Back / Next restored it still deletes its upload', async () => {
    const { docs, api } = renderWizard();
    await attach('fees.pdf', 1, docs.upload);
    act(() => api.current!.showStep(false));
    act(() => api.current!.showStep(true));

    removeAttachment('fees.pdf');

    await waitFor(() => expect(docs.deleted()).toEqual(['doc-1']));
  });

  // Must-not-fire controls
  it('a successful Finish, then closing, deletes nothing', async () => {
    const { docs, api, setOpen } = renderWizard();
    await attach('fees.pdf', 1, docs.upload);

    await act(async () => {
      expect((await api.current!.finish()).success).toBe(true);
    });
    setOpen(false);
    await settle();

    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it.each([
    ['no response', throwing(new TypeError('Failed to fetch'))],
    ["a host fetch's own error", throwing(new Error('socket hang up'))],
    ['a 500', answering(500)],
  ])('a Finish with %s (the email may have gone out), then closing, deletes nothing', async (_label, fetch) => {
    const { docs, api, setOpen } = renderWizard({ fetch });
    await attach('fees.pdf', 1, docs.upload);

    await act(async () => {
      expect((await api.current!.finish()).success).toBe(false);
    });
    setOpen(false);
    await settle();

    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });

  it('a document the wizard passed in (no delete of its own) is never deleted', async () => {
    const initial: IComposedEmail = {
      ...READY,
      attachments: [
        { id: 'doc:d-9', source: 'related', fileName: 'brief.pdf', sizeBytes: 10, documentId: 'd-9', selected: true },
      ],
    };
    const { docs, setOpen } = renderWizard({ initial });
    removeAttachment('brief.pdf');
    setOpen(false);
    await settle();

    expect(docs.deleteDocument).not.toHaveBeenCalled();
  });
});
