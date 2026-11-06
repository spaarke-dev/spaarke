# 164a-4b: the Add line service, steps 7 to 9, and the resume

Branch `task/ebilling-164a4a` (continued from 164a-4a at `be3e15358c`). Rigor FULL. Steps 0 to 6 and the route are 164a-4a (`164a-4a-service.md`).

## What landed

- **`InvoiceRuleSignalReconciler`** (new file, `Services/Finance`): the correction re-evaluation's rule reconcile of `pendingSignalChanges.rule` (`ReconcileRuleAsync`, `CloseClearedAsync`, `WriteAsync`, `RecordWriteRefusals`), moved out of `InvoiceCorrectionReevaluation` unchanged and created per run. The correction's Signals step and Add line's step 9 both use it. Log lines take a label; a correction's renders as before ("Correction of {field} on invoice ...").
- **`SignalLogContext.AddLine`** in `Submission/SignalEpisodeLifecycle.cs` (the seam the POML names): two log lines only.
- **`InvoiceLineAddService`**, after the commit, with `CancellationToken.None`:
  - **Step 7, totals:** `IInvoiceTotalsService.RecalculateAsync` (016, EB-D-199), then the re-index (`InvoiceIndexingJobHandler.TryEnqueueAsync`, change key `a{decisionId:N}`, best effort).
  - **Step 8, record:** one `add-line` record through `BillingDecisionRecorder.RecordAsync`, idempotent on the decision id.
  - **Step 9, Signals:** re-read the latest submission, reconcile each `rule` entry through the reconciler (`SignalLogContext.AddLine`), then ONE row-version-conditional write that removes the entries, records the writer's permanent refusals and sets `addLine.state = "done"`. Nothing is written when nothing changed.
  - A fault at a step is `AddLineStatus.Incomplete`: 503 `ebilling.decision.incomplete` with `failedStep` (`totals`, `record` or `signals`), `requestId`, `lineId` and `decisionId`.
  - The 200 body now carries `totals` (billed, fees, expenses, line and invoice adjustments, approved).
- **The resume.** A repeat finds the derived row with the same content (step 1) and goes to `ResumeAsync`:
  - the decision is rebuilt from the row (sequence, as-billed codes, resolved timekeeper) and the stored findings (rule reason codes); its hashed members (code, subject, decider, role, outcome, reason, empty resolved set) are what the first attempt had, so the record is the same record;
  - when `addLine` names this decision at `done`, the totals are computed from the rows (no write) and step 9 is skipped;
  - otherwise steps 7 to 9 run again (each is idempotent);
  - step 8 runs on EVERY repeat, so the recorder's hash refuses the same decision id with another reason or decider; that is answered 409 `id_reused` (the row does not store the reason, so the row compare cannot see it).
- **Endpoint:** `ToResult(AddLineResult, ...)` maps `Incomplete` to the 503 problem above.
- **Texts (G2):** the `FindingsJsonAppOnlyReadGuardTests` comment and row 15 of `notes/122-findings-json-field-secure.md` name the step-9 write. No new reader type.

## The 4a seam, decided

| Question (from 164a-4a) | Decision |
|---|---|
| Do the plan changes move to `addLine.signals`? | No. They stay in `pendingSignalChanges.rule`, where the correction's Signals step also reads them. Whichever runs first reconciles an entry and removes it in its conditional write; the other finds it gone. Every close and write is idempotent, so a race converges and nothing is reconciled twice to a different end. Both run under the per-invoice lock or are serialized by the submission row version. |
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
| T7 I-2 | `T7_TheAddedLine_IsOpenForApprovalUntilDecided` asserts the added line is created Flagged; the approval's I-2 refusal of any Flagged line and Accept all Clean's Clean-only selection are the existing approval and line-decision suites (the added line is an ordinary Flagged row to them). See deviation 2. |
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
| S1, S2, S4 | 164a-4a (route filter, firm gate, internal-caller group) | not re-run; no change to those paths |

## Live check for the dev verification (not run: no live writes)

The commit includes a version-only conditional update of the invoice (`UpdateRequest`, no columns, `IfRowVersionMatches`). It is assumed that Dataverse refuses it when the invoice's row version changed since the read. Check on dev: read an In Review invoice, change it, then send the add with the old version; expect 409 `ebilling.line_add.changed` and no line. If Dataverse ignores a column-free conditional update, that is the POML's escalation trigger: STOP and propose an alternative (for example, a no-op write of an unchanged column).

## Deviations

1. **Refactor beyond the named seam, inside `Services/Finance`.** The rule reconcile was moved, not copied, into `InvoiceRuleSignalReconciler` so step 9 reconciles through the correction's own code (constraint: rules and Signals only through the extracted code, never a copy). No file under `Services/Signals/**` changed; `DecisionRecordWriter.WriteAsync` is untouched.
2. **T7** asserts only that the added line is Flagged; it does not drive `InvoiceApprovalService` (its `OpenLines` is private, and making it visible would edit a file outside this task's list). The I-2 behaviour for a Flagged line is the approval suite's.
3. **C1-C4 and P1-P2** are the client (164b) and the ribbon script (164c), not 164a; not built here.
4. **A repeat with another reason after a fault at step 7** recomputes the totals (idempotent, same figures) before step 8 refuses it with `id_reused`.
5. **A first attempt that never wrote its record**, repeated with another reason, records the repeat's reason (there is nothing to compare it with).

## For ontology-r1 (review set)

- `Services/Finance/Submission/SignalEpisodeLifecycle.cs`: `SignalLogContext.AddLine` and its two log lines.
- `Services/Finance/InvoiceRuleSignalReconciler.cs`: the moved rule reconcile (episode rules unchanged: refresh on the same fingerprint, Superseded then n+1 on another, ConditionCleared when the group is gone, ReopenAsNextEpisode for a resolved latest episode).
- No hold link (Q7). `ResolvedSignalIds` stays empty. `Services/Signals/**` unchanged.

## Publish size (CLAUDE.md section 10)

Measured 2026-11-06 with `dotnet publish src/server/api/Sprk.Bff.Api -c Release` into an empty folder, summing every file:

| Build | Bytes | `Sprk.Bff.Api.dll` |
|---|---|---|
| Fresh `origin/master` (`bcdc0e0069`, exported with `git archive`, built from clean) | 136,791,493 | 24,913,920 |
| This branch at the steps 7-9 commit | 137,659,097 | 25,671,168 |
| Delta | +867,604 (about +0.83 MB) | +757,248 |

The delta is the whole unmerged `work/ebilling-r1` line plus 164a, not this task alone; it is all in `Sprk.Bff.Api.dll` and no file was added or removed from the output (no package). It is below the +5 MB justification threshold. No package, DI registration or background work was added by 164a-4b; the service's constructor takes `IInvoiceTotalsService`, `SignalWriter` and the optional `JobSubmissionService`, all already registered.
