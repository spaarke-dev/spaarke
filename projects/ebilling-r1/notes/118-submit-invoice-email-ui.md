# Task 118 - Submit Invoice in the email UI

Status: done (2026-11-03). Frontend only (`Spaarke.Communication.Components` + the CommunicationActions PCF). Nothing under `src/server/**` changed.

## What shipped

Three entry points, one flow (`SubmitInvoiceFromEmail`), one transport (`createEmailInvoiceSubmission`).

| Entry point | Where | Opens |
|---|---|---|
| Toolbar "Submit Invoice" (was "Link Invoice") | `EmailToolbar` -> `EmailWorkspace` | `SubmitInvoiceDialog` (SprkModal shell, ADR-050) |
| Flagged attachment "Submit as invoice" (+ "Invoice" badge) | `EmailReadingAttachments` | the same dialog, that PDF pre-marked Invoice |
| "Invoice" tab (only with a PDF attachment) | `ReconciliationWorkspace` -> `InvoiceSubmitTab` | the same flow inline |
| CommunicationActions PCF "Submit Invoice" button (was "Link Invoice") | `CommunicationActionsApp` | the same dialog |

The flow: 118a `InvoiceAttachmentMarking` (Invoice / Supporting / Don't save), record + firm pickers, firm invoice number (required) and PO number, then Submit. Success and refusals are shown by the existing `InvoiceSubmissionOutcome` / `invoiceSubmissionErrorMessage` (reason-code wording), so 403, 409, 413, 422 and 503 (including the 118b fix round's TooLarge and per-supporting-document filing refusals) need no new strings.

## Wire contract used (094 notes section 7 + 118b)

`POST /api/finance/invoice-submissions/from-document` body: `{communicationId, documentId, coreRecord:{kind,id}, firmId, firmInvoiceNumber, poNumber?, supportingDocumentIds?}`. `supportingDocumentIds` and `poNumber` are omitted when empty. No `mode` (absent = New). Don't-save attachments are never sent. Status: `GET /api/finance/invoice-submissions/{id}`; firms: the existing core-records firms route.

Firm: this UI always sends a firm and keeps it required (a record with no active firm shows a message and Submit stays disabled). The optional-firm change of 095c-2 needs no change here.

## Reuse (CLAUDE.md section 11)

Records / firms / status come from `createInternalInvoiceSubmission` (task 052), the outcome from `InvoiceSubmissionOutcome`, the attachment read from `CommunicationAttachmentsService` (its one `sprk_document` query gained `sprk_documenttype`; query count unchanged). Only the from-document POST is new.

## Decisions

- `launchCreate` drops the `invoice` kind (`CreateKind = 'event' | 'todo'`): a raw `sprk_invoice` create form is a client-side create (ADR-002). No other host used it (grep of `src/**`).
- `ReconciliationWorkspace` gains an optional `dataService` prop (default `createXrmDataService()`): `dataverseClient.retrieveMultipleRecords` is FetchXML-only, so the Invoice tab's OData attachment read needs its own source. The Invoice tab is not gated on a confirmed Related-to (the server files an unfiled email under the chosen record); a confirmed record is only the starting choice.
- The tab's visibility probe is best-effort: a failed attachment read shows no tab, never a broken dialog.
- The PCF is React 16: `SubmitInvoiceDialog` is cast at the boundary (same as `SendEmailPage`). PCF version 1.3.20 -> 1.3.21 (manifest, `index.ts`, Solution manifest, `solution.xml`).

## Known limits / findings not fixed (out of scope)

- `ReconciliationWorkspace.resolveAttachments` / `resolveEmlArchive` hand OData option strings to an `IDataverseClient` whose `retrieveMultipleRecords` is FetchXML-only. Pre-existing; the new Invoice-tab probe does not depend on it.
- The display-only "Link Invoice" chip in `CommunicationConnections` provenance (`provenance.ts`) and the shared `logic/connections` equivalent still say "Link Invoice". They are suggestion chips, not the create launcher; left as is.
- The per-file size limit (413) is the server's; the UI only shows its reason-code wording.
- Not deployed (no PCF/solution import, no BFF change): deploy belongs to task 112.

## Tests

`SubmitInvoiceFromEmail.test.tsx` (29), `emailInvoiceSubmission.test.ts` (20), additions to `EmailWorkspace`, `ReconciliationWorkspace`, `EmailReadingAttachments`, `EmailReadingPaneShell` and the PCF `launchCreate.test.ts`. Package suite: 40 suites, 405 tests. PCF suite: 6 suites, 34 tests.
