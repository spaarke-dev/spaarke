/**
 * Task 121 (spaarkeai-word-add-in-r1): the Outlook ribbon Quick Save sends the RFC Message-ID as
 * `email.internetMessageId` (as it always did) and now also the Exchange item id, in its own `email.exchangeItemId`
 * field — the key the server's Graph fallback fetches by when no content was read here (a host without Mailbox 1.8).
 * Graph cannot address a message by its RFC Message-ID, which was the only key Quick Save sent before task 121. (The
 * item id is sent exactly as Office reports it, as the task pane always has; it is not converted to Graph's REST id
 * format here.)
 *
 * Same harness as commands.emailContent.test.ts (fresh module per test; network/auth/prediction/dialog module-mocked).
 */

// A module (not a script), so these names do not collide with the other commands suites in the type checker.
export {};

const mockApiPost = jest.fn();
jest.mock('@shared/services', () => ({
  authService: { initialize: jest.fn() },
  apiClient: { configure: jest.fn(), post: (...args: unknown[]) => mockApiPost(...args) },
}));

jest.mock('@shared/taskpane/services/communicationSuggestionsService', () => ({
  fetchEnginePreSelection: jest.fn().mockResolvedValue(null),
}));

const mockOpenQuickSaveDialog = jest.fn();
jest.mock('@shared/commands/quickSaveDialog', () => ({
  openQuickSaveDialog: (...args: unknown[]) => mockOpenQuickSaveDialog(...args),
}));

type OfficeGlobal = {
  context: { mailbox: { item: unknown }; requirements: { isSetSupported: jest.Mock } };
  addin?: unknown;
};

const RFC_ID = '<abc@contoso.com>';
const ITEM_ID = 'AAMkAGI2TG93AAA=';

describe('outlook/commands/index.ts — quickSave sends both email keys (task 121)', () => {
  const office = (global as unknown as { Office: OfficeGlobal }).Office;
  const originalItem = office.context.mailbox.item;
  const originalAddin = office.addin;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;

  function readItem(extra: Record<string, unknown>) {
    return {
      itemType: 'message',
      internetMessageId: RFC_ID,
      subject: 'Re: Acme',
      from: { emailAddress: 'sender@contoso.com', displayName: 'The Sender' },
      to: [],
      cc: [],
      attachments: [],
      ...extra,
    };
  }

  beforeEach(() => {
    jest.resetModules();
    mockApiPost.mockReset();
    mockApiPost.mockResolvedValue({ jobId: 'job-1' });
    mockOpenQuickSaveDialog.mockReset();
    mockOpenQuickSaveDialog.mockResolvedValue({ show: jest.fn(), finish: jest.fn() });
    // No Mailbox 1.8 → no content read here: the request is the one the server's Graph fallback serves.
    office.context.requirements.isSetSupported.mockReturnValue(false);
    office.addin = { showAsTaskpane: jest.fn().mockResolvedValue(undefined) };
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  afterAll(() => {
    office.context.mailbox.item = originalItem;
    office.addin = originalAddin;
  });

  function sentEmail(): Record<string, unknown> {
    expect(mockApiPost).toHaveBeenCalledTimes(1);
    return (mockApiPost.mock.calls[0] as [string, { email: Record<string, unknown> }])[1].email;
  }

  it('sends the RFC Message-ID as internetMessageId and the item id as exchangeItemId', async () => {
    office.context.mailbox.item = readItem({ itemId: ITEM_ID });

    await commands.quickSave({ completed: jest.fn() });

    const email = sentEmail();
    expect(email.internetMessageId).toBe(RFC_ID);
    expect(email.exchangeItemId).toBe(ITEM_ID);
    expect(email.body).toBeUndefined();
  });

  it('an item without an item id sends no exchangeItemId', async () => {
    office.context.mailbox.item = readItem({});

    await commands.quickSave({ completed: jest.fn() });

    const email = sentEmail();
    expect(email.internetMessageId).toBe(RFC_ID);
    expect('exchangeItemId' in email).toBe(false);
  });
});
