/**
 * invoiceSubmissionService.ts — the add-in's client for ebilling's from-email invoice submission (task 133 A1).
 *
 * Contract: `projects/ebilling-r1/notes/095-from-email-contract.md`, agreed 2026-10-27 at 717cf238d9 (work/ebilling-r1):
 * - §2 `POST /api/finance/invoice-submissions/from-email` — {@link submitInvoiceFromEmail};
 * - §3 `GET statusUrl` until the status is final — {@link pollSubmission};
 * - §8 `GET /api/finance/core-records/{entityType}/{entityId}/firms` — {@link loadInvoiceFirms};
 * - §5, §6 and §9 codes → user text in `utils/invoiceSubmissionMessages.ts`.
 *
 * The request's `email` is EXACTLY `/office/save`'s: built by the ribbon Quick Save's `buildEmailSaveRequest` (only its
 * `email` is taken — the contract refuses a nested SaveRequest) over the pane's own `captureEmailContent`. No second
 * builder and no second capture.
 *
 * Marks: the contract matches a mark to an attachment by (fileName, size, contentType) AS SENT. Capture renames an
 * attached Outlook item to `<name>.eml` and sends it as `application/octet-stream`, so each mark is rewritten here from
 * the captured attachment with the same `attachmentId`. A mark for an attachment that was not sent (a cloud link, or
 * one past the size budget) keeps Outlook's values: it can only be `DontSave`, which the contract accepts unmatched.
 */

import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import { authenticatedJsonFetch } from '@shared/services/authenticatedJsonFetch';
import { describeFetchFailure } from '../utils/errorMessages';
import {
  describeInvoiceOutcome,
  describeSubmissionRefusal,
  EMAIL_UNIDENTIFIABLE_CODE,
  isRetryableSubmissionRefusal,
  type InvoiceMessageContext,
} from '../utils/invoiceSubmissionMessages';
import {
  captureEmailContent,
  EmailCaptureError,
  hostAdapterEmailReader,
  type EmailContentCapture,
  type EmailContentReader,
} from './emailContentCapture';
import { buildEmailSaveRequest, type OfficeSaveRequestBody, type QuickSaveEmailContext } from './quickSaveHelpers';
import type {
  InvoiceFirmOption,
  InvoiceSubmissionInput,
  InvoiceSubmissionMark,
  InvoiceSubmissionTarget,
  InvoiceSubmitHandler,
  InvoiceWireMark,
} from './invoiceSubmissionSeam';

/** The BFF base URL and token getter every call needs. */
export interface InvoiceServiceConfig {
  apiBaseUrl: string;
  getAccessToken: () => Promise<string>;
}

/** The open email, as the submission reads it at Submit (and again on each Retry). */
export interface InvoiceEmailSource {
  /** Subject, sender, recipients and the RFC Message-ID (`internetMessageId` is '' when Office gives none). */
  readContext(): Promise<QuickSaveEmailContext>;
  /** Every attachment, inline ones included (the `.eml` is the complete email). */
  listAttachments(): Promise<AttachmentInfo[]>;
  /** Reads the body and attachment content (the pane's capture reader). */
  reader: EmailContentReader;
}

/** The §2 `marks[]` entry on the wire. */
export interface InvoiceWireMarkEntry {
  fileName: string;
  size: number;
  contentType: string;
  mark: InvoiceWireMark;
}

/** The §2 request body. */
export interface InvoiceFromEmailRequestBody {
  targetEntity: InvoiceSubmissionTarget;
  email: OfficeSaveRequestBody['email'];
  marks: InvoiceWireMarkEntry[];
  firmId: string;
  firmInvoiceNumber: string;
  poNumber: string | null;
  mode: 'New' | 'Correction';
  invoiceId: string | null;
}

/** The §3 202 body. */
export interface InvoiceSubmissionAccepted {
  submissionId: string;
  jobId: string;
  trackingNumber: string;
  statusUrl: string;
  emlDocumentId?: string;
}

/** The §6 extras a refusal can carry. */
export interface InvoiceRefusalExtras {
  reason?: string;
  fileName?: string;
  mark?: string;
  invoiceStatus?: string;
}

/**
 * What became of one submit request.
 * - `accepted`: the 202 (also a replay of it, §5);
 * - `refused`: the server (or, with `local: true`, the add-in before sending) said no; retrying the same request
 *   will not help. `code` is `errorCode ?? reasonCode` (OP-8), undefined when the server sent neither;
 * - `retryable`: transient (503, 429, the access check's system failure, …) — retry the SAME action (same key).
 */
export type InvoiceSubmitResult =
  | { kind: 'accepted'; accepted: InvoiceSubmissionAccepted; replayed: boolean }
  | ({
      kind: 'refused';
      status: number;
      code: string | undefined;
      detail?: string;
      local?: true;
    } & InvoiceRefusalExtras)
  | { kind: 'retryable'; status: number; code: string | undefined; retryAfterSeconds?: number; detail?: string };

/** §3 statuses. */
export type InvoiceSubmissionStatus = 'Received' | 'Processing' | 'Accepted' | 'Rejected' | 'Failed';
export type InvoiceFinalStatus = Extract<InvoiceSubmissionStatus, 'Accepted' | 'Rejected' | 'Failed'>;

const FINAL_STATUSES: ReadonlySet<string> = new Set<InvoiceFinalStatus>(['Accepted', 'Rejected', 'Failed']);

/** What {@link pollSubmission} ends with. */
export type InvoicePollResult =
  | { kind: 'final'; status: InvoiceFinalStatus; outcomeReasonCode: string | null }
  /** The cap was reached while the submission was still Received / Processing (or unreadable for a moment). */
  | { kind: 'pending'; status: string | undefined }
  /** The status cannot be read at all (404, or a refusal that will not change). */
  | { kind: 'error'; status: number; code: string | undefined };

export const FROM_EMAIL_ROUTE = '/api/finance/invoice-submissions/from-email';

/** Default polling: 2 s, doubling to at most 10 s, for up to 30 reads (about 4.5 minutes). */
export const DEFAULT_POLL = { initialDelayMs: 2000, maxDelayMs: 10_000, maxAttempts: 30 } as const;

const MAX_RETRY_AFTER_SECONDS = 300;

function stringOrUndefined(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

/** `Retry-After` (seconds, or an HTTP date) and the body's `retryAfterSeconds` (429 OFFICE_015). */
function readRetryAfterSeconds(response: Response, body: Record<string, unknown> | null): number | undefined {
  const header = response.headers.get('Retry-After');
  let seconds: number | undefined;
  if (header) {
    const asNumber = Number(header);
    if (Number.isFinite(asNumber)) {
      seconds = asNumber;
    } else {
      const at = Date.parse(header);
      if (!Number.isNaN(at)) seconds = Math.ceil((at - Date.now()) / 1000);
    }
  }
  if (seconds === undefined && typeof body?.retryAfterSeconds === 'number') {
    seconds = body.retryAfterSeconds;
  }
  if (seconds === undefined || !Number.isFinite(seconds)) return undefined;
  return Math.min(Math.max(0, Math.ceil(seconds)), MAX_RETRY_AFTER_SECONDS);
}

async function readJson(response: Response): Promise<Record<string, unknown> | null> {
  try {
    const body: unknown = await response.json();
    return body && typeof body === 'object' ? (body as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function absoluteUrl(apiBaseUrl: string, url: string): string {
  return /^https?:\/\//i.test(url) ? url : `${apiBaseUrl}${url.startsWith('/') ? '' : '/'}${url}`;
}

/** The pane's {@link InvoiceEmailSource} over its host adapter (capability-gated, as SaveView reads the same fields). */
export function hostAdapterInvoiceEmailSource(adapter: IHostAdapter): InvoiceEmailSource {
  return {
    reader: hostAdapterEmailReader(adapter),
    listAttachments: () => adapter.getAttachments(),
    readContext: async () => {
      const capabilities = adapter.getCapabilities();
      const keys =
        capabilities.canResolveEmailIdentity && adapter.getEmailIdentityKeys
          ? await adapter.getEmailIdentityKeys()
          : null;
      const subject = await adapter.getSubject();
      const senderEmail = capabilities.canGetSender ? await adapter.getSenderEmail() : undefined;
      const outlook = adapter as IHostAdapter & {
        getSenderDisplayName?: () => Promise<string | undefined>;
        getSentDate?: () => Date | undefined;
      };
      const senderName =
        capabilities.canGetSender && typeof outlook.getSenderDisplayName === 'function'
          ? await outlook.getSenderDisplayName()
          : undefined;
      const recipients = capabilities.canGetRecipients ? await adapter.getRecipients() : [];
      const sentDate = typeof outlook.getSentDate === 'function' ? outlook.getSentDate() : undefined;
      return {
        internetMessageId: keys?.internetMessageId ?? '',
        ...(keys?.exchangeItemId ? { exchangeItemId: keys.exchangeItemId } : {}),
        subject,
        ...(senderEmail ? { senderEmail } : {}),
        ...(senderName ? { senderName } : {}),
        recipients: recipients.map(r => ({
          email: r.email,
          type: r.type,
          ...(r.displayName !== undefined ? { displayName: r.displayName } : {}),
        })),
        ...(sentDate ? { sentDate } : {}),
      };
    },
  };
}

/** The §2 marks: each rewritten from the captured attachment with the same id (see the file header). */
export function toWireMarks(
  marks: readonly InvoiceSubmissionMark[],
  captured: EmailContentCapture
): InvoiceWireMarkEntry[] {
  const sent = new Map(captured.attachments.map(a => [a.attachmentId, a] as const));
  return marks.map(mark => {
    const attachment = sent.get(mark.attachmentId);
    return attachment
      ? { fileName: attachment.fileName, size: attachment.size, contentType: attachment.contentType, mark: mark.mark }
      : { fileName: mark.fileName, size: mark.size, contentType: mark.contentType, mark: mark.mark };
  });
}

/** The §2 body from the view's input, the email's context and its capture. */
export function buildInvoiceFromEmailBody(
  input: InvoiceSubmissionInput,
  context: QuickSaveEmailContext,
  captured: EmailContentCapture
): InvoiceFromEmailRequestBody {
  // `/office/save`'s own email builder; only its `email` (a whole SaveRequest is refused by the contract). The
  // idempotency key argument goes into the discarded outer body, never onto the wire here.
  const { email } = buildEmailSaveRequest(context, null, '', captured);
  return {
    targetEntity: { entityType: input.targetEntity.entityType, entityId: input.targetEntity.entityId },
    email: { ...email, isNameSystemDerived: true },
    marks: toWireMarks(input.marks, captured),
    firmId: input.firmId,
    firmInvoiceNumber: input.firmInvoiceNumber,
    poNumber: input.poNumber,
    mode: input.mode,
    invoiceId: input.mode === 'Correction' ? input.invoiceId : null,
  };
}

/**
 * Submits the invoice: reads the email (context + capture), builds the §2 body and POSTs it with
 * `X-Idempotency-Key: idempotencyKey` — one value per user action, the SAME value on a retry (§4).
 *
 * Refused locally, before anything is sent:
 * - no RFC `internetMessageId` (OP-2) — as the server's `email_unidentifiable` / `no_message_id`;
 * - an attachment marked Invoice or Supporting that the capture could not send — as `marked_file_missing`.
 * A capture that could not read the email is `retryable`.
 */
export async function submitInvoiceFromEmail(
  input: InvoiceSubmissionInput,
  options: { config: InvoiceServiceConfig; email: InvoiceEmailSource; idempotencyKey: string }
): Promise<InvoiceSubmitResult> {
  const { config, email, idempotencyKey } = options;

  const context = await email.readContext();
  if (!context.internetMessageId?.trim()) {
    return { kind: 'refused', status: 0, code: EMAIL_UNIDENTIFIABLE_CODE, reason: 'no_message_id', local: true };
  }

  let captured: EmailContentCapture;
  try {
    const attachments = await email.listAttachments();
    // Invoice and Supporting files must be in the request (a read failure on one of them throws, nothing is sent);
    // the rest go into the .eml when they fit.
    const mustSend = new Set(input.marks.filter(m => m.mark !== 'DontSave').map(m => m.attachmentId));
    captured = await captureEmailContent(email.reader, attachments, mustSend);
  } catch (err) {
    return {
      kind: 'retryable',
      status: 0,
      code: undefined,
      detail: err instanceof EmailCaptureError ? err.message : "Couldn't read this email. Try again.",
    };
  }

  const sentIds = new Set(captured.attachments.map(a => a.attachmentId));
  const missing = input.marks.find(m => m.mark !== 'DontSave' && !sentIds.has(m.attachmentId));
  if (missing) {
    return {
      kind: 'refused',
      status: 0,
      code: 'ebilling.submission.marked_file_missing',
      fileName: missing.fileName,
      mark: missing.mark,
      local: true,
    };
  }

  const body = buildInvoiceFromEmailBody(input, context, captured);

  let response: Response;
  try {
    const token = await config.getAccessToken();
    response = await authenticatedJsonFetch(
      `${config.apiBaseUrl}${FROM_EMAIL_ROUTE}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': idempotencyKey },
        body: JSON.stringify(body),
      },
      token,
      { getRetryToken: config.getAccessToken }
    );
  } catch {
    // No response at all (network or token): nothing is known to have happened — the same key makes a retry safe.
    return { kind: 'retryable', status: 0, code: undefined };
  }

  const json = await readJson(response);

  if (response.ok) {
    const submissionId = stringOrUndefined(json?.submissionId);
    const statusUrl = stringOrUndefined(json?.statusUrl) ?? response.headers.get('Location') ?? undefined;
    if (submissionId && statusUrl) {
      const emlDocumentId = stringOrUndefined(json?.emlDocumentId);
      return {
        kind: 'accepted',
        accepted: {
          submissionId,
          jobId: stringOrUndefined(json?.jobId) ?? '',
          trackingNumber: stringOrUndefined(json?.trackingNumber) ?? '',
          statusUrl,
          ...(emlDocumentId ? { emlDocumentId } : {}),
        },
        replayed: response.headers.get('X-Idempotency-Status') === 'cached',
      };
    }
    // A 2xx without the contract's fields: the server answered something this client does not know. It may have
    // accepted the submission, so offer the same action again (the key makes that safe).
    return { kind: 'retryable', status: response.status, code: undefined };
  }

  const code = stringOrUndefined(json?.errorCode) ?? stringOrUndefined(json?.reasonCode);
  const detail = stringOrUndefined(json?.detail);
  if (isRetryableSubmissionRefusal(response.status, code)) {
    const retryAfterSeconds = readRetryAfterSeconds(response, json);
    return {
      kind: 'retryable',
      status: response.status,
      code,
      ...(retryAfterSeconds !== undefined ? { retryAfterSeconds } : {}),
      ...(detail ? { detail } : {}),
    };
  }
  const reason = stringOrUndefined(json?.reason);
  const fileName = stringOrUndefined(json?.fileName);
  const mark = stringOrUndefined(json?.mark);
  const invoiceStatus = stringOrUndefined(json?.invoiceStatus);
  return {
    kind: 'refused',
    status: response.status,
    code,
    ...(detail ? { detail } : {}),
    ...(reason ? { reason } : {}),
    ...(fileName ? { fileName } : {}),
    ...(mark ? { mark } : {}),
    ...(invoiceStatus ? { invoiceStatus } : {}),
  };
}

/**
 * Follows `statusUrl` (§3) until the status is final (Accepted, Rejected or Failed), waiting `initialDelayMs`, doubling
 * up to `maxDelayMs`, for at most `maxAttempts` reads. A transient read failure (network, 5xx, 429) is retried within
 * the cap; a 4xx is the end (`error`). Never polls `/api/office/jobs` (§3: `jobId` is not an Office job).
 */
export async function pollSubmission(
  statusUrl: string,
  options: {
    config: InvoiceServiceConfig;
    initialDelayMs?: number;
    maxDelayMs?: number;
    maxAttempts?: number;
    sleep?: (ms: number) => Promise<void>;
  }
): Promise<InvoicePollResult> {
  const {
    config,
    initialDelayMs = DEFAULT_POLL.initialDelayMs,
    maxDelayMs = DEFAULT_POLL.maxDelayMs,
    maxAttempts = DEFAULT_POLL.maxAttempts,
    sleep = ms => new Promise<void>(resolve => setTimeout(resolve, ms)),
  } = options;
  const url = absoluteUrl(config.apiBaseUrl, statusUrl);
  let delay = initialDelayMs;
  let lastStatus: string | undefined;

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    await sleep(delay);
    delay = Math.min(delay * 2, maxDelayMs);

    let response: Response;
    try {
      const token = await config.getAccessToken();
      response = await authenticatedJsonFetch(url, { method: 'GET' }, token, { getRetryToken: config.getAccessToken });
    } catch {
      continue;
    }
    const json = await readJson(response);
    if (!response.ok) {
      if (response.status >= 500 || response.status === 429) continue;
      return {
        kind: 'error',
        status: response.status,
        code: stringOrUndefined(json?.errorCode) ?? stringOrUndefined(json?.reasonCode),
      };
    }
    const status = stringOrUndefined(json?.status);
    lastStatus = status ?? lastStatus;
    if (status && FINAL_STATUSES.has(status)) {
      return {
        kind: 'final',
        status: status as InvoiceFinalStatus,
        outcomeReasonCode: stringOrUndefined(json?.outcomeReasonCode) ?? null,
      };
    }
  }
  return { kind: 'pending', status: lastStatus };
}

/**
 * The firms linked to a core record (§8). `[]` only when the server says none is linked (200 `[]`); any failure THROWS
 * (a 503 is never an empty list — the view must not say "no firm is linked" when it could not ask).
 */
export async function loadInvoiceFirms(
  target: InvoiceSubmissionTarget,
  config: InvoiceServiceConfig
): Promise<InvoiceFirmOption[]> {
  let response: Response;
  try {
    const token = await config.getAccessToken();
    response = await authenticatedJsonFetch(
      `${config.apiBaseUrl}/api/finance/core-records/${encodeURIComponent(target.entityType)}/${encodeURIComponent(
        target.entityId
      )}/firms`,
      { method: 'GET', headers: { 'Content-Type': 'application/json' } },
      token,
      { getRetryToken: config.getAccessToken }
    );
  } catch {
    throw new Error("Couldn't reach Spaarke to list the firms. Check your connection and try again.");
  }
  if (!response.ok) {
    if (response.status === 503) {
      throw new Error("Spaarke couldn't list the firms for this record right now. Try again in a moment.");
    }
    throw new Error((await describeFetchFailure(response)).message);
  }
  const json: unknown = await response.json().catch(() => null);
  if (!Array.isArray(json)) {
    throw new Error("Spaarke's answer about the firms for this record couldn't be read. Try again.");
  }
  return json
    .filter(
      (row): row is { firmId: string; name: string } =>
        !!row && typeof row.firmId === 'string' && typeof row.name === 'string'
    )
    .map(row => ({ firmId: row.firmId, name: row.name }));
}

/** The text context for messages: the file the user marked Invoice, from the client's own marks. */
function messageContext(input: InvoiceSubmissionInput): InvoiceMessageContext {
  const invoiceFile = input.marks.find(m => m.mark === 'Invoice')?.fileName;
  return invoiceFile ? { invoiceFileName: invoiceFile } : {};
}

/**
 * The view's {@link InvoiceSubmitHandler}: submit, then (once accepted) follow the status to a final answer, and turn
 * each into the view's generic {@link InvoiceSubmissionOutcome}. `attempt.actionId` is the `X-Idempotency-Key`.
 */
export function createInvoiceSubmitHandler(deps: {
  config: InvoiceServiceConfig;
  email: InvoiceEmailSource;
  poll?: Omit<Parameters<typeof pollSubmission>[1], 'config'>;
}): InvoiceSubmitHandler {
  return async (input, attempt) => {
    const ctx = messageContext(input);
    const result = await submitInvoiceFromEmail(input, {
      config: deps.config,
      email: deps.email,
      idempotencyKey: attempt.actionId,
    });

    if (result.kind === 'retryable') {
      // A local capture failure (status 0, nothing sent) carries the capture's own sentence for the user.
      const message = result.status === 0 && result.detail ? result.detail : describeSubmissionRefusal(result, ctx);
      return { kind: 'retryable', message };
    }
    if (result.kind === 'refused') {
      const message = describeSubmissionRefusal(result, ctx);
      return result.code === EMAIL_UNIDENTIFIABLE_CODE ? { kind: 'permanent', message } : { kind: 'refused', message };
    }

    const { trackingNumber, statusUrl } = result.accepted;
    attempt.onAccepted?.({ trackingNumber });
    const polled = await pollSubmission(statusUrl, { config: deps.config, ...deps.poll });
    const tracking = trackingNumber ? { trackingNumber } : {};

    if (polled.kind === 'final') {
      if (polled.status === 'Accepted') {
        return {
          kind: 'success',
          ...tracking,
          ...(polled.outcomeReasonCode ? { message: describeInvoiceOutcome(polled.outcomeReasonCode, ctx) } : {}),
        };
      }
      return { kind: 'refused', message: describeInvoiceOutcome(polled.outcomeReasonCode, ctx) };
    }
    // Accepted for processing, but the final answer is not known here: it is not a failure.
    return {
      kind: 'success',
      ...tracking,
      message:
        polled.kind === 'pending'
          ? 'Spaarke is still processing it. You can follow it in Spaarke with the tracking number.'
          : "Its progress couldn't be read here. You can follow it in Spaarke with the tracking number.",
    };
  };
}
