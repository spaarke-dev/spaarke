# Task 124 — Retire `Finance:ProcedureLibraryContainerId` (EB-D-163)

> Project: ebilling-r1. Branch `task/ebilling-124` from `d3e20a3ca8`. Rigor FULL. Pattern: 092's retirement of `Finance:EbillingOutputContainerId` (`092-submission-on-files.md`, "Retired key"). Also removes uac-r2's legacy ExternalVisibility "Procedure Library drive" hide step, under the six conditions the uac-r2 owner set (recorded per section below).

## 1. What changed

Procedure files are Stored Files in the Finance file store (116: `RuleSetService.cs:880/885`, `RateCardImportService.cs:343` write `StoredFilePurpose.ProcedureSource`; neither touches SPE or `sprk_document` services). The Procedure Library SPE container has no role left, so the key has no replacement.

| Area | File | Change |
|---|---|---|
| Options | `Configuration/FinanceOptions.cs` | Property, `ContainerIdPattern` and the DataAnnotations using removed; retirement comment beside 092's. A leftover setting binds to nothing. |
| Validator | `Configuration/FinanceOptionsValidator.cs` | Constructor, `IConfiguration` field, key constants and the collision check removed. Only `ValidateFileStore` remains. Parameterless; the registration (`AddSingleton<IValidateOptions<FinanceOptions>, FinanceOptionsValidator>`) is unchanged. |
| SPE ownership | `Infrastructure/DI/GraphModule.cs` | `StampContainerSettingKeys` is now `EmailProcessing:DefaultContainerId`, `Communication:ArchiveContainerId` and `OwnedContainerIdsKey`. |
| Declared unfiled home | `Infrastructure/Dataverse/RecordContainerResolver.DocumentPointer.cs`, `RecordContainerResolver.cs` | The #2151 declared-unfiled-home mechanism is removed (its only key was this one): `DeclaredUnfiledHomeContainerKeys`, `PointerDriveColumn`, `_declaredUnfiledHomes`, `DeclaredUnfiledHomesFrom`, `DeclaredUnfiledHomeOf`, the `depth == 0 && DeclaredUnfiledHomeOf(row)` branch, the `DeclaredHome` property and the three extra column appends. An unfiled document is `Unfiled` everywhere. |
| Visibility | `Infrastructure/ExternalAccess/ExternalVisibility.cs`, `Infrastructure/DI/ExternalAccessModule.cs` | `DriveColumn`, `Readers.ProcedureLibraryDriveId`, the step-4 library loop and `ExternalVisibilityReason.ProcedureLibraryDocument` removed. Step 4 is now only the task-188 comment. Every other step is unchanged. |
| Comments | `FinanceModule.cs`, `SecureChildLineage.cs`, `SecureChildReconciliationJob.cs`, `Api/Finance/RuleSetEndpoints.cs` | Comment/doc text only. |
| Template | `appsettings.template.json` | `_EbillingContainers_comment` and `ProcedureLibraryContainerId` lines deleted. |
| Docs | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-4 row) | The declared-home clause is replaced by a note that task 124 removed it. |

## 2. Before / after grep

`git grep -E "ProcedureLibraryContainerId|PROCEDURE_LIBRARY_CONTAINER"` over `src tests scripts infrastructure .github docs config .claude`.

Before (`d3e20a3ca8`), files and hit counts: `.claude/constraints/provisioning.md` 1, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` 1, `FinanceOptions.cs` 3, `FinanceOptionsValidator.cs` 2, `ExternalAccessModule.cs` 1, `GraphModule.cs` 1, `RecordContainerResolver.DocumentPointer.cs` 1, `ExternalVisibility.cs` 1, `appsettings.template.json` 2, `RefileFileRelocationTests.cs` 1, `RelocationTerminalAndHomeRuleTests.cs` 1, `SpeAppOnlyContainerIsolationTests.cs` 3, `FinanceContainerIdOptionsTests.cs` 4. Nothing under `scripts`, `infrastructure`, `.github`, `config`.

After: no live reader of the key anywhere. What remains is of two kinds only.

- Retirement comments: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:135`, `FinanceOptions.cs:38`, `FinanceOptionsValidator.cs:11`, `FinanceModule.cs:32`, `GraphModule.cs:24`, `RecordContainerResolver.DocumentPointer.cs:634`, `ExternalVisibilityTests.cs:255`.
- Must-not-fire controls that set a leftover key and assert it does nothing: `RefileFileRelocationTests.cs` (37, 329, 332, 349, 744), `RelocationTerminalAndHomeRuleTests.cs:314`, `SpeAppOnlyContainerIsolationTests.cs:916`, `FinanceContainerIdOptionsTests.cs` (15, 26, 52, 57).
- `.claude/constraints/provisioning.md:269`, not edited by this task (section 10).

The brief's "grep finds nothing" is therefore met for code and configuration, and not literally for comments and controls (section 11).

## 3. Condition 1 — per-environment evidence (verbatim from the main session, collected 2026-11-22)

- The App Service setting `Finance__ProcedureLibraryContainerId`, read by name only (no values printed), is **absent** on: `spaarke-bff-dev` (Spaarke Development); `sprk-acme-prod-api` and its `staging` slot (Spaarke Demo); `sprksharedprod-api` (Spaarke Shared Production); `sprk-legal-rules` (Spaarke Legal Rules Solution).
- The provisioning control-plane apps do not have it either.
- The BFF has no Key Vault configuration provider (`Program.cs` has no `AddAzureKeyVault`).
- `appsettings.template.json` is not published (csproj `CopyToPublishDirectory="Never"`).
- So the step's drive id is blank in every deployed environment, and the legacy step was already a no-op everywhere.
- Dev Dataverse (org `spaarkedev1`): `SELECT statecode, COUNT(...) FROM sprk_document WHERE sprk_graphdriveid = <dev Procedure Library container>` grouped by `statecode` returned **no rows** (0 active, 0 inactive).
- SPRK Power Platform 1 has no web apps.
- No non-dev Key Vault was read.

## 4. Condition 2 — the attachments argument

The legacy step hid a `sprk_document` row whose drive was the Procedure Library container, and its attachments (children) followed it. Dev holds 0 such documents, and no deployed environment has the setting that would make the step fire, so no library document exists in any environment and therefore no child of one exists. Nothing can be left exposed by removing the step.

Going forward, Procedure sources are Stored Files, not `sprk_document` rows: 116's census (dev, 2026-11-10) found 0 `sprk_sourcedocument` rows on `sprk_ruleset` and `sprk_ratecard`, and the writers named in section 1 use `StoredFilePurpose.ProcedureSource` only. A Procedure Stored File with its Procedure Version parent is refused to an external caller by the firm file gate (section 5). The test `ADocumentsDrive_IsNotRead_AndAnAttachmentFollowsItsParent` (section 8) pins that an attachment follows its parent's verdict with no drive input.

## 5. Condition 3 — the external route census and the replacement deny proof

### 5.1 Every external route that touches `sprk_storedfile` or the file store

Group `/api/v1/external` is `ExternalAccessEndpoints.cs:77`; the submission routes are mapped at `ExternalAccessEndpoints.cs:144` (`MapExternalInvoiceSubmissionEndpoints`). All paths below are under `src/server/api/Sprk.Bff.Api`.

| Route | Where | What it does with the file store |
|---|---|---|
| `POST /matters/{id}/invoice-submissions`, `/projects/{id}/...`, `/workassignments/{id}/...` | `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs:39/49/59` → `SubmitAsync` (:200) → `InvoiceSubmissionService` | Stages, and deletes, only the caller's own Invoice PDF or supporting file, parent the submission's invoice: `Services/Finance/Submission/InvoiceSubmissionService.cs:1150`, `:2168` (`StageAsync`), `:2335` (`DeleteStagedAsync`), `:288`/`:898` (`CanStageAsync`). A write, never a read of someone else's file. |
| `GET /invoice-submissions/{id}`, `GET /invoice-submissions`, `GET /invoice-submissions/firm-records` | `ExternalInvoiceSubmissionEndpoints.cs:69/80/91` | Submission rows only. No file bytes and no file listing. |
| `GET /invoices/{id}/files` | `ExternalInvoiceSubmissionEndpoints.cs:106` → `ExternalInvoiceSubmissionAuthorizationFilter.cs:526` | Lists via `StoredFileService.ReadFirmVisibleRowsForInvoiceAsync` (`Services/Finance/Files/StoredFileService.cs:947`: invoice parent, Invoice PDF purpose, firm-visible, Servable, no source), then re-checked per row by `IsFirmFile` (`ExternalInvoiceSubmissionAuthorizationFilter.cs:585`). |
| `GET /invoices/{id}/files/{fileId}/content` | `ExternalInvoiceSubmissionEndpoints.cs:122`, handler `GetInvoiceFileContentAsync` :169-191 → `StoredFileEndpoints.StreamServableAsync` | Streams only a file from the gate's set (the same `ReadFirmVisibleRowsForInvoiceAsync` + `IsFirmFile`). Any other id is the uniform 404. |
| `DELETE /documents/{id}` (review fix round 1) | `ExternalProjectDataEndpoints.cs:547` (`DeleteOwnDocument`) → `DocumentCheckoutService.DeleteAsync` (:844) → `CountStoredFileCopiesAsync` (`Services/DocumentCheckoutService.cs:1137-1140`); refusal at `ExternalProjectDataEndpoints.cs:4030-4034` | **Existence only**, for the caller's own document: an app-only OData count of the Stored Files that name the document as `sprk_sourcedocument` (a Submit Invoice copy), ids never returned. The route answers a generic "in use" refusal (`DocumentInUse`) with no count, no file and no reason. A Procedure source has no source document, so a Procedure Stored File can never be counted. No Stored File content, name, id or metadata reaches the caller. |
| (not a route) the visibility decision | `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs:72-75`, `:105`, `:613-622` | OData row reads of `sprk_storedfile` for the (a') decision. No bytes and nothing returned to the caller. |
| (not a route) table classification | `Services/Access/SecureChildLineage.cs:368`, `Services/Access/ExternalTeamShareSynchronizer.cs:277` | `sprk_storedfile` is a HardDeny table for external reach. |

Re-enumerated in review fix round 1, from the booted endpoint table, not from reading: 70 routes admit the Ciam scheme (the same 70 in Development and Production; `ExternalUserContextEndpoint`, `MeEntitlementsEndpoint`, `ExternalProjectDataEndpoints` (todos, documents, events, contacts, organizations, deletes), `ExternalMessageEndpoints`, `ExternalIntakeEndpoints`, `ExternalInvoiceSubmissionEndpoints`, `FirmOutcomeEndpoints`, `BillingGuidelinesEndpoints`, `ExternalNotificationFeedEndpoints`, `ContactGrantEndpoints`, `ExternalModuleDataEndpoints`, `ExternalRegistrationEndpoints`, `ExternalWorkforceLinkEndpoints`), every handler a named method. The call closure of each handler (5.3) reaches a file-store member or the `sprk_storedfile` table name only in: the three submission POST routes (staging, rows above), the two invoice file routes (the gate, above), `DELETE /documents/{id}` (the existence-only count above), and, for almost every route, six static tables that merely NAME the table (a HardDeny classification, the (a') decision's lineage map, and four deny or ownership lists; each is justified in the guard's allow-list). No other route reaches `StoredFileService`, `StoredFileBlobs`, `StoredFileEndpoints`, `StoredFileSchema` or a type classified `NotOnAnExternalRoute`.

What the guard cannot see (stated, not hidden): a table name built at run time, a call through a delegate or function field, reflection, endpoint filters, and calls inside packages. See 5.3 and the guard's class doc. This is why 5.1 says "the closure reaches", not "no route can".

### 5.2 The replacement deny proof (a unit/route test)

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalInvoiceFileRoutesTests.cs`, theory `ProcedureSourceFile_IsNeverListedOrServedToAnExternalCaller`: a Procedure Stored File (purpose ProcedureSource, parent a Procedure Version, even marked firm-visible) is seeded; for a contact and for a flagged systemuser, and with the Procedure Version id both different from and equal to the invoice id, the listing is empty, the content route returns the uniform 404 that an unknown file gets, no audit event is written and no byte is read. `IsFirmFile_RefusesAProcedureSourceRow_AndAdmitsTheInvoicePdf` pins the decision point directly, with the firm's own Invoice PDF as the passing control.

Note: the fake Dataverse in `StoredFileRig` ignores query criteria for Stored File queries, so what these tests exercise is the `IsFirmFile` re-check, which is the gate's authoritative check.

Review fix round 1 (F-E): the cases above carry two defects at once (Procedure purpose and a Procedure Version parent). `ProcedureSourceFile_EachCheckRefusesOnItsOwn` adds one-defect rows through the route, and `IsFirmFile_RefusesAProcedureSourceRow_AndAdmitsTheInvoicePdf` gained a parent-kind-only row; what each pins is in section 14 (mutation runs).

### 5.3 The guard (ArchTest, must-fire and must-not-fire)

`tests/Spaarke.ArchTests/ExternalFileStoreRouteCensusTests.cs` (`[Collection(BootedBffCollection.Name)]`; ADR-038 path #1, KEEP). It complements 093's namespace-limited guard (`ExternalDocumentNarrowingGuardTests`, unchanged) with a whole-assembly IL census.

| Test | Asserts |
|---|---|
| `EveryFileStoreCaller_IsInTheCensus` | Every caller of `StoredFileService`, `StoredFileBlobs` or `StoredFileEndpoints` in the assembly is classified with a reason: `ExternalGate`, `ExternalStagingWrite` or `NotOnAnExternalRoute` (17 callers). A new caller is unclassified and fails. |
| `Census_MustFireAndMustNotFire` | Compiled control fixtures: a direct call, a call via generic resolution (must fire) and a type with no file-store reference (must not fire). |
| `CiamRoutes_ReachTheFileStoreOnlyThroughTheFirmFileGate` | On the real endpoint table, every route admitting `AuthSchemes.Ciam` is checked twice. (a) Direct: the only handler METHOD that itself references the file store is `ExternalInvoiceSubmissionEndpoints::GetInvoiceFileContentAsync`, and no Ciam handler type is a `NotOnAnExternalRoute` caller or a file-store endpoint type. (b) **Closure (review fix round 1)**: see below. |
| `Routes_MustFireAndMustNotFire` | Controls for (a): a Ciam route whose handler is a file-store endpoint (`StoredFileEndpoints.GetContent`) is caught. |
| `Closure_MustFire` | Controls for (b): an indirect call (handler -> middle -> leaf -> `StoredFileService.ReadRowsForInvoiceAsync`); an indirect call into a service the census classifies `NotOnAnExternalRoute` (the real `ApExportService.GetBatchAsync`); a direct table-name query (a folded `$"{StoredFileSchema.EntitySet}?..."` and the inlined `StoredFileSchema.Entity`); the same through an interface (dispatch to the implementer); the table name in a static table (static constructor); a `StoredFileSchema` member and a `StoredFileSchema` field. All must fire. |
| `Closure_DepthBound_MustFire` | A closure still open at the depth bound fails (`CLOSURE NOT EXHAUSTED`); one that closes within it does not. |
| `Closure_MustNotFire_AndTheAllowListRatchets` | A handler with no file-store reference passes; a reach in the allow-list (handler-keyed, or shared `*`) or in the gate's pinned list passes; an entry keyed on another handler fails and is stale; an entry nothing reaches fails (`STALE`); an entry with no route or reason fails. |
| `RealGraph_HasTheExpectedShape` | The real call graph has more than 10,000 nodes and classifies `StoredFileService` and `ApExportService` as terminals; the gate content handler's closure closes within the bound and contains every entry of the pinned gate list. |

**The closure check (b).** For each distinct Ciam handler method (70 routes, 68 distinct handler methods), `Follow` walks the calls through the BFF assembly, breadth first, at most `MaxDepth` = 30 calls deep; a closure still open at the bound fails, it is never cut silently (at depth 60 no closure was open, so the bound has margin). The graph is 093's (`ExternalDocumentNarrowingGuardTests.Scan`, made `internal` with its call graph exposed; nothing else of 093 changed), widened with interface and virtual dispatch (a call to `IFoo.Bar` reaches every implementer's `Bar`), static constructors (a static field read runs the owner's cctor) and closed generic types. It fails when the closure reaches a member of the file store (`StoredFileService`, `StoredFileBlobs`, the `StoredFile*` endpoints and filters, `StoredFileSchema`) outside the gate's pinned list; a member of a type classified `NotOnAnExternalRoute`; a `StoredFileSchema` field; a type reference to or generic resolution of any of those; or any string literal containing `sprk_storedfile` (the compiler inlines `StoredFileSchema.Entity` / `.EntitySet` and folds interpolated OData urls, so the literal is what the IL carries). Data shapes in the store's namespace (rows, specs, results, id helpers) are not behaviour and do not count.

**Gate list** (the only reach the guard accepts without an allow-list entry): `GetInvoiceFileContentAsync` -> `StoredFileEndpoints.StreamServableAsync`, `.SafeContentType`, `StoredFileAccessFilter.ScanPending`.

**Allow-list entries** (each with route and reason in the guard; the list only shrinks, an entry no longer reached fails):

| Handler | Reached member | Why it exposes no Stored File content or metadata |
|---|---|---|
| `SubmitAsync` (3 POST invoice-submissions routes) | `InvoiceSubmissionService.WriteAsync` -> `StoredFileService.StageAsync` | Stages the caller's own upload; a write that lists and returns no file. |
| same | `InvoiceSubmissionService.SubmitAsync` -> `StoredFileService.CanStageAsync` | Whether the store is configured; reads no row and no file. |
| same | `InvoiceSubmissionService.CompensateAsync` -> `StoredFileService.DeleteStagedAsync` | Deletes the staged file of the same request, by the id that request staged. |
| same | `InvoiceSubmissionService.UploadName` -> `StoredFileSchema.NameSegmentOf` | A pure string sanitiser for the upload's display name. |
| `DeleteOwnDocument` (`DELETE /documents/{id}`) | `DocumentCheckoutService.CountStoredFileCopiesAsync` (table-name literal) | Existence-only count; the caller gets a generic "in use" refusal (5.1). |
| any (shared `*`) | `SecureChildLineage`, `ExternalInvoiceVisibility`, `RecordOwnershipResolver`, `RecordCreatorPerson`, `InheritedAccessPermission`, `ExternalTeamShareSynchronizer` static constructors | Static tables that name the table (a HardDeny classification, the visibility decision's lineage map, deny and ownership lists); none is a query. |

**Known limits of the guard (kept, stated in its class doc).** (1) A table name built at run time (taken from the request, concatenated pieces the compiler could not fold, read from configuration) is not a literal; routes that take a table name from the request are governed by the table-classification guards, not by this one. (2) A call through a delegate or function field (a seam such as `DocumentNarrower`) has no call edge; those seams are pinned by 093's guard. (3) Reflection and `dynamic`. (4) Endpoint filters and middleware are not followed (the firm file gate is a filter: pinned by 093 and `ExternalInvoiceFileRoutesTests`). (5) The graph is by name: overloads and a method's lambdas merge, which can over-report (an allow-list entry is then needed) but not under-report on that account. (6) Calls into packages end the walk. (7) A generic virtual method, or an explicit implementation under a renamed member, is not followed. The guard claims no more than this.

### 5.4 The deleted file, and what was wrongly deleted with it (corrected in review fix round 1)

`tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs` is deleted. It held `ProcedureLibraryPointerTests`, `ProcedureLibraryExternalDownloadTests` and `CiamSearchRouteTests`. The first version of this note said the first two "proved a property of a container that no longer exists". That was wrong. They never set the retired key: they prove, with the real pointer check and through the real `/api/v1/external/.../documents/{id}/content` route, that a document whose file pointer is in a container no record derives gets 409 `DocumentStorageUnverifiedCode` and no SPE read happens (both rules, interim and strict), with a derived-container control. That is a general rule of the external download path, and it holds with the Procedure Library gone. They are restored with logic and controls unchanged, renamed for the rule: `tests/integration/auth/ExternalAccess/ExternalDownloadUnderivedContainerDenyTests.cs` (`ExternalDownloadUnderivedContainerPointerTests`, `ExternalDownloadUnderivedContainerDenyTests`; only the container id and wording that named the Procedure Library changed). `CiamSearchRouteTests` is a general route fact, kept unchanged in logic in its own file `CiamSearchRouteTests.cs`; its DEF-015 known-limit assertion text, dropped in the move, is restored.

## 6. Condition 4 — the reason-name grep

`git grep -i ProcedureLibraryDocument` over the whole tree after the change: **zero hits**. Before the change the name existed only in `ExternalVisibility.cs`, `ExternalVisibilityTests.cs` and comments. Searched outside code with the same name: `*.kql`, `*.json`, `*.bicep`, `*.md`, `*.yml`, `*.yaml`, `*.ps1`, `*.sh`, `*.txt`, `*.csv` (docs, KQL/alert queries, runbooks, scripts, infrastructure): no hit in any. `ProcedureLibraryDriveId` appears only in `projects/unified-access-control-r2/notes/task-191b-external-visibility-predicate.md:78` (K4), a project note that records history and is not edited here.

The ExternalVisibility.cs comments (class doc, the step-4 line, the enum) were updated to drop the library step.

## 7. Condition 5 — nothing else in ExternalVisibility moved

Task 188's DocumentNarrowing full-only pass and every other ExternalVisibility step are unchanged (diff of `ExternalVisibility.cs` touches only `DriveColumn`, the `Readers` member and its param doc, the step-4 loop, the `columns.Add(DriveColumn)` and the enum member). Existing ExternalVisibility tests other than the library ones pass unmodified. Tests I modified in that file:

- Deleted `ALegacyProcedureLibraryDocument_IsHidden_ByItsDrive`, `WithNoProcedureLibraryConfigured_NoDocumentIsOne`, `AnAttachmentUnderAProcedureLibraryDocument_IsHidden` (the library ones).
- Added `ADocumentsDrive_IsNotRead_AndAnAttachmentFollowsItsParent`.
- Removed the `Library` const and `LibraryDrive` property, and the `LibraryDrive` argument in the world's `Readers()` helper (the `Readers` record lost that member; this is the only edit to a non-library test's setup).

## 8. Tests deleted, modified and added

| File | Change | Why |
|---|---|---|
| `tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs` | Deleted (its two general deny tests restored in fix round 1 as `ExternalDownloadUnderivedContainerDenyTests.cs`) | 5.4 |
| `tests/integration/auth/ExternalAccess/CiamSearchRouteTests.cs` | New file, moved content | 5.4 |
| `tests/unit/.../Infrastructure/ExternalAccess/ExternalVisibilityTests.cs` | 3 deleted, 1 added, helper arg removed | Section 7 |
| `tests/unit/.../OptionsValidation/FinanceContainerIdOptionsTests.cs` | Rewritten | Section 9 |
| `tests/integration/data-mutation/DocumentContainer/RefileFileRelocationTests.cs` | Deleted the five declared-home tests (served under strict, interim-rule served, without the bound item, blank key, 092's declared-home test) and the `EbillingOutputKey` const; added three leftover-key must-not-fire tests; renamed the unset-key derivation test; converted the job test to `TheJob_ALeftoverProcedureLibraryKey_DeclaresNoHome_TheDocumentIsReportedAndLeft` | The mechanism they tested is removed; the replacements prove a leftover key does nothing |
| `tests/integration/data-mutation/DocumentContainer/RelocationTerminalAndHomeRuleTests.cs` | A4 tests become three leftover-key controls (`Unfiled`, `Allows(...)`, `PrimaryContainer`); `DeclaredHome` assertions removed | Same |
| `tests/integration/tenant/Spe/SpeAppOnlyContainerIsolationTests.cs` | The three eBilling container tests replaced by `ConfiguredStampContainer_IsOwned_WithoutAGraphCall_AndAnUnlistedContainerIsStillRefused` and the theory `RetiredFinanceKey_WhenStillConfigured_IsNotOwned` (both retired keys) | The key left `StampContainerSettingKeys` |
| `tests/unit/.../Api/ExternalAccess/ExternalInvoiceFileRoutesTests.cs` | Additive: `SeedFirmFile` gained an optional `StoredFileParent? parent = null`; the two new tests of 5.2 | Replacement deny proof |
| `tests/Spaarke.ArchTests/ExternalFileStoreRouteCensusTests.cs` | New; extended in fix round 1 with the call-closure check | 5.3 |
| `tests/Spaarke.ArchTests/IOptionsDriftTests.cs`, `tests/integration/Shared/Dataverse/TestRecordContainerResolver.cs` | Comment only | Names |

### Failed before / pass after

The new and changed tests were copied into a fresh worktree at the base commit (`d3e20a3ca8`, built in place, source unchanged) and run there: **15 of the 230 selected failed** against the old source.

- Startup theory: "b!two ids", "b!x;b!y", "   ", "b!x,b!y", a type id, the unsubstituted `#{...}#` token and "b!" fail before (the old validator rejected them). The valid id and the empty value passed before as well (expected: they were legal before).
- `124 must-not-fire: the old collision ... no longer fails start-up` (both keys), `FinanceOptions has no member that binds either retired key`, `092 / 124 must-not-fire: a retired Finance container key admits nothing` (Procedure key case).
- The four relocation controls (`RefileFileRelocationTests` strict, interim and job; `RelocationTerminalAndHomeRuleTests` A4).
- The deny proof (5.2) passes before and after by design: it pins behaviour 124 must not change (the gate already refused a Procedure Stored File), so it is a regression guard with its passing control, not a fail-before test. `ExternalVisibilityTests` cannot compile against the old source (the `Readers` signature changed), so its fail-before is the compile break. The census ArchTest carries its own must-fire controls (5.3).

After (this branch): see section 12.

## 9. Condition 6 — the startup test

`FinanceContainerIdOptionsTests`:

- `NoSetting_Boots`.
- `LeftoverProcedureLibraryKey_IsIgnored_WhateverItHolds` (theory): a valid id (`b!…`), `""`, `"   "`, a type id, `#{SPE_PROCEDURE_LIBRARY_CONTAINER_ID}#`, `"b!"`, `"b!two ids"`, `"b!x;b!y"`, `"b!x,b!y"`. The host starts, `FinanceOptions` binds without the value and `IValidateOptions<FinanceOptions>` reports no failure.
- `LeftoverProcedureLibraryKey_EqualToAnotherContainer_Boots` (email default, archive): the old collision no longer fails start-up.
- `RetiredOutputKey_IsIgnored_WhateverItHolds` (092's key, same).
- `FinanceOptions_HasNoContainerIdMember`.
- `Control_ABadFileStoreEndpoint_StillFailsStartup`: the validator still runs `ValidateFileStore`, and the message names `Finance:FileStore:BlobEndpoint`.

## 10. For the main session: `.claude/constraints/provisioning.md:269`

Not edited (the task is barred from `.claude/`). Suggested replacement for the clause `/ Finance__ProcedureLibraryContainerId (eBilling; Finance__EbillingOutputContainerId was retired by ebilling-r1 task 092: export files are Stored Files in Finance:FileStore, not SPE) /`:

> `/` (no eBilling key: `Finance__EbillingOutputContainerId` was retired by ebilling-r1 task 092 and `Finance__ProcedureLibraryContainerId` by task 124; every eBilling file is a Stored File in `Finance:FileStore`, not SPE) `/`

and delete the trailing sentence "The eBilling Procedure Library container is never stamped on a business unit (`sprk_containerid`)." (it describes a container that no longer exists). Leftover settings of either name are harmless (section 9).

## 11. Deviations and things left

1. **The #2151 declared-unfiled-home mechanism is removed**, not kept as an empty-key extension point. Its only key was this one, so with the key gone it would be dead code with unrunnable positive tests. The first version of this note called the removal "fail-closed (it only tightens)". **That was wrong in one hypothetical case** (corrected in review fix round 1): with the key SET and an UNFILED library document present, the document derived to the library container while the mechanism existed; without it, the derivation is the business unit's container, and the operator-gated legacy migration would move the file there. That is a widening of where a file may sit, not a tightening. **No deployed environment can reach that case**: the key is absent in every deployed environment (section 3), dev has 0 such documents (section 3), and the pre-116 library writers refused without the key. Removing #2151 needed uac-r2's explicit agreement; it is given (section "#2151 declared unfiled home - retired" below).
2. The brief's "grep finds nothing" is met for code and config, not for retirement comments and the leftover-key must-not-fire controls (section 2).
3. Left untouched on purpose: the "(Procedure Library)" column descriptions in the Dataverse schema (Entity.xml, ps1); changing a schema description needs a solution import. (The relocator's "no declared home" runtime strings and comments were reworded in review fix round 1, F-D.)
4. The tenant integration tests in `SpeAppOnlyContainerIsolationTests` are edited but only those that run offline were run.
5. 093's guard (`ExternalDocumentNarrowingGuardTests`) is unchanged; the census sits beside it.
6. Test files modified (not added): `ExternalVisibilityTests` (helper arg), `ExternalInvoiceFileRoutesTests` (additive), `SpeAppOnlyContainerIsolationTests`, `RefileFileRelocationTests`, `RelocationTerminalAndHomeRuleTests`, `FinanceContainerIdOptionsTests`; reasons in section 8.

## 12. Build, tests, publish size, CVE

Commands (all from the worktree root):

- `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` and `dotnet build tests/Spaarke.ArchTests`: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --no-build --filter "FullyQualifiedName~ExternalVisibility|...FinanceContainerId|...FinanceOptions|...RuleSet|...RateCard|...Procedure|...StoredFile|...ExternalInvoiceSubmission|...ExternalInvoiceFile|...GraphModule|...StampContainer|...SpeAppOnlyContainerIsolation|...RecordContainerResolver|...Relocat|...Refile|...CiamSearch|...DocumentContainer|...TestSpeOwnership|...ExternalTeamShare|...SecureChildReconciliation"`: **4508 passed, 0 failed**.
- After the last comment edit: `--filter "CiamSearch|FinanceContainerId|ExternalInvoiceFileRoutes|ExternalVisibilityTests"`: **170 passed, 0 failed**.
- `dotnet test tests/Spaarke.ArchTests` (the whole project): **2718 passed, 0 failed**.

Publish size (BFF hygiene item 4, NFR-01):

- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -r linux-x64 --self-contained false -o deploy/api-publish`, framework-dependent linux-x64, Release, from fresh worktrees at short paths (`C:\wt124m` at `origin/master` `1e6ee676d7`, `C:\wt124b` at the branch tip).
- Zip: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; the published folders include the 4 `.pdb` files the publish emits.
- File counts: master 191, branch 191 (match).
- Master 42,575,604 bytes (40.603 MB); branch 42,537,808 bytes (40.567 MB); **delta −37,796 bytes (−0.036 MB)**. Well under +5 MB, 55 MB and 60 MB. No package changed (`git diff` on csproj/props empty).
- Scratch worktrees (and the base-commit worktree used for fail-before) removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.

CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports "has no vulnerable packages given the current sources". No new HIGH.

BFF hygiene check (CLAUDE.md §10, §11): no new endpoint, service, DI registration, package, Dataverse column or file surface; the diff removes surface. One new test class (the census) and one moved test class.

## 13. #2151 declared unfiled home — retired

uac-r2 agreed (relayed by the main session) to remove the #2151 mechanism, on three conditions, all met here.

**What was removed** (in `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/`; section 1 has the table):

- `RecordContainerResolver.DocumentPointer.cs`: `DeclaredUnfiledHomeContainerKeys`, `PointerDriveColumn`, `DeclaredUnfiledHomesFrom`, `DeclaredUnfiledHomeOf`, the `depth == 0 && DeclaredUnfiledHomeOf(row)` branch of the derivation, the `DeclaredHome` property of the derivation result, and the three extra column appends the branch needed.
- `RecordContainerResolver.cs`: the `_declaredUnfiledHomes` field and its constructor line (`_declaredUnfiledHomes = DeclaredUnfiledHomesFrom(configuration)`).
- Tests: in `RefileFileRelocationTests` the five declared-home tests (served under strict, interim-rule served, without the bound item, blank key, 092's declared-home test) and the `EbillingOutputKey` const; in `RelocationTerminalAndHomeRuleTests` the A4 declared-home tests and the `DeclaredHome` assertions. Replaced by leftover-key must-not-fire controls (a leftover `Finance:ProcedureLibraryContainerId` leaves a document `Unfiled`, `Allows(...)` false, `PrimaryContainer` the business unit's) and `NoHomeIsDeclared_DerivationIsTheBusinessUnits` (renamed from `DeclaredHomes_Unset_...`).

**Where it came from.** Commit `84291ecea8` (PR #2161, "fix(uac-r2): #2159 a re-filed document's file follows its derived container"), fixing #2159 and #2151. That commit also added the re-file mechanism, which stays; only the declared-home part was removed.

**Status.** #2151's behaviour is retired together with the Procedure Library key, the only key it ever read. Nothing else declares a home. Any future need for a declared home restores it from those commits (`git show 84291ecea8` and the later edits to `RecordContainerResolver.DocumentPointer.cs`, `RefileFileRelocationTests.cs`, `RelocationTerminalAndHomeRuleTests.cs`) instead of redesigning it.

**The one hypothetical case, and why no environment can reach it.** Key set, plus an unfiled library document: the derivation used to be the library container (the declared home); it is now the business unit's container, so the operator-gated legacy migration would move that file. Section 11 item 1 states this correctly (the first version of this note said "only tightens", which is false for this case). No deployed environment can reach it: the key is absent everywhere, dev has 0 such documents, and the pre-116 library writers refused without the key (section 3).

**Grep, before and after** (`DeclaredUnfiledHomeOf | DeclaredHome | declared[ -]unfiled[ -]home | declared[ -]home`, case-insensitive; `src tests docs scripts infrastructure .github config projects`):

| File | Before (`09bdbf2d3b`) | After | What the after-hits are |
|---|---|---|---|
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` | 1 | 1 | The I-4 row: states the removal (task 124). |
| `Infrastructure/Dataverse/RecordContainerResolver.DocumentPointer.cs` | 1 | 1 | The derivation doc: states the removal. |
| `Services/Access/SecureChildReconciliationJob.cs` | 1 | 1 | Doc: states the removal. |
| `Services/Documents/DocumentContainerRelocator.cs` | 6 | 2 | Two comments stating the removal; the other four (comments and two runtime strings) reworded. No test asserts those strings. |
| `tests/.../RefileFileRelocationTests.cs` | 3 | 1 | The comment stating the removal; the test display name and method name no longer say "declared home". |
| `projects/ebilling-r1/notes/092-submission-on-files.md` | 2 | 2 | History of task 092; each now ends with a "Superseded by task 124" line. |
| `projects/ebilling-r1/notes/rule-set-scope-design.md` | 0 | 1 | The "UAC #2151 ... must be settled" line (a stale current-behaviour claim) now ends with "Resolved by task 124" (it matched "#2151", not the grep pattern; counted here for the record). |
| `projects/ebilling-r1/notes/124-...md`, `defer-issues.md`, `tasks/124-...poml` | (notes and brief) | (notes and brief) | This task's own record. |
| `projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-open-questions.md` | 1 | 1 | Unrelated: "the contract's declared home" is a documentation location, not this mechanism. Not edited. |

No other project's notes state this mechanism's current behaviour (`unified-access-control-r2`'s notes mention #2151 only as history; left alone).

## 14. Review fix round 1

Two Opus adversarial reviews of the first pass. Branch `task/ebilling-124`, tip at the start of the round `09bdbf2d3b`.

| Finding | Fix | Test |
|---|---|---|
| **F-A** The route census saw only a handler's direct references; an indirect reach through a classified service, or a direct `sprk_storedfile` query by table name (const inlined by the compiler), passed. | Closure check over the whole BFF call graph for every Ciam handler (5.3): interface and virtual dispatch, static constructors, closed generics, depth bound with a fail-on-open rule, string-literal and `StoredFileSchema` member and field sinks, a justified allow-list that only shrinks. 093's `Scan` made `internal` with its call graph exposed; nothing else of 093's file changed. | `Closure_MustFire` (both probe shapes plus interface, static table, schema member and field), `Closure_DepthBound_MustFire`, `Closure_MustNotFire_AndTheAllowListRatchets`, `RealGraph_HasTheExpectedShape`, and the real-endpoint-table test. Real-code mutation recorded below. |
| **F-B** §5.1 missed `DELETE /api/v1/external/documents/{id}`. | Added, marked existence-only for the caller's own document; Procedure sources have no source document. Re-enumerated the 70 Ciam routes from the booted endpoint table (5.1). Reworded the sentence that said the guard "computes" the whole claim. | The allow-list entry for `DeleteOwnDocument` (stale if no longer reached). |
| **F-C** `ProcedureLibraryExternalDownloadTests` and `ProcedureLibraryPointerTests` (a general deny rule) were deleted. | Restored, logic and controls unchanged, as `ExternalDownloadUnderivedContainerDenyTests.cs`; the DEF-015 known-limit text restored in `CiamSearchRouteTests`; section 5.4 corrected. | 6 restored cases pass (a pointer into an underived container refused under both rules, the derived container served, the real route answers 409 with no SPE read, a firm upload served). |
| **F-D** Stale comments. | `ExternalVisibility.Readers.Entities` no longer says "a document's drive"; four relocator comments and two runtime refusal strings no longer describe a declared home. | No test asserts the strings (grep over `tests`); relocator, refile and visibility suites pass. |
| **F-E** The deny test was carried by one check (a mutation of `IsFirmFile` passed every route case). | A parent-kind-only row added at the decision point (the purpose-only row already existed there); single-defect rows added through the route (end-to-end). | Mutation runs below: each `IsFirmFile` mutation fails the decision-point test; the route cases do not fail under either mutation (stated below). |
| **F-F** Deferral bookkeeping. | DEF-013 Resolved; DEF-014 Resolved; DEF-015 Narrowed (eBilling part closed by evidence; the general limit stays recorded in the deny tests). No GitHub change; the main session closes #1967 and tells customer-provisioning-orchestration-r1. | n/a |
| **F-G** "Only tightens" was wrong. | Section 11 item 1 corrected; the hypothetical case and why no deployed environment can reach it stated; uac-r2's agreement recorded (section 13). | n/a |
| Coordinator addition: #2151 section and grep. | Section 13; stale mentions fixed in code, docs and ebilling-r1's own notes. | grep before and after, section 13. |

### Mutation runs (F-A, F-E)

| Mutation (temporary, reverted) | Result |
|---|---|
| F-A real code: `InvoiceSubmissionService.SubmitAsync` also calls `_files.ReadRowsForInvoiceAsync(Guid.Empty, 5, ct)` (a new file-store read inside a classified service reached from the submission route) | The Ciam closure test failed: `CIAM CLOSURE reaches the file store outside the firm file gate: ...ExternalInvoiceSubmissionEndpoints::SubmitAsync => file store member: ...InvoiceSubmissionService::SubmitAsync -> ...StoredFileService::ReadRowsForInvoiceAsync`. Reverted; passes. |
| F-E M1: `IsFirmFile` admits `ProcedureSource` as well as `InvoicePdf` | `IsFirmFile_RefusesAProcedureSourceRow_AndAdmitsTheInvoicePdf` FAILED (1 of 61). All route cases, old and new, passed. |
| F-E M2 (before the new row): `IsFirmFile` drops the parent-kind check | Nothing failed (61 of 61): the old decision-point rows all carried the Procedure purpose too. This is the gap the review found. |
| F-E M2 (after the new row): same mutation | `IsFirmFile_RefusesAProcedureSourceRow_AndAdmitsTheInvoicePdf` FAILED (1 of 61). Route cases passed. |

**What the route cases do and do not pin.** The brief asked for route theory cases that fail under the matching mutation. They cannot: a stored file row has a second, independent defence, `StoredFileService.PathIsOwn`, which re-derives the blob path from the typed purpose and parent and refuses any row whose purpose does not belong to its parent kind, so a single-defect row is refused through the route even when `IsFirmFile` is weakened. The route theory `ProcedureSourceFile_EachCheckRefusesOnItsOwn` therefore pins the end-to-end refusal of each single-defect row (defence in depth), and the two `IsFirmFile` checks are each pinned on their own at the decision point, where the mutations above fail. Both are in the test comments.

### Known limits kept

- The closure guard's limits (5.3, and the guard's class doc): runtime-built table names, delegate seams, reflection, endpoint filters, packages, generic virtual methods. The guard claims no more.
- The fake Dataverse in `StoredFileRig` ignores query criteria for Stored File queries (5.2); the query's own filters are pinned by `TheStoredFileQuery_IsFilteredByTheStore`.
- DEF-015's general fact stays recorded in the two deny tests' messages.
- The "(Procedure Library)" Dataverse schema descriptions (needs a solution import).

### Test counts, filtered (this round)

| Project | Filter | Result |
|---|---|---|
| `Spaarke.ArchTests` | `ExternalFileStoreRouteCensus`, `ExternalDocumentNarrowingGuard`, `IOptionsDrift` | 47 passed, 0 failed |
| `Sprk.Bff.Api.Tests` | `ExternalInvoiceFileRoutes`, `ExternalVisibility`, `CiamSearchRoute`, `ExternalDownloadUnderivedContainer`, `ExternalRecordDocumentRoutes`, `DocumentPointerContainerCheck`, `RefileFileRelocation`, `RelocationTerminalAndHomeRule`, `DocumentContainerRelocator`, `FinanceContainerIdOptions` | 562 passed, 0 failed |

Before the round the guard's census tests were 4 (`Census_MustFireAndMustNotFire`, `EveryFileStoreCaller_IsInTheCensus`, `CiamRoutes_...`, `Routes_MustFireAndMustNotFire`); now 8. The whole `Sprk.Bff.Api.Tests` and the whole ArchTests were not re-run (round 1 rule); the first pass ran both in full (section 12) and this round changed only comments and runtime refusal strings in `src`, plus tests.

## 15. Approval and merge

- **uac-r2 APPROVED 2026-11-22 at `decb4de05f`** (whole diff `d3e20a3ca8..decb4de05f`): conditions 3-6 and the #2151 removal met, no findings. uac-r2 accepted each census allow-list entry and the 8-line change to its `ExternalDocumentNarrowingGuardTests` (Scan, ScanResult, Normalize made internal; the Calls map exposed; behaviour unchanged). Any dev BFF deploy that contains 124 goes through uac-r2 on the owner naming the exact SHA.
- Merged into `work/ebilling-r1` 2026-11-22 (`3b7367bfbf`).
- Section 10 applied by the main session: `.claude/constraints/provisioning.md:269` no longer names `Finance__ProcedureLibraryContainerId`; the "never stamped on a business unit" sentence is gone.
- #1967 closed with the reason; customer-provisioning-orchestration-r1 told that eBilling needs no SPE container on a stamp (only 091's Finance file store).
