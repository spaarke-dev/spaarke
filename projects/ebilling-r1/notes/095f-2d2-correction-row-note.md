# 095f-2d2: correct and confirm carry a note on the correction row (`sprk_invoicecorrection.sprk_note`)

> **Task:** `tasks/095f-2d2-correction-row-note.poml` (FULL) · **Design:** `095f-part2-decision-notes-design.md` §1.1 and §5.2 option B, as amended by §15 · **Decision:** EB-D-242 · **Base:** `task/ebilling-095f-2d2` from 251f9602ef · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## What was built

| Item | Where |
|---|---|
| `CorrectionRequest(Field, Value, Confirm, Note = null)`. Both routes (line and header) pass `body.Note` to the command | `Api/Finance/InvoiceCorrectionEndpoints.cs` |
| `CorrectionCommand(..., ReviewerSystemUserId, Note = null)`. Existing positional callers compile unchanged | `Services/Finance/InvoiceCorrectionService.cs` |
| `Validate` checks the note with `InvoiceCorrectionNote.Check` as its LAST rule, so before any read and after the request's own rules. A refusal is `Bad(code, detail)`, a 400 with `ebilling.note_too_long` or `ebilling.note_invalid_characters` from `DecisionRefusalOf` (the same codes as reject, return and approve; the detail never echoes the note) | same file |
| `CorrectAsync` replaces the note with the checked, trimmed value (`null` when blank) once, so every later step sees one value | same file |
| `BuildRow` adds `sprk_note` only when the note is non-blank, in the same `UpsertRequest` as the row and in the same transaction as the value update. The row's id is built before it from the field, version and final text and does not use the note | same file |
| `InvoiceCorrectionNote.CorrectionRowColumn = "sprk_note"` (the one place the column name lives in the BFF) | `Services/Finance/InvoiceCorrectionNote.cs` |
| Route descriptions and the endpoint remarks name the note and the two new 400 codes | `InvoiceCorrectionEndpoints.cs` |
| ArchTest `ExternalPlane_NeverNamesTheCorrectionRowNote` plus controls `CorrectionRowNoteControls`; the stale comment ("not yet sprk_invoicecorrection.sprk_note") restated | `tests/Spaarke.ArchTests/InvoiceNoteColumnsGuardTests.cs` |

## Rules as built

1. **One validation.** `InvoiceCorrectionNote.Check`: 2000 characters accepted, 2001 refused; a control character other than `\n`, `\r` and `\t` (for example `\u0007`, `\u001b`, NUL, C1 `\u0085`) refused; `\n` and `\t` accepted and kept; trimmed; never truncated or stripped. A refusal is a 400 before any read or write (the strict Dataverse mock sees no call).
2. **Order.** The request's own rules (invoice id, field, value or confirm) come first, so a request with a bad field and a bad note is the field's refusal. This leaves every pre-existing refusal exactly as before.
3. **No note, no column.** Null, `""` and whitespace-only write no `sprk_note` key. Never a null or empty column.
4. **The note is not part of the id.** The id is still `IdOf("ebilling-correction-edit-{scope}-{field}-{version}-{finalText}")`, pinned in a test against the key itself. A repeat with an edited, removed or added note finds the same row (`alreadyApplied`), writes no transaction and leaves the note as first written. This is the EB-D-242 position; the note is the first writer's.
5. **Labels unchanged (D-64).** Corrected and Confirmed individually stay labels. "Accepted in bulk" and "Unreviewed" rows (written by the approve and the line decisions, `InvoiceLineDecisionService`) never carry a note: that code was not touched, and a test shows an approve with a note writes no `sprk_note` on any correction row.
6. **No Decision Record, no writer change.** Only `sprk_invoicecorrection` rows are written, as before.
7. **Logs.** No log line contains the note (success, refusal, a failed write). The service's existing log lines carry ids, the field name and the outcome only.
8. **Firm visibility.** `sprk_note` reaches no reader:
   - The History pane and calibration report read through `CorrectionColumns` (an explicit list without `sprk_note`) into `CorrectionDto`, which has no note member. Tested by reflection on both.
   - `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`, the external DI module and the external SPA source do not name `sprk_note`. `ExternalInvoiceVisibility` and `ExternalTeamShareSynchronizer` only list `sprk_invoicecorrection` as a table whose rows are hidden or shared with their invoice; neither carries a column list for it. The new ArchTest keeps it that way.

## Tests

| Acceptance criterion | Test | Fail before |
|---|---|---|
| A note is written on every row the call creates, in the same transaction | `InvoiceCorrectionNoteTests.Note_IsOnTheRowTheCallCreated` (line correct, line confirm, header correct, header confirm); HTTP: `InvoiceCorrectionEndpointsAuthorizationContractTests.Note_IsStoredOnTheRow` (both routes) | 4 + 2 failed |
| No note writes no `sprk_note` (must-not-fire) | `NoNote_WritesNoColumn` (null, `""`, spaces, mixed whitespace) | passed (control) |
| Row ids equal the pre-change ids (must-not-fire on an id change) | `Note_IsNotPartOfTheRowId` (4 shapes), `RowId_IsThePreChangeDerivedId` (pinned from the key) | passed (control) |
| A repeat with another note keeps the first note | `ARepeat_WithAnotherNote_KeepsTheFirstNote` (edited, removed), `ARepeat_WithANoteAfterNone_DoesNotAddOne` | 2 failed (the first); the other passed |
| Labels unchanged | `Labels_AreUnchangedByANote` | passed (control) |
| 2000 accepted whole; `\n` `\t` kept | `NoteUpToTheLimit_IsAccepted_AndNeverTruncated` (4 shapes); HTTP `NoteAtTheLimit_WithNewlineAndTab_Is200` (both routes) | 4 + 2 failed |
| 2001 refused, before any read | `NoteTooLong_Is400_BeforeAnyRead` (4 shapes); HTTP `RefusedNote_Is400_AndReadsAndWritesNothing` | 4 + 2 failed |
| `\u0007`, `\u001b`, NUL, `\u0085` refused, before any read | `ControlCharacter_Is400_BeforeAnyRead`; HTTP (same theory) | 4 + 2 failed |
| Detail never echoes the note; the request's own rules go first; a refusal leaves no state | `Refusal_NeverEchoesTheNote`, `TheFieldRuleIsCheckedBeforeTheNote`, `ARefusedNote_LeavesNoState` | 2 failed; the field-order test passed (control) |
| No log line contains the note | `NoLogLine_CarriesTheNote` (success, refusal, failed write) | passed (control) |
| The listed row and the read column set have no note | `TheListedRow_HasNoNote` | passed (control) |
| Bulk accepted rows carry no note | `BulkAcceptedRows_CarryNoNote` (an approve with a note writes seven "Accepted in bulk" rows and none carries `sprk_note`) | passed (control) |
| The firm-facing planes never name `sprk_note` | ArchTests `ExternalPlane_NeverNamesTheCorrectionRowNote`, controls `CorrectionRowNoteControls` (must fire on synthetic text and on the real external DI module with the column planted in it; must not fire on the real module, lookalikes and a comment) | passed (control) |

**Fail-before.** The records were first given the `Note` member and nothing else (the request binds and the command carries it; no check, no write), and the new tests run: **29 failed, 89 passed of 118** (the new tests plus the existing contract file). The failures are the write, the two refusals and the repeat-keeps-first tests above; the passes are the must-not-fire controls. The service and note edits were then added: **0 failed, 118 passed**.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| New and touched correction tests (`InvoiceCorrectionNoteTests` and the endpoint contract file) | 118 passed, 0 failed |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Invoice\|FullyQualifiedName~Ebilling\|FullyQualifiedName~Correction"` | 12540 passed, 0 failed, 6 skipped (12546) |
| `dotnet test tests/Spaarke.ArchTests` | 2663 passed, 0 failed (2661 before, plus the two new facts) |
| Publish size (`dotnet publish -c Release`). Base is 251f9602ef: the same tree with the three changed source files checked out at base, both published inside this worktree; the publish folders were deleted afterwards | Branch 140,461,621 B; base 140,459,941 B; delta **+1,680 B** |
| CVE (`dotnet list package --vulnerable --include-transitive`) | No vulnerable packages. No `.csproj` or props file changed |

## Known limits and notes

(a) **Deploy order.** Every correct or confirm that carries a non-blank note now writes `sprk_invoicecorrection.sprk_note`. This BFF must not reach an environment before `scripts/ebilling/Deploy-EbillingDecisionNoteColumns.ps1 -Apply` has created the column there (the same rule as 095f-2a's limit (a)); the column is on the ArchTest's pending list, so the guard stays green until the SpaarkeMaster sync. A request with no note writes no new column, so notes-free traffic is unaffected. Without the column, a correction that carries a note fails 503 on the row write; the same request repeated with the column present writes the row.
(b) **The note is write-only in this task.** Nothing reads it back: not the list route, not the History pane, not the calibration report. Showing it to an internal reviewer is a later, separate change (add the column to `CorrectionColumns` and a member to `CorrectionDto`; the firm-facing planes stay guarded).
(c) **First writer wins.** A repeat after a value write whose row exists never rewrites the note, by design (EB-D-242). A repeat after a version change is a different row id (the id carries the version read), so it is a new row with its own note.
(d) **`sprk_note` is matched by name alone** in the column guard (as its remarks already say). The new external-plane test inherits that: a whole-word `sprk_note` in any external file fails it, whichever table it meant.
(e) **No writers guard for `sprk_note`.** `InvoiceDecisionNoteWritersGuardTests` keys writers by method for the two invoice columns. A "who may write `sprk_note`" guard was not in the task and was not added; `InvoiceCorrectionNote.CorrectionRowColumn` has one use, in `InvoiceCorrectionService.BuildRow`.
(f) **The commit hook.** The repository's pre-commit hook (lint-staged) takes its own temporary backup of the index during a commit and removes it; no stash of this session's work remains.
