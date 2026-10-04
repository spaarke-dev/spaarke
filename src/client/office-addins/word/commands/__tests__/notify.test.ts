/**
 * word/commands/notify.html — the Word ribbon commands' notification window (task 037; timing changed by task 089).
 *
 * Task 089 (UAT-9): a Quick Save error now carries a reason the user must READ — often the server's own message —
 * so an error stays open for 8 s (or until Close is pressed) while a success still closes after 2.2 s. These tests
 * run the page's own inline script against its own markup (no copy of the logic), with fake timers.
 */

import * as fs from 'fs';
import * as path from 'path';

const HTML = fs.readFileSync(path.join(__dirname, '..', 'notify.html'), 'utf8');

/** The page's markup inside <body>, and its inline script — run exactly as the page ships them. */
function loadPage(search: string): { close: jest.Mock } {
  const body = /<body>([\s\S]*?)<script>/.exec(HTML)![1]!;
  const script = /<script>([\s\S]*?)<\/script>/.exec(HTML)![1]!;

  document.body.className = '';
  document.body.innerHTML = body;
  (window.location as unknown as { search: string }).search = search;

  const close = jest.fn();
  Object.defineProperty(window, 'close', { value: close, configurable: true, writable: true });

  // eslint-disable-next-line @typescript-eslint/no-implied-eval
  new Function(script)();
  return { close };
}

describe('notify.html — how long a notification stays readable (task 089)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    (window.location as unknown as { search: string }).search = '';
  });

  it('an ERROR stays open for at least 8 s, then closes itself', () => {
    const { close } = loadPage(
      `?message=${encodeURIComponent('Spaarke did not save this document: No access. (403)')}&status=error`
    );

    jest.advanceTimersByTime(7999);
    expect(close).not.toHaveBeenCalled();

    jest.advanceTimersByTime(1);
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('an ERROR shows its message as text (never markup), marked as an alert, with a Close button that closes it', () => {
    const { close } = loadPage(`?message=${encodeURIComponent('<b>no</b> access')}&status=error`);

    const message = document.getElementById('message')!;
    expect(message.textContent).toBe('<b>no</b> access');
    expect(message.querySelector('b')).toBeNull();
    expect(message.getAttribute('role')).toBe('alert');
    expect(document.body.classList.contains('error')).toBe(true);

    (document.getElementById('dismiss') as HTMLButtonElement).click();
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('a SUCCESS stays short: it closes after 2.2 s', () => {
    const { close } = loadPage(`?message=${encodeURIComponent("Saved to Spaarke as 'Brief.docx'.")}&status=info`);

    jest.advanceTimersByTime(2199);
    expect(close).not.toHaveBeenCalled();
    jest.advanceTimersByTime(1);
    expect(close).toHaveBeenCalledTimes(1);
    expect(document.body.classList.contains('error')).toBe(false);
  });
});
