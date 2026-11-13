# Task 045e — invoice family: the regarding pair agrees with the core lookup (#2772, DI-029)

Owner decision 2026-11-13 ("Refuse it", #2772 option (a); `decisions.md`). POML: `tasks/045e-invoice-family-pair-agrees-with-core.poml`.
FR-23 (the regarding pair stays pushable) is amended for the six invoice-family tables only.

## Rule

1. On the six invoice-family tables (`InvoiceFamilyCoreRecordGuard.CoreRecordLookups`: `sprk_invoice`, `sprk_billingevent`,
   `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`), a write that sends
   the regarding pair (`sprk_regardingrecordid`, `sprk_regardingrecordtype`, either half) must name the row's effective core
   record: the core lookup written in the same request, else the stored one. For an external identifier filed under an
   invoice only, the effective core record is that invoice (D-68).
2. "Agrees" means: the id parses as a GUID equal to the core record's id (any case, braces allowed), and the type is the
   catalog row (`sprk_recordtype_ref`, active, by `sprk_recordlogicalname`) of the core record's table. The catalog is read
   app-only, on every path.
3. A write that disagrees is refused through the path's existing refusal form, with ONE code
   (`invoice_family_regarding_pair_core_record_mismatch`), and nothing is written:
   - create (`RecordCreationService` step 5½, `RecordTableRules.InvoiceFamilyRule`): `PreWriteRefusal.Refused`, 400;
   - child-record PATCH (`sprk_invoice` only — the route re-files no other family table): `ChildRecordEndpoints.InvoiceFamilyRefused`, 400 with the code;
   - chat update tool (`dataverse.update_record`): the tool error with the code;
   - playbook Update Record (`UpdateRecordActionCore`) and output orchestrator (`DataverseUpdateHandler`): the guard's
     code-only `RefusalException`.
4. Only the halves a write sends are checked; a half it does not send is not read. On an update a sent clear (null) of
   either half disagrees: the row would no longer describe its core record. On a create a null half is not checked (the
   045d derive writes the pair).
5. A write that moves the core lookup (sets or clears one so the row names another record) is never refused by this rule:
   045d's derive replaces the pair with the pair of the record the row will name. A write that re-names the record the row
   already names moves nothing, so the pair it sends is checked.
6. Order, unchanged otherwise: the agreement check is asked after the guard's own decision admits the row (a row the guard
   refuses keeps its own code), and before any derive or write. On the caller update paths it is asked in
   `OwnedChildWrite.DecideInvoiceFamilyAsync`, i.e. inside `RefileAsync`, after 045b's planner (`KeepRegardingPairWholeAsync`);
   the planner never moves a core lookup, so the pair it leaves is the one checked.
7. Fail closed: a fault reading the stored row or the catalog writes nothing (`invoice_family_read_failed`, the fault's text
   never in the reason); no active catalog row for the core table is `invoice_family_resolver_fields_incomplete`; a stored
   row that names no single core record keeps the guard's own answer (`no_core_record`, `multiple_core_records`).
   Cancellation is rethrown.
8. The field-mapping push asks the same guard (`CheckGenericUpdateAsync`, push order unchanged: source gate → access-column
   scrub → guard → planner → derive), so a pair in a family child's payload would refuse that child only. A push never puts
   a pair in its payload: the mapping engine (`CreateTimeFieldMapping.ApplyRule`, FR-05(e)) skips a rule aimed at the pair or
   the resolver fields on every table. On the push a family child's pair moves only with its core lookup (045d).
9. Not in scope (ebilling-r1, held): a rule that a child's core record equals its invoice's.

## Code decision

No existing guard code says "the regarding record disagrees with the core record":
`multiple_core_records` / `no_core_record` describe the core lookups, `line_invoice_core_record_mismatch` a line and its invoice,
`resolver_fields_incomplete` a missing catalog row. One new code was added,
`InvoiceFamilyCoreRecordGuard.RegardingPairCoreRecordMismatchCode = "invoice_family_regarding_pair_core_record_mismatch"`, with
fixed text that names no record (`RegardingPairCoreRecordMismatchReason`). The code-only paths report the code through the
existing `CodeOnlyRefusalMessage`; `ChildRecordEndpoints.InvoiceFamilyRefused` maps an unlisted guard code to 400 with the
reason, which is this code's intended status. No per-path code.

## §11 — new surface

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Code `invoice_family_regarding_pair_core_record_mismatch` + reason | The guard's codes (above); none names a pair/core disagreement. | Reusing `multiple_core_records` would tell the caller the core lookups are wrong, which they are not. | A disagreeing pair is refused with no code that says why, or with a wrong one. |
| `InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync` | `Check` (core lookups only), 045d's `Derive*` (writes the pair; never compares). | It is the one place every path calls; it extends the guard file, beside `Check` / `CheckGenericUpdateCoreAsync`, as the POML asks. | Each of five paths re-derives the comparison, or a row is left naming one record by its core lookup and another by its pair. |
| `RegardingPairWrite` (record struct) + `RegardingPairWrite.Of` | `OwnedChildWrite.PairChangeOf` (caller-path tuple), 045b's `PairOf(MappedItem)`. | It is a thin public carrier over `PairChangeOf`, so the guard's signature does not take a private tuple; it adds no parsing. | The guard cannot take the pair from the app-only payloads and the create's mapped item through one signature. |
| `OwnedChildWrite.PairWriteOf(MappedItem)` | `PairOf(item)`. | Maps `PairOf` onto `RegardingPairWrite` (`InvalidId` → `Unreadable`); no new parsing. | The create's step 5½ cannot hand its item's pair to the one check. |
| `OwnedChildWrite.DecidePairOnlyUpdateAsync` (private) | `DecideInvoiceFamilyAsync` returned Proceed for an update writing no guarded column. | A private branch of `DecideInvoiceFamilyAsync`, reusing `ReadLookupsAsCallerAsync` and `Check`. | A pair-only PATCH or chat update of a family row is never checked (the core lookup is not written, so the old path read nothing). |
| `recordTypes` parameter on `DecideInvoiceFamilyAsync` (optional) | `PreWriteRow.RecordTypes` (the create's app-only catalog reader, 045d). | Optional, defaults to `AppOnlyRecordTypes(appOnlyEntities)`; the create passes its own reader. | The create cannot read the catalog through the reader it already holds. |
| Test-only: `EntitySetNameFixtures` pins `sprk_recordtype_ref` → `sprk_recordtype_refs` | The fixture's pinned set names. | One entry. | The playbook path's tests cannot bind the pair type. |

## What changed

- `InvoiceFamilyCoreRecordGuard.cs`: the code and reason; `CheckRegardingPairAgreesAsync`; `CheckGenericUpdateCoreAsync`
  asks it after its own decision (and on a pair-only update, on the stored decision); `RegardingPairWrite`.
- `OwnedChildWrite.cs`: `PairWriteOf`; `DecideInvoiceFamilyAsync` asks the check after an admitted decision (create and
  update) and on a pair-only update (`DecidePairOnlyUpdateAsync`); optional `recordTypes`.
- `RecordTableRules.cs`: `InvoiceFamilyRule` passes `row.RecordTypes` to `DecideInvoiceFamilyAsync`.
- `FieldMappingEndpoints.cs`: unchanged (it already calls `CheckGenericUpdateAsync`).
- Tests (KEEP paths):
  - `tests/unit/domain/Dataverse/InvoiceFamilyRegardingPairAgreementTests.cs` — the check on all six tables and every core
    lookup, and the create rule (`RecordTableRules.InvoiceFamilyRule`) on all six tables.
  - `tests/integration/data-mutation/RecordOwnership/InvoiceFamilyGenericUpdatePathTests.PairAgreement.cs` — playbook
    update and output orchestrator on all six tables; the push.
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InvoiceFamilyPairAgreement.cs` — the
    PATCH (invoice) and the chat tool (all six tables), with scripted metadata for the approval, external identifier and
    correction and an app-only catalog answer in `AppEntities()`.
  - `tests/integration/seam/RecordCreation/RecordCreationServiceInvoiceFamilyRuleSeamTests.cs` — one create through the
    real service and problem map.
  - Existing test changed: `ActionSeamTests.UpdateRecordAsync_AnInvoiceWhosePairPlannerReadFails_AfterTheFamilyGuardPassed_WritesNothing`.
    It sent a pair-only write to a stored invoice with no core record, which the guard now refuses (`no_core_record`) before
    the planner. Its stored invoice now has a matter and its pair names that matter, so the guard still passes and the
    planner's read failure is still what it tests.

## Evidence

New tests (267), all passing with the rule on:

| Set | Tests | Failed with the rule off |
|---|---|---|
| `InvoiceFamilyRegardingPairAgreementTests` (domain: the check, six tables × each core lookup; the create rule, six tables) | 156 | 120 |
| `InvoiceFamilyGenericUpdatePathTests.PairAgreement` (playbook update + output orchestrator × six tables; push × six tables) | 75 | 33 |
| `SecureChildOwnershipAiToolTests.InvoiceFamilyPairAgreement` (PATCH on the invoice; chat tool × six tables) | 35 | 22 |
| `RecordCreationServicePipelineSeamTests.CreateAsync_AnInvoiceWhosePairNamesAnotherMatter_Is400PairMismatch_AndWritesNothing` | 1 | 1 |

- Rule off = `CheckRegardingPairAgreesAsync` answering "no decision" at its first line; restored afterwards. Every must-fire
  (a different id, the right id under the wrong type, a clear, an id that names no record, the D-68 external identifier, a
  re-named core with a wrong pair, a disagreeing create) and every catalog-fault / cancellation test failed with it off. The
  domain must-not-fire `ThePairOfTheCoreRecord_Agrees` (19) also failed with it off, because it asserts the catalog row was
  read (the comparison ran). The rest of the must-not-fire tests (a pair that agrees, no pair, a move with or without a
  pair, a rename, the push's skipped pair) pass both ways, as they must. The two "stored-row read faults with a pair sent"
  tests pass both ways: the guard already reads the stored row before this check.
- Filtered suites (Invoice | OwnedChildWrite | FieldMapping | UpdateRecordAction | DataverseUpdateHandler | RecordCreation |
  ChildRecord | SecureChildOwnership | RegardingPair | RecordTableRule | UpdatePlanner | DataverseUpdateRecordHandler |
  ActionSeam, minus `Api.Office.`): 9160 passed, 0 failed, 3 skipped. Before the new tests: 8861 passed, 1 failed (the
  `ActionSeamTests` case adapted above), 3 skipped.
- ebilling-r1 filters (Finance | ProcedureAssignment | InvoiceFamily, minus `Api.Office.`): 8027 passed, 0 failed, 4 skipped.
- ArchTests (run alone): 183 passed.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `dotnet format whitespace --folder --verify-no-changes` on the touched files: clean.

## Known limits (K)

- K1: on a create, a type-only pair that step 4½ clears (no agreeing lookup) is not refused; the derive writes the pair.
- K2: an update that sends one half is checked on that half only; the stored other half is not read or re-checked.
- K3: the type agrees only with the catalog row the app reads as active for the core table (top 1). A pair naming another
  row for the same table (an inactive or duplicate row) is refused.
- K4: on the caller update paths the stored core lookups are read as the caller (`ReadLookupsAsCallerAsync`, as the guard
  already reads them there); the catalog is read app-only. A caller who cannot read the stored row is refused as the guard
  refuses it today.
- K5: on the caller update paths 045b's planner runs before the refusal, so its reads (the catalog, a display read) happen
  for a write that is then refused. Nothing is written.
- K6: extra reads: one stored-row read as the caller on a caller-path update that sends only a pair; one catalog read when a
  write sends a type (none when the id alone decides, or no pair is sent).
- K7: on the app-only paths run as a person, the check is asked on the app's read of the row before Dataverse checks the
  person's access to it, as the guard's other questions are.
- K8: the field-mapping push maps no pair on any table (the engine's FR-05(e) skip), although `PushMayWrite` answers true for
  the pair columns. This task does not change either; a family child's pair moves on the push only with its core lookup.
- K9: model-driven form edits of a family row's pair do not pass through these paths and are not covered.
