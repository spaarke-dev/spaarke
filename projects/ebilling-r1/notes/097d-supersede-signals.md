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

- **ChangedSinceRead is a Retry, not "left and done" (deviation from POML criterion `:115` and constraint `:46`).** The POML assumes ChangedSinceRead means "a person resolved it". The step now re-reads each Signal just before closing it. ChangedSinceRead therefore means a write landed in the few milliseconds between the re-read and the close. That write may have been an Acknowledge, which leaves the Signal open. Billing Signals have no sweep: `SignalReevaluationJob` covers Ontology policies only, and both the billing evaluation and 101's re-evaluation skip a Superseded invoice. So a Signal left open there would stay open for good. The step writes nothing and returns Retry. The next delivery re-reads: a person's close is left as it is, and a still-open Signal is closed. The cost is one extra delivery. The adversarial review agreed. **Owner/main session: amend POML `:115` and `:46` to match** (this agent does not edit the task file).
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

rework: fix-round-1 task 097
