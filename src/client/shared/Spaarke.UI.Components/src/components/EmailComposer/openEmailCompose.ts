/**
 * openEmailCompose.ts
 *
 * Pattern B launcher for opening the email COMPOSER standalone — a "New email",
 * "Reply", "Reply All", or "Forward" — from anywhere in the app (e.g. the
 * Messages surface) WITHOUT hosting the whole Email Workspace.
 *
 * It opens the `sprk_emailpage` web resource as a centered modal in COMPOSE mode:
 * the code page (`main.tsx`) reads the `compose=<mode>` (+ optional `&id=<source>`)
 * envelope and mounts ONLY the composer dialog — reply/forward prefill, recipient
 * lookup, template picker, AI sparkle, "Related to", and SPE share links all work
 * because the code page wires the same Xrm handlers the reading-pane surface uses.
 * The composer closes its own host window on Cancel or after a successful Send.
 *
 * Sibling of {@link openEmailRecord} (which opens the READING surface for one
 * record). Both are Xrm-coupled by design and best-effort: outside a Model-Driven
 * App host they no-op with a `console.warn` rather than throwing.
 *
 * @example
 * import { openEmailCompose } from '@spaarke/ui-components';
 * openEmailCompose();                                              // New email
 * openEmailCompose({ mode: 'reply', sourceCommunicationId: id });  // Reply
 * openEmailCompose({ mode: 'replyAll', sourceCommunicationId: id });
 * openEmailCompose({ mode: 'forward', sourceCommunicationId: id });
 *
 * @see src/solutions/EmailPage/src/main.tsx — the receiving code page (compose mode).
 */
import { getXrm } from '../../services/xrmGlobal';
import { EMAIL_PAGE_WEBRESOURCE_NAME } from './openEmailRecord';

/** Compose entry points. `new` needs no source; the rest require a source communication. */
export type OpenEmailComposeMode = 'new' | 'reply' | 'replyAll' | 'forward';

export interface OpenEmailComposeOptions {
  /** Which composer to open. Default `'new'`. */
  mode?: OpenEmailComposeMode;
  /**
   * `sprk_communication` GUID of the email being replied to / forwarded. REQUIRED
   * for `reply` / `replyAll` / `forward`; ignored for `new`. Braces + casing are
   * normalised here.
   */
  sourceCommunicationId?: string;
}

/**
 * Open the email composer as a centered modal dialog (Pattern B).
 *
 * Best-effort: no-ops with a `console.warn` when there is no
 * `Xrm.Navigation.navigateTo` (non-MDA host) or when a reply/forward mode is
 * requested without a `sourceCommunicationId`.
 */
export async function openEmailCompose(options?: OpenEmailComposeOptions): Promise<void> {
  const mode: OpenEmailComposeMode = options?.mode ?? 'new';
  const sourceId = (options?.sourceCommunicationId ?? '').replace(/[{}]/g, '').trim().toLowerCase();

  if (mode !== 'new' && !sourceId) {
    console.warn(`[openEmailCompose] mode "${mode}" requires a sourceCommunicationId — nothing to open.`);
    return;
  }

  const xrm = getXrm();
  if (typeof xrm?.Navigation?.navigateTo !== 'function') {
    console.warn(
      '[openEmailCompose] Xrm.Navigation.navigateTo is unavailable — this launcher only works inside a Model-Driven App host. No-op.'
    );
    return;
  }

  // Compose `data` envelope: `compose=<mode>` with an optional `&id=<source>` for
  // reply/replyAll/forward. `main.tsx` reads `compose` first (compose mode short-
  // circuits the reading-surface launch) then the folded source id.
  const data = sourceId ? `compose=${mode}&id=${sourceId}` : `compose=${mode}`;

  try {
    // Call navigateTo DIRECTLY on Xrm.Navigation — never via an extracted local
    // alias (that strips the `this` binding and makes the call a silent no-op).
    await xrm.Navigation.navigateTo(
      {
        pageType: 'webresource',
        webresourceName: EMAIL_PAGE_WEBRESOURCE_NAME,
        data,
      },
      {
        target: 2, // modal dialog
        position: 1, // centered
        width: { value: 60, unit: '%' },
        height: { value: 80, unit: '%' },
      }
    );
  } catch (err) {
    console.warn('[openEmailCompose] navigateTo failed:', err);
  }
}
