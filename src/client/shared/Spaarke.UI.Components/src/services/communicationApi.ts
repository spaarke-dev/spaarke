/**
 * communicationApi.ts
 *
 * Typed client wrapper around `POST /api/communications/send`. The BFF
 * endpoint accepts the request shape defined in
 * `Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs`.
 *
 * IMPORTANT — `attachmentDocumentIds` semantics
 * ---------------------------------------------
 * The BFF field `AttachmentDocumentIds` is forwarded into
 * `CommunicationService.DownloadAndBuildAttachmentsAsync(...)` which calls
 * `SpeFileStore.GetFileMetadataAsync(driveId, itemId)` — i.e. the BFF
 * currently expects **SPE driveItem IDs**, not `sprk_document` Dataverse
 * GUIDs (despite the field name). Callers in the UI typically have the
 * `sprk_document` GUID at hand; the responsibility for translating that to
 * a driveItem id lives upstream of this wrapper (see DocumentEmailWizard).
 *
 * This wrapper is intentionally a thin pass-through and does no translation.
 *
 * Constraints:
 *   - Does NOT import from `@spaarke/auth` directly — `authenticatedFetch`
 *     is injected so the shared library stays decoupled from a particular
 *     auth bootstrapping strategy (PCF vs. Code Page differ).
 *   - Returns `{ communicationId }` extracted from the BFF's
 *     `SendCommunicationResponse` (which carries additional fields the UI
 *     does not currently consume).
 */

import type { AuthenticatedFetchFn } from './EntityCreationService';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Whether the email is sent via the shared mailbox (app-only) or as the
 * caller (OBO). Matches BFF `SendMode` enum (string form via JsonStringEnumConverter).
 */
export type CommunicationSendMode = 'sharedMailbox' | 'user';

/** Body format options. Matches BFF `BodyFormat`. */
export type CommunicationBodyFormat = 'html' | 'text';

/** Entity link to attach to the generated `sprk_communication` record. */
export interface ICommunicationAssociation {
  /** Dataverse logical name (e.g. "sprk_matter", "sprk_document"). */
  entityType: string;
  /** Dataverse record GUID (no braces). */
  entityId: string;
  /** Optional display name (for UI rendering / audit). */
  entityName?: string;
  /** Optional canonical URL to the record (for UI rendering / audit). */
  entityUrl?: string;
}

/** Options accepted by {@link sendCommunication}. */
export interface SendCommunicationOptions {
  /** Recipient email addresses (required, ≥ 1). */
  to: string[];
  /** Optional CC addresses. */
  cc?: string[];
  /** Optional BCC addresses. */
  bcc?: string[];
  /** Subject line (required). */
  subject: string;
  /** Body content (required). HTML by default. */
  body: string;
  /** Body content format. Default `'html'`. */
  bodyFormat?: CommunicationBodyFormat;
  /**
   * SPE driveItem ids for files to attach. See file-level note above —
   * these are **driveItem ids**, not `sprk_document` GUIDs. Max 150
   * attachments, 35 MB total (enforced server-side).
   */
  attachmentDocumentIds?: string[];
  /** Whether to archive the outbound email as a `.eml` in SPE. Default `false`. */
  archiveToSpe?: boolean;
  /** Entity associations to link onto the generated `sprk_communication`. */
  associations?: ICommunicationAssociation[];
  /** Send mode. Default `'sharedMailbox'`. */
  sendMode?: CommunicationSendMode;
  /** Caller-provided correlation ID for tracing. Optional. */
  correlationId?: string;
  /** Optional approved sender mailbox (otherwise the BFF default is used). */
  fromMailbox?: string;
}

/** Successful send result. Extra BFF fields are discarded. */
export interface SendCommunicationResult {
  /** GUID of the created `sprk_communication` record. */
  communicationId: string;
}

/** Dependencies for the wrapper (kept explicit for testability). */
export interface ICommunicationApiClientOptions {
  /** Authenticated fetch (Bearer-attached). Required. */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * Optional base URL. When omitted, the URL is sent as a relative path
   * (`/api/communications/send`) and the supplied fetch is expected to
   * resolve it (as `@spaarke/auth`'s `authenticatedFetch` does).
   */
  bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/** Map our friendly send-mode string to the BFF enum value. */
function toBffSendMode(mode: CommunicationSendMode | undefined): 'SharedMailbox' | 'User' {
  return mode === 'user' ? 'User' : 'SharedMailbox';
}

/** Map our friendly body-format string to the BFF enum value. */
function toBffBodyFormat(format: CommunicationBodyFormat | undefined): 'HTML' | 'PlainText' {
  return format === 'text' ? 'PlainText' : 'HTML';
}

/** Resolve the request URL using the optional base. */
function resolveUrl(base: string | undefined): string {
  const path = '/api/communications/send';
  if (!base) return path;
  // Trim trailing slash on base, leading slash on path is already present.
  return base.replace(/\/+$/, '') + path;
}

/** Attempt to extract a meaningful error message from a non-OK response. */
async function extractErrorMessage(response: Response): Promise<string> {
  try {
    const ct = response.headers.get('content-type') ?? '';
    if (ct.includes('application/problem+json') || ct.includes('application/json')) {
      const body = (await response.json()) as { title?: string; detail?: string };
      return body.detail ?? body.title ?? `HTTP ${response.status}`;
    }
    const text = await response.text();
    return text || `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

// ---------------------------------------------------------------------------
// sendCommunication
// ---------------------------------------------------------------------------

/**
 * Send a communication via the BFF.
 *
 * Throws on HTTP failures (including validation errors); callers should
 * wrap the call in a try/catch and surface failures to the user.
 *
 * @example
 * ```ts
 * import { authenticatedFetch } from '@spaarke/auth';
 * const result = await sendCommunication(
 *   {
 *     to: ['alice@example.com'],
 *     subject: 'Documents for review',
 *     body: '<p>Please see attached.</p>',
 *     bodyFormat: 'html',
 *     attachmentDocumentIds: ['<driveItemId-1>', '<driveItemId-2>'],
 *     associations: [{ entityType: 'sprk_matter', entityId: matterId }],
 *     sendMode: 'sharedMailbox',
 *   },
 *   { authenticatedFetch }
 * );
 * console.log('Communication ID:', result.communicationId);
 * ```
 */
export async function sendCommunication(
  opts: SendCommunicationOptions,
  client: ICommunicationApiClientOptions
): Promise<SendCommunicationResult> {
  if (!opts.to || opts.to.length === 0) {
    throw new Error('sendCommunication: at least one recipient is required.');
  }
  if (!opts.subject || !opts.subject.trim()) {
    throw new Error('sendCommunication: subject is required.');
  }
  if (typeof opts.body !== 'string') {
    throw new Error('sendCommunication: body is required.');
  }
  if (!client.authenticatedFetch) {
    throw new Error('sendCommunication: authenticatedFetch is required.');
  }

  const requestBody = {
    to: opts.to,
    cc: opts.cc,
    bcc: opts.bcc,
    subject: opts.subject,
    body: opts.body,
    bodyFormat: toBffBodyFormat(opts.bodyFormat),
    fromMailbox: opts.fromMailbox,
    sendMode: toBffSendMode(opts.sendMode),
    archiveToSpe: opts.archiveToSpe ?? false,
    attachmentDocumentIds: opts.attachmentDocumentIds,
    correlationId: opts.correlationId,
    associations: opts.associations?.map(a => ({
      entityType: a.entityType,
      entityId: a.entityId,
      entityName: a.entityName,
      entityUrl: a.entityUrl,
    })),
  };

  const url = resolveUrl(client.bffBaseUrl);
  const response = await client.authenticatedFetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody),
  });

  if (!response.ok) {
    const message = await extractErrorMessage(response);
    throw new Error(`sendCommunication failed (${response.status}): ${message}`);
  }

  // The BFF response (`SendCommunicationResponse`) carries multiple fields;
  // we extract communicationId (camelCase via STJ default) and tolerate
  // varying casing for robustness.
  let payload: Record<string, unknown> = {};
  try {
    payload = (await response.json()) as Record<string, unknown>;
  } catch {
    throw new Error('sendCommunication: response body was not valid JSON.');
  }

  const id = (payload['communicationId'] as string | undefined) ?? (payload['CommunicationId'] as string | undefined);

  if (!id) {
    throw new Error('sendCommunication: response did not include communicationId.');
  }

  return { communicationId: id };
}
