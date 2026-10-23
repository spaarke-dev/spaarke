# Defer / Issue Tracking — ebilling-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label ebilling-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-009 — Before merging to master: every BFF environment must define CONTENT_UNDERSTANDING_ENDPOINT (task 043)

| Field | Value |
|---|---|
| **Status** | Open - merge gate |
| **Urgency** | before the first ebilling-r1 BFF merge to master (and before any dev BFF deploy of this branch) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 043 |
| **GitHub Issue** | none - project merge checklist item (task 190) |

**Description**: `appsettings.template.json` now carries `#{CONTENT_UNDERSTANDING_ENDPOINT}#`. An unreplaced token fails options validation at startup (intended: fail loud). Every environment's token set must define it (the endpoint where eBilling runs, an empty string elsewhere, which leaves `ReadInvoiceAsync` refused as `ai.invoice.read.not_configured` and the health check Healthy). `spaarke-bff-dev` additionally needs `ContentUnderstanding__Endpoint` set to `spaarke-docintel-invoices-dev` and the BFF identity granted access before eBilling reads work there. See `src/server/api/Sprk.Bff.Api/appsettings.tokens.md`.

### ISS-007 — Invoice status can be changed outside IInvoiceLifecycleService (OOB Activate / Deactivate, direct writes)

| Field | Value |
|---|---|
| **Status** | Open - resolution planned |
| **Urgency** | before task 029 (forms and command bar) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 014 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1957 |

**Description**: the FR-06 transition matrix holds only for callers of the service. Activate on a Paid invoice returns it to In Review; Web API and flow writes bypass it too. ADR-002 rules out a plugin.

**Resolution (technical, per DATAVERSE-WRITE-PATH-ARCHITECTURE.md L1-L4)**: task 029 hides the OOB Activate / Deactivate commands on sprk_invoice, so the product's only status writer is the service; non-product writes are the architecture's accepted L4 gap (statuscode is audited). A report-only reconciliation job is not planned for R1.

### ISS-008 — Console user-profile role reads the timekeeper list and hard-codes its options (D-83, D-84)

| Field | Value |
|---|---|
| **Status** | Resolved for ebilling (D-84, 2026-10-15). #1954 stays open for the Console improvement only |
| **Urgency** | none for ebilling; low for the Console |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 017 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1954 |

**Description**: `sprk_userprofile.sprk_primaryrole` was bound to `sprk_timekeeperrole`, and `userProfileService.ts` (`PRIMARY_ROLE_OPTIONS`) and `StatedProfileReader.cs` hard-code its 14 original options. D-83 planned to move it to a lookup on `sprk_contacttype_ref` first and delete the 10 non-LEDES options afterwards.

**Outcome (D-84, 2026-10-15)**: ebilling did not wait for the Console. `Deploy-EbillingUserProfileRoleChoice.ps1` created the global choice `sprk_userprofilerole` with the same 14 values and labels, recreated `sprk_userprofile.sprk_primaryrole` (same logical and schema name) on it with the one stored value (Legal Operations) restored and the User Profile main form position kept, then **deleted the 10 non-LEDES options**: `sprk_timekeeperrole` now holds exactly the 20 LEDES classifications. The Console code is unchanged and still correct. #1954 remains open only for reading the role list from Dataverse (a choice or `sprk_contacttype_ref`) instead of the hard-coded constants; nothing in ebilling waits on it.

### DEF-013 — Customer environments need the Procedure Library and E-Billing Output Files containers (hand-off to customer-provisioning-orchestration-r1)

| Field | Value |
|---|---|
| **Status** | Open - handed off |
| **Urgency** | before eBilling is enabled on any customer stamp (blocks FR-16 outside dev) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 028 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1967 |

**Description**: dev has both containers (D-55, owner-created). Each customer environment needs: two containers of the stamp's EXISTING container type (no new type); both marked `spaarkeCustomerId` = `Customer__Id`; neither stamped on a business unit; two app settings written by H4b from new `per_env_settings` entries in `scripts/canonical-secret-catalog/manifest.yaml`: `Finance__ProcedureLibraryContainerId`, `Finance__EbillingOutputContainerId` (dev examples in task 028 notes). Repeatable per environment.

**Fails without it**: on a customer stamp the keys stay empty, so the consumers (075, 078, 133, 150) refuse with "container not configured" and guidelines and exports have nowhere to go (D-35). The BFF still boots: the keys are optional at start-up.

### DEF-014 — The document-pointer check refuses app-only reads of `sprk_document` rows whose file is in an eBilling container

| Field | Value |
|---|---|
| **Status** | Open - decide in task 075 / 078 (first consumers) |
| **Urgency** | before 075 / 078 create Procedure Library documents; also 133 / 150 |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 028 (CIAM trace) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1968 |

**Description**: `RecordContainerResolver`'s document-pointer check (run by `DocumentStorageResolver` and by `FileIndexingService.IndexFileAppOnlyAsync` when a document is named) serves only a container DERIVED for the document's records: business-unit, secure-record, archive or unfiled default. The eBilling containers are none of those by design. This is what keeps CIAM users out of the Procedure Library (task 028 test `ProcedureLibraryCiamDenyTests`), but it also refuses every INTERNAL app-only read of such a row: the RAG job, upload enqueuer and send-to-index return "Document storage could not be verified", so a Procedure Library guideline with a `sprk_document` row is never indexed (D-55 says it is), and standard download / preview returns 409.

**Fails without a decision**: D-55 "Procedure Library documents are indexed" and internal opening of guideline / export documents through the standard routes.

**Options**: (1) the derivation learns that a BFF-marked eBilling document belongs in the configured eBilling container (shared UAC code: coordinate with unified-access-control-r2; re-check the CIAM deny tests); (2) an eBilling-owned read path that names the configured container itself, with indexing through a server-built request carrying no document id.

**Warning for option 1**: the pointer check is the ONLY thing that keeps CIAM callers from the Procedure Library's bytes today. If the derivation starts serving the eBilling containers, the external download routes would serve them too. Option 1 therefore needs an explicit refusal of the eBilling containers on the `/api/v1/external` routes in the same change, and `ProcedureLibraryExternalDownloadTests` must stay green.

### DEF-015 — External document lists still show the name and summary of a Procedure Library row filed under a firm-readable record

| Field | Value |
|---|---|
| **Status** | Open - design discussion with the owner (2026-10-15) |
| **Urgency** | before 075 / 078 create Procedure Library `sprk_document` rows linked to matters or projects |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 028 (adversarial review pass 1) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1975 |

**Description**: task 028 proves a CIAM caller cannot download the BYTES of a Procedure Library document (`ProcedureLibraryCiamDenyTests`). The external document LIST and fetch routes on `/api/v1/external/{record}/documents` filter only on the record lookup, not on the file's container. A Procedure Library `sprk_document` row filed under a record the firm can read (by design or by mistake) would still show its name and `sprk_filesummary` to the firm user.

**Fails without it**: D-63 "firm users never see the Procedure Library" holds for content but not for metadata, if any Procedure Library row is ever linked to a matter or project a firm collaborates on.

**Options**: (1) the eBilling consumers (075 / 078) never link Procedure Library rows to a matter or project (a convention, with a test in those tasks); (2) the external list and fetch routes exclude rows whose `sprk_graphdriveid` is a configured eBilling container (a change to shared external-access code owned by SPA-external-access-platform-r3 / unified-access-control-r2).

**Owner (2026-10-15)**: needs more discussion. Procedure files need not be `sprk_document` rows at all: a dedicated Procedures table with its own SPE file association, or Documents that cannot be linked to matters / projects. Today every environment writes to one container, so the container is never checked; eBilling introduces the first internal-only containers. Until decided, no task creates Procedure Library `sprk_document` rows linked to a core record (constraint on 075 / 078). The independent 028 review adds a second exposure route: the external module-data documents grid (`ExternalAccessModule.cs:397-421`), which cannot filter on `sprk_graphdriveid`.

### ISS-010 — Generic child-record create route bypasses the FR-05 invoice-family core-record guard

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-15 (unified-access-control-r2 direction; see "Resolution" below) |
| **Urgency** | before the first eBilling writer ships (tasks 044 / 049) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 013 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1973 |

**Description**

`POST /api/v1/child-records/{table}` (`ChildRecordEndpoints.CreateTables`) creates `sprk_invoice` and `sprk_billingevent` from the caller's payload without `InvoiceFamilyCoreRecordGuard.Check`, so a row naming two core records is accepted there and FR-05's negative acceptance fails on that route. The secure-record ribbon's "New Billing Event" uses it.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs` (unified-access-control-r2 owns the route)
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs`

**Suggested fix**

For a table in `InvoiceFamilyCoreRecordGuard.CoreRecordLookups`, call `Check` on the payload (merged with the stored row on update) and refuse with its stable code before the write; fill the five resolver fields from `DeriveResolverFieldsAsync`. Test both refusals on the route.

**Estimated effort**: 2-3 hours

**Blockers**: unified-access-control-r2 agreement (their route)

**Related**: FR-05, D-75, task 013 notes

**Resolution (2026-10-15, as unified-access-control-r2 directed; details in the task 013 notes, section 11)**

- Call the guard, do not refuse the tables: `sprk_invoice` and `sprk_billingevent` stay in `CreateTables`, `sprk_invoice` in `RefileTables`.
- The guard is invoked from the shared write core, not per route: `OwnedChildWrite.CreateAsync` (after the caller's G5 check) and `OwnedChildWrite.RefileAsync` (first, before re-file or ordinary update) call `OwnedChildWrite.CheckInvoiceFamilyAsync`, keyed by table. That covers `POST /api/v1/child-records/{table}`, `PATCH /api/v1/child-records/sprk_invoice/{id}`, and the chat create and update tools. `InvoiceFamilyCoreRecordGuard` owns the rule (`CheckWriteAsync`, `GuardedLookups`).
- Merge: on an update that sets or clears a guarded lookup, the stored lookups are read once as the caller (`ReadLookupsAsCallerAsync`) and overlaid with the patch (`EffectiveLookups`) before the guard runs. A read fault is the caller's own error, and a column the answer lacks is `invoice_family_read_failed`. Neither counts as "not set".
- `sprk_supersedesinvoice` is a peer reference. The superseding and superseded invoices must regard the same core record, or the write is refused with `invoice_family_supersedes_core_record_mismatch`. A superseded invoice the caller cannot read gets the uniform not-found.
- Not done here: filling the five resolver fields on these routes (`DeriveResolverFieldsAsync`) was not in the direction, so it is left to the eBilling writers.

---

### ISS-011 — AP export batches of secure invoices are outside the secure-record family (owner decision)

| Field | Value |
|---|---|
| **Status** | Closed - owner decision D-85 (2026-10-15): batch stays outside the secure family; accepted that secure invoices in a batch are visible to anyone who can see the batch |
| **Urgency** | before task 150 writes the first batch |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 013 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1974 |

**Description**

Task 013 did not register `sprk_billingexport` in the secure-record family (the POML listed it): its only lookups are the export documents, a batch spans many core records, and the role file admits only tables a writer assigns to the Secure Record team. A batch holding secure invoices is therefore ordinary-owned, and its row and export files are readable by that team's business unit, which can expose secure invoice numbers and amounts.

**Entry-points**

- `docs/data-model/sprk_billingexport.md`; task 150 (export writer); task 028 (containers); `config/secure-record-owner-role.json`

**Suggested fix**

Owner choice: (1) separate batches for secure invoices, owned by the Secure Record team (then the table joins the family with a lineage row through its invoices); (2) one batch restricted to the AP role and a restricted container; (3) accept AP users seeing every invoice.

**Estimated effort**: depends on the option (1: about a day with task 150)

**Blockers**: owner decision

**Related**: tasks 023, 028, 150; task 013 notes

---
### ISS-001 — InvoiceLiveFactResolver reads non-existent sprk_invoice columns (sprk_total, sprk_status)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | `/project-pipeline` task authoring (POML 015 discovery) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1904 |

**Description**

`InvoiceLiveFactResolver` reads `sprk_total` and `sprk_status` from `sprk_invoice`. Neither column exists: the table has `sprk_totalamount`, and status is `statecode` / `statuscode` plus the legacy `sprk_invoicestatus`. Requesting a column that doesn't exist fails the Dataverse retrieve, so the invoice live facts fail or come back empty.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/InvoiceLiveFactResolver.cs:70-77`, `:142-143`, `:297-316`
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/Entity.xml`

**Suggested fix**

Read `sprk_totalamount` and `statuscode`, in task 015's scope.

**Estimated effort**: 1–2 hours

**Blockers**: none

**Related**: #1771. The private invoice-status copies tracked there now carry an added comment: `FinancialCalculationToolHandler.cs:44-45` uses status values 100000002 and 100000003, which do not exist. Both are fixed in scope by task 015.

---

### DEF-001 — Customer stamps need gpt-5.2 / gpt-5.4-mini and a Content Understanding region (hand-off to customer-provisioning-orchestration-r1)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (step 8 hand-off) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1915 |

**Description**

eBilling R1 deployed `gpt-5.2` (Content Understanding extraction, FR-47, D-51, D-53) and `gpt-5.4-mini` (UTBMS coding, D-46) in dev, with a CU resource whose model defaults map to them. Customer stamps built from `openai.bicep` / `PinnedModelCatalog` have neither model and no CU-capable (AIServices) resource. Without them the extraction and coding Actions fail on a stamp: CU analyze has no completion deployment, and the coding Action resolves a deployment that does not exist. gpt-4o / gpt-4o-mini retire 2027-04-14.

**Entry-points**

- `infrastructure/bicep/modules/openai.bicep` and `PinnedModelCatalog` (owned by customer-provisioning-orchestration-r1; not edited by ebilling-r1).
- CU GA regions (api 2025-11-01): australiaeast, canadacentral, eastus, eastus2, japaneast, southcentralus, southeastasia, swedencentral, uksouth, westeurope, westus, westus3 (westus2 is NOT listed). Cross-region Bring Your Own Capacity is supported.
- Dev reference inventory: `projects/ebilling-r1/notes/003-model-deployments.md`.

**Suggested fix**

Add the model pins and a CU resource + `PATCH contentunderstanding/defaults` step to the stamp template or handlers (single owner for the pin catalog), with `Cognitive Services Content Understanding Reader` for the stamp BFF identity in the runtime RBAC module (keyless, ADR-028 A6).

**Estimated effort**: unknown - needs design in customer-provisioning-orchestration-r1.
**Blockers**: none for eBilling R1 (dev only); needed before any customer-stamp eBilling go-live.
**Related**: ebilling-r1 tasks 003, 004, 043.

---

### DEF-002 — Dev BFF still uses key-based Azure OpenAI / Document Intelligence settings

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | backlog |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (found, not caused) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1916 |

The dev App Service has `Ai__OpenAiKey`, `AzureOpenAI__ApiKey`, `DocumentIntelligence__OpenAiKey` and `DocumentIntelligence__DocIntelKey` (values not read). ADR-028 A6 requires keyless access. Out of eBilling scope; the new Content Understanding path is keyless.

---

### DEF-003 — Dev deployment `gpt-4o-mini` serves gpt-4.1-mini (deprecated 2027-04-14)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | backlog |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 003 (found, not caused) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1917 |

The deployment name misdescribes its model and the model retires 2027-04-14. eBilling does not use it (coding uses `gpt-5.4-mini`, D-46).

---

### ISS-002 — Dev cannot run the legacy Finance extraction pipeline (no playbook-id setting, no FinanceExtraction / FinanceClassification playbooks, no gpt-4o deployment)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline; escalation trigger 1) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1918 |

**Description**

`spaarke-bff-dev` has no `Finance__InvoiceExtractionPlaybookId` setting, so `InvoiceExtractionJobHandler` marks every invoice Failed at `:329-336`. The `FinanceExtraction` and `FinanceClassification` playbook rows that `InvoiceAnalysisService` loads by name do not exist in dev (and have no seed in the repo), and the dev OpenAI account has no `gpt-4o` deployment (the `FinanceOptions` extraction default). Any Confirm in dev enqueues a job that cannot succeed.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:328-336`
- `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceAnalysisService.cs:35,41`
- `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs:37,42`

**Suggested fix**

None before 044: the path is replaced by Content Understanding + Actions (D-52) and deleted in 050. Owner decides whether to repair dev for an interim run.

**Estimated effort**: 1-2 hours (config + seed), only if the owner wants an interim run
**Blockers**: owner decision (task 001 escalation)
**Related**: ebilling-r1 tasks 001, 044, 050; evidence `notes/baseline-finance-pipeline.md`

---

### ISS-003 — Legacy InvoiceExtraction persists nothing on success (output mapping unreachable, placeholder loads, no Extracted status, no billing events)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round (superseded by 050 deletion) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1919 |

**Description**

The handler's only success-path write is `OutputOrchestrator.ApplyOutputMappingAsync`, which is a no-op because `PlaybookService.GetPlaybookAsync` never selects `sprk_configjson`; `LoadInvoiceRecordAsync` (and the indexing handler's loads) are placeholders; success never sets the extraction status; no `sprk_billingevent` rows are ever written by this path. Related existing issues: #229, #233, #1087 (wrong-entity failure-status write).

**Entry-points**

- `InvoiceExtractionJobHandler.cs:339, 499-527, 559-583`
- `Services/Ai/PlaybookService.cs:223-274`; `Services/Ai/OutputOrchestratorService.cs:317-346`
- `InvoiceIndexingJobHandler.cs:259-328`

**Suggested fix**

No fix: task 050 deletes the legacy extraction path (D-71); 044/152 build the replacement and the real indexing inputs.

**Estimated effort**: n/a (deleted by 050)
**Blockers**: none
**Related**: #229, #233, #1087; ebilling-r1 tasks 044, 050, 152

---

### ISS-004 — AttachmentClassification job is never enqueued; finance architecture doc describes triggers and writes that do not exist

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 001 (baseline) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1920 |

**Description**

No code in `src/` submits an `AttachmentClassification` job, and `EmailToDocumentJobHandler` (named as the trigger in `docs/architecture/finance-intelligence-architecture.md`) does not exist. Dev has 0 classified documents. The same doc also claims extraction writes billing events and that the snapshot job runs after extraction; neither is true.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AttachmentClassificationJobHandler.cs:38`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs:156-159, 168-174`
- `docs/architecture/finance-intelligence-architecture.md` (data flow steps 1-4, 7)

**Suggested fix**

Task 042 migrates classification to an Action + Binding with its own trigger; correct the architecture doc in the eBilling docs work.

**Estimated effort**: 30 min (doc), trigger design is task 042
**Blockers**: none
**Related**: ebilling-r1 tasks 001, 042; rollup evidence is on existing issue #1663 (comment 2026-10-15)

---

### ISS-005 — Linear AI path ignores the Binding's sprk_modeltieroverride

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before 047 / 075 tune their tier |
| **Filed** | 2026-10-15 |
| **Source** | Task 041 (found in passing; platform code, not changed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1934 |

**Description**

Spec "Rules for the AI steps" says the model is the Action's tier *with* the Binding's `sprk_modeltieroverride`. On the linear path (`IActionResolver` → `IActionRunner`, which every `IInvoiceAi` capability method uses) the override is never applied: `ActionResolver.ResolveAsync` returns the Action row, and `ActionRunner` resolves `action.ModelTier` only. Only `SessionDispatchOrchestrator` (chat dispatch) applies `binding.ModelTierOverride`. A maker who sets the override on an eBilling Binding sees no effect.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionResolver.cs:44-72`
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs:144` (`ModelTierDeploymentResolver.Resolve(action.ModelTier, …)`)
- Contrast `Services/Ai/Chat/SessionDispatchOrchestrator.cs:630`

**Suggested fix**

Platform (ADR-043 engine owner): have the linear resolver return the Action with the Binding's override folded into its tier. Not in 041 scope (its escalation trigger forbids ActionRunner changes). Workaround for R1: set the tier on the Action row (D-51).

**Estimated effort**: 1–2 hours plus a seam test

**Blockers**: platform owner decision

**Related**: ebilling-r1 tasks 041, 042, 047, 075, 131; ADR-016, ADR-043

---

### ISS-006 — `## Input` operand escapes every non-ASCII character as \uXXXX

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | low (cost, not correctness) |
| **Filed** | 2026-10-15 |
| **Source** | Task 041 code review (found in passing; frozen platform format, not changed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1935 |

**Description**

`PromptInputSection.Render` serializes the operand with `new JsonSerializerOptions { WriteIndented = true }`, whose default `JavaScriptEncoder` escapes every non-ASCII and HTML-sensitive character (`€` → `€`, `§` → `§`, `'` → `'`). Document text sent through `## Input` (`ClassifyAsync`, `DraftRulesAsync`) therefore costs several tokens per such character and is harder for the model to quote back verbatim — relevant to `clauseQuote` in rule drafting. The format is golden-pinned (ADR-043 frozen contract), so a fix is a platform decision.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/PromptInputSection.cs:37`
- Golden test `tests/integration/seam/Ai/ContextBinderActionRunnerSeamTests.cs` (`PromptInputSection_Render_ProducesTheFrozenGoldenFormat`)

**Suggested fix**

Use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (prompt text is not HTML) and re-pin the golden test, coordinated with the ADR-043 engine owner. Measure in 075's evaluation whether clause quotes are affected first.

**Estimated effort**: 1 hour plus re-pinning

**Blockers**: platform owner decision

**Related**: ebilling-r1 tasks 041, 075; ADR-043

---

### DEF-004 — SpendSnapshotService ignores sprk_costtype and approved amounts: new Adjustment lines would be summed as spend

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before the first Adjustment line is stored as Invoiced (tasks 014 / 016 / 100) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 010 (reader of a column task 010 extended; not changed) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1923 |

**Description**

`SpendSnapshotService` reads `sprk_costtype` into `BillingEventData.CostType` but never uses it. Spend is `billingEvents.Sum(e => e.Amount)` over every line of the matter / project with `sprk_visibilitystate = Invoiced`, summing the billed `sprk_amount`. Task 010 added the option Adjustment (100000002) to `sprk_costtype`, plus `sprk_approvedamount` and `sprk_linestatus`. Once Adjustment lines are stored as Invoiced, their `sprk_amount` (never negative: the column is 0 .. 1,000,000,000) is added to spend instead of reducing it, and Disallowed / Reduced lines still count at the billed amount. Not reachable today: nothing writes those lines yet.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs:185, 235, 253, 268, 286, 402, 455-459`

**Suggested fix**

Settle the spend definition with the line writers (exclude or sign Adjustment lines; sum `sprk_approvedamount`, falling back to `sprk_amount` when unreviewed) and add a unit test.

**Estimated effort**: 1-2 hours plus test

**Blockers**: owner decision on the spend definition for adjusted lines

**Related**: ebilling-r1 tasks 010, 014, 016, 100; evidence `notes/010-invoice-line-columns.md`

---

### DEF-005 — UTBMS LOC 2013 revised activity/expense lists and IP expense codes E125-E131 are not seeded

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before IP (patent / trademark) or eDiscovery customers' invoices are coded |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 018 (UTBMS code sets) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1929 |

**Description**

Task 018 seeded the original ABA Activity (A101-A111) and Expense (E101-E124) lists, which utbms.com publishes as clean spreadsheets. Not seeded: the LOC 2013 revised Activity list (A112-A128, incl. discovery / eDiscovery codes) and Expense list (X-series), and the seven IP expense codes E125-E131. utbms.com publishes them only as PDFs whose tables do not extract reliably (code and name columns misalign), so rows could not be produced with confidence. A patent / trademark / eDiscovery line carrying one of these codes is outside the seeded set: FR-22 cannot propose it and a firm-supplied one is flagged as a mismatch.

**Entry-points**

- `scripts/seed-data/ebilling/utbms-activity.csv`, `utbms-expense.csv`
- `scripts/ebilling/Seed-EbillingBillingCodes.ps1` (create-only: re-running after adding rows adds them without touching existing rows)

**Suggested fix**

Owner decides the product standard (original ABA, LOC 2013 revised, or ABA plus E125-E131). Then add hand-verified rows to the CSVs and re-run the seed.

**Estimated effort**: 2-3 hours plus the owner decision

**Blockers**: owner decision on which revision is the standard

**Related**: ebilling-r1 tasks 018, 047; evidence `notes/018-utbms-code-sets.md`

---

### DEF-006 — Billing code / code set primary names are not application-required in dev

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `Set-PrimaryNameRequired` now runs for `sprk_billingcodeset.sprk_name` (ApplicationRequired) and `sprk_billingcode.sprk_code` is SystemRequired (alternate-key column), both in dev; the helper lives in `scripts/ebilling/EbillingSchema.psm1` (`notes/schema-script-hardening.md`). |
| **Urgency** | before admins edit code sets by hand |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 017 (found while fixing the same defect in its own script) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1931 |

**Description**

`Deploy-EbillingBillingCodeSchema.ps1` (task 018) asks for `ApplicationRequired` on the primary names `sprk_billingcode.sprk_code` and `sprk_billingcodeset.sprk_name` in the entity-create body. Dataverse ignores that property (like the primary-name `MaxLength`); dev reads back `RequiredLevel = None` on both. A form or API write can create a code or code set with an empty name, which the alternate key (`sprk_codeset`, `sprk_code`) cannot address.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (`Add-Table`, then `Set-PrimaryNameLength`)
- `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (`Set-PrimaryNameRequired`, the model fix)

**Suggested fix**

Add the `Set-PrimaryNameRequired` step to the 018 script after table creation and re-run it in dev (existing rows are unaffected).

**Estimated effort**: 30 minutes

**Blockers**: none

**Related**: ebilling-r1 tasks 017, 018; `notes/017-timekeeper-ratecard-schema.md` sections 6 and 7

---

### DEF-007 — `sprk_ruleset` primary name is not application-required in dev

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `Deploy-EbillingPolicyStoreSchema.ps1` calls `Set-PrimaryNameRequired` for `sprk_ruleset.sprk_name`; dev now reads ApplicationRequired. |
| **Urgency** | before admins create rule sets by hand (task 076) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 020 (found while fixing the same defect in its own script) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1937 |

**Description**

`Deploy-EbillingPolicyStoreSchema.ps1` (task 019) asks for `ApplicationRequired` on `sprk_ruleset.sprk_name` in the entity-create body. Dataverse ignores that property; dev reads back `RequiredLevel = None`. A form or API write can create a rule set with an empty name. The rule set name is shown to firms and cited on every finding (D-66, D-79), so a nameless rule set yields a finding that cites nothing. Same class as DEF-006.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1` (`Add-Table`, then `Set-PrimaryNameLength`)
- `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (`Set-PrimaryNameRequired`, the model fix)

**Suggested fix**

Add the `Set-PrimaryNameRequired` step to the 019 script after `Set-PrimaryNameLength` and re-run it in dev (existing rows are unaffected).

**Estimated effort**: 30 minutes

**Blockers**: none

**Related**: DEF-006; ebilling-r1 tasks 019, 020

---

### DEF-008 — eBilling schema scripts fail a re-run on a stale lookup read (`0x80072551` is not treated as already-exists)

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `0x80072551` (and every other duplicate code) is recognised by one `Test-DuplicateRefusal` in `scripts/ebilling/EbillingSchema.psm1`, which all seven schema scripts now import; confirmed on a settled read before it counts as skipped. |
| **Urgency** | before the next schema script re-run (tasks 021-028 should copy the fixed helper) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 020 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1938 |

**Description**

A lookup (relationship) create that hits an existing relationship returns `0x80072551: NavigationPropertyName ... is not unique within an entity`. The scripts only treat "already exists" as a skip, so a re-run whose existence read is stale stops with exit 1 instead of reporting no change. "A second run reports no change and exits 0" is an acceptance criterion of every schema task. Nothing is lost (the scripts are re-runnable). Fixed in `Deploy-EbillingSubmissionSchema.ps1` (`Invoke-DvCreate`).

**Entry-points**

- Not fixed in: `Deploy-EbillingInvoiceLineSchema.ps1`, `Deploy-EbillingInvoiceHeaderSchema.ps1`, `Deploy-EbillingTimekeeperRateCardSchema.ps1`, `Deploy-EbillingBillingCodeSchema.ps1`, `Deploy-EbillingPolicyStoreSchema.ps1`, `Deploy-EbillingApprovalSchema.ps1` (grep: none contains `0x80072551`; not individually verified)
- `scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1` (`Invoke-DvCreate`, the model fix)

**Suggested fix**

Add `0x80072551` to the duplicate test of each script's lookup-create helper (confirm on a settled read, throw if the column never becomes readable), or move the helper into one shared file.

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-006, DEF-007; ebilling-r1 task 020

---

### DEF-009 — ApplicationRequired is not enforced on API writes; eBilling schema scripts and docs rely on it (SystemRequired is)

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: SystemRequired (null-row guarded) on `sprk_ratecardrate.sprk_ratecard`, `sprk_billingcode.sprk_codeset` / `sprk_code`, `sprk_invoicesubmission.sprk_vendororg` / `sprk_trackingnumber`; other columns stay ApplicationRequired with scripts and `docs/data-model/*.md` corrected; per-column table in `notes/schema-script-hardening.md`. |
| **Urgency** | before any writer relies on a "required" column from schema 017-020 |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1939 |

**Description**

Verified live: a Web API create of `sprk_invoiceapproval` without the lookup marked ApplicationRequired succeeded. After raising `sprk_invoice` to SystemRequired and publishing, the same create is refused (`Attribute 'sprk_invoice' cannot be NULL`). Scripts, comments and docs of tasks 017-020 say an API write without the value is refused; only forms refuse. Where a null breaks an invariant, nothing but a server-side writer stands between the data and a broken row.

**Entry-points**

- `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1` (`sprk_ratecardrate.sprk_ratecard`, line 665; `Set-PrimaryNameRequired` comment)
- `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (`sprk_codeset`, `sprk_codetype`, lines 531, 533)
- the primary-name steps and comments in the 019 and 020 scripts; the matching data-model docs (grep, not individually verified)
- `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1` (`Set-LookupRequiredLevel`, the working model)

**Suggested fix**

Raise the level to SystemRequired for each lookup or column whose null breaks an invariant (idempotent PUT of the attribute plus publish, as in `Set-LookupRequiredLevel`); keep primary names at ApplicationRequired and correct the comments and docs. Check that no writer, seed or import creates rows without the value first.

**Estimated effort**: 2 hours

**Blockers**: none

**Related**: DEF-006, DEF-007; ebilling-r1 tasks 017, 018, 019, 020, 021

---

### DEF-010 — eBilling schema scripts: table-create duplicate (`0x8009050d`) not recognised and false "has rows" in `Set-PrimaryNameLength`

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: `0x8009050d` is in `Test-DuplicateRefusal` and `Set-PrimaryNameLength` counts rows with `$count=true` (no false "has rows"), both once in `scripts/ebilling/EbillingSchema.psm1`. |
| **Urgency** | before the next schema script re-run (tasks 022-028 should copy the fixed helpers) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1940 |

**Description**

(1) A table create whose existence read was stale is refused with `0x8009050d: Entities already exist`; the helpers match only `already exists` and a few codes, so the re-run stops with exit 1 instead of reporting no change. (2) Right after a table create, `Set-PrimaryNameLength` judged `@($rows).Count -gt 0` true on an empty table and left the primary name at 850 until the next run. Both fixed in `Deploy-EbillingApprovalSchema.ps1` (`Add-Table` / `Invoke-DvCreate` regex; row count by `$count=true` repeated until a count is returned).

**Entry-points**

- Regex not fixed in: `Deploy-EbillingBillingCodeSchema.ps1`, `Deploy-EbillingInvoiceHeaderSchema.ps1`, `Deploy-EbillingInvoiceLineSchema.ps1`, `Deploy-EbillingPolicyStoreSchema.ps1`, `Deploy-EbillingSubmissionSchema.ps1`, `Deploy-EbillingTimekeeperRateCardSchema.ps1` (grep for `already exists|0x80047013`)
- Row count not fixed in: the 017, 018, 019 and 020 scripts (grep for `@($rows).Count`)
- `scripts/ebilling/Deploy-EbillingApprovalSchema.ps1` (the model fix)

**Suggested fix**

Port both changes to the other scripts, or move the helpers into one shared file so the next fix lands once. Nothing is lost today (the scripts are re-runnable).

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-008; ebilling-r1 tasks 017-021

---

### DEF-011 — `sprk_matter.sprk_billingapproverrole` is single-select; Project and Work Assignment are multi-select (D-33 / task 024)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-15 - owner OK; Matter column recreated as multi-select in dev (task 024) |
| **Urgency** | before task 024 and task 103 start |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 021 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1941 |

**Description**

D-33, FR-14 and task 024 describe `sprk_billingapproverrole` as multi-select on Matter, Project and Work Assignment. In dev, Project and Work Assignment hold a `MultiSelectPicklistAttributeMetadata`, but Matter holds a plain `PicklistAttributeMetadata` (one value). Approver resolution (task 103) cannot store or read the default "Assigned Attorney 1 + Assigned To Internal" on a Matter. Also noted: Matter and Project have no `sprk_assignedto` lookup, so the option "Assigned To" resolves only on Work Assignment. All assignment lookups that exist target `contact` (task 021's pre-check), so the eligible-approver store is unaffected.

**Entry-points**

- `sprk_matter.sprk_billingapproverrole` (dev metadata, read-only check 2026-10-15)
- `projects/ebilling-r1/tasks/024-approver-role-billing-contact.poml`; task 103

**Suggested fix**

In task 024 decide with the owner: replace the Matter column with a multi-select (a column type cannot change in place, so this is destructive and needs the owner's OK and a check that no Matter row has a value) or accept single-select on Matter and amend the spec. Task 103 handles both shapes until then.

**Estimated effort**: 1-2 hours

**Blockers**: owner decision (destructive change)

**Related**: D-33, D-54; ebilling-r1 tasks 024, 103

**Evidence (task 024, 2026-10-15, dev spaarkedev1)**

- Rows: `sprk_matter` 65 total (62 on the final read; test rows are being deleted by other work), **0 hold a value** in `sprk_billingapproverrole` (counted by `ne null` and by reading every row). Project 0 of 23, Work Assignment 0 of 22.
- Dependencies (`RetrieveDependenciesForDelete`, component type 2): **1**, the platform-generated `aiskillconfig` `ais_FormFillFieldOptOut_sprk_matter_sprk_billingapproverrole` (type 10314, Default solution), created with the column. No form (5 forms), view (9), personal view (1), chart or workflow / business rule of `sprk_matter` references the column; no other table's dependency is listed. So no form or view needs re-adding after the replacement.
- The column and the global choice are already SpaarkeMaster components (`sprk_matter` is a whole-table component).
- Prepared path: `scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1 -ReplaceMatterApproverRole`. It re-runs the guards and refuses (exit 2, nothing changed) when any Matter row holds a value, when anything other than that skill config depends on the column, or when a form / view / chart / workflow references it; otherwise it deletes the single-select column, waits until it is gone and creates `sprk_billingapproverrole` as a multi-select on the same global choice, then verifies and publishes. `-WhatIf -ReplaceMatterApproverRole` runs the guards without writing. Without the switch the script only reports the evidence and lists the column as deferred. **The switch has not been run** (it needs the owner's OK: deleting a column).
- The "Assigned To" option: Matter and Project have no `sprk_assignedto`; task 024 does not depend on it. Task 103 must treat option 100000006 as "no approver" on Matter and Project.
- Side effect to check on first use: whether the platform recreates the `FormFillFieldOptOut` skill config for the new column (it is deleted with the old one). Harmless either way.

**Resolution (2026-10-15)**: owner OK given. The column delete was first refused (0x8004f01f) by the generated `aiskillconfig`; the script now deletes that config first. `-ReplaceMatterApproverRole` then recreated `sprk_matter.sprk_billingapproverrole` as a multi-select on the global choice (0 rows before and after); re-run reports no change. #1941 closed.

---

### DEF-012 — eBilling schema scripts: `Set-PrimaryNameRequired` can undo the primary-name length change on a stale read

| Field | Value |
|---|---|
| **Status** | Closed |
| **Resolution** | Fixed 2026-10-15: the shared module (`EbillingSchema.psm1`) waits for a settled read of the length `Set-PrimaryNameLength` set before the required-level PUT and checks it again afterwards (commit `0cfeec7c1`); the last two scripts that carried their own helper copies, `Deploy-EbillingExternalIdentifierSchema.ps1` (022) and `Deploy-EbillingExportSchema.ps1` (023), now import the module (the module gained `Set-PicklistRequiredLevel` for 022's choice key column). Evidence, dev spaarkedev1: both scripts re-run with `created : 0  changed : 0  skipped : 20` (022, key Active) and `skipped : 12` (023), "nothing changed - no publish", exit 0; 020 and 018 (module users) re-run unchanged after the module edit. |
| **Urgency** | before the next new-table schema script run (tasks 022 and 024-028 copy these helpers) |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 023 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1942 |

**Description**

`Set-PrimaryNameLength` shortens a new table's primary name (850 to N); `Set-PrimaryNameRequired` then reads the attribute and PUTs the whole attribute back with ApplicationRequired. When that read still shows the old MaxLength (850), the second PUT silently restores it. Seen on the first run of `Deploy-EbillingExportSchema.ps1`: the log said "CHANGED max length 850 -> 100", verification then read 850, exit 1; a second run fixed it. Nothing is lost (empty table, re-runnable).

**Entry-points**

- `scripts/ebilling/EbillingSchema.psm1` (`Set-PrimaryNameRequired` / `Set-ColumnRequiredLevel`, same read-modify-PUT)
- `scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1` (own copy, task 022)
- model fix: `scripts/ebilling/Deploy-EbillingExportSchema.ps1` (`Set-PrimaryNameRequired` takes the wanted length and waits up to 60 s until the read shows it)

**Suggested fix**

Make the required-level update wait for a settled read of the length the previous step set (or set both in one PUT), once, in the shared module.

**Estimated effort**: 1 hour

**Blockers**: none

**Related**: DEF-010; ebilling-r1 tasks 017-023

---

### DEF-016 — Binding mirror `sprk_playbookconsumer-rows.json` is missing four live dev rows; `Seed-PlaybookConsumers.ps1 -DiffOnly` is never clean

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before the next task that adds a Binding row and is asked for a clean `-DiffOnly` |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 042 (pre-existing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1986 |

**Description**: the mirror (dated 2026-07-07) lacks `assistant-suggest|default|`, `chat-summarize|reanalyze|*`, `create-project|default|*` and `create-todo|default|`, which exist in dev, so `-DiffOnly` reports four differences on every run (none for `invoice-classify`). `-Export` would also drop the per-row `$comment` fields. Fails without a fix: the mirror cannot serve as the drift gate and a stamp seeded from it misses four routed capabilities.

**Suggested fix**: add the four rows with comments, make Export keep comments, then gate on `-DiffOnly`. **Estimated effort**: 1 hour. **Blockers**: none. **Related**: task 042.

*(ID is provisional: other worktrees allocated DEF-013..015 in parallel; renumber on merge.)*

---

### DEF-017 — Dev `/healthz/catalog` is Unhealthy: eight Binding rows have no `ConsumerTypes` constant; the check is documented backwards

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before `/healthz/catalog` is used as a deploy gate |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 042 (pre-existing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1987 |

**Description**: rows without a constant on this branch: `agreement-classify`, `compose-make-concise`, `compose-rewrite-instruction`, `create-project`, `create-todo`, `list-tasks`, `nda-review`, `nda-standard-summary`. `RoutingConsumerTypeHealthCheck` reports a row with no `All` entry as Unhealthy and an `All` entry with no row as Degraded (forward-declared); the task 042 POML and the `ConsumerTypes.cs` block comment said the opposite (comment corrected in task 042; main `/healthz` stays Healthy). Fails without a fix: an always-Unhealthy check masks real routing regressions.

**Suggested fix**: reconcile constants and rows per branch at merge. **Estimated effort**: 1-2 hours. **Blockers**: other branches' constants. **Related**: task 042.

---

### DEF-018 — `FinanceOptions.ClassificationConfidenceThreshold` is declared but never read

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | when classification routing is next changed |
| **Filed** | 2026-10-15 |
| **Source** | ebilling-r1 task 042 (pre-existing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1988 |

**Description**: the handler writes every verdict and sends InvoiceCandidate and Unknown to review without comparing the confidence to the threshold. Fails without a fix: an operator who sets it expects different handling and gets none.

**Suggested fix**: implement the intended routing or delete the option and its docs. **Estimated effort**: 1 hour. **Blockers**: owner decision on intended behaviour. **Related**: task 042.

---

### DEF-019 — Contact key `sprk_externalobjectiduniquekey` is unmapped in SpaarkeMaster (another project)

| Field | Value |
|---|---|
| **Status** | Open (owned by the identity work that added the key) |
| **Urgency** | before the next SpaarkeMaster release import |
| **Filed** | 2026-10-16 |
| **Source** | ebilling-r1 task 029 (pre-existing on master) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2010 |

**Description**: `Import-SolutionScoped.ps1 -PlanOnly` warns `Unmapped component 14/18e5156a-89be-f111-aaaf-0022482913fc`. That component is the contact alternate key, which is already in master's `Entities/Contact/Entity.xml`. The same 6 identity components make the F12 guard in `Export-SpaarkeMasterSource.ps1` throw against dev. It fired task 029's escalation trigger. eBilling did not add it and does not depend on it.

**Suggested fix**: map the components in the components inventory or package scope, or move them to their owning solution. **Estimated effort**: 1 hour. **Blockers**: none. **Related**: task 029.

---

### DEF-020 — Invoice line coding cannot run on gpt-5.4-mini with low reasoning effort (AI platform gap)

| Field | Value |
|---|---|
| **Status** | Open (AI platform; eBilling interim: Fast tier) |
| **Urgency** | before task 162 sets the final coding model |
| **Filed** | 2026-10-16 |
| **Source** | ebilling-r1 task 047 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2011 |

**Description**: D-51 puts coding on gpt-5.4-mini with low reasoning effort, but:
- no dev tier maps to that deployment;
- `OpenAiClient` sends `max_tokens` to every deployment except the configured ReasoningModel, and gpt-5.4-mini rejects it with 400;
- the Action dispatch path has no reasoning-effort setting (ADR-043).

Measured on 120 lines: 55.0% task top-1 on gpt-4.1-mini and 63.3% on gpt-5.4-mini; NFR-05 is 80%.

**Suggested fix**:
- detect the gpt-5 or o-series family by model and send `max_completion_tokens`;
- let a tier or Action name its deployment;
- add an Action reasoning-effort setting.

**Estimated effort**: 1–2 days (platform). **Blockers**: AI platform ownership. **Related**: tasks 047, 162; D-51.

---

### DEF-022 — FR-05 invoice-family guard missing on three generic update paths (pre-existing)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-17 (task 026 fix-round-2, commit 553bc6f41; the PR can close #2056) |
| **Urgency** | before any playbook or field-mapping rule targets `sprk_invoice` / `sprk_billingevent` core lookups (none configured today) |
| **Filed** | 2026-10-17 |
| **Source** | ebilling-r1 task 026 fix-round-1 (K7) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2056 |

**Description**: `InvoiceFamilyCoreRecordGuard` runs from the shared write core (`OwnedChildWrite`; ISS-010). Three paths can write an invoice's or line's core lookups without going through it:

- `UpdateRecordActionCore.cs:251` (the playbook update);
- `DataverseUpdateHandler.cs:95` (the output orchestrator);
- `FieldMappingEndpoints.cs:1068` (the field-mapping push).

Each restamps afterwards, but none refuses any of these:

- a two-core invoice;
- a line with no core;
- a line whose invoice has another core record.

**Suggested fix**: on each path, call `CheckWriteAsync` for tables in `GuardedLookups` before the write. unified-access-control-r2 direction (2026-10-17):
- Do not route these paths through `OwnedChildWrite`. Instead, each path calls `InvoiceFamilyCoreRecordGuard` before the write when the target is `sprk_invoice` or `sprk_billingevent` and the patch touches a core lookup.
- Keep the same refusal codes and leave the paths' write mechanics unchanged. Add one test per path.
- Fix it before any customer uses playbooks or field mapping on invoices. Until then the reconciler and the 409s fail closed, so this is a data-quality gap, not an access leak.

**Estimated effort**: 0.5 day. **Related**: FR-05, D-75, ISS-010 (#1973), task 026.

**Resolution (2026-10-17, task 026 fix-round-2 item 2, commit 553bc6f41)**: as unified-access-control-r2 directed, each path calls `InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync` before its write. It runs when the target is `sprk_invoice` or `sprk_billingevent` and the patch touches a guarded lookup. The stored lookups are read as the app and overlaid with the patch. The check includes D-96's line check.
- The playbook update and the output orchestrator throw `RecordOwnerUnresolvedException` with the guard's stable code.
- The push records a code-only error.
- Write mechanics are unchanged; there are no new DI registrations.
- Tests: `InvoiceFamilyGenericUpdatePathTests` (a refusal test and an unaffected test per path). With the three guard calls disabled, the three refusal tests fail.
- The PR that carries this can close #2056.

---

### DEF-021 — SpaarkeMaster export blocked by the F12 dependency guard (pre-existing)

| Field | Value |
|---|---|
| **Status** | Open (owning projects; not ebilling) |
| **Urgency** | before the next full SpaarkeMaster export/commit (any project) |
| **Filed** | 2026-10-16 |
| **Source** | ebilling-r1 task 026 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2045 |

**Description**: exporting the live SpaarkeMaster solution stops at its F12 guard on 7 missing dependencies: `sprk_externaltenantid`, `sprk_homeobjectid`, `sprk_hometenantid` (each twice, type 70) and the `sprk_SpaarkePlatform` app module. The live solution also differs from source in ~238 files owned by other projects. Task 026 synced only its own 4 files.

**Suggested fix**: bring the components into scope (or move dependants out) with their owning projects, then re-baseline drift per project.

**Deploy order while this is open (task 026 fix-round-1)**: the BFF from task 026 on selects `sprk_regardinginvoiceline` on every Signal and Decision Record read, so in any environment the SpaarkeMaster schema (an import, or on dev `Deploy-EbillingSignalLineSubjectSchema.ps1`) must land BEFORE that BFF, or every Signal and Decision Record read answers 400. Then run `Set-EbillingFirmReasonFieldSecurity.ps1` (its profile member is per environment). Task 053 step 5 is the binding check.

**Estimated effort**: 0.5–1 day (owning projects). **Related**: task 026, 053.

---

### DEF-023 — System billing policy: disable only in the model-driven app; not shown in Rule Review; delete refused only by privilege

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before UAT (163) |
| **Filed** | 2026-10-19 |
| **Source** | ebilling-r1 task 049, ontology-r1 review of the D-125 system policy (`039c3fda9..212bdc935`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2106 |

**Description**: D-125 says an admin may disable `BILL-SYSTEM-SUBMISSION`, new versions and deletion are refused, and Rule Review shows it read-only. Today: disabling is possible only in the model-driven app (the ontology admin routes return 404 for a Billing-domain policy, and the billing side has no route); Rule Review lists rule sets only, so it never shows the policy; new versions are refused on BFF routes only; deletion is refused only by the Dataverse privilege, and a deletion stops these Signals while the ontology sweep closes the open ones as PolicyRetired.

**Suggested fix**: A read-only "System rules" section in Rule Review with an admin-only enable/disable route; decide whether a delete guard is needed beyond the privilege.

---

### DEF-024 — Seed-BillingSystemPolicy.ps1 (and Apply-DoLanePolicies.ps1) set no owning team

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before the seed script runs on any environment |
| **Filed** | 2026-10-19 |
| **Source** | ebilling-r1 task 049, ontology-r1 review of the D-125 system policy (`039c3fda9..212bdc935`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2107 |

**Description**: Both scripts leave the policy owned by whoever runs them, which conflicts with ontology decisions D-129 / D-130 on policy ownership.

**Suggested fix**: Bind `ownerid` to the team the ontology decisions name, check it in `-Verify`, and agree the team with ontology-r1.

---

### DEF-025 — SubmissionIntegrity cleanup: unreachable Describe arm; tests loop over authorable types only

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | low (cleanup) |
| **Filed** | 2026-10-19 |
| **Source** | ebilling-r1 task 049, ontology-r1 review of the D-125 system policy (`039c3fda9..212bdc935`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2108 |

**Description**: `BillingRuleCatalogue.Describe` has an unreachable SubmissionIntegrity arm. Several `SubmissionRejectionRuleTests` and consequence-gate checks loop over `BillingRuleCatalogue.Types` (authorable) only, so a future system type would escape them.

**Suggested fix**: Remove the arm or make it throw; add a loop over the system types with the system expectations (Flag only, never reject-capable, never authorable).

---

### DEF-026 — Billing Duplicate Signal keeps the prior invoice's detail after a later Secure transition

| Field | Value |
|---|---|
| **Status** | Open (owner picks the scope and the owning project when scheduled) |
| **Urgency** | medium (information exposure across a secure wall, after the fact) |
| **Filed** | 2026-10-19 (by ontology-r1 as their DI-120, label ontology-deferred) |
| **Source** | ontology-r1 review of the D-170 billing PolicyRetired sweep (`07a2f67b3`); facts from ebilling-r1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2118 |

**Description**: D-112 withholds a Duplicate finding's detail only at write time. A same-partition Duplicate renders `duplicateOf.trackingNumber` (the earlier invoice's tracking number) and, for line reasons, the matched line's date and timekeeper into `sprk_sentence` and the fact snapshot (`BillingRuleCatalogue.cs:131`, `Handlers/DuplicateHandler.cs`). If the prior's core record later becomes Secure, or the subject ends up behind a different wall, the stored Signal still shows that detail. Nothing re-derives the root sets (`SecureChildShareSynchronizer.SecureRootsAboveAsync`) after the write. No other billing type quotes a record outside its own subject family. The D-123 stored-evidence redaction never runs on billing Signals.

**Suggested fix (owner options)**: (a) re-check on a Secure transition and redact or close the affected Duplicate Signals; (b) re-check on read in `SignalReadService`; (c) accept as a known limit (a tracking number plus one line's date and timekeeper).

---

### DEF-027 — Rate card becomes firm-wide when its matter, project or work assignment is deleted outside the BFF

| Field | Value |
|---|---|
| **Status** | Open; fix decided (owner, ontology D-172 / ebilling D-137); fixed by tasks 080 (schema) and 085 (code) |
| **Urgency** | medium (wrong rates applied to a whole firm's invoices) |
| **Filed** | 2026-10-20 |
| **Source** | rule-set scope design for #2129 (`notes/rule-set-scope-design.md`); relationships checked live on dev |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2132 |

**Description**: The rate card's core-record lookups (`sprk_matter_ratecard_matter`, `sprk_project_ratecard_project`, `sprk_workassignment_ratecard_workassignment`) are RemoveLink on dev. `RateCardResolver.cs:13/:138` treats a card with no core-record scope as firm-wide. A matter deleted outside the D-120 BFF delete path (for example in the model-driven app) clears the link, and its matter-specific card silently becomes the firm's general card.

**Suggested fix (owner decision, 2026-10-20, ontology D-172 / ebilling D-137)**: option (b) only. Deleting a matter must never require first deleting its rate cards, because the user may lack permission, so **no Restrict: the three relationships stay RemoveLink**.
- **Explicit scope.** A new `sprk_ratecard.sprk_scope` (Firm / CoreRecord), with a one-time backfill of existing cards, which lists every card it marks Firm so the owner can check it.
- **Fail-closed resolution.** `RateCardResolver` and the rate query in `InvoiceResolutionService` require the lookup for a CoreRecord card. A card whose record is gone applies to nothing and never widens to firm-wide. An empty scope is not used either.
- **Q10 cleanup.** *(Superseded by D-146, 2026-10-20: a core-record delete always keeps the card, unlinked, and never deletes or re-owns it; no notice is sent.)* Was: the card is deleted with its record, or retired if something that survives the delete cites it. Cards orphaned by a delete outside the BFF are listed in an admin view added by 079.
- **Tasks.** **080** adds the column and runs the backfill, after the owner approves the script. **085** changes the resolver, the rate query and the import writer. **079** re-runs the backfill before it deploys the BFF.
- **Design.** `notes/rule-set-scope-design.md`, S6 and §3.5.

**UAC context (2026-10-20, also on #2132)**: the UAC delete design D1 keeps RemoveLink deliberately. D6 removes `prvDelete` on the core tables from Core User and replaces the model-driven app's Delete with the BFF flow. After UAC delete task 1 ships, only admins and holders of the named delete role can delete a core record outside the BFF. That narrows the defect but does not close it: a D-120 preview check or a sweep is still worth having, and (b) remains the structural fix.

---

### DEF-028 — Rate-card import does not check AppendTo on the scoped core record

| Field | Value |
|---|---|
| **Status** | Open (separate fix, owner D-172 Q12) |
| **Urgency** | medium (a card can be scoped to a record the admin cannot file under) |
| **Filed** | 2026-10-20 |
| **Source** | rule-set scope design for #2129 (Q12) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2135 |

**Description**: `RateCardImportService.cs:173` checks only that the scoped matter, project or work assignment exists. It does not check that the caller holds AppendTo on it. The rule-set scope work (081) requires AppendTo on the core record for a record-scoped set.

**Suggested fix**: before any write, probe AppendTo on the core record as the caller. A caller without it gets a 403 and nothing is written. Add a must-fire and a must-not-fire test.

---

### DEF-029 — 105: a planted empty Signal set makes a decision resolve no Signals (known limit)

| Field | Value |
|---|---|
| **Status** | Open (known limit, ontology-r1 2nd pass 2026-10-20) |
| **Urgency** | low |
| **Filed** | 2026-10-20 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2144 |

**Description**: a user with Write on a submission can plant an empty `humanDecisionSignalSet`. The Signals then stay open, and the record names the real caller. The harmful variant (another invoice) is refused since F1-a. Fix: rebuild the set from an app-only read when the stored set is empty, or add field security on `sprk_findingsjson`.

---

### DEF-030 — 105: a Signal deleted between storing the set and writing the record leaves the decision stuck at 409

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | medium (a decision can stay blocked) |
| **Filed** | 2026-10-20 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2145 |

**Description**: 105 has no shrink-and-retry path like 049's. A retry re-uses the stored set that names the missing Signal, so it ends 409 `record_refused` every time. Fix: give 105 the 049 shrink path, plus a test.

---

### DEF-031 — 105: a junk Signal set in the user-writable findings JSON blocks a submission's decision

| Field | Value |
|---|---|
| **Status** | Open (owner and uac-r2 decide) |
| **Urgency** | medium |
| **Filed** | 2026-10-20 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2146 |

**Description**: `sprk_findingsjson` has no field security, and Core User has Write Deep. A planted junk set makes every Override or Return answer 409 `findings_unreadable`. Fix: field security that lets only the writer identity write the column, or move the set to a writer-owned row.

---

### DEF-032 — 115: AddedFee 'not allowed' and 'after N days' wait for extraction of fee kind and due date

| Field | Value |
|---|---|
| **Status** | Open (deferred to ebilling-r2) |
| **Urgency** | low |
| **Filed** | 2026-10-21 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2216 |

**Description**: Task 115 runs only the AddedFee cap form. The other two forms are refused at save and at evaluation (`rule_form_not_supported`) until extraction gives a per-line fee kind and the invoice due date. Known limit: fee lines are matched on expense codes only.

---

### DEF-033 — BFF fails to start with the AI gates off (ChatEndpoints IChatClient)

| Field | Value |
|---|---|
| **Status** | Open (pre-existing; not ebilling scope) |
| **Urgency** | medium |
| **Filed** | 2026-10-21 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2218 |

**Description**: Found by task 099's fix round. With analysis/document-intelligence off, building the endpoint table throws "Failure to infer one or more parameters ... chatClient | UNKNOWN" because `ChatEndpoints` binds an unregistered `IChatClient`. `GateCombinationConstructibilityTests` does not build the endpoint data source, so it misses this. 099's AI-off registration guard therefore composes `CommunicationModule` + `NotificationsModule` directly instead of booting the host.

---

### DEF-034 — 091 hand-off: wire the e-billing file store into the customer stamp

| Field | Value |
|---|---|
| **Status** | Open (owned by customer-provisioning-orchestration-r1) |
| **Urgency** | medium |
| **Filed** | 2026-10-21 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2232 |

**Description**: Task 091's §6 hand-off. Wire `modules/ebilling-file-store.bicep` into `customer.bicep` for the stamp UAMI, then recompile `customer.json` (never hand-merge). Also covered: the per-environment scanning setting, task 090's config keys, network Deny at prod hardening, and the per-stamp verification scripts. Never remove the account-scope grant on ACME or any existing stamp from ebilling.

---

### DEF-035 — D-176: native forms still show Duplicate earlier-invoice detail

| Field | Value |
|---|---|
| **Status** | Decided 2026-10-22: fix (owner). Field-secure sprk_findingsjson (BFF read/write only, also closes DEF-029) and remove it from the submission form; Decision Records store the generic Duplicate sentence. New task after 090. |
| **Urgency** | medium |
| **Filed** | 2026-10-21 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2235 |

**Description**: 119's second adversarial pass found two native surfaces that still show the detail to anyone with Read on the row:
- the `sprk_invoicesubmission` main form, which binds `sprk_findingsjson`;
- the reject Decision Record, which copies plan sentences into `sprk_factsnapshot` (`InvoiceSubmissionAfterSteps.cs:1429`);
- the override and return Decision Records, which copy `reasons[].sentence` the same way (`SubmissionOverrideService.cs:1056`; found in 119 round 2, added 2026-10-21).

The BFF routes (`/review`, `/blocked`) are covered by 119.

---

### DEF-036 — Document profile app-only write skips the ParentLineage refusal

| Field | Value |
|---|---|
| **Status** | Handed to uac-r2 (their #2237; #2236 closed as duplicate). Whichever of 088 and #2237 merges second makes sure `sprk_restrictedbysubmission` is in the filtered set |
| **Urgency** | medium |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2237 (owner uac-r2; #2236 closed as duplicate) |

**Description**: Found by 088's adversarial pass, pre-existing. `DocumentProfileOutputMapper.BuildFields` (`DocumentProfileOutputMapper.cs:50-106`) maps every top-level property of the Document Profiler output to a `sprk_document` column of the same name. Two app-only writers use it, at `AppOnlyAnalysisService.cs:570` and `PublicContracts/DocumentProfileAi.cs:169`. Neither runs the `ParentLineage` app-only refusal that protects the access columns and, after 088, `sprk_restrictedbysubmission`. Fix options: run the refusal on the dictionary, or limit the mapper to an allowlist of profile columns.

---

### DEF-037 — /review shows a submission planted by a user-writable sprk_invoice link

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | medium |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2254 |

**Description**: Found by ontology-r1's 119 review; it dates from task 100. `InvoiceReviewReadService.ReadSubmissionAsync` takes the latest submission whose user-writable `sprk_invoice` points at the invoice, and has no planted-link check like `/blocked`'s (`SubmissionOverrideService.cs:458-459`). The D-176 gate still applies, so this is an integrity problem, not a disclosure. Fix: accept a submission only when `InvoiceIdFor(submission.Id)` equals the invoice id, with a must-fire test.

---

### DEF-038 — File store has no blob soft delete or versioning

| Field | Value |
|---|---|
| **Status** | Decided 2026-10-22: 30 days, all environments incl. dev; module change on task/ebill-2255-softdelete; applied on dev 2026-10-22 (091 §8.8); other environments get it through the stamp wiring (#2232) |
| **Urgency** | medium |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2255 |

**Description**: Raised by uac-r2 in the 091 review. `ebilling-file-store.bicep` leaves blob soft delete and versioning off, and the BFF identity can delete blobs (the 091 probe returned 202). A wrong delete of an invoice file cannot be recovered. Options: soft delete or versioning with retention in the module, at least for non-dev environments, with each environment's value coming in through the stamp wiring (#2232).

---
### DEF-039 — Rule-set firm-visibility columns are not refused to app-only generic writers

| Field | Value |
|---|---|
| **Status** | Open: do it with the first consumer of RuleSetService.GetFirmClauseAsync |
| **Urgency** | medium |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2274 |

**Description**: Raised by uac-r2 in the 081 review. `sprk_ruleset.sprk_visibletofirms`, `sprk_scope` and `sprk_firm` decide which firm sees clause quotes (`GetFirmClauseAsync`), which has no consumer yet. When one lands, add the three columns to `InheritedAccessPermission.AccessColumns` with a must-fire test.

---
### DEF-040 — Creator-person schema script: a raced secure FAIL is not reported on re-run

| Field | Value |
|---|---|
| **Status** | Open: needs a design (legitimate stamps on a secured column cannot be told apart afterwards) |
| **Urgency** | low |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2280 |

**Description**: Raised by uac-r2 in the 081 re-check. `Set-ChildRecordCreatorPersonSchema.ps1` fails a run when a value lands between its count and the secure, but a re-run sees the column secured and exits 0; values written on columns secured before the gate existed are never counted. Dev was checked read-only (9 existing values, all app-user stamps).

---
### DEF-041 — Rule sets: 403 vs 404 reveals a Procedure-owned rule set exists

| Field | Value |
|---|---|
| **Status** | Open: known limit accepted by uac-r2; a uniform 404 if wanted |
| **Urgency** | low |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2284 |

**Description**: Raised by uac-r2 in the 081 round-2 re-check. A billing admin without Read on a Procedure gets 403 on its rule set where a missing set gives 404; the 409 procedure_owned answers reveal the same. The caller already holds the GUID and the billing-admin privilege.

---
### DEF-042 — Basic/Core User read sprk_ruleset and sprk_policy around the BFF Procedure Read gate

| Field | Value |
|---|---|
| **Status** | Open: OWNER decision on the issue (should Billing policy and rule-set rows be hidden from internal users using Dataverse directly?). ontology: Read on sprk_policy is intended (Console worklist outer link) and must stay; any fix is row scoping or a split |
| **Urgency** | medium |
| **Filed** | 2026-10-22 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2285 |

**Description**: Raised by uac-r2 in the 081 round-2 re-check; pre-existing. Spaarke Basic User holds prvReadsprk_ruleset and prvReadsprk_Policy at Deep, Spaarke Core User prvReadsprk_ruleset at Deep, so internal users read rule sets and policies in Dataverse whether or not they can Read the Procedure. UAC 191 covers flagged-external users only.

---

### DEF-043 — Duplicate detail after a line or subject re-point (fixed: number-only Duplicates show the generic sentence)

| Field | Value |
|---|---|
| **Status** | Fixed on work/ebilling-r1 (0bba46c1ba, b2f9c28e61), pending uac-r2 / ontology re-check; K1 below accepted as a fail-closed known limit |
| **Urgency** | low |
| **Filed** | 2026-10-23 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2294 |

**Description**: sprk_billingevent.sprk_invoice stays writable by generic paths (D-96's same-core re-attach). A line can also be moved within its core record by a person's run, or by a native edit from a user with Write. The fix, under uac-r2's decision:
- A Duplicate identified only by tracking number always shows the generic withheld sentence, on the worklist, /review and /blocked. The tracking-number lookup was removed.
- `duplicate.line_same_invoice` is judged by the caller's Read of the line's own invoice, inside the Signal's frozen core-record bound.
- Findings carrying the earlier invoice id keep the readability check.
- sprk_signal's regarding pair, its subject lookups and its core pair are refused to app-only generic writers.

K1 (ontology): a D-96 re-file across core records withholds line Duplicates permanently. It fails closed.

---|---|
| **Status** | Open: known limit accepted by ontology-r1 (F4(b) design); bounded by core record on write and read |
| **Urgency** | low |
| **Filed** | 2026-10-23 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2294 |

**Description**: sprk_billingevent.sprk_invoice is not refused to app-only generic writers, because D-96's detach/move/re-attach sequence writes it through generic paths. InvoiceFamilyCoreRecordGuard refuses a cross-core-record re-point, and DuplicatesShownAsync shows nothing when the invoice's core record differs from the Signal's frozen sprk_corerecordid. Residual: a generic re-point within the same core record means the Duplicate is judged in that invoice's submission's firm. Exploiting it also needs a tracking-number match there.

---
### DEF-044 — Stored file: ETag pin misses a reused staged blob

| Field | Value |
|---|---|
| **Status** | Open: known limit (uac-r2 090 round-2 K2); fix before StoredFileReconcileJob is enabled anywhere |
| **Urgency** | medium |
| **Filed** | 2026-10-23 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2296 |

**Description**: Staging that reuses an existing blob doesn't write it, so its ETag is unchanged (StoredFileReconcileJob.cs:446-453, StoredFileService.cs:342). If a deterministic-id create is retried more than 24 h after staging, reconcile deletes the orphan, the retry reuses it, and the flip answers BlobMissing: the row stays not servable, with a refusal and an alert. DeleteStagedBlobAsync (null ifMatch) has the same gap. Fix: touch metadata on reuse; carry the ETag in StagedBlob and pin DeleteStagedBlobAsync.

---
### DEF-045 — Stored file: parent-gone pass doesn't check the blob path's parent

| Field | Value |
|---|---|
| **Status** | Open: defence in depth (uac-r2 090 round-2 K3); not reachable after F4 |
| **Urgency** | low |
| **Filed** | 2026-10-23 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2297 |

**Description**: StoredFileReconcileJob.cs:283-321 deletes the blob named by pathFile == row.Id without checking that pathParent is one of the gone lookups. Fix: also require row.Parents.Contains(pathParent) or ParentConfirmedGoneAsync(pathParent).

---
### DEF-046 — Deploy-EbillingFormsViews.ps1 navigation step crashes on dev

| Field | Value |
|---|---|
| **Status** | Open; worked around by task 122's -FormsOnly |
| **Urgency** | medium |
| **Filed** | 2026-10-23 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2312 |

**Description**: A full run fails in step 3 (Navigation) with InsertAfter "The reference node is not a child of this node". The live sprk_MatterManagement sitemap has sprk_invoice in two areas. It fails after the forms/views steps and before publish. Fix: a deterministic anchor or an early refusal, and/or correct the sitemap (check first who added the second entry).

---
