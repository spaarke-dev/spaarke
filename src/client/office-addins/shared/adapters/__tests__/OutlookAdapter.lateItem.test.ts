/**
 * Task 139: an Outlook pane that opened with NO item (nothing selected, or several emails selected) must work once an
 * item is there. The adapter reads the item's read/compose mode at `initialize()`; with no item it cached 'unknown',
 * and every call after the user selected an email (the pane's Retry) failed with UNKNOWN_ERROR.
 */
import { OutlookAdapter } from '../OutlookAdapter';

const readItem = {
  itemType: 'message',
  itemId: 'AAMkAD-item-1',
  subject: 'Discovery schedule',
  from: { emailAddress: 'sender@example.com', displayName: 'Sender' },
  internetMessageId: '<id@example.com>',
};

const originalMailbox = Office.context.mailbox;

/** Selecting another email changes `item` on the SAME mailbox object the adapter captured at initialize(). */
function setMailboxItem(item: unknown) {
  const context = Office.context as unknown as { mailbox: { item: unknown } };
  if (context.mailbox === originalMailbox || !context.mailbox) {
    context.mailbox = { item };
  } else {
    context.mailbox.item = item;
  }
}

afterEach(() => {
  (Office.context as unknown as { mailbox: unknown }).mailbox = originalMailbox;
});

describe('OutlookAdapter opened with no item', () => {
  it('refuses with NO_ITEM_SELECTED while there is no item', async () => {
    setMailboxItem(null);
    const adapter = new OutlookAdapter();
    await adapter.initialize();

    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'NO_ITEM_SELECTED' });
  });

  it('reads the item (and its mode) once one is selected', async () => {
    setMailboxItem(null);
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'NO_ITEM_SELECTED' });

    setMailboxItem(readItem);

    await expect(adapter.getItemId()).resolves.toBe('AAMkAD-item-1');
    expect(adapter.getCapabilities().canResolveEmailIdentity).toBe(true);
  });

  it('does not re-derive a known mode: an adapter started on an item keeps that item mode', async () => {
    setMailboxItem(readItem);
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    expect(adapter.getCapabilities().canResolveEmailIdentity).toBe(true);

    // A compose item arrives under the same adapter: the pane follows the selection with a NEW adapter, not this one.
    setMailboxItem({
      itemType: 'message',
      from: { getAsync: jest.fn() },
      subject: { getAsync: jest.fn(), setAsync: jest.fn() },
      getItemIdAsync: jest.fn(),
    });
    await adapter.getItemId().catch(() => undefined);

    expect(adapter.getCapabilities().canResolveEmailIdentity).toBe(true);
  });
});
