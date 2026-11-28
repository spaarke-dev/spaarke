/**
 * Task 143 (owner A1, "Yes gate it"): an attachment saved on its own ("Save the email" unticked) whose bytes are identical
 * to a document already in Spaarke. The server answers `contentDuplicate: true`; it names the existing document
 * (`existingDocument`: id + name) only when the caller holds Read on it and the external narrowing shows it.
 *
 * - Named: "Already in Spaarke as {name}", the name a link that opens it through GET /api/documents/{id}/open-links.
 * - Withheld: just "Already in Spaarke" — no name, no id, no link.
 * - It counts as neither saved nor failed in the outcome's summary.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker are doubled.
 */
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow, describeAttachmentsOnlyOutcome } from '../SaveFlow';
import { existingDocumentOf } from '../../hooks/useSaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../hooks/emailContentCaptureContext';
import type { EmailContentCapture } from '../../services/emailContentCapture';
import type { AttachmentInfo } from '@shared/adapters/types';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => ({ RelatedToPicker: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: {
    digest: jest.fn(async (_alg: string, data: ArrayBuffer) => {
      const bytes = new Uint8Array(data);
      const out = new Uint8Array(32);
      bytes.forEach((b, i) => {
        out[i % 32] = (out[i % 32]! * 31 + b) % 256;
      });
      return out.buffer;
    }),
  },
});

const MESSAGE_ID = '<CAF0-143-dup@mail.example.com>';
const ITEM_ID = 'AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/dup=';
const A = 'Exhibit A.pdf';
const B = 'Exhibit B.pdf';
const EXISTING_ID = '33333333-3333-3333-3333-333333333333';
const EXISTING_NAME = 'Signed Lease — Exhibit A';
const OPEN_LINKS = { webUrl: 'https://contoso.sharepoint.com/contentstorage/lease.pdf', desktopUrl: null };

const pdf = (name: string): AttachmentInfo => ({
  id: `att-${name}`,
  name,
  contentType: 'application/pdf',
  size: 20_000,
  isInline: false,
});

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(
  async (attachments, selected) => ({
    body: '<p>Please see attached.</p>',
    isBodyHtml: true,
    attachments: attachments.map(a => ({
      attachmentId: a.id,
      fileName: a.name,
      size: a.size,
      contentType: a.contentType,
      contentBase64: `BYTES-${a.name}`,
      isInline: false,
    })),
    selectedAttachmentFileNames: attachments.filter(a => selected.has(a.id)).map(a => a.name),
    skipped: [],
  })
);

const response = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  json: async () => body,
  text: async () => JSON.stringify(body),
});
const job = { jobId: 'job-143', statusUrl: '/api/office/jobs/job-143', streamUrl: '/api/office/jobs/job-143/stream' };
const accepted = () => response(202, { success: true, duplicate: false, ...job });
const alreadyNamed = () =>
  response(202, {
    success: true,
    duplicate: false,
    ...job,
    contentDuplicate: true,
    existingDocument: { id: EXISTING_ID, name: EXISTING_NAME },
  });
const alreadyWithheld = () => response(202, { success: true, duplicate: false, ...job, contentDuplicate: true });
// A re-save answered through the job key (the caller's earlier save of these bytes resolved to an existing document):
// 200, `duplicate: true`, `contentDuplicate: true`, and never an `existingDocument` (the server names nothing there).
const reSavedContentDuplicate = () => response(200, { success: true, duplicate: true, ...job, contentDuplicate: true });
// A re-save of an attachment that was an ordinary save: 200, `duplicate: true`, no `contentDuplicate`.
const reSavedPlain = () => response(200, { success: true, duplicate: true, ...job });
const forbidden = () =>
  response(403, {
    type: 'https://spaarke.com/errors/office/access-denied',
    title: 'Forbidden',
    status: 403,
    detail: 'You do not have access to save this attachment.',
    errorCode: 'OFFICE_009',
  });

const retry = jest.fn();
const onComplete = jest.fn();

function OutlookPane({ attachments }: { attachments: AttachmentInfo[] }) {
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId={ITEM_ID}
          internetMessageId={MESSAGE_ID}
          itemName="Lease renewal"
          senderEmail="opposing@counsel.example"
          sentDate={new Date(Date.UTC(2026, 10, 20, 12))}
          attachments={attachments}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          onComplete={onComplete}
          onRetryDocumentIdentity={retry}
        />
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

/** Answers each attachment's save in order with `answers`; open-links with {@link OPEN_LINKS}. */
function answerSaves(...answers: Array<() => ReturnType<typeof response>>) {
  let n = 0;
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) return answers[n++]!();
    if (String(url).includes('/open-links')) return response(200, OPEN_LINKS);
    return response(200, { data: {} });
  });
}

async function saveAttachmentsOnly(names: string[]) {
  render(<OutlookPane attachments={names.map(pdf)} />);
  fireEvent.click(screen.getByRole('checkbox', { name: 'Save the email' }));
  names.forEach(name => fireEvent.click(screen.getByRole('checkbox', { name: `Select ${name}` })));
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  return waitFor(() => screen.getByTestId('attachments-only-outcome'));
}

const openLinksCalls = () =>
  mockFetch.mock.calls.map(([url]) => String(url)).filter(url => url.includes('/open-links'));

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('SaveFlow — an attachment already in Spaarke (task 143, owner A1)', () => {
  it('named: "Already in Spaarke as {name}", and the name opens that document through open-links', async () => {
    answerSaves(alreadyNamed);
    const outcome = await saveAttachmentsOnly([A]);

    const line = within(outcome).getByTestId('attachments-only-already-in-spaarke');
    expect(line).toHaveTextContent(`${A}: Already in Spaarke as ${EXISTING_NAME}`);
    expect(
      within(outcome).getByText(
        'The attachment is already in Spaarke. Nothing new was saved, and the email was not saved.'
      )
    ).toBeInTheDocument();

    fireEvent.click(within(line).getByText(EXISTING_NAME));
    await waitFor(() => expect(openLinksCalls()).toEqual([`https://bff/api/documents/${EXISTING_ID}/open-links`]));
    const openBrowserWindow = Office.context.ui.openBrowserWindow as jest.Mock;
    await waitFor(() => expect(openBrowserWindow).toHaveBeenCalledWith(OPEN_LINKS.webUrl));
    // Opening it is not a save, and the outcome never completes as an email save.
    expect(mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'))).toHaveLength(1);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it('withheld: just "Already in Spaarke" — no name, no id, no link', async () => {
    answerSaves(alreadyWithheld);
    const outcome = await saveAttachmentsOnly([A]);

    const line = within(outcome).getByTestId('attachments-only-already-in-spaarke');
    expect(line).toHaveTextContent(`${A}: Already in Spaarke`);
    expect(line.textContent).toBe(`${A}: Already in Spaarke`);
    expect(within(line).queryByRole('link')).toBeNull();
    expect(within(line).queryByRole('button')).toBeNull();
    expect(openLinksCalls()).toHaveLength(0);
  });

  it('negative: a malformed identity is treated as withheld', async () => {
    answerSaves(() =>
      response(202, { success: true, ...job, contentDuplicate: true, existingDocument: { id: EXISTING_ID } })
    );
    const outcome = await saveAttachmentsOnly([A]);

    expect(within(outcome).getByTestId('attachments-only-already-in-spaarke').textContent).toBe(
      `${A}: Already in Spaarke`
    );
  });

  it('summary: one saved and one already in Spaarke — counted as 1 saved, never 2, and not as a failure', async () => {
    answerSaves(accepted, alreadyWithheld);
    const outcome = await saveAttachmentsOnly([A, B]);

    expect(within(outcome).getByText('1 attachment saved to Spaarke. The email was not saved.')).toBeInTheDocument();
    expect(within(outcome).getByTestId('attachments-only-already-in-spaarke')).toHaveTextContent(
      `${B}: Already in Spaarke`
    );
    expect(outcome).not.toHaveTextContent(`${A}:`);
    // The saved one updates the marks.
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('summary: one failed and one already in Spaarke — the failure is reported alone; nothing saved, no identity re-read', async () => {
    answerSaves(forbidden, alreadyNamed);
    const outcome = await saveAttachmentsOnly([A, B]);

    expect(within(outcome).getByText('The attachment was not saved.')).toBeInTheDocument();
    expect(outcome).toHaveTextContent(`${A}:`);
    expect(within(outcome).getByTestId('attachments-only-already-in-spaarke')).toHaveTextContent(
      `${B}: Already in Spaarke as ${EXISTING_NAME}`
    );
    expect(retry).not.toHaveBeenCalled();
  });

  it('a 200 duplicate answer through the job key that says contentDuplicate: "Already in Spaarke", counted neither saved nor failed', async () => {
    answerSaves(reSavedContentDuplicate);
    const outcome = await saveAttachmentsOnly([A]);

    const line = within(outcome).getByTestId('attachments-only-already-in-spaarke');
    expect(line.textContent).toBe(`${A}: Already in Spaarke`);
    expect(within(line).queryByRole('link')).toBeNull();
    expect(
      within(outcome).getByText(
        'The attachment is already in Spaarke. Nothing new was saved, and the email was not saved.'
      )
    ).toBeInTheDocument();
    expect(outcome).not.toHaveTextContent('saved to Spaarke');
    expect(outcome).not.toHaveTextContent('not saved:');
    expect(openLinksCalls()).toHaveLength(0);
    expect(retry).not.toHaveBeenCalled();
  });

  it('negative: a 200 duplicate answer without contentDuplicate is still counted saved (already saved by an earlier save)', async () => {
    answerSaves(reSavedPlain);
    const outcome = await saveAttachmentsOnly([A]);

    expect(within(outcome).getByText('1 attachment saved to Spaarke. The email was not saved.')).toBeInTheDocument();
    expect(within(outcome).queryByTestId('attachments-only-already-in-spaarke')).toBeNull();
  });
  it('negative: an ordinary accepted save is "saved", never "already in Spaarke"', async () => {
    answerSaves(accepted);
    const outcome = await saveAttachmentsOnly([A]);

    expect(within(outcome).getByText('1 attachment saved to Spaarke. The email was not saved.')).toBeInTheDocument();
    expect(within(outcome).queryByTestId('attachments-only-already-in-spaarke')).toBeNull();
  });
});

describe('describeAttachmentsOnlyOutcome — already in Spaarke counts as neither saved nor failed (task 143, owner A1)', () => {
  const already = (n: number) => Array.from({ length: n }, (_, i) => ({ name: `x${i}.pdf` }));

  it('only already in Spaarke: info, singular and plural', () => {
    expect(describeAttachmentsOnlyOutcome({ saved: [], failed: [], alreadyInSpaarke: already(1) })).toEqual({
      intent: 'info',
      title: 'The attachment is already in Spaarke. Nothing new was saved, and the email was not saved.',
    });
    expect(describeAttachmentsOnlyOutcome({ saved: [], failed: [], alreadyInSpaarke: already(2) })).toEqual({
      intent: 'info',
      title: 'These attachments are already in Spaarke. Nothing new was saved, and the email was not saved.',
    });
  });

  it('saved and already: the saved count alone', () => {
    expect(describeAttachmentsOnlyOutcome({ saved: ['a.pdf'], failed: [], alreadyInSpaarke: already(2) })).toEqual({
      intent: 'success',
      title: '1 attachment saved to Spaarke. The email was not saved.',
    });
  });

  it('saved, failed and already: the total is saved + failed', () => {
    expect(
      describeAttachmentsOnlyOutcome({
        saved: ['a.pdf'],
        failed: [{ name: 'b.pdf', message: 'no' }],
        alreadyInSpaarke: already(1),
      })
    ).toEqual({ intent: 'warning', title: '1 of 2 attachments saved. These were not saved:' });
  });
});

describe('existingDocumentOf (task 143, owner A1)', () => {
  it('reads the id and name; anything missing or malformed is withheld', () => {
    expect(existingDocumentOf({ existingDocument: { id: EXISTING_ID, name: EXISTING_NAME } })).toEqual({
      id: EXISTING_ID,
      name: EXISTING_NAME,
    });
    expect(existingDocumentOf({ contentDuplicate: true })).toBeUndefined();
    expect(existingDocumentOf({ existingDocument: { id: EXISTING_ID, name: '' } })).toBeUndefined();
    expect(existingDocumentOf({ existingDocument: { name: EXISTING_NAME } })).toBeUndefined();
    expect(existingDocumentOf({ existingDocument: null })).toBeUndefined();
    expect(existingDocumentOf(undefined)).toBeUndefined();
  });
});
