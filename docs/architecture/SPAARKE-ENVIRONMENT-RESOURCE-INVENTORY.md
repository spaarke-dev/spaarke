# Spaarke Customer Environment Resource Inventory

> **Authored**: 2026-08-31 by `customer-provisioning-orchestration-r1` (owner-mandated inventory ahead of tasks 213.7 / 186)
> **Rewritten**: 2026-09-30 by the same project, task 236 — to the **dedicated Model 1 target** the owner confirmed on
> 2026-09-30 after D-12 / D-13 retired the shared Model 1 tier. Remaining work: [`model1-dedicated-remediation-plan.md`](../../projects/customer-provisioning-orchestration-r1/notes/model1-dedicated-remediation-plan.md).
> **Purpose**: one owner-reviewable page listing every resource a Spaarke customer needs — what it is, whether it is
> **dedicated** to the customer or **shared** across customers, its name, what creates it, and whether today's code
> already matches the target.
> **Authority**: source-cited inline. Where this document disagrees with the cited source, the source wins and this doc
> is corrected. Where today's code does not yet match the target, the row says so and names the task that closes it —
> it never presents the target as already built.

---

## How to read this document

**The target (owner decisions 2026-09-30, plan §2):**

| | Model 1 — **in scope** |
|---|---|
| Hosting tenant | Spaarke's Entra / Azure / Power Platform tenant |
| Azure | **Own subscription + resource group per customer** (ADR-027 amended 2026-09-28), Spaarke-owned and Spaarke-billed |
| Dataverse | **Own environment per customer**, **managed** solutions |
| SharePoint Embedded | Containers in Spaarke's tenant, container type `Spaarke Model 1` (`standard` — Spaarke pays) |
| Users | **B2B guests** in Spaarke's tenant; **Spaarke pays** — pay-as-you-go on the stamp subscription, no per-user licences (owner 2026-10-07, T232) |
| Environments per customer | **prod only** (`{env}` = `prod`) |

**Model 2** (customer-tenant stamp, `directToCustomer` container type) is **out of scope** for this project (D3) —
a variant of Model 1 to address when a Model 2 customer arrives. It has no column here.

**Columns used in every table:**

- **Deployment** — **Dedicated** = one per customer, in the customer's own subscription / environment.
  **Shared** = operated by Spaarke once and used by many customers; allowed only where there is no customer data
  needing segregation and no subscription requirement (plan §1 rule).
- **Status vs target** — ✅ current code matches the target · 🔲 **T2xx** = current code differs; the named task in
  the [plan §7](../../projects/customer-provisioning-orchestration-r1/notes/model1-dedicated-remediation-plan.md) closes it.

**`{customerId}` in every name** follows the customerId standard (D-14, `unified-access-control-r2` task 124,
master `3293ee421`): **3–8 characters, lowercase letters and digits, starting with a letter**
(`^[a-z][a-z0-9]{2,7}$`). Eight is binding — the Key Vault name `sprk-{customerId}-{env}-kv` must fit 24 characters
without ending in a hyphen — and the character rule prevents silent storage-account name collisions (hyphens are
stripped). ARM can enforce only the length; the character rule must be enforced at intake. Rule and derivation:
[`AZURE-RESOURCE-NAMING-CONVENTION.md`](AZURE-RESOURCE-NAMING-CONVENTION.md) § "The `customerId` standard".
🔲 **T237** — intake validation, registry column `MaxLength = 8`, re-issuing existing non-compliant values
(e.g. `trial-2026-08-18`) and recording any abbreviation once on the registry row.

**Three time-scales** (the "Created by" column always makes clear which):

1. **Manual prerequisite** — the operator creates it before starting `/provision-environment` (D4).
2. **Per-customer provisioning run** — the L2 control plane's handlers H0–H14, dispatched by
   `/provision-environment {customerId}` → `POST /api/runs`.
3. **Once per Spaarke platform** — shared infrastructure Spaarke stands up once and every run uses.

---

## Manual prerequisites (D4)

Created by the operator before the provisioning run; the run verifies them and never creates them.

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Azure subscription for the customer | Dedicated | Operator-chosen (D4) | Operator (PRQ-S-00); **H1** verifies it is reachable in the run's tenant and holds no other customer's stamp | ✅ **T228** — `subscriptionId` required at intake for every model; nothing defaults or shares one |
| Dataverse environment for the customer | Dedicated | Operator-chosen (D4) | **Operator creates it** (PRQ-C-09; domain `spaarke-{customerId}`) and supplies its URL at intake (`dataverseEnvUrl`); **H5 adopts** it and never creates one (owner 2026-09-30, plan Q1) | ✅ **T228** — the BAP creator and H0's creation-rate probe are removed |
| L2 control-plane UAMI role assignments on the customer subscription | Dedicated (one set per subscription) | — | Operator, as part of preparing the subscription: **Owner** for the L2 UAMI (PRQ-S-04 — `modules/controlplane-subscription-rbac.bicep`, deployed at the customer's subscription; owner decision 2026-10-06 — customer.bicep writes role assignments) | ✅ **T228** — no longer deployed on the platform subscription |

---

# Dataverse

## Environment and application users

| Resource | Deployment | Naming / identity | Created by | Status vs target |
|---|---|---|---|---|
| Customer Dataverse environment | Dedicated | See manual prerequisites | Operator creates (+ the L2 Worker identity as System Administrator application user, PRQ-C-09); H5 adopts | ✅ T228 |
| App user — the customer's **BFF app registration** (D-13) | Dedicated | `systemuser` where `applicationid` = the customer's `spaarke-bff-api-{customerId}` appId; System Administrator, root BU | **H10** | ✅ H3 creates one app registration per customer unconditionally (task 222, `H3EntraAppRegHandler.cs`) |
| App user — the customer's **UAMI** | Dedicated | `systemuser` where `azureactivedirectoryobjectid` = UAMI **principalId** (never clientId); System Administrator, root BU | **H10** post-step; trap **T2** query verifies exactly one row | ✅ per-customer UAMI `mi-spaarke-{customerId}-prod` (`customer.bicep:214`) |
| Graph app-role grants on the UAMI (~15) | Dedicated | Per [`GraphAppRoles.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs) | **H10** (trap **T3**) | ✅ — 11 of 14 null `AppRoleId` GUIDs must be completed before the first production customer (project MUST rule) |
| Custom security roles `Spaarke User`, `Spaarke AI Analysis User`, `Spaarke AI Analysis Admin` | Dedicated (shipped in the solution) | Defined in the solution package | **H6** (solution import) | ✅ **T218e** (2026-10-08) — the root-unit roles ship in SpaarkeMaster 1.2.0.0, incl. "Spaarke Office Add In User". "Secure Record Owner" does NOT: it is contained in the Secure Record business unit by design (SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.2), Dataverse packages no child-unit role, and H7b creates it per environment (**T256**) |
| Customer users | Dedicated (guest accounts in Spaarke's tenant) | **B2B guests** (D2); access paid **pay-as-you-go** on the stamp subscription (`PRQ-C-11`) | **H11** | ✅ **T232** — invites (an existing guest reused, no second mail), adds each redeemed guest to `sprk-{customerId}-users`, makes it a Dataverse user with the Spaarke role. Live behaviour (on-demand user add under PAYG) is a T186 check |
| Environment security group `sprk-{customerId}-users` | Dedicated | Entra security group set on the Dataverse environment — keeps other customers' guests out | Operator (`PRQ-C-10`); H11 adds members | ✅ T232 |
| Email (Graph API mailbox configuration) | Dedicated | — | Configured per customer **outside this project** (D2) | Out of scope |

## Solution package

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Spaarke Dataverse solution package | Dedicated (imported into each customer environment) | **One solution, `SpaarkeMaster`** (publisher `Spaarke`, prefix `sprk`) — **managed by default, unmanaged on explicit instruction** | **H6** | 🟡 **T218** — defined 2026-10-07 (ADR-027 §3 amended); H6 imports it (218b ✅); the package content, git source and CI publish are 218c–218e |
| 7 environment-variable **values** | Dedicated | `sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId` | **H7** | ✅ — `sprk_BffApiAppId` / `sprk_MsalClientId` carry the **customer's own** BFF app registration (D-13); `sprk_TenantId` is Spaarke's tenant for every Model 1 customer (I1 still forbids a hard-coded default) |
| Secure Record setup: business unit `Secure Record`, Owner team `Secure Record Owners`, role `Secure Record Owner` (in that unit), BFF-managed field-security memberships | Dedicated (data, not package — a child-unit role cannot ship) | Names: `config/secure-record-owner-role.json` (unit, role, Read-at-Basic tables) and the BFF's compiled `SecureRecord:OwnerTeamName` default | **H7b** | 🟡 **T256** — handler built (unit-tested); first live run is T186. `sprk_noaccessentry` itself ships in SpaarkeMaster ≥ 1.2.0.0 (H6); H7b checks it before the BFF deploy |
| Per-customer M365 Copilot agent | Dedicated | — | TBD (INCOMING §9 Q2 answered: per customer) | 🔲 **T257** — an M365 package, not Dataverse content (split from T218) |

**Defined 2026-10-07 (T218, D7/D8).** The package is ONE solution, `SpaarkeMaster`; its scope is a rule — every `sprk`
component in the authoring environment (incl. every code page) + the `sprk_` columns on OOB tables − a committed
exclusion list. Git holds the unpacked source; CI publishes the managed and unmanaged zips; H6 imports managed unless
the run's `solutionPackageType` is `unmanaged`, refuses a type switch and a downgrade; environment-variable values never
ship (H7). Binding rule: [ADR-027 §3–§4](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md);
runbook: [`SPAARKE-SOLUTION-RELEASE-PROCESS.md`](../procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md); evidence:
`projects/customer-provisioning-orchestration-r1/notes/t218-plan.md`. **Replaced** (218b, 2026-10-07): H6's 9-entry
`CanonicalSolutionCatalog`, 6 of whose solutions existed nowhere. Spaarke's own environments followed in T218f
(2026-10-08): `Deploy-Release.ps1` Phase 3 imports the same published package with H6's rules
(`Import-SpaarkeMasterPackage.ps1`, typed by `config/environments.json`); `Deploy-DataverseSolutions.ps1` and its list are
deleted (ISS-005 / #1401 resolved).

## Registry row (admin environment — Shared)

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Admin Dataverse environment holding the `sprk_dataverseenvironment` registry | **Shared** (Spaarke platform) | `spaarkedev1.crm.dynamics.com` today (dual-use dev + registry); prod registry not yet provisioned | Once per platform; L2 UAMI is an app user with the scoped `Spaarke Provisioning Registry` role (`Grant-ControlPlaneIdentity.ps1`) | ✅ |
| One `sprk_dataverseenvironment` row per customer | Dedicated row in a shared table | Keyed by `sprk_customerid` (customerId standard — 🔲 **T237** sets `MaxLength = 8` and re-issues non-compliant values); carries `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid`, `sprk_tenancymodel` (`Model1` / `Model2`, post-T224), `sprk_tenantid` (immutable post-create), `sprk_bffversion`, `sprk_solutionversion`, `sprk_currentrunid`, `sprk_setupstatus` | **H13** writes `Ready`; L3 skill Step 6 patches columns | ✅ — `sprk_containertypeid` holds the **container type** GUID, which is the same for every Model 1 customer; each customer still gets its own **container** |

---

# Azure — per customer (Dedicated)

Everything in this section is created by **H2a** deploying [`infrastructure/bicep/customer.bicep`](../../infrastructure/bicep/customer.bicep)
into the customer's own subscription (`targetScope = 'subscription'`), unless another handler is named.

✅ **T225a (2026-10-01) — the `model1-*` Bicep surfaces are deleted**; H2a failed closed for Model 1 until **T228
(2026-10-06)** made every run carry the customer's own subscription — since then Model 1 deploys `customer.bicep`. ✅ **T225b (2026-10-02)** converged the
rest of the Model 1 code path (H2b, H13's I2 probe and H12c use the stamp's own AI Search / OpenAI; the shared-platform
options, Worker settings and seed entry are gone). ✅ **T228** gave every Model 1 run its own subscription and pointed
H2a's Model 1 arm at `customer`. Every ✅ below means "`customer.bicep` does this", for both models.

| Area | Resource | Deployment | Naming (`{env}` = `prod`) | Created by | Status vs target |
|---|---|---|---|---|---|
| Container | Resource group | Dedicated | `rg-spaarke-{customerId}-prod` (`customer.bicep:135`) | H2a | ✅ |
| Identity | User-assigned managed identity (bound to both App Service slots; T5 fix) | Dedicated | `mi-spaarke-{customerId}-prod` (`:214`) | H2a | ✅ |
| Identity | BFF Entra app registration (D-13) + FIC `spaarke-uami-trust`; SPA redirect = the customer's Dataverse origin; shared clients (production Office add-in `1958aec2…`) pre-authorized on `user_impersonation` | Dedicated | `spaarke-bff-api-{customerId}` | **H3** | ✅ task 222; client access T240a; an existing registration is adopted only if nobody but the control plane can act as it |
| Identity | App role `Provisioning.KeylessProof` on that registration, assigned to the L2 Worker identity (Model 1) | Dedicated | role id `528b7c40-41f6-4dbe-aaf0-9e6d1a622f4b` (`KeylessProofContract`) | **H3** | ✅ task 230b — lets H13 call the stamp BFF's keyless proof |
| Secrets | Key Vault (RBAC, soft-delete 90d, purge protection) | Dedicated | `sprk-{customerId}-prod-kv`, capped at 24 chars (`:150`) | H2a; **H4** populates | ✅ |
| Observability | Log Analytics + App Insights | Dedicated | `sprk-{customerId}-prod-logs` / `-insights` (`:174-177`) | H2a (`modules/monitoring.bicep`) | ✅ |
| Data | Storage account (+ containers `temp-files`, `document-processing`, `ai-chunks`) — **shared-key access disabled** (T244, owner D13); callers use the blob endpoint with an identity (the stamp UAMI holds Storage Blob Data Contributor) | Dedicated | `sprk{customerId}prodsa`, ≤24 chars, no hyphens (`:141`) | H2a | ✅ |
| Data | Service Bus (Standard) + queues `sdap-jobs`, `document-indexing`, `ai-indexing`, `sdap-communication` + topic `membership-events` / subscription `recon` — **local (SAS) auth disabled, no authorization rule** (T244) | Dedicated | `spaarke-{customerId}-prod-sbus` (`:153`) | H2a | ✅ |
| Data | Cosmos DB (serverless; database `spaarke-ai`; partition `/tenantId`) — **local auth disabled** (T244, owner 2026-10-06); the BFF connects with the stamp UAMI (Cosmos DB Built-in Data Contributor, data-plane `sqlRoleAssignments`) | Dedicated | `spaarke-{customerId}-prod-cosmos`, ≤44 chars (`:161`) | H2a | ✅ |
| Data | Azure Managed Redis (`Microsoft.Cache/redisEnterprise`) — **Balanced_B0, high availability on, access keys disabled** (owner D12, 2026-09-30; supersedes the Standard C1 decision — Azure Cache for Redis retires 2028-09-30; D-12 §3: holds OBO tokens + the `uac-access` cache). The stamp UAMI holds the only access-policy assignment; the BFF connects over RESP3 with `Redis__Endpoint` (host:10000). | Dedicated | `sprk-{customerId}-{env}-redis` | H2a | ✅ **T242** (2026-10-04) — `modules/redis.bicep`; no key, connection string or vault secret |
| AI | Azure OpenAI (`kind=OpenAI`, `openAiLocation` default westus3) + 3 deployments, all **DataZoneStandard** (owner 2026-10-06: US data zone): `gpt-4o` (gpt-4o 2024-11-20, 150K TPM), `gpt-4o-mini` (runs **gpt-4.1-mini 2025-04-14** — gpt-4o-mini is Deprecating; the BFF calls the name, 200K), `text-embedding-3-large` (1, 350K) — **local auth disabled** (T244); the BFF uses the stamp UAMI. ADR-028 E-2 (MI 401) was observed on the shared dev `AIServices`-kind account; H13 measures this kind (T230) | Dedicated | `sprk-{customerId}-prod-openai` (`:168`) | H2a | ✅ T247 — H0 checks quota and pin status for exactly this set (`PinnedModelCatalog`) in `openAiLocation`. Pins retire 2027-04-14; refresh before ~2027-01-14 |
| AI | Azure AI Search (Standard, semantic) — **local auth disabled, Entra only** (T244, G16 closed) | Dedicated | `sprk-{customerId}-prod-search` (`:171`) | H2a | ✅ |
| AI | AI Search indexes — 7 canonical (`spaarke-files-index`, `spaarke-discovery-index`, `spaarke-records-index`, `spaarke-rag-references`, `spaarke-insights-index`, `spaarke-session-files`, `spaarke-invoices-index`) | Dedicated | Catalog = [`Deploy-AllIndexes.ps1`](../../scripts/ai-search/Deploy-AllIndexes.ps1) `$Catalog` (schemas embedded in the control plane) | **H2b** (SDK `SearchIndexClientProvisioner`, L2 identity) | ✅ **T225b** — one path for both models on the stamp's own service (the Model 1 shared-service branch and its `tenantId`-filter template are deleted) |
| AI | Document Intelligence (S0, `prebuilt-layout`) — **local auth disabled** (T244; the BFF has used the UAMI since T243) | Dedicated | `sprk-{customerId}-prod-docintel` (`:180`) | H2a | ✅ |
| AI | Azure AI Content Safety (S0, `kind=ContentSafety`; Prompt Shields + Groundedness Detection for the BFF safety perimeter) — **local auth disabled**; the stamp UAMI holds Cognitive Services User and the BFF calls with it. S0 is billed per call (no fixed fee), so a dedicated account costs nothing idle; D-12 lists Content Safety as shareable, but a per-stamp account keeps customer prompt text in the stamp and needs no `customerId` discriminator (T246, G26) | Dedicated | `sprk-{customerId}-prod-contentsafety` | H2a; endpoint → `AiSafety__ContentSafety__Endpoint` (customer.bicep + H4b from H2a's `contentSafetyEndpoint`) | ✅ T246 (2026-10-06) |
| Compute | App Service Plan (Linux, S1 default) — a dedicated plan is **forced** (an app cannot use a plan in another subscription; ADR-027 §1) | Dedicated | `sprk-{customerId}-prod-plan` (`:535`) | H2a | ✅ |
| Compute | BFF App Service + `staging` slot (.NET 10, UAMI-only, `/health`) | Dedicated | `sprk-{customerId}-prod-api` (`:547`) | H2a; H4 PATCHes `keyVaultReferenceIdentity` on both slots (T1); **H9** zip-deploys | ✅ |
| Optional | SignalR (flag-gated `signalrEnabled`, default off) | Dedicated | `sprk-{customerId}-prod-signalr` (`:165`) | H2a | ✅ |
| Optional | ACS messaging + Event Grid (flag-gated `deployAcsMessaging`) — the subscription dead-letters to Storage **as the system topic's managed identity** (Storage Blob Data Contributor on the dead-letter container; T244, owner 2026-10-06) | Dedicated | `sprk-{customerId}-prod-acs` (`:156`) | H2a | ✅ |
| RBAC | Customer UAMI: Service Bus Data Sender + Receiver; AI Search Index Data Contributor + Service Contributor | Dedicated | On the customer's own SB + Search | H2a → `modules/bff-runtime-rbac.bicep` (`:657`) | ✅ for `customer.bicep`; ✅ **T225a** — the module grants only the stamp UAMI (its shared-UAMI caller was deleted) |
| RBAC | L2 UAMI: Website Contributor on the customer BFF (H4b log fetch, H9 deploy); Search Service Contributor (H2b index create/verify) + Search Index Data Reader (H13 tenant-filter query) on the customer AI Search (T244, G16) | Dedicated — **Model 1 only** (T249: a Model 2 stamp is in the customer's tenant and is reached through Lighthouse) | On the customer App Service | H2a → `modules/customer-l2-bff-rbac.bicep` | ✅ |

## Key Vault secrets and BFF app settings

| Group | Deployment | Secrets | Written by | Status vs target |
|---|---|---|---|---|
| Customer-resource credentials and endpoints | Dedicated | `DocumentIntelligence-Endpoint`, `AiSearch-Endpoint`, `AzureOpenAI-Endpoint`, `AppInsights-ConnectionString`, `Communication-WebhookUrl` | `customer.bicep` (`kvSecretValues` → `kvSecrets` module) writes them **from the customer's own resources** into the customer vault; H4b points the BFF's KV references at the **customer** vault. | ✅ **T226 (2026-09-30)** — no secret is sourced from a shared service. The Document Intelligence key was removed by **T243** (2026-10-02) and `Redis-ConnectionString` by **T242** (2026-10-04) — the BFF reaches both with the stamp UAMI (Redis via the plain `Redis__Endpoint` setting). |
| Removed from the process (T226) | Dedicated | `ServiceBus-ConnectionString`, `AiSearch--AdminKey`, `AzureOpenAI-ApiKey`, `Storage-ConnectionString`, `PromptFlow-Endpoint`, `PromptFlow-Key` | — (the BFF reaches Service Bus, AI Search and OpenAI with the stamp UAMI; nothing reads the Storage connection string; Prompt Flow is retired, D5) | ✅ T226. ⚠️ AI Search is still created keys-only, so MI calls 403 until T244 (G16). |
| Identity and tenant | Dedicated | `TenantId`, `BFF-API-ClientId`, `BFF-API-Audience`, `Dataverse-ServiceUrl` | `TenantId`: **H4** from the intake `tenantId` (`from-intake-parameter`). `BFF-API-ClientId` / `-Audience`: **H3** writes them to the customer vault itself; H4 (which runs before H3) skips them (`written-by-h3`). `Dataverse-ServiceUrl`: nothing yet — it is H5's output and H5 runs after H4. | ✅ TenantId, ClientId, Audience (task 245a). 🔲 **T245b** — `Dataverse-ServiceUrl`. |
| SPE | Dedicated (the customer's container) + shared type ID | **No SPE secrets.** The container type id is the plain setting `SharePointEmbedded__ContainerTypeId` (intake `containerTypeId` — same for every Model 1 customer). The customer's container id is plain too: H4b writes `EmailProcessing__DefaultContainerId` and `Communication__ArchiveContainerId` from H8's `SpeContainerId` (H4b ← H8) | **H4b** (type id from intake; container id from H8) | ✅ **T227c** — `SPE-DefaultContainerId` / `SPE-CommunicationArchiveContainerId` left the catalog (nothing could write them: H8 runs after H4); the email setting now uses the key the BFF reads. ✅ **T227e** — `SPE-ContainerTypeId` and its app setting `DEFAULT_CT_ID` left too: nothing read either |
| Communications | Dedicated | `Communication-DefaultMailbox`, `Communication-WebhookClientState`, `Communication-Webhook-SigningKey`, `Compose-Webhook-*`, `Email-Webhook*` | **H4** generates the webhook secrets and writes `Communication-DefaultMailbox` from the required intake value `communicationDefaultMailbox` | ✅ generated secrets. ✅ **T245c** — `Communication-DefaultMailbox` is operator intake, validated at `POST /api/runs` (mailbox configuration itself is out of scope, D2) |
| Third-party vendor keys | — | `BingSearch-ApiKey`, `LlamaParse-ApiKey` | not provisioned | ⛔ **Removed from customer stamps — owner D18 (2026-10-02), T225b.** Bing Search v7 was retired by Microsoft 2025-08-11 (the BFF's web-search tool uses it; dev/demo use keyless Grounding with Bing instead); LlamaParse has no production caller. No value existed in any vault. The BFF's dead Bing v7 code is on the BFF follow-up list. |
| Content Safety key | — | `ContentSafety-ApiKey` | not provisioned | ⛔ **Removed — T246 (2026-10-06).** Each stamp has its own keyless Content Safety account reached with the UAMI; the endpoint is a plain app setting (`AiSafety__ContentSafety__Endpoint`, H4b). |
| Retired credentials | — | `BFF-API-ClientSecret`, `Dataverse-ClientSecret` | **Never** created, seeded or restored on secret-free stamps; never delete the live `Dataverse-ClientSecret` or purge rollback copies before **2026-11-23** ([`.claude/constraints/provisioning.md`](../../.claude/constraints/provisioning.md) §KV credential lifecycle) | ✅ |
| BFF app settings (~50 across 26 IOptions sections) | Dedicated | The canonical secret-catalog manifest: each secret's `app_settings` (Key Vault references) + `per_env_settings` — exactly what [`Configure-AppServiceSettings.generated.ps1`](../../scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1) writes (parity test) | **H4b**, through the ARM SDK on the production site + `staging` slot (merge, never replace), one write per slot → one restart | ✅ **T253** (2026-10-08) — H4b no longer runs the script (the Worker host has no pwsh) |
| `Customer__Id` app setting (the BFF's "which customer am I" handle; the BFF refuses to start in deployed envs without it or a derivable `rg-spaarke-{customerId}-{env}` RG) | Dedicated | = the intake `customerId`, never re-derived | `customer.bicep` emits it on the production site (master `ddfacd8ee`); **H4b** writes it to both slots from the run's customerId; **H13 trap T7** fails the run when either slot lacks it or differs | ✅ **T238** (2026-10-01) |
| `WorkforceIdentity__CustomerTenantIds__N` app settings (the customer's workforce tenant(s) whose MEMBERS the BFF email-binds at first sign-in; empty = deny every customer employee) | Dedicated | = the intake `customerWorkforceTenantIds` (the customer's HOME tenant for Model 1 — never Spaarke's or the CIAM tenant) | **H4b** writes both slots and removes stale indices; **H13 trap T7** fails the run when either slot differs from the run's list | ✅ **T255** (2026-10-09, INCOMING-141) |
| `acct` optional claim on the stamp BFF registration's access tokens (member = 0, guest = 1 — the BFF's workforce member test fails closed without it) | Dedicated | — | **H3** (create + reconcile, read back) | ✅ **T255** (2026-10-09) |

---

# Shared Spaarke platform (once per platform — not per customer)

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| L2 control plane: App Service API host + slotless Worker host (+ EXO PowerShell sidecar) | Shared | `spaarke-provisioning-controlplane-{env}` / `-worker-{env}` | `platform-controlplane.bicep` | ✅ — T225b removed the Model 1 shared-platform settings and the vendor-key vault setting; the Worker sets `KvSecretsPopulationOptions__RequireSecretFreeIdentity=true` (every new stamp secret-free, G21) |
| L2 Cosmos (`spaarke-provisioning` / `runs`, partition `/customerId`) | Shared | per `cosmos-provisioning.bicep` | `platform-controlplane.bicep` | ✅ |
| Platform Key Vault | Shared | `sprk-controlplane-{env}-kv` (dev) / `sprk-platform-prod-kv` (prod) | `platform-controlplane.bicep` | ✅ — `Seed-PlatformKeyVault.ps1` seeds 5 secrets; T225b retired the `AzureOpenAI-Endpoint` entry (a copy already in a vault is left alone) |
| Provisioning job queue `sprk-provisioning-jobs` (sessions keyed by `customerId`) | Shared | on `spaarke-servicebus-{env}` | `platform-controlplane.bicep` | ✅ |
| L2 fleet UAMI | Shared | `sprk-controlplane-{env}-uami` | `platform-controlplane.bicep` | ✅ — its role assignments on each **customer subscription** are a manual prerequisite (✅ T228 — PRQ-S-04) |
| BFF build artifacts + container registry | Shared | `sprkcpartifacts{env}`, `sprkcontrolplane{env}acr` | `platform-controlplane.bicep` | ✅ |
| Admin Dataverse environment (registry) | Shared | see Dataverse → Registry | once per platform | ✅ |
| ~~`customer.bicep` `platformKeyVaultName` parameter~~ | — | — | — | ✅ **Removed by T249** (2026-10-02, D19) — no consumer read the output; `Provision-Customer.ps1` stopped passing it |

---

# SharePoint Embedded

Rules and procedures: [`SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md) +
[`SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md`](../guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md). Container types are
permanent, capped at 25 per tenant and cannot be deleted; creating one is delegated-only (app-only returns 403).

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Container type `Spaarke Model 1` (`standard` — Spaarke pays, D1) | **Shared** by all Model 1 customers | `Spaarke Model 1` — id `fb3817a8-5a55-42ba-8cc9-12cf055168b8` | Operator, once — runbook Step 4 (SharePoint admin center, delegated) | ✅ created by the owner 2026-10-03 (T248) |
| Billing account for the container type (`standard` billing binding — **permanent**) | **Shared** | `Microsoft.Syntex/accounts` `dc4749c2-ca04-4b38-b6c2-e38dc3eec72b` in `rg-spaarke-shared-prod` (subscription "Spaarke Shared Production", eastus; owner D23) | Attached by the admin-center creation flow | ✅ 2026-10-03 — never delete the account or its resource group |
| Owning app `Spaarke SPE Model 1 Owner` (single-tenant; permanent 1:1 binding) | **Shared** | appId `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e`; Graph application permissions `FileStorageContainer.Selected` + `FileStorageContainerTypeReg.Selected` (admin-consented via `appRoleAssignedTo`) | Operator, once — runbook Steps 1–2 | ✅ T248 — **no certificate, no client secret**; nothing stored in any Key Vault |
| Owning-app credential: federated identity credential trusting the L2 Worker UAMI (MI-FIC, owner D16) | **Shared** (one per Worker UAMI that acts as the owner; max 20 per app) | `sprk-controlplane-dev-uami-assertion` — subject = `sprk-controlplane-dev-uami` principal `38f7693f-…`, audience `api://AzureADTokenExchange` | Operator, once per control-plane environment — runbook Step 3 | ✅ T248 — proven live 2026-10-03 (`appidacr` 2). Replaces the never-created KV certificate secret `SPE-OwnerCert-Pfx` (G28) |
| Worker owner entry `SpeContainerOptions__ContainerTypeOwners__{i}__ContainerTypeId` / `__OwnerAppId` | **Shared** | `speContainerTypeOwners` in `platform-controlplane-{env}.bicepparam` | `platform-controlplane.bicep` — runbook Step 6 | ✅ dev, deployed 2026-10-03 |
| Container-type **registration** in Spaarke's tenant (one registration for all Model 1 customers → one settings baseline, no per-customer overrides) | **Shared** | — | Registered by the admin-center creation flow (2026-10-03T22:54:58Z); H0's `SpeOwnerCredential` check GETs it as the owning app on every run | ✅ T248 — grants: the owning app (`full`/`full`) and Microsoft Graph Explorer `de8bc8b5-…` (delegated `full`, application `none`; known extra grant, owner: keep) |
| `applicationPermissionGrants` entries for the customer's BFF identities on that registration | Dedicated (two grants per customer) | keyed by appId: the stamp UAMI (application `full` — the BFF's app-only Graph calls) and the BFF app registration (delegated `full` — its OBO calls) | **H8**, as the owning app, before creating the container (GET, then PUT/PATCH) | ✅ **T227b** — Learn documents no per-registration grant limit |
| Default container | Dedicated | `displayName` = `speContainerDisplayName` (default `Spaarke Container - {customerId}`); custom properties `spaarkeBusinessUnitId` = root business unit (uac-r2 task 165) and `spaarkeCustomerId` = customer id (T227e) | **H8** (created once — a re-entry resumes from the run's record, a later run reuses the container the environment records in `sprk_SharePointEmbeddedContainerId`; never a second (T227e) — then activate + verify + bind + marker, app-only as the container type's owning app through the Worker UAMI's federated credential — not as the BFF or its UAMI); **H7** records it on the root business unit (`businessunit.sprk_containerid` — unified-access-control-r2 task 076's non-secure default; never overwrites another container, T227g); **H13 T6** lists the type's containers as the owning app and requires this one | ✅ task 214; credential T248 |
| Communication-archive container | Dedicated | the customer's root container (owner D28) — `Communication__ArchiveContainerId` = H8's `SpeContainerId` | **H8** (same container) | ✅ **T227c** — H13 I4 requires both container settings to name this container |
| Secure-record containers | Dedicated (one per secure project / matter / work assignment) | the record's name; custom properties `spaarkeBusinessUnitId` = the record's business unit (`Secure Record`) and `spaarkeCustomerId` = `Customer__Id`; recorded on the record's `sprk_containerid` (the `Secure Record` business unit itself has none) | **BFF at runtime** (`ProvisionProjectEndpoint` → `ContainerOperations.CreateContainerAsync`, which writes the marker) — not provisioning | ✅ **T227d** — the BFF's app-only calls reach only containers that are configured or carry this marker; everything else is refused (404) |
| Further business-unit containers (optional) | Dedicated (one per business unit an administrator gives its own) | custom properties `spaarkeBusinessUnitId` = that unit and `spaarkeCustomerId` = `Customer__Id` | **BFF at runtime** (SPE admin plane → `SpeAdminGraphService.CreateContainerForConfigAsync`: bind, then mark); linking it to the unit's `sprk_containerid` is the administrator's step. `New-BusinessUnitContainer.ps1` works only for a legacy certificate owning app | ✅ T227d/T227g — a unit without one stores its non-secure files nowhere (uac-r2 task 076 resolves to the record's own unit) |

⚠️ **Capacity**: how many containers one `standard` container type can hold is **undocumented** by Microsoft
(topology doc §8 Q1). Under Model 1 that number is the ceiling on Model 1 customers — confirm before it becomes
load-bearing.

## Exchange mailbox access (H14a — RBAC for Applications, task 251)

The stamp's managed identity reaches only the customer's mailboxes: H14a grants it the Exchange application roles for
the Graph mailbox permissions, scoped to the customer's mail-enabled security group. H10 never grants those
permissions in Entra (Exchange adds the two together). Owner decisions D24–D26.

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Exchange admin app `Spaarke Exchange Admin` (single-tenant; `Exchange.ManageAsApp`) | **Shared** (one per tenant L2 provisions into) | appId `46670ee2-ac0c-44b0-9ac2-d40ae4dcbdd7` (Spaarke tenant) | Operator, once — deployment guide §4.2.1 / `PRQ-E-15` | ✅ 2026-10-04 — **no secret, no certificate, no Entra directory role** |
| Its federated identity credential trusting the L2 Worker UAMI | **Shared** (one per control-plane environment) | `sprk-controlplane-dev-uami-assertion` — subject `38f7693f-…` | Operator, once | ✅ 2026-10-04 (verified: token `appidacr` 2, `Exchange.ManageAsApp`) |
| Exchange org customization (`Enable-OrganizationCustomization`) | **Shared** (tenant-wide, irreversible) | — | Operator, once per tenant | ✅ Spaarke tenant 2026-10-04 (owner-approved) |
| Exchange role `Spaarke App RBAC Admin` (Role Management child, 14 cmdlets) + the admin app's assignments (that role, `View-Only Recipients`, `-Delegating` for the four application roles) | **Shared** | assignments `sprk-exoadmin-*` | Operator, once per tenant | ✅ 2026-10-04 — refuses self-grants of wider roles (tested) |
| Platform parameter `exchangeAdminAppId` → Worker `IntegrationWiring__ExchangeAdminAppId` | **Shared** | `platform-controlplane-{env}.bicepparam` | `platform-controlplane.bicep` | ✅ dev (T251) |
| Customer scope group (mail-enabled security group; direct members only) | Dedicated | intake `exchangePolicyScopeGroupId` | The tenant's Exchange admin, before the run — `PRQ-C-08` | per customer |
| Stamp identity's Exchange service principal + four role assignments `{prefix}-{customerId}-{MailRead, MailReadWrite, MailSend, MailboxSettingsRead}`, each scoped to the scope group | Dedicated | prefix `IntegrationWiring:ExchangeAssignmentNamePrefix` (default `Spaarke`) | **H14a** through the Worker's sidecar; **H13 T4** verifies | 🔄 T251 — see the design note §7 for the live status of app-only registration |

---

# M365 (Shared packages)

| Resource | Deployment | Naming | Created by | Status vs target |
|---|---|---|---|---|
| Outlook + Word add-in manifests | Shared | Dev client app `c1258e2d-1688-49d2-ac99-a7485ebd9995` (dev BFF only); production client app "Spaarke Office Add-in (Production)" `1958aec2-0218-495e-8e3c-37133e9b8357` (word-add-in-r1, 2026-10-07; single tenant; pre-authorized on every customer BFF by H3) ([`manifest.json`](../../src/client/office-addins/outlook/manifest.json)) | `scripts/Deploy-OfficeAddins.ps1` (not per customer) | ⚠️ see note below |
| Add-in hosting (Static Web App) | Shared | Dev: `spaarke-office-addins` (RG `spe-infrastructure-westus2`). Prod: `swa-spaarke-office-addins-prod` (RG `rg-spaarke-shared-prod`, Standard, created 2026-10-07; custom domain `addins.spaarke.com`; no content deployed yet) | `Deploy-OfficeAddins.ps1` | ✅ dev / ⚠️ prod empty |
| External Access SPA + Teams tab hosting (Static Web App) | Shared | Dev: `swa-spaarke-external-spa-dev` (RG `rg-spaarke-dev`). Prod: `swa-spaarke-external-spa-prod` (RG `rg-spaarke-shared-prod`, Standard, created 2026-10-07; custom domain `external.spaarke.com`; no content deployed yet) | spaarke-SPA-external-access-platform-r3 | ✅ dev / ⚠️ prod empty |
| Teams app package + hosting | Shared | Teams `id 23610794-67de-4e6c-be61-ff80cc8cbe7f`; external-spa SWA | M365 Agents Toolkit / external-spa deploy | ⚠️ see note below |
| Teams app Entra registration | Shared | `SDAP-BFF-SPE-API` (`1e40baad-…`), multi-tenant workforce app (ADR-028 A2) | once | ⚠️ see note below |

⚠️ **Open — 🔲 T240 (owner D9: in scope)**: the add-ins and the Teams tab are single shared packages bound to one
Entra app, while D-13 gives every customer's BFF its **own** app registration and token audience. How a shared
client obtains a token for a specific customer's BFF — and which BFF it routes to, given every Model 1 customer has
Spaarke's `tid` — is not yet designed (plan §4 gap **G14**).

---

# Retired — the shared Model 1 tier (D-12 2026-09-28; owner 2026-09-30)

None of these may be provisioned for a customer. Listed so that a reader who meets them in code or older docs knows
they are defects, not options.

| Retired resource | Where it still appears | Retiring task |
|---|---|---|
| One shared Azure subscription for all Model 1 customers | none in code (✅ T228 — intake requires the customer's own subscription; the platform-subscription grant is no longer deployed; an existing live assignment stays until an owner removes it) | T228 ✅ |
| `rg-spaarke-shared-{env}` and the per-tenant RGs of `model1-shared.bicep` | Bicep + `prereqs.yaml` cleared ✅ T225a. Live: `rg-spaarke-shared-prod` (`sprksharedprod-api` stopped 2026-09-30) | T225a ✅ · decommission T241 — the shared-tier **resources** only: `rg-spaarke-shared-prod` itself is kept (owner D23) because it holds the permanent `Spaarke Model 1` billing account (SharePoint Embedded section) |
| `sprkshared{env}-*` / `sprksharedprod-*` services (plan, OpenAI, AI Search, Redis, Service Bus, Storage, DocIntel) | none in code (stack deleted ✅ T225a; `service_ref`s + H4-shared removed ✅ T226) | T225a ✅ · decommission T241 |
| `sprk-{env}-shared-bff-uami` | none (caller stack deleted; `bff-runtime-rbac.bicep` names only the stamp UAMI) | T225a ✅ |
| Shared BFF App Service `spaarke-bff-{env}` in `rg-spaarke-{env}` as a customer runtime | none in code: `bffAppServiceRg` / `bffAppServiceName` and the skill's `{bffAppServiceId}` token removed | T227a ✅ |
| Shared BFF app registrations `Spaarke BFF - Trial 1` / `Spaarke BFF - Model 1` | none in code: `-CreateBffApp`, `bffApiAppId` / `bffProdBase`, SKILL Step 0.5c BFF checks and PRQ-C-05 removed | T227a ✅ |
| H0 `shared-trial` cost tier; `Model1MarginalEnvelopeUsd` (marginal-on-shared-platform) | none: tiers are smb / enterprise / dedicated, required for every model; H13 has one `DedicatedStampEnvelopeUsd` | T229 ✅ (2026-10-06) |
| H13 I2 probe against a shared AI Search `tenantId`-filter template | `AiSearchTenantFilterInvariantProbe` Model 1 branch | ✅ T225b (2026-10-02) — one path on the stamp's own service |
| Prompt Flow secrets (`PromptFlow-Endpoint`, `PromptFlow-Key`) | `manifest.yaml:502-528` | T226 (D5 — BFF readers removed 2026-08-21) |

**Out of scope for this project** (plan §6): Model 2; Model 1 with SPE containers in the customer's tenant;
trial / sandbox environments (may use unmanaged solutions — special case); customer staging/dev stamps (D6);
per-customer vendor accounts (D5); email/Graph mailbox configuration (D2); Power BI F-SKU capacity (placeholder only).
