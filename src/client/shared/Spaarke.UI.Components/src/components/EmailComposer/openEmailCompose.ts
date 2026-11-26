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
import { getXrm } from '../../utils/xrmContext';
import { EMAIL_PAGE_WEBRESOURCE_NAME } from './openEmailRecord';
import { OOB_MODAL_SIZES } from '../../utils/adapters/oobModalSizes';
import { cleanGuid, normalizeRecordRef } from '../../utils/guid';
import type { ISendEmailDialogRegarding } from './wrappers/SendEmailDialog';

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
  /**
   * `new` only: the record the new email starts "Related to" (#2908: New Email on a record's communications grid).
   * The composer shows it as an association chip and the send files the email under it. Ignored for the other modes,
   * whose associations come from the source email. An invalid value is dropped with a warning; the composer still opens.
   */
  regarding?: ComposeRegarding;
}

/**
 * A "Related to" record carried in the compose envelope: the composer's own regarding shape (`SendEmailDialog`'s
 * `regarding` prop), so the two cannot drift.
 */
export type ComposeRegarding = ISendEmailDialogRegarding;

const REGARDING_TYPE_PARAM = 'regardingType';
const REGARDING_ID_PARAM = 'regardingId';
const REGARDING_NAME_PARAM = 'regardingName';

/** Validate and normalize a "Related to" record. Fields are `unknown`: plain-JS callers can pass anything. */
function normalizeRegarding(
  regarding: { entityType?: unknown; id?: unknown; name?: unknown } | undefined
): ComposeRegarding | undefined {
  if (!regarding) return undefined;
  const ref = normalizeRecordRef(regarding.entityType, regarding.id);
  if (!ref) return undefined;
  const name = typeof regarding.name === 'string' ? regarding.name.trim() : '';
  return name ? { ...ref, name } : ref;
}

/**
 * The compose envelope's "Related to" parameters (`regardingType`, `regardingId`, `regardingName`), form-encoded and
 * ready to append after `&`. Empty when `regarding` is absent or invalid. The secure-child ribbon writes the same three
 * parameters by hand (plain JS, no imports); keep the names in step with {@link parseComposeRegarding}.
 */
export function composeRegardingParams(regarding: ComposeRegarding | undefined): string {
  const r = normalizeRegarding(regarding);
  if (!r) return '';
  const params = new URLSearchParams();
  params.set(REGARDING_TYPE_PARAM, r.entityType);
  params.set(REGARDING_ID_PARAM, r.id);
  if (r.name) params.set(REGARDING_NAME_PARAM, r.name);
  return params.toString();
}

/**
 * Read the "Related to" record from a compose envelope (the `sprk_emailpage` side of {@link composeRegardingParams}).
 * Returns `undefined` when the parameters are absent or invalid.
 */
export function parseComposeRegarding(params: URLSearchParams): ComposeRegarding | undefined {
  return normalizeRegarding({
    entityType: params.get(REGARDING_TYPE_PARAM) ?? '',
    id: params.get(REGARDING_ID_PARAM) ?? '',
    name: params.get(REGARDING_NAME_PARAM) ?? undefined,
  });
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
  const sourceId = cleanGuid(options?.sourceCommunicationId);

  if (mode !== 'new' && !sourceId) {
    console.warn(`[openEmailCompose] mode "${mode}" requires a sourceCommunicationId — nothing to open.`);
    return;
  }

  const xrm = getXrm('navigation');
  if (typeof xrm?.Navigation?.navigateTo !== 'function') {
    console.warn(
      '[openEmailCompose] Xrm.Navigation.navigateTo is unavailable — this launcher only works inside a Model-Driven App host. No-op.'
    );
    return;
  }

  // Compose `data` envelope: `compose=<mode>` with an optional `&id=<source>` for
  // reply/replyAll/forward. `main.tsx` reads `compose` first (compose mode short-
  // circuits the reading-surface launch) then the folded source id.
  let data = sourceId ? `compose=${mode}&id=${sourceId}` : `compose=${mode}`;
  if (mode === 'new' && options?.regarding) {
    const regardingParams = composeRegardingParams(options.regarding);
    if (regardingParams) data += `&${regardingParams}`;
    else console.warn('[openEmailCompose] Ignoring an invalid "Related to" record:', options.regarding);
  }

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
        // `createForm` OOB size (70%×80%) — composing = creating a new email
        // (spec FR-11/FR-18, task 090); was an ad-hoc 60%×80% literal.
        width: OOB_MODAL_SIZES.createForm.width,
        height: OOB_MODAL_SIZES.createForm.height,
      }
    );
  } catch (err) {
    console.warn('[openEmailCompose] navigateTo failed:', err);
  }
}
