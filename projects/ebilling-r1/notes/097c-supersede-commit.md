# Task 097c: the correction's commit supersedes the old invoice; the undo reopens it

Part three of task 097 (`tasks/097-corrected-invoice.poml`), split for size. Decisions: EB-D-179 (owner, binding), EB-D-174; model 9.1 "Existing invoice" steps 4-5. Base `932efea9cc` (097a and 097b merged); branch `task/ebill-097c-supersede-commit`. 097d (Signals close) and 097e (prefill) are separate. Rigor FULL (code change in the BFF commit path).

## What changed

| Area | File | Change |
|---|---|---|
| The target at commit | `InvoiceSubmissionPersister.cs` (`CheckCorrectionTargetAsync`) | For a Correction (`sprk_resubmissionof` set) that is not AutoRejected, the explicit target replaces 104's automatic lookup. It is resolved through `SubmissionSnapshot.ReadCorrectedInvoiceAsync` (097b), then the old invoice is read ONCE (state, status, firm, core lookups, `versionnumber`). It must still be this firm's, filed under this core record alone, and In Review on an active row. The close is 097a's `BuildSupersedeRequest(oldId, <status as read>, version)`; a refused build is a stop (Refused), never a send. The new invoice's `sprk_supersedesinvoice` = the old invoice. Reads only. |
| 104 unchanged for New | same | 104's automatic Returned-to-Firm lookup runs only when `sprk_resubmissionof` is empty. |
| The transaction | same | The close (`UpdateRequest`, `IfRowVersionMatches` at the version read) goes into the commit's one `ExecuteTransaction`, after the approval create and before the source-document guard. |
| A fault at commit | same (`RecheckSupersedeTargetAsync`) | After a rolled-back transaction (not `OutcomeUnknown`) and the existing document re-check, the old invoice is read again: Approved, Sent to AP or Paid is Refused `...correction_not_allowed.invoice_status` (the submission ends Failed); anything else, a read fault included, is the ordinary Retry. Nothing was written (one transaction). |
| Re-index after the commit | same | `InvoiceIndexingJobHandler.TryEnqueueAsync(jobs, old invoice, "v{version read}")`, right after the commit, before the gate (reason under "Decisions"). Best effort. |
| The undo | `SubmissionCommitGate.cs` (`CompensateAsync` step `reopen`, `ReopenSupersededAsync`) | After the new invoice's rows are deleted: no `sprk_resubmissionof` is nothing to do; otherwise resolve the target (static `ReadCorrectedInvoiceAsync`), read status and `versionnumber`. Any status but Superseded (already In Review, i.e. Current == Requested, another status, or one the lifecycle does not know) is done. A Superseded invoice gets `BuildSupersedeUndoRequest` at the version read, sent as its own transaction. A stale row version faults, the compensation's catch logs `held at step reopen` and returns held (the existing pattern: the link stays, the note stays pending, a later delivery re-runs the undo). After a reopen, the re-index is queued at `v{version}`. A Superseded invoice that another live invoice still supersedes is not reopened. |
| Gate wiring | `SubmissionCommitGate` constructor | Takes `IInvoiceLifecycleService` (scoped, already registered) and optional `JobSubmissionService` (singleton, already registered). `SubmissionColumns` reads `sprk_resubmissionof`. |
| Snapshot | `SubmissionEvaluationOutcome.cs` | `ReadCorrectedInvoiceAsync(entities, ct)` delegates to a new `internal static ReadCorrectedInvoiceAsync(entities, earlier, ct)`, so the undo (which has the submission row, not a snapshot) uses the same fail-closed resolution. Behaviour unchanged. |
| Stored codes | `SubmissionReasonCodes.cs` | `CorrectionInvoiceStatus` = `ebilling.submission.correction_not_allowed.invoice_status`; `CorrectionDifferentFirm` (`.different_firm`); `CorrectionDifferentCoreRecord` (`.different_core_record`); `CorrectionTargetUnavailable` = `ebilling.submission.correction_target_unavailable`. All Refused (permanent, Failed). |

No new endpoint, schema change, package, DI registration, Dataverse write outside the commit transaction and the undo, or App Service setting.

## Decisions

- **Stored code shape.** The submission stores one reason code, so the intake's 409 code and its `reason` are joined: `ebilling.submission.correction_not_allowed.<reason>`. A target that no longer resolves (earlier submission gone, no longer linking its invoice, or the invoice deleted) is `correction_target_unavailable`: fail closed, never committed as a New invoice beside the one it was meant to replace.
- **AutoRejected corrections** link nothing and supersede nothing, as 104 treats an automatic rejection. The old invoice stays In Review for the next correction.
- **Pre-commit check vs post-fault check.** Before the commit every status but In Review is refused (as the intake; owner 2026-10-21: an Inactive invoice is not changeable). After a faulted transaction only Approved, Sent to AP or Paid is Failed (the brief); any other change is a Retry and the next delivery's pre-commit check decides (for example Rejected becomes a Failed `invoice_status` there).
- **The builder gets the status as read**, not an assumed In Review, so 097a's matrix judges it too (defence in depth; the seeding proof below relies on it).
- **Re-index before the gate.** The index document is built from the old invoice's own row, nothing of the new submission or its source, so model 9.1's "no indexing before step 5" (about the new submission) does not apply. Superseded is terminal, and a resume after a gate fault never returns to this code, so queueing after the gate could lose the re-index for good. If the gate then undoes the commit, the reopen queues its own re-index; each job reads the row as it then is.
- **Undo, stale version.** Held and reported (`held at step reopen`), not swallowed; a later delivery re-reads and reopens. Current == Requested is done.
- **Item 6, what the old submission shows once its invoice is Superseded: not settled.** Neither the POML nor EB-D-179/EB-D-174 says, so nothing was changed. Open question for the owner. Today the old submission keeps its own status; 097a's finding 5 notes a held old submission then reads Blocked with no action. No test depends on it.

## Component justification (§11)

- **`SupersedeTarget`, `CheckCorrectionTargetAsync`, `RecheckSupersedeTargetAsync`** (private/internal, in the persister). Existing: 104's predecessor lookup and the document re-check after a fault, in the same method. Extension: added beside them; no new service. Cost of doing nothing: EB-D-179 / model 9.1 step 4 has no commit-time guard, so an invoice approved after the intake could be superseded.
- **`ReopenSupersededAsync`** (private, in the gate). Existing: `CompensateAsync`'s delete steps. Extension: one more step in that method's pattern. Cost of doing nothing: an undone correction leaves the old invoice Superseded with no live successor (model 9.1 step 5).
- **Static `ReadCorrectedInvoiceAsync(entities, earlier, ct)`.** Existing: the instance method (097b). Extension: the instance one now delegates to it. Cost of doing nothing: the undo would need a second copy of the fail-closed resolution.
- **Four reason codes.** Existing: the intake's 409 code and reasons. Extension: composed from those constants. Cost of doing nothing: a commit-time refusal would have no stored reason distinct from other failures.
- **Gate constructor dependencies.** Both already registered; no DI change.

## BFF hygiene (§10)

Placement: in the BFF, inside the existing ADR-004 submission job's commit (`InvoiceSubmissionPersister`) and its undo (`SubmissionCommitGate`), the only write path for submissions. No endpoint, package, DI registration or background work. Publish size and CVE: see "Gate".

## Tests

New `tests/unit/.../Submission/InvoiceSubmissionPersisterCorrectionTests.cs` (partial of `InvoiceSubmissionPersisterTests`, 23 cases), on the existing in-memory `Store`, extended to model the old invoice (status, `versionnumber`, Dataverse's conditional update: refused at a stale version, an unconditional update overwrites), other submissions, and the index queue (`IndexQueueDouble`).

Brief's list: successful correction (link, close in the same transaction, re-index `v41`); New submission unchanged with 104's link; approval race (Approved / Sent to AP / Paid between the check and the commit) is Failed `invoice_status` with nothing written; row-version race is Retry then commits; undo reopens (re-index `v41` then `v42`); undo with the invoice already In Review is done; undo at a stale version is held, then completes (`v43`).

Item 8: `tests/unit/.../Signals/Billing/BillingFactLoaderSupersededReevaluationTests.cs` (partial of `BillingFactLoaderTests`): 101's `InvoiceCorrectionReevaluation` on the new invoice over 072's real loader and evaluator: a Superseded prior of the same firm and number raises no Duplicate (must fire); the same prior In Review still raises `DuplicateInvoiceNumber` (must not fire). `InvoiceCorrectionReevaluationTests.Store` became `internal static` for it.

Beyond the brief, with the reason:
- Decided before the commit (Approved / Sent to AP / Paid at the read): the pre-commit half of the guard, refused without a send.
- Another firm or core record at commit (firm, core, two cores): the brief's "re-check same firm and same core record at commit".
- Unresolved target (no invoice, another invoice, invoice gone): fail closed, never committed as New.
- Old-invoice read fault: Retry, not Failed.
- AutoRejected correction supersedes nothing: mirrors 104; guards against closing an invoice for a rejected correction.
- Undo, superseded by another correction: stops an auto-rejected correction's undo from reopening an invoice a later correction closed.

**Fail before.** With the behaviour switched off (the persister's correction branch and the gate's reopen step), 17 of the 21 new persister tests then present failed; the 4 controls passed (New unchanged, AutoRejected, already In Review, superseded by another).

**Seeding proof (the two guards the brief names).**
- Undo row-version guard: the reopen request forced to `ConcurrencyBehavior.Default`: `Undo_StaleVersion_IsHeld_ThenCompletes` and `Undo_ReopensTheOldInvoice` fail. Restored: pass.
- Commit-time status re-check: `RecheckSupersedeTargetAsync` forced to return null: the 3 `ApprovalRace` rows fail. Restored: pass.
- Also tried: removing only the explicit pre-commit status condition fails nothing, because the builder (given the status as read) refuses too; additionally passing In Review to the builder makes the 3 `DecidedBeforeTheCommit` rows fail. Restored: pass.

## Review (one adversarial pass, Opus)

No F1, F2 or F4. The reviewer re-derived the approval race: the approval path records its claim and then runs the conditional transition, so whichever commits first faults the other.

| # | Class | Finding | Resolution |
|---|---|---|---|
| 1 | F3 | The test store returned every column whatever was asked, so a column dropped from the gate's submission read (`sprk_resubmissionof`) or the old invoice's reads would pass every test while the undo silently skipped the reopen. | Fixed: `Store.ProjectReads` (on for the correction tests) returns only the columns requested, for the submission, the earlier submission and the old invoice. Seeded: dropping `sprk_resubmissionof` from `SubmissionCommitGate.SubmissionColumns` makes 3 tests fail. Restored: pass. |
| 2 | K | The re-index can still be lost: an `OutcomeUnknown` commit whose link re-read faults is a Retry, the next delivery resumes, and the resume does not re-index the old invoice; likewise a crash between the commit and the enqueue. | Known limit K2 below. |
| 3 | K (fixed) | The reopen demanded a known status and a version before deciding whether there was anything to reopen, so an unknown status or a missing version on a row that is not Superseded would hold the undo. | Fixed: any status other than Superseded (an unknown one included) is done; the version is required only for a Superseded row. New test `Undo_UnknownStatusWithoutVersion_IsDone` (fails against the first commit, passes now). |
| 4 | K | Neither the intake nor the commit check refuses an old invoice whose own submission's undo is unfinished; if that held undo later deletes the old invoice, the new invoice stays live with `sprk_supersedesinvoice` cleared (lookup RemoveLink). | Known limit K4. |
| 5 | K | The undo's only evidence that this commit's close is in force is that no live invoice points at the old one; a successor deleted some other way (admin delete, core-record cascade) lets a later compensation of a correction naming the old invoice reopen it. | Known limit K5. |
| 6 | K | A Rejected / Cancelled / Returned-to-Firm change during the race on the job's final delivery ends as `transaction_failed`, not `invoice_status`. | As the brief directs (anything but Approved / Sent to AP / Paid is Retry). Known limit K6. |

## Gate

Measured on head `5fcfb80135` (before the notes commit).

| Step | Result |
|---|---|
| Build, 5 test projects (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests) | 0 warnings, 0 errors each (at `e205c93419`; the review fix changes no public signature, and the unit and integration runs below rebuilt at `5fcfb80135`) |
| Unit filter `Finance\|Ebilling\|Billing\|Invoice\|Submission\|FromEmail\|Correction\|Index` | 7717 passed, 0 failed, 5 skipped |
| ArchTests | 1858 passed, 0 failed (ISS-005 did not flake) |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 0 failed, 8 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |

**Publish size (§10).** `dotnet publish -c Release -r linux-x64 --self-contained false`, base `932efea9cc` (`C:\p097cb`) against head `5fcfb80135` (`C:\p097ch`), both exported to short paths, zipped with `Compress-Archive -CompressionLevel Optimal`. Base 41,073,961 B, head 41,078,186 B: **+4,225 B** zipped; 192 files both; `Sprk.Bff.Api.dll` +15,360 B. No package or project file changed.

## Known limits

- K1: the new stored codes have no mapping in the clients (word-add-in `invoiceSubmissionMessages.ts` `INVOICE_OUTCOME_MESSAGES`, external SPA); they show the generic fallback. Follow-up for word-add-in-r1 and the 095 contract §9 (not editable here).
- K2: the old invoice's re-index is best effort (`TryEnqueueAsync` logs and continues), as every lifecycle move's re-index is. It is lost on a commit whose outcome is unknown and whose link re-read faults (the resume does not re-index), or on a crash between the commit and the enqueue; the index then shows the old invoice In Review until its next re-index. A resume-time re-index under an idempotent key would close it; not done here (rare path, a stale index row only, no data change).
- K3: a reopen is held when the old invoice is Superseded and its `versionnumber` cannot be read; the undo retries on the next delivery and the note stays pending.
- K4: an old invoice whose own submission has an unfinished (held) undo is not refused as a target. If that undo later deletes it, the new invoice stays live and loses its `sprk_supersedesinvoice` link. Data stays consistent; lineage is lost. Fix candidate: refuse with `correction_target_unavailable` when `SubmissionCommitGate.UndoUnfinished` holds for the earlier submission.
- K5: a successor deleted outside the undo (admin delete, cascade) makes the old invoice look reopenable to a later compensation of a correction that names it.
- K6: on the job's last delivery, a race to Rejected, Cancelled or Returned to Firm ends `transaction_failed` rather than `invoice_status`.

## What 097d needs from this branch

- An after-step, after model 9.1 step 5 (the gate), that closes the old invoice's open Signals as Superseded. The old invoice id: the submission's `sprk_resubmissionof` through `SubmissionSnapshot.ReadCorrectedInvoiceAsync(entities, earlier, ct)`, or the new invoice's `sprk_supersedesinvoice`.
- Close only when the old invoice is Superseded and this submission's invoice is the one superseding it.
- The undo runs inside the gate, before any after-step, so Signals are never closed for an undone commit.
