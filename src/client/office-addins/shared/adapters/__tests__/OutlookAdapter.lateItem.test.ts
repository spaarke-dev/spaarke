/**
 * Task 139: an Outlook pane that opened with NO item (nothing selected, or several emails selected) must work once an
 * item is there. The adapter reads the item's read/compose mode at `initialize()`; with no item it cached 'unknown',
 * and every call after the user selected an email (the pane's Retry) failed with UNKNOWN_ERROR.
 */
import { OutlookAdapter } from '../OutlookAdapter';
import { captureEmailContent, hostAdapterEmailReader } from '../../taskpane/services/emailContentCapture';

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
    // (and, since review F3, an item that is not the one it served is refused outright)
    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'ITEM_CHANGED' });

    expect(adapter.getCapabilities().canResolveEmailIdentity).toBe(true);
  });
});

describe('OutlookAdapter: the email it serves cannot be swapped under a running save (review F3)', () => {
  const otherReadItem = { ...readItem, itemId: 'AAMkAD-item-2', subject: 'Another email' };

  it('refuses to read a different email than the one it first served: ITEM_CHANGED, with a message the user can act on', async () => {
    setMailboxItem(readItem);
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    await expect(adapter.getItemId()).resolves.toBe('AAMkAD-item-1');

    setMailboxItem(otherReadItem);

    for (const read of [() => adapter.getItemId(), () => adapter.getSubject(), () => adapter.getBody('html')]) {
      await expect(read()).rejects.toMatchObject({
        code: 'ITEM_CHANGED',
        message: 'The selected email changed. Open the pane on the email you want to save.',
      });
    }
  });

  it('a save that is collecting content when the email changes stops with that message, and posts nothing of the new email', async () => {
    const body = jest.fn((_type: unknown, callback: (r: unknown) => void) =>
      callback({ status: Office.AsyncResultStatus.Succeeded, value: '<p>body of A</p>' })
    );
    setMailboxItem({ ...readItem, body: { getAsync: body } });
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    // The user selects email B before the save reads the body.
    setMailboxItem({ ...otherReadItem, body: { getAsync: body } });

    await expect(captureEmailContent(hostAdapterEmailReader(adapter), [], new Set())).rejects.toThrow(
      /nothing was saved.*The selected email changed. Open the pane on the email you want to save./
    );
    expect(body).not.toHaveBeenCalled();
  });

  it('serves the same email again when the same item comes back', async () => {
    setMailboxItem(readItem);
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    setMailboxItem(otherReadItem);
    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'ITEM_CHANGED' });

    setMailboxItem(readItem);

    await expect(adapter.getItemId()).resolves.toBe('AAMkAD-item-1');
  });

  it('is not tripped by Outlook handing out a new object for the SAME email', async () => {
    setMailboxItem(readItem);
    const adapter = new OutlookAdapter();
    await adapter.initialize();

    setMailboxItem({ ...readItem });

    await expect(adapter.getItemId()).resolves.toBe('AAMkAD-item-1');
  });

  it('an adapter that never saw an item adopts the first one it is given, and then guards it', async () => {
    setMailboxItem(null);
    const adapter = new OutlookAdapter();
    await adapter.initialize();
    setMailboxItem(readItem);
    await expect(adapter.getItemId()).resolves.toBe('AAMkAD-item-1');

    setMailboxItem(otherReadItem);

    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'ITEM_CHANGED' });
  });

  it('a NEW adapter (what the pane creates on ItemChanged) serves the newly selected email', async () => {
    setMailboxItem(readItem);
    const first = new OutlookAdapter();
    await first.initialize();

    setMailboxItem(otherReadItem);
    const second = new OutlookAdapter();
    await second.initialize();

    await expect(second.getItemId()).resolves.toBe('AAMkAD-item-2');
    await expect(first.getItemId()).rejects.toMatchObject({ code: 'ITEM_CHANGED' });
  });
});

describe('OutlookAdapter compose: an item id that does not exist yet (review F2)', () => {
  const composeItem = (getItemIdAsync?: unknown) => ({
    itemType: 'message',
    from: { getAsync: jest.fn() },
    subject: { getAsync: jest.fn(), setAsync: jest.fn() },
    ...(getItemIdAsync ? { getItemIdAsync } : {}),
  });

  it('an unsaved draft (ItemNotSaved) has no id: "" rather than a failure', async () => {
    setMailboxItem(
      composeItem((callback: (r: unknown) => void) =>
        callback({
          status: Office.AsyncResultStatus.Failed,
          error: { name: 'ItemNotSaved', message: 'The item has not been saved yet.' },
        })
      )
    );
    const adapter = new OutlookAdapter();
    await adapter.initialize();

    await expect(adapter.getItemId()).resolves.toBe('');
  });

  it('a client without getItemIdAsync (below Mailbox 1.8) has no id: "" rather than a TypeError', async () => {
    setMailboxItem(composeItem());
    const adapter = new OutlookAdapter();
    await adapter.initialize();

    await expect(adapter.getItemId()).resolves.toBe('');
  });

  it('any OTHER getItemIdAsync failure is still a CONTENT_RETRIEVAL_FAILED', async () => {
    setMailboxItem(
      composeItem((callback: (r: unknown) => void) =>
        callback({ status: Office.AsyncResultStatus.Failed, error: { name: 'InternalError', message: 'boom' } })
      )
    );
    const adapter = new OutlookAdapter();
    await adapter.initialize();

    await expect(adapter.getItemId()).rejects.toMatchObject({ code: 'CONTENT_RETRIEVAL_FAILED', message: 'boom' });
  });
});
