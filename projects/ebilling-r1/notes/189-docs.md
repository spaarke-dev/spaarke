# Task 189: docs for the Procedure and Stored File tables

> **Date**: 2026-11-25. **Branch**: `task/ebilling-189` (from 2ac250cae4). Docs only: nothing under `src/`, `scripts/` or `tests/` changed, nothing under `.claude/`.

## Gate

The POML gate ("blocked until 079") is moot. 079's schema writes and its SpaarkeMaster source sync are done and in master; its remaining steps only seed smoke-test data (`079-wp3-deploy.md`). The docs describe the SpaarkeMaster source and the BFF code, not a live read (no Dataverse access in this run).

## What changed

New: `docs/data-model/sprk_procedure.md` (three tables), `docs/data-model/sprk_storedfile.md`, `docs/architecture/EBILLING-FILE-STORE.md`; both INDEX files list them.

Updated: `sprk_ruleset.md`, `sprk_ratecard.md`, `sprk_billingexport.md`, `sprk_invoice.md`, `sprk_invoicesubmission.md`, `sprk_matter-related-tables.md`, `sprk_workassignment.md`, `sprk_invoice-matter-project.md` (the project columns live in this combined page), `child-access-permission.md`, and `projects/ebilling-r1/CLAUDE.md` (D-44 / D-55 and D-67 marked "superseded by EB-D-148" with the new rule in one line; the D-66 wording and the dev containers line fixed). `decisions-log.md` and older notes are untouched.

## Points left undocumented (escalation)

Three points where the deployed schema or code and the model differ in a way that touches access were left out of every doc, per the task's escalation rule, and are reported to the owner in the task report rather than here. The pages state only what the model and the schema agree on.

## Model vs deployed schema or code (no access effect)

| # | Model | Deployed | Pages |
|---|---|---|---|
| a | The version holds its file | The Procedure Version has no file column; the file is a Stored File row whose `sprk_procedureversion` names it | sprk_procedure, sprk_storedfile |
| b | (not in the model) | The version also carries `sprk_publishedon` (D-203), `sprk_notconverted` and `sprk_notconvertedlist` (task 116, EB-D-211) | sprk_procedure |
| c | Blob path `{tenantId}/...` | The first segment is the Dataverse organization id (read from the `organization` row), not the Entra tenant id | sprk_storedfile, EBILLING-FILE-STORE |
| d | Firm gate: Invoice PDF + Firm-visible + Servable + scan clean + parent matches | The code gate also requires no source document and no source communication (`IsFirmFile`) | EBILLING-FILE-STORE |
| e | The rule set "loses" `sprk_sourcedocument` | `sprk_ruleset.sprk_sourcedocument` and `sprk_ratecard.sprk_sourcedocument` are still deployed and unused; removal is owner-gated (`Remove-EbillingSourceDocumentColumns.ps1 -Delete`; task 116 census found no values) | sprk_ruleset, sprk_ratecard |
| f | Export documents retire | `sprk_billingexport.sprk_csvdocument` and `sprk_jsondocument` are still deployed and unused | sprk_billingexport |
| g | `sprk_procedureversion_procedure_procedure` description says Stored File links are Restrict | The deployed `sprk_storedfile_procedureversion_procedureversion` is RemoveLink, which matches the model; the description text in `Deploy-EbillingProcedureSchema.ps1` (about line 584) and the XML is stale. Not changed here; owner task 080 | sprk_procedure |
| h | Ruleset, policy and policyversion creator column "created unsecured; 081 secures it" | All three `sprk_createdbyperson` columns are field-secured in the source | sprk_ruleset |
| i | Work Assignment form has no Billing tab (read 2026-10-21) | 079 added `tab_billing` | sprk_workassignment |
| j | One upload/download PCF | The control is the Invoice Files code page (`src/solutions/InvoiceFiles`) | EBILLING-FILE-STORE |
| k | Ruleset `sprk_firm` listed as plain | Field-secured in the source | sprk_ruleset |

## Spot-check list: option values and delete behaviours (source: `src/dataverse/solutions/SpaarkeMaster/`)

Read with a script over `Entities/*/Entity.xml` and `Other/Relationships/*.xml`; every value below equals the XML.

| Item documented | Value | Source file |
|---|---|---|
| `sprk_proceduretype` | Guideline 100000000, Rate card 100000001, Billing instructions 100000002, Internal Standard 100000003, Other 100000004 | `OptionSets/` global choice; `Entities/sprk_procedure/Entity.xml` |
| `sprk_procedureappliesto` | All records 100000000, Firm 100000001, Assigned records only 100000002 | same |
| `sprk_procedurestatus` | Active 100000000, Retired 100000001 | same |
| `sprk_procedureversionstatus` | Draft 100000000, Active 100000001, Superseded 100000002, Retired 100000003 | `Entities/sprk_procedureversion/Entity.xml` |
| `sprk_policydomain` | Billing 100000000, Ontology 100000001, Docketing 100000002 | `Entities/sprk_procedure/Entity.xml` (`sprk_area`) |
| `sprk_publishstate` | Draft 100000000, Published 100000001 | `Entities/sprk_PolicyVersion/Entity.xml` |
| `sprk_procedureassignment.sprk_mode` (local) | Follow latest 100000000, Pinned 100000001, Excluded 100000002 | `Entities/sprk_procedureassignment/Entity.xml` |
| `sprk_storedfilepurpose` | Procedure source 100000000, Invoice PDF 100000001, Invoice supporting file 100000002, Export file 100000003 | `Entities/sprk_StoredFile/Entity.xml` |
| `sprk_storedfile.sprk_filestatus` (local) | Not yet servable 100000000, Servable 100000001, Removed 100000002 | same |
| `sprk_ruleset.sprk_scope` (local) | Client default 100000000, Firm 100000001, Record 100000002, Firm record 100000003 | `Entities/sprk_ruleset/Entity.xml` |
| Version alternate key | `sprk_procedure_versionnumber` on (procedure, version number) | `Entities/sprk_procedureversion/Entity.xml` |
| version -> procedure | Cascade (delete only; parental) | `Other/Relationships/sprk_procedure.xml` |
| assignment -> procedure; ruleset -> procedure; assignment -> firm; procedure -> firm | Restrict | `Other/Relationships/sprk_procedure.xml`, `sprk_Organization.xml` |
| procedure issued by, external contact, internal contact | RemoveLink | `sprk_Organization.xml`, `Contact.xml` |
| assignment -> pinned version; policyversion -> procedure version; ratecard -> procedure version | Restrict | `Other/Relationships/sprk_procedureversion.xml` |
| version previous version | RemoveLink | same |
| assignment -> excluded policy | Cascade (delete only; the assignment's only parental relationship) | `Other/Relationships/sprk_Policy.xml` |
| assignment -> matter, project, work assignment | RemoveLink | `sprk_Matter.xml`, `sprk_Project.xml`, `sprk_WorkAssignment.xml` |
| storedfile -> procedure version, invoice, billing export | RemoveLink | `sprk_procedureversion.xml`, `sprk_Invoice.xml`, `sprk_billingexport.xml` |
| storedfile -> source document, source communication | Restrict | `sprk_Document.xml`, `sprk_Communication.xml` |
| `sprk_createdbyperson` relationships on the five tables | RemoveLink | `SystemUser.xml` |
| `sprk_billingfirm` / `sprk_billingresponsible` on matter, project, work assignment | RemoveLink | `sprk_Organization.xml` |
| `sprk_policy.sprk_ruleset` | RemoveLink | `sprk_ruleset.xml` |
| `sprk_document.sprk_restrictedbysubmission` | Two options, default No, field-secured | `Entities/sprk_Document/Entity.xml` |
| Field-secured columns listed per table | as in the pages | the `IsSecured` flag in each `Entity.xml` |

## Code and solution mentions of the retired names (not changed; owner in the second column)

None presents the name as current store design; the retirement comments describe the removal.

| File | Mention | Owner |
|---|---|---|
| `SpaarkeMaster/Entities/sprk_ratecard/Entity.xml`, `Entities/sprk_ruleset/Entity.xml`, `Entities/sprk_billingexport/Entity.xml`, `Other/Relationships/sprk_Document.xml` | Column descriptions of the unused document lookups | The `sprk_sourcedocument` / export document column removal (task 116 script, owner-gated) or the next SpaarkeMaster resync |
| `scripts/ebilling/Deploy-EbillingExportSchema.ps1`, `Deploy-EbillingPolicyStoreSchema.ps1`, `Deploy-EbillingTimekeeperRateCardSchema.ps1` | The same descriptions, in the original schema scripts | Same |
| `Configuration/FinanceOptions.cs`, `FinanceOptionsValidator.cs`, `Infrastructure/DI/FinanceModule.cs`, `GraphModule.cs`, `Infrastructure/Dataverse/RecordContainerResolver.DocumentPointer.cs`, `Infrastructure/ExternalAccess/ExternalVisibility.cs`, `Services/Access/SecureChildReconciliationJob.cs`, `Services/Finance/FirmFindingsProjection.cs` | Comments recording the retirement (tasks 092, 124, EB-D-163) | Tasks 092 and 124 (done) |
| `Api/Ai/RagEndpoints.cs` (2), `Services/Ai/IFileIndexingService.cs`, `Services/Ai/Jobs/BulkRagIndexingJobHandler.cs`, `Services/Ai/PostUploadIndexingEnqueuer.cs`, `Services/Jobs/Handlers/RagIndexingJobHandler.cs`, `Workers/Office/IndexingWorkerHostedService.cs` | Comments on a "deliberately not indexed" skip for the E-Billing Output Files container (FR-16). The skip is live code for a container the feature no longer uses | Filed for the owner: remove the skip when the dev containers are removed (O-13) |

Also found and left alone: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` line 135 names `Finance:ProcedureLibraryContainerId` only to say it was removed.

Under `docs/`, no line now presents "Procedure Library" or "E-Billing Output Files" as current; `projects/ebilling-r1/CLAUDE.md` keeps the names only inside lines marked "superseded by EB-D-148".
