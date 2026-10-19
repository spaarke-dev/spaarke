# Task 049 part A - foundation (notes)

> 2026-10-19. Rigor FULL, Sonnet @ high. Base `bafa0cb9e` (`work/ebilling-r1`). Scope: the five foundation items of the 049 map
> (`049-submission-processing-job.md` section 8, G1 / G2) that the processor (part B) builds on. The processor, the handler,
> `SubmissionReasonCodes.cs` and the FinanceBaseValues swap are not here. No script was run against any environment.

## 1. What was built

| # | Item | Where |
|---|---|---|
| G1 | `IGenericEntityService.ExecuteTransactionAsync(IReadOnlyList<OrganizationRequest>, ct)` | `Spaarke.Dataverse/IGenericEntityService.cs`, `DataverseServiceClientImpl.cs`, new `DataverseTransactionException.cs` |
| G2 / D-101 | `IInvoiceLifecycleService.BuildCreateRequest(Entity, InvoiceLifecycleStatus)` | `Services/Finance/InvoiceLifecycleService.cs` |
| Sink | `IEbillingNotificationSink` + `NoOpEbillingNotificationSink`, registered in `FinanceModule` | `Services/Finance/Submission/IEbillingNotificationSink.cs` |
| D-121 | widen-only script for `sprk_billingevent.sprk_amount`; totals accept credit lines | `scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1`, `InvoiceTotalsCalculator.cs` |
| D-124 | `sprk_invoice.sprk_trackingnumber` (Text 100) script, doc, constants | `scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1`, `InvoiceReviewService.InvTrackingNumber` |

## 2. G1 - the transaction seam

- `BuildTransaction` (pure, public static, as `BuildBulkUpdateTransaction`) makes ONE `ExecuteTransactionRequest`, `ReturnResponses = false`.
  It throws `ArgumentException` before any I/O for a null or empty list, a null entry, or an `ExecuteMultiple` / `ExecuteTransaction` entry.
- A failure is `DataverseTransactionException : InvalidOperationException`:
  - `FailedRequestIndex` (int?) is the zero-based index in the submitted list. It is null when Dataverse names no in-range request.
  - `OutcomeUnknown` is true when the exception chain holds no Dataverse fault (a timeout can land after the commit). **The processor must
    treat that as "re-read the submission's `sprk_invoice` link", never as "nothing landed"** (the 049 resume marker already works this way).
  - The message names request kind, table and id. It adds no field value of its own; the appended fault text is Dataverse's.
- The interface default body throws `NotSupportedException` (the `BulkUpdateIfRowVersionsMatchAsync` precedent). No existing fake needed a
  change (about a dozen implement `IGenericEntityService`), and a Moq double must `Setup` the method or it fails loudly.
- Documented bound: keep a call to 1,000 requests (the bound `BulkUpdateAsync` uses). Dataverse states none for transactions. An invoice
  with many hundreds of lines needs the processor to check this (open item for part B: one invoice = 1 + lines + approval + 2 requests).
- Cancellation propagates unwrapped, as in `BulkUpdateAsync`.

**§11.**
- Existing: `BulkUpdateAsync` and `BulkUpdateIfRowVersionsMatchAsync` (single-table updates, 1:1 with `BuildBulkUpdateTransaction`).
- Extension: widening those signatures changes every caller and still cannot create. A second builder next to the first is the smallest addition.
- Cost of doing nothing: FR-20's all-or-nothing needs a compensation path with a real orphan window (an invoice without lines).

**§10 placement.** BFF shared library (`Spaarke.Dataverse`, consumed by the BFF): a synchronous write primitive used by an ADR-004 job
handler that already lives in the BFF; no Function / Container Apps Job signal applies (ADR-052). No endpoint, no package, no AI call. ADRs:
ADR-002 (one write owner per invariant), ADR-004 (the caller), ADR-010 (no new registration), ADR-038 (tests). Publish-size delta: not
measured here by instruction (the main session measures once for all of 049); no package reference changed.

## 3. G2 - the create shape (D-101)

`BuildCreateRequest(Entity invoice, InvoiceLifecycleStatus initialStatus)` returns `InvoiceCreateRequestResult { Succeeded, Request, Reason }`.
- **Why it returns a request and does not execute.** 049 puts the invoice create, the lines, the approval portion and the document link in
  one `ExecuteTransactionAsync`. An `Task CreateAsync(...)` on the lifecycle service would write outside that unit. The status stays owned by
  the lifecycle service because it is the only code that adds `statecode` + `statuscode` (the paired values, from `InvoiceStatusCodes`).
- It is pure (no I/O), so it takes no token and has no failure mode beyond refusal.
- Only `InReview` (state 0, status 1) and `Rejected` (state 1, status 659490003). Every other status, an undefined enum value and a row that
  already carries `statecode` / `statuscode` (any casing) is refused with a stable reason (`invoice.initial_status_not_allowed`,
  `invoice.status_supplied_by_caller`) and no request, so a refusal cannot be sent by mistake. A row of another table throws
  `ArgumentException` (a programming error).
- The caller's row is copied, never mutated. The caller keeps its client-chosen id.
- **Unverified live (K-class, first exercise is task 079):** creating directly with `statecode = 1` (Rejected). Dataverse allows a non-default
  state on create for most custom tables, but the repo has no precedent and nothing here was run. If it is refused, change this one method
  to emit Create (In Review) followed by a state change in the same transaction; callers keep the same list slot only if they take the
  request list. **Owner/main decision if wanted now:** have the method return `IReadOnlyList<OrganizationRequest>` (a list of one today)
  so that contingency is not a signature change. I kept the single `CreateRequest` (YAGNI) and name the cost here.

**§11.** Existing: `TransitionAsync` moves an existing row and cannot create. Extension: this IS the extension of the 014 owner (no new
service); a status writer elsewhere would break ADR-002's one-owner rule (D-101: "do not write statuscode elsewhere"). Cost of doing nothing:
049 would write `statuscode` itself for the Rejected-at-create case.

## 4. Notification sink

- `SubmissionDecidedAsync(SubmissionDecidedNotification, ct)`, the name from the 049 map. The record carries `SubmissionId`, `Outcome`
  (Accepted / Rejected), `Channel`, `InvoiceId?` (null when the document was never read, D-101) and `ReasonCode?`. Identifiers and a stable
  code only (ADR-015); 132 reads recipients and adjustments from Dataverse by these ids and enqueues its job.
- Contract in the XML doc: idempotent per (submission, outcome); a failure must not undo the decision (the caller logs and does not retry the
  pipeline for it).
- Registered `AddScoped`, unconditional (the processor always calls it; the flag rule). The no-op logs id and outcome at Debug.
- ADR-010 1:1 ceiling in `ADR010_DITests.cs` raised **166 -> 167** with the seam justification (132 is the second implementation). **Merge
  note:** part B / C may add their own interface; sum the raises.

**§11.** Existing: `OutboxService` / `CommunicationService` / `IActionSeam` (132's delivery paths; nothing named `EbillingNotification*`).
Extension: they need recipients, templates and channel logic that are 132's design; adding any of it here is scope creep. Cost of doing
nothing: the processor's step 9 has no call target, so either 049 hard-codes notifications or omits the after-step and 132 retrofits a tested
pipeline.

## 5. D-121 - credit lines

- **Script.** `Deploy-EbillingBillingEventAmountRange.ps1`: reads `sprk_billingevent.sprk_amount` (Money; escalates exit 2 if another type),
  sets `MinValue` to -922337203685477 only when the current minimum is higher, confirms the maximum did not move, publishes `sprk_billingevent`
  only when it wrote. A re-run reports "skipped". Style copied from `Deploy-EbillingPendingSpendSchema.ps1`; parse-checked, **not run**.
  **Dev ordering dependency:** run it before the first live 049 run, or a credit line's create (and a negative Adjustment-line amount from
  `RecalculateAsync`) faults at the platform minimum.
- **Totals (016) did NOT sum credit lines.** `InvoiceTotalsCalculator` refused any Fee or Expense line below 0
  (`invoice-totals.line-amount-negative`, "the platform minimum for the totals is 0"), so a credit-line invoice would have been committed and
  then left with no totals. D-121 says totals sum signed values, so this was a defect against the decision, fixed here:
  - the per-line refusal is gone; amounts are summed signed (percent adjustments are taken on the fees net of credits);
  - new refusal `invoice-totals.total-below-storable-minimum` when fees, expenses, line adjustments or invoice adjustments would be below 0
    (the platform minimum of 0 on those columns). It is a clean refusal (nothing written, the 049 map's step 6c records it) instead of a
    Dataverse fault after the commit;
  - a Reduced credit stays refused (`line-approved-amount-invalid`: no approved amount is valid for it); a Disallowed credit is summed
    (the credit is not honoured) and is refused only when nothing offsets it.
  - Tests: calculator (7 cases) and service (2 cases) with a credit line. The old `NegativeFeeOrExpenseAmount_IsRefused` was replaced.
- **Could `sprk_invoice.sprk_totalamount` (min 0) go negative? Yes.** An all-credit invoice (a pure reversal) has a negative billed total.
  The invoice create inside the 049 transaction would then fail the platform minimum (and `sprk_feestotal` / `sprk_expensestotal` /
  `sprk_lineadjustmentstotal` / `sprk_invoiceadjustmentstotal` / `sprk_approvedtotal` have the same minimum, which is why the calculator
  refuses rather than writes). **Reported, not changed.** Options for the owner: (a) the processor refuses such a submission before the
  transaction with a stable code (suggested `ebilling.submission.credit_invoice_unsupported`), or (b) widen `sprk_totalamount` (and the five
  totals) with the same widen-only script. A mixed invoice (net total >= 0) is fine, unless a credit exceeds the charges of its own cost type
  (e.g. an expense credit on an invoice with no expense charge), which the calculator now refuses cleanly.

## 6. D-124 - tracking number column

- **Name and length.** `sprk_invoice.sprk_trackingnumber`, Text, **100**, optional, no key. No conflict: grep and the data-model docs show no
  such column on `sprk_invoice`; the submission has the same logical name (by design, so 049 copies one to the other).
- **Length rationale.** The allocator caps the firm invoice number at 89 characters and the format is `{number}.{n}` with n up to 10 digits:
  89 + 1 + 10 = 100. A test pins `InvTrackingNumberMaxLength` against that and the logical name against the allocator's column constant.
- **Constants.** `InvoiceReviewService.InvTrackingNumber` and `InvTrackingNumberMaxLength`, in the `Inv*` invoice-column block.
- **Script.** `Deploy-EbillingInvoiceTrackingNumberSchema.ps1`: `Add-Column` (check-then-create), stops with exit 2 if an existing column is
  shorter than 100 (it never alters an existing column), publishes `sprk_invoice` only when it wrote. Parse-checked, **not run**. It adds no
  form field (the invoice form's eBilling section belongs to `Deploy-EbillingFormsViews.ps1`; a follow-up for 053 / the review UI).
- Doc: `docs/data-model/sprk_invoice.md` (the column, the create path, the totals refusal). The generated stub
  `sprk_financial-related-entities.md` has no R1 invoice columns, so it only got the `sprk_amount` minimum change.

**§11.** Existing: `sprk_name` (primary name, 850) and `sprk_invoicenumber` (the firm's own number; D-124 keeps it so); the submission's
`sprk_trackingnumber`. Extension: not possible: the primary name is display text and the firm number must stay the firm's. Cost of doing
nothing: the invoice (Signals, Duplicate findings, the SPA and the Decision Record) can show the tracking number only through a join to the
submission (owner decision D-124).

## 7. Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "...Finance|Billing|Lifecycle|Totals|Dataverse|Submission"`: 4343 passed, 0 failed, 8 skipped
  (the skipped are the existing live-environment tests). `tests/Spaarke.ArchTests`: 1209 passed. No `Spaarke.Dataverse` test project exists
  (its tests live in `tests/unit/domain/Dataverse`, in the filtered run).
- **Fail before.** With the behaviour mutated back (initial status allowed for any value, the per-line negative refusal restored, no nested-batch
  check, no failing index) 17 of the new tests failed; restored, all pass. New surface (the transaction, the sink, the column constants) has no
  "before" to run, so its tests prove the contract.

## 8. Self-review (one adversarial pass) and what it changed

- Fixed: the status-column guard in `BuildCreateRequest` was case-sensitive; Dataverse is not, so a `StatusCode` key would have slipped past
  and written a second, uncontrolled status. Now case-insensitive, with tests.
- Fixed: the exception doc claimed the message never carries a value; it appends Dataverse's own fault text. Wording corrected.
- Known limits (K-class, reported): create-in-Rejected live (section 3); the transaction request-count bound (section 2); the D-121 script
  must run on dev before a live credit line; Disallowed on a credit line is summed (a product call: is "disallow a credit" legal for a reviewer?).
- Not changed, noticed: `scripts/README.md` does not list `Deploy-EbillingPendingSpendSchema.ps1` either; neither new script is listed (053).
