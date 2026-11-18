/**
 * Open and Download for one file (ebilling-r1 task 151e). Both fetch the bytes from the content route through `authenticatedFetch`
 * (a bearer token cannot ride a plain link), then hand them to the browser: Open in a new tab (a blob URL), Download as a saved file.
 * The browser pieces are injected so the logic is tested without a DOM.
 *
 * Open opens its tab BEFORE the fetch (still inside the click, so a pop-up blocker allows it) and points it at the blob afterwards; a
 * failed fetch closes the tab and returns the reason instead. A file whose scan is Pending is refused here without a call (the list marks
 * it and the buttons are disabled; the content route would answer 409 anyway).
 */

import { fileFailureMessage, type InvoiceFile, type InvoiceFilesApi } from './invoiceFilesApi';

/** The part of a browser window Open uses. */
export interface TabLike {
  close(): void;
  /** Navigate the tab to the URL. */
  navigate(url: string): void;
}

export interface FileActionDeps {
  api: Pick<InvoiceFilesApi, 'fetchFile'>;
  /** Open an empty tab (called synchronously on click); null when the browser refused. */
  openTab: () => TabLike | null;
  createObjectUrl: (blob: Blob) => string;
  revokeObjectUrl: (url: string) => void;
  /** Save the blob as a file named `fileName` (the dialog's `saveBlob`). */
  saveBlob: (blob: Blob, fileName: string) => void;
  /** Schedule `fn` after `ms` (revoking blob URLs later). */
  later: (fn: () => void, ms: number) => void;
}

export type ActionResult = { ok: true } | { ok: false; message: string };

/** How long an opened blob URL lives; the tab has long since loaded it. */
export const REVOKE_AFTER_MS = 60_000;

const PENDING_MESSAGE = 'This file is still being scanned for malware. Try again in a few minutes.';

/** Open `file` in a new tab. */
export async function openFile(file: InvoiceFile, deps: FileActionDeps): Promise<ActionResult> {
  if (file.scanState !== 'Ready') {
    return { ok: false, message: PENDING_MESSAGE };
  }

  // Synchronously, before the first await: inside the click, so a pop-up blocker allows the tab.
  const tab = deps.openTab();
  if (!tab) {
    return { ok: false, message: 'Your browser blocked the new tab. Allow pop-ups for this site, or use Download.' };
  }

  const result = await deps.api.fetchFile(file.fileId);
  if (!result.ok) {
    tab.close();
    return { ok: false, message: fileFailureMessage(result.failure) };
  }

  const url = deps.createObjectUrl(result.value.blob);
  tab.navigate(url);
  deps.later(() => deps.revokeObjectUrl(url), REVOKE_AFTER_MS);
  return { ok: true };
}

/** Save `file` to the user's disk under its own name. */
export async function downloadFile(file: InvoiceFile, deps: FileActionDeps): Promise<ActionResult> {
  if (file.scanState !== 'Ready') {
    return { ok: false, message: PENDING_MESSAGE };
  }

  const result = await deps.api.fetchFile(file.fileId);
  if (!result.ok) {
    return { ok: false, message: fileFailureMessage(result.failure) };
  }

  deps.saveBlob(result.value.blob, file.name || `invoice-file-${file.fileId}`);
  return { ok: true };
}

/** The browser wiring used by the page (not by the tests). */
export function browserDeps(api: Pick<InvoiceFilesApi, 'fetchFile'>, saveBlob: (blob: Blob, fileName: string) => void): FileActionDeps {
  return {
    api,
    openTab: () => {
      const tab = window.open('', '_blank');
      if (!tab) return null;
      try {
        // The tab must not reach back into the form (it will show a file the user uploaded, or one a firm sent).
        tab.opener = null;
      } catch {
        // Some hosts refuse the assignment; the blob is same-origin content either way.
      }
      return { close: () => tab.close(), navigate: url => { tab.location.href = url; } };
    },
    createObjectUrl: blob => URL.createObjectURL(blob),
    revokeObjectUrl: url => URL.revokeObjectURL(url),
    saveBlob,
    later: (fn, ms) => {
      window.setTimeout(fn, ms);
    },
  };
}
