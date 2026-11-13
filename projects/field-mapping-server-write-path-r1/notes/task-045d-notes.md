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
5. Order:
   - On the create, the derive runs inside step 5½, after step 5's AppendTo on every record the row names, and only when
     the guard admits the row.
   - On the app-only update paths (playbook update, output mapping, push) it runs after that path's invoice-family guard
     and the 045b planner, so their refusals come first and are unchanged.
   - On the caller update paths (child-record PATCH, chat update tool) it runs inside the 045b caller-path planner, which
     runs BEFORE `RefileAsync`'s guard. It asks the guard's pure `Check` first and derives nothing for a row `Check`
     refuses, so that refusal is still answered by `RefileAsync`'s guard with its own code. A derive failure on a row
     `Check` admits answers before the guard's later questions (K3).
6. The dedicated submission writers are unchanged: their derive still refuses a blank name or number (all five or none).
7. On the app-only paths the name and number are read as the 045b planner reads a moved pair's display values on that
   path:
   - on the push, as the caller;
   - on the playbook update and the output orchestrator, as the person the run acts as (`MovedDisplayReader`);
   - with no person they are cleared, never derived app-only. This is 045b's rule (fix round F-045b-1), which the derive
     matches so it never refills what the planner cleared.

   A record the reader may not read (403 or 404) that exists has its name and number cleared; the type, id and URL are
   still written. A record that does not exist refuses (`record_not_found`, or `read_failed` on a write run as a person).
   A reader fault, or no query, refuses `read_failed`.
8. A fault's own text never enters a generic writer's refusal reason (only its type name); the reason can reach a 500's
   detail. The submission writers' text is unchanged.
9. The derive reads the stored row only to derive from it. A stored row that does not exist gets no derive refusal; the
   path answers it as it answers any missing row. A missing or unreadable NEW core record still refuses.
10. An update that names the record the row already names (the same table and id, after the change is overlaid on the
    stored row) derives nothing and writes none of the five, on every update path. The row keeps the fields it has, as the
    045b planner keeps a pair that does not move (rule 4). A re-run that binds the same matter, an external identifier
    given only its invoice while its matter decides, or a push by a caller who may append to but not read the matter no
    longer clears the name and number.
11. On the app-only update paths a row the derive's own `Check` refuses is refused with that code, through the path's
    existing form. The path's guard admitted it on its own read, so the row changed in between; the write must not move
    the core with stale fields. On the caller paths the derive still leaves such a row to `RefileAsync`'s guard, which
    runs after it (rule 5).
12. On the create (step 5½) the name and number are read AS THE CALLER, through the same reader as the planner's caller
    read (`OwnedChildWrite.CallerRecordDisplay`, wrapped by `CallerDisplayRecords`). A caller who may append to the matter
    but not read it gets the row with the type, id and URL, and with the name and number cleared; the matter is checked to
    exist app-only, and a missing one refuses as before (the uniform not-found). The catalog row stays app-only: it holds
    no record's values. The submission writers are unchanged (strict variant).
13. No generic writer can fall back to an app-only read of the name and number: `DeriveForGenericUpdateAsync`'s `records`
    and `KeepInvoiceFamilyResolverFieldsAppOnlyAsync`'s `displayRecord` are required parameters.

## H1 widened, ebilling-r1 + unified-access-control-r2 agreed 2026-11-13

The generic update paths' invoice-family guard (`CheckGenericUpdateAsync`) covers every family table: the approval, the
external identifier, the correction and the submission as well as the invoice and the line.
- The same rule applies (one core record per row; D-96 for an invoice). The stored row is read before deciding, as for the
  invoice and the line.
- `IsUpdateOnlyTarget` covers the same six tables. Their writes go out update-only (`If-Match: *`): a missing row is
  refused with `invoice_family_record_not_found`, or `invoice_family_read_failed` on a write run as a person, and is never
  created.
- Refusals use each path's existing form and `CodeOnlyRefusalMessage`; no new code.
- The guard has no rule comparing an external identifier's core record with its invoice's. With a core lookup set, that
  lookup decides; D-68 applies only to a row with none.
- A submission's core lookups are access columns on the playbook update and the output orchestrator. There they are refused
  before the guard (`sdap.access.access_column_server_only`); the push reaches the guard.
- Order on the push: source gate → access-column scrub → guard → planner → derive.
- Rule 9 and H1 together: the guard now reads the stored row of all six tables first and refuses a missing one with its own
  code. The derive's own "missing stored row" therefore falls through to that answer (or, in a race, to the update-only
  write's) and never adds a code of its own.

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
  - the record's name and number, as the caller or the person (rule 7); app-only only to check, after a 403 or 404, that
    the record exists.
- It decides with the guard's pure `Check` on the stored values overlaid with the change. The path's guard ran first, so a
  row `Check` refuses here changed in between; it is refused with that code (rule 11). A row whose decided record equals
  the stored row's derives nothing (rule 10).
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

## Who reads the name and number (fix round 5 inventory)

Every caller of the derive (`DeriveResolverFieldsAsync`, `DeriveGenericResolverFieldsAsync`, `DeriveForGenericUpdateAsync`)
and of `PreWriteRow.Records`, found by grep over `src/server`:

| Path | Writes | Name and number read as | Why that is right |
|---|---|---|---|
| Create step 5½ (`RecordCreationService.RunPreWriteRulesAsync` → `RecordTableRules.InvoiceFamilyRule`), reached by every route that creates through the service: the child-record route, the chat create tool (`DataverseCreateRecordHandler`), the Office add-in creates (`OfficeService`, both calls) and the other `RecordCreationPrincipal.ForCaller` callers | A family row, as the caller | The caller (fixed in round 5; it was app-only) | The row is the caller's; a value they may not read is not copied into it. A create with no caller (system principal) is refused before any read |
| Create's mapping-source rebuild (`RecordCreationService.Mapping.cs`, `StripMappedSourceValuesAsync` → `RunPreWriteRulesAsync`) | The same row, rebuilt | The caller (the same `RunPreWriteRulesAsync`, so fixed with it) | As above |
| Caller update planner (`OwnedChildWrite.KeepInvoiceFamilyResolverFieldsAsync`): child-record PATCH, chat update tool | A family row, as the caller | The caller (`CallerRecordReader`; a record they may not read is refused not-found, K8) | Unchanged since round 1 |
| Field-mapping push (`KeepInvoiceFamilyResolverFieldsAppOnlyAsync`) | A family row, app-only on the caller's behalf | The caller (`CallerDisplayReader`) | Round 3; the 045b planner reads the same way there |
| Playbook update, output mapping (`KeepInvoiceFamilyResolverFieldsAppOnlyAsync`) | A family row, as the run's person or app-only | The person the run acts as (`MovedDisplayReader`); with no person, not read (cleared) | Round 4; 045b's rule F-045b-1 |
| Invoice submission service (`InvoiceSubmissionService`, submission row) and persister (`InvoiceSubmissionPersister`, invoice, lines, approval) | Their own new rows | App-only (strict variant) | Typed system writers that create their own rows, not a person's generic path; app-only by design and unchanged |
| From-email submission (`SubmitInvoiceFromEmailOperation`) | The submission row | App-only (it takes the submission service's derived fields) | The same submission writer |
| Submit Invoice step A (`InvoiceSubmissionEndpoints.RefileBodyAsync`) | Not a family row: the source communication's regarding pair, re-filed as the caller | App-only | Outside this task (a communication, ebilling-r1's code); reported to the main session for an owner decision, not changed here |

## What changed

- `RecordTableRules`:
  - `PreWriteCheck` returns `PreWriteResult` (a refusal, or columns to set).
  - `PreWriteRow` carries the create's app-only readers (`Records`, `RecordTypes`).
  - `InvoiceFamilyRule` derives after the guard admits, with the generic variant. With no reader it fails closed
    (`read_failed`).
- `RecordCreationService`: step 5½ merges the rules' columns into the item (`OwnedChildWrite.WithColumnsSet`) before step 6.
  - The readers: the record's name and number as the caller (fix round 5, rule 12:
    `CallerDisplayRecords(_entities, CallerRecordDisplay(user))`), and the catalog row by
    `ICommunicationDataverseService.QueryRecordTypeRefAsync`, from a scope (the dedicated writers' catalog reader).
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
| `CallerDisplayRecords` (push) and the `records` parameter of `DeriveForGenericUpdateAsync` | `CallerDisplayReader` (045b, the push's display read) | Reused: wrapped as the derive's record reader; only the exists-check on a 403 or 404 is new | The push writes app-only display values that the 045b planner, on the same path, reads as the caller |
| H1: the widened table set of `CheckGenericUpdateAsync` / `IsUpdateOnlyTarget` | The same guard, invoice and line only | Extended in place: the condition reads `CoreRecordLookups`; no new member | A generic update can give an approval, external identifier, correction or submission a second core record, or re-create a deleted one by upsert |
| `InvoiceFamilyCoreRecordGuard.NamesTheSameRecord` (fix round 5) | `Check`'s `Regarding`; 045b planner rule 4 | A pure comparison of two `Check` answers, shared by the app-only derive and the caller-path derive so the two agree | A write that re-names the record a row already names re-derives, and on a run that cannot read the record clears its name and number every time |
| `OwnedChildWrite.CallerRecordDisplay` (fix round 5) | The record reader inside `CallerReaders` (045b) | Extracted from it unchanged; `CallerReaders` now calls it, so there is one caller display read | The create reads the name and number app-only and copies them into a row a caller who may not read the record can read |
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
- Third round (fix round 3 and H1), tests new or changed:
  - `InvoiceFamilyGenericUpdatePathTests.AllFamilyTables.cs` (H1), on the playbook update, the output orchestrator and the
    push, for each of the approval, the external identifier, the correction and the submission:
    - must fire: a second core record is refused; a missing row is refused `record_not_found` and never created;
    - must not fire: a write touching no core column is written once, update-only; a core the row already names is
      written; an external identifier given an invoice is written;
    - `IsUpdateOnlyTarget` covers all six tables.
  - `InvoiceFamilyGenericUpdatePathTests.ResolverFields.cs`:
    - the push with a caller who may not read the new matter: type, id and URL written, name and number cleared;
    - an approval moved to another matter (three paths);
    - an external identifier moved to invoice-only;
    - a push that sends the pair and moves the core lookup gets the derived pair;
    - a cancellation during the derive is rethrown, with nothing written;
    - a stored row that does not exist derives nothing (three variants).
  - `RecordCreationServiceInvoiceFamilyRuleSeamTests`: a catalog or matter read fault is 500 `read_failed`, and the fault's
    text is not in the response.
  - `RecordCreationServiceMappingSeamTests.RestrictedSource`: a stripped invoice is created with its matter's resolver fields,
    set again on the rebuilt row.
  - `tests/unit/domain/Dataverse/InvoiceFamilyPairTablesTests.cs`: no family table is a BFF-managed pair table.
  - The D-96 must-not-fire also pins one catalog query and two queries in all.
  - `InvoiceFamilyCoreRecordGuardLineSplitTests`: the `IsUpdateOnlyTarget` row for the submission is now true (H1).
- Fail-then-pass (third round):
  - Run A switched off H1, rule 7, rule 8, rule 9, the rebuilt row's re-set and the cancellation rethrow. 43 of 61 failed.
    The 18 that pass both ways are:
    - the two submission rows refused as an access column;
    - the 12 "core already named" rows;
    - the 3 "external identifier given an invoice" rows;
    - the pair-table pin.
  - Run B switched off the app-only derive. All 5 failed: the approval moves on 3 paths, the external identifier to
    invoice-only, and the pushed pair replaced.
  - Restored: all pass.
- Fourth round (fix round 4), tests new or changed:
  - on both handlers, a run as a person:
    - who may not read the new matter: type, id and URL written, name and number cleared;
    - who may read it: the person's name and number written;
  - on the push:
    - a caller read that times out, or no caller query: `read_failed`, nothing written;
    - a 403 on a matter that does not exist: `record_not_found`;
  - H1 on the two handlers, a run as a person: a missing row is `read_failed` and never created;
  - H1 delete race on every path, table and identity: the guard finds the row, the update-only write does not, and the
    guard's code is answered (never the upsert);
  - the "core already named" theory now lists only the rows whose core lookup is writable (no empty passes);
  - the no-person rows of the handler tests expect name and number cleared (rule 7).
- Fail-then-pass (fourth round): the switch-offs were the handlers' person display read, the push's fault branch and
  exists-check, the guard's person mapping, and `IsUpdateOnlyTarget` narrowed. With them, 44 failed. The 4 that passed are
  the push rows of the moved-record tests (unchanged on the push) and one unrelated test the filter matched. Restored: all
  pass.
- Fifth round (fix round 5), tests new or changed:
  - rule 10, must fire: a write that re-names the matter the row already names leaves the name and number out of the
    payload and reads nothing for the derive — the two handlers with no person and with a person, the push for a caller
    who may and may not read the matter (6), and the child-record PATCH and chat update tool (2);
  - rule 10, payload pins: the "core already named" theory and the "external identifier given an invoice" theory now
    assert that none of the five is in the payload;
  - rule 11: a row given a second core record between the guard's read and the derive's is refused
    `invoice_family_multiple_core_records` and nothing is written, on all three paths;
  - rule 12, create seam: a caller who may append to but not read the matter gets type, id and URL, name and number
    cleared, and the app-only exists-check ran; a caller who may read it gets the values they read (not the app's); the
    read-fault theory now faults the caller's read and, separately, the exists-check;
  - existing create seam tests now script the matter's name and number as the caller reads them;
  - the "blank name or number" theory runs the two handlers as a person who may read the matter, with both gaps on every
    path (6 rows, was 3), so "number blank, name kept" is really tested;
  - the output handler fixture names entity sets from metadata, as the playbook fixture already did.
- Fail-then-pass (fifth round): `NamesTheSameRecord` forced false, the K2 refusal returned to `null`, and step 5½'s record
  reader returned to the app-only read. 32 failed. The 2 that passed are the catalog-fault row (unrelated to this round) and
  the exists-check-fault row (an app-only fault answers `read_failed` either way). Restored: all pass.
- ebilling-r1's proof (minus `Api.Office.`), fifth round: Finance 7280 passed, 0 failed, 4 skipped; ProcedureAssignment 392
  passed; InvoiceFamily 514 passed (fourth round 502).
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `dotnet test tests/Spaarke.ArchTests/`: 2536 passed.
- `Sprk.Bff.Api.Tests` filtered run: 9477 passed, 0 failed, 3 skipped (live tests), run alone (fifth round; fourth round
  9460). A wider run of three classes, made earlier in the round, failed once on
  `CreatePath_ANamedSourceMappedCreate_WithDelayedReads_OverlapsTheMappingReads_AndAnswersAsTheSequentialRun` (a timing bound
  on a `sprk_event` create, which step 5½ does not touch); it passed on three reruns alone and in the full filtered run.
  - Filter: Invoice | RecordTableRules | PreWrite | RecordCreation | OwnedChildWrite | UpdatePlanner | ChildRecord |
    RegardingPair | SecureChildOwnership | DataverseUpdateRecordHandler | BillingExport | Refusal | ActionSeam | FieldMapping |
    UpdateRecordAction | DataverseUpdateHandler | AiTool | InvoiceSubmission, minus `Api.Office.`.
  - An earlier run of the same filter, made while ArchTests ran alongside, had 1 failure. Its name was not captured, and it
    did not reproduce.
- `dotnet format whitespace --verify-no-changes` on the touched files: clean.

## Known limits (K)

- K1: an update that changes a core lookup costs up to four more reads, plus any metadata read the record reader needs:
  - the stored core lookups and the catalog row: as the caller on a caller path, app-only on an app-only path;
  - the record's name and number: as the caller on the create, on a caller path and on the push, as the person on the two handlers, and
    not read at all on a handler run with no person (they are cleared);
  - after a 403 or 404 on that read, one app-only read to check that the record exists.
- K2: the derive's stored-lookup read and the guard's own are separate reads. A row changed between them so that the
  derive's `Check` refuses it is refused with that code (rule 11). A change the derive's `Check` still admits is written
  with the five of the record the derive's read decided; the guard's other questions (D-96, supersedes) were asked on the
  guard's read. This is the same window as 045b's planner.
- K3: when a derive failure and a later refusal both apply on an update, the derive failure is answered. The later refusals
  are the D-96 lines, supersedes, the work-assignment cycle and `RefileAsync`'s shape refusals. Each alone answers as before.
- K4 (replaced by option (b)): a blank name or number is written as a clear on the generic paths; the submission writers still
  refuse it.
- K5: the derived pair type's catalog row is not asked for AppendTo, the same as the dedicated writers.
- K6: on the create, the app-only catalog reader is resolved per call from a DI scope. `UpdateRecordActionCore` also takes
  its `IGenericEntityService` from a scope, and only for a family table.
- K7: on the app-only paths the derive runs for every family table whose core lookup the path lets through:
  - the invoice, the line, the approval, the external identifier and the correction on all three paths;
  - the submission on the push and on a confirmed proposal.
  A submission's core lookups are access columns on the playbook update and the output orchestrator, so they are refused
  there before the guard or the derive is reached.
- K8: a caller update path (child-record PATCH, chat update tool) reads the new core record as the caller for its name and
  number, so it needs Read on that record, where a create needs only AppendTo. A caller without Read is refused (fail
  closed).
- K9: before deploy, each environment's catalog must hold an active `sprk_recordtype_ref` row with
  `sprk_regardingrecordnumberfield` set, for matter, project, work assignment and invoice. Without one, a create or a
  core-lookup move of a family row refuses with `invoice_family_resolver_fields_incomplete`.
- K10: model-driven form edits of a family row's core lookup do not pass through these paths and are not covered.
- K11: a pair-only write that disagrees with the row's core lookup is a separate owner decision (#2772, DI-029).
- K12: a new core record deleted while a push runs gives `record_not_found` for that child (a delete race only).
- K13: on a handler run as a person, a new core record that does not exist answers `read_failed`, while one that exists but
  the person may not read is written (name and number cleared). The two outcomes differ; Dataverse's bind of the lookup
  shows the same difference.
- K14: on a handler run with no person, the name and number of a moved family row are cleared, as 045b clears a moved
  pair's display with no person. Only the type, id and URL describe the record until a write that can read it. A run that
  re-names the record the row already names leaves them as they are (rule 10).
- K15: a re-name of the same record never refreshes stale name and number values (rule 10, as 045b rule 4). A row whose
  record was renamed since keeps the old name until a write moves it.
- K16: on the create, a caller who may append to the matter but not read it costs one more app-only read (the exists-check),
  plus the caller's metadata read of the record's entity set.
