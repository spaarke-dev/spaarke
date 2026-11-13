# Task 045d — invoice-family resolver fields re-derived on generic writes (Q8 (a))

Design: `task-045c-notes.md` "Q8 (a)" (copied, not redesigned). POML: `tasks/045d-invoice-family-rederive.poml`.

## Rules

1. An invoice-family row (`sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`,
   `sprk_externalidentifier`, `sprk_invoicecorrection`) created through the generic create carries the resolver fields
   of the core record the guard decided: `sprk_regardingrecordtype` (the table's `sprk_recordtype_ref` row),
   `sprk_regardingrecordid` (lower-case "D"), `sprk_regardingrecordurl`, and, when the record has them,
   `sprk_regardingrecordname` (cut to 100) and `sprk_regardingrecordnumber` (the column the catalog row names). The server's
   values replace any the caller sent.
2. An update that sets or clears one of the row's own core lookups (`InvoiceFamilyCoreRecordGuard.ColumnsToRead`) writes the
   fields of the record the row will name, in the same write. This holds on the caller paths (child-record PATCH, chat
   update tool) and on the app-only paths (playbook Update Record, output mapping, field-mapping push).
3. All five are written together or not at all. A blank name or number is written as a clear, so a moved row never keeps
   the previous record's display. A derive that cannot be done (no catalog row, record missing, a read fault) refuses; the
   row is never written, or moved, without the type, id and URL of the record it names.
4. An update that touches no core lookup reads nothing more and writes none of the five.
5. Order: on the create, the derive runs inside step 5½, after step 5's AppendTo on every record the row names, and only
   when the guard admits the row. On every update path it runs after that path's invoice-family guard and the 045b planner,
   so their refusals come first and are unchanged.
6. The dedicated submission writers are unchanged: their derive still refuses a blank name or number (all five or none).

## Decisions

**Main session, 2026-11-12.** A derive failure maps through the EXISTING invoice-family refusal mappings, so the write fails
closed. No new code, mapping arm or text (NFR-07 byte for byte).

| derive answer | create (step 5½) | child-record PATCH | chat tool | playbook Update Record / output mapping | field-mapping push |
|---|---|---|---|---|---|
| `invoice_family_read_failed` (a catalog or record read faulted, no reader) | 500, that code | 500, that code | error code | `RefusalException` with that code | per-child error, that code's text |
| `invoice_family_resolver_fields_incomplete` (no catalog row, no number column) | 400, that code | 400, that code | error code | `RefusalException` with that code | per-child error |
| `invoice_family_record_not_found` (record missing or not readable) | the uniform 404 | the uniform 404 | the tool's existing denial | `RefusalException` with that code; on a run as a person, `read_failed` (as the guard answers there) | per-child error |

On the two app-only handlers the form is the guard's own: `InvoiceFamilyCoreRecordGuard.RefusalException(table, code)`, the
code-only message. On the push it is the guard's per-child form: `failed++`, an error `"Not written: " +
CodeOnlyRefusalMessage(code)`, and the row is skipped.

**Owner, 2026-11-12, option (b): write what the record has.** On the generic paths the type, the id and the URL are always
written; the name and the number are written when the record has them. A blank name or number is no longer
`resolver_fields_incomplete` on a generic path. The submission writers keep their behaviour through a separate generic-path
variant; their existing tests pin it (`InvoiceFamilyCoreRecordGuardTests.AFieldThatCannotBeFilled_IsARefusal_NeverAPartialSet`
and `RecordTableRulesInvoiceFamilyDeriveTests.TheSubmissionWritersDerive_StillRefusesABlankName_AllFiveOrNone`).

Why (K4 fact check, from `src/dataverse/solutions/SpaarkeMaster/Entities/*/Entity.xml`): `sprk_matter.sprk_mattername` and
`sprk_project.sprk_projectname` are not required; `sprk_invoice.sprk_invoicenumber` is neither required nor an autonumber.
A blank name or number is legitimate data, and refusing it would make such a record unusable as a generic core record.

## The app-only half

- The derive runs after each path's guard and planner, on the payload those produced, so its values win.
- It reads app-only through the `IGenericEntityService` the path already holds:
  - the row's stored core lookups (`RetrieveAsync`);
  - the catalog row by table name (`RetrieveMultipleAsync`, a `QueryExpression` on `sprk_recordtype_ref`:
    `sprk_recordlogicalname` = table, active, top 1);
  - the record's name and number (`RetrieveAsync`).
- It decides with the guard's pure `Check` on the stored values overlaid with the change. A row `Check` refuses is left to the
  path's existing guard, so there is no new refusal for any table.
- Payload shapes: one helper, the dictionary overload of `OwnedChildWrite.WithColumnsSet`:
  - the type becomes `{navigation}@odata.bind` = `/sprk_recordtype_refs({id})`, on the table's spelling of the navigation
    property;
  - text columns are written as values, a blank as `null`;
  - the caller's own spelling of any of the five is removed first.
- Push and 022c: the source gate and `DropAccessColumns` run before the guard and the planner; the derived fields are added
  after them. They are server values, not access columns. The regarding pair stays pushable under FR-23, and a pushed pair
  that disagrees with a moved core lookup is replaced by the derived one.
- A run as a person (the playbook or output mapping acting as the caller) answers a missing record as `read_failed`, the
  same as the guard on that path (#2081).

## What changed

- `RecordTableRules`:
  - `PreWriteCheck` returns `PreWriteResult` (a refusal, or columns to set).
  - `PreWriteRow` carries the create's app-only readers (`Records`, `RecordTypes`).
  - `InvoiceFamilyRule` derives after the guard admits, with the generic variant. With no reader it fails closed
    (`read_failed`).
- `RecordCreationService`: step 5½ merges the rules' columns into the item (`OwnedChildWrite.WithColumnsSet`) before step 6.
  - The readers are `_entities.RetrieveAsync` and `ICommunicationDataverseService.QueryRecordTypeRefAsync`, from a scope (the
    readers the dedicated writers use).
  - The mapping-source rebuild (`StripMappedSourceValuesAsync`) re-applies them.
- `InvoiceFamilyCoreRecordGuard`:
  - `DeriveResolverFieldsAsync` (the submission writers) is unchanged in behaviour.
  - `DeriveGenericResolverFieldsAsync` is the generic variant: blank name or number admitted.
  - `AppOnlyRecords` and `AppOnlyRecordTypes` are the readers over `IGenericEntityService`.
  - `DeriveForGenericUpdateAsync` decides the change for an app-only update.
  - `InvoiceFamilyResolverFields.ToGenericFields` (blank → `null`); `ToFields` is unchanged.
- `OwnedChildWrite`:
  - `DecideInvoiceFamilyAsync` returns the admitted decision; `CheckInvoiceFamilyAsync` is now a projection of it.
  - `KeepRegardingPairWholeAsync` (the 045b caller-path planner) then runs `KeepInvoiceFamilyResolverFieldsAsync`. It reads the
    stored core lookups, the catalog row and the record AS THE CALLER, decides with the guard's pure `Check`, and sets the
    five.
  - `KeepInvoiceFamilyResolverFieldsAppOnlyAsync` and the dictionary `WithColumnsSet` serve the app-only paths.
- `ChildRecordEndpoints`: the planner-refusal switch maps `InvoiceFamilyRefusal` through `InvoiceFamilyRefused` (one arm).
- `DataverseUpdateHandler`, `UpdateRecordActionCore`, `FieldMappingEndpoints` (push): one call each to
  `KeepInvoiceFamilyResolverFieldsAppOnlyAsync` after the planner, plus the path's existing refusal form.

## §11 — new surface

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `PreWriteResult` | `PreWriteRefusal?` (the old return) | A refusal cannot carry columns; wrapping it keeps the refusal record and its contract unchanged | Step 5½ has no way to add the five; a generic-created family row is written without them (Q8 (a) unmet) |
| `InvoiceFamilyCoreRecordGuard.RecordLookup` | `DeriveResolverFieldsAsync(…, IGenericEntityService, …)` | Extended: both entry points share one private body | The caller update path has no app-only service; without the delegate it cannot use the guard's derive and would fork it |
| `DeriveGenericResolverFieldsAsync` / `BuildGenericResolverFields` / `ToGenericFields` | `DeriveResolverFieldsAsync` / `BuildResolverFields` / `ToFields` | A flag on the shared private body; the strict builder is untouched so the submission writers stay byte for byte | Option (b) cannot be met without changing the submission writers' refusal |
| `AppOnlyRecords` / `AppOnlyRecordTypes` | `ICommunicationDataverseService.QueryRecordTypeRefAsync` (not held by the app-only paths) | Both are delegates over the `IGenericEntityService` the paths already hold | The app-only paths cannot read the catalog row and the derive cannot run there |
| `DeriveForGenericUpdateAsync` | `CheckGenericUpdateAsync` (invoice and billing event, decision only) | Kept separate: the derive covers all six tables and returns the fields; the guard's decision code is unchanged | Each app-only path would repeat the change, read and decision logic |
| `InvoiceFamilyDecision` / `DecideInvoiceFamilyAsync` | `CheckInvoiceFamilyAsync` | Extended in place: the old method is now a projection | The create cannot learn which core record the guard decided without running the guard twice |
| `WithColumnsSet` (mapped item and dictionary) | `WithoutColumns`, `Merge` | Both reused; a setter is needed for SDK values (an `EntityReference` pair type → bind on the table's navigation property) | The derived columns cannot be set over the caller's payload; the caller's values would win |
| `KeepInvoiceFamilyResolverFieldsAsync` / `…AppOnlyAsync` | 045b planner rule 4 (moves the pair with an agreement lookup) | Runs after the planner; rule 4 does not cover a core-table switch | A family row moved matter → project keeps a stale or cleared pair (045b notes, "What is not covered") |

## Evidence

- First round (create and caller paths), 49 new tests, all under KEEP paths:
  - `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesInvoiceFamilyDeriveTests.cs`;
  - `RecordCreationServiceInvoiceFamilyRuleSeamTests.cs` (+6);
  - `SecureChildOwnershipAiToolTests.InvoiceFamilyResolverFields.cs` (8).
  - With the derive switched off, 42 of 49 failed; restored, all passed.
- Second round (option (b) and the app-only half), 30 tests new or changed:
  - `RecordTableRulesInvoiceFamilyDeriveTests`:
    - a blank name or number is admitted (matter name null and blank, project name null, invoice number null and empty for the
      external identifier);
    - the submission writers' derive still refuses a blank name.
  - `RecordCreationServiceInvoiceFamilyRuleSeamTests`: a matter with no name or no number is created with the type, id and
    URL, and without the blank one.
  - `InvoiceFamilyGenericUpdatePathTests.ResolverFields.cs`, on the playbook update, the output mapping and the push:
    - must fire: matter → matter (the read order is pinned), matter → project, a blank name or number cleared;
    - fail closed: no catalog row → incomplete, catalog fault → read_failed, matter gone → record_not_found, each with nothing
      written; matter gone on a run as a person → read_failed;
    - must not fire: a rename reads nothing for the derive and writes none of the five.
  - Harness: `AnswerDeriveReads` answers the catalog and record reads in the strict mock worlds. The D-96 `Times.Once` verify is
    narrowed to the billing-event query it pins.
- Fail-then-pass (second round): with option (b) off (the generic flag forced strict) and the app-only call returning early,
  26 of the 30 failed. The 4 that pass both ways are the 3 rename must-not-fire rows and the submission writers' pin.
  Restored: 30/30.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `dotnet test tests/Spaarke.ArchTests/`: 2536 passed.
- `Sprk.Bff.Api.Tests` filtered run: 9361 passed, 0 failed, 3 skipped (live tests).
  - Filter: Invoice | RecordTableRules | PreWrite | RecordCreation | OwnedChildWrite | UpdatePlanner | ChildRecord |
    RegardingPair | SecureChildOwnership | DataverseUpdateRecordHandler | BillingExport | Refusal | ActionSeam | FieldMapping |
    UpdateRecordAction | DataverseUpdateHandler | AiTool | InvoiceSubmission, minus `Api.Office.`.
  - An earlier run of the same filter, made while ArchTests ran alongside, had 1 failure. Its name was not captured, and it
    did not reproduce.
- `dotnet format whitespace --verify-no-changes` on the touched files: clean.

## Known limits (K)

- K1: an update that changes a core lookup costs up to three more reads (the stored core lookups, the catalog row, the
  record's name and number), plus any metadata read the record reader needs. On a caller path they run as the caller; on an
  app-only path they run app-only.
- K2: the derive's stored-lookup read and the guard's own are separate reads. A row changed between them is decided by the
  guard on its read, and the five reflect the derive's read. This is the same window as 045b's planner.
- K3: when a derive failure and a later refusal both apply on an update, the derive failure is answered. The later refusals
  are the D-96 lines, supersedes, the work-assignment cycle and `RefileAsync`'s shape refusals. Each alone answers as before.
- K4 (replaced by option (b)): a blank name or number is written as a clear on the generic paths; the submission writers still
  refuse it.
- K5: the derived pair type's catalog row is not asked for AppendTo, the same as the dedicated writers.
- K6: on the create, the app-only catalog reader is resolved per call from a DI scope. `UpdateRecordActionCore` also takes
  its `IGenericEntityService` from a scope, and only for a family table.
- K7: on the app-only paths, a core lookup that those paths refuse as an access column is refused before the derive is
  reached. The derive therefore fires there only for the lookups the paths let through (for example the invoice's and the
  billing event's core lookups).
