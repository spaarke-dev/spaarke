# 164a-1 — create-only refusal of the generic `sprk_billingevent` create

Piece 164a-1 of task 164 (Add line). Scope: uac-r2's approved conditions 1–3 for Q6 (POML criteria "One owner of a NEW line"
and "uac-r2 conditions on the create-only refusal"). Branch `task/ebilling-164a1`, based on `work/ebilling-r1` at `2377cf643a`.

## 1. What changed

- **Registry.** `RecordTableRule.GenericCreateOnlyRefused` (new flag) is set on `sprk_billingevent` only. A derived
  `GenericCreateRefused => CreatedOnlyByItsWriter || GenericCreateOnlyRefused` is what every generic create site reads.
  `CreatedOnlyByItsWriter` is unchanged and stays `false` on the line, so the update and delete refusals do not fire for it.
- **Create sites (read `GenericCreateRefused`).**
  - `RecordCreationService.CreateAsync` (start of the pipeline): `record_creation.table_unsupported`.
  - `DataverseCreateRecordHandler.IsCreatedOnlyByItsWriter` (used by `ValidateChat` and `ExecuteChatAsync`): the chat tool's
    hard block (`ValidationFailed`), with an invoice-line message (`CreatedOnlyByItsWriterCreateBlockedMessage` branches on the
    table, as `ServerWrittenInvoiceFamilyCreateBlockedMessage` does for the external identifier).
- **Child-record route.** `sprk_billingevent` left `ChildRecordEndpoints.CreateTables` (400 `child_record.table_unsupported`,
  before the service is asked) and the route summary.
- **Ribbon.** `sprk_secure_child_ribbon.js` (and its SpaarkeMaster mirror, kept in parity per #1651): the "New Billing Event"
  create (`CREATE_THEN_OPEN` entry and its invoice core-record bind helper) is removed. `sprk_billingevent` stays in `TABLES`,
  so its platform "+ New" is still hidden by the enable rule; a "New Billing Event" click now sends nothing and says
  "Nothing was created."
- **Guards.** `AccessTableReaderScanTests` (the `sprk_billingevent` reason), the `RecordTableRules` remark,
  `RecordTableRulesRegistryGuardTests` (no fixed create list may name a `GenericCreateRefused` table; must-fire now includes the
  line), `WriterOnlyTablesUpdateGuardTests` / `WriterOnlyTablesDeleteGuardTests` (the chat create refuses every table the
  update/delete refuses, plus the create-only ones; was strict equality), `SecureChildNewCommandAgreementTests` (a named
  "served with no ribbon create" exception for the line, valid only while the BFF refuses its generic create),
  `ChildRecordCreateTablesAgreementTests` (ribbon-only list), the route census, and `ExternalInvoiceVisibilityGuardTests`
  (`CreatedOnlyByItsWriterCreateBlockedMessage` listed in `NotInvoiceSurfaces`: it now names `sprk_billingevent` for its own
  refusal text and reads no row, as `ServerWrittenInvoiceFamilyCreateBlockedMessage` already is).
- **Replaced tests.** The FR-05 guard's line-create cases on the route and the chat tool
  (`SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs`) can no longer reach the guard. The four route cases became one
  refusal theory (`ChildCreate_AnInvoiceLine_IsRefusedAsATableTheRouteDoesNotCreate`), and the two chat cases now assert the
  generic-create refusal. The guard's update cases are unchanged.

## 2. Classification of every `CreatedOnlyByItsWriter` reader (for the PR body)

| Reader | Kind | Reads after 164a-1 |
|---|---|---|
| `RecordCreationService.cs:182` | create | `GenericCreateRefused` |
| `DataverseCreateRecordHandler.cs:158` (`IsCreatedOnlyByItsWriter`, used at :309 validate and :392 execute) | create | `GenericCreateRefused` |
| `ChildRecordEndpoints.cs:670` (`UpdateAsync`, the browser re-file / PATCH core) | **update** | `CreatedOnlyByItsWriter` (unchanged) |
| `WorkProductRecordPersister.cs:189` (PATCHes one registry column) | **update**, does not create | unchanged |
| `OwnedChildWrite.cs:775` (re-file) | update | unchanged |
| `DataverseUpdateHandler.cs:114`, `UpdateRecordActionCore.cs:225`, `DataverseUpdateRecordHandler.cs:237`, `FieldMappingEndpoints.cs:702/1107` | update | unchanged (through `RefuseWriterOnlyTableUpdate`) |
| `DataverseDeleteRecordHandler.cs:117` | delete | unchanged (through `RefuseWriterOnlyTableDelete`) |

Deviation from the brief: the brief and the POML list `ChildRecordEndpoints:670` and `WorkProductRecordPersister:189` among
the create sites. Both are update paths (the first is `UpdateAsync`, the second PATCHes an existing row), so they keep the old
flag; switching them would have refused D-96's generic update of a line. The route's create site is its `CreateTables`
accept-list, which item 2 covers.

**Delete is unchanged**: D-96 does not need it, and changing it is out of this piece's scope.

**Other `RecordCreationService` callers** (`AnalysisEndpoints`, `EventEndpoints`, `ProvisionProjectEndpoint`,
`QuickCreateSourceAccessFilter`, `OfficeEndpoints` / `OfficeService`, `SecureChildReconciler`, `EmailDraftToolHandler`,
`ProjectMatterFiling`, `RecordThreadFiling`, `TrackingNumberAllocator`, `BudgetInquiryExecutor`, `DecisionRouteCores`):
none creates a billing event (the only mention is a comment in `OfficeService`). The typed line writers do not go through
the service.

## 3. Component justification (root CLAUDE.md §11) — `GenericCreateOnlyRefused` and `GenericCreateRefused`

1. **Existing.** `RecordTableRule.CreatedOnlyByItsWriter` (042c / 048) refuses create, update and delete together; the chat
   tool's `ServerWrittenInvoiceFamilyTables` is a hand-kept create-only set for four other invoice-family tables.
2. **Extension.** Setting `CreatedOnlyByItsWriter` on the line would also refuse its generic update and delete, which breaks
   D-96's supported detach / move / re-attach sequence (uac-r2 condition 1 forbids it). Adding the line to the chat's
   hand-kept set would leave `RecordCreationService` creating it. One more `bool` on the existing entry plus one derived property
   keeps one registry answer for every create site.
3. **Cost of doing nothing.** A browser, chat or playbook caller can create an invoice line outside its typed writers, so a new
   line has more than one server-side owner (ADR-002; Q6), and the approved-total and decision rules that assume a line came
   from extraction or Add line can be bypassed.

## 4. The refusal's log fields (for the PR; ontology's deploy check)

- **`RecordCreationService`** (any caller of the service): warning
  `[RECORD-CREATE] create of {Table} refused at {Step}: {Kind} {Code} (correlationId={CorrelationId})`, with
  `Table` = the trimmed, lower-cased table name (`sprk_billingevent`), `Step` = empty (no step began), `Kind` = `Invalid`,
  `Code` = `record_creation.table_unsupported`, and the request's correlation id. The table name is carried, so the check can
  filter `record_creation.table_unsupported` by `Table`.
- **Child-record route**: refused by its own accept-list before the service, as 400 `child_record.table_unsupported`
  (problem detail "'sprk_billingevent' records are not created here."). This path writes **no log line** and never reaches the
  service, so a filter on `record_creation.table_unsupported` will not see it.
- **Chat create tool**: execute logs `[dataverse.create_record][ADR-015] entity={Entity} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}`
  with `Entity` = `sprk_billingevent` and `Outcome` = `VALIDATION_FAILED`'s code; the validate leg refuses without a log line.

No person ids or field values are logged on any of the three.

## 5. Conflict note — field-mapping 049b

`ChildRecordEndpoints.CreateTables` and its comment (and the route summary) are also edited by field-mapping's 049b
(`secure=true` on the same route). Expect a textual conflict there: **whoever merges second resolves it**, keeping
`sprk_billingevent` out of `CreateTables`. `RecordTableRules.cs` is field-mapping code too (fm copied on the diff; uac-r2
reviews the PR).

## 6. Seed result

Seed: `GenericCreateRefused => CreatedOnlyByItsWriter` (the new flag dropped from the derived property), then restored.

- BFF tests (new 164a-1 tests, `RecordTableRulesTests`, `ChatServerWrittenTableBlockTests`): **13 failed / 143 passed** under the
  seed. They were the service seam theory (3 ways in), the service's word-for-word refusal, both chat execute cases, both
  chat validate cases, the chat create-only pin, the registry fact, and the two D-96 re-attach cases. The two D-96 cases fail
  on their precondition pin (`GenericCreateRefused` true); the update itself still went through. After the restore: **156 / 156**.
- ArchTests (`RecordTableRulesRegistryGuardTests`, `WriterOnlyTables*`, `SecureChildNewCommandAgreementTests`): **2 failed /
  25 passed** under the seed (`WriterOnlyGuard_MustFire_…`, `EveryServedTable_HasACreateSurface_…`). After the restore: **27 / 27**.
- The route theory (`ChildCreate_AnInvoiceLine_IsRefusedAsATableTheRouteDoesNotCreate`) does not read the flag; the route refuses
  through `CreateTables`. It was seeded separately against the old `ChildRecordEndpoints.cs`: 4 / 4 cases failed, along with the
  three `ChildRecordCreateTablesAgreementTests` and the census (8 failed). After the restore: 10 / 10.
- Ribbon (`secureChildRibbonScript.test.ts`) against the old script: 2 failed / 21 passed. With the new script: 23 / 23.

## 7. Open items (not done here)

- The SpaarkeMaster `Entities/sprk_BillingEvent/RibbonDiff.xml`, `Merge-SecureChildRibbon.ps1` and
  `Deploy-SecureChildNewCommands.ps1` still generate the `sprk.SecureChild.sprk_billingevent.New` button (not deployed). With
  this change a click on it creates nothing. Whether to drop the line from the served tables (and then hide its platform
  "+ New" outright, which `SecureChildNewCommandAgreementTests` would then require) belongs with 164c's hides and uac-r2.
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` still lists "billing event" in the child-record route's summary.
- The typed-writer must-not-fire tests (line decision, Add line, extraction still create, and not through the service) belong
  with the Add line service pieces (164a-2 / 164a-3).
