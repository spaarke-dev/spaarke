# Spaarke Customer Deployment Guide

> **Version**: 1.0 (consolidation baseline)
> **Last Updated**: 2026-08-17
> **Status**: **Authoritative**. Supersedes the customer-provisioning content in `CUSTOMER-DEPLOYMENT-GUIDE.md`, `CUSTOMER-ONBOARDING-RUNBOOK.md`, `ENVIRONMENT-DEPLOYMENT-GUIDE.md`, `auth-deployment-setup.md`, `SPAARKE-DEPLOYMENT-GUIDE.md`, and `PRODUCTION-DEPLOYMENT-GUIDE.md`. Those files are retained as one-paragraph stubs pointing here.
> **Audience**: Platform Operator (primary). Claude Code AI executes automated phases; a human operator holds accountability for gates, secrets, and customer communication.
> **Applies To**: Every new Spaarke customer environment. **Model 1** = dedicated stamp in Spaarke's Azure tenant; **Model 2** = dedicated stamp in the customer's own Azure tenant (D-12, 2026-09-28).
> **Owner**: Platform Operations. Maintained by `customer-provisioning-orchestration-r1` and its successors.

---

## 0. How to Use This Guide

This is the single authoritative guide for standing up a new Spaarke customer environment end-to-end. It covers **both** target-state (the r1 pipeline delivering L1 handlers + L2 control-plane + L3 `/provision-environment` skill) and the **transitional operator path** (existing PowerShell scripts + manual gates) that remains in use until Phase D of `customer-provisioning-orchestration-r1` lands.

### What this guide contains

- Prerequisites, tenancy-model selection, per-phase execution walkthrough (H0 through H14)
- Tenant-isolation invariants (I1–I5) and silent-fail trap catalog (T1–T7)
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
- Model 1 (Spaarke-tenant dedicated stamp) + Model 2 (customer-tenant dedicated stamp)
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
| Customer ID | `acme` — the customerId standard: 3-8 lowercase letters and digits, starting with a letter ([naming convention](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)) | Assigned once at customer intake; abbreviate longer names (`northwind` → `nwind`) |
| Customer display name | "Acme Legal Services" | Customer intake |
| The customer's own subscription ID | `2ff9ee48-...` | **The operator creates it** (prereqs.yaml PRQ-S-00) and grants the L2 identity **Owner** on it (PRQ-S-04, `infrastructure/bicep/modules/controlplane-subscription-rbac.bicep`). Required at intake for every model; nothing defaults it (T228) |
| The customer's Dataverse environment URL | `https://spaarke-acme.crm.dynamics.com/` | **The operator creates it** (PRQ-C-09) with domain `spaarke-{customerId}` (or `spaarke-{customerId}-{environmentName}`) and adds the L2 Worker identity as a System Administrator application user. Required at intake (`dataverseEnvUrl`); H5 adopts it (T228) |
| Target Entra tenant ID (`tid`) | `a221a95e-...` | Model 2: customer's tenant; Model 1: Spaarke tenant |
| Azure region | `westus2` (default) | Customer intake / geo requirement |
| Dataverse region | `unitedstates` (default) | Chosen by the operator when creating the environment (PRQ-C-09); match the Azure region's locality |
| Tenancy model | `Model1` (dedicated stamp hosted in Spaarke's tenant) or `Model2` (dedicated stamp in the customer's tenant). Exact case — `POST /api/runs` refuses any other value. | Per §3 selection criteria |
| Deployment profile | Follows from the tenancy model: `Model1` ↔ `spaarke-hosted-model2`, `Model2` ↔ `customer-owned-model2`. `POST /api/runs` refuses any other pair (`tenancy-profile-invalid`). The names predate the D-12 renumbering. `spaarke-hosted-model1-trial` is **retired** and refused as an unknown profile. | Per D15 |
| Customer admin contact | Name, email, phone | For H0.5 consent flow (Model 2) |

### 2.4 External lead-time items (surface BEFORE starting pipeline)

Per H0 preflight (§7.1). Items surfaced **up front**, NOT counted as pipeline time (NFR-03):

- **Azure OpenAI regional TPM quota** — `az cognitiveservices` returns per-model per-region quota; lead time 1–3 days for quota bump
- **Azure subscription vCPU quota** — verify per SKU per region
- **The customer's subscription and Dataverse environment** — created by the operator before the run (PRQ-S-00, PRQ-S-04,
  PRQ-C-09; T228). L2 creates neither — the former environment-creation rate limit no longer applies
- **SPE container type + owning app** — one-time per container type, not per customer: [`SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md`](./SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md) (owning app with a federated credential trusting the L2 Worker UAMI — no certificate, no secret; `Spaarke Model 1` exists since 2026-10-03). H0's `SpeOwnerCredential` check refuses the run until it is in place; there is no 24 h wait
- **Exchange admin app** — one-time per tenant, not per customer: §4.2.1 (`Spaarke Exchange Admin`, a federated credential trusting the L2 Worker UAMI — no certificate, no secret — plus a narrowed Exchange role). H14a and H13 T4 report "not configured" until it is in place
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

> 🔴 **REWRITTEN 2026-09-28 — owner decisions D-12 + D-13.** This section previously defined **Model 1 as
> a shared trial/SMB tier** sharing App Service Plan, Azure OpenAI and AI Search across customers. That tier
> is **RETIRED**. It was documented, specified, funded and partly built in Bicep, but **never implemented in
> the engine** — H5 always made one Dataverse environment per customer, keyed `dvenv-{customerId}` (since T228 it
> adopts the one the operator created). There has never been a code path in which two customers share an environment.

Spaarke supports **two deployment models**. They differ on **one axis only: which Azure tenant owns the
customer's subscription.** Everything else is the same.

| | Dataverse environment | Azure tenant | Azure subscription + resource group |
|---|---|---|---|
| **Model 1** | dedicated, one per customer | **Spaarke's** | dedicated, one per customer |
| **Model 2** | dedicated, one per customer | **the customer's own** | dedicated, one per customer |

**Only two things actually differ**, and both follow from tenant ownership:

| | Model 1 | Model 2 |
|---|---|---|
| **H0.5 admin consent** | not needed (Spaarke's own tenant) | **required** |
| **H1 Azure Lighthouse delegation** | not needed (Spaarke owns the subscription) | **required** |

⚠️ **The models are now nearly identical infrastructurally.** Resist the pull to justify having two models
by inventing differences between them — the list above is complete.

### 3.1 Terminology — binding

| Term | Meaning |
|---|---|
| **tenant** / `tenantId` | The Entra / Azure / Dataverse **tenant GUID**. Model 1 ⇒ always Spaarke's. Model 2 ⇒ the customer's. |
| **customer** | The business entity Spaarke sells to. **Many customers share one Azure tenant** under Model 1. |

🔴 **Never write "tenant" when you mean "customer."** Under Model 1 every customer presents the **same**
`tenantId`, so any control keyed on it — an AI Search `tenantId eq` filter, a Cosmos `/tenantId` partition,
an SPE container resolver, a `tenant:{tenantId}:…` cache key — **cannot separate customers, and its tests
pass anyway.**

### 3.2 Composition — identical in both models

**Dedicated per customer**: Dataverse environment · SPE container-type + root container ·
**Entra app registration (BFF)** · App Service + Plan · Azure OpenAI · AI Search · Cosmos DB ·
**Redis (Azure Managed Redis Balanced_B0, HA, Entra only)** · Storage · Key Vault · Service Bus · Document Intelligence ·
**Content Safety (T246, Entra only)** · App Insights + Log Analytics · UAMI · Power BI workspace.

**Cost (T229)**: one envelope per stamp in both models — Model 1 paid by Spaarke, Model 2 by the customer. An empty
stamp's fixed cost at 2026-10-06 list prices (westus2, 730 h): App Service S1 Linux $58.40 + AI Search Standard S1 $245.28 +
Managed Redis Balanced_B0 with high availability $23.36 + Service Bus Standard $10.00 = **$337.04/month**; Cosmos DB
(serverless), Storage, Key Vault, Log Analytics / App Insights, Azure OpenAI, Document Intelligence and Content Safety are
consumption-billed. H13 compares the subscription's cost with **$400/month** (`H13AcceptanceOptions.DedicatedStampEnvelopeUsd`,
> 20 % drift flagged). Re-derive both numbers when `customer.bicep`'s SKUs change.

#### 3.2a Cost envelope at intake (T229)

The intake carries `tier` and `estimatedMonthlyUsd` for **every** model (schema, POST /api/runs and H0 apply the same
rules — `CostEnvelopeIntake`). `estimatedMonthlyUsd` is the stamp's projected monthly Azure spend: the fixed cost above
plus the customer's expected OpenAI, Document Intelligence, Content Safety, Cosmos DB, storage and log volume. `tier` is
the budget class whose ceiling covers it — `smb` $700 · `enterprise` $2,500 · `dedicated` $5,000 per month
(`H0Options.DefaultCeilingsUsd`; a deployment may change a tier's ceiling with `H0__TierMonthlyCostCeilingsUsd__{tier}`).
H0 refuses an estimate above the ceiling (`quota-cost-overrun`, Resumable) — there is no override; the operator picks the
tier that covers the stamp. The shared-trial tier and its `costEnvelopePolicy = warnAndProceed` waiver are retired (D-12):
POST /api/runs refuses both.

#### 3.2b Optional OpenAI spend limit (T254, owner G37)

**No limit is the default** — a customer is never prevented from activating and working. A limit is set only when one
is wanted, and it caps the stamp's Azure OpenAI use, not provisioning:

- **At provisioning**: intake `openAiMonthlyLimitUsd` (optional; a plain number of USD in (0, 1,000,000] — POST /api/runs
  refuses anything else, `quota-openai-monthly-limit-invalid`). H4b writes `AiSpendLimit__MonthlyLimitUsd` on the
  production and staging slots. Without it H4b writes nothing.
- **Later — add, change or remove**: `scripts/Set-AiSpendLimit.ps1 -SubscriptionId <customer sub> -ResourceGroupName
  rg-spaarke-{customerId}-prod -AppServiceName spaarke-bff-{customerId}-prod -MonthlyLimitUsd 500` (or `-Remove`). It
  writes both slots, changes nothing when the value is already set, supports `-WhatIf`, and runs as the operator's own
  identity. Changing an app setting restarts the site — do it outside busy hours. **A later provisioning run that carries
  the intake value re-applies it** — overriding a change made with the script, or re-adding a removed limit; run
  upgrades without the value (the setting is then left alone) or with the current one.

**What the limit does** — the stamp's BFF keeps an estimate of the month's OpenAI spend (UTC calendar month) in the
stamp's Redis, shared by every instance and both slots: input and output tokens at list prices
(`AiSpendLimit__InputUsdPer1MTokens` 2.50 / `AiSpendLimit__OutputUsdPer1MTokens` 10.00 by default — at or above the list
rates of the chat models stamps deploy). Once the estimate reaches the limit, every model call is refused: HTTP 429
ProblemDetails, code `ai_spend_limit_exceeded`, `Retry-After` = seconds to the next UTC month start; a chat turn that
crosses it mid-turn ends with an in-band error carrying the same code. One limit per stamp — a stamp is one customer,
whoever signs in. Application Insights `ai.metering.tokens` remains the authoritative usage record.

**Known limits** — the estimate is list-price based, not the invoice: set the limit with headroom. Chat messages and text
refinement get the 429 before they start; other AI endpoints that turn every error into their own message show that
message instead. **Background AI jobs (document indexing, profiling) refuse while the month is over the limit** and
fail after their retries — nothing replays them when the month resets or the limit is raised, so re-run
indexing / profiling for documents uploaded in that window. A call already running when the limit is crossed completes.
If Redis cannot be read within 250 ms, calls are allowed (logged); if the setting is not a number, it is ignored (logged
once as an error). The stamp's Redis evicts least-recently-used keys under memory pressure, which would restart the
month's figure at 0. The keyless proof's two calls per provisioning run are outside the limit by design. Decisions:
`projects/customer-provisioning-orchestration-r1/notes/t254-ai-spend-limit-decisions.md`.

**Keyless (owner D13, T244)**: AI Search, Azure OpenAI, Document Intelligence, Content Safety (T246), Service Bus, Cosmos DB
and (when enabled) SignalR have local (key/SAS) auth disabled, Storage has shared-key access disabled, and Redis has access keys
disabled (T242). The BFF reaches every one with the stamp UAMI; L2 holds Search roles for H2b and the H13 probe. The
template emits no key or connection-string output. Not keyless by design: App Insights ingestion (connection string)
and the ACS resource (its local-auth setting is unverified on the pinned API; the BFF already uses its identity).

**Proved on every run (T230b)** — H13 refuses `Ready` unless both halves hold:
- **The keyless proof**: H13 calls the stamp BFF's `POST /api/platform/keyless-proof` as the L2 Worker identity (token
  for `api://{BFF app id}`; H3 gives that identity the application role `Provisioning.KeylessProof`, and nothing else
  holds it). The BFF makes one real, read-only call with **its own managed identity** to Service Bus (peek), Redis (PING),
  Azure OpenAI (chat, a one-word reply; embeddings), Document Intelligence (resource details), AI Search (document count),
  Cosmos DB (container metadata), Blob (one listing page) and Content Safety (Prompt Shield + groundedness, one text
  record each) — a fraction of a cent per run — and reports per service `proved` / `refused` / `key-credential` /
  `not-configured` / `unreachable` / `failed` / `not-in-use`, with status and timing only. Anything but `proved` fails
  H13 (QuarantineRequired), except: `unreachable` is Resumable, and `not-in-use` passes for Blob only — the session-file
  store ships with `SessionFileStore__BlobEndpoint` empty until compose-r8 task 063, so the BFF writes no blob and there is
  nothing to prove (a stamp that sets the endpoint gets the Blob probe). The call itself: plain-http BFF URLs are refused
  (the token is never sent), a token without the role fails with the token's `oid` named, a 404 (a BFF build that
  predates the route) is Resumable — redeploy (H9), then resume — and a 500 fails. The `openai-chat` result is ADR-028
  E-2's measurement on the stamp's `kind: OpenAI` account (logged as such).
- **ARM**: key/local auth disabled on every keyed resource of the stamp resource group (the types above, plus classic
  Redis, Event Hubs, App Configuration and Event Grid topics if one is ever added), and no key setting —
  `StampKeySettingCatalog` or a key-shaped value — on any App Service of the group or any of its slots
  (`h13-stamp-key-auth-enabled`; a site L2 cannot read fails too).
A build-time census (`tests/Spaarke.ArchTests/KeyCredentialCensusTests.cs`) pins every key-credential site in server code
and fails the build on a new one, or on one the H13 catalog or the proof does not cover.

**Shareable — exactly two named exceptions**, each requiring a **`customerId`** discriminator in the BFF
runtime: **Static Web Apps** (Office add-ins + external SPA) and **Content Safety**. The list is **CLOSED**;
the admission test is *holds no privileged legal content at rest*. Full criteria:
`projects/unified-access-control-r2/notes/D-12-resource-sharing-analysis.md`.
Shareable is permitted, not required: since **T246** every stamp has its **own** Content Safety account
(`sprk-{customerId}-{env}-contentsafety`, keyless). S0 is billed per call with no fixed fee, so it costs nothing
idle, keeps customer prompt text inside the stamp, and needs no `customerId` discriminator. The BFF refuses to
start outside Development/Testing without `AiSafety__ContentSafety__Endpoint` (customer.bicep + H4b set it).

🔴 **The BFF Entra app registration is per customer — D-13, BINDING, do not re-open.** The app registration
determines the Dataverse **application user**, which is assigned to exactly one **business unit**, and every
BFF-created record is owned by that application user — so a record can only land in customer X's business
unit if the BFF authenticated as an app registration dedicated to customer X. Mechanism and rejected
counter-arguments: `projects/unified-access-control-r2/notes/D-13-per-customer-bff-app-registration.md`.

⚠️ **Why dedication, and not a filter.** Dedicating a resource moves isolation from a query predicate to a
resource boundary. A predicate must be written correctly in every query, forever, by everyone; a boundary
cannot be forgotten. For a product holding privileged legal material that difference is the whole argument.

**Azure subscription structure**: one subscription **and** one resource group per customer, so usage is
segregated and billed per customer natively (ADR-027, amended 2026-09-28). 🔴 This **forces** a dedicated
App Service Plan — an App Service app cannot use a plan in a different subscription — which is the largest
single per-customer fixed cost.

**Bicep stack**: `infrastructure/bicep/customer.bicep` — the **only** customer-stamp template, the same
per-customer stamp for both models (owner decision D19, 2026-10-02). It is deployed **only** by the L2 control
plane's handler H2a, into the customer's own subscription (amended ADR-027); no CI workflow deploys it —
`deploy-infrastructure.yml` ("Validate Bicep Infrastructure") lints and compiles it and deploys nothing.
✅ The Model 1 shared-tier templates — `stacks/model1-shared.bicep` (+ its checked-in
JSON), `stacks/model1-customer.bicep`, `modules/model1-shared-l2-rbac.bicep`, `parameters/model1-prod.bicepparam`
and `parameters/{dev,staging,prod}.bicepparam` — were **deleted by task 225a (2026-10-01)**, together with the
inverted-polarity assertion in `bicep-e2e-dry-run.ps1`, the `model1-shared` compile +
upload in `publish-provisioning-arm-artifacts.yml` (its manifest schema requires only `customer`) and the
`model1-shared` option of `deploy-infrastructure.yml`. ✅ `stacks/model2-full.{bicep,json}`,
`stacks/{dev,staging,prod}.bicepparam`, `parameters/model2-customer-template.bicepparam` and
`customer-deployment.bicepparam` were **deleted by task 249 (2026-10-02)** together with that workflow's what-if
and deploy stages — `model2-full.bicep` deployed no live environment. H2a deploys `customer` for **both** models:
T228 (2026-10-06) made Model 1 deployable once every run carries the customer's own subscription (T225b, 2026-10-02,
converged the rest of the Model 1 code path).

### 3.3 Handler behaviour — what differs

Handlers execute the same code and, as of D-12, almost the same inputs.

| Handler | Model 1 | Model 2 |
|---|---|---|
| **H0.5 consent-callback** | **skipped** — Spaarke's own tenant | **required** |
| **H1 subscription readiness** | SpaarkeOwned ⇒ **no Lighthouse** | CustomerOwned ⇒ **Lighthouse required** |

🔴 **Every other handler behaves identically.** The former per-model table — H0 shared-platform capacity,
H2a `model1-shared.bicep`, H2b "7 indexes already exist on shared platform", H7/H12c pointing at shared
platform OpenAI, H13 verifying `tenantId`-filter enforcement — described the retired tier and is deleted.

✅ **Resolved** (T224 enum rename + T225b pairing): `Model1` maps to Spaarke-owned (no Lighthouse) and `Model2` to
customer-owned (Lighthouse required), and intake pairs `Model1` ↔ `spaarke-hosted-model2`, `Model2` ↔
`customer-owned-model2`. Since T228 a Model 1 run deploys like Model 2, into the customer's own subscription.

### 3.4 Invariants — what I1–I5 actually enforce

I2 (AI Search `tenantId` filter), I3 (Cosmos `/tenantId` partition) and I4 (SPE container resolver) remain
in force and remain ArchTest-enforced, **but they are belt-and-braces, not the customer boundary.** They key
on `tenantId`, which is identical for every Model 1 customer. The boundary is the dedicated per-customer
resource in the per-customer subscription. I1 and I5 are unaffected.

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

**L2-owned run inputs — Worker configuration, validated at startup** (task 245b). These are not run
parameters: `POST /api/runs` rejects them. A missing or malformed value stops the Worker at boot.

| Setting (`Worker` app setting) | What it is | Read by |
|---|---|---|
| `ControlPlaneIdentity__PrincipalObjectId` | Object id of L2's own UAMI (one setting since task 249; it replaced `KvSecretsPopulationOptions__ControlPlanePrincipalObjectId`). H4 grants it **Key Vault Secrets Officer** on each customer vault before writing it — never the stamp's BFF UAMI, which only reads its vault. H2a sends it as `customer.bicep`'s `controlPlaneUamiPrincipalId` on **Model 1** stamps (Website Contributor on the stamp BFF). **Rollout**: deploy `platform-controlplane` (which sets the new name) FIRST, then the Worker code built from task 249 straight away (`Deploy-ControlPlane.ps1` deploys code only) — each Worker version refuses to start without the name it reads, so the Worker is down between the two steps; queued Service Bus messages wait. (The old name only ever existed on the project branch, since T245b.) | H4, H2a |
| `KvSecretsPopulationOptions__RequireSecretFreeIdentity` | `true` — every new stamp is secret-free: H4 omits `BFF-API-ClientSecret` and `Dataverse-ClientSecret` (BINDING credential-lifecycle rule; no sentinel). The code default is also `true` (T225b, G21). | H4 |
| `SpeContainerOptions__ContainerTypeOwners__{i}__*` | Per SPE container type: `ContainerTypeId` and `OwnerAppId` (the container type's **owning** app) — nothing else. L2 signs in as the owning app through the Worker UAMI's federated identity credential on that app (MI-FIC, task 248 / owner D16): the UAMI's token for `api://AzureADTokenExchange` is the client assertion. No certificate or secret is stored; the former `OwnerCert*` settings and `SPE-OwnerCert-Pfx` no longer exist. The run's intake `containerTypeId` selects the entry. Empty is valid at boot; H0 then rejects every run (`spe-owner-not-configured`) until the topology runbook has set up a container type + owning app and its entry is added. Dev: `Spaarke Model 1` `fb3817a8-…` → `Spaarke SPE Model 1 Owner` `bfac7f6e-…`. | H0 (SpeOwnerCredential), H8, H13 (T6) |
| `IntegrationWiring__ExchangeAdminAppId` | Client id of `Spaarke Exchange Admin` (§4.2.1). The Worker signs in as it through its UAMI's federated credential and sends the Exchange Online token to the H14a sidecar with each request — the sidecar holds no credential (task 251, owner D24). Empty: the Worker boots; H14a / H13 T4 fail with "ExchangeAdminAppId is not configured". Dev: `46670ee2-…`. | H14a, H13 (T4) |
| `IntegrationWiring__SidecarSharedSecret{VaultName,SubscriptionId,Name}` | Where the Worker reads the Worker↔sidecar shared secret (`Sidecar-Shared-Secret` in the platform vault). The sidecar gets the same secret through the `ExchangeSidecar__SharedSecret` Key Vault reference setting: a sitecontainer variable must **name** an app setting, never hold a literal (Microsoft's `sitecontainers` contract — G30). | H14a, H13 (T4) |

Bicep: `modules/controlplane-worker-app-service.bicep` params `controlPlanePrincipalId` (passed `uami.outputs.principalId`),
`speContainerTypeOwners` (array) and `exchangeAdminAppId` (both threaded from `platform-controlplane.bicep`). T225b removed `vendorKeysKeyVaultName`
(owner D18: no Spaarke-shared vendor key remains in the customer catalog).

Artifact versions in idempotency keys (`bicepVer`, `indexVer`, `secretsVer`) are computed by L2 from the artifact each
handler applies — the ARM template H2a deploys, the index schemas H2b PUTs, the secret-catalog manifest H4 / H4b read —
so the same artifact gives the same key and a changed artifact is re-applied.

**Operator intake — required at `POST /api/runs`** (task 245c). Values only the operator knows. `POST /api/runs`
refuses a run that breaks a rule, **with the handler's own rule and rejection code** (H11's through the same
`UserProvisioningIntake` code H11 runs), before the run guard, the registry lookup, any Cosmos write or enqueue — intake
is fixed once the run exists. `/provision-environment` Step 1e-bis collects them; `intake.schema.json` carries the
same rules for batch mode.

| Intake key | Read by | Rule (400 `errorCode`) |
|---|---|---|
| `identityPreset` | H11 | `B2BGuest` \| `NativeAccount`, exact case (`userprov-missing-identity-preset` / `userprov-invalid-identity-preset`); **a `Model1` run takes only `B2BGuest`** (owner D2, T232 — `userprov-model1-requires-b2b-guest`) |
| `environmentSecurityGroupId` | H11 | **B2BGuest (every Model 1 run)**: object id (GUID) of the environment's security group `sprk-{customerId}-users`, created by the operator and set on the environment (`PRQ-C-10`) — `userprov-missing-security-group-id` / `userprov-invalid-security-group-id` (T232) |
| `usersJson` | H11 | JSON array, 1–500 entries; `NativeAccount`: non-blank `firstName` + `lastName`; `B2BGuest`: `email` (`userprov-missing-users` / `userprov-malformed-users-payload` / `userprov-invalid-user-entry` / `userprov-too-many-users`). Personal data: stored in the L2 run document (owner decision D15); never in git; diagnostics and logs identify users by position / Entra object id. |
| `exchangePolicyScopeGroupId` | H14a | non-blank (`h14a-missing-policy-scope-group-id`). The mail-enabled security group H14a scopes the stamp identity's Exchange mailbox roles to (only its **direct** members' mailboxes are reachable) — **created by the Exchange admin of the stamp's tenant before the run** (prerequisite `PRQ-C-08`; L2 never creates it — its membership is the customer's access decision). |
| `communicationGraphResource` / `emailGraphResource` | H14b | at least one non-blank (`h14b-no-webhook-targets-configured`) |
| `communicationDefaultMailbox` | H4 → KV `Communication-DefaultMailbox` | `local@domain.tld` (`intake-communication-default-mailbox-invalid`) |

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

#### 4.2.1 Exchange admin app (one-time per tenant — task 251)

H14a grants each stamp's managed identity the Exchange **"Application Mail.\*" roles, scoped to the customer's mail-enabled
security group** (Exchange *RBAC for Applications*, owner D26 — it replaced ApplicationAccessPolicy, which Microsoft calls
legacy and caps at a few hundred policies per tenant). L2 does that through its Exchange sidecar, signed in as
**`Spaarke Exchange Admin`**. Set it up once per tenant (Spaarke's, for Model 1):

1. **Entra (Global Admin)**: single-tenant app `Spaarke Exchange Admin` — **no secret, no certificate**; a federated
   identity credential with issuer `https://login.microsoftonline.com/{tenant}/v2.0`, subject = the L2 Worker UAMI's
   principal id, audience `api://AzureADTokenExchange`; the Office 365 Exchange Online application permission
   `Exchange.ManageAsApp`, admin-consented. Do **not** give it an Entra directory role — the Exchange Administrator role
   made its Exchange writes fail in the 2026-10-04 test.
2. **Exchange (an Organization Management admin, `Connect-ExchangeOnline`)**:
   - `Enable-OrganizationCustomization` if `(Get-OrganizationConfig).IsDehydrated` is true — once per tenant, **cannot be
     undone**, and took ~1 h to apply on Spaarke's tenant. Without it Exchange refuses every role assignment.
   - `New-ServicePrincipal -AppId <admin app id> -ObjectId <its Entra service-principal object id> -DisplayName 'Spaarke Exchange Admin'`.
   - Role **`Spaarke App RBAC Admin`**: `New-ManagementRole -Parent 'Role Management'`, then remove every entry except
     `Get/New/Set/Remove-ServicePrincipal`, `Get/New/Set/Remove-ManagementScope`,
     `Get/New/Set/Remove-ManagementRoleAssignment`, `Get-ManagementRole`, `Test-ServicePrincipalAuthorization`.
   - Assign it to the app (`New-ManagementRoleAssignment -App <app id> -Role 'Spaarke App RBAC Admin'`), plus
     `View-Only Recipients`, plus `-Delegating` assignments for exactly `Application Mail.Read`, `Application Mail.ReadWrite`,
     `Application Mail.Send` and `Application MailboxSettings.Read`. The delegating assignments are what limit it: tested
     2026-10-04, it is refused when it tries to grant itself Exchange Full Access, Role Management or Mail Recipients.
3. **Platform parameter** `exchangeAdminAppId` = the app's client id (`platform-controlplane-{env}.bicepparam`).
4. **Graph read for the Worker**: the L2 Worker managed identity must be able to read `GET /organization` in the tenant
   (`Organization.Read.All` or `Directory.Read.All`; on Spaarke's tenant it already holds `Directory.ReadWrite.All`). The
   Worker reads the tenant's initial domain (`contoso.onmicrosoft.com`) there and the sidecar connects with
   `-Organization <initial domain>` — the only value Microsoft documents for app-only sign-in. With the tenant id, Exchange
   connects and reads but refuses every write with "doesn't have write permission to target DC" (2026-10-04).

Residual risk, stated plainly: an identity that can create application role assignments can grant any app — itself
included — the four mailbox roles organization-wide (the scope is optional). Only the L2 Worker can obtain its token;
audit `New-ManagementRoleAssignment` in the unified audit log.

> **Verified live (2026-10-04)**: sign-in, connect with the initial domain, app-only writes (`New-ServicePrincipal`,
> `New-ManagementRoleAssignment -RecipientGroupScope`), the escalation refusals, and `Test-ServicePrincipalAuthorization`
> (a mailbox in the group is in scope, one outside is not). Log:
> `projects/customer-provisioning-orchestration-r1/notes/t251-exchange-sidecar-design.md` §7.

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
- `sprk_tenancymodel` (Choice) — 🔴 **MIGRATE, DO NOT RELABEL** (D-12 §6): value `1` (`Model2Dedicated`) holds **both** old-2a and old-2b, which now split across **different** models and need **opposite** Lighthouse answers; value `0` (`Model1Shared`) has **no successor**
- `sprk_tenantid` (populated from H0.5 or run params)
- `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken` (§14A upgrade compat)

---

## 5. Handler Catalog (H0 – H14)

Every handler is idempotent, resumable, and has a verified post-condition. Full dependency DAG in `design.md` §4.1.

| # | Handler | Purpose | Gate | Idempotency key |
|---|---|---|---|---|
| **H0** | Preflight + quota checks | Validate run params + **cost envelope** (§3.2a — `tier` + `estimatedMonthlyUsd`, every model; `quota-cost-envelope-*` / `quota-cost-overrun`, no override) + Azure OpenAI TPM headroom + subscription vCPU + SPE owner check (`SpeOwnerCredential`: the Worker has an owner entry for the run's container type, signs in as that owning app through its federated credential, and GETs the container type's registration). Resumable rejections: `spe-owner-not-configured` (no `containerTypeId` on the run, or no owner entry for it), `spe-owner-token-failed` (FIC token exchange failed, or Graph refused the owning-app token with 401/403 — e.g. missing consent), `spe-container-type-not-registered` (registration GET 404). No 24 h age gate | Quota headroom sufficient for +1 provision | `preflight-{customerId}-{paramHash}` |
| **H0.5** | Consent-capture callback | (Model 2 only) Anonymous HMAC-verified `POST /api/onboarding/consent-callback`; captures customer admin `tid`; kicks pipeline | Re-consent semantics: no-op if run exists Ready/Running; restart from H0 if Failed/Cancelled | `consent-{customerId}-{tid}` |
| **H1** | Subscription readiness | ARM verification the customer's subscription is reachable **in the run's tenant** and holds **no other customer's** `rg-spaarke-*` resource group (T228, ADR-027 — checked before H1 registers resource providers); Lighthouse delegation (`CustomerOwned` only) | `subready-subscription-not-dedicated` / `-unreachable` are Resumable, nothing written | `subready-{customerId}` |
| **H2a** | Per-customer Bicep infra | Deploy the CI-published `customer.bicep` ARM template: RG, KV, Storage, Service Bus, Cosmos, Redis (per customer since D-12; Azure Managed Redis with access keys disabled since T242 — the BFF connects with the stamp UAMI via `Redis__Endpoint`), OpenAI, AI Search, Doc Intelligence, Content Safety (T246), App Insights + Log Analytics, optional SignalR — all keyless (T244: local auth / shared key disabled; L2 gets Search Service Contributor + Search Index Data Reader on the stamp search service). Structural checks (pinned model versions, no `SystemAssigned` KV-reference identity) run on the same template bytes | — | `infra-{customerId}-{bicepVer}` — `bicepVer` = content version of the deployed template |
| **H2b** | AI Search indexes | Provision the 7 canonical indexes (`files`, `discovery`, `records`, `rag-references`, `insights`, `session-files`, `invoices`) on the stamp's own AI Search service via the SDK (`SearchIndexClientProvisioner`, L2 identity), then verify them — same path for both models (T225b) | — | `aisearch-{customerId}-{indexVer}` — `indexVer` = content version of the schema set applied |
| **H3** | Entra app registration | 🔴 **One BFF app-reg PER CUSTOMER, both models (D-13, BINDING)** — ~14 Graph + Dynamics permission grants (`GraphAppRoles.cs`); sign-in audience `AzureADMultipleOrgs` (enables Model 2 consent). Exposes the application role `Provisioning.KeylessProof` and assigns it to the L2 Worker identity (T230b; Model 1 — a Model 2 app-reg is in the customer's tenant, where L2 has no service principal). ✅ Implemented by T222 (2026-09-29): the former `Model1Shared` branch is deleted; H3 creates one registration per customer, unconditionally. | Admin consent granted (Graph query) | `appreg-{customerId}-{tenantId}` |
| **H4** | Key Vault secrets | Grant L2's own principal Secrets Officer on the customer vault; populate KV secrets per canonical catalog manifest; `keyVaultReferenceIdentity` PATCH to UAMI on both slots (**T1** trap) | — | `kv-{customerId}-{secretsVer}` — `secretsVer` = content version of the manifest |
| **H5** | Dataverse env **adoption** | Adopts the environment the **operator created** (intake `dataverseEnvUrl`; PRQ-C-09) — never creates one (T228). Checks the URL is this customer's (`spaarke-{customerId}[-{environmentName}]`, the rule POST /api/runs applies) and that `GET /WhoAmI` answers for the L2 Worker identity | `InterStepState.DataverseEnvUrl` = the canonical URL; `env-url-invalid` / `worker-not-app-user` / `env-health-check-failed` are Resumable | `dvenv-{customerId}` |
| **H6** | Managed solution import | Package Deployer dependency-ordered import — **9 authoritative solutions** (§11.1a; raised 8→9 SESSION 19 MDA-GAP fix): Tier 1 `SpaarkeCore` → Tier 2 `SpaarkeWebResources` → Tier 3 (parallel) `CalendarSidePane` / `DocumentUploadWizard` / `EventRibbons` / `EventDetailSidePane` / `EventsPage` / `LegalWorkspace` → Tier 4 MDA `SpaarkeCorporateCounselApp` | All 9 imported at correct versions | `solimport-{customerId}-{solutionVer}` |
| **H7** | Dataverse env-var values | Set 7 per-customer env vars per §10.3 (`sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId`). First links the environment's **root business unit** to H8's container (`businessunit.sprk_containerid` — unified-access-control-r2 task 076's non-secure default); a root unit already naming another container → Resumable `root-business-unit-container-conflict` naming both, never overwritten (task 227g) | Client startup validates no hardcoded URL fallbacks; the root unit's container reads back | `envvars-{customerId}-{configVer}` |
| **H8** | SPE container | Creates ONE customer container in the pre-existing container type, then activates and verifies it, app-only as that type's **owning app** (signed in through the Worker UAMI's federated credential on the owning app named in `SpeContainerOptions:ContainerTypeOwners` — never the customer BFF app or the BFF's UAMI; **T6** trap — delegated 403s). **Binds the container to the new environment's ROOT business unit** (custom property `spaarkeBusinessUnitId`, read back, container removed if it did not land — unified-access-control-r2 task 165, owner round 35 item 1), so it needs **H5**. Before creating, grants the customer's BFF identities on the container-type registration (stamp UAMI application `full`, BFF app delegated `full` — task 227b). **Records what it created at once and RESUMES with it** (rounds 41 + 49): a recorded container is never created again, and one whose activation failed is re-activated. A create whose answer was lost is QuarantineRequired `spe-container-creation-in-doubt`: never repeated, never auto-adopted (§7.5). **A later run reuses the customer's existing container** — the one the environment records in `sprk_SharePointEmbeddedContainerId` — and never removes it; the run's record and the environment naming different containers stops the run naming both (task 227e). After the bind it writes the `spaarkeCustomerId` marker the BFF recognises its containers by (T227d) | Container GET succeeds; stamp reads back; container ID handed to H7 only once bound | `spe-{customerId}` |
| **H9** | BFF deploy | CI-published artifact (`latest.json` manifest) → scheduled-jobs slot guard on the staging slot (`Scheduling__RunScheduledJobs=false`, slot-sticky — ADR-036 A1 rule 2) → Kudu zip-deploy to staging → slot swap; hardened `Deploy-Release.ps1` Phase 4 scanned for a `spaarkedev1` hardcode | `/health` = 200; slot-swap smoke test produces no cold-start KV-ref failures | `bff-{customerId}-{buildId}` |
| **H10** | Dataverse App User + Graph app-role parity | Runs right after H3 + H5 and **before H6** (T228 — H6 and H7 sign in as the BFF app registration it makes an application user). Register 2 App Users (BFF app-reg + UAMI) as System Administrator; sync Graph app-role parity from `GraphAppRoles.cs` (**T3**) | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 (**T2**) | `appuser-{customerId}` |
| **H11** | User provisioning | `B2BGuest` (every Model 1 run): checks the environment's security group (`sprk-{customerId}-users`) and guest access, invites each user (an existing guest is reused — no second email), then adds each redeemed guest to the group and makes it a Dataverse user with the Spaarke role. `NativeAccount` (Model 2): creates + licenses users | B2B: consent-verification gate | `users-{customerId}` |
| **H12a** | AI seed chain | type-lookups → actions → tools → knowledge → skills → playbooks → output-types → playbook consumers (single AI routing surface per **ADR-039**) | All seed rows present, no dupes | `aiseed-{customerId}-{seedVer}` |
| **H12b** | App-config seed | DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions (DAG-parallel with H12a) | Config records seeded per manifest | `configseed-{customerId}-{configSeedVer}` |
| **H12c** | Runtime references | `sprk_aimodeldeployment` rows point at the customer's **own dedicated** OpenAI deployment — both models (D-12 §3) | Endpoint resolves via env-var + join | `runtimerefs-{customerId}-{modelVer}` |
| **H13** | E2E acceptance gate | Pure C# in the Worker (ARM, Graph, Dataverse, AI Search, Cost Management calls — no script) — verifies `/health`, `/ping`, the Dataverse CORS origin, **the keyless proof** (one managed-identity call per stamp service, made by the BFF — any refusal fails; T230b) and **ARM keyless** (key auth off, no key setting on any slot), **all 7 T1–T7 traps cleared**, **I2–I5 invariants sample-verified on the stamp** (I1 + naming are CI gates — T230a), cost envelope (one $400/month envelope for both models, §3.2) | `Setup Status = Ready` only if H13 exits 0 | `validate-{customerId}-{buildId}` — `buildId` = the build H9 deployed |
| **H14** | Post-deploy integrations | (a) Exchange mailbox access: the stamp UAMI gets the 4 `Application Mail.*` roles scoped to the customer's group (RBAC for Applications — **T4**); (b) Graph webhook subscriptions per Communication/Email module; (c) Dataverse service-endpoint webhooks. Sub-steps DAG-parallel | H13 T4: every role held in scope, none outside | `integrations-{customerId}-{integrationVer}` |

### 5.1 Handler dependency DAG

Authoritative source: `DagAdvancer.HandlerDependencies` (a handler is ready when every handler listed for it has
completed). As of task 227c:

T228 moved **H10** to right after H3 + H5 and made **H6** wait for it: H6 and H7 sign in to the customer
environment as the BFF app registration, which is an application user there only once H10 has registered it. H5 and
H8 act as the L2 Worker identity, which the operator makes an application user (PRQ-C-09).

Task 165 (rounds 35, 41) carries two of these edges: **H8 <- H5** (H8 binds the container to the environment's root business unit, known once H5 has adopted the environment) and **H7 <- H8** (H7 writes H8's container, handed off only once BOUND).
```
H1   <- H0                 H2a  <- H1                 H2b  <- H2a
H4   <- H2a                H5   <- H2a                H3   <- H4
H4b  <- H4, H3, H5, H8     H6   <- H5, H3, H10        H8   <- H3, H5
H9   <- H3, H4b            H7   <- H6, H8, H9         H10  <- H3, H5
H11  <- H10, H7            H12a <- H11                H12b <- H11
H12c <- H12a, H12b, H2a    H14  <- H12c, H9           H13  <- H14
```

Every edge carries data: a handler that reads another's `InterStepState` output has it as an ancestor, and
`RunContextContractTests` fails the build if a required input has no producer running first.

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

**BINDING pre-check** (BEFORE removing any alias / fallback spelling): verify LIVE App Service + KV + Dataverse-persisted config first. **KV credential-lifecycle rule** (§6.5 resolution 2026-08-25 per ADR-028 A4 + E-3 closure — the "OBO + shared-lib Dataverse still depend" rationale was empirically wrong and closed 2026-08-24): H4 **omits** `BFF-API-ClientSecret` in secret-free envs (no sentinel — §9.1 opaque `AADSTS7000215`); do not purge soft-deleted rollback copies or delete live `Dataverse-ClientSecret` before 2026-11-23; original never-delete survives only for unmigrated envs. Full rule: `.claude/constraints/provisioning.md` §KV credential lifecycle.

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
| `sprk_AzureOpenAiEndpoint` | The customer's **own dedicated** Azure OpenAI endpoint — both models (D-12 §3). `SharedPlatformOpenAiEndpoint` is a retired artifact |
| `sprk_ShareLinkBaseUrl` | Customer-facing share link base URL |
| `sprk_SharePointEmbeddedContainerId` | Populated from H8 output (I4 enforcement) |

Client startup validates no hardcoded URL fallbacks (per task 024).

### 6.5 App Service configuration

**Reference-only**: full v2 auth setting matrix is in [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) and, until r1 delivers Phase H `/config.json` fetch, in the archived `auth-deployment-setup.md` stub. Core settings the operator must confirm post-H9:

- `Graph__ManagedIdentity__Enabled=true`
- `Graph__ManagedIdentity__ClientId={uami-client-id}`
- `ManagedIdentity__ClientId={uami-client-id}`
- `Dataverse:ClientSecret` = KV reference (BFF `/health` fails fast per r3 task 061 `ValidateOnStart` if unresolved — **NFR-05**)
- ⚠️ **Every application identity the BFF authenticates as must be NAMED in configuration** (unified-access-control-r2
  task 166): the document-pointer check recognises the rows and files the BFF itself created only by the client ids in
  `AzureAd__ClientId`, `API_APP_ID`, `Graph__ManagedIdentity__ClientId`, `ManagedIdentity__ClientId` and
  `Dataverse__ClientId`. A stamp whose app-only Graph or Dataverse identity is under none of these keys (for example a
  **system-assigned** managed identity with no client-id setting) cannot verify any BFF-created document, and every
  download of one is refused (409 `document_storage_unverified`, fail closed). Set the user-assigned identity's client id
  in `Graph__ManagedIdentity__ClientId` / `ManagedIdentity__ClientId` as above.
- `DocumentPointer__StrictDerivedContainer` — **leave unset (false) at deployment.** It switches the download check to the
  strict derived-container rule, and is set to `true` only after `scripts/Invoke-DocumentContainerMigration.ps1 -Verify`
  passes on that environment (task 166 note §20). `DocumentContainerMigration__WritesEnabled` is set only by that
  script's `-Apply`, for the run.
- ⚠️ **Schema, then field security, before any file relocation** (task 166 f1-v1 / f1-v2): run
  `scripts/Set-DocumentRelocationSchema.ps1 -Apply` then `-Verify` on the environment's Dataverse, then
  `scripts/Set-DocumentPointerFieldSecurity.ps1` (dry run → `-ClientNoLongerWritesPointers -Apply` → `-Verify`), before
  the migration's `-Apply` and before Make Secure is used. The schema script creates two columns, both **field-secured
  from creation** (no window in which a user could write them): `sprk_document.sprk_relocationpending`, the row's
  relocation ledger (what a moved file still owes — a source delete, a re-key, the index — and the old item's witness, its
  size / quickXorHash / version when its copy was verified, the only thing a later call deletes it against), and
  `sprk_document.sprk_relocatedversions`, the version record (the ORIGINAL author, date and size of every version a move
  replayed into the copy — Graph cannot set them; the version history routes report them). Every relocation reads both
  and fails closed — nothing moves — until they exist; the field-security script grants the BFF-managed reader / writer
  profiles on them, and until it does, a BFF application user that does not hold System Administrator cannot write them,
  so relocations keep failing closed (retried) in between.
- `PowerBi__AllowedWorkspaces__{n}__WorkspaceId` (+ optional `__CustomerBusinessUnitId`) when the Reporting module is
  enabled — the workspaces the catalog may act on; empty refuses every report ([reporting-admin.md](reporting-admin.md#environment-variables)).

#### 6.5.1 Customer identity (`Customer__Id`) — required per stamp

> Added 2026-09-29 by `unified-access-control-r2` task 123, per decision
> [D-14](../../projects/unified-access-control-r2/notes/D-14-customer-discriminator.md).

**Every BFF stamp must be able to say which customer it serves.** `customerId` names the resource group
and every resource inside it, and until this setting existed no line of BFF code could read it. That
mattered because — per D-12 — `tenantId` is **identical for every Model 1 customer** (they share the
Spaarke Entra tenant), so a `tenantId`-keyed cache key, log scope or metric dimension separates *Entra
tenants*, not *customers*, and its tests pass anyway because there is only ever one value.

| | |
|---|---|
| **Setting** | `Customer__Id` (configuration key `Customer:Id`) |
| **Value** | the customerId — 3–8 chars, lowercase letters and digits, starting with a letter. See [`AZURE-RESOURCE-NAMING-CONVENTION.md` § "The `customerId` standard"](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md). |
| **Emitted by** | `infrastructure/bicep/customer.bicep` (production site only), from the `customerId` it already holds; and **H4b** writes it to **both** slots from the run's customerId, verbatim (manifest `per_env_settings` → `Configure-AppServiceSettings.generated.ps1 -CustomerId`, customer-provisioning-orchestration-r1 T238) — so a staging → production swap cannot drop it. **No operator action** on a provisioned stamp |
| **Verified by** | **H13 trap T7** (`CustomerIdentityT7Probe`): both slots must carry exactly the run's customerId; missing, blank or different on either slot quarantines the run (`h13-trap-T7-customer-identity`) |
| **Assignment authority** | Dataverse `sprk_dataverseenvironment.sprk_customerid`. Bicep CONSUMES it; nothing mints one. |

**Resolution order, and what happens when it fails:**

1. **`Customer__Id` if set** — the intended path.
2. **Otherwise derived from `WEBSITE_RESOURCE_GROUP`**, which App Service sets automatically and which is
   literally `rg-spaarke-{customerId}-{env}`. This is why an older per-customer stamp keeps working with
   no change. ⚠️ **The derived path logs a WARNING every boot, by design** — a stamp running on the
   fallback forever is a stamp whose settings were never finished.
3. **Otherwise the BFF refuses to start**, with a message naming both sources. There is deliberately
   **no default**: an absent customer identity must never resolve to a shared value. (Development and
   Testing environments are exempt from the startup failure; there the identity simply stays unresolved
   and throws if anything asks for it.)

🔴 **Resource groups that are NOT customers.** `rg-spaarke-platform-{env}`, `rg-spaarke-shared-{env}` and
`rg-spaarke-byok-prod` match the per-customer *shape* but name platform functions. Derivation refuses
them by name — otherwise the platform stamp would invent `customerId = "platform"`, which is exactly the
silently-shared value this mechanism exists to prevent. **A BFF in one of those groups must set
`Customer__Id` explicitly**:

```bash
az webapp config appsettings set \
  --resource-group rg-spaarke-platform-prod \
  --name <app-service-name> \
  --settings Customer__Id=<customerId>
```

⚠️ **Pre-existing stamps need this before the branch carrying task 123 is deployed.** `Deploy-BffApi.ps1`
defaults to `rg-spaarke-dev` (which is not a per-customer shape at all) and documents
`rg-spaarke-platform-prod` (deny-listed). Neither derives, and App Service runs as the `Production`
environment, so neither is exempt from the startup failure. What customerId those pre-D-12 stamps should
carry is an **owner decision** — do not invent one.

#### 6.5.2 Customer workforce tenants (`WorkforceIdentity__CustomerTenantIds__N`) — required per stamp

> Added 2026-10-01 by `unified-access-control-r2` task 141 (owner decision I1 = (b)). Contract:
> [`141-link-contract.md`](../../projects/unified-access-control-r2/notes/141-link-contract.md).

A customer employee **without a Power Apps licence** ("Type 2") signs in to Teams / the SPA with company SSO.
On their FIRST sign-in the BFF binds them to a contact by their Entra object id (`oid`): onto the one active,
unbound contact carrying their email, or — when none does — a new contact keyed by the oid. **Only a MEMBER of
one of this deployment's customer workforce tenants gets that first-sign-in bind or creation.** The setting
names those tenants.

| | |
|---|---|
| **Setting** | `WorkforceIdentity__CustomerTenantIds__0`, `__1`, … (configuration key `WorkforceIdentity:CustomerTenantIds`, a string array) |
| **Value** | the customer's Entra **tenant id(s)** (GUIDs) whose employees use this stamp |
| **Empty / absent** | **DENY** — nobody is ever email-bound or gets a contact created; a Type-2 first sign-in gets `sdap.access.deny.workforce_tenant_list_empty`. Existing oid bindings still resolve. |
| **Never** | a fallback to `AzureAd:TenantId`, or `TenantRouting:Tenants[]` |
| **Startup check** | a non-GUID, the all-zero GUID, or the CIAM tenant id **fails startup** (`ValidateOnStart`) |
| **Written by** | provisioning (`customer-provisioning-orchestration-r1`) — handoff `projects/unified-access-control-r2/notes/handoffs/INCOMING-141-workforce-tenant-list.md` |

🔴 **Model 1 is the case that makes this a separate setting.** In Model 1 the per-customer BFF app registration
lives in **Spaarke's** tenant (D-13), so `AzureAd:TenantId` is Spaarke's tenant while the customer's employees
sign in with the **customer's** `tid`. Keying the member test on `AzureAd:TenantId` would refuse every Model-1
Type-2 employee **and** auto-bind Spaarke's own staff into the customer's environment. Set the CUSTOMER's tenant
here. In Model 2 the registration lives in the customer's tenant and the two values coincide — list it anyway;
nothing is inferred.

```bash
az webapp config appsettings set --resource-group <rg> --name <app-service-name> \
  --settings WorkforceIdentity__CustomerTenantIds__0=<customer-tenant-guid>
```

**The member test also needs the `acct` claim** (§7.3): a member is a user token (`CallerIdentity`, never an
app-only token) whose `tid` is listed here AND whose `acct` claim is `0`. A token with no `acct` claim fails
closed (`sdap.access.deny.workforce_acct_claim_missing`) — membership is never inferred from an email domain or
a `#EXT#` UPN.

**The identity-link reconciliation job** (`identity-link-reconciliation`, every 5 minutes) links every licensed
user to their contact. It runs **report-only** until `IdentityLink__Reconciliation__WritesEnabled=true` — absent,
empty or unparseable writes nothing. Review one report-only run (App Insights `[ID-LINK-RECON] before-state`
lines and the run's ResultJson) before enabling writes on a new stamp.

The same switch gates the **inline link** a licensed user would otherwise get at their first Teams/SPA sign-in,
so between the BFF deploy and the switch nothing links a licensed user to a contact and the report-only run is a
true preview. Two writes are **not** gated, by design: a Type-2 (unlicensed) member's own first sign-in (bind or
create, behind the member test) and the link written when the BFF itself creates a systemuser during demo
provisioning. Do not run demo registrations while the report-only run is under review, or its counts will drift.

**Which environments the job reconciles.** The BFF's own `Dataverse:ServiceUrl` AND every environment it
provisions users into: `DATAVERSE_URL` (registration's default) and every **active** `sprk_dataverseenvironment`
row — the only environments the approve endpoint provisions into. Each is reconciled the same way (pass 1 links or
flags every enabled interactive systemuser; pass 2 re-evaluates and clears resolved flags), through the
registration service's existing per-environment token path (the BFF's own managed identity — it must be an
application user in each, which demo provisioning already requires). So **a registration link that does not land
is retried**: whether it faulted, was refused, lost a race or raised a collision flag, the next run re-decides that
user and re-evaluates that flag (App Insights: `[ID-BIND] New systemuser … contact link NOT made … re-decides the
user on its next run`). One environment's failure fails the run (`[ID-LINK-RECON] {environment}: …`, and the
ResultJson's `provisioningTargets.environments[]`) but never stops the others. Deactivating a registry row stops
the BFF reconciling that environment — deliberately. (Before 2026-10-01's third fix round the job scanned only its
own environment and this was a recorded gap.)

**Every one of those environments needs the Dataverse prerequisite below** — an environment without it is
reported by every run as a failed environment, and registration links there deny `binding_column_missing`.

**Cost of leaving a stamp report-only.** Each run reads every enabled interactive systemuser; a user with no
verified link costs roughly 2–4 further Dataverse reads per run (every 5 minutes) for as long as writes stay off.
Fine at dev scale; on a large stamp, enable writes after the review rather than leaving the job report-only.

**Dataverse prerequisite — apply BEFORE deploying a BFF that carries task 141**, in the BFF's own environment and
in every provisioning target above. The BFF selects the new columns, so without them every binding read fails
closed (`sdap.access.deny.binding_column_missing`) and CIAM and Type-2 sign-ins are denied.

```powershell
.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>]            # dry run: read-only
.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Apply
.\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Verify   # must exit 0 (re-run until the key is Active)
```

**Uniqueness lives on a mirror column (owner decision, 2026-10-01).** Dataverse refuses an alternate key on a
field-secured column ([Work with alternate keys](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity)),
and the binding `contact.sprk_externalobjectid` must stay field-secured (only the BFF may write it — it decides
whose grants a caller inherits). So the script creates an UNSECURED mirror, `contact.sprk_externalobjectidkey`,
copies every existing binding into it, and puts the alternate key `sprk_ExternalObjectIdUniqueKey` on the mirror.
The BFF writes the mirror with the same oid, in the same request, as every bind and create; the platform's unique
index then guarantees **exactly one contact per oid**, including when two first sign-ins race. Every read that
decides who a contact IS uses the secured binding; nothing resolves by the mirror. A user with Write on contact
can therefore only **deny service** through the mirror (put someone's oid into it): that person's bind or create is
refused, the BFF denies `sdap.access.deny.contact_key_conflict`, and the holder is flagged with reason **"Key mirror
held by another contact"** (§6.5.3) — visible, never a takeover. The script also adds the plane and collision
columns (backfilling existing bindings as External), the "Contacts with Identity Collisions" view, and the
field-level security that lets ONLY the BFF write `contact.sprk_externalobjectid` and
`systemuser.sprk_primarycontact` while every user keeps reading them. It refuses `-Apply` (before any write) if
an edit ever puts the key and field security on one column again, and reports a mirror value its own binding does
not carry as `FAIL` without touching it. **A business unit created later needs `-Apply` re-run**, so its default
team joins the reader profile.

**Dataverse prerequisite — apply BEFORE deploying a BFF that carries `unified-access-control-r2` task 133.** That BFF
records the PERSON who created every project, matter and work assignment it creates in `sprk_createdbyperson` (a
BFF-only, field-secured lookup to `systemuser`; owner decision 2026-10-02) — the person secure provisioning's resume
shares to when `createdby` is the BFF application user. Without the column, every Office quick-create and every
`POST /api/v1/work-assignments` fails (Dataverse refuses a create naming a column it does not have). Run
`scripts/Set-RecordCreatorPersonSchema.ps1` with the same `-BffApplicationIds` as above — dry run, then `-Apply`, then
`-Verify` (must exit 0); re-run `-Apply` when a business unit is added. Details:
`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` §7b.

#### 6.5.3 Identity collisions — the operator procedure

A **collision** is any of: an email match on a contact bound to a different oid; an oid carried by more than one
contact; a licensed user whose `sprk_primarycontact` points at a contact bound to a different oid; an invite
whose email matches a workforce-bound contact or a contact a systemuser links to; or an oid whose unique-index slot
another contact holds in its `sprk_externalobjectidkey` mirror without the binding. Every collision is refused
(403 at sign-in, 409 at invite) **and flagged on the contact** — `sprk_identitycollisionon` (when),
`sprk_identitycollisionoid` + `sprk_identitycollisionplane` (who collided), `sprk_identitycollisionreason` (why),
for the FIRST identity; `sprk_identitycollisionparties` lists EVERY identity that collided with the contact.
A repeated collision by the same identity does not write again; a different identity's collision is added.

**Find them**: Contacts → view **"Contacts with Identity Collisions"**.

**Resolve** (System Administrator — the fields are field-secured):

1. Decide which identity owns the contact. One contact carries one sign-in; it is never shared or merged.
2. **The colliding identity should own it** — clear ALL THREE of `sprk_externalobjectid`,
   `sprk_externalobjectidkey` and `sprk_identityplane` on the contact (clearing the binding without its marker
   leaves it UNREADABLE, which denies; clearing it without the mirror leaves the oid's unique-index slot held, which
   also denies — reason "Key mirror held by another contact"). The other identity's next sign-in or the next
   reconciliation run binds it correctly.
   **The existing binding is right** — leave the binding. For an invite, invite a different email; for a
   licensed user whose `sprk_primarycontact` points at someone else's contact, point it at the user's own contact
   (or clear it and let the job create one). ⚠️ Changing a licensed user's link changes their Assigned-To access —
   it is deliberately never done automatically.
   **Duplicate email** (reason "Email carried by more than one contact") — deactivate or correct the duplicate
   contact(s); only ACTIVE contacts take part in an email match.
   **Duplicate oid** (reason "Oid carried by more than one contact") — clear all three binding columns
   (`sprk_externalobjectid`, `sprk_externalobjectidkey`, `sprk_identityplane`) on every contact that is not that
   person's. Deactivating is NOT enough here: the oid lookup reads contacts of every state, so an oid left on an
   inactive duplicate keeps the oid ambiguous. (Once the `sprk_ExternalObjectIdUniqueKey` alternate key exists it
   can only recur through a binding someone wrote by hand without its mirror; the schema script refuses to create
   the key while two contacts would share a mirror value.)
   **Key mirror held** (reason "Key mirror held by another contact") — the flagged contact carries an oid in
   `sprk_externalobjectidkey` that its binding `sprk_externalobjectid` does not: someone with Write on contact put
   it there, or a binding was cleared without its mirror. The identity named in the flag cannot be bound or get a
   contact until the slot is freed. If the flagged contact is NOT that person's, clear its `sprk_externalobjectidkey`;
   if it IS, ask an administrator holding the identity-link writer profile to restore the binding (or clear the
   mirror and let that person's next sign-in, or the next reconciliation run, bind them again). Deactivating the
   contact does NOT free the slot — the unique index counts inactive rows — so the flag stays until the mirror is
   cleared.
   Deactivating is how a person is removed: an oid on an inactive contact is denied, and no replacement is
   created.
   **Several identities on one contact** — read `sprk_identitycollisionparties` and resolve each party; the
   summary columns name only the first.
3. **Do not clear the flag by hand.** The next `identity-link-reconciliation` run re-evaluates every recorded
   party, drops the ones that no longer collide (the summary columns then name the next one), and clears the flag
   only once none does. Two exceptions, cleared by hand AFTER resolving every collision on the contact (clear all
   five `sprk_identitycollision*` columns): a flag listing **20 parties** (a later identity was not recorded), and
   one whose `sprk_identitycollisionparties` no longer reads as the BFF wrote it (someone edited it). The job never
   clears either, so that an unrecorded collision cannot vanish.

#### 6.5.4 SPE admin operator-environment marker (`SpeAdmin__PlatformOperatorEnvironment`) — NEVER on a customer stamp

> Added 2026-10-05 by `unified-access-control-r2` task 165, owner round 49 item 1.

The BFF's SPE admin routes whose answer spans the **whole SharePoint Embedded tenant** (security alerts, secure score) or a
**whole container type** (its app permissions, consuming apps, registration) serve only a root-unit administrator of a
**Spaarke-operated** environment. Under Model 1 every customer environment has its own root-unit administrator, so the
root check alone cannot confine them; the deployment setting `SpeAdmin__PlatformOperatorEnvironment=true` marks Spaarke's
own environments — **today only `dev`** (round 57 item 1; Spaarke's production operator environment adds it in the change
that stands it up). It is a deployment setting, not a Dataverse column, because a customer administrator can edit a column.

- **Customer environments never carry it** — Model 1 shared stamp, Model 2 stamp, every per-customer setting set. Customer
  provisioning (the L2 control plane, the canonical app-settings catalog, `customer.bicep`, the Model 1 stack) does not emit
  it, and since task 249 removed the Model 2 stack no Bicep template names it at all — the marker reaches an App Service
  only through `Deploy-BffApi.ps1`. `SpeAdminOperatorEnvironmentMarkerGuardTests` fails the build if any template names it.
- **Missing = false = refused** (fail closed); a value that is not a boolean stops the BFF at startup.
- **Spaarke-operated environments** declare `"speAdminPlatformOperatorEnvironment": true` in `config/environments.json`
  (today only `dev`); `scripts/Deploy-BffApi.ps1` then sets the App Service setting on the slot(s) it deploys to, and FAILS a
  deploy to an environment that carries the setting without declaring it. The declaration must be a JSON boolean (`true` /
  `false`, unquoted): the deploy FAILS on any other type, because PowerShell reads the string `"false"` as true (round 57
  item 3; the parse is `scripts/common/SpeAdminOperatorMarker.ps1`, which `SpeAdminOperatorEnvironmentMarkerGuardTests` runs).
- **Standing up Spaarke's production operator environment** (`demo` — `spaarke-bff-demo`, Stopped, which declares `false`
  today) is the change that adds the marker: its registry key set to `true`, its name in
  `SpeAdminOperatorEnvironmentMarkerGuardTests.SpaarkeOperatedEnvironments`, and the App Service setting (the next
  `Deploy-BffApi.ps1 -Environment demo` run sets it). Until then it refuses those routes (fail closed). The entry is
  reachable through `-Environment` (main-session round 62 item 2): task T242b added `demo` to `Deploy-BffApi.ps1`'s
  `-Environment` `ValidateSet`, so the declaration and its `requiredCorsOrigins` gate are read by a demo deploy.
- **A declared marker is bound to its App Service** (round 62 item 1): when the entry declares `true`, the deploy FAILS
  unless `-AppServiceName` and `-ResourceGroupName` equal that entry's `appServiceName` / `resourceGroup`
  (`Assert-SpeAdminOperatorMarkerTarget`, `scripts/common/SpeAdminOperatorMarker.ps1`). The declaration is read by the
  `-Environment` label, which defaults to `dev`, so without this a deploy naming another App Service would inherit dev's
  marker.
- A customer root-unit admin calling those routes gets `403 spe.admin.deny.platform_operator_required` — the same answer as
  a leaf admin anywhere.

---

## 7. Pipeline Execution Phases (walkthrough)

Each phase invokes one or more handlers. Post-Phase-D, `/provision-environment` runs this end-to-end. Pre-Phase-D, operator follows §12 interim runbook.

### 7.1 Phase 1 — Preflight & Consent (H0, H0.5)

**Purpose**: fail fast on quota shortage or naming collision BEFORE any resource is created.

**H0** runs unconditionally; **H0.5** runs only for Model 2 self-service (consent-capture branch).

**[GATE]** — H0 must pass:
- Azure OpenAI regional TPM headroom for the stamp deployment set, per Azure quota name, in `openAiLocation` (default westus3): DataZoneStandard gpt-4o 150, gpt-4.1-mini 200, text-embedding-3-large 350 (task 247)
- Dataverse env-creation rate quota (`pac admin quota`)
- Subscription vCPU quota
- SPE owner check (`SpeOwnerCredential`): owner entry configured, owning-app token obtained through the federated credential, container type registered

Failure diagnostic surfaces to operator; run does not start until resolved.

### 7.2 Phase 2 — Subscription & Bicep Infra (H1, H2a, H2b)

**H1** verifies the customer's subscription is reachable in the run's tenant and holds no other customer's stamp
(`rg-spaarke-{otherId}-*`; ADR-027, T228), registers the resource providers, and checks Lighthouse delegation
(`CustomerOwned` only).

**H2a** deploys the per-customer Bicep stack — **the same stack in both models** (D-12):
- `customer.bicep` — 15 resources per §7.2 of design.md, into the customer's own subscription + resource group
- `model1-shared.bicep` was deleted by task 225a (§3.2); since T228 Model 1 deploys `customer.bicep` too, into the subscription the operator created for the customer

Upgrade mode: `az deployment group what-if` runs FIRST; defaults to REJECT + report on drift (per FR-34).

**H2b** provisions AI Search indexes via `scripts/ai-search/Deploy-AllIndexes.ps1` — 7 canonical indexes; per-index invariant verifier confirms required filterable fields + vector fields + forbidden fields absent.

### 7.3 Phase 3 — Identity & Secrets (H3, H4)

**H3** creates the customer's Entra app registration via `scripts/Register-EntraAppRegistrations.ps1`:
- `-TenantId` is **mandatory** (I1 enforcement — no default per v3.3 code fix `1834b77bc`)
- Grants ~14 permissions per `Infrastructure/Auth/GraphAppRoles.cs`
- Client secret stored as KV URI reference (never cleartext)

**Office add-in SPA redirect URIs (Outlook/Word add-in sign-in — REQUIRED, per add-in host).** If the environment serves the Spaarke Office add-in, register BOTH of the following as **Single-page application (SPA)** platform redirect URIs on the app registration:

- `brk-multihub://<addin-host>` — the Nested-App-Authentication (NAA) broker redirect used by **desktop** Office (Windows/Mac).
- `https://<addin-host>/auth-callback.html` — the standard MSAL popup redirect used by **Office on the web** (which does not support NAA, so `OfficeNaaStrategy` falls back to a standard `PublicClientApplication`).

`<addin-host>` is the origin serving the add-in bundle (the Static Web App / CDN host in the manifest `SourceLocation`), so these are **per-host** — every environment (dev / each customer) that serves the add-in from a distinct host needs its own pair. Missing them produces `AADSTS7000471` ("no matching redirect URI") at add-in sign-in. Reference impl: [`src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts`](../../src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts) derives the broker redirect as `brk-multihub://${window.location.hostname}` and the web fallback as `https://<host>/auth-callback.html`.

**The `acct` optional claim (access tokens) — REQUIRED on every per-customer BFF registration** (task 141).
The BFF's first-sign-in identity binding admits only a MEMBER of a configured customer tenant (§6.5.2), and
"member" is read from the `acct` claim (`0` member, `1` guest). Step 1 of the script adds it to a NEW
registration; an EXISTING one needs it added once:

```powershell
.\scripts\Register-EntraAppRegistrations.ps1 -TenantId <tenant-of-the-registration> `
  -AcctClaimOnly -AcctClaimAppId <bff-app-registration-appid>
```

It is idempotent and keeps every other optional claim. Optional claims on the resource registration apply to
every access token issued FOR it — Teams SSO included (`webApplicationInfo.id` is this registration) — see
Microsoft's [optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference).
Without it every Type-2 first sign-in is denied `sdap.access.deny.workforce_acct_claim_missing` (fail closed).

**Escalation gate** (per FR-13 / H10): 10 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` must be completed via `az` enumeration BEFORE first production customer provisioning.

**H4** populates KV secrets from the canonical catalog manifest + PATCHes `keyVaultReferenceIdentity` to UAMI on both slots (**T1 verification**).

### 7.4 Phase 4 — Dataverse Environment (H5, H6, H7)

**H5** adopts the Dataverse environment the operator created (PRQ-C-09) — it never creates one (owner D4 / Q1;
T228). It refuses a URL not named for this customer and stops `worker-not-app-user` until the L2 Worker identity is a
System Administrator application user there. **H10** follows (before H6 — §5.1).

**H6** imports 8 managed solutions via Package Deployer with dependency ordering per `$SolutionImportOrder` in `scripts/Deploy-DataverseSolutions.ps1`:
- **Tier 1**: `SpaarkeCore`
- **Tier 2**: `webresources`
- **Tier 3 (parallel)**: CalendarSidePane, DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage, LegalWorkspace

**H7** sets the 7 per-customer env-var values (§6.4).

**After H6 — record numbering (interim; required in EVERY environment).** Matters and Projects are numbered by
Dataverse's platform autonumber (`MAT-######` / `PRJ-######`, `spaarkeai-word-add-in-r1` task 076; interim until the
numbering function). `SpaarkeCore` carries the column format and the alternate keys, but **the seed is per environment
and is not carried by a solution import** — without this step the first number is `MAT-001000`, and an environment
whose format is missing creates **nameless** Matters and Projects (the number is the primary name; the BFF logs
`record_number_unassigned`). The first `-Apply` also numbers any existing blank rows, oldest first (each write updates
the row's `modifiedon`). Production: check existing matter numbers are unique first (the script lists duplicates and
writes nothing for that table). Where the tables are managed, the format and keys must arrive with the `SpaarkeCore`
import — the script never customises a managed component (ADR-027); it then only seeds and backfills.

```powershell
.\scripts\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com           # dry run: every write it would make
.\scripts\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Apply
.\scripts\Set-RecordNumberingSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Verify   # must exit 0
```

### 7.5 Phase 5 — SharePoint Embedded (H8)

**H8** creates (or reuses), activates, verifies, binds and marks the customer's root container inside the pre-existing container type (the
container type itself is created once by the operator — [`SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md`](./SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md)).
**One root container per customer, ever**: a re-entry of the run resumes with the container it recorded; a later run reuses
the container the environment records (task 227e — details below). It is the root business unit's container — the default,
email and communication-archive container; H7 records it on the root unit (`businessunit.sprk_containerid`, task 227g).
**The customer's other containers are not provisioning's**: unified-access-control-r2 gives every secure project, matter and
work assignment its OWN container (`ProvisionProjectEndpoint`, at runtime), and an administrator may add a business unit's
container through the SPE admin plane. The BFF creates both, binds each to its business unit and marks it with
`spaarkeCustomerId`. The `Secure Record` business unit itself has no container.
**T6 fix**: H8 uses an app-only token as the container type's **owning app**, obtained through the Worker UAMI's
federated identity credential on that app (task 248) — no certificate or secret is involved. Delegated tokens produce
`public client not allowed` 403s.

**Business-unit binding (unified-access-control-r2 task 165, owner round 35 items 1-2).** Every SPE container carries the
business unit that owns it as the custom property `spaarkeBusinessUnitId`, and the BFF's SPE admin plane reaches **no**
unbound container — not even for a root-unit administrator. H8 therefore runs after H5, reads the new environment's
**root** business unit before creating anything (no environment URL or no root unit → Resumable, nothing created), and
once the root container is verified readable stamps it, reads the stamp back, and **removes** the container if the stamp
did not land (QuarantineRequired: `spe-container-binding-failed` / `…-not-removed` / `…-infra-fault`). Every other creation
path follows the same rule: the BFF, `New-BusinessUnitContainer.ps1`, `Provision-Customer.ps1` step 10,
`Create-NewContainerType.ps1 -CreateTestContainer -TestContainerBusinessUnitId <bu>`. Each also writes the `spaarkeCustomerId`
marker (the scripts take `-CustomerId` / `-TestContainerCustomerId` = the BFF's `Customer__Id`) and removes the container if
either property did not read back (task 227g) — the BFF's app-only calls refuse an unmarked container (T227d).

**The replication wait and every other resume (owner rounds 41 + 49; ported onto the single-container H8 at the
2026-10-05 master merge).** An SPE container may be unaddressable for up to 24h after creation, so H8 binds only after the
app-only GET verifies it. To make that safe, H8 **records what it created in the run immediately, in TYPED fields**
(`interStepState.speContainerCreation`: the container, and whether a creation got no answer) before it verifies, binds or
writes anything else. Gate evidence is never read back for this: the Cosmos SDK's Newtonsoft serializer stored a
`JsonElement` there as `{"valueKind":1}`. Every later entry (the re-dispatch after the 24h wait, a resume after a
quarantine, a crash) reads that record and **resumes with the same container**. A recorded container is never created
again, and one whose activation failed is re-activated rather than replaced.

**A create whose answer was lost** (a client timeout or a dropped connection after the POST was sent) may have created a
container. H8 records that and answers QuarantineRequired `spe-container-creation-in-doubt`. It creates NO container, and
it does not adopt one by listing the type: the container type is shared with other customers, so a listed container could
be another customer's. The operator checks, as the owning app or with a SharePoint Embedded admin token:
1. List the containers of the container type the diagnostic names.
2. Look for the display name it gives, with a **description naming this customer and this run id**. H8 writes the run id
   into every container's description for this purpose.
3. If one exists, set `interStepState.speContainerCreation.rootContainerId` on the run document to its id (H8 then
   activates, verifies and binds THAT container), or remove the container.
4. Clear the quarantine (`POST /api/runs/{id}/clear-quarantine`) and resume. Clearing it is the confirmation: without a
   recorded container, H8 then creates one.

The container is **handed to H7** (`interStepState.speContainerId` → `sprk_SharePointEmbeddedContainerId`) **only once it
is bound**. H8 writes that field only on completion, after the bind. H7 depends on H8 in the DAG and refuses a container id
from a run where H8 has not completed. While the run waits, H9 and the other branches still advance; H7, H10 and what
follows wait for the bound container. A creation H8 could not record (the run deleted, or every merge lost a concurrent
write) is QuarantineRequired `spe-creation-record-not-persisted`, naming the container for an operator to bind (`-Bind`)
or remove.

**A later run of the same customer (task 227e).** The creation record is per run, so a new run starts with none — and
H4b would repoint the customer's BFF at any new container, leaving their files behind. H8 therefore reads the container the
environment records (`sprk_SharePointEmbeddedContainerId`, which H7 writes from H8's container at the end of the first
run) and reuses it: recorded as `adopted` (kept on the record for every later entry), verified, bound and marked —
**never removed**, even if its bind fails (QuarantineRequired `spe-container-binding-failed-not-removed`). An environment
variable is data, not proof, so before writing anything to that container H8 reads it and refuses (Resumable, nothing
written, nothing created) a container that does not exist (`spe-recorded-container-not-found`) or is not the customer's —
another container type, another customer's `spaarkeCustomerId`, or another business unit's stamp
(`spe-recorded-container-not-the-customers`). These checks, like the grants that follow, run before H8 writes anything.

**Operator rule — resume, never start a new run for a half-built customer.** The environment variable exists only once H7
has run. If a run stops after H8 completed but before H7, **resume that run**: a new run would find no recorded container
and create a second one (the first stays bound and marked, but H4b would point the BFF at the new one). If the run's own record and the environment name
different containers, H8 stops (Resumable `spe-duplicate-customer-containers`, both named): it never picks one. An
unreadable value is Resumable `spe-recorded-container-unreadable` — nothing is created without an answer. No listing is
involved (the type is shared), so renaming the container does not matter.

**Ownership marker (task 227e).** After the bind H8 writes the custom property `spaarkeCustomerId` = customer id — the
marker the customer's BFF recognises its containers by (T227d) — GET first, so a re-run writes nothing. A refusal is
Resumable (`spe-container-marker-failed`); the container stays on the run's record and a resume marks it.

Container ID persisted to the Dataverse env-var (`sprk_SharePointEmbeddedContainerId`, H7) and to the BFF as plain settings (`EmailProcessing__DefaultContainerId`, `Communication__ArchiveContainerId`, H4b — T227c) — enables I4 invariant enforcement.

**Lead-time**: none per customer. The container type and its owning app are a one-time setup; H0's `SpeOwnerCredential`
check confirms the owner entry, the owning-app token and the registration before any resource is created.

### 7.6 Phase 6 — BFF Deployment (H9)

**H9** (`H9BffDeployHandler`) deploys the CI-published BFF artifact — nothing is built at provision time. In order:

1. Scan the hardened `Deploy-Release.ps1` Phase 4 for a `spaarkedev1` hardcode (Gap 2) — blocks if found.
2. Resolve and verify the artifact manifest (`latest.json`) — blocks on a missing or red gate result recorded by CI.
3. Download the artifact zip (UAMI RBAC).
4. **Scheduled-jobs slot guard** — set `Scheduling__RunScheduledJobs=false` on the staging slot and make it slot-sticky on the site (merge, never replace; a re-run writes nothing). Fails closed: if it cannot be set, nothing is deployed, because a slot without it runs the BFF's scheduled jobs against production data (ADR-036 A1 rule 2).
5. Kudu zip-deploy to the staging slot.
6. Staging `/healthz` probe, then the NFR-01 publish-size check.
7. Swap staging → production, production `/healthz` smoke test, re-swap rollback on failure.

**Blue-green** via staging slot in upgrade mode; rollback via re-swap.

r3-era gates (all must pass — run in CI and recorded in the manifest):
- Analyzers-as-errors
- ~~God-class ratchet~~ — retired 2026-08-20 (root CLAUDE.md §11.5: complexity is judged at review, with a non-blocking `scripts/report-large-server-files.ps1` observation report)
- 5 new ArchTests (I1–I5 tenant-isolation invariants)
- Naming-conformance (`scripts/naming-conformance-check.ps1` exit 0 — blocking in `sdap-ci.yml` since T230a)
- Graph app-role parity (`GraphAppRoles.cs` constant)

**BFF publish-size ceiling**: **≤ 60 MB compressed (HARD)**. Current net10 baseline: 44.96 MB incl. PDBs. Per-PR measurement + delta report required (NFR-01).

### 7.7 Phase 7 — Dataverse App Users + Users (H10, H11)

**H10** registers 2 Dataverse Application Users as System Administrator:
- BFF app-reg
- UAMI (post-Phase-C)

Then grants the Entra Graph app roles from `GraphAppRoles.cs` onto the UAMI SP — **except** the four mailbox roles
(`Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read`), which H14a grants through Exchange scoped to the
customer's group. Exchange adds the two sources together, so an Entra mailbox grant would reach every mailbox in the tenant.

**T2 verification**: `systemusers?$filter=applicationid eq {uami-app-id}` returns count 1.
**T3 verification**: UAMI SP `appRoleAssignments` holds the 11 Entra-granted roles and **none** of the 4 mailbox roles.

**H11** provisions the customer's users (T232, owner D2 — Model 1 users are **B2B guests in Spaarke's tenant**, and a
Model 1 run takes only `B2BGuest`):

1. **Before inviting anyone** it refuses (Resumable, nothing written) a run whose environment security group
   (`environmentSecurityGroupId`, `PRQ-C-10`) is not named `sprk-{customerId}-users` or not security-enabled
   (`userprov-security-group-rejected`), or whose environment restricts guests
   (`organization.restrictguestuseraccess`, `PRQ-C-12` — `userprov-guest-access-restricted`). The group is the
   isolation boundary between Model 1 environments: all live in Spaarke's tenant, so an environment without it admits
   every user of that tenant, including other customers' guests. H11 checks the group's **name**; that it is the group
   **set on the environment** is checked by the skill (Step 1e-bis, Power Platform admin API) — only a Power Platform
   admin can see it. It also resolves the guests' security role(s) here, so a missing role sends no invitation.
2. Each user becomes a guest: an existing guest with that address is **reused without a second invitation email**; an
   address that belongs to a member of the tenant is refused.
3. It waits for every guest to redeem the invitation (`b2b-consent` gate → `WaitingOnGate`; re-run H11 to re-check).
4. It adds each guest to the group, then reads the guest's Dataverse user through the `azureactivedirectoryobjectid`
   alternate key — Dataverse adds a group member who is not yet a user on that read (Microsoft's documented app-only
   path; no Power Platform admin role) — and associates `H11UserProvisioningOptions:GuestSecurityRoleNames`
   (default `Spaarke Basic User`, root business unit; resolved in step 1). A refusal right after the group add can be
   membership propagation — resume. A guest who never redeems keeps the gate pending; resend the invitation from the
   Entra admin center, then resume (H11 never re-invites — it would re-send mail on every re-check).

**Licensing**: Model 1 guests get **no licence**. Spaarke pays for their access **pay-as-you-go** on the customer's
stamp subscription (owner 2026-10-07): the operator links the environment to a billing policy on that subscription
(`PRQ-C-11`, checked by the skill — L2 cannot see billing). Meter: Power Apps per app, **per active user per app per
month**. `NativeAccount` (Model 2) still assigns the configured licence SKUs and refuses to create anyone when none is
configured (`userprov-license-sku-not-configured`).

### 7.8 Phase 8 — Configuration Seed (H12a, H12b, H12c)

**H12a** and **H12b** are DAG-parallel (no cross-dependency):

**H12a** seeds the AI chain: type-lookups → actions → tools → knowledge → skills → playbooks → output-types → **playbook consumers** (single AI routing surface per ADR-039; `spaarke-playbook-embeddings` retired).

**H12b** seeds app-config: DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions.

Both consume the **declarative seed manifest** (resolves the `scripts/seed-data` MVP vs `infra/dataverse` R7 drift per INVENTORY §9).

**H12c** wires runtime references: `sprk_aimodeldeployment` rows point at customer's OpenAI deployment (dedicated per customer — both models). Runs AFTER H12a + H12b + H2a (OpenAI deployed).

### 7.9 Phase 9 — Post-Deploy Integrations (H14)

Three DAG-parallel sub-steps:

- **(a) Exchange mailbox access (RBAC for Applications, task 251)** — the stamp UAMI only (the BFF app registration does no app-only mail; granting it would widen its reach). Through the Worker's sidecar: register the UAMI in Exchange (`New-ServicePrincipal`), then one assignment per mailbox role, named `{prefix}-{customerId}-{Role}`, scoped to the intake group. Get-before-set: any existing assignment that differs → Drift, nothing changed (**T4**). Grants take effect in 30 min – 2 h (Microsoft).
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
- **All 7 §4B T1–T7 silent-fail traps cleared** (see §9)
- **The §4D I2–I5 tenant-isolation invariants sample-verified on the deployed stamp** (see §8; I1 is a build gate —
  T230a), with I3 checking the Cosmos account holds exactly `cosmos-db.bicep`'s containers and partition keys
- (Naming conformance is a blocking CI gate — `scripts/naming-conformance-check.ps1` in `sdap-ci.yml` — not a per-run
  H13 check: it lints the repository's own files, T230a.)
- Cost envelope: the subscription's month-to-date cost, extrapolated, within 20 % of $400/month (one envelope for both models — §3.2, T229)

**`sprk_dataverseenvironment.Setup Status` transitions to `Ready` only if H13 exits 0.**

### 7.11 Phase 11 — Secure-record environment setup (operator; before the customer is told the environment is ready)

Secure projects, matters and work assignments (unified-access-control-r2) need Dataverse security configuration no handler
creates: the `Secure Record` business unit (holding no users and **no container**), the named `Secure Record Owners` team, the
`Secure Record Owner` role and its privileges, role depth, and field security on `sprk_issecure`. Until it is done the BFF
refuses to make any record secure (fail closed). Follow [`SECURE-PROJECT-ENVIRONMENT-SETUP.md`](./SECURE-PROJECT-ENVIRONMENT-SETUP.md)
for this environment — it is the one source for the steps and their scripts — and treat **its §7 verification checklist as
the gate**: do not report the environment as secure-record ready until every item passes. The containers are not part of
this phase: the BFF creates each secure record's container when the record is made secure (task 227g).

---

## 8. Tenant Isolation Invariants (I1 – I5)

Cross-tenant data bleed is the single class of catastrophe r1 must make structurally impossible. **These five invariants are BINDING** and enforced by 5 new ArchTests sequencing into the r3 forcing-functions ecosystem.

| # | Invariant | Enforcement | Severity if breached |
|---|---|---|---|
| **I1** | No hardcoded default tenant in provisioning scripts — every script requires `-TenantId` mandatory | Pre-commit ArchTest; grep-scan for tenant-shaped GUID defaults. Code fix `1834b77bc` removed the `Register-EntraAppRegistrations.ps1:63` default | HIGH — data-bleed (Spaarke users granted to customer Dataverse) |
| **I2** | All AI Search queries include unconditional `tenantId eq` filter — regardless of index or query shape | New ArchTest scans BFF for AI Search `.Search(...)` without `tenantId eq` filter | **CATASTROPHIC** — legal privilege leak (one firm's motions returned to another firm) |
| **I3** | All Cosmos reads/writes include partition-key predicate — no cross-partition queries against tenant-scoped containers | New ArchTest scans for Cosmos SDK `.ReadItemAsync(...)`/`.CreateItemAsync(...)` without explicit `PartitionKey` | HIGH — conversational PII leak |
| **I4** | SPE container IDs come from the record or the stamp's own settings — no fallback default; every app-only SPE call passes `SpeContainerOwnershipGuard`, the one definition of the stamp's containers (T227d/T227f) | New ArchTest fails on any SPE-container-ID string literal (`b!...`) in BFF services | **CATASTROPHIC** — privileged docs in wrong container |
| **I5** | Graph token acquisition per-tenant scoped — delegated: OBO with caller `tid`; app-only: `.default` with target `tid` explicitly named | New ArchTest scans `GraphClientFactory` for token acquisition without explicit `tenantId` | **CATASTROPHIC** — Graph resources (SPE files, mail, group membership) from wrong tenant |

**Verification lifecycle**:

- **At code time**: 5 ArchTests (CI Tier-1 blocking, coordinated PR with `ci-cd-unit-test-remediation-r1`)
  - **Functions projects** live under `src/server/functions/` ([ADR-052](../adr/ADR-052-workload-placement.md) §5). The I2 and I3 ArchTests scan all of `src/server/**`, so a Model-1 shared Function carries those invariants automatically; I4 and I5 currently scan BFF paths only (`Sprk.Bff.Api/Services`, `Sprk.Bff.Api/Infrastructure/{Graph,Auth}`), so a Function's SPE-container and Graph-token code is covered by review until those scans are widened.
- **At provisioning time**: H13 samples I2–I5 on the deployed stamp (I3: the Cosmos account holds exactly the template's containers and partition keys). I1 is verified at code time only — the L2 Worker ships and runs no script (T230a / T253)
- **At runtime**: OpenTelemetry span attributes include `tenantId`; log samples cross-referenced for anomaly detection

**Scope**: r1's threat model is honest-but-buggy code + operator error. External-actor threat (cross-tenant abuse from the internet) is handled by CORS + AAD auth + per-request `tid` validation per ADR-028.

---

## 9. Silent-Fail Trap Catalog (T1 – T7)

Seven known-issue guardrails baked into handler post-conditions. Each has been diagnosed in production; ignoring any results in a BFF that boots but fails silently in a specific code path.

| # | Trap | Owning handler | Verified by |
|---|---|---|---|
| **T1** | App Service `keyVaultReferenceIdentity` not set to UAMI → KV references fail post-swap | H4 | ARM read `keyVaultReferenceIdentity == UAMI-resource-id` |
| **T2** | Dataverse App User for UAMI not registered → BFF loses Dataverse access post-swap | H10 | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 |
| **T3** | UAMI SP missing Graph app-role → Graph calls fail with 403 | H10 | UAMI SP `appRoleAssignments` includes all 14 IDs from `GraphAppRoles.cs` |
| **T4** | Stamp identity missing its group-scoped Exchange mailbox roles (Mail.* calls 403), or holding one outside the group (reaches other customers' mailboxes) | H14(a) | H13 reads the identity's assignments via the sidecar: every `Application Mail.*` role in scope, none outside |
| **T5** | Slot MI vs slot MI KV RBAC parity broken → cold-start KV-ref failure after slot swap | H4 (interim); H10 + Phase C UAMI (structural) | Both slot MIs have KV RBAC (interim); **structurally impossible post-Phase-C** |
| **T6** | SPE container work uses a delegated token → 403 "public client not allowed" | H8 | H13 lists `GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}` app-only as the owning app (through the Worker UAMI's federated credential) and passes only if the run's container (H8 output) is in the list. Container absent or a delegated-token refusal → Failed; other refusals / 404 / errors → InfraFault |
| **T7** | `Customer__Id` missing, blank or another customer's id on either BFF slot → the BFF runs on the derived-from-resource-group path, or names the wrong customer (§6.5.1) | H4b (writes both slots) | ARM read of both slots' app settings: `Customer__Id` == run customerId (T238) |

H13 acceptance gate verifies all 7 traps cleared with 0-failure status.

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
2. Verify §2.4 external lead-time items (Azure quota, SPE container type + owning app set up per the topology runbook, Model 2 admin consent)
3. Verify §2.2 identity + access role assignments
4. Confirm the tenancy model with the customer (§3) — Model 1 = Spaarke tenant, Model 2 = customer tenant / stakeholder

### 12.2 Provisioning sequence (interim)

```powershell
# Phase 2 — infra
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -TenantId "<customer-tenant-guid>" `   # MANDATORY per I1
    -Environment "prod" `
    -SubscriptionId "<sub-guid>" `
    -Region "westus2"

# Phase 3 — identity + secrets
# BINDING: -SkipClientSecret is the DEFAULT since Bucket B HIGH#4 SESSION 18 (customer-provisioning-
# orchestration-r1 adversarial verify workflow wepdcb8we). The script REFUSES to mint a new
# BFF-API-ClientSecret without an explicit `-AllowClientSecretMint -MintReason '<audit string>'` pair.
# Passing -SkipClientSecret here is redundant with the default but preserved for explicit operator
# intent per the auth-v4 task 033 (2026-08-24) closure of the BFF-API-ClientSecret lifecycle. See
# .claude/constraints/provisioning.md § KV credential lifecycle rule 1.
.\scripts\Register-EntraAppRegistrations.ps1 `
    -CustomerId "acme" `
    -TenantId "<customer-tenant-guid>" `   # MANDATORY per I1 (code fix 1834b77bc)
    -SkipClientSecret                      # BINDING per Bucket B HIGH#4 (session 18); silent-absence default now = skip anyway

# H4 KV secret population is performed inside Provision-Customer.ps1 in the interim path
# When Phase H canonical catalog manifest lands, seeder invocation is `scripts/canonical-secret-catalog/Seed-Secrets.ps1`

# Phase 4 — Dataverse. The OPERATOR creates the environment in every path (prereqs.yaml PRQ-C-09; L2's H5 only adopts
# it — T228). Its domain MUST be spaarke-{customerId} (→ https://spaarke-acme.crm.dynamics.com/), the name POST /api/runs
# and H5 require. If the domain is taken, Dataverse silently appends a digit (spaarke-acme0) — check the URL it reports
# and use spaarke-{customerId}-{environmentName} instead. Then add the L2 Worker identity (its application/client id) as a
# System Administrator application user (PRQ-C-09).
pac admin create `
    --name "spaarke-acme" `
    --domain "spaarke-acme" `
    --region unitedstates `
    --type Sandbox
pac admin assign-user --environment "https://spaarke-acme.crm.dynamics.com/" `
    --user "<L2 control-plane UAMI client id>" --role "System Administrator" --application-user

.\scripts\Deploy-DataverseSolutions.ps1 -EnvironmentUrl "<dv-org-url>"

# Phase 5 — SPE (app-only as the owning app — T6 fix)
# The container type + owning app are ONE-TIME setup: docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md.
# Create-NewContainerType.ps1 is deprecated (task 213.3) and throws. The owning app has no certificate or
# secret (task 248) — only H8, running as the L2 Worker UAMI, can act as it. The customer's container is
# therefore created by an L2 provisioning run (H8), not from a workstation: New-BusinessUnitContainer.ps1
# needs a certificate-based (legacy) owning app and cannot act as an MI-FIC owner.

# Phase 5b — SPE container BINDING GATE (task 165, owner round 35 item 2 — MANUAL GATE, BLOCKING).
# No SPE admin route reaches an unbound container. Before task 165's BFF is deployed to an environment, and before a
# further environment is onboarded onto a container type another environment already uses, EVERY container of every
# config must be bound. Dry run first (read-only), then -Apply, then -Verify — which must exit 0:
.\scripts\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl "<dv-org-url>" -KeyVaultName "<bff-kv>"
.\scripts\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl "<dv-org-url>" -KeyVaultName "<bff-kv>" -Apply
#   containers no record claims are listed UNDERIVABLE: bind each to its owner explicitly (or remove it) —
.\scripts\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl "<dv-org-url>" -KeyVaultName "<bff-kv>" `
    -Bind '<containerId>=<businessUnitId>' -Apply
.\scripts\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl "<dv-org-url>" -KeyVaultName "<bff-kv>" -Verify   # MUST exit 0
# When onboarding onto a SHARED container type, run the -Verify above in EVERY environment that already uses the type —
# a container of the type left unbound in one environment is otherwise nobody's to administer.

# Phase 5c — SPE config Key Vault secret names (task 165, round 35 item 3; round 41 item 4; narrowed by round 65 item 1).
# The BFF reads NO secret by this name (SPE Admin runs as the BFF's own identity) and refuses no config for it; it only
# answers 400 when a config is SAVED naming a secret outside 'spe-owning-app-'. The one reader left is the Phase 5b
# backfill, which lists a config's containers as its owning app and SKIPS a config whose stored name does not conform.
# Run this before Phase 5b; for each config it lists, repair it below or bind its containers with -Bind:
.\scripts\Test-SpeConfigSecretNames.ps1 -EnvironmentUrl "<dv-org-url>" -Verify
#   for each config it lists (MANUAL GATE; never delete the config): store the owning app's client secret under a
#   conforming name in the BFF Key Vault(s) and point the config at it — dry run, then -Apply (mint a new client secret
#   with -MintClientSecret, or copy one with -SourceKeyVaultName/-SourceSecretName), then -Verify (exit 0):
.\scripts\Repair-SpeConfigSecretName.ps1 -EnvironmentUrl "<dv-org-url>" -ConfigId "<config-id>" `
    -SecretName "spe-owning-app-<name>" -KeyVaultName "<bff-kv>","<operator-kv>"
.\scripts\Repair-SpeConfigSecretName.ps1 ... -MintClientSecret -Apply
.\scripts\Repair-SpeConfigSecretName.ps1 ... -Verify
# Phase 6 — BFF deploy
.\scripts\Deploy-BffApi.ps1 -CustomerId "acme" -Slot production

# Phase 7 — Dataverse App User + Graph app-role parity
# Interim: PPAC UI + Graph SDK; see auth-deployment-setup stub for MI-first checklist
# T2 verification MANDATORY: systemusers?$filter=applicationid eq {uami-app-id} returns 1

# Phase 8 — config seed
.\scripts\seed-data\Deploy-All-AI-SeedData.ps1 -EnvironmentUrl "<dv-org-url>"
.\scripts\seed-data\Seed-PlaybookConsumers.ps1 -EnvironmentUrl "<dv-org-url>"

# Phase 9 — post-deploy integrations
# H14a runs only inside the L2 Worker (its sidecar signs in as 'Spaarke Exchange Admin') — no manual script (task 251).
# Graph webhook subscriptions per Communication module — see COMMUNICATION-DEPLOYMENT-GUIDE.md

# Phase 10 — acceptance gate
.\scripts\Validate-DeployedEnvironment.ps1 -CustomerId "acme"
# ONLY if this exits 0 mark Setup Status = Ready in Dataverse
```

### 12.3 Post-provisioning verification (T1 – T7 individually)

Run each verification separately + record in the run's handoff notes:

```powershell
# T1 — App Service keyVaultReferenceIdentity == UAMI
az webapp config show --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --query "keyVaultReferenceIdentity"

# T2 — UAMI registered as Dataverse App User
pac admin application list --environment <dv-org-url>

# T3 — Graph app-role parity: the 11 Entra-granted roles, and none of Mail.Read / Mail.ReadWrite / Mail.Send / MailboxSettings.Read
az rest --uri "https://graph.microsoft.com/v1.0/servicePrincipals/<uami-principal-id>/appRoleAssignments"

# T4 — the stamp identity's Exchange mailbox roles, all limited to the customer's group (Organization Management admin)
# A group-scoped assignment reads RecipientWriteScope = Group, with the group's Name in CustomResourceScope
Get-ManagementRoleAssignment -RoleAssignee '<uami-principal-id>' | Select Name, Role, RecipientWriteScope, CustomResourceScope

# T6 — the run's container is listed for its container type, app-only as the owning app
# Performed by H13 (the owning app is reachable only through the L2 Worker UAMI's federated credential);
# read the T6 result in the run's H13 output rather than reproducing it from a workstation.

# T7 — Customer__Id on BOTH slots == the customerId (§6.5.1)
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --query "[?name=='Customer__Id'].value" -o tsv
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --slot staging --query "[?name=='Customer__Id'].value" -o tsv
```

### 12.4 Handoff report

Record in `projects/{active-project}/runs/{runId}.md`:

- CustomerId, tenancy model, all resource names + GUIDs
- Every T1-T7 verification result (green / red)
- Every I1-I5 sample-query result
- BFF publish-size delta vs baseline
- Any manual gate + operator decision + timestamp

### 12.5 T1 trap: exit-134 (SIGABRT) on first startup after provisioning

> Added 2026-08-25 by `customer-provisioning-orchestration-r1` task 202 A40 (auth-v4 §10.1 Δ4 + FR-37). Governs recovery when H4's T1 PATCH silently regressed after provisioning claimed success.

**Symptom**: BFF container exits with code 134 (SIGABRT) at startup. Kudu docker-log shows an unhandled exception referencing an `IOptions<T>` module whose values source from a `@Microsoft.KeyVault(SecretUri=...)` app setting — but the app-setting value in the App Service configuration LOOKS correct in the portal.

**Root cause**: `keyVaultReferenceIdentity` on the App Service is either unset OR set to `'SystemAssigned'` while the actual identity attached is user-assigned (UAMI). App Service resolves `@Microsoft.KeyVault(...)` references against the identity named in that site property; when it names a non-existent identity, the resolved setting value is `null` at runtime → downstream `IOptions.ValidateOnStart` throws → SIGABRT.

Provisioning should have set this via `ArmAppServiceIdentityPatcher.cs` at H4 (task 125 SDK-native impl; `WebSiteResource.UpdateAsync` / `WebSiteSlotResource.UpdateAsync` on both slots, verified post-PATCH by the H4 handler via `IArmKeyVaultRefProbe`, task 044/123). If you're hitting SIGABRT, either H4 didn't run, H4 ran against the wrong identity, or a later slot ceremony (H9 deploy, rollback per obligation 051-E — see §12.6 below) created a new slot without re-asserting the site property.

**Recovery** (per pattern `.claude/patterns/provisioning/keyvault-reference-identity-invariant.md` — F16.5 workaround kept operator-runnable even though production H4 uses the SDK directly):

```bash
sub="{subscription-id}"
rg="{resource-group}"
app="{app-service-name}"
uamiId="/subscriptions/$sub/resourceGroups/{uami-rg}/providers/Microsoft.ManagedIdentity/userAssignedIdentities/{uami-name}"

# Production slot
az rest --method patch \
  --url "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Web/sites/$app?api-version=2022-03-01" \
  --body "{\"properties\":{\"keyVaultReferenceIdentity\":\"$uamiId\"}}"

# Staging slot (T5 slot-parity — required, not optional)
az rest --method patch \
  --url "https://management.azure.com/subscriptions/$sub/resourceGroups/$rg/providers/Microsoft.Web/sites/$app/slots/staging?api-version=2022-03-01" \
  --body "{\"properties\":{\"keyVaultReferenceIdentity\":\"$uamiId\"}}"

# Restart both slots so the runtime picks up the corrected reference identity
az webapp restart --name "$app" --resource-group "$rg"
az webapp restart --name "$app" --resource-group "$rg" --slot staging

# VERIFY (read-back — do NOT trust the PATCH response alone)
az webapp show --name "$app" --resource-group "$rg" --query "keyVaultReferenceIdentity" -o tsv
az webapp show --name "$app" --resource-group "$rg" --slot staging --query "keyVaultReferenceIdentity" -o tsv
# Both MUST print $uamiId verbatim. Anything else = re-run this recipe.
```

If PATCH landed but startup still fails: RBAC hasn't propagated — check that the UAMI has `Key Vault Secrets User` on the target KV (`az role assignment list --assignee <uami-principal-id> --scope <kv-resource-id>`) and give AAD RBAC up to ~5 min.

### 12.6 Slot persistence: `keyVaultReferenceIdentity` is NOT copied by `az webapp deployment slot create --configuration-source`

> Added 2026-08-25 by `customer-provisioning-orchestration-r1` task 202 A40 (auth-v4 §10.2 CORRECTION). Governs any slot create / swap ceremony downstream of H4.

**BINDING (auth-v4 change request §10.2)**: `keyVaultReferenceIdentity` is a **SITE property, not an app setting**. When creating a new deployment slot with `--configuration-source`, app settings ARE copied but this site property is NOT. Every slot create / swap ceremony MUST re-assert the property on the new slot BEFORE its first startup, or the new slot will SIGABRT with exit 134 (§12.5 above).

**Live counter-example** (do NOT copy this pattern):

- `projects/dotnet-10-upgrade-r1/notes/deploy-net10-slot-phase1.ps1:40` — a slot-create script that assumes `--configuration-source` copies everything. Under that assumption the new slot has empty `keyVaultReferenceIdentity` at first-startup time and would immediately SIGABRT once any `@Microsoft.KeyVault(...)` app setting resolves.

**Recovery paths that MUST re-assert the property** (before starting or swapping the affected slot):

- **H9 BFF deploy** — if it creates a new slot (rather than deploying into an existing one), re-run the PATCH recipe from §12.5 against the new slot immediately after create + before any warmup.
- **Rollback ceremony** — per auth-v4 obligation 051-E (live to 2026-11-23), any rollback that resurrects an older slot MUST re-assert the property against the resurrected slot's current site-property state (which may be stale from a prior rollback point).
- **Any operator-initiated slot manipulation** — `az webapp deployment slot create`, `az webapp deployment slot swap` (safe for the property because swap is metadata-preserving), or portal-driven slot operations.

**Enforcement**: task 186 (Phase F E2E acceptance) asserts `keyVaultReferenceIdentity == {UAMI RID}` on BOTH slots AFTER H9 deploys — see `projects/customer-provisioning-orchestration-r1/tasks/186-real-phase-f-e2e-acceptance-rerun-task-089-for-real-this-time.poml` acceptance-criterion added 2026-08-25. If that criterion fails for a real customer run, it means either H9 (or a rollback since) created a slot without re-asserting the property — apply §12.5 recovery THEN escalate per Human Escalation Triggers to determine which downstream ceremony broke the invariant.

### 12.7 Dual Dataverse App-User contract + the `azureactivedirectoryobjectid` silent-fail trap (T2, §10.4)

> Added 2026-08-26 by `customer-provisioning-orchestration-r1` task 205d (auth-v4 §10.1 Δ5 + §10.4). Documented by auth-v4 as its **"single most-missed item"** — three prior audits missed it. H10 (`H10DataverseAppUserGraphParityHandler`) creates both rows; the T2 probe (`DataverseAppUserPairT2Probe`, invoked by H13) re-verifies both, now including the byte-equality assertion this section describes.

**The dual-row contract**: every provisioned environment needs **TWO** `systemuser` rows on the target Dataverse environment, not one:

1. **App-reg row** (for OBO tokens) — `applicationid` = the stamp's own BFF app-registration client ID (one per customer in **both** models — D-13; the former single shared multitenant Model 1 app-reg is retired). `azureactivedirectoryobjectid` is left to Dataverse's own AAD auto-resolution — reliable for a standard Entra app registration.
2. **UAMI row** (for app-only calls) — `applicationid` = the UAMI's client ID **AND** `azureactivedirectoryobjectid` = the UAMI's **principalId (service principal object id) — NEVER its clientId**. Both models: the per-stamp `mi-spaarke-{customerId}-{env}` (the former shared Model 1 `sprk-{env}-shared-bff-uami` was retired with its stack by task 225a).

**The trap**: both the UAMI's clientId and its principalId are valid-shaped GUIDs. A row created with `azureactivedirectoryobjectid` set to the clientId **still passes every existence + count check** — `pac admin application list`, the §12.3 T2 verification, and (pre-task-205d) H13's own T2 probe all reported the row as present and correct. The only symptom is that Dataverse rejects the token: the app-only Dataverse call's `oid` claim (the UAMI's real principalId) never matches a row registered under the wrong value.

**Trap-recognition symptom**: an app-only Dataverse call 401s (or the BFF logs a Dataverse auth failure) for a customer whose provisioning run reported H10/T2 as fully green — the systemuser row **EXISTS** and **passes the count check**. This combination (401 despite a passing count-check) is the tell. Do not assume the row is missing; it is present with the wrong `azureactivedirectoryobjectid`.

**Diagnose**:

```powershell
# 1. Confirm the row exists (this alone is NOT sufficient — it's the trap's own symptom)
# GET /api/data/v9.2/systemusers?$filter=applicationid eq {uamiClientId}&$select=systemuserid,azureactivedirectoryobjectid

# 2. Compare the returned azureactivedirectoryobjectid against the UAMI's REAL principalId
az identity show --name <uami-name> --resource-group <rg> --query principalId -o tsv
az identity show --name <uami-name> --resource-group <rg> --query clientId -o tsv

# If azureactivedirectoryobjectid == clientId (NOT principalId) -> the trap has fired.
```

**Recovery** — repair the existing row via Web API PATCH (do NOT delete + recreate; the `systemuserid` and any role associations stay valid):

```bash
env="{dataverse-environment-url}"           # e.g. https://spaarke-acme.crm.dynamics.com
rowId="{systemuserid-from-the-diagnose-step}"
principalId="{uami-real-principalId-from-az-identity-show}"

az rest --method patch \
  --url "$env/api/data/v9.2/systemusers($rowId)" \
  --headers "Content-Type=application/json" \
  --body "{\"azureactivedirectoryobjectid\": \"$principalId\"}"

# VERIFY (read-back)
az rest --method get \
  --url "$env/api/data/v9.2/systemusers($rowId)?\$select=azureactivedirectoryobjectid"
# MUST print the UAMI's principalId verbatim, not its clientId.
```

If the repair PATCH itself 403s, the caller lacks the Dataverse privilege to update `systemusers` — use an existing System Administrator app user or an operator identity with that role, per §12.1 pre-work.

**Code-level protection (task 205d)**: `H10DataverseAppUserGraphParityHandler` now writes `azureactivedirectoryobjectid` explicitly (in the same POST as `applicationid`) for the UAMI row — it does not rely on Dataverse's applicationid-only auto-resolution for a Managed Identity. `DataverseAppUserPairT2Probe`'s H13 re-verification now byte-compares the observed `azureactivedirectoryobjectid` against the expected UAMI principalId (`DataverseAppUserPairT2ProbeTests.ProbeAsync_ByteEqualityMismatch_ReturnsFailed_NamesTheTrap` pins the negative case) — a row that passes the count check but fails the byte-equality check now fails T2 loud, at H13, rather than surfacing as a silent 401 later at first customer use.

> **Operator note (rollout of this check)**: if a run PROVISIONED BEFORE task 205d relied on Dataverse's applicationid-only auto-resolution and it happened to resolve the UAMI row's `azureactivedirectoryobjectid` incorrectly, that environment may fail T2 for the FIRST TIME on the next H13/task-186 run after this change deploys — even though it previously reported green. This is NOT a regression: it is this section's detection working as intended on a latent pre-existing misconfiguration. Apply the recovery recipe above; do not assume the provisioning pipeline itself broke.

**Scope note**: this section is purely about the `azureactivedirectoryobjectid` identity-matching trap. It does not concern, and does not reintroduce, any never-delete rationale for `Dataverse-ClientSecret` / `BFF-API-ClientSecret` — that credential-retention question was superseded 2026-08-24 when ADR-028 exception E-3 closed (see root `CLAUDE.md` §9 + this guide's credential sections); do not conflate the two.

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

> 🔴 **CONTRADICTED 2026-08-28** by `sdap-SPE-admin-app-r2`. The remedy below conflates two different
> things: **"confidential client" is not "app-only".** Container-type CREATE is **delegated-only** —
> Microsoft lists Application permission as *"Not supported"*, and an app-only
> (`client_credentials`) token gets `403 accessDenied` even with the role granted and
> admin-consented (probed twice 2026-08-28; also task 010, 2026-08-21).
> `scripts/Create-NewContainerType.ps1` does exactly that (line 46 `client_credentials` → line 76
> `POST /beta/storage/fileStorage/containerTypes`) and therefore **cannot work**. The correct fix for
> a public-client rejection is a confidential client performing a **delegated** exchange
> (auth-code / OBO), not `client_credentials`.
> **Until fixed, create container types via the SPE Admin app, the VS Code extension, or the
> SharePoint admin center.** Treat H8 as unproven. Full analysis:
> [`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](../architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md) §7.

T6 root cause. Container **types** are created once by the operator through a delegated flow (topology runbook).
Container work (H8 create / activate / verify, H13's T6 check) uses an app-only token as the container type's owning
app, obtained through the L2 Worker UAMI's federated identity credential on that app (task 248) — no certificate or
secret. If retrofitting an existing env: re-run H8.

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

### 13.6 Naming-conformance failure in CI

`scripts/naming-conformance-check.ps1` exits non-0 in `sdap-ci.yml` (a build gate since T230a — H13 no longer runs it per
customer). Read the output — typically a secret or vault name in a changed file that matches neither `sprk-{env}-kv` /
`sprk-{customerId}-{env}-kv` nor the secret-name rules. Rename it, or codify an exception in the script with a rationale
(reviewed at code-review).

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
| 2026-08-25 | §12.5 (T1 exit-134 SIGABRT symptom recognition + recovery) + §12.6 (slot-persistence BINDING — `keyVaultReferenceIdentity` not copied by `--configuration-source`) added | task 202 A40 (auth-v4 §10.1 Δ4 + §10.2 CORRECTION; FR-37, T1/T5) |
| 2026-10-01 | §6.5.2 (customer workforce tenants `WorkforceIdentity__CustomerTenantIds__N`, identity-link job switch, contact identity-binding schema prerequisite) + §6.5.3 (identity-collision operator procedure) + §7.3 (`acct` optional claim, `-AcctClaimOnly`) added | `unified-access-control-r2` task 141 (owner decisions I1 = (b), I2 = (1)); provisioning handoff `projects/unified-access-control-r2/notes/handoffs/INCOMING-141-workforce-tenant-list.md` |
| 2026-10-01 | §6.5.2: the schema prerequisite is BLOCKED pending an owner decision (alternate key vs field-level security on `contact.sprk_externalobjectid` — Dataverse allows only one); the switch also gates the inline licensed-user link. §6.5.3: a flag records every colliding identity (`sprk_identitycollisionparties`); the two hand-cleared exceptions | `unified-access-control-r2` task 141 verifier fix round (`task/uac-r2-141-f1`) |
| 2026-10-01 | §6.5.2: a registration link that does not land in a target environment is NOT retried by this BFF (the job scans only `Dataverse:ServiceUrl`) and how App Insights shows it; the cost of leaving a stamp report-only | `unified-access-control-r2` task 141 second verifier fix round (`task/uac-r2-141-f2`) |
| 2026-10-02 | §6.5.2: the schema prerequisite is UNBLOCKED — owner decision B2: uniqueness on the unsecured mirror `contact.sprk_externalobjectidkey` (key `sprk_ExternalObjectIdUniqueKey`), field-level security stays on the binding; what a mirror squat can and cannot do. The job now reconciles every provisioning target (`DATAVERSE_URL` + active `sprk_dataverseenvironment` rows), so a registration link that does not land IS retried, and each target needs the schema. §6.5.3: clear all three binding columns; the "Key mirror held by another contact" procedure | `unified-access-control-r2` task 141 third fix round (`task/uac-r2-141-f3`; owner round 4 item 4) |
| 2026-10-02 | §6.5.2: second Dataverse prerequisite — `scripts/Set-RecordCreatorPersonSchema.ps1` (`sprk_createdbyperson`, BFF-only, field-secured) must run BEFORE a BFF carrying task 133 is deployed, or Office quick-create and work-assignment creates fail | `unified-access-control-r2` task 133 round b2 (owner round 7 item 2) |
| 2026-10-02 | §7.4: record numbering after H6 — `scripts/Set-RecordNumberingSchema.ps1` in every environment (the autonumber seed is not carried by a solution import; first run numbers blank rows) | `spaarkeai-word-add-in-r1` task 076 (owner decisions 2026-10-02: platform autonumber, interim until the numbering function) |
| 2026-10-05 | §5 H8 row + §7.5: H8's creation record is TYPED (`interStepState.speContainerCreation`) — round 41's gate-evidence record did not survive the Cosmos serializer, so production resumes created a second root container; a recorded type's containers are listed and adopted before a root container is created; container-type / root-container creations with no answer are recorded (type: QuarantineRequired `spe-container-type-creation-in-doubt` until an operator checks; root: waited for). §6.5.4: the SPE admin operator-environment marker — never on a customer stamp | `unified-access-control-r2` task 165, owner round 49 (`task/uac-r2-165-f2-v2`) |
| 2026-10-05 | §6.5.4: a declared marker is bound to its registry entry's App Service / resource group (the deploy fails otherwise); the `demo` stand-up also adds `demo` to `Deploy-BffApi.ps1`'s `-Environment` `ValidateSet` | `unified-access-control-r2` task 165, round 62 (batch-4 integration) |
| 2026-10-05 | §6.5.4: only `dev` carries the marker today — Spaarke's production operator environment (`demo`, declared `false`) adds it in the change that stands it up; the registry declaration must be a JSON boolean and the deploy fails on any other type (`[bool]"false"` is true in PowerShell) | `unified-access-control-r2` task 165, round 57 (`task/uac-r2-165-h`) |

---

*Maintained by `customer-provisioning-orchestration-r1`. Update this guide (do not fork) when handler catalog / DAG / tenancy model / naming convention evolves. All updates should be per-project and cite the driving spec / FR / task ID.*
