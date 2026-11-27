/**
 * Task 143 (owner 2026-11-27): "we do want to be able to save an attachment even if the email is not saved".
 *
 * - The Outlook first save has a "Save the email" checkbox, ticked by default. Ticked: the email save, its request
 *   unchanged (pinned whole below).
 * - Unticked: one `contentType: 'Attachment'` save per ticked attachment, sequential, each with its own idempotency key,
 *   the bytes read in the pane, the open email as the parent (`parentEmailId` = its Message-ID, else its item id) and the
 *   picked record or none. Save is disabled while nothing is ticked. A partial failure is reported per attachment, never
 *   as "all saved"; afterwards no saved-email state is adopted, and the identity is re-read for the marks.
 * - The attachments the email lookup says were saved on their own show as saved and cannot be ticked; without that field
 *   nothing is marked.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker are doubled.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow, describeAttachmentsOnlyOutcome } from '../SaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../hooks/emailContentCaptureContext';
import type { EmailContentCapture } from '../../services/emailContentCapture';
import type { AttachmentInfo } from '@shared/adapters/types';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: (props: { onChange: (e: unknown) => void; disabled?: boolean }) =>
      ReactModule.createElement(
        'button',
        {
          type: 'button',
          disabled: props.disabled,
          onClick: () =>
            props.onChange({
              id: '{22222222-2222-2222-2222-222222222222}',
              entityType: 'Matter',
              logicalName: 'sprk_matter',
              name: 'Gamma Merger',
              displayInfo: 'MAT-000777',
            }),
        },
        'Pick matter'
      ),
  };
});
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

// A digest that differs per input, so two requests' idempotency keys can be told apart.
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

const MESSAGE_ID = '<CAF0-143-only@mail.example.com>';
const ITEM_ID = 'AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def=';
const A = 'Exhibit A.pdf';
const B = 'Exhibit B.pdf';

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
const accepted = () =>
  response(202, {
    jobId: 'job-143',
    statusUrl: '/api/office/jobs/job-143',
    streamUrl: '/api/office/jobs/job-143/stream',
    status: 'Queued',
    duplicate: false,
  });
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

function OutlookPane({
  attachments,
  internetMessageId = MESSAGE_ID,
  standaloneSavedAttachmentNames,
}: {
  attachments: AttachmentInfo[];
  internetMessageId?: string;
  standaloneSavedAttachmentNames?: string[];
}) {
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId={ITEM_ID}
          {...(internetMessageId ? { internetMessageId } : {})}
          itemName="Document: Lease renewal"
          senderEmail="opposing@counsel.example"
          sentDate={new Date(Date.UTC(2026, 10, 20, 12))}
          attachments={attachments}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          onComplete={onComplete}
          {...(standaloneSavedAttachmentNames !== undefined ? { standaloneSavedAttachmentNames } : {})}
          onRetryDocumentIdentity={retry}
        />
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

const saveCalls = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the request body is read loosely, as JSON
const bodyOf = (call: unknown[]) => JSON.parse((call[1] as RequestInit).body as string) as Record<string, any>;
const keyOf = (call: unknown[]) => ((call[1] as RequestInit).headers as Record<string, string>)['X-Idempotency-Key'];
const saveEmailBox = () => screen.getByRole('checkbox', { name: 'Save the email' });
const saveButton = () => screen.getByRole('button', { name: 'Save' });
const tick = (name: string) => fireEvent.click(screen.getByRole('checkbox', { name: `Select ${name}` }));
const stateOf = (name: string) =>
  (screen.getByText(name).closest('[data-saved-state]') as HTMLElement | null)?.getAttribute('data-saved-state');

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save') ? accepted() : response(200, { data: {} })
  );
});

describe('SaveFlow — "Save the email" (task 143)', () => {
  it('is ticked by default, and ticked the save is the email save exactly as before', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);
    expect(saveEmailBox()).toBeChecked();

    tick(A);
    fireEvent.click(saveButton());
    await waitFor(() => expect(saveCalls()).toHaveLength(1));

    expect(bodyOf(saveCalls()[0]!)).toEqual({
      contentType: 'Email',
      triggerAiProcessing: true,
      aiOptions: { profileSummary: true, ragIndex: true, deepAnalysis: false },
      email: {
        subject: 'Document: Lease renewal',
        isNameSystemDerived: true,
        senderEmail: 'opposing@counsel.example',
        recipients: [],
        sentDate: '2026-11-20T12:00:00.000Z',
        body: '<p>Please see attached.</p>',
        isBodyHtml: true,
        attachments: [A, B].map(name => ({
          attachmentId: `att-${name}`,
          fileName: name,
          size: 20_000,
          contentType: 'application/pdf',
          contentBase64: `BYTES-${name}`,
          isInline: false,
        })),
        internetMessageId: MESSAGE_ID,
        exchangeItemId: ITEM_ID,
        selectedAttachmentFileNames: [A],
      },
    });
  });

  it('negative: unticked with no attachment ticked, Save is disabled and nothing is sent', () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);
    fireEvent.click(saveEmailBox());
    expect(saveEmailBox()).not.toBeChecked();

    expect(saveButton()).toBeDisabled();
    fireEvent.click(saveButton());
    expect(saveCalls()).toHaveLength(0);
  });

  it('unticked, two attachments, no record: two Attachment saves with their bytes and the email as parent; no .eml', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);
    fireEvent.click(saveEmailBox());
    tick(A);
    tick(B);
    expect(saveButton()).toBeEnabled();

    fireEvent.click(saveButton());
    await waitFor(() => expect(screen.getByTestId('attachments-only-outcome')).toBeInTheDocument());

    const calls = saveCalls();
    expect(calls).toHaveLength(2);
    const bodies = calls.map(bodyOf);
    expect(bodies.map(b => b.contentType)).toEqual(['Attachment', 'Attachment']);
    expect(bodies.map(b => b.attachment.fileName)).toEqual([A, B]);
    bodies.forEach((b, i) => {
      const name = [A, B][i]!;
      expect(b.attachment).toEqual({
        attachmentId: `att-${name}`,
        fileName: name,
        contentType: 'application/pdf',
        size: 20_000,
        contentBase64: `BYTES-${name}`,
        parentEmailId: MESSAGE_ID,
      });
      expect(b.targetEntity).toBeUndefined();
      expect(b.email.internetMessageId).toBe(MESSAGE_ID);
      expect(b.email.exchangeItemId).toBe(ITEM_ID);
      expect(b.email.subject).toBe('Document: Lease renewal');
      // The email carries no bytes: each request carries its own file.
      expect(b.email.attachments.every((a: Record<string, unknown>) => !('contentBase64' in a))).toBe(true);
      expect(b.email.savedDocumentId).toBeUndefined();
      expect(b.email.selectedAttachmentFileNames).toBeUndefined();
    });
    expect(keyOf(calls[0]!)).not.toBe(keyOf(calls[1]!));

    expect(screen.getByText('2 attachments saved to Spaarke. The email was not saved.')).toBeInTheDocument();
    // Never adopted as a saved email: no completion, no "Saved to Spaarke" box; the identity is re-read for the marks.
    expect(onComplete).not.toHaveBeenCalled();
    expect(screen.queryByText('Saved to Spaarke')).toBeNull();
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('with a record picked, every attachment is filed to it (canonical id)', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);
    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    fireEvent.click(saveEmailBox());
    tick(A);
    tick(B);
    fireEvent.click(saveButton());
    await waitFor(() => expect(saveCalls()).toHaveLength(2));

    saveCalls()
      .map(bodyOf)
      .forEach(b =>
        expect(b.targetEntity).toEqual({
          entityType: 'Matter',
          entityId: '22222222-2222-2222-2222-222222222222',
          displayName: 'Gamma Merger',
        })
      );
  });

  it('without a Message-ID the parent is the Exchange item id', async () => {
    render(<OutlookPane attachments={[pdf(A)]} internetMessageId="" />);
    fireEvent.click(saveEmailBox());
    tick(A);
    fireEvent.click(saveButton());
    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    expect(bodyOf(saveCalls()[0]!).attachment.parentEmailId).toBe(ITEM_ID);
  });

  it('a partial failure names the attachment not saved and never says all were saved', async () => {
    let n = 0;
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save') ? (++n === 1 ? accepted() : forbidden()) : response(200, { data: {} })
    );
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);
    fireEvent.click(saveEmailBox());
    tick(A);
    tick(B);
    fireEvent.click(saveButton());

    await waitFor(() =>
      expect(screen.getByText('1 of 2 attachments saved. These were not saved:')).toBeInTheDocument()
    );
    const outcome = screen.getByTestId('attachments-only-outcome');
    expect(outcome).toHaveTextContent(`${B}:`);
    expect(outcome).not.toHaveTextContent(`${A}:`);
    expect(screen.queryByText(/2 attachments saved to Spaarke/)).toBeNull();
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('negative: nothing saved → no identity re-read', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save') ? forbidden() : response(200, { data: {} })
    );
    render(<OutlookPane attachments={[pdf(A)]} />);
    fireEvent.click(saveEmailBox());
    tick(A);
    fireEvent.click(saveButton());

    await waitFor(() => expect(screen.getByText('The attachment was not saved.')).toBeInTheDocument());
    expect(retry).not.toHaveBeenCalled();
  });
});

describe('SaveFlow — attachments saved on their own, in the first save (task 143)', () => {
  it('a name the lookup reports shows as saved and cannot be ticked; the others stay tickable', () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} standaloneSavedAttachmentNames={[A.toUpperCase()]} />);

    expect(stateOf(A)).toBe('saved');
    expect(screen.queryByRole('checkbox', { name: `Select ${A}` })).toBeNull();
    expect(stateOf(B)).toBe('unsaved');
    expect(screen.getByRole('checkbox', { name: `Select ${B}` })).toBeInTheDocument();
  });

  it('negative: no names from the lookup → no saved/unsaved claim, every attachment tickable as before', () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} />);

    expect(screen.queryAllByRole('img', { name: 'Saved' })).toHaveLength(0);
    expect(screen.getByRole('checkbox', { name: `Select ${A}` })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: `Select ${B}` })).toBeInTheDocument();
  });
});

describe('describeAttachmentsOnlyOutcome (task 143)', () => {
  it('success only when nothing failed; a warning for a partial failure; an error when nothing was saved', () => {
    expect(describeAttachmentsOnlyOutcome({ saved: [A], failed: [] }).intent).toBe('success');
    expect(describeAttachmentsOnlyOutcome({ saved: [A], failed: [{ name: B, message: 'x' }] })).toEqual({
      intent: 'warning',
      title: '1 of 2 attachments saved. These were not saved:',
    });
    expect(describeAttachmentsOnlyOutcome({ saved: [], failed: [{ name: B, message: 'x' }] }).intent).toBe('error');
  });
});
