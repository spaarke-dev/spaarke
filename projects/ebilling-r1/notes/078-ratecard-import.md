# Task 078 - Rate card and timekeeper import and publish (FR-29, FR-08)

Rigor: STANDARD in the POML, run as FULL (the task writes .cs and tests/**). Model: Sonnet 5.5 @ high.

## 1. What shipped

Three routes under `/api/finance/ratecards`, all `RequireAuthorization()`, each with its OWN `AddFinanceAuthorizationFilter` then `.RequireEbillingEnabled()` (the flag never replaces authorization; ADR-008, ADR-018):

| Route | Needs (caller's Dataverse table privilege, D-76, fail closed) | Result |
|---|---|---|
| `GET /import-template` | Create on `sprk_ratecard` | `.xlsx` generated from code (no template file, no package) |
| `POST /import?firmId&name[&matterId\|projectId\|workAssignmentId]` (multipart `file`) | Create on `sprk_ratecard`, `sprk_ratecardrate`, `sprk_Timekeeper`; `finance.read` Record check on the core record if one is named | 201 Draft card; 400 with `errors:[{sheet,row,column,message}]`; 409 owner refused (`ProblemDetailsHelper.RecordOwnerRefused`); 413 `ratecard.file_too_large`; 422 `ratecard.result_truncated`; 503 `ratecard.container_not_configured`; 500 `ratecard.import_failed` |
| `POST /{id}/publish` | Write on `sprk_ratecard` and `sprk_Timekeeper` | 200; 400 `ratecard.publish_invalid` + `errors`; 404 `ratecard.not_found`; 409 `ratecard.not_draft`; 422 `ratecard.result_truncated` |

Files: `Api/Finance/RateCardImportEndpoints.cs`, `Services/Finance/RateCardImportService.cs` (concrete, `AddScoped` in `FinanceModule`, no interface, ADR-010), `Services/Finance/RateCardWorkbook.cs` (OpenXml 3.5.1 already referenced; NFR-12). Two shared append lines: `FinanceModule.cs`, `EndpointMappingExtensions.cs`.

Behaviour:
- **All or nothing.** Every row is validated first (required cells, classification label or LEDES code, ISO date or Excel serial, To not before From, contact email -> one active contact, rate has a timekeeper XOR a classification, amounts 0..1e9 with at most 2 decimals, approved <= rack, currency by ISO code, overlap). Any problem -> 400 listing ALL problems, nothing written (no row, no file).
- **Writes**, only after the owner resolves (`IRecordOwnershipResolver`, `owner.ApplyTo`): upload to the Procedure Library, unlinked document row, Pending timekeepers (a name already on the firm roster is reused), the Draft card (`sprk_sourcedocument` = that document, scoped to the core record if given), the rates. Any failure, **including a client cancel**, deletes the created rows in reverse order and the uploaded file, then answers 500 `ratecard.import_failed`.
- **Nothing is in force before publish.** Card is created Draft (1), timekeepers Pending (659490000). Publish re-checks overlap on the STORED rates (a form edit after import can break it), approves (`BulkUpdateAsync`, one transaction) the card firm's Pending timekeepers that the rates name, then sets the card Published (659490000) LAST, so a retry after a partial failure completes.
- **Overlap rule:** inclusive ranges, empty end = open-ended, same timekeeper OR same classification on one card.

## 2. Deviations and decisions (read before the next task)

1. **Metadata is in the query string, not multipart fields** (`firmId`, `name`, core record id). The sync authorization resolver and the handler then read the same source (Finance doctrine, task 130 defect C8). The POML said "multipart: workbook, firm, ...".
2. **Admin = D-76 caller table privileges**, not a role check. 076 had not landed; privilege names were confirmed read-only against dev (`prvCreatesprk_ratecard`, `prvCreatesprk_ratecardrate`, `prvCreatesprk_Timekeeper`, `prvWritesprk_ratecard`, `prvWritesprk_Timekeeper`; mind the `sprk_Timekeeper` capital T). No role or privilege was changed.
3. **No schema link ties a timekeeper to its card**, so "the card's imported timekeepers" is defined as: the firm's Pending timekeepers named by the card's rates. To keep that exact, import refuses a NEW timekeeper that has no rate row.
4. **Example rows** in the template start with `(example)` and are skipped; an `Instructions` sheet is never read.
5. **DEF-015:** the source workbook is stored in the Procedure Library via the existing `ProcedureLibraryContainerId`, and its `sprk_document` row is created UNLINKED (no matter, project or work assignment lookup), asserted by a test even when the card itself is scoped to a matter. No new storage model.
6. **Reserved shared guards touched** (ArchTests, edits are additive entries): `RouteAuthorizationGuardTests.Ledger.cs` (new GovernedFile, `ExpectedEndpointFileCount` 125 -> 126 with history line, `RequireEbillingEnabled` classified in `NotAuthorizationForms` - it is the first route to use the kill switch), `SpeWriteSinkContainerProvenanceGuardTests.cs` (two `ServerDerivedConfig` entries: `UploadSmallAsync` and the compensating `DeleteFileAsync`, both on `FinanceOptions.ProcedureLibraryContainerId`). The SPE flat-path guard is satisfied by `SpeUploadPath.SanitizeFileName` into a local named `uploadPath`.
7. **UAT:** there is no UI entry point for 078 (077's Rule Review page does not cover rate cards). Admins call the routes (template, import, publish) or use a later UI.

## 3. Verification

- Build: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors.
- Tests (this task): `RateCardImportServiceTests` + `RateCardImportEndpointsTests` = **72 pass / 0 fail** (the real `MapRateCardImportEndpoints` and filters: 401, 403 per route and per privilege, 503 `ebilling.disabled` on every route).
- ArchTests: **1052 pass / 0 fail**. ADR-010 1:1 interface ceiling unchanged at 162 (no interface added).
- Fail-before / pass-after, by mutating the source and rerunning the 67-72 tests, each mutant killed: import writes despite invalid rows (9 fail), overlap never true (6), publish skips overlap re-check (2), approved above rack allowed (2), publish route without its auth filter (4), import route without the flag filter (1), card created Published (2), timekeepers created Approved (2), no compensation (1), document linked to the core record (1 - needed a new assertion in the core-record test, added), publish ignores a truncated rate read (1), import ignores a truncated roster read (1), no compensation on cancel (1), unpacked-size check removed (1), corrupt sheet XML becomes a 500 (1).
- Publish size (Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, PDBs included, fresh short-path copies `C:\wt078b` / `C:\wt078h`, 192 files each): base **38,991,706 B** -> head **39,031,826 B**, delta **+40,120 B** (+0.038 MB; 37.22 MB total). The base is the branch point `70bfb7586` (same convention as notes 016/040/042), not the master tip. No PackageReference change; `dotnet list package --vulnerable --include-transitive`: none.
- Placement (root section 10): BFF, B1 caller identity. Section 11 three-question answers are in the POML `<justification>`.

## 4. Review findings (Step 9.5, self-run; the independent reviewer agent could not be launched - concurrent-subagent cap)

Fixed in scope (F-class):
- F1 client cancel mid-import left half-written Draft rows and an orphan file (compensation was skipped for `OperationCanceledException`). Now compensates and rethrows.
- F1 a zip that opens but has corrupt sheet XML answered 500 instead of 400. Now a bad-file error.
- F2 no unpacked-size guard on the workbook (zip bomb into the in-memory DOM). Now refused above 64 MB declared unpacked size.
- F1 `IGenericEntityService.RetrieveMultipleAsync` does not page (5,000-row cap): publish would silently skip rates past the cap and import would miss roster names (duplicates). Both now fail closed with 422 `ratecard.result_truncated`.
- ADR-007: no Microsoft.Graph type is referenced by the service. ADR-001/008/010/019/002/003 checked: Minimal API group, per-route filters, concrete DI, ProblemDetails with `errorCode`, no plugin, SPE container server-derived.

## 5. Known limits / deferred (none blocks the task)

- **K: declared-size zip guard only.** A zip that lies about entry sizes is not caught by the pre-check; the 5,000-row cap and admin-only privilege bound the exposure. Streaming the sheet instead of loading its DOM would close it.
- **K: crash between a create and its cleanup** (process death, not an exception) can leave inert Draft rows (and an orphan file). They are Draft/Pending and in force nowhere; the import is not transactional because `IGenericEntityService` has no multi-entity transaction.
- **K: concurrent imports for one firm** can create two timekeepers of the same name (no alternate key on name+firm).
- **K: a Draft card with zero rates** (rates deleted on the form) can be published; it has no effect. Not refused.
- **K: DEF-014** - an app-only read of the source row is refused; the import reads core-record access as the caller via the filter, then writes as the app.
- **K: classification list is hard-coded** (the 20 LEDES options of `sprk_timekeeperrole`); it must follow that global choice if it changes.
- **K: more than 5,000 timekeeper or rate rows per workbook** are refused with "split the import".

## 5. Review fixes (2026-10-16)

Three confirmed F1 findings and two K1 items, fixed in `RateCardImportService.cs` and `RateCardImportEndpoints.cs`. Regression tests: `tests/integration/regression/Ebilling/RateCardImportRosterRenewalAndPublishChunkingTests.cs` and `RateCardImportMalformedMultipartReturns400Tests.cs`.

- **F1-2, roster reuse by name discarded the sheet's values (broke the annual renewal).** The roster read now takes `sprk_classification`, `sprk_effectivefrom`, `sprk_effectiveto` and `sprk_contact`. A Timekeepers row is matched against the firm's stored rows of that name:
  - **No stored row overlaps the sheet's period** (the 2027 card for Jane Doe whose stored row ends 2026-12-31; a promotion starting after the stored row ends): a NEW Pending row is created for the new period. The rates of the sheet point at it, and publish approves it as before. Needs the 046 matcher to pick the row eligible on the line date; verified and tested (see note 046 section 14).
  - **One stored row overlaps and every value matches** (Effective From, Effective To, classification; contact when the sheet names one): the row is reused, as before.
  - **One stored row overlaps and a value differs:** a per-row 400 in the existing `errors:[{sheet,row,column,message}]` shape, naming the difference: "Jane Doe is on the roster with Effective To 2026-12-31 and classification Associate; this sheet says Effective To 2027-12-31 and classification Partner. Correct the sheet or the roster row, or start the sheet row after the roster row ends." The column is the first differing one.
  - **More than one stored row overlaps:** the existing "More than one timekeeper named ..." refusal (now "... for these dates").
  - **Decision:** a blank Contact Email cell says nothing about the contact, so it never differs from a stored contact; a blank Effective To means open-ended and does differ from a stored end date.
  - **Existing test changed:** `Import_ReusesTimekeepersAlreadyOnTheFirmRoster_...` seeded a roster row with no values; it now seeds values equal to the sheet row (the reuse-when-equal case).
  - **Limit.** A rate row that names a roster-only timekeeper (not on the Timekeepers sheet) whose name has several stored rows is still refused as ambiguous; list the person on the Timekeepers sheet with the period to say which row. Unchanged behaviour.
- **F1-3, publish approved every Pending timekeeper in one `BulkUpdateAsync` (one ExecuteTransaction, Dataverse cap 1,000 requests); an import allows 5,000 rows.** Approvals now go in chunks of at most `MaxApprovalsPerTransaction` = 1,000. Each chunk is atomic, the whole is not; safe to retry because approval is idempotent (only Pending rows are selected) and the card is set Published after the last chunk. Tests: 1,000 (one call), 1,001 (1,000 + 1), 2,500 (1,000 + 1,000 + 500), card last by call order, and a failed second chunk leaves the card Draft and a retry approves only the remaining 1,500.
- **K1, `catch (OperationCanceledException)`** now has `when (ct.IsCancellationRequested)`: a Graph or HTTP timeout (token not cancelled) falls to the general catch, which also cleans up, and answers 500 `ratecard.import_failed`. The existing client-cancel test now really cancels its token.
- **K1, `ReadFormAsync`** in the import endpoint catches `InvalidDataException` and `BadHttpRequestException` and answers 400 `ratecard.import_invalid` ("The request body is not valid multipart/form-data."), as `ChatDocumentEndpoints` does; the parser's message is not returned.
- **Not changed, to check:** the publish read of the card's timekeepers uses one `In` condition over every timekeeper id the rates name (up to 5,000); whether Dataverse accepts that many `In` values in one query was not verified here.

rework: fix-round-1 task 078

**In-list batching (main session, 2026-10-16).** The publish read of a card's timekeepers and the import read of contacts by email each put every value in one `ConditionOperator.In`. That is up to 5,000 SQL parameters, and SQL Server refuses more than about 2,100. Both reads now run in batches of `MaxInValues` = 500.

Regression test: `Publish_ReadsTheCardsTimekeepersInBatchesOfAtMost500InValues`. It fails with batching disabled and passes with it on.
