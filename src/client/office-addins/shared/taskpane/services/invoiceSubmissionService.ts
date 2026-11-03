/**
 * invoiceSubmissionService.ts — the add-in's client for ebilling's from-email invoice submission (task 133 A1).
 *
 * Contract: `projects/ebilling-r1/notes/095-from-email-contract.md`, agreed 2026-10-27 at 717cf238d9, current at fd70b8e1f6 (work/ebilling-r1); the 2026-10-28 review round
 * (cc7bb5eeee, task/ebill-095-from-email: key v2 without the firm fields, 422 `attachment_not_preserved`, 500
 * `OFFICE_AUTH_ERROR`, the firms route's 404, `poNumber` at most 100 characters) agreed by word-add-in-r1; task 094's
 * rows (97c793670b, merged into work/ebilling-r1 at ba63082008: step 1 and step A refusals, the "filing is kept" note,
 * 503 `earlier_attempt_being_removed`, outcome `source_changed`):
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
import { cleanGuid } from '@spaarke/ui-components/guid';
import { describeFetchFailure } from '../utils/errorMessages';
import {
  describeInvoiceOutcome,
  describeSubmissionRefusal,
  EMAIL_UNIDENTIFIABLE_CODE,
  FILING_KEPT_DETAIL,
  describeAcceptedOutcome,
  isRetryableSubmissionRefusal,
  PERMANENT_SUBMISSION_CODES,
  type InvoiceMessageContext,
} from '../utils/invoiceSubmissionMessages';
import {
  captureEmailContent,
  EmailCaptureError,
  formatSize,
  hostAdapterEmailReader,
  type EmailContentCapture,
  type EmailContentReader,
  type SkippedAttachment,
} from './emailContentCapture';
import { buildEmailSaveRequest, type OfficeSaveRequestBody, type QuickSaveEmailContext } from './quickSaveHelpers';
import {
  INVOICE_CORE_RECORD_TYPES,
  type InvoiceCoreRecordType,
  type InvoiceCorrectionPrefill,
  type InvoiceFiledUnder,
  type InvoiceFirmOption,
  type InvoiceSubmissionInput,
  type InvoiceSubmissionMark,
  type InvoiceSubmissionTarget,
  type InvoiceSubmitHandler,
  type InvoiceWireMark,
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
  /** §6 "After step A" (task 094): the email was filed under the record before the refusal, and stays filed. */
  filingKept?: true;
  /** §6 (ebilling task 192): on 409 `document_core_record_mismatch`, the readable record the email is filed under. */
  filedUnder?: InvoiceFiledUnder;
}

/** A well-formed `filedUnder` (a core record type, an id and a name), or undefined: anything else is ignored. */
function readFiledUnder(value: unknown): InvoiceFiledUnder | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const v = value as Record<string, unknown>;
  const entityType = stringOrUndefined(v.entityType);
  const entityId = stringOrUndefined(v.entityId);
  const name = stringOrUndefined(v.name);
  if (!entityType || !entityId || !name) return undefined;
  if (!(INVOICE_CORE_RECORD_TYPES as string[]).includes(entityType)) return undefined;
  return { entityType: entityType as InvoiceCoreRecordType, entityId, name };
}

/**
 * What became of one submit request.
 * - `accepted`: the 202 (also a replay of it, §5);
 * - `refused`: the server (or, with `local: true`, the add-in before sending) said no; retrying the same request
 *   will not help. `code` is `errorCode ?? reasonCode` (OP-8), undefined when the server sent neither;
 * - `retryable`: transient (503, 429, the access check's system failure, …) — retry the SAME action (same key).
 *
 * `skipped` (only when there are some): attachments the capture left out of the email it sent, each with the sentence
 * that says why. Present on every result of a request that was actually sent (accepted, or answered by the server).
 */
export type InvoiceSubmitResult = (
  | { kind: 'accepted'; accepted: InvoiceSubmissionAccepted; replayed: boolean }
  | ({
      kind: 'refused';
      status: number;
      code: string | undefined;
      detail?: string;
      local?: true;
    } & InvoiceRefusalExtras)
  | {
      kind: 'retryable';
      status: number;
      code: string | undefined;
      retryAfterSeconds?: number;
      detail?: string;
      filingKept?: true;
    }
) & { skipped?: SkippedAttachment[] };

/** §3 statuses. */
export type InvoiceSubmissionStatus = 'Received' | 'Processing' | 'Accepted' | 'Rejected' | 'Failed';
export type InvoiceFinalStatus = Extract<InvoiceSubmissionStatus, 'Accepted' | 'Rejected' | 'Failed'>;

const FINAL_STATUSES: ReadonlySet<string> = new Set<InvoiceFinalStatus>(['Accepted', 'Rejected', 'Failed']);

/** What {@link pollSubmission} ends with. */
export type InvoicePollResult =
  | {
      kind: 'final';
      status: InvoiceFinalStatus;
      outcomeReasonCode: string | null;
      /** §9 (ebilling 097f): the correcting submission's tracking number, when the server names it. */
      replacedByTrackingNumber?: string;
    }
  /** The cap was reached while the submission was still Received / Processing (or unreadable for a moment). */
  | { kind: 'pending'; status: string | undefined }
  /** The status cannot be read at all (404, or a refusal that will not change). */
  | { kind: 'error'; status: number; code: string | undefined }
  /** The caller's `signal` was aborted (the view went away): nothing more was read. */
  | { kind: 'aborted' };

export const FROM_EMAIL_ROUTE = '/api/finance/invoice-submissions/from-email';

/** Shown when the open email (its context, attachment list or content) could not be read. Nothing was sent. */
export const EMAIL_READ_FAILED_MESSAGE = "Couldn't read this email, so nothing was submitted. Try again.";

/**
 * The capture's failure in Submit Invoice wording. {@link EmailCaptureError.message} is the Save view's text ("untick
 * it", "nothing was saved"), which does not apply here; this one says what to do on this view.
 */
export function describeCaptureFailure(err: EmailCaptureError): string {
  const failure = err.failure;
  switch (failure?.kind) {
    case 'attachment-unreadable':
      return `Couldn't read '${failure.fileName}'. Mark it Don't save, or try again. Nothing was submitted.`;
    case 'body-too-large':
      return (
        `This email's text is too large to submit (${formatSize(failure.sizeBytes)}; one submission can carry about ` +
        `${formatSize(failure.budgetBytes)}). Nothing was submitted.`
      );
    default:
      return EMAIL_READ_FAILED_MESSAGE;
  }
}

/** Default polling: 2 s, doubling to at most 10 s, for up to 30 reads (about 4.5 minutes). */
export const DEFAULT_POLL = { initialDelayMs: 2000, maxDelayMs: 10_000, maxAttempts: 30 } as const;

const MAX_RETRY_AFTER_SECONDS = 300;

function stringOrUndefined(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

/**
 * §6's client rule, `errorCode ?? reasonCode`; on a 403 only, then `extensions.code`. Task 094's step A 403
 * (`COMMUNICATION_REFILE_FORBIDDEN`) went out through the problem middleware, which writes the code only under
 * `extensions`; ebilling is moving it to `errorCode` (2026-10-29), and this stays as a fallback. Other statuses never
 * read it: the middleware's 401 `obo_failed` and 502/504 Graph errors keep their code-less handling.
 */
function problemCode(status: number, json: Record<string, unknown> | null): string | undefined {
  const extensions = json?.extensions;
  return (
    stringOrUndefined(json?.errorCode) ??
    stringOrUndefined(json?.reasonCode) ??
    (status === 403 && extensions && typeof extensions === 'object'
      ? stringOrUndefined((extensions as Record<string, unknown>).code)
      : undefined)
  );
}

/** §6 "After step A": the top-level `filingKept: true`, or a `detail` that ends with the contract's pinned sentence. */
function isFilingKept(json: Record<string, unknown> | null, detail: string | undefined): boolean {
  return json?.filingKept === true || detail?.trimEnd().endsWith(FILING_KEPT_DETAIL) === true;
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

/** A timer that ends early when `signal` aborts (the caller then checks the signal). */
function abortableSleep(ms: number, signal: AbortSignal | undefined): Promise<void> {
  return new Promise<void>(resolve => {
    if (signal?.aborted) {
      resolve();
      return;
    }
    const onAbort = (): void => {
      clearTimeout(timer);
      resolve();
    };
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, ms);
    signal?.addEventListener('abort', onAbort, { once: true });
  });
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
 * A context read or capture that could not read the email is `retryable`, with a sentence for the user (never the
 * adapter's own text).
 */
export async function submitInvoiceFromEmail(
  input: InvoiceSubmissionInput,
  options: { config: InvoiceServiceConfig; email: InvoiceEmailSource; idempotencyKey: string; signal?: AbortSignal }
): Promise<InvoiceSubmitResult> {
  const { config, email, idempotencyKey, signal } = options;

  let context: QuickSaveEmailContext;
  try {
    context = await email.readContext();
  } catch {
    // An adapter error is Outlook's wording, not the user's: say the capture's kind of sentence instead.
    return { kind: 'retryable', status: 0, code: undefined, detail: EMAIL_READ_FAILED_MESSAGE };
  }
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
      detail: err instanceof EmailCaptureError ? describeCaptureFailure(err) : EMAIL_READ_FAILED_MESSAGE,
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
      // Never silent: why the capture left files out (a large body that used the budget, say) is the reason this
      // marked file is missing, so it travels with the refusal.
      ...(captured.skipped.length > 0 ? { skipped: captured.skipped } : {}),
    };
  }

  const body = buildInvoiceFromEmailBody(input, context, captured);
  const result = await postFromEmail(body, { config, idempotencyKey, signal });
  // Never silent: what the capture left out of the email travels with the answer, for the view to show.
  return captured.skipped.length > 0 ? { ...result, skipped: captured.skipped } : result;
}

/** POSTs the §2 body and classifies the answer (§3, §5, §6). */
async function postFromEmail(
  body: InvoiceFromEmailRequestBody,
  options: { config: InvoiceServiceConfig; idempotencyKey: string; signal: AbortSignal | undefined }
): Promise<InvoiceSubmitResult> {
  const { config, idempotencyKey, signal } = options;
  let response: Response;
  try {
    const token = await config.getAccessToken();
    response = await authenticatedJsonFetch(
      `${config.apiBaseUrl}${FROM_EMAIL_ROUTE}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Idempotency-Key': idempotencyKey },
        body: JSON.stringify(body),
        ...(signal ? { signal } : {}),
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

  const code = problemCode(response.status, json);
  const detail = stringOrUndefined(json?.detail);
  const filingKept = isFilingKept(json, detail) ? ({ filingKept: true } as const) : {};
  if (isRetryableSubmissionRefusal(response.status, code)) {
    const retryAfterSeconds = readRetryAfterSeconds(response, json);
    return {
      kind: 'retryable',
      status: response.status,
      code,
      ...(retryAfterSeconds !== undefined ? { retryAfterSeconds } : {}),
      ...(detail ? { detail } : {}),
      ...filingKept,
    };
  }
  const reason = stringOrUndefined(json?.reason);
  const fileName = stringOrUndefined(json?.fileName);
  const mark = stringOrUndefined(json?.mark);
  const invoiceStatus = stringOrUndefined(json?.invoiceStatus);
  const filedUnder = readFiledUnder(json?.filedUnder);
  return {
    kind: 'refused',
    status: response.status,
    code,
    ...(detail ? { detail } : {}),
    ...(reason ? { reason } : {}),
    ...(fileName ? { fileName } : {}),
    ...(mark ? { mark } : {}),
    ...(invoiceStatus ? { invoiceStatus } : {}),
    ...(filedUnder ? { filedUnder } : {}),
    ...filingKept,
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
    /** Aborting stops the polling at once (no further read is made) and ends it as `aborted`. */
    signal?: AbortSignal;
  }
): Promise<InvoicePollResult> {
  const {
    config,
    initialDelayMs = DEFAULT_POLL.initialDelayMs,
    maxDelayMs = DEFAULT_POLL.maxDelayMs,
    maxAttempts = DEFAULT_POLL.maxAttempts,
    signal,
    sleep = ms => abortableSleep(ms, signal),
  } = options;
  const url = absoluteUrl(config.apiBaseUrl, statusUrl);
  let delay = initialDelayMs;
  let lastStatus: string | undefined;

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    if (signal?.aborted) return { kind: 'aborted' };
    await sleep(delay);
    if (signal?.aborted) return { kind: 'aborted' };
    delay = Math.min(delay * 2, maxDelayMs);

    let response: Response;
    try {
      const token = await config.getAccessToken();
      if (signal?.aborted) return { kind: 'aborted' };
      response = await authenticatedJsonFetch(url, { method: 'GET', ...(signal ? { signal } : {}) }, token, {
        getRetryToken: config.getAccessToken,
      });
    } catch {
      continue;
    }
    const json = await readJson(response);
    if (!response.ok) {
      if (response.status >= 500 || response.status === 429) continue;
      return { kind: 'error', status: response.status, code: problemCode(response.status, json) };
    }
    const status = stringOrUndefined(json?.status);
    lastStatus = status ?? lastStatus;
    if (status && FINAL_STATUSES.has(status)) {
      const replacedBy = json?.replacedBy;
      const replacedByTrackingNumber =
        replacedBy && typeof replacedBy === 'object'
          ? stringOrUndefined((replacedBy as Record<string, unknown>).trackingNumber)
          : undefined;
      return {
        kind: 'final',
        status: status as InvoiceFinalStatus,
        outcomeReasonCode: stringOrUndefined(json?.outcomeReasonCode) ?? null,
        ...(replacedByTrackingNumber ? { replacedByTrackingNumber } : {}),
      };
    }
  }
  return signal?.aborted ? { kind: 'aborted' } : { kind: 'pending', status: lastStatus };
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
    if (response.status === 404) {
      // §8 (2026-10-28): the uniform read gate — no Read on the record, not found, or a refused or faulted rights read.
      throw new Error("This record isn't available to you: it may not exist, or you can't open it.");
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

const UNREADABLE_PREFILL_MESSAGE = "Spaarke's answer about this invoice couldn't be read. Try again.";

/** A §10 body, or undefined when any field the contract makes required is missing or of the wrong type. */
function readCorrectionPrefill(json: unknown): InvoiceCorrectionPrefill | undefined {
  if (!json || typeof json !== 'object') return undefined;
  const body = json as Record<string, unknown>;
  const firm = body.firm && typeof body.firm === 'object' ? (body.firm as Record<string, unknown>) : undefined;
  const firmId = stringOrUndefined(firm?.id);
  const firmName = stringOrUndefined(firm?.name);
  const coreRecord = readFiledUnder(body.coreRecord);
  const invoiceId = stringOrUndefined(body.invoiceId);
  const firmInvoiceNumber = stringOrUndefined(body.firmInvoiceNumber);
  if (!invoiceId || !firmId || firmName === undefined || !coreRecord || firmInvoiceNumber === undefined)
    return undefined;
  if (typeof body.canCorrect !== 'boolean') return undefined;
  const replaced =
    body.replacedBy && typeof body.replacedBy === 'object' ? (body.replacedBy as Record<string, unknown>) : undefined;
  const replacedId = stringOrUndefined(replaced?.invoiceId);
  const replacedName = stringOrUndefined(replaced?.name);
  return {
    invoiceId,
    firm: { id: firmId, name: firmName },
    coreRecord,
    invoiceStatus: stringOrUndefined(body.invoiceStatus) ?? null,
    trackingNumber: stringOrUndefined(body.trackingNumber) ?? null,
    firmInvoiceNumber,
    canCorrect: body.canCorrect,
    reason: stringOrUndefined(body.reason) ?? null,
    replacedBy: replacedId && replacedName ? { invoiceId: replacedId, name: replacedName } : null,
  };
}

/**
 * The correction prefill of an invoice (§10, ebilling 097e). Any failure THROWS with a sentence for the user: the
 * uniform 404 (no Write on the invoice, not found, or a faulted rights read), 503 `ebilling.disabled`, any other 503,
 * no answer, or a body that can't be read.
 */
export async function loadCorrectionPrefill(
  invoiceId: string,
  config: InvoiceServiceConfig
): Promise<InvoiceCorrectionPrefill> {
  let response: Response;
  try {
    const token = await config.getAccessToken();
    response = await authenticatedJsonFetch(
      `${config.apiBaseUrl}/api/finance/invoices/${encodeURIComponent(cleanGuid(invoiceId))}/correction-prefill`,
      { method: 'GET', headers: { 'Content-Type': 'application/json' } },
      token,
      { getRetryToken: config.getAccessToken }
    );
  } catch {
    throw new Error("Couldn't reach Spaarke to read this invoice. Check your connection and try again.");
  }
  if (!response.ok) {
    if (response.status === 404) {
      throw new Error("This invoice isn't available to you: it may not exist, or you can't change it.");
    }
    if (response.status === 503) {
      const json = (await response.json().catch(() => null)) as Record<string, unknown> | null;
      if (problemCode(503, json) === 'ebilling.disabled')
        throw new Error('Invoice submission is turned off in Spaarke.');
      throw new Error("Spaarke couldn't read this invoice right now. Try again in a moment.");
    }
    throw new Error((await describeFetchFailure(response)).message);
  }
  const prefill = readCorrectionPrefill(await response.json().catch(() => null));
  if (!prefill) throw new Error(UNREADABLE_PREFILL_MESSAGE);
  return prefill;
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
      ...(attempt.signal ? { signal: attempt.signal } : {}),
    });
    const skipped = result.skipped ? { skipped: result.skipped } : {};

    if (result.kind === 'retryable') {
      // A local capture failure (status 0, nothing sent) carries the capture's own sentence for the user.
      const message = result.status === 0 && result.detail ? result.detail : describeSubmissionRefusal(result, ctx);
      return { kind: 'retryable', message, ...skipped };
    }
    if (result.kind === 'refused') {
      const message = describeSubmissionRefusal(result, ctx);
      return result.code !== undefined && PERMANENT_SUBMISSION_CODES.has(result.code)
        ? { kind: 'permanent', message, ...skipped }
        : { kind: 'refused', message, ...(result.filedUnder ? { filedUnder: result.filedUnder } : {}), ...skipped };
    }

    const { trackingNumber, statusUrl } = result.accepted;
    attempt.onAccepted?.({ trackingNumber });
    const polled = await pollSubmission(statusUrl, {
      config: deps.config,
      ...deps.poll,
      ...(attempt.signal ? { signal: attempt.signal } : {}),
    });
    const tracking = trackingNumber ? { trackingNumber } : {};

    if (polled.kind === 'final') {
      if (polled.status === 'Accepted') {
        // An Accepted invoice with a code is HELD (§9), except one replaced by a correction (097f): never the catalog's
        // Failed / Rejected sentences.
        const message = describeAcceptedOutcome(polled.outcomeReasonCode, polled.replacedByTrackingNumber);
        return { kind: 'success', ...tracking, ...(message ? { message } : {}), ...skipped };
      }
      // Rejected or Failed AFTER the 202: the submission exists (it has a tracking number), so it is not "not submitted".
      return {
        kind: 'ended',
        status: polled.status,
        message: describeInvoiceOutcome(polled.outcomeReasonCode, ctx, polled.status),
        ...tracking,
        ...skipped,
      };
    }
    // Accepted for processing, but the final answer is not known here: it is not a failure.
    return {
      kind: 'success',
      ...tracking,
      message:
        polled.kind === 'pending'
          ? 'Spaarke is still processing it. You can follow it in Spaarke with the tracking number.'
          : polled.kind === 'aborted'
            ? 'You left this view before its result was known. You can follow it in Spaarke with the tracking number.'
            : "Its progress couldn't be read here. You can follow it in Spaarke with the tracking number.",
      ...skipped,
    };
  };
}
