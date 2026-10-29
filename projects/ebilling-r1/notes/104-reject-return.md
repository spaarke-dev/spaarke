# Task 104: Reject and Return to Firm; resubmission linkage

Branch `task/ebill-104-reject-return`, from `work/ebilling-r1` at `842110e6ab`. Rigor FULL (bff-api, authorization). Model: Sonnet @ high.

## What was built

| Component | File | What it does |
|---|---|---|
| `InvoiceRejectReturnService` | `Services/Finance/InvoiceRejectReturnService.cs` | One service for two verbs (`RejectAsync`, `ReturnAsync`). Reason, locks, eligibility, held check, Signals, filing check, claim, 014 transition, portion, 074 record, link. |
| Endpoints | `Api/Finance/InvoiceRejectReturnEndpoints.cs` | `POST /api/finance/invoices/{id}/reject` and `/return` (`ebilling.invoice.decide`). Step 0 on the group, the route filter, then `RequireEbillingEnabled`. |
| Resubmission linkage | `Services/Finance/Submission/InvoiceSubmissionPersister.cs` | `FindSupersededInvoiceAsync`: sets `sprk_supersedesinvoice` on a newly accepted invoice (D-43). |
| DI and routes | `FinanceModule.cs` (1 line), `EndpointMappingExtensions.cs` (1 line) | |
| ArchTests ratchets | `RouteAuthorizationGuardTests.Ledger.cs`, `ExternalDocumentNarrowingGuardTests.cs` | Endpoint file count 146 to 147, a `RouteLevelGate` ledger entry, and two generic-writer entries (the claim and its release write only the portion's decider and time). |

## Behaviour

Both verbs take a body `{ reason }` (required, at most 2,000 chars). The decider is the caller's `systemuserid`; the decision id is derived from the approval portion (`IdOf("ebilling-reject-{id}")` / `"ebilling-return-{id}"`), as 103 does for approve, so every attempt by every approver is one decision.

Order (nothing is written before the claim):

1. Reason check (400 `ebilling.reason_required` / `ebilling.reason_too_long`), before any lock or read.
2. Two locks: the invoice-decision lock (`SubmissionOverrideService.LockKey`) and the line lock (`InvoiceLineDecisionService.LockKeyFor`). Busy gives 409; a lock fault gives 503.
3. Read the invoice and its one portion. Already in the verb's target status goes to the repeat path (below).
4. Only an invoice In Review (409 `ebilling.invoice.not_in_review`); one Pending portion; exactly one core record (`SingleCoreOf`, 194b), all before any claim.
5. Eligibility, live, by the caller's OWN contact via `IIdentityNormalizationService` (EB-D-171). The stored N:N never grants; `contact.sprk_systemuser` is never read. No contact is 403; a failed identity read is 503.
6. K1: a portion claimed by another approver's unfinished decision is 409 `ebilling.approval.claimed`.
7. Held (D-101): a blocked submission holds the invoice, 409 `ebilling.invoice.held`. The way out is 105's Override or Return of the submission.
8. Signals (D-122/EB-D-172): the invoice's and its lines' Open and Acknowledged Signals created at or before the decision time. The close rights are asked as the caller before any write (403 `ebilling.decision.not_authorized`); the set is stored with the record.
9. The recorder's filing read must name that same core record; the record is preflighted.
10. Claim: decider and time on the portion, conditional on the row version as read (409 `ebilling.approval.changed` for the loser).
11. 014 transition to Rejected or Returned to Firm. Only `Transitioned` continues. `Refused` clears the claim and is 409; `AlreadyInTarget` is a no-op; an exception is 503 incomplete.
12. Finish: portion status, then the Decision Record (`reject-invoice` / `return-to-firm`, reason in `Reason` and `Values.reason`), then the Signal closes with the failed count acted on (Incomplete at step `signals`, before the link), then the link. Every step is idempotent; a fault is 503 `ebilling.invoice.decision_incomplete` with `failedStep`.

**Repeat.** The claimant (matched by the caller's own contact) completes what is missing; the record answers AlreadyRecorded, so there is never a second record. Anyone else gets a no-op. Once the record exists the request's reason no longer matters (fix round, F1-a): the repeat finds the stored record through the stored set, closes that set, writes the portion-to-record link and answers Decided. The stored record keeps the first reason (test `RepeatWithAnotherReason_FinishesTheClosesAndTheLink_WithoutASecondRecord`).

**Resubmission linkage (D-43).** On an accepted submission (not AutoRejected), the persister reads the earlier invoices with the same firm (`sprk_vendororg`) and trimmed firm invoice number (ordered by created on then id, excluding the new invoice; no top count). It walks back past AUTOMATIC rejections (F1-b) and takes the first invoice that is not one as the predecessor. It links `sprk_supersedesinvoice` only if that predecessor is Returned to Firm and its single core record equals the new invoice's. A human-Rejected, Approved, In Review or Cancelled predecessor is never linked.

An automatic rejection is told apart by its **approval row**: 049 creates an AutoRejected invoice Rejected with no `sprk_invoiceapproval`, while every other invoice, a human-Rejected one included, keeps its row (read for the Rejected candidates in one `In` query). The approval row was chosen over the submission's status because the submission is Rejected for both an automatic rejection and a Blocked one, and the row is on the invoice side of the same read. A read fault is a Retry (`ebilling.submission.supersedes_read_failed`) with nothing persisted.

## Decisions applied

- **EB-D-169..172.** As above. Reject and return need the same Write on the invoice (`ebilling.invoice.decide`) plus live eligibility.
- **D-43.** Linkage only from Returned to Firm.
- **D-101.** Held invoices are refused here.
- **D-122.** Closed Signals are the invoice's and its lines' (D-122 names 104 for the invoice).

## Deviations and judgement calls (for the reviewer)

1. **Held is refused (409).** The POML does not say; D-101 puts the way out in 105. Easy to change if the operator wants rejection of a held invoice allowed.
2. **Two prior Returned invoices.** I applied the POML's stated default (the most recent by created on) and did not stop: a chain of returns is not ambiguous. Test `TwoReturnedPredecessors_TheMostRecentIsLinked`.
3. **Most recent earlier invoice is not Returned, an older one is.** Not linked when the newer one is a human decision or another status (the literal rule: the firm is working from a later invoice). An automatic rejection in between is walked past (F1-b).
4. **Cross-core predecessor.** No link, a warning logged.
5. **AutoRejected resubmissions are not linked.** They have no approval path and no invoice to review.
6. **The decision id derives from the portion**, not a request id (as in 103).
7. **The reason is stored only in the Decision Record** (no portion column).
8. **Reject and return share one service** (a `Verb` record) instead of two near-copies; the cross-verb concurrency test shows one record across both action codes.

## Seeds (all run, all restored; script in the session scratchpad)

| Guard disabled | Change | Tests red |
|---|---|---|
| Eligibility (EB-D-171) | caller falls back to "anyone is an approver" | 11 (service: not eligible, contact spoof, stored set never grants, nobody eligible, identity/read faults; contract: Write-but-not-eligible) |
| Reason check | blank and empty reasons let through | 6 |
| Held (D-101) | held check never fires | 4 (service and contract) |
| Close authorization | rights never asked | 2 (`NoCloseRights_IsRefused_AndWritesNothing`) |
| Failed-close count (first finish) | count ignored | 4 (`FailedClose_IsIncomplete_AndTheRepeatFinishesIt`) |
| Failed-close count (finish over an existing record) | count ignored | 0 at first, which exposed a gap; test `FailedClose_OverAnExistingRecord_IsIncomplete` added, now 2 red under the seed |
| Single core | first core lookup used | 2 (`TwoCoreLookups_AreRefused_BeforeAnyWrite`) |
| Claim version condition | unconditional update | 3 (concurrent decisions with the lock defeated) |
| K1 claimed by another | check removed | 1 (`ClaimedByAnother_Is409`) |
| Supersedes status filter | any status links | 5 (`Rejected/Approved/InReview/Cancelled` not linked, most recent decides) |
| Route filter | `AddFinanceAuthorizationFilter` removed from reject | 11 contract tests (denied, right on another invoice, step-0 must-fire and must-not-fire, flag-off, failed rights check, needs Write) |

## Gate

| Check | Result |
|---|---|
| BFF build | 0 errors, 0 warnings |
| Touched suites (`InvoiceRejectReturnServiceTests`, `InvoiceRejectReturnEndpointsAuthorizationContractTests`, `InvoiceSubmissionPersisterTests`, all `InvoiceSubmission*`) | 72 service tests, 44 contract tests; `InvoiceSubmission*` 889 / 0 |
| Broad filter (`Finance\|Billing\|Signals\|Invoice\|Submission\|Decision\|Approv\|Reject\|Return\|Ownership`) | First run: 13,971 passed, 4 failed, 14 skipped. The 4 were `InvoiceSubmissionProcessingFlowTests` (049 E), whose store threw on the new `sprk_invoice` supersedes read; the store now answers it (fixed in `ff3f499569`), and the 4 pass in the rerun of `InvoiceSubmission*` |
| Full ArchTests | 1,858 / 0 (after the three ratchet entries) |
| `Sprk.Bff.Api.IntegrationTests` | 199 passed, 0 failed, 8 skipped (pre-existing skips) |

## BFF hygiene (§10) and placement

- **Placement.** In the BFF under `Services/Finance` and `Api/Finance`: CRUD plus authorization over Dataverse next to 103 and 105, reusing the line lock, the lifecycle service, the recorder and `HumanDecisionSignalClose`. No AI dependency (ADR-013). No background work.
- **Publish size.** `dotnet publish -c Release -r linux-x64 --no-self-contained` from `git archive` trees (src plus config) in the scratchpad. No package or csproj changed.

  | Build | Unzipped | Zipped |
  |---|---|---|
  | Fresh master `b86381d4c4` | 132,753,064 B | 37.97 MB |
  | Branch base `842110e6ab` | 136,054,280 B | n/a |
  | This branch | 136,124,192 B | 39.03 MB |

  - **Delta of this task:** +69,912 B (+0.07 MB) against the base.
  - **Against master:** +3.37 MB unzipped (+1.06 MB zipped). That is the whole work branch, not this task. Under the 5 MB task threshold and the 55 MB total.
- **CVE.** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.

## §11 component justification

| Component | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceRejectReturnService` | `InvoiceApprovalService` approves; `SubmissionOverrideService` decides submissions; nothing rejects or returns an invoice (grep `reject-invoice`, `return-to-firm`: only the action catalog). | No. Approve's service is 800+ lines of approve-only logic (D-72, totals); the shared parts are about 150 lines of duplicated helpers (see known limits). | Nobody can reject or return an invoice (FR-36); a Returned to Firm invoice can never be superseded. |
| Two routes | None. | Own endpoints file (parallel work on the other endpoint files). | No API for FR-36. |
| `FindSupersededInvoiceAsync` | The persister already creates the invoice and has `sprk_supersedesinvoice` in `InvoiceFamilyCoreRecordGuard`. | Yes, it is a method on the persister. | D-43 linkage is never set, so a resubmission is not tied to the invoice it replaces. |

## Code review and ADR check (Step 9.5)

Self-review at FULL depth; the operator's adversarial review follows.

| Finding | Class | Disposition |
|---|---|---|
| `FinishAsync` branch for an existing record (link present, portion not decided) is defensive: this service writes the status before the link, so it is unreachable from its own writes. It had no test and the failed-count guard there was unseeded. | F-class | Fixed: test `FailedClose_OverAnExistingRecord_IsIncomplete` and its seed. |
| After a record and its link exist, a later Signal close is not retried (the call is complete). | K3 | Known limit, same as 103. |
| A repeat with a different reason was Incomplete at `record` on every attempt (the repeat rebuilt the decision from the new reason and asked the writer again). | F1 | Fixed (fix round): the stored record is finished as it is. |
| An automatic rejection newer than a Returned invoice hid it from the D-43 walk. | F1 | Fixed (fix round): automatic rejections are skipped. |
| A claimed portion whose claimant never repeats blocks other approvers (K1) and leaves the invoice decided with no record. | K2 | Known limit, same as 103 (the coordinator files the sweep). |
| Duplicated helpers with `InvoiceApprovalService` (lock acquire/release, caller contact, Signals read/replay, filing check, claim release), about 150 lines. | K4 | Known limit. A shared helper is a consolidation follow-up once both are stable; extracting now would touch 103's reviewed code. |
| `InvoiceRejectReturnService.cs` is about 985 lines. | none | Cohesive: one decision flow (two verbs) with its repeat path. |

**ADR check.** Compliant with ADR-001 (Minimal API), ADR-002 (one BFF owner of the invariant, fails closed, no plugin), ADR-008 (endpoint filters, step 0), ADR-009 (no cross-request cache), ADR-010 (concrete registration, no new interface), ADR-013 (no AI), ADR-015 (records and logs carry ids only; the reason is in the Decision Record, never logged), ADR-018 (kill switch after authorization), ADR-019 (stable codes), ADR-028 (caller token only). No violations or warnings.

## Known limits and open questions

1. Held invoices cannot be rejected or returned here (judgement call 1).
2. The decision id is per portion, so a second reject after a return cannot happen (the invoice is no longer In Review).
3. The live evidence step was not run (nothing live per the brief).
4. A reviewer who is eligible but lacks Write on the invoice gets 403 from the route filter before eligibility is asked (same as 103).
5. The older-Returned-invoice case (judgement call 3).

### Known limits recorded in the fix round (no code)

- A resubmission filed on a corrected core record is not linked (the core-record guard refuses a supersedes link across core records).
- A Blocked resubmission is linked at creation, as an accepted one is (it is created In Review with its approval).
- Two resubmissions persisted at once can both link to the same Returned to Firm invoice (the same race as Duplicate detection).
- The repeat path treats a null portion status as completable (bad data only; the first call refuses it as `status_unknown`).
- The claimant's completion does not re-check eligibility, as 103: the claim proved eligibility at decision time.
- `DecidedInvoice_IsExcludedFromTheRollupSets` now also builds the actual and pending rollup queries from the sets (`BuildGroupedSpendFetchXml`, `BuildGroupedPendingSpendFetchXml`) and asserts the decided status is in neither.
- The predecessor read has no top count (a firm and number are a handful of rows); a single `RetrieveMultiple` is not paged, fine below 5,000 rows.
