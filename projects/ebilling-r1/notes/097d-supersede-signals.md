# Task 097d: the after-step closes the superseded invoice's open Signals

Part four of task 097 (`tasks/097-corrected-invoice.poml`), the last code part. Decisions: EB-D-179, EB-D-174; model 9.1 "Existing invoice"; D-122 (the Signal set). Base `6beb102634` (097c's head, with 097a and 097b); branch `task/ebill-097d-supersede-signals`. Rigor FULL (code change in the BFF after-steps; the task is tagged auth). 097e (prefill) is separate.

## Escalation checks (none fired)

- **A Superseded close reason exists:** `SignalWriter.ResolutionSuperseded` = 100000003, a system resolution that `CloseAsync` accepts (`SignalWriter.cs:411`, `:415`).
- **No new Signals capability:** the step calls the after-steps' existing Superseded close delegate, one Signal at a time. There is no new writer entry point and no helper that closes a set.
- **The step can run before the terminal status:** it sits inside `RunFromStoredAsync`, which both the first run and the resume reach only after the commit gate passed. The first run reaches it after the persister's gate (`InvoiceSubmissionPersister.cs:756`); the resume runs `ResumeAsync`'s gate first (`InvoiceSubmissionAfterSteps.cs`, model 9.1 step 6).

## What changed

| Area | File | Change |
|---|---|---|
| The step | `InvoiceSubmissionAfterSteps.cs` (`CloseSupersededInvoiceSignalsAsync`, step "2b" in `RunFromStoredAsync`) | Runs after the Signals and the automatic rejection's record, and before the totals Retry, the index job, the notification and the terminal status. Order of checks: no `sprk_resubmissionof` returns at once with no read (a New submission, 104's automatic link included). Next, the old invoice is resolved through `SubmissionSnapshot.ReadCorrectedInvoiceAsync(entities, earlier, ct)` (097b/097c, fail closed). Next, this submission's invoice must name it in `sprk_supersedesinvoice`, and the old invoice must be Superseded. If any check fails, nothing is closed (one Information line, ids only). Then the open set on the old invoice and its lines is read live (`ReadOpenSignalSetAsync`, D-122: Open or Acknowledged, every policy, subject checked from the row). Each Signal is re-read (status, row version) just before its close. A Signal that is no longer open (a person or an earlier run closed it) or that no longer exists is left as it is. A still-open Signal is closed through the existing delegate at the version re-read. |
| Failures | same | A read fault (target, invoice, set) returns Retry `ebilling.submission.supersede_signals_failed` before anything is closed. A close fault is remembered and the other Signals are still tried; the step then returns Retry with the same code. A `ChangedSinceRead` writes nothing and returns Retry `ebilling.submission.signal_changed` (decision below). So does a Signal read without a row version. Retry leaves the submission Processing with its link; the redelivery resumes, re-runs the gate, and re-reads the set, so a Signal already closed is not in it and nothing is closed twice. |
| The binding | same (`SupersedeClose(SignalWriter)`) | The existing lambda at the public constructor (`CloseAsync(…, ResolutionSuperseded, …)`) moves into a named internal factory, so a test can reach the exact delegate. It is still one binding, shared by the option-a lifecycle and this step. The seam stores it in `_supersedeSignal` as well as handing it to `SignalEpisodeLifecycle`. |
| Submission read | same (`SubmissionColumns`) | Adds `sprk_resubmissionof`. |
| New code | same | `SupersedeSignalsFailedCode` = `ebilling.submission.supersede_signals_failed` (Retry only; never stored as the submission's reason; on the final delivery it appears in the administrator's "Processing stopped" note). |

No new endpoint, schema change, package, DI registration or App Service setting. The only new Dataverse writes are the Signal closes, made by the existing writer.

## Decisions

- **ChangedSinceRead is a Retry, not "left and done" (deviation from POML criterion `:115` and constraint `:46`).** The POML assumes ChangedSinceRead means "a person resolved it". The step now re-reads each Signal just before closing it. ChangedSinceRead therefore means a write landed in the few milliseconds between the re-read and the close. That write may have been an Acknowledge, which leaves the Signal open. Billing Signals have no sweep: `SignalReevaluationJob` covers Ontology policies only, and both the billing evaluation and 101's re-evaluation skip a Superseded invoice. So a Signal left open there would stay open for good. The step writes nothing and returns Retry. The next delivery re-reads: a person's close is left as it is, and a still-open Signal is closed. The cost is one extra delivery. The adversarial review agreed. **ontology-r1 approved it on 2026-10-30 on two conditions, met in the follow-up below.** POML `:46` and `:115` are amended there ("(amended 2026-10-30, ontology-r1 OK)").
- **Both ids are used.** `sprk_resubmissionof` finds the old invoice. The new invoice's `sprk_supersedesinvoice` shows that this submission's commit is the one that superseded it. An automatically rejected correction links nothing (persister `:532`), so it closes nothing even when another correction later supersedes the same invoice.
- **Placement before the totals Retry.** The step's own failure returns before the totals code would. Both are Retry, and the resume recalculates the totals anyway.
- **A missing old invoice or an unresolved target is done, not a Retry.** There is nothing to close, and a retry cannot change the answer (097c treats the undo the same way).

## Component justification (§11)

- **`CloseSupersededInvoiceSignalsAsync`** (private). Existing: `CloseResolvedSetAsync` (closes a set as Acted by a record) and the option-a lifecycle's single Superseded close. Extension: one more after-step in the same runner, using the shared set read and the existing delegate; no new service. Cost of doing nothing: model 9.1 fails, because the superseded invoice keeps open, decidable findings that nothing else closes (no billing sweep; evaluation skips Superseded).
- **`SupersedeClose(SignalWriter)`** (internal static). The existing lambda moved, not a second binding. Cost of doing nothing: no test can show which resolution this path writes (POML criterion "test asserts the resolution").
- **`SupersedeSignalsFailedCode`**. Existing: the step-specific Retry codes beside it (`IndexEnqueueFailedCode`, …). Cost of doing nothing: an administrator's "Processing stopped" note could not say which step stopped.

## BFF hygiene (§10)

Placement: in the BFF, in the existing ADR-004 submission job's after-steps (the only post-commit runner of a submission). No endpoint, package, DI registration or new background work. Publish size and CVE: see "Gate".

## Tests

New `tests/unit/.../Submission/InvoiceSubmissionAfterStepsSupersedeTests.cs`, a partial of `InvoiceSubmissionAfterStepsTests` (17 cases), on the existing `World`. `World` gained one hook, `SupersedeFaults` (a close that throws once), and the class became `partial`. The world adds the earlier submission, the two invoices, the old invoice's line, and the Signal re-read. Every read returns only the columns asked for.

The brief's list:
- A superseded invoice is left with no open Signal: its Open and Acknowledged Signals, and its line's, close through the Superseded delegate; never `CloseActedByDecisionAsync`; the new invoice's own Signal stays open; the terminal status is written after every close.
- Signals a person closed are left as they are, both before the set read and between that read and the close (the re-read sees it; nothing is written).
- An old invoice that is not Superseded (In Review, Approved, Returned to Firm) closes nothing and reads no Signal.
- An old invoice superseded by a different invoice (this invoice links another, or none as after an automatic rejection) closes nothing. The link is checked before the old invoice is read.
- The close fails once mid-way: Retry `supersede_signals_failed`, no terminal status and no notification, the other Signals closed. The resume closes the rest, and no Signal is closed twice.
- A New submission with 104's link (and an old invoice that is even Superseded) reads nothing and closes nothing.
- An undone commit (the resume's gate Undone or UndoHeld) closes nothing and reads no invoice.

Beyond the brief, with the reason:
- ChangedSinceRead: Retry, nothing written, and the resume closes it. This is the POML criterion, with the decision above.
- A read fault on the old invoice, and on the open set: Retry with nothing closed, and the resume completes (brief item 3, "any failure").
- A target that no longer resolves, and an old invoice that no longer exists: done with nothing closed, so they do not Retry forever. Added at the review's request.
- `SupersedeClose` over a real `SignalWriter` (fake Dataverse): the delegate this step calls writes resolution Superseded with no `sprk_resolvedby` and no `sprk_decisionrecord`, and a stale version is `ChangedSinceRead`. This is the POML criterion "test asserts the resolution".

**Fail before.** The step was switched off (its call made unreachable). Then 5 of the 13 cases at the first commit failed; the 8 controls passed (not Superseded ×3, another invoice ×2, New, undone ×2). Restored: all pass.

**Seeding proof.**
- "Superseded by THIS invoice" (the brief's one): with the `sprk_supersedesinvoice` condition disabled, both `Correction_SupersededByAnotherInvoice_ClosesNothing` rows fail. Restored: pass.
- The resolution binding: switching `SupersedeClose` to `ResolutionConditionCleared` makes `SupersedeClose_WritesResolutionSuperseded_NoPerson` fail. Restored: pass.

## Review (one adversarial pass, Opus)

No F-class finding. Re-derived as clean:
- the gate runs before the step on both paths;
- the step runs before the totals Retry, the index, the notification and the terminal status;
- an automatic rejection and a New submission (104 included) close nothing;
- only the existing delegate closes;
- a resume closes nothing twice;
- logs carry ids and codes only.

| # | Class | Finding | Resolution |
|---|---|---|---|
| 1 | — | ChangedSinceRead returns Retry, against POML `:115` / `:46`. The reviewer judged Retry correct (an Acknowledge in the window would otherwise leave an open Signal forever). | Kept. Decision recorded above; POML amendment flagged for the main session. |
| 2 | K | After the step closes the Signals, a later Retry (index enqueue, terminal conflict) followed by a resumed gate that fails, because the invoice's files changed out of band, undoes the commit. The old invoice reopens In Review but its Signals stay closed Superseded. | Known limit K1 below. |
| 3 | K | A 101 field correction already past its In Review check when the commit supersedes the invoice can write a new open episode on it after this step closed the set. | Known limit K2. |
| 4 | K | A close that keeps failing holds the new submission (terminal status, notification, index) on another invoice's Signals; after the final delivery it stays Processing with the administrator note. A Signal read without a row version takes this path on every delivery (not reachable on real Dataverse reads). | Known limit K3 (as the brief requires: any failure is Retry). |
| 5 | F2 (fixed) | Missing tests: unresolved target, old invoice gone, open-set read fault; no test showed the resolution written (the test double does not see it). | Fixed: the three cases above, and `SupersedeClose` plus its real-writer test. Seeded. |
| 6 | K | Trust: `sprk_resubmissionof` and `sprk_supersedesinvoice` are ordinary columns. Rewriting both could only close open Signals on another invoice that is already Superseded, as Superseded, which is the correct state for it. The old id is derived (`InvoiceIdFor`), not free. | No change; see 097b K6. |

## Gate

Measured on head `43ac7819af` (before the notes commit).

| Step | Result |
|---|---|
| Build, 5 test projects (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests) | 0 warnings, 0 errors each |
| Unit filter `Finance\|Ebilling\|Billing\|Signals\|Invoice\|Submission\|Correction` | 9554 passed, 0 failed, 7 skipped |
| ArchTests (full) | 1858 passed, 0 failed (ISS-005 did not flake) |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 0 failed, 8 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; no package or project file changed |
| Overlap with `origin/work/ebilling-r1` since the merge base `932efea9cc` | no commit there touches the two changed files |

**Publish size (§10).** `dotnet publish -c Release -r linux-x64 --self-contained false`, base `6beb102634` (`C:\p097db`) against head `43ac7819af` (`C:\p097dh`). Both were exported with `git archive` (src, config, the root props, global.json) to short paths and zipped with `Compress-Archive -CompressionLevel Optimal`. Base 41,078,175 B, head 41,080,692 B: **+2,517 B** zipped. 192 files on both sides. `Sprk.Bff.Api.dll` +6,656 B.

## Known limits (097d)

- K1: an undo after this step ran (only possible when the invoice's files change outside the pipeline between two deliveries) reopens the old invoice but not its Signals. Fix candidate: the undo's reopen also reopens Signals the step closed Superseded at or after the commit. Sits beside 097c's K5.
- K2: a field correction (101) in flight on the old invoice at the moment it is superseded can raise a new open episode after the close. Nothing closes it unless this submission resumes again.
- K3: a persistent close or read failure holds the new submission at Processing (by design: any failure is Retry). On the final delivery the administrator's note names `supersede_signals_failed` or `signal_changed`.

## Follow-up: ontology-r1's conditions for ChangedSinceRead → Retry (2026-10-30)

Branch `task/ebill-097d-followup`, cut from `70f453942a`; `origin/work/ebilling-r1` did not yet contain 097d.

### 1. Bounded and visible

**What already happened.** A Retry from the after-steps maps to `TransientAsync` (`InvoiceSubmissionProcessingJobHandler.cs:445`, `:567-611`).
- **Before the final delivery:** `Failure`, so the broker redelivers. The redelivery finds the submission Processing and linked, and resumes it (`:272`): gate first, then the after-steps, then this close.
- **Bounded:** on the final delivery the job is `Poisoned` with the reason code as its error message (`:610`). That is the dead-letter, and it carries `signal_changed` or `supersede_signals_failed`.
- **Logged:** a Warning names the job, the code and the submission id (`:587-589`).
- **Visible:** because the invoice is linked, the submission is never written Failed. It stays Processing and gets the "Processing stopped: <code>; …" administrator note in `sprk_adminattention` (`:590-595`, R3 / D-133). It never sits at Processing silently.

**What was missing, now added.** No Warning named the Signals left open. The `signal_changed` path logged no Signal id at all, and the handler cannot know the ids. The step now logs ONE Warning on every failing run, the final delivery included (`InvoiceSubmissionAfterSteps.cs:1090-1096`). It names the submission, the old invoice, the code and exactly the Signal ids not closed (ids and codes only, ADR-015). A read fault fails before any Signal id is known, so its existing Warning (`:1033`) names the submission and the fault type only.

**Tests.**
- `InvoiceSubmissionAfterStepsTests.Correction_FailingRun_WarnsWithTheSignalsLeftOpen` (`changed`, `fault`): one Warning with the submission id, the code and the stuck Signal's id, and not the id of the Signal that closed. Fail before: with the Warning removed, both rows fail; restored, both pass.
- `InvoiceSubmissionProcessingJobHandlerTests.SupersedeCloseRetry_OnTheFinalDelivery_IsDeadLetteredAndVisible` (both codes): an earlier delivery is `Failed`, redelivered, and writes nothing. The final delivery is `Poisoned` with `ErrorMessage` = the code, no status write, still Processing, `sprk_adminattention` = `StoppedNote(code)`, and a Warning with the submission id and the code. This pins existing behaviour (the general pattern was pinned only for `signal_write_skipped`), so it passes before and after.

### 2. Idempotent re-runs

A Retry ends the delivery. The redelivery resumes through `ResumeAsync`, which re-runs every step before the supersede close (`InvoiceSubmissionAfterSteps.cs:492`):

| Step (order on a resume) | Code | Why a re-run changes nothing | Test |
|---|---|---|---|
| Commit gate | `:285`; `SubmissionCommitGate` | Once a file is Servable the source re-run is skipped and nothing is flipped twice. A completed undo run again deletes and writes nothing. | `InvoiceSubmissionPersisterTests` "094 step 6 (must not fire): once a file is Servable the re-run is skipped…" and "094 (uac-r2 condition 3): the compensation is idempotent…"; `InvoiceSubmissionAfterStepsTests` "094 (model 9.1 step 6): a resume runs the commit gate FIRST…" |
| Totals (016) | `:297`; `InvoiceTotalsService.RecalculateAsync` | Writes the totals computed from the lines (absolute values, conditional on the version read), never an increment. An adjustment line already right is left alone. | `InvoiceTotalsServiceTests.Recalculate_WritesTheFiveTotals_ConditionallyOnTheVersionRead_AndTheAdjustmentLineAmount`, `Recalculate_LeavesAnAdjustmentLineAlone_WhenItsStoredAmountIsAlreadyRight`; `InvoiceSubmissionAfterStepsTests` "049 13a A resume: a totals fault on resume … the next resume completes" |
| Totals refusal record | `:368` | No write when the stored member already holds the same code. | "049 13a A: a totals refusal is recorded in the findings JSON once (idempotent across a resume)…" |
| System Signals and the policy note | `:381` | The attention text is written only when it changes. | "049 R5: the policy note is idempotent - a redelivery that finds the same misconfiguration writes the attention column once" |
| Signal writes (option-a lifecycle) | `:399` | Same fingerprint and open: refresh in place. Same fingerprint and resolved: skipped. The dedupe key makes a write exactly-once per episode. | "049 D lifecycle: a resolved Signal with the same fingerprint is skipped (a resume never re-raises it)"; "049 D resume: a crash at each step … the redelivery completes with no duplicate episode, one record, one notify, one terminal write" |
| Signal write refusals record (F1-3, D-133) | `:423` | Recorded once; a resume that finds them stored writes nothing new. | "049 F1-3: a permanent refusal is recorded once across a resume (idempotent)…"; "049 D-133: a resume that finds the refusal stored writes nothing new…" |
| Automatic rejection: the stored set, the Decision Record, the Acted closes | `:440-487` | AutoRejected only. The set is stored once and never re-read. The record id is deterministic (`RejectionDecisionIdFor`), so the writer's exactly-once applies. A close already made by this record is `AlreadyClosedByThisRecord`. An AutoRejected correction supersedes nothing, so it never reaches a supersede Retry. | "049 13c: the set is stored BEFORE the record; a crash between them resumes … with no fresh read…"; "049 13c: a resume after a partial close set re-closes from the stored set only; each Signal is written once"; "049 13c: ChangedSinceRead on a close is a Retry … the redelivery completes" |
| The supersede close itself | `:492` | It re-reads the open set, so a Signal already closed is not in it. | `Correction_CloseFailsOnce_RetryThenResumeCompletes` (`OnlyHaveUniqueItems`), `Correction_ChangedSinceRead_LeftAsIs_RetryThenResumeCloses` |

The steps after the close run only on the completing run, and are idempotent too: the index job by its key ("152: a redelivery (resume) queues the same index job again; it is idempotent by its key"), the notification by its marker ("049 D: the notification is sent once per submission…"), and the terminal status only while Processing ("049 R7: a redelivery after completion writes nothing more").

**Result: every earlier step is idempotent on re-delivery; no new guard was needed.**

### 3. POML

`tasks/097-corrected-invoice.poml`: the constraint "model 9.1 (ontology-r1 v5.8)" and the criterion "Idempotent close" now read "ChangedSinceRead writes nothing and returns Retry; the next run re-reads (amended 2026-10-30, ontology-r1 OK)".

### Gate (follow-up)

Measured at `21533910b6` (code and tests); the POML and notes commits change no code.

| Step | Result |
|---|---|
| Build Sprk.Bff.Api.Tests and Spaarke.ArchTests | 0 warnings, 0 errors |
| Unit filter `Submission\|Signals\|Invoice` | 7628 passed, 0 failed, 6 skipped |
| ArchTests (src touched) | 1858 passed, 0 failed |
| Re-run of the two touched classes after the commit hook's `dotnet format` | 280 passed, 0 failed |

Publish size is not re-measured: one log statement and a list, on top of the +2,517 B above.

## 097 summary (parts a to d)

| Part | Branch | What it does |
|---|---|---|
| 097a | `task/ebill-097a-superseded-status` | Status Superseded (659490005, Inactive) in the lifecycle. The only moves into and out of it are two request builders used inside a transaction: In Review → Superseded, and its undo. Every status reader treats it as closed. The Duplicate rule no longer counts a Superseded invoice as a prior. |
| 097b | `task/ebill-097b-correction-intake` | Correction mode on both Submit Invoice routes, checked at intake: the caller's own Write; same firm; one and the same core record; In Review only; the invoice's own submission; the same firm invoice number. `sprk_resubmissionof` is written. A correction raises no Duplicate against the invoice it replaces. |
| 097c | `task/ebill-097c-supersede-commit` | At commit: the target is re-checked (firm, core record, In Review), the new invoice links it (`sprk_supersedesinvoice`), and the old invoice closes Superseded in the same transaction, guarded on its row version. An approval race fails the submission with `invoice_status`. The undo reopens the old invoice. The old invoice is re-indexed. |
| 097d | `task/ebill-097d-supersede-signals` | After the gate and before the terminal status, the old invoice's and its lines' open Signals close Superseded. Durable (Retry), idempotent, and never for a New submission, an automatic rejection or an undone commit. |

**Error codes the clients must map** (word-add-in `invoiceSubmissionMessages.ts` `INVOICE_OUTCOME_MESSAGES`, the external SPA; 095 contract §9):

| Code | Where | Meaning |
|---|---|---|
| 409 `ebilling.submission.correction_not_allowed` with `reason` = `different_firm`, `different_core_record`, `invoice_status` (+ `invoiceStatus`), `no_submission`, `different_invoice_number` | intake, both routes (097b) | the correction is refused before anything is written |
| 404 reasonCode `sdap.access.deny.record_unavailable` | intake (097b) | no Write on the invoice, or it does not exist (uniform answer) |
| 400 `invoiceId` / `mode` | from-document intake (097b) | inconsistent mode and invoice id |
| stored `ebilling.submission.correction_not_allowed.invoice_status` | submission reason code, Failed (097c) | the old invoice was no longer In Review at commit (an approval race included) |
| stored `ebilling.submission.correction_not_allowed.different_firm` | Failed (097c) | the target became another firm's before the commit |
| stored `ebilling.submission.correction_not_allowed.different_core_record` | Failed (097c) | the target moved to another core record (or to several) before the commit |
| stored `ebilling.submission.correction_target_unavailable` | Failed (097c) | the target no longer resolves (earlier submission gone, unlinked, or invoice deleted); never committed as New |
| `ebilling.submission.supersede_signals_failed`, `ebilling.submission.signal_changed` | Retry only (097d); administrator note on the final delivery | not a stored submission reason; no client mapping needed |

**Known limits collected from 097a to 097d**

- 097a: the builders answer "already in target" with `NotAllowed`, and only `Current` tells it apart from a forbidden move (K4). A corrected held invoice's submission still reads Blocked with no action (K4; open owner question, below). The undo's statecode 1 → 0 conditional update inside `ExecuteTransaction` is to be verified on the first live run (K2).
- 097b: the external multi-root rule withholds a submission whose `sprk_resubmissionof` no longer resolves (K1). The guard's submission query has no row cap (K2). The per-user idempotency cache can replay a caller's own 202 after they lose Write (K4). A Write-probe fault answers the uniform 404, not 503 (K5). `sprk_resubmissionof` and `sprk_firminvoicenumber` are ordinary columns (K6).
- 097c: the new stored codes have no client mapping yet (K1). The old invoice's re-index is best effort (K2). A Superseded invoice whose version cannot be read holds the reopen (K3). A target whose own submission has an unfinished undo is not refused (K4). A successor deleted outside the undo makes the old invoice look reopenable (K5). On the final delivery, a race to Rejected, Cancelled or Returned to Firm ends `transaction_failed` (K6).
- 097d: K1 to K3 above.
- **Open owner question (unchanged):** what the old submission shows once its invoice is Superseded (097a finding 5, 097c item 6). Left as is.
- **Release condition (097b, ontology-r1):** 097b reaches master only together with 097c (and now 097d). word-add-in-r1 keeps Correction off until 097b, 097c and 097e are on master.

## Second review fixes (2026-10-30)

Branch `task/ebill-097-review2-fixes`, cut from `7e6cd95787` (097a to 097d and the 097d follow-up). A second independent adversarial review of 097 found the items below. Each fix has a test that fails before it and passes after. The task is tagged auth; the fix diff and its direct callers were re-checked (no separate full review, as directed).

| # | Finding | Fix | Test |
|---|---|---|---|
| F1 | A correction was accepted while the old invoice's own submission was still running. `CheckCorrectionAsync` found that submission but never read its status, and neither did the commit-time check. | `InvoiceSubmissionService.CorrectionTargetSubmissionFinished`: only Accepted (any reason: processing, an override, uncoded), or Rejected with `ebilling.submission.blocked` (held Blocked, D-101, the same model as `FinanceBaseValues.HeldSubmissionConditions`) has finished. Received, Processing (a Processing row carrying the Blocked code included: its after-steps still run), Failed, Rejected with any other code, and an unknown or unread status have not. The intake reads `sprk_processingstatus` and `sprk_outcomereasoncode` (`CorrectionSubmissionColumns`). If it has not finished, the intake answers 409 `correction_not_allowed` with the new reason `submission_in_progress`. That reason is checked last, after the number, and needs a distinct name: the invoice itself is In Review, so `invoice_status` would name the allowed status. At commit, `CheckCorrectionTargetAsync` re-reads the earlier submission (`CorrectionTargetSubmissionColumns`); if it has not finished, the result is Refused (the submission ends Failed) with `ebilling.submission.correction_not_allowed.submission_in_progress`, and no transaction is sent. That is 097c's commit-time refusal, not a Retry, because the old submission may run a long time; the same request after Failed makes a new attempt (095 contract section 5). | Intake: `Refused_TheInvoicesSubmissionHasNotFinished` (Processing, Received, Processing+Blocked, Failed+source_check_failed, Rejected+returned, none), `Allowed_TheInvoicesSubmissionHasFinished` (Accepted, Accepted+overridden, Rejected+blocked), `FromDocument_TheInvoicesSubmissionIsProcessing_IsRefused_NothingSaved`, contract row `submission_in_progress`. Commit: `EarlierSubmissionNotFinishedAtCommit_IsRefused_NothingSent` (5 rows), `EarlierSubmissionFinishedAtCommit_Commits` (3 rows). |
| F2 | The commit trusted `sprk_resubmissionof`. | At commit the earlier submission's `sprk_firminvoicenumber` must equal this submission's typed number. The comparison is `InvoiceSubmissionService.SameFirmInvoiceNumber`: trimmed, ignoring case, and a missing number is never equal. The intake now uses the same helper. A mismatch is Refused `ebilling.submission.correction_not_allowed.different_invoice_number`, with nothing sent. 097b K6 is rewritten. | `EarlierSubmissionAnotherNumberAtCommit_IsRefused_NothingSent`, `EarlierSubmissionNumberAtCommit_ComparedAsTheIntakeDoes` (`"  inv-100 "` commits, none refused). |
| F4 | The (a′) acceptance criterion (POML `:117`) had no test. | Test only. 098's predicate (`ExternalInvoiceVisibility.DecideAsync`, through `ExternalDataService.HideInvoicesForAsync`) reads each invoice's Stored Files and submissions by `sprk_invoice` and never reads the invoice row, so it cannot follow `sprk_supersedesinvoice`. No production change, and no stop. | `ExternalInvoiceVisibilityTests.Correction_EachInvoiceIsJudgedByItsOwnSource_NotThroughSupersedesInvoice`: a pair linked by `sprk_supersedesinvoice` and `sprk_resubmissionof`, under one record. Hiding the old invoice's source leaves the new invoice visible, and the reverse. Covered for a contact and a flagged-external systemuser, asked together and alone. `sprk_invoices` is never read. It passes before and after: it pins existing behaviour. |
| K4 | The after-step retried forever if the submission's own invoice was gone. | `CloseSupersededInvoiceSignalsAsync`: a not-found read of the submission's own invoice is done, with one Information line (ids only), as the old invoice's read already was. | `Correction_TargetUnresolvedOrGone_ClosesNothing("own-gone")`: Completed, nothing closed, and the old invoice is not read. |
| K5 | Contract note only. A from-document body with any other `mode` (for example lower-case `"correction"`) is now 400 `mode`; before 097b it was ignored. | Lines added to `094-submit-invoice.md` (118 body) and `095-from-email-contract.md` section 6. No code change. | — |

Also: 097c K4 (an old invoice whose submission's undo is unfinished) is closed by F1, because a Failed submission has not finished. The 097c note is updated.

**Harness change.** The intake harness (`InvoiceSubmissionServiceTests.Harness`) now returns the guard's submission query through its `ColumnSet`, as Dataverse does. A column dropped from the read therefore fails a test. `CorrectionSeed.Submission` defaults to Accepted, and the persister's `CorrectionStore` seeds the earlier submission Accepted with the typed number `INV-100`.

**Fail before.** The three changed production files (`InvoiceSubmissionService.cs`, `InvoiceSubmissionPersister.cs`, `InvoiceSubmissionAfterSteps.cs`) were set back to `7e6cd95787`, keeping the new constants. Then 16 of the new or changed cases failed: every F1 must-fire row at intake and at commit, the from-document F1 case, the contract `submission_in_progress` row, both F2 refusals, and K4 `own-gone`. The controls passed: the allowed rows, the trimmed-number row and the (a′) test. Restored: all pass.

**Seeding proof.**
- F1 terminal check disabled (`CorrectionTargetSubmissionFinished` always true): 13 fail. These are the 6 intake rows, the 5 commit rows, the from-document case and the contract row. Restored: pass.
- F2 number check at commit disabled: 2 fail (the mismatch and the missing-number row). Restored: pass.
- `sprk_processingstatus` dropped from the commit read (`CorrectionTargetSubmissionColumns`): 12 persister tests fail, because every committing correction is then refused. Restored: pass.

**Contract changes for word-add-in-r1** (095 contract section 6, 094 notes):
- New 409 `correction_not_allowed` `reason` `submission_in_progress`. Show it as "the invoice is still being processed; try again later". Clients already accept unknown reasons.
- New stored `outcomeReasonCode` values on a Failed correction: `ebilling.submission.correction_not_allowed.submission_in_progress` and `ebilling.submission.correction_not_allowed.different_invoice_number`. They join 097c's codes in the table above, and none has a client mapping yet (097c K1).
- From-document `mode` is exact-case; any other value is 400 `mode` (K5).

| Code | Where | Meaning |
|---|---|---|
| 409 `ebilling.submission.correction_not_allowed` `reason` = `submission_in_progress` | intake, both routes | the invoice's own submission has not finished; retry later |
| stored `ebilling.submission.correction_not_allowed.submission_in_progress` | Failed (commit) | the old invoice's submission had not finished at commit |
| stored `ebilling.submission.correction_not_allowed.different_invoice_number` | Failed (commit) | the submission `sprk_resubmissionof` names carries another firm invoice number |

**Known limits (second review).**
- K-R2-1: an old submission stuck at Processing for good (for example a poisoned after-step, 097d K3) makes its invoice uncorrectable until an administrator resolves it. This is intended: superseding an invoice whose own processing is unfinished is the defect F1 closes.
- K-R2-2: the commit reads the earlier submission twice: once to resolve the target (`ReadCorrectedInvoiceAsync`), once for status and number. The check is not a lock, as at intake. A finished submission only moves on to another finished outcome (held Blocked to Overridden), or to Returned. Returned moves the old invoice to Returned to Firm, and the old invoice's row-version guard then faults the transaction.
- K-R2-3: `sprk_resubmissionof` stays an ordinary column (097b K6). Field-securing it is an owner decision, pending; no schema change was made.

### Gate (second review)

Measured on head `1f2e865479` (code and tests; the notes commit changes no code).

| Step | Result |
|---|---|
| Build, 5 test projects (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests) | 0 warnings, 0 errors each |
| Unit filter `Finance\|Ebilling\|Billing\|Signals\|Invoice\|Submission\|FromEmail\|Correction` | 9598 passed, 0 failed, 7 skipped |
| ArchTests (full) | 1858 passed, 0 failed (ISS-005 did not flake) |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 0 failed, 8 skipped |

No package, project file, endpoint, DI registration, schema or App Service setting changed. The Dataverse change is one more read of a row already read at commit. Publish size was not re-measured: two predicates, two constants and one catch.

rework: fix-round-1 task 097
