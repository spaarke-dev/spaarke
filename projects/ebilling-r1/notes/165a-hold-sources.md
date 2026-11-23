# 165a — hold decisions read only server-written sources

Task 165 part a (EB-D-200; uac-r2 option D, 2026-11-06). Rigor FULL. Branch `task/ebilling-165a` from `work/ebilling-r1` at `1df8f38a8c`.

## 1. Inventory

Every submission column read by a hold, approval, AP export or visibility decision is classified (a), (b) or (c) in uac-r2's terms: (a)
written only on server paths, (b) field-secured, Writers only, (c) re-derived or cross-checked by the BFF. The table (reader file:line, class,
evidence) is held privately and was sent to uac-r2.

## 2. What changed

- **One FR-20 reader.** `InvoiceBlockedHold` (`Services/Finance/Submission/InvoiceBlockedHold.cs`) replaces the two private copies in
  `InvoiceApprovalService` and `InvoiceRejectReturnService`. Approval, the approval state view, reject and return decide the hold only from
  server-written sources: the recorded outcome in the field-secured findings and the invoice's own submission. The column state can add a hold,
  never release one. When the sources disagree, the invoice is held.
- **The own-submission helper.** `InvoiceBlockedHold.OwnSubmissionOf` / `ReadOwnSubmissionAsync` select the invoice's own submission: linked to
  the invoice AND the one whose immutable id derives the invoice's (`InvoiceSubmissionPersister.InvoiceIdFor`). The FR-20 hold, the correction
  follow-through (reads and writes), the unapplied-corrections read (approve, state view, line decisions), the correction header provenance and the
  review page submission panel all use it. Approval reads only the invoice's own submission. With none linked, every caller fails closed: nothing
  is written, approval refuses (existing code), the provenance answer is `no_provenance` and the panel is empty. A line decision refuses only
  when an unapplied correction row of that line exists (every such row then counts as unapplied); it never reads the FR-20 hold. The parallel
  164a-4a branch moves the follow-through reader into `InvoiceFindingsDiff`; the merge is a one-line call change to `ReadOwnSubmissionAsync`.
- **The release stamps.** A release is the server-written Override or Return write (the column state and its stamp in one write) or the 095d-2
  re-run commit (the recorded outcome rewritten). The re-run commit now drops the Return stamp as well as the Override stamp, so a re-held
  submission is held again; fix round 2 keeps the Return stamp when the invoice is already Returned to Firm.
- **Override classification (K3).** `SubmissionOverrideService.Classify` decides the held and released states from the recorded outcome where it
  is readable, and from the columns otherwise; a decision still requires readable findings that store the hold. Client-visible codes are
  unchanged. Behaviour change: a submission whose recorded outcome is Blocked classifies Blocked whatever its channel, so an SPA submission the
  095d-2 re-run held for review (EB-D-187) is offered Override and Return; before, nothing could release it.
- **Refusal text.** On an own-submission-unlinked hold, approve, reject and return keep their codes (`ebilling.approve.held`,
  `ebilling.invoice.held`) and say: "The invoice has no linked originating submission, so it cannot be approved, rejected or returned here. An
  administrator must restore the link." (`InvoiceBlockedHold.OwnSubmissionUnlinkedDetail`). The other holds keep their text.

Each hold logs a distinct reason code with the table and row id only:

| Reason code | Table, row |
|---|---|
| `ebilling.hold.blocked_submission` | `sprk_invoicesubmission`, the submission |
| `ebilling.hold.recorded_outcome_blocked` | `sprk_invoicesubmission`, the submission |
| `ebilling.hold.own_submission_unlinked` | `sprk_invoice`, the invoice |

No field-security change, no schema change, no script, no live write. `FinanceBaseValues` is not changed.

**DEF-053 / #2571** (the FR-20 held-submission test is not anchored in a field-secured column). 165a closes it for approve, the approval state
view, reject and return. It stays open for pending spend until 165b. The issue is closed at merge by the main session.

## 3. Open items (effect and status)

- **Pending spend:** fixed in 165b (scope item 4 in the POML).
- **K2, review detail label:** can be blank for a held invoice. Display only; approval refuses. Open.
- **K3, Override classification:** fixed in this task. Fix round 2: a column state that differs from a recorded release is written back by a
  repeat of that Override or Return. Residual (fail closed, held): a submission whose status reads Processing is never decided (see fix round 2,
  item 1). Open as a known limit.
- **K4, review page "held" flag:** display only; can differ from approval. Open.
- **K5, correction target check:** not a hold decision. Listed for 165b.
- **K6, invoices with no own submission:** held, with the "restore the link" text. Dev has 11 In Review invoices with no linked submission, all
  held. Awaiting owner decision (EB-D-201, task 096b). The hold behaviour is unchanged.
- **External visibility:** two findings in a file another project owns were reported to uac-r2. Not changed in 165a.

## 4. Tests and seeds

Unit tests (filtered suite `Finance|Submission|Invoice|ApExport|ExternalAccess`): 12825 passed, 0 failed, 6 skipped. ArchTests: 2106 passed, 0 failed.

- `InvoiceBlockedHoldTests`: must-fire and must-not-fire for each source; the release and the re-hold built through the real re-run
  `FindingsAfter` (Override and Return stamps); the refusal text per verdict for approve, reject and return.
- `InvoiceCorrectionEndToEndTests`, `InvoiceCorrectionServiceTests`, `InvoiceReviewReadServiceTests`: a second submission linked to the invoice is
  neither read nor written; the invoice's own submission still is.
- `InvoiceApprovalProcedureHoldTests`: the pipeline Procedure case in the state the persister leaves (held), then the Override release (approved).
- `SubmissionOverrideServiceTests`: K3 must-fire (Override and Return proceed) and must-not-fire (an Accepted recorded outcome is refused, nothing
  written; a released submission is already decided); `RecordedDispositionOf` per recorded state.
- Fixtures: `ApprovalWorld`'s query double honours `AddOrder`; `CorrectionWorld` and `InvoiceReviewReadServiceTests` derive the invoice id from
  the submission id; the Override fixtures model each state as its writer leaves it.

First pass seeds (each restored byte-identical):

| Seed | Fails |
|---|---|
| Recorded-outcome check dropped | 9 |
| Own-submission check dropped | 7 |
| Release stamps ignored | 4 |
| The two services restored to HEAD | 5 |

Fix-round seeds (each dropped, the targeted tests run, then restored with `git checkout`):

| Seed | Fails |
|---|---|
| S1: the own-submission selection reverted to the latest linked submission | 3 (follow-through, hold, unfinished correction) |
| S2: the per-verdict refusal text dropped | 4 (approve x2, reject, return) |
| S3: the re-run keeps the `return` stamp | 1 (re-hold, `return`) |
| S4: `Classify` from the columns only | 7 (Override x5, Return, already decided) |

## 5. Publish size

Fresh short-path worktrees, `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` (framework-dependent, linux-x64), zipped with
`Compress-Archive -CompressionLevel Optimal`, PDBs included (4 each), 191 files each.

| Build | Zip | Uncompressed |
|---|---|---|
| master `bcdc0e0069` | 41,137,100 B (39.23 MiB) | 136,770,553 B |
| this branch `025dda7a92` | 41,397,855 B (39.48 MiB) | 137,571,653 B |
| Delta | +260,755 B (+0.25 MiB) | +801,100 B |

The delta includes all of `work/ebilling-r1` not yet on master; 165a adds no package. Below every threshold (+5 MB, 55 MB, 60 MB).

## 6. Fix round 2 (adversarial pass 2)

Owner decision EB-D-203 (recorded by the main session): the Return of a Blocked SPA submission needs no privilege (extends D-73); the reviewer
still needs Write on the invoice; the Return notification uses the submission's own channel.

| # | Finding | Fix | Tests |
|---|---|---|---|
| 1 | Override or Return allowed while the submission is still Processing (the recorded outcome was read before the status). | `SubmissionOverrideService.Classify` answers StillProcessing whenever the status is Processing, before the recorded outcome is read; the status can only refuse. The view shows the recorded state but offers neither Override nor Return. | `Override_OfASubmissionThatIsNotBlocked_Is409_AndNothingChanges` and `Return_OfASubmissionThatIsNotBlocked_Is409_AndNothingChanges` (`still-processing`), `Get_WhileStillProcessing_OffersNothing` |
| 2 | The still-processing case removed the findings, a state production never has. | The `still-processing` shape keeps the Blocked findings, as the persister's commit writes them, and asserts `still_processing` with nothing written, for Override and Return. | as item 1 |
| 3 | A released invoice could stay held: the stamp answered AlreadyDecided while the column state still held. | In the AlreadyDecided branch (before and under the lock), when this verb's stamp is valid and the column state differs from the released state, one write conditioned on the row version puts it back: Override writes status Accepted and code Overridden (this also restores `InvoiceProcedureHold.IsReleased`), Return writes code Returned. No record, Signal or notification. A version conflict answers `concurrent_change`, a fault `unavailable`. Logs `ebilling.release.columns_repaired` with the table and row id. | `Override_AfterTheRelease_ColumnStateDiffers_IsRepaired` (replaces the AlreadyDecided expectation), `Return_AfterTheRelease_ColumnStateDiffers_IsRepaired`, must-not-fire `Repeat_OfAReleaseInItsReleasedState_WritesNothing` (Override, Return), `Repair_OnAConcurrentChange_Is409` |
| 4 | Repository wording described the hold sources too closely. | Test names, display names, remarks and the commit message say "column state differs from the recorded outcome" and "own submission absent". | renamed tests in `InvoiceBlockedHoldTests`, `SubmissionOverrideServiceTests`, `InvoiceCorrectionEndToEndTests`, `InvoiceCorrectionServiceTests` and `InvoiceReviewReadServiceTests`; the 122 inventory row |
| 5 | EB-D-203: the Return notification was always Internal. | The Return notification carries the submission's own channel; Return stays allowed without the privilege (remarks cite EB-D-203). | `Return_OfABlockedSpaSubmission_IsAnnouncedOnTheSpaChannel` |
| 6 | Wrong comment: unreadable findings on the own submission are refused by the own-submission check first. | `InvoiceBlockedHold` remarks and check (2) comment corrected. | comment only |
| 7 | Docs. | Section 2 above: a line decision refuses only when an unapplied correction row of that line exists, and never reads the FR-20 hold. `InvoiceCorrectionReevaluation` says "the invoice's own submission". | docs only |
| 8 | A re-run that commits Accepted on an invoice already Returned to Firm dropped the Return stamp, so the view showed "not blocked". | `InvoiceSubmissionRerun.FindingsAfter` keeps the Return stamp when the invoice is Returned to Firm (the Override stamp is still dropped). | `TheReturnStamp_IsKept_OnlyOnAnInvoiceReturnedToFirm` (Returned to Firm: kept, classifies Returned; In Review: dropped) |

Seeds (each applied, the targeted tests run, then restored byte-identical):

| Seed | Fails |
|---|---|
| S5: the Processing check dropped from `Classify` | 3 (Override and Return `still-processing`, the view's offer) |
| S6: the column-state repair dropped | 3 (Override repair, Return repair, concurrent change) |
| S7: the Return notification hard-coded Internal | 1 (the SPA Return notification) |
| S8: the re-run always drops the Return stamp | 1 (Returned to Firm: stamp kept) |

Gates: filtered unit suite (`InvoiceBlockedHold|SubmissionOverrideService|InvoiceApproval|InvoiceProcedureHold|InvoiceCorrection|InvoiceReviewReadService|InvoiceSubmissionRerun|InvoiceRejectReturn|InvoiceSubmissionPersister|ExternalInvoiceVisibility|ApExport`):
1755 passed, 0 failed, 0 skipped. ArchTests: 2106 passed, 0 failed.

Residual (known limit, fail closed): a submission whose status reads Processing is never decided (item 1), so a repeat cannot write its released
column state back while it reads Processing. Publish size not re-measured: fix round 2 adds no package, endpoint or registration.

Known limits (fix round 2):

- A Return stamp kept through a re-run on a Returned-to-Firm invoice lets a repeat Return rewrite the re-derived reason code to Returned. The invoice is terminal and nothing is released; only the column's record of the re-run is lost.
- The repair does not run while the status reads Processing (fail-closed); an operator clears that by fixing the status or replaying the job.
