# Task 096b - close the remaining direct invoice-create paths (EB-D-201)

Wording: invoices are created only by submission; lines only by submission, extraction and Add line. Source work only: no import, no live call, no role script run. Scope 4 (dev data) is the main session's.

## 1. Every create path found, and its disposition

| # | Path | Where | Disposition |
|---|---|---|---|
| 1 | `POST /api/v1/child-records/sprk_invoice` | `ChildRecordEndpoints.CreateTables` (was `Api/ChildRecordEndpoints.cs:73`) | CLOSED in this task. Answers the route's unsupported-table 400 (`child_record.table_unsupported`) before any Dataverse call. Re-file (`RefileTables`, `PATCH`) unchanged. |
| 2 | `POST /api/v1/child-records/sprk_billingevent` | same set | Already closed by 164a-1 (not in the set at 682dc4a661). Pinned again here by test (below). |
| 3 | Client `BFF_CHILD_CREATE_TABLES` held `sprk_invoice` | `bffChildWriteAdapter.ts:43` | CLOSED: removed (the `ChildRecordCreateTablesAgreementTests` agreement forbids a client table the server refuses). No client creates an invoice: the only callers of `createRecord`/`createChildRecordViaBff` name other tables; `internalInvoiceSubmission.test.ts:423` scans `src/client` for a `createRecord` of `sprk_invoice`. No live caller beyond the ribbon (grep over `src/client`, `src/solutions`, `src/dataverse` non-bundle). |
| 4 | Secure-record ribbon "New Billing Event" (button + alert) | `sprk_secure_child_ribbon.js` `TABLES`; SpaarkeMaster `sprk_BillingEvent/RibbonDiff.xml` | CLOSED in source: billing event removed from `TABLES`, the merge script, the deploy script and the template's table list; its RibbonDiff now only hides the platform New / Save and New / grid New / subgrid AddNewStandard / subgrid NewRecord (the `sprk_billingexport` pattern). Needs the ribbon import to reach dev (main session). |
| 5 | Invoice form "New" and "Save & New", invoice grid "New" | SpaarkeMaster `sprk_Invoice/RibbonDiff.xml` | CLOSED in source: three `HideCustomAction` added (`Mscrm.Form.sprk_invoice.NewRecord`, `...SaveAndNew`, `Mscrm.HomepageGrid.sprk_invoice.NewRecord`). Needs the ribbon import. |
| 6 | Invoice quick-create form | `Entities/sprk_Invoice/Entity.xml:3304` | `IsQuickCreateEnabled` = 0 in source, so nothing to change; the quick-create form file remains. Live value to be read (read-only) by the main session; source/live may differ. |
| 7 | Sitemap "New" | `AppModuleSiteMaps/sprk_MatterManagement/AppModuleSiteMap.xml:30,238` | The sitemap holds navigation SubAreas only (no "New" command). The global "+" lists quick-create-enabled tables only (row 6). Nothing to change. Modern-command (appaction) rows are not in the source; main session lists them read-only (see CreatePrivilegeRibbons README). |
| 8 | Platform subgrid "+ New" for invoices on a NON-secure matter/project/work-assignment | `secure-child-new.template.xml` (`NativeNewAllowed`) | OPEN, needs a decision (section 4, D1). |
| 9 | AI chat create tool / any `RecordCreationService` caller creating `sprk_invoice` (as the caller, app-only write) | `RecordTableRules.cs:288-301` (`sprk_invoice` rule has no `GenericCreateOnlyRefused`); `DataverseCreateRecordHandler.cs:160` | OPEN, needs a decision (section 4, D2). Not a route in the brief's list; found while inventorying. |
| 10 | Office quick-create `POST /api/office/quickcreate/invoice` | `QuickCreateRequest.cs:180`, `QuickCreateSourceAccessFilter.cs:117` | Task 096 removes it (as the brief says). No add-in client posts it today (grep `src/client/office-addins`). Left as is. |
| 11 | Confirm route `POST /api/finance/invoice-review/confirm` | `FinanceEndpoints.cs:178-181` | Creates only by submission (D-134); asks the caller's Create privilege (a reason Create is held, section 3). No client in `src` calls it. Not a direct create. |
| 12 | Submission persister, Add line, extraction | `InvoiceSubmissionPersister.cs:730`, `InvoiceLineAddService.cs:816` | The sanctioned writers (app-only, section 2). |

## 2. Proof for uac-r2: the submission paths create `sprk_invoice` and `sprk_billingevent` app-only

Identity. `IGenericEntityService` resolves to `IDataverseService` (`Infrastructure/DI/GraphModule.cs:90`, alias `:145`), the singleton `DataverseServiceClientImpl` (`Spaarke.Dataverse/DataverseServiceClientImpl.cs:29`), a `ServiceClient` built from the managed identity or the app's client credentials (`:89-140`), never from a user token. Every writer below holds only `IGenericEntityService`; none holds `IDataverseUserClient`, an OBO token or the request token (grep of `InvoiceSubmissionPersister.cs`, `InvoiceSubmissionProcessor.cs`, `InvoiceSubmissionAfterSteps.cs`, `SubmissionCommitGate.cs`, `InvoiceTotalsService.cs`, `InvoiceLifecycleService.cs`, `InvoiceLineAddService.cs` for `IDataverseUserClient|OnBehalfOf|UserToken|ExtractBearerToken`: no hit).

Create Invoice wizard (052) - every step:
1. Client `CreateInvoiceWizard` only POSTs the submission route and polls its status; it creates nothing (`CreateInvoiceWizard.tsx:8`; scan test `internalInvoiceSubmission.test.ts:423`).
2. `POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions` (`InvoiceSubmissionEndpoints.cs:58-80`). The only caller-identity work is the filter: Read + AppendTo on the core record and AppendTo on the firm (`ResolveSubmitTargets`, `:214-247`; `finance.submit_invoice`, `finance.attach_invoice`). It asks no Create on `sprk_invoice` or `sprk_billingevent`.
3. Handler -> `InvoiceSubmissionService` (`SubmitAsync`, `:754`/`:768`) writes the submission row and the tracking number with `_entities` (`Submission/TrackingNumberAllocator.cs:249,431`, `InvoiceSubmissionService.cs:174`), then enqueues the job (`InvoiceSubmissionService.cs:1505`). The job contract carries the submission id only (`InvoiceSubmissionProcessingJobHandler.cs:166`).
4. Job (`InvoiceSubmissionProcessingJobHandler.cs:355`) -> `InvoiceSubmissionPersister.PersistAsync` (`InvoiceSubmissionPersister.cs:265`): builds the invoice row from the submission's own stamp (`:461`, `BuildInvoiceCreateRequest` `:614`, `InvoiceLifecycleService.cs:382,417,447`), the file rows (`:668-681`), the line rows (`:684`, built `:1589`), the approval (`:687`), the submission update (`:712`), and commits them in ONE transaction with `_entities.ExecuteTransactionAsync` (`:730`; `_entities` is the `IGenericEntityService`, `:179,:200`). The owner is the resolver's team (`:476`), the person who asked is only recorded in `sprk_createdbyperson` (`:440-456`).
5. After-steps run in the same job on the same identity: the commit gate (`SubmissionCommitGate.cs:123,568`), totals (`InvoiceTotalsService.cs:72`), Signals and the rest (`InvoiceSubmissionAfterSteps.cs:106`). The reviewer's Add line also writes app-only (`InvoiceLineAddService.cs:246,816`).

Outlook Submit Invoice (095, from-email):
1. `POST /api/finance/invoice-submissions/from-email` (`InvoiceSubmissionEndpoints.cs:126-148`). Filters: Finance rights as the caller (Read + AppendTo core record, AppendTo firm), Office caller, `EntityAccessFilter`, rate limit, version-save gate, kill switch, idempotency. No Create on the invoice tables.
2. `SubmitInvoiceFromEmailOperation.SubmitAsync` (`Submission/SubmitInvoiceFromEmailOperation.cs:90`): step 1 is read-only as the caller (`PrepareSourceAsync`), then `DecideForFromEmailAsync` (`:113`) and the staging/reads (`:119-148`) - none creates `sprk_invoice`. The caller's token is used only to confirm Read on the core record (`InvoiceSubmissionFromEmailHttp.cs:480-482`, `:712`), a read.
3. The submission row is queued and the SAME job and persister as above create the invoice and lines (rows 3-5 of the wizard path).

Result: `sprk_invoice` and `sprk_billingevent` are created only at `InvoiceSubmissionPersister.cs:730` (and a line at `InvoiceLineAddService.cs:816`), by the application identity; no step of either path creates either table as the caller.

## 3. Reasons a user might need Create on `sprk_invoice` / `sprk_billingevent`

`sprk_billingevent`: no source role holds `prvCreatesprk_BillingEvent` (checked all 13 `Roles/*.xml`), and `FeatureRolePrivilegeGuardTests.MustNotHold` (`:562-565`) pins that. Nothing to change.

`sprk_invoice` (Core User and Office Add In User hold `prvCreatesprk_Invoice` at Deep, `Roles/Spaarke Core User.xml:291`, `Roles/Spaarke Office Add In User.xml:222`). Reasons still in the source:

| # | Reason | Evidence | Closes with |
|---|---|---|---|
| R1 | Confirm route asks the caller's Create (`finance.confirm` target `CallerPrivilege(CreateInvoicePrivilege)`) | `FinanceEndpoints.cs:178-181`, `FinanceAuthorizationFilter.cs:348`; contract tests `FinanceEndpointsAuthorizationContractTests.cs:488,520,554,742` | A BFF change: drop the Create ask from the confirm route (the invoice is created by the persister) or retire the route. No client in `src` calls it. |
| R2 | Office quick-create asks Create for `entityType=invoice` | `QuickCreateSourceAccessFilter.cs:117`; `OfficeQuickCreateContractTests.cs:506,525,995` | Task 096 (removes invoice quick-create). |
| R3 | ArchTest pins the roles to the features | `FeatureRolePrivilegeGuardTests.cs:156` (Invoice wizard, through the child-records route: no longer true after this task), `:232`, `:240` (Office quick create) | Remove the `:156` row with R1/R2; remove invoice from `:232/:240` with R2. |
| R4 | Ribbon display rule `Mscrm.CreateSelectedEntityPermission` on the secure-child "New invoice" command (the Invoice wizard launcher) | `secure-child-new.template.xml` `New.Command` DisplayRules; SpaarkeMaster `sprk_Invoice/RibbonDiff.xml` | The wizard launcher would vanish for a user without Create. Needs the launcher rule changed for `sprk_invoice` (D1). |
| R5 | Append / AppendTo on invoice (lines, to-dos, documents, communications filed under it) | `prvAppendsprk_Invoice` / `prvAppendTosprk_Invoice` | Separate privileges; unaffected by dropping Create. |
| R6 | Chat create tool / generic `RecordCreationService` create asks Create | `CallerWriteAuthorization` (record-creation service) | D2. |

No Create on `sprk_invoice` is needed by the two sanctioned paths themselves (section 2). R1-R4 and R6 are the reasons it is still held, so by the brief (escalation trigger: removing Create breaks a path that runs as the caller; R1 and R4 do) the role change is NOT made here: no role source change, no role script. This narrows nothing yet. When it is made, it narrows the secure-child ribbon's round-28 stance ("the Create privilege is NOT removed") for `sprk_invoice` only, by EB-D-201 (the script docblock already points here).

## 4. Decisions needed (not guessed)

- D1 (ribbon, launcher). Hiding the platform "+ New" for invoices on a non-secure host (row 8) removes the only launcher of the Invoice wizard there, because the "New invoice" button is today the complement of the platform New (shown only on a secure or unverifiable host). To close row 8 the "New invoice" button (and its display rule, R4) must show on every host for `sprk_invoice`, and the platform AddNewStandard be hidden outright. That departs from the template (`Merge-SecureChildRibbon.ps1` regenerates `sprk_invoice` from it). Recommendation: do it as a template option for `sprk_invoice` in a follow-up, with the Create-privilege role change.
- D2 (chat/generic create). Add `GenericCreateOnlyRefused = true` to the `sprk_invoice` rule (`RecordTableRules.cs:288`), as 164a-1 did for the line. It also refuses the invoice-family create seams in the service tests (`RecordCreationServiceMappingSeamTests`, the FR-05 create cases), so it needs its own test pass; not done here.
- D3 (role). Sequence: R1 + R2 (096) + R3 + R4 (D1) + D2, then Core User and Office Add In User drop `prvCreatesprk_Invoice`, with a role script copied from `Grant-EbillingWorkforceSubmissionPrivileges.ps1` (-WhatIf / apply / -Verify). Main session decides.

## 5. Tests (all update an existing pin, none baselined)

- `SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs` `ChildCreate_AnInvoice_IsRefusedAsATableTheRouteDoesNotCreate` (must fire, 6 shapes): 400 `child_record.table_unsupported`, `CreateTables` lacks the table, zero app creates, zero caller POSTs, zero reads. The four route-create FR-05 cases it replaces are removed (the guard is unchanged and keeps its update/chat cases).
- `SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs`: census theory drops `sprk_invoice` (must-not-fire: every other table still creates, `TheCensusTheory_CoversEveryCreateTable`); `ChildCreate_OfATableOutsideTheCensus_IsRefused400` gains `sprk_invoice` and `sprk_billingevent` and a zero-POST assertion.
- `ChildRecordCreateTablesAgreementTests.TheOutsideTheCensusTheory_NamesOnlyTablesTheRouteDoesNotCreate` reads that theory: putting `sprk_invoice` back in `CreateTables` fails it (the seed).
- `SecureChildNewCommandAgreementTests` (ArchTests): billing event not served, hidden outright in the SpaarkeMaster RibbonDiff (five locations), no `sprk.SecureChild.` left in it, route lacks both tables, invoice keeps its wizard and re-file.
- Client: `bffChildWriteAdapter.test.ts` (invoice and billing event are not create tables and are refused before any fetch; invoice re-file stays); `secureChildRibbonScript.test.ts` (billing event not served, invoice wizard kept).
