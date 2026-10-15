/**
 * Task 116a (spaarkeai-word-add-in-r1): `SaveView` provides the live email reader the save uses, and shows the
 * selected attachments a save left out.
 *
 * Pins the seam between `SaveView` and Office.js (the same narrow approach as `SaveView.captureAtSave.test.tsx`;
 * `SaveFlow` is mocked and reads the context the real `useSaveFlow` reads):
 * - the reader is provided only when the adapter can read attachment content (`canGetAttachments`), never on a
 *   `hostType` check (NFR-10), and mounting reads nothing;
 * - calling it reads the HTML body and every attachment through the adapter (the complete .eml), naming only the
 *   ticked ones for documents;
 * - an attachment it leaves out is shown, by name, with its reason — never silent.
 */
import { useContext } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveView } from '../SaveView';
import { SaveFlow } from '../../SaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../../hooks/emailContentCaptureContext';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';
import type { AttachmentInfo, HostCapabilities } from '@shared/adapters/types';

let providedCapture: CaptureEmailContent | undefined;

jest.mock('../../SaveFlow', () => ({ SaveFlow: jest.fn(() => null) }));
const mockedSaveFlow = SaveFlow as unknown as jest.Mock;

const OUTLOOK_CAPABILITIES: HostCapabilities = {
  canGetAttachments: true,
  canGetRecipients: true,
  canGetSender: true,
  canGetDocumentContent: false,
  canGetDocumentUrl: false,
  canReadDocumentStamp: false,
  canWriteDocumentStamp: false,
  canSaveAsPdf: false,
  canSaveAsEml: true,
  canInsertLink: false,
  canAttachFile: false,
  canOpenBrowserWindow: false,
  canComposeEmail: false,
  canEmailFromPane: false,
  canShowLinkedTodos: true,
  canSuggestRelatedRecords: true,
  canProvideDocumentName: false,
  canDetectDocumentChanges: false,
  canOpenDesktopWord: false,
  minApiVersion: '1.5',
  supportedRequirementSet: 'Mailbox 1.8',
};

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'a', name: 'Contract.pdf', contentType: 'application/pdf', size: 3, isInline: false, attachmentType: 'file' },
  { id: 'b', name: 'Notes.pdf', contentType: 'application/pdf', size: 3, isInline: false, attachmentType: 'file' },
  {
    id: 'c',
    name: 'Plan.docx',
    contentType: 'application/octet-stream',
    size: 3,
    isInline: false,
    attachmentType: 'cloud',
  },
];

function makeOutlookAdapter(capabilities: HostCapabilities = OUTLOOK_CAPABILITIES): IHostAdapter {
  return {
    getHostType: () => 'outlook',
    getItemId: jest.fn().mockResolvedValue('<msg@contoso.com>'),
    getItemType: () => 'email',
    getSubject: jest.fn().mockResolvedValue('Re: Filing'),
    getBody: jest.fn().mockResolvedValue({ content: '<p>Hi</p>', type: 'html' }),
    getAttachments: jest.fn().mockResolvedValue(ATTACHMENTS),
    getAttachmentContent: jest.fn(async (id: string) => ({
      ...ATTACHMENTS.find(a => a.id === id)!,
      content: 'YWJj',
      contentFormat: 'base64' as const,
    })),
    getSenderEmail: jest.fn().mockResolvedValue('counsel@contoso.com'),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn(),
    getDocumentUrl: jest.fn().mockResolvedValue(null),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    writeDocumentStamp: jest.fn(),
    getCapabilities: () => capabilities,
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: () => true,
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    registerDocumentChangeHandler: jest.fn().mockResolvedValue(() => undefined),
  };
}

function renderSaveView(hostAdapter: IHostAdapter) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveView
        hostAdapter={hostAdapter}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

beforeEach(() => {
  providedCapture = undefined;
  mockedSaveFlow.mockReset();
  mockedSaveFlow.mockImplementation(() => {
    // eslint-disable-next-line react-hooks/rules-of-hooks -- this mock IS a component; React calls it as one
    providedCapture = useContext(EmailContentCaptureContext);
    return null;
  });
});

describe('SaveView — the email reader for the save (task 116a)', () => {
  it('provides a reader when the adapter can read attachment content, and mounting reads nothing', async () => {
    const adapter = makeOutlookAdapter();
    renderSaveView(adapter);
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    expect(typeof providedCapture).toBe('function');
    expect(adapter.getBody).not.toHaveBeenCalled();
    expect(adapter.getAttachmentContent).not.toHaveBeenCalled();
  });

  it('provides NO reader when the adapter cannot read attachment content (the server Graph path stays)', async () => {
    renderSaveView(makeOutlookAdapter({ ...OUTLOOK_CAPABILITIES, canGetAttachments: false }));
    await waitFor(() => expect(mockedSaveFlow).toHaveBeenCalled());

    expect(providedCapture).toBeUndefined();
  });

  it('reads the HTML body and every attachment through the adapter; only the ticked one is named for a document', async () => {
    const adapter = makeOutlookAdapter();
    renderSaveView(adapter);
    await waitFor(() => expect(providedCapture).toBeDefined());

    const files = ATTACHMENTS.filter(a => a.attachmentType === 'file');
    let captured: Awaited<ReturnType<CaptureEmailContent>> | undefined;
    await act(async () => {
      captured = await providedCapture!(files, new Set(['a']));
    });

    expect(adapter.getBody).toHaveBeenCalledWith('html');
    expect((adapter.getAttachmentContent as jest.Mock).mock.calls.map(c => c[0])).toEqual(['a', 'b']);
    expect(captured?.body).toBe('<p>Hi</p>');
    expect(captured?.attachments.map(x => x.fileName)).toEqual(['Contract.pdf', 'Notes.pdf']);
    expect(captured?.selectedAttachmentFileNames).toEqual(['Contract.pdf']);
    expect(screen.queryByTestId('save-skipped-attachments')).toBeNull();
  });

  it('shows each attachment the save left out, by name and reason, while the rest are sent', async () => {
    const adapter = makeOutlookAdapter();
    renderSaveView(adapter);
    await waitFor(() => expect(providedCapture).toBeDefined());

    let captured: Awaited<ReturnType<CaptureEmailContent>> | undefined;
    await act(async () => {
      captured = await providedCapture!(ATTACHMENTS, new Set(['a', 'c']));
    });

    expect(captured?.attachments.map(x => x.fileName)).toEqual(['Contract.pdf', 'Notes.pdf']);
    expect(captured?.selectedAttachmentFileNames).toEqual(['Contract.pdf']);
    const notice = await screen.findByTestId('save-skipped-attachments');
    expect(notice).toHaveTextContent('One attachment was not saved');
    expect(notice).toHaveTextContent(
      '"Plan.docx" was not saved with the email: it is a link to a file stored in the cloud'
    );
  });

  it('a read failure rejects with the reader message (useSaveFlow shows it; nothing is sent)', async () => {
    const adapter = makeOutlookAdapter();
    (adapter.getBody as jest.Mock).mockRejectedValue({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'boom' });
    renderSaveView(adapter);
    await waitFor(() => expect(providedCapture).toBeDefined());

    await act(async () => {
      await expect(providedCapture!(ATTACHMENTS, new Set(['a']))).rejects.toThrow(
        "Couldn't read this email's text, so nothing was saved. Try again. (boom)"
      );
    });
    expect(adapter.getAttachmentContent).not.toHaveBeenCalled();
  });
});
