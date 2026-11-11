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

/**
 * What `loadRecords` resolves with. `truncated` is true when the list is a prefix of the accessible records (a row cap
 * was reached, so a record may be missing and the search box cannot find it). `partial` is true when a source could not be
 * read at all, so the records of that source are missing (a retry may fix it). The Record step says which.
 */
export interface InvoiceCoreRecordList {
  records: InvoiceCoreRecordOption[];
  truncated?: boolean;
  partial?: boolean;
}

/**
 * What the wizard asks `loadRecords` for. `firmId` is set when the firm of the invoice is known (the one firm of a
 * single-firm caller, or the one chosen in the "Choose firm" step): the host lists only the records linked to that firm
 * (D-106), because the server refuses a firm that is not linked to the record. Absent when the firms are unknown.
 */
export interface InvoiceRecordQuery {
  firmId?: string;
}

/** One firm the user can submit for (a row of the "Choose firm" step). */
export interface InvoiceFirmOption {
  /** The firm's organization GUID: the value a host sends as `firmId`. */
  firmId: string;
  /** Display name. */
  name: string;
}

/**
 * What the wizard hands the host's `submit` callback. The server decides authorization and, for a single-firm caller,
 * the firm itself (so none is named); `firmId` is set only when the user chose one in the "Choose firm" step.
 */
export interface InvoiceSubmissionInput {
  record: InvoiceCoreRecordOption;
  file: File;
  /** Required (D-91); already trimmed and non-empty. */
  firmInvoiceNumber: string;
  /** Optional; trimmed, undefined when blank. */
  poNumber?: string;
  /**
   * The firm the user picked in the "Choose firm" step, which exists only for a caller with more than one firm. Absent
   * for everyone else. The server still checks it is one of the caller's own firms (403 otherwise).
   */
  firmId?: string;
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

/** The data callbacks a host provides (`loadFirms` is optional). */
export interface InvoiceSubmissionCallbacks {
  /**
   * The accessible core records the user may choose from. Called again when the firm changes; `query.firmId` (when set)
   * asks for the records linked to that firm only.
   */
  loadRecords: (query: InvoiceRecordQuery) => Promise<InvoiceCoreRecordList>;
  /**
   * The firms the signed-in user belongs to; `null` means UNKNOWN (an older server, eBilling off, a failed read), which
   * is not "no firm". The wizard adds a required "Choose firm" step only when this resolves with MORE THAN ONE firm;
   * with none, one, null or a rejection there is no step and no `firmId` is sent (the server picks a single firm itself).
   * When the server then answers `firm_required` (it could not pick), the wizard calls this again and, if the caller now
   * has several firms, inserts the step. A host with no firm concept omits it.
   */
  loadFirms?: () => Promise<InvoiceFirmOption[] | null>;
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

/** The `errorCode` of "the caller has several firms and none was named" (400). The wizard answers it by asking for the firms again. */
export const FIRM_REQUIRED_ERROR_CODE = 'ebilling.submission.firm_required';

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
