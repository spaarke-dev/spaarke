/**
 * InvoiceSubmission types (ebilling-r1 task 051, FR-18 / FR-19).
 *
 * The wizard is host-agnostic (ADR-012): it never imports SPA auth, Xrm or a BFF URL. The host (the external SPA
 * today, the internal CreateInvoiceWizard rewire in task 052) injects three data callbacks and adapts its own
 * transport errors to {@link InvoiceSubmissionError}. Shapes mirror the BFF contract in
 * projects/ebilling-r1/notes/044-submission-operation.md section 2.
 */

/** The core record kinds an invoice is submitted against (the BFF routes: matters, projects, workassignments). */
export type InvoiceCoreRecordKind = 'matter' | 'project' | 'workassignment';

/** One choosable core record in the Record step. */
export interface InvoiceCoreRecordOption {
  kind: InvoiceCoreRecordKind;
  /** The record GUID. */
  recordId: string;
  /** Display name. */
  name: string;
  /** Human-readable number or reference, when the record has one. */
  number?: string | null;
}

/** What `loadRecords` resolves with. `truncated` is true when the list is a prefix of the accessible records. */
export interface InvoiceCoreRecordList {
  records: InvoiceCoreRecordOption[];
  truncated?: boolean;
}

/** What the wizard hands the host's `submit` callback. The firm is the server's to decide, so none is named here. */
export interface InvoiceSubmissionInput {
  record: InvoiceCoreRecordOption;
  file: File;
  /** Required (D-91); already trimmed and non-empty. */
  firmInvoiceNumber: string;
  /** Optional; trimmed, undefined when blank. */
  poNumber?: string;
}

/** The 202 body of the submit route. */
export interface InvoiceSubmissionAccepted {
  submissionId: string;
  jobId?: string | null;
  /** Never null from the BFF today (D-87 / D-91); tolerated as null so a host adapter cannot break the screen. */
  trackingNumber: string | null;
  statusUrl?: string | null;
}

/** The server-side lifecycle of a submission. `Received` and `Processing` both read as "Processing" to the user. */
export type InvoiceSubmissionStatusName = 'Received' | 'Processing' | 'Accepted' | 'Rejected' | 'Failed';

/** The status view both GET routes return (the fields this UI reads). */
export interface InvoiceSubmissionStatusView {
  submissionId: string;
  trackingNumber?: string | null;
  firmInvoiceNumber?: string | null;
  poNumber?: string | null;
  /** Null when the stored value is unknown to the BFF. */
  status: string | null;
  /** The stable reason code of a terminal outcome, when the job set one. */
  outcomeReasonCode?: string | null;
  receivedOn?: string | null;
}

/** The three data callbacks a host provides. */
export interface InvoiceSubmissionCallbacks {
  /** The accessible core records the user may choose from. */
  loadRecords: () => Promise<InvoiceCoreRecordList>;
  /** Submits the invoice. Rejects with {@link InvoiceSubmissionError} (or any Error) on a refusal. */
  submit: (input: InvoiceSubmissionInput) => Promise<InvoiceSubmissionAccepted>;
  /** Reads one submission's current status from the server. */
  getStatus: (submissionId: string) => Promise<InvoiceSubmissionStatusView>;
}

/**
 * A refusal from the host's transport, in the shape the screen needs. The host adapts its own error type (the SPA's
 * `ApiError` carrying a ProblemDetails body) to this; the screen never sees a transport type.
 */
export class InvoiceSubmissionError extends Error {
  /** HTTP status of the refusal, when it came from the server. Absent for a network failure. */
  readonly statusCode?: number;
  /** The ProblemDetails `errorCode` extension (for example `ebilling.submission.not_pdf`). */
  readonly errorCode?: string;
  /** The ProblemDetails `detail`: the server's own user-facing sentence. */
  readonly detail?: string;

  constructor(init: { message?: string; statusCode?: number; errorCode?: string; detail?: string }) {
    super(init.message ?? init.detail ?? 'The invoice submission request failed.');
    this.name = 'InvoiceSubmissionError';
    this.statusCode = init.statusCode;
    this.errorCode = init.errorCode;
    this.detail = init.detail;
  }
}

/** The `errorCode` of a feature-off answer (503). A host hides its submit entry on this code only. */
export const EBILLING_DISABLED_ERROR_CODE = 'ebilling.disabled';

/** True for the 503 `ebilling.disabled` answer, and for nothing else (an outage 503 carries another code). */
export function isEbillingDisabledError(err: unknown): boolean {
  return (
    err instanceof InvoiceSubmissionError && err.statusCode === 503 && err.errorCode === EBILLING_DISABLED_ERROR_CODE
  );
}

/** Statuses after which the server will not change the answer. */
export function isTerminalStatus(status: string | null | undefined): boolean {
  return status === 'Accepted' || status === 'Rejected' || status === 'Failed';
}
