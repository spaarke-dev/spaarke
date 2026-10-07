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

describe('notify.html — the Quick Save progress / result states (task 110)', () => {
  const LINK =
    'https://spaarkedev1.crm.dynamics.com/main.aspx?etn=sprk_document&id=2bcfc5d2-0000-4000-8000-000000000001';
  type Handler = (arg: { message: string }) => void;
  const originalOffice = (window as unknown as { Office?: unknown }).Office;
  let parentHandler: Handler | null;
  let messageParent: jest.Mock;

  /** Installs a minimal Office.js for the page's optional bridge; `withOffice: false` removes it entirely. */
  function installOffice(withOffice: boolean): void {
    parentHandler = null;
    messageParent = jest.fn();
    (window as unknown as { Office?: unknown }).Office = withOffice
      ? {
          onReady: (cb: () => void) => cb(),
          context: {
            ui: {
              messageParent,
              addHandlerAsync: (_type: string, handler: Handler) => {
                parentHandler = handler;
              },
            },
          },
        }
      : undefined;
  }

  /** The command updates the open dialog (messageChild). */
  function commandSends(payload: unknown): void {
    parentHandler!({ message: JSON.stringify(payload) });
  }

  const posted = (): unknown[] => messageParent.mock.calls.map(c => JSON.parse(c[0] as string));

  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
    (window as unknown as { Office?: unknown }).Office = originalOffice;
    (window.location as unknown as { search: string }).search = '';
  });

  it('PROGRESS: shows the spinner and the message, no Close button, and never closes itself', () => {
    installOffice(false);
    const { close } = loadPage(`?message=${encodeURIComponent('Saving to Spaarke…')}&status=progress`);

    expect(document.getElementById('message')!.textContent).toBe('Saving to Spaarke…');
    expect(document.body.classList.contains('progress')).toBe(true);
    expect(document.body.classList.contains('done')).toBe(false);

    jest.advanceTimersByTime(10 * 60 * 1000);
    expect(close).not.toHaveBeenCalled();
  });

  it('SUCCESS: shows the message and the "Open in Spaarke" link, and stays open until it is closed', () => {
    installOffice(false);
    const { close } = loadPage(
      `?message=${encodeURIComponent("Saved to Spaarke as 'A.docx'.")}&status=success&linkUrl=${encodeURIComponent(LINK)}`
    );

    const link = document.getElementById('link') as HTMLAnchorElement;
    expect(link.classList.contains('visible')).toBe(true);
    expect(link.getAttribute('href')).toBe(LINK);
    expect(link.textContent).toBe('Open in Spaarke');
    expect(document.body.classList.contains('done')).toBe(true);

    jest.advanceTimersByTime(10 * 60 * 1000);
    expect(close).not.toHaveBeenCalled();

    (document.getElementById('dismiss') as HTMLButtonElement).click();
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('SUCCESS without a link shows no link (and still stays open)', () => {
    installOffice(false);
    const { close } = loadPage(`?message=${encodeURIComponent('Saved to Spaarke.')}&status=success`);

    expect((document.getElementById('link') as HTMLAnchorElement).classList.contains('visible')).toBe(false);
    jest.advanceTimersByTime(60 * 1000);
    expect(close).not.toHaveBeenCalled();
  });

  it.each([
    ['a javascript: URL', 'javascript:alert(1)'],
    ['a data: URL', 'data:text/html,<b>x</b>'],
    ['a non-URL', 'main.aspx'],
  ])('NEGATIVE: never shows %s as the link', (_label, linkUrl) => {
    installOffice(false);
    loadPage(`?message=Saved&status=success&linkUrl=${encodeURIComponent(linkUrl)}`);

    const link = document.getElementById('link') as HTMLAnchorElement;
    expect(link.classList.contains('visible')).toBe(false);
    expect(link.getAttribute('href')).toBe('#');
  });

  it('NEGATIVE: an error never shows a link, even if the URL carries one', () => {
    installOffice(false);
    loadPage(`?message=No&status=error&linkUrl=${encodeURIComponent(LINK)}`);

    expect((document.getElementById('link') as HTMLAnchorElement).classList.contains('visible')).toBe(false);
  });

  it('without Office.js the link is left to the anchor itself (not intercepted)', () => {
    installOffice(false);
    loadPage(`?message=Saved&status=success&linkUrl=${encodeURIComponent(LINK)}`);

    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    document.getElementById('link')!.dispatchEvent(click);

    expect(click.defaultPrevented).toBe(false);
  });

  describe('with Office.js (the command can update and talk to it)', () => {
    it('tells the command it is ready', () => {
      installOffice(true);
      loadPage(`?message=Saving&status=progress`);

      expect(posted()).toEqual([{ type: 'ready' }]);
    });

    it('is updated in place from progress to a success with a link', () => {
      installOffice(true);
      loadPage(`?message=Saving&status=progress`);

      commandSends({ type: 'result', status: 'success', message: "Saved to Spaarke as 'A.docx'.", linkUrl: LINK });

      expect(document.getElementById('message')!.textContent).toBe("Saved to Spaarke as 'A.docx'.");
      expect(document.body.classList.contains('progress')).toBe(false);
      expect(document.body.classList.contains('done')).toBe(true);
      expect((document.getElementById('link') as HTMLAnchorElement).getAttribute('href')).toBe(LINK);
    });

    it('is updated in place to an error: text only, alert role, no link, closes itself after 8 s', () => {
      installOffice(true);
      const { close } = loadPage(`?message=Saving&status=progress`);

      commandSends({ type: 'result', status: 'error', message: '<b>no</b> access', linkUrl: LINK });

      const message = document.getElementById('message')!;
      expect(message.textContent).toBe('<b>no</b> access');
      expect(message.querySelector('b')).toBeNull();
      expect(message.getAttribute('role')).toBe('alert');
      expect((document.getElementById('link') as HTMLAnchorElement).classList.contains('visible')).toBe(false);

      jest.advanceTimersByTime(7999);
      expect(close).not.toHaveBeenCalled();
      jest.advanceTimersByTime(1);
      expect(close).toHaveBeenCalledTimes(1);
      expect(posted()).toContainEqual({ type: 'close' });
    });

    it('a message that is not a result (or not JSON) changes nothing', () => {
      installOffice(true);
      loadPage(`?message=Saving&status=progress`);

      commandSends({ type: 'something-else', message: 'x' });
      parentHandler!({ message: 'not json' });

      expect(document.getElementById('message')!.textContent).toBe('Saving');
    });

    it('clicking the link asks the command to open it (and does not navigate the dialog itself)', () => {
      installOffice(true);
      loadPage(`?message=Saved&status=success&linkUrl=${encodeURIComponent(LINK)}`);

      const click = new MouseEvent('click', { bubbles: true, cancelable: true });
      document.getElementById('link')!.dispatchEvent(click);

      expect(click.defaultPrevented).toBe(true);
      expect(posted()).toContainEqual({ type: 'open-link', url: LINK });
    });

    it('Close asks the command to close the dialog and closes the window', () => {
      installOffice(true);
      const { close } = loadPage(`?message=Saved&status=success`);

      (document.getElementById('dismiss') as HTMLButtonElement).click();

      expect(posted()).toContainEqual({ type: 'close' });
      expect(close).toHaveBeenCalledTimes(1);
    });
  });
});
