/**
 * FilePreviewDialog — the email dialog gets the compose template picker + AI-draft handlers
 * (task 186, owner D-219 Q3, UAT F-R4-2 / F-R4-3).
 *
 * `EmailComposer` hides the template picker unless the host passes `onListEmailTemplates` AND
 * `onRenderEmailTemplate`, and hides the AI-draft sparkle unless it passes `onDraftWithAi`.
 * FilePreviewDialog rendered `<SendEmailDialog>` without them. This pins that the adapter forwards
 * the handlers built by `createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl })`.
 *
 * NOTE — LegalWorkspace has no jest runner wired yet (see widthPreferenceGuard.test.ts, same
 * convention): this file is authored against the standard jest + @testing-library/react API so it
 * runs unchanged once a runner is added. It was run in task 186 under a throwaway jest config.
 *
 * @see ../FilePreviewDialog.tsx — unit under test
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
  createXrmNavigationService: jest.fn(() => ({ openRecord: jest.fn(), openRecordModal: jest.fn() })),
  getXrm: jest.fn(() => undefined),
}));
jest.mock('@spaarke/ui-components/components/FilePreview/RichFilePreviewDialog', () => ({
  RichFilePreviewDialog: () => <div data-testid="rich-file-preview-stub" />,
}));
jest.mock('../../../services/DocumentApiService', () => ({
  getDocumentPreviewUrl: jest.fn(),
  getDocumentOpenLinks: jest.fn(),
}));
jest.mock('../filePreviewService', () => ({
  copyDocumentLink: jest.fn(),
  setWorkspaceFlag: jest.fn(),
}));
jest.mock('../../CreateMatter/matterService', () => ({ searchUsersAsLookup: jest.fn() }));
jest.mock('../../../services/authInit', () => ({ authenticatedFetch: authenticatedFetchMock }));
jest.mock('../../../config/runtimeConfig', () => ({ getBffBaseUrl: () => 'https://test-bff.example.com' }));

// Import AFTER the mocks.
import { FilePreviewDialog } from '../FilePreviewDialog';

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
});
