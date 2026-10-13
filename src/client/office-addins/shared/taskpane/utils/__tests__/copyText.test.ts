/**
 * Task 116 (owner UAT 2026-10-08, B2B guest in Outlook on the web): the Clipboard API can be blocked by the host's
 * permissions policy. `copyText` falls back to the selection copy and reports whether anything was copied.
 */
import { copyText } from '../copyText';

describe('copyText (task 116)', () => {
  const originalClipboard = navigator.clipboard;
  let execCommand: jest.Mock;

  beforeEach(() => {
    execCommand = jest.fn(() => true);
    document.execCommand = execCommand;
  });

  afterAll(() => {
    Object.assign(navigator, { clipboard: originalClipboard });
  });

  it('uses the Clipboard API when it works', async () => {
    const writeText = jest.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });

    await expect(copyText('https://x/1')).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith('https://x/1');
    expect(execCommand).not.toHaveBeenCalled();
  });

  it('falls back to the selection copy when the Clipboard API is blocked, leaving no element behind', async () => {
    Object.assign(navigator, {
      clipboard: { writeText: jest.fn().mockRejectedValue(new DOMException('blocked', 'NotAllowedError')) },
    });
    const before = document.body.childElementCount;

    await expect(copyText('https://x/2')).resolves.toBe(true);
    expect(execCommand).toHaveBeenCalledWith('copy');
    expect(document.body.childElementCount).toBe(before);
  });

  it('falls back when there is no Clipboard API at all', async () => {
    Object.assign(navigator, { clipboard: undefined });
    await expect(copyText('https://x/3')).resolves.toBe(true);
    expect(execCommand).toHaveBeenCalledWith('copy');
  });

  it('reports false when every route fails, and never throws', async () => {
    Object.assign(navigator, { clipboard: { writeText: jest.fn().mockRejectedValue(new Error('denied')) } });
    execCommand.mockImplementation(() => {
      throw new Error('not allowed');
    });
    await expect(copyText('https://x/4')).resolves.toBe(false);

    execCommand.mockImplementation(() => false);
    await expect(copyText('https://x/4')).resolves.toBe(false);
  });
});
