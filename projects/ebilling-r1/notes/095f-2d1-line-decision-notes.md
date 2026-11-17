# 095f-2d1: the line decision note (accept, reduce, disallow); Accepted in bulk carries none

> **Task:** `tasks/095f-2d1-line-decision-notes.poml` (FULL) · **Design:** `095f-part2-decision-notes-design.md` §1, §5.1 and §8, as amended by §15 (EB-D-240) · **Base:** `task/ebilling-095f-2d1` from 545ba823dc · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

A line decision's note lives only on that line's own Decision Record (`Values["note"]` inside `sprk_steps`, D-75). It is never written to `sprk_invoice.sprk_decisionnote`, `sprk_invoice.sprk_correctionnote` or a correction row (`sprk_invoicecorrection.sprk_note`). No new column is needed, so there is no deploy-order limit for this task.

## What was built

| Item | Where |
|---|---|
| `SingleLineDecisionRequest` and `MultiLineDecisionRequest` gain an optional last member `Note` (default null, so existing callers and bodies bind unchanged). `AcceptCleanRequest` is unchanged | `Api/Finance/InvoiceLineReviewEndpoints.cs` |
| The two routes pass `body.Note` to `LineDecisionCommand`. Accept all Clean passes none | same |
| `LineDecisionCommand` gains `Note` (last, default null) | `Services/Finance/InvoiceLineDecisionService.cs` |
| `ShapeError` checks the note with `InvoiceCorrectionNote.Check` and maps a refusal through `DecisionRefusalOf` to `ebilling.note_too_long` / `ebilling.note_invalid_characters`. It runs after the reason checks and before the amount checks, in `RunAsync` before the lock and before any read. The detail never echoes the note | same |
| `BuildDecision` builds `Values` only when needed: `approvedAmount` for a Reduce, `note` only when the checked, trimmed note is non-blank. No note means no key (never `note: null`), and an Accept or Disallow without a note keeps `Values` null, exactly as before | same |
| `note` (TextArea, optional) declared on `accept-line`, `reduce-line` and `disallow-line` (diff below) | `Services/Signals/Actions/DecisionActionCatalog.cs` |
| Endpoint descriptions and class remarks: the note, its two 400 codes, "Accept all Clean takes no note", and what a repeat does | `InvoiceLineReviewEndpoints.cs` |

Unchanged on purpose: `AcceptCleanAsync`, `AcceptCleanHeldAsync` (the approval's bulk accept) and `CompleteBulkAcceptHeldAsync` build their commands without a `Note`, so they never carry one, even when the approval itself has a note. `LineFields` (the line's own row update) is untouched.

## Rules as built

1. **One validation, 2a's.** 2000 characters accepted, 2001 refused; any control character other than `\n`, `\r`, `\t` refused; whitespace-only is no note; never truncated or stripped. A refusal is a 400 before any lock, read, write or record.
2. **Order.** Request shape (ids, lines, reason) first, then the note, then the amount rules. A missing reason with a bad note answers `ebilling.line.reason_required`.
3. **One note, N records.** A multi-line call has one note; each selected line's own record carries it, as the one reason is applied to every line (design K-2).
4. **Hash.** The note is inside the content hash. Without a note (null, empty or whitespace-only) the hash is byte-identical to before (golden values below).
5. **Repeats (the line service's existing behaviour, tested).** A line decision's repeat always reaches the writer: the lines are rewritten with the same values, then the record is looked up by decision id.
   - **Same decision id, same note:** applied; every line answers `alreadyRecorded`; no second record.
   - **Same decision id, another note, or the note dropped:** the writer refuses it as other content (the same refusal that a changed reason or amount already gets). The route answers 503 `ebilling.decision.incomplete` with `failedStep` `record`; no second record is written; the first note stays on the record. The line rows are rewritten with the same values first, so nothing else changes. A client that retries after a failure must resend the same note; a different note needs a new decision id.
   - This differs from reject and return (2a, K-1), where an edited note on a repeat after the record is silently dropped: those services never call the writer again once their record exists. The line service always does.
6. **Logs.** The service logs ids, counts and exception type names only. No log line carries note text (tested over the service's and the recorder's loggers).
7. **Firm visibility.** Nothing was added to any firm-facing reader or allow-list; the note is in `sprk_steps` of a Decision Record, which no external allow-list exposes.

## Golden hashes (no note; captured on base 545ba823dc before any source edit)

Fixed world: submission `09500000-0000-4000-8000-00000000f2a1`, one Flagged 400 line, decision id `09500000-0000-4000-8000-00000000d201`, decider Alice, reason "Rate above the card" (Reduce and Disallow), Reduce to 300, single-line route (Bulk false). Two captures on base agreed.

| Decision | `reviewHash` |
|---|---|
| `accept-line` | `7AC93232492FFDCB1B61DBA27964018A498F81496AFD28B70070789639FE3343` |
| `reduce-line` | `4E5038DC295FCA9EAC63DD5AF7725BDA866E39124DC93E169616275EBCCC36CD` |
| `disallow-line` | `3BC0C09CC3810B18F1741BEADE63FD64DB03BE44C4875843E8A816BBECFA3240` |

Pinned in `InvoiceLineDecisionNoteTests.NoNote_HashesAsBefore` (null, `""`, whitespace-only for each act).

## Catalog diff for ontology-r1 (light review)

`DecisionActionCatalog.cs`, entries `accept-line`, `reduce-line` and `disallow-line` only. `NoteParameter()` is the 2a helper: `new("note", "Note", DecisionParameterKind.TextArea, Required: false)`.

```diff
         // ---- Line decisions (subject: the invoice line) ------------------------------------------------------
+        // ebilling-r1 095f-2d1 (EB-D-236; design 8, Q6): "note" declared optional on the three line decisions, the Values key the line decision
+        // service writes on each line's own record when a note is given. The writer reads only required parameters, so declaring it changes no
+        // behaviour. (Accepted in bulk writes accept-line records with no note.)
         new("accept-line", "Accept line", DecisionLane.Decide, IsNextStep: false, WorkType: null,
-            [],
+            [NoteParameter()],
...
                 new("reason", "Reason for reduction", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint),
+                NoteParameter(),
             ],
...
         new("disallow-line", "Disallow line", DecisionLane.Decide, IsNextStep: false, WorkType: null,
-            [new("reason", "Reason for disallowing", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint)],
+            [new("reason", "Reason for disallowing", DecisionParameterKind.TextArea, Required: true, Hint: ReasonHint), NoteParameter()],
```

The writer reads only required parameters, so this documents the `Values["note"]` key and changes no behaviour. No other entry, gate factor, exclusion or record class changed. `DecisionActionCatalogTests.BillingActions_RequireExactlyTheStatedParameters...` (required parameters only) stays green; `InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter` gains the three line codes.

## Tests

New file `tests/integration/data-mutation/Ebilling/InvoiceLineDecisionNoteTests.cs` (43 cases, the real line decision service, totals, recorder and writer over `ApprovalWorld`), plus 12 cases added to `tests/integration/contract/Api/Finance/InvoiceLineReviewEndpointsAuthorizationContractTests.cs` and 3 rows to `DecisionActionCatalogTests`.

| Criterion | Test |
|---|---|
| A single-line note is in the line's own record, trimmed, `\r\n` and `\t` kept; reason and approved amount unchanged | `Note_IsInTheLinesOwnRecord` (3 acts) |
| One note goes onto N records in a multi-line call (3 lines, 3 records, same note); none on the bulk correction rows | `MultiLine_OneNote_GoesOnEveryRecord` (3 acts) |
| No note (null, `""`, whitespace) means no key and the golden hash, per act | `NoNote_HashesAsBefore` (8 cases) |
| A note changes the hash | `Note_IsInsideTheHash` (3 acts) |
| Must-not-fire: Accept all Clean records carry no note and no correction row has a note | `AcceptClean_Records_CarryNoNote` |
| Must-not-fire: the approval's bulk accept records carry no note while the approval's own record and column hold its note (controls) | `ApprovalBulkAccept_CarriesNoNote_EvenWhenTheApprovalHasOne` |
| Must-not-fire: no line decision writes `sprk_decisionnote` or `sprk_correctionnote` (prior values stand), and the note is on no row but its record | `NoLineDecision_WritesAnInvoiceNoteColumn` (3 acts), `NoLineDecision_AddsTheColumnsToAnInvoiceWithout`; plus the existing `InvoiceDecisionNoteWritersGuardTests` (see seed 5) |
| 2a validation set: 2001 characters, `\u0007`, `\u001b` are coded 400s before any lock or read, the note never echoed (single and multi) | `RefusedNote_Is400_BeforeAnyLockOrRead` (6) |
| 2000 and 1 characters accepted untruncated | `NoteUpToTheLimit_IsAccepted_Untruncated` (4) |
| The reason is checked before the note | `MissingReason_IsRefusedBeforeTheNote` (2; a control, passes with or without the note check) |
| Repeat, same note: nothing recorded twice | `Repeat_WithTheSameNote_RecordsNothingTwice` (3) |
| Repeat, another or dropped note: 503 incomplete at `record`, one record, first note stays, line rewritten identically | `Repeat_WithAnotherNote_IsRefusedAsOtherContent` (4) |
| No log line carries the note (success, refusal, repeat, edited repeat, a failed totals step, a Disallow) | `NoLogLine_CarriesTheNote` |
| HTTP: a refused note is a coded 400 on both routes before any read or write, never echoed | contract `Decide_RefusedNote_Returns400_WithItsCode_BeforeAnyRead` (6) |
| HTTP: a valid note is accepted (not a 400; the request reaches the invoice read, 409 not in review) | contract `Decide_ValidNote_IsAccepted_AndReachesTheRead` (4; a control on its own) |
| HTTP: the bodies bind a JSON `note` and none binds null | contract `RequestBodies_BindTheNote` |
| HTTP must-not-fire: Accept all Clean ignores a note in its body (200, not a 400) | contract `AcceptClean_IgnoresANote` |
| Catalog | `DecisionActionCatalogTests.InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter` (+3 rows) |

### Fail-before evidence

On base the new tests do not compile (`LineDecisionCommand` and the request records have no `Note`), so the evidence is seeds: the source mutated, the note tests run, the source restored from a saved copy (byte-identical, checked).

| Seed | Failed |
|---|---|
| 1. The note key is written with a null value (the note is never stored) | 20 of 43 (the note, multi-line, limit, repeat and approval-control tests) |
| 2. `Values["note"]` is always written, even null (a null-valued key) | 10 of 43: the 8 `NoNote_HashesAsBefore` cases and the 2 accept-clean / approval bulk checks |
| 3. The note check removed from `ShapeError` | 12: the 6 service refusal cases and the 6 HTTP 400 cases (the service and HTTP refusal tests are the fail-before for the validation set) |
| 4. `AcceptCleanAsync` and `AcceptCleanHeldAsync` pass a note | 2 of 43: `AcceptClean_Records_CarryNoNote` and the approval bulk test |
| 5. The line update also writes `sprk_decisionnote` | 3 of 43 (`NoLineDecision_WritesAnInvoiceNoteColumn`), and the existing ArchTest `InvoiceDecisionNoteWritersGuardTests` fails (1 of 15 in the guard classes), naming `InvoiceLineDecisionService` |
| 6. The catalog restored to base | 3 of 170 (`accept-line`, `reduce-line`, `disallow-line`) |

Controls (pass with and without the change, by design): `MissingReason_IsRefusedBeforeTheNote`, `Decide_ValidNote_IsAccepted_AndReachesTheRead` (on base the unknown `note` property is ignored), the approval-record and column asserts inside the approval test, and the `Corrections` count asserts inside the Accept cases.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` (also `--no-incremental`) | Succeeded, 0 warnings, 0 errors |
| Narrow suites (`InvoiceLineDecision`, `LineDecision`, `InvoiceApproval`, `DecisionActionCatalog`, `DecisionNote`, `InvoiceCorrection`, `InvoiceLineReview`, `InvoiceRejectReturn`, `SubmissionOverride`) | 1649 passed, 0 failed |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice\|FullyQualifiedName~Ebilling"` | 17104 passed, 0 failed, 12 skipped (17116) |
| `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~Invoice\|FullyQualifiedName~Decision\|FullyQualifiedName~Catalog"` | 151 passed, 0 failed |
| ArchTests, route, access, findings, money, ledger and endpoint guards | 408 passed, 0 failed |
| Publish size (`dotnet publish -c Release`; base: the same tree with the three changed source files checked out at 545ba823dc) | `Sprk.Bff.Api.dll` +2,560 B, `Sprk.Bff.Api.pdb` +256 B: **+2,816 B**. Whole output: branch 140,479,065 B, base 140,763,777 B; the 284,712 B difference in the other direction is one file, `System.Security.Cryptography.Pkcs.dll` (287,528 B), present only in the second publish's output and unrelated to this change (no `.csproj`, props or reference changed) |
| New package, new HIGH CVE | None. No `.csproj` or props file changed |

The whole ArchTests and BFF test projects were not run in one process (machine limit); the filtered suites above were. The publish delta is against the branch base, not a fresh master build, because the work branch carries unmerged code.

## Component justification (CLAUDE.md section 11)

The new surface is an optional `Note` on two request records and on the command, one check call in `ShapeError`, three optional catalog parameters, and no new type, interface, endpoint, registration, column or package. (1) Existing: `InvoiceCorrectionNote.Check` and `DecisionRefusalOf`, the shared 400 codes and `NoteParameter()` from 095f-2a; the line decision service's own record path. (2) Extension: all are reused as they are. (3) Cost of doing nothing: a reviewer's reason-for-this-line beyond the required reason (EB-D-236) has nowhere to be stored, so the internal "why" of individual line decisions is lost.

## Known limits

(a) **A repeat must resend the same note** (rule 5). A different note under the same decision id is refused as other content (503, `failedStep` `record`), after the lines were rewritten with the same values. The client (095f-2f, InvoiceReview) should reuse the note it first sent when it retries, and use a new decision id for a different note. This is the existing refusal for any changed reason or amount; the note joins it because it is inside the hash (design Q8, answered).

(b) **One note for all lines of a multi-line call** (design K-2).

(c) **Client not changed.** `invoiceReviewApi.ts` `decideLine` / `decideLines` do not yet send `note`, and there is no Note box; both are the client task (design §7). The server accepts and ignores nothing else: a body without a note behaves exactly as before.

(d) **Writer logger not captured.** The log test covers the line service's and the recorder's loggers; the decision-record writer's own logger (inside the shared test harness) logs ids and codes only by its source, but is not asserted here.

(e) **The HTTP "accepted" test stops at a 409.** `Decide_ValidNote_IsAccepted_AndReachesTheRead` stops at 409 (invoice not In Review) because the contract host's totals service is a stub; the record content is proven at the service level, and the JSON binding by `RequestBodies_BindTheNote`.

## Process note

One command in this session ran `python` by mistake (a bare `python -` on Windows, which hit the Store stub). It was stopped within seconds and a process check found no leaked python process. No python was used for any work.
