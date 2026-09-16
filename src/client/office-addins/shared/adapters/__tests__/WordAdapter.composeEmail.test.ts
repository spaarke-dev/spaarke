/**
 * Unit tests for WordAdapter's (lack of) Send Email compose support
 * (spaarkeai-word-add-in-r1 task 036 / FR-15).
 *
 * A NEW test file per this task's constraint (never modify/weaken an existing test file) — sibling
 * to `WordAdapter.test.ts`. `Office.context.mailbox` does not exist in Word, so this adapter is
 * always the "compose is not available" side of FR-15.
 */

import { WordAdapter } from '../WordAdapter';

describe('WordAdapter — Send Email compose (task 036 / FR-15)', () => {
  let adapter: WordAdapter;

  beforeEach(() => {
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
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('getCapabilities().canComposeEmail is always false', async () => {
    await adapter.initialize();

    expect(adapter.getCapabilities().canComposeEmail).toBe(false);
  });

  it('composeNewEmail returns a defined failure result rather than throwing', async () => {
    await adapter.initialize();

    const result = await adapter.composeNewEmail({ subject: 'Test', htmlBody: '<p>Test</p>' });

    expect(result.success).toBe(false);
    expect(result.errorMessage).toBeTruthy();
  });
});
