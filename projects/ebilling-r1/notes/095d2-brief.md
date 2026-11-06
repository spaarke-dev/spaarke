# 095d-2 brief: the re-run after Set firm, and the hold release

2026-11-05, base `b0695afab4`; task `tasks/095d2-rerun-and-hold-release.poml`. Sources: N95 `notes/095c-from-email-firm-optional.md`, N82 `notes/082-procedure-resolver.md`, B82 `notes/082c-brief.md`, N114 `notes/114-override-releases-gap-hold.md`, CT `current-task.md`, P95 `tasks/095d-set-invoice-firm.poml`, TI `tasks/TASK-INDEX.md`, DL `notes/decisions-log.md`, ONT = ontology `notes/decisions.md` in `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1` @`08218b7aef` (this worktree's copy stops at D-200).

## 1. Goal

After Set firm (095d-1) applies and records, re-evaluate the submission with its firm through pipeline steps 2-5 and the supersedes lookup, from the stored rows (no AI read). One write replaces the three firm markers with the new outcome, resets no-firm line decisions and re-derives the review detail. Then re-map the shared `BILL-SYSTEM-SUBMISSION` Signal; it closes `ConditionCleared` with the assign-firm record id only when `InvoiceOpenHolds` finds no reason left. No new endpoint, column or Decision Record.

## 2. Requirements (closed set)

| Id | Requirement | Source |
|---|---|---|
| R1 | Second evaluation of the persisted submission, through the existing entry points (not a copy): resolution, the firm-code step, rules (Procedures, Duplicate priors, the replaced invoice), 073 and the supersedes lookup | N95:472, :529, :716, :1055; P95:68-69 |
| R2 | The new outcome removes all three markers: the reason, the pipeline finding and every line cause `firm_missing`. Typed writer | N95:714, :1102, :1290-1291 |
| R3 | `rules.not_checked` is RECOMPUTED from the new refusals, not cleared. An evaluation fault keeps every marker and writes nothing | P95:70 |
| R4 | K5: reset line statuses and discard no-firm line decisions. The coding hold's title returns | N95:715, :1249 |
| R5 | A blocking result does NOT reject: the D-101 held set, no "automated:" record. D-122's reject-closes-all does not run | P95:73; DL:128 |
| R6 | Not taken for a resume of the intake run; tested | P95:71 |
| R7 | New rule Signals are new D-13 keys (episode 1) | P95:72 |
| R8 | Re-map the shared group. Reasons remain: open, title recomputed. None remain: `ConditionCleared` with the assign-firm record id in the close facts. Never `Acted` | N95:1247-1251; ONT:448-453, :467 |
| R9 | "Every reason cleared" = `InvoiceOpenHolds` over every linked submission is empty | N114:153; ONT:468; CT:13 |
| R10 | Find the record by `BillingDecisionKey` (`sprk_invoice`, invoice, `DecisionIdFor(submission)`) | N95:1486 |
| R11 | Keep `assignFirm` intact; never change `BuildDecision`'s hashed values | N95:1360, :1487-1489 |
| R12 | D-205 writer 4: the detail comes from the remaining reasons via `InvoiceOpenHolds`, in the re-run's own write; add it to `InvoiceReviewDetailWritersGuardTests.Writers` | N82:604, :610, :686-688, :940; B82:180 |
| R13 | Set firm's detail stays on `InvoiceOpenHolds` (met, 082c-2 round 3, `InvoiceAssignFirmService.cs` ~:1071-1079); pin it | N82:842, :887, :940; CT:13 |
| R14 | A re-hold removes the findings `override` stamp in the same write | N114:63-64, :73; TI:104; `InvoiceProcedureHold.cs:43,164` |
| R15 | D-201: an Override leaves the shared Signal open while a hold remains; Return is outside the filter; 095d-2 is the closer | N114:129-135, :147-153; ONT:468-472 |
| R16 | NO Superseded link on `sprk_signal` | ONT:703-705; N82:1122; CT:13 |
| R17 | Runbook line: "`Permanent=true` = operator, not retry". Permanent failures carry the marker | CT:13-14; N95:1501; ONT:466 |
| R18 | The `AccessColumns` text for `sprk_findingsjson` names every typed writer: add the re-run (`ParentLineage.cs:1324`, remarks ~:1008-1011, ~:1055-1059) and the census texts | N95:1522 |
| R19 | Guard lists: `FindingsJsonAppOnlyReadGuardTests` (+ the notes/122 row); `InvoiceFirmHoldAppOnlyGuardTests` if the column is named | N95:1314, :1527; N82:935 |
| R20 | AI only where a rule's outcome can change; the PR lists each rule as "AI: yes/no" with the reason | N95:1080; ONT:446 |
| R21 | No path from held straight to Approved; approve works once the lines are decided | N95:718; P95:109 |
| R22 | K3: the line Signals' `sprk_regardingrecordname` keeps the old number. Refresh it, or record a limit | N95:1361 |
| R23 | Write no firm or number column (owned by the allocator and Set firm) | N95:1209-1213, :1550-1557 |
| R24 | 095d-1 never reaches master without 095d-2: one master PR | N95:1252, :1490; CT:14 |

## 3. Design

**Recommended: a queued ADR-004 job.** Set firm keeps its phases and adds one: apply → record → **enqueue the re-run** → 200. A failed enqueue is 503 `incomplete` with `failedStep` `rerun`. The repeat (`CompleteRepeatAsync`) re-enqueues until the re-run reports done.

Why: pipeline-sized work (roster and rates, Duplicate priors, maybe AI coding (Q3), a 1000-request transaction, N Signals) does not fit the 2-minute lock (N95:1381) or a request. The 049 job is its home. It runs app-only (ADR-052); Set firm asked the rights. Until it runs, the markers keep the invoice held.
Rejected: running it synchronously; extending the 049 handler (its "linked or terminal is never re-evaluated" invariant protects every committed outcome).

**Job** `InvoiceSubmissionRerunJobHandler`. Payload `{submissionId, decisionId}`; key `ebilling-assign-firm-rerun-{submissionId:N}`. Queue duplicate detection is off (ADR-004), so the handler is idempotent.

0. **Gate.** Flag; the 049 processing lock `invoice-submission:{id:N}`. `assignFirm` absent, `decisionId` ≠ `DecisionIdFor(submission)`, or row firm ≠ stored firm: Poisoned, `Permanent=true`, nothing written. No record (R10): Failure.
1. **State.** New findings member `assignFirmRerun {decisionId, state, on}`. It is a hand-rolled state machine (ADR-004 / ADR-052 §7 reason: two repeatable Dataverse phases). `done`: no-op. `committed`: go to step 4.
2. **Evaluate (no write).**
   - Rebuild the read from the stored invoice, lines and `sprk_extractionconfidence` (copy `InvoiceCorrectionReevaluation`). Use the stored firm.
   - Call `InvoiceSubmissionProcessor.EvaluateFromReadAsync`, a pure extraction of `EvaluateAsync` steps 2-5 (R1).
   - Build the findings with the persister's builders (`BuildFindingsJson`, `RulesNotChecked`, `LineStatusFor`, `TerminalFor`). Keep `assignFirm`, the correction members and the admin text; DROP `override` (R14).
   - A 073 reject maps to held (R5, Q5). Then `FindSupersededInvoiceAsync`.
3. **Commit.**
   - Take the decision and line locks briefly and re-read the versions. A change re-evaluates once (`MaxReEvaluations`), then Failure.
   - Derive `remaining = InvoiceOpenHolds.ReasonsAfterAsync(replacement)` before any write (114 finding 4).
   - One `ExecuteTransactionAsync`, every row `IfRowVersionMatches`:
     - submission: reason code; `sprk_processingstatus` (`TerminalFor`, already terminal); findings with `committed`;
     - invoice: `sprk_reviewdetail = ValueOf(ReviewDetailOf(remaining))`, if the probe finds the column;
     - lines: the resolution, coding and status columns `BuildLines` derives (never as-billed or corrected values), plus the reset (Q4);
     - predecessor: the link and its conditional transition (as the persister).
   - Above `MaxRequestsPerTransaction`: Poisoned, `Permanent=true`.
4. **After the commit (repeatable from the stored state).**
   - Totals (016), then the supersede close, then rule Signals via `SignalEpisodeLifecycle`, then the system re-map.
   - The shared Signal (`IsHoldBearingSignal`):
     - `InvoiceOpenHolds.ReasonsAsync` empty AND its group empty: `CloseAsync(ConditionCleared)`, then the record link (Q1).
     - Otherwise: a lifecycle write (Q2).
     - A reason from another submission: untouched (fail toward open).
   - Last, a conditional write sets `done`.
5. **Set firm** returns `rulesRerun` (`pending` | `done`) read from the state member (Q8). `assignFirm.rulesRerun` is untouched (R11).

**Concurrency.** A Set firm repeat still sees `AlreadyRenumbered` (`assignFirm` unchanged). Override and line decisions are stopped by the locks plus row versions; corrections by row versions. A 049 redelivery takes the lock and hits the terminal no-op; a second re-run delivery is stopped by the lock and the state.
**Fail closed.**
- A read fault is a Failure. On the final delivery the markers stay and admin attention is set; never a release without a full evaluation.
- A Procedure fault on the final delivery holds `procedures_unresolved`. Unreadable findings: Poisoned, `Permanent=true`. A permanent Signal refusal is recorded.
- A native firm write (N95:1374) never starts a re-run.

## 4. Open questions (recommendation first)

1. **ontology-r1, "close facts".** `SignalWriter.CloseAsync` writes status, resolution and dates only (`SignalWriter.cs:952-977`). **Rec:** `LinkConditionClearedRecordAsync` (`:1149`, D-145b; `resolvedBy` null), which extends D-145b to this one Decide-lane close. Fallback: a log join (C19 (iii)). A new writer parameter is out.
2. **ontology-r1, "same Signal, title recomputed".** On an open row the writer refreshes `lastevaluated` only (`SignalWriter.cs:340-352`). A new reason set goes through the lifecycle: Superseded (record-free, R16), then episode n+1. **Rec:** accept, as 101 does; the 114b filter matches policy + subject.
3. **Owner, AI table (R20).** **Rec:** never re-read the document (the extraction does not depend on the firm). Re-run AI coding only on lines whose code set, resolved with the firm, differs from the stored `codeSetId`. Step 1 builds the per-rule table and STOPS if a rule needs an AI fact the rows lack.
4. **Owner, K5 "discard".** Decisions are correction rows plus Decision Records (append-only). **Rec:** the commit resets each decided line's status and amount to the re-run's derivation. The rows and records stay as history, listed in `assignFirmRerun`. Value corrections stay and feed the re-run.
5. **Owner, confirm P95:73.** A 073 reject after Set firm holds (Internal Blocked; Override or Return). **Rec:** confirm.
6. **Owner + uac-r2, a new hold.** A gap or unresolved hold found once the firm is known: Internal Blocked, detail 659490000/1 (a partner-visible label change), stamp removed, Override available. **Rec:** confirm. The Procedures MUST run, or the hold lifts with no rule checked.
7. **uac-r2, placement.** **Rec:** a new handler (one DI registration) beside 049's in `Services/Jobs/Handlers`, with AI only through the processor's facade. Use `Services/Ai/Jobs` if bff-extensions:66 binds.
8. **Owner / 095d-3.** Add the `rulesRerun` value `done`. **Rec:** accept; no new field.

### 4a. Answers received

- **uac-r2, 2026-11-05 (Q6, Q7).** No access objection to Q6, with three conditions:
  1. Linked submissions stay withheld from partners while the re-run is pending (keyed on `rulesRerun`). Partners only see a state decided with the firm.
  2. A failed re-run leaves the submission held, never released. This covers a dead-letter, exhausted row-version retries, and unresolvable Procedures.
  3. The Procedures run in the re-run (binding). A new hold removes the stamp in the same write, and Override stays under D-142.
- **ontology, 2026-11-05 (Q1, Q2; D-201 note `402e210116`).**
  - **Q1:** link the close with `LinkConditionClearedRecordAsync` (D-145b extended) under these conditions:
    - only when the re-run started by the assign-firm decision is the run that clears the last reason;
    - `resolvedBy` = the assign-firm decider, and `expectedPriorRecordId` = null;
    - the record's `ResolvedSignalIds` stays empty;
    - the writer's doc comment and tests are updated (a Decide-lane link, plus ForeignLink and NotApplicable).
  - **Q2:** accepted. A changed reason set closes the Signal as Superseded (record-free) and opens episode n+1, and only on a real change. The age restarts, which is accepted. A stored Override or Return set that names the superseded Signal closes it as `LeftAsIs`, not as a failure, so retries don't loop. Part 2b can proceed.
- **Q7:** the handler goes in `Services/Ai/Jobs/` if the re-run can call AI coding, and in `Services/Jobs/Handlers/` if it never does. Decide after the owner answers Q3. All of these are acceptance criteria in the POML.

- **Owner, 2026-11-05 (D-148, D-149, D-150).**
  - Q3: no re-read. The AI read has no firm input except an optional vendor-name hint, and the guidelines are applied by the rules, which re-run. Re-code changed lines only. So the handler goes in `Services/Ai/Jobs/`.
  - Q4: reset any decisions made before D-148.
  - Q5: hold, don't auto-reject.
  - Q6: hold for review.
  - Q8: `completed`, not `done`.
  - D-148: no line decision, Override or approval without a firm, or while the re-run is pending. Slice 095d-0 builds that gate.

## 5. Files

| File | Change |
|---|---|
| `Services/Finance/Submission/InvoiceSubmissionRerun.cs` | new; steps 0-4 |
| `Services/Jobs/Handlers/InvoiceSubmissionRerunJobHandler.cs` | new; thin shell |
| `InvoiceSubmissionProcessor.cs` | the extraction only |
| `InvoiceAssignFirmService.cs` | enqueue, `rulesRerun`, re-enqueue on repeat |
| `Infrastructure/DI/FinanceModule.cs` (~:253) | registrations |
| `Services/Dataverse/ParentLineage.cs` | texts (R18) |
| `scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1` | check (g) "recorded, re-run not done", INFO/FAIL window as check (f) |
| `InvoiceSubmissionPersister.cs` | visibility hunks only (082d edits it too) |
| ArchTests | `InvoiceReviewDetailWritersGuardTests`, `FindingsJsonAppOnlyReadGuardTests`, `AccessTableReaderScanTests` / `AccessTableAppOnlyWriterTests` texts, `SubmissionOverrideRecordPathGuardTests` |

**Copy:** `InvoiceCorrectionReevaluation` (row rebuild, `ReconcileSystemAsync`, `CloseClearedAsync`, lifecycle `WriteAsync`, final conditional write); `SubmissionOverrideService` 114 (two conditional updates, derive first); `InvoiceSubmissionProcessingJobHandler` (lock, final delivery, Poisoned); `InvoiceIndexingJobHandler.BuildJob` + `InvoiceSubmissionAfterSteps.EnqueueAcceptedIndex` (enqueue).

## 6. §11 justification

| Surface | Existing / why not extend | Cost of nothing |
|---|---|---|
| `InvoiceSubmissionRerun` | The persister's linked path writes nothing by contract; 101 patches one field's diff | The hold never lifts (N95:1102) |
| Job, handler, DI | The 049 handler's no-re-evaluate invariant | No durable retry for a long re-run |
| `EvaluateFromReadAsync` | Is the extension of `EvaluateAsync` | A copy of steps 2-5 (P95:69) |
| `assignFirmRerun` member | Changing `assignFirm.rulesRerun` risks the repeat (R11) | No idempotent re-run |
| Check (g) | Extends check (f), same script | A lost enqueue is silent |

## 7. ADRs and constraints

ADR-002; ADR-004 (deterministic key, correlation id, outcomes); ADR-052 (BFF, B2; ADR-036 unused); ADR-010; ADR-017/019; ADR-028 (no new right or route); ADR-038; bff-extensions §10 (placement line, publish delta against a fresh master build, no package).

## 8. Test scope (closed)

Must-fire, each with a must-not-fire control: T1 clean (markers gone, not held, detail blank, `ConditionCleared` + link, `done`; control: held before the re-run); T2 a remaining note keeps the Signal open; T3 a gap appears (Blocked, 659490000, approval refused, open); T4 unresolved holds on the final delivery, an earlier one writes nothing; T5 a 073 reject holds (no Rejected invoice, no record); T6 seeded stamp + `overridden`: after a re-hold `IsReleased` false (control: another submission's stamp kept); T7 a decided line reset, a value correction kept; T8 `rules.not_checked` with the new cause; T9 Duplicate prior and supersede for this firm only; T10 `assignFirm` byte-identical, repeat still `alreadyAssigned`, same record; T11 a 049 redelivery is a no-op; T12 a repeat after `committed` runs the Signals only; T13 a row-version conflict (Override, Set firm, correction) re-evaluates once then retries, nothing partial; T14 enqueue failure 503 `rerun`, the repeat enqueues; T15 stored decision missing or foreign: Poisoned, nothing written; T16 unreadable findings: nothing written, `Permanent=true`; T17 a 503 then the repeat: one re-run; T18 a firm hold on another linked submission: open, 659490002; T19 no `ConditionCleared` while `ReasonsAsync` is non-empty; T20 the Superseded close writes no `sprk_decisionrecord`; T21 the detail is in the same transaction; T22 no `ReadInvoiceAsync`, coding AI per Q3.

Seeds (fail-open and data-loss guards only; restored `cmp`-identical): stamp removal (T6); the close condition (T3, T19); markers cleared without an evaluation (T4); `assignFirm` dropped (T10); the row-version condition dropped (T13).

## 9. Expected known limits

K1 line Signals keep the old number (R22). K2 Duplicate is one-way (earlier firm submissions are not re-judged). K3 held (safe) between Set firm's 200 and `done`. K4 an Override replaying a stored set closes nothing new. K5 a native firm write stays an operator remedy. K6 discarded decisions' records remain. K7 `IIdempotencyService` check-then-set (#984), covered by row versions and the state.

## 10. Rigor and model

FULL, Opus, effort `high`. Split like 095d-1: **2a** extraction, evaluate, commit (T1-T13, T15-T16, T18, T21-T22); **2b** job, after-commit Signals, close, Set firm enqueue, check (g) (T14, T17, T19-T20). Reviews: one adversarial pass per part (two on 2a: fail-open sensitive), then uac-r2's full review, then ontology K-3 (N95:1083). Answers before 2a: Q3, Q4, Q6; before 2b: Q1, Q2, Q5.
