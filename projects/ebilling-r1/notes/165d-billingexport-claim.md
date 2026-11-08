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
| `RecordCreationService` create-time mapping `.Mapping.cs:235`, Repair `.Repair.cs:188` and per rule `:450` | app-only | yes | **covered** |
| `RecordCreationService.DecideAsync` system-principal create (step 2), `:401` | app-only | yes, but its reaction was filtered to the document code | **was NOT covered before the fix round** (the call was there, the `when` clause ignored the claim's code); now asks `RefuseWriterOnlyColumns` directly |
| `RecordCreationService.CallerBlockedColumnRefusal` (caller create, and the chat create tool's pre-suspend check) | the caller | no | **was NOT covered before the fix round**; now asks `RefuseWriterOnlyColumns` (fix round item 1) |
| `FieldMappingEndpoints.ValidatePushRequest` `:699` | - | no | exempt: the request check carries the table only, no columns; the per-rule ask is in the push |
| `DataverseUpdateRecordHandler.ValidateChat` / `ExecuteChatAsync` (chat update tool) | the person (user token), after they confirm | column check only | **covered, uac-r2 U1 option A** (was exempt) |
| `ChildRecordEndpoints.UpdateAsync` (browser child-record update) | the caller (user client) | column check only | **covered, option A** (was exempt) |
| `OwnedChildWrite.RefileAsync` (re-file core, the caller's own PATCH) | the caller (user client) | column check only | **covered, option A** (was exempt) |
| `TopicRegistryWorkProductPersister.PersistAsync` | the person (user token); the target column comes from a registry row | column check only | **covered** (was exempt; configuration-built payload rule #2257, "fix the registry row") |
| `UpdateRecordActionCore` confirmed-proposal branch (the action seam's apply of a person's confirmed proposal, written AS them) | the person | skipped by design | **not covered, by design**: the person confirmed the exact payload; their Dataverse privileges decide it (field security, U5, closes it) |
| `DataverseDeleteRecordHandler` (`RefuseWriterOnlyTableDelete`) | - | no | not an update; a delete carries no payload columns |

**(A) SUPERSEDED by the fix round (section 8): uac-r2 chose option A for U1, and the person-token writers now ask the column check.** What follows is the original build-time statement.

**(A) The escalation trigger fired, and I did not add a per-writer check.** Five generic update members that can reach `sprk_invoice` do NOT go through `RefuseAppOnlyWrite`: the chat update tool (2 members), the browser child-record update, the re-file core and the work-product persister. They write AS the person (the user-token client), so their own Dataverse privileges decide the write; `RefuseAppOnlyWrite` is the refusal of payloads built by configuration or a model, and `RefuseAppOnlyWriteCallerIdentityGuardTests` allows its `Caller` identity to the field-mapping push alone. Adding the ask there would also apply the document, owner and creator refusals to a person's own edit (a behaviour change beyond this task). The same holds for the action seam's apply of a confirmed proposal, which skips the refusal by design. They are listed in `BillingExportClaimWriterOnlyGuardTests.Exempt` with this reason, so the pin is honest rather than vacuous, and a member listed there that starts asking the refusal fails the pin (the list can only shrink). **Decision needed from the lead / uac-r2:** accept these as the person's own edit (closed by field security, U5 below), or ask for a Caller-identity ask at those sites.

## 3. Known limit (U5) - an owner question, not built here

`sprk_billingexport` on `sprk_invoice` is **not field-secured**. A user with Write on an invoice can still set or clear the column natively (the form, the Web API, or any of the person-identity writers above); the BFF refusal closes only the BFF's generic writers. Field security on the column (the BFF application users the only writers, default teams read; the `sprk_createdbyperson` pattern; no plugin) would close the native write. It is a schema step that needs the owner's go, so it goes on the dev-deploy checklist and is **not** built here. Question for the owner: should the column be field-secured like the seven invoice columns of task 166?

Related, unchanged: Repair on an invoice is skipped whole by the invoice-family guard on this build (`RecordRepairModeTests.AnInvoiceFamilyRow_IsNotWritten_UntilTheInvoiceGuardIsOnThisBuild`); the per-rule ask still reports the claim as refused, which the new test pins.

## 4. Tests

| Test | Pins |
|---|---|
| `tests/integration/data-mutation/Ebilling/BillingExportClaimWriterOnlyTests.cs` (33) | The refusal for the AppOnly AND Caller identities in six key forms (plain, case, padded, `@odata.bind`, `_col_value`), with the marker family and the column named; must-not-fire controls (an ordinary invoice column, a neighbouring name, the same name on a line, a submission, the batch table, a matter, a null table); the registry declaration (one table, mapping-protected); the Update Record node (set, cleared, lookup) and the output mapping (set, cleared, entity reference, string) are refused with nothing written, and still write an ordinary column; the field-mapping push (caller) skips the rule and sends the rest; Repair reports the code and writes nothing naming the claim; **AP export's own claim and release run (ApExportWorld) next to the refusal** (U4). |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.BillingExportClaimProtected.cs` (3) | A post-mapping default aimed at the claim is an argument error (the protected set alone); the protection is on the invoice only; a create-time rule aimed at the claim is skipped with the existing warning while an unprotected target of the same profile maps. |
| `tests/Spaarke.ArchTests/BillingExportClaimWriterOnlyGuardTests.cs` (7; fix round: see section 8) | U3: every member that asks the table-level update check also asks `RefuseAppOnlyWrite`, or is in `Exempt` with a reason (stale entries fail); the column refusal is asked from `RefuseAppOnlyWrite` alone; it reads the registry set. U4: `ApExportService` never asks `RefuseAppOnlyWrite` / `RefuseWriterOnlyColumns` and writes through `IGenericEntityService`; only the reviewed types name the column string (the writer set is `{ ApExportService }`; the others name the batch table or another table's lookup, reviewed in the test). Must-fire and must-not-fire controls for both detectors. |

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

## 8. Fix round (review findings and the lead's additions)

Commits on `task/ebilling-165d` after `998c22b365`. Wording stays neutral: "`sprk_billingexport` of a sprk_invoice is changed only by Spaarke itself and can't be changed by this write".

| # | Item | What changed |
|---|---|---|
| 1 | **F1, create path** | `RecordCreationService.CallerBlockedColumnRefusal` and the system-principal branch (step 2, `DecideAsync`) now ask `RecordTableRules.RefuseWriterOnlyColumns` and refuse with kind `BlockedColumn` (HTTP 403 / chat access denied), code `record_update.changed_only_by_its_writer.column`. An invoice can no longer be created already claimed. |
| 2 | **F1, node mapping** | `UpdateRecordNodeExecutor`: the `when` clause also takes `ChangedOnlyByItsWriterColumnReasonCode`, so the refusal is `ValidationFailed`, not `InternalError`. |
| 3 | **F1, message** | The Reason no longer carries "(code)" or "nothing was written"; `RecordOwnerUnresolvedException` adds both. The field-mapping push's `ErrorMessage` adds the code itself (the skip is its only carrier). Tests that pinned the text updated. |
| 4 | **F3, pin bypass** | The "only reviewed types name the column" pin compares `SecureRootInheritance.NormalizeColumn(value)` with the claim, so `sprk_billingexport@odata.bind`, `sprk_BillingExport@odata.bind`, `_sprk_billingexport_value` and a compiler-folded `BatchLookup + "@odata.bind"` are caught. Must-fire fixtures for each, must-not-fire for neighbouring names. |
| 5 | **Registry persister** | `TopicRegistryWorkProductPersister.PersistAsync` asks `RefuseWriterOnlyColumns` next to `RefuseFixedAfterCreateColumns` ("Fix the registry row"), and left `Exempt`. |
| 6 | **K1, batch side** | See below: no generic writer creates a batch row, so no navigation key was added. |
| 7 | **K2, notes** | Coverage table above: `RecordCreationService` :401 was not covered before this round; the action core's confirmed-proposal branch skips the check by design; the coverage pin checks MEMBERS, not branches (a member that asks the refusal for one identity or mode only still passes it). |
| 8 | **uac-r2 U1 = option A** | The four person-token sites (chat update tool `ValidateChat` + `ExecuteChatAsync`, `ChildRecordEndpoints.UpdateAsync`, `OwnedChildWrite.RefileAsync`) plus the persister ask `RecordTableRules.RefuseWriterOnlyColumns` ONLY, never the full `RefuseAppOnlyWrite`. Each returns its existing refusal shape with the column code (chat: `The update was NOT written: {reason} ({code}).` and the code as the tool error code; child-records route: 403 with `reasonCode` = the column code; re-file core: `Invalid` text with the code). `ValidatePushRequest` stays the only exempt member (it carries no columns). |

**The coverage pin** now accepts either column ask: `RefuseAppOnlyWrite` (the app-only writers) or `RefuseWriterOnlyColumns` (the person sites). `Exempt` holds `ValidatePushRequest` alone. A second exact list, `DirectColumnAskers`, names every member allowed to call `RefuseWriterOnlyColumns` other than `RefuseAppOnlyWrite`: the two `RecordCreationService` members, the persister and the four person-token members; a new asker or a dropped one fails. Controls kept (table-only fires; both, direct, listed and delete-only do not).

**uac-r2's gate query** for the marker now uses a contains match on `changed_only_by_its_writer` (so the `.column` code is counted).

**Create callers that still reach `sprk_invoice` after 096b** (merged into this branch): 096b removed `sprk_invoice` from `ChildRecordEndpoints.CreateTables` (`:73`; the route answers unsupported-table before any Dataverse call, pinned by 096b's own test in `SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs:441`) and blocks the chat create tool (`DataverseCreateRecordHandler.IsInvoiceCreate`, `ValidateChat` and `ExecuteChatAsync`). The remaining caller of `RecordCreationService.CreateAsync` with a table chosen at run time is the Office quick create (`Services/Office/OfficeService.cs:2431`; an invoice is one of its tables, its payload is built from fixed fields, never from the request's columns). The other callers fix their table (`AnalysisEndpoints.cs:196` analysis, `EventEndpoints.cs:748` event, `EmailDraftToolHandler.cs:450` communication, `BudgetInquiryExecutor.cs:277` budget). The service refusal therefore stays as defence in depth for any create caller; its must-fire tests drive `RecordCreationService` directly (caller branch, system branch, `CallerBlockedColumnRefusal`), not the route or the chat tool, which 096b closes first.

### K1 (batch side) answer

The invoices could also be associated from the `sprk_billingexport` row through the collection-valued navigation `sprk_invoice_billingexport_billingexport` (Dataverse accepts a collection-valued `@odata.bind` on a POST, not on a PATCH). Every generic writer was checked for a way to CREATE or UPDATE a batch row:

| Writer | Reaches a batch row? | Evidence |
|---|---|---|
| `ChildRecordEndpoints` create route | no | `CreateTables` (`ChildRecordEndpoints.cs:71-83`) does not list the batch table |
| Chat create tool / `RecordCreationService` | no | the table is user-owned (`docs/data-model/sprk_billingexport.md`), so `OwnedChildWrite.PathFor` (`OwnedChildWrite.cs:188-197`) takes the service, which refuses a table with no registry rule (`RecordCreationService.cs:~183`, `table_unsupported`); `RecordTableRules.Find("sprk_billingexport")` is null |
| Update Record node, output mapping, field-mapping push, Repair | no create; PATCH only | `UpdateRecordActionCore.cs:253`, `DataverseUpdateHandler.cs:276`, `FieldMappingEndpoints.cs:1308` use `UpdateExistingRecordFieldsAsync` (update-only, `If-Match: *`); Repair skips a table with no rule (`RecordCreationService.Repair.cs:88-92`); a collection-valued bind on PATCH is not accepted by Dataverse |
| Chat update tool, child-records PATCH, re-file core | PATCH only | same platform limit; the batch table is also on no `RefileTables` list (`ChildRecordEndpoints.cs:112-115`) |
| `$ref` associate calls | none for this relationship | the only `$ref` writers are `NodeService` and `PlaybookService` (the playbook scope relationships) |

So no key was added, and the table was not made writer-only (that would have registered the table and made it creatable by the service). A regression pin was added: `CreateAsync_ABatchRowNamingTheInvoiceNavigation_IsNotCreatedHere` asserts the table has no rule, is on no create list, and a create naming the navigation is `table_unsupported`; registering or listing the table fails it until the navigation is added to a writer-only set first. The "PATCH does not accept it" half is the platform's documented behaviour, not re-measured live here.

### Defect found and fixed in scope

`SecureRootInheritance.NormalizeColumn("_value")` threw `ArgumentOutOfRangeException` (it sliced `name[1..^6]` on a 6-character name). Every column refusal that normalises a payload key (including a model's chat item key) could throw on a key of exactly `_value`. Guarded by a length check; pinned by `AKeyThatOnlyLooksLikeTheReadBackForm_IsNotARefusalAndDoesNotThrow`.

### Seeds and gates

- Seed: the create-path checks removed (both sites): 12 of the new/updated create-path tests failed; restored, all pass. Pin normalisation reverted to the exact string: the column-scan control (folded bind and the other forms) failed; restored. The four person-token checks removed: 6 of the 9 new tests failed (every must-fire; the 3 must-not-fire controls pass); restored, all pass.
- Final gate counts: in the task report.

