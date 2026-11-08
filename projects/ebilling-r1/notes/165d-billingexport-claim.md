# Task 165d - the AP export claim (`sprk_invoice.sprk_billingexport`) is writer-only (EB-D-207)

Branch `task/ebilling-165d` (from `work/ebilling-r1` at `f95fbace4f`). Gate: unified-access-control-r2 approved the one hook in `RefuseAppOnlyWrite` on 2026-11-07, with conditions U1-U5 (task POML).

## 1. What changed

| File | Change |
|---|---|
| `Services/RecordCreation/RecordTableRules.cs` | New `RecordTableRule.WriterOnlyColumns` (next to `CreatedOnlyByItsWriter`, the table-level writer-only rule). `sprk_invoice` declares `{ ApExportService.BatchLookup }`, and the same column joins its `MappingProtectedColumns`. New `RecordTableRules.RefuseWriterOnlyColumns(table, keys)` beside `RefuseWriterOnlyTableUpdate`, and the reason code `ChangedOnlyByItsWriterColumnReasonCode` = `record_update.changed_only_by_its_writer.column`. |
| `Services/Dataverse/ParentLineage.cs` (uac-r2's `InheritedAccessPermission`) | ONE hunk in `RefuseAppOnlyWrite`: `if (RecordTableRules.RefuseWriterOnlyColumns(trimmed, columns) is { } ...) return ...;`, placed after the access-column check and before the owner / creator checks, OUTSIDE the `appOnly &&` gate so every `WriteIdentity` is asked (U1). Plus one sentence in the precedence paragraph. Nothing else in the file moved. The claim is NOT in `AccessColumns` (EB-D-207: a workflow claim decides no access). |
| `Services/Finance/ApExportService.cs` | Remarks only: the "only writer" statement cites EB-D-207 and the two pins. No behaviour change. |

Wording is neutral: "`sprk_billingexport` of a sprk_invoice is changed only by Spaarke itself and can't be changed by this write; nothing was written. (record_update.changed_only_by_its_writer.column)".

**The marker (U2).** The refusal code extends the existing `record_update.changed_only_by_its_writer` code and the message carries it, so a gate query that counts the marker as a substring (or a prefix) counts this refusal, and the message names the column. A query that matches the EXACT string `record_update.changed_only_by_its_writer` would not count the new code: tell uac-r2 to count with `contains` / `startswith`. The refusal logs through each caller's existing refusal handling (the code and the reason travel in the exception or the skipped-rule result); `RefuseAppOnlyWrite` is a pure function and logs nothing itself, as before.

## 2. Writer inventory (U3 / escalation trigger)

Every call site of the table-level check `RefuseWriterOnlyTableUpdate` (the generic update paths), with whether it also asks `RefuseAppOnlyWrite` (so the column rule reaches it with no edit of its own). Line numbers are on this branch; the POML's were stale.

| Site | Identity | Asks `RefuseAppOnlyWrite` | Result |
|---|---|---|---|
| `UpdateRecordActionCore.UpdateAsync` (Update Record node, the action seam without an impersonated user) `:208` | app-only (also the node of a person's run, #2257) | yes | **covered** |
| `DataverseUpdateHandler.UpdateAsync` (a playbook's output mapping) `:97` | app-only | yes | **covered** |
| `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` (the push, per rule) `:1156` | caller | yes (`WriteIdentity.Caller`) | **covered** (U1) |
| `RecordCreationService` create `:401`, create-time mapping `.Mapping.cs:235`, Repair `.Repair.cs:188` and per rule `:450` | app-only | yes | **covered** |
| `FieldMappingEndpoints.ValidatePushRequest` `:699` | - | no | exempt: the request check carries the table only, no columns; the per-rule ask is in the push |
| `DataverseUpdateRecordHandler.ValidateChat` / `ExecuteChatAsync` (chat update tool) `:193` / `:239` | the person (user token), after they confirm | no | **exempt, owner / lead question (A)** |
| `ChildRecordEndpoints.UpdateAsync` (browser child-record update) `:674` | the caller (user client) | no | **exempt, owner / lead question (A)** |
| `OwnedChildWrite.RefileAsync` (re-file core, the caller's own PATCH) `:777` | the caller (user client) | no | **exempt, owner / lead question (A)** |
| `TopicRegistryWorkProductPersister.PersistAsync` `:191` | the person (user token) | no | **exempt, owner / lead question (A)** |
| `DataverseDeleteRecordHandler` (`RefuseWriterOnlyTableDelete`) | - | no | not an update; a delete carries no payload columns |

**(A) The escalation trigger fired, and I did not add a per-writer check.** Five generic update members that can reach `sprk_invoice` do NOT go through `RefuseAppOnlyWrite`: the chat update tool (2 members), the browser child-record update, the re-file core and the work-product persister. They write AS the person (the user-token client), so their own Dataverse privileges decide the write; `RefuseAppOnlyWrite` is the refusal of payloads built by configuration or a model, and `RefuseAppOnlyWriteCallerIdentityGuardTests` allows its `Caller` identity to the field-mapping push alone. Adding the ask there would also apply the document, owner and creator refusals to a person's own edit (a behaviour change beyond this task). The same holds for the action seam's apply of a confirmed proposal, which skips the refusal by design. They are listed in `BillingExportClaimWriterOnlyGuardTests.Exempt` with this reason, so the pin is honest rather than vacuous, and a member listed there that starts asking the refusal fails the pin (the list can only shrink). **Decision needed from the lead / uac-r2:** accept these as the person's own edit (closed by field security, U5 below), or ask for a Caller-identity ask at those sites.

## 3. Known limit (U5) - an owner question, not built here

`sprk_billingexport` on `sprk_invoice` is **not field-secured**. A user with Write on an invoice can still set or clear the column natively (the form, the Web API, or any of the person-identity writers above); the BFF refusal closes only the BFF's generic writers. Field security on the column (the BFF application users the only writers, default teams read; the `sprk_createdbyperson` pattern; no plugin) would close the native write. It is a schema step that needs the owner's go, so it goes on the dev-deploy checklist and is **not** built here. Question for the owner: should the column be field-secured like the seven invoice columns of task 166?

Related, unchanged: Repair on an invoice is skipped whole by the invoice-family guard on this build (`RecordRepairModeTests.AnInvoiceFamilyRow_IsNotWritten_UntilTheInvoiceGuardIsOnThisBuild`); the per-rule ask still reports the claim as refused, which the new test pins.

## 4. Tests

| Test | Pins |
|---|---|
| `tests/integration/data-mutation/Ebilling/BillingExportClaimWriterOnlyTests.cs` (33) | The refusal for the AppOnly AND Caller identities in six key forms (plain, case, padded, `@odata.bind`, `_col_value`), with the marker family and the column named; must-not-fire controls (an ordinary invoice column, a neighbouring name, the same name on a line, a submission, the batch table, a matter, a null table); the registry declaration (one table, mapping-protected); the Update Record node (set, cleared, lookup) and the output mapping (set, cleared, entity reference, string) are refused with nothing written, and still write an ordinary column; the field-mapping push (caller) skips the rule and sends the rest; Repair reports the code and writes nothing naming the claim; **AP export's own claim and release run (ApExportWorld) next to the refusal** (U4). |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.BillingExportClaimProtected.cs` (3) | A post-mapping default aimed at the claim is an argument error (the protected set alone); the protection is on the invoice only; a create-time rule aimed at the claim is skipped with the existing warning while an unprotected target of the same profile maps. |
| `tests/Spaarke.ArchTests/BillingExportClaimWriterOnlyGuardTests.cs` (7) | U3: every member that asks the table-level update check also asks `RefuseAppOnlyWrite`, or is in `Exempt` with a reason (stale entries fail); the column refusal is asked from `RefuseAppOnlyWrite` alone; it reads the registry set. U4: `ApExportService` never asks `RefuseAppOnlyWrite` / `RefuseWriterOnlyColumns` and writes through `IGenericEntityService`; only the reviewed types name the column string (the writer set is `{ ApExportService }`; the others name the batch table or another table's lookup, reviewed in the test). Must-fire and must-not-fire controls for both detectors. |

ApExport claim and release tests (`ApExportBatchTests` and the rest of `ApExport*`) pass **unmodified**.

## 5. Seed

Removed the check (`if (false && ... RefuseWriterOnlyColumns ...)`): 28 of the 33 tests in `BillingExportClaimWriterOnlyTests` fail, including the Update Record node, output mapping, push, Repair and both AP-export-next-to-refusal tests; the check restored, all pass. The mapping protection is pinned independently by the seam tests (`ProtectedFromMapping...`, `PostMappingDefault...`), which fail if the `MappingProtectedColumns` entry is removed.

## 6. Gates

- BFF build (`dotnet build src/server/api/Sprk.Bff.Api/`): 0 warnings, 0 errors.
- Filtered unit suite (Finance, ApExport, RecordCreation, FieldMapping, UpdateRecord, InheritedAccess, DataverseUpdate, ChildRecord, WorkProduct, plus AccessTable, RecordTableRules, RecordRepair, BillingExport, WriterOnly, OutputMapping, ActionSeam, OwnerCreator, Document*, ContactAccess, CoreRecordPair): **10207 passed, 0 failed, 4 skipped** (10211). One existing exact-set pin needed the new entry: `RecordTableRulesTests.MappingProtectedColumns_TheInvoiceFamilysCoreLookups_AreNeverMapped` (expected set gains `sprk_billingexport`).
- Full ArchTests: **2129 passed, 0 failed** (includes the 7 new).
- New tests: 33 + 3 + 7 = 43, all pass. `dotnet format --verify-no-changes` on the changed .cs files: clean.
- **Publish delta (root section 10 bullet 4)**: Release publish of this task's source vs the base `f95fbace4f` source, same machine and settings: `Sprk.Bff.Api.dll` 25,744,384 -> 25,745,408 bytes (+1,024); whole publish folder 137,744,221 -> 137,745,453 bytes (+1,232). The baseline is the branch base, not a fresh master build, so it measures this task's delta only; the folder total is not comparable with the 55 / 60 MB thresholds (it includes symbols and other files). No package added, no new CVE surface.
- The drift check (`scripts/check-task-status-drift.ps1 -Project ebilling-r1`) does not list 165d; it lists other tasks already behind before this task (e.g. 161, 166): out of scope, not touched.

## 7. Dev-deploy checklist additions

- Owner decision: field-secure `sprk_invoice.sprk_billingexport` (U5)? Not built.
- uac-r2: gate query for the `changed_only_by_its_writer` marker should match by substring / prefix (section 1).

rework: none task 165d
