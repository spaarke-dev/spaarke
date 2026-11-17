# 095f-2a: the lifecycle writes the decision note; reject, return and approve take a note

> **Task:** `tasks/095f-2a-lifecycle-reject-return-approve-notes.poml` (FULL) · **Design:** `095f-part2-decision-notes-design.md` §1, §2 and §5.3, as amended by §15 (EB-D-240) · **Base:** `task/ebilling-095f-2a` from 5508b576fe · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

Every "invoice column" in this task is `sprk_invoice.sprk_decisionnote` (EB-D-240). `sprk_correctionnote` is untouched: its only writer is still the submission persister.

## What was built

| Item | Where |
|---|---|
| `TransitionAsync(invoiceId, target, decisionNote, ct)`. The three-argument member forwards `null`. The note joins the status in the one fields dictionary of the one conditional update, so a version conflict writes neither | `Services/Finance/InvoiceLifecycleService.cs` |
| `DecisionColumn`, the shared codes `ebilling.note_too_long` and `ebilling.note_invalid_characters`, and `DecisionRefusalOf` (maps a `Check` refusal to its code and detail) | `Services/Finance/InvoiceCorrectionNote.cs` |
| Reject and return: the note is checked after the reason and before any lock or read. `Values["note"]` is set only for a non-blank note. The lifecycle gets `note ?? reason` | `Services/Finance/InvoiceRejectReturnService.cs` |
| `InvoiceDecisionRequest(Reason, Note = null)` | `Api/Finance/InvoiceRejectReturnEndpoints.cs` |
| Approve: the note is checked before any lock or read. `Values["note"]` is set only for a non-blank note. The lifecycle gets the note, so no note leaves the column unchanged | `Services/Finance/InvoiceApprovalService.cs` |
| The optional `InvoiceApprovalRequest(Note)` body (nullable: no body binds `null`), plus `ProducesProblem(400)` | `Api/Finance/InvoiceApprovalEndpoints.cs` |
| `note` (TextArea, optional) declared on `reject-invoice` and `return-to-firm` | `Services/Signals/Actions/DecisionActionCatalog.cs` |
| The `InvoiceDecisionNoteWritersGuardTests` ArchTest | `tests/Spaarke.ArchTests/` |

## Rules as built

1. **One validation.** The note goes through `InvoiceCorrectionNote.Check`:
   - 2000 characters are accepted; 2001 are refused with `ebilling.note_too_long`.
   - A control character other than `\n`, `\r` and `\t` (for example `\u0007` or `\u001b`) is refused with `ebilling.note_invalid_characters`.
   - Whitespace-only counts as no note.

   The stored note is the value `Check` returns. It is never truncated or stripped by the lifecycle. A refusal is a 400 before any lock, read, transition or record.
2. **The reason is checked first.** A missing reason with a bad note is still refused as `ebilling.reason_required`.
3. **The hash is unchanged without a note.** No note, `""` and whitespace-only leave no `note` key in Values, never `note: null`. The Decision Record hash then matches the pinned golden values below.
4. **What the column holds:**

   | Decision | With a note | With no note |
   |---|---|---|
   | Reject, return | The note | The reason |
   | Approve | The note | Not written; its current value stands |
5. **K-1 adds no new refusal.** A repeat whose note differs from the first is never refused for that difference. What happens depends on when the repeat arrives:
   - **After the record.** The stored record is found and returned. The edited note is dropped, and the record and column keep the first note.
   - **Before the record.** The invoice is already in its target, so the lifecycle writes nothing and the column keeps the first note. The record is written with the repeat's note.
6. **Logs.** No log line contains note text. Tested on the lifecycle and on the reject, return and approve services.
7. **Firm visibility.** `sprk_decisionnote` was added to no firm-facing reader. `Values["note"]` lives on the Decision Record, which is not on any external allow-list.

## Golden hashes (no note; captured on base 5508b576fe before any source edit)

Fixed subject `09500000-0000-4000-8000-00000000f2a1`, confirmed by the ApprovalWorld's fixed test approver:

| Code | Reason / lines | `reviewHash` |
|---|---|---|
| `reject-invoice` | "Rate card not followed" | `1E91C99B7444B0C67B664ACBB030DAB3B34BAE56A7945B8B7A4FCDA41B48043D` |
| `return-to-firm` | "Please correct the task codes" | `832DF98E3F4C689526E02C315F79EE1790ED5EA909622490449274DE0362F198` |
| `approve-invoice` | Accepted 400/400, Reduced 600/450 | `A3AB0EB79298A7E6779A6A9AEE92B3A14769C2C7461BEDCCD2AB78E0F9679B45` |

These are pinned in `InvoiceDecisionNoteTests` (`NoNote_HashesAsBefore_AndTheColumnTakesTheReason`, `Approve_NoNote_IsUnchanged`).

## The writers guard

`InvoiceDecisionNoteWritersGuardTests` scans IL string loads.

- **Decision-note writers.** `sprk_decisionnote` may be named only by:
  - `InvoiceLifecycleService.TransitionAsync`;
  - `SubmissionOverrideService.BuildReleaseTransaction`;
  - `InvoiceTotalsService.RecalculateAsync`.

  The last two are the release transaction and the totals path the design allows. They are listed as *pending*: allowed, but not checked for staleness, because they do not write the column yet.
- **Correction-note writer.** `sprk_correctionnote` may be named only by `InvoiceSubmissionPersister.FillInvoice`.
- **Controls.** Each guard has a must-fire fixture (a planted extra decision-note writer; a planted correction-note writer on a decision path) and a must-not-fire fixture (a type that names neither column).

## Tests

| Criterion | Test |
|---|---|
| The note is in the same conditional update as the status | `InvoiceLifecycleDecisionNoteTests.Note_LandsInTheSameUpdateAsTheStatus` (approve, reject, return) |
| A version conflict writes neither | `ConcurrentChange_WritesNeitherTheStatusNorTheNote`, `KeepsChanging_GivesUp_WithNoNote` |
| The three-argument call and a null note write no note column (must-not-fire) | `ThreeArgumentCall_AndNullNote_WriteNoNoteColumn` |
| K-1 at the lifecycle | `AlreadyInTarget_WritesNoNote` |
| The note is in Values and in the column (reject, return) | `InvoiceDecisionNoteTests.Note_IsInValuesAndInTheColumn` |
| No note hashes as before; the column takes the reason | `NoNote_HashesAsBefore_AndTheColumnTakesTheReason` (null, `""`, whitespace) |
| The note is inside the hash | `Note_IsInsideTheHash` |
| The 2000-character and 1-character limits | `NoteUpToTheLimit_IsAccepted` |
| 2001 characters, `\u0007` and `\u001b` are a 400 before any lock or read | `RefusedNote_Is400_BeforeAnyLockOrRead`, `Approve_RefusedNote_Is400_BeforeAnyLockOrRead` |
| The reason is checked before the note | `MissingReason_IsRefusedBeforeTheNote` |
| K-1 (both cases) | `K1_RepeatAfterTheRecord_DropsTheEditedNote`, `K1_RepeatBeforeTheRecord_RecordsTheEditedNote_ColumnKeepsTheFirst` |
| No log line carries the note | `NoLogLine_CarriesTheNote` (lifecycle and services) |
| Approve with no note is unchanged | `Approve_NoNote_IsUnchanged` (null, `""`, whitespace; golden; column unchanged) |
| An approve note is recorded | `Approve_Note_IsInValuesAndInTheColumn` |
| The bulk accept carries no note (must-not-fire) | `Approve_BulkAccept_CarriesNoNote` |
| HTTP: reject and return | `InvoiceRejectReturnEndpointsAuthorizationContractTests.Note_IsRecorded`, `RefusedNote_Is400_AndWritesNothing` |
| HTTP: approve with no body, `{}`, `{"note":null}` or whitespace behaves as before | `InvoiceApprovalEndpointsAuthorizationContractTests.Approve_WithoutANote_BehavesAsBefore` |
| HTTP: approve with a note, and a refused note | `Approve_WithANote_RecordsIt`, `Approve_RefusedNote_Is400_AndWritesNothing` |
| Catalog | `DecisionActionCatalogTests.InvoiceDecisions_DeclareNoteAsAnOptionalTextAreaParameter` |
| Writers guard | `InvoiceDecisionNoteWritersGuardTests` (4 facts) |

**Seeding.** Three seeds were applied together, the note tests run, and the files restored. The run gave 10 failed of 42:

- Dropping the lifecycle's note line failed `Note_LandsInTheSameUpdateAsTheStatus` ×3.
- Passing `note` instead of `note ?? reason` to the lifecycle failed `NoNote_HashesAsBefore_AndTheColumnTakesTheReason` ×5.
- Never adding `Values["note"]` on approve failed `Approve_Note_IsInValuesAndInTheColumn` and `Approve_BulkAccept_CarriesNoNote`.

The goldens were captured on base before any source edit.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Finance\|FullyQualifiedName~Signals\|FullyQualifiedName~Invoice"` | 16635 passed, 0 failed, 12 skipped (16647) |
| The new note tests, contract and catalog tests (same project, filtered by class) | 294 passed, 0 failed |
| `dotnet test tests/Spaarke.ArchTests` | 2660 passed, 0 failed |
| Publish size (`dotnet publish -c Release`). Base is 5508b576fe: the same tree with the seven changed source files checked out at base | Branch 140,440,261 B; base 140,436,149 B; delta **+4,112 B** |
| CVE (`dotnet list package --vulnerable --include-transitive`) | No vulnerable packages. No `.csproj` or props file changed |

## Deviations and known limits

1. **Approve K-1 gap (not in design §5.3).** In one window, a repeated approve with a different note gets 503 instead of a result:
   - **The window.** The first approve fails after the writer's record exists but before the portion link (at "signals" or "link"). The claimant's repeat then calls `RecordAsync` again, because there is no link to find the stored record by.
   - **Outcome with the first note, or no note in both.** The writer finds its record (same key, same hash), and the repeat completes as before this task.
   - **Outcome with an edited or removed note.** The hash differs, the writer refuses, and the call answers 503 Incomplete at "record". It completes once repeated with the first note.

   This is no new refusal of a valid decision, and nothing is lost. It is still a user-visible snag. The fix is for the approve completion to look up the stored record by key first, as reject and return do (`FindStoredResolvedSetAsync`). That is a change to the approve completion path, so it is left for the coordinator.
2. **Approve body binding.** Approve now binds an optional JSON body. No body, `{}` and `{"note":null}` behave exactly as before (tested). Two cases now fail where the body used to be ignored:
   - A malformed JSON body gets 400.
   - A non-JSON content type with a body gets 415.

   No client sends either today.
3. **Pending decision-note writers in the guard.** The release transaction and the totals path are allowed by the design but do not name the column yet. The guard lists them as allowed without a staleness check, so the later tasks that wire them need no guard edit.
4. **`reject-invoice` is `ExternallyVisible`.** The firm reads only `sprk_reason`, never the note; this task does not change that. The catalog note parameter is internal input to the record.
