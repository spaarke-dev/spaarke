# 095f-2c: the override note and the blocked-return note

> **Task:** `tasks/095f-2c-override-blocked-return-notes.poml` (FULL) · **Design:** `095f-part2-decision-notes-design.md` §1 and §4, as amended by §15 (EB-D-240) · **Base:** `task/ebilling-095f-2c` from a5ad76a8de · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

Every "invoice column" in this task is `sprk_invoice.sprk_decisionnote` (EB-D-240). `sprk_correctionnote` is untouched: its only writer is still the submission persister.

## What was built

| Item | Where |
|---|---|
| `BlockedDecisionRequest(Reason, Note = null)`. The override and return routes pass the note on | `Services/Finance/SubmissionOverrideService.cs`, `Api/Finance/SubmissionOverrideEndpoints.cs` |
| `OverrideAsync` and `ReturnAsync` gain a `note` overload. The existing four-argument members forward `null` | `SubmissionOverrideService.cs` |
| The note is checked by `InvoiceCorrectionNote.Check` after the reason and before any read, lock or write. A refusal is a coded 400 (`ebilling.note_too_long`, `ebilling.note_invalid_characters`) through `DecisionRefusalOf`. The privilege or decider check still comes first, as it did for the reason | `SubmissionOverrideService.DecideAsync` |
| `Values["note"]` is set only for a non-blank (checked, trimmed) note, never `note: null`. The no-note record hashes as before | `SubmissionOverrideService.BuildDecision` (new optional `note` parameter, last) |
| **Override.** `BuildReleaseTransaction` always carries `sprk_decisionnote` on the invoice update (the note, or the required reason when there is no note). It still writes no status, and the review detail only when a Procedure hold is lifted. Both updates stay conditioned on the versions read (`IfRowVersionMatches`), so a fault applies neither | `SubmissionOverrideService.BuildReleaseTransaction` (signature gains `bool writesReviewDetail` and `string decisionNote`) |
| **Blocked return.** The 2a overload: `TransitionAsync(invoiceId, ReturnedToFirm, note ?? reason, ct)`. The note joins the status in the one conditional update | `SubmissionOverrideService.DecideLockedAsync` |
| The remark "writes no other invoice column" is rewritten (class remarks, `BuildReleaseTransaction` summary), and the review-detail remark no longer says an override with no hold "writes only the submission" | `SubmissionOverrideService.cs` |
| `note` declared optional on `override-submission` (ontology-r1 light review; the exact diff is below) | `Services/Signals/Actions/DecisionActionCatalog.cs` |
| The writers guard lists `BuildReleaseTransaction` as a built writer (moved from the pending list). The totals path stays pending (095f-2b) | `tests/Spaarke.ArchTests/InvoiceDecisionNoteWritersGuardTests.cs` |

## Rules as built

1. **One validation, 2a's.** 2000 characters accepted, 2001 refused; a control character other than `\n`, `\r`, `\t` refused; whitespace-only is no note; never truncated or stripped. A refusal answers 400 before any read, lock, record or write, and its detail never echoes the note.
2. **Order.** The decider or privilege check, then the reason (a missing reason with a bad note is `ebilling.reason_required`), then the note, then the reads.
3. **What the column holds.** The note when given, else the required reason, for both verbs.
4. **Override always writes the invoice now.** Before, only an override that lifted a Procedure hold (and found the detail column) wrote the invoice. Now every override runs the release transaction. The invoice's row version is read under the lock, and a missing version is 503 before any write, for every override (it was only for the hold-lifting one).
5. **The release writes no status.** The invoice stays In Review. Its update carries only `sprk_decisionnote`, plus the review detail when a hold is lifted.
6. **Never `sprk_correctionnote`.** `InvoiceDecisionNoteWritersGuardTests` is green with the release transaction as a built writer; the correction-note guard is unchanged.
7. **Repeats.**
   - After the release (the submission already Overridden or Returned): AlreadyDecided, nothing written, the column keeps the first note.
   - A blocked return that finds its invoice already Returned to Firm moves nothing, so it writes no note (the lifecycle's K-1, as in 2a).
   - After the record was written but before the state change (superseded by review round 1, below): the repeat finishes from the stored record. It does not need the first reason or note; the column and the stamp take the stored text.
8. **Logs.** No log line carries note text (tested over success, repeat, a failed write, a version conflict, a failed transition and a refused note).
9. **Firm visibility.** `sprk_decisionnote` was added to no firm-facing reader or allow-list. The stamp the reviewer reads back (`override` / `return` in the findings) is unchanged: it holds the reason, not the note.

## Golden hashes (no note; captured on base a5ad76a8de before any source edit)

The fixed worlds of `SubmissionOverrideServiceTests.World`, the world's default reason ("Negotiated rate is not yet in the rate card."). Two captures on base agreed.

| Decision | `reviewHash` |
|---|---|
| Override, ordinary blocked internal submission | `174D456674D962F45A84422B1AF435E71C5F4782AE92FE38EE906C675AFBA1B7` |
| Blocked return, the same submission | `779A8F87C3A2574B0B5915AD926AD5A5B0DEF04496956D73A3D04CAB8C5D5A98` |
| Override of a Procedure-gap hold (Internal) | `10BFFB69C9AB91B72DECF8E447F204469772A5BE45A17209549DD3B08FBE6CFF` |

Pinned in `SubmissionOverrideDecisionNoteTests` (`Override_NoNote_HashesAsBefore_AndTheColumnTakesTheReason`, `Override_OfAHold_NoNote_HashesAsBefore`, `Return_NoNote_HashesAsBefore_AndTheTransitionTakesTheReason`).

## Catalog diff for ontology-r1 (light review)

`DecisionActionCatalog.cs`, entry `override-submission` only. `return-to-firm` (the blocked return's code too) already declares `note` since 095f-2a.

```diff
         new("override-submission", "Override blocking failure", DecisionLane.Decide, IsNextStep: false, WorkType: null,
-            [new("reason", "Reason for overriding", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
+            [new("reason", "Reason for overriding", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint), NoteParameter()],
```

`NoteParameter()` is the 2a helper: `new("note", "Note", DecisionParameterKind.TextArea, Required: false)`. Two comment lines were added above the entry. The writer reads only required parameters, so this documents the `Values["note"]` key and changes no behaviour. No other catalog entry, gate factor or exclusion changed.

## Tests

| Criterion | Test |
|---|---|
| The release transaction carries the column on the invoice update, writes no status and no correction note, submission first, both `IfRowVersionMatches` | `SubmissionOverrideDecisionNoteTests.Override_Note_IsInTheRecord_AndOnTheInvoiceUpdate_WithNoStatus` |
| With a hold lifted the same update carries the note and the review detail, and still no status | `Override_ThatLiftsAHold_WritesTheNoteAndTheDetailTogether` |
| A version fault (invoice, or submission) applies neither update; the repeat lands the note once | `Override_VersionFault_AppliesNeitherUpdate_AndTheRepeatLandsTheNote` |
| No note: reason in the column, no Values key, golden hash (null, `""`, whitespace) | `Override_NoNote_HashesAsBefore_AndTheColumnTakesTheReason`, `Override_OfAHold_NoNote_HashesAsBefore`, `Return_NoNote_HashesAsBefore_AndTheTransitionTakesTheReason` |
| The note is inside the hash | `Note_IsInsideTheHash` |
| Blocked return note via `TransitionAsync`; no second invoice write | `Return_Note_IsInTheRecord_AndPassedToTheTransition` |
| A return that moves nothing writes no note; a lifecycle refusal writes no state | `Return_InterruptedReturn_WritesNoNote`, `Return_WhenTheTransitionIsRefused_WritesNoState` |
| 2001 characters, `\u0007`, `\u001b`: 400 before any read, lock or write, nothing echoed | `RefusedNote_Is400_BeforeAnyReadLockOrWrite` (override and return) |
| The reason is checked before the note | `MissingReason_IsRefusedBeforeTheNote` |
| 2000 characters and 1 character accepted untruncated; `\n \r \t` kept | `NoteUpToTheLimit_IsAccepted_Untruncated` |
| Repeats: another note before the state change is a 409 conflict; the same note completes; a repeat after the release writes nothing | `Override_RepeatBeforeTheState_WithAnotherNote_IsRefused_AndTheSameNoteCompletes`, `Override_RepeatAfterTheRelease_WritesNothing_AndKeepsTheFirstNote` |
| No log line carries the note | `NoLogLine_CarriesTheNote` |
| HTTP: both routes take the note; a refused note is a coded 400 and writes nothing | `SubmissionOverrideEndpointsAuthorizationContractTests.Post_WithANote_Is200_AndRecordsIt`, `Post_WithARefusedNote_Is400_AndNothingChanges` |
| Catalog | `DecisionActionCatalogTests.InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter` (adds `override-submission`) |
| Writers guard | `InvoiceDecisionNoteWritersGuardTests` (the release transaction is now a built writer, so its staleness is checked) |

**Existing tests restated** (all in `SubmissionOverrideServiceTests.cs`; the behaviour they pinned changed by design):

- The test world's strict lifecycle mock is set up on the four-argument `TransitionAsync` (and records `TransitionNotes`), because a Return now calls that overload. The same change applies to the F1-a helper that sets up a second invoice.
- `Release_KeepsTheFactsShape` asserted "an override that lifts no Procedure hold writes only the submission". It now asserts the transaction's invoice update carries only the decision note (no review detail).
- `Release_WhenTheDetailColumnIsMissing_StillReleases_WithoutTheInvoiceWrite` is renamed `..._WithoutTheReviewDetail`. Without the review detail column, the invoice update carries only the note.
- `Release_OfTheLastHold_BlanksTheDetail_InTheSameTransaction` asserted the invoice update held only the detail. It now asserts the detail and the note.

Every other existing `SubmissionOverrideServiceTests`, contract and `InvoiceRejectReturn*` test passed unchanged.

**Seeding (fail-before).** Seeds applied to the source, the note tests run, the files restored:

| Seed | Failed |
|---|---|
| Override column takes the reason, never the note; Values never gets the note; Return passes `note` (not `note ?? reason`); the note check is replaced by a pass-through (run together) | 27 of 37 |
| Override column takes `note ?? ""` | 5 of 5 (the override no-note tests) |
| `values["note"] = note` always (a null-valued key) | 8 of 8 (the no-note and golden tests) |

The three restated tests failed on the new source before they were restated (3 failed of 361 in the override suites). The goldens were captured on base before any source edit.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice\|FullyQualifiedName~Ebilling"` | 16967 passed, 0 failed, 12 skipped (16979) |
| The override, note, reject/return, lifecycle, catalog, approval and hold suites (narrow filter) | 1335 passed, 0 failed, 5 skipped |
| `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~Invoice\|FullyQualifiedName~Decision\|FullyQualifiedName~Catalog"` | 147 passed, 0 failed |
| ArchTests, route, access-table, findings, money and ledger guards | 202 passed, 0 failed |
| Publish size (`dotnet publish -c Release`; base a5ad76a8de: the same tree with the four changed source files checked out at base) | Branch 140,461,113 B; base 140,459,925 B; delta **+1,188 B** |
| New package, new HIGH CVE | None. No `.csproj` or props file changed |

The publish delta is against the branch base, not a fresh master build, because the work branch carries unmerged code (as for 095f-2a). The whole ArchTests project and the whole BFF test project were not run in one process (machine limit); the filtered suites above were.

## Component justification (CLAUDE.md section 11)

The new surface is an optional `Note` on an existing request record, two forwarding `note` overloads on the existing service, an optional `note` parameter on one catalog entry, and an `InvoiceDecisionNoteWritersGuardTests` list move. (1) Existing: `InvoiceCorrectionNote.Check`, the `TransitionAsync` overload and `NoteParameter()` from 095f-2a; the release transaction itself. (2) Extension: all three are reused as they are; nothing new is built. (3) Cost of doing nothing: the reviewer's override and blocked-return notes (EB-D-236) have nowhere to be stored, so the internal "why" of two of the invoice-level decisions is lost.

## Known limits

(a) **Deploy order.** Override and blocked return now always write `sprk_decisionnote`. This BFF must never reach an environment before `scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Apply` has created the column there (dev before the work branch merges to master; SpaarkeMaster through task 079). Without the column an override fails 503 at its release write, with the record already written (a repeat after the column exists completes it), and a blocked return fails 503 at its transition. This is the same limit as 095f-2a (a).

(b) **A wider version condition on override.** Every override's release is now conditioned on the invoice's row version, where before only a hold-lifting one was. An unrelated write to the invoice between the read under the lock and the release (for example a totals recalculation) is retried inside the call (review round 1, F1: a re-read under the lock, up to 3 release attempts in all); only when the bound is used up does it answer 409 `ebilling.blocked.concurrent_change`, applying neither update, and a repeat then finishes it with the one record. A missing invoice version is 503 before any write, for every override.

(c) **When a repeat completes, and what it uses (replaces the first build's "refused, not dropped").** A repeat that finds the decision's writer-created record finishes THAT decision. The request's reason and note are ignored: the invoice column, the stamp and the Signal closes take the stored note (or stored reason, with no note), the stored record and the stored decider, and the Decision Record writer is never asked to record again. Exactly:
   - **Completes** (200 Decided) when the decision's record exists, the caller is the decider the record names, and the record can be read. This covers a repeat after the record but before the closes, after the closes but before the state change, for a return after the transition but before the submission write, and after a release that was refused 409 by an exhausted invoice fault. The text sent is irrelevant; sending none, or another reason or note, is fine.
   - **Refused 409 `decision_conflict`, nothing written** when the caller is not the stored decider (another person's unfinished decision is theirs to complete, as with reject and return's claimed approval). The original decider's repeat then completes it.
   - **Refused 503, nothing written** when the stored record cannot be read as a decision (no stored reason, no readable step or values, a note that is not text).
   - **AlreadyDecided, nothing written** after the release (the submission already Overridden or Returned); the column keeps the first note.
   - A repeat BEFORE the record exists (the record write itself failed) is a first attempt and uses its own reason and note.

(d) **The column's text with no note is the reason.** It is checked by the reason's own rule (length only), not the note's control-character rule. Same as 095f-2a (e).

(e) **The blocked reviewer view does not show the note.** The stamp read back by `GET /blocked` carries the reason only. Showing the note is client work (095f-2ui).


## Review round 1

Base 9414f92d9b, branch `task/ebilling-095f-2c-fix`. An adversarial review of 095f-2c raised the findings below. Evidence column: "fail-before" is the new test run against the pre-fix source (copied back from the base commit), the way the earlier rounds did it.

| Finding | Fix | Tests | Fail-before |
|---|---|---|---|
| **F1.** Every override's release is conditioned on the invoice's row version, and the invoice has writers that do not take the decision lock (the totals recalculation, which runs from line decisions, adjustments, line-add, the manual recalc route and the persister's after-steps; direct form edits). A version fault went straight to 409 `concurrent_change` with no retry. | `DecideLockedAsync` retries the release when the invoice's request faulted: `DataverseTransactionException.FailedRequestIndex == 1` (`ReleaseInvoiceRequestIndex`, the invoice's position in `BuildReleaseTransaction`; the submission is `0`). Under the same lock it re-reads the invoice (`ReReadInvoiceForReleaseAsync`): still In Review (else 409 `invoice_not_held`), firm gate open (else 409 `firm_missing`), a row version (else 503), the review detail re-derived through the one derivation when it is written (`ReasonsAfterReleaseAsync`). Bound and exhaustion copy `InvoiceLifecycleService.TransitionAsync`: `InvoiceReviewService.MaxConditionalWriteAttempts` (3 release attempts in all), then the existing 409 through `AfterConflictAsync` (so a concurrent run that finished the decision still answers AlreadyDecided). A submission-side fault, or a fault Dataverse does not attribute to a request, is not retried (as before). The record, the set store and the closes are not repeated by the retry. | `ReleaseRetry_AnInvoiceWriteInTheWindow_IsRetried_AndTheOverrideCompletesOnce`, `..._ReDerivesTheReviewDetail_WhenItIsWritten`, `..._RepeatedInvoiceFaults_EndInThe409_AtTheBound_WithNothingApplied`, `..._AReReadThatFindsTheInvoiceNoLongerInReview_StopsWithoutWriting`, `..._AReReadThatFindsTheFirmGateClosed_StopsWithoutWriting`, `..._AReReadThatFaults_Is503_AndWritesNothing`, `..._ASubmissionSideFault_BehavesAsBefore`, `..._AFaultWithNoFailedIndex_IsNotRetried` (all in `SubmissionOverrideServiceTests.ReleaseRetry.cs`) | 7 of 8 fail before (the no-index test fails only on its wording). The submission-side test passes before and after: it pins unchanged behaviour. |
| **F1 (wording).** The 409 said "The submission changed" when the invoice had. | `AfterConflictAsync` takes the side: "The invoice changed...", "The submission changed...", or, with no failed index, "The submission or its invoice changed...". `BlockedSubmissionErrors.ConcurrentChange` doc says what the code now means. | The exhaustion test asserts the invoice wording; the submission-side test the submission's; the no-index test the neutral one. | As above. |
| **F1, found in passing (pre-existing, made likelier by the retry gap).** A stranded override or blocked return could be finished only by retyping the exact reason and note: a repeat called `_record` again and the writer refused another text as 409 `decision_conflict`; `GET /blocked` shows only the stamp, written at the state change, so no client could recover the first text. | When `_findStored` finds the writer-created record, the call finishes from it and never calls `_record`: `BillingStoredResolvedSet` now carries the record's stored `Reason` (`sprk_reason`), `Note` (the taken step's `values.note`, read the way approve reads `StoredApprovedAmount`) and `StoredContentReadable`. The column (`note ?? reason`), the stamp and the transition take the stored text; the Signal closes take the stored decider. The decider rule is reject and return's "claimed" rule: another person's unfinished decision is theirs to complete, so a caller that is not the stored decider gets 409 `decision_conflict` with nothing written (it was the writer's refusal before, now the same answer by an explicit check). A record that cannot be read (no stored reason, no step or values, a note that is not text) is 503 with nothing written. The no-note record and its hash are unchanged. | `SubmissionOverrideDecisionNoteTests`: `Strand_RepeatWithAnotherText_FinishesFromTheStoredRecord` (8 strands: override release failed, closes and release failed, invoice bound exhausted; return transition failed, return state write failed; with another note, no note, another reason; the column and stamp hold the stored text, one record, one `Recorded`), `Strand_RepeatByAnotherDecider_IsRefused_AndWritesNothing` (2), `Strand_AnUnreadableStoredRecord_IsRefused_AndWritesNothing` (6). The golden-hash tests are unchanged and pass. | 16 of 16 fail before. |
| **F2 (docs).** | `SubmissionOverrideEndpoints` remarks name the `note` body field; the `DecisionConflict` doc, the class remark and the refusal detail describe the repeat as built; known limit (c) says exactly when a repeat completes. | - | - |
| **K3.** No test pinned the 503 for an invoice read without a row version on an ordinary override. | Test only; behaviour unchanged. | `Override_AnInvoiceReadWithoutARowVersion_Is503_BeforeAnyWrite` | Passes before (characterises existing behaviour); with the guard seeded away it fails. |

One existing test was restated: `Override_ARetryAfterTheSubmissionWriteFailed_FindsTheSameRecordAndFinishes` asserted that the retry asked the recorder a second time (`Recorded` of 2). It now asserts the retry never asks again (`Recorded` of 1); `RecordCreates` stays 1. The restated 2c test `Override_RepeatBeforeTheState_WithAnotherNote_IsRefused_AndTheSameNoteCompletes` (another note refused) is replaced by the strand tests above, because that refusal is exactly the defect fixed.

### Known limits added by this round

- **Live check of the failed request index.** The retry keys on `FailedRequestIndex == 1`, which `MapTransactionFailure` sets from the transaction fault's `FaultedRequestIndex`. It is proven against the double that reports the index as `MapTransactionFailure` words it; it has not been seen against a live version-mismatch fault. If Dataverse reported no index, the behaviour is the old one (409 `concurrent_change`, a repeat finishes it).
- **A stranded decision belongs to its decider.** If the decider cannot return to it, no one else can finish it from the route (409 `decision_conflict`). This is the rule reject and return already apply to a claimed approval; changing it needs an owner decision.
- **Client (095f-2ui).** Keep the reason and note across a 409 or 503 so the reviewer can resend, and do not reload the page on `concurrent_change`: the retry inside the call makes it rare, and a repeat finishes it.
