# Task 114 - Override releases a no-version-in-force gap hold (that invoice only), recorded through WriteBillingDecisionAsync

Branch `task/ebilling-114` (base `767244e003`). Commits: `bf7e283560` (feature), `2836bd767c` (review round).
Rigor FULL, model tier Opus 5.5 trailer per the brief. TASK-INDEX.md, current-task.md and the POML status are the main session's.

## What was built

- **Disposition.** `SubmissionOverrideService.Classify` gains `ProcedureHeld`: an Accepted submission whose outcome reason is
  `procedure_gap` or `procedures_unresolved`, on either channel (the SPA hold). It is overridable like a Blocked one; it has no Return
  (`not_internal`, as before). An Accepted submission reading `overridden` is `Overridden` on any channel (repeat = AlreadyDecided).
- **C22 lifted safely.** `DecideLockedAsync` no longer requires `DecidingIndexes.Count > 0` unconditionally: `StoredHoldMatches` needs, for
  Blocked, a deciding finding OR a stored Procedure hold; for ProcedureHeld, an Accepted-kind document with a stored hold. Findings that
  do not name the hold stay `FindingsUnreadable` (fail closed). A Return of an Internal gap-only Blocked invoice is now allowed (C22).
- **Release marker.** The released state is status Accepted + reason code `ebilling.submission.overridden`. `InvoiceProcedureHold.ReasonsOf`
  returns no Procedure reason for it (findings keep the gap as evidence, FR-37, I-5). Unreadable findings still fail closed.
- **Review detail.** `InvoiceOpenHolds.ReasonsAfterAsync` (new, `ReasonsAsync` delegates to it) substitutes the released submission's
  post-write state and reads everything else app-only (invoice own hold, firm column, other linked submissions). The detail is
  `ReviewDetailOf(remaining)`: 659490002 while the firm hold is open, then 659490001, then 659490000, else blank. It is written in the SAME
  `ExecuteTransactionAsync` as the submission's state: two `UpdateRequest`s, each `IfRowVersionMatches`. A mismatch on either row applies
  nothing (409 `concurrent_change`; the repeat finds the record and finishes). Used only when the Override lifts a Procedure hold and the
  `sprk_reviewdetail` probe says the column exists; otherwise the existing single conditional write (an ordinary rate-card override is unchanged).
- **Decision Record.** Through `BillingDecisionRecorder` -> `WriteBillingDecisionAsync` only. The Facts shape is unchanged (the `channel` fact
  value now follows the row: Internal / SPA). Two additive Values keys on a release: `releasedHolds` (sorted codes) and `releasedHoldDetail`
  ("Name (workDate)" for a gap, the cause code for an unresolved). Nothing else is created: no Procedure Version, assignment, override row or Signal.
- **Column script.** `Get-SubmissionHoldReasons` in `Deploy-EbillingInvoiceReviewDetailColumn.ps1` mirrors the released state (+ Pester case).

## Deviations / decisions to confirm

1. **Scope wider than the title.** The POML title says "gap hold"; the release also covers the *unresolved* hold, and both channels (owner
   note 2026-10-21 + the brief: both channels). If the owner wants gap-only, `Classify`/`StoredHoldMatches` narrow with one condition.
2. **Reason-code marker.** Superseded by the F1 fix below: the code alone no longer releases; the field-secured stamp is required too.
3. Values were extended with two additive keys; Facts were not changed. ontology-r1 awareness only.
4. **Scope deviation: the firm hold is NOT released (needs owner confirmation).** The owner's 2026-10-21 note says an Override releases "whatever
   raised the hold". 114 releases the Procedure holds (gap, unresolved) only. The firm hold (`firm_missing`) stays: Set firm is its route, and
   approving with no firm breaks attribution. An invoice with a firm hold and a Procedure hold is still held after the Override (the detail then
   reads "waiting for the firm"). **Owner to confirm** this narrower reading.

## Review (Step 9.5)

adr-check: no conflict. ADR-002 (no plugins; one server-side owner), ADR-004/052 (no background work), bff-extensions (no endpoint, DI registration,
package or background work added; one new internal method on an existing static class). Component justification: `ReasonsAfterAsync` extends
`ReasonsAsync` (cost of nothing: the detail would be derived from pre-write state and read blank while the release lands);
`StoredProcedureHold` is a reader-member projection of the stored findings (the record must name what was released).
code-review / adversarial pass (1, read-only, on bf7e283560): **no F-class finding.** Fixed in 2836bd767c: K4 unreadable findings under `overridden`
now still fail closed; K4 cosmetic empty `" ()"` in `releasedHoldDetail`.

## Review round 2: F1 (forged release marker) and finding 4

**F1 (fixed).** `sprk_outcomereasoncode` is not field-secured. The first design treated `ebilling.submission.overridden` as "no Procedure hold"
whatever the findings said, so any internal user with Write on `sprk_invoicesubmission` could set it and approval would pass with no Override and no
Decision Record. Before 114 the hold was anchored only in `sprk_findingsjson`, which is field-secured and writable only by the BFF app user.
Fix (code only): a submission is **released only when BOTH hold**: the code is `overridden` AND the parsed findings carry a valid `override` stamp
(`SubmissionOverrideService.WithStamp` writes it in the same transaction; `ReadStamp` validates it: GUID `recordId` and `bySystemUserId`, both
non-empty, and a parseable `on`). One helper owns this, `InvoiceProcedureHold.IsReleased(reasonCode, json)`; it is used by:
- `InvoiceProcedureHold.ReasonsOf` (approval, `InvoiceOpenHolds`, the review detail): a code with no valid stamp falls through to the findings
  derivation, so the submission is held (`procedure_held`);
- `SubmissionOverrideService.Classify`: Accepted + `overridden` with a valid stamp is `Overridden`; without one, findings that store a hold give
  `ProcedureHeld` (a genuine Override proceeds, records, and writes the stamp), never `Overridden` / `AlreadyDecided`. `StoredHoldMatches` for
  `ProcedureHeld` also accepts Blocked-kind findings, so a forged Internal submission can still be genuinely overridden;
- `Get-SubmissionHoldReasons` in the column script (`Test-SubmissionReleased`, Pester-tested).
Grep for other readers keyed on the code: only these three, plus the persister/correction intake tables that list the code as an Accepted terminal
(they decide nothing about a hold).
**Instruction for future re-check writers (095d-2, ebilling-r2):** a writer that re-holds a submission MUST remove the `override` stamp from the
findings in the same write, or the submission still reads as released.

**Finding 4 (fixed): order.** The `ReasonsAfterAsync` derivation now runs BEFORE the set store, the Decision Record and the Signal close (it needs no
record id; it asks "what remains once this submission reads as released" through a provisional stamp carrying the decision id). A derivation or
row-version fault returns 503 before anything is written: no record, no stored set, no closed Signal. The real stamp (with the record id) is still
written with the state, and the transaction is built from the derived detail. Test: `Release_WhenTheRemainingHoldsCannotBeRead_IsUnavailable_BeforeAnyWrite_AndTheRepeatFinishes`.

**Pass 2 F4 (fixed): PS release check case.** `Test-SubmissionReleased` now uses `-cne`, because the BFF compares the code ordinally; a Pester case
covers a re-cased code with a valid stamp (seed `-ne` → that case red). The stale approval comment (`InvoiceApprovalService`) now names the stamp.
Pass 2 K2: the re-hold obligation above is prose only; it is an acceptance criterion on 095d-2 (TASK-INDEX row).

Known limits (not fixed, reported):
- **FR-20 held-submission test (pre-existing; DEF-053, #2571).** It reads two unsecured columns only (`FinanceBaseValues.HeldSubmissionConditions`).
  Not widened by 114. Fix direction in DEF-053 (ebilling-owned code).
- The D-122 close marks the shared SubmissionIntegrity Signal Acted while a firm hold remains. 114 widens this to Internal gap + firm-missing.
  Goes to ontology-r1.
- `GET .../blocked` lists no entry for a Procedure hold. On SPA it shows Blocked/Accepted with zero findings and `Channel` "Other". UI follow-up.
- An SPA gap that pre-empted a 073 rejection becomes approvable after Override. It is named in `blockingReasonCodes`. Owner awareness.
- The partner JSON shows `outcomeReasonCode: ebilling.submission.overridden`. The UI does not render it.
- The notification sink deduplicates on (submissionId, outcome), so a release notice for a submission already Accepted would be dropped. The sink
  is a no-op today (task 132).
- K4 409 wording after an invoice-version conflict says "The submission changed ..." (nothing applied, retry succeeds). Not changed: wording.
- K4 a row with no `sprk_channel` Accepted with a gap code would record channel `Other` and notify as SPA; the persister always sets the channel.
- K4 the version-conflict test fake throws the text form (`0x80060882`); the typed-fault branch of `DataverseServiceClientImpl.IsRowVersionMismatch` is its own helper's.

Follow-ups noticed (not in scope, not changed):
- The partner sees reason code `ebilling.submission.overridden` on an SPA release; no new partner wording was added (STOP trigger not hit).
- The POML note that `CreateEntryPoints` lacks `WriteBillingDecisionAsync` is stale: it is already listed.

## Tests and seeds

Unit (xunit, filters: `SubmissionOverrideService|InvoiceOpenHolds|InvoiceProcedureHold|InvoiceSubmission|InvoiceApproval|InvoiceFirmHold|InvoiceReviewDetail|InvoiceAssignFirm|ProcedureReader|BillingDecision|DecisionRecord`): 2294 pass / 0 fail.
Wider `Services.Finance|Signals`: 7742 pass, 2 skipped (pre-existing), 0 fail. ArchTests (`InvoiceReviewDetailWritersGuard|SubmissionOverrideRecordPathGuard|DecisionRecordContextImmutability|RecordOwnerAssignmentCensus|FindingsJsonAppOnlyReadGuard|InvoiceFirmHoldAppOnlyGuard|ProcedureReaderRedactionGuard`): 95 pass / 0 fail.
Pester (`tests/scripts/Deploy-EbillingInvoiceReviewDetailColumn.Tests.ps1`, Pester 5.7.1): 11 pass (round 1) (Pester 6.2 cannot load this file; pre-existing).

Must-fire / must-not-fire: gap released with the firm hold open -> detail 659490002 (and 659490001 / 659490000 for the other remaining holds), approval still refused
(`Release_ReDerivesTheDetail_FromTheRemainingHolds`); last reason released -> detail blank, written explicitly, same transaction
(`Release_OfTheLastHold_BlanksTheDetail_InTheSameTransaction`).

Seeds (each restored, verified with `cmp`):
| Seed | Result |
|---|---|
| Derive the release detail from the released submission only (skip `ReasonsAfterAsync`) | 5 unit tests fail (the 4 must-fire cases + the fault-read case) and `TheOverrideReleaseDerivesThroughOpenHolds` fails |
| `SubmissionOverrideService` calls `WriteConditionClearedAsync` | `SubmissionOverrideRecordPathGuardTests.TheOverrideServiceCallsNoWriterEntryPoint` fails, naming the call |
| Add a public member to `DecisionRecordWriter` | `EveryPublicWriteResultMemberIsAListedEntryPoint` fails |
| `InvoiceProcedureHold.ReasonsOf` ignores `overridden` | 14 tests fail across `SubmissionOverrideServiceTests` and `InvoiceOpenHoldsTests` |

Round 2 (F1 + finding 4), same filters as the brief: unit 1837 pass / 0 fail (the 8-prefix filter); ArchTests (5 guards) 34 pass; Pester 20 pass.
Seed: `InvoiceProcedureHold.IsReleased` made to ignore the stamp (code alone releases) -> 19 tests fail, including the forged-code approval pin
(`procedure_held`), the `Classify` cases and the malformed-stamp cases; restored, `cmp` identical.

Fail-before: the new tests reference members that do not exist on the base (`ReasonsAfterAsync`, `ReleasedHoldsValue`, `ReviewDetailProbe`, `Classify` for
ProcedureHeld), so against the base they do not compile; the seeds above are the behavioural proof.

## BFF hygiene

Placement: BFF, existing `Services/Finance` files; no new endpoint, DI registration, package, background work or AI PublicContracts surface.
Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, `git archive` of base `767244e003` and of HEAD): 137,418,045 -> 137,427,205 bytes, delta +9,160 bytes (about 9 KB; the directory total includes symbols and runtime assets, so the absolute figure is not the 55/60 MB zip measure). No package or project file changed, so no new dependency and no new CVE.

rework: none task 114
