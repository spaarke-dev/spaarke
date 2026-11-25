/**
 * GitHub #1571 review F1 (spaarkeai-word-add-in-r1 task 136, fix round 1): the Outlook ribbon's Quick Save signs in
 * from the click. A ribbon click IS a user gesture, so when the silent acquire found no token it calls `signIn()` ONCE;
 * when there is still no token it saves nothing and says "Open the Spaarke pane and sign in, then try again." — not
 * "Open Spaarke to try manually". A 401 that survives the request's retry says the same; other failures keep the old
 * text.
 *
 * Same harness as commands.test.ts, with the auth double's token and sign-in programmable.
 */

// A module, so its top-level doubles do not collide with commands.test.ts's in the type-check (TS6200).
export {};

const mockAuthInitialize = jest.fn();
const mockGetAccessToken = jest.fn();
const mockSignIn = jest.fn();
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
const mockApiGet = jest.fn();
jest.mock('@shared/services', () => ({
  authService: {
    initialize: (...args: unknown[]) => mockAuthInitialize(...args),
    // #1571 review F1: Quick Save checks for a token (and signs in once from the click when there is none).
    getAccessToken: (...args: unknown[]) => mockGetAccessToken(...args),
    signIn: (...args: unknown[]) => mockSignIn(...args),
  },
  apiClient: {
    configure: (...args: unknown[]) => mockApiConfigure(...args),
    post: (...args: unknown[]) => mockApiPost(...args),
    get: (...args: unknown[]) => mockApiGet(...args),
  },
}));

const mockFetchEnginePreSelection = jest.fn();
jest.mock('@shared/taskpane/services/communicationSuggestionsService', () => ({
  fetchEnginePreSelection: (...args: unknown[]) => mockFetchEnginePreSelection(...args),
}));

// The shared progress -> result dialog, replaced by a recording fake (it has its own suite).
const mockDialogShow = jest.fn();
const mockDialogFinish = jest.fn();
const mockOpenQuickSaveDialog = jest.fn();
jest.mock('@shared/commands/quickSaveDialog', () => ({
  openQuickSaveDialog: (...args: unknown[]) => mockOpenQuickSaveDialog(...args),
}));

const ORG = 'https://spaarkedev1.crm.dynamics.com';

function createMockEvent(): Office.AddinCommands.Event {
  return { completed: jest.fn() } as unknown as Office.AddinCommands.Event;
}

type OfficeGlobal = {
  context: { mailbox: { item: unknown } };
  MailboxEnums: Record<string, unknown>;
  addin?: unknown;
};

describe('outlook/commands/index.ts — Quick Save signs in from the ribbon click (#1571 review F1)', () => {
  const office = (global as unknown as { Office: OfficeGlobal }).Office;
  const originalItem = office.context.mailbox.item;
  const originalAddin = office.addin;
  const originalEnv = { ORG_URL: process.env.ORG_URL, SPAARKE_APP_NAME: process.env.SPAARKE_APP_NAME };

  /** What happened, in order: 'dialog-open', 'prediction', 'save'. */
  let order: string[];
  let replaceAsync: jest.Mock;
  let showAsTaskpane: jest.Mock;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;

  beforeEach(() => {
    jest.resetModules();
    order = [];
    process.env.ORG_URL = ORG;
    process.env.SPAARKE_APP_NAME = 'sprk_MatterManagement';
    mockAuthInitialize.mockResolvedValue(undefined);
    mockGetAccessToken.mockReset();
    mockGetAccessToken.mockResolvedValue('token');
    mockSignIn.mockReset();
    mockSignIn.mockResolvedValue(undefined);
    mockApiConfigure.mockReturnValue(undefined);
    mockApiPost.mockReset();
    mockApiPost.mockImplementation(async () => {
      order.push('save');
      return { jobId: 'job-1' };
    });
    mockApiGet.mockReset();
    mockFetchEnginePreSelection.mockReset();
    mockFetchEnginePreSelection.mockImplementation(async () => {
      order.push('prediction');
      return null;
    });
    mockDialogShow.mockReset();
    mockDialogFinish.mockReset();
    mockOpenQuickSaveDialog.mockReset();
    mockOpenQuickSaveDialog.mockImplementation(async () => {
      order.push('dialog-open');
      return { show: mockDialogShow, finish: mockDialogFinish };
    });

    replaceAsync = jest.fn();
    office.context.mailbox.item = {
      internetMessageId: '<abc@contoso.com>',
      subject: 'Re: Secure matter',
      from: { emailAddress: 'sender@contoso.com', displayName: 'The Sender' },
      to: [{ emailAddress: 'a@contoso.com', displayName: 'A' }],
      cc: [],
      dateTimeCreated: new Date('2026-09-30T10:00:00Z'),
      notificationMessages: { replaceAsync },
    };
    showAsTaskpane = jest.fn().mockResolvedValue(undefined);
    office.addin = { showAsTaskpane };

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  afterAll(() => {
    office.context.mailbox.item = originalItem;
    office.addin = originalAddin;
    process.env.ORG_URL = originalEnv.ORG_URL;
    process.env.SPAARKE_APP_NAME = originalEnv.SPAARKE_APP_NAME;
  });

  /** Every result the command showed in the dialog, in order. */
  function shown(): Array<{ message: string; status: string; linkUrl?: string | null }> {
    return mockDialogShow.mock.calls.map(c => c[0] as { message: string; status: string; linkUrl?: string | null });
  }

  const SIGN_IN_MESSAGE = 'Open the Spaarke pane and sign in, then try again.';

  it('with a silent token: no sign-in, the save runs', async () => {
    await commands.quickSave(createMockEvent());

    expect(mockSignIn).not.toHaveBeenCalled();
    expect(mockApiPost).toHaveBeenCalledTimes(1);
  });

  it('with no silent token: signs in ONCE from the click, then saves', async () => {
    mockGetAccessToken.mockResolvedValueOnce(null).mockResolvedValue('token');

    await commands.quickSave(createMockEvent());

    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockApiPost).toHaveBeenCalledTimes(1);
  });

  it('a sign-in that does not complete saves nothing and says to sign in from the pane', async () => {
    mockGetAccessToken.mockResolvedValue(null);
    mockSignIn.mockRejectedValue(new Error('Sign-in did not complete.'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockApiPost).not.toHaveBeenCalled();
    expect(shown()).toEqual([{ message: SIGN_IN_MESSAGE, status: 'error' }]);
    expect(mockDialogFinish).toHaveBeenCalledTimes(1);
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });

  it('a 401 that survived the retry says to sign in from the pane', async () => {
    mockApiPost.mockRejectedValue(
      Object.assign(new Error('Unauthorized'), {
        name: 'ApiClientError',
        error: { title: 'Unauthorized', status: 401 },
      })
    );
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(shown()).toEqual([{ message: SIGN_IN_MESSAGE, status: 'error' }]);
    errorSpy.mockRestore();
  });

  it('NEGATIVE: another failure keeps "Open Spaarke to try manually"', async () => {
    mockApiPost.mockRejectedValue(new Error('500'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(shown()).toEqual([{ message: 'Failed to save email. Open Spaarke to try manually.', status: 'error' }]);
    errorSpy.mockRestore();
  });
});
