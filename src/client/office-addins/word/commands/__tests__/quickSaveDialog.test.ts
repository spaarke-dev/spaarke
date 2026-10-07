/**
 * Unit tests for word/commands/quickSaveDialog.ts (spaarkeai-word-add-in-r1 task 110, UAT round 11 item 2).
 *
 * The dialog opens in a progress state, is then updated in place (`messageChild`, DialogApi 1.2) or closed and
 * reopened with the result, opens its link through the parent (validated to the ORG_URL origin), and completes the
 * ribbon command exactly once. The page itself (`notify.html`) is covered by `notify.test.ts`.
 */

import {
  openQuickSaveDialog,
  READY_WAIT_MS,
  RESULT_CAP_MS,
  SAVING_MESSAGE,
  type QuickSaveDialog,
} from '../quickSaveDialog';

const ORG = 'https://spaarkedev1.crm.dynamics.com';
const RECORD_URL = `${ORG}/main.aspx?appname=sprk_MatterManagement&etn=sprk_document&id=2bcfc5d2-0000-4000-8000-000000000001&pagetype=entityrecord&navbar=off`;

interface FakeDialog {
  close: jest.Mock;
  messageChild?: jest.Mock;
  handlers: Record<string, (arg: unknown) => void>;
  addEventHandler: jest.Mock;
}

describe('quickSaveDialog', () => {
  let dialogs: FakeDialog[];
  let displayDialogAsync: jest.Mock;
  let openBrowserWindow: jest.Mock;
  let onComplete: jest.Mock;
  let dialogApi12: boolean;
  /** Whether the fake dialogs expose `messageChild` at all. */
  let withMessageChild: boolean;

  const urlOf = (index: number): URL => new URL(displayDialogAsync.mock.calls[index]![0] as string);
  const latest = (): FakeDialog => dialogs[dialogs.length - 1]!;
  const pageSays = (dialog: FakeDialog, payload: unknown): void =>
    dialog.handlers['dialogMessageReceived']!({ message: JSON.stringify(payload) });
  const userClosesDialog = (dialog: FakeDialog): void => dialog.handlers['dialogEventReceived']!({ error: 12006 });

  function open(orgUrl: string | undefined = ORG): Promise<QuickSaveDialog> {
    return openQuickSaveDialog({ onComplete, orgUrl });
  }

  beforeEach(() => {
    jest.useFakeTimers();
    dialogs = [];
    dialogApi12 = true;
    withMessageChild = true;
    onComplete = jest.fn();
    openBrowserWindow = jest.fn();

    displayDialogAsync = jest.fn((_url: string, _options: unknown, callback: (result: unknown) => void) => {
      const handlers: FakeDialog['handlers'] = {};
      const dialog: FakeDialog = {
        close: jest.fn(),
        handlers,
        addEventHandler: jest.fn((type: string, handler: (arg: unknown) => void) => {
          handlers[type] = handler;
        }),
        ...(withMessageChild ? { messageChild: jest.fn() } : {}),
      };
      dialogs.push(dialog);
      callback({ status: 'succeeded', value: dialog });
    });

    const ui = global.Office.context.ui as unknown as Record<string, unknown>;
    ui.displayDialogAsync = displayDialogAsync;
    ui.openBrowserWindow = openBrowserWindow;
    (global.Office.context as unknown as { requirements: unknown }).requirements = {
      isSetSupported: jest.fn((name: string, version: string) =>
        name === 'DialogApi' && version === '1.2' ? dialogApi12 : name === 'OpenBrowserWindowApi'
      ),
    };
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('opening', () => {
    it('opens the dialog straight away in the progress state, with the saving text', async () => {
      await open();

      expect(displayDialogAsync).toHaveBeenCalledTimes(1);
      const url = urlOf(0);
      expect(url.pathname).toBe('/word/commands-notify.html');
      expect(url.searchParams.get('status')).toBe('progress');
      expect(url.searchParams.get('message')).toBe(SAVING_MESSAGE);
      expect(displayDialogAsync.mock.calls[0]![1]).toMatchObject({ promptBeforeOpen: false });
    });
  });

  describe('showing the result — host with DialogApi 1.2 (update in place)', () => {
    it('sends the result with messageChild once the page is ready — one dialog, no reopen', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });

      await dialog.show({ message: "Saved to Spaarke as 'A.docx'.", status: 'success', linkUrl: RECORD_URL });

      expect(displayDialogAsync).toHaveBeenCalledTimes(1);
      expect(latest().close).not.toHaveBeenCalled();
      expect(latest().messageChild).toHaveBeenCalledTimes(1);
      expect(JSON.parse(latest().messageChild!.mock.calls[0]![0])).toEqual({
        type: 'result',
        message: "Saved to Spaarke as 'A.docx'.",
        status: 'success',
        linkUrl: RECORD_URL,
      });
    });

    it('waits for a page that is not ready YET, then updates in place', async () => {
      const dialog = await open();

      const shown = dialog.show({ message: 'Saved.', status: 'success' });
      await jest.advanceTimersByTimeAsync(500);
      expect(latest().messageChild).not.toHaveBeenCalled();
      pageSays(latest(), { type: 'ready' });
      await shown;

      expect(latest().messageChild).toHaveBeenCalledTimes(1);
      expect(displayDialogAsync).toHaveBeenCalledTimes(1);
    });

    it('FALLBACK: a page that never says ready (Office.js did not load) is closed and reopened with the result', async () => {
      const dialog = await open();
      const first = latest();

      const shown = dialog.show({ message: 'Saved.', status: 'success', linkUrl: RECORD_URL });
      await jest.advanceTimersByTimeAsync(READY_WAIT_MS);
      await shown;

      expect(first.messageChild).not.toHaveBeenCalled();
      expect(first.close).toHaveBeenCalledTimes(1);
      expect(displayDialogAsync).toHaveBeenCalledTimes(2);
      expect(urlOf(1).searchParams.get('status')).toBe('success');
      expect(urlOf(1).searchParams.get('message')).toBe('Saved.');
      expect(urlOf(1).searchParams.get('linkUrl')).toBe(RECORD_URL);
    });

    it('FALLBACK: a messageChild that throws reopens the dialog with the result', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });
      latest().messageChild!.mockImplementation(() => {
        throw new Error('host refused');
      });

      await dialog.show({ message: 'Saved.', status: 'success' });

      expect(displayDialogAsync).toHaveBeenCalledTimes(2);
      expect(urlOf(1).searchParams.get('message')).toBe('Saved.');
    });
  });

  describe('showing the result — host without DialogApi 1.2 (close and reopen)', () => {
    it('closes the progress dialog and opens ONE new dialog carrying the result', async () => {
      dialogApi12 = false;
      const dialog = await open();
      const first = latest();
      pageSays(first, { type: 'ready' });

      await dialog.show({ message: 'Saved.', status: 'success', linkUrl: RECORD_URL });

      expect(first.messageChild).not.toHaveBeenCalled();
      expect(first.close).toHaveBeenCalledTimes(1);
      expect(displayDialogAsync).toHaveBeenCalledTimes(2);
      expect(urlOf(1).searchParams.get('status')).toBe('success');
      expect(urlOf(1).searchParams.get('linkUrl')).toBe(RECORD_URL);
    });

    it('a dialog object without messageChild takes the same path', async () => {
      withMessageChild = false;
      const dialog = await open();

      await dialog.show({ message: 'Saved.', status: 'success' });

      expect(displayDialogAsync).toHaveBeenCalledTimes(2);
    });

    it('closing it to reopen is not the user closing it — the command is not completed', async () => {
      dialogApi12 = false;
      const dialog = await open();
      const first = latest();

      await dialog.show({ message: 'Saved.', status: 'success' });
      dialog.finish();
      userClosesDialog(first); // a late close event from the dialog we replaced

      expect(onComplete).not.toHaveBeenCalled();
    });
  });

  describe('the result content', () => {
    it('an error shows its text and never a link, even if one is passed', async () => {
      dialogApi12 = false;
      const dialog = await open();

      await dialog.show({ message: 'No access.', status: 'error', linkUrl: RECORD_URL });

      expect(urlOf(1).searchParams.get('status')).toBe('error');
      expect(urlOf(1).searchParams.has('linkUrl')).toBe(false);
    });

    it('NEGATIVE: a link that is not on the ORG_URL origin is dropped, not shown', async () => {
      dialogApi12 = false;
      const dialog = await open();

      await dialog.show({ message: 'Saved.', status: 'success', linkUrl: 'https://evil.example.com/main.aspx' });

      expect(urlOf(1).searchParams.has('linkUrl')).toBe(false);
    });

    it('NEGATIVE: without ORG_URL no link is shown', async () => {
      dialogApi12 = false;
      const dialog = await open('');

      await dialog.show({ message: 'Saved.', status: 'success', linkUrl: RECORD_URL });

      expect(urlOf(1).searchParams.has('linkUrl')).toBe(false);
    });

    it('a long message is bounded before it goes into the URL', async () => {
      dialogApi12 = false;
      const dialog = await open();

      await dialog.show({ message: 'x'.repeat(5000), status: 'error' });

      const message = urlOf(1).searchParams.get('message')!;
      expect(message.length).toBeLessThanOrEqual(400);
      expect(message.endsWith('…')).toBe(true);
    });
  });

  describe('the "open-link" request from the page', () => {
    it('opens the link with openBrowserWindow when it is on the ORG_URL origin', async () => {
      await open();

      pageSays(latest(), { type: 'open-link', url: RECORD_URL });

      expect(openBrowserWindow).toHaveBeenCalledTimes(1);
      expect(openBrowserWindow).toHaveBeenCalledWith(RECORD_URL);
    });

    it.each([
      ['another origin', 'https://evil.example.com/main.aspx'],
      ['a javascript: URL', 'javascript:alert(1)'],
      ['no url', undefined],
    ])('NEGATIVE: ignores a request for %s', async (_label, url) => {
      await open();

      pageSays(latest(), { type: 'open-link', url });

      expect(openBrowserWindow).not.toHaveBeenCalled();
    });

    it('NEGATIVE: ignores every link when ORG_URL is not configured', async () => {
      await open('');

      pageSays(latest(), { type: 'open-link', url: RECORD_URL });

      expect(openBrowserWindow).not.toHaveBeenCalled();
    });

    it('ignores a message that is not JSON, and one of an unknown type', async () => {
      await open();

      latest().handlers['dialogMessageReceived']!({ message: 'not json' });
      pageSays(latest(), { type: 'mystery' });

      expect(openBrowserWindow).not.toHaveBeenCalled();
      expect(onComplete).not.toHaveBeenCalled();
    });
  });

  describe('event.completed lifetime — exactly once', () => {
    it('is called when the user closes the dialog after the result, and not before', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });
      await dialog.show({ message: 'Saved.', status: 'success' });
      dialog.finish();

      expect(onComplete).not.toHaveBeenCalled();
      userClosesDialog(latest());

      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('is called when the page asks to be closed (its Close button / auto-close) — and only once', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });
      await dialog.show({ message: 'No access.', status: 'error' });
      dialog.finish();

      pageSays(latest(), { type: 'close' });
      userClosesDialog(latest());

      expect(latest().close).toHaveBeenCalled();
      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('is called after the safety cap, which closes the dialog', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });
      await dialog.show({ message: 'Saved.', status: 'success' });
      dialog.finish();

      await jest.advanceTimersByTimeAsync(RESULT_CAP_MS - 1);
      expect(onComplete).not.toHaveBeenCalled();
      await jest.advanceTimersByTimeAsync(1);

      expect(latest().close).toHaveBeenCalledTimes(1);
      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('a user close before the cap cancels the cap (no second call)', async () => {
      const dialog = await open();
      pageSays(latest(), { type: 'ready' });
      await dialog.show({ message: 'Saved.', status: 'success' });
      dialog.finish();
      userClosesDialog(latest());

      await jest.advanceTimersByTimeAsync(RESULT_CAP_MS * 2);

      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('a dialog closed WHILE saving keeps the command alive until the save is over, then completes once', async () => {
      const dialog = await open();

      userClosesDialog(latest());
      expect(onComplete).not.toHaveBeenCalled();

      await dialog.show({ message: 'Saved.', status: 'success' }); // nothing to show it in
      expect(displayDialogAsync).toHaveBeenCalledTimes(1);
      dialog.finish();

      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('completes at once when no dialog could be opened (failed open)', async () => {
      displayDialogAsync.mockImplementation((_u: string, _o: unknown, cb: (r: unknown) => void) =>
        cb({ status: 'failed', value: null })
      );
      const dialog = await open();

      await dialog.show({ message: 'Saved.', status: 'success' });
      dialog.finish();

      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('completes at once when displayDialogAsync throws', async () => {
      displayDialogAsync.mockImplementation(() => {
        throw new Error('unavailable');
      });
      const dialog = await open();

      dialog.finish();

      expect(onComplete).toHaveBeenCalledTimes(1);
    });

    it('finish() is idempotent', async () => {
      displayDialogAsync.mockImplementation((_u: string, _o: unknown, cb: (r: unknown) => void) =>
        cb({ status: 'failed', value: null })
      );
      const dialog = await open();

      dialog.finish();
      dialog.finish();

      expect(onComplete).toHaveBeenCalledTimes(1);
    });
  });
});
