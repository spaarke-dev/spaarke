/**
 * "Open in desktop" — one behaviour for every surface (ISS-004, #2807).
 *
 * - **Office files.** `GET /api/documents/{id}/open-links` returns a `desktopUrl` (`ms-word:`, `ms-excel:`,
 *   `ms-powerpoint:`) only for Word, Excel and PowerPoint. It is launched through a hidden iframe whose `src` is
 *   the protocol link: the browser hands the link to the Office app, the page stays where it is, and no unload
 *   event fires. (Assigning `location.href`, or clicking an anchor, to a protocol URL fires `beforeunload` in
 *   Chromium: that raises an unsaved-changes prompt in Compose and closes side panes. A new window would leave
 *   an empty tab behind.)
 * - **Every other file** (PDF, image, saved email…) is downloaded through the BFF `/content` route, and the
 *   browser hands it to the user's default app. It is NOT sent to the SharePoint `webUrl`: a user who may read a
 *   secure record but not edit it has no SharePoint access and would get an access-denied page
 *   (`FileAccessEndpoints.GetOpenLinks`, step 9). `/content` authorizes on the Dataverse record, so the download
 *   works for everyone who can see the document.
 *
 * A web page cannot tell whether the Office app is installed; when it is not, the browser shows its own message.
 *
 * React-free and import-free so PCF controls can use it (`@spaarke/ui-components/dist/utils/openInDesktopApp`).
 * The caller passes its own authenticated fetch, so this module never pulls in a second copy of `@spaarke/auth`
 * (whose token state is a module singleton).
 */

/** The fields of the open-links response this helper reads. */
export interface IDesktopOpenLinks {
  desktopUrl?: string | null;
  fileName?: string | null;
}

/** The sentence every surface shows when `openInDesktopApp` (or the open-links request before it) fails. */
export const OPEN_IN_DESKTOP_FAILED_MESSAGE = "Couldn't open the file. Try again, or download it from the record.";

/** What happened: the Office app was asked to open the file, or the file was downloaded. */
export type DesktopOpenResult = 'office-app' | 'downloaded';

export interface IOpenInDesktopAppOptions {
  /** BFF host, without `/api` (e.g. `https://spaarke-bff.example.net`). */
  bffBaseUrl: string;
  /** The `sprk_document` id. */
  documentId: string;
  /** The open-links response for that document. */
  links: IDesktopOpenLinks;
  /** Authenticated GET (e.g. `authenticatedFetch` from `@spaarke/auth`). A non-2xx response must throw or have `ok: false`. */
  fetch: (url: string) => Promise<Response>;
  /** Download file name when the response has none (e.g. the record name). */
  fallbackFileName?: string;
}

/**
 * The BFF route that streams a document's bytes after authorizing the caller on the record. `bffBaseUrl` must be
 * host-only (callers get it from `normalizeBffBaseUrl` in `@spaarke/auth`); a trailing `/` is tolerated.
 */
export function documentContentUrl(bffBaseUrl: string, documentId: string): string {
  const host = bffBaseUrl.trim().replace(/\/+$/, '');
  return `${host}/api/documents/${encodeURIComponent(documentId)}/content`;
}

/** How long the hidden launch iframe stays in the DOM, so the browser can hand the protocol link to the app. */
const LAUNCH_FRAME_LIFETIME_MS = 5000;

/**
 * Launch a protocol URL (`ms-word:` ...) without leaving the page. A hidden iframe does not fire `beforeunload`
 * on the host page, unlike `location.href` or an anchor click.
 */
function launchProtocolUrl(url: string): void {
  const frame = document.createElement('iframe');
  frame.style.display = 'none';
  frame.setAttribute('aria-hidden', 'true');
  frame.src = url;
  document.body.appendChild(frame);
  setTimeout(() => frame.remove(), LAUNCH_FRAME_LIFETIME_MS);
}

/** Save a blob as a file through a temporary link. */
export function saveBlobAsFile(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = objectUrl;
  a.download = fileName;
  a.style.display = 'none';
  document.body.appendChild(a);
  try {
    a.click();
  } finally {
    document.body.removeChild(a);
    // Revoke after the click has been handled; revoking synchronously can cancel the download in some browsers.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 0);
  }
}

/**
 * Open a document in the user's desktop app: the Office app for Word / Excel / PowerPoint, otherwise a download
 * the browser hands to the default app.
 *
 * @throws when the download fails (the caller shows its own error message).
 */
export async function openInDesktopApp(options: IOpenInDesktopAppOptions): Promise<DesktopOpenResult> {
  const { bffBaseUrl, documentId, links, fetch, fallbackFileName } = options;

  if (links.desktopUrl) {
    launchProtocolUrl(links.desktopUrl);
    return 'office-app';
  }

  const response = await fetch(documentContentUrl(bffBaseUrl, documentId));
  if (!response.ok) {
    throw new Error(`The download failed (HTTP ${response.status}).`);
  }
  const blob = await response.blob();
  saveBlobAsFile(blob, links.fileName || fallbackFileName || 'document');
  return 'downloaded';
}
