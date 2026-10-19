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
| `Resumed` | the submission already linked an invoice: nothing created, totals re-run | `InvoiceId` (the stored one), `LineIds` (queried: `sprk_linesequence` - 1) |
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

**Input.** `RunAsync(SubmissionPersistResult, correlationId)` takes Committed, Resumed, or a totals Retry that has an invoice id.
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
- After `PersistAsync` returns Committed, Resumed, or Retry with an invoice id, call `RunAsync(persisted, correlationId)`:

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
(instead of re-running the processor and `PersistAsync`), and treat `stored_state_mismatch`, `signal_plan_missing` and `not_processing` as
Refused like the other codes.

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
1. A persister `Resumed` result routes to `ResumeAsync`, not `RunAsync` (brief step 4): the terminal derived from this run's outcome could contradict the committed one.
2. Closed: `ResumeAsync` is implemented (13a). An exception from it still maps to Failure (Failed on the final delivery without a link; stays Processing with one).
3. Should a dead-letter replay of a Failed-without-link submission be reprocessable? Today it is a no-op.
4. Final delivery with an invoice link: stays Processing + Poisoned (so the replay can resume). Confirm.
5. The flag check sits after the terminal rule (a finished submission is never overwritten).
6. The unreadable-document notification can be lost in the window between the Rejected write and the sink call (the sink contract does not retry).
7. The step-7 snapshot check fails on every missing D-87 field, a superset of the tracking-number check in brief 0d.

### 14a. Flow tests on the core fixes

Seven flow tests (criterion 1, criterion 2, criterion 4, criterion 5, the Blocked internal test, I-5 `NoAiOutputAloneRejects_EndToEnd`, and the 074 K1 concurrent-deliveries test) returned `JobStatus.Failed` for one cause: the flow's Dataverse double predated 13a B. The after-steps now write the terminal status through `IGenericEntityService.BulkUpdateIfRowVersionsMatchAsync` using the `versionnumber` of the submission it read. The double (a strict `Mock<IGenericEntityService>`) had no setup for that call (the interface default throws, so the strict mock raised), and its submission row had no row version, so the terminal write could not be conditional. Nothing in the stored findings was missing: the persister's real `sprk_findingsjson` carries the `signalPlan` and the kind/reason, and the stored-state checks all passed. The criterion-4 failure was the same cause one step earlier (the first delivery never reached Rejected, so the redelivery found Processing). Harness changes (test file only): the submission row has a row version that starts at 1 and every write bumps (transaction update, plain update, conditional bulk update); a read returns `versionnumber` only when asked for it, plus `RowVersion`; `BulkUpdateIfRowVersionsMatchAsync` applies the fields only when every expected version is the current one, otherwise throws `DBConcurrencyException` and applies nothing (as `DataverseServiceClientImpl` does). The status history is now recorded wherever `sprk_processingstatus` is written. `Accepted_WritesEverythingOnce` also asserts exactly one conditional write (the terminal status). Stand-in removal: `ResumingAfterSteps`, `BuildHandler(resumeStandIn)` and `Flow.Resuming` are gone; the redelivery tests go through the real `ResumeAsync`. The one assertion written around the stand-in (`Resuming.ResumeCalls == 1`) now asserts the real contract: a resume re-runs the invoice totals once (`TotalsCalls == [invoice, invoice]`) while the processor is never called (`Spy.Calls == 0`). No production defect was found; production code is unchanged.
