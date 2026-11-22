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
| (not a route) the visibility decision | `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs:72-75`, `:105`, `:613-622` | OData row reads of `sprk_storedfile` for the (a') decision. No bytes and nothing returned to the caller. |
| (not a route) table classification | `Services/Access/SecureChildLineage.cs:368`, `Services/Access/ExternalTeamShareSynchronizer.cs:277` | `sprk_storedfile` is a HardDeny table for external reach. |

No other route in the `/api/v1/external` group reaches `StoredFileService`, `StoredFileBlobs` or `StoredFileEndpoints`. This is not only a reading: the ArchTest in 5.3 computes it from the compiled assembly and the booted endpoint table.

### 5.2 The replacement deny proof (a unit/route test)

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalInvoiceFileRoutesTests.cs`, theory `ProcedureSourceFile_IsNeverListedOrServedToAnExternalCaller`: a Procedure Stored File (purpose ProcedureSource, parent a Procedure Version, even marked firm-visible) is seeded; for a contact and for a flagged systemuser, and with the Procedure Version id both different from and equal to the invoice id, the listing is empty, the content route returns the uniform 404 that an unknown file gets, no audit event is written and no byte is read. `IsFirmFile_RefusesAProcedureSourceRow_AndAdmitsTheInvoicePdf` pins the decision point directly, with the firm's own Invoice PDF as the passing control.

Note: the fake Dataverse in `StoredFileRig` ignores query criteria for Stored File queries, so what these tests exercise is the `IsFirmFile` re-check, which is the gate's authoritative check.

### 5.3 The guard (ArchTest, must-fire and must-not-fire)

`tests/Spaarke.ArchTests/ExternalFileStoreRouteCensusTests.cs` (`[Collection(BootedBffCollection.Name)]`; ADR-038 path #1, KEEP). It complements 093's namespace-limited guard (`ExternalDocumentNarrowingGuardTests`, unchanged) with a whole-assembly IL census.

| Test | Asserts |
|---|---|
| `EveryFileStoreCaller_IsInTheCensus` | Every caller of `StoredFileService`, `StoredFileBlobs` or `StoredFileEndpoints` in the assembly is classified with a reason: `ExternalGate`, `ExternalStagingWrite` or `NotOnAnExternalRoute` (17 callers). A new caller is unclassified and fails. |
| `Census_MustFireAndMustNotFire` | Compiled control fixtures: a direct call, a call via generic resolution (must fire) and a type with no file-store reference (must not fire). |
| `CiamRoutes_ReachTheFileStoreOnlyThroughTheFirmFileGate` | On the real endpoint table, every route admitting `AuthSchemes.Ciam` is checked: the only handler that reaches the file store is `ExternalInvoiceSubmissionEndpoints::GetInvoiceFileContentAsync` on `GET /api/v1/external/invoices/{id:guid}/files/{fileId:guid}/content`, and no Ciam handler is a `NotOnAnExternalRoute` caller or a file-store endpoint type. |
| `Routes_MustFireAndMustNotFire` | Controls: a Ciam route whose handler is a file-store endpoint (`StoredFileEndpoints.GetContent`) is caught. |

### 5.4 Deleted test, with the reason

`tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs` is deleted. It held `ProcedureLibraryPointerTests` and `ProcedureLibraryExternalDownloadTests` (the "a library SPE document streams no bytes to a firm user" proof) and `CiamSearchRouteTests`. The first two proved a property of a container that no longer exists, so they were removed, not weakened; 5.2 and 5.3 prove the equivalent property for where Procedure files now live. `CiamSearchRouteTests` is a general route fact and is kept unchanged in logic in its own file `tests/integration/auth/ExternalAccess/CiamSearchRouteTests.cs`.

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
| `tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs` | Deleted | 5.4 |
| `tests/integration/auth/ExternalAccess/CiamSearchRouteTests.cs` | New file, moved content | 5.4 |
| `tests/unit/.../Infrastructure/ExternalAccess/ExternalVisibilityTests.cs` | 3 deleted, 1 added, helper arg removed | Section 7 |
| `tests/unit/.../OptionsValidation/FinanceContainerIdOptionsTests.cs` | Rewritten | Section 9 |
| `tests/integration/data-mutation/DocumentContainer/RefileFileRelocationTests.cs` | Deleted the five declared-home tests (served under strict, interim-rule served, without the bound item, blank key, 092's declared-home test) and the `EbillingOutputKey` const; added three leftover-key must-not-fire tests; renamed the unset-key derivation test; converted the job test to `TheJob_ALeftoverProcedureLibraryKey_DeclaresNoHome_TheDocumentIsReportedAndLeft` | The mechanism they tested is removed; the replacements prove a leftover key does nothing |
| `tests/integration/data-mutation/DocumentContainer/RelocationTerminalAndHomeRuleTests.cs` | A4 tests become three leftover-key controls (`Unfiled`, `Allows(...)`, `PrimaryContainer`); `DeclaredHome` assertions removed | Same |
| `tests/integration/tenant/Spe/SpeAppOnlyContainerIsolationTests.cs` | The three eBilling container tests replaced by `ConfiguredStampContainer_IsOwned_WithoutAGraphCall_AndAnUnlistedContainerIsStillRefused` and the theory `RetiredFinanceKey_WhenStillConfigured_IsNotOwned` (both retired keys) | The key left `StampContainerSettingKeys` |
| `tests/unit/.../Api/ExternalAccess/ExternalInvoiceFileRoutesTests.cs` | Additive: `SeedFirmFile` gained an optional `StoredFileParent? parent = null`; the two new tests of 5.2 | Replacement deny proof |
| `tests/Spaarke.ArchTests/ExternalFileStoreRouteCensusTests.cs` | New | 5.3 |
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

1. **The #2151 declared-unfiled-home mechanism is removed**, not kept as an empty-key extension point. Its only key was this one, so with the key gone it would be dead code with unrunnable positive tests. Fail-closed (it only tightens) and a no-op wherever the key was never set (every deployed environment, section 3). The main session may reject this and ask for the extension point back.
2. The brief's "grep finds nothing" is met for code and config, not for retirement comments and the leftover-key must-not-fire controls (section 2).
3. Left untouched on purpose: the relocator runtime strings "no declared home" in `DocumentContainerRelocator.cs` (about lines 1174/1177, and comments), and the "(Procedure Library)" column descriptions in the Dataverse schema (Entity.xml, ps1). They are descriptions of a state that still correctly reads as "no home declared"; changing a schema description needs a solution import.
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
