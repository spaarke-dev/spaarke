/**
 * FilePreviewDialog — the email dialog gets the compose template picker + AI-draft handlers
 * (task 186, owner D-219 Q3, UAT F-R4-2 / F-R4-3).
 *
 * `EmailComposer` hides the template picker unless the host passes `onListEmailTemplates` AND
 * `onRenderEmailTemplate`, and hides the AI-draft sparkle unless it passes `onDraftWithAi`.
 * FilePreviewDialog rendered `<SendEmailDialog>` without them. This pins that the adapter forwards
 * the handlers built by `createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl })`.
 *
 * NOTE — LegalWorkspace has no jest runner of its own, so this test lives in the SpaarkeAi suite
 * (the Console embeds LegalWorkspace and CI runs that suite: `client-tests.yml`, job `test`, package
 * `src/solutions/SpaarkeAi`), mocking LegalWorkspace's services by relative path like
 * `workspaceGrid.launchSites.test.tsx`. Moved here in task 197 fix round 1 (it was never run in CI).
 *
 * @see LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx — unit under test
 */

import * as React from 'react';
import { render } from '@testing-library/react';

const sendEmailDialogProps: { current: Record<string, unknown> | null } = { current: null };
const onListEmailTemplatesSentinel = jest.fn();
const onRenderEmailTemplateSentinel = jest.fn();
const onDraftWithAiSentinel = jest.fn();
const onUploadLocalAttachmentSentinel = jest.fn();
const createXrmEmailComposeHandlersMock = jest.fn(() => ({
  onListEmailTemplates: onListEmailTemplatesSentinel,
  onRenderEmailTemplate: onRenderEmailTemplateSentinel,
  onDraftWithAi: onDraftWithAiSentinel,
  onUploadLocalAttachment: onUploadLocalAttachmentSentinel,
}));
const authenticatedFetchMock = jest.fn();
const BFF_BASE_URL = 'https://test-bff.example.com';

jest.mock('@spaarke/ui-components', () => ({
  SendEmailDialog: (props: Record<string, unknown>) => {
    sendEmailDialogProps.current = props;
    return <div data-testid="send-email-dialog-stub" />;
  },
  createXrmEmailComposeHandlers: (...args: unknown[]) =>
    (createXrmEmailComposeHandlersMock as unknown as (...a: unknown[]) => unknown)(...args),
  createXrmDataService: jest.fn(() => ({})),
  createXrmNavigationService: jest.fn(() => ({ openRecord: jest.fn(), openRecordModal: jest.fn() })),
  getXrm: jest.fn(() => undefined),
  // Task 197: the real helper (React-free, no Xrm), not a stub, so the test sees the real conversion.
  plainTextToHtml: jest.requireActual('@spaarke/ui-components/components/EmailComposer/composeOnlyText')
    .plainTextToHtml,
}));
jest.mock('@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog', () => ({
  RichFilePreviewDialog: () => <div data-testid="rich-file-preview-stub" />,
}));
jest.mock('../../../../../LegalWorkspace/src/services/DocumentApiService', () => ({
  getDocumentPreviewUrl: jest.fn(),
  getDocumentOpenLinks: jest.fn(),
}));
jest.mock('../../../../../LegalWorkspace/src/components/FilePreview/filePreviewService', () => ({
  copyDocumentLink: jest.fn(),
  setWorkspaceFlag: jest.fn(),
}));
jest.mock('@spaarke/ui-components/services/userLookup', () => ({ searchUsersAsLookup: jest.fn() }));
jest.mock('../../../../../LegalWorkspace/src/services/authInit', () => ({ authenticatedFetch: authenticatedFetchMock }));
jest.mock('../../../../../LegalWorkspace/src/config/runtimeConfig', () => ({ getBffBaseUrl: () => 'https://test-bff.example.com' }));

// Import AFTER the mocks.
import { FilePreviewDialog } from '../../../../../LegalWorkspace/src/components/FilePreview/FilePreviewDialog';

describe('FilePreviewDialog — email dialog compose handlers (task 186, D-219 Q3)', () => {
  beforeEach(() => {
    sendEmailDialogProps.current = null;
    createXrmEmailComposeHandlersMock.mockClear();
  });

  it('passes the template-list, template-render and AI-draft handlers to SendEmailDialog', () => {
    render(<FilePreviewDialog open documentId="doc-1" documentName="Contract.docx" onClose={jest.fn()} />);

    expect(sendEmailDialogProps.current).not.toBeNull();
    expect(sendEmailDialogProps.current?.onListEmailTemplates).toBe(onListEmailTemplatesSentinel);
    expect(sendEmailDialogProps.current?.onRenderEmailTemplate).toBe(onRenderEmailTemplateSentinel);
    expect(sendEmailDialogProps.current?.onDraftWithAi).toBe(onDraftWithAiSentinel);
    expect(sendEmailDialogProps.current?.onUploadLocalAttachment).toBe(onUploadLocalAttachmentSentinel);
  });

  it('builds the handlers from the module authenticatedFetch and the runtime BFF base URL', () => {
    render(<FilePreviewDialog open documentId="doc-1" documentName="Contract.docx" onClose={jest.fn()} />);

    expect(createXrmEmailComposeHandlersMock).toHaveBeenCalledWith({
      authenticatedFetch: authenticatedFetchMock,
      bffBaseUrl: BFF_BASE_URL,
    });
    // Send path unchanged: same fetch/BFF pair reaches the dialog.
    expect(sendEmailDialogProps.current?.authenticatedFetch).toBe(authenticatedFetchMock);
    expect(sendEmailDialogProps.current?.bffBaseUrl).toBe(BFF_BASE_URL);
    // Attachment-bearing association for the document is untouched.
    expect(sendEmailDialogProps.current?.associations).toEqual([{ entityType: 'sprk_document', entityId: 'doc-1' }]);
  });

  // Task 197 (owner dev recheck 2026-11-18): every composer host opens in rich text.
  it('opens the email body in rich text: the plain-text pre-fill is converted to HTML, its text and line breaks kept', () => {
    render(
      <FilePreviewDialog
        open
        documentId="doc-1"
        documentName="Fees <draft> & notes.docx"
        fileSummary={'Line one\nLine two < 5 & done'}
        onClose={jest.fn()}
      />
    );

    expect(sendEmailDialogProps.current?.initialBodyFormat).toBe('HTML');
    const body = sendEmailDialogProps.current?.initialBody as string;
    expect(body).toContain('<p>Document: Fees &lt;draft&gt; &amp; notes.docx</p>');
    expect(body).toContain('<p>Line one<br>Line two &lt; 5 &amp; done</p>');
    expect(body.startsWith('<p>Dear Colleague,</p>')).toBe(true);
    // Escaped once, never raw markup.
    expect(body).not.toContain('<draft>');
    expect(body).not.toContain('&amp;amp;');
  });
});
