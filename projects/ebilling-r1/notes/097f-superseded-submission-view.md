# Task 097f: the status read of a superseded invoice's submission

Closes the 097c open item (EB-D-180, owner 2026-10-30): when a correction supersedes an invoice, the old invoice's submission stays Accepted and its status read says why the invoice is no longer live. Branch `task/ebill-097f-superseded-view`, base `2220b995f8` on `work/ebilling-r1`. Rigor FULL (the task changes what a caller is told about another submission; tags bff-api, auth). The wire contract is `notes/095-from-email-contract.md` section 9, "Replaced by a correction" (Added 2026-11-01).

## Decision EB-D-180 (kept as it stands)

The owner's decision is unchanged and is "Accepted" in `decisions-log.md`: the submission keeps status Accepted, the view adds an outcome reason that the invoice was replaced by a correction with the correcting submission's tracking number, derived at read time from the invoice (Superseded) and the new submission's link. No new submission status, no schema change. This task does not edit the log.

## Which routes show a processing status (the surfaces)

Every route that returns `InvoiceSubmissionView` to a client. All three carry the note.

| # | Route | Who | Access rule for the correcting submission |
|---|---|---|---|
| S1 | `GET /api/finance/invoice-submissions/{id}` | internal callers; the add-in's status poll (`statusUrl` of the from-email and from-document routes) | the caller's own Read on that submission (`finance.read`, `CallerHoldsRightsAsync`), the right the route's filter asks for the submission itself |
| S2 | `GET /api/v1/external/invoice-submissions/{id}` | external SPA callers (firm view) | the replacement's firm is one of the caller's firms, the caller can Read its core record, and `VisibleSubmissionsAsync` (the one (a') seam both external reads use) keeps it |
| S3 | `GET /api/v1/external/invoice-submissions` | external SPA callers (firm view, list) | the same as S2, decided for the replacements of the page |

Checked and not surfaces: `InvoiceSubmissionService.GetAsync` and `ListAsync` themselves (also used by the intake's replays, the confirm route and the visibility decisions, which must see the stored view); `InvoiceReviewService` and `InvoiceReviewReadService` (read a submission's stored status for review flows, return no status view); the `ExternalDataService` and `ExternalInvoiceVisibility` readers (visibility decisions). The note is added by the routes, through one service method, so none of those sees it.

No surface needed a client contract change to carry it. The SPA shows Accepted without reading the code (`InvoiceSubmissionOutcome.tsx`). One add-in behaviour needs word-add-in-r1's attention; see "For word-add-in-r1".

## Response shape

Unchanged fields stay. For an Accepted submission whose own invoice is Superseded:

```json
{ "submissionId": "...", "trackingNumber": "INV-7.1", "status": "Accepted",
  "outcomeReasonCode": "ebilling.submission.replaced_by_correction",
  "replacedBy": { "submissionId": "...", "trackingNumber": "INV-7.2" } }
```

- `outcomeReasonCode` takes the place of any code stored on the Accepted row (an uncoded or overridden hold no longer applies to a closed invoice). Nothing is written.
- `replacedBy` is omitted from the JSON (not `null`) unless the caller may read the correcting submission. Rationale: the property is `WhenWritingNull`, so every other answer keeps its exact bytes (tested byte-for-byte). A client reads absent and null the same way; the contract says so.
- Only an Accepted submission carries it. A Rejected submission whose held (Blocked) invoice was later superseded is answered as stored (see Known limits).

## What changed

| Area | File | Change |
|---|---|---|
| The note | `Services/Finance/Submission/SupersededSubmissionNote.cs` (new) | `FindReplacementAsync` (the lookup, shared with 097e) and `ApplyAsync` (the page read of Superseded invoices, then the lookup for those only). `ReplacementReadGate` is the delegate each route supplies. |
| Service | `InvoiceSubmissionService.cs` | `WithReplacementNoteAsync` (one line over the note); `ViewColumns` and `IsNotFound` made `internal`. `GetAsync` and `ListAsync` are untouched. |
| View | `InvoiceSubmissionContracts.cs` | `InvoiceSubmissionView.ReplacedBy` (`WhenWritingNull`) and `SubmissionReplacement`. |
| Code | `SubmissionReasonCodes.cs` | `ReplacedByCorrection`, under a "derived at read time" heading (never stored; the held set reads the stored column, so it is not a hold). |
| S1 | `Api/Finance/InvoiceSubmissionEndpoints.cs` | `GetAsync` applies the note with the Read gate. |
| S2, S3 | `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs` | `GetAsync` is now async and applies the note; `ListAsync` applies it to the visible page; one shared `ReplacementGate`. |
| 097e | `Api/Finance/InvoiceSubmissionFromEmailHttp.cs` | `FindReplacementAsync` now calls the shared lookup (same results; the lookup reads the submission's `ViewColumns` instead of one column). |
| Guard | `tests/Spaarke.ArchTests/ResubmissionOfAppOnlyWriteGuardTests.cs` | C1 reviewed list: `SupersededSubmissionNote` replaces `InvoiceSubmissionFromEmailHttp` (the lookup moved there, so the HTTP file no longer names the column). Both are app-only readers through `IGenericEntityService`; C2 and C3 pass for it. |
| Tests | see below | 15 contract cases and 5 unit cases; the harness answers the page read of Superseded invoices. |
| Contract | `notes/095-from-email-contract.md` | Sections 3 and 9. |

## Decisions

- **The note is enrichment, so its faults never fail the call (deliberate, as 097e's `replacedBy`).** A fault finding the replacement or deciding the caller's right to it leaves the reason code and no `replacedBy` (Warning). A fault finding which invoices are Superseded leaves the answer exactly as it was before this task (the stored status is still true). None is a 503. Open for the owner if a 503 is preferred for the last case; it would make a status poll fail on a secondary read.
- **One read per page.** `ApplyAsync` reads which of the page's Accepted submissions' invoices are Superseded with one query per 500 invoice ids; the replacement lookup (2 reads and one per candidate invoice) runs only for the Superseded ones, which are rare. A page with none costs one extra read; a view that is not Accepted or has no invoice costs none and is returned as the same instance.
- **The access rule is each route's own.** S1 asks the caller's Read on the replacement submission, the right its filter asks for any submission. S2 and S3 apply what lets an external caller see a submission at all: firm, core-record Read, and the one (a') decision. The replacement is read app-only only to choose it (`sprk_resubmissionof` is field-secured: read and written only by the BFF application user).
- **The lookup is 097e's.** One implementation chooses the replacement for the prefill (`replacedBy` there is an invoice id and name) and the status reads (a submission id and tracking number). The two fields have the same name and different shapes by agreement with each surface; the contract says so.

## Component justification (§11)

- **`SupersededSubmissionNote` (`FindReplacementAsync`, `ApplyAsync`, `ReplacementReadGate`, `FoundReplacement`).** Existing: 097e's `FindReplacementAsync`, a private static of the from-email HTTP file for the prefill route. Extension: it is extracted and shared, not copied; `ApplyAsync` is the new part (a page read plus the note) and needs the same Dataverse reads and the same tie-break. It cannot live on `InvoiceSubmissionService.GetAsync` because that is also used by the intake's replays and the visibility decisions, which must see the stored view. Cost of doing nothing: the owner's EB-D-180 is not met; the add-in and the SPA would show a superseded invoice's submission as a plain Accepted one with no hint that a correction replaced it.
- **`InvoiceSubmissionView.ReplacedBy` and `SubmissionReplacement`.** Existing: the view has `OutcomeReasonCode` only (a string). Extension: a reason code alone cannot carry the correcting submission's tracking number, which EB-D-180 requires. Cost of doing nothing: the status read cannot name the correcting submission.
- **`SubmissionReasonCodes.ReplacedByCorrection`.** Existing: the one class of submission reason codes. Extension: added there, marked derived. Cost of doing nothing: a literal in the routes and the contract.
- **`InvoiceSubmissionService.WithReplacementNoteAsync`.** Existing: `GetAsync`/`ListAsync`. Extension: a separate method, so the stored view stays available to the service's other callers. It is what gives the routes the service's Dataverse client without injecting `IGenericEntityService` into three handlers. Cost of doing nothing: three routes would each reach Dataverse themselves.
- **No new route, endpoint file, DI registration, package, column, background work or schema.** The three handlers gain injected parameters of types already registered (`AuthorizationService`; the external hiding seam and logger the list already took).

## BFF hygiene (§10)

Placement: `Sprk.Bff.Api`, `Services/Finance/Submission` and the two existing endpoint files, where the status reads already live (the BFF is the single backend for the add-in and the SPA; ADR-001, ADR-008). No DI registration, package or project file changed; no AI facade involved.

**Publish size.** Command `dotnet publish -c Release -r linux-x64 --self-contained false <project> -o <dir>\deploy\api-publish`, framework-dependent linux-x64, Release, each side exported with `git archive` to a short path (`C:\w97fm` fresh master `b50301a219`, `C:\w97fb` this task's code head `0666b656ee`), zipped with `Compress-Archive -CompressionLevel Optimal` over `api-publish\*`, PDBs included, **192 files on both sides**. Both folders were plain exports (not worktrees) and were deleted afterwards.

| Side | Zipped | `Sprk.Bff.Api.dll` |
|---|---|---|
| master `b50301a219` | 40,980,995 B (39.08 MiB) | 24,243,200 B |
| this head `0666b656ee` | 41,103,975 B (39.20 MiB) | 24,548,864 B |

Delta against the fresh master build: **+122,980 B (+0.12 MB)**, dll +305,664 B. That is the whole of `work/ebilling-r1` over master, not this task alone (the base was not measured separately; 097e measured the earlier base at +116,864 B). Well under the +5 MB trigger and the 55 MB review line.

**CVE check.** `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive`: no vulnerable packages. No package or project file changed.

## Tests

Contract cases in `InvoiceSubmissionEndpointsContractTests` (the existing host that carries both route families, the real filters, the real kill switch and the real (a') seam over `InvoiceWorld`), 15 cases; unit cases in `SupersededSubmissionNoteTests`, 5 cases. The harness (`InvoiceSubmissionServiceTests.Harness`) answers the page read of Superseded invoices and can fault it.

The POML scope: a Superseded invoice gives Accepted, the code and `replacedBy` (internal and external one read; the list case below); the caller cannot read the replacement gives the code with no `replacedBy` and no trace of it (internal, and external by the (a') decision); not Superseded is the same bytes (4 cases, plus the not-Accepted case); a replacement-read fault gives the code and no `replacedBy` (both read families); the seed proof.

Beyond that, one-line justifications:

- `External_ReplacementOfAnotherFirm_IsNotNamed`: the external rule's firm test (the two-sided control, a replacement of the caller's own firm is named).
- `SupersededReadFault_LeavesTheAnswerUnchanged`: the deliberate choice above.
- `SeveralReplacements_PreferTheLive_ElseNone`: the agreed tie-break.
- `ExternalList_NotesOnlyTheSuperseded_WithOneStatusRead`: the list surface and its one-read bound.
- `NotAccepted_IsUnchanged`: pins the scope (Accepted only).
- Unit: the gate that faults (the rights fault, which the host cannot raise), a gate that names none, no replacement found (the gate is not asked), and the same-instance guarantee for unaffected views.

**Fail before.** `WithReplacementNoteAsync` made to return its views untouched: 9 of the 15 contract cases fail (the cases that expect the note); the 6 controls pass (not Superseded x4, not Accepted, status-read fault). Restored: all pass.

**Seed proof, the Read check on the replacement** (a mutation, the tests turned red, the mutation reverted): on S1 `CallerHoldsRightsAsync` replaced by `true`, and on S2/S3 the firm, core-record and (a') decision replaced by "all". Result: exactly the three cases that deny the replacement fail (`Superseded_CallerCannotReadTheReplacement_...` for the internal and the external route, and `External_ReplacementOfAnotherFirm_IsNotNamed`); the other 12 pass.

## Gate

Measured on code head `0666b656ee` (the later commits change notes only).

| Step | Result |
|---|---|
| Build, 5 test projects (Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests) | 0 warnings, 0 errors each |
| Unit filter `Submission\|Invoice\|Finance` (Sprk.Bff.Api.Tests) | 6334 passed, 0 failed, 5 skipped |
| ArchTests (full) | 1869 passed, 0 failed (ISS-005 did not flake) |
| Sprk.Bff.Api.IntegrationTests | 199 passed, 0 failed, 8 skipped |
| `ResubmissionOfAppOnlyWriteGuardTests` (C1 to C4 and controls) | pass, inside the full ArchTests run |

`Spe.Integration.Tests` and the provisioning `LoadTests` are built, not run (they cover other areas; the task touches neither).

## For word-add-in-r1

`src/client/office-addins/shared/taskpane/services/invoiceSubmissionService.ts` (about line 636) treats an Accepted submission with ANY `outcomeReasonCode` as held for review (contract section 9 until now: "while an Accepted invoice is held"). The add-in only polls the submission it just created, which is not superseded, so a normal submit is unaffected. It would show the held message if it polled the status of a submission whose invoice has since been replaced: the one place that can happen is a duplicate replay (section 5) that answers the earlier submission's `statusUrl` after a correction replaced it. The contract now says `replaced_by_correction` is not a hold. The add-in's fix is one condition (treat that code as a plain Accepted); it is not made here (not this task's files, and a client change). Ask word-add-in-r1 to agree the contract text and make that change.

## Known limits and things noticed

- K: only Accepted submissions carry the note. A Rejected submission held as Blocked (D-101) whose invoice a correction later superseded is answered as stored (Rejected, `blocked`); the intake allows that correction (`CorrectionTargetSubmissionFinished`). The owner's decision speaks of the accepted submission; if the held case should also say "replaced", it needs its own status wording (Rejected is not Accepted) and is a decision.
- K: `replacedBy` is the direct replacement only, as in 097e; a replacement that is itself later superseded is named as it is.
- K: a correction attempt that failed and left no invoice is not a replacement (only invoice-bearing submissions are).
- Noticed, not changed: `InvoiceSubmissionService` is a very large class (about 2,000 lines of cohesive intake and read code); this task adds one method, not a responsibility.
