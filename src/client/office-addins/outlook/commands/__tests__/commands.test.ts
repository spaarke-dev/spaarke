/**
 * Unit tests for outlook/commands/index.ts — the Outlook ribbon one-click `quickSave` (FR-B2), as changed
 * by spaarkeai-word-add-in-r1 task 084:
 *   - #1037: when the caller cannot file to the engine's PREDICTED record (`predicted.canFile === false`
 *     — the save would refuse it, filing needs AppendTo), the ribbon must NOT post a save. It shows the
 *     owner's verbatim notification and opens the pane, the same path as "no prediction".
 *   - #1075: a fileable prediction is posted with the FRIENDLY type name ("Matter") the save accepts,
 *     never the logical name ("sprk_matter" → 400 OFFICE_002).
 *
 * `quickSaveHelpers` is deliberately REAL (pure request shaping), so the #1075 assertion is on the actual
 * wire body. The network (`apiClient`), auth bootstrap and the engine prediction are module-mocked.
 *
 * The module caches its bootstrap state in a module-level `bootstrapped` flag and registers its
 * functions in `Office.onReady` at load, so each test `require`s a FRESH module (`jest.resetModules()`)
 * — the same convention as word/commands/__tests__/commands.test.ts.
 *
 * NEW FILE — ADR-038 (new tests in new files). First test file under outlook/ (jest.config.js `roots`
 * already lists `<rootDir>/outlook` for exactly this).
 */

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

/** The owner's copy, VERBATIM with {name} substituted (task 084 constraint "exact copy"). */
const notFileableMessage = (name: string) =>
  `Spaarke suggests ${name}, but you can't file to it. Open Spaarke to choose where to file.`;

const MATTER_ID = '11111111-1111-1111-1111-111111111111';

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

describe('outlook/commands/index.ts — quickSave (task 084)', () => {
  const office = (global as unknown as { Office: OfficeGlobal }).Office;
  const originalItem = office.context.mailbox.item;
  const originalNotificationType = office.MailboxEnums.ItemNotificationMessageType;
  const originalAddin = office.addin;

  let replaceAsync: jest.Mock;
  let showAsTaskpane: jest.Mock;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;

  beforeEach(() => {
    jest.resetModules();

    mockAuthInitialize.mockResolvedValue(undefined);
    mockApiConfigure.mockReturnValue(undefined);
    mockApiPost.mockResolvedValue({ jobId: 'job-1' });

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
    office.MailboxEnums.ItemNotificationMessageType = {
      InformationalMessage: 'informationalMessage',
      ErrorMessage: 'errorMessage',
    };
    showAsTaskpane = jest.fn().mockResolvedValue(undefined);
    office.addin = { showAsTaskpane };

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  afterAll(() => {
    office.context.mailbox.item = originalItem;
    office.MailboxEnums.ItemNotificationMessageType = originalNotificationType;
    office.addin = originalAddin;
  });

  /** Every message posted on the item, in order. */
  function messages(): string[] {
    return replaceAsync.mock.calls.map(c => (c[1] as { message: string }).message);
  }

  it('does NOT post a save when the prediction is not fileable; shows the verbatim notification and opens the pane', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));
    const event = createMockEvent();

    await commands.quickSave(event);

    expect(mockApiPost).not.toHaveBeenCalled();
    expect(messages()).toContain(notFileableMessage('Secure Matter'));
    // Posted as the same keyed informational notification as the other quick-save outcomes.
    expect(replaceAsync).toHaveBeenLastCalledWith(
      'spaarke_save',
      expect.objectContaining({
        type: 'informationalMessage',
        message: notFileableMessage('Secure Matter'),
      })
    );
    expect(showAsTaskpane).toHaveBeenCalledTimes(1);
    expect(messages().some(m => m.startsWith('Filed to'))).toBe(false);
    expect(event.completed).toHaveBeenCalledTimes(1);
  });

  it('still does not post, and reports no failure, when the host cannot open the pane programmatically', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));
    showAsTaskpane.mockRejectedValue(new Error('not supported'));
    const event = createMockEvent();

    await commands.quickSave(event);

    expect(mockApiPost).not.toHaveBeenCalled();
    expect(messages()).toContain(notFileableMessage('Secure Matter'));
    expect(messages()).not.toContain('Failed to save email. Open Spaarke to try manually.');
    expect(event.completed).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['true', true],
    ['null', null],
    ['absent', undefined],
  ])('files a prediction whose canFile is %s, sending the FRIENDLY type name (#1075)', async (_label, canFile) => {
    mockFetchEnginePreSelection.mockResolvedValue(prediction(canFile));
    const event = createMockEvent();

    await commands.quickSave(event);

    expect(mockApiPost).toHaveBeenCalledTimes(1);
    const [route, body] = mockApiPost.mock.calls[0] as [string, { targetEntity: Record<string, string> }];
    expect(route).toBe('/api/office/save');
    expect(body.targetEntity).toEqual({ entityType: 'Matter', entityId: MATTER_ID, displayName: 'Secure Matter' });
    expect(showAsTaskpane).not.toHaveBeenCalled();
    expect(messages()).toContain('Filed to Secure Matter.');
    expect(event.completed).toHaveBeenCalledTimes(1);
  });

  it('posts every quick-save message with replaceAsync under one key, so the outcome replaces "Saving to Spaarke…"', async () => {
    // addAsync with a key that already exists is refused by Outlook, which would leave "Saving…" on screen
    // and hide the outcome. Every spaarke_save message must therefore go through replaceAsync.
    mockFetchEnginePreSelection.mockResolvedValue(prediction(false));

    await commands.quickSave(createMockEvent());

    const keys = replaceAsync.mock.calls.map(c => c[0] as string);
    expect(keys[0]).toBe('spaarke_save');
    expect(messages()[0]).toBe('Saving to Spaarke…');
    expect(keys[keys.length - 1]).toBe('spaarke_save');
    expect(messages()[messages().length - 1]).toBe(notFileableMessage('Secure Matter'));
  });

  it("keeps the notice within Outlook's 150-character limit for a long record name, shortening only the name", async () => {
    const longName = `Matter ${'x'.repeat(140)}`;
    const p = prediction(false);
    p.predicted.name = longName;
    mockFetchEnginePreSelection.mockResolvedValue(p);

    await commands.quickSave(createMockEvent());

    const notice = messages()[messages().length - 1] ?? '';
    expect(notice.length).toBeLessThanOrEqual(150);
    expect(notice.startsWith('Spaarke suggests Matter xxx')).toBe(true);
    expect(notice.endsWith(", but you can't file to it. Open Spaarke to choose where to file.")).toBe(true);
    expect(mockApiPost).not.toHaveBeenCalled();
  });

  it('no prediction → opens the pane without posting (unchanged path the non-fileable branch mirrors)', async () => {
    mockFetchEnginePreSelection.mockResolvedValue(null);
    const event = createMockEvent();

    await commands.quickSave(event);

    expect(mockApiPost).not.toHaveBeenCalled();
    expect(messages()).toContain('No suggested record — open Spaarke to choose where to file.');
    expect(showAsTaskpane).toHaveBeenCalledTimes(1);
    expect(event.completed).toHaveBeenCalledTimes(1);
  });
});
