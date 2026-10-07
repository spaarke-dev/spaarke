/**
 * Unit tests for `OutlookAdapter.readDocumentStamp()` — spaarkeai-word-add-in-r1 task 051 (FR-02
 * client half). A NEW test file (ADR-038), mirroring the existing per-feature Outlook test files
 * (e.g. `OutlookAdapter.composeEmail.test.ts`) rather than editing the large, ungated
 * `OutlookAdapter.test.ts`.
 *
 * Outlook has no open document — this capability is Word-only. These tests pin the SAME convention
 * task 013 established for `getDocumentUrl()`: `canReadDocumentStamp` is unconditionally `false`,
 * and calling `readDocumentStamp()` anyway rejects with a typed `CAPABILITY_NOT_SUPPORTED`
 * `HostAdapterError` — never `undefined`, never a raw thrown `Error`, never a silent `null`.
 */
import { OutlookAdapter } from '../OutlookAdapter';

describe('OutlookAdapter.readDocumentStamp() (FR-02 client half / task 051)', () => {
  let adapter: OutlookAdapter;

  beforeEach(async () => {
    adapter = new OutlookAdapter();

    (global.Office as unknown as Record<string, unknown>).context = {
      ...global.Office.context,
      mailbox: {
        item: null,
        displayNewMessageForm: jest.fn(),
        userProfile: { displayName: 'Test User', emailAddress: 'testuser@example.com', timeZone: 'UTC' },
      },
      requirements: { isSetSupported: jest.fn().mockReturnValue(true) },
    };

    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Outlook });
      return Promise.resolve({ host: Office.HostType.Outlook });
    });

    await adapter.initialize();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('rejects with a typed CAPABILITY_NOT_SUPPORTED HostAdapterError, not undefined/a raw Error/a silent null', async () => {
    const rejection = adapter.readDocumentStamp();

    await expect(rejection).rejects.toMatchObject({ code: 'CAPABILITY_NOT_SUPPORTED' });
    await expect(rejection).rejects.not.toBeInstanceOf(Error);
  });

  it('reports canReadDocumentStamp as false, unconditionally — even when CustomXmlParts happens to report supported', () => {
    global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(true);

    expect(adapter.getCapabilities().canReadDocumentStamp).toBe(false);
  });

  it('reports canReadDocumentStamp as false when the requirement set is unsupported too (same answer either way — no open document in Outlook at all)', () => {
    global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(false);

    expect(adapter.getCapabilities().canReadDocumentStamp).toBe(false);
  });
});
