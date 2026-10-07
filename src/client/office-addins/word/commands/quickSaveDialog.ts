/**
 * The Quick Save dialog (spaarkeai-word-add-in-r1 task 110, UAT round 11 item 2).
 *
 * One Spaarke dialog follows a Quick Save from its first moment to its last: it opens at the START in a progress
 * state ("Saving to Spaarke…"), then shows the result — success (with an "Open in Spaarke" link to the document
 * record) or the error — in the same window. The page is `word/commands/notify.html`.
 *
 * Host support (decided by capability, never host type — NFR-10):
 * - **Update in place** with `dialog.messageChild` when `DialogApi 1.2` is supported AND the page has told us it is
 *   listening (`ready` over `messageParent` — a message sent before the page registered its handler is lost).
 * - Otherwise (older host, or the page could not load Office.js) **close it and reopen** the page with the final
 *   state in the URL — one dialog at a time.
 * - The link opens through the PARENT: the page `messageParent`s `open-link`, and this module opens it with
 *   `Office.context.ui.openBrowserWindow` (else `window.open`, via `openFileUrl`) — only if it is on the configured
 *   `ORG_URL` origin. Without Office.js in the page the page's own anchor opens it.
 *
 * Function-command lifetime: the ribbon command's `event.completed()` is called EXACTLY ONCE, via `onComplete`, once
 * the save is over (`finish()`) AND the dialog is gone — closed by the user, closed by the page's own timer, or
 * closed by the {@link RESULT_CAP_MS} safety cap — or immediately when there is no dialog at all.
 */

import { isUrlOnConfiguredOrigin } from '@shared/taskpane/services/quickSaveHelpers';
import { openFileUrl } from '@shared/taskpane/services/openRecordLauncher';

/** Longest text sent to the dialog — the reopen path carries it in the URL (task 089). */
export const DIALOG_MAX_CHARS = 400;

/** The progress text. */
export const SAVING_MESSAGE = 'Saving to Spaarke…';

/** Safety cap: how long after the result is shown the dialog is closed (and the command completed) if still open. */
export const RESULT_CAP_MS = 2 * 60 * 1000;

/** How long to wait for the page's `ready` before falling back to close-and-reopen. */
export const READY_WAIT_MS = 3000;

/** One size for every state, so an update in place never clips an error. */
const DIALOG_SIZE = { height: 30, width: 35 } as const;

const MESSAGE_RECEIVED = 'dialogMessageReceived';
const EVENT_RECEIVED = 'dialogEventReceived';

export type QuickSaveDialogStatus = 'success' | 'error';

export interface QuickSaveDialogResult {
  message: string;
  status: QuickSaveDialogStatus;
  /** Validated against `orgUrl` before the dialog shows it. */
  linkUrl?: string | null;
}

export interface QuickSaveDialog {
  /** Show the final state. Never throws; when no dialog is open it does nothing. */
  show(result: QuickSaveDialogResult): Promise<void>;
  /** The save is over (success, error or early exit): completes the command once the dialog is gone. Idempotent. */
  finish(): void;
}

export interface QuickSaveDialogDeps {
  /** Called exactly once — the command's `event.completed()`. */
  onComplete: () => void;
  /** The configured Spaarke organisation URL (`ORG_URL`); links are only opened on this origin. */
  orgUrl: string | undefined;
}

function bounded(message: string): string {
  return message.length > DIALOG_MAX_CHARS ? `${message.slice(0, DIALOG_MAX_CHARS - 1)}…` : message;
}

function supportsMessageChild(): boolean {
  try {
    return Office.context.requirements.isSetSupported('DialogApi', '1.2');
  } catch {
    return false;
  }
}

function canOpenBrowserWindow(): boolean {
  try {
    return (
      Office.context.requirements.isSetSupported('OpenBrowserWindowApi', '1.1') &&
      typeof Office.context.ui?.openBrowserWindow === 'function'
    );
  } catch {
    return false;
  }
}

/**
 * Opens the dialog in its progress state and returns the controller. Always resolves: a host that cannot open a dialog
 * yields a controller whose `show` is a no-op and whose `finish` completes the command at once.
 */
export async function openQuickSaveDialog(deps: QuickSaveDialogDeps): Promise<QuickSaveDialog> {
  let dialog: Office.Dialog | null = null;
  let ready = false;
  let readyWaiters: Array<(ready: boolean) => void> = [];
  /** True while this module is closing the dialog itself to reopen it — the close is not the user's. */
  let reopening = false;
  let finished = false;
  let completed = false;
  let capTimer: ReturnType<typeof setTimeout> | null = null;

  function complete(): void {
    if (completed) {
      return;
    }
    completed = true;
    if (capTimer) {
      clearTimeout(capTimer);
      capTimer = null;
    }
    deps.onComplete();
  }

  function dialogGone(): void {
    dialog = null;
    ready = false;
    flushReadyWaiters(false);
    if (finished) {
      complete();
    }
  }

  function flushReadyWaiters(value: boolean): void {
    const waiters = readyWaiters;
    readyWaiters = [];
    waiters.forEach(resolve => resolve(value));
  }

  function waitForReady(): Promise<boolean> {
    if (ready) {
      return Promise.resolve(true);
    }
    return new Promise(resolve => {
      const timer = setTimeout(() => {
        readyWaiters = readyWaiters.filter(w => w !== waiter);
        resolve(false);
      }, READY_WAIT_MS);
      const waiter = (value: boolean): void => {
        clearTimeout(timer);
        resolve(value);
      };
      readyWaiters.push(waiter);
    });
  }

  function closeDialog(): void {
    const current = dialog;
    dialog = null;
    ready = false;
    flushReadyWaiters(false);
    try {
      current?.close?.();
    } catch {
      // Already closed — nothing to do.
    }
  }

  function onPageMessage(raw: unknown): void {
    let data: { type?: string; url?: unknown } | null = null;
    try {
      data = JSON.parse(String(raw)) as { type?: string; url?: unknown };
    } catch {
      return;
    }

    switch (data?.type) {
      case 'ready':
        ready = true;
        flushReadyWaiters(true);
        break;
      case 'close':
        closeDialog();
        if (finished) {
          complete();
        }
        break;
      case 'open-link':
        if (isUrlOnConfiguredOrigin(data.url, deps.orgUrl)) {
          try {
            openFileUrl(data.url as string, canOpenBrowserWindow());
          } catch (error) {
            console.warn('[Spaarke] Quick Save: could not open the document link', error);
          }
        } else {
          console.warn('[Spaarke] Quick Save: refused to open a link that is not on the configured Spaarke origin');
        }
        break;
      default:
        break;
    }
  }

  /** Open the page with the given state in its URL. Resolves true when a dialog is open afterwards. */
  function openPage(
    message: string,
    status: 'progress' | QuickSaveDialogStatus,
    linkUrl?: string | null
  ): Promise<boolean> {
    const params = new URLSearchParams({ message: bounded(message), status });
    if (linkUrl) {
      params.set('linkUrl', linkUrl);
    }
    const url = `${window.location.origin}/word/commands-notify.html?${params.toString()}`;

    return new Promise(resolve => {
      try {
        Office.context.ui.displayDialogAsync(url, { ...DIALOG_SIZE, promptBeforeOpen: false }, result => {
          const opened = String(result?.status) === 'succeeded' && result.value ? result.value : null;
          if (!opened) {
            resolve(false);
            return;
          }
          dialog = opened;
          ready = false;
          opened.addEventHandler?.(MESSAGE_RECEIVED as unknown as Office.EventType, (arg: unknown) => {
            if (dialog === opened) {
              onPageMessage((arg as { message?: unknown } | undefined)?.message);
            }
          });
          opened.addEventHandler?.(EVENT_RECEIVED as unknown as Office.EventType, () => {
            // The user (or the page's own timer) closed it. A close WE asked for, to reopen, is not that.
            if (dialog === opened && !reopening) {
              dialogGone();
            }
          });
          resolve(true);
        });
      } catch {
        // Office.context.ui unavailable — never blocks the save.
        resolve(false);
      }
    });
  }

  await openPage(SAVING_MESSAGE, 'progress');

  return {
    async show(result: QuickSaveDialogResult): Promise<void> {
      if (!dialog) {
        return;
      }
      const message = bounded(result.message);
      const linkUrl =
        result.status === 'success' && isUrlOnConfiguredOrigin(result.linkUrl, deps.orgUrl) ? result.linkUrl : null;

      let shown = false;
      if (supportsMessageChild() && typeof dialog.messageChild === 'function' && (await waitForReady()) && dialog) {
        try {
          dialog.messageChild(JSON.stringify({ type: 'result', message, status: result.status, linkUrl }));
          shown = true;
        } catch (error) {
          console.warn('[Spaarke] Quick Save: messageChild failed; reopening the dialog instead', error);
        }
      }

      if (!shown && dialog) {
        reopening = true;
        try {
          closeDialog();
          await openPage(message, result.status, linkUrl);
        } finally {
          reopening = false;
        }
        shown = !!dialog;
      }

      if (shown && !completed) {
        capTimer = setTimeout(() => {
          closeDialog();
          complete();
        }, RESULT_CAP_MS);
      }
    },

    finish(): void {
      finished = true;
      if (!dialog) {
        complete();
      }
    },
  };
}
