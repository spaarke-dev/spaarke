# Task 045d — invoice-family resolver fields re-derived on generic writes (Q8 (a))

Design: `task-045c-notes.md` "Q8 (a)" (copied, not redesigned). POML: `tasks/045d-invoice-family-rederive.poml`.

## Rules

1. An invoice-family row (`sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`,
   `sprk_externalidentifier`, `sprk_invoicecorrection`) created through the generic create carries the five resolver fields
   of the core record the guard decided: `sprk_regardingrecordtype` (the table's `sprk_recordtype_ref` row),
   `sprk_regardingrecordid` (lower-case "D"), `sprk_regardingrecordname` (cut to 100), `sprk_regardingrecordurl`,
   `sprk_regardingrecordnumber` (the column the catalog row names). The server's values replace any the caller sent.
2. A caller update (child-record PATCH, chat update tool) that sets or clears one of the row's own core lookups
   (`InvoiceFamilyCoreRecordGuard.ColumnsToRead`) writes the five of the record the row will name, in the same PATCH.
3. All five or none. A derive that cannot complete them refuses; the row is never written, or re-filed, without them.
4. An update that touches no core lookup reads nothing more and writes none of the five.
5. Order: on the create, the derive runs inside step 5½, after step 5's AppendTo on every record the row names, and only
   when the guard admits the row. The guard's own refusals come first and are unchanged.

## Decision (main session, 2026-11-12)

A derive failure maps through the EXISTING invoice-family refusal mappings — the create's `RecordTableRules.InvoiceFamilyPreWrite`,
the route's `ChildRecordEndpoints.InvoiceFamilyRefused`, the chat tool's `RefileRefusal` — so the write fails closed:

| derive answer | create (step 5½) | child-record PATCH | chat tool |
|---|---|---|---|
| `invoice_family_read_failed` (catalog or record read faulted, no reader) | 500, that code | 500, that code | error code `invoice_family_read_failed` |
| `invoice_family_resolver_fields_incomplete` (no catalog row, no number column, blank name or number) | 400, that code | 400, that code | error code |
| `invoice_family_record_not_found` (record missing / not readable) | the uniform 404 | the uniform 404 | the tool's existing denial (`Denied`, access-denied code) |

The refusal contract (NFR-07) is byte for byte unchanged: no new code, no new mapping arm, the same builders.

## What changed

- `RecordTableRules`: `PreWriteCheck` returns `PreWriteResult` (a refusal, or columns to set); `PreWriteRow` carries the
  create's app-only readers (`Records`, `RecordTypes`); `InvoiceFamilyRule` derives after the guard admits. With no reader it
  fails closed (`read_failed`).
- `RecordCreationService`: step 5½ merges the rules' columns into the item (`OwnedChildWrite.WithColumnsSet`) before step 6;
  the readers are `_entities.RetrieveAsync` and `ICommunicationDataverseService.QueryRecordTypeRefAsync` from a scope (the
  readers the dedicated writers use). The mapping-source rebuild (`StripMappedSourceValuesAsync`) re-applies them.
- `InvoiceFamilyCoreRecordGuard`: `DeriveResolverFieldsAsync` overload over a `RecordLookup` delegate (same body; the old
  overload delegates to it).
- `OwnedChildWrite`: `DecideInvoiceFamilyAsync` returns the admitted decision (`CheckInvoiceFamilyAsync` is a projection of
  it); `KeepRegardingPairWholeAsync` (the 045b caller-path planner) then runs `KeepInvoiceFamilyResolverFieldsAsync`, which reads
  the stored core lookups, the catalog row and the record AS THE CALLER, decides with the guard's pure `Check`, and sets the
  five. A row `Check` refuses is left to `RefileAsync`, which refuses it in its own order.
- `ChildRecordEndpoints`: the planner-refusal switch maps `InvoiceFamilyRefusal` through `InvoiceFamilyRefused` (one arm).

## §11 — new surface

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `PreWriteResult` | `PreWriteRefusal?` (the old return) | A refusal cannot carry columns; wrapping it keeps the refusal record and its contract unchanged | Step 5½ has no way to add the five; a generic-created family row is written without them (Q8 (a) unmet) |
| `InvoiceFamilyCoreRecordGuard.RecordLookup` | `DeriveResolverFieldsAsync(…, IGenericEntityService, …)` | Extended: the old overload delegates to the new one | The caller update path has no app-only service; without the delegate it cannot use the guard's derive and would fork it |
| `InvoiceFamilyDecision` / `DecideInvoiceFamilyAsync` | `CheckInvoiceFamilyAsync` | Extended in place: the old method is now a projection | The create cannot learn which core record the guard decided without running the guard twice |
| `WithColumnsSet` | `WithoutColumns`, `Merge` | Both reused; a pure setter is needed for SDK values (an `EntityReference` pair type → bind on the table's navigation property) | The derived columns cannot be set over the caller's mapped item; the caller's values would win |
| `KeepInvoiceFamilyResolverFieldsAsync` | 045b planner rule 4 (moves the pair with an agreement lookup) | Runs inside the planner wrapper; rule 4 does not cover a core-table switch | A family row moved matter → project keeps a stale or cleared pair (045b notes, "What is not covered") |

## Evidence

- New tests (49), all under KEEP paths:
  - `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesInvoiceFamilyDeriveTests.cs` (35): every family table × every
    core lookup + the invoice-only external identifier (19 rows); 100-character cut; catalog fault / no row / no number column;
    record missing / fault / blank name or number; no readers; guard refusal reads nothing; table outside the family reads
    nothing; `WithColumnsSet` overwrites caller values on both navigation-property spellings.
  - `RecordCreationServiceInvoiceFamilyRuleSeamTests.cs` (+6): the five in the created row over the caller's own; step-5
    AppendTo refusal makes no catalog or record read; catalog fault 500; no catalog row 400; no number 400; record gone 404.
  - `SecureChildOwnershipAiToolTests.InvoiceFamilyResolverFields.cs` (8): PATCH matter → project and matter → matter, chat tool
    matter → project carry the five; no catalog row 400 (route and tool); catalog fault 500; record unreadable 404; nothing
    patched on each refusal; a rename-only update reads no catalog and writes none of the five.
- Fail-then-pass: with the derive switched off (the rule returning the guard's answer only; the planner wrapper returning the
  045b result), 42 of the 49 failed (133 run, 91 passed). The 7 that pass both ways are the must-not-fire, order and pure
  setter tests. Restored: 133/133.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `dotnet test tests/Spaarke.ArchTests/`: 2536 passed.
- `Sprk.Bff.Api.Tests` filtered (Invoice | RecordTableRules | PreWrite | RecordCreation | OwnedChildWrite | UpdatePlanner |
  ChildRecord | RegardingPair | SecureChildOwnership | DataverseUpdateRecordHandler | BillingExport | Refusal | ActionSeam,
  minus `Api.Office.`): 8697 passed, 0 failed, 3 skipped.
- `dotnet format whitespace --verify-no-changes` on the 15 touched files: clean.

## Not done — the app-only update half (reported, not built)

Design item 4 also names the three app-only update paths (`UpdateRecordActionCore`, `DataverseUpdateHandler`, the field-mapping
push). They are not changed. Their only reader is `IGenericEntityService`; the catalog by table name is reachable through its
`RetrieveMultipleAsync` (a `QueryExpression` on `sprk_recordtype_ref`), so the half is feasible, but it needs decisions this
task does not answer: the payload shapes (SDK dictionary, mapped binds on the push), the refusal form on those paths (the
guard's code-only `RefusalException`, and what `record_not_found` becomes), and its tests on three paths. Until then a core
lookup change through those paths keeps 045b's behaviour (the pair moves with an agreement lookup, or is cleared).

## Known limits (K)

- K1: a caller update that changes a core lookup costs up to three more caller reads (stored core lookups, the catalog row, the
  record's name and number), plus any metadata read the record reader needs.
- K2: the planner's stored-lookup read and `RefileAsync`'s own are separate reads; a row changed between them is decided on
  the later read by the guard, and the five reflect the earlier one. Same window as 045b's planner.
- K3: when a derive failure and a later refusal (D-96 lines, supersedes, work-assignment cycle, `RefileAsync`'s shape
  refusals) both apply on an update, the derive failure is answered. Each alone answers as before.
- K4: a core record with a blank name or number now refuses the create or move (`resolver_fields_incomplete`), as the
  dedicated writers already do.
- K5: the derived pair type's catalog row is not asked for AppendTo, the same as the dedicated writers.
- K6: on the create, the app-only catalog reader is resolved per call from a DI scope.
