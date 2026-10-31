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

## 10. Part B - processor steps 1-5 (2026-10-19)

**Files.** `Services/Finance/Submission/`:
- `SubmissionReasonCodes.cs`: every outcome and pipeline-finding code the job writes. 043 / 047 codes are referenced, not re-spelled. `FinanceBaseValues.BlockedReasonCode` now takes `SubmissionReasonCodes.Blocked` (the `TODO(049)` literal is gone).
- `IInvoiceSubmissionProcessor.cs`, `InvoiceSubmissionProcessor.cs`, `SubmissionEvaluationOutcome.cs`.

Tests: `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionProcessorTests.cs`. The read, resolution and coding are faked at their interfaces. The 072 evaluation (in memory, on loaded facts, through an internal ctor seam) and the 073 decider are the real ones.

**Entry point.** `EvaluateAsync(Entity submission, Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>> loadDocument, ct)`. The row is retrieved with `SubmissionSnapshot.Columns`. It writes nothing.
- **Not in part B:** the Processing status write (step 1) and the document download with its container check (step 1a, the `InvoiceExtractionJobHandler` pattern). Those are the job's.
- `loadDocument` is called only after the flag and the row have passed, so nothing is downloaded for a submission that fails closed.

**Order.**
1. Flag (`ebilling.disabled`).
2. `SubmissionSnapshot.Read` fails closed on each of these, before any download or AI call:
   - tracking number: `sprk_submissionsequence` > 0 and `sprk_trackingnumber` not blank;
   - `createdon` (null or default);
   - firm;
   - exactly one core record;
   - typed firm invoice number;
   - document;
   - SPA / Internal channel.
3. Read. `unreadable` / `page_limit` / `no_invoice` → RejectedUnreadable. `FeatureDisabledException` → Failed with its `ErrorCode`. Every other `InvoiceAiException` (service_failed) and every transient fault propagates.
4. D-91 comparison.
5. 046, then 047. `FeatureDisabledException` → Failed. `LineCodingException` → Failed with its code. The coding `InvoiceAiException` propagates.
6. 072, then 073.
7. Classify: Accepted; Rejected + SPA → AutoRejected (reason = `DecidingFindings[0].Finding.ReasonCode`); Rejected + Internal → Blocked (`SubmissionReasonCodes.Blocked`).

**Mapping decisions.**
- `ReceivedOn` = createdon's UTC day (Unspecified kind is UTC).
- `TrackingNumber` = `sprk_trackingnumber` (the text the evaluator and its priors compare; `sprk_submissionsequence` is its `n`, required too).
- `FirmInvoiceNumber` in the request is the number **as read**, since 072 treats it as an extracted header field with a confidence. The typed one is D-91's input.
- `Currency` is passed as read. The evaluator normalises it through `BillingCurrency` (D-119); the processor never does.
- `HeaderConfidence` = 046 `HeaderFields` combined, so `"Currency"` is under exactly that key.
- Line confidence:
  - 046 combined per field;
  - the coding confidence under `TaskCode` / `ActivityCode` / `ExpenseCode`;
  - the highest confidence per observation kind under `Observation.*`.
- Line index `i` is the 043 list position, the 046 `LineIndex` and the 047 `LineKey` `"i"`.
- Credit lines pass through with negative values.

**D-91.**
- `InvoiceNumberCheck.Compare`: letters and digits only, upper-invariant.
- Mismatch → one `SubmissionPipelineFinding(ebilling.submission.invoice_number_mismatch, {typed, read})`. It never changes the kind.
- No number read → `NotRead`, with no finding (a missing value is RequiredData's).

**Tests note.** I-5 and D-89 are proved twice:
- through the gate, which already downgrades a stored Reject on a non-eligible type to Flag;
- with the stored Reject restored after the gate (a row written outside publish), so the decider's own guards are proved: `type_not_reject_eligible` for AiObservation, and `type_holds_only` for ApprovedTimekeeper with `ReferenceMissingCap` 0.95 > threshold 0.90.

**Deviation from the brief's name.** The kind is `Failed`, not `FailedBeforeRead`, because `ebilling.coding.no_code_set` and a coding kill switch fail after the read. `Read` is non-null in that case. Per section 2, no invoice is created for any Failed.

**DI.** Not registered. FinanceModule is the shared append point agent A also edits; part E registers it scoped with the handler. Its dependencies are all registered unconditionally (`IInvoiceAi` has `NullInvoiceAi`).

## 11. Part C - persistence, step 6 (2026-10-19)

**Files.** `Services/Finance/Submission/IInvoiceSubmissionPersister.cs`, `InvoiceSubmissionPersister.cs` (§11 header in the file). Tests:
`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionPersisterTests.cs` (outcomes from the real part B processor
harness; Dataverse, owner and totals are strict doubles, so any write outside the one transaction fails a test).

**Entry point.** `PersistAsync(SubmissionEvaluationOutcome, string? correlationId, ct)` → `SubmissionPersistResult`:

| Status | Meaning | Members |
|---|---|---|
| `Committed` | the transaction committed (or an unknown outcome was proved by the link), totals ran | `InvoiceId`, `LineIds` (line index → row id), `ApprovalId`, `Totals` |
| `Resumed` | the submission already linked an invoice: nothing created and (R2, 14c) NO totals; the after-steps' `ResumeAsync` recalculates them | `InvoiceId` (the stored one), `LineIds` (queried: `sprk_linesequence` - 1) |
| `ReEvaluate` | the submission's core record differs from `outcome.CoreRecord` (072 part L): nothing written | `CurrentSubmission` (pass to the processor) |
| `Retry` | transient: nothing persisted when `InvoiceId` is null; committed but totals failed when set | `ReasonCode` |
| `Refused` | will not succeed on retry (core fields incomplete, owner refused, > 1,000 requests, findings JSON over 1,048,576) | `ReasonCode` |
| `SubmissionMissing` | the row is gone (044 K4) | |

A submission read fault or cancellation propagates. A Failed / RejectedUnreadable outcome is an `ArgumentException` (D-101: no invoice).

**The one transaction**, in order: 014's create request(s) (`BuildCreateRequest` now returns `Requests`, a list of one), one create per
line, the approval portion (not for AutoRejected), the `sprk_document.sprk_invoice` update, the submission update (`sprk_invoice`,
`sprk_processingstatus`, `sprk_outcomereasoncode`, `sprk_findingsjson`). Accepted / AcceptedUncoded → submission Accepted, reason null;
AutoRejected → Rejected + the first deciding code, invoice Rejected; Blocked → Rejected + `ebilling.submission.blocked`, invoice In Review.

**Ids are deterministic** (`InvoiceIdFor`, `LineIdFor`, `ApprovalIdFor`: SHA-256 of a key on the submission id, as `DecisionRecordWriter.IdOf`).
A retry reuses them, so a second invoice cannot be created, and part D can key Signals without a read.

**Rows.** Invoice: status via 014 only; core lookup + 5 resolver fields from 013's guard (`Check` + `DeriveResolverFieldsAsync`, once, reused on
every row; each built row is re-checked and a refusal is a builder defect); owner `ResolveOwnerAsync(ForParents(core))` once + `ApplyTo` on
every row; `sprk_name` (`InvoiceReviewService.BuildInvoiceName`), `sprk_invoicenumber` as typed, `sprk_trackingnumber` (D-124),
`sprk_vendororg`, `sprk_document`, dates, `sprk_totalamount` (TotalDue as read, else the signed line sum), `sprk_currency`,
`sprk_correlationid` (the job's, else the submission's). Lines (6b): billed values signed (D-121), fee hours / expense quantity in
`sprk_quantity` and rate / unit cost in `sprk_rate` (the 072 fact loader reads these back for Duplicate), `sprk_eventdate` at UTC midnight
(076's convention; falls back to the invoice date), resolved timekeeper, approved rate, coding confidence, `sprk_linestatus` Flagged when a
072 finding names the line or 047 refused a code / found a firm-code mismatch, else Pending; `sprk_extractionconfidence`
`{ fields: { name: { confidence, combined, page, repair, box?, returned? } }, coding: { status, codeSetId, codeSetName, codeSetSource,
needsReview, confidence, task/activity/expense {code, firmCode, mismatch}, firmCodeMismatch, refused[], observations[] } }`, or
`coding: { status: "NotCoded", hold }` for D-126. Approval: Pending, invoice, core trio + resolver fields, owner; no approver (103).

**`sprk_findingsjson`** `{ schema, kind, reasonCode, invoiceId, trackingNumber, receivedOn, decision {outcome, reliabilityThreshold,
deciding[]}, codeSet, findings[] {index, ruleType, policyId, policyCode, policyVersionId, clause, clauseLocation, subject, line, reasonCode,
detailWithheld, consequence, rejects, heldBecause, valuesRead[]}, pipelineFindings[], codingFindings[] }`. **D-112:** in a (policy, subject)
group holding a withheld finding, no entry carries `valuesRead`.

**D-126 (part B changed, the cleaner option).** The processor, not the persister, classifies: a `LineCodingException` or a coding
`FeatureDisabledException` after the read is `SubmissionEvaluationKind.AcceptedUncoded` (read + resolution, no coding / evaluation /
decision) with a hold pipeline finding `ebilling.submission.coding_no_code_set` or `ebilling.submission.coding_unavailable` (value `cause`).
Reason: the persister then never re-interprets a Failed outcome, and `Failed` again means "never read". New SubmissionIntegrity reasons
`coding.no_code_set` / `coding.unavailable` (template, headline, flag-only, `BillingSystemFindings.NoCodeSet` / `CodingUnavailable`); part D maps
the two hold codes to them. No rule is evaluated on uncoded lines (owner question 2 below).

**Faults.** `DataverseTransactionException` not unknown → `Retry` (`transaction_failed`), nothing persisted, submission untouched (never
Accepted). Unknown → re-read `sprk_invoice`: our invoice id → committed; else (or the re-read fails) → `Retry` (`transaction_outcome_unknown`).
Totals: a refusal is returned in `Totals` (part D records it); a fault is `Retry` (`totals_failed`) with `InvoiceId` set, and the next run
resumes from the link (tested: one transaction across both runs).

**ArchTests.** ADR-010 ceiling 167 → **168** (+1, `IInvoiceSubmissionPersister`, seam for part E's handler tests). Census: the persister's
creates are invisible to the scanner (rows built in helpers, handed to `new CreateRequest { Target = variable }`), so it is listed in
`UnscannedWriters` (it calls the resolver; that test passes). **DI: not registered** (part E, with the processor and handler).

**Verification.** Build 0 errors. Filtered unit suite 5,563 passed / 0 failed / 11 skipped. ArchTests 1,209 passed. Fail-before: the two
D-126 processor tests fail on the part B processor; the lifecycle tests did not compile against the old shape; for the new persister, nine
mutations (core re-check off, unknown-outcome always retry, resume off, line status always Pending, `Math.Abs` amounts, no approval, no
D-112 scrub, AutoRejected created In Review) fail 11 of its 18 tests.

**For part E / D (contract consequences, not decided here).**
- The terminal status is committed in the transaction (brief), so section 1 rows 6 / 8 ("status stays Processing"; terminal status after
  Signals) are superseded. **Part E's step 0c must test `sprk_invoice` BEFORE the "Accepted / Rejected → no-op" rule**, or a retry after a
  Signal or totals failure would no-op and never write Signals. A redelivered, finished submission then resumes (part D is idempotent).
  The 074 order "record after the commit, before the terminal status" can no longer hold literally; Preflight before the transaction still can.
- On `Retry` the handler abandons for redelivery; on `Refused` it marks the submission Failed with the code; on `ReEvaluate` it re-runs the
  processor on `CurrentSubmission` (bound the loop).

**Known limits (K-class).** The core-record re-check reads before the transaction, so a re-file in the milliseconds between them is not
caught (an `IfRowVersionMatches` on the submission update would close it; needs a live check that `RetrieveAsync` fills `RowVersion`).
`sprk_invoiceapproval.sprk_regardingrecordnumber` is Text(100) while the guard does not cut the number (as on 044's submission write).
No AI code lookups (`sprk_taskcode` / `sprk_activitycode` / `sprk_expensecode`): 047's `ResolvedCodeSet` carries codes, not
`sprk_billingcode` ids; the codes are in the line's `coding` JSON. A coding model fault (`InvoiceAiException`) after the read still
propagates, so after the job's last retry part E would mark Failed with no invoice (D-126 names only no-code-set and switched-off).
A deterministic id collides if a committed invoice's submission link is ever cleared and the submission re-processed (the create then
faults and retries); nothing clears the link today.

**Owner questions (not guessed).**
1. Creator person (task 146 c1-r1): the job stamps no `sprk_createdbyperson` on the invoice, lines or approval. Should it be the submitter
   (internal: the systemuser; SPA: a contact, which the column may not take)? Until decided the persister is not listed as person-less.
2. D-126: should 072 still run on uncoded lines (Duplicate, rates, windows), with any Reject held instead of applied? Today nothing is judged.
3. `sprk_outcomereasoncode` on an AcceptedUncoded submission is left null (the column is documented for Failed / Rejected); the cause is in
   the findings JSON. Set it instead?
4. AutoRejected "findings on both": the findings JSON lives on the submission (which links the invoice); the invoice has no findings
   column, so its copy is part D's Signals. Confirm no invoice-side column is wanted.

## 12. Part C2 - D-130 follow-ups to parts B and C (2026-10-19)

Supersedes the matching statements in sections 10-11 (the "no rule is evaluated on uncoded lines" rule, owner questions 1-3, and "the terminal
status is committed in the transaction"). Files: `InvoiceSubmissionProcessor.cs`, `SubmissionEvaluationOutcome.cs`, `InvoiceSubmissionPersister.cs`,
`IInvoiceSubmissionPersister.cs`; tests `InvoiceSubmissionProcessorTests.cs`, `InvoiceSubmissionPersisterTests.cs`; ArchTests census `PersonBearingWriters`.

**1. Rules run on an uncoded submission (D-130 1).** A coding `FeatureDisabledException` / `LineCodingException` after the read now only adds the hold
pipeline finding (`coding_no_code_set` / `coding_unavailable`) and leaves `Coding` null; 072 and 073 still run on the firm's billed data. Uncoded lines
reach the evaluator with null `TaskCode` / `ActivityCode` / `ExpenseCode`, no observations and no coding or observation confidence.
- A rejecting rule: AutoRejected (SPA) or Blocked (Internal) as usual; the outcome keeps the coding hold finding, the invoice is created Rejected (SPA),
  and the findings JSON carries the rule finding and the hold finding.
- Otherwise: `AcceptedUncoded` (held In Review), with the rule findings beside the coding finding.
- AI-assigned inputs: checked, no change needed. The evaluator handlers read codes through `BillingText.Text` (null-safe) and the rejection rule holds a
  finding that lists an AI-assigned field as `ai_assigned_input` before it looks at confidence. Pinned: an over-cap expense with no billed code reads
  `ExpenseCode` and is held; the same line with a billed code rejects (control); every billing rule type evaluates uncoded lines without a fault.
- The persister now requires an evaluation and a decision for every kind; only `Accepted` requires `Coding`.

**2. Submitter (D-130 3).** Source: the submission's own `sprk_createdbyperson` (a lookup to `systemuser`), which intake stamped through
`RecordOwnershipContext.RequestedBy` (SPA: the external caller's `SystemUserId`; internal: the caller's object id) and which 013 added to the submission
table. `sprk_submitter` (a contact, SPA only) does not fit the systemuser lookup and is not used. The persister reads the column in its existing
submission re-read (added to `SubmissionColumns`) and stamps it with `RecordCreatorPerson.Stamp` on the invoice, every line and the approval. A submission
with no `sprk_createdbyperson` is `Refused` with `ebilling.submission.submitter_missing` before any write (fail closed; part E marks it Failed).
Consequence to confirm: an SPA caller whose external contact has no linked systemuser (nothing stamped at intake) now fails here instead of creating a
creator-less invoice.

**3. Processing until part D (D-130 a).** `BuildSubmissionUpdate` writes `sprk_invoice`, `sprk_findingsjson` and `sprk_outcomereasoncode` (the reason
code of the outcome; null for Accepted), and not `sprk_processingstatus`. `SubmissionPersistResult` exposes what part D writes last:
`TerminalStatus` (`InvoiceSubmissionStatus?`) and `TerminalReasonCode`, set on Committed, Resumed and a totals Retry (derived from the outcome by
`InvoiceSubmissionPersister.TerminalFor`).

| Kind | TerminalStatus | TerminalReasonCode |
|---|---|---|
| Accepted | Accepted | null |
| AcceptedUncoded | Accepted | `ebilling.submission.coding_no_code_set` / `coding_unavailable` (D-130 b) |
| AutoRejected | Rejected | first deciding finding's code |
| Blocked | Rejected | `ebilling.submission.blocked` (held representation unchanged) |

Part E: a Processing submission with `sprk_invoice` set means "resume the after-steps" (the status is no longer a commit signal); the
section 11 note that step 0c must test the link before the "Accepted / Rejected is a no-op" rule is now the intended order, not a workaround.

**4. Reason code (D-130 b).** Done through item 3 (`AcceptedUncoded` outcome `ReasonCode` = the coding hold code). Owner question 3 is answered.

**5. Approval record number.** `BuildApproval` cuts `sprk_regardingrecordnumber` to 100 characters. Not cut (outside the brief): the same value on the
invoice (column length not documented in the data-model doc) and on the submission (044's write); the line column is Text(1000).

**Owner questions 1, 2 and 3 of section 11 are closed by D-130.** Question 4 (invoice-side findings column) is closed by D-130 (c).

**Verification.** Build 0 errors. Filtered unit suite 5,156 passed / 0 failed / 11 skipped (the part C figure of 5,563 came from another base).
ArchTests 1,209 passed. Fail-before: against the part B processor 9 tests fail (the three D-126 tests and six C2 ones); with the persister mutated
(no line / approval stamp, status written in the transaction, no trim) 7 tests fail.

## 12a. Part C3 - Submitted By (D-131)

Owner decision (2026-10-19, D-131): `sprk_invoice.sprk_submittedby` (Submitted By), a lookup to `contact`, created live on dev (spaarkedev1). The BFF sets
it from the external submitter's contact. This supersedes the C2 note that `sprk_submitter` "is not used".

- **Read.** The persister's submission re-read (`SubmissionColumns`) now includes `sprk_submitter` (set at intake for the SPA channel by
  `InvoiceSubmissionService`); it was not in the explicit column set before.
- **Write.** When `sprk_submitter` is present, `sprk_submittedby` = that contact is stamped on the INVOICE only (lines and the approval have no such
  column). Constants: `InvoiceSubmissionPersister.SubmissionSubmitter` and `InvoiceSubmittedBy`.
- **`sprk_createdbyperson` now follows the BFF-created-row rule** (`RecordOwnershipResolver.ResolveOwnerAsync`: an unknown person refuses nothing, the row
  records nobody):

| Person (`sprk_createdbyperson`) | Contact (`sprk_submitter`) | Result |
|---|---|---|
| present | any | Committed; person on invoice, every line and the approval; contact (if any) as `sprk_submittedby` on the invoice |
| absent | present | Committed with no `sprk_createdbyperson` on any row; `sprk_submittedby` on the invoice; one Warning (submission id and column name only, ADR-015) |
| absent | absent | `Refused`, `ebilling.submission.submitter_missing`, before any write (intake recorded nobody: a data fault) |

- **Tests** (`InvoiceSubmissionPersisterTests`): person + contact (invoice carries both, lines and approval the person only, the re-read asks for
  `sprk_submitter`); contact only (Committed, no row has the person, exactly one Warning without the contact id); internal person only (no
  `sprk_submittedby`, no warning); the existing neither-case refusal is unchanged.
- **Owner actions.** The column must reach the SpaarkeMaster solution source by an export from dev. `ExternalAccessModule`'s column allow-list was NOT
  changed: no SPA surface reads `sprk_submittedby` yet.

## 13. Part D - after-commit steps (2026-10-19)

**Files.**
- New: `Services/Finance/Submission/IInvoiceSubmissionAfterSteps.cs` (the interface, `SubmissionAfterStepsStatus`, `SubmissionAfterStepsResult`)
  and `InvoiceSubmissionAfterSteps.cs` (the section 11 answers are in its header). Not registered in DI.
- Changed:
  - `BillingFindingSignalMapper` takes 073's decisions. The deciding reason is primary. A Reject group that 073 held throughout gets a
    `billing.decision` / `Held` evidence line carrying the hold reason. The fingerprint does not change. (a): mismatch precedence.
  - `BillingSystemFindings.CodeRefused` and `BillingRuleCatalogue`: (b).
  - `InvoiceSubmissionPersister`: the findings JSON stores `invoiceName`, `invoiceNumberReadConfidence` and `signalPlan`. `IdOf`, `LineNameFor`
    and `SignalSubjectsFor` are now shared with part D.
- Tests: `InvoiceSubmissionAfterStepsTests` (34), `BillingFindingSignalMapperDecisionTests` (5), and additions to `BillingSystemPolicyTests` and
  `InvoiceSubmissionPersisterTests`.

**Input.** `RunAsync(SubmissionPersistResult, correlationId)` takes Committed, Resumed, or a totals Retry that has an invoice id. (Since R2, 14c, the handler calls it for Committed only; a submission found already linked goes to `ResumeAsync`.)
- Everything is read from the STORED submission: `sprk_findingsjson`, the link, the channel and the document. Nothing re-runs AI or the
  evaluation.
- The rule Signals are written from the stored `signalPlan`: the mapper's scrubbed requests, rendered once at commit. The first run and every
  resume therefore write the same requests.
- A missing row returns `SubmissionMissing`. A link to another invoice, or unreadable findings, returns `Refused` (`invoice_link_mismatch`,
  `findings_unreadable`) and nothing is written.

**Step order and why.**
1. **Signals**: the rule Signals from the plan, then the system Signals.
   - They come first, so the record's fact snapshot and the firm's notification never describe Signals that do not exist yet. D-122 moved the
     record after them.
   - Every request is attempted. Any failure returns `Retry` before step 2.
2. **Decision Record**, AutoRejected only.
   - Action `reject-invoice`; subject the invoice; decision id `IdOf("ebilling-auto-reject-{submissionId}")`; decider
     `BillingDecider.Automated("submission rejection rule")`.
   - Reason: the distinct deciding codes, joined by ", ". PolicyVersionId: the first deciding finding's version.
   - Values: submissionId, trackingNumber and decidingReasonCodes, as strings.
   - Facts: decision, outcome, threshold, the deciding codes, and every plan entry (policy, subject, codes, consequence, fingerprint, group
     decision, rendered sentence).
   - Written: the invoice.
   - Refusals: the permanent ones (review_invalid, fact_snapshot_missing, subject_*, record_class_underivable, core_unsupported, owner_refused,
     owner_missing) return `Refused`; the rest return `Retry`.
   - A totals Retry stops here with the persister's `totals_failed` code.
3. **Indexing**, for Accepted, AcceptedUncoded and Blocked (an invoice In Review).
   - The `JobContract` has `JobId = IdOf("ebilling-index-{invoiceId}")` and `IdempotencyKey = invoice-index-{invoiceId}`, which is the extraction
     handler's key, so Service Bus duplicate detection on the MessageId applies. Payload: `{InvoiceId, DocumentId}`.
   - A fault returns `Retry` (`indexing_enqueue_failed`).
   - Once per process, an Information line says the handler needs `DocumentIntelligence:Enabled`.
   - No document: indexing is skipped with a warning.
4. **Notify**, once. The marker is `ebilling-submission-notified:{submissionId:N}` and is kept for 30 days.

   | Kind | Notified as | Reason code |
   |---|---|---|
   | Accepted, AcceptedUncoded | Accepted | none |
   | AutoRejected | Rejected | the deciding code |
   | Blocked | Rejected, with the invoice id | `ebilling.submission.blocked` |

   - Blocked, decided: the submission did not pass automatically; the invoice exists In Review.
   - A sink fault is logged and the run continues, under the sink's existing contract ("a failure must not undo the decision ... does not
     retry the pipeline"). The marker is not set then.
   - Delivery is at-least-once only in two cases: the window between the sink returning and the marker being set, and a cache that fails
     open (`IsEventProcessedAsync` returns false on a fault).
5. **Terminal status, last**: `UpdateAsync(sprk_processingstatus = TerminalStatus, sprk_outcomereasoncode = TerminalReasonCode)`. Only a
   `Completed` run writes it. Any earlier failure leaves the submission Processing with its link, which is part E's resume marker.

**System Signals (D-123, D-125).**
- Sources, all written under `BILL-SYSTEM-SUBMISSION`:
  - the stored pipeline findings: the mismatch (typed and read numbers, plus the read confidence) and the no-code-set and coding-unavailable
    holds;
  - `codeSet.needsReview`;
  - `codingFindings`: refused codes and firm-code mismatches.
- The policy is read once per run, app-only, and only when a system finding exists: `sprk_policy` (billing domain, by code) joined to its
  current version.
- It is usable only when there is exactly one row, the policy is enabled and Active, and its current version is Active, of type
  SubmissionIntegrity and in force on the stored `receivedOn`.
- Otherwise there are no system Signals, one Warning names only the policy code and its state, and the run continues.

**Idempotency per step.**
- Signals: the option-a lifecycle (section 6) on the dedupe-key prefix. A resume finds the open episode with the same fingerprint and
  refreshes it, so there is no duplicate episode.
- Record: the decision id is deterministic, and the writer answers `AlreadyRecorded`.
- Indexing: the job id and idempotency key are deterministic.
- Notify: the marker.
- Terminal: the same values on every run.

**Lock key and peers.**
- No new lock. The handler's `invoice-submission:{submissionId:N}` lock (part E step 0) serialises 049 per submission.
- `SignalWriter` and `SignalReevaluationJob` take no processing lock. They rely on the dedupe key and row-version conditional updates.
- The supersede close is row-version conditional. If the Signal changed since the read, the close returns `ChangedSinceRead`, and part D
  returns `Retry` (`signal_changed`) without writing a new episode. Test: a concurrent refresh between the read and the close, after which the
  redelivery completes.
- 104 and 105 do not exist yet. When they close the same Signals, they must use the same conditional close.

**What part E must call and register.**
- Before `PersistAsync`, call `afterSteps.PreflightRejection(outcome)` to refuse before the transaction. A `DecisionRecordRefusedException`
  there means Failed, with its reason.
- After `PersistAsync` returns Committed, call `RunAsync(persisted, correlationId)` (as written; since R2, 14c, Resumed and Retry-with-an-invoice-id
  call `ResumeAsync(submissionId, correlationId)` instead, so the table below is the Committed case):

  | Result | Part E does |
  |---|---|
  | Completed | Nothing more |
  | Retry | Leaves the submission Processing and lets the job retry (throw or abandon, per ADR-004) |
  | Refused | Marks the submission Failed with `ReasonCode` |
  | SubmissionMissing | Completes the message |
  | An exception (a Dataverse read or write fault, a Signal write fault) | Treats it as transient: Processing, retry |

- Register, scoped: `IInvoiceSubmissionProcessor`, `IInvoiceSubmissionPersister`, `IInvoiceSubmissionAfterSteps -> InvoiceSubmissionAfterSteps`,
  and the handler.
- Already registered dependencies: `SignalWriter`, `BillingDecisionRecorder` (singleton), `JobSubmissionService`, `IIdempotencyService`,
  `IEbillingNotificationSink` (no-op until 132), `IGenericEntityService`.

**Census.** ADR-010's one-to-one interface ceiling went from 168 to 169 for `IInvoiceSubmissionAfterSteps`. The seam: part E's handler tests
fake it. Falsifiable: if they do not, register the concrete class and lower the ceiling.

**(a)** Among flag-only reasons, the D-91 invoice-number mismatch is the primary reason of a shared system Signal. Tests: a `MapAll` test with
both reasons on one invoice, and a must-not-fire test with two coding notes.

**(b)**
- `CodeRefused` cites the code only when `BillingRuleCatalogue.IsCodeShaped` holds. That is the rule schemas' `$defs.code.pattern`, read once,
  and it must match the whole value.
- Otherwise the reason is `coding.refused_code_not_shown`, with no `line.refusedCode` fact.
- `NotApplicableToLineType` becomes `coding.code_kind_not_for_line`, with its own template and headline.

**Open questions (not guessed).**
1. **D-122 resolve-all is not implemented.**
   - No path closes a Signal as resolved by a billing decision:
     - `SignalWriter.CloseAsync` accepts system resolutions only (ConditionCleared, Superseded, PolicyRetired);
     - `DecisionCommitService.CloseSignalAsync` is private and stamps a person in `resolvedby`;
     - the recorder leaves closing to its caller.
   - Listing Signals in `ResolvedSignalIds` without closing them would half-resolve them, so the record lists none.
   - Needed (ontology-owned): a SignalWriter entry that closes a Signal as Acted, linked to a Decision Record, with no person, for a system
     decision.
   - Note for that work: the record hash includes `ResolvedSignalIds`, so the resolved set must be stable across a resume.
2. A totals refusal is not recorded in the findings. Part C said "part D records it"; the brief did not ask for it.
3. `IEbillingNotificationSink`'s "do not retry" contract was kept. If the notification must be exactly-once even when the sink faults, the sink
   contract has to change (owner decision).

### 13a. After-steps core fixes (ontology review of part D)

Supersedes the matching statements in section 13 ("Terminal status, last: `UpdateAsync(... = TerminalStatus ...)`", the policy read's "joined to
its current version", open item 2). Files: `IInvoiceSubmissionAfterSteps.cs`, `InvoiceSubmissionAfterSteps.cs`, `InvoiceSubmissionPersister.cs`
(`TerminalFor` overload only), `SubmissionReasonCodes.cs`; tests `InvoiceSubmissionAfterStepsTests.cs`. Not touched: `SignalWriter`, the mapper,
D-122, preflight at persist.

**A. The terminal comes from the STORED state (F4-1).**
- `InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind, string?)` is the one mapping; `TerminalFor(outcome)` calls it. The after-steps
  call it with the stored findings JSON's `kind` and root `reasonCode` for the status, the reason code and the notification.
- `RunAsync(persisted, ...)` stays the first-run entry after Committed. Its `TerminalStatus` / `TerminalReasonCode` are now only checked: if
  they differ from the stored mapping the result is `Refused` with `ebilling.submission.stored_state_mismatch`
  (`SubmissionReasonCodes.StoredStateMismatch`) and nothing is written. A crash-then-resume can therefore never write Accepted beside a Rejected
  invoice and an AutoRejected record.
- `StoredFindings.Parse` also refuses (null → `findings_unreadable`) a stored `kind` that is not persistable (Accepted, AcceptedUncoded,
  AutoRejected, Blocked).
- **Totals refusal (closes section 13 open item 2).** A refused 016 calculation (`persisted.Totals.Refusal` on the first run; the resume's own
  recalculation) is written into `sprk_findingsjson` as `totalsRefusal: { code, lineId }` (code only, never the message or amounts). Idempotent:
  no write when the same code is stored. The invoice is not undone. The write moves the row version, so the terminal write re-reads first.

**Resume entry contract (part E codes against exactly this).**
`Task<SubmissionAfterStepsResult> ResumeAsync(Guid submissionId, string? correlationId, CancellationToken ct = default)`.

| Step | Behaviour |
|---|---|
| Read | the submission: `sprk_findingsjson`, `sprk_invoice`, channel, document, correlation id, `sprk_processingstatus`, `versionnumber` |
| Missing row | `SubmissionMissing` |
| Accepted / Rejected / Failed | `Completed`, `AlreadyTerminal = true`, `TerminalStatus` = the status found; nothing runs, totals included |
| Received / no status | `Refused`, `ebilling.submission.not_processing` |
| No invoice link | `Refused`, `invoice_link_mismatch` |
| Totals | `IInvoiceTotalsService.RecalculateAsync(invoiceId)`; a fault → the Signals and the record run, then `Retry` (`totals_failed`), as the first run's totals Retry; a refusal → recorded (above) |
| Then | the same steps as `RunAsync`, from the stored state; never AI, never the evaluation |

- Line ids: not needed. The rule Signals carry their subject ids in the stored plan, and the system Signals use the deterministic
  `LineIdFor(submissionId, index)` the persister created the lines with, on the first run and on a resume alike. No line query was added
  (the persister's Resumed query stays the only one).
- New constructor dependency: `IInvoiceTotalsService` (already registered). `SubmissionAfterStepsResult.AlreadyTerminal` is new.

**B. The terminal is written only while Processing (F4-2).**
- The submission read now includes `sprk_processingstatus` and `versionnumber`. Not Processing at the start → no step runs (above). Reason for
  skipping every step, not only notify and terminal: a terminal status means either every step already completed or someone else decided
  (a reviewer, the job's Failed), and re-running the system Signals after a policy change could open a new episode on a decided invoice.
- The terminal write is `IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync` with one row (the existing conditional seam on this identity;
  `UpdateIfRowVersionMatchesAsync` is the ontology writer's own connection). On `DBConcurrencyException`: re-read status and version once;
  still Processing → write once more on the new version; now terminal → `Completed`, `AlreadyTerminal`, nothing written; a second conflict →
  `Retry` (`ebilling.submission.terminal_write_conflict`), still Processing. No row version readable → `Retry` (`row_version_missing`).
- Scenario test: Blocked completes (Rejected), a reviewer sets Accepted, a re-delivery through `RunAsync` and through `ResumeAsync` completes
  with nothing written, no second notification and no step run.

**F. System-policy predicate (K1).** The policy is read (billing domain, by code), then all its versions; the version used is
`InvoiceComplianceEvaluator.SelectVersion(versions, receivedOn)`, i.e. `BillingRuleCandidate.IsInForceOn` (current from its start, a superseded
one inside `[from, to)`), the evaluator's own definition, called, not copied. It must be SubmissionIntegrity. "Current version Active" is
gone, so a re-seed after `receivedOn` no longer drops that submission's system Signals. Two reads instead of one join.

**G. `signalPlan` required.** `StoredFindings.HasSignalPlan` is false when the stored JSON has no `signalPlan` array; the run is `Refused`
with `ebilling.submission.signal_plan_missing` and nothing is written. An empty array is still a valid (empty) plan. Schema stays 1: the
persister always writes the member (its `Validate` requires an evaluation for every persisted kind), and no stored data exists yet.

**E (part).** The rejection decision id test now asserts `IdOf("ebilling-auto-reject-{S:D}")` (and that it differs from the invoice id and
another submission's), not the id against itself. The test fake now separates a findings-JSON `UpdateAsync` (`FindingsWrites`) from a status
write; the terminal is counted from the conditional write, and any unconditional status write is collected separately and asserted empty.

**Fail-before evidence.**
- Against the base sources (650109d97) the new tests do not compile: CS1061 (`ResumeAsync` x8, `HasSignalPlan`, `AlreadyTerminal` x2),
  CS0117 (`StoredStateMismatch`, `SignalPlanMissingCode`, `NotProcessingCode`, `TerminalWriteConflictCode`, `TotalsRefusalMember`), CS1729
  (the constructor with `IInvoiceTotalsService`).
- Mutations of the new code (non-constant expressions), each run on the 57 after-steps tests:

| Mutation | Tests failing |
|---|---|
| A: terminal taken from `persisted` and the mismatch check off (the old behaviour) | 2 (both stored-state mismatch cases) |
| B: status gate off (every status treated as Processing) | 6 (reviewer scenario, now-terminal conflict, 4 not-Processing cases) |
| B: no re-read after a conflict (same version retried) | 2 (still-Processing retry, now-terminal conflict) |
| F: only the current version considered | 1 (superseded-in-window) |
| G: a missing plan read as present | 1 |
| Totals refusal not recorded | 1 |
| Totals refusal written again when stored | 1 |
| E: decision id key `:N` instead of `:D` | 1 (the section 7 shape test) |

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors. Filtered unit suite 5,306 passed / 0 failed / 11 skipped
(after-steps tests 34 → 57). ArchTests 1,214 passed.

**Left for other parts.** D-122 wiring, `SignalWriter.PreflightRefusal` at persist, firmCode / invoice-number caps, permanent-refusal
classification and the Held presentation (other agents). Part E: call `ResumeAsync` for a Processing submission that links an invoice
(instead of re-running the processor and `PersistAsync`), and treat `signal_plan_missing` and `not_processing` as Refused like the other
codes. **Corrected after ontology-r1's re-check (2026-10-19):** `stored_state_mismatch` cannot occur on a legitimate path. The handler
(72a2cd64d, ~:285-288) sends the persister's Resumed result to `ResumeAsync`, so `RunAsync` is only reached on Committed, where the stored
JSON and the outcome are the same object. A mismatch therefore means a bug; the handler never calls `RunAsync` with a re-evaluated outcome.
**Corrected again (review fix R2, 14c):** that was not yet true. The persister's resume path ran the totals, and a totals fault turned the
Resumed result into a Retry that carries the invoice id, which the handler sent to `RunAsync` with this run's re-evaluated terminal. The
invariant now holds: the persister's resume path does no totals (it answers Resumed), and the handler sends every outcome in which the
submission is already linked (Resumed, or Retry with an invoice id) to `ResumeAsync`; `RunAsync` is for Committed only, a run that
committed in this delivery.
Marking such a submission Failed would strand an invoice that has no Signals or record, and nothing on the handler path resumes after Failed.

**Known limits (ontology-r1 re-check of 5a04a41fb, no change now).**
- K1: the test fake does not move the row version on a findings `UpdateAsync`, so removing `rowVersion = null` after the totals-refusal
  write survives all tests. In production that costs one extra conflict and re-read; the retry succeeds.
- K2: a failed sink delivery is logged and swallowed, the marker is not set, and the terminal is still written; that notification is never
  redelivered (matches `IEbillingNotificationSink`'s "does not retry"). Re-check the marker if task 132 makes the sink a durable enqueue.
- K3: the persister's `sprk_findingsjson` cap check does not allow for the ~100-character `totalsRefusal` member; a fault there is
  thrown and retried, not lost.
- K4: a reviewer can decide between the start read and the Signals step, and the Signals are still written on the decided invoice. The
  terminal write itself is safe (conditional on Processing).

### 13b. D-122 close method - owner review (ontology-r1)

Test-only follow-up to ontology-r1's review of `SignalWriter.CloseActedByAutomatedDecisionAsync` (landed as 650109d97). No production change: no test exposed a defect.

**Tests added** (`tests/unit/domain/Signals/SignalWriterTests.AutomatedClose.cs`):
- F3-1: the open-Signal test asserts the read's column set is exactly `sprk_signalstatus, sprk_resolutiontype, sprk_resolvedon, sprk_decisionrecord, versionnumber` (the shared fake already records the requested ColumnSet).
- F3-2: Resolved + ConditionCleared with no `sprk_resolvedon` -> LeftAsIs, no write; a concurrent run closes it with this record between the read and the write -> `AlreadyClosedByThisRecord` on the re-read; `resolvedon == setReadAt` -> Closed.
- F3-3: `org.UpdateCalls.Should().BeEmpty()` added to every no-write test (already-closed, left-as-is, person-resolves, two mismatches, fault) and the mismatch-then-close test.
- Also: the missing-row-version throw (nothing written, `signal_close_failed`) and the `DataverseAccessDenied` reason on a 0x80040220 fault.

**Mutation evidence** (each applied to `SignalWriter.cs` alone, the SignalWriterTests suite run, then restored):

| Mutation | Result |
|---|---|
| M3 null `resolvedon` treated as after the read | 1 fails: the null-resolvedon test |
| M4 `sprk_resolvedon` dropped from `AutomatedCloseColumns` | 2 fail: the open-Signal test (Open, Acknowledged) |
| M5 "already closed by this record" skipped on the re-read (`attempt == 0 &&`) | 1 fails: the concurrent-run test |
| M6 `>=` -> `>` | 1 fails: the `resolvedon == setReadAt` test |
| F3-3 an unconditional `UpdateAsync` on the LeftAsIs path | 7 fail: the left-as-is theory rows, person-resolves, null-resolvedon |

**Known limits.**
- K1: Dataverse stores `sprk_resolvedon` to the second, so a system close in the same second after the read reads as before it -> LeftAsIs. It fails safe, and matches the commit route's `SystemClosedSince`. Do not round `setReadAt` down: that would fail open.
- K4: the extra Information logs on Closed / ChangedSinceRead carry ids and the policy code only. The row-version line duplicates the one in `LinkConditionClearedRecordAsync` (~:1096); a shared helper is optional.

### 13c. D-122 wiring (rejection resolves every open Signal)

Supersedes section 7's "`ResolvedSignalIds` is empty" and section 13 open item 1. File: `InvoiceSubmissionAfterSteps.cs` only (plus its tests and the flow tests). `SignalWriter`, the recorder and the writer are unchanged.

**Order (AutoRejected only; agreed with ontology-r1, the commit route's order).**
1. Signal writes, as before. Any failure returns Retry before step 2.
2. **The set.** If the findings JSON already holds a stored set, it is used and nothing is read (the set is frozen: the record's hash covers it). Otherwise it is read once, app-only:
   - the clock (`TimeProvider`) is read first and becomes `setReadAtUtc`, so a system close after the read is at or after it (the writer's re-close rule);
   - the invoice's lines: `sprk_billingevent` where `sprk_invoice` = the invoice (the persister's resume query);
   - the Signals: one query, `sprk_regardinginvoice` = the invoice OR `sprk_regardinginvoiceline` in the lines; each row's dedupe key is parsed (`SignalWriter.TryParseDedupeKey`), and the subject must be the invoice or one of its lines (a row that does not parse, or names another subject, is skipped: it fails closed);
   - kept: **every** row whose status is Open or Acknowledged, ordered by id so the record's hash is stable (ontology review K1). It is NOT the latest episode per (policy, subject): two open episodes of one (policy, subject) are both listed and both closed, and an open earlier episode under a resolved later one is listed too. This matches D-122's "every open Signal". Never the write results (a refresh or skip returns no id).
   - Both queries (the lines, the Signals) are single `RetrieveMultiple` calls with no paging, which is fine below 5,000 lines or Signals per invoice (ontology review K2; a code comment sits at each query).
   - The set is written into `sprk_findingsjson` BEFORE the record. The write moves the row version, so the terminal write re-reads it.
3. **The record**, `ResolvedSignalIds` = the stored set's ids.
4. **The closes**: `SignalWriter.CloseActedByAutomatedDecisionAsync(signalId, recordId, setReadAtUtc, policyCode)` for each Signal in the stored set, with the STORED `setReadAtUtc` (a resume does not use its own clock).
5. Indexing (not for a rejection), notify, terminal status last, as before.

Accepted, AcceptedUncoded and Blocked read no set, store none and close nothing. `ResumeAsync` runs the same steps from the stored state.

**Stored shape** (findings JSON member `resolvedSignalSet`):
```json
{ "setReadAtUtc": "2026-10-19T09:00:00.0000000Z",
  "signals": [ { "id": "<signal id>", "policyCode": "BILL-AGE" } ] }
```
A member that is present but unreadable makes the whole findings JSON unreadable (`findings_unreadable`, Refused); it is never silently re-read.

**"Already Acted by this record".** Because the set is stored before the record exists, a fresh read never happens after a close by this record: a resume after a partial close finds the stored set and re-closes from it (`AlreadyClosedByThisRecord` for the closed ones, no second write). The fresh read therefore needs no "this record" clause, and the record id (assigned at create, not deterministic) is not known at read time anyway.

**No pre-D-122 fallback (removed; ontology review (c)).** No record written before D-122 can exist: part D has never been deployed or pushed. The earlier `recordPredatesSet` path (re-recording with an empty list on `review_invalid`) is deleted. `review_invalid` is a **permanent Refused** after exactly one record call, because it means a bug (a record exists under this decision id with other content, or the request is invalid). Part E then leaves the submission Processing and Poisoned. The writer's order is why no wrong record can follow: it looks up the key first, so an existing record under other content is refused before anything is created.

**A listed Signal that no longer exists (ontology review (a): shrink and retry once).**
- Before the store: impossible by construction (the set is read from existing rows).
- After the store and before the record: the writer refuses the record with `owner_refused`. The after-steps then read the set's ids (one query by `sprk_signalid`). If none is missing, `owner_refused` stands as before (one record call, no retry). If some are missing:
  1. ONLY the missing ids are removed; `setReadAtUtc` is kept;
  2. the shrunk set is stored again (`StoreResolvedSetAsync`, the same findings write as the first store; the stored set is frozen again);
  3. the record is tried ONCE more with the shrunk set in the same run;
  4. if that call is refused too, the refusal is classified as any refusal (no third call, no further shrink).
- Why no wrong record: the writer looks up the key before it checks the Signals, so `owner_refused` proves no record exists under the key at that moment. If a record appears under other content before the retry, the shrunk call gets `review_invalid` and falls to the permanent Refused above.
- The code `ebilling.submission.resolved_signal_missing` is removed (nothing used it any more).
- A Signal the plan writes again after the delete is a new row, not in the stored set; it stays open and the next evaluation owns it.
- After the record (deleted before its close): the close's read fails with ObjectDoesNotExist. That Signal has nothing to close: one Warning, the run continues. The record still lists it, which is true at decision time. **Ontology confirmed this (b)**: a Signal deleted after the record gets a Warning and the run continues.
- Ontology review K3 (no code change): ontology-domain Signals regarding the invoice or a line are also closed as Acted by this record. That is correct under D-122's "every open Signal".

**Close outcomes.**

| Outcome | Handling |
|---|---|
| Closed, AlreadyClosedByThisRecord | Done |
| LeftAsIs | Stays listed in the record; one Information line (submission, Signal and record ids, policy code) |
| ChangedSinceRead | Every Signal is still attempted, then Retry `ebilling.submission.signal_changed`; the submission stays Processing; nothing notified, no terminal |
| ObjectDoesNotExist | One Warning, continue (above) |
| Any other exception | Propagates, as a Signal write fault does (part E: transient, stays Processing) |

**Seams and census.** The internal test constructor gains `closeActed` (production: `SignalWriter.CloseActedByAutomatedDecisionAsync`) and an optional `TimeProvider` (production: `TimeProvider.System`). The public constructor's signature is unchanged, so DI and the ADR-010 ceiling are unchanged; no ArchTest census changed.

**Tests.** `InvoiceSubmissionAfterStepsTests` (+10): every open Signal on the invoice and its lines (written now, an earlier episode's, Acknowledged; resolved and other-invoice ones untouched); Accepted and Blocked close nothing; the set stored before the record with a crash between (ResumeAsync uses the stored set, no fresh read, the stored read time); a Signal opened after the read stays open; a resume after a partial close (no double write); ChangedSinceRead → Retry, then completes; LeftAsIs stays listed with one Information line; (follow-up: review_invalid is Refused after one record call; a missing listed Signal shrinks the set and retries once, with the read time kept; owner_refused with nothing missing stands, and a refused retry is final; a record under other content gives review_invalid on the shrunk call; K1 lists every open episode); a not-found close skipped and a transient close fault propagated. `InvoiceSubmissionProcessingFlowTests`: the rejected SPA flow closes every Signal with the one record and stores the set; the blocked flow closes nothing.

**Fail-before evidence.**
- Against the base sources (0307881b5) the new tests do not compile: CS0426 (`ResolvedSignalSet`).
- Mutations (non-constant expressions), each run on the after-steps and flow tests (81):

| Mutation | Tests failing |
|---|---|
| M1 the close step gets an empty set | 7 (the flow rejection, every-open-Signal, stored-before-record, partial close, LeftAsIs, close faults, ChangedSinceRead) |
| M2 a stored set ignored (re-read every run) | 10 (the 13c resume tests and the 13a / part D crash-resume tests) |
| M3 the set not stored before the record | 13 |
| M4 ChangedSinceRead ignored | 1 |
| M5 resolved Signals listed (status filter off) | 1 |
| M6 Acknowledged not treated as open | 1 |
| M7 earliest episode per key instead of latest | 1 |
| M8 pre-D-122 fallback off | 1 |
| M9 the fallback keeps the read set (closes Signals the record does not list) | 1 |
| M10 missing-Signal check off | 1 |
| M11 a not-found close propagates | 1 |
| M12 LeftAsIs not logged | 1 |
| M13 the close uses the run's clock, not the stored read time | 1 |
| M14 the record lists no Signal | 5 |
| M15 the read time taken after the read | 1 |
| M16 `resolved_signal_missing` treated as transient | 1 |

All 16 killed; the source was restored byte-identical after the run.

**Follow-up (ontology review (a), (c), K1, K2): what changed in the tests.** After-steps suite 67 → 70 (two removed: the pre-D-122 record and the refuse-on-missing test; five added). Mutants M7 (earliest episode), M8, M9 (the fallback), M10 and M16 (the missing code) no longer apply. Mutations (non-constant expressions), run on the after-steps suite (70; M5 and M6 on all 416 `InvoiceSubmission*` tests), the source restored byte-identical after each:

| Mutation | Tests failing |
|---|---|
| (a) a missing listed Signal refuses again instead of shrinking (`missing.Count == 0 \|\| set.Signals.Count > 0`) | 3 (shrink and retry; owner_refused stands / refused retry final; record exists under other content) |
| (c) the fallback re-added (`review_invalid` re-records with the empty list) | 2 (review_invalid after one record call; the part D permanent-refusal test) |
| K1 latest episode per (policy, subject) decides again | 1 (every open Signal is listed) |
| M5 again: resolved Signals listed | 2 (every-open-Signal; K1) |
| M6 again: Acknowledged not treated as open | 2 (every-open-Signal; K1) |

Fail-before for the new tests is these kills: each new test fails against the mutant that restores the old behaviour.

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors. Broad filtered unit run 8,182 passed / 0 failed / 11 skipped (after-steps 57 → 67; after-steps + flow 81). ArchTests 1,214 passed, unchanged. After the follow-up: build 0 / 0; broad filtered run 8,194 passed / 0 failed / 11 skipped (after-steps 70; `InvoiceSubmission*` 416); ArchTests 1,214 passed.

**Open questions (closed by the ontology review).**
1. Missing listed Signal after the store: shrink and retry once (a). Done.
2. A listed Signal deleted after the record is skipped with a Warning rather than retried forever (b). Confirmed by ontology-r1.

### 13d. F1-3: caps, preflight at persist, permanent refusals

Ontology-r1's part D finding F1-3: a deterministic Signal-write refusal was retried until the handler gave up, and because the Signals step runs before the Decision Record, an AutoRejected invoice ended with no record (074 K-3). Live vector: Signal sentences quoted raw firm text. Commits 082c1a49d (caps), 2baff05be (preflight at persist), bd76c2033 (permanent refusals), c2e1dff14 (item 5: ontology's F2 re-check). `SignalWriter` has one change, item 5 (its `CoreAncestorStatus.Error` case), made at ontology-r1's request and owner-reviewed by ontology-r1; the rest of this section needed no writer change.

**1. Caps at the source.** One definition per shape, in `BillingRuleCatalogue` beside `IsCodeShaped`: `IsInvoiceNumberShaped` (`^[A-Za-z0-9][A-Za-z0-9._/#-]{0,39}\z`), `IsNameShaped` (`^\p{L}[\p{L}\p{M} .,'’-]{0,99}\z`), and `IsCitable(token, value)`, the one map from a template fact to its shape. A value out of shape is left out, never truncated.

| Fact | Source | Guard | Where |
|---|---|---|---|
| `line.firmCode` (system) | the code the firm billed | `IsCodeShaped`; otherwise reason `coding.firm_code_mismatch_not_shown` (own template, headline "Unrecognised billed code differs from the suggested code"), no firm-code fact or value read | `BillingSystemFindings.FirmCodeMismatch` |
| `invoice.invoiceNumber` (system, D-91 read) | extracted | `IsInvoiceNumberShaped`; otherwise reason `submission.invoice_number_mismatch_not_shown` (cites the typed number only; headline "Invoice number read differs from the submission"); the read stays as evidence (field, confidence) with no text | `BillingSystemFindings.InvoiceNumberMismatch` |
| `line.refusedCode` (system) | model output | `IsCodeShaped` (D-125 review (b), unchanged) | `CodeRefused` |
| `line.expenseCode` (ExpenseRule) | billed code, else AI-assigned | `IsCodeShaped` | mapper `MessageOf` |
| `line.activityCode`, `line.taskCode` (NonBillableActivity) | extracted / AI-assigned | `IsCodeShaped` | mapper |
| `invoice.firmInvoiceNumber` (Duplicate) | the submission's firm invoice number | `IsInvoiceNumberShaped` | mapper |
| `line.timekeeperName` (ApprovedTimekeeper, ApprovedRate, TimeIncrement, DailyHoursCap, NonBillableActivity, Duplicate) | extracted | `IsNameShaped` | mapper |

- The mapper filters the facts BEFORE choosing a message, so a version template that names a dropped token no longer renders and the existing fallback (default wording, then headline only) is used: the finding still holds. The value-read evidence text is dropped by the same rule (the evidence line keeps field and confidence).
- The two variants are flag-only system reasons; the D-91 variant keeps the mismatch's primary-reason precedence (D-125 review (a)).
- Not guarded (not firm text, or bounded): numbers, dates and money (formatted by `BillingText`), rule parameters (`rule.*`, `observation`), `missingField`, `duplicateOf.trackingNumber`, `coreRecord.*`, `codeSet.name` (configuration), `line.suggestedCode` (a code of the line's code set), `submission.invoiceNumber` (the submitter's typed number: at most 89 characters, `TrackingNumberAllocator.MaxFirmInvoiceNumberLength`, checked at submission).

**2. Preflight at persist.** `InvoiceSubmissionPersister.SignalPlanJson` runs `SignalWriter.PreflightRefusal` on every mapped entry before rendering it. A refused entry is left out of the plan; the rest is kept. `BuildFindingsJson` records the refused ones as `signalPlanRefusals: [{ policyCode, subject: { entity, id }, code }]` (no values; present only when an entry was refused), and `PersistAsync` logs them once at Warning (policy and refusal codes only). The finding itself stays in `findings`.

**3. Permanent vs transient at write time** (`InvoiceSubmissionAfterSteps.PermanentWriteRefusal`, `PermanentSkipCodes`).

| Permanent (recorded, the run continues) | Code recorded |
|---|---|
| `ArgumentException` | `SignalWriter.PreflightRefusal(request)` (e.g. `sentence_too_long`, `headline_too_long`), else `request_refused` |
| `SignalSentenceTemplateException` | the preflight code, else `sentence_template_invalid` |
| `SignalWriterEscalationException` with no inner exception, or an `ArgumentException` inner (unverified subject, core record unresolved because the row is gone or ambiguous / not cataloged, matter lookup empty, owner mismatches on create or reconcile, no-core owner refused, a regarding pair refused by an argument check) | its `Reason` |
| a deterministic privilege refusal (`SignalWriter.IsDeterministicPrivilegeRefusal`: 403 / 0x80040220 / 0x80040299, not wrapped in an escalation) | `dataverse_access_denied` |
| skipped with `core_record_changed`, or with the ownership resolver's `record_owner_parent_unresolved`, `_secure_parent_not_isolated`, `_secure_team_unresolved`, `_secure_bu_ambiguous`, `_no_default_team`, `_acting_user_unresolved`, `_no_source`, `_record_missing` | the skip code |

Transient (the exception propagates or the step returns Retry): an escalation that wraps a read or resolver fault (`owner_read_back_failed`, a catalog or no-core resolver fault, and since item 5 `core_record_unresolved` when the core-ancestor derivation FAULTED: the writer's refusal of an unknown state, as `IsDeterministicPrivilegeRefusal` treats it); a skip with `record_owner_parent_undetermined` (a derivation that failed) or any unlisted code; every other exception.

A permanent refusal: one Error log (submission id, policy code, subject entity and id, code); recorded in the findings JSON as `signalWriteRefusals: [{ policyCode, subject: { entity, id }, code }]`, merged by (policy, subject, code) so a resume writes nothing new; counted (`SubmissionAfterStepsResult.SignalsRefused`, new); then the remaining Signals, the Decision Record (whose set is read after the Signals step, so a refused Signal is simply not in it), indexing, notify and the terminal status run as before. The refusals write moves the row version, so the terminal write re-reads it.

**4. One failure never stops the others.** A permanent refusal is caught per Signal and the loop continues. A retryable failure code (`signal_write_skipped`, `signal_changed`) was already remembered while the loop went on; the loop comment that said "the rest are still written" is corrected to say exactly that. An exception that is not permanent still propagates out of the loop (unchanged; the job retries and every write is idempotent).

**5. A transient core-ancestor fault is not permanent (ontology-r1's F1-3 re-check, F2; `SignalWriter.ResolveCoreAsync`).** The writer's `CoreAncestorStatus.Error` case threw `SignalWriterEscalationException(core_record_unresolved)` with no inner exception for EVERY error, including the resolver's transient `Failed()` (`IsPermanent` false, `RefusalKind.Transient`: the column probe, the row read). The classifier in item 3 reads "no inner exception" as permanent, so after a 503 or a throttle on the line read the Signal was recorded as refused and never existed. Fix, in the writer only: when the resolver result is not `IsPermanent`, the escalation wraps the fault (`new InvalidOperationException(result.Error)`); a permanent Error (the row is gone, `NotFound`; two roots, `MultipleRoots`, which an invoice-family subject already maps to `core_record_ambiguous`) keeps no inner exception, as before. The `Reason`, the log and the metric are the same either way, and the classifier is unchanged. The other consumers catch by type and read `Reason`: `SignalEvaluator` (the `DeriveCoreRecordAsync` catch, ~:275), `SignalReevaluationJob` (~:907), and `WriteAsync`'s catch (logs, meters, rethrows with `throw;`). None reads `InnerException`, so their behaviour is unchanged.

Tests (fail-before: the original source fails 5 of 7; the two permanent cases are must-not-fire and pass either way): in `InvoiceSubmissionAfterStepsTests`, over the REAL `SignalWriter` and the REAL `CoreAncestorResolver` (their column probe and row read are the test's): a column-probe fault and a row-read fault (HTTP 503) each propagate out of `RunAsync` as the escalation with an inner exception, `PermanentWriteRefusal` returns null for it, and there is no `signalWriteRefusals` entry, no Decision Record and no terminal write, the row still Processing (the run does not return a Retry result: a writer exception has always propagated out of the loop so the job retries, see item 4); a line that is gone (0x80040217) is recorded as `core_record_unresolved`, the run completes with its record and Rejected terminal. In `SignalWriterTests.CoreAncestorFault.cs` (new partial): the inner exception is non-null for the row-read fault and the column-probe fault and null for the NotFound refusal, on `WriteAsync` and on `DeriveCoreRecordAsync` (the planning path). Mutation (a non-constant expression, `result.IsPermanent || result.Stamps.Count == 0`, true for every Error): the same 5 tests fail; the source was restored byte-identical (SHA-256 checked). Verification after item 5: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors; the broad filtered unit run 8,257 passed / 0 failed / 11 skipped; `Spaarke.ArchTests` 1,214 passed.

**6. Surfaced to the customer admin (D-133, option A).** A refusal (a plan entry left out at persist, item 2, or a permanent write refusal, item 3) also sets the new text column `sprk_invoicesubmission.sprk_adminattention` ("Needs Admin Attention", Text 500), and the admin view "Submissions needing attention" (public, on `sprk_invoicesubmission`) lists every submission with it set or with processing status Failed.

| Part | Behaviour |
|---|---|
| Plan refusals | `InvoiceSubmissionPersister` passes the text to `BuildSubmissionUpdate`, so it is written in the SAME transaction update as `sprk_findingsjson` (with `signalPlanRefusals`). Nothing refused: the column is not in the update (never written null or empty). |
| Write refusals | `InvoiceSubmissionAfterSteps.RecordSignalWriteRefusalsAsync` adds the column to the SAME update (a plain `UpdateAsync`; findings writes carry no row-version guard) that stores `signalWriteRefusals`. A resume that adds nothing returns before the update, so nothing is written and nothing is repeated. Since R3/R5 (14c) the column is MERGED into its current value, not overwritten. |
| Text | `AdminAttentionText` (new, internal static; a pure function of the refusals stored in the findings JSON: plan refusals first, then write refusals, each distinct entry once, in stored order). `"{n} Signal(s) could not be created: {code} ({policy} on line|invoice {first 8 characters of the id}), ..."`. Codes and an id prefix only, never an extracted value. At most 500 characters (`InvoiceSubmissionPersister.AdminAttentionMaxLength`): entries that do not fit give way to `, ... (+N more)` with a real ellipsis character, whole entries only. |
| Clearing | Code does not clear it, except that since R7 (14c) the after-steps remove the "Processing stopped" segment when they write the terminal; it never writes the Signal segment again once the refusals are stored. The administrator clears it after review; a later resume does not bring it back (the idempotent resume writes nothing). |
| Detail | The full detail stays in `sprk_findingsjson` (`signalPlanRefusals`, `signalWriteRefusals`); the Error and Warning logs are unchanged. |
| Schema | `scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1` creates the column and the view (idempotent, `-WhatIf`, entity-scoped publish). It must run on dev BEFORE the BFF that writes the column is deployed; a submission update that names a missing column would fail. |
| Data model | `docs/data-model/sprk_invoicesubmission.md` (column row, Views section). |

Section 11. **Existing:** `sprk_outcomereasoncode` covers only the Failed / Rejected terminal reason, and `sprk_backgroundjobrun` is unused and for scheduled jobs. **Extension:** the existing submission table is extended by one column and one view; no new table, service, interface, registration or package (`AdminAttentionText` is an internal static formatter). **Cost of doing nothing:** an AutoRejected invoice ends with no Signal and the only trace is an Error log the admin never sees. A platform-wide process-error table (option B) is a future platform proposal, recorded in the decisions log (D-133); an App Insights-only trace (option C) was rejected because customer admins never see it.

Tests (fail-before, each mutation applied with a non-constant expression and the source restored byte-identical): `InvoiceSubmissionPersisterTests` +2 (the refused plan entry sets the column in the same submission update as the findings; nothing refused leaves it out), `InvoiceSubmissionAfterStepsTests` +5 (the write refusal sets it in the same update as the record of the refusal; a resume writes nothing new and an admin's clear stands; plan and write refusals together; 41 refusals capped at 500 with the count; a run with no refusal never touches it), `AdminAttentionTextTests` (new, 8: none gives null, one reads plainly, order and de-duplication, same refusals same text, the cap at three sizes, both findings members).

**Tests.** `BillingCitableFactTests` (new): the two shapes; the read invoice number and the firm code cited in shape, and an injected phrase, a spaced value and a 3,000-character value not cited (variant reason, sentence and facts without the value, evidence without its text, `PreflightRefusal` null); each echoing rule fact (expense, activity, task code, timekeeper name, firm invoice number) cited in shape and not cited out of shape (injected, 3,000 characters, both), the finding still mapped; must-not-fire for other facts. `BillingSystemPolicyTests`: the two variants join the system-reason theories. `InvoiceSubmissionPersisterTests` (+3): the refused entry dropped and recorded, the rest kept; no member when nothing is refused; `PersistAsync` commits and warns once with codes only. `InvoiceSubmissionAfterStepsTests` (+14): each permanent class (7) completes an AutoRejected run with one record, Rejected terminal, the following system Signal written, one recorded refusal and one Error log; three transient faults still propagate with no record or terminal; `record_owner_parent_undetermined` still retries; idempotent recording across a crash and `ResumeAsync`; a Blocked run completes; and one test over the REAL `SignalWriter` (its Dataverse dependencies all throw): (a) the persisted plan no longer holds the 2,100-character entry and the record happens, (b) a plan stored before the preflight is refused by the real writer before any I/O (`sentence_too_long`), recorded, and the record and terminal still happen.

**Fail-before evidence.** Mutations (non-constant expressions), each applied, the named suite run, the source restored byte-identical:

| Mutation | Suite | Failing |
|---|---|---|
| M1 read number always cited | Billing (1,550) | 5 |
| M2 firm code always cited | Billing | 3 |
| M3 `IsCitable` always true | Billing | 5 (every rule-fact out-of-shape row) |
| M4 no preflight at persist | InvoiceSubmission (416) | 2 |
| M5 no Warning at persist | InvoiceSubmission | 1 |
| M6 no exception classified permanent (the old behaviour) | InvoiceSubmission (430) | 8 |
| M7 permanent skip codes ignored | InvoiceSubmission | 2 |
| M8 every escalation permanent | InvoiceSubmission | 2 (read-back, catalog fault) |
| M9 refusals not recorded | InvoiceSubmission | 9 |
| M10 recording not idempotent | InvoiceSubmission | 1 |
| M11 privilege refusal not permanent | InvoiceSubmission | 2 |

All 11 killed.

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors. Broad filtered unit run 8,247 passed / 0 failed / 11 skipped. `Spaarke.ArchTests` 1,214 passed. No new interface, registration or package; the ADR-010 census is unchanged.

**Known limits.**
- K1: the classifier treats any inner exception on an escalation as "the writer met a fault, retry". `TodoRegardingBuilder` throws `InvalidOperationException` for some deterministic states (lines ~220, ~307); the writer wraps it as `subject_pair_unresolved`, and because the Dataverse client also throws `InvalidOperationException` for I/O failures, that inner is classified transient. Such a Signal is still retried until the handler gives up. Since item 5 the writer sets an inner exception in one more place (a core-ancestor fault, an `InvalidOperationException` carrying the resolver's message), and that one is a true fault; K1's direction is unchanged: when in doubt, retry, never drop. `SignalWriter` is no longer untouched by this section (item 5), so a new deterministic state wrapped the same way would be retried too.
- K2: a transient exception still aborts the loop; a permanent refusal met earlier in the same run is recorded on the next run (it recurs deterministically), not on this one.
- K3: the evidence lines of a stored plan written before this change keep the raw value text; `sprk_evidencerefs` is not length-checked by the writer's preflight.
- K4: the name shape cannot tell a crafted phrase written in name characters (letters, spaces, `. , ' -`, at most 100) from a name; it is shown as the timekeeper name. The length problem is closed.
- K5: `docs/data-model/sprk_ruleset.md` lists the system reasons and does not name the two `_not_shown` variants yet.
- K6: `RecordSignalWriteRefusalsAsync` does not re-check the `sprk_findingsjson` cap when it adds `signalWriteRefusals`, the same as the totals-refusal and set writes (compare 13a K3). It matters only if the JSON is already near 1 MiB.

**Open questions.**
1. The brief named "unresolved subject pair" as permanent; the writer's own contract (`IsDeterministicPrivilegeRefusal` remarks) says an escalation wrapping a fault is retryable. Implemented: permanent only when the escalation wraps no fault or an argument error (K1). Confirm, or name the inner types to treat as permanent.
2. (judgement call (ii)) The owner-refusal skip codes are treated as permanent within a job's retry window. The premise recorded here earlier, that a refused Signal is raised later once the configuration is fixed, was FALSE (ontology-r1's correction): `PolicyScopeResolver` loads only Ontology-domain policies, and `BillingFindingSignalMapper.MapAll` runs only from submission processing, so a billing Signal refused for ownership is NEVER raised again. A configuration fix (a default team added) will not bring it back. The finding stays in `findings` and the refusal stays in `signalWriteRefusals`. An AutoRejected invoice can therefore end Rejected with no Signal. Keeping the refusal permanent is still the right call (a retry cannot help the invoice). **Decided: D-133 (owner, 2026-10-19), option A: it is surfaced to the customer administrator in Dataverse (item 6 below).**
   - Ownership was already checked twice before any Signal exists: the submission service and the persister both resolve the owner from the same core record. A writer ownership refusal therefore needs a change or delete race in the seconds between those checks and the write (the core record's parent, team or business unit changed or was removed), so it is rare; it is still surfaced, because the consequence (an invoice with no Signal) is silent otherwise.
   - Nuance: `record_owner_secure_parent_not_isolated` (the ownership skip) and `secure_owner_mismatch` (`OntologyWriterFailureReason.SecureOwnerMismatch`, the writer's escalation) are fixed by the secure-child reconciler, so they are "permanent" only within the job's retry window: the same Signal would be writable after the reconciler runs, but this run no longer waits for it.

#### D-133 live run on dev (2026-10-19, owner-approved)

Before and after notices went to ontology-r1 and uac-r2; both had no objection. uac-r2's condition holds: the view is a plain public savedquery, so it runs under the viewer's own read access.

1. `pwsh scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1 -WhatIf`, then the same command without `-WhatIf`. Result: CREATED `sprk_invoicesubmission.sprk_adminattention` (String 500) and the public view 'Submissions needing attention' `a371c4e6-32cc-f111-a05b-7c1e520a989f`. Only `sprk_invoicesubmission` was published. Exit 0.
2. `pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -WhatIf` changed only the `sprk_invoicesubmission` main form; every other form, view and sitemap was unchanged. The live run then rebuilt main form 'Information' `c412f942-875e-45c4-9ea1-cae75a81f1b9`. The publish was entity-scoped (sprk_invoicesubmission; never PublishAllXml), the read-back showed nothing pending, and the script exited 0.
3. Still to do: the manual step of adding `sprk_invoicesubmission` to the Matter Management app (already a queued owner action, 'add the 7 tables to the apps'). Until then the view is reachable only from the table itself. The SpaarkeMaster export of the column, view and form is also still to do.

### 13e. Held presentation

Ontology-r1's part D item: a Blocked submission (the rejection rule would reject, but an Internal submission is held for review instead) showed the same Signal as an automatic rejection. Files: `BillingFindingSignalMapper.cs` (the presentation), `InvoiceSubmissionPersister.cs` (passes the outcome; one argument), `BillingRuleCatalogue.cs` (a comment). `InvoiceSubmissionAfterSteps`, `SignalWriter` and `SignalWriterTests*` are unchanged.

**Behaviour.** `BillingFindingSignalMapper.MapAll(..., decisions, outcome)` takes the outcome kind as part of its decision input (new optional last parameter `SubmissionEvaluationKind? outcome`). For `Blocked`, a group that holds a deciding finding (its group decision was `Rejects`) becomes group decision `Blocked`:

| Part of the Signal | Value |
|---|---|
| Headline (`sprk_name`) | `Invoice held for review` (`HeldHeadline`) |
| Sentence | `The rejection rule would reject this invoice: <deciding reason>` (`HeldSentenceTemplate`; the reason is the fact `decidingReason`) |
| `<deciding reason>` | the deciding (primary) finding's existing rendered reason, the text its own `billing.reason` evidence line carries (`ReasonText`); never its code |
| Evidence | one `billing.decision` line, `Ref` = `Held`, `Text` = `ebilling.submission.blocked`; the other lines (fingerprint, one reason line per reason, values read) are as before |
| Lane, severity, fingerprint, reason codes, consequence | unchanged (the policy's; the lifecycle fingerprint does not depend on the decision) |
| Message source / group decision | `BillingMessageSource.HeldForReview` / `BillingGroupDecision.Blocked` (stored in the plan as `"Blocked"`) |

- Not touched under a Blocked outcome: a group 073 held throughout (still `Held`, with its hold-reason text) and a non-Reject group.
- The after-steps do nothing special: the persister passes `outcome.Kind`, stores the mapper's output in the plan, and part D writes the stored plan as it is.
- A `Blocked` outcome with no decisions is refused (`ArgumentException`), so it can never silently show as a rejection.
- Withheld Duplicate (D-112): the reason is the token-free withheld text; no value reaches the sentence.

**Length limits.**
- The headline is a constant of 23 characters (limit 200).
- The sentence is checked by the writer's own I/O-free checks (`SignalWriter.PreflightRefusal`, the same ones the persisted plan is held to; notes 13d part 2). If the held sentence is refused (it would pass 2,000 characters, or its template would not render), the Signal gets `HeldSentenceNoValue`, `The rejection rule would reject this invoice.`, with the headline and the Held line unchanged. The reason is left out whole, never cut; it stays in its `billing.reason` evidence line.
- Boundary tests: a reason of exactly 1,954 characters (sentence 2,000) is cited; 1,955 falls back. In both cases the plain rejection sentence of the same reason is within the limit, so the persister keeps the Signal rather than recording a `signalPlanRefusals` entry.

**K4 (the planner): what the stored plan holds for Blocked.**
- The note's K4 in 13a is the reviewer race, not the planner; I read "the planner" as the stored `signalPlan` (`SignalPlanJson`). Owner to confirm.
- One entry per (policy, subject) group, as before. For a deciding group: `headline` = the held headline; `template` = the held template; `facts` = the finding's facts plus `decidingReason` (the rendered text, as a string); `sentence` = the rendered held sentence; `evidence` as in the table; `decision` = `Blocked`; `consequence` = `Reject`; `fingerprint` unchanged.
- A group 073 held keeps `decision` = `Held`; a Flag group keeps `NotRejecting`.
- With the no-value fallback: `template` = `HeldSentenceNoValue`, `facts` = the finding's own facts, no `decidingReason`.
- A stored plan from before this change keeps the rejection wording. Nothing is deployed, so no such plan exists.
- Several deciding groups on one invoice each get the held headline and sentence with their own reason (one Signal per policy and subject, as for a rejection). Owner to confirm that is wanted rather than one invoice-level Signal.

**The Held reason is stripped on read (actual behaviour).**
- The writer stores the evidence array as written, including `text`. The Signal read route (`SignalReadService.ApplyWitnessRuleAsync`, D-123 / D-142) keeps `text` and `source` on a line only when the line's `ref` names a witness row (`{table}:{id}` of a secure-family child, `SignalSuppressionService.WitnessOf`) that the caller can read in the Signal's own family.
- `Held` is not such a reference, so on read the line comes back as `{ kind: "billing.decision", ref: "Held", tier: "Fact", withheld: true }` with no `text`. The code `ebilling.submission.blocked` is stored but not returned. The same holds for the existing 073 hold-reason text and for every `billing.reason` / `billing.field` line.
- What a reader does get: the headline, the sentence (with the deciding reason; `sprk_sentence` is not stripped) and the presence of the Held line. That is why the reason is in the sentence and not only in evidence.
- I did not change the read path (`SignalReadService` belongs to the ontology work). If the code must reach a reader, the line needs a different carrier (for example the code as the `ref`).

**K2: raw codes for screens 130 and 133 (a later UI task, not done here).**
- Both screens read the stored `sprk_findingsjson`. For a Blocked submission they will receive codes, not text:
  - root `kind` = `Blocked`, root `reasonCode` = `ebilling.submission.blocked`;
  - `findings[].reasonCode` (rule reason codes, for example `invoice_age.too_old`), `findings[].rejects` and `findings[].heldBecause` (hold codes such as `read_field_below_threshold`);
  - `pipelineFindings[]` and `codingFindings[]` codes;
  - `signalPlan[]` with the rendered `headline` and `sentence` (text), `decision` = `Blocked`, `reasonCodes[]`, and the Held evidence line with the blocked code.
- The submission row itself is Rejected with `sprk_outcomereasoncode` = `ebilling.submission.blocked`, and the invoice is In Review. Task 130's own rule ("an invoice still In Review projects status and billed total only") means a firm gets none of this; the internal screens do.
- The code-to-wording map (reason code, hold reason, blocked code) belongs to that UI task. Use the plan's `headline` / `sentence` where text is wanted; they are already firm-safe by F1-3.

**CODE_PATTERN decision.**
- `BillingRuleCatalogue.CodePattern` (read from the ExpenseRule schema's `$defs.code.pattern`) is `^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$`. The NonBillableActivity schema and `src/solutions/RuleReview/src/services/ruleTypes.ts:63` (`CODE_PATTERN`) hold the same string. They already agree, so **no TypeScript change** and no RuleReview typecheck was needed. In JavaScript, as in JSON Schema, `$` without the `m` flag matches only at the very end, which is what the schema means; only .NET's `$` also matches before a final newline, which `IsCodeShaped` already guards.
- The server schema is the enforced one (the rule body validator); the client copy only gives an administrator the same answer before a save. The C# comment claimed "pinned equal by a test" but only the two schemas were pinned. The comment is now accurate and the missing pin is added.
- New `BillingCodePatternTests` (15 cases): the server string is pinned to a literal; both schemas carry it; the `ruleTypes.ts` literal (read from its source) equals it with no flags; `IsCodeShaped` accepts and refuses (20/21 characters, trailing newline, injected text).

**Docs.** `docs/data-model/sprk_ruleset.md` lists the two F1-3 reasons, `coding.firm_code_mismatch_not_shown` and `submission.invoice_number_mismatch_not_shown`. The doc lists no sentence templates, so the Held text is not added there.

**Tests.**
- `BillingFindingSignalMapperHeldTests` (8): the presentation; must-not-fire for AutoRejected, no outcome, Accepted and AcceptedUncoded; a group 073 held and a Flag group untouched; the deciding reason (not another reason) in the sentence; withheld Duplicate; the length boundary and the no-value fallback; the headline limit; the Blocked-without-decisions refusal.
- `InvoiceSubmissionPersisterTests` (+2): the stored Blocked plan entry (and the AutoRejected control); the overflow case keeps its Signal with the no-value wording and records no refusal.
- `InvoiceSubmissionAfterStepsTests` (+1, REAL `SignalWriter` over the writer test harness's Dataverse doubles, not the fake writer): a Blocked submission creates one `sprk_signal` row with `sprk_name` = `Invoice held for review`, `sprk_sentence` starting `The rejection rule would reject this invoice: ` followed by the reason line's own text (no raw code), and `sprk_evidencerefs` holding the `billing.decision` / `Held` line with `ebilling.submission.blocked`; no record, no close, indexed, terminal Rejected.
- Two existing after-steps tests changed because the behaviour they pinned is the one that changed on purpose: `Blocked_HeldSignal_Indexed_NotifiedRejectedBlocked` asserted "no Held line" for a Blocked Signal and now asserts the held headline and the blocked Held line; `AHeldPlanEntry_IsWrittenWithItsHeldLine` built its Held entry on a Blocked outcome (which now carries its own Held line) and uses an AutoRejected outcome instead (same intent: a stored Held entry is written with its text).

**Fail-before evidence.** Mutations (non-constant expressions), each applied, the 26 new tests run (8 + 2 + 1 + 15), the source restored from git after each:

| Mutation | New tests failing |
|---|---|
| Mapper ignores the Blocked outcome (`blocked: ... && groups.Count < 0`) | 7 (4 mapper, 2 persister, the real-writer test) |
| Persister does not pass the outcome (`invoiceId == Guid.Empty ? outcome.Kind : null`) | 3 (2 persister, the real-writer test) |
| No no-value fallback (`PreflightRefusal(...) is null \|\| facts.Count >= 0`) | 2 (boundary, persister overflow) |
| Raw reason code in the sentence (`facts.Count >= 0 ? ReasonCode : ReasonText(...)`) | 7 |
| Held line text = the hold reason instead of the blocked code | 5 |
| Headline not replaced (`held && headline.Length < 0`) | 5 |
| Blocked-without-decisions guard off | 1 |
| Held applied to every decided group, not only deciding ones | 1 |
| `ruleTypes.ts` `{0,19}` changed to `{0,20}` | 1 (the client-pattern test) |
| ExpenseRule schema `{0,19}` changed to `{0,20}` | 3 of the pattern tests (the pre-existing `TheCodePattern_IsTheSchemasDefinition` fails too) |

All killed.

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors. Filtered unit run 8,276 passed / 0 failed / 11 skipped. `InvoiceSubmission*` 436 passed. `Spaarke.ArchTests` 1,214 passed. No new interface, registration or package; the ADR-010 census is unchanged. No TypeScript was touched.

**Open questions.**
1. Which note is "K4 (the planner)"? Read here as the stored Signal plan. Confirm.
2. Each deciding group on a held invoice gets its own held Signal. Confirm, or ask for one invoice-level Signal.
3. If the blocked code must reach a reader of the Signal, the evidence line needs another carrier; `SignalReadService` is ontology's.
4. A later Override or Return (task 105) closes or supersedes these Signals; whether their "held for review" wording should change then is 105's call.

## 14. Part E - the handler (2026-10-19)

**Files.**
- New: `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` (`IJobHandler`, `JobType` = `InvoiceSubmissionService.JobType`, payload `{ submissionId }`).
- Changed:
  - `Infrastructure/DI/FinanceModule.cs`: scoped `IInvoiceSubmissionProcessor`, `IInvoiceSubmissionPersister`, `IInvoiceSubmissionAfterSteps` and the handler as `IJobHandler`. Unconditional (ADR-032); `NullInvoiceAi` keeps the AI-off case a data outcome.
  - `IInvoiceSubmissionAfterSteps.cs` / `InvoiceSubmissionAfterSteps.cs`: only `ResumeAsync(Guid submissionId, string? correlationId, CancellationToken)` was added (interface plus a `NotImplementedException` stub). Superseded: the real body is the after-steps core fix (13a, 5a04a41fb); the stub and the duplicate member were removed in 8b66c6dac.
  - `SubmissionReasonCodes.cs`: `DocumentUnavailable`, `DocumentStorageUnverified`, `ProcessingFailed`.
- Tests: `InvoiceSubmissionProcessingJobHandlerTests` (strict doubles for the processor, persister, after-steps, lock and Dataverse), `InvoiceSubmissionProcessingFlowTests` (one in-memory Dataverse double, the real processor, persister and a spy), and a `Build()` / `LoadDocument` addition to the part B processor harness.

**Step-0 order (coordinator correction).**
1. Payload: no `submissionId` -> Poisoned.
2. Processing lock `invoice-submission:{id}`, owner = job id, 20 minutes, released in `finally`. Not acquired -> Failure (Poisoned on the final delivery); the row is not touched (074 K1).
3. Re-read. Row gone -> Success (044 compensation).
4. **Terminal (Accepted, Rejected, Failed) -> Success, nothing written.** First, so a finished submission is never rewritten and the flag cannot overwrite it.
5. Flag off (ADR-018): no link -> Failed `ebilling.disabled` + Poisoned; linked -> stays Processing + Poisoned (a replay resumes).
6. **Processing + `sprk_invoice` -> `IInvoiceSubmissionAfterSteps.ResumeAsync`.** Never `EvaluateAsync` or `PersistAsync`.
7. Snapshot check (D-87 fields) -> Failed before Processing.
8. Processing written (skipped if already Processing), before any AI call (ADR-017).
9. `EvaluateAsync` -> `PreflightRejection` (074 K-3) -> `PersistAsync` -> `RunAsync` / `ResumeAsync`. A `ReEvaluate` runs once more; a second one is Failure.

The handler writes only Processing, Failed and (RejectedUnreadable only) Rejected. It never writes Accepted or Rejected after a commit; the after-steps do, last.

**Outcome -> JobOutcome.**

| Situation | Submission status | JobOutcome |
|---|---|---|
| Terminal on entry; row gone; after-steps `Completed` / `SubmissionMissing`; persister `SubmissionMissing` | unchanged | Success |
| RejectedUnreadable (document-level read failure) | Rejected + code, no invoice, one notification | Success |
| Evaluation `Failed`; persister `Refused`; after-steps `Refused`; preflight refusal; snapshot failure; flag off (no link); permanent step failure | Failed + code | Poisoned |
| Persister `Retry` (no invoice); after-steps `Retry`; exception; second `ReEvaluate`; lock busy | unchanged (Processing) | Failure |
| Any Failure case on the final delivery, no invoice link | Failed + code | Poisoned |
| Any Failure case on the final delivery, invoice linked | stays Processing (resumable) | Poisoned |
| Cancellation | unchanged | exception propagates |

**Failed on redelivery.** A Failed submission is terminal, so a redelivery is a no-op Success; recovery is a resubmission. This is the choice to confirm (open question 3).

**Gates.**
- Build `Sprk.Bff.Api`: 0 warnings, 0 errors.
- Unit, broad filtered run: 8082 passed, 0 failed, 11 skipped (live seam tests). The new classes add 84 handler and flow tests.
- `Spaarke.ArchTests`: 1214 passed, 0 failed. ADR-010 ceiling unchanged at 169: `IInvoiceSubmissionAfterSteps` was already counted in part D; the handler adds no interface, and the interface is kept because the handler tests fake it.
- Mutation run (12 mutants of the handler): all killed (1 to 26 failing tests each) after one added test, `AfterStepsRetry_OnTheFinalDelivery_NoLink_FailsWithTheResultCode`; the survivor was the after-steps Retry mapping, which fell back to the exception path with the same outcome but a different reason code.
- Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false -p:NuGetAudit=false`, `git archive` exports at `C:\wt049m` / `C:\wt049e`, `Compress-Archive -CompressionLevel Optimal`, PDBs included; both folders removed):

| Build | Files | Zip (B) | Zip (MiB) | Uncompressed (B) | `Sprk.Bff.Api.dll` (B) |
|---|---|---|---|---|---|
| fresh `origin/master` | 192 | 39,250,449 | 37.43 | 131,032,744 | 19,765,248 |
| this branch head | 192 | 39,817,317 | 37.97 | 132,718,468 | 21,190,144 |
| Delta | 0 | +566,868 (+0.54 MiB) | | +1,685,724 | +1,424,896 |

  The delta is an upper bound for part E: it holds every `work/ebilling-r1` commit not on master. Code only, no package change. Under +5 MB; 37.97 MiB total, under the 55 MB review line.
- CVE: `dotnet list ... package --vulnerable --include-transitive` -> no vulnerable packages.

**Acceptance criteria (amended) -> tests.**
- Criterion 1 (Accepted writes everything once): `Accepted_WritesEverythingOnce`.
- Criterion 2, superseded by D-101 (a read submission always gets its invoice; a Rejected SPA invoice is created Rejected; no approval): `RejectedSpa_InvoiceCreatedRejected_NoApproval`, `BlockedInternal_InvoiceInReview_SubmissionRejectedBlocked`. Only a pre-read failure has no invoice: `DocumentLevelReadFailure_IsRejected_NoInvoice`.
- Criterion 3 (a fault commits nothing, never Accepted): `TransactionFault_CommitsNothing_NeverAccepted`.
- Criterion 4 (redelivery of a completed job is a no-op): `RedeliveryOfACompletedJob_IsANoOp`.
- Criterion 5 (redelivery after a Signal-write failure completes, never a second invoice): `RedeliveryAfterASignalWriteFailure_CompletesWithoutASecondInvoice`. It now goes through the real `ResumeAsync` (14a; the test-only stand-in is gone).
- Serialisation (074 K1): `TwoConcurrentDeliveries_OneRuns_TheOtherFails`.
- Reason codes: `ServiceFailed_RetriesThenFailed_NoInvoice`, `FlagOff_IsFailed_EbillingDisabled`, `NullAiFacade_IsFailed_AiInvoiceDisabled`, plus the unit theories.
- I-5 (no AI output alone rejects): part B `AiObservationAlone_NeverRejects` and `NoAiOutputAloneRejects_EndToEnd`.
- D-125 ("Signals for every finding" qualified): system-policy Signals only when BILL-SYSTEM-SUBMISSION is enabled and Active; covered by part D's `InvoiceSubmissionAfterStepsTests`, not repeated here.

**Open questions.**
1. A persister `Resumed` result routes to `ResumeAsync`, not `RunAsync` (brief step 4): the terminal derived from this run's outcome could contradict the committed one. (R2, 14c: so does a Retry that carries an invoice id.)
2. Closed: `ResumeAsync` is implemented (13a). An exception from it still maps to Failure (Failed on the final delivery without a link; stays Processing with one).
3. Should a dead-letter replay of a Failed-without-link submission be reprocessable? Today it is a no-op.
4. Final delivery with an invoice link: stays Processing + Poisoned (so the replay can resume). Confirm.
5. The flag check sits after the terminal rule (a finished submission is never overwritten).
6. The unreadable-document notification can be lost in the window between the Rejected write and the sink call (the sink contract does not retry).
7. The step-7 snapshot check fails on every missing D-87 field, a superset of the tracking-number check in brief 0d.

### 14a. Flow tests on the core fixes

Seven flow tests (criterion 1, criterion 2, criterion 4, criterion 5, the Blocked internal test, I-5 `NoAiOutputAloneRejects_EndToEnd`, and the 074 K1 concurrent-deliveries test) returned `JobStatus.Failed` for one cause: the flow's Dataverse double predated 13a B. The after-steps now write the terminal status through `IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync` using the `versionnumber` of the submission it read. The double (a strict `Mock<IGenericEntityService>`) had no setup for that call (the interface default throws, so the strict mock raised), and its submission row had no row version, so the terminal write could not be conditional. Nothing in the stored findings was missing: the persister's real `sprk_findingsjson` carries the `signalPlan` and the kind/reason, and the stored-state checks all passed. The criterion-4 failure was the same cause one step earlier (the first delivery never reached Rejected, so the redelivery found Processing). Harness changes (test file only): the submission row has a row version that starts at 1 and every write bumps (transaction update, plain update, conditional bulk update); a read returns `versionnumber` only when asked for it, plus `RowVersion`; `BulkUpdateIfRowVersionsMatchAsync` applies the fields only when every expected version is the current one, otherwise throws `DBConcurrencyException` and applies nothing (as `DataverseServiceClientImpl` does). The status history is now recorded wherever `sprk_processingstatus` is written. `Accepted_WritesEverythingOnce` also asserts exactly one conditional write (the terminal status). Stand-in removal: `ResumingAfterSteps`, `BuildHandler(resumeStandIn)` and `Flow.Resuming` are gone; the redelivery tests go through the real `ResumeAsync`. The one assertion written around the stand-in (`Resuming.ResumeCalls == 1`) now asserts the real contract: a resume re-runs the invoice totals once (`TotalsCalls == [invoice, invoice]`) while the processor is never called (`Spy.Calls == 0`). No production defect was found; production code is unchanged.

### 14b. A linked submission is never marked Failed (ontology review of part E)

**Finding F4-1.** `MapAfterStepsAsync`'s `Refused` branch called `FailAsync`, which calls `MarkFailedAsync`: a plain `UpdateAsync` of status and reason code with no status or version condition. `ResumeAsync` / `RunAsync` answer Refused only for an invoice that is already linked (`stored_state_mismatch`, `signal_plan_missing`, `invoice_link_mismatch`, `not_processing`), and each means a bug. Effects: (a) Failed is terminal, so the invoice was stranded with no Signals, record or notification and every replay after a code fix was a no-op; (b) a reviewer who decided the invoice between the read and this write was overwritten with Failed (the job lock does not cover reviewer writes).

**The rule.** A submission that links its invoice is NEVER marked Failed by the handler. In the Refused branch (reached from `RunAsync` after Committed / Retry-with-invoice, from `Resumed`, and from the Processing-with-link resume) the handler writes nothing, leaves the submission Processing, `LogError`s the submission id and reason code (ids and codes only, ADR-015) and returns `Poisoned` with the code. It is the same rule as the final transient delivery with a link and the flag-off-with-link path. Implementation: `LeaveLinkedSubmission` (no write, Error log, Poisoned). `FailAsync` now takes a required `linked` argument and delegates to `LeaveLinkedSubmission` when it is true, so every caller states whether the submission is linked. `MarkFailedAsync` carries the invariant in its XML doc (only called before an invoice is linked; the write is unconditional because of it); the class remarks say it too.

**Callers checked** (every `FailAsync` / `MarkFailedAsync` caller):
1. `ProcessAsync` catch, permanent `SubmissionStepFailure` (document unavailable / storage unverified): raised inside the document load, before any invoice, but the catch cannot see the row. It now re-reads the link (`IsLinkedAsync`, the `TransientAsync` read) and passes it; a read fault falls to the existing write-fault catch (Failure, retried). Reaches a link only through a data fault (a Received row carrying a link).
2. Flag off, not linked: `linked: false` (the linked branch returns Poisoned above it already).
3. Snapshot failure (0d): runs after the Processing-with-link resume, so a link here means a Received row with a link (data fault; `ReceivedWithLink_TakesTheNormalPath` shows the code treats it as reachable). Passes `linked`: left alone.
4. Evaluation `Failed` and the preflight refusal: same Received-with-link path; pass `linked`.
5. Persister `Refused`: the persister returns Resumed first for a linked row (line 214), so Refused implies no link at that moment; passes `linked` for the Received-with-link edge.
6. `TransientAsync` final delivery: already skips when linked or terminal (its read is a fresh one).
7. `MapAfterStepsAsync` Refused: always linked; the fix above.
`RejectUnreadableAsync` writes Rejected (document never read, no invoice); not a Failed write and unchanged.

**Tests** (`InvoiceSubmissionProcessingJobHandlerTests`): `ResumeRefused_WithALink_LeavesProcessing_AndWritesNothing` (theory, the four codes: Poisoned, outcome carries the code, `Writes` empty, one Error log with id and code); `RunRefused_AfterCommitted_LeavesProcessing_AndWritesNoFailed` (the four codes: only the Processing write); `PersistResumed_ThenRefused_LeavesProcessing`; `ReceivedWithLink_ThenFailedOutcome_IsNotMarkedFailed`; `PermanentStepFailure_WithALink_IsNotMarkedFailed`. Updated: `ResumeResults_MapToOutcomes` and `AfterStepsResults_MapToOutcomes` each had a Refused row asserting a `Failed` write with the code; that asserted the defect, so the Refused row left both theories (they no longer take a `failedCode` parameter and assert no Failed write) and the new tests own the case. The flow tests had no Refused case.

**Mutation evidence.** With the Refused branch restored to `FailAsync(..., linked: false, ...)` and `FailAsync`'s linked check disabled (non-constant static flags), 11 of 93 `InvoiceSubmissionProcessing*` tests fail (all 11 new cases: 4 resume codes, 4 run codes, Resumed-then-Refused, Received-with-link evaluation Failed, permanent step failure with a link); restored, 93 of 93 pass. Gates: build 0 warnings / 0 errors; filtered `Sprk.Bff.Api.Tests` 8181 passed / 0 failed / 11 skipped; `Spaarke.ArchTests` 1214 passed / 0 failed.

Known limit (ontology-r1 re-check of 8098ec9b3, PASS): on the Received-with-link data-fault path, `LeaveLinkedSubmission` leaves the
status Received, not Processing, so a replay takes the normal path again rather than the resume. Acceptable for a data fault.

### 14c. Review fixes R2, R3, R5, R6 (adversarial review of the 049 change set, 2026-10-20)

**R2 (F1): a persister Resumed result whose totals failed went to `RunAsync`.** Two deliveries can overlap because the processing lock is
non-atomic and fails open (`IdempotencyService`). Delivery B evaluates, the persister finds A's link and takes `ResumeAsync` -> `WithTotalsAsync`;
a totals fault made that result a `Retry` that CARRIES the invoice id, and the handler sent every `Retry` with an invoice id to `RunAsync` with
B's re-evaluated terminal. If B's AI outcome differed from the stored one, the after-steps answered `stored_state_mismatch` (Refused) and the job
was Poisoned with the submission left Processing. That falsified the invariant 13a states.

| | Before | After |
|---|---|---|
| Persister, submission already linked | queries lines, runs the totals; a fault returns `Retry` + invoice id | queries lines and returns `Resumed`; NO totals |
| Handler, `Retry` with an invoice id | `RunAsync(persisted)` with this run's terminal | `ResumeAsync(submissionId)`, from the stored state |
| Handler, `Resumed` | `ResumeAsync` | unchanged |
| Handler, `Committed` | `RunAsync` | unchanged: the only caller of `RunAsync` |

The after-steps' `ResumeAsync` already recalculates the totals (`InvoiceSubmissionAfterSteps.ResumeAsync`, `_totals.RecalculateAsync`; a fault there is
a totals Retry after the Signals and the record), so no totals work is lost; `ResumeAsync_TotalsFault_RetriesThenCompletes` covers it. A `Retry` with an
invoice id can now come only from `Committed` + a totals fault (this delivery's own commit), and it also resumes from the stored state, which is
safe (the stored findings are the ones this run just wrote) and retries the totals once more in the same delivery. `RunAsync` still accepts
Resumed / Retry as an input (its contract and tests are unchanged); the handler no longer sends them.
`SubmissionReasonCodes.StoredStateMismatch` and 13a now state the invariant as it is.

**R3 (F2, D-133): a submission left Processing is shown to the administrator.** `LeaveLinkedSubmission` (now `LeaveLinkedSubmissionAsync`),
the linked branch of `TransientAsync` (the final delivery of a retry) and so every permanent `Refused` from the after-steps
(`decision_owner_refused`, `decision_subject_unfileable`, `decision_core_unsupported`, `stored_state_mismatch`, ...) call
`AdminAttentionText.NoteAsync` with a code-only segment:
`Processing stopped: {code}; the invoice is linked and the submission stays Processing: review it, then resume or correct`.
Rules: (1) NOT a status write: the 14b rule (a linked submission is never marked Failed by the handler) is unchanged, and the write carries
only `sprk_adminattention`. (2) MERGE, never overwrite: `NoteAsync` reads the current `sprk_adminattention` and `sprk_findingsjson`, and
`AdminAttentionText.Merge` replaces only the segment of its own kind: the Signal segment is rebuilt from the stored refusals (the same function the
persister and `RecordSignalWriteRefusalsAsync` use), the policy segment and the stopped segment are each replaced by their own, any other text
(an administrator's) is kept. The order is fixed (Signals, system policy, stopped, other text) and joined by ` | `, so the result is a pure
function of its inputs. (3) Idempotent: the column is written only when the merged text differs from the stored one, so a redelivery with the
same cause writes nothing; a different code replaces only the stopped segment. (4) Capped at 500 characters
(`InvoiceSubmissionPersister.AdminAttentionMaxLength`): the policy and stopped segments are short (the code is cut at 80 characters) and stay whole;
the Signal segment gives way with its `... (+N more)` count (`Build(items, maxLength)`); a final hard cut is the last resort. (5) A fault
(the read, the parse or the write) is logged at Warning with the exception type only and swallowed; the handler's outcome never changes.
Not covered on purpose: the flag-off-with-link path (the administrator's own switch, `ebilling_disabled`) sets no text. A Received row that carries a
link (a data fault) is flagged too, and stays Received (14b known limit).

**R5 (F2, D-133): a misconfigured system policy is no longer silent.** `SystemSignalsAsync` skipped the system Signals with a Warning for
every unusable state of `BILL-SYSTEM-SUBMISSION`. D-125 accepted that only for a policy an administrator DISABLED. Now `missing`, `ambiguous`,
`without_a_version_in_force` and `of_another_rule_type` also add the segment
`System billing policy BILL-SYSTEM-SUBMISSION is {missing | ambiguous (more than one) | without a version in force | of the wrong rule type}: system Signals were not created`
(same merge, idempotency, cap and swallow rules as R3); `disabled` stays a Warning only. Because the attention write moves the row version, the
terminal write re-reads it first (`rowVersion = null`), as after the findings writes. `RecordSignalWriteRefusalsAsync` now merges into the
current value (one extra read, only when it adds a refusal) instead of overwriting, so a later Signal refusal does not erase the policy note.

**R6 (F4): documentation.** D-133 and 13d item 6 said "conditional update"; the write is a plain `UpdateAsync` and now says "the same update".
13a's invariant, `SubmissionReasonCodes.StoredStateMismatch` and the `IInvoiceSubmissionPersister` / `IInvoiceSubmissionAfterSteps` XML docs state
the R2 rule. `docs/data-model/sprk_invoicesubmission.md` lists the three sources of `sprk_adminattention`: Signal plan and write refusals, processing
stopped on a linked submission, system policy misconfigured.

**Tests (fail-before).** Persister: `AlreadyLinked_WithATotalsFault_IsStillResumed_AndNeverRecalculates` (new); `AlreadyLinked_CreatesNothing_ReturnsStoredIds`
and `TotalsFailure_AfterCommit_RetryResumes_NoSecondInvoice` now assert the resume path runs no totals. Handler (`InvoiceSubmissionProcessingJobHandlerTests`):
`RetryWithInvoice_ResumesFromTheStoredState` (replaces the test that pinned `RunAsync`), `ResumedWithATotalsFault_ResumesFromStoredState_NoStoredStateMismatch`
(the real component's answer to a differing terminal is scripted as `stored_state_mismatch`; the resume completes, nothing is flagged),
`PermanentRecordRefusal_SetsAdminAttention_AndKeepsProcessing` (theory: owner / subject / core), `RunRefused_AfterCommitted_SetsAdminAttention`,
`LinkedFailAsync_SetsAdminAttention_NotFailed`, `LinkedFinalDelivery_SetsAdminAttention_EarlierDeliveryDoesNot`, `UnlinkedFinalDelivery_IsFailed_WithoutAttention`,
`StoppedNote_MergesWithTheSignalRefusalAttention`, `StoppedNote_KeepsOtherSegments`, `StoppedNote_IsIdempotentAcrossARedelivery`,
`AttentionWriteFault_DoesNotChangeTheOutcome`, `AttentionReadFault_DoesNotChangeTheOutcome`, `FlagOff_WithALink_SetsNoAttention` (must not fire);
`ResumeRefused_WithALink_LeavesProcessing_AndWritesNothing` now asserts no STATUS write and exactly the one attention write. After-steps (`InvoiceSubmissionAfterStepsTests`):
`SystemPolicyMisconfigured_SetsAdminAttention` (theory: missing, ambiguous, no version in force, wrong type), `SystemPolicyDisabled_SetsNoAdminAttention`,
`SystemPolicyUsable_OrNoSystemFinding_SetsNoAdminAttention`, `SystemPolicyMisconfigured_AdminAttention_IsIdempotentAcrossARedelivery`,
`SystemPolicyMisconfigured_MergesWithSignalRefusalAttention` (both orders), `SystemPolicyMisconfigured_AttentionWriteFault_DoesNotChangeTheOutcome`.
`AdminAttentionTextTests`: the two notes, the merge (own kind replaced, others kept, fixed order, rebuild from findings, admin text, null, cap, determinism).
The world double gained an `Ambiguous` policy state and an attention-write fault.
Fail-before (source restored to 6c92df33d behaviour): R2 reverted (the persister file and the handler's Retry/Resumed case) fails 5 of the 493 filtered
`InvoiceSubmission|AdminAttention` tests (the 3 new R2 tests and the 2 updated persister assertions); `NoteAsync` made a no-op fails 21 (R3 and R5);
all restored byte-identical, 493 of 493 pass.
Gates: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings / 0 errors; the broad filtered `Sprk.Bff.Api.Tests` run 8,337 passed / 0 failed / 11 skipped;
`Spaarke.ArchTests` 1,214 passed / 0 failed.

**R7 (follow-up to R3, D-133): completing the submission withdraws its "Processing stopped" segment.** A submission that R3 flagged and a later replay then completed (terminal Accepted or Rejected) kept the segment, so the administrator's view showed a completed submission as stopped. Decision: code withdraws only its OWN now-false statement. `InvoiceSubmissionAfterSteps.WriteTerminalAsync` puts `sprk_adminattention` WITHOUT the "Processing stopped" segment (`AdminAttentionText.WithoutStopped`) into the SAME row-version-conditional update that writes the terminal status: the new text is computed before the write from the text read with the same row version (the attention column is now in `SubmissionColumns` and `StateColumns`), so the update's condition is unchanged and not weakened; a conflict re-reads and recomputes from the new text, so an administrator's note added meanwhile is never overwritten with a stale copy. Everything else stays for the administrator: the Signal-refusal segment, the system-policy segment and any text an administrator typed are true facts to review. If the stopped segment was all there was, the column is set to null (`DBNull.Value`, the explicit clear): the only case where code empties it, because it is withdrawing its own statement, not clearing the administrator's review. A stored text with no stopped segment is not written at all (the column is not in the update), so a first run, a run with only Signal or policy text and a redelivery after completion (the submission is terminal, nothing is written) all leave it alone. The computation is pure and cannot fault; a fault of the update is the terminal write's own, as before. A submission a reviewer completed while Processing (the after-steps write nothing) keeps its stopped segment; the administrator clears it. Two other answers recorded here: (a) flag-off with a link stays unflagged: the flag is the administrator's own switch (`ebilling_disabled`), and a linked submission left by it is the administrator's own doing; (b) a Retry that carries an invoice id after this delivery's own commit resumes from the stored state, and that is safe (R2: the stored findings are the ones this run just wrote). Tests: `InvoiceSubmissionAfterStepsTests` `Completion_*` and `NoTerminalWrite_LeavesTheStoppedSegment` (stalled-then-completed keeps Signal and policy, only-stopped gives null, no stopped segment writes nothing [theory: null, empty, admin text], admin text kept, redelivery idempotent, concurrent change recomputed on the new version, already-terminal untouched) and `AdminAttentionTextTests` `WithoutStopped_*`. The test world's read now returns only the columns it asks for, as Dataverse does, so a missing column in the list fails the tests. Fail-before: the stopped-segment predicate mutated to the policy prefix fails the R7 tests (and R5's, which share the merge); the attention column removed from the two column lists fails the 5 R7 tests that need the stored text; restored, 511 of 511 `InvoiceSubmission|AdminAttention` pass (493 before, 18 new).

**Known limits.** (1) A reviewer-completed submission (terminal while Processing, no after-steps write) keeps a stale "Processing stopped" segment; the administrator clears it (R7 withdraws it only on the after-steps' own terminal write). (2) The R2 race is closed on the handler path; the lock itself is still non-atomic and fail-open
(`IdempotencyService`), so two deliveries can still both run the AI read; they can no longer contradict each other's terminal. (3) Ontology re-check of 59b0db838 (PASS, 2026-10-20): the attention merge reads the current text and writes it unconditionally (except R7's terminal write, which is row-version conditional), so an administrator clearing the field at the same moment can see the old text written back. Rare and harmless: the administrator clears it again.

### 15. Review fixes R1 and R4

Two findings from the adversarial review of 049. Both touch ontology-r1 files (`FinanceBaseValues.cs`, `SignalWriter.cs`); the diffs are kept to the one definition and the one catch.

#### R1 (F1, confirmed): pending spend counted a blocked invoice while its submission was Processing

- **Before.** `FinanceBaseValues.HeldSubmissionConditions` held an invoice only when a linked submission was Rejected AND carried the blocked reason code. Since D-130(a) the persist transaction writes the blocked code and sets the invoice In Review, but the submission stays Processing until the after-steps' final write (and for good if that write is poisoned). So during normal processing, and permanently for a blocked submission that ends Processing/Poisoned, the In Review invoice counted in Pending Spend. That broke D-102/D-104 (held invoices never count).
- **After.** Held = a linked submission with `sprk_processingstatus` IN (Processing, Rejected) AND `sprk_outcomereasoncode` = the blocked code. Only a committed Blocked outcome writes the code, so a Processing submission carrying it is already a blocked one. Override moves the submission to Accepted, which leaves the set. Return moves the invoice out of In Review.
- **Shape.** `HeldSubmissionConditions` is now `(Column, Values)` pairs and both paths write every condition as `in` (FetchXML `<condition operator="in"><value>..</value>..` and `ConditionOperator.In`), one value for the reason code. Both stay a LEFT OUTER link with the conditions inside the link plus the `held.sprk_invoicesubmissionid` null check. The columns are unchanged, so `DerivedBaseColumns` and `DerivedBaseColumnsFieldSecurityTests` need no change (checked; nothing else pins the conditions: the arch guard only flags consumers that spell them). `FinanceRollupService` changed one line (`In` with the values).
- **Tests** (`SpendHeldAndPendingTests`): must-fire on both paths, Processing + blocked code is held and Rejected + blocked code is still held (`D130a_AProcessingSubmissionWithTheBlockedCode_HoldsItsInvoice_OnBothPaths`); Override: Processing + blocked held, then Accepted leaves the set (`D130a_AfterOverride_...`); must-not-fire: Processing with no code or another code, Rejected with no/another code, Accepted/Failed/Received with the blocked code (`D104_OnlyABlockedSubmission_HoldsAnInvoice`, whose old Processing-does-not-hold row predated D-130(a) and was flipped out of the not-held set); the shape test pins the new `in` conditions on both builders.
- **Fail-before.** With the status values mutated back to Rejected alone (non-constant edit of the one array), 3 of 51 `SpendHeldAndPendingTests` fail: both new D-130(a) tests and the shape test; restored, 51 of 51 pass.
- **Notes updated.** `049-pending-spend.md`: the held representation, the "Processing does not hold" must-not-fire line (flipped to must-fire), K4.
- **Not run.** No live read. The seam case `FinanceBaseValuesSeamTests.PendingSpendQueries_AreAcceptedByDataverse_ReadOnly` sends the pending FetchXML shape; re-running it on dev will confirm Dataverse accepts `in` inside the outer link (it accepts `in` on the root filter today). Offered, not run.

#### R4 (F2): a Signal deleted after the record produced a false Error log and failure metric

- **Before.** `SignalWriter.CloseActedByAutomatedDecisionAsync` caught every exception, logged `WriteRefused` at Error and metered `signal_close_failed`, then rethrew. `InvoiceSubmissionAfterSteps.CloseResolvedSetAsync` treats ObjectDoesNotExist as benign (one Warning, continue; ontology accepted that under 13c (b)). So a deleted Signal produced a false Error and a false failure metric.
- **After.** A catch placed before the generic one, `when (IsSignalGone(ex))`, logs at Debug and rethrows the same exception (`throw;`), unmetered. `IsSignalGone` walks the inner chain with `RecordContainerResolver.IsRecordNotFound`, the same test the after-steps use, so every exception the caller treats as "gone" is also unreported here. No new outcome, no enum member, the after-steps untouched. Every other fault is logged at Error and metered as before (the access-denied reason still applies).
- **Tests** (`SignalWriterTests.AutomatedClose.cs`): ObjectDoesNotExist on the read, on the conditional write (deleted between read and write) and wrapped in an inner exception: the same instance is rethrown, no failure metric, nothing at Warning or above, no write. Another fault (0x80040216, one off) on the read or the write: still one Error with `WriteRefused` and `signal_close_failed`. The test fake gained `FakeOrganizationService.SignalRetrieveFault` (a retrieve that throws), in `SignalWriterTests.cs`.
- **Fail-before.** With the new catch disabled (`when (false && IsSignalGone(ex))`), the 3 not-found cases fail; restored, `SignalWriterTests` 194 of 194 pass.
- **Residual.** ObjectDoesNotExist on the conditional update can also mean a missing lookup target (the Decision Record). The record is written before the close in the same flow, and the after-steps already read the same code as "Signal gone", so this stays consistent with them; noted, not changed.

- **R1 live check (2026-10-20, read-only):** `FinanceBaseValuesSeamTests.PendingSpendQueries_AreAcceptedByDataverse_ReadOnly` passed against spaarkedev1 on a7c2c8e49 (env `SIGNALS_LIVE_DATAVERSE_URL` and `SIGNALS_LIVE_CALLER_ID` = the writer). Dataverse accepts the `in` conditions inside the LEFT OUTER link on both pending shapes; nothing was created.
- **R1 and R4 owner review (ontology-r1, 2026-10-20): PASS, no F findings.**
- **K (R4, ontology):** ObjectDoesNotExist on the close can also mean the Decision Record lookup target is missing. The record id comes from this run's own writer result (created or AlreadyRecorded), and Decision Records have no delete path except an admin's. So the two are confused only when an admin deletes the record concurrently, and the after-steps' Warning still surfaces that case.
