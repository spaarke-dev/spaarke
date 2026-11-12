# Task 045c notes: display columns, reply type, number

Branch `task/fm-045c`. Line numbers are at the commit the file was written on.

## Done

| Item | Where | Test |
|---|---|---|
| Reply or forward copies the pair's type (as a `sprk_recordtype_ref` reference) and the number; the type only beside the id it describes | `CommunicationService.CopyRegardingFromSourceAsync` | `ReplyRegardingCopyTests` |
| An explicit association to another record drops the copied type, number, name and url | `CommunicationService.MapAssociationFieldsAsync` | `ReplyRegardingCopyTests` |
| Office to-do writes the record url | `OfficeService.CreateTodoAsync` | `OfficeTodoRegardingContractTests` |
| Client clear nulls `sprk_regardingrecordnumber` | `TodoRegardingUpdateBuilder.buildTodoRegardingClear` | `TodoRegardingUpdateBuilder.test.ts` |
| Builder takes an optional number (last parameter); writes the column only when non-blank | `TodoRegardingBuilder.ApplyResolverFieldsAsync` and `ApplyResolverPairAsync` | `RegardingRecordNumberTests` |
| `InquiryReplyTodoCreator` passes the matter's number from the matter read it already makes (`sprk_matternumber` added to that read's columns) | `InquiryReplyTodoCreator.CreateIfNeededAsync` | `RegardingRecordNumberTests` |

## Review round 3: the number on a re-regard of a saved record

| Item | Where | Test |
|---|---|---|
| A re-regard of a saved host whose new target has no number nulls `sprk_regardingrecordnumber` (gated to hosts that carry `sprk_regardingrecordtype`, since writing a missing column is a 400) | shared `nullRecordNumberWhenTargetHasNone` (`PolymorphicResolverService.ts`), called by `ResolverWriteHandler.applyRegardingSelection` (saved-host path only) | `PolymorphicResolverService.test.ts`, `ResolverWriteHandler.test.ts` |
| Same for the Communication Connections primary | `ConnectionsWriteHandler.applyRegardingSelection` | `ConnectionsWriteHandler.test.ts` |
| Create-mode form: picking a target with no number clears the number an earlier auto-detect staged | `RegardingResolverApp.handlePickerSelect` | `RegardingResolverApp.test.tsx` |

- **Filing route:** `PATCH /api/communications/{id}/filing` (`CommunicationEndpoints.cs:133`) runs `ChildRecordEndpoints.UpdateAsync(filingOnly: true)`, which writes the `sprk_regarding...` columns it is sent. `OwnedChildWrite.cs:474-475` clears the display columns (name, number, url) only when a typed lookup is cleared with no replacement; it does not derive or clear the number when a new pair is sent. So the client defect was live and is fixed on the client. The server is not the single owner today: deriving display columns on a move (045 Q7) belongs to the Q8/045d work, which already needs `OwnedChildWrite.cs` and `ChildRecordEndpoints.cs`, files this task may not touch.
- **`buildTodoRegardingUpdate` null-number fix** (`TodoRegardingUpdateBuilder.ts`, round 2): it has no production caller (the live update paths are the two handlers above). It is harmless and left in place.

## Not changed, on purpose

- **Office to-do number (W11):** the request contract has no number field and the adapter reads no record. The number stays unset. No field was added to the Office request.
- **W13 (thread inherit) and W14 (association):** no number column to copy. The `sprk_communicationthread` table has no `sprk_regardingrecordnumber`, and `CommunicationAssociation` carries only type, id, name and url. No change.
- **Other builder callers** (`TodoGenerationService`, signal and decision-record writers, `TaskActionCore`) do not hold the number and pass none.

## Q8 (a): invoice-family re-derivation on a generic write. Deferred to 045d

Decision (coordinator, 2026-11-08): not in 045c. `RecordTableRules`, `RecordCreationService` and `OwnedChildWrite` are untouched. 045d copies the proposal below after 045a merges; 045b's update planner carries the update half.

### Why it cannot be done in the pre-write rule as it stands

- `PreWriteCheck` returns only `PreWriteRefusal?` (`RecordTableRules.cs:1033`), and a rule is `PreWriteRule(NamedRule, PreWriteCheck)` (`:1041`).
- `InvoiceFamilyRule` (`:93-98`) asks `OwnedChildWrite.CheckInvoiceFamilyAsync` and maps the answer to a refusal. It cannot add columns.
- Create: step 5½ runs `RunPreWriteRulesAsync(table, rule, user, item, noun, ct)` (`RecordCreationService.cs:705-713`, method `:1710-1744`) and returns only a refusal; the `item` written afterwards is the one before the rules ran.
- Update: `OwnedChildWrite.CheckInvoiceFamilyAsync` (`:966`, called at `:784`) is where an update asks the same question; its callers are the update handlers.

### Proposed shape

1. Change the delegate to return a result:
   `PreWriteCheck: Task<PreWriteResult>` where `PreWriteResult` is either a `PreWriteRefusal` or `AdditionalColumns` (a `Dictionary<string, object>` of column to value). A rule that adds nothing returns the empty result. Keep the factories (`Refused`, `Failed`, `NotFound`, `Undecided`) on the refusal, so the refusal contract (NFR-07) is byte for byte unchanged.
2. `InvoiceFamilyRule` builds its extra columns from the decision it already makes:
   - `InvoiceFamilyCoreRecordGuard.Check` decides the core record (`InvoiceFamilyCoreRecordGuard.cs`, `Check`).
   - On admit, with a core lookup set (create) or changed (update), call `InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(decided, records, recordTypes, ct)` (`:731-794`) and return `decided.ResolverFields.ToFields()` (`:909-916`: type reference, id lower-case "D", name, url, number; all five whole, or none).
   - A derive failure (`ReadFailedCode`, `ResolverFieldsIncompleteCode`, `RecordNotFoundCode`) must map through the existing `InvoiceFamilyPreWrite` mapping (`RecordTableRules.cs:110-121`) or, to keep NFR-07, be treated as "add nothing" and left to the existing outcome. Decide this in 045d with UAC-r2 and state it in the test list.
3. `RecordCreationService.RunPreWriteRulesAsync` returns the merged additional columns with a null refusal. Step 5½ applies them to `item` (the same local, as step 5 does at `:696`) before step 6. Columns already set by the caller are overwritten, because the pair must describe the core lookup the row names.
4. Update half (045b's planner): when the body sets or changes the family row's own core lookup, run the same derive and add the five columns to the PATCH body. The reader is the one the path already has (the caller's client; the app-only client on the playbook paths).
5. Tests (KEEP paths): create of each family table with a core lookup gets all five fields; a derive failure leaves today's outcome; an update that moves the core lookup rewrites all five; an update that does not touch the core lookup writes none; a refusal suite with unchanged assertions.

### Evidence that the reads are permitted

`DeriveResolverFieldsAsync` reads the regarded record with `IGenericEntityService` (app-only). The dedicated writers (`InvoiceSubmissionPersister.cs:447`, `InvoiceSubmissionEndpoints.cs:542`) do the same after the caller's access to the record was checked. In the generic path that check is step 5's AppendTo on every record the row names, so the derive runs only after it. 045d must keep that order.
