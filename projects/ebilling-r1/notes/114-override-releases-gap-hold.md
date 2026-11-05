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
2. **Reason-code marker.** The release is carried by a column a direct Dataverse writer could set (see K2 below).
3. Values were extended with two additive keys; Facts were not changed. ontology-r1 awareness only.

## Review (Step 9.5)

adr-check: no conflict. ADR-002 (no plugins; one server-side owner), ADR-004/052 (no background work), bff-extensions (no endpoint, DI registration,
package or background work added; one new internal method on an existing static class). Component justification: `ReasonsAfterAsync` extends
`ReasonsAsync` (cost of nothing: the detail would be derived from pre-write state and read blank while the release lands);
`StoredProcedureHold` is a reader-member projection of the stored findings (the record must name what was released).
code-review / adversarial pass (1, read-only, on bf7e283560): **no F-class finding.** Fixed in 2836bd767c: K4 unreadable findings under `overridden`
now still fail closed; K4 cosmetic empty `" ()"` in `releasedHoldDetail`.

Known limits (not fixed, reported):
- **K2 (route to uac-r2, not a public issue).** The marker is one column: an internal user with Write on `sprk_invoicesubmission` could set
  `sprk_outcomereasoncode` to `ebilling.submission.overridden` and approval stops answering `procedure_held`, with no Decision Record. Same trust
  posture as 082c-2 (the FR-20 hold is likewise lifted by the status column); the hardening is to require status Accepted and the writer-created
  record `OverrideDecisionIdFor(submissionId)` in the approval check.
- K4 409 wording after an invoice-version conflict says "The submission changed ..." (nothing applied, retry succeeds). Not changed: wording.
- K4 a row with no `sprk_channel` Accepted with a gap code would record channel `Other` and notify as SPA; the persister always sets the channel.
- K4 the version-conflict test fake throws the text form (`0x80060882`); the typed-fault branch of `DataverseServiceClientImpl.IsRowVersionMismatch` is its own helper's.

Follow-ups noticed (not in scope, not changed):
- GET `.../blocked` lists only `findings[]`, so a Procedure hold shows no entry for the reviewer (UI follow-up).
- The shared SubmissionIntegrity Signal is closed Acted by the existing D-122 close even when a firm hold remains.
- The partner sees reason code `ebilling.submission.overridden` on an SPA release; no new partner wording was added (STOP trigger not hit).
- The POML note that `CreateEntryPoints` lacks `WriteBillingDecisionAsync` is stale: it is already listed.

## Tests and seeds

Unit (xunit, filters: `SubmissionOverrideService|InvoiceOpenHolds|InvoiceProcedureHold|InvoiceSubmission|InvoiceApproval|InvoiceFirmHold|InvoiceReviewDetail|InvoiceAssignFirm|ProcedureReader|BillingDecision|DecisionRecord`): 2294 pass / 0 fail.
Wider `Services.Finance|Signals`: 7742 pass, 2 skipped (pre-existing), 0 fail. ArchTests (`InvoiceReviewDetailWritersGuard|SubmissionOverrideRecordPathGuard|DecisionRecordContextImmutability|RecordOwnerAssignmentCensus|FindingsJsonAppOnlyReadGuard|InvoiceFirmHoldAppOnlyGuard|ProcedureReaderRedactionGuard`): 95 pass / 0 fail.
Pester (`tests/scripts/Deploy-EbillingInvoiceReviewDetailColumn.Tests.ps1`, Pester 5.7.1): 11 pass (Pester 6.2 cannot load this file; pre-existing).

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

Fail-before: the new tests reference members that do not exist on the base (`ReasonsAfterAsync`, `ReleasedHoldsValue`, `ReviewDetailProbe`, `Classify` for
ProcedureHeld), so against the base they do not compile; the seeds above are the behavioural proof.

## BFF hygiene

Placement: BFF, existing `Services/Finance` files; no new endpoint, DI registration, package, background work or AI PublicContracts surface.
Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, `git archive` of base `767244e003` and of HEAD): 137,418,045 -> 137,427,205 bytes, delta +9,160 bytes (about 9 KB; the directory total includes symbols and runtime assets, so the absolute figure is not the 55/60 MB zip measure). No package or project file changed, so no new dependency and no new CVE.

rework: none task 114
