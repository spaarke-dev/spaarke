# Task 132: Notifications (server half): firm email, SPA feed, approver in-app

Branch `task/ebilling-132`, from `08134f4483`. Rigor STANDARD. Model: Sonnet @ high. FR-39, FR-50, NFR-10. Amended by EB-D-254 (no PDF, no task 133), EB-D-258 (server half only; 132ui fills the wizard notify step), EB-D-259 (the confirmed prose stays recoverable), uac-r2 N2 (prose is rendered only through `RenderHtml`; a reviewer subject is header-safe).

No Dataverse write, no push, no deploy was made. No App Service setting is added (task 136: none).

## What was built

| Piece | File | What it does |
|---|---|---|
| Enqueuer | `Services/Finance/EbillingNotificationEnqueuer.cs` | One `IJobHandler` job per (event, subject id). Never throws (15 s bound): the decision has already happened. Job id and idempotency key `ebilling-notify:{event}:{id:N}`. A Return or Reject carries the confirmed notice only when both parts are present, within caps (subject 280, prose 1500). |
| Sink | `Services/Finance/Submission/EbillingNotificationSink.cs` | Replaces the logging-only sink: Accepted enqueues `InvoiceAccepted`, Rejected enqueues `SubmissionRejected` (a `Blocked` rejection is held and not told), Returned enqueues `ReturnedToFirm` (no notice, so no email). |
| Recipients | `Services/Finance/EbillingNotificationRecipients.cs` | Reads the subject's facts, the firm name, and resolves the firm addresses: the submitter plus the firm's billing contact, kept only when the contact is Active AND holds an active membership in the notice's own firm now (`ExternalInvoiceSubmissionAuthorizationFilter.ReadContactFirmsAsync`, the one definition of "a contact's firms"; a missing `statecode` is not active), well-formed address, de-duplicated. Logs ids only. |
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
- **A degraded or unavailable outcome (130f).** Once 130f lands, a fault reading the lines of a Rejected or Returned invoice gives a Degraded status-only outcome, and the composer answers `Unavailable`. The handler composes BEFORE it writes the marker, so such a job sends nothing (never an empty or partial findings table), writes no marker, logs ids only, and returns a retryable `Failure` (`Poisoned` at the final delivery, so it is not dropped silently). A later delivery that reads fully sends once. The case is covered for the composed path (approved with adjustment) and the confirmed-notice path (Return, Reject); it does not depend on 130f being in the base, because the composer already maps `Degraded` and the projection's `Unavailable` kind to `Unavailable`.
- **Approvers.** Each eligible approver with a linked user gets one `IActionSeam.CreateNotificationAsync` (category `invoice-review`, regarding the invoice, link to the Invoice Review page). No new `NotificationKind`, no spine outbox kind. The seam's own unread-duplicate skip applies. A failure for one approver does not stop the rest. A person with no user, and a person who is not eligible, get nothing.
- **Missing billing contact.** The submitter alone is told, with an ids-only warning. Nobody with an address: no email, a warning, no marker.
- **Recipients are active members of the notice's firm (review F1).** A submitter who has left the firm, a billing contact who belongs to another firm, a deactivated contact and a contact whose `statecode` was not read are not emailed. A membership that cannot be read throws, so the job retries (never "not a member", never a pass). If nobody is left, the "no recipient" path applies: ids-only warning, no marker, no email.
- **A refused confirmed notice is a 400 before the decision (review F1).** The Reject and Return routes run `FirmNoticeProse.ScreenConfirmed` (the job's own code: the caps, the subject cleaned, the tracking suffix removed, then `Check`) BEFORE deciding. A refusal answers 400 `ebilling.notice_refused` with the reason code (never the text); nothing is decided, recorded or queued, and the reviewer can edit and decide again. Neither part given means no notice (feed and status only). A half notice (only the subject or only the prose) is screened, and refused when it fails; it no longer silently becomes "no notice". The route reads the invoice's tracking number to strip the code-written suffix exactly; if that read fails it strips a trailing parenthetical group leniently. The screen runs after the route's authorization filter but before the service's eligibility check, so a caller with Write who is not an eligible approver and sends refusable prose gets 400 where it used to get 403: the 400 names only a reason code for the caller's own input.
- **Kill switch.** `EbillingOptions.Enabled` is read per job through `IOptionsMonitor` (ADR-018). Off: the job succeeds and does nothing. The SPA feed source reads it too (review F1): off, `EbillingFeedSource.ReadAsync` returns no items and reads nothing.
- **Rejected submission still `Received` or `Processing`.** The sink runs just before the status is written, so the handler waits briefly (5 x 2 s) and then retries; at the final delivery it is poisoned.

### At-most-once marker (written BEFORE the send)

`IIdempotencyService` is a non-atomic cache that fails open. The marker `ebilling-notified:{event}:{id:N}` is written and then read back. If it cannot be read back, nothing is sent and the job is retried. If a send faults after the marker is written, what happens depends on what is KNOWN about the fault (review F1):

- **Provably not sent** (a send path refusal before the channel: validation, sender, daily limit, record preparation; or Graph's own settled refusal, `GRAPH_SEND_FAILED` with `sendOutcome` = `NotSent`): the marker is removed (`IIdempotencyService.ClearEventProcessedAsync`, read back as gone) and the job returns a retryable `Failure` (`Poisoned` at the final delivery). The later delivery sends once. If the marker cannot be shown gone, the job is `Poisoned` instead, because a silent drop is worse than an alert.
- **Ambiguous** (a 5xx, a 408, a transport fault, any other exception, or a `GRAPH_SEND_FAILED` with `sendOutcome` = `Unknown`): the message may have been accepted, so the job is poisoned (an Error with ids and the exception type), the marker stays and the notice is NOT sent again.

The trade-off for the ambiguous case is deliberate: a crash between the marker and the send loses one email (an operator sees it in the dead-letter queue); the other order could send the firm two. A redelivery finds the marker and does nothing. The approver path writes its marker before notifying in the same way.

### Stored sent notice (EB-D-259, for task 130g)

The existing column `sprk_invoicesubmission.sprk_firmnoticetext` (memo, 100000) holds ONE JSON object. No column was added. Nothing else in the code base read the column before 132.

```
{ "v": 1, "source": "Reviewer" | "Action" | "Template", "kind": "ReturnedToFirm" | "InvoiceRejected" | "SubmissionRejected" | "ApprovedWithAdjustments",
  "subject": "<as sent>", "prose": "<the screened prose, plain text, no markup>", "html": "<the body as sent>" | null, "htmlOmitted": false | true }
```

`prose` is what the reviewer confirmed (for `source` = `Reviewer`) and is always kept; `html` is omitted (`htmlOmitted`: true) only when it would not fit the column. 130g should read `prose` (and `source`, to show it only for `Reviewer` when that is the rule). `subject` carries the code-written tracking suffix. The text is stored on the invoice's originating submission, and on the submission itself for a submission rejection. A store fault is a warning (the email is out).

**The column is field-secured (review F2).** The stored JSON holds firm-bound prose that a reviewer, not a model, may have typed, and a submitter may read their own submission row through the external routes, so the column is `IsSecured` = 1 and the "Spaarke BFF-Managed Field Writers" profile carries it (read, update, create), the same shape as `sprk_findingsjson` (ADR-028). The BFF writes it app-only through `IGenericEntityService`, so the write path is unchanged; the column is removed from the main form (and from the form deploy script) so no workforce user sees a field they cannot read. There is no reader profile, as for `sprk_findingsjson`: nothing but the BFF reads it. `FirmNoticeTextFieldSecuredGuardTests` (copied from `FindingsJsonFieldSecuredGuardTests`, with must-fire and must-not-fire controls) pins that the column the handler writes is secured in `Entity.xml` and granted in the profile. `docs/data-model/sprk_invoicesubmission.md` is updated.

**Obligation for task 130g (read this before showing the sent notice).** Whatever reads `sprk_firmnoticetext` must re-screen `prose` with `FirmNoticeProse` (`Check`) before showing it to a firm, and must NEVER render the stored `html`: `html` is a record of what was sent, not content that is safe to render in a firm-facing page. Show `prose` as plain text.

**The dev apply is NOT done and needs the owner's typed OK.** The new script `scripts/ebilling/Set-EbillingFirmNoticeTextFieldSecurity.ps1` (idempotent, copied from the findings-json script; `-WhatIf`, `-Verify`, `-Revert`) turns `IsSecured` on and grants the writers profile in the live environment. It was NOT run. A solution import of the changed `Entity.xml` and `FieldSecurityProfiles.xml` does the same on import; either way the live change waits for the owner's typed OK (CLAUDE.md §9: security-sensitive, and a Dataverse write).

### The SPA feed source (FR-39)

- **Derived, no table.** Candidates are `sprk_invoice` rows of the caller's firms with status Rejected, Returned to Firm or Approved, changed inside the feed window, newest first.
- **Audience (review F4, NFR-10, ADR-028).** Only the invoice's SUBMITTER (`sprk_submittedby` = the caller's contact) and the firm's BILLING CONTACT (`sprk_billingcontact` = the caller's contact, read per firm) get an item. The candidate QUERY is narrowed to that (`sprk_submittedby` = contact OR `sprk_vendororg` in the caller's billing firms), and every row it returns is checked again in memory (`InAudience`), so a query that came back too wide still shows nothing to another associate of the same firm. An invoice with no recorded submitter reaches the billing contact only. A billing-firm read that faults leaves the caller's own submissions and flags `truncated`. Every item is still decided by the invoice rule (below); the audience only narrows who is asked.
- **Approved** is an item only when 130's projection says `ApprovedWithAdjustments`; the projection is read only for a VISIBLE Approved invoice.
- **Re-checked, fail closed, in one batch (review F3).** The candidates are decided TOGETHER by `FirmOutcomeAuthorizationFilter.DecideInvoicesForFeedAsync`, over the outcome route's own invoice rule (firm membership, Read on the core record, secure roots, the (a') hiding decision). The route is the same code over a batch of one, so the rule is not copied. Only Visible is an item. Denied is dropped. A decision that faults or is unavailable drops the items and flags the feed `truncated`. A caller with no firm, a workforce systemuser, an empty contact or an unreadable contact gets nothing.
- **The cap is applied AFTER the decision (review F3).** At most 100 candidates are read (newest first); at most 25 VISIBLE items are returned. `truncated` is raised only by something the caller could open (an item past the 25th, an undecidable invoice) or by the read bound, never by an invoice hidden from the caller. Before, the cap cut the 26 newest candidates before the decision, so invoices the caller could not see used up the cap.
- **Call counts per feed read (review F3).** Before: per candidate (up to 26 read, 25 decided), 1 invoice-lookup read, 0 to 1 live-parent reads, 0 to 1 secure-flag reads and 1 hiding decision (itself several reads): roughly 4 to 8 Dataverse calls each, about 100 to 200 per poll. After: a fixed 1 firm read, 1 billing-firm read, 1 candidate query (the scope columns ride on it, so no per-invoice lookup), 1 live-parent read, 1 secure-flag read and 1 hiding call for the whole batch, plus one projection read per VISIBLE Approved invoice only. `FirmOutcomeScopeTests.FeedDecisions_MixedBatch_AreEachRight_WithOneHidingCall` proves one hiding call and at most one flag read for six mixed invoices.
- **Kill switch (review F1).** `EbillingOptions.Enabled` is checked at the top of `ReadAsync`; off, no items and no reads.
- **Contract unchanged.** An item is the existing `message` kind, record type `invoices` (the existing segment for `sprk_invoice`), the fixed sender label `Billing`, titled by the invoice's name. A workforce ceiling that cannot Read `sprk_invoice` reads none. An invoice that no longer exists is dropped by the existing title step.
- **Titles (review F1).** `ExternalDataService.GetRecordNamesAsync` did not know `sprk_invoice` (production returned an incomplete read, so every eBilling item was untitled and the feed said `truncated`; the test double had hidden it by naming any entity). It now knows `sprk_invoice` (`sprk_invoices`, name column `sprk_name`, `ExternalDataService.InvoiceNameColumn`); the contract-suite double answers `Complete: false` for any entity the production read has no binding for; `EbillingItem_HasTheExistingMessageShape` asserts the title and `Truncated == false`. A deleted invoice still drops through the title step (`EbillingItem_ForAnInvoiceThatIsGone_IsDropped`).
- **Why not STOP:** the source needed no change to the SPA contract, and the approver notification needed no spine outbox kind.

### EB-D-207 pin

`EbillingFeedSource.RenderedInvoiceColumns` (`sprk_invoiceid`, `statuscode`, `modifiedon`, and `sprk_name`, the item's title, review F1) is what the feed renders from `sprk_invoice`. The candidate query reads those (the name aside: the title step reads it), the two audience columns (`sprk_submittedby`, `sprk_vendororg`) and the access rule's own scope columns (`FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames`); a test pins the query's columns to exactly that union. `EbillingFeedSourceTests` checks every rendered column is in the external invoice module's `ReadableColumns`, with one named exception, the timestamp-only `modifiedon` (not a grid column; task 157 left it out of ReadableColumns). A control case shows the rule catches an added column. The audience and scope columns are read only to decide and are never returned. The firm email renders no `sprk_invoice` column of its own: tracking number, totals and findings come from 130's projection (submission and lines).

## Deviations and known limits

| # | Item |
|---|---|
| D1 | `ExternalInvoiceVisibilityGuardTests.NotInvoiceSurfaces` has one new entry (`EbillingFeedSource.CandidateQuery`) with its reason: it builds the candidate read, which returns nothing until the audience re-check and the batch invoice rule clear it. |
| D3 | **Enqueue happens BEFORE the submission's terminal status is written** (the sink runs just before the status write), so the handler waits (5 x 2 s) for a final status and retries. This deviates from the POML wording ("after the terminal status"); it is how the pipeline orders the two writes, and the wait covers it. |
| D4 | **The send path does not pass the notice kind to `RenderHtml`.** On this base `RenderHtml(outcome, prose)` has no kind parameter. On `work/ebilling-r1` after 130f it is `RenderHtml(FirmOutcome, string, FirmNoticeKind? kind = null)` and hides the Approved column for a Return or Reject. MERGE OBLIGATION: when this branch meets 130f, `FirmNoticeComposer.ComposeConfirmedForInvoiceAsync` (and the composed path) must pass `kind` to `RenderHtml`, or a Return or Reject notice will show the approved-amount column 130f removed. Not changeable here: the parameter does not exist on this base. |
| D2 | `modifiedon` is rendered as the item's time but is not in ReadableColumns; named as the one pinned exception (above). |
| K1 | Feed items are invoice-only: a rejected submission that created no invoice has no route to open, so it has no feed item (its email still goes). |
| K2 | The item's time is the invoice's `modifiedon`, so a later unrelated edit re-surfaces the item inside the 30-day window. |
| K3 | An Approved-with-adjustment item disappears once the invoice leaves Approved (for example Sent to AP). |
| K4 | The feed audience is the invoice's submitter and the firm's billing contact (review F4), each re-checked per invoice; another associate of the same firm gets no item. At most 100 candidates are read and 25 visible items returned per read; reaching the read bound sets `truncated`. |
| K5 | **Concurrent duplicates.** `IdempotencyService` has no atomic set-if-absent, so two deliveries that run at the SAME moment can both pass the marker check and both send. The read-back mitigates a lost write, not a race; the Service Bus lock makes a concurrent pair rare. An atomic primitive on `IIdempotencyService` would remove it. Not built here. |
| K6 | An AMBIGUOUS send fault after the marker is not retried (Poisoned and logged); a provably not-sent one is (marker cleared, retried; see the marker section). **The approver marker is written FIRST** (before the in-app notifications), so a total failure to notify approvers is not retried. |
| K9 | **The message kind text stays `message`** (the owner kept it; a distinct kind is owner question 1). |
| K10 | **Re-surfacing on `modifiedon`**: see K2; any later change to the invoice inside the window re-surfaces the item (no event table records the outcome time). |
| K11 | **The confirmed prose is not tied to the 131 draft.** The route and the job screen what the request carries; nothing proves it is the text the 131 draft route returned (a reviewer may edit it, and a client may send any text that passes the screen). The screen, not the draft, is the control (EB-D-258). |
| K7 | `SubmissionRejected` is composed with `invoiceDecided=false`, so its email has no citation and no Signal reason. |
| K8 | A `Failure` outcome is an immediate abandon and redelivery (max 5 deliveries); the in-handler wait for a `Processing` submission reduces that. |

## Owner questions

1. **Distinct feed kind.** eBilling items reuse the SPA's `message` kind with sender `Billing`. A distinct kind (for 134 / spa-r4) needs an SPA change. Keep as is until 134 decides.
2. **Return without a notice.** Task 105's Return request carries no notice yet, so a Return sends no email until 132ui supplies one (same for Reject). Accepted under EB-D-258; confirm 132ui covers both routes.
3. **Feed audience.** Resolved in review (F4): the submitter and the billing contact only, never firm-wide (K4).

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | 0 warnings, 0 errors |
| Filtered unit tests (EbillingNotification, FirmNotice, InvoiceRejectReturn, InvoiceApproval, ExternalNotificationFeed, InvoiceSubmission, Communication, plus EbillingFeedSource and FirmOutcome) | 5358 passed, 0 failed, 2 skipped (before this task's last test, 33 in the handler seam suite now pass) |
| Filtered ArchTests (Route, Finance, Notification, Job, External, FirmNotice, FirmFindings) | 615 passed, 0 failed |
| Vulnerable packages (`dotnet list package --vulnerable --include-transitive`) | none; no package added |
| Publish size (compressed, Release, fresh `origin/master` build at `08161cfb0e` vs this branch at `c8f67d6b92`) | 40.44 MB vs 40.47 MB, delta +0.03 MB (no threshold reached) |

**Review fix round (this commit series).** `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. The filtered unit tests (EbillingNotification, EbillingFeedSource, ExternalNotificationFeed, FirmOutcomeScope, InvoiceRejectReturn, InvoiceApprovalEndpoints, FirmNotice, ExternalDataService): 742 passed, 0 failed. The filtered ArchTests (ExternalInvoiceVisibilityGuard, FirmFindingsProjectionCallerBoundary, FirmNoticeComposerCallerBoundary, FieldSecured, ScriptMetadataStrongConsistency, RouteAuthorization): 127 passed, 0 failed. Fail-before of the review's must-fire tests (production line commented out, the tests run, then restored): without the invoice name binding 1 failed (`GetRecordNames_ReadsTheRightEntitySet...("sprk_invoice")`); without the audience narrowing (query filter and `InAudience`) 4 failed; without the route's notice screen 5 failed.

Fail-before (the production code mutated, the new tests run, then restored): removing the Return/Reject confirmed-notice guard, skipping the marker check, and skipping the feed's per-invoice decision failed 6 tests together (two guard cases, redelivery, send-fault no-resend, and two feed denial cases). Turning the composer's `Unavailable` into a success failed the three degraded-outcome cases.
