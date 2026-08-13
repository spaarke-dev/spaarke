# Spaarke Solution — Authoritative Component Inventory (Bill of Materials)

> **Created**: 2026-08-12
> **Owner**: customer-provisioning-orchestration-r1
> **Purpose**: The authoritative bill-of-materials for one Spaarke customer environment — every deployable/seedable artifact, its deploy mechanism, dependency order, and per-customer-vs-shared disposition. This is the dependency that the provisioning package (`Provision-Customer.ps1` → control plane) must fully cover.
> **Status**: Reconnaissance-grade → being hardened to authoritative. Counts marked ✅ come from the machine source of truth; counts marked ⚠️ are agent-surveyed and need one verification pass against a live env.

---

## 0. Sources of truth (how this inventory was assembled)

| Source | What it authoritatively defines | Path |
|---|---|---|
| **`Build-SpaarkeMaster.ps1`** ✅ | The machine composition of the full Dataverse component set — entities, web resources, option sets, PCF, roles, env-vars, MDA app/sitemap. Discovers by `sprk_` prefix + explicit IDs; **386 total components** (incl. auto-added subcomponents). | `scripts/Build-SpaarkeMaster.ps1` |
| `Deploy-DataverseSolutions.ps1` | Managed-solution import set + dependency order | `scripts/` |
| `src/client/pcf/*` | PCF control source (33 control folders) | repo tree |
| `src/solutions/*`, `src/client/code-pages/*` | Code-page SPA source | repo tree |
| `infrastructure/bicep/**` | Azure resource stamp (modules + model1/model2 stacks) | repo |
| `docs/data-model/entity-relationship-model.md` | Entity schema + relationships | repo |
| `scripts/seed-data/**`, `infra/dataverse/**` | Config/seed data (the layer solutions don't carry) | repo |
| `.claude/skills/spe-integration/SKILL.md` | SharePoint Embedded provisioning | repo |

**Structural fact that governs everything below:** managed solutions carry table/column **definitions** but **not configuration rows**. A fully imported solution is still a non-functional app until the **config/seed layer (§9)** is loaded. This is the single most important thing this inventory exists to make visible.

---

## 1. Dataverse solution layer

Imported in dependency order by `Deploy-DataverseSolutions.ps1` (~10 managed solutions). `SpaarkeCore` first (entities, option sets, security roles); then web resources; then feature solutions.

| # | Solution | Contents | Import order | Deploy mechanism |
|---|---|---|---|---|
| 1 | **SpaarkeCore** | Root entities, option sets, security roles, sitemap, MDA app shell | **1st (dependency root)** | `pac solution import` (managed) |
| 2 | webresources | JS + ribbon web resources | 2nd | solution import / web-resource upload |
| 3 | spaarke_document_management | `sprk_container`, `sprk_document` | after core | solution import |
| 4 | spaarke_documents | `sprk_aiprofile`, `sprk_document`, `sprk_documentassociation`, `sprk_documentitem` + plugin assemblies | after core | solution import |
| 5 | **Spaarke.CustomApiProxy** | Custom API proxy plugin registration (`BaseProxyPlugin`, `GetFilePreviewUrlPlugin`) | after core | solution import (Managed=Both) |
| 6–10 | ~5 feature solutions | Wrap code pages + PCF + feature schema (LegalWorkspace, AI/Analysis, Communication, Reporting, etc.) | last | solution import |

> **ALM note (ADR-027, amended 2026-06-02):** the repo currently uses **unmanaged** solutions everywhere for dev; **managed** packaging for customer environments is locked as decision **D1** but the scripted export→fix→pack-managed→verify pipeline is a build item (r1 handler **H6**). `src/dataverse/solutions/*` folders are **unpacked skeletons** (`Entities/` are `.gitkeep` stubs) — not the sole source of truth; schema is provisioned by a mix of managed-ZIP import and Web-API/PAC `Deploy-*.ps1` scripts.

---

## 2. Dataverse schema — entities

**87+ custom `sprk_*` entities** ✅ (discovered by prefix in `Build-SpaarkeMaster.ps1`) + **3 customized standard entities** (`account`, `contact`, `businessunit` — metadata + `sprk_` columns). Grouped by seed obligation:

### 2A. Transactional entities — start EMPTY (filled at runtime / data migration)
- **Core**: `sprk_matter`, `sprk_project`, `sprk_organization`, `sprk_document`, `sprk_fileversion`, `sprk_container`, `sprk_documentassociation`, `sprk_documentitem`
- **Financial**: `sprk_invoice`, `sprk_billingevent`, `sprk_budget`, `sprk_budgetbucket`, `sprk_kpiassessment`, `sprk_spendsignal`, `sprk_spendsnapshot`
- **Activity/Comms**: `sprk_event`, `sprk_eventset`, `sprk_workassignment`, `sprk_communication`, `sprk_communicationaccount`, `sprk_todo`, `sprk_notificationoutbox`
- **AI runtime**: `sprk_analysis` + children (`sprk_analysisaction` [instance], `sprk_analysischatmessage`, `sprk_analysisoutput`, `sprk_analysisknowledge`, `sprk_analysisskill`, `sprk_analysistool`, `sprk_analysisworkingversion`, `sprk_analysisemailmetadata`)

### 2B. Reference `*_ref` entities — NEED seed rows (option-list style)
`sprk_mattertype_ref`, `sprk_mattersubtype_ref`, `sprk_recordtype_ref` (**polymorphic discriminator — load-bearing for regarding lookups**), `sprk_practicearea_ref`, `sprk_eventtype_ref`; AI type lookups `sprk_aiactiontype`/`sprk_analysisactiontype`, `sprk_aiknowledgetype`, `sprk_aitooltype`, `sprk_aiskilltype`, `sprk_aioutputtype`, `sprk_airetrievalmode`, `sprk_analysisdeliverytype`.

### 2C. Configuration/definition entities — NEED seed rows (app is non-functional without them → see §9)
`sprk_gridconfiguration`, `sprk_fieldmappingprofile` + `sprk_fieldmappingrule`, `sprk_workspacelayout`, `sprk_analysisaction` (definitions), `sprk_analysisplaybook`, `sprk_playbookconsumer` (**the single AI routing surface, ADR-039**), `sprk_analysistool`, `sprk_aimodeldeployment`, `sprk_aiknowledgesource`, `sprk_chartdefinition`.

### 2D. M2M intersection tables (auto-added as relationship subcomponents; do not seed directly)
`sprk_analysis_knowledge/skill/tool`, `sprk_analysisplaybook_action/analysisoutput/mattertype`, `sprk_playbook_knowledge/skill/tool`, `sprk_playbooknode_skill/knowledge/tool`, `sprk_matter_contact`, `sprk_event_contact`, `sprk_project_contact`, `sprk_project_organization`, `sprk_matter_organization`, `sprk_project_sprk_matter`.

> ⚠️ **Verification item**: the full 87-entity roster is not enumerated in code (discovered by prefix). A complete named catalog should be exported from a live env (`EntityDefinitions?$filter=startswith(LogicalName,'sprk_')`) and pinned here.

---

## 3. Other Dataverse metadata components (from `Build-SpaarkeMaster.ps1` ✅)

| Component type | Count | Notes |
|---|---|---|
| Web resources (`sprk_*`) | **195** | JS + code-page bundles + ribbon; type 61 |
| Global option sets (`sprk_*`) | **24** | type 9 |
| Environment variable **definitions** (`sprk_*`) | **21** | type 380 — client surfaces read these at runtime |
| Environment variable **values** | per-env | type 381 — **7 are set per-customer** by `Provision-Customer.ps1` step 8 (see §9) |
| Security roles ("Spaarke", root BU) | **7** | type 20 |
| MDA app | **1** | `sprk_MatterManagement` (type 80) |
| Sitemap | **1** | `sprk_MatterManagement` (type 62) |
| App module components | **14** | from `SpaarkeCorporateCounselApp` (type 10075) |
| Customized standard entities | **3** | account, contact, businessunit (metadata + `sprk_` columns) |
| **TOTAL solution components** | **386** | incl. auto-added subcomponents |

---

## 4. PCF controls

**33 control folders** in `src/client/pcf/`. Only **7 are confirmed in-use and included in SpaarkeMaster** ✅; the rest are either feature-solution-scoped, orphaned, or retired. This is a real signal for the provisioning package — do not assume all 33 ship.

### 4A. Confirmed in-use (in SpaarkeMaster, explicit IDs)
`DocumentRelationshipViewer`, `EventFormController`, `RelatedDocumentCount`, `SpeDocumentViewer`, `VisualHost`, `SemanticSearchControl`, `EventAutoAssociate`.

### 4B. Explicitly EXCLUDED / orphaned per `Build-SpaarkeMaster.ps1`
- Not on forms: `UpdateRelatedButton`, `EmailProcessingMonitor`, `ThemeEnforcer`, `RegardingLink`
- Removed from forms: `ScopeConfigEditor`, `UniversalDocumentUpload`
- **Broken** (bad `styles.css` web-resource ref): `UniversalDatasetGrid` (also sunsetting in DataGrid Framework Phase F)

### 4C. Retired
`AssociationResolver` (superseded by `RegardingResolver`).

### 4D. Remaining controls (feature-solution-scoped; verify per-feature)
AnalysisBuilder, AnalysisWorkspace, CommunicationActions, CommunicationAttachments, CommunicationConnections, CommunicationConversationPanel, CommunicationMessageActions, CommunicationTimeline, CommunicationTimelineRegarding, DrillThroughWorkspace, DueDatesWidget, EventCalendarFilter, MatterHeader, PlaybookBuilderHost, RegardingResolver, ScopeConfigEditor, SpaarkeGridCustomizer, SpeFileViewer, TrackingFieldTrio, UniversalQuickCreate.

> ⚠️ **Verification item**: reconcile the 33 source folders against what each feature solution actually packs. The 7-vs-33 gap is the kind of thing that silently breaks a customer standup.

---

## 5. Code pages (React/Vite SPAs → Dataverse web resources)

Deployed as web resources via `code-page-deploy` / `Deploy-*CodePages.ps1`. ~28 SPAs in `src/solutions/` + ~6 in `src/client/code-pages/`.

**Flagship / active**: **SpaarkeAi** (primary AI workspace host), DailyBriefing (Pattern D dual-use), Reporting, SmartTodo, EmailPage, Notepad, PlaybookLibrary, AllDocuments, FindSimilarCodePage, SpeAdminApp, WorkspaceLayoutWizard, EventsPage/EventDetailSidePane, CalendarSidePane, communication/invoice/KPI pages, SummarizeFilesWizard.
**Wizards (7, thin wrappers on shared WizardShell)**: CreateEvent / CreateInvoice / CreateMatter / CreateProject / CreateReportCard / CreateTodo / CreateWorkAssignment; plus DocumentUploadWizard.
**Retired (kept as component library, not standalone)**: **LegalWorkspace** — SpaarkeAi is the host (OC-R4-05).
**Non-SPA in folder**: CopilotAgent (M365 declarative agent), EventCommands (ribbon JS), SpaarkeCore/spaarke_insights (solution staging), DemoRegistration, TodoDetailSidePane.

---

## 6. Dataverse plugins

| Assembly | Plugins | Registration | Path |
|---|---|---|---|
| `Spaarke.Dataverse.CustomApiProxy` (.NET FW 4.6.2) | `BaseProxyPlugin` (base), `GetFilePreviewUrlPlugin`; helper `SimpleAuthHelper` | Plugin assembly + step registration on Custom API messages; Custom API message definitions must exist in target org | `src/dataverse/plugins/Spaarke.CustomApiProxy/` |

Proxies Dataverse Custom API calls → BFF (e.g., file-preview URL generation).

---

## 7. BFF + Azure resource stamp (per customer)

BFF `Sprk.Bff.Api` (.NET 8 Minimal API, ~35 DI modules / 269 registrations) → **Azure App Service** (`bff-deploy` / `Deploy-BffApi.ps1`; **≤60 MB compressed** ceiling). Backing Azure resources (per `infrastructure/bicep/**` + `auth-azure-resources.md`):

| Resource | Purpose | Disposition (see §11) |
|---|---|---|
| App Service + Plan | BFF host | 🟡 shared (Model 1) / 🔴 dedicated (Model 2 / r1 D3) |
| User-Assigned Managed Identity | Server-outbound identity (Graph app-only, Dataverse, Cosmos, KV) | 🔴 per-deployment |
| Key Vault | All secrets; App Service resolves `@Microsoft.KeyVault(...)` via UAMI | 🔴 dedicated (cheap) |
| Azure OpenAI (+ model deployments) | LLM inference, embeddings | **decision point** — see §11 |
| Azure AI Search | Vector + semantic RAG (7-index catalog) | **decision point** — see §11 |
| Cosmos DB (serverless) | AI sessions, prompts, audit, memory, feedback; **also ProvisioningRun state (r1 D13)** | 🟡 partition by `/tenantId` |
| Redis | OBO token cache (ADR-009), session state | 🟡 key-prefix / 🔴 Premium+VNet |
| Service Bus | Job queues + membership topic (ADR-034) | 🟢 shareable |
| Storage | temp/doc-processing/AI-chunks | 🟡 container / 🔴 doc content |
| App Insights + Log Analytics | Telemetry, audit, UAC-DIAG | 🟢 shareable |
| Content Safety, Doc Intelligence | Prompt-injection detection, OCR | 🟢 shareable (stateless) |
| SignalR (optional/Null-Object) | Notifications spine realtime | 🟢 |
| Power BI Embedded | Reporting module | per-customer workspace |

> **RBAC provisioning steps (each a known failure point):** UAMI → KV Secrets User; **`keyVaultReferenceIdentity` PATCHed to UAMI** (silent-failure trap); UAMI → **Cognitive Services User** (wildcard, narrower OpenAI-User role insufficient for `kind=AIServices`); UAMI → Cosmos Data Contributor; **MI registered as Dataverse Application User** in every env (silent 403→500 if omitted); ~11 Graph app-role grants replicated onto MI; **two Exchange ApplicationAccessPolicies** (app-reg + MI); GitHub OIDC → Contributor. Staging slot has a **different MI** → grant KV RBAC to both slots.

---

## 8. Static Web Apps / add-ins / external SPA

| Artifact | Deploy target | Current isolation |
|---|---|---|
| Outlook add-in | Azure Static Web App (`office-addins-deploy` / SWA CLI) | **single shared SWA today** |
| Word add-in (Compose) | Azure Static Web App | single shared SWA today |
| external-spa (Entra External ID portal) | Static Web App / Power Pages code site | single shared; **BFF host baked at Vite build time** (rebuild+redeploy on host change) |
| M365 Copilot declarative agent | CopilotAgent manifest package | 1 |

> ⚠️ Office add-ins + external portal are **single shared instances**, not per-customer. If a customer needs an isolated add-in/portal, that provisioning **does not exist yet** (gap).

---

## 9. Configuration & seed data — THE CRITICAL LAYER

Managed solutions do **not** carry these rows. A fresh environment imports solutions and creates SPE containers but **grids render nothing, wizards don't map fields, the AI layer is dark, and the workspace won't render** until these run. **This layer is decoupled from `Provision-Customer.ps1` today (top gap — see PROJECT-UPDATE §6, Gap 1).**

| Config data | Table(s) | Seeder | Order |
|---|---|---|---|
| AI type lookups | `sprk_ai*type`, `sprk_analysisactiontype` | `Deploy-TypeLookups` via `Deploy-All-AI-SeedData.ps1` (`type-lookups.json`) | 1 (AI prereq) |
| AI Actions (defs) | `sprk_analysisaction` | `infra/dataverse/actions/*.action.json` + `Deploy-AnalysisAction.ps1` (**current R7**); legacy MVP `scripts/seed-data/actions.json` | 2 |
| Analysis tools | `sprk_analysistool` | `infra/dataverse/sprk_analysistool-*-row.json` (~40) + `Seed-TypedHandlers.ps1` | 3 |
| Knowledge / skills / output-types | `sprk_aiknowledgesource`, `sprk_analysisskill`, `sprk_aioutputtype` | `Deploy-Knowledge/Skills/OutputTypes.ps1` | 4 |
| Playbooks | `sprk_analysisplaybook` (+ intersections) | `Deploy-Playbooks.ps1` (`playbooks.json`); multinode in `infra/dataverse/playbooks/` | 5 |
| **Bindings (AI routing surface)** | `sprk_playbookconsumer` | `Seed-PlaybookConsumers.ps1`, mirror `infra/dataverse/sprk_playbookconsumer-rows.json` | 6 |
| DataGrid configs | `sprk_gridconfiguration` | authored records (`sprk_configjson`); maker/MCP export | any |
| Field-mapping profiles/rules | `sprk_fieldmappingprofile` + `sprk_fieldmappingrule` | authored (OOB forms); Web-API seeding recipe in `FIELD-MAPPING-ADMIN-GUIDE.md` | any |
| System workspace layouts | `sprk_workspacelayout` (isSystem) | `Deploy-SystemWorkspaceLayouts.ps1` | any |
| Chart definitions | `sprk_chartdefinition` | `Create-*ChartDefinitions.ps1` / `Load-DemoSampleData.ps1` | any |
| AI model deployments | `sprk_aimodeldeployment` | env-specific; points at the customer's Azure OpenAI deployment | after infra |
| **7 Dataverse env-var values** | `environmentvariablevalue` | `Provision-Customer.ps1` step 8 | after infra |
| AI Search indexes | (Azure) | `ai-search/Deploy-AllIndexes.ps1` (7 indexes) | after infra |

**One-shot for AI layer**: `scripts/seed-data/Deploy-All-AI-SeedData.ps1` (type-lookups → actions → tools → knowledge → skills → playbooks → output-types), then `Seed-PlaybookConsumers.ps1`.

**7 per-customer env-var values** (client surfaces are non-functional without them): `sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId`.

> ⚠️ **Drift risk**: two competing AI seed sources — `scripts/seed-data/*.json` (2026-01 MVP) vs `infra/dataverse/**` (R7 current). Confirm authoritative source per environment before seeding.

---

## 10. SharePoint Embedded (SPE)

Policy: **one container per client/business unit** (ADR-005 flat storage; hierarchy in Dataverse `sprk_documentassociation`; access via `SpeFileStore` facade, ADR-007).

| Step | Automated? | Tooling | Frequency |
|---|---|---|---|
| Create container **type** (owned by BFF API app) | Semi (needs global-admin consent) | `Create-NewContainerType.ps1` | one-time per Entra tenant |
| Register owning app + MI with container type | Yes | `Register-BffApiWithContainerType.ps1`, `Register-BffMiWithContainerType.ps1` | one-time per tenant |
| Register container type in **consuming tenant** | Manual consent + script | `RegisterContainer.ps1` | one-time per tenant |
| Cert bootstrap (KV → register) | Yes | `Import-And-Register.ps1` | one-time |
| Create per-customer **container** | Yes | `Provision-Customer.ps1` step 10 (Graph `POST /storage/fileStorage/containers`) | per customer |
| Additional BU containers | Yes | `New-BusinessUnitContainer.ps1` | on demand |
| Wire container ID into Dataverse | Yes | `Set-ContainerId.ps1` + `sprk_SharePointEmbeddedContainerId` | per customer |

> ⚠️ **Live drift trap**: container creation now **403s on the delegated token** ("public client not allowed") — needs a **confidential-client app-only** token. Documented as not-yet-fixed in the scripts. Also: SPE content **always lands in the consuming (customer) tenant's** SharePoint, and container-type setting changes take **up to 24h** to replicate.

---

## 11. Per-customer provisioning BOM — summary

| Deploy mechanism | Count | Disposition |
|---|---|---|
| Dataverse managed-solution import | ~10 solutions (386 components) | 🔴 per-customer env |
| PCF controls | 7 in-use (of 33 built) | shipped inside solutions |
| Code-page web resources | ~28 SPAs (+~6) | shipped inside solutions |
| Dataverse plugin assembly | 1 (2 plugins) + Custom API msgs | inside CustomApiProxy solution |
| App Service (BFF) | 1 + ~10 backing Azure services | see §7 |
| Static Web Apps | 2–3 (Office add-ins + external portal) | ⚠️ shared today |
| Config/seed layer | ~12 seed operations (§9) | 🔴 per-customer, **not yet in orchestrator** |
| SPE container | 1+ per customer | 🔴 per-customer |
| M365 Copilot agent | 1 | shared/per-customer TBD |

### Shared-vs-dedicated decision (the open architectural fork)
- 🔴 **Always dedicated (cheap/customer-owned)**: Dataverse, SPE, Key Vault secrets, Storage, MI, Entra app config, CIAM.
- 🟡 **The cost levers (fixed floors)**: **App Service Plan, Azure OpenAI (provisioned TPM), Azure AI Search (fixed tier)** — shared in Model 1; **dedicated under r1 decision D3**.
- 🟢 **Safely shared**: Service Bus, App Insights/Log Analytics, Content Safety, Doc Intelligence.

> r1 **D3 (no shared resources) + D4 (subscription per customer)** dissolve the cost-allocation problem (native per-customer Azure bill) at the price of a per-customer fixed floor. If a **shared trial/SMB tier** is added, it requires an **APIM/gateway token-metering layer** (per-tenant attribution) + fixed-cost allocation for AI Search. See PROJECT-UPDATE §4–5.

---

## 12. Inventory gaps / verification backlog

1. ⚠️ Export and pin the **complete named 87-entity roster** from a live env.
2. ⚠️ Reconcile **33 PCF folders → 7 in-use** and map each remaining control to the feature solution that packs it (or mark retired).
3. ⚠️ Resolve the **two-source AI seed drift** (`scripts/seed-data` MVP vs `infra/dataverse` R7) → single authoritative source.
4. ⚠️ Produce a **single validated env-var/app-setting manifest** reconciled against BFF code `[Required]` annotations (today split across two docs; ~25 settings found only by startup exceptions).
5. ⚠️ Confirm **managed-solution export/fix/pack** pipeline (r1 H6) covers all 10 solutions.
6. ⚠️ Decide per-customer vs shared for **Office add-ins, external SPA, Copilot agent** (currently shared).
