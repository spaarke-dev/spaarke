# Task 049 - InvoiceSubmissionProcessing job: step-to-component map (pipeline part)

> Rigor FULL. Base `c342f4b9a` (`work/ebilling-r1`). Written before coding (POML step 0). Scope: everything in the POML except D-102 / D-104
> spend (FinanceBaseValues, FinanceRollupService, the rollup column and its script), which a parallel agent owns.
> D-101 overrides POML step 4 and the "Rejected: no invoice" criterion. Every submission that was read gets its invoice and lines.
> Rejected-path tests are written to D-101, not to the old text.

## 1. Step-to-component map

| # | Step | Component (contract source) | Input built by 049 from | Output 049 keeps |
|---|---|---|---|---|
| 0a | Payload, flag | `JobContract` (ADR-004), `EbillingOptions.Enabled` (040) | payload `{submissionId}` (044 `InvoiceSubmissionService.BuildJob`) | off -> submission Failed `ebilling.disabled`, `JobOutcome.Poisoned` (no retry storm) |
| 0b | Serialise | `IIdempotencyService.TryAcquireProcessingLockAsync(key "invoice-submission:{id:N}", ownerId = job id)` | submission id | not acquired -> `Failure` (retry); see section 3 |
| 0c | Re-read | `IGenericEntityService.RetrieveAsync(sprk_invoicesubmission)` | submission id | missing row -> Success (044 K4); Accepted / Rejected -> Success no-op; `sprk_invoice` set -> resume at after-commit steps; else restart from read |
| 0d | Tracking number | submission `sprk_submissionsequence`, `sprk_trackingnumber` (045, D-87) | the same read | missing -> Failed `ebilling.submission.tracking_missing`, Poisoned (never allocates) |
| 1 | Processing | `UpdateAsync(sprk_processingstatus = Processing)` (ADR-017) | - | persisted before any AI call |
| 1a | Bytes | `sprk_document` (graph drive / item), `RecordContainerResolver.IsDocumentPointerContainerAllowedAsync`, `ISpeFileOperations.DownloadFileAsync` (pattern of `InvoiceExtractionJobHandler`) | `sprk_document` lookup on the submission | unverifiable container -> Failed |
| 1b | Read | `IInvoiceAi.ReadInvoiceAsync(InvoiceReadRequest)` (043) | bytes, document id | `ExtractedInvoice`. `unreadable` / `page_limit` / `no_invoice` -> Rejected with that code and **no invoice** (not read, D-101). `service_failed` and transient -> retry, then Failed. `FeatureDisabledException` -> Failed with its `ErrorCode` (`ai.invoice.disabled`, `ai.invoice.read.not_configured`) |
| 1c | D-91 | pure comparer (new, in the processor) | typed `sprk_firminvoicenumber` vs `Header.FirmInvoiceNumber.Value`, normalised (case, whitespace, punctuation) | mismatch -> pipeline finding `ebilling.submission.invoice_number_mismatch`, both values, hold only. Never rejects, and never changes the tracking number |
| 2 | Resolve | `IInvoiceResolutionService.ResolveAsync(InvoiceResolutionInput)` (046) | `FirmId` = `sprk_vendororg`, `CoreRecord` = the submission's `sprk_regarding{core}`. Header totals and lines as `ResolutionField<T>(value, analyzer confidence)`. `OtherFields`: header `FirmInvoiceNumber`, `InvoiceDate`, `BillingPeriodStart` / `End`, `Currency`; line `Narrative`, `BilledTaskCode` / `BilledActivityCode` / `BilledExpenseCode` (the `BillingFieldNames` spellings). `TimekeeperSummaryNames` = the summary rows' names | per field `FieldConfidence.Combined`; per fee line `TimekeeperResolution`, `RateResolution` |
| 3 | Code | `ILineCodingService.CodeLinesAsync(LineCodingRequest)` (047) | `CodingCoreRecord` (core table, id), one `InvoiceLineToCode` per line, key = line index | per line codes, `Confidence`, `Observations`, `RefusedCodes`, `HasFirmCodeMismatch`, `CodeSetSource`, `CodeSetNeedsReview`. `LineCodingException.NoCodeSet` -> Failed. `InvoiceAiException` -> retry, then Failed |
| 4 | Evaluate | `InvoiceComplianceEvaluator.EvaluateAsync(BillingEvaluationRequest)` (072) | `SubmissionId`, `FirmId`, `CoreRecord` (**from the same read that files the invoice**), `TrackingNumber` = `sprk_trackingnumber` (allocated under D-87). `ReceivedOn` = `DateOnly.FromDateTime(createdon.UtcDateTime)`; a missing `createdon` fails closed, never `?? default`. Header dates, `Currency` as read, `HeaderConfidence[BillingFieldNames.*]` from 046's `HeaderFields` (currency under exactly `"Currency"`). `BillingLineFacts` per line: 046 resolution + 047 codes / observations, and `FieldConfidence` = 046 combined + the coding confidence under `TaskCode` / `ActivityCode` / `ExpenseCode` + each observation's own confidence under `Observation.*` | `BillingEvaluationResult` |
| 5 | Decide | `SubmissionRejectionRule.DecideAsync(result)` (073) | the evaluation | `SubmissionDecision` (Outcome, `DecidingFindings`, `HeldBecause`). D-89 holds by type. AI output alone never rejects (I-5): the decider's own guards |
| 5a | Channel | pure | decision + `sprk_channel` | Accepted -> **Accepted**; Rejected + SPA -> **Rejected**; Rejected + Internal -> **Blocked** (section 2) |
| 6 | Persist | **one `ExecuteTransactionRequest`** (section 4): invoice create, line creates, approval portion create (Accepted, Blocked), `sprk_document.sprk_invoice` update, submission update (`sprk_invoice`, `sprk_findingsjson`; status stays Processing) | row builders below | all or nothing |
| 6a | Invoice row | status from 014 (`InvoiceLifecycleService`, extended to own the create-time status, D-101); core lookup + 5 resolver fields through `InvoiceFamilyCoreRecordGuard.Check` / `DeriveResolverFieldsAsync` (013); owner from `IRecordOwnershipResolver.ResolveOwnerAsync(ForParents(core))` + `ApplyTo` (uac-r2); `sprk_vendororg`, `sprk_invoicenumber` (as typed), dates, `sprk_totalamount` (billed total), `sprk_correlationid` | the submission read and the extraction | client-chosen id, so Signals and the Decision Record can be keyed before the commit |
| 6b | Line rows | `sprk_billingevent`: `sprk_costtype`, `sprk_linestatus` (Pending; Flagged when it has a finding), `sprk_timekeeper` (as billed), `sprk_resolvedtimekeeper`, `sprk_approvedrate`, `sprk_amount`, codes as billed, AI code lookups (`sprk_taskcode` / `sprk_activitycode` / `sprk_expensecode` to the set's `sprk_billingcode` rows), `sprk_codingconfidence`, `sprk_extractionconfidence` (JSON, section 5, carries `CodeSetSource` / `CodeSetNeedsReview`), the line's OWN core lookup (D-75) + resolver fields, owner | 043 lines, 046, 047 | |
| 6c | Totals | `IInvoiceTotalsService.RecalculateAsync(invoiceId)` (016) | - | after the commit, before Signals. A refusal (`IsRefused`) is recorded on the submission findings and leaves totals unset; it does not undo the invoice |
| 7 | Signals | `BillingFindingSignalMapper.MapAll(result, subjects)` (072) then `SignalWriter.WriteAsync` (ontology-owned, used unchanged) | invoice and line ids from step 6 | one request per (policy, subject). Option a lifecycle: section 6 |
| 7a | Decision Record (Rejected only) | `BillingDecisionRecorder.RecordAsync` (074, D-100) | section 7 | |
| 8 | Terminal status | `UpdateAsync(sprk_processingstatus, sprk_outcomereasoncode)` | - | Accepted / Rejected / Rejected+`ebilling.submission.blocked` |
| 9 | After | `JobSubmissionService.SubmitJobAsync(InvoiceIndexing)` for an Accepted invoice (Blocked too: it is In Review); `IEbillingNotificationSink.SubmissionDecidedAsync` (new, no-op; 132 replaces it) for Accepted and Rejected | | InvoiceIndexing has a handler only when `DocumentIntelligence:Enabled` (FinanceModule): with none the message dead-letters, which the processor does not see. Logged once at Information |

## 2. Outcomes (D-101) and the held representation (agreed with the main session, 2026-10-19)

| Outcome | Invoice | Approval portion | Submission | Decision Record |
|---|---|---|---|---|
| Accepted | In Review | yes (Pending, no approver: 103) | Accepted, `sprk_invoice` | none |
| Rejected (SPA) | **Rejected**, created in that status through 014 | no | Rejected, reason = the first deciding finding's reason code | yes, system decision (section 7) |
| Blocked (internal, decider said Rejected) | In Review, **held** | yes | **Rejected** with `sprk_outcomereasoncode = ebilling.submission.blocked` | none (105's Override / Return record it) |
| Not read (`unreadable`, `page_limit`, `no_invoice`) | none | none | Rejected, that code | none |
| Failed (flag off, AI off, service failure after retries, no code set, missing tracking number) | none, or the committed one stays and the job retries the after-steps | | Failed, reason code | |

**Held** = invoice `statuscode` In Review AND a `sprk_invoicesubmission` with `sprk_invoice` = it, `sprk_processingstatus` = Rejected (100000003)
and `sprk_outcomereasoncode` = `SubmissionReasonCodes.Blocked` (`Sprk.Bff.Api.Services.Finance.Submission`, "ebilling.submission.blocked").
No schema option was added. A `Blocked` value on `sprk_processingstatus` would be cleaner, but that is a schema change and the owner's call (deferred).
**For 104 / 105.** A blocked internal submission shows status Rejected with the blocked reason code, and 044's GET route shows internal users
"Rejected". So 105 must set the submission to Accepted on Override (that releases the invoice). Return must move the invoice out of In Review.

## 3. Serialisation (074 K1 / K-4, acceptance criterion)

The handler holds `IIdempotencyService` processing lock `invoice-submission:{submissionId:N}`, owner = job id, for the whole run.
- A second delivery of the same submission cannot run beside it, so the Decision Record (decision id = `IdOf(submission id)`, deterministic),
  the Signals and the invoice create are serialised per submission and therefore per invoice. The invoice belongs to exactly one submission.
- Test: two concurrent deliveries with one submission id: one runs, the other returns Failure, and exactly one invoice create and one record call happen.

## 4. All-or-nothing

There is no multi-table transaction on `IGenericEntityService` today: `BulkUpdateAsync` is single-table updates only.
- Proposed: an additive `ExecuteTransactionAsync(IReadOnlyList<OrganizationRequest>)` on `IGenericEntityService`. Its default body throws, as the
  `BulkUpdateIfRowVersionsMatchAsync` precedent does. `DataverseServiceClientImpl` implements it with a pure builder, as `BuildBulkUpdateTransaction` is.
- §11. Existing: `BulkUpdateAsync` / `BulkUpdateIfRowVersionsMatchAsync`. Extension: they build update-only, single-table requests, and widening
  their signature changes callers. Cost of doing nothing: FR-20's all-or-nothing would need a compensation path that has a real orphan window.
- **Resume marker.** The submission update (`sprk_invoice` + `sprk_findingsjson`) is inside the transaction. "Linked invoice" therefore equals
  "the transaction committed", and the stored findings record is what the resume path builds its Signals and Decision Record from. It never
  re-runs AI (AI output is not deterministic).

## 5. Stored shapes

- `sprk_findingsjson` (submission, for 130 / 133). Holds:
  - one entry per finding: rule type, policy code, version id, clause, subject, reason, values read with confidences, held-because or deciding;
  - the pipeline findings: D-91 mismatch, refused codes, firm-code mismatch, code set needs review;
  - the Signal plan: the merged, scrubbed `BillingSignalMapping` request, sentence and evidence, never `.Findings` (D-112);
  - the decision.
- Line `sprk_extractionconfidence` (FR-04):
  - `{ field: { value?, confidence, combined, page, box, repair } }` per field;
  - plus `coding: { codeSetId, codeSetSource, needsReview, refused[], firmCodeMismatch }`.
  - **Deviation:** no line column holds `CodeSetSource` / `CodeSetNeedsReview` (010's column list), so they are stored in this JSON on the line.
  - The suggested set is never persisted as a mapping (048's).

## 6. Signal lifecycle (option a, owned here)

For each mapping, read the Signals with dedupe key prefix `SignalWriter.DedupeKeySubjectPrefix(policy, subject)`, and take the latest episode with its status, `versionnumber` and the `billing.reasonset` evidence `Ref`.
- None: write episode 1.
- Open and the same fingerprint: write the same episode (the writer refreshes).
- Open and a different fingerprint: `SignalWriter.CloseAsync(id, rowVersion, ResolutionSuperseded, ...)`, then write episode n+1.
- Resolved and the same fingerprint: skip (a resume never re-raises what a person resolved).
- A writer result with `SkippedRefusalCode`: treated as a failed write. The submission stays Processing and the job retries.

**Primary reason.**
- Rejected: the primary reason (`sprk_sentence`, headline) of a group that holds a deciding finding is the first deciding finding in reason order, re-rendered by the mapper. 072's mapper gains an optional "deciding" input. It is 072's file, not ontology's.
- When 073 held every reason of a group, the Signal is not presented as a rejection.
  - Its lane and severity are the policy's.
  - The sentence is the mapper's primary reason.
  - Proposal: the evidence gains a `billing.decision` line, `Held` with the hold reason.

## 7. Automatic rejection's Decision Record (D-100, 074 caller obligations)

The decision is `reject-invoice`, subject = the invoice. Its decision id is a deterministic `Guid` derived server-side from the submission id, so a retry reuses it. The decider is `BillingDecider.Automated("submission rejection rule")`: null confirmer, null gate tier.
- `Reason` = the deciding reason codes.
- `PolicyVersionId` = the first deciding finding's version (server-derived).
- `Facts` = decision, threshold, and every merged reason from the scrubbed mappings.
- `Values` = strings and ints only (stable hash, K-5).

Order:
1. `Preflight` before the transaction (record-before-mutate, K-3).
2. After the commit, before the Signals and before the submission's terminal status.
3. A resume re-records idempotently.

`ResolvedSignalIds` is empty (open question 3).

## 8. Composition result

The contracts compose. No escalation trigger fires. Each gap below has an in-scope answer or is an owner question.

| # | Gap | Answer |
|---|---|---|
| G1 | No multi-table transaction | Additive `ExecuteTransactionAsync` (section 4) |
| G2 | 014 cannot create in a status | Extend 014: `InvoiceStatusCodes` create-time fields for In Review / Rejected only, owned by `InvoiceLifecycleService` (D-101) |
| G3 | No line column for code-set source / review | Stored in `sprk_extractionconfidence` JSON (section 5) |
| G4 | No pipeline rule for D-91 and the coding findings (SignalWriter needs a policy) | Recorded in `sprk_findingsjson` and on the line, and they hold. They are not Signals (open question 1) |
| G5 | `ReceivedOn` contract says "received date"; brief says `createdon` | `createdon` UTC date, fail closed when missing |

## 9. Owner / main-session questions (not guessed)

1. Should the D-91 mismatch and the coding findings (refused code, firm-code mismatch, code set needs review) be Signals? That needs a system billing policy row, and SignalWriter refuses a write without one.
2. **Credit lines.** `sprk_billingevent.sprk_amount` cannot hold a negative value (016 notes: platform minimum 0). An invoice with a credit line (D-105) therefore cannot be persisted, and the transaction would fail. Options:
   - store the absolute value with a credit flag in the JSON;
   - a schema change;
   - fail as `ebilling.submission.credit_line_unsupported`.
3. Should a rejected invoice's open Signals stay open? The automatic rejection resolves none of them (closing with Acted is the commit route's). Or should 049 close them as `ConditionCleared` or `PolicyRetired`?
4. Is there an invoice column for the tracking number? The POML step 3 lists it, but `docs/data-model/sprk_invoice.md` has none. Proposed: `sprk_name` = the tracking number.
