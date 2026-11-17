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
   - After the record was written but before the state change, a repeat must carry the first reason and note. Another note is refused by the Decision Record writer as 409 `ebilling.blocked.decision_conflict`, exactly as another reason already was. The detail text now reads "another reason, note or decider".
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

(b) **A wider version condition on override.** Every override's release is now conditioned on the invoice's row version, where before only a hold-lifting one was. An unrelated write to the invoice between the read under the lock and the release (for example a totals recalculation) answers 409 `ebilling.blocked.concurrent_change`, applies neither update, and a repeat finishes it with the one record. A missing invoice version is 503 before any write, for every override.

(c) **A repeat with another note before the state change is refused, not dropped.** Override and blocked return build one Decision Record from the reason and note, and the writer refuses another content under the same decision id (409 `decision_conflict`, "another reason, note or decider"). This is the existing behaviour for a changed reason, extended to the note. 095f-2a's reject and return drop an edited note on a repeat because they have their own repeat path; override and blocked return never had one. A repeat after the release is AlreadyDecided and writes nothing, so the first note stands.

(d) **The column's text with no note is the reason.** It is checked by the reason's own rule (length only), not the note's control-character rule. Same as 095f-2a (e).

(e) **The blocked reviewer view does not show the note.** The stamp read back by `GET /blocked` carries the reason only. Showing the note is client work (095f-2ui).

