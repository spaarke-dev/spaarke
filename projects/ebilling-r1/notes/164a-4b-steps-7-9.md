# 164a-4b: the Add line service, steps 7 to 9, and the resume

Branch `task/ebilling-164a4a` (continued from 164a-4a at `be3e15358c`). Rigor FULL. Steps 0 to 6 and the route are 164a-4a (`164a-4a-service.md`).

## What landed

- **`InvoiceRuleSignalReconciler`** (new file, `Services/Finance`): the correction re-evaluation's rule reconcile of `pendingSignalChanges.rule` (`ReconcileRuleAsync`, `CloseClearedAsync`, `WriteAsync`, `RecordWriteRefusals`), moved out of `InvoiceCorrectionReevaluation` unchanged and created per run. The correction's Signals step and Add line's step 9 both use it. A correction's log lines keep the template and the `Field` property they had before the move ("Correction of {Field} on invoice {InvoiceId}: ..."); Add line's read "Add line on invoice {InvoiceId}: ..." (fix round K3).
- **`SignalLogContext.AddLine`** in `Submission/SignalEpisodeLifecycle.cs` (the seam the POML names): two log lines only.
- **`InvoiceLineAddService`**, after the commit, with `CancellationToken.None`:
  - **Step 7, totals:** `IInvoiceTotalsService.RecalculateAsync` (016, EB-D-199), then the re-index (`InvoiceIndexingJobHandler.TryEnqueueAsync`, change key `a{decisionId:N}`, best effort).
  - **Step 8, record:** one `add-line` record through `BillingDecisionRecorder.RecordAsync`, idempotent on the decision id.
  - **Step 9, Signals:** re-read the invoice's own submission (fix round F1), reconcile each `rule` entry through the reconciler (`SignalLogContext.AddLine`), then ONE row-version-conditional write that removes the entries, records the writer's permanent refusals and sets `addLine.state = "done"`. Nothing is written when nothing changed.
  - A fault at a step is `AddLineStatus.Incomplete`: 503 `ebilling.decision.incomplete` with `failedStep` (`totals`, `record` or `signals`), `requestId`, `lineId` and `decisionId`.
  - The 200 body now carries `totals` (billed, fees, expenses, line and invoice adjustments, approved).
- **The resume.** A repeat finds the derived row with the same content (step 1) and goes to `ResumeAsync`:
  - the decision is rebuilt from the row (sequence, as-billed codes, resolved timekeeper) and the stored findings (rule reason codes); its hashed members (code, subject, decider, role, outcome, reason, empty resolved set) are what the first attempt had, so the record is the same record;
  - when `addLine` names this decision at `done`, the totals are computed from the rows (no write) and step 9 is skipped;
  - otherwise steps 7 to 9 run again (each is idempotent);
  - step 8 runs on EVERY repeat, so the recorder refuses the same decision id with other content (another reason: the row does not store it, so the row compare cannot see it); only that refusal is answered 409 `id_reused` (fix round K2). Another caller is `id_reused` already at step 1 (the row's `sprk_createdbyperson`);
  - the repeat is looked up before the status, firm and correction refusals, and on an invoice no longer In Review only step 8 runs (fix round F2).
- **Endpoint:** `ToResult(AddLineResult, ...)` maps `Incomplete` to the 503 problem above.
- **Texts (G2):** the `FindingsJsonAppOnlyReadGuardTests` comment and row 15 of `notes/122-findings-json-field-secure.md` name the step-9 write. No new reader type.

## The 4a seam, decided

| Question (from 164a-4a) | Decision |
|---|---|
| Do the plan changes move to `addLine.signals`? | No. They stay in `pendingSignalChanges.rule`, where the correction's Signals step also reads them. Whichever runs first reconciles an entry and removes it in its conditional write; the other finds it gone. Corrected in the fix round (K1): a correction takes no processing lock, so the two can overlap. They converge this way: a Signal write is unique on `sprk_dedupekey` (a second write of the same episode refreshes it), a close is conditional on the Signal's row version and the entry's removal on the submission's, and the run that loses a condition fails its step (503 Incomplete) and converges on its repeat, which finds the entry gone or the Signal already closed. |
| A `system` entry | Only a correction writes one, and an add is refused while a correction is unapplied. Step 9 leaves a `system` entry for the correction (it removes only `rule`). |
| A second add overwrites the single `addLine` member | Kept single. A repeat of the earlier add then does not see its own `done`, so it runs steps 7 to 9 again; each is idempotent (test `ASecondAdd_OverwritesAddLine_AndTheFirstRepeatStillConverges`). No keyed member needed. |
| The reason-only retry | Caught by the recorder hash on the repeat (step 8 always runs); answered `id_reused` (N7 test). |
| `alreadyApplied` without steps 7 to 9 | Replaced by the resume. |

## Acceptance criteria (164a) and evidence

Tests are in `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceLineAddServiceTests.cs` unless named otherwise.

| Criterion | Evidence |
|---|---|
| T1 Fee happy path: row, `sprk_createdbyperson` = caller, Flagged, marker, Q1 totals stored, one record, 200 with lineId, decisionId, totals | `T1_AFeeLine_StoresQ1Totals_AndOneAddLineRecord_AfterTheLineWrite`; the row shape tests from 4a (`AnExpenseLine_IsCreatedFlagged...`, `AFeeLine_StoresTheTypedCodes...`) |
| T2 Expense | `T2_AnExpenseLine_StoresQ1Totals_AndOneAddLineRecord` |
| The record: one, add-line, subject the line, the caller, the reason, empty resolved set, ids and codes, only through the recorder and after the line write | T1 (record columns, facts without the description or name, order commit, totals, record); `TheDecision_NamesTheCaller_AndHoldsIdsAndCodesOnly` |
| T3 a rule on the new line raises `{policy}\|sprk_billingevent\|{lineId}\|1` | `T3_ARuleOnTheNewLine_RaisesEpisodeOne` |
| T4 changed set: Superseded (no record), then n+1; must-not-fire: unchanged set refreshes in place | `T4_AChangedSet_IsSuperseded_AnUnchangedSet_IsRefreshed` (both cases) |
| T5 a cleared non-hold group is ConditionCleared, record-free | `T5_AClearedGroup_IsConditionCleared_RecordFree` |
| T6 (Q7) held invoice: no link, no ConditionCleared, Blocked entry and override stamp byte-identical | `T6_OnAHeldInvoice_NothingIsCleared_AndTheHoldIsByteIdentical` (an Expense and a Fee add). Add line holds only the Signal writer's write and close calls, so `LinkConditionClearedRecordAsync` is unreachable from it. |
| T6b the commit route refuses add-line; a retry gives one line and one record | 164a-2's `ABillingCode_IsRefusedByTheCommitRoute`; `N7_...`, `N10_...` (one record after the repeat) |
| T6c a reviewer-typed code is judged like an imported one | `T6c_AReviewerTypedCode_IsJudgedLikeAnImportedOne` (NonBillableActivity finding and Signal on the added line) |
| T7 I-2 | `T7_TheAddedLine_IsCreatedFlagged` (renamed in the fix round: it asserts the row only); the seam is `T7_ApprovalWithAnAddedLine_IsRefusedUntilDecided_ThenStoresTheAddedLineRule` (`InvoiceLineAddServiceTests.FixRound.cs`, fix round F3) through `InvoiceApprovalService`: Flagged is 409 with the open-lines count, Accept all Clean leaves the added line alone, and after accept (1200) or reduce to 150 (1150) approve succeeds with the EB-D-199 total. |
| T8 Q1 for accept, reduce, disallow | 164a-2: `InvoiceTotalsCalculatorTests` T8 cases and the real line decision service; T1/T2 here for the undecided case through `RecalculateAsync` |
| N7 repeat: alreadyApplied, one row, one record, no new episode; other content id_reused | `N7_ARepeat_IsOneRowOneRecordNoNewEpisode_AndAnotherReasonIsIdReused`; 4a's `TheSameRequestId_WithOtherContent_IsIdReused` |
| N8 changed submission or invoice version: 409, no row, no record, no Signal; held lock busy; the version-only invoice condition | `N8_ASubmissionChangedBeforeTheCommit_IsChanged`, `N8_AHeldLock_IsBusy`, 4a's `AnInvoiceChangedBeforeTheCommit_IsChanged...` (the test world checks a version-only conditional update against the row version, as Dataverse is assumed to; see the live check below) |
| N10 fault at totals, record or signals: 503 incomplete with failedStep; the repeat completes with one record | `N10_AFaultAfterTheCommit_IsIncomplete_AndTheRepeatCompletesIt` (three cases, including the HTTP mapping) |
| N1-N6, N9, N11 | 164a-4a (unchanged, still green) |
| G2 FindingsJson guard and notes/122 row | texts updated; ArchTests run (below) |
| G4 `DecisionActionCatalogTests`, `DecisionRecordWriterTests.Billing` C2 | 164a-2; in the filtered run (below) |
| G5 the correction suites pass unchanged | the refactor commit ran the correction, Signal and submission suites (1720 passed) before any Add line change |

## Seeds (each reverted)

| Seed | Mutation | Result |
|---|---|---|
| S3 | the commit's submission and invoice updates use `ConcurrencyBehavior.Default` | 3 failures: both N8 tests and the request-shape assertion of the 4a happy path. The test world was made faithful first: an unconditional update is not checked against the version (as Dataverse), so this seed is visible. |
| R1 (the resume runs the later steps) | `finished` forced true in `ResumeAsync` | 3 failures: N10 totals, record, signals |
| R2 (the reason-only retry) | the `ReviewInvalid` to `id_reused` mapping disabled | 1 failure: N7 |
| S1, S2, S4 | 164a-4a (route filter, firm gate, internal-caller group) | re-run in the fix round; counts in `164a-4a-service.md` |

## Live check for the dev verification (not run: no live writes)

The commit includes a version-only conditional update of the invoice (`UpdateRequest`, no columns, `IfRowVersionMatches`). It is assumed that Dataverse refuses it when the invoice's row version changed since the read. Check on dev: read an In Review invoice, change it, then send the add with the old version; expect 409 `ebilling.line_add.changed` and no line. If Dataverse ignores a column-free conditional update, that is the POML's escalation trigger: STOP and propose an alternative (for example, a no-op write of an unchanged column).

## Deviations

1. **Refactor beyond the named seam, inside `Services/Finance`.** The rule reconcile was moved, not copied, into `InvoiceRuleSignalReconciler` so step 9 reconciles through the correction's own code (constraint: rules and Signals only through the extracted code, never a copy). `DecisionRecordWriter.WriteAsync` is untouched. (The fix round adds two message constants to `DecisionRecordWriter`; see below.)
2. **T7** (superseded by the fix round): the seam test drives `InvoiceApprovalService` through its public entry points; nothing in it was made visible.
3. **C1-C4 and P1-P2** are the client (164b) and the ribbon script (164c), not 164a; not built here.
4. **A repeat with another reason after a fault at step 7** recomputes the totals (idempotent, same figures) before step 8 refuses it with `id_reused`.
5. **A first attempt that never wrote its record**, repeated by the same caller with another reason, records the repeat's reason (there is nothing to compare it with). Another caller is `id_reused` (fix round K2).

## For ontology-r1 (review set)

- `Services/Finance/Submission/SignalEpisodeLifecycle.cs`: `SignalLogContext.AddLine` and its two log lines.
- `Services/Finance/InvoiceRuleSignalReconciler.cs`: the moved rule reconcile (episode rules unchanged: refresh on the same fingerprint, Superseded then n+1 on another, ConditionCleared when the group is gone, ReopenAsNextEpisode for a resolved latest episode).
- No hold link (Q7). `ResolvedSignalIds` stays empty.
- Fix round: `Services/Signals/DecisionRecordWriter.cs` gains `BillingOtherContentRefusal` and `BillingContentUnreadableRefusal`, the two messages of the existing billing `ReviewInvalid` throw (which one is chosen by whether a same-code record has no readable hash). Same refusal reason, same condition, nothing else; `WriteAsync` byte-identical.

## Publish size (CLAUDE.md section 10)

Measured 2026-11-06 with `dotnet publish src/server/api/Sprk.Bff.Api -c Release` into an empty folder, summing every file:

| Build | Bytes | `Sprk.Bff.Api.dll` |
|---|---|---|
| Fresh `origin/master` (`bcdc0e0069`, exported with `git archive`, built from clean) | 136,791,493 | 24,913,920 |
| This branch at the steps 7-9 commit | 137,659,097 | 25,671,168 |
| Delta | +867,604 (about +0.83 MB) | +757,248 |

The delta is the whole unmerged `work/ebilling-r1` line plus 164a, not this task alone; it is all in `Sprk.Bff.Api.dll` and no file was added or removed from the output (no package). It is below the +5 MB justification threshold. No package, DI registration or background work was added by 164a-4b; the service's constructor takes `IInvoiceTotalsService`, `SignalWriter` and the optional `JobSubmissionService`, all already registered.

## Fix round (review of 164a, 2026-11-06)

Branch `task/ebilling-164a`. Tests are in `InvoiceLineAddServiceTests.FixRound.cs` unless named otherwise. Seeds: each mutation was applied, the named tests run, and the code restored from a copy.

| # | Finding | Fix | Tests | Seed |
|---|---|---|---|---|
| F1 | The submission read took the latest submission of any invoice (`TopCount`) | `InvoiceCorrectionReevaluation.ReadSubmissionAsync` keeps only the row whose `InvoiceSubmissionPersister.InvoiceIdFor(row.Id)` is the invoice; no match is Unavailable with nothing written. Every caller (Add line steps 1 and 9, the correction follow-through) reads through it. The test worlds derive the invoice id from the submission id, as 165a's do (`CorrectionWorld`, `ApprovalWorld.OwnSubmissionId`, `InvoiceCorrectionEndToEndTests`) | `TheSubmissionRead_TakesOnlyTheOwnSubmission`, `AddLine_WritesOnlyTheOwnSubmission`, `AddLine_WithNoOwnSubmission_IsUnavailable_AndWritesNothing` | `.FirstOrDefault()` without the filter: 2 fail |
| F2 | A committed add whose later steps failed could not finish once the invoice left In Review, the firm was held or a correction was pending | Step 1 reads the derived row before the status, firm and correction refusals, which apply to a first add only. In `FinishAsync` step 8 always runs, also after step 7 fails (the answer then names `totals`); steps 7 and 9 run only while the invoice is In Review | `ARepeat_AfterTheInvoiceMovedOn_StillWritesTheRecord` (returned, rejected, correction pending, firm held), `AFirstAdd_OnAnInvoiceNotInReview_IsStillRefused`, `TheRecordStep_RunsEvenWhenTheTotalsFail` | refusals before the lookup: 4 fail; return before step 8 on a totals failure: 1 fails |
| F2 (line decisions) | The same order in `InvoiceLineDecisionService` | **Stopped.** The line row stores no decision id (status, approved amount, reason, reviewed by, reviewed on), so a repeat cannot be told from a new decision on an already-decided line. It needs a design choice (a decision marker on the row, or a record-store lookup); not a contained reorder | none | n/a |
| F3 | T7 claimed the approval seam without driving it | T7 renamed `T7_TheAddedLine_IsCreatedFlagged`. New seam test through `InvoiceApprovalService` | `T7_ApprovalWithAnAddedLine_IsRefusedUntilDecided_ThenStoresTheAddedLineRule` (Flagged: 409 with the open-lines count; Accept all Clean leaves the added line Flagged; accept: approve stores 1200; reduce to 150: 1150) | n/a |
| 4 | The same command by another decider | Step 1 compares the row's `sprk_createdbyperson` with the caller: another caller is `id_reused`, nothing written | `ARepeat_ByAnotherDecider_IsIdReused_AndTheRecordNamesTheFirstCaller` (one record, the first caller) | see K2-b |
| K2-a | The reviewer-added marker was not tied to the line's invoice | `AddedLineIdFor(invoiceId, decisionId)`; `IsReviewerAdded` also requires a non-empty `sprk_invoice` and `line.Id == AddedLineIdFor(line.sprk_invoice, decisionId)`. The marker stays numbers and flags (no request id). The readers that call `IsReviewerAdded` now select `sprk_invoice` (`InvoiceReviewReadService.LineColumns`, `InvoiceTotalsService.ReadLinesAsync`, `ApExportService.ReadLinesAsync`) | `ExtractionProvenanceReviewerAddedTests.AnAddedRow_MovedToAnotherInvoice_ReadsFalse`, the distinct-per-invoice id test; review-read, totals and AP export tests updated to carry `sprk_invoice` | the id without the invoice: 2 fail |
| K2-b | A repeat written before the record by another decider | `RecordCreatorPerson.Column` added to `ExistingColumns` | `ARepeat_ByAnotherDecider_BeforeTheRecord_WritesNoRecord` | the creator compare forced true: 1 fails |
| K2-c | Every `ReviewInvalid` from the recorder was `id_reused` | Only the writer's "same code, other content" refusal (`DecisionRecordWriter.BillingOtherContentRefusal`) is `id_reused`; any other refusal is Incomplete (`record`) | `ARepeat_WhoseRecordedContentIsUnreadable_IsNotIdReused`; `DecisionRecordWriterTests.Billing_K2_ReadableOtherContent_IsTheOtherContentRefusal` and the no-hash test's message | any `ReviewInvalid` mapped: 1 fails |
| K3 | The correction's log lines lost the `Field` property in the move | `InvoiceRuleSignalReconciler` takes the field (null for Add line); the correction logs "Correction of {Field} on invoice {InvoiceId}", Add line "Add line on invoice {InvoiceId}" | `InvoiceRuleSignalReconcilerTests.TheCorrectionContext_KeepsTheFieldProperty`, `TheAddLineContext_LogsItsOwnWording` | the pre-fix files: 4 of 4 fail |
| K1 | These notes said both reconciles run under the per-invoice lock | Corrected in "The 4a seam" and in the class remarks: a correction takes no processing lock; Signal writes are unique on `sprk_dedupekey`, closes and the entry removal are conditional, and the loser fails its step (503) and converges on its repeat | n/a | n/a |
| 10 | Every add-line, reduce and disallow record carries its regarding link | Already true: the writer maps the line codes to subject `sprk_billingevent` and `ValidateBilling` refuses a mismatched subject, so the regarding invoice-line lookup is always set. No fix needed | `TheAddLineRecord_AlwaysCarriesTheRegardingLineLink`; `InvoiceLineDecisionServiceTests.AReduceOrDisallowRecord_AlwaysCarriesTheRegardingLineLink` (reduce, disallow) | n/a |
| 11 | Seeds S1, S2, S4 | Re-run; counts in `164a-4a-service.md` | n/a | S1 2/10, S2 2/3, S4 20/20 |

**Change in `Services/Signals/**`.** `DecisionRecordWriter` gains two `internal const` messages, `BillingOtherContentRefusal` and `BillingContentUnreadableRefusal`; the existing billing `ReviewInvalid` throw picks one by whether a same-code record has no readable hash. Same reason, same condition. `WriteAsync` byte-identical.

**The reviewer-added rule, for 165b.** `IsReviewerAdded(line)` holds iff the row's `sprk_extractionconfidence` has `provenance.source == "reviewer"`, its `decisionId` parses as a non-empty Guid, the row has a non-empty `sprk_invoice`, and `line.Id == ExtractionProvenance.AddedLineIdFor(line.sprk_invoice.Id, decisionId)`, where `AddedLineIdFor(invoiceId, decisionId) = IdOf("ebilling-added-line-{invoiceId:D}-{decisionId:D}")`. A reader must select `sprk_invoice` with the marker.

**Merge points with 165a.** `InvoiceCorrectionReevaluation.ReadSubmissionAsync` (165a has its own own-submission read in `InvoiceReviewReadService`; consolidate at merge, not copied here), `CorrectionWorld`, `ApprovalWorld` and `InvoiceCorrectionEndToEndTests` (the same derived-id lines).

**Behaviour choices made in the fix round.** A repeat on an invoice no longer In Review answers 200 `alreadyApplied` with totals computed from the rows (no write); `addLine` stays `committed`. A repeat while a correction is pending or the firm is held, with the invoice In Review, runs steps 7 and 9. A held lock is still 409 busy.
