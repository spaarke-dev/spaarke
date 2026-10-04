# Spaarke Infrastructure Packaging Strategy

> **Version**: 2.0
> **Date**: December 4, 2025
> **Last Reviewed**: 2026-09-28
> **Reviewed By**: `unified-access-control-r2` (D-12 deployment-model redefinition)
> **Status**: Current
> **Purpose**: Define how Azure resources and Power Platform components are packaged for both deployment models

---

> 🟡 **REWRITTEN 2026-09-28 for owner decision D-12** ([`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md)).
> This document previously described Model 1 as a **multi-tenant / shared** stack — a shared App Service
> Plan, shared Redis, shared Azure OpenAI, a shared AI Search service with one index per customer, and
> "multi-tenant slots". **That model is retired.** So are the `model1-shared.bicep` /
> `model1-customer.bicep` stacks and `Deploy-Model1-Shared.ps1`.

## Executive Summary

Spaarke has **two deployment models**. They differ in **one axis only: which Azure tenant owns the
customer's subscription.** Everything else is the same.

| | Dataverse environment | Azure tenant | Azure subscription + resource group |
|---|---|---|---|
| **Model 1** | dedicated, one per customer | **Spaarke's** | dedicated, one per customer |
| **Model 2** | dedicated, one per customer | **the customer's own** | dedicated, one per customer |

**Every Azure resource is dedicated per customer in both models** — App Service Plan, App Service, AI
Search, Redis, Azure OpenAI, Cosmos, Key Vault, Storage, Service Bus, App Insights, SPE container,
Dataverse environment. There are exactly **two named exceptions**, both of which hold no customer data at
rest: **Static Web Apps** and **Content Safety** (see §2).

Only two things actually differ, and both follow from tenant ownership: **Model 2 requires H0.5 admin
consent** and **Model 2 requires Azure Lighthouse delegation**. Model 1 requires neither. There is also one
genuine infrastructural difference outside Azure: Model 1 customers share **one SPE *consuming tenant***
(Spaarke's) — see [`SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md).

**There is no Model 3, no "shared"/"trial"/"SMB" tier, and no 2a/2b split.**

🔴 **Terminology, binding**: *tenant* = the Entra/Azure/Dataverse **tenant GUID**. *customer* = the business
entity. Under Model 1 **many customers share one Azure tenant**, so `tenantId` **cannot** distinguish them.
Never write "tenant" when you mean "customer".

Infrastructure-as-Code uses **Bicep** for Azure resources and **Power Platform managed solutions** for Dataverse/model-driven apps.

---

## 1. Deployment Models

### Model 1 vs Model 2 Resource Ownership

| Layer | Model 1 (Spaarke's Azure tenant) | Model 2 (Customer's Azure tenant) |
|-------|----------------------------------|-----------------------------------|
| **Foundation** | Spaarke's Entra tenant; **the customer's own Azure subscription + resource group** | Customer's Entra tenant; the customer's own Azure subscription + resource group |
| **Platform Services** | Spaarke-owned, **dedicated per customer** | Customer-owned, dedicated per customer |
| **Application** | Spaarke-owned, **one BFF App Service + plan per customer** | Customer-deployed, one BFF App Service + plan per customer |
| **Customer Resources** | Spaarke-owned, dedicated per customer | Customer-owned, dedicated per customer |
| **AI Services** | Spaarke-owned, **dedicated per customer** | Customer-owned (BYOK), dedicated per customer |
| **Admin consent (H0.5)** | not required | **required** |
| **Azure Lighthouse delegation** | not required | **required** |

🔴 **Why dedication, not a filter.** Under Model 1 every customer presents the **same `tenantId`**
(Spaarke's). Every isolation control keyed on `tenantId` — an AI Search `tenantId eq` filter, a Cosmos
`/tenantId` partition, an SPE container resolver, a `tenant:{tenantId}:…` Redis key — therefore **cannot
separate customers, and its tests pass anyway**. Customer isolation comes from the **dedicated resource in
a per-customer subscription**: a boundary, not a filter. A filter must be written correctly in every query,
forever, by everyone; a boundary cannot be forgotten. `tenantId` controls stay in force as
**belt-and-braces**.

### Infrastructure Layers

Every layer below is provisioned **once per customer**, into that customer's own subscription and resource
group.

| Layer | Contents |
|-------|----------|
| **Layer 0: Billing boundary** | **Azure subscription + resource group, one per customer** (D-12 §3a — makes usage segregable and billable per customer, and gives genuine Azure OpenAI TPM-quota isolation, since quota is per-subscription-per-region) |
| **Layer 1: Foundation** | Entra ID tenant (Spaarke's for Model 1, the customer's for Model 2), Resource Providers |
| **Layer 2: Observability + secrets** | Key Vault, Log Analytics, App Insights — **all dedicated per customer** |
| **Layer 3: Platform Services** | App Service Plan, Redis Cache, Service Bus, Storage — **all dedicated per customer** |
| **Layer 4: Application** | App Registration, Sprk.Bff.Api App Service — **one per customer** |
| **Layer 5: Customer Resources** | SPE Container, AI Search service + index, Dataverse Environment |
| **Layer 6: AI Services** | Azure OpenAI, Azure AI Search, Document Intelligence — **all dedicated per customer** |

🔴 **An App Service app cannot use an App Service Plan in a different subscription.** With one subscription
per customer, a shared plan is not merely undesirable — it is **impossible**. This is what closed the old
"shared plan for Model 1" option (D-12 §4, ADR-027 amendment).

---

## 2. Resource Inventory

### Entra ID Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **BFF API App Registration** | Sprk.Bff.Api authentication | Spaarke tenant | Customer tenant |
| **PCF/UI App Registration** | Client-side auth (MSAL) | Spaarke tenant | Customer tenant |
| **SPE App Registration** | SharePoint Embedded access | Spaarke tenant | Customer tenant |
| **Application Users** | Service accounts in Dataverse | Per-environment | Per-environment |

### Azure Resources

The Model 1 / Model 2 columns are gone deliberately: **the disposition is the same in both models.**

| Resource | Purpose | Disposition (both models) | Why |
|----------|---------|---------------------------|-----|
| **Azure Subscription + Resource Group** | Billing + blast-radius boundary | **Dedicated per customer** | Segregates and attributes usage per customer; gives real Azure OpenAI TPM-quota isolation (quota is per-subscription-per-region); forces the App Service Plan to be dedicated |
| **App Service Plan** | Compute for BFF API | **Dedicated per customer** | A plan cannot span subscriptions — forced by the per-customer subscription. Largest single per-customer fixed cost |
| **App Service** | Sprk.Bff.Api hosting | **One BFF app per customer** | Each BFF is bound to one Dataverse environment through per-deployment config (`AzureAd:TenantId`, Dataverse URL) |
| **Key Vault** | Secrets, certificates | **Dedicated per customer** | Holds secrets; vault-scoped RBAC; ~free to dedicate |
| **Redis Cache** | Token caching, sessions | **Dedicated per customer — Azure Managed Redis Balanced_B0, high availability, Entra only** (owner D12, task 242) | Auth is **per-instance, not per-keyspace** — any identity with access reaches the whole keyspace, so each customer gets its own cache. Access keys are disabled; the stamp UAMI holds the only access-policy assignment. Azure Cache for Redis (Basic/Standard/Premium) retires 2028-09-30 and blocks new-customer creation since 2026-04-01. A Redis loss costs a cold start, not data |
| **Service Bus** | Job queue | **Dedicated namespace per customer** | Already per-customer; ~free |
| **Storage** | Document/temp bytes | **Dedicated per customer** | Holds data at rest |
| **Cosmos DB** | Audit, sessions, memory | **Dedicated account per customer** | Holds data at rest. Serverless ⇒ no fixed floor. ⚠️ A `/tenantId` partition does **not** separate Model 1 customers |
| **App Insights / Log Analytics** | Telemetry | **Dedicated per customer** (promoted from shared 2026-09-28) | Telemetry legitimately carries user and record identifiers; a customer may need access to their own output, which cannot be granted on a workspace holding other customers' telemetry; billing is per workspace, so this also makes cost attributable |
| **SignalR** | Realtime notifications | Dedicated when enabled | Feature-gated (`Notifications:SignalRSpine:Enabled`), Null-Object when off |

### AI Resources

| Resource | Purpose | Disposition (both models) | Why |
|----------|---------|---------------------------|-----|
| **Azure OpenAI** | LLM inference | **Dedicated per customer** | ⚠️ The reasoning differs from the others: Azure OpenAI does not persist prompts or completions by default, so the *data*-segregation case is weaker. The reason to dedicate is **noisy-neighbour / quota isolation**. Consumption-priced, so dedication costs nothing extra. Model 2 may additionally be customer-BYOK |
| **Azure AI Search** | Vector search | **Dedicated service per customer** | Holds indexed document text and embeddings — the highest-value segregation case after Dataverse. A shared service with a `tenantId` preFilter separates **Entra tenants**, not customers, so one filter defect exposes another firm's material |
| **Document Intelligence** | OCR/extraction | **Dedicated per customer** | Stateless per call, so technically shareable — dedicated because consumption pricing makes it free to be |

### 🔴 The exception list, and it is CLOSED

Exactly **two** Azure resources may stay shared across customers. A resource not named here is dedicated;
adding a third is a new owner decision, not an inference from "it seemed like infrastructure".

| Resource | Why it may stay shared | Required of it |
|----------|------------------------|----------------|
| **Static Web Apps** (Office add-ins + external SPA) | Static client bundles. Hold no customer data at rest; the privileged content they display is fetched per-request through the BFF, which **is** per-customer | 🔴 A **`customerId`** discriminator in the BFF runtime for anything they read or write |
| **Content Safety** | Stateless classifier — no persistence, nothing to leak between calls | 🔴 A **`customerId`** discriminator on any call that is logged or metered |

The test any future candidate must pass is **"it holds no privileged legal content at rest"** — not cost,
and not "it feels like plumbing". Note that `customerId` is modelled correctly in the L2 control plane
(alongside `TenantId`) and **has never reached the BFF runtime**; these two resources are the explicit,
bounded reason it must.

### Power Platform Resources

| Resource | Purpose | Packaging |
|----------|---------|-----------|
| **Dataverse Solution** | Entities, forms, views | Managed solution `.zip` |
| **PCF Controls** | React components | Part of solution |
| **Security Roles** | RBAC | Part of solution |
| **Environment Variables** | Runtime config | Part of solution |

---

## 3. Packaging Strategy

### Package Types

Infrastructure is organized into two parallel packaging tracks:

**Azure (Bicep)**:
- `infrastructure/bicep/modules/` — Reusable Bicep modules per resource type
- `infrastructure/bicep/customer.bicep` — The **full per-customer stack** and the **only** customer-stamp template (owner decision D19, 2026-10-02). It deploys the same set of dedicated resources in both models, and only the L2 control plane deploys it — handler H2a, into the customer's own subscription (amended ADR-027).
- `infrastructure/bicep/stacks/` — Holds only the standalone `ai-foundry-stack.bicep` (not used by `customer.bicep`).
  > 🔴 **Retired and deleted** (task 225a, 2026-10-01): `model1-shared.bicep` (+ its manifest entry),
  > `model1-customer.bicep`, `model1-shared-l2-rbac.bicep`, `model1-prod.bicepparam` and the
  > `parameters/{dev,staging,prod}.bicepparam` that `using` them. They encoded the shared-then-overlay shape
  > and have no successor.
  >
  > 🔴 **Retired and deleted** (task 249, 2026-10-02): `stacks/model2-full.{bicep,json}`,
  > `stacks/{dev,staging,prod}.bicepparam`, `parameters/model2-customer-template.bicepparam` and
  > `customer-deployment.bicepparam`. `model2-full.bicep` deployed no live environment and could not deploy as
  > written; `customer.bicep` is its successor. Git history keeps the files.
- `infrastructure/bicep/parameters/` — `customer-template.bicepparam` and `demo-customer.bicepparam` (both `using '../customer.bicep'`) are compile-checked **reference** parameter sets — H2a does not read them; it sends a computed parameter payload per run (task 249). Plus `platform-*` / `redis-*` files for non-customer infrastructure.

**Power Platform**:
- `power-platform/solutions/SpaarkeCore/` — Core entities, forms, views (managed solution ZIP)
- `power-platform/solutions/SpaarkePCF/` — PCF controls (managed solution ZIP)
- `power-platform/solutions/SpaarkeAI/` — AI configuration entities (managed solution ZIP)

### The deployment shape — one full stack per customer, in both models

There is **no shared-platform step and no per-customer overlay on top of it.** Onboarding a customer is a
single full-stack deployment into that customer's own subscription and resource group:

1. **Create the customer's Azure subscription and resource group** (in Spaarke's Azure tenant for Model 1,
   in the customer's for Model 2).
2. **Model 2 only** — obtain H0.5 admin consent and establish Azure Lighthouse delegation. *Model 1 requires
   neither: Spaarke already owns the tenant.*
3. **Deploy the full per-customer Azure stack via Bicep** (`customer.bicep`, deployed by L2 handler H2a) —
   App Service Plan + BFF App Service, Key Vault, Redis (Azure Managed Redis B0, Entra only), Service Bus, Storage, Cosmos,
   App Insights / Log Analytics, Azure OpenAI, AI Search, Document Intelligence.
4. **Create App Registrations** and the per-customer BFF identity (applies to **both** models).
5. **Create the SPE Container**, import the Power Platform managed solutions, create the Application User,
   and set the Dataverse environment variables.

> There is **no standalone deployment script** for the customer stack: the L2 control plane (handler H2a)
> is its only deployer, driven by the `/provision-environment` operator skill. The `Deploy-Model1-Shared.ps1`,
> `Deploy-Model1-Customer.ps1` and `Deploy-Model2-Full.ps1` scripts named in earlier revisions of this
> document never existed in the repository *(references retired by task 249, 2026-10-02)*.

---

## 4. Configuration Management

### Configuration Strategy: Two Mechanisms

Spaarke uses **two distinct config mechanisms** depending on whether config is consumed by the BFF API (server-side) or client components (browser-side):

| Config Type | Storage | Varies By Env | Varies By Customer |
|-------------|---------|---------------|--------------------|
| **Client auth config** | Dataverse Environment Variables | Yes | Yes |
| **Server auth/infra** | Azure App Service + Key Vault | Yes | **Yes — each customer has its own BFF App Service and its own Key Vault** |
| **Feature Flags** | Azure App Service config | Yes | Yes (per-customer app) |
| **AI Config** | Azure App Service config + Key Vault | Yes | Yes (per-customer app) |
| **Business Rules** | Dataverse Environment Variables | No | Yes |

**Key constraint**: Client-side components (code pages, PCF controls, Office add-ins) run in the browser and cannot read Azure App Service settings. They call `resolveRuntimeConfig()` from `@spaarke/auth` at startup — this queries the 7 Dataverse Environment Variables via REST API using session cookie auth (before MSAL is initialized).

### Client-Side: Dataverse Environment Variables (7 vars)

Set once per Dataverse environment after solution import. No hardcoded values ship in the solution package. If any required variable is missing, `resolveRuntimeConfig()` throws and the page fails to load — there are no silent fallbacks.

| Variable | Purpose |
|----------|---------|
| `sprk_BffApiBaseUrl` | BFF API base URL |
| `sprk_BffApiAppId` | BFF API OAuth audience |
| `sprk_MsalClientId` | UI MSAL client ID for Entra ID sign-in |
| `sprk_TenantId` | Entra ID tenant ID. **v2 requirement (ADR-028)**: `initAuth()` reads this FIRST; Xrm frame-walk is fallback only. See [`auth-deployment-setup.md`](../guides/auth-deployment-setup.md) §3. |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI endpoint |
| `sprk_ShareLinkBaseUrl` | Base URL for document share links |
| `sprk_SharePointEmbeddedContainerId` | SPE Container ID |

### Server-Side: Azure App Service + Key Vault

Configures Sprk.Bff.Api. Sensitive values use Key Vault references (`@Microsoft.KeyVault(VaultName=...;SecretName=...)`). In local dev, values go in `appsettings.Development.json` or user secrets.

### Per-Customer Configuration

In **both models**, customer-specific configuration is stored in a `sprk_CustomerConfiguration` Dataverse entity in that customer's own Dataverse environment, including SPE Container ID, AI Search index name, enabled AI features, and usage limits. Because each customer has a dedicated BFF App Service, its Azure-side configuration (`AzureAd:TenantId`, Dataverse URL, resource endpoints) is per-customer by construction.

---

## 5. BFF API Binary Packaging

The BFF API (`src/server/api/Sprk.Bff.Api/`) targets Linux App Service and uses framework-dependent publishing with several csproj-level constraints to minimize publish size and patch transitive vulnerabilities. The patterns below were established by the `sdap-bff-api-remediation-fix` project (Phase 4 Outcomes A + B, 2026-05-25).

### Framework-Dependent linux-x64 Publish (FR-A1)

The csproj contains:

```xml
<RuntimeIdentifier>linux-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
```

This eliminates the entire `runtimes/` tree (10 RIDs → 0). The .NET runtime is supplied by the App Service Linux host. `<SelfContained>false</SelfContained>` is made explicit alongside the RID to prevent accidental self-contained publishes when a future `dotnet publish` call lacks `--no-self-contained`.

### Sourcemap Exclusion (FR-A2)

```xml
<Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" />
```

Keeps `.js.map` files in the source tree for dev-time debugging but excludes them from publish output. The 4 sourcemaps in `wwwroot/playbook-builder/assets/` are unchanged in source.

### Transitive Vulnerability Override Pattern (FR-B1)

```xml
<PackageReference Include="System.Security.Cryptography.Xml" Version="8.0.3" />
```

An explicit transitive override patches CVEs (GHSA-37gx-xxp4-5rgx + GHSA-w3x6-4m5h-cxqf, both HIGH) without bumping the parent identity stack (`Microsoft.IdentityModel.*` family). Pattern of last resort for surgical CVE patching when major version bumps are out of scope. Same pattern already established in csproj for `System.Text.RegularExpressions 4.3.1`.

### Phase 5 Measured Baselines (Post-Outcome A)

| Metric | Before (2026-05-19 drift) | After (Phase 4 close) | Delta |
|---|---:|---:|---:|
| Uncompressed publish | 212.5 MB | 139 MB | -35% |
| Compressed zip | 72.9 MB | 45.65 MB | -37% |
| `deps.json` entries | 526 | 268 | -49% |
| File count | 287 | 279 | -8 |

### References

- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — binding rules for BFF publishing (publish location, baseline size, stdout logging)
- [`projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md`](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) Phase 4 Outcome A — full evidence (smoke probes, deploy logs, reflection-load delta)
- [ADR-029](../adr/ADR-029-bff-publish-hygiene.md) — BFF Publish Hygiene (Accepted 2026-05-26)

---

## 6. The per-customer deployment package

The same template deploys the same stack in both models. What differs is **where**: under Model 1 the stamp
lands in the customer's subscription inside Spaarke's tenant; under Model 2 (via Lighthouse delegation,
after H0.5 consent) in the customer's own tenant. In both, the **L2 control plane is the only deployer** —
handler H2a deploys `customer.bicep` into the customer's own subscription (owner decision D19, amended
ADR-027). There is no hand-carried package or customer-run script; the Azure inputs are:

```
infrastructure/bicep/
├── customer.bicep                         # The customer-stamp template (deployed by H2a)
├── modules/                               # Modules it composes
└── parameters/customer-template.bicepparam  # Reference parameter set (H2a computes its own payload)
```

The operator entry point is the `/provision-environment` skill; the end-to-end procedure is
[`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md). *(The
`model2-deployment-package/` layout around a `Deploy-Model2-Full.ps1` script that this section showed
before task 249 was never built.)*

---

## Appendix: Related ADRs

| ADR | Description |
|-----|-------------|
| ADR-012 | Infrastructure Packaging Strategy |
| ADR-014 | Multi-Tenant Resource Isolation — ⚠️ read alongside D-12: the mechanisms it describes separate **Entra tenants**; **customer** separation is the dedicated per-customer resource |
| ADR-027 | Subscription Isolation — amended 2026-09-28 for the per-customer subscription (D-12 §3a) |
| ADR-009 / ADR-013 / ADR-015 / ADR-052 | Amended 2026-09-28 for D-12 — see each ADR's amendment block |

---

## References

- [Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Power Platform ALM](https://learn.microsoft.com/en-us/power-platform/alm/)
- [SharePoint Embedded Provisioning](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/admin-exp/cta)

---

*Document Owner: Spaarke Engineering*
