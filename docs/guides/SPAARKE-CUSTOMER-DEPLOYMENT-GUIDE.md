# Spaarke Customer Deployment Guide

> **Version**: 1.0 (consolidation baseline)
> **Last Updated**: 2026-10-14
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
[L1 Handlers]  H0, H0.5, H1, H2a, H2b, H3, H4, H4b, H5, H6, H7, H7b, H8, H9, H10, H11, H12a, H12b, H12c, H13, H14
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
| The customer's own subscription ID | `2ff9ee48-...` | **The operator creates it** (prereqs.yaml PRQ-S-00), places it in the `spaarke-customers` management group (PRQ-S-06 — the common customer policy, ADR-027; `scripts/provisioning/Deploy-ManagementGroups.ps1`) and grants the L2 identity **Owner** on it (PRQ-S-04, `infrastructure/bicep/modules/controlplane-subscription-rbac.bicep`). Required at intake for every model; nothing defaults it (T228) |
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
- **Azure subscription vCPU quota** — no longer checked: H0's Compute vCPU check was removed 2026-10-15 (T186 L17) because the stamp deploys no Microsoft.Compute resources and App Service plans draw on their own per-SKU worker quota (no supported API reads it), so a real shortfall surfaces as an H2a deployment error
- **The customer's subscription and Dataverse environment** — created by the operator before the run (PRQ-S-00, PRQ-S-04,
  PRQ-C-09; T228). L2 creates neither — the former environment-creation rate limit no longer applies
- **Management groups + common customer policy** — one-time per tenant (ADR-027, T262): `spaarke-environments` →
  `spaarke-customers` (`infrastructure/bicep/management-groups.bicep`) with `infrastructure/bicep/customer-policy.bicep`
  assigned at `spaarke-customers` (built-in definitions, Audit / DoNotEnforce — it reports, never blocks a deployment).
  `pwsh scripts/provisioning/Deploy-ManagementGroups.ps1` prints the plan; `-Apply` creates, assigns and moves
  (needs Owner — or Contributor + User Access Administrator — at `/`; see the script's REQUIRED ACCESS). Each new
  customer subscription then joins `spaarke-customers` before its first run (PRQ-S-06):
  `az account management-group subscription add --name spaarke-customers --subscription <id>` (management-group write on
  `spaarke-customers` + Owner on the subscription — no tenant-level right)
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

21 idempotent handlers (H0–H14; H14 runs its one sub-step, H14a, in-process) implementing `IJobHandler` per **ADR-004**. Each is a self-contained coarse-grained operation (deploy infra, import solutions, deploy BFF) with:

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
| `ControlPlaneIdentity__PrincipalObjectId` | Object id of L2's own UAMI (one setting since task 249; it replaced `KvSecretsPopulationOptions__ControlPlanePrincipalObjectId`). H4 grants it **Key Vault Secrets Officer** on each customer vault before writing it — never the stamp's BFF UAMI, which only reads its vault. H2a sends it as `customer.bicep`'s `controlPlaneUamiPrincipalId` on **Model 1** stamps (Website Contributor on the stamp BFF). H3 makes it the subject of the customer BFF registration's `spaarke-l2-worker` federated credential (ISS-015) — so H6/H7/H7b can sign in as that registration — and assigns it the keyless-proof role. **Rollout**: deploy `platform-controlplane` (which sets the new name) FIRST, then the Worker code built from task 249 straight away (`Deploy-ControlPlane.ps1` deploys code only) — each Worker version refuses to start without the name it reads, so the Worker is down between the two steps; queued Service Bus messages wait. (The old name only ever existed on the project branch, since T245b.) | H4, H2a, H3 |
| `KvSecretsPopulationOptions__RequireSecretFreeIdentity` | `true` — every new stamp is secret-free: H4 omits `BFF-API-ClientSecret` and `Dataverse-ClientSecret` (BINDING credential-lifecycle rule; no sentinel). The code default is also `true` (T225b, G21). | H4 |
| `EnvVarValues__Credentials__{Order__0,RequireSecretFreeIdentity}`, `SolutionImportOptions__Credentials__{Order__0,RequireSecretFreeIdentity}` | `ManagedIdentityFederated` / `true` — the credential the Worker uses to sign in to a customer's Dataverse environment as that customer's BFF app registration (FR-39 chain, task 205i). Emitted by `platform-controlplane.bicep` when `requireSecretFreeIdentity` is `true`, the default since T252; the Worker then has **no** Key Vault reference to `BFF-API-ClientSecret`, and `Seed-PlatformKeyVault.ps1` never creates it. `false` is a prong-3 opt-in for an unmigrated environment only. | H6, H7, H7b |
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
| `displayName` | H10 (customer business unit), registry `sprk_name` | **Every run (T259, ISS-010 — owner 2026-10-09)**: the customer's full name (the intake file's `displayName`; the skill defaults it to `customerId`). 1–160 characters, no leading/trailing whitespace, no control character, never `Secure Record` in any case (`h10-customer-display-name-required` / `h10-customer-display-name-invalid`). H10 creates the customer's own business unit with this name **directly under the root** — a sibling of the `Secure Record` unit — and puts the BFF's application users in it; H11 puts every guest in it. |
| `identityPreset` | H11 | `B2BGuest` \| `NativeAccount`, exact case (`userprov-missing-identity-preset` / `userprov-invalid-identity-preset`); **a `Model1` run takes only `B2BGuest`** (owner D2, T232 — `userprov-model1-requires-b2b-guest`) |
| `environmentSecurityGroupId` | H11 | **B2BGuest (every Model 1 run)**: object id (GUID) of the environment's security group `sprk-{customerId}-users`, created by the operator and set on the environment (`PRQ-C-10`) — `userprov-missing-security-group-id` / `userprov-invalid-security-group-id` (T232) |
| `usersJson` | H11 | JSON array, 1–500 entries; `NativeAccount`: non-blank `firstName` + `lastName`; `B2BGuest`: `email` (`userprov-missing-users` / `userprov-malformed-users-payload` / `userprov-invalid-user-entry` / `userprov-too-many-users`). Personal data: stored in the L2 run document (owner decision D15); never in git; diagnostics and logs identify users by position / Entra object id. |
| `exchangePolicyScopeGroupId` | H14a, H14m | non-blank (`h14a-missing-policy-scope-group-id`). The mail-enabled security group H14a scopes the stamp identity's Exchange mailbox roles to (only its **direct** members' mailboxes are reachable) — **created by the Exchange admin of the stamp's tenant before the run**, named **`Spaarke-AppAccess-{customerId}`** (prerequisite `PRQ-C-08`; L2 never creates it). H14m adds exactly one member — the customer's shared mailbox (T263); every other member is the customer's access decision. |
| `communicationDefaultMailbox` | H4 → KV `Communication-DefaultMailbox`; H14m | `local@domain.tld` (`intake-communication-default-mailbox-invalid`). **The address of the customer's Spaarke-tenant shared mailbox that H14m creates** (T263): an address in an accepted domain of Spaarke's tenant (e.g. `acme@spaarke.onmicrosoft.com`) that is **not** in use — an existing mailbox there that is not this customer's `sprk-{customerId}-mail` quarantines the run. |

**API surface** (per FR-21):

| Endpoint | Purpose |
|---|---|
| `POST /api/runs` | Initialize a new ProvisioningRun |
| `POST /api/runs/{id}/preflight` | Run H0 quota + naming checks on an open run (409 `run-not-preflightable` on a terminal run — use `/resume` for a Failed one) |
| `GET /api/runs/{id}` | Poll run state |
| `POST /api/runs/{id}/gates/{gateId}/advance` | Re-check a Pending gate now: re-dispatches the gate's verifier handler with a fresh attempt (the reconciler's own re-dispatch reuses the first MessageId, which Service Bus drops for 1 h). It does not force the gate open. 409 unless the run is `WaitingOnGate` and the gate is Pending |
| `POST /api/runs/{id}/resume` | Resume a `Failed` run: re-takes the customer guard, sets it `Running` and dispatches `currentPhase` with a fresh attempt (H0 included). 409 on any other status (a Quarantined run is cleared first) |
| `GET /api/runs/{id}/phases/{phaseId}/logs` | Retrieve phase logs |
| `POST /api/runs/{id}/cancel` | Set the run `Cancelled` and release the customer guard. 409 on Completed/Cancelled/Quarantined. A handler already running finishes, but its write loses the ETag race and nothing further dispatches |
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
     `View-Only Recipients`, plus `-Delegating` assignments for exactly `Application Mail.Read`, `Application Mail.ReadWrite` and
     `Application Mail.Send` (the three H14a grants; task 261 dropped `Application MailboxSettings.Read` — nothing reads
     mailbox settings. An app that still carries a delegating assignment for it is harmless; remove it at the next
     maintenance). The delegating assignments are what limit it: tested
     2026-10-04, it is refused when it tries to grant itself Exchange Full Access, Role Management or Mail Recipients.
3. **Platform parameter** `exchangeAdminAppId` = the app's client id (`platform-controlplane-{env}.bicepparam`).
4. **Graph read for the Worker**: the L2 Worker managed identity must be able to read `GET /organization` in the tenant
   (`Organization.Read.All` or `Directory.Read.All`; the Worker's catalog, `ControlPlaneGraphAppRoles.cs`, carries
   `Directory.Read.All` — task 261). The
   Worker reads the tenant's initial domain (`contoso.onmicrosoft.com`) there and the sidecar connects with
   `-Organization <initial domain>` — the only value Microsoft documents for app-only sign-in. With the tenant id, Exchange
   connects and reads but refuses every write with "doesn't have write permission to target DC" (2026-10-04).
5. **Shared-mailbox roles for H14m (task 263) — owner decision D32 (2026-10-10); applied on Spaarke's tenant 2026-10-13;
   common parameters added to the `New-Mailbox` entry 2026-10-20 (T186 L62)** (M1–M5 and M7 passed 2026-10-13; RBAC
   changes take 30 min – 2 h to propagate). Apply exactly this set on any other tenant; nothing wider.
   H14m creates each customer's shared mailbox and adds it to the customer's scope group. `Spaarke App RBAC Admin` cannot
   (it holds no `New-Mailbox` or `Add-DistributionGroupMember`), and widening it is an owner decision. The narrowest set
   found is two custom roles plus one scope, assigned with `-App` (non-delegating). Run as an Organization Management
   admin; first confirm the parents with `Get-ManagementRoleEntry '*\New-Mailbox' -Parameters Shared` and
   `Get-ManagementRoleEntry '*\Add-DistributionGroupMember' -Parameters BypassSecurityGroupManagerCheck`:
   ```powershell
   New-ManagementRole -Name 'Spaarke Shared Mailbox Create' -Parent 'Mail Recipient Creation' -EnabledCmdlets New-Mailbox
   Set-ManagementRoleEntry 'Spaarke Shared Mailbox Create\New-Mailbox' -Parameters Shared,Name,Alias,DisplayName,PrimarySmtpAddress,ErrorAction,ErrorVariable,WarningAction,WarningVariable,OutVariable,OutBuffer,Confirm,WhatIf
   New-ManagementRole -Name 'Spaarke Scope Group Membership' -Parent 'Security Group Creation and Membership' -EnabledCmdlets Add-DistributionGroupMember
   Set-ManagementRoleEntry 'Spaarke Scope Group Membership\Add-DistributionGroupMember' -Parameters Identity,Member,BypassSecurityGroupManagerCheck
   New-ManagementScope -Name 'Spaarke Customer Scope Groups' -RecipientRestrictionFilter "RecipientTypeDetails -eq 'MailUniversalSecurityGroup' -and Name -like 'Spaarke-AppAccess-*'"
   New-ManagementRoleAssignment -Name 'sprk-exoadmin-sharedmbx-create' -App <app id> -Role 'Spaarke Shared Mailbox Create'
   New-ManagementRoleAssignment -Name 'sprk-exoadmin-scopegroup-member' -App <app id> -Role 'Spaarke Scope Group Membership' -CustomResourceScope 'Spaarke Customer Scope Groups'
   ```
   **Grant the PowerShell common parameters on the `New-Mailbox` entry (T186 L62).** `-Parameters` REPLACES the entry's
   list, and Exchange checks the common parameters the module forwards like any other: without `ErrorAction`, the
   sidecar's `New-Mailbox … -ErrorAction Stop` gets 403 `CmdletAccessDeniedException`, which the module reports only as
   "A server side error has occurred … try again after some time". The `Add-DistributionGroupMember` entry **cannot**
   take them — its parent role holds none for that cmdlet (Exchange refuses the `Set-ManagementRoleEntry`) — so the
   sidecar calls it with no common parameter and reads its errors from the error stream. `Get-Command` in the app's
   session lists every parameter either way, so the sidecar's PRQ-E-16 check calls both writes on an address that
   can never be accepted (`…@authz-probe.invalid`): a validation error means authorized, anything else refused, and
   nothing is created. Then run live checks M1–M8 (`projects/customer-provisioning-orchestration-r1/notes/t263-customer-shared-mailbox.md`
   §5). Without these roles H14m fails Resumable (`h14m-mailbox-ensure-failed`, naming PRQ-E-16) **before** creating
   anything: the sidecar checks both cmdlets with `Get-Command`, then runs the probe above. Never use `-WhatIf` to test
   these roles by hand: Exchange checks it against the role entry like any other parameter, so a 403 then says nothing
   about the call itself.
   Not requested: `Remove-Mailbox` / `Remove-DistributionGroupMember` (decommission is manual in r1 — §7.9) and
   `Set-Mailbox`.

Residual risk, stated plainly: an identity that can create application role assignments can grant any app — itself
included — the four mailbox roles organization-wide (the scope is optional). With step 5 it can also create any
shared mailbox and add any recipient to any `Spaarke-AppAccess-*` group, which would let that customer's stamp reach it;
the code only ever adds `sprk-{customerId}-mail` to its own customer's group. Only the L2 Worker can obtain its token;
audit `New-ManagementRoleAssignment`, `New-Mailbox` and `Add-DistributionGroupMember` in the unified audit log.

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

Columns added by this project (FR-26 / design.md §6.1 — the 12 v3.3 columns, then T225b and T257):

- `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid`, `sprk_provisionedon`
- `sprk_currentrunid` (I5 concurrency serialization)
- `sprk_tenancymodel` (Choice) — `0` = `Model1` (stamp in Spaarke's tenant), `1` = `Model2` (stamp in the customer's tenant). Task 224 renamed the labels from `Model1Shared` / `Model2Dedicated` and kept the values. D-12 §6 had asked for a migration instead of a relabel; no registry row carried a tenancy value when the relabel landed (checked 2026-10-07), so nothing needed migrating
- `sprk_tenantid` (populated from H0.5 or run params)
- `sprk_bffversion`, `sprk_solutionversion`, `sprk_ClientCacheBustToken` (§14A upgrade compat)
- `sprk_credentialmode` (T225b secret-free marker, written by H4)
- `sprk_bffappid` (T257: the customer BFF app registration id; H13 promotes it from H3's output) and `sprk_copilotauthconfigid` (T257: the customer's Copilot agent auth config id; the operator writes it at the post-Ready Copilot gate, §7.12)

All of them are added by `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1` (idempotent; prereq PRQ-E-14 checks them). Run it **before** deploying any control-plane build that carries the T257 H13 (it promotes `sprk_bffappid`); otherwise Dataverse rejects H13's whole promoted-columns PATCH and the registry goes stale.

---

## 5. Handler Catalog (H0 – H14)

Every handler is idempotent, resumable, and has a verified post-condition. Full dependency DAG in `design.md` §4.1.

| # | Handler | Purpose | Gate | Idempotency key |
|---|---|---|---|---|
| **H0** | Preflight + quota checks | Validate run params + **cost envelope** (§3.2a — `tier` + `estimatedMonthlyUsd`, every model; `quota-cost-envelope-*` / `quota-cost-overrun`, no override) + Azure OpenAI TPM headroom + OpenAI pin freshness + SPE owner check (`SpeOwnerCredential`: the Worker has an owner entry for the run's container type, signs in as that owning app through its federated credential, and GETs the container type's registration). Resumable rejections: `spe-owner-not-configured` (no `containerTypeId` on the run, or no owner entry for it), `spe-owner-token-failed` (FIC token exchange failed, or Graph refused the owning-app token with 401/403 — e.g. missing consent), `spe-container-type-not-registered` (registration GET 404). No 24 h age gate | Quota headroom sufficient for +1 provision | `preflight-{customerId}-{paramHash}` |
| **H0.5** | Consent-capture callback | (Model 2 only) Anonymous HMAC-verified `POST /api/onboarding/consent-callback`; captures customer admin `tid`; kicks pipeline | Re-consent semantics: no-op if run exists Ready/Running; restart from H0 if Failed/Cancelled | `consent-{customerId}-{tid}` |
| **H1** | Subscription readiness | ARM verification the customer's subscription is reachable **in the run's tenant** and holds **no other customer's** `rg-spaarke-*` resource group (T228, ADR-027 — checked before H1 registers resource providers); Lighthouse delegation (`CustomerOwned` only) | `subready-subscription-not-dedicated` / `-unreachable` are Resumable, nothing written | `subready-{customerId}` |
| **H2a** | Per-customer Bicep infra | Deploy the CI-published `customer.bicep` ARM template: RG, KV, Storage, Service Bus, Cosmos, Redis (per customer since D-12; Azure Managed Redis with access keys disabled since T242 — the BFF connects with the stamp UAMI via `Redis__Endpoint`), OpenAI, AI Search, Doc Intelligence, Content Safety (T246), App Insights + Log Analytics, optional SignalR — all keyless (T244: local auth / shared key disabled; L2 gets Search Service Contributor + Search Index Data Reader on the stamp search service). Structural checks (pinned model versions, no `SystemAssigned` KV-reference identity) run on the same template bytes | — | `infra-{customerId}-{bicepVer}` — `bicepVer` = content version of the deployed template |
| **H2b** | AI Search indexes | Provision the 7 canonical indexes (`files`, `discovery`, `records`, `rag-references`, `insights`, `session-files`, `invoices`) on the stamp's own AI Search service via the SDK (`SearchIndexClientProvisioner`, L2 identity), then verify them — same path for both models (T225b) | — | `aisearch-{customerId}-{indexVer}` — `indexVer` = content version of the schema set applied |
| **H3** | Entra app registration | 🔴 **One BFF app-reg PER CUSTOMER, both models (D-13, BINDING)** — delegated permissions per `EntraAppRegPermissionCatalog` (H10 grants the application roles in `GraphAppRoles.cs`); sign-in audience `AzureADMultipleOrgs` (enables Model 2 consent). Exposes the application role `Provisioning.KeylessProof` and assigns it to the L2 Worker identity (T230b; Model 1 — a Model 2 app-reg is in the customer's tenant, where L2 has no service principal). **Client access (T240a):** sets the SPA redirect to exactly the customer's Dataverse origin (from intake `dataverseEnvUrl`; the code pages sign in through this registration) and pre-authorizes exactly the platform's shared clients on `user_impersonation` (Worker setting `EntraAppRegOptions__PreAuthorizedClientAppIds__N`; today the production Office add-in `1958aec2…` — never the dev client `c1258e2d…`, §7.3). The FIC issuer for Model 1 is `EntraAppRegOptions__SpaarkeTenantId` (set by the Worker Bicep). **Two FICs (ISS-015):** `spaarke-uami-trust` (stamp BFF UAMI) and `spaarke-l2-worker` (the L2 Worker UAMI's principalId, `ControlPlaneIdentity__PrincipalObjectId`) — the second is how H6/H7/H7b sign in as the registration secret-free. **`acct` optional claim (T255):** on the access tokens of every new and existing registration, read back (`appreg-acct-claim-failed`) — the BFF's workforce member test fails closed without it. **`removeUnverifiedEmailClaim` (T264):** sets `authenticationBehaviors.removeUnverifiedEmailClaim = true` on every new and existing registration (other `authenticationBehaviors` values preserved), read back (`appreg-unverified-email-claim-failed`) — R3's email-bound contact registration is safe only when Entra strips an unverified-domain `email` claim (§7.3). ✅ Implemented by T222 (2026-09-29): the former `Model1Shared` branch is deleted; H3 creates one registration per customer, unconditionally. | Admin consent granted (Graph query) | `appreg-{customerId}-{tenantId}` |
| **H4** | Key Vault secrets | Grant L2's own principal Secrets Officer on the customer vault; populate KV secrets per canonical catalog manifest; re-assert `keyVaultReferenceIdentity` = UAMI on both slots (idempotent PATCH — `customer.bicep` already sets it and H2a verifies **T1**, T186 L21) | — | `kv-{customerId}-{secretsVer}` — `secretsVer` = content version of the manifest |
| **H4b** | BFF app settings | Writes every BFF app setting from the canonical secret-catalog manifest — each secret's `app_settings` as a Key Vault reference to the stamp vault, plus the `per_env_settings` values (intake values and H2a/H3/H5/H8/H10 outputs) — to the **production site and the `staging` slot** through the ARM SDK (T253: no pwsh — the Worker host has none). **Merge, never replace**: settings already on a slot that H4b does not set stay (an optional `required: false` entry the run does not carry, e.g. `AiSpendLimit__MonthlyLimitUsd`, is not written — T254); one write per slot, none when a slot already matches. Exactly the settings `Configure-AppServiceSettings.generated.ps1` writes (parity test). Then polls `/healthz` (~8 min budget) and, on timeout, names the failing IOptions module from the container log | `/healthz` = 200; a timeout is QuarantineRequired `h4b-healthz-timeout` | `appsettings-{environmentName}-{secretsVer}` |
| **H5** | Dataverse env **adoption** | Adopts the environment the **operator created** (intake `dataverseEnvUrl`; PRQ-C-09) — never creates one (T228). Checks the URL is this customer's (`spaarke-{customerId}[-{environmentName}]`, the rule POST /api/runs applies) and that `GET /WhoAmI` answers for the L2 Worker identity | `InterStepState.DataverseEnvUrl` = the canonical URL; `env-url-invalid` / `worker-not-app-user` / `env-health-check-failed` are Resumable | `dvenv-{customerId}` |
| **H6** | Solution package import | **One solution, `SpaarkeMaster`** — **managed by default, unmanaged only on explicit instruction** (intake `solutionPackageType`; ADR-027 §3 amended 2026-10-07, owner D8). Refuses a managed↔unmanaged switch and a downgrade. Before the import it applies the org-settings contract (`organization.maxuploadfilesize` ≥ 25 MB — F14; `isauditenabled` = true and `auditretentionperiodv2` = -1, audit kept Forever — #2027, owner decision 2026-10-18) with one Dataverse Web API PATCH as its importer identity, then reads the row back and fails the step on any setting that did not take (a PATCH of an ignored column still returns 204) (T253: no pac — the Worker host has none; `org-settings-contract-failed` is Resumable). It installs **no** application: the CI-built package needs none a fresh environment lacks (F13's Power BI dependency is gone; `SpaarkeMasterApplicationDependencyTests`). Runbook: [`SPAARKE-SOLUTION-RELEASE-PROCESS.md`](../procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md). ⚠️ *A real run fails here until the package is published in the one-package manifest format (T218c–e).* | SpaarkeMaster present at the package version, `ismanaged` = requested type | `solimport-{customerId}-{solutionVer}` |
| **H7** | Dataverse env-var values | Set 7 per-customer env vars per §10.3 (`sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`, `sprk_AzureOpenAiEndpoint`, `sprk_ShareLinkBaseUrl`, `sprk_SharePointEmbeddedContainerId`). First links the environment's **root business unit** to H8's container (`businessunit.sprk_containerid` — unified-access-control-r2 task 076's non-secure default); a root unit already naming another container → Resumable `root-business-unit-container-conflict` naming both, never overwritten (task 227g). **T259:** then links the **customer's own unit** (H10's `customerBusinessUnitId`) to the same container with the same rules (`customer-business-unit-unresolved` / `customer-business-unit-container-conflict`) — every user owns its records there, and the BFF resolves a non-secure record's container from its owning unit | Client startup validates no hardcoded URL fallbacks; the root unit's container reads back | `envvars-{customerId}-{configVer}` |
| **H7b** | Secure Record setup (T256 — unified-access-control-r2 INCOMING-145) | Read-then-write, as the BFF app registration (H6/H7's identity). **Checks** `sprk_noaccessentry` is readable with the BFF deny-list reader's columns (else Resumable `secure_setup.noaccessentry_missing` — the BFF is never deployed: H9 waits for H7b), `sharetopreviousowneronassign` is off, and the root unit's default team reaches no secure table at Deep/Global. **Creates what is missing**: the `Secure Record` unit (a direct child of the root, no users ever), the named memberless Owner team `Secure Record Owners`, and the `Secure Record Owner` role **inside that unit** (a child-unit role cannot ship in SpaarkeMaster) with exactly the Read-at-Basic set of `config/secure-record-owner-role.json` (metadata names, then the §5.4 strip of what Dataverse injects, every time). Puts the role on the named team only; removes it and System Administrator from the unit's default team. Adds every default team to `Spaarke BFF-Managed Field Readers` and `Spaarke Identity Link Readers`, and H10's two application users (the BFF's) to `Spaarke BFF-Managed Field Writers` and `Spaarke Identity Link Writers` — nobody else (profiles, the `sprk_issecure` lock and the contact identity-binding lock on `contact.sprk_externalobjectid` / `systemuser.sprk_primarycontact` ship in SpaarkeMaster — verified, never created; T255 / INCOMING-141); sets NULL `sprk_issecure` to false. Refusals that need an owner decision (users in the unit, team members, wrong parent, a stray member of either writer profile, another profile that may write a locked column) quarantine. Dry run: intake `secureRecordSetupDryRun=true` writes nothing, records the plan in gate `h7b-secure-setup-plan` and stops the run | **T259 (ISS-010, §6 T1/T3):** also refuses, QuarantineRequired, a customer business unit (H10's `customerBusinessUnitId`) that is missing (`secure_setup.customer_bu_missing`) or not a direct child of the root (`secure_setup.customer_bu_wrong_parent`), and a BFF application user outside it (`secure_setup.app_user_outside_customer_bu`); any user of any kind in the `Secure Record` unit is `secure_setup.bu_has_users` | Re-read: role privileges = the file exactly (all Basic), role held by the named team alone, no members/users, all four profiles' memberships exact, no NULL flag | `secure-setup-{customerId}-{setHash}` |
| **H8** | SPE container | Creates ONE customer container in the pre-existing container type, then activates and verifies it, app-only as that type's **owning app** (signed in through the Worker UAMI's federated credential on the owning app named in `SpeContainerOptions:ContainerTypeOwners` — never the customer BFF app or the BFF's UAMI; **T6** trap — delegated 403s). **Binds the container to the new environment's ROOT business unit** (custom property `spaarkeBusinessUnitId`, read back, container removed if it did not land — unified-access-control-r2 task 165, owner round 35 item 1), so it needs **H5**. Before creating, grants the customer's BFF identities on the container-type registration (stamp UAMI application `full`, BFF app delegated `full` — task 227b). **Records what it created at once and RESUMES with it** (rounds 41 + 49): a recorded container is never created again, and one whose activation failed is re-activated. A create whose answer was lost is QuarantineRequired `spe-container-creation-in-doubt`: never repeated, never auto-adopted (§7.5). **A later run reuses the customer's existing container** — the one the environment records in `sprk_SharePointEmbeddedContainerId` — and never removes it; the run's record and the environment naming different containers stops the run naming both (task 227e). After the bind it writes the `spaarkeCustomerId` marker the BFF recognises its containers by (T227d) | Container GET succeeds; stamp reads back; container ID handed to H7 only once bound | `spe-{customerId}` |
| **H9** | BFF deploy | CI-published artifact (`latest.json` manifest) → scheduled-jobs slot guard on the staging slot (`Scheduling__RunScheduledJobs=false`, slot-sticky — ADR-036 A1 rule 2) → Kudu zip-deploy to staging → slot swap; hardened `Deploy-Release.ps1` Phase 4 scanned for a `spaarkedev1` hardcode | `/health` = 200; slot-swap smoke test produces no cold-start KV-ref failures | `bff-{customerId}-{buildId}` |
| **H10** | Dataverse App User + Graph app-role parity | Runs right after H3 + H5 and **before H6** (T228 — H6 and H7 sign in as the BFF app registration it makes an application user). **T259 (ISS-010):** first finds or creates the customer's own business unit (intake `displayName`) **directly under the root**, a sibling of the `Secure Record` unit (another parent → QuarantineRequired `h10-customer-bu-wrong-parent`; two of that name → `h10-customer-bu-ambiguous`), and records it as `customerBusinessUnitId`. Register 2 App Users (BFF app-reg + UAMI) **in that unit** as System Administrator (the unit's copy of the role); an App User already in another unit is QuarantineRequired `h10-app-user-in-foreign-business-unit` — never moved (a unit change strips its roles); reconcile the stamp identity's Graph app roles to `GraphAppRoles.cs` — grant `FileStorageContainer.Selected`, remove every other Graph role (**T3**, task 261) | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 (**T2**) | `appuser-{customerId}` |
| **H11** | User provisioning | `B2BGuest` (every Model 1 run): checks the environment's security group (`sprk-{customerId}-users`) and guest access, invites each user (an existing guest is reused — no second email), then adds each redeemed guest to the group and makes it a Dataverse user **in the customer's business unit** (H10's `customerBusinessUnitId`; T259 — never the root, never `Secure Record`) with that unit's copy of the Spaarke role. `NativeAccount` (Model 2): creates + licenses users | B2B: consent-verification gate | `users-{customerId}` |
| **H12a** | AI seed chain | type-lookups → actions → tools → knowledge → skills → playbooks → output-types → playbook consumers (single AI routing surface per **ADR-039**) | All seed rows present, no dupes | `aiseed-{customerId}-{seedVer}` |
| **H12b** | App-config seed | DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions (DAG-parallel with H12a) | Config records seeded per manifest | `configseed-{customerId}-{configSeedVer}` |
| **H12c** | Runtime references | `sprk_aimodeldeployment` rows point at the customer's **own dedicated** OpenAI deployment — both models (D-12 §3) | Endpoint resolves via env-var + join | `runtimerefs-{customerId}-{modelVer}` |
| **H13** | E2E acceptance gate | Pure C# in the Worker (ARM, Graph, Dataverse, AI Search, Cost Management calls — no script) — verifies `/health`, `/ping`, the Dataverse CORS origin, **the keyless proof** (one managed-identity call per stamp service, made by the BFF — any refusal fails; T230b) and **ARM keyless** (key auth off, no key setting on any slot), **all 7 T1–T7 traps cleared**, **I2–I5 invariants sample-verified on the stamp** (I1 + naming are CI gates — T230a), cost envelope (one $400/month envelope for both models, §3.2), **the BFF's secure-record isolation census answers `isolated`** (T260) | `Setup Status = Ready` only if H13 exits 0 | `validate-{customerId}-{buildId}` — `buildId` = the build H9 deployed |
| **H14** | Post-deploy integrations | (a) Exchange mailbox access: the stamp UAMI gets the 4 `Application Mail.*` roles scoped to the customer's group (RBAC for Applications — **T4**). One sub-step (H14b Graph webhook subscriptions and H14c Dataverse service-endpoint webhooks were removed — §7.9) | H13 T4: every role held in scope, none outside | `integrations-{customerId}-{integrationVer}` |

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
H4b  <- H4, H3, H5, H8, H10 H6   <- H5, H3, H10        H8   <- H3, H5
H7b  <- H6                 H9   <- H3, H4b, H6, H7b   H7   <- H6, H8, H9
H10  <- H3, H5             H11  <- H10, H7            H12a <- H11
H12b <- H11                H12c <- H12a, H12b, H2a    H14  <- H12c
H13  <- H14, H7b
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
- `DocumentPointer__ItemIdBoundBackfillComplete` — **leave unset (false) at deployment** (task 171, owner round 72
  item 1). The BFF refuses any document whose `sprk_graphitemid` differs from its field-secured copy
  `sprk_graphitemidbound` (only the BFF writes the copy; `sprk_graphitemid` cannot be secured because it is in an
  alternate key). While unset, a row with an EMPTY copy falls back to the rule in force; set it to `true` only after
  `scripts/Invoke-DocumentItemIdBoundBackfill.ps1 -Verify` passes — from then on a row with no copy is refused.
  🔴 The column must exist and be field-secured (`Set-DocumentRelocationSchema.ps1 -Apply`, then
  `Set-DocumentPointerFieldSecurity.ps1 -Apply`) on an environment **before any BFF build containing it can reach that
  environment** — for the shared dev environment that means before the change merges to master (other projects deploy
  master there). Without it every pointer WRITE fails (uploads, Office saves, email archives, Compose saves,
  relocations) and every pointer-check READ is refused 409 `document_storage_unverified` on all content routes.
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
- `ExternalAccess__ImpersonatedRootSets__Enabled` — **leave unset (false) at deployment** (unified-access-control-r2
  task 036, FR-20). Off, an internal user's (systemuser's) access on the SPA and Teams comes from the BFF's membership
  rules, exactly as before the setting existed. On, Dataverse decides it: the user sees what Dataverse lets them read, and
  every write is checked against their own Dataverse rights on the record. Turn it on only through
  [§12.8](#128-fr-20--let-dataverse-decide-internal-users-spateams-access), per environment, never in a checked-in
  settings file (`ImpersonationNegativeCanaryTests` fails the build unless every checked-in value is literally `false`).
- `PowerBi__AllowedWorkspaces__{n}__WorkspaceId` (+ optional `__CustomerBusinessUnitId`) when the Reporting module is
  enabled — the workspaces the catalog may act on; empty refuses every report ([reporting-admin.md](reporting-admin.md#environment-variables)).
- `ExternalAccess__CustomerBusinessUnitId` — **REQUIRED on every stamp** ([§6.5.5](#655-customer-contact-owner-externalaccess__customerbusinessunitid--required-per-stamp),
  ISS-005 / #1673). The customer's own business unit: a child of the root with exactly one default Owner team, never the Secure
  Record business unit. When it is absent the BFF creates no customer contact and registration and first sign-in answer 500
  `sdap.access.registration_owner_unconfigured`.

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
| **Emitted by** | `infrastructure/bicep/customer.bicep` (production site only), from the `customerId` it already holds; and **H4b** writes it to **both** slots from the run's customerId, verbatim (manifest `per_env_settings` source `from-intake-parameter:customer_id`, written through the ARM SDK since T253; customer-provisioning-orchestration-r1 T238) — so a staging → production swap cannot drop it. **No operator action** on a provisioned stamp |
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
| **Empty / absent** | **DENY** — nobody is ever email-bound or gets a contact created; every contact-only Type-2 sign-in gets `sdap.access.deny.workforce_tenant_list_empty`, existing oid bindings included (#2128 round 1). Enabled systemusers are unaffected. |
| **Never** | a fallback to `AzureAd:TenantId`, or `TenantRouting:Tenants[]` |
| **Startup check** | a non-GUID, the all-zero GUID, or the CIAM tenant id **fails startup** (`ValidateOnStart`) |
| **Written by** | provisioning — **H4b**, from intake `customerWorkforceTenantIds`, on both slots (T255; hand-off `projects/unified-access-control-r2/notes/handoffs/INCOMING-141-workforce-tenant-list.md`) |
| **Verified by** | **H13 trap T7** — both slots carry exactly the run's list (no missing, extra or stale index; on Model 1 none equal to `AzureAd__TenantId`) |

**How provisioning writes it (T255).** The operator gives the CUSTOMER's tenant id(s) at intake as the array
`customerWorkforceTenantIds` (required for every run; prerequisite `PRQ-C-13`). `POST /api/runs` applies one rule
(`CustomerWorkforceTenantsRule`) and refuses, before anything is created: a missing value (`workforce-tenants-required`);
not a JSON array of 1–10 distinct non-zero GUIDs (`workforce-tenants-invalid`); the CIAM tenant (`workforce-tenants-ciam-tenant`
— the BFF would not start); Spaarke's own tenant, or on Model 1 the run's `tenantId` (`workforce-tenants-spaarke-tenant`
— it would bind Spaarke's staff into the customer's environment). The control plane knows those tenants from its own
settings `ReservedTenants__SpaarkeTenantId` (the deployment tenant) and `ReservedTenants__CiamTenantIds__N`
(`platform-controlplane.bicep` `ciamTenantIds`; both hosts refuse to start without them). H4b re-applies the rule, writes
`WorkforceIdentity__CustomerTenantIds__0 … __{n-1}` on the production site and the `staging` slot, and **removes any other
`WorkforceIdentity__CustomerTenantIds__*` setting** there (a tenant dropped from the list must not linger). To change
the list of a running stamp, start a new run (upgrade) with the new list — never `az webapp config appsettings set` by
hand (H13 T7 fails a stamp whose slots differ from its run). Never `AzureAd__TenantId`, never `TenantRouting:Tenants[]`.

🔴 **Model 1 is the case that makes this a separate setting.** In Model 1 the per-customer BFF app registration
lives in **Spaarke's** tenant (D-13), so `AzureAd:TenantId` is Spaarke's tenant. Keying the member test on
`AzureAd:TenantId` would refuse every Model-1 Type-2 employee **and** auto-bind Spaarke's own staff into the
customer's environment. Set the CUSTOMER's tenant here. In Model 2 the registration lives in the customer's tenant
and the two values coincide — list it anyway; nothing is inferred.

##### Model 1: why a customer has a "home tenant" at all (read this before asking again)

> Recorded 2026-10-11 at the owner's request (`spaarke-SPA-external-access-platform-r3`), verified against
> `WorkforceIdentityOptions.cs` `WorkforceMembershipTest.Evaluate` / `IsRegisteredGuest`.

Model 1 puts **Spaarke's side** (the stamp, its BFF app registration, its Dataverse environment) in Spaarke's
tenant. It does **not** move the customer's people. A customer's employees keep their own company work accounts
in **their company's own Entra tenant** — the customer's **home tenant** (e.g. Contoso's tenant). They sign in at
that **home directory** and self-register (ADR-028 A9). A B2B guest account in Spaarke's tenant with no enabled
systemuser is refused `sdap.access.deny.workforce_guest_use_home_signin` (#2128 retired the R3 FR-15 guest admission).
An enabled systemuser is resolved first and admitted, a licensed B2B guest included (item 1 below).

| Where the home tenant appears | What it is |
|---|---|
| A B2B guest's token from the stamp (Spaarke-pinned authority, ADR-028 A8; proven live 2026-10-08). Since #2128 it is refused when the guest has no enabled systemuser (the contact-only branch), and the `idp` home tenant only selects the deny code. A guest with an enabled systemuser (a licensed guest) is admitted by the systemuser branch, which runs first | `tid` = **Spaarke's** tenant, `acct = 1` (guest), `idp` = `https://sts.windows.net/{home tenant id}/` |
| `WorkforceIdentity__CustomerTenantIds__N` (this setting) | The customer's home tenant id(s): "whose employees count as this customer's workforce" |
| The Spaarke registry, next to `sprk_securitygroupid` (R3 task 060 for current customers; provisioning intake for new ones) | The same list per customer key, meant for the R3 FR-14 workforce self-registration service. That service is superseded by A9 (item 2 below): the A9 route and the WorkforceHome scheme read this App Service setting, not the registry (`AuthorizationModule.cs:587`) |

It is checked in two places:

1. **Every request to the customer's BFF** on the Workforce token plane **from a caller with no enabled systemuser**
   (the contact-only branch). The resolver first looks the caller's `oid` up as a systemuser
   (`WorkforcePrincipalResolver.cs:190`): an enabled systemuser is resolved and admitted there, before any member test
   — a licensed B2B guest and a Model 1 Spaarke staff member included (`WorkforcePrincipalResolver.cs:207-251`); a
   disabled one is denied (`:257`). Only the contact-only branch runs `WorkforceMembershipTest`, on every sign-in and
   before any contact lookup — an existing binding does not override it (`ContactIdentityBinder.ResolveWorkforceCallerAsync`,
   `ContactIdentityBinder.cs:337-345`). On that
   branch only a direct member of a listed tenant (`tid` ∈ list, `acct = 0`) passes: the Model 2 / dev path. No guest
   is admitted on that branch (ADR-028 A9): customer workforce users sign in at their home directory and register (2.
   below). A Spaarke-tenant B2B guest whose home tenant (from `idp`) is listed still answers `Member` from the member
   test, and the binder refuses it `sdap.access.deny.workforce_guest_use_home_signin` before any store call
   (`ContactIdentityBinder.cs:313-328`; #2128 retired the R3 FR-15 guest admission). A guest from any other home tenant
   gets its member-test deny: `sdap.access.deny.workforce_tenant_not_customer`, or `sdap.access.deny.workforce_guest`
   where the stamp lists its own tenant (dev).
2. ~~**Workforce self-registration** (R3 FR-14, task 060)~~ — **superseded by ADR-028 A9.** FR-14 invited the person
   into Spaarke's tenant as a B2B guest, which is exactly the identity item 1 now refuses. Registration is the A9 route
   `POST /api/v1/external/registration` (`ExternalRegistrationEndpoints.cs:78`). The person signs in at their home
   directory; the WorkforceHome scheme accepts that token only if its `tid` ∈ the customer's list **and** `acct = 0`
   (a member there, not someone else's guest; `AuthorizationModule.cs:673-700`). The route answers 200 with no write
   for a caller who already resolves; otherwise, for the token's email, it refuses a licensed user's email
   (`licensed_user_link_required`), answers 409 `existing_record_found` for one bindable existing contact and binds it
   on the confirmed resubmit, refuses contacts it cannot bind (`registration_contact_conflict`), or creates one
   WorkforceHome contact (`ContactIdentityBinder.RegisterHomeContactAsync`, `ContactIdentityBinder.cs:686`). Nothing
   is invited into Spaarke's tenant.

**Without the list**, anyone with any Microsoft work account could register as that customer's staff, and a guest
from another company invited for an unrelated reason would be bound into the customer's environment.

**Not to be confused with:** `AzureAd:TenantId` (the stamp's own tenant = Spaarke's on Model 1), the CIAM tenant
(partners with no company Entra account; never valid here — startup refuses it), or `TenantRouting:Tenants[]`.

Outside a provisioning run (Spaarke's own environments — dev), the same setting by hand, on BOTH slots:

```bash
az webapp config appsettings set --resource-group <rg> --name <app-service-name> \
  --settings WorkforceIdentity__CustomerTenantIds__0=<customer-tenant-guid>
az webapp config appsettings set --resource-group <rg> --name <app-service-name> --slot staging \
  --settings WorkforceIdentity__CustomerTenantIds__0=<customer-tenant-guid>
```

**The member test also needs the `acct` claim** (§7.3): a member is a user token (`CallerIdentity`, never an
app-only token) whose `tid` is listed here AND whose `acct` claim is `0`. For a Model 1 B2B guest (`acct = 1`,
`tid` = stamp tenant), `WorkforceMembershipTest.Evaluate` answers `Member` when its `idp` home tenant is listed here
(the FR-15 conjunction) and `ForeignTenant` otherwise (`WorkforceIdentityOptions.cs:329`; where the stamp lists its own
tenant, `Guest`, `:340`). With no enabled systemuser neither is admitted: the binder refuses the `Member` guest
`sdap.access.deny.workforce_guest_use_home_signin` and that person signs in at their home directory instead (ADR-028
A9); the others get their member-test deny. A token
with no `acct` claim fails closed (`sdap.access.deny.workforce_acct_claim_missing`) — membership is never inferred
from an email domain or a `#EXT#` UPN.

**The identity-link reconciliation job** (`identity-link-reconciliation`, every 5 minutes) links every licensed
user to their contact. It runs **report-only** until `IdentityLink__Reconciliation__WritesEnabled=true` — absent,
empty or unparseable writes nothing. Review one report-only run (App Insights `[ID-LINK-RECON] before-state`
lines and the run's ResultJson) before enabling writes on a new stamp.

**Provisioning does not set this switch (T255 decision).** A new stamp starts report-only by design, and H4b MERGES
its settings — a run that wrote `false` would silently turn writes off again on a stamp the operator had already
switched on. After H13 passes: wait for one report-only cycle, review it, then set
`IdentityLink__Reconciliation__WritesEnabled=true` on **both** slots (the same two `az webapp config appsettings set`
commands as above). Record the date in the run's handoff report.

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

**A new stamp gets it from the package (T255).** The columns, the two global choices, the field security on the
binding and on `systemuser.sprk_primarycontact`, both identity-link profiles with their permissions and the collisions
view ship in SpaarkeMaster, which H6 imports before H9 deploys the BFF (no shell tool). The package rule includes the
alternate key on the mirror (`sprk_ExternalObjectIdUniqueKey`, an OOB-table key) since T255; until the next release
export brings it into git, the committed package lacks it (`ContactIdentityBindingSchemaPackagedTests` pins the gap).
The profile MEMBERSHIPS (readers = every business unit's default team, writers = the BFF's application users) are per
environment — no package carries them: **H7b adds them on every run** (S15–S18, as it does the BFF-managed profiles'),
verifies the shipped lock, and quarantines a writer-profile member that is not one of the BFF's application users or
another profile that may write either column (`secure_setup.identity_link_*`). On a stamp, a business unit created
later joins the reader profile when H7b runs again. The script below remains the tool for an EXISTING environment (it also copies
existing bindings into the mirror and backfills the plane, which a new environment does not need).

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

**Dataverse prerequisite — the child tables: `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify` must exit 0
BEFORE deploying a BFF that stamps a NEW table** (one added to `RecordCreatorPerson.StampedTables` or
`StampedChildTables`). Since #2273 the BFF trusts `sprk_createdbyperson` as "who created this row" ONLY on those
tables — the secure-exit creator check and people-targeting both read it — and that trust holds only while the
column there is field-secured so that only the BFF can write it. On an unsecured column any user with Write can
name themselves as creator and move the row out of secure isolation. Run it with the same `-BffApplicationIds` and
the stamp's `-OntologyWriterApplicationId` — dry run, then `-Apply`, then `-Verify` (must exit 0). In source, the
ArchTest `CreatorPersonColumnSecuredGuardTests` fails a build where a stamped table's `Entity.xml` does not declare
the column `<IsSecured>1</IsSecured>`; it cannot see the environment, so the `-Verify` run is the deploy gate.

**Dataverse prerequisite — contact access-level and identity bookkeeping columns are field-secured.** Only the BFF and
Standing Grant Administrators write them; every user keeps reading the ones a person needs to see.
`scripts/Set-ExternalAccessContactSecuritySchema.ps1` secures `sprk_accesspermissiongrant`, `sprk_identityplane` and the
five `sprk_identitycollision*` columns, and creates three BFF-only columns secured at creation:
`sprk_externalinvitedemailhash`, `sprk_externalinvitedtenantid` and `sprk_externalaccessremovedon`.

| Column | Read | Create / update |
|---|---|---|
| `sprk_accesspermissiongrant` | every user (BFF-Managed Field Readers) | Standing Grant Administrators (create, update); the BFF (update) |
| `sprk_identityplane`, `sprk_identitycollision*` | every user (Identity Link Readers) | the BFF (Identity Link Writers) |
| `sprk_externalaccessremovedon` | every user (BFF-Managed Field Readers) | the BFF (BFF-Managed Field Writers) |
| `sprk_externalinvitedemailhash`, `sprk_externalinvitedtenantid` | the BFF only | the BFF only |

No other profile (System Administrator excepted) may read, create or update these columns. Run it with the same
`-BffApplicationIds` as above — dry run, then `-Apply`, then `-Verify`, which checks every column is secured and every
profile x column pair, and **must exit 0** before a BFF that writes the new columns is deployed. `-Verify` also checks
that each BFF application user is a direct member of Standing Grant Administrators, which H7b adds (§7.11), so run it
after H7b completes. In an environment no provisioning run reaches (for example spaarkedev1), the operator adds each
BFF application user to Standing Grant Administrators directly, with the owner's OK — the same association H7b's S20
writes: `POST {org}/api/data/v9.2/fieldsecurityprofiles({Standing Grant Administrators id})/systemuserprofiles_association/$ref`
with body `{"@odata.id": "{org}/api/data/v9.2/systemusers({BFF application user id})"}` — then runs `-Verify`. A new stamp receives
the columns, the field security and the profile permissions through the SpaarkeMaster import (H6) — that path applies
only once SpaarkeMaster 1.2.4.0 is published (1.2.3.1 and earlier do not secure these columns; until then run the
script on a new stamp too). H7b's identity-link check (S18) covers the plane and collision columns, and its
standing-grant check (S19) the access level (§7.11). The BFF trusts an EMPTY value of `sprk_accesspermissiongrant`, the
binding, the plane or a collision column only after it has proven it can read the column (the System Administrator role,
matched by its role template id, or a field security profile granting Read, directly or through a team; re-proven every
10 minutes). Until then the standing grant confers no access, no contact is bound or created, no collision flag is
written over, and a revoke of a grantee with no email and an empty
binding is reported as not cleared. `-Verify` therefore also runs the proof's reads (systemuser, role, systemuserroles,
fieldpermission, systemuserprofiles, teamprofiles, teammembership) as each BFF application user; the operator running
it needs the act-on-behalf-of-another-user privilege.

```powershell
.\scripts\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>]            # dry run: read-only
.\scripts\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Apply
.\scripts\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Verify   # must exit 0
```

**Rollout order — schema first, on every environment.** Apply this script and pass `-Verify` (exit 0: the two stamp
columns, their field security, and the proof reads) BEFORE deploying a BFF build that requires the invite stamp. An
environment without the stamp columns refuses EVERY email bind, on the external (CIAM) plane as well as the Workforce
plane and WorkforceHome registration, with `contact_not_invited`: the stamp is read in the same query as the invite
marker, so a column that is not provisioned leaves no stamp to meet, and no bind is made without one. Nothing is bound
or created while the columns are missing; contacts already bound by oid are not affected.

**Rollout — an email bind needs a current invite stamp.** From the release that writes these two columns, every invite
(`/invite` and `/invite-and-grant`) records the invited address (hashed) and, when the inviter names it, the person's
Entra tenant id (`invitedTenantId`, a GUID; anything else is refused with 400). An unbound contact is bound to a sign-in
by its email only when that stamp is current (the invite marker's 30-day window), records the sign-in's email and the
contact's current `emailaddress1`, and — for a work account (the Workforce plane and WorkforceHome registration) —
names the sign-in's tenant, unless the token carries `xms_edov` as boolean `true`. A successful bind clears the stamp.
Otherwise the sign-in is refused with `contact_not_invited` ("invite again"); nothing is bound or created. Contacts
already bound by oid are not affected. Plan for:

- **Workforce contacts never invited through the BFF, or invited before this release, need a re-invite that names
  their tenant** (unless their tokens carry `xms_edov`). Their first work-account sign-in is refused until then.
- A pending invite from before this release has no stamp, so a grant to that contact sends a new invite instead of
  answering "already pending".
- The systemuser plane (licensed users) is unchanged: no stamp is needed there.

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
   A person is removed by an administrator's **Remove person** (the contact stays as it is: its access is revoked and
   it is marked removed). An oid on an inactive contact is also denied and no replacement is created; with the
   removal sweep's write switch on, the reconciliation job marks such a contact removed and revokes its access the
   same way. **Reinstate** applies only to an active contact: reactivate the contact first, then reinstate. It also
   applies only once the removal has finished: while the person still holds an active grant, Reinstate is refused
   (`sdap.access.person.grants_pending_revoke`); run **Remove person** again first, then reinstate.
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

- **Customer environments never carry it** — no customer stamp (Model 1 or Model 2) and no per-customer setting set. Customer
  provisioning (the L2 control plane, the canonical app-settings catalog, `customer.bicep`) does not emit
  it, and since task 249 removed the Model 2 stack no Bicep template names it at all — the marker reaches an App Service
  only through `Deploy-BffApi.ps1`. `SpeAdminOperatorEnvironmentMarkerGuardTests` fails the build if any template names it.
- **Missing = false = refused** (fail closed); a value that is not a boolean stops the BFF at startup.
- **Spaarke-operated environments** declare `"speAdminPlatformOperatorEnvironment": true` in `config/environments.json`
  (today only `dev`); `scripts/Deploy-BffApi.ps1` then sets the App Service setting on the slot(s) it deploys to, and FAILS a
  deploy to an environment that carries the setting without declaring it. The declaration must be a JSON boolean (`true` /
  `false`, unquoted): the deploy FAILS on any other type, because PowerShell reads the string `"false"` as true (round 57
  item 3; the parse is `scripts/common/SpeAdminOperatorMarker.ps1`, which `SpeAdminOperatorEnvironmentMarkerGuardTests` runs).
- **Standing up Spaarke's production operator environment** is the change that adds the marker: a registry entry in
  `config/environments.json` with the key set to `true`, its name in
  `SpeAdminOperatorEnvironmentMarkerGuardTests.SpaarkeOperatedEnvironments`, the entry added to `Deploy-BffApi.ps1`'s
  `-Environment` `ValidateSet` (so the declaration and its `requiredCorsOrigins` gate are read by a deploy), and the App
  Service setting (the next `Deploy-BffApi.ps1` run for it sets it). Until then it refuses those routes (fail closed). The old
  `demo` entry (`spaarke-bff-demo`) was the candidate until it was retired on 2026-10-14 and removed from the registry; the
  demo is now the ACME stamp built by provisioning (T186), a customer environment that never carries the marker.
- **A declared marker is bound to its App Service** (round 62 item 1): when the entry declares `true`, the deploy FAILS
  unless `-AppServiceName` and `-ResourceGroupName` equal that entry's `appServiceName` / `resourceGroup`
  (`Assert-SpeAdminOperatorMarkerTarget`, `scripts/common/SpeAdminOperatorMarker.ps1`). The declaration is read by the
  `-Environment` label, which defaults to `dev`, so without this a deploy naming another App Service would inherit dev's
  marker.
- A customer root-unit admin calling those routes gets `403 spe.admin.deny.platform_operator_required` — the same answer as
  a leaf admin anywhere.

#### 6.5.5 Customer contact owner (`ExternalAccess__CustomerBusinessUnitId`) — required per stamp

> Added 2026-10-22 by `spaarke-SPA-external-access-platform-r3` ISS-005 (#1673; owner wording of the rule pending). The write
> rule it serves is invariant I-6 in [`DATAVERSE-WRITE-PATH-ARCHITECTURE.md`](../architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md).

**Every contact the BFF creates for a customer's people is owned at creation by the default Owner team of one business unit —
the customer's own.** A contact created with no owner team is owned by the BFF's own application user, in whatever
business unit that user sits (the root business unit on dev), not by the customer's team. The BFF creates an **identity contact** (the contact a person signs in as) in four places, and all four read this one
setting: **registration** (`POST /api/v1/external/registration`, a WorkforceHome contact), a Type-2 workforce user's **first
sign-in** (a contact keyed by their `oid`; §6.5.2), and the **licensed-user link** (a contact for a licensed systemuser who has
none — registration approval, the first resolution of a licensed user, and the identity-link reconciliation job), and the **invite** (`/invite`, `/invite-and-grant`): an email that matches no contact creates one, owned by the same team; an invite of an
existing contact never re-owns it.
The rule is plane-independent: every **identity** contact the BFF creates in its own environment gets this team.

> **A fifth contact create is outside this rule.** The chat tool `dataverse.create_record` can create a `contact` row (a user asks the assistant to "add a person"). That create is app-only, runs after an as-the-caller check, and is owned by the **caller's own business-unit default team, record-first**, through the general record-ownership resolver (`OwnedChildWrite`); it does **not** read `ExternalAccess__CustomerBusinessUnitId`. It creates no sign-in identity (no `oid`, no plane) and is not refused when this setting is unset.

| | |
|---|---|
| **Setting** | `ExternalAccess__CustomerBusinessUnitId` (configuration key `ExternalAccess:CustomerBusinessUnitId`) — **REQUIRED on every stamp**, on both slots |
| **Value** | the GUID of the customer's own business unit: a **child of the root** business unit, with **exactly one default Owner team** (Dataverse creates one with the unit) **whose roles give it Read on `contact`: the BU's default team must hold a security role with contact Read (and Create/Append as needed)** (Dataverse refuses a contact owned by a team that cannot read it), and **never the Secure Record business unit** (the unit named by `SecureRecord__BusinessUnitName`). A value that is not a GUID **fails startup** |
| **Absent, empty or the all-zero GUID** | **REFUSE, create nothing.** Registration answers `500 sdap.access.registration_owner_unconfigured`; a first sign-in that would create a contact answers `500` with the same code; an invite that would create a contact answers `500` with the same code and creates no contact, marker, CIAM account or email; the licensed-user link is not made (the reconciliation job reports the user `Denied` with the same code, report-only runs included, and the run is recorded **Partial**, never Ok). The job does **not** switch to a dry run: with the setting unset every create is refused while binds and links still write. Binding an **existing** contact (by `oid` or by email) and inviting an existing contact need no owner and still work. At startup the BFF logs one **Warning** (`[EXT-ACCESS] ExternalAccess:CustomerBusinessUnitId is not set to a non-zero GUID ...`); startup does not fail |
| **Also refused with the same code** | the unit is the Secure Record business unit (decided by id) or the Secure Record name is ambiguous; the unit has no default Owner team, or more than one; **or Dataverse refuses the create itself because the default team cannot own a contact** (`0x80040299`, "Read Privilege Check For
| **Unreadable owner lookup** | the owner lookup could not be answered (a failed read): nothing is created and the answer is **retryable**; each route answers with its own code, not one shared one. Registration: `503 sdap.access.registration_unavailable`. First sign-in: `sdap.access.deny.contact_lookup_failed` (its HTTP status is tracked in #2269). Invite and invite-and-grant: `503 sdap.access.invite.contact_lookup_failed`. Licensed-user link (registration approval, the reconciliation job): the user is `Denied` with `sdap.access.deny.contact_lookup_failed`, and the reconciliation run is recorded **Partial**, never Ok. The BFF logs a Warning naming the business unit (no email) | Owner failed": no role of the team holds Read on `contact`). That last case is mapped to the same `500 sdap.access.registration_owner_unconfigured` on **all four** creates (registration, first sign-in, licensed-user link, invite), with no id in the answer; the BFF log names the missing role at Error. On the invite the shared Web API client drops the error body, so any `403` on the owned create is read as this fault. A privilege fault of the BFF identity itself (`0x80040220`, e.g. no Create on contact) keeps its existing answer |
| **Not checked today** | that the unit is a child of the root. The BFF accepts the root unit; refusing it is an open owner decision, so the operator must not set it |
| **Written by** | provisioning — **H4b** writes it on both slots from the unit H10 creates, and **H13 trap T7** reads it back (T265), once `customer-provisioning-orchestration-r1` merges (issue #2265). Until then, and for Spaarke's own environments (dev), set it by hand on **both** slots |

```bash
az webapp config appsettings set --resource-group <rg> --name <app-service-name> \
  --settings ExternalAccess__CustomerBusinessUnitId=<customer-business-unit-guid>
az webapp config appsettings set --resource-group <rg> --name <app-service-name> --slot staging \
  --settings ExternalAccess__CustomerBusinessUnitId=<customer-business-unit-guid>
```

⚠️ **Behaviour change for an existing stamp (ISS-005).** Before ISS-005 a first sign-in on a stamp without this setting created an
**unowned** contact; it now **refuses** (a Type-2 user with no existing contact cannot sign in until the setting is set), and a
licensed user with no contact is no longer given one, and an invite of an email with no contact (which created an unowned
contact) now answers `500`. **The setting must be present on every slot (production and staging) BEFORE this build is deployed
to it**; otherwise the first sign-in, registration or invite that needs a new contact returns `500
sdap.access.registration_owner_unconfigured`. Check for the startup Warning above after the deploy.

**Contacts created before this build stay app-owned in the root business unit.** Nothing re-owns them; a backfill is out of
scope here and is tracked as #2278. **Who can read a new contact changes with it:** a contact owned by the
customer business unit's team is readable by users with business-unit-scoped Read on contact in that unit, where an
app-user-owned contact in the root unit was readable only by root-scoped readers.

**Known limit — contacts created in a FOREIGN environment are ownerless (#2270).** The setting names a business unit of **this**
BFF's Dataverse environment. When registration approval links a new systemuser's contact in a *different* environment (a demo or
customer target environment the BFF provisions users into, built by `ContactIdentityBinderFactory`), that unit does not exist
there, so the contact is created with no owner by design (`ContactCreateOwner.NoneForForeignEnvironment`, used by the factory
alone). The BFF's own environment is never treated that way: `RegistrationDataverseService.ContactBinderFor` returns the
owner-aware binder for it, however its URL is spelled (`IsSameEnvironment`).

---

## 7. Pipeline Execution Phases (walkthrough)

Each phase invokes one or more handlers. Post-Phase-D, `/provision-environment` runs this end-to-end. Pre-Phase-D, operator follows §12 interim runbook.

### 7.1 Phase 1 — Preflight & Consent (H0, H0.5)

**Purpose**: fail fast on quota shortage or naming collision BEFORE any resource is created.

**H0** runs unconditionally; **H0.5** runs only for Model 2 self-service (consent-capture branch).

**[GATE]** — H0 must pass:
- Azure OpenAI regional TPM headroom for the stamp deployment set, per Azure quota name, in `openAiLocation` (default westus3): DataZoneStandard gpt-4o 150, gpt-4.1-mini 200, text-embedding-3-large 350 (task 247)
- ~~Dataverse env-creation rate quota (`pac admin quota`)~~ — removed earlier by T228; H0 no longer runs it.
- ~~Subscription vCPU quota~~ — removed 2026-10-15 (T186 L17): the stamp deploys no Microsoft.Compute resources and App Service plans draw on their own per-SKU worker quota (not readable via any supported API), so a real shortfall surfaces at H2a's deployment with Azure's own error.
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

**H3** creates the customer's Entra app registration in C# with the Graph SDK (`GraphAppRegistrationProvisioner`; the
former `scripts/Register-EntraAppRegistrations.ps1` path was retired by task 130):
- the run's `tenantId` is **mandatory** (I1 enforcement — no default)
- grants the delegated permissions in `EntraAppRegPermissionCatalog`; the exposed scope is `user_impersonation`
- no client secret on a new stamp (secret-free identity, ADR-028 A4); a federated credential (`spaarke-uami-trust`) trusts
  the stamp's UAMI
- **a second federated credential, `spaarke-l2-worker` (ISS-015, 2026-10-09):** subject = the L2 Worker UAMI's
  **principalId** (`ControlPlaneIdentity__PrincipalObjectId` — never its clientId), issuer = Spaarke's tenant, audience
  `api://AzureADTokenExchange`. H6, H7 and H7b sign in to the customer's Dataverse **as this registration** (D-13) with an
  assertion from the Worker's own UAMI; without it they fail with `AADSTS700213`. Kept for the registration's lifetime
  (re-runs and upgrades sign in through it); Spaarke-tenant profiles only (MI-FIC cannot cross tenants; Model 2 is out
  of scope). A missing or invalid Worker principal id refuses H3 before any write (`appreg-worker-fic-identity-missing`).
  A stamp created before this fix gets the credential when H3 next runs for it (resume H3, then H6)
- **client access (T240a, 2026-10-07):** `spa.redirectUris` = exactly the customer's Dataverse origin
  (`https://spaarke-{customerId}[-{environmentName}].crm[N].dynamics.com`, from intake `dataverseEnvUrl` — the code pages
  sign in through this registration with `redirectUri = window.location.origin`), and `api.preAuthorizedApplications` =
  exactly the platform's shared clients on `user_impersonation` (`EntraAppRegOptions__PreAuthorizedClientAppIds__N`, set by
  the Worker Bicep parameter `preAuthorizedClientAppIds`; default the **production** Office add-in `1958aec2…` "Spaarke Office Add-in (Production)"; the dev add-in
  `c1258e2d…` calls only the dev BFF and is never pre-authorized on a customer stamp). A manual extra redirect
  or pre-authorization is removed on the next run.
- **adopts an existing registration only when it is provably the control plane's** (T240a review): refused when more
  than one app has the name, when it holds a client secret or certificate, when it carries a federated credential H3 did
  not create (H3 keeps exactly `spaarke-uami-trust` and `spaarke-l2-worker`, repairing either by triple), or when anyone but
  the control plane owns it (`appreg-adoption-refused`; nothing is written). The name
  `spaarke-bff-api-{customerId}` is predictable, and an adopted registration gets the stamp's FIC and Dataverse admin.
- **admin-consent gate (Model 1, Spaarke tenant):** when the run waits on H3's `admin-consent` gate, a Spaarke-tenant admin opens Entra admin center -> App registrations -> `spaarke-bff-api-{customerId}` -> API permissions -> **Grant admin consent for Spaarke**; that covers all 6 delegated permissions in `EntraAppRegPermissionCatalog`. Then advance the gate with `POST /api/runs/{id}/gates/admin-consent/advance` (API surface table above).
  Known limit (ISS-018 K1): an app registration H3 created before 2026-10-14 would keep a stale delegated entry with id `75359482-...` (the reconcile only adds); none exists, because H3 had never run live.

**Office add-in and Teams sign-in redirects are NOT on the customer's registration.** The shared clients sign in with
their OWN app registrations (production add-in: `1958aec2…` "Spaarke Office Add-in (Production)"), so `brk-multihub://<host>` and
`https://<host>/auth-callback.html` are registered ONCE, on that client app, for the production site
(`addins.spaarke.com`) — never per customer. A customer BFF only pre-authorizes the client (above) and lists its origin in
CORS (H4b). Reference impl: [`src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts`](../../src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts).

**The `acct` optional claim (access tokens) — REQUIRED on every per-customer BFF registration** (task 141).
The BFF's first-sign-in identity binding admits only a MEMBER of a configured customer tenant (§6.5.2), and
"member" is read from the `acct` claim (`0` member, `1` guest). **H3 sets it (T255)**: a new registration is created
with it, an existing one gets it added (every other optional claim preserved), and H3 reads it back
(`appreg-acct-claim-failed`, Resumable, if it cannot). A registration outside provisioning (Spaarke's own dev BFF) gets
it once by hand:

```powershell
.\scripts\Register-EntraAppRegistrations.ps1 -TenantId <tenant-of-the-registration> `
  -AcctClaimOnly -AcctClaimAppId <bff-app-registration-appid>
```

Both are idempotent and keep every other optional claim. Optional claims on the resource registration apply to
every access token issued FOR it — including the workforce tokens the External Access SPA and Teams tab acquire (the single-tenant Spaarke client, NAA then MSAL popup, scope `user_impersonation`; the Teams manifest declares no `webApplicationInfo` and there is no Teams-SSO path, ADR-028 A8) — see
Microsoft's [optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference).
Without it every contact-only Type-2 sign-in is denied `sdap.access.deny.workforce_acct_claim_missing` (fail closed).

**`authenticationBehaviors.removeUnverifiedEmailClaim = true` — REQUIRED on every per-customer BFF registration** (T264, #2001).
The BFF binds a workforce sign-in to an existing contact by the token `email` claim; that is safe only when Entra strips the
claim for an unverified domain, otherwise a user in an allowed customer directory whose unverified-domain email equals a
contact's can register as that contact. **H3 sets it** on new and existing registrations (read first, written only when it is
not already `true`, read back; `appreg-unverified-email-claim-failed`, Resumable, if it cannot) and **H13 trap T7 verifies it**.
H3 sets it only on a run that reaches H3 after T264; a run that finished H3 earlier (its idempotency key is recorded, so
neither a resume nor H3 re-runs it) and a registration outside provisioning set it by hand, in the registration's own
tenant — the run's tenant, Spaarke's for Model 1 (the PATCH replaces the whole `authenticationBehaviors` object, so include
any other value already set there). The body goes in a file: PowerShell strips the inner quotes of an inline JSON argument
to `az.cmd`.

```powershell
'{"authenticationBehaviors":{"removeUnverifiedEmailClaim":true}}' | Set-Content -Path body.json -Encoding utf8NoBOM
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/<object-id>" `
  --headers "Content-Type=application/json" --body "@body.json"
```

The multitenant Spaarke Workforce SPA client carries the same flag as a one-time operator setting; H3 never touches it.

**Escalation gate** (per FR-13 / H10): every `AppRoleId` in `GraphAppRoles.cs` must be populated from a live enumeration of the Microsoft Graph service principal's `appRoles` (all are; re-read 2026-10-09 by task 261). H10 refuses to run on a null.

**H4** populates KV secrets from the canonical catalog manifest + re-asserts `keyVaultReferenceIdentity` = UAMI on both slots (an idempotent PATCH: `customer.bicep` sets it at creation and H2a verifies **T1** right after the deploy — T186 L21).

**H4b CORS (T240a).** The BFF refuses to start outside Development when `Cors:AllowedOrigins` is empty, so the manifest
carries the shared client sites as literals on every stamp: `Cors__AllowedOrigins__0` = `https://addins.spaarke.com`
(Office add-ins) and `__1` = `https://external.spaarke.com` (External Access SPA and Teams tab). The customer's Dataverse
origins need no entry — `CorsModule` admits `*.dynamics.com` and `*.powerapps.com` by suffix.

**H4b startup keys (T258).** The BFF also refuses to start without these, so H4b writes them on both slots:
`PublicConfig__BffUrl` = `https://{appServiceName}.azurewebsites.net` (the URL H9 probes and records),
`PublicConfig__MsalClientId` = the customer BFF app registration (= `AzureAd__ClientId`), `PublicConfig__TenantId` = the
intake `tenantId` (= `AzureAd__TenantId`), `Graph__Scopes__0` = `https://graph.microsoft.com/.default` and
`ServiceBus__QueueName` = `sdap-jobs` (a queue `customer.bicep` creates). The H0.5 consent callback
(`POST /api/onboarding/consent-callback`, Model 2) is **off** on a stamp — it is mapped only with `Onboarding__Enabled=true`,
which no stamp sets — so a stamp carries no `Onboarding__*` setting and no HMAC signing key.

**H4b external-registration owner (R3 073 / ADR-028 A9).** H4b also writes `ExternalAccess__CustomerBusinessUnitId` on both slots: the customer's own business unit, from H10's `InterStepState.CustomerBusinessUnitId` (so H4b follows H10 in the DAG). The BFF refuses to start on a non-GUID value, and without it the external registration route answers 500 `registration_owner_unconfigured`. **H13 trap T7 reads it back** (task 265): both slots must carry a GUID equal to H10's unit.

**H4b session-file store (T186 follow-up).** H4b also writes `SessionFileStore__BlobEndpoint` on both slots: the stamp storage account's blob endpoint, from H2a's `storagePrimaryEndpoint` output (`InterStepState.StorageBlobEndpoint`). The container is not written — the BFF defaults to `ai-chunks`, which every stamp creates. Without it chat session-file uploads are refused (`StoreDisabled`).

### 7.4 Phase 4 — Dataverse Environment (H5, H6, H7)

**H5** adopts the Dataverse environment the operator created (PRQ-C-09) — it never creates one (owner D4 / Q1;
T228). It refuses a URL not named for this customer and stops `worker-not-app-user` until the L2 Worker identity is a
System Administrator application user there. **H10** follows (before H6 — §5.1).

**H6** imports **one solution, `SpaarkeMaster`**, from the provisioning-artifacts store — managed by default, unmanaged
only when the intake says `solutionPackageType = unmanaged` (ADR-027 §3, amended 2026-10-07). It refuses to switch an
environment's type and refuses a downgrade; an upgrade is `StageAndUpgrade`. Package definition, release, IAM and
upgrade: [`SPAARKE-SOLUTION-RELEASE-PROCESS.md`](../procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md). H6 side built
2026-10-07 (T218b). ⚠️ A real run fails at H6 until the package is published in the new format (T218c–e: content,
git source, CI publish) — the store still holds the hand-made 2026-08-21 manifest.

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

**After H6, and BEFORE the BFF: a document's Access Permission (`unified-access-control-r2` task 189, D7 T7a).** A
document holds max(its own narrowing, the value its parents give it), and under D7 a document's Restricted hides it from
partners (task 188). The BFF records which part is the document's own in `sprk_document.sprk_accessinheritance`
(Multiple lines of text, NOT field-secured). `scripts/Set-DocumentAccessPermissionSchema.ps1` creates it and checks
`sprk_document.sprk_accesspermission` too: dry run, then `-Apply`, then `-Verify` (must exit 0).

```powershell
.\scripts\Set-DocumentAccessPermissionSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com           # dry run
.\scripts\Set-DocumentAccessPermissionSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Apply
.\scripts\Set-DocumentAccessPermissionSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -Verify   # must exit 0
```

🔴 **Deploy order: the schema first, then the BFF.** A BFF carrying task 189 cannot tell a document's own narrowing
from its inherited value until the record column exists, so until then it only RAISES a document's Access Permission
and never lowers it. The reconcile job reads the column list each run: while documents lack the column, it logs a
warning and lists `sprk_document` under `tablesWithoutColumn` in its run report. A document below what its parents give
is raised to it (the value alone, counted in `raisedOnly`); one at or above it is left as it is. The child-records
create refresh does the same, and neither reads nor writes the missing column. To Do, Event and Communication are
reconciled as before. A BFF deployed first therefore lowers no document and leaves none below its parents, **but what
it raises stays.** A document raised while the column is missing, whose parents then widen again, keeps the stricter
value. On the first run after the script, the job reads that value as the document's own narrowing (it is stricter than
what its parents give, and the document has no record yet), records it so, and never widens it again.

🔴 **A BFF carrying `spaarkeai-word-add-in-r1` task 130 (D7 T8) REQUIRES the column; it is not raise-only.** When the
Outlook add-in saves a document from an email it cannot identify, the BFF creates that document Restricted and writes
`sprk_accessinheritance` in the same create (write-path registry I-19). Without the column, that create fails:
- a single `.eml` or single-attachment save returns a generic 500;
- each Restricted attachment child fails inside `UploadFinalizationWorker`. The failure is logged only as a Warning and
  the job still reports success, so the user sees a saved email whose attachments are missing.

Nothing is exposed (it fails closed), but saves are lost. Run the script and `-Verify` before deploying such a BFF.

**So deploy the schema BEFORE the BFF.** If the BFF went first, find and fix these documents after the script has run:

1. From the window between the BFF deploy and the script, collect the documents the BFF raised: the job's
   `[CHILD-ACCESS-PERMISSION] set: sprk_document …` log lines that say `own unknown` (each run counts them in
   `raisedOnly`), and the child-records refresh's `[INHERITED-ACCESS-PERMISSION] sprk_document …` warnings that name
   the missing `sprk_accessinheritance`.
2. In the first runs after the script (until `sweep.complete` is `true`), the job counts every document held above its
   parents by its own narrowing in `ownKept`; its `set:` line says `record written` with an `own` value stricter than
   `inherited`. Unless a user narrowed it in that window, a document in both lists is stricter only because of the
   raise.
3. Widen each one by hand, as a user with Write on it: set its Access Permission to the value its parents give
   (`inherited` in the line). The next run reads that edit as the own narrowing removed, and the document follows its
   parents again.

Work assignments and projects keep their own record (the same column name, field-secured there) through
`scripts/Set-AccessInheritanceSchema.ps1` (task 175). The document script does not touch them.

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

**Pre-marker containers (issue #1889).** Only the BFF's and H8's *creation* paths write the marker, so per-record (secure)
containers created before T227d carry none and the guard refuses them (404 `spe_container_not_owned`). An environment that
holds such containers runs `Backfill-SpeContainerBusinessUnitStamp.ps1 -CustomerId <the BFF's Customer__Id>` — dry run,
then `-Apply -MaxWritesPerRun 1`, then `-Apply`, then `-Apply` once more — and then `-Verify` (it must exit 0) **before the
guard-carrying BFF is deployed there**. `-CustomerId` is the BFF's `Customer__Id` app setting and must match the BFF's
format `^[a-z][a-z0-9]{2,7}$` (3–8 characters, lowercase letters and digits, a letter first; anything else exits 2 before
any call). When that setting is absent, the BFF derives the id from its `WEBSITE_RESOURCE_GROUP` name
`rg-spaarke-{customerId}-{env}` (`CustomerIdResolver`: exactly two segments after the prefix, lower-cased; `platform`,
`shared` and `byok` are never a customer) — pass that id. An explicit setting that breaks the format is *not* replaced by the
derived one: the BFF treats it as unresolved. The marker is what makes the BFF serve a container to this customer, and every
customer stamp shares one container type, so the script marks only a container that **already carried a stamp naming one of
this environment's business units** and whose owner its records derive (or one an explicit `-Bind` names). A container the
run stamps from records alone is **not** marked in that pass: it is listed `MARK-NEXT-RUN`, the operator reviews the stamp,
and the next run marks it. **The operator reviews the dry-run list before `-Apply`, because the record claim
(`sprk_containerid` on a secure root) is customer-writable.** A container stamped by an earlier `-Bind` that no record
claims is listed `UNMARKED-NO-CLAIM` (re-run with `-Bind '<id>=<unit>'` to mark it). A marker is written only where it is
absent (a different or ambiguous marker is listed, never overwritten) after a reversal-manifest row is on disk, and
`-Verify` exits 1 while any derivable container is unmarked. Known limit: the guard caches a missing marker for up to 10
minutes, so access can lag a passing `-Verify` by that long.

Container ID persisted to the Dataverse env-var (`sprk_SharePointEmbeddedContainerId`, H7) and to the BFF as plain settings (`EmailProcessing__DefaultContainerId`, `Communication__ArchiveContainerId`, H4b — T227c) — enables I4 invariant enforcement.

**Lead-time**: none per customer. The container type and its owning app are a one-time setup; H0's `SpeOwnerCredential`
check confirms the owner entry, the owning-app token and the registration before any resource is created.

**Users and container roles — no per-user step (unified-access-control-r2 task 171, owner rounds 69 + 70).** Do NOT add
users to any container by hand. Document access follows Dataverse:

- **Every non-edit byte path is broker-only.** Upload, preview, download, versions, Compose and AI run **app-only** after
  the BFF checks the caller's Dataverse rights on the record — on the environment/root, business-unit and per-record
  secure containers alike, and for contacts. This needs the BFF identity's `full` application permission on the
  container type (the registration step in the topology runbook), nothing per user.
- **Office edit (web and desktop) runs as the user**, so it needs the user's own role:
  - **Business-unit / environment containers:** every enabled, internal (`sprk_isexternal` not true) person user of the
    unit(s) whose `sprk_containerid` names the container is kept as a **writer** by the scheduled job
    `spe-container-membership-sync` (**every 5 minutes**; enabled by default). New, enabled or moved-in users are added;
    disabled, moved-out or flagged-external users lose the role — but only a role the job created. Owner and hand-granted
    roles are never touched. Disable it without a redeploy: `POST /api/admin/jobs/spe-container-membership-sync/disable`.
  - **Per-record secure containers:** no standing members. A user with **Write** on the secure record gets a
    **just-in-time writer** role when they choose Open in Word/Excel; the same job removes it once Dataverse answers that
    they no longer have Write (unshare, No Access, Restricted + flagged external, disabled user). A removed role stops new
    opens and saves within minutes; a copy already open in desktop Office stays readable until it is closed.
- **Recommended once per container type:** set `isSharingRestricted = true` on the container type registration so that a
  writer cannot re-share a file from Office outside Dataverse (under the default "open" model any member with edit can).
  This is a container-type setting change — an operator decision, not done by any handler.

**Trade-off to tell the customer (owner question pending, 2026-10-06).** Because the BFF writes app-only, SharePoint's own
**"Modified by"** on a file and its SPE version history shows the **Spaarke application**, not the person. The person is
recorded in Spaarke: a new document's row carries its creator (`createdby`, or `sprk_createdbyperson` for a row the BFF
created), and every save is authorized as that person and logged. There is **no per-version "modified by person" column**
in Dataverse today, so the person behind each later version is in the BFF logs, not on the row.

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
- Naming-conformance (`scripts/naming-conformance-check.ps1` exit 0 — blocking as the Tier 1 `naming-conformance` job in `ci-tier1-blocking.yml` since T230a)
- Graph app-role parity (`GraphAppRoles.cs` constant)

**BFF publish-size ceiling**: **≤ 60 MB compressed (HARD)**. Current net10 baseline: 44.96 MB incl. PDBs. Per-PR measurement + delta report required (NFR-01).

### 7.7 Phase 7 — Dataverse App Users + Users (H10, H11)

**H10** first finds or creates the **customer's own business unit** — named by intake `displayName`, a **direct child
of the root** and so a sibling of the `Secure Record` unit (T259, ISS-010 / #1486, owner decision 2026-10-09: "for secure
records, only users explicitly granted access should have access; no users are added to the secure business unit") — and
records its id as `customerBusinessUnitId`. It then registers 2 Dataverse Application Users as System Administrator, both
**created in that unit** (the role is the unit's own copy):
- BFF app-reg
- UAMI (post-Phase-C)

Why the customer's own unit, not the root: `Spaarke Basic User` holds Read at **Deep** on project, matter
and work assignment. Deep at the root reaches every child unit, the `Secure Record` unit included, so a user in the root
would read every secure record. Deep in the customer's unit stops at that unit. An App User that already exists in
another unit (an environment provisioned before T259) is QuarantineRequired, never moved: a business-unit change removes
all of a user's roles, and H6/H7/H7b sign in as the BFF App User.

**Both App Users must hold `prvActOnBehalfOfAnotherUser` and stay at Organization scope** (unified-access-control-r2
tasks 034–036). The BFF reads some Dataverse data AS the signed-in internal user (the `MSCRMCallerID` header): the
communication reads, the Office add-in picker, and — once [§12.8](#128-fr-20--let-dataverse-decide-internal-users-spateams-access)
turns it on — the internal user's SPA/Teams record sets. Dataverse honours the header only for a caller holding
`prvActOnBehalfOfAnotherUser`. No Spaarke role carries it: the App Users hold it through **System Administrator**
(Global depth). In dev that is `# mi-bff-api-dev`, `SDAP-BFF-SPE-API` and `# Spaarke DMS-SPE Dev 1` — checked by role on
2026-09-30, and proven live only by the task 034 canary (§12.8 step 3). Never grant it to a Spaarke role as a workaround.
⚠️ **Do not narrow an App User** (register E-2 / E-3). An impersonated read returns what BOTH the App User and the user
can read: an App User moved to a smaller role or a narrower depth silently returns FEWER records than the user can read —
no error, an under-grant that looks like a correct answer.

Then reconciles the stamp identity's Microsoft Graph app roles to exactly the evidence-backed set in `GraphAppRoles.cs`
(task 261 / G31 — every app-only call the stamp BFF makes, its Microsoft Learn least-privileged permission, and the
live evidence: `projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md`):
- **grants** `FileStorageContainer.Selected` in Entra — the only Entra role a stamp holds (every app-only SharePoint
  Embedded call needs only it plus the container-type permission H8 grants; no app-only call leaves an SPE container);
- **removes** every other Graph app role on the stamp identity, each removal logged — a stamp lives in Spaarke's tenant,
  so a leftover `Directory.ReadWrite.All`, `User.ReadWrite.All`, `Sites.ReadWrite.All`, … reaches Spaarke's own
  directory and SharePoint. Before removing, H10 checks the target is the stamp's managed identity (its `appId` is the
  run's `miClientId`, its type `ManagedIdentity`); otherwise it removes nothing (`h10-graph-role-removal-failed`).
- never grants the mailbox roles (`Mail.Read`, `Mail.ReadWrite`, `Mail.Send`): H14a grants them through Exchange, scoped
  to the customer's group. Exchange adds the two sources together, so an Entra mailbox grant would reach every mailbox
  in the tenant.

The stamp needs no directory role: the demo self-service registration feature (the BFF's only app-only `/users` and
`/groups` writer) is registered only where the COMPLETE `DemoProvisioning` settings are present (AccountDomain, DemoUsersGroupId, the three
license SKUs, at least one AdminNotificationEmails — a partial set disables it rather than crashing start) — Spaarke's platform BFF, never a
stamp — and H11's invitations run as the L2 Worker.

**T2 verification**: `systemusers?$filter=applicationid eq {uami-app-id}` returns count 1.
**T3 verification**: UAMI SP `appRoleAssignments` on Microsoft Graph holds exactly `FileStorageContainer.Selected` —
nothing else (H10 re-reads with backoff — Graph is eventually consistent; extras still listed after its own removal →
Resumable `h10-graph-role-extras-unverified`; extras it did not see → QuarantineRequired `h10-trap-T3-unexpected-roles`;
H13 T3 re-checks). H10's idempotency key carries a fingerprint of the Entra role set, so a re-dispatch after a catalog
change re-runs the reconcile. A stamp whose H10 completed before task 261 is cleaned with
`scripts/provisioning/Remove-StampGraphExtraRoles.ps1` (dry run by default).

**H11** provisions the customer's users (T232, owner D2 — Model 1 users are **B2B guests in Spaarke's tenant**, and a
Model 1 run takes only `B2BGuest`):

1. **Before inviting anyone** it refuses (Resumable, nothing written) a run whose environment security group
   (`environmentSecurityGroupId`, `PRQ-C-10`) is not named `sprk-{customerId}-users` or not security-enabled
   (`userprov-security-group-rejected`), or whose environment restricts guests
   (`organization.restrictGuestUserAccess`, `PRQ-C-12` — `userprov-guest-access-restricted`). The group is the
   isolation boundary between Model 1 environments: all live in Spaarke's tenant, so an environment without it admits
   every user of that tenant, including other customers' guests. H11 checks the group's **name**; that it is the group
   **set on the environment** is checked by the skill (Step 1e-bis, Power Platform admin API) — only a Power Platform
   admin can see it. It also resolves the guests' security role(s) here, so a missing role sends no invitation.
2. Each user becomes a guest: an existing guest with that address is **reused without a second invitation email**; an
   address that belongs to a member of the tenant is refused.
3. It waits for every guest to redeem the invitation (`b2b-consent` gate → `WaitingOnGate`). Microsoft sends the
   invitation from `invites@microsoft.com` to the guest's own address; a demo-tenant user without an Exchange Online
   mailbox never receives it (T186). A guest can redeem without the email: open
   `https://myapps.microsoft.com/?tenantid=<Spaarke tenant id>` in a private window, sign in as the guest and accept the
   permissions prompt. Then `POST /api/runs/{id}/gates/b2b-consent/advance?customerId={cid}` — it re-runs H11, which
   reads each guest's `externalUserState` (`Accepted`).
4. It adds each guest to the group, gives it the **Power Apps Plan 2 Trial** licence (below), then reads the guest's Dataverse user through the `azureactivedirectoryobjectid`
   alternate key — Dataverse adds a group member who is not yet a user on that read (Microsoft's documented app-only
   path; no Power Platform admin role). Dataverse adds the user to the **root** unit; H11 moves it to the customer's
   business unit (`customerBusinessUnitId`, T259) and reads the move back **before** it associates any role (a unit
   change strips roles), then associates `H11UserProvisioningOptions:GuestSecurityRoleNames` (default `Spaarke Basic
   User` — the copy in the customer's unit; resolved in step 1). A guest already in any other unit (for example
   `Secure Record`) is QuarantineRequired `userprov-guest-in-foreign-business-unit` and is never moved; a guest that
   holds a role of another unit (an organisation that keeps roles on a unit change) is QuarantineRequired
   `userprov-guest-holds-role-outside-customer-unit` — no role is removed. Dataverse adds the guest only once its user
   sync has seen the group membership **and** the licence, which takes minutes (T186: more than 4); H11 re-reads for up
   to `DataverseUserSyncRetryCount × DataverseUserSyncRetryDelay` (5 min). A 404 after that is
   `userprov-dataverse-user-failed` — check the group, guest access and the licence, then resume. A guest who never redeems keeps the gate pending; resend the invitation from the
   Entra admin center, then resume (H11 never re-invites — it would re-send mail on every re-check).

**Licensing** (owner 2026-10-16, T186 L51): every Model 1 guest gets the **Microsoft Power Apps Plan 2 Trial** licence
(`POWERAPPS_VIRAL`, skuId `dcb1a3ae-b33f-4487-846a-a640262fadf4`, the same id in every tenant) in Spaarke's tenant. H11
sets the guest's usage location (`US`) when it has none and assigns the SKU (`H11UserProvisioningOptions:GuestLicenseSkuId`,
defaulting to that id; blank refuses the run before any invitation, `userprov-license-sku-not-configured`). Dataverse
does not add an unlicensed guest. Check that the tenant has free units before a run:
`az rest --method get --url "https://graph.microsoft.com/v1.0/subscribedSkus" --query "value[?skuPartNumber=='POWERAPPS_VIRAL'].[prepaidUnits.enabled, consumedUnits]"`.
The earlier design (owner 2026-10-07) had guests unlicensed, paid **pay-as-you-go**. That does not work as configured:
the T186 billing policy (`PRQ-C-11`) had only the Dataverse storage meters on (`AppPass` off), so the guests were
covered by nothing and Dataverse refused them. `PRQ-C-11` stays for Dataverse capacity. `NativeAccount` (Model 2)
still assigns the configured licence SKUs and refuses to create anyone when none is configured.

**B2B guests are flagged external** (unified-access-control-r2 task 114, owner round 67). A blank
`systemuser.sprk_isexternal` means NOT external, so a B2B guest (`#EXT#` in its user name) that is genuinely external
must carry `sprk_isexternal = Yes`. A user flagged external is refused a share on a Restricted record and gets no
internal-only message.

> **On a Model 1 stamp, run the script with `-CustomerTenantIds`** (spaarkeai-word-add-in-r1 task 126, owner 2026-10-13;
> ADR-028 A8/A9). Under Model 1 the customer's licensed core users are B2B guests (`#EXT#`) in the Spaarke tenant (A8), so
> "every guest is external" would mark the customer's own staff external. Workforce contacts are not guests (A9: they sign
> in to the SPA with their company account and are contacts), so they never reach this script. The script requires a mode:
>
> | Stamp | Mode | Rule |
> |---|---|---|
> | Model 1 | `-CustomerTenantIds <id>[,<id>…]` = the stamp's `WorkforceIdentity__CustomerTenantIds__N` list (§6.5.2) | Each guest's email domain is resolved to its home directory with Graph `findTenantInformationByDomainName`. A guest of a listed directory (or one listed in `-InternalUserIds`) is the customer's staff and is **never written**. A guest of another directory is set to Yes unless it already is Yes. A guest whose domain does not resolve, or whose two addresses resolve to different directories, is **reported for review and never written** (unless already Yes). Only a stored Yes counts as a decision: No is the column's default, so a new guest carries it without anyone choosing it. The script refuses the stamp's own directory or an empty id as a customer directory, and refuses `-Apply` when it has guests to set but none of the stamp's guests resolves to the customer's directories (before the customer has any guest staff, set such an outsider's flag by hand). These guards catch only an id that matches no guest; another real directory with guests on the stamp passes them, so **check the ids against the stamp BFF's settings (the §12.3 query) before `-Apply`**. |
> | Every guest is an outsider (not Model 1) | `-AllGuestsExternal` | Unchanged task 114 behaviour: every guest still blank or No is set to Yes. |
>
> Dry run → review the report with the owner → `-Apply` → `-Verify` (exit 0 only when nothing is left to set **or to
> review**). Settle each "review" row:
>
> - **Unresolved guest:** set the user's flag to Yes (an outsider), or add the user's `systemuserid` to `-InternalUserIds`
>   (customer staff, for example on a subsidiary's domain verified in another directory) and pass that list on every run.
> - **Customer staff flagged external:** set the flag back to No. On Model 1 the customer's staff are internal; this row
>   does not clear while the flag is Yes.
>
> The same applies to a customer staffer whose address resolves to another directory: the dry run proposes "set Yes", so
> add them to `-InternalUserIds` before `-Apply`. §12.2 Phase 7b has the commands.

### 7.8 Phase 8 — Configuration Seed (H12a, H12b, H12c)

**H12a** and **H12b** are DAG-parallel (no cross-dependency):

**H12a** seeds the AI chain: type-lookups → actions → tools → knowledge → skills → playbooks → output-types → **playbook consumers** (single AI routing surface per ADR-039; `spaarke-playbook-embeddings` retired).

**H12b** seeds app-config: DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions.

Both consume the **declarative seed manifest** (resolves the `scripts/seed-data` MVP vs `infra/dataverse` R7 drift per INVENTORY §9).

**H12c** wires runtime references: `sprk_aimodeldeployment` rows point at customer's OpenAI deployment (dedicated per customer — both models). Runs AFTER H12a + H12b + H2a (OpenAI deployed).

**Worklist grid configuration (ontology task 164, D-185).** The ontology worklist reads its membership and order from one `sprk_gridconfiguration` row (`00000000-0000-4000-8000-000000005010`, defined in `config/worklist.gridconfiguration.json`). H12b does not seed it. A **release** (`scripts/Deploy-Release.ps1`, Phase 2b) applies it for every target environment and verifies it (`-Verify -RequireRow`) after the BFF (Phase 2) and **before** the SpaarkeMaster import (Phase 3). The order is binding: the SpaarkeMaster import carries the D-185 role change that removes policy Read from Spaarke Basic User and Spaarke Console User, and with the old row that still joins `sprk_policy`, every user gets an empty worklist once policy Read is removed (HTTP 200, with a log warning only). An old BFF with the new row loses rule names and severity, so Phase 2b does not run if the BFF failed (a skipped BFF phase, `-SkipPhase BffApi`, is an explicit operator choice and still runs it). If Phase 2 (the BFF) or Phase 2b fails for an environment, Phase 3 does not run for that environment, even without `-StopOnFailure`. `-SkipPhase OntologyConfig` skips it as an explicit operator choice and does not block Phase 3, so use it only when the row is already in place and in sync. The step uses the operator's `az` sign-in (no service principal, no client secret), like Phase 3.

**Fresh environments** have no old row, so the order does not matter there; Phase 2b still applies the row after the BFF. **On dev** (the authoring environment, which skips Phase 3), an unmanaged import does not remove privileges: the D-185 role change is applied with `RemovePrivilegesRole` (see `projects/spaarke-ontology-platform-r1/notes/security-roles.md` section 19).

For an out-of-band fix (a row that is missing or drifted outside a release), run the same script by hand:

```powershell
pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://<env>.crm.dynamics.com -WhatIf
pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://<env>.crm.dynamics.com
pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://<env>.crm.dynamics.com -Verify -RequireRow
```

Known gap: the Do-lane and know-promotion policies (`scripts/Apply-DoLanePolicies.ps1`) still reach a customer environment only by hand; no release phase applies them.

### 7.9 Phase 9 — Post-Deploy Integrations (H14)

Two sub-steps, in order (H14m is not attempted when H14a fails — its authorization test needs H14a's assignments):

- **(a) Exchange mailbox access (RBAC for Applications, task 251)** — the stamp UAMI only (the BFF app registration does no app-only mail; granting it would widen its reach). Through the Worker's sidecar: register the UAMI in Exchange (`New-ServicePrincipal`), then one assignment per mailbox role, named `{prefix}-{customerId}-{Role}`, scoped to the intake group. Get-before-set: any existing assignment that differs → Drift, nothing changed (**T4**). Grants take effect in 30 min – 2 h (Microsoft).
- **(m) The customer's shared mailbox (H14m, task 263 — owner decision 2026-10-10, #1562)** — Model 1 reaches only
  Spaarke-tenant mailboxes (customer staff mailboxes live in their home tenant), so each customer gets **one Spaarke-tenant
  shared mailbox**:
  1. Through the sidecar (`POST /ensure-customer-mailbox`), get-before-set: Exchange name/alias `sprk-{customerId}-mail`,
     address = intake `communicationDefaultMailbox` (the same value H4 writes to `Communication-DefaultMailbox`, so the
     BFF's default sender is this mailbox), display name = intake `displayName`; created with `New-Mailbox -Shared` and
     added to the scope group `Spaarke-AppAccess-{customerId}` (`Add-DistributionGroupMember`), then
     `Test-ServicePrincipalAuthorization` must report every stamp mail role (`Application Mail.Read/ReadWrite/Send`) in scope.
  2. In the stamp's Dataverse, as the BFF app registration (the H7/H7b identity): exactly one `sprk_communicationaccount`
     row for that address — Shared Account, App-Only, send- and receive-enabled, default sender, `sprk_securitygroupid` =
     the scope group, **Verified** (the message says it is Exchange's configuration-level authorization; Graph calls can lag a
     new grant by 30 min – 2 h, and a user can re-run `POST /api/communications/accounts/{id}/verify` any time). L2 does not
     call that endpoint: it is a user endpoint (Write on the account), and right after H14a its Graph test would fail.
  - **A second run writes nothing** (mailbox `AlreadyCompliant`, row already Verified).
  - **Quarantine, nothing changed** (`h14m-mailbox-drift`): a recipient with that name, alias or address that is not a
    shared mailbox, has another name/address (another customer's or a person's), sits outside the scope group or in any
    other group; or the scope group is not named `Spaarke-AppAccess-{customerId}` (optionally followed by exactly 14
    digits: a group created in the admin center gets its Exchange Name from the alias plus its creation time, e.g.
    `Spaarke-AppAccess-acme20261014194847` — T186 L59). `h14m-account-row-conflict`: the stamp
    has several rows for the address, an inactive one, or one of another type.
  - **Resumable**: `h14m-mailbox-ensure-failed` (sidecar / Exchange / **the T263 Exchange roles not applied — §4.2.1 step 5,
    pending owner decision D32**; nothing is created), `h14m-mailbox-unverified` (a role not yet in scope; no row written),
    `h14m-account-verification-failed` (the row says the BFF's own verification FAILED — never overwritten; re-verify in the
    app, then resume), `h14m-account-row-failed`.
  - **Decommission (manual in r1)**: `Remove-DistributionGroupMember -Identity 'Spaarke-AppAccess-{customerId}' -Member
    'sprk-{customerId}-mail' -BypassSecurityGroupManagerCheck`, `Remove-Mailbox 'sprk-{customerId}-mail'` (soft-deleted 30
    days), then **deactivate** (never delete) the account row. Design: `projects/customer-provisioning-orchestration-r1/notes/t263-customer-shared-mailbox.md`.

**Removed (ISS-019 / #1560): H14b (Graph webhook subscriptions) and H14c (Dataverse service-endpoint webhook).** They registered
receivers at `{stamp}/api/webhooks/graph/{module}` and `{stamp}/api/webhooks/dataverse/communication`; the stamp BFF maps neither
route (its mail receiver is `POST /api/communications/incoming-webhook`), so the Graph create handshake would 404 and a run would
stop at H14b. Mail needs no provisioning-time subscription: the BFF's `GraphSubscriptionManager` creates and renews its own
Graph subscription for each receive-enabled mailbox, using the `Communication-WebhookUrl` that `customer.bicep` sets
(`{stamp}/api/communications/incoming-webhook`); `InboundPollingBackupService` (5 min) and delta reconciliation (15 min)
run regardless. The mailbox record (`sprk_communicationaccount`) is created and verified by H14m (above) — no hand step.
Run intake no longer takes `communicationGraphResource` / `emailGraphResource`. The stamp's
`Communication__WebhookSigningKey` setting still resolves to Key Vault secret `Communication-Webhook-SigningKey` (H4 generates it; the BFF requires it for the receiver's HMAC check).

**Explicitly NOT included** (per r3 task 060): S2S consent flows — the S2S Dataverse app-reg was dropped.

### 7.10 Phase 10 — Acceptance Gate (H13 → Ready)

**H13** invokes the extended `Validate-DeployedEnvironment.ps1`, asserting **effects** not intentions (per R7):

- BFF `/health` = 200
- Sample AI analysis end-to-end succeeds
- Sample document upload + AI Search indexing succeeds
- Workspace-layout render succeeds
- Wizard field-map succeeds
- **All 7 §4B T1–T7 silent-fail traps cleared** (see §9)
- **The customer mailbox (T263)**: read-only, the shared mailbox `sprk-{customerId}-mail` exists at the intake address,
  in the scope group and no other group, every stamp mail role in scope (`Test-ServicePrincipalAuthorization`), and the
  stamp has exactly one active, shared, **Verified** `sprk_communicationaccount` row for it. Missing, foreign or
  unverified → QuarantineRequired `h13-customer-mailbox-failed`; no verdict (sidecar or Dataverse unreachable, missing
  input) → Resumable `h13-customer-mailbox-inconclusive`
- **The §4D I2–I5 tenant-isolation invariants sample-verified on the deployed stamp** (see §8; I1 is a build gate —
  T230a), with I3 checking the Cosmos account holds exactly `cosmos-db.bicep`'s containers and partition keys
- (Naming conformance is a blocking CI gate — `scripts/naming-conformance-check.ps1`, the Tier 1 `naming-conformance` job in `ci-tier1-blocking.yml` — not a per-run
  H13 check: it lints the repository's own files, T230a.)
- Cost envelope: the subscription's month-to-date cost, extrapolated, within 20 % of $400/month (one envelope for both models — §3.2, T229)
- **Secure-record isolation (T260, ISS-014)**: H13 calls the stamp BFF's `POST /api/platform/secure-record-isolation-census`
  as the L2 Worker identity — the keyless proof's token (`api://{BFF app id}`) and application role
  (`Provisioning.KeylessProof`); no other role opens it. The BFF runs, read-only, the census its 15-minute
  `secure-record-isolation-census` job runs (spec NFR-05: no role reaches the `Secure Record` unit by depth, the unit
  holds no users, its owner team is memberless and alone holds the owner role, the role covers the codified tables) and
  answers `isolated` | `findings` | `inert` | `error` with the job's findings. Only `isolated` passes:
  - `findings` → QuarantineRequired `h13-secure-isolation-not-isolated` (each finding, naming the principal or table, is
    in the run's error detail);
  - `inert` (the BFF finds no `Secure Record` unit) → QuarantineRequired `h13-secure-isolation-inert`. H7b creates the
    unit before H13, so inert is a broken stamp, not a fresh one. The census grades the security topology, not records,
    so a new environment with no secure record yet is graded in full;
  - a refused or failed call (401/403, 500, an unknown answer) → QuarantineRequired `h13-secure-isolation-census-failed`;
  - `error` (the BFF could not read the directory), a timeout, a transport fault or a BFF build without the route (404)
    → Resumable `h13-secure-isolation-inconclusive`.

**`sprk_dataverseenvironment.Setup Status` transitions to `Ready` only if H13 exits 0.**

### 7.11 Phase 11 — Secure-record environment setup (H7b; the operator verifies it before telling the customer the environment is ready)

Secure projects, matters and work assignments (unified-access-control-r2) need Dataverse security configuration that
no solution import can create. Since T256, **H7b creates it on every run**, after H6 and before H9:
- the `Secure Record` business unit, which holds no users and **no container**;
- the named, memberless `Secure Record Owners` team;
- the `Secure Record Owner` role, created inside that unit, with the Read-at-Basic set in
  `config/secure-record-owner-role.json`, on that team only;
- the BFF-managed field-security memberships;
- the contact identity-link memberships;
- the BFF's two application users as members of the `Standing Grant Administrators` field-security profile (without
  it the platform hides `contact.sprk_standinggrant` from the BFF and every standing grant reads as not held; ISS-020 /
  #1565) — other members of that profile are never touched;
- a check that `sprk_noaccessentry` is readable, and that the `Spaarke Access Administrator` role holds Read on it at
  Global while `Spaarke Core User` holds none (unified-access-control-r2 task 154; verified, never repaired — H7b
  refuses `secure_setup.no_access_entry_roles_incomplete` and names `scripts/Set-NoAccessEntryRolePrivileges.ps1`);
- a read-only check (S22, ISS-025 / #1803) that `contact.sprk_externalinvitedon` (SpaarkeMaster 1.2.2.0 and later) is
  field-secured, that `Spaarke BFF-Managed Field Readers` holds Read only (4/0/0) and `Writers` 4/4/4, and that no other
  profile (System Administrator excepted) can create or update it — a missing column or a wrong lock refuses
  `secure_setup.invite_marker_lock_incomplete` (Resumable: import SpaarkeMaster) or
  `secure_setup.invite_marker_lock_other_writer` (QuarantineRequired);
- the same check for `contact.sprk_externalinvitecodehash` (unified-access-control-r2 task 185) once a SpaarkeMaster release
  carries it: secured, `Writers` 4/4/4, and no access for `Readers` (no row or 0/0/0) or any other profile (System
  Administrator excepted), else `secure_setup.invite_code_hash_lock_incomplete` (Resumable) or
  `secure_setup.invite_code_hash_lock_other_access` (QuarantineRequired). While the column is absent that is the expected
  state: H7b does not fail, and records an "absent — ships in a later SpaarkeMaster release" note in the `notes` of gate
  `h7b-secure-record-setup`. Neither check writes a field-security row; auditing and Advanced Find on the hash column are
  not verified by H7b;
- the identity-link lock check (S18) covers `contact.sprk_identityplane` and the five `sprk_identitycollision*` columns
  as well as the binding columns: secured, `Identity Link Readers` 4/0/0, `Writers` 4/4/4, nobody else may create or
  update (`secure_setup.identity_link_*`);
- the standing-grant lock check (S19) covers two columns:
  - `contact.sprk_standinggrant`: secured, `Standing Grant Administrators` 4/4/4, and no other profile (System
    Administrator excepted; the Readers profile is not) may create or update it, else
    `secure_setup.standing_grant_lock_incomplete` (Resumable: import SpaarkeMaster) or
    `secure_setup.standing_grant_lock_other_writer` (QuarantineRequired);
  - `contact.sprk_accesspermissiongrant` (#2317): secured, `Standing Grant Administrators` 4/4/4,
    `BFF-Managed Field Readers` Read (4/0/0), `BFF-Managed Field Writers` Read and Update (4/0/4), and no other profile
    (System Administrator excepted; the Readers profile is not) may create or update it, else
    `secure_setup.access_level_lock_incomplete` (Resumable) or `secure_setup.access_level_lock_other_writer`
    (QuarantineRequired);
- read-only checks (S22, #2317) for the columns a later SpaarkeMaster release carries. `contact.sprk_externalinvitedemailhash`
  and `contact.sprk_externalinvitedtenantid` follow the code-hash rule (secured, `Writers` 4/4/4, no access for anyone
  else), else `secure_setup.bff_only_column_lock_incomplete` (Resumable) or
  `secure_setup.bff_only_column_lock_other_access` (QuarantineRequired). `contact.sprk_externalaccessremovedon` follows
  the invite-marker rule (secured, `Readers` Read, `Writers` 4/4/4, no other writer), else
  `secure_setup.access_removed_on_lock_incomplete` (Resumable) or `secure_setup.access_removed_on_lock_other_writer`
  (QuarantineRequired). While a column is absent that is the expected state: H7b records the same "absent" note and
  does not fail. `scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Verify` (exit 0, §6.5.2) remains the gate for
  these columns before that release.

**Release ordering (#2317).** An H7b carrying these checks requires SpaarkeMaster >= 1.2.4.0 published before the
control plane is deployed. SpaarkeMaster 1.2.3.1 and earlier do not secure the identity plane, collision and access-level
columns, so against such an environment S18 refuses `secure_setup.identity_link_lock_incomplete` and S19
`secure_setup.access_level_lock_incomplete`, each naming 1.2.4.0. A new stamp imports 1.2.4.0 or later (H6) and the run
is resumed; in an existing environment run `scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Apply`, then resume.
H7b re-checks the locks itself and adds the BFF to Standing Grant Administrators. Run `-Verify` (exit 0, §6.5.2) after
H7b completes, before the BFF deploy.

An intake `secureRecordSetupDryRun: true` makes H7b read only. It records its plan in gate `h7b-secure-setup-plan` and
stops the run.

The operator's part is **verification**. Run the **§7 verification checklist** of
[`SECURE-PROJECT-ENVIRONMENT-SETUP.md`](./SECURE-PROJECT-ENVIRONMENT-SETUP.md) against this environment, and do not
report it as secure-record ready until every item passes. While anything is missing, the BFF refuses to make a record
secure (it fails closed). The containers are not part of this phase: the BFF creates each secure record's container
when the record is made secure (task 227g).

**Customer business unit (T259).** H10 creates the customer's own unit (intake `displayName`) directly under the root,
a sibling of `Secure Record`, with both BFF application users in it; H11 moves every guest into it before any role.
No user of any kind is placed in `Secure Record`. **H13 proves it on every run (T260, ISS-014)**: it asks the stamp
BFF to run its read-only isolation census — the same code as the BFF's 15-minute `secure-record-isolation-census` job —
and refuses `Ready` unless the answer is `isolated` (§7.10). Any human who reaches the `Secure Record` unit quarantines
the run with the census findings in its error detail; fix each finding, never by moving a user into `Secure Record`.

### 7.12 Phase 12 — Per-customer Copilot agent (operator, then the customer's IT; after Ready)

Each customer gets its own Microsoft Copilot agent, "Spaarke AI", bound to that customer's BFF only (owner, §9 Q2,
2026-09-28). The owner accepted the design on 2026-10-09:
[`t257-copilot-agent-design.md`](../../projects/customer-provisioning-orchestration-r1/notes/t257-copilot-agent-design.md)
§3 and §5.

**Where it runs.** The customer's IT installs the agent in the customer's **home** tenant, the same way they install
the Word and Outlook add-ins. Model 1 users are guests in Spaarke's tenant, and Microsoft documents no way for a guest
to use an agent from the tenant where they are a guest. **Never publish a customer's agent in Spaarke's own catalog.**
That catalog holds only the agents for Spaarke's own environments (`scripts/Deploy-CopilotAgent.ps1`).

**How it reaches only that customer's BFF:**
- The agent signs the user in to **Spaarke's** tenant. This is the same guest identity the add-ins use (`tid` =
  Spaarke, `acct` = 1).
- Sign-in uses OAuth 2.0 + PKCE through one shared client app per control-plane environment, "Spaarke Copilot Agent -
  {Env}" (naming: `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` §Entra ID App Registrations). It is a public
  client with no secret. Dev: "Spaarke Copilot Agent - Dev", `3a36eac4-10c5-4b90-9a83-913138a466af` (created 2026-10-09).
- The client gets a token for `api://{customerBffAppId}/user_impersonation`. H3 pre-authorizes the client on every
  customer BFF app, so no user sees a consent prompt.
- Each customer has its own **auth config**, which binds that scope and the BFF's base URL. A package can't reach
  another customer's BFF.
- What the user sees is decided by the stamp's Dataverse roles, as for every other client.

The package has no `permissions`, `webApplicationInfo`, bot or knowledge capability. As a result:
- it needs no Copilot licence and no metering, so users with a Microsoft 365 plan that includes Copilot Chat can use
  it;
- updates install without a consent prompt.

**One-time platform setup (operator, Spaarke's tenant).** Each step is a live action and needs the owner's OK.

1. **Create the client app "Spaarke Copilot Agent - {Env}"** (e.g. `Spaarke Copilot Agent - Dev`) in the Entra admin center
   or through Graph (`POST /applications`, then `POST /oauth2PermissionGrants` with `consentType: AllPrincipals` for the
   admin consent — this works where `az ad app permission admin-consent` does not):
   - single tenant (Spaarke);
   - platform **Single-page application**, redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`;
   - **no client secret and no certificate** (ADR-028 A4; the project's no-secret rule);
   - API permissions: Microsoft Graph delegated `openid`, `profile` and `offline_access`, then **Grant admin consent**.
     `az ad app permission admin-consent` fails for this (see the project gotchas).
   - Add **no** permission for any customer BFF: H3's pre-authorization covers it.
   - If the first live test hits `AADSTS9002327` (cross-origin SPA redemption), add the same redirect under "Mobile and
     desktop applications" and remove the SPA one (design §2, item 3a).
2. **Add the registry columns** on the admin environment:
   `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1 -EnvironmentDomain <admin env host>`. The script is idempotent
   and adds `sprk_bffappid` and `sprk_copilotauthconfigid`.
   - Do this **before any** control-plane deploy that carries the T257 H13 (step 3, or any later L2 release). H13
     promotes `sprk_bffappid`, and Dataverse rejects the whole promoted-columns PATCH if the column is missing.
   - Prereq PRQ-E-14 checks all three columns before every run.
3. **Add its id to the control plane.** Set `param copilotAgentClientAppId = '<application id>'` in
   `infrastructure/bicep/parameters/platform-controlplane-{env}.bicepparam`. Then redeploy the control plane
   (`scripts/provisioning/Deploy-ControlPlane.ps1`).
   - The Worker then emits the client as the next `EntraAppRegOptions__PreAuthorizedClientAppIds__N`, and every H3 run
     pre-authorizes it.
   - A stamp provisioned **before** this redeploy gets the client on its next run, because H3 reconciles the list
     exactly.
4. **Publish the template.** Run `publish-copilot-agent-template.yml` on `master` with `publish: true`. It writes
   `copilot-agent-template-{version}.zip` and `copilot-agent-template-latest.json` to `provisioning-artifacts`.

**Per customer (after `Setup Status = Ready`; the `/provision-environment` post-Ready Copilot gate):**

5. **Check the pre-authorization (read-only).**
   `az ad app show --id <bffAppRegId> --query "api.preAuthorizedApplications[].appId"` must list the Spaarke Copilot
   Agent client id.
6. **Create the customer's auth config.** This is a delegated step under the operator's own Microsoft 365 sign-in; no
   API exists for it yet (design §2, item 2). In the Teams developer portal, go to Tools → OAuth client registration →
   **New** and set:
   - registration name `spaarke-copilot-{customerId}`. If one with that name already exists, reuse it; never create a
     second one;
   - base URL: the customer's BFF, `https://{sprk_appservicename}.azurewebsites.net`;
   - "Restrict usage by org": **Any Microsoft 365 organization**, because the agent runs in the customer's tenant;
   - "Restrict usage by app": **Any Teams app**. The first live test (design §3, step 8) tries restricting it to the
     customer's derived manifest id;
   - client id: the Spaarke Copilot Agent client id. **Leave the client secret empty**;
   - authorization endpoint `https://login.microsoftonline.com/{SpaarkeTenantId}/oauth2/v2.0/authorize`;
   - token and refresh endpoints `https://login.microsoftonline.com/{SpaarkeTenantId}/oauth2/v2.0/token`;
   - scope `api://{bffAppRegId}/user_impersonation offline_access`;
   - PKCE **on**.

   **The portal form cannot save these values (T186 ACME, 2026-10-28):** it marks the client secret required, and the
   Spaarke Copilot Agent client has no secret (SPA redirect + PKCE), so a dummy value would make every sign-in fail.
   Use the Agents Toolkit `oauth/register` instead, which takes no secret when `isPKCEEnabled: true`. Copy
   `provisioning-runs/acme-b6941254-ac1b-42a6-8b12-dec7587090fc/copilot-auth-config/m365agents.yml` into the
   customer's run record, change the name, base URL and scope, then run (operator's own Microsoft 365 sign-in):
   `npm install -g @microsoft/m365agentstoolkit-cli`, `atk auth login m365 --tenant {SpaarkeTenantId}`,
   `atk provision --env {customerId} --folder {that folder}`. The id lands in `env/.env.{customerId}` as
   `SPAARKE_COPILOT_AUTH_CONFIG_ID` — commit that file: the toolkit skips creation only while that variable has a
   value, so a run from a fresh folder creates a second auth config. Scope is comma-separated there; `appId` is
   required by the driver and unused with `AnyApp`. The portal's **Client password authentication method** does not
   apply (no secret). The portal still shows, edits and deletes the auth config. **Sign in with the Spaarke work
   account**, not a personal Microsoft account: the browser reuses whatever account is already signed in, and
   `--tenant` does not stop it. A personal account fails `oauth/register` with `Request failed with status code 401`;
   `~/.fx/account/homeId.cache.appStudio` then ends in the consumer tenant `9188040d-6c67-4c5b-b112-36a304b66dad`.
   Fix: `atk auth logout m365`, sign in again choosing the work account, re-run `atk provision` (safe — nothing was
   created).
7. **Record the id** on the customer's registry row. PATCH `sprk_copilotauthconfigid` on `sprk_dataverseenvironment`
   with your own identity (`pac data update` or the Web API). The id is not a secret; re-renders and decommission read
   it from the row.
   - If the row has no `sprk_bffappid`, set it in the same PATCH. This happens for a stamp that reached Ready before
     T257, or when H13's promoted-columns PATCH failed. The value is the run's `interStepState.bffAppRegId`, or the
     `appId` of the stamp's BFF app registration.
   - Without it, the render refuses the row, and `-AllActive` skips the row and exits non-zero.
8. **Render the package.** Everything here is read-only except the local file it writes.
   - Download `copilot-agent-template-latest.json` and the template it names (`az storage blob download --auth-mode login`).
   - Run:

     ```powershell
     scripts/copilot-agent/Render-CopilotAgentPackage.ps1 -TemplatePath <zip> -TemplateManifestPath <latest.json> `
         -OutputFolder <folder> -CustomerId <customerId> -AdminEnvironmentUrl <admin env URL>
     ```

   - The result is `spaarke-copilot-{customerId}-{version}.zip`. Its manifest id is UUIDv5 of the customerId, so every
     version updates the same app in the customer's catalog.
9. **Send the package to the customer's IT** with
   [`COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md`](COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md). They upload it in **their**
   Microsoft 365 admin center (Agents → Upload custom agent) and assign it to the staff they invited as guests.
10. **Verify** with one invited guest:
    - The guest opens Copilot in their home tenant → Spaarke AI → "What are my overdue tasks?" → signs in.
    - The BFF log shows `tid` = Spaarke, `acct` = 1, `azp` = the Spaarke Copilot Agent client and `aud` = this
      customer's BFF app.
    - Record the result in `runs/{runId}.md`.

**Releases.** Change the agent only when its instructions or its OpenAPI surface change; tool behaviour lives in the
BFF.
1. Bump `version` in `src/solutions/CopilotAgent/appPackage/manifest.json`.
2. Merge, then publish the template.
3. Render for every active, Ready customer: `Render-CopilotAgentPackage.ps1 -AllActive -AdminEnvironmentUrl <admin env URL> ...`.
4. Send each package to that customer's IT. Users get the update without a prompt.

**Decommission.**
1. The customer's IT removes the app.
2. The operator deletes the auth config in the Teams developer portal, which is the only place to delete one.
3. The registry row is deactivated, never deleted.

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
| **T1** | App Service `keyVaultReferenceIdentity` not set to UAMI → KV references fail post-swap | H2a (set by `customer.bicep`; H4 re-asserts) | ARM read `keyVaultReferenceIdentity == UAMI-resource-id` |
| **T2** | Dataverse App User for UAMI not registered → BFF loses Dataverse access post-swap | H10 | `systemusers?$filter=applicationid eq {uami-app-id}` returns 1 |
| **T3** | UAMI SP missing a Graph app role → Graph calls fail with 403; or holding one outside the stamp set → tenant-wide reach into Spaarke's tenant | H10 | UAMI SP Graph `appRoleAssignments` are exactly the Entra-granted roles of `GraphAppRoles.cs` (task 261) |
| **T4** | Stamp identity missing its group-scoped Exchange mailbox roles (Mail.* calls 403), or holding one outside the group (reaches other customers' mailboxes) | H14(a) | H13 reads the identity's assignments via the sidecar: every `Application Mail.*` role in scope, none outside |
| **T5** | Slot MI vs slot MI KV RBAC parity broken → cold-start KV-ref failure after slot swap | H4 (interim); H10 + Phase C UAMI (structural) | Both slot MIs have KV RBAC (interim); **structurally impossible post-Phase-C** |
| **T6** | SPE container work uses a delegated token → 403 "public client not allowed" | H8 | H13 lists `GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}` app-only as the owning app (through the Worker UAMI's federated credential) and passes only if the run's container (H8 output) is in the list. Container absent or a delegated-token refusal → Failed; other refusals / 404 / errors → InfraFault |
| **T7** | `Customer__Id` missing, blank or another customer's id on either BFF slot → the BFF runs on the derived-from-resource-group path, or names the wrong customer (§6.5.1); **and (T255)** the workforce tenant list missing, stale or different on either slot → every customer employee's contact-only sign-in is denied, or the wrong organisation is admitted (§6.5.2) | H4b (writes both slots, removes stale indices) | ARM read of both slots' app settings: `Customer__Id` == run customerId (T238); `WorkforceIdentity__CustomerTenantIds__*` == the run's `customerWorkforceTenantIds` as a set, none equal to `AzureAd__TenantId` on Model 1 (T255); **and (T264)** the BFF app registration lacks `authenticationBehaviors.removeUnverifiedEmailClaim = true` → an unverified-domain email can bind to a contact (§7.3) — Graph v1.0 read of `applications(appId='{BffAppRegId}')` in the run's tenant: the flag must be `true` (missing app, null or false fails; a Graph read error is an InfraFault, never a pass); **and (T265)** `ExternalAccess__CustomerBusinessUnitId` missing, blank, not a GUID or different from H10's customer business unit on either slot → the external registration answers 500 `registration_owner_unconfigured` and first-sign-in contact creates are refused — the same two slot reads: the setting must equal the run's `InterStepState.CustomerBusinessUnitId` as a GUID (a run with no H10 unit fails; H4b writes it) |

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

### 10.5 Upgrade note: `sprk_signal.sprk_regardingrecordtype` changed from text to a lookup (2026-10-12)

**Who is affected.** Any environment that already has the `sprk_signal` table from a SpaarkeMaster package built before
this change (1.2.0.0 and 1.2.1.0 both carry the old `String(100)` column). A solution import **cannot change a
column's type**: importing the new version over the old column fails (or leaves the text column in place), and the
Signal writer then fails on every create. As of 2026-10-12 the only environment that has `sprk_signal` is
**spaarkedev1** (unmanaged, already converted); `spaarke-demo`, `spaarke-model1-prod` and the Teams default environment have no
`sprk_signal`, `sprk_decisionrecord` or `sprk_policy`, so they take the new package as a first install and need nothing
here. Re-check with the query in step 1 before every upgrade; do not rely on this list.

**What changed.** `sprk_signal.sprk_regardingrecordtype` is now a Lookup to `sprk_recordtype_ref` (the ADR-024 shape
`sprk_todo`, `sprk_event` and `sprk_communication` use), same logical name. `sprk_decisionrecord` gains the 14 standard
regarding columns (nine subject lookups, `sprk_regardingrecordid`, `sprk_regardingrecordtype`,
`sprk_regardingrecordname`, `sprk_regardingrecordnumber`, `sprk_regardingrecordurl`); additive, no action needed.

**Ordering: the 140 schema before the BFF (#1780 K1).** The `sprk_decisionrecord` `sprk_regarding*` columns above must reach an environment **before** any BFF build that contains the Decision Record lineage row (unified-access-control-r2 task 182). `SecureChildShareSynchronizer` and `SecureChildReconciliationJob` select those columns without a probe, so a BFF deployed ahead of the schema fails the whole secure-child load (every secure Signal and Decision Record stops being re-owned or mirrored, not just the Decision Records). Import the SpaarkeMaster version that carries the columns first (in dev, by the scoped import only: import, then publish only the imported components, D-83 and #1618; never a full SpaarkeMaster import), run the full column check below, and only then deploy the BFF.

**Full column check (ontology task 035 step 3.5, `projects/spaarke-ontology-platform-r1/tasks/035-deploy-bff-evaluator.poml`).** Read the TARGET environment's metadata (`GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_decisionrecord')/Attributes?$select=LogicalName,AttributeType`) and confirm that every one of these 14 `sprk_decisionrecord` columns exists with the stated type: nine `Lookup` columns `sprk_regardingcommunication`, `sprk_regardingdocument`, `sprk_regardingevent`, `sprk_regardinginvoice`, `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingservicerequest`, `sprk_regardingtodo` and `sprk_regardingworkassignment`; four `String` columns `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber` and `sprk_regardingrecordurl`; and `sprk_regardingrecordtype` (`Lookup`). Then confirm the TARGET of `sprk_regardingrecordtype` on BOTH `sprk_decisionrecord` and `sprk_signal` (`GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_decisionrecord')/Attributes(LogicalName='sprk_regardingrecordtype')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=Targets` returns `sprk_recordtype_ref`; the same on `sprk_signal`). The BFF selects the Decision Record lookups at `SecureChildLineage.cs:100-104`: the typed core lookups `sprk_matter`, `sprk_project` and `sprk_workassignment`, and eight of the nine `sprk_regarding*` lookups (all but `sprk_regardingservicerequest`). The task 035 check still requires all 14 plus the target, because a failed or partial schema deployment can leave `sprk_regardingmatter` present and another column absent. Any missing column or wrong type: STOP and apply the 140 runbook (below) first, with owner approval.

**What a missing column does.** It stops mirroring and re-owning for **every secure-family child table**, not only for Decision Records: `SecureChildShareSynchronizer.LoadSecureChildrenAsync` reads all child tables of `SecureChildLineage.Children` in one pass, and the recent-changes pass of `SecureChildReconciliationJob` queries them all too, so one absent column faults the whole load. Symptom: the BFF logs `[SECURE-CHILD-SHARES] The secure children could not be read (scope all, on the full pass; a root-scoped run names the root); nothing was synchronized.` and `[SECURE-CHILD-RECONCILE] The recently changed related records could not be listed or placed since ...`, the scheduled and the 2-minute passes report failure, and secure Signals, Decision Records, communications, documents and the other child rows are no longer re-owned to the Secure team or given their root's sharees (a record re-filed under a secure root stays readable to its old business unit). Recorded by ontology task 146 (from the code; a missing column was not provoked on dev) (`projects/spaarke-ontology-platform-r1/notes/146-live-gate.md`).

**Which runbook.** SpaarkeMaster is **managed by default** in customer environments (ADR-027;
`docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`), so the managed runbook is the normal case. Check the environment's
`ismanaged` flag on the `SpaarkeMaster` solution row first and follow exactly one of the two.

> **Status of the behaviour below: expected, not tested.** No solution import was run for this change (D-83, #1618). That a
> type-changing import fails, and that a `StageAndUpgrade` that drops the column deletes it, is the documented Dataverse
> behaviour, not something this project observed. Treat the first managed upgrade as the test and have a backup.

#### A. Unmanaged environment (e.g. spaarkedev1)

Delete the column through the API, then import or recreate. Do this before importing the new SpaarkeMaster version:

1. **Check it needs doing.** `GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_signal')/Attributes(LogicalName='sprk_regardingrecordtype')?$select=AttributeType`
   returns `String` (needs the runbook), `Lookup` (already done), or 404 (table absent: nothing to do).
2. **Stop Signal writers.** Confirm no Signal job is writing: the Signal writer is only live where
   `Ontology:Writer:ManagedIdentityClientId` is set on the BFF; clear it (or stop the app) for the window.
3. **Decide about existing rows.** The old column held the subject's logical name as text; the new lookup needs the
   matching `sprk_recordtype_ref` row. Dev rows were test data and were dropped with the column. For a non-dev
   environment, export the rows first (`sprk_signalid`, `sprk_regardingrecordtype`) and back-fill the lookup after the
   import by mapping each text value to the `sprk_recordtype_ref` whose `sprk_recordlogicalname` equals it.
4. **Read-only dependency check.** `GET RetrieveDependenciesForDelete(ObjectId=<attribute MetadataId>,ComponentType=2)`
   must return an empty `value`; also confirm no alternate key, view, form, chart or flow uses the column
   (`sprk_signaldedupekey` covers `sprk_dedupekey` only). Any dependency: stop and escalate; do not delete.
5. **Delete the String column** (`DELETE EntityDefinitions(LogicalName='sprk_signal')/Attributes(<MetadataId>)`).
6. **Import the new SpaarkeMaster version.** The import creates the lookup under the same logical name. If it does not,
   create it by the Web API recipe (`POST RelationshipDefinitions`, lookup schema name `sprk_RegardingRecordType`,
   referenced entity `sprk_recordtype_ref`, relationship `sprk_signal_regrecordtype_recordtype_ref`, header
   `MSCRM.SolutionUniqueName`) and publish `sprk_signal` and `sprk_decisionrecord` only (`PublishXml`).
7. **Verify** the metadata read now returns `Lookup` with `Targets = [sprk_recordtype_ref]`, then re-enable the writers
   (they must be on a build that writes the lookup, task 141) and clear the cached entity metadata
   (`sdap:dv:entitymetadata:sprk_signal` and `sprk_decisionrecord`, up to 6 h in Redis) in the same window.

Between steps 5 and 6 the column is absent; keep that gap short and do not run Signal jobs inside it.

#### B. Managed environment (the default)

**Do not delete the column through the API and do not recreate it with the Web API.** In a managed environment the column
belongs to the managed SpaarkeMaster layer: the API refuses the delete, and a column created by the Web API fallback would
sit in an unmanaged layer under the same name and collide with the packaged lookup on the next import. The only supported
way to remove a managed column is to ship a SpaarkeMaster version that no longer contains it and apply that version with
`StageAndUpgrade` (H6 does; "a component removed from the package is deleted together with its data"). So the change
goes out in **two SpaarkeMaster versions**:

1. **Check it needs doing** (step 1 above). `Lookup` or 404: nothing to do. `String`: continue.
2. **Stop Signal writers, decide about existing rows, run the read-only dependency check** (steps 2-4 above). The
   `StageAndUpgrade` in step 4 deletes the column with its data, so export the rows (`sprk_signalid`,
   `sprk_regardingrecordtype`) first if any exist.
3. **Ship an intermediate SpaarkeMaster version WITHOUT `sprk_signal.sprk_regardingrecordtype`.** Build it from the source
   with that attribute removed from `Entities/sprk_Signal/Entity.xml` (everything else unchanged). The published 1.2.x
   packages, which carry the `String` column, are the **old** side of this sequence: the intermediate version must be
   higher than the installed one (H6 refuses a downgrade) and is a **Major** bump because it removes a component.
4. **Apply it with `StageAndUpgrade`.** This deletes the `String` column. Verify the step-1 query now returns 404.
5. **Ship the final SpaarkeMaster version WITH the lookup** (the source on this branch: `sprk_RegardingRecordType` ->
   `sprk_recordtype_ref`, relationship `sprk_signal_regrecordtype_recordtype_ref`), higher again, and apply it with
   `StageAndUpgrade`. Creating the lookup is then an ordinary additive change.
6. **Verify** the metadata read returns `Lookup` with `Targets = [sprk_recordtype_ref]`, back-fill the lookup from the
   exported rows if there were any, then re-enable the writers (a build that writes the lookup, task 141) and clear the
   cached entity metadata (`sdap:dv:entitymetadata:sprk_signal` and `sprk_decisionrecord`, up to 6 h in Redis).

Between steps 4 and 5 the column is absent; keep that gap short and do not run Signal jobs inside it. Both versions are
cut at release time (`Assemble-SpaarkeMasterSolution.ps1 -Version`); this task did not bump the version, and the source
still carries `IntroducedVersion` `1.0.0.0` on the new components.

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

# SpaarkeMaster (managed — ADR-027 §3): H6 does this in a provisioning run; by hand, the same package and rules:
.\scripts\solution-authoring\Import-SpaarkeMasterPackage.ps1 -EnvironmentUrl "<dv-org-url>" -PackageType managed

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
# Phase 7b (Set-ExternalFlagForB2BGuests.ps1) takes -CustomerTenantIds on a Model 1 stamp — see Phase 7b below.
# EXISTING environment receiving the BFF that carries unified-access-control-r2 task 114: run Phase 7b (all
# three steps, -Verify exit 0) BEFORE this deploy. That BFF reads a BLANK sprk_isexternal as internal, so a B2B guest still
# blank when it starts is shared with on Restricted records and receives internal-only messages. (A NEW environment has
# no users yet: Phase 7b follows H11 below, before anyone is given access.)
.\scripts\Deploy-BffApi.ps1 -CustomerId "acme" -Slot production

# Phase 7 — Dataverse App User + Graph app-role parity
# Interim: PPAC UI + Graph SDK; see auth-deployment-setup stub for MI-first checklist
# T2 verification MANDATORY: systemusers?$filter=applicationid eq {uami-app-id} returns 1

# Phase 7b — B2B guests who are outsiders are flagged external (unified-access-control-r2 task 114, owner round 67;
# Model 1 rule: spaarkeai-word-add-in-r1 task 126, owner 2026-10-13 — per environment, after the users exist, and again
# whenever B2B guests are added outside the product). A BLANK sprk_isexternal means NOT external: Manage Access "+ User"
# and the Assigned-To rule share with the user, internal-only messages reach them, and a Restricted record keeps their
# share. The script requires a mode (§7.7, after the H11 notes):
#   MODEL 1 stamp: -CustomerTenantIds = the stamp's WorkforceIdentity__CustomerTenantIds__N values (read them with the
#     §12.3 query). The customer's own staff (guests of a listed home directory, or in -InternalUserIds) are never
#     written; a guest of another directory not already Yes is set to Yes (No is the column default, not a decision);
#     an unresolvable or inconsistent guest is listed as "review" and never written.
#   Every guest an outsider (not Model 1): -AllGuestsExternal (sets every #EXT# user still blank or No to Yes).
# Dry run first and review the report with the owner, then -Apply, then -Verify (exit 0). A "review" row fails -Verify
# until it is settled (§7.7): an outsider gets Yes by hand; customer staff go in -InternalUserIds (same list every run).
# ORDER: in an EXISTING environment this runs BEFORE Phase 6 deploys the BFF carrying task 114 (the BFF before it already
# treats a blank flag as external, so running this first changes nothing for it). If that BFF was deployed first: run
# this at once, and allow 10 minutes after -Apply — the identity resolver caches sprk_isexternal for 10 minutes, and that
# cached answer also decides a licensed user's Restricted-record access on the SPA/Teams plane.
# Within 5 minutes of -Apply the BFF removes each newly flagged guest's share on every Restricted record.
.\scripts\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "<dv-org-url>" -CustomerTenantIds "<customer-tenant-guid>"   # Model 1 dry run (read-only)
.\scripts\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "<dv-org-url>" -CustomerTenantIds "<customer-tenant-guid>" -Apply
.\scripts\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "<dv-org-url>" -CustomerTenantIds "<customer-tenant-guid>" -Verify   # MUST exit 0
# Not Model 1: replace -CustomerTenantIds "<…>" with -AllGuestsExternal in all three commands.

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

# T3 — Graph app-role parity: exactly FileStorageContainer.Selected on Microsoft Graph, and nothing else (no Mail.* — those are Exchange-scoped)
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
# T7 (T255) — the workforce tenant list on BOTH slots == the run's customerWorkforceTenantIds (§6.5.2)
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --query "[?starts_with(name, 'WorkforceIdentity__CustomerTenantIds')].[name,value]" -o tsv
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --slot staging --query "[?starts_with(name, 'WorkforceIdentity__CustomerTenantIds')].[name,value]" -o tsv
# T7 (T265) — ExternalAccess__CustomerBusinessUnitId on BOTH slots == H10's customer business unit (the run's InterStepState.CustomerBusinessUnitId)
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --query "[?name=='ExternalAccess__CustomerBusinessUnitId'].value" -o tsv
az webapp config appsettings list --name spaarke-bff-{customer}-{env} --resource-group rg-spaarke-{customer}-{env} `
    --slot staging --query "[?name=='ExternalAccess__CustomerBusinessUnitId'].value" -o tsv
# T7 (T264) — the BFF app registration strips unverified emails (signed in to the run's tenant — Spaarke's for Model 1; expect true)
# ($filter form: az.cmd's batch wrapper breaks on the bare ")" of applications(appId='…'); authenticationBehaviors is
#  returned only when named in $select, so `az ad app show` does not show it)
az rest --method GET --uri "https://graph.microsoft.com/v1.0/applications?`$filter=appId eq '{bffAppId}'&`$select=authenticationBehaviors" `
    --query "value[0].authenticationBehaviors.removeUnverifiedEmailClaim"
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

### 12.8 FR-20 — let Dataverse decide internal users' SPA/Teams access

> Added by `unified-access-control-r2` task 036 (FR-20; owner rules C9 / D1, round 59, round 75). Setting:
> `ExternalAccess__ImpersonatedRootSets__Enabled` (App Service). It ships **off**. Design and decisions:
> `projects/unified-access-control-r2/notes/036-fr20-implementation.md`; ADR-034 Amendment A2.

The owner rule: an internal user (a systemuser) who can open a record in the model-driven app can open it in the SPA and
in Teams, with the same rights, and a user Dataverse does not admit cannot. The setting changes **systemusers only**; CIAM
contacts and workforce contacts are decided exactly as before in both states.

| | Off (the default; transitional) | On (the end state) |
|---|---|---|
| Records an internal user sees | The BFF's membership rules (ownership, owning team, owning business unit, the "Assigned *" contacts), each at Read / Write / Create | Dataverse's own answer, read AS the user — one id-only read per root type (project, matter, work assignment), cached 2 minutes — each at Read |
| A write (create to-do, upload, create event, edit to-do, post a message, delete own) | Decided by the rights above | The BFF also asks Dataverse for the user's rights on the target record, app-only, impersonating the user's systemuser (one call per write, for every internal user however they signed in — precondition 3). A child (to-do, document, event, message) needs AppendTo on the parent **and** the user's Create privilege on the child table (`prvCreatesprk_Todo`, `prvCreatesprk_Document`, `prvCreatesprk_Event`, `prvCreatesprk_Communication`; a second call); an edit needs Write on the to-do; a delete-own needs Delete on the row as well as the creator stamp. A service request's writes stay the requester rule in both states (accepted, decision e) |
| Contact grants of a user who is also a granted contact | Kept | Kept — Dataverse cannot see them |
| No Access list, Restricted veto | In force | In force |
| A root set over 5,000 records | Module data responses carry `"truncated": true` | Same; the 5,000 kept are the most recently modified (task 036 decision (f), owner 2026-10-18) |
| A Dataverse read that fails | — | The request fails, or the write is refused. There is no fallback to the membership rules |

**Off does not close the business-unit over-grant of the membership rules.** It is the transitional state; on is the end
state.

**Preconditions — every one, per environment, before turning it on:**

1. Tasks **142** (Assigned-To access materialized as Dataverse shares) and **143** (the No Access list applied to internal
   users in Dataverse) are complete and deployed to the environment. Without them, turning it on removes internal users'
   "Assigned To *" access and the internal half of the No Access list.
2. Both Dataverse App Users hold `prvActOnBehalfOfAnotherUser` and are not narrowed (§7.7).
3. **How a write is confirmed — settled, task 036 decision (a), owner 2026-10-18, widened by option C, owner
   2026-10-20; nothing to do beyond item 2.** The BFF never exchanges an SPA/Teams user's token on this plane (ADR-028
   A1–A3), and a token issued by a customer's directory (WorkforceHome, ADR-028 A9) could not be exchanged for a
   Dataverse token in Spaarke's tenant anyway. For **every** internal user — a Spaarke-tenant sign-in or one resolved
   through the stored home link — the BFF asks app-only, as the Dataverse App User, naming the user's systemuser:
   `RetrievePrincipalAccess` with `Principal = systemusers(<id>)` on the target record, and
   `RetrieveUserSetOfPrivilegesByNames` on that systemuser for the child table's Create privilege (both impersonating the
   user, like the read, so item 2's `prvActOnBehalfOfAnotherUser` covers them). Dataverse answers from the user's own
   roles, teams and shares, so such a user is expected to write what the same user could write in the model-driven app,
   and their lists' `canCreate` is the same answer. That parity is **not yet proven live** for roles the user holds only
   through a team, or for disabled and unlicensed users: the task 036 live checks (Verify, (vi) and (vii)) cover them.
   A record created seconds earlier that answers "not found" is asked again (twice, within about 1.6 s); a fault, an
   impersonation failure (`0x8004A110`, or `0x80040220` whose message names `prvActOnBehalfOfAnotherUser`; logged as a
   Warning, not as "no rights") or no answer within 15 seconds refuses the write.
4. **Internal users who write in the SPA or Teams hold Spaarke Core User** (or a role with the same privileges). Under
   "on", Dataverse decides writes from the user's own roles, as the model-driven app does. Spaarke Core User grants
   Create, Write and Delete on `sprk_todo`, `sprk_document`, `sprk_event` and `sprk_communication`. **Spaarke Basic User**
   grants them on `sprk_communication` only (Basic depth), and **Spaarke Office Add In User** grants only Create and Write
   on `sprk_event`. A user whose only role is one of these two keeps reading. A Basic User can post messages but loses
   the to-do, document and event writes; an Office Add In User keeps creating events (the role also grants AppendTo on
   `sprk_project` and `sprk_matter`) but loses the to-do and document writes and delete-own on events — exactly as in the
   model-driven app. Check the roles of SPA/Teams writers before turning it on.
5. **The task 034 negative canary passes against the environment, in this rollout** (`tests/integration/auth/README.md`
   § Provisioning the canary user, then § Running it):

   ```bash
   export SPAARKE_CANARY_DATAVERSE_URL="https://<env>.crm.dynamics.com"
   export SPAARKE_CANARY_SYSTEMUSERID="<an existing non-admin test user's systemuserid>"
   export SPAARKE_CANARY_SEEDED_MATTER_IDS="<the matters that user's role depth admits, derived app-only>"
   export SPAARKE_CANARY_REQUIRED=true
   export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
   export SPAARKE_TESTS_ALLOW_OUTBOUND=1
   az login   # as an identity that maps to the BFF Dataverse App User

   dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
     --filter "FullyQualifiedName~ImpersonationNegativeCanaryTests"
   ```

   🔴 **PASS means the impersonated set is STRICTLY SMALLER than the app-only set. Equal sets are a FAILURE**:
   impersonation is inert and the read is returning every row the App User can read, which looks exactly like success.
   A NOT RUN is not a pass. Record both set sizes, the user's roles and how the expected set was derived in
   `projects/unified-access-control-r2/notes/task-034-negative-canary.md`. On a failure, stop: do not turn it on.
6. **SPA affordances under "on" (settled, task 036 decision g).** `/me` reports such a user's levels at Read (the set
   confers Read) and adds `writeAccessPerRecord: true`. The SPA then takes a record's create and upload controls from
   that record's own list, which asks Dataverse once for the record in hand (`canCreate`: Add Task and Create Event on a
   project, Upload Document on a project, matter, work assignment or invoice). Download and version history are offered
   wherever a document list rendered (they need only Read), and a project to-do's status toggle is offered and decided by
   the edit route itself (a refusal reverts it and says so). Remaining limits: Invite User and the issued-grants list stay
   hidden for such a user (contact grants are a contact's action; the route refuses a systemuser); to-do and event create
   is offered only on a project page, as before; each single-record list costs one extra Dataverse call for such a user.

**Turn it on:**

```bash
az webapp config appsettings set --name <app> --resource-group <rg> \
  --settings ExternalAccess__ImpersonatedRootSets__Enabled=true
```

App Service restarts the app on a settings change. Cached principals and root sets lapse within 2 minutes.

**Verify:**

- On an internal user's next request, the BFF logs `[WF-AUTHZ] Access source for systemuser … : ImpersonatedRootSet`
  (Application Insights: `traces | where message has "Access source for systemuser"`). `Membership` there means the
  setting did not apply.
- Run the task 036 live checks with an existing non-admin test user in the right business unit, and record each record id
  in the task notes: (ii) a record the user can Read but not Write in the model-driven app is listed and refuses a write
  with 403; (iii) a record in the user's business unit (or owned by its default team) but outside the user's role depth is
  not listed; (iv) a record whose "Assigned To *" names the user's linked contact is listed (task 142's share); (v) a
  secure record with the user on its No Access list is refused in both the model-driven app and the SPA/Teams; (vi) in a
  tenant whose internal users sign in through WorkforceHome, sign in to the SPA as such a user and repeat (ii): the
  write the model-driven app refuses is refused with 403, the one it allows succeeds, and the BFF log for it says
  `answered via app-only-named-principal`; (vii) with one disabled and one unlicensed test systemuser, a write is
  refused (if Dataverse answers either with rights, stop: do not turn it on, and report it), and with a user whose only
  role comes through a team, the SPA allows exactly the writes the model-driven app allows.
- A Warning `[DELEGATION-RPA-UNAVAILABLE] App-only RetrievePrincipalAccess … answered a fault, not an access answer`
  means the BFF App User lacks `prvActOnBehalfOfAnotherUser` (item 2): every write is refused until it is fixed. Microsoft
  documents no error code for that fault, and a bare `0x80040220` can also carry it; so if EVERY systemuser's
  confirmation logs `[DELEGATION] Rights … : None`, check the privilege before reading it as a roles problem.

**Turn it off (the kill switch):** set `ExternalAccess__ImpersonatedRootSets__Enabled=false`, or delete the setting. The
next composition uses the membership rules again. The "on" state writes nothing, so there is nothing to undo.

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

`scripts/naming-conformance-check.ps1` exits non-0 in the Tier 1 `naming-conformance` job (`ci-tier1-blocking.yml`; a build gate since T230a — H13 no longer runs it per
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
| Per-customer Copilot agent (T257) | §7.12 above; for the customer's IT: [`COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md`](COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md) |
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
| 2026-10-08 | §5 handler table: **H4b** row added — the BFF app settings are written through the ARM SDK on both slots (merge, never replace), no longer by running the generated Configure script; **H6** row — the org-settings contract is one Dataverse Web API PATCH and H6 installs no application. The L2 Worker runs no shell tool (G38) | `customer-provisioning-orchestration-r1` T253 |
| 2026-10-20 | §7.4: the document Access Permission schema — `scripts/Set-DocumentAccessPermissionSchema.ps1` (`sprk_document.sprk_accessinheritance`, not field-secured) runs BEFORE a BFF carrying task 189 is deployed; until it has, that BFF only raises a document's Access Permission to what its parents give and never lowers one (documents listed under `tablesWithoutColumn`, raises counted in `raisedOnly`). A raise kept in that window is recorded as the document's own on the first run after the script and is never widened again: the schema must go first, and §7.4 says how to find such documents and widen them by hand | `unified-access-control-r2` task 189 (D7 T7a, #2126; security pass 2) |
| 2026-10-22 | §6.5.2: `scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify` must exit 0 before deploying a BFF that stamps a new table — the BFF trusts `sprk_createdbyperson` only on stamped tables, and only a field-secured column makes that safe; source guard `CreatorPersonColumnSecuredGuardTests` | #2273 fix round 1 (PR #2277) |
| 2026-10-22 | §5 H3 row + §7.3 + §9 T7 + §12.3: `authenticationBehaviors.removeUnverifiedEmailClaim = true` set by H3 on every BFF registration (`appreg-unverified-email-claim-failed`) and verified by H13 trap T7 | `customer-provisioning-orchestration-r1` task 264 (#2001 from SPA-r3 ISS-013) |
| 2026-10-22 | §9 T7 + §12.3 + H4b external-registration owner note: H13 trap T7 reads back `ExternalAccess__CustomerBusinessUnitId` on both slots (equal to H10's customer business unit) | `customer-provisioning-orchestration-r1` task 265 (#2265) |
| 2026-10-23 | §6.5.2: contact access-level and identity bookkeeping columns are field-secured — `scripts/Set-ExternalAccessContactSecuritySchema.ps1` (dry run, `-Apply`, `-Verify` exit 0) secures `sprk_accesspermissiongrant`, `sprk_identityplane` and the `sprk_identitycollision*` columns and adds three BFF-only columns; the H6 path for a new stamp applies once SpaarkeMaster 1.2.4.0 is published; §7.11: H7b S18 covers the plane and collision columns, S19 verifies the access-level lock and the whole standing-grant lock (Standing Grant Administrators 4/4/4; no other writer but System Administrator — `secure_setup.standing_grant_lock_other_writer`), S22 verifies the three BFF-only columns' locks once a release carries them; an H7b carrying these checks requires SpaarkeMaster >= 1.2.4.0 published before the control plane is deployed | `spaarke-SPA-external-access-platform-r3` (contact identity hardening, slice A) |
| 2026-10-23 | §6.5.2 rollout note: rollout order — the contact security schema is applied and `-Verify` passes (stamp columns and proof reads) on every environment BEFORE a BFF build that requires the invite stamp is deployed; without the stamp columns every email bind (CIAM and Workforce) is refused; an email bind needs a current invite stamp (invited address and, for work accounts, the invited tenant unless `xms_edov` is boolean true); invites take an optional `invitedTenantId` (GUID); Workforce contacts never invited through the BFF, or invited before this release, need a re-invite naming their tenant | `spaarke-SPA-external-access-platform-r3` (contact identity hardening, slice B) |

---

*Maintained by `customer-provisioning-orchestration-r1`. Update this guide (do not fork) when handler catalog / DAG / tenancy model / naming convention evolves. All updates should be per-project and cite the driving spec / FR / task ID.*
