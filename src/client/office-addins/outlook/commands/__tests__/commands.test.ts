/**
 * Unit tests for outlook/commands/index.ts — the Outlook ribbon one-click `quickSave` (FR-B2), as changed by
 * spaarkeai-word-add-in-r1 tasks 084 and 118:
 *   - #1075 (084): a fileable prediction is posted with the FRIENDLY type name ("Matter") the save accepts, never
 *     the logical name ("sprk_matter" -> 400 OFFICE_002).
 *   - task 118 (UAT round 12, O5): Quick Save shows the SAME progress -> result dialog as Word's (the controller is
 *     shared; here it is replaced by a recording fake and tested in word/commands/__tests__/quickSaveDialog.test.ts).
 *     The dialog opens BEFORE anything is read or saved; a prediction the caller cannot file to (084's
 *     `canFile === false`) or no prediction at all (incl. the 404 "not saved yet") saves the email UNFILED and says
 *     so — it never refuses, never posts the refused filing, and leaves no Outlook info bar behind.
 *
 * `quickSaveHelpers` is deliberately REAL (pure request shaping), so assertions are on the actual wire body. The
 * network (`apiClient`), auth bootstrap and the engine prediction are module-mocked. A fresh module per test (the
 * bootstrap flag is module-level) — the convention of word/commands/__tests__/commands.test.ts.
 */

const mockAuthInitialize = jest.fn();
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
const mockApiGet = jest.fn();
jest.mock('@shared/services', () => ({
  authService: { initialize: (...args: unknown[]) => mockAuthInitialize(...args) },
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

const UNFILED_MESSAGE = 'Saved to Spaarke — not filed to a record. Open Spaarke to file it.';

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const DOCUMENT_ID = '2bcfc5d2-0000-4000-8000-000000000001';
const ORG = 'https://spaarkedev1.crm.dynamics.com';

function prediction(canFile?: boolean | null) {
  return {
    predicted: {
      id: MATTER_ID,
      entityType: 'Matter',
      logicalName: 'sprk_matter',
      name: 'Secure Matter',
      ...(canFile !== undefined ? { canFile } : {}),
    },
    alternates: [],
    model: { state: 'suggested', candidates: [] },
  };
}

function createMockEvent(): Office.AddinCommands.Event {
  return { completed: jest.fn() } as unknown as Office.AddinCommands.Event;
}

type OfficeGlobal = {
  context: { mailbox: { item: unknown } };
  MailboxEnums: Record<string, unknown>;
  addin?: unknown;
};

describe('outlook/commands/index.ts — quickSave (tasks 084, 118)', () => {
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

  it('opens the dialog (progress) BEFORE the prediction is read or anything is saved, for the Outlook notify page', async () => {
    mockFetchEnginePreSelection.mockImplementation(async () => {
      order.push('prediction');
      return prediction(true);
    });

    await commands.quickSave(createMockEvent());

    expect(order).toEqual(['dialog-open', 'prediction', 'save']);
    expect(mockOpenQuickSaveDialog).toHaveBeenCalledWith(
      expect.objectContaining({ pagePath: '/outlook/commands-notify.html', orgUrl: ORG })
    );
  });

  it('a prediction the caller cannot file to saves the email UNFILED, says so, and posts no targetEntity (task 118)', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));

    await commands.quickSave(createMockEvent());

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const [route, body] = mockApiPost.mock.calls[0] as [string, Record<string, unknown>];
    expect(route).toBe('/api/office/save');
    expect('targetEntity' in body).toBe(false);
    expect((body.email as { internetMessageId: string }).internetMessageId).toBe('<abc@contoso.com>');
    expect(shown()).toEqual([{ message: UNFILED_MESSAGE, status: 'success', linkUrl: null }]);
    // No stale Outlook info bar, no pane opened.
    expect(replaceAsync).not.toHaveBeenCalled();
    expect(showAsTaskpane).not.toHaveBeenCalled();
    expect(mockDialogFinish).toHaveBeenCalledTimes(1);
  });

  it('no prediction (incl. the 404 "not saved yet") saves UNFILED, quietly — nothing logged', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(null);
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    expect('targetEntity' in (mockApiPost.mock.calls[0]![1] as object)).toBe(false);
    expect(shown().map(r => r.message)).toEqual([UNFILED_MESSAGE]);
    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });

  it('a prediction that fails to load (not a 404) still saves unfiled — a warning, never an error', async () => {
    mockFetchEnginePreSelection.mockRejectedValue(new Error('boom'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    expect(shown()[0]!.message).toBe(UNFILED_MESSAGE);
    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalled();
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });

  it.each([
    ['true', true],
    ['null', null],
    ['absent', undefined],
  ])('files a prediction whose canFile is %s, sending the FRIENDLY type name (#1075)', async (_label, canFile) => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(canFile));

    await commands.quickSave(createMockEvent());

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const [route, body] = mockApiPost.mock.calls[0] as [string, { targetEntity: Record<string, string> }];
    expect(route).toBe('/api/office/save');
    expect(body.targetEntity).toEqual({ entityType: 'Matter', entityId: MATTER_ID, displayName: 'Secure Matter' });
    expect(showAsTaskpane).not.toHaveBeenCalled();
    expect(shown().map(r => r.message)).toEqual(['Saved to Spaarke and filed to Secure Matter.']);
    expect(shown()[0]!.status).toBe('success');
  });

  it('success carries the "Open in Spaarke" link to the saved .eml sprk_document, read from the save job', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(true));
    mockApiPost.mockResolvedValue({ jobId: 'job-1', statusUrl: '/api/office/jobs/job-1' });
    mockApiGet.mockResolvedValue({ status: 'Completed', result: { artifact: { id: DOCUMENT_ID, webUrl: null } } });

    await commands.quickSave(createMockEvent());

    expect(mockApiGet).toHaveBeenCalledWith('/api/office/jobs/job-1');
    const result = shown()[0]!;
    expect(result.status).toBe('success');
    expect(result.linkUrl).toContain(`${ORG}/main.aspx`);
    expect(result.linkUrl).toContain('etn=sprk_document');
    expect(result.linkUrl).toContain(`id=${DOCUMENT_ID}`);
    expect(result.linkUrl).toContain('appname=sprk_MatterManagement');
  });

  it('an unfiled save also carries the record link', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));
    mockApiPost.mockResolvedValue({ jobId: 'job-1', statusUrl: '/api/office/jobs/job-1' });
    mockApiGet.mockResolvedValue({ status: 'Completed', result: { artifact: { id: DOCUMENT_ID } } });

    await commands.quickSave(createMockEvent());

    expect(shown()[0]!.message).toBe(UNFILED_MESSAGE);
    expect(shown()[0]!.linkUrl).toContain(`id=${DOCUMENT_ID}`);
  });

  it('NEGATIVE: without ORG_URL there is no link and the message is unchanged', async () => {
    process.env.ORG_URL = '';
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));
    mockApiPost.mockResolvedValue({ jobId: 'job-1', statusUrl: '/api/office/jobs/job-1' });
    mockApiGet.mockResolvedValue({ status: 'Completed', result: { artifact: { id: DOCUMENT_ID } } });

    await commands.quickSave(createMockEvent());

    expect(shown()).toEqual([{ message: UNFILED_MESSAGE, status: 'success', linkUrl: null }]);
  });

  it('a save the server could not finish shows the error, with no link', async () => {
    mockApiPost.mockResolvedValue({ jobId: 'job-1', statusUrl: '/api/office/jobs/job-1' });
    mockApiGet.mockResolvedValue({ status: 'Failed', error: { message: 'storage unavailable' } });

    await commands.quickSave(createMockEvent());

    expect(shown()).toEqual([
      { message: 'Spaarke could not finish saving this email: storage unavailable.', status: 'error' },
    ]);
  });

  it('a save request that fails shows the error and logs it', async () => {
    mockApiPost.mockRejectedValue(new Error('500'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    await commands.quickSave(createMockEvent());

    expect(shown()).toEqual([{ message: 'Failed to save email. Open Spaarke to try manually.', status: 'error' }]);
    expect(mockDialogFinish).toHaveBeenCalledTimes(1);
    errorSpy.mockRestore();
  });

  it('an unreadable email shows the error and posts nothing', async () => {
    office.context.mailbox.item = {};

    await commands.quickSave(createMockEvent());

    expect(mockApiPost).not.toHaveBeenCalled();
    expect(shown()).toEqual([{ message: 'Could not read the current email.', status: 'error' }]);
    expect(mockDialogFinish).toHaveBeenCalledTimes(1);
  });

  it('completes the ribbon command through the dialog (finish) exactly once, never directly', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(true));
    const event = createMockEvent();

    await commands.quickSave(event);

    // The command hands `event.completed` to the dialog controller, which calls it once the dialog is gone.
    expect(mockDialogFinish).toHaveBeenCalledTimes(1);
    expect(event.completed).not.toHaveBeenCalled();
    const { onComplete } = mockOpenQuickSaveDialog.mock.calls[0]![0] as { onComplete: () => void };
    onComplete();
    expect(event.completed).toHaveBeenCalledTimes(1);
  });

  it('names how many attachments were left out of the saved email', () => {
    const skipped = { skipped: [{}, {}] } as never;
    expect(commands.describeEmailSaved(null, skipped, false)).toBe(
      `${UNFILED_MESSAGE} 2 attachments were left out: too large, a cloud link or unreadable.`
    );
    expect(commands.describeEmailSaved({ name: 'M' }, undefined, true)).toBe(
      'This email is already saved in Spaarke. Nothing new was saved.'
    );
  });
});
