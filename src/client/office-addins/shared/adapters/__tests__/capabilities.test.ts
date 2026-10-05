/**
 * Unit tests for `HostCapabilities` — spaarkeai-word-add-in-r1 task 040 / FR-19 (Outlook parity pass
 * + capability-gating audit).
 *
 * A NEW test file (never modify/weaken an existing test file) that pins WHAT EACH ADAPTER HONESTLY
 * REPORTS for every capability, including the two this task added (`canShowLinkedTodos`,
 * `canSuggestRelatedRecords`, formalizing the pre-existing `hostType === 'outlook'` gates in
 * `App.tsx`'s LinkedTodosBanner and `SaveFlow.tsx`'s auto-match-candidates fetch). This is the
 * evidence `notes/parity-checklist.md` cites for each capability's per-host status — see that file
 * for the full audit + reasoning.
 *
 * `WordAdapter.test.ts` and `OutlookAdapter.test.ts` already exercise the rest of each adapter's
 * surface (and sit on this package's known-failing baseline for unrelated reasons); this file is
 * scoped narrowly to `getCapabilities()`.
 */

import { WordAdapter } from '../WordAdapter';
import { OutlookAdapter } from '../OutlookAdapter';
import type { HostCapabilities } from '../types';
import { getAvailableTabs } from '../../taskpane/components/TaskPaneNavigation';

describe('WordAdapter.getCapabilities()', () => {
  let adapter: WordAdapter;

  beforeEach(async () => {
    adapter = new WordAdapter();

    (global.Office as unknown as Record<string, unknown>).context = {
      ...global.Office.context,
      document: { url: '' },
      requirements: {
        isSetSupported: jest.fn().mockReturnValue(true),
      },
    };

    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Word });
      return Promise.resolve({ host: Office.HostType.Word });
    });

    await adapter.initialize();
  });

  afterEach(() => {
    jest.clearAllMocks();
    delete process.env.ADDIN_EMAIL_TAB_ENABLED;
  });

  it('reports the full capability set honestly (Word-only true, Outlook-only false)', () => {
    process.env.ADDIN_EMAIL_TAB_ENABLED = 'true';
    const capabilities: HostCapabilities = adapter.getCapabilities();

    // Outlook-only — Word has no mailbox/email concept for any of these.
    expect(capabilities.canGetAttachments).toBe(false);
    expect(capabilities.canGetRecipients).toBe(false);
    expect(capabilities.canGetSender).toBe(false);
    expect(capabilities.canSaveAsEml).toBe(false);
    expect(capabilities.canAttachFile).toBe(false);
    expect(capabilities.canComposeEmail).toBe(false);
    // Task 096: Word emails a document from its own Email tab (in-pane form, shared compose engine) —
    // when the build setting is on (this test sets it; the held-off default is pinned below).
    expect(capabilities.canEmailFromPane).toBe(true);

    // Word-only.
    expect(capabilities.canGetDocumentContent).toBe(true);
    expect(capabilities.canGetDocumentUrl).toBe(true);
    expect(capabilities.canInsertLink).toBe(true);

    // Shared.
    expect(capabilities.canSaveAsPdf).toBe(true);

    // task 040 / FR-19: the two capabilities this task added — both spec'd Outlook-only
    // (spec.md Assumptions: "linked-todos", "triage").
    expect(capabilities.canShowLinkedTodos).toBe(false);
    expect(capabilities.canSuggestRelatedRecords).toBe(false);

    // task 020 / FR-06 (converted from a hostType gate per this task's coordinator follow-up):
    // Word can supply the open document's own name as the Document Name field's default.
    expect(capabilities.canProvideDocumentName).toBe(true);

    // task 094: GA (not preview) requirement set WordApi 1.6 — isSetSupported stubbed true by beforeEach.
    expect(capabilities.canDetectDocumentChanges).toBe(true);
  });

  // Owner decision 2026-10-04 (task 097 note §6): the Email tab is held OFF until the send route authorizes
  // each attachment and association (unified-access-control-r2 task 161). Only the exact string "true" on.
  it.each([
    ['unset', undefined, false],
    ['"false"', 'false', false],
    ['"1"', '1', false],
    ['"TRUE"', 'TRUE', false],
    ['"true"', 'true', true],
  ])('canEmailFromPane follows the ADDIN_EMAIL_TAB_ENABLED build setting (%s)', (_label, value, expected) => {
    if (value === undefined) delete process.env.ADDIN_EMAIL_TAB_ENABLED;
    else process.env.ADDIN_EMAIL_TAB_ENABLED = value;

    const capabilities = adapter.getCapabilities();
    expect(capabilities.canEmailFromPane).toBe(expected);
    // The switch moves nothing else.
    expect(capabilities.canComposeEmail).toBe(false);
    expect(capabilities.canGetDocumentContent).toBe(true);
  });

  // The chain App.tsx uses (adapter capability → tab table), with the build setting as CI ships it: unset/off.
  it('with the build setting off, the real Word adapter yields a tab list with no Email tab', () => {
    delete process.env.ADDIN_EMAIL_TAB_ENABLED;
    const tabs = getAvailableTabs('word', { canEmailFromPane: adapter.getCapabilities().canEmailFromPane });
    expect(tabs.map(t => t.value)).not.toContain('email');

    process.env.ADDIN_EMAIL_TAB_ENABLED = 'true';
    const tabsOn = getAvailableTabs('word', { canEmailFromPane: adapter.getCapabilities().canEmailFromPane });
    expect(tabsOn.map(t => t.value)).toContain('email');
  });

  it('canOpenBrowserWindow follows the runtime requirement-set check (task 027), not a hardcoded value', () => {
    (global.Office.context.requirements.isSetSupported as jest.Mock).mockImplementation(
      (set: string) => set !== 'OpenBrowserWindowApi'
    );

    expect(adapter.getCapabilities().canOpenBrowserWindow).toBe(false);
  });

  // task 094: WordApi 1.6 is a genuinely conditional requirement set (same shape as OpenBrowserWindowApi
  // above) — a host can satisfy this adapter's WordApi 1.3 init floor and still lack 1.6.
  it('canDetectDocumentChanges is false when WordApi 1.6 is not supported, without affecting the other flags', () => {
    (global.Office.context.requirements.isSetSupported as jest.Mock).mockImplementation(
      (set: string, version?: string) => !(set === 'WordApi' && version === '1.6')
    );

    const capabilities = adapter.getCapabilities();
    expect(capabilities.canDetectDocumentChanges).toBe(false);
    expect(capabilities.canGetDocumentContent).toBe(true);
    expect(capabilities.canInsertLink).toBe(true);
  });

  // task 094: platform, never hostType — Office.context.platform is not part of the requirements mock,
  // so it is set directly on context for these two tests.
  it.each([
    ['PC', Office.PlatformType.PC, true],
    ['Mac', Office.PlatformType.Mac, true],
    ['OfficeOnline', Office.PlatformType.OfficeOnline, false],
  ])('canOpenDesktopWord is %s on platform %s', (_label, platform, expected) => {
    (global.Office.context as unknown as { platform?: Office.PlatformType }).platform = platform;

    expect(adapter.getCapabilities().canOpenDesktopWord).toBe(expected);
  });
});

describe('OutlookAdapter.getCapabilities()', () => {
  let adapter: OutlookAdapter;

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

  it('reports canShowLinkedTodos and canSuggestRelatedRecords as true in read mode', async () => {
    (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
    await adapter.initialize();

    const capabilities = adapter.getCapabilities();
    expect(capabilities.canShowLinkedTodos).toBe(true);
    expect(capabilities.canSuggestRelatedRecords).toBe(true);
  });

  it('reports canShowLinkedTodos and canSuggestRelatedRecords as true even with no item selected (unconditional — the consuming views/hooks are the ones that no-op on a missing id)', async () => {
    // No item set — mode stays 'unknown'.
    await adapter.initialize();

    const capabilities = adapter.getCapabilities();
    expect(capabilities.canShowLinkedTodos).toBe(true);
    expect(capabilities.canSuggestRelatedRecords).toBe(true);
    // Contrast: capabilities that DO depend on mode/item are correctly false here.
    expect(capabilities.canGetAttachments).toBe(false);
    expect(capabilities.canInsertLink).toBe(false);
  });

  it('keeps canEmailFromPane false even with the Word Email-tab build setting on (the switch is Word-only)', async () => {
    process.env.ADDIN_EMAIL_TAB_ENABLED = 'true';
    try {
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();
      expect(adapter.getCapabilities().canEmailFromPane).toBe(false);
    } finally {
      delete process.env.ADDIN_EMAIL_TAB_ENABLED;
    }
  });

  it('reports the full capability set honestly for read mode (Outlook-only true where applicable, Word-only false)', async () => {
    (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
    await adapter.initialize();

    const capabilities: HostCapabilities = adapter.getCapabilities();

    // Word-only — Outlook has no open-document concept.
    expect(capabilities.canGetDocumentContent).toBe(false);
    expect(capabilities.canGetDocumentUrl).toBe(false);

    // Outlook read-mode.
    expect(capabilities.canGetAttachments).toBe(true);
    expect(capabilities.canGetRecipients).toBe(true);
    expect(capabilities.canGetSender).toBe(true);
    expect(capabilities.canSaveAsEml).toBe(true);
    expect(capabilities.canComposeEmail).toBe(true);
    // Task 096 (owner: "Outlook unchanged"): no in-pane Email tab — Send Email opens native compose.
    expect(capabilities.canEmailFromPane).toBe(false);

    // Compose-only, so false in read mode.
    expect(capabilities.canInsertLink).toBe(false);
    expect(capabilities.canAttachFile).toBe(false);

    // Shared.
    expect(capabilities.canSaveAsPdf).toBe(true);

    // task 020 / FR-06: Outlook is false — not because it cannot supply a name (it has a subject),
    // but because the Document Name box already overrides that subject under a different contract
    // (task 046 (b)'s isNameSystemDerived). See the capability's doc comment in types.ts.
    expect(capabilities.canProvideDocumentName).toBe(false);

    // task 094: no open document in Outlook — content-change detection is Word-only.
    expect(capabilities.canDetectDocumentChanges).toBe(false);
  });

  // task 094: canOpenDesktopWord is PLATFORM, not hostType — it can be true on an Outlook-hosted pane too
  // (the collision prompt's "Open in Word" opens the COLLIDING FILE, always a Word document).
  it.each([
    ['PC', Office.PlatformType.PC, true],
    ['OfficeOnline', Office.PlatformType.OfficeOnline, false],
  ])('canOpenDesktopWord is %s on platform %s, independent of hostType', async (_label, platform, expected) => {
    (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
    (global.Office.context as unknown as { platform?: Office.PlatformType }).platform = platform;
    await adapter.initialize();

    expect(adapter.getCapabilities().canOpenDesktopWord).toBe(expected);
  });
});
