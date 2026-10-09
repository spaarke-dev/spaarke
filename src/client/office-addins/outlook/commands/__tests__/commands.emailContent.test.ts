/**
 * Task 116a (spaarkeai-word-add-in-r1): the Outlook ribbon Quick Save reads the email's body and attachments in the
 * add-in and sends them, like the pane — the server's Graph fetch cannot reach a B2B guest's home-tenant mailbox.
 *
 * Pins: body + every attachment are sent (the complete .eml), and the non-inline ones are named to become documents;
 * a cloud attachment is left out and the outcome notice says so; a read failure posts NOTHING; a host that cannot read attachment content
 * sends no content (the server's Graph path, unchanged).
 *
 * Same harness as commands.test.ts (fresh module per test; network/auth/prediction module-mocked). The read-mode
 * item carries `itemType` so `OutlookAdapter` sees read mode, as a real reading-pane item does.
 */

// A module (not a script), so these names do not collide with commands.test.ts in the type checker.
export {};

const mockAuthInitialize = jest.fn();
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
jest.mock('@shared/services', () => ({
  authService: { initialize: (...args: unknown[]) => mockAuthInitialize(...args) },
  apiClient: {
    configure: (...args: unknown[]) => mockApiConfigure(...args),
    post: (...args: unknown[]) => mockApiPost(...args),
  },
}));

const mockFetchEnginePreSelection = jest.fn();
jest.mock('@shared/taskpane/services/communicationSuggestionsService', () => ({
  fetchEnginePreSelection: (...args: unknown[]) => mockFetchEnginePreSelection(...args),
}));

// Task 118: the progress -> result dialog is shared with Word and covered by word/commands/__tests__/quickSaveDialog.test.ts;
// here it is replaced by a recording fake, so these tests assert what the command SAYS and when it completes.
const mockDialogShow = jest.fn();
const mockDialogFinish = jest.fn();
const mockOpenQuickSaveDialog = jest.fn();
jest.mock('@shared/commands/quickSaveDialog', () => ({
  openQuickSaveDialog: (...args: unknown[]) => mockOpenQuickSaveDialog(...args),
}));

type OfficeGlobal = {
  context: { mailbox: { item: unknown }; requirements: { isSetSupported: jest.Mock } };
  MailboxEnums: Record<string, unknown>;
  addin?: unknown;
};

const PREDICTION = {
  predicted: {
    id: '11111111-1111-1111-1111-111111111111',
    entityType: 'Matter',
    logicalName: 'sprk_matter',
    name: 'Acme Matter',
    canFile: true,
  },
  alternates: [],
  model: { state: 'suggested', candidates: [] },
};

const ATTACHMENTS = [
  { id: 'pdf', name: 'Contract.pdf', contentType: 'application/pdf', size: 3, isInline: false, attachmentType: 'file' },
  { id: 'logo', name: 'image001.png', contentType: 'image/png', size: 3, isInline: true, attachmentType: 'file' },
  {
    id: 'link',
    name: 'Plan.docx',
    contentType: 'application/octet-stream',
    size: 3,
    isInline: false,
    attachmentType: 'cloud',
  },
];

describe('outlook/commands/index.ts — quickSave sends the email content (task 116a)', () => {
  const office = (global as unknown as { Office: OfficeGlobal }).Office;
  const originalItem = office.context.mailbox.item;
  const originalNotificationType = office.MailboxEnums.ItemNotificationMessageType;
  const originalAddin = office.addin;

  let replaceAsync: jest.Mock;
  let getAttachmentContentAsync: jest.Mock;
  let bodyGetAsync: jest.Mock;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;

  beforeEach(() => {
    jest.resetModules();
    mockAuthInitialize.mockResolvedValue(undefined);
    mockApiConfigure.mockReturnValue(undefined);
    mockApiPost.mockReset();
    mockApiPost.mockResolvedValue({ jobId: 'job-1' });
    mockDialogShow.mockReset();
    mockDialogFinish.mockReset();
    mockOpenQuickSaveDialog.mockReset();
    mockOpenQuickSaveDialog.mockResolvedValue({ show: mockDialogShow, finish: mockDialogFinish });
    mockFetchEnginePreSelection.mockResolvedValue(PREDICTION);
    office.context.requirements.isSetSupported.mockReturnValue(true);

    replaceAsync = jest.fn();
    bodyGetAsync = jest.fn((_type: string, cb: (r: unknown) => void) =>
      cb({ status: 'succeeded', value: '<p>The email text</p>' })
    );
    getAttachmentContentAsync = jest.fn((_id: string, cb: (r: unknown) => void) =>
      cb({ status: 'succeeded', value: { content: 'YWJj', format: 'base64' } })
    );
    office.context.mailbox.item = {
      itemType: 'message',
      internetMessageId: '<abc@contoso.com>',
      subject: 'Re: Acme',
      from: { emailAddress: 'sender@contoso.com', displayName: 'The Sender' },
      to: [],
      cc: [],
      dateTimeCreated: new Date('2026-10-08T10:00:00Z'),
      body: { getAsync: bodyGetAsync },
      attachments: ATTACHMENTS,
      getAttachmentContentAsync,
      notificationMessages: { replaceAsync },
    };
    office.MailboxEnums.ItemNotificationMessageType = {
      InformationalMessage: 'informationalMessage',
      ErrorMessage: 'errorMessage',
    };
    office.addin = { showAsTaskpane: jest.fn().mockResolvedValue(undefined) };

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  afterAll(() => {
    office.context.mailbox.item = originalItem;
    office.MailboxEnums.ItemNotificationMessageType = originalNotificationType;
    office.addin = originalAddin;
  });

  /** Every result the command showed in the dialog, in order. */
  function messages(): string[] {
    return mockDialogShow.mock.calls.map(c => (c[0] as { message: string }).message);
  }

  it('sends the body and every attachment, names the non-inline ones for documents; a cloud link is reported', async () => {
    await commands.quickSave({ completed: jest.fn() });

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const email = (mockApiPost.mock.calls[0] as [string, { email: Record<string, unknown> }])[1].email;
    expect(email.body).toBe('<p>The email text</p>');
    expect(email.internetMessageId).toBe('<abc@contoso.com>');
    expect(email.attachments).toEqual([
      expect.objectContaining({ attachmentId: 'pdf', fileName: 'Contract.pdf', contentBase64: 'YWJj' }),
      expect.objectContaining({ attachmentId: 'logo', fileName: 'image001.png', isInline: true }),
    ]);
    // The inline image is in the .eml (part of the body) but does not become a document.
    expect(email.selectedAttachmentFileNames).toEqual(['Contract.pdf']);

    // The cloud link is never read.
    expect(getAttachmentContentAsync.mock.calls.map(c => c[0])).toEqual(['pdf', 'logo']);
    expect(messages()).toContain(
      'Saved to Spaarke and filed to Acme Matter. 1 attachment was left out: too large, a cloud link or unreadable.'
    );
  });

  it('a read failure posts NOTHING and says so', async () => {
    getAttachmentContentAsync.mockImplementation((_id: string, cb: (r: unknown) => void) =>
      cb({ status: 'failed', error: { message: 'not found' } })
    );

    await commands.quickSave({ completed: jest.fn() });

    expect(mockApiPost).not.toHaveBeenCalled();
    expect(mockDialogShow).toHaveBeenLastCalledWith({
      message: "Couldn't read this email or an attachment, so nothing was saved. Open Spaarke to try again.",
      status: 'error',
    });
  });

  it('a host without Mailbox 1.8 sends no content — the server fetches the email as before', async () => {
    office.context.requirements.isSetSupported.mockReturnValue(false);

    await commands.quickSave({ completed: jest.fn() });

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const email = (mockApiPost.mock.calls[0] as [string, { email: Record<string, unknown> }])[1].email;
    expect(email.body).toBeUndefined();
    expect('attachments' in email).toBe(false);
    expect(email.selectedAttachmentFileNames).toBeUndefined();
    expect(bodyGetAsync).not.toHaveBeenCalled();
    expect(messages()).toContain('Saved to Spaarke and filed to Acme Matter.');
  });
});
