/**
 * communicationApi.ts
 *
 * Typed client wrapper around `POST /api/communications/send`. The BFF
 * endpoint accepts the request shape defined in
 * `Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs`.
 *
 * IMPORTANT — `attachmentDocumentIds` semantics (CORRECTED 2026-07-14, R4 W0)
 * ----------------------------------------------------------------------------
 * `AttachmentDocumentIds` correctly carries Dataverse **`sprk_document`
 * GUIDs** — email attachments are always tracked Documents; the server
 * resolves each Document to its backing SPE File internally. The prior R3
 * assessment ("the BFF expects raw SPE driveItem IDs, not sprk_document
 * GUIDs") was a misread of `DownloadAndBuildAttachmentsAsync` and has been
 * retracted by the project owner (see `projects/email-communication-solution-r4/
 * current-task.md` "Key W0 decisions" + `[[email-r4-attachment-id-semantics]]`).
 * `DocumentEmailWizard` sending `sprk_document` GUIDs here is correct
 * behavior, not a bug — **do not "fix" it and do not rename this field.**
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
 *
 * `SendCommunicationError` (task 020 boundary note)
 * --------------------------------------------------
 * Task 022 (FR-13, "sendCommunication() refinements") owns the full typed
 * error contract per design §5.9 / ADR-019. Task 020 (`<EmailComposer />`)
 * depends on `SendCommunicationError` existing here for its `send()` error
 * contract and landed first, so this file additively defines the minimal
 * ProblemDetails-parsing error class task 020 needs — per the task 020 POML's
 * own guidance ("if 022 has not landed when this task runs, import against
 * the interface and coordinate ... define the import boundary cleanly").
 * Task 022 should verify/extend this shape rather than redefining it.
 * NOTE: `attachmentDocumentIds` is NOT renamed here — R4 W0 (owner decision,
 * 2026-07-14) confirmed the existing field name is correct (it carries
 * Dataverse Document GUIDs, not raw driveItem ids as R3 assumed); task 022's
 * FR-13 rename premise is stale and should not be reintroduced.
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
   * `sprk_document` GUIDs for files to attach. See file-level note above —
   * despite historical confusion, this field correctly expects Dataverse
   * Document GUIDs (the server resolves each to its SPE File). Max 150
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

/** Shape of a parsed ProblemDetails body (RFC 7807 + BFF `errorCode`/`correlationId` extensions). */
interface IProblemDetailsBody {
  /** RFC 7807 problem type URI. */
  type?: string;
  title?: string;
  detail?: string;
  status?: number;
  /** RFC 7807 instance URI (used as a correlation fallback). */
  instance?: string;
  errorCode?: string;
  code?: string;
  correlationId?: string;
  /** ASP.NET Core trace identifier (used as a correlation fallback). */
  traceId?: string;
}

/**
 * Typed error thrown by {@link sendCommunication} on any non-2xx response.
 * Parses the BFF's RFC 7807 ProblemDetails body (ADR-019) so callers can
 * branch on `status`/`code` instead of string-matching `message`.
 *
 * Canonical taxonomy note (task 020 / design §5.8-§5.9): `code` may also
 * carry one of the composer's own canonical validation codes
 * (`FROM_REQUIRED`, `FROM_NOT_APPROVED`) when the BFF rejects a send for a
 * sender-identity reason the client cannot pre-validate — see
 * `EmailComposer.types.ts` `ValidationErrorCode`.
 *
 * Construct instances from a failed {@link Response} via the async
 * {@link SendCommunicationError.fromResponse} factory (task 022 / FR-13),
 * which performs the ProblemDetails parse + non-JSON fallback.
 */
export class SendCommunicationError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail: string;
  readonly correlationId?: string;

  constructor(status: number, code: string, detail: string, correlationId?: string) {
    super(`sendCommunication failed (${status}): ${detail}`);
    this.name = 'SendCommunicationError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.correlationId = correlationId;
    // Restore the prototype chain (extending built-ins across ES5/ts-jest
    // transpilation targets can otherwise break `instanceof` checks).
    Object.setPrototypeOf(this, SendCommunicationError.prototype);
  }

  /**
   * Build a {@link SendCommunicationError} from a non-OK {@link Response}.
   *
   * Parses the BFF's RFC 7807 ProblemDetails body per ADR-019
   * (`{ type, title, status, detail, instance | traceId, correlationId? }`),
   * mapping `errorCode`/`code` → {@link SendCommunicationError.code},
   * `detail`/`title` → {@link SendCommunicationError.detail}, and
   * `correlationId`/`traceId`/`instance` → {@link SendCommunicationError.correlationId}.
   *
   * Falls back to a minimal error (status only, `HTTP_<status>` code) when the
   * body is absent, non-JSON, or unparseable — never throws while parsing.
   */
  static async fromResponse(response: Response): Promise<SendCommunicationError> {
    const status = response.status;
    try {
      const ct = response.headers.get('content-type') ?? '';
      if (ct.includes('application/problem+json') || ct.includes('application/json')) {
        const body = (await response.json()) as IProblemDetailsBody;
        const code = body.errorCode ?? body.code ?? `HTTP_${status}`;
        const detail = body.detail ?? body.title ?? `HTTP ${status}`;
        const correlationId = body.correlationId ?? body.traceId ?? body.instance;
        return new SendCommunicationError(status, code, detail, correlationId);
      }
      const text = await response.text();
      return new SendCommunicationError(status, `HTTP_${status}`, text || `HTTP ${status}`);
    } catch {
      return new SendCommunicationError(status, `HTTP_${status}`, `HTTP ${status}`);
    }
  }
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

// ---------------------------------------------------------------------------
// sendCommunication
// ---------------------------------------------------------------------------

/**
 * Send a communication via the BFF.
 *
 * Throws {@link SendCommunicationError} on any non-2xx response (including
 * validation errors) — parsed from the BFF's ProblemDetails body per ADR-019,
 * so callers can branch on `status`/`code` rather than string-matching the
 * message. Callers should wrap the call in a try/catch and surface failures to
 * the user. (Argument-shape guards before the request still throw a plain
 * `Error`; both are `instanceof Error`.)
 *
 * `attachmentDocumentIds` carries Dataverse `sprk_document` GUIDs — see the
 * file-level note; the field is NOT renamed and the server resolves each
 * Document to its backing SPE File.
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
 *     attachmentDocumentIds: ['<sprk_document-guid-1>', '<sprk_document-guid-2>'],
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
    throw await SendCommunicationError.fromResponse(response);
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
