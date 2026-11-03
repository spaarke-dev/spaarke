/**
 * emailInvoiceSubmission — the transport behind the email UI's "Submit Invoice" flow (ebilling-r1 task 118).
 *
 * The three entry points (toolbar, flagged attachment, Email Review "Invoice" tab) all run ONE flow
 * (`SubmitInvoiceFromEmail`), and that flow talks to the server only through the object this module builds.
 * The client never creates a `sprk_invoice`, a submission or a Stored File and never decides authorization
 * (model 9.1, ADR-002, FR-17): it names the chosen attachments and shows the server's answer.
 *
 *   POST /api/finance/invoice-submissions/from-document   (task 094; `supportingDocumentIds` from task 118b)
 *   GET  /api/finance/invoice-submissions/{id}             (the status view `InvoiceSubmissionOutcome` polls)
 *
 * Reuse (CLAUDE.md section 11): the record list, the record's firms and the status read are
 * `createInternalInvoiceSubmission`'s (`@spaarke/ui-components`, task 052); the attachment read is
 * `CommunicationAttachmentsService`'s single query (it now also returns the document's `sprk_documenttype`,
 * the "Invoice" flag). Only the from-document POST is new here.
 *
 * ADR-012: no Xrm, MSAL or BFF URL. The host injects `authenticatedFetch` (and an optional base URL) and the
 * Dataverse read. The injected fetch may THROW an `ApiError` (what `@spaarke/auth` does for every non-2xx) or
 * RETURN a non-2xx `Response`; both become an `InvoiceSubmissionError` the screen words by `errorCode`.
 */
import {
  InvoiceSubmissionError,
  createInternalInvoiceSubmission,
  type IDataService,
  type InvoiceCoreRecordKind,
  type InvoiceCoreRecordList,
  type InvoiceCoreRecordOption,
  type InvoiceFirmOption,
  type InvoiceSubmissionAccepted,
  type InvoiceSubmissionStatusView,
} from '@spaarke/ui-components';
import { CommunicationAttachmentsService, isInvoiceDocumentType } from '../attachments';
import type { IAttachmentItem, IAttachmentRecord, IAttachmentsWebApi } from '../attachments';
import type { MarkableAttachment } from '../../components/InvoiceAttachmentMarking';

/** The Dataverse read the flow needs: the same OData `retrieveMultipleRecords` `IDataService` / `context.webAPI` expose. */
export interface IEmailInvoiceDataService {
  retrieveMultipleRecords(entityName: string, options?: string): Promise<{ entities: Record<string, unknown>[] }>;
}

/** Either-shape fetch: `@spaarke/auth`'s throws `ApiError` on non-2xx; another host's may return the failed `Response`. */
export type EmailInvoiceFetch = (url: string, init?: RequestInit) => Promise<Response>;

/** What the flow hands `submit`: the marked attachments, the record, the firm and the firm's invoice number. */
export interface SubmitInvoiceFromDocumentInput {
  /** The `sprk_communication` the attachments belong to. */
  communicationId: string;
  /** The `sprk_document` marked Invoice (exactly one). */
  invoiceDocumentId: string;
  /** The `sprk_document`s marked Supporting (may be empty). */
  supportingDocumentIds: readonly string[];
  record: Pick<InvoiceCoreRecordOption, 'kind' | 'recordId'>;
  firmId: string;
  /** Required by the server; already trimmed and non-empty. */
  firmInvoiceNumber: string;
  /** Optional; trimmed, undefined when blank. */
  poNumber?: string;
}

/** The request body of `POST /api/finance/invoice-submissions/from-document` (094 notes section 7). */
export interface FromDocumentRequestBody {
  communicationId: string;
  documentId: string;
  coreRecord: { kind: InvoiceCoreRecordKind; id: string };
  firmId: string;
  firmInvoiceNumber: string;
  poNumber?: string;
  supportingDocumentIds?: string[];
}

/**
 * The wire body. Carries the communication, the invoice document, the supporting documents (only when there are
 * some: absent behaves exactly as before 118b) and the record / firm / number the server needs. Attachments marked
 * "Don't save" never reach it. No `mode`: the email UI only submits a NEW invoice (absent is `New`).
 */
export function buildFromDocumentBody(input: SubmitInvoiceFromDocumentInput): FromDocumentRequestBody {
  const body: FromDocumentRequestBody = {
    communicationId: input.communicationId,
    documentId: input.invoiceDocumentId,
    coreRecord: { kind: input.record.kind, id: input.record.recordId },
    firmId: input.firmId,
    firmInvoiceNumber: input.firmInvoiceNumber,
  };
  if (input.poNumber) body.poNumber = input.poNumber;
  if (input.supportingDocumentIds.length > 0) body.supportingDocumentIds = [...input.supportingDocumentIds];
  return body;
}

/** The object the flow talks to. Built by {@link createEmailInvoiceSubmission}; tests and other hosts may supply their own. */
export interface EmailInvoiceSubmission {
  /** The email's file attachments as markable rows (id = the attachment's `sprk_document` id). */
  loadAttachments: (communicationId: string) => Promise<MarkableAttachment[]>;
  /** The core records (matter / project / work assignment) the user may submit against. */
  loadRecords: () => Promise<InvoiceCoreRecordList>;
  /** The active firms linked to a record (D-106). */
  loadFirms: (record: InvoiceCoreRecordOption) => Promise<InvoiceFirmOption[]>;
  /** Submits; rejects with an `InvoiceSubmissionError` on a refusal (403, 409, 413, 422, 503 ...). */
  submit: (input: SubmitInvoiceFromDocumentInput) => Promise<InvoiceSubmissionAccepted>;
  /** One submission's current status, polled by `InvoiceSubmissionOutcome`. */
  getStatus: (submissionId: string) => Promise<InvoiceSubmissionStatusView>;
}

export interface EmailInvoiceSubmissionOptions {
  /** The host's fetch (`@spaarke/auth`'s `authenticatedFetch` in every Dataverse host). */
  authenticatedFetch: EmailInvoiceFetch;
  /** Dataverse reads (attachments, documents, the record picker). */
  dataService: IEmailInvoiceDataService;
  /**
   * BFF base URL without `/api`. Optional: `@spaarke/auth`'s fetch resolves a relative `/api/...` path against its
   * own configured base, so a host that has no base URL at hand passes none.
   */
  bffBaseUrl?: string;
}

const FROM_DOCUMENT_PATH = '/api/finance/invoice-submissions/from-document';

/** Shown for an attachment with no saved document: the server can copy only a `sprk_document`. */
export const NO_DOCUMENT_REASON = 'This attachment has no saved document, so it cannot be submitted.';

/**
 * The attachment rows as markable rows. `id` is the `sprk_document` id (the id the wire carries); two rows of one document
 * count once, and a row with no document is listed disabled (it can never be sent). `suggestedInvoice` is the profile's
 * Invoice flag (a hint only: any PDF can be marked Invoice).
 */
export function toMarkableAttachments(items: readonly IAttachmentItem[]): MarkableAttachment[] {
  const seen = new Set<string>();
  const out: MarkableAttachment[] = [];
  for (const item of items) {
    const fileName = item.name || item.documentName || 'Attachment';
    if (!item.documentId) {
      out.push({ id: `attachment:${item.attachmentId}`, fileName, disabledReason: NO_DOCUMENT_REASON });
      continue;
    }
    const key = item.documentId.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    out.push({ id: item.documentId, fileName, suggestedInvoice: isInvoiceDocumentType(item.documentType) });
  }
  return out;
}

function hasName(err: unknown, name: string): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === name;
}

/** Problem members as the screen's error type: the server's `detail` and `errorCode` win over any wording of ours. */
function fromProblem(status: number | undefined, problem: Record<string, unknown> | null): InvoiceSubmissionError {
  const detail = typeof problem?.detail === 'string' && problem.detail.trim() ? problem.detail : undefined;
  const errorCode = typeof problem?.errorCode === 'string' && problem.errorCode ? problem.errorCode : undefined;
  return new InvoiceSubmissionError({ statusCode: status, errorCode, detail });
}

/**
 * A thrown failure of the injected fetch as the screen's error type. Structural (by `name` + fields), never
 * `instanceof`: a second bundled copy of `@spaarke/auth` fails `instanceof` silently. No status means no answer at
 * all (a network failure): the screen then says the outcome is unconfirmed.
 */
export function toSubmissionError(err: unknown): InvoiceSubmissionError {
  if (err instanceof InvoiceSubmissionError) return err;
  if (hasName(err, 'ApiError') && typeof (err as { status?: unknown }).status === 'number') {
    const problem = (err as { problemDetails?: unknown }).problemDetails;
    return fromProblem(
      (err as { status: number }).status,
      typeof problem === 'object' && problem !== null ? (problem as Record<string, unknown>) : null
    );
  }
  return new InvoiceSubmissionError({ message: err instanceof Error ? err.message : undefined });
}

/** Adapts the host's OData read to the attachment service's shape (as `EmailReadingAttachments` does). */
function toAttachmentsWebApi(dataService: IEmailInvoiceDataService): IAttachmentsWebApi {
  return {
    async retrieveMultipleRecords<T = IAttachmentRecord>(entityLogicalName: string, options?: string) {
      const result = await dataService.retrieveMultipleRecords(entityLogicalName, options);
      return { entities: result.entities as unknown as T[] };
    },
  };
}

export function createEmailInvoiceSubmission(options: EmailInvoiceSubmissionOptions): EmailInvoiceSubmission {
  const { authenticatedFetch, dataService } = options;
  const baseUrl = options.bffBaseUrl ?? '';
  const attachments = new CommunicationAttachmentsService(toAttachmentsWebApi(dataService));

  // The record list and the status read are the internal wizard's adapter (task 052). It only ever calls
  // `retrieveMultipleRecords` on the data service, so the narrow read stands in for the full `IDataService`.
  const adapterFor = (record?: InvoiceCoreRecordOption) =>
    createInternalInvoiceSubmission({
      fetch: authenticatedFetch,
      baseUrl,
      dataService: dataService as unknown as IDataService,
      initialRecord: record,
      lockRecord: record !== undefined,
    });
  const unlocked = adapterFor();

  return {
    async loadAttachments(communicationId) {
      return toMarkableAttachments(await attachments.getFileAttachments(communicationId));
    },

    loadRecords: () => unlocked.loadRecords({}),

    // A locked adapter knows its record up front, which is what `loadFirms` needs (the firms depend on the record).
    loadFirms: record =>
      adapterFor(record)
        .loadFirms()
        .then(firms => firms ?? []),

    async submit(input) {
      let response: Response;
      try {
        response = await authenticatedFetch(`${baseUrl.replace(/\/+$/, '')}${FROM_DOCUMENT_PATH}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify(buildFromDocumentBody(input)),
        });
      } catch (err) {
        throw toSubmissionError(err);
      }
      if (!response.ok) {
        // A body that is not ProblemDetails JSON leaves the status alone to speak.
        const body: unknown = await response.json().catch(() => null);
        throw fromProblem(
          response.status,
          typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null
        );
      }
      try {
        return (await response.json()) as InvoiceSubmissionAccepted;
      } catch {
        throw new InvoiceSubmissionError({
          statusCode: response.status,
          message: 'The answer of the server could not be read.',
        });
      }
    },

    getStatus: submissionId => unlocked.getStatus(submissionId),
  };
}
