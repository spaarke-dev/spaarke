# Azure resource-group review — 2026-08-30

> **Trigger**: owner observation — *"we now have Azure resources spread across different resource groups
> which does not seem like a good process."*
>
> **Method**: live `az` inventory across all 5 accessible subscriptions, cross-referenced against every
> RG name appearing in `infrastructure/`, `.github/`, `config/`, `docs/`, `scripts/`.
>
> **Status**: assessment only. **No resources were moved or modified.**

---

## 1. What actually exists

| Subscription | Resource group | Resources | Purpose |
|---|---|---:|---|
| **Spaarke Devlopment Environment** | `spe-infrastructure-westus2` | **28** | catch-all (see §2.2) |
| | `rg-spaarke-dev` | 8 | dev app: BFF site + plan, SignalR, ACS, external SPA, CIAM |
| | `rg-spaarke-platform-dev` | 11 | provisioning control plane (dev) |
| | **`rg-spaarke-platform-prod`** | **8** | **PRODUCTION** BFF + prod OpenAI/DocIntel + `api.spaarke.com` cert |
| | `rg-spaarke-website` | — | marketing site (eastus2) |
| | `SharePointEmbedded` | — | SPE (eastus) |
| | `DefaultResourceGroup-EUS` / `-EUS2` / `ai_appi-…managed` | — | Azure-created |
| **Spaarke Demo Environment** | `rg-spaarke-demo` | — | demo |
| **Spaarke Model 1 Production** | `rg-spaarke-trial01-prod-model1`, `rg-spaarke-shared-prod` | — | Model-1 customer stamp |
| **SPRK Power Platform 1** | `SPRK_DEV1` | — | Power Platform |
| **Spaarke Legal Rules Solution** | `Spaarke_Legal_Rules_Solution` | — | separate solution |

---

## 2. Findings, most severe first

### 2.1 🔴 Production workloads run inside the "Development" subscription

`rg-spaarke-platform-prod` lives in subscription **"Spaarke Devlopment Environment"** and contains:

```
spaarke-bff-prod            Web/sites          <- the production BFF
spaarke-bff-prod-plan       Web/serverFarms
spaarke-openai-prod         CognitiveServices  <- production AI
spaarke-docintel-prod       CognitiveServices
sprk-platform-prod-kv       KeyVault/vaults    <- production secrets
sprk-platform-prod-insights / -logs
api.spaarke.com             Web/certificates   <- production TLS cert
```

**Why this is the top finding.** The subscription is the strongest blast-radius, RBAC, policy, and cost
boundary Azure offers. Putting prod inside a dev subscription forfeits all four at once: anyone with dev
subscription access can reach production secrets and the production BFF, dev cost cannot be separated from
prod cost, and a subscription-scoped policy or budget action intended for dev applies to prod.

Note this is **separate** from the "Model 1 Production" subscription, which holds the customer stamps. So
there are two different production footprints in two different places, one of them mislabelled.

### 2.2 🟠 `spe-infrastructure-westus2` is a misnamed catch-all

28 resources, and the name describes almost none of them:

| Actually inside | Examples |
|---|---|
| Dev AI platform | `spaarke-openai-dev`, `spaarke-docintel-dev`, `spaarke-search-dev`, `spe-cosmos-dev-ai`, AI Foundry hub + project |
| Caches | `spe-redis-dev-67e2xz`, `spaarke-bff-redis-dev` (**two** — see §2.4) |
| **Company-wide DNS** | **`spaarke.com` DNS zone** |
| Identity | `mi-bff-api-dev` (the BFF's managed identity) |
| Client hosting | `spaarke-office-addins` static site |
| Compute | `insights-spaarkedev-func` + plan |
| Bot | `spaarke-bot-dev` |
| Monitoring | 2 App Insights, 2 Log Analytics, 4 alert rules |

Two consequences:

- **It cannot safely be treated as "dev".** The `spaarke.com` DNS zone is company-wide and production-critical. Any bulk action on this RG — a cleanup, a policy, an accidental delete — reaches DNS for everything.
- **The name actively misleads.** "spe-infrastructure" implies SharePoint Embedded; there are no SPE resources in it (SPE has its own `SharePointEmbedded` RG in eastus).

### 2.3 🟠 One logical application is split across two resource groups

The dev BFF is not self-contained:

| Component | Resource group | Region |
|---|---|---|
| `spaarke-bff-dev` (site + plan) | `rg-spaarke-dev` | westus2 |
| `mi-bff-api-dev` (its managed identity) | `spe-infrastructure-westus2` | westus2 |
| `spaarke-bff-redis-dev` (its cache) | `spe-infrastructure-westus2` | westus2 |
| `spaarke-openai-dev`, `spaarke-search-dev` | `spe-infrastructure-westus2` | westus2 |
| **`spaarke-spekvcert` (its SECRETS)** | **`SharePointEmbedded`** | **eastus** |

**Three resource groups, two regions, for one application.** The last row is the sharpest example and was
found by following the live config rather than the inventory: the dev BFF resolves its Redis connection via

```
ConnectionStrings__Redis = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)
```

and `spaarke-spekvcert` sits in the **`SharePointEmbedded`** resource group in **eastus** — a different RG,
a different region, and a name implying both a different product concern (SharePoint Embedded) and a
different purpose (`kvcert` reads as certificates, not application secrets).

Consequences: neither RG can be recreated independently, cost views split one application across three
places, RBAC must be granted three times, and secret resolution makes a **cross-region** call on cold start.
This is the concrete mechanism behind the owner's instinct that something is off.

### 2.4 🟡 Two dev Redis caches — one may be unused

`spe-redis-dev-67e2xz` and `spaarke-bff-redis-dev` sit in the same RG. Redis is one of the more expensive
line items in a dev footprint.

**Not yet confirmed which is live** — the `az webapp config appsettings list` call did not return within the
time budget of this review. **Confirm before acting**; do not assume the older-looking name is the dead one.

### 2.5 🟡 Five RG names in the repo exist in no subscription

Checked against all five subscriptions:

| Referenced name | Repo hits | Exists? |
|---|---:|---|
| `rg-spaarke-prod` | 15 | ❌ nowhere |
| `rg-spaarke-staging` | 14 | ❌ nowhere |
| `rg-spaarke-byok-prod` | 11 | ❌ nowhere |
| `rg-spaarke-prod-westus2` | 10 | ❌ nowhere |
| `rg-spaarke-platform-staging` | 3 | ❌ nowhere |
| `rg-spaarke-demo` | 13 | ✅ Demo subscription |

Some of this is aspirational (a staging tier that was planned), some is stale. Either way, scripts and docs
naming a non-existent RG fail at the worst moment — during a deployment or an incident.

### 2.6 🟡 Three competing naming conventions

```
spe-infrastructure-westus2        no rg- prefix, region suffix, product prefix "spe"
rg-spaarke-dev                    rg- prefix, no region, environment suffix
rg-spaarke-trial01-prod-model1    rg- prefix, customer + environment + tenancy model
```

There is no rule that tells you which to use for a new resource group, which is how sprawl starts.

---

## 3. Proposed organization

Principle: **a resource group should be a unit of lifecycle and blast radius** — things created, deleted,
and RBAC'd together. Two rules follow: (a) one application's resources live together; (b) shared, long-lived
resources are explicitly shared, not incidental lodgers in an environment RG.

| Proposed RG | Holds | Replaces / absorbs |
|---|---|---|
| `rg-spaarke-shared-global` | `spaarke.com` DNS zone, any tenant-wide identity | carved OUT of `spe-infrastructure-westus2` |
| `rg-spaarke-dev` | Dev BFF + plan + **its** managed identity, Redis, SignalR, ACS, external SPA, CIAM | absorbs the BFF-owned resources from `spe-infrastructure-westus2` |
| `rg-spaarke-ai-dev` | OpenAI, DocIntel, Search, Cosmos-AI, AI Foundry hub/project | carved out of `spe-infrastructure-westus2` |
| `rg-spaarke-platform-dev` | control plane (unchanged) | — |
| `rg-spaarke-prod` *(new subscription)* | production BFF, prod AI, prod KV, cert | **moved out of the dev subscription** |
| `rg-spaarke-clients-dev` | Office add-ins static site, bot | carved out |

**Naming rule to adopt**: `rg-spaarke-{workload}-{env}`, no region in the name (the RG's own `location`
carries that, and it is a metadata field, not an identity). Region belongs in a name only when the same
workload is genuinely deployed per-region.

---

## 4. Sequencing — and what NOT to do

**Do not start by moving resources.** Several of these cannot be moved at all, and moving is the riskiest
step, not the first one.

1. **Decide the prod-subscription question first (§2.1).** It is the only finding with a security and cost
   dimension, and it changes the target layout for everything else. Moving a workload across subscriptions
   is a redeploy, not a `az resource move`.
2. **Confirm the Redis duplication (§2.4)** — cheap, and possibly an immediate cost saving.
3. **Fix the stale RG references (§2.5)** — pure repo change, zero Azure risk, removes a class of
   deployment failure.
4. **Adopt the naming rule (§3)** for *new* resource groups. Costs nothing, stops the sprawl growing.
5. **Only then** consider carving up `spe-infrastructure-westus2`, starting with the DNS zone.

### Known constraints on moving resources

- **DNS zones, Key Vaults, App Service plans and their sites, and Cognitive Services accounts** each have
  move restrictions or require the whole dependency set to move together. Check
  `az resource invoke-action --action validateMoveResources` before planning any move.
- **Managed identities cannot be moved** without breaking every federated credential and RBAC assignment
  that references their principal ID. `mi-bff-api-dev` is referenced by the auth stack — treat it as
  create-new-and-repoint, never move.
- Moving a resource **does not** update anything that references it by resource ID — pipelines, Key Vault
  references, and Bicep parameter files all need the same change in the same window.

---

## 5. Open items

- **Which dev Redis is live** (§2.4) — the one measurement this review could not complete.
- Whether `rg-spaarke-website` (eastus2) and `SharePointEmbedded` (eastus) are deliberately cross-region or
  historical accidents. **`SharePointEmbedded` is no longer just an SPE RG** — it holds `spaarke-spekvcert`,
  which the dev BFF depends on for application secrets (§2.3). Whatever else changes, that vault's contents
  and consumers should be catalogued before anyone treats the RG as SPE-only and safe to reorganise.
- Whether the "Model 1 Production" subscription is intended to be the *only* production home, which would
  make §2.1 a migration target rather than an open question.
