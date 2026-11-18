# Task 132: Notifications (server half): firm email, SPA feed, approver in-app

Branch `task/ebilling-132`, from `08134f4483`. Rigor STANDARD. Model: Sonnet @ high. FR-39, FR-50, NFR-10. Amended by EB-D-254 (no PDF, no task 133), EB-D-258 (server half only; 132ui fills the wizard notify step), EB-D-259 (the confirmed prose stays recoverable), uac-r2 N2 (prose is rendered only through `RenderHtml`; a reviewer subject is header-safe).

No Dataverse write, no push, no deploy was made. No App Service setting is added (task 136: none).

## What was built

| Piece | File | What it does |
|---|---|---|
| Enqueuer | `Services/Finance/EbillingNotificationEnqueuer.cs` | One `IJobHandler` job per (event, subject id). Never throws (15 s bound): the decision has already happened. Job id and idempotency key `ebilling-notify:{event}:{id:N}`. A Return or Reject carries the confirmed notice only when both parts are present, within caps (subject 280, prose 1500). |
| Sink | `Services/Finance/Submission/EbillingNotificationSink.cs` | Replaces the logging-only sink: Accepted enqueues `InvoiceAccepted`, Rejected enqueues `SubmissionRejected` (a `Blocked` rejection is held and not told), Returned enqueues `ReturnedToFirm` (no notice, so no email). |
| Recipients | `Services/Finance/EbillingNotificationRecipients.cs` | Reads the subject's facts, the firm name, and resolves the firm addresses: the submitter plus the firm's billing contact, active contacts only, well-formed address, de-duplicated. Logs ids only. |
| Job handler | `Services/Jobs/Handlers/EbillingNotificationJobHandler.cs` | The firm email and the approver notifications (below). |
| Composer addition | `Services/Finance/FirmNoticeComposer.cs` | `ComposeConfirmedForInvoiceAsync`: screens the reviewer's subject and prose with the same `FirmNoticeProse.Check` and renders through `RenderHtml`. `FirmNoticeSource.Reviewer`. |
| Decision requests | `Api/Finance/InvoiceRejectReturnEndpoints.cs`, `InvoiceApprovalEndpoints.cs` | After a `Decided` / `Approved` outcome the route enqueues the job. Reject and Return take two optional request fields, `NoticeSubject` and `NoticeProse`. The enqueuer parameter is optional (null in a host that registers none). |
| Feed source | `Api/ExternalAccess/EbillingFeedSource.cs`, `ExternalNotificationFeedEndpoints.cs`, `FirmOutcomeAuthorizationFilter.cs` | The derived eBilling source of the external feed (below). |
| DI | `Infrastructure/DI/FinanceModule.cs` | Sink mapping changed (still one mapping, so the ADR-010 ceiling is unchanged); enqueuer, recipients, handler, feed source added. |

### Placement (§11)

- **Existing**: the 131 composer, `CommunicationService`, `ServiceBusJobProcessor` / `IJobHandler` (ADR-004), the external feed, 130's invoice rule, `IActionSeam`, `EligibleApproverResolver` (103), and the 107 sink seam.
- **Extension**: all of them are extended or called; the only new surface is the enqueuer, the recipients reader, the handler and the feed source, each answering a concrete missing behaviour (no sender, no recipient rule, no feed source). The feed has no new route and no change of item shape.
- **Cost of doing nothing**: a firm is never told of a rejection, a return or an adjustment, and an approver is never told an invoice is waiting.

## Behaviour

| Event | Firm email | Notes |
|---|---|---|
| `SubmissionRejected` | The composed 131 draft, unchanged | Composed with `invoiceDecided=false`. |
| `ApprovedWithAdjustments` | The composed draft, unchanged | An approval with no adjustment is `NotApplicable`: no email. |
| `ReturnedToFirm`, `InvoiceRejected` | Only when the decision request carried the confirmed notice | Without it: feed and status only, an ids-only info line. |
| `InvoiceAccepted` | None to the firm | Approvers get an in-app notification (below). |

- **Edited prose is screened.** If `FirmNoticeProse.Check` refuses it, the decision still stands, no email is sent, the stable reason code is logged (never the text), and the template is NOT substituted.
- **Approvers.** Each eligible approver with a linked user gets one `IActionSeam.CreateNotificationAsync` (category `invoice-review`, regarding the invoice, link to the Invoice Review page). No new `NotificationKind`, no spine outbox kind. The seam's own unread-duplicate skip applies. A failure for one approver does not stop the rest. A person with no user, and a person who is not eligible, get nothing.
- **Missing billing contact.** The submitter alone is told, with an ids-only warning. Nobody with an address: no email, a warning, no marker.
- **Kill switch.** `EbillingOptions.Enabled` is read per job through `IOptionsMonitor` (ADR-018). Off: the job succeeds and does nothing.
- **Rejected submission still `Received` or `Processing`.** The sink runs just before the status is written, so the handler waits briefly (5 x 2 s) and then retries; at the final delivery it is poisoned.

### At-most-once marker (written BEFORE the send)

`IIdempotencyService` is a non-atomic cache that fails open. The marker `ebilling-notified:{event}:{id:N}` is written and then read back. If it cannot be read back, nothing is sent and the job is retried. If a send faults after the marker is written, the job is poisoned (an Error with ids and the exception type) and is NOT sent again. The trade-off is deliberate: a crash between the marker and the send loses one email (an operator sees it in the dead-letter queue); the other order could send the firm two. A redelivery finds the marker and does nothing. The approver path writes its marker before notifying in the same way.

### Stored sent notice (EB-D-259, for task 130g)

The existing column `sprk_invoicesubmission.sprk_firmnoticetext` (memo, 100000) holds ONE JSON object. No column was added. Nothing else in the code base read the column before 132.

```
{ "v": 1, "source": "Reviewer" | "Action" | "Template", "kind": "ReturnedToFirm" | "InvoiceRejected" | "SubmissionRejected" | "ApprovedWithAdjustments",
  "subject": "<as sent>", "prose": "<the screened prose, plain text, no markup>", "html": "<the body as sent>" | null, "htmlOmitted": false | true }
```

`prose` is what the reviewer confirmed (for `source` = `Reviewer`) and is always kept; `html` is omitted (`htmlOmitted`: true) only when it would not fit the column. 130g should read `prose` (and `source`, to show it only for `Reviewer` when that is the rule). `subject` carries the code-written tracking suffix. The text is stored on the invoice's originating submission, and on the submission itself for a submission rejection. A store fault is a warning (the email is out).

### The SPA feed source (FR-39)

- **Derived, no table.** Candidates are the caller's firms' `sprk_invoice` rows with status Rejected, Returned to Firm or Approved, changed inside the feed window, newest first, at most 25 (the cut is flagged as `truncated`).
- **Approved** is an item only when 130's projection says `ApprovedWithAdjustments`.
- **Re-checked per invoice, fail closed.** Each candidate is decided by `FirmOutcomeAuthorizationFilter.DecideInvoiceForFeedAsync`, an additive wrapper over the outcome route's own invoice rule (firm membership, Read on the core record, secure roots, the (a') hiding decision). Only Visible is an item. Denied is dropped. A decision that faults or is unavailable drops the item and flags the feed `truncated`. A caller with no firm, a workforce systemuser, an empty contact or an unreadable contact gets nothing.
- **Contract unchanged.** An item is the existing `message` kind, record type `invoices` (the existing segment for `sprk_invoice`), the fixed sender label `Billing`, titled by the invoice's name. A workforce ceiling that cannot Read `sprk_invoice` reads none. An invoice that no longer exists is dropped by the existing title step.
- **Why not STOP:** the source needed no change to the SPA contract, and the approver notification needed no spine outbox kind.

### EB-D-207 pin

`EbillingFeedSource.RenderedInvoiceColumns` (`sprk_invoiceid`, `statuscode`, `modifiedon`) is the exact column set of the candidate query, so the pin cannot drift from what is read. `EbillingFeedSourceTests` checks every column is in the external invoice module's `ReadableColumns`, with one named exception, the timestamp-only `modifiedon` (not a grid column; task 157 left it out of ReadableColumns). A control case shows the rule catches an added column. The firm email renders no `sprk_invoice` column of its own: tracking number, totals and findings come from 130's projection (submission and lines).

## Deviations and known limits

| # | Item |
|---|---|
| D1 | `ExternalInvoiceVisibilityGuardTests.NotInvoiceSurfaces` has one new entry (`EbillingFeedSource` constructor) with its reason: it builds the candidate read, which returns nothing until the per-invoice rule clears it. |
| D2 | `modifiedon` is rendered as the item's time but is not in ReadableColumns; named as the one pinned exception (above). |
| K1 | Feed items are invoice-only: a rejected submission that created no invoice has no route to open, so it has no feed item (its email still goes). |
| K2 | The item's time is the invoice's `modifiedon`, so a later unrelated edit re-surfaces the item inside the 30-day window. |
| K3 | An Approved-with-adjustment item disappears once the invoice leaves Approved (for example Sent to AP). |
| K4 | The feed is firm-wide by the caller's firms (any contact of the firm sees the firm's items), not billing-contact only; at most 25 candidates per read. |
| K5 | The idempotency cache is non-atomic and fails open; the read-back mitigates it, it cannot remove it. |
| K6 | A send fault after the marker is not retried (Poisoned and logged). The approver marker is written first as well, so a total failure to notify is not retried. |
| K7 | `SubmissionRejected` is composed with `invoiceDecided=false`, so its email has no citation and no Signal reason. |
| K8 | A `Failure` outcome is an immediate abandon and redelivery (max 5 deliveries); the in-handler wait for a `Processing` submission reduces that. |

## Owner questions

1. **Distinct feed kind.** eBilling items reuse the SPA's `message` kind with sender `Billing`. A distinct kind (for 134 / spa-r4) needs an SPA change. Keep as is until 134 decides.
2. **Return without a notice.** Task 105's Return request carries no notice yet, so a Return sends no email until 132ui supplies one (same for Reject). Accepted under EB-D-258; confirm 132ui covers both routes.
3. **Feed audience.** Firm-wide (K4), or the billing contact and the submitter only?

## Gates

Filled in at the final commit (see the task report): build, unit, ArchTests, publish delta, vulnerable-package check. Fail-before: three mutations of the production code, run against the new tests, all failed (see the report).
