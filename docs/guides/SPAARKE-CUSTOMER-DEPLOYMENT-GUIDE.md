# Spaarke Customer Deployment Guide

> **Version**: 1.0 (consolidation baseline)
> **Last Updated**: 2026-08-17
> **Status**: **Authoritative**. Supersedes the customer-provisioning content in `CUSTOMER-DEPLOYMENT-GUIDE.md`, `CUSTOMER-ONBOARDING-RUNBOOK.md`, `ENVIRONMENT-DEPLOYMENT-GUIDE.md`, `auth-deployment-setup.md`, `SPAARKE-DEPLOYMENT-GUIDE.md`, and `PRODUCTION-DEPLOYMENT-GUIDE.md`. Those files are retained as one-paragraph stubs pointing here.
> **Audience**: Platform Operator (primary). Claude Code AI executes automated phases; a human operator holds accountability for gates, secrets, and customer communication.
> **Applies To**: Every new Spaarke customer environment (Model 2 dedicated stamp; Model 1 shared trial/SMB).
> **Owner**: Platform Operations. Maintained by `customer-provisioning-orchestration-r1` and its successors.

---

## 0. How to Use This Guide

This is the single authoritative guide for standing up a new Spaarke customer environment end-to-end. It covers **both** target-state (the r1 pipeline delivering L1 handlers + L2 control-plane + L3 `/provision-environment` skill) and the **transitional operator path** (existing PowerShell scripts + manual gates) that remains in use until Phase D of `customer-provisioning-orchestration-r1` lands.

### What this guide contains

- Prerequisites, tenancy-model selection, per-phase execution walkthrough (H0 through H14)
- Tenant-isolation invariants (I1–I5) and silent-fail trap catalog (T1–T6)
- Upgrade-model reference, rollback / quarantine semantics, troubleshooting
- Operator runbook for the **interim** manual path until `/provision-environment` skill is delivered

### What this guide does NOT duplicate

Reference-only cross-links (do not restate the source):

| Topic | Canonical source |
|---|---|
| Resource + KV-secret naming convention (`sprk-{env}-kv`, per-env prefixes, tags) | [`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) |
| Auth architecture v2 (MI-first, 21 MUSTs, OBO, KV federation) | [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) |
| Full project design + decisions D1–D20 | [`projects/customer-provisioning-orchestration-r1/design.md`](../../projects/customer-provisioning-orchestration-r1/design.md) |
| Functional + Non-Functional Requirements (FR-01..FR-37, NFR-01..NFR-12) | [`projects/customer-provisioning-orchestration-r1/spec.md`](../../projects/customer-provisioning-orchestration-r1/spec.md) |
| Component bill-of-materials (386 solution components, entities, PCF, Azure stamp) | [`projects/customer-provisioning-orchestration-r1/COMPONENT-INVENTORY.md`](../../projects/customer-provisioning-orchestration-r1/COMPONENT-INVENTORY.md) |
| Sourced pricing (Aug 2026, per-tenancy-model cost floors) | [`projects/customer-provisioning-orchestration-r1/notes/pricing-research-2026-08-12.md`](../../projects/customer-provisioning-orchestration-r1/notes/pricing-research-2026-08-12.md) |
| Secret rotation cadence + procedures | [`docs/guides/SECRET-ROTATION-PROCEDURES.md`](SECRET-ROTATION-PROCEDURES.md) |
| Deployment verification quick-reference | [`docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md`](DEPLOYMENT-VERIFICATION-GUIDE.md) |
| Component-specific release guides (see Appendix B) | AI / Communication / Copilot / Office add-ins / Declarative agent |

### Execution legend

| Icon | Meaning |
|------|---------|
| **[AI]** | Claude Code executes autonomously via script/CLI or (post-Phase-D) via `/provision-environment` skill |
| **[HUMAN]** | Human operator must act (portal action, DNS, approval, customer-side action) |
| **[AI+HUMAN]** | AI runs the script; human verifies/approves output |
| **[DECISION]** | Decision point — AI presents options; human chooses |
| **[GATE]** | Verified gate; pipeline blocks until asserted true |

---

## 1. Overview

### 1.1 What "customer provisioning" means at Spaarke

A **customer environment** is the composition of Azure resources + Dataverse organization + SharePoint Embedded container-type + Entra app registrations + BFF API deployment + configuration seed + integration wiring required for one paying (or trial) tenant to use the Spaarke platform. Every environment is provisioned to reach a single terminal state: `sprk_dataverseenvironment.Setup Status = Ready`.

Historically Spaarke has three generations of provisioning assets (Gen 1 manual guide, Gen 2 `Provision-Customer.ps1` + 24 Bicep modules, Gen 3 `sprk_dataverseenvironment` registry). Operators today manually merge three documents per provision (design.md §2). **This guide is the consolidation that ends that fragmentation.**

### 1.2 Target-state pipeline (`customer-provisioning-orchestration-r1`)

```
[Operator]
   |
   |  /provision-environment  (L3 Claude Code skill — Phase D deliverable)
   v
[L2 Control Plane]  .NET 10 App Service  +  Cosmos DB state  +  Service Bus enqueue
   |
   |  fires 19 idempotent handlers via IJobHandler
   v
[L1 Handlers]  H0, H0.5, H1, H2a, H2b, H3, H4, H5, H6, H7, H8, H9, H10, H11, H12a, H12b, H12c, H13, H14
   |
   v
[sprk_dataverseenvironment.Setup Status = Ready]
```

**Design principles** (see `design.md` §3 for the full locked-decision set):

- **D8** — Three-layer architecture built in order: L1 handlers → L2 control plane → L3 front ends
- **D10** — Gates are **verified**, not inferred; ProvisioningRun (Cosmos) is system of record
- **D11** — Every step **idempotent + resumable**; failed runs resume, they do not restart
- **D4** — Azure subscription per customer = isolation + billing unit
- **D2** — One deployment package, two targets; tenant is a run parameter, not a code fork

### 1.3 What ships in r1 vs r2

**In-scope for r1** (this guide covers all of these):

- 19 handlers H0–H14, L2 control plane, `/provision-environment` skill (Phase D)
- Model 1 shared trial/SMB tier + Model 2 dedicated stamp
- Tenant-isolation invariants I1–I5 (ArchTest-enforced)
- Canonical KV secret catalog + naming compliance (Phase G/H)
- UAMI migration (Phase C — structural fix for T5 slot-swap trap)
- Upgrade model U1/U2/U3 + version-compatibility matrix

**Deferred to r2**:

- Registry-aware decommission pipeline (`Decommission-Customer.ps1` remains manual)
- Fleet-management web UI (read-only Cosmos dashboard)
- TF Power Platform provider adoption (deferred to first-customer engagement per M-10)

### 1.4 Interim (pre-Phase-D) reality

The L2 control plane + `/provision-environment` skill are the **target**. Until Phase D lands, operators run the interim manual path documented in **§12 Operator Runbook — Interim Manual Path**, which invokes the existing `Provision-Customer.ps1` + `Register-EntraAppRegistrations.ps1` + `auth-deployment-setup.md` sequence with per-phase manual verification.

---

## 2. Prerequisites

### 2.1 Tooling (operator machine)

| Tool | Version | Purpose |
|---|---|---|
| PowerShell | ≥ 7.4 | Script execution (`Provision-Customer.ps1`, Bicep invocations) |
| Azure CLI (`az`) | ≥ 2.60 | Azure resource + RBAC + KV operations |
| Power Platform CLI (`pac`) | ≥ 1.35 | Dataverse environment + solution ops |
| .NET SDK | 10.x | BFF API build (r1 baseline is .NET 10) |
| Git | ≥ 2.40 | Repository operations |
| Bash / `bash` shell | Any | Cross-platform `az` + `pac` scripting |
| Node.js | ≥ 18 | PCF / code-page build (if that surface is being redeployed) |

**Optional but recommended**:

| Tool | Purpose |
|---|---|
| Claude Code CLI | Executes `/provision-environment` skill (Phase D+) |
| Azure Bicep CLI | Local template validation (`bicep build`) |
| Dataverse MCP | Read-side introspection during provisioning |
| Azure MCP | Reserved fallback for KV/RG/App Service queries |

### 2.2 Identity + access (operator)

Per design.md §4.3a.2, Claude Code + the operator use the **operator's own AAD identity** (not a service principal). Required role assignments:

| Scope | Role | Purpose |
|---|---|---|
| Target Azure subscription | Contributor | Create/modify Azure resources |
| Target subscription's KV | Key Vault Secrets Officer (RBAC mode) | Write secrets during H4 |
| Entra tenant | Application Administrator | Create app registrations during H3 |
| Target Power Platform | System Administrator (Environment Admin on new env) | Import solutions, register App Users |
| Target M365 tenant | SharePoint Administrator | Create SPE container types during H8 |
| L2 control-plane app-reg (once L2 deployed) | `Operator` app-role | Invoke mutating L2 REST endpoints |

### 2.3 Information to collect (before running any pipeline)

| Item | Example | Where to find |
|---|---|---|
| Customer ID | `acme` (lowercase, 3-10 alphanumeric) | Assigned per customer intake |
| Customer display name | "Acme Legal Services" | Customer intake |
| Target subscription ID | `2ff9ee48-...` | Azure Portal > Subscriptions |
| Target Entra tenant ID (`tid`) | `a221a95e-...` | Model 2: customer's tenant; Model 1: Spaarke tenant |
| Azure region | `westus2` (default) | Customer intake / geo requirement |
| Dataverse region | `unitedstates` (default) | Must match Azure region locality |
| Tenancy model | `Model2Dedicated` (default) or `Model1Shared` | Per §3 selection criteria |
| Deployment profile | `spaarke-hosted`, `customer-owned`, `demo`, `trial` | Per D15 |
| Customer admin contact | Name, email, phone | For H0.5 consent flow (Model 2) |

### 2.4 External lead-time items (surface BEFORE starting pipeline)

Per H0 preflight (§7.1). Items surfaced **up front**, NOT counted as pipeline time (NFR-03):

- **Azure OpenAI regional TPM quota** — `az cognitiveservices` returns per-model per-region quota; lead time 1–3 days for quota bump
- **Azure subscription vCPU quota** — verify per SKU per region
- **Dataverse environment-creation rate** — ~4/hour per tenant typical (`pac admin quota`)
- **SPE container-type replication** — up to 24h (T6); customer prereq checklist item, NOT in-pipeline wait
- **Customer admin consent (Model 2)** — one-time customer action captured by H0.5

### 2.5 Preflight naming collision check

Before starting, verify names are available (all resources use canonical convention per [`AZURE-RESOURCE-NAMING-CONVENTION.md`](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)):

```powershell
# Resource group must NOT exist
az group exists --name "rg-spaarke-{customerId}-{env}"

# Key Vault name must be globally available (soft-delete check)
az keyvault list-deleted --query "[?name=='sprk-{customerId}-{env}-kv']"

# Storage account name (max 24 chars, no hyphens)
az storage account check-name --name "sprk{customerId}{env}sa"

# App Service name must be globally available
az webapp list --query "[?name=='spaarke-bff-{customerId}-{env}']"
```

---

## 3. Deployment Model Selection

Per D3 (v3), Spaarke supports **two tenancy models**. Same code, different Bicep composition, different post-conditions.

### 3.1 Model 2 — Dedicated Stamp (default)

**When to choose**: regulated/enterprise customers requiring physical isolation, expected sustained usage, per-customer cost transparency.

**Composition**:

- Dedicated per-customer: **Azure OpenAI**, **AI Search**, Document Intelligence, Service Bus, Cosmos DB, Key Vault, App Insights, Storage, App Service Plan, App Service (BFF), UAMI
- Dedicated: Dataverse env, SPE container-type + root container, Entra app registration (BFF; multitenant per FR-06)
- **Not per-customer**: Redis (per-environment via `scripts/Deploy-RedisCache.ps1` per Q-E FR-12)

**Cost floor**: ≤ $400/mo Azure per empty environment (per NFR-04); usage-passthrough pricing native via Azure Cost Management + tags.

**Bicep stack**: `infrastructure/bicep/customer.bicep` or `model2-full.bicep`.

### 3.2 Model 1 — Shared Trial/SMB Tier

**When to choose**: trial prospects, SMB customers where the fixed-floor cost of a dedicated stamp is uneconomic, evaluation deployments.

**Composition** (per §3A A1):

- **Shared** across all Model 1 tenants: App Service Plan, Azure OpenAI (metered per D19), Azure AI Search (per-tenant `tenantId` filter on every query)
- **Dedicated per-customer**: Dataverse env, SPE container-type + root container, Key Vault, Storage, UAMI, Entra app config

**Cost envelope**: ≤ $430/mo marginal per customer (5-10 users, capped tokens); ≤ $400/mo shared platform floor.

**Bicep stack**: `infrastructure/bicep/stacks/model1-shared.bicep`.

**Additional invariants for Model 1** (see §8 for full I1–I5 detail):

- Per-tenant token metering (D19) enforces `tokenBudgetMonthlyUSD` — over-budget attempts return HTTP 429
- **`tenantId eq` filter mandatory on every AI Search query** (I2 / FR-29) — never a fallback default index scan
- All Cosmos operations carry `/tenantId` partition-key predicate (I3 / FR-30)

### 3.3 Model 1 vs Model 2 handler-behavior differences

Handlers execute the same code but different inputs and post-conditions per tenancy model. Full table in `design.md` §4.1a; summarized here:

| Handler | Model 2 (dedicated) | Model 1 (shared trial/SMB) |
|---|---|---|
| **H0 preflight** | Full per-customer OpenAI quota + subscription vCPU headroom | Verify per-tenant token budget + shared-platform capacity for +1 tenant |
| **H2a Bicep** | `customer.bicep` — full dedicated stamp | `model1-shared.bicep` — dedicated KV/Cosmos/Storage/UAMI **only**; shares App Service Plan + AI Search + OpenAI |
| **H2b AI Search indexes** | 7 indexes on customer's dedicated AI Search | 7 indexes **already exist** on shared platform — H2b verifies + provisions per-tenant `tenantId`-filter query template |
| **H7 env-var + app-settings** | Points at customer's dedicated OpenAI/AI Search/App Insights | Points at shared platform OpenAI/AI Search; per-tenant metering headers set via D19 layer |
| **H12c runtime refs** | `sprk_aimodeldeployment` → customer's dedicated OpenAI | `sprk_aimodeldeployment` → shared platform OpenAI with per-tenant attribution |
| **H13 acceptance** | Full E2E + verify dedicated-resource isolation | Full E2E + verify `tenantId`-filter enforcement + token-metering attribution |

Handlers not listed behave identically across tiers.

---

## 4. Architecture Overview

### 4.1 L1 handlers — the deterministic layer

19 idempotent handlers (H0–H14) implementing `IJobHandler` per **ADR-004**. Each is a self-contained coarse-grained operation (deploy infra, import solutions, deploy BFF) with:

- **3-level idempotency** (NFR-10): Service Bus MessageId dedup + Redis `IdempotencyService` check/lock + Dataverse alternate-key upsert
- **Deterministic idempotency key** using content hashes / semantic versions (not run-attempt counters)
- **Verified post-conditions** asserting the handler's silent-fail trap (§9) is cleared before reporting success

Full handler catalog in §5.

### 4.2 L2 control plane — the orchestrator

**Standalone .NET 10 App Service** in `rg-spaarke-platform-{env}` (parity with BFF per B2):

- REST API with JWT bearer auth (audience `api://spaarke-provisioning-controlplane-{env}`)
- App-roles: `Operator` (mutating endpoints), `Reader` (read-only endpoints)
- OpenAPI at `/swagger`

**Handler execution model** (per FR-22 / R20 — resolves App Service 230s HTTP timeout vs 30-min handlers):

```
POST /api/runs/{id}/phases/{phase}/execute
  |
  v
L2 endpoint ENQUEUES via Service Bus and returns 202 Accepted (< 100ms)
  |
  v
State-reconciler BackgroundService polls Cosmos every 5s to advance DAG
  |
  v
Handlers run in BFF's existing IJobHandler infrastructure (ADR-004)
```

**Concurrency + crash recovery** (per FR-23, invariants I5 + I6):

- Same-customer runs serialized via optimistic concurrency on `sprk_dataverseenvironment.sprk_currentrunid` (409 on conflict)
- Cross-customer runs parallel
- On L2 startup, Cosmos scan resumes `Running` / `WaitingOnGate` runs older than 2× median-handler-duration

**API surface** (per FR-21):

| Endpoint | Purpose |
|---|---|
| `POST /api/runs` | Initialize a new ProvisioningRun |
| `POST /api/runs/{id}/preflight` | Run H0 quota + naming checks |
| `GET /api/runs/{id}` | Poll run state |
| `POST /api/runs/{id}/gates/{gateId}/advance` | Advance a manual gate |
| `POST /api/runs/{id}/resume` | Resume a `Failed` run from `currentPhase` |
| `GET /api/runs/{id}/phases/{phaseId}/logs` | Retrieve phase logs |
| `POST /api/runs/{id}/cancel` | Cancel a run |
| `POST /api/onboarding/consent-callback` | (BFF endpoint) H0.5 consent capture |
| `POST /api/runs/{id}/clear-quarantine` | Clear `Quarantined` state (reason required, audit-logged) |

### 4.3 L3 operator skill — `/provision-environment` (Phase D)

Delivered as `.claude/skills/provision-environment/SKILL.md`. Step 0 is prereqs check (§2.1); interactive intake collects customerId / tenantId / tenancyModel / profile; preflight → confirmation gate → execute loop (enqueue → poll → advance → surface manual-gate instructions) → completion writes handoff report to `runs/{runId}.md`.

Reference model: `.claude/skills/deploy-new-release/SKILL.md`.

### 4.4 ProvisioningRun data model (Cosmos DB)

- **Database**: `spaarke-provisioning`
- **Container**: `runs`
- **Partition key**: `/customerId`
- **TTL**: 365 days
- **Secrets in `parameters`**: KV URI references only — **never** cleartext

Enumerated `gateStates` + `interStepState` shapes per `design.md` §6.2.

### 4.5 Registry extension — `sprk_dataverseenvironment`

12 new columns added by this project (see FR-26 / design.md §6.1):

- `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid`, `sprk_provisionedon`
- `sprk_currentrunid` (I5 concurrency serialization)
- `sprk_tenancymodel` (Choice: Model1Shared / Model2Dedicated)
- `sprk_tenantid` (populated from H0.5 or run params)
- `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken` (§14A upgrade compat)

---

## 5. Handler Catalog (H0 – H14)

Every handler is idempotent, resumable, and has a verified post-condition. Full dependency DAG in `design.md` §4.1.

| # | Handler | Purpose | Gate | Idempotency key |
|---|---|---|---|---|
| **H0** | Preflight + quota checks | Validate run params + Azure OpenAI TPM headroom + Dataverse env-creation rate + subscription vCPU + SPE cert-bootstrap | Quota headroom sufficient for +1 provision | `preflight-{customerId}-{paramHash}` |
| **H0.5** | Consent-capture callback | (Model 2 only) Anonymous HMAC-verified `POST /api/onboarding/consent-callback`; captures customer admin `tid`; kicks pipeline | Re-consent semantics: no-op if run exists Ready/Running; restart from H0 if Failed/Cancelled | `consent-{customerId}-{tid}` |
| **H1** | Subscription readiness | ARM verification target sub is reachable | Lighthouse delegation (`CustomerOwned` only) | `subready-{customerId}` |
| **H2a** | Per-customer Bicep infra | Deploy: RG, KV, Storage, Service Bus, Cosmos, OpenAI, AI Search, Doc Intelligence, App Insights + Log Analytics, optional SignalR. Redis explicitly **NOT** per-customer | — | `infra-{customerId}-{bicepVer}` |
| **H2b** | AI Search indexes | Provision 7 canonical indexes via `scripts/ai-search/Deploy-AllIndexes.ps1` (`files`, `discovery`, `records`, `rag-references`, `insights`, `session-files`, `invoices`) | — | `aisearch-{customerId}-{indexVer}` |
| **H3** | Entra app registration | 1 BFF app-reg with ~14 Graph + Dynamics permission grants (`GraphAppRoles.cs`); sign-in audience `AzureADMultipleOrgs` (enables Model 2 consent) | Admin consent granted (Graph query) | `appreg-{customerId}-{tenantId}` |
| **H4** | Key Vault secrets | Populate KV secrets per canonical catalog manifest; `keyVaultReferenceIdentity` PATCH to UAMI on both slots (**T1** trap) | — | `kv-{customerId}-{secretsVer}` |
| **H5** | Dataverse env creation | Interim: `pac admin create-environment`; target: TF `powerplatform_environment` (deferred to first-customer engagement per M-10) | `sprk_dataverseurl` populated + env accessible | `dvenv-{customerId}` |
| **H6** | Managed solution import | Package Deployer dependency-ordered import — **8 authoritative solutions** (§11.1a): SpaarkeCore, webresources, then 6 tier-3 parallel | All 8 imported at correct versions | `solimport-{customerId}-{solutionVer}` |
| **H7** | Dataverse env-var values | Set 7 per-customer env vars per §10.3 (`sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId`) | Client startup validates no hardcoded URL fallbacks | `envvars-{customerId}-{configVer}` |
| **H8** | SPE container-type + root container | Uses **confidential-client (app-only) token** with cert bootstrapped from KV (**T6** trap — delegated 403s) | Container GET succeeds; container ID persisted to Dataverse + KV | `spe-{customerId}` |
| **H9** | BFF deploy | `Deploy-BffApi.ps1` + hardened `Deploy-Release.ps1` Phase 4 (customerId-driven, no `spaarkedev1` hardcode) | `/health` = 200; slot-swap smoke test produces no cold-start KV-ref failures | `bff-{customerId}-{buildId}` |
| **H10** | Dataverse App User + Graph app-role parity | Register 2 App Users (BFF app-reg + UAMI) as System Administrator; sync Graph app-role parity from `GraphAppRoles.cs` (**T3**) | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 (**T2**) | `appuser-{customerId}` |
| **H11** | User provisioning | Per identity preset (`B2BGuest` or `NativeAccount`) via r1 registration flow | B2B: consent-verification gate | `users-{customerId}` |
| **H12a** | AI seed chain | type-lookups → actions → tools → knowledge → skills → playbooks → output-types → playbook consumers (single AI routing surface per **ADR-039**) | All seed rows present, no dupes | `aiseed-{customerId}-{seedVer}` |
| **H12b** | App-config seed | DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions (DAG-parallel with H12a) | Config records seeded per manifest | `configseed-{customerId}-{configSeedVer}` |
| **H12c** | Runtime references | `sprk_aimodeldeployment` rows point at correct OpenAI deployment (Model 2 dedicated; Model 1 shared with attribution) | Endpoint resolves via env-var + join | `runtimerefs-{customerId}-{modelVer}` |
| **H13** | E2E acceptance gate | Extended `Validate-DeployedEnvironment.ps1` — verifies `/health`, sample analysis, sample upload+index, layout render, wizard field-map, **all 6 T1–T6 traps cleared**, **all 5 I1–I5 invariants sample-verified**, cost envelope ≤ target | `Setup Status = Ready` only if H13 exits 0 | `validate-{customerId}-{buildId}` |
| **H14** | Post-deploy integrations | (a) 2 Exchange `ApplicationAccessPolicy` (BFF app-reg + UAMI — **T4**); (b) Graph webhook subscriptions per Communication/Email module; (c) Dataverse service-endpoint webhooks. Sub-steps DAG-parallel | `Get-ApplicationAccessPolicy` returns 2 with both principals | `integrations-{customerId}-{integrationVer}` |

### 5.1 Handler dependency DAG

```
H0 --> H1 --> H2a --> { H2b (indexes), H4 (KV), H5 (dv-env) }   # 3-way parallel post-Bicep
                            |
                            v
                       H4 --> H3 (needs KV for secrets) --> { H8 (SPE), H9 (BFF deploy) }

H5 --> H6 (solutions) --> H7 --> H10 (needs H6) --> H11
                                    |
                                    v
                              { H12a (AI seed), H12b (config seed) }   # parallel
                                    |
                                    v
                              H12c (needs H12a + H12b + H2a OpenAI)
                                    |
                                    v
                              H14 { (a) Exchange x2, (b) Graph webhooks, (c) service-endpoint webhooks }  # parallel
                                    |
                                    v
                              H13 (final gate)
```

**Model 2 self-service branch**: `H0.5 (consent-capture) → H0 → …` — pipeline starts on consent callback rather than operator-initiated.

---

## 6. Naming, Configuration & Secret Bootstrap

**Do not restate the naming standard here.** The single authoritative source is [`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md). This section states only the provisioning-relevant call-outs.

### 6.1 KV secret + resource naming (Phase G binding rules)

Per FR-35 (BINDING, per r3 task 063):

- **R1**: Env-agnostic secret names (no `DEV`/`DEMO`/`PROD` as delimited segment)
- **R2**: One canonical casing per logical secret (kebab-case new; PascalCase grandfathered)
- **R3**: Vault name pattern `sprk-{env}-kv` — vault name is **Bicep parameter**, not hardcoded
- **R4**: Dev exception `spaarke-spekvcert` **DO-NOT-RENAME** (codified in Bicep param)
- **Reference syntax (single form)**: `@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=<Canonical-Name>)`

**BINDING pre-check** (BEFORE removing any alias / fallback spelling): verify LIVE App Service + KV + Dataverse-persisted config first. **NEVER delete** `Dataverse-ClientSecret` or `BFF-API-ClientSecret` (OBO + shared-lib Dataverse still depend).

### 6.2 Canonical secret-catalog manifest (Phase H)

Per FR-36. The manifest at `scripts/canonical-secret-catalog/**` is the **single generated source** for:

- The secret seeder script
- The Configure-{env} script
- The tokens documentation
- The Bicep KV secret set

Same manifest generates all four outputs identically — this closes the 4-way drift that produced 3 AI-Search-key aliases in 3 casings + 6 orphan template references pre-consolidation.

### 6.3 UAMI structural fix (Phase C — supersedes system-assigned MI)

Per FR-37. New `uami.bicep` module + `app-service.bicep` refactor consuming UAMI as:

```bicep
identity: {
  type: 'UserAssigned'
  userAssignedIdentities: { '${uamiResourceId}': {} }
}
```

Bound to **both** production and staging slots. All RBAC + Graph app-role grants + Dataverse App User registration migrate from System-Assigned MI principal to UAMI principal. This makes T5 (slot-swap cold-start KV-ref failure) **structurally impossible**.

### 6.4 Dataverse environment variables (7 per customer)

Set by H7 per §10.3 of design.md:

| Variable | Purpose |
|---|---|
| `sprk_BffApiBaseUrl` | BFF App Service URL |
| `sprk_BffApiAppId` | BFF app-reg client ID |
| `sprk_MsalClientId` | MSAL client ID for browser flows |
| `sprk_TenantId` | Customer tenant GUID (I1 enforcement — no default) |
| `sprk_AzureOpenAiEndpoint` | Model 2: dedicated OpenAI; Model 1: shared platform OpenAI |
| `sprk_ShareLinkBaseUrl` | Customer-facing share link base URL |
| `sprk_SharePointEmbeddedContainerId` | Populated from H8 output (I4 enforcement) |

Client startup validates no hardcoded URL fallbacks (per task 024).

### 6.5 App Service configuration

**Reference-only**: full v2 auth setting matrix is in [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) and, until r1 delivers Phase H `/config.json` fetch, in the archived `auth-deployment-setup.md` stub. Core settings the operator must confirm post-H9:

- `Graph__ManagedIdentity__Enabled=true`
- `Graph__ManagedIdentity__ClientId={uami-client-id}`
- `ManagedIdentity__ClientId={uami-client-id}`
- `Dataverse:ClientSecret` = KV reference (BFF `/health` fails fast per r3 task 061 `ValidateOnStart` if unresolved — **NFR-05**)

---

## 7. Pipeline Execution Phases (walkthrough)

Each phase invokes one or more handlers. Post-Phase-D, `/provision-environment` runs this end-to-end. Pre-Phase-D, operator follows §12 interim runbook.

### 7.1 Phase 1 — Preflight & Consent (H0, H0.5)

**Purpose**: fail fast on quota shortage or naming collision BEFORE any resource is created.

**H0** runs unconditionally; **H0.5** runs only for Model 2 self-service (consent-capture branch).

**[GATE]** — H0 must pass:
- Azure OpenAI regional TPM headroom sufficient for +1 provision (150+200+30+350 per-model TPM sum)
- Dataverse env-creation rate quota (`pac admin quota`)
- Subscription vCPU quota
- SPE container-type cert bootstrap done

Failure diagnostic surfaces to operator; run does not start until resolved.

### 7.2 Phase 2 — Subscription & Bicep Infra (H1, H2a, H2b)

**H1** verifies target subscription is reachable + Lighthouse delegation (`CustomerOwned` only).

**H2a** deploys the per-customer Bicep stack:
- **Model 2**: `customer.bicep` — 15 resources per §7.2 of design.md
- **Model 1**: `model1-shared.bicep` — dedicated KV/Cosmos/Storage/UAMI only

Upgrade mode: `az deployment group what-if` runs FIRST; defaults to REJECT + report on drift (per FR-34).

**H2b** provisions AI Search indexes via `scripts/ai-search/Deploy-AllIndexes.ps1` — 7 canonical indexes; per-index invariant verifier confirms required filterable fields + vector fields + forbidden fields absent.

### 7.3 Phase 3 — Identity & Secrets (H3, H4)

**H3** creates the customer's Entra app registration via `scripts/Register-EntraAppRegistrations.ps1`:
- `-TenantId` is **mandatory** (I1 enforcement — no default per v3.3 code fix `1834b77bc`)
- Grants ~14 permissions per `Infrastructure/Auth/GraphAppRoles.cs`
- Client secret stored as KV URI reference (never cleartext)

**Escalation gate** (per FR-13 / H10): 10 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` must be completed via `az` enumeration BEFORE first production customer provisioning.

**H4** populates KV secrets from the canonical catalog manifest + PATCHes `keyVaultReferenceIdentity` to UAMI on both slots (**T1 verification**).

### 7.4 Phase 4 — Dataverse Environment (H5, H6, H7)

**H5** creates the Dataverse env (interim: `pac admin create-environment`; target: TF Power Platform provider per D14).

**H6** imports 8 managed solutions via Package Deployer with dependency ordering per `$SolutionImportOrder` in `scripts/Deploy-DataverseSolutions.ps1`:
- **Tier 1**: `SpaarkeCore`
- **Tier 2**: `webresources`
- **Tier 3 (parallel)**: CalendarSidePane, DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage, LegalWorkspace

**H7** sets the 7 per-customer env-var values (§6.4).

### 7.5 Phase 5 — SharePoint Embedded (H8)

**H8** provisions container-type + root container. **T6 fix**: uses confidential-client (app-only) token with cert bootstrapped from KV — delegated tokens produce `public client not allowed` 403s.

Container ID persisted to Dataverse env-var (`sprk_SharePointEmbeddedContainerId`) AND KV secret (`customer-{customerId}-spe-container-id`) — enables I4 invariant enforcement.

**Lead-time**: container-type replication up to 24h; H0 preflight ensures cert-bootstrap done, so this is not an in-pipeline wait.

### 7.6 Phase 6 — BFF Deployment (H9)

**H9** invokes `Deploy-BffApi.ps1` + hardened `Deploy-Release.ps1` Phase 4 (customerId-driven, no `spaarkedev1` hardcode per Gap 2).

**Blue-green** via staging slot in upgrade mode; rollback via re-swap.

r3-era gates (all must pass):
- Analyzers-as-errors
- God-class ratchet (no NEW server `.cs` > 2,000 LOC; 13 frozen files respect +100 grace)
- 5 new ArchTests (I1–I5 tenant-isolation invariants)
- Naming-conformance (`scripts/naming-conformance-check.ps1` exit 0)
- Graph app-role parity (`GraphAppRoles.cs` constant)

**BFF publish-size ceiling**: **≤ 60 MB compressed (HARD)**. Current net10 baseline: 44.96 MB incl. PDBs. Per-PR measurement + delta report required (NFR-01).

### 7.7 Phase 7 — Dataverse App Users + Users (H10, H11)

**H10** registers 2 Dataverse Application Users as System Administrator:
- BFF app-reg
- UAMI (post-Phase-C)

Then syncs Graph app-role parity from `GraphAppRoles.cs` constant onto UAMI SP.

**T2 verification**: `systemusers?$filter=applicationid eq {uami-app-id}` returns count 1.
**T3 verification**: UAMI SP `appRoleAssignments` includes all 14 role IDs from `GraphAppRoles.cs`.

**H11** provisions users per identity preset (`B2BGuest` or `NativeAccount`) via r1 registration flow.

### 7.8 Phase 8 — Configuration Seed (H12a, H12b, H12c)

**H12a** and **H12b** are DAG-parallel (no cross-dependency):

**H12a** seeds the AI chain: type-lookups → actions → tools → knowledge → skills → playbooks → output-types → **playbook consumers** (single AI routing surface per ADR-039; `spaarke-playbook-embeddings` retired).

**H12b** seeds app-config: DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions.

Both consume the **declarative seed manifest** (resolves the `scripts/seed-data` MVP vs `infra/dataverse` R7 drift per INVENTORY §9).

**H12c** wires runtime references: `sprk_aimodeldeployment` rows point at customer's OpenAI deployment (Model 2 dedicated; Model 1 shared with per-tenant attribution). Runs AFTER H12a + H12b + H2a (OpenAI deployed).

### 7.9 Phase 9 — Post-Deploy Integrations (H14)

Three DAG-parallel sub-steps:

- **(a) 2 Exchange `ApplicationAccessPolicy`** — BFF app-reg + UAMI. Action-and-verify semantics: on 0 or 1 create; on 2+ verify AppIds match else fail with drift diagnostic (**T4 verification**).
- **(b) Graph webhook subscriptions** — per Communication/Email module; HMAC signing keys from H4.
- **(c) Dataverse service-endpoint webhooks** — fire with correct HMAC.

**Explicitly NOT included** (per r3 task 060): S2S consent flows — the S2S Dataverse app-reg was dropped.

### 7.10 Phase 10 — Acceptance Gate (H13 → Ready)

**H13** invokes the extended `Validate-DeployedEnvironment.ps1`, asserting **effects** not intentions (per R7):

- BFF `/health` = 200
- Sample AI analysis end-to-end succeeds
- Sample document upload + AI Search indexing succeeds
- Workspace-layout render succeeds
- Wizard field-map succeeds
- **All 6 §4B T1–T6 silent-fail traps cleared** (see §9)
- `scripts/naming-conformance-check.ps1` exits 0
- **All 5 §4D I1–I5 tenant-isolation invariants sample-verified** (see §8)
- Cost envelope ≤ target per pricing model

**`sprk_dataverseenvironment.Setup Status` transitions to `Ready` only if H13 exits 0.**

---

## 8. Tenant Isolation Invariants (I1 – I5)

Cross-tenant data bleed is the single class of catastrophe r1 must make structurally impossible. **These five invariants are BINDING** and enforced by 5 new ArchTests sequencing into the r3 forcing-functions ecosystem.

| # | Invariant | Enforcement | Severity if breached |
|---|---|---|---|
| **I1** | No hardcoded default tenant in provisioning scripts — every script requires `-TenantId` mandatory | Pre-commit ArchTest; grep-scan for tenant-shaped GUID defaults. Code fix `1834b77bc` removed the `Register-EntraAppRegistrations.ps1:63` default | HIGH — data-bleed (Spaarke users granted to customer Dataverse) |
| **I2** | All AI Search queries include unconditional `tenantId eq` filter — regardless of index or query shape | New ArchTest scans BFF for AI Search `.Search(...)` without `tenantId eq` filter | **CATASTROPHIC** — legal privilege leak (one firm's motions returned to another firm) |
| **I3** | All Cosmos reads/writes include partition-key predicate — no cross-partition queries against tenant-scoped containers | New ArchTest scans for Cosmos SDK `.ReadItemAsync(...)`/`.CreateItemAsync(...)` without explicit `PartitionKey` | HIGH — conversational PII leak |
| **I4** | SPE container IDs always tenant-scoped-derived via `ITenantContainerResolver` — no fallback default | New ArchTest fails on any SPE-container-ID string literal (`b!...`) in BFF services | **CATASTROPHIC** — privileged docs in wrong container |
| **I5** | Graph token acquisition per-tenant scoped — delegated: OBO with caller `tid`; app-only: `.default` with target `tid` explicitly named | New ArchTest scans `GraphClientFactory` for token acquisition without explicit `tenantId` | **CATASTROPHIC** — Graph resources (SPE files, mail, group membership) from wrong tenant |

**Verification lifecycle**:

- **At code time**: 5 ArchTests (CI Tier-1 blocking, coordinated PR with `ci-cd-unit-test-remediation-r1`)
- **At provisioning time**: H13 samples a query in each of the 5 classes
- **At runtime**: OpenTelemetry span attributes include `tenantId`; log samples cross-referenced for anomaly detection

**Scope**: r1's threat model is honest-but-buggy code + operator error. External-actor threat (cross-tenant abuse from the internet) is handled by CORS + AAD auth + per-request `tid` validation per ADR-028.

---

## 9. Silent-Fail Trap Catalog (T1 – T6)

Six known-issue guardrails baked into handler post-conditions. Each has been diagnosed in production; ignoring any results in a BFF that boots but fails silently in a specific code path.

| # | Trap | Owning handler | Verified by |
|---|---|---|---|
| **T1** | App Service `keyVaultReferenceIdentity` not set to UAMI → KV references fail post-swap | H4 | ARM read `keyVaultReferenceIdentity == UAMI-resource-id` |
| **T2** | Dataverse App User for UAMI not registered → BFF loses Dataverse access post-swap | H10 | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 |
| **T3** | UAMI SP missing Graph app-role → Graph calls fail with 403 | H10 | UAMI SP `appRoleAssignments` includes all 14 IDs from `GraphAppRoles.cs` |
| **T4** | Missing Exchange `ApplicationAccessPolicy` → Mail.* calls 403 despite Graph permission grant | H14(a) | `Get-ApplicationAccessPolicy` returns 2 entries with both principals |
| **T5** | Slot MI vs slot MI KV RBAC parity broken → cold-start KV-ref failure after slot swap | H4 (interim); H10 + Phase C UAMI (structural) | Both slot MIs have KV RBAC (interim); **structurally impossible post-Phase-C** |
| **T6** | SPE container-type creation uses delegated token → 403 "public client not allowed" | H8 | Confidential-client cert bootstrapped from KV; container GET via app-only token succeeds |

H13 acceptance gate verifies all 6 traps cleared with 0-failure status.

---

## 10. Upgrade Model (§14A reference)

Handlers execute in **upgrade mode** when `sprk_dataverseenvironment.sprk_provisionedon` is not null.

### 10.1 Three upgrade classes

- **U1 — BFF code**: `Deploy-Release.ps1` / `Deploy-BffApi.ps1` blue-green via staging slot; rollback via re-swap (H9)
- **U2 — Solutions**: Package Deployer upgrade mode retires the holding solution (H6)
- **U3 — Bicep infra**: `az deployment group what-if` runs FIRST; defaults to REJECT + report on drift (H2a)

### 10.2 Per-handler upgrade-mode semantics

| Handler | Upgrade-mode behavior |
|---|---|
| **H0 preflight** | Reads `sprk_bffversion` + `sprk_solutionversion` from registry; queries version-compatibility matrix; blocks incompatible pairs (Red) |
| **H2a Bicep** | Runs `what-if` first; REJECT + report on drift; `runNotes/drift-{customerId}-{timestamp}.md` |
| **H4 KV** | **Rotation-safe** — never overwrites live secrets absent explicit `H4-rotate` variant |
| **H6 solutions** | Package Deployer version-check; retires holding solution |
| **H7 env-vars** | Updates changed values; leaves unchanged in place |
| **H9 BFF** | Blue-green slot-swap; rollback via re-swap |
| **H12a/b/c** | **Additive-only by default**; `--overwrite-authored-content` flag reserved for security-critical fixes |
| **H14 integrations** | Verify-then-add pattern; drift diagnostic on principal mismatch |

### 10.3 Version-compatibility matrix

Published at `docs/deployment/version-compatibility-matrix.md` (task 006). H0 preflight queries it. **Six breaking-change classes**: U-CB-1..U-CB-6 (see task 007 customer-comms templates).

### 10.4 Drift detection

`az deployment group what-if` on H2a; solution import version-check on H6; ArchTest naming-conformance on every PR.

---

## 11. Rollback & Quarantine (§4C reference)

Idempotency + resumability (D11) covers the happy path. For failures that leave the environment in a state a downstream handler cannot proceed from:

### 11.1 Failure classification

| Class | Definition | Recovery |
|---|---|---|
| **Resumable** | Handler failed before external side effect (or wrote to Cosmos only) | Cosmos run marked `Failed`; operator resolves precondition; `POST /api/runs/{id}/resume` restarts the failed handler |
| **Retryable-with-cleanup** | Handler wrote partial external side effect that its own idempotency handles | Re-run handler; own idempotency resumes |
| **Quarantine-required** | Handler wrote partial external side effect NOT self-healing on re-run | Cosmos run marked `Quarantined`; environment NOT usable; operator must manually resolve OR mark for `Decommission-Customer.ps1` teardown; **new run against same `customerId` blocked until quarantine cleared** |
| **Successful-but-drifted** | Handler completed but human edited config between runs | H13 detects; operator re-runs affected phases with `resumeFromPhase` param |

### 11.2 Clearing quarantine

`POST /api/runs/{id}/clear-quarantine` — **reason required**, audit-logged to App Insights.

### 11.3 Automated rollback is explicitly out of scope

Per D17. Rollback = quarantine + operator decision (repair or teardown). This matches Terraform semantics (`terraform destroy` is a separate operator action).

---

## 12. Operator Runbook — Interim Manual Path

**Use this section until Phase D of `customer-provisioning-orchestration-r1` delivers the `/provision-environment` skill.** Once Phase D lands, invoke `/provision-environment` from Claude Code instead.

### 12.1 Pre-work

1. Complete §2.5 preflight naming collision check
2. Verify §2.4 external lead-time items (Azure quota, SPE cert-bootstrap, Model 2 admin consent)
3. Verify §2.2 identity + access role assignments
4. Confirm §3.1/§3.2 tenancy model choice with customer / stakeholder

### 12.2 Provisioning sequence (interim)

```powershell
# Phase 2 — infra
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -TenantId "<customer-tenant-guid>" `   # MANDATORY per I1
    -Environment "prod" `
    -TenancyModel "Model2Dedicated" `      # or "Model1Shared"
    -SubscriptionId "<sub-guid>" `
    -Region "westus2"

# Phase 3 — identity + secrets
.\scripts\Register-EntraAppRegistrations.ps1 `
    -CustomerId "acme" `
    -TenantId "<customer-tenant-guid>"     # MANDATORY per I1 (code fix 1834b77bc)

# H4 KV secret population is performed inside Provision-Customer.ps1 in the interim path
# When Phase H canonical catalog manifest lands, seeder invocation is `scripts/canonical-secret-catalog/Seed-Secrets.ps1`

# Phase 4 — Dataverse
pac admin create-environment `
    --name "spaarke-acme" `
    --region unitedstates `
    --type Sandbox

.\scripts\Deploy-DataverseSolutions.ps1 -EnvironmentUrl "<dv-org-url>"

# Phase 5 — SPE (confidential-client — T6 fix)
.\scripts\Create-NewContainerType.ps1 -CustomerId "acme"
.\scripts\Register-*.ps1                 # per SPE registration ceremony
.\scripts\New-BusinessUnitContainer.ps1 -CustomerId "acme"

# Phase 6 — BFF deploy
.\scripts\Deploy-BffApi.ps1 -CustomerId "acme" -Slot production

# Phase 7 — Dataverse App User + Graph app-role parity
# Interim: PPAC UI + Graph SDK; see auth-deployment-setup stub for MI-first checklist
# T2 verification MANDATORY: systemusers?$filter=applicationid eq {uami-app-id} returns 1

# Phase 8 — config seed
.\scripts\seed-data\Deploy-All-AI-SeedData.ps1 -EnvironmentUrl "<dv-org-url>"
.\scripts\seed-data\Seed-PlaybookConsumers.ps1 -EnvironmentUrl "<dv-org-url>"

# Phase 9 — post-deploy integrations
.\scripts\Set-ApplicationAccessPolicy.ps1     # for both BFF app-reg AND UAMI (T4)
# Graph webhook subscriptions per Communication module — see COMMUNICATION-DEPLOYMENT-GUIDE.md

# Phase 10 — acceptance gate
.\scripts\Validate-DeployedEnvironment.ps1 -CustomerId "acme"
# ONLY if this exits 0 mark Setup Status = Ready in Dataverse
```

### 12.3 Post-provisioning verification (T1 – T6 individually)

Run each verification separately + record in the run's handoff notes:

```powershell
# T1 — App Service keyVaultReferenceIdentity == UAMI
az webapp config show --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --query "keyVaultReferenceIdentity"

# T2 — UAMI registered as Dataverse App User
pac admin application list --environment <dv-org-url>

# T3 — Graph app-role parity (14 roles per GraphAppRoles.cs)
az ad sp show --id <uami-principal-id> --query "appRoleAssignments"

# T4 — Exchange ApplicationAccessPolicy (2 entries)
Get-ApplicationAccessPolicy | Where-Object { $_.AppId -in @($bffAppId, $uamiAppId) }

# T6 — SPE container GET via app-only token
# (see auth-deployment-setup stub for the exact confidential-client invocation)
```

### 12.4 Handoff report

Record in `projects/{active-project}/runs/{runId}.md`:

- CustomerId, tenancy model, all resource names + GUIDs
- Every T1-T6 verification result (green / red)
- Every I1-I5 sample-query result
- BFF publish-size delta vs baseline
- Any manual gate + operator decision + timestamp

---

## 13. Troubleshooting

### 13.1 BFF `/health` fails at boot

Per **NFR-05** (r3 task 061 landed): any Tier-1 `IOptions<T>` misconfig fails `/health` on startup, not runtime.

Diagnostic:
1. `az webapp log tail --name spaarke-bff-{customer}-{env}` — look for `OptionsValidationException`
2. Verify KV RBAC — UAMI must have Key Vault Secrets User role
3. Verify `keyVaultReferenceIdentity` == UAMI (T1)
4. Re-check any renamed secret cited in the exception against Phase G rename map

### 13.2 Slot-swap produces 503 cold-start window (T5)

**Pre-Phase-C** (System-Assigned MI): both slot MIs must have KV RBAC parity. Grant KV Secrets User to BOTH prod-slot + staging-slot MI principals before swap.

**Post-Phase-C** (UAMI): T5 is structurally impossible — same UAMI bound to both slots. If still observed, verify `app-service.bicep` refactor landed on this environment.

### 13.3 SPE 403 "public client not allowed"

T6 root cause. Handler H8 (or interim `Create-NewContainerType.ps1`) must use confidential-client (app-only) token with cert bootstrapped from KV. If retrofitting an existing env: re-run H8 (or manually invoke updated script) with `-UseConfidentialClient` switch.

### 13.4 AI Search returns cross-tenant results

I2 violation. This is a **CATASTROPHIC** severity finding. Immediate actions:

1. Take affected BFF instance out of rotation
2. Audit the query in question — confirm missing `tenantId eq` filter
3. File security incident per `docs/guides/INCIDENT-RESPONSE.md`
4. Verify I2 ArchTest is in CI (if not, the offending change bypassed the gate — investigate)

### 13.5 Handler quarantined

Per §11.1. If `sprk_currentrunid` is set + status = `Quarantined`:

1. Investigate the partial external side effect (Cosmos run log at `GET /api/runs/{id}/phases/{phase}/logs`)
2. Choose repair-vs-teardown
3. **Repair path**: manual resolve + `POST /api/runs/{id}/clear-quarantine` (reason required, audit-logged)
4. **Teardown path**: `.\scripts\Decommission-Customer.ps1 -CustomerId {id}` then start fresh run

### 13.6 Naming-conformance failure at H13

`scripts/naming-conformance-check.ps1` exits non-0. Read the output — typically a resource created via portal / manual `az` invocation that doesn't match `sprk-{env}-*` / `spaarke-*-{env}` pattern. Either rename (if safe) or add to the Phase G exception list with rationale (must be reviewed at code-review).

---

## 14. Appendix A — Legacy Guides Merged Into This Doc

The following files previously carried overlapping / fragmented customer-provisioning content. They have been reduced to one-paragraph stubs pointing here. Their git history is preserved.

| Retired guide | Coverage merged into |
|---|---|
| [`CUSTOMER-DEPLOYMENT-GUIDE.md`](CUSTOMER-DEPLOYMENT-GUIDE.md) | §1, §2, §3, §7 |
| [`CUSTOMER-ONBOARDING-RUNBOOK.md`](CUSTOMER-ONBOARDING-RUNBOOK.md) | §2, §12 |
| [`ENVIRONMENT-DEPLOYMENT-GUIDE.md`](ENVIRONMENT-DEPLOYMENT-GUIDE.md) | §7.2 – §7.9, §13 |
| [`auth-deployment-setup.md`](auth-deployment-setup.md) | §6.5, §7.3, §7.7, §13.1 |
| [`SPAARKE-DEPLOYMENT-GUIDE.md`](SPAARKE-DEPLOYMENT-GUIDE.md) | Whole guide (2026-06-26 partial consolidation attempt superseded) |
| [`PRODUCTION-DEPLOYMENT-GUIDE.md`](PRODUCTION-DEPLOYMENT-GUIDE.md) | Whole guide (already superseded by SPAARKE-DEPLOYMENT-GUIDE) |

## 15. Appendix B — Related Component-Specific Deployment Guides (retained)

These are **module-scoped** deployment / build workflows — NOT customer-provisioning guides. They are retained as-is; this authoritative guide does not restate them.

| Component | Guide |
|---|---|
| PCF controls (build + push workflow) | [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md) |
| AI Document Intelligence module | [`AI-DEPLOYMENT-GUIDE.md`](AI-DEPLOYMENT-GUIDE.md) |
| Email / Communication Service | [`COMMUNICATION-DEPLOYMENT-GUIDE.md`](COMMUNICATION-DEPLOYMENT-GUIDE.md) |
| M365 Copilot integration | [`M365-COPILOT-DEPLOYMENT-GUIDE.md`](M365-COPILOT-DEPLOYMENT-GUIDE.md) |
| Declarative agent | [`DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md`](DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md) |
| Office add-ins | [`office-addins-deployment-checklist.md`](office-addins-deployment-checklist.md) |
| AI playbook deploy recipe | [`ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md) |
| Cross-cutting verification quick-reference | [`DEPLOYMENT-VERIFICATION-GUIDE.md`](DEPLOYMENT-VERIFICATION-GUIDE.md) |
| Secret rotation cadence + procedures | [`SECRET-ROTATION-PROCEDURES.md`](SECRET-ROTATION-PROCEDURES.md) |
| MI configuration patterns | [`MI-CONFIGURATION-PATTERNS.md`](MI-CONFIGURATION-PATTERNS.md) |
| Incident response | [`INCIDENT-RESPONSE.md`](INCIDENT-RESPONSE.md) |
| Dataverse authentication | [`DATAVERSE-AUTHENTICATION-GUIDE.md`](DATAVERSE-AUTHENTICATION-GUIDE.md) |

## 16. Appendix C — Change Log

| Date | Change | Source |
|---|---|---|
| 2026-08-17 | Initial consolidation (task 001 of `customer-provisioning-orchestration-r1`) | spec.md Gap 4 + R6 doc-drift carry-over; design.md §2 (3-generation fragmentation) |

---

*Maintained by `customer-provisioning-orchestration-r1`. Update this guide (do not fork) when handler catalog / DAG / tenancy model / naming convention evolves. All updates should be per-project and cite the driving spec / FR / task ID.*
