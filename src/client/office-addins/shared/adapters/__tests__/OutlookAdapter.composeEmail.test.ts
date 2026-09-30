/**
 * Unit tests for OutlookAdapter's Send Email compose support
 * (spaarkeai-word-add-in-r1 task 036 / FR-15).
 *
 * A NEW test file per this task's constraint (never modify/weaken an existing test file) — sibling
 * to `OutlookAdapter.test.ts`, which already exercises the rest of the adapter surface and is on
 * this package's known-failing baseline for unrelated reasons.
 */

import { OutlookAdapter } from '../OutlookAdapter';

// `OutlookAdapter.determineMode` only classifies read vs compose when `'itemType' in item` is true
// (see OutlookAdapter.ts `determineMode`) — `itemType` + an `emailAddress`-shaped `from` is what
// makes this resolve to 'read' rather than 'unknown'.
const mockReadItem = {
  itemType: Office.MailboxEnums.ItemType.Message,
  itemId: 'test-item-123',
  subject: 'Test Email Subject',
  from: { emailAddress: 'sender@example.com', displayName: 'Test Sender' },
  internetMessageId: '<test-message-id@example.com>',
};

function setMailboxContext(overrides: { isSetSupported?: (set: string, version?: string) => boolean } = {}): void {
  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    mailbox: {
      item: null,
      displayNewMessageForm: jest.fn(),
      userProfile: {
        displayName: 'Test User',
        emailAddress: 'testuser@example.com',
        timeZone: 'UTC',
      },
    },
    requirements: {
      isSetSupported: jest.fn(overrides.isSetSupported ?? (() => true)),
    },
  };
}

describe('OutlookAdapter — Send Email compose (task 036 / FR-15)', () => {
  let adapter: OutlookAdapter;

  beforeEach(() => {
    adapter = new OutlookAdapter();
    setMailboxContext();
    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Outlook });
      return Promise.resolve({ host: Office.HostType.Outlook });
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('getCapabilities().canComposeEmail', () => {
    it('is true in read mode when Mailbox 1.6 is supported', async () => {
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();

      expect(adapter.getCapabilities().canComposeEmail).toBe(true);
    });

    it('is false when Mailbox 1.6 is not supported, even in read mode', async () => {
      setMailboxContext({ isSetSupported: (set: string) => set !== 'Mailbox' });
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();

      expect(adapter.getCapabilities().canComposeEmail).toBe(false);
    });

    it('is false in an unknown/uninitialized mode (no item set)', async () => {
      await adapter.initialize();

      expect(adapter.getCapabilities().canComposeEmail).toBe(false);
    });
  });

  describe('composeNewEmail', () => {
    it('opens displayNewMessageForm with the given subject and HTML body in read mode', async () => {
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();

      const result = await adapter.composeNewEmail({
        subject: 'Test Subject',
        htmlBody: '<p><a href="https://example.com/doc">Open document</a></p>',
      });

      expect(result).toEqual({ success: true });
      expect(global.Office.context.mailbox.displayNewMessageForm).toHaveBeenCalledWith({
        subject: 'Test Subject',
        htmlBody: '<p><a href="https://example.com/doc">Open document</a></p>',
      });
    });

    it('returns a defined failure result (does not throw) when composing is not supported', async () => {
      // No item set — mode stays 'unknown', so composing is not supported.
      await adapter.initialize();

      const result = await adapter.composeNewEmail({ subject: 'x', htmlBody: '<p>x</p>' });

      expect(result.success).toBe(false);
      expect(result.errorMessage).toBeTruthy();
      expect(global.Office.context.mailbox.displayNewMessageForm).not.toHaveBeenCalled();
    });

    it('returns a defined failure result when displayNewMessageForm throws synchronously', async () => {
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();
      (global.Office.context.mailbox.displayNewMessageForm as jest.Mock).mockImplementation(() => {
        throw new Error('Parameter exceeds size limit.');
      });

      const result = await adapter.composeNewEmail({ subject: 'x', htmlBody: '<p>x</p>' });

      expect(result).toEqual({ success: false, errorMessage: 'Parameter exceeds size limit.' });
    });
  });
});
