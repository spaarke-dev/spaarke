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
