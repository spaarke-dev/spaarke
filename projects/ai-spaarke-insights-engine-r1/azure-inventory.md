# Azure Resource Inventory — AI Subsystem

> **Date**: 2026-05-19 (updated with Demo env scope correction)
> **Subscriptions in scope**:
> - **Spaarke Development Environment** (`484bc857-3802-427f-9ea5-ca47b43db0f0`) — primary
> - **Spaarke Demo Environment** (`2ff9ee48-6f1d-4664-865c-f11868dd1b50`) — separate complete Spaarke environment
>
> **Subscriptions OUT OF SCOPE** (confirmed by project owner):
> - SPRK Power Platform 1 — not relevant to Insights Engine
> - Spaarke Legal Rules Solution — not relevant to Insights Engine

## TL;DR

| Question | Answer |
|---|---|
| What AI substrate exists today? | Azure OpenAI + AI Search + Document Intelligence in both dev and prod RGs; AI Foundry hub + project in dev only |
| Cosmos DB status | One account (`spe-cosmos-dev-ai`) — **SQL/NoSQL API only**. No Gremlin (graph) account exists. |
| Function Apps | **None deployed.** All async work currently runs in BackgroundService inside the BFF App Service. |
| Biggest cost-savings opportunities (immediate) | (1) Dev App Service Plan P2v3 > Prod P1v3 — likely backwards; (2) Search dev tier has 2 replicas (HA-grade) for a dev workload; (3) Multiple `_managed` resource groups from auto-managed services worth auditing |
| What's missing for the Insights Engine | Cosmos Gremlin account, Function App(s) for sync, possibly additional AI Search indexes |
| Multi-tenant readiness | Resources are per-environment (dev/prod), not per-tenant. Physical per-tenant isolation (the design recommendation) is not yet structurally in place. |

## Resource groups in scope

| RG | Purpose (inferred) | Region | Notable |
|---|---|---|---|
| `rg-spaarke-platform-prod` | Production BFF + AI services | westus2 | Has prod BFF, OpenAI (westus3), Search, Doc Intel, KV, App Insights |
| `rg-spaarke-demo-prod` | Demo environment | westus2 | Has Redis, Service Bus, KV, Storage — **NO AI services** (likely shares prod's OpenAI/Search) |
| `spe-infrastructure-westus2` | DEV (despite "infrastructure" name) | westus2 | Has the dev BFF, dev AI services, **AI Foundry hub + project**, Cosmos AI, Redis, managed identity |
| `SharePointEmbedded` | SPE infrastructure (older) | eastus | Has older Service Bus (`spaarke-servicebus-dev` Basic), Storage, consumption-tier ASP |
| `rg-spaarke-website` | Marketing site | eastus2 | Static Web App for spaarke.com — unrelated to Insights Engine |
| `DefaultResourceGroup-EUS2`, `DefaultResourceGroup-EUS`, `ai_appi-...-managed` | Auto-managed | Various | Auto-created; review for orphaned resources |

## AI / Data substrate by resource

### Compute hosting

| Resource | RG | SKU | Used For | Cost Flag |
|---|---|---|---|---|
| `spaarke-bff-prod-plan` | platform-prod | **P1v3** (1 worker) | BFF prod | OK for prod |
| `spe-plan-dev-67e2xz` | spe-infrastructure | **P2v3** (1 worker) | BFF dev | ⚠️ **Larger than prod** — likely should be P1v3 or smaller (~$140/mo savings if downsized to P1v3) |
| `ASP-SharePointEmbedded-9b4e` | SharePointEmbedded | Y1 (consumption) | Legacy SPE | Check if still in use; consumption is cheap but worth verifying |
| `mi-bff-api-dev` | spe-infrastructure | — | Managed Identity | Active |

**No Function Apps deployed.** This is the gap to fill for the Insights Engine sync work (now permitted by the updated ADR-001).

### AI services

| Resource | RG | Kind | SKU | Region | Notes |
|---|---|---|---|---|---|
| `spaarke-openai-prod` | platform-prod | OpenAI | S0 | westus3 | Prod LLM endpoint |
| `spaarke-openai-dev` | spe-infrastructure | **AIServices** (multi-svc) | S0 | eastus | Dev — different kind than prod. AIServices is the newer multi-modal account; OpenAI-only is older. Inconsistent — worth standardizing. |
| `spaarke-search-prod` | platform-prod | AI Search | standard, **2 replicas / 1 partition** | westus2 | HA-grade for prod ✓ |
| `spaarke-search-dev` | spe-infrastructure | AI Search | standard, **2 replicas / 1 partition** | westus2 | ⚠️ **2 replicas in dev** is overkill — 1 replica saves ~$125/mo |
| `spaarke-docintel-prod` | platform-prod | FormRecognizer | S0 | westus2 | Document Intelligence |
| `spaarke-docintel-dev` | spe-infrastructure | FormRecognizer | S0 | westus2 | Dev |
| `sprkspaarkedev-aif-hub` | spe-infrastructure | ML Workspace (AI Foundry hub) | Basic | westus2 | **Foundry hub in dev only** — utilization unknown |
| `sprkspaarkedev-aif-proj` | spe-infrastructure | AI Foundry project | Basic | westus2 | Tied to hub above |
| `sprkspaarkedev-aif-kv`, `-sa`, `-logs`, `-insights` | spe-infrastructure | Foundry-required supporting resources | various | westus2 | Provisioned alongside Foundry hub |
| `spaarke-bot-dev` | spe-infrastructure | Bot Service | F0 (free) | global | Unknown purpose — possible Teams bot demo. No cost but consider deleting if unused. |

### Data + state

| Resource | RG | Kind | SKU | Notes |
|---|---|---|---|---|
| `spe-cosmos-dev-ai` | spe-infrastructure | Cosmos DB | GlobalDocumentDB (**SQL/NoSQL API**) | Used for AI-related state (work history per design.md). **NOT Gremlin.** A new Cosmos account with Gremlin API will be needed for the Insights Engine graph. |
| `spe-redis-dev-67e2xz` | spe-infrastructure | Redis | — | Caching |
| `spaarke-demo-prod-cache` | demo-prod | Redis | — | Demo caching |
| `sprkdemoprodsa` | demo-prod | Storage | Standard_LRS | Demo |
| `sprkspaarkedevaifsa` | spe-infrastructure | Storage | Standard_LRS | Foundry hub support |
| `sharepointembeddedabc0` | SharePointEmbedded | Storage | Standard_LRS | SPE infrastructure |
| `stspaarkewebsite` | rg-spaarke-website | Storage | Standard_LRS | Marketing site |

### Messaging

| Resource | RG | SKU | Notes |
|---|---|---|---|
| `spaarke-demo-prod-sbus` | demo-prod | Service Bus Standard | Demo environment |
| `spaarke-servicebus-dev` | SharePointEmbedded | Service Bus **Basic** | Older dev bus — Basic tier doesn't support topics/sessions. May be legacy. |

⚠️ **No production Service Bus** in `rg-spaarke-platform-prod`. The BFF prod likely points to one of these — needs confirmation. If prod is using the demo bus, that's a separation-of-concerns issue.

### Observability + security

| Resource | RG | Purpose |
|---|---|---|
| `sprk-platform-prod-insights` | platform-prod | App Insights (prod) |
| `sprk-platform-prod-kv` | platform-prod | Key Vault (prod) |
| `sprk-platform-prod-logs` | platform-prod | Log Analytics (prod) |
| `spe-insights-dev-67e2xz` | spe-infrastructure | App Insights (dev) |
| `spe-logs-dev-67e2xz` | spe-infrastructure | Log Analytics (dev) |
| `sprk-demo-prod-kv` | demo-prod | Key Vault (demo) |
| `sprkspaarkedev-aif-kv` | spe-infrastructure | Foundry-managed KV |

## Mapping current resources to Insights Engine needs

| Engine component | Existing resource | Gap |
|---|---|---|
| Insight Index (vector + structured) | `spaarke-search-prod` / `spaarke-search-dev` | **Exists.** Need new indexes designed (`insight-matters`, `insight-decisions`, etc.). Sync pipeline TBD. |
| Insight Graph (entity + edges) | None | ❌ **Missing.** Provision new Cosmos account with Gremlin API. |
| Live Facts | Dataverse + BFF | ✓ Already in place |
| Insights Agent (BFF-hosted) | Sprk.Bff.Api (existing) | ✓ Just code extension |
| Dataverse → Search sync | None | ❌ **Missing.** Need Function App(s) for change-feed + scheduled reconciliation. Approved by updated ADR-001. |
| LLM for synthesis | `spaarke-openai-prod` | ✓ Already deployed (gpt-4o available?) — verify deployment list |
| Embedding model | Same OpenAI account | Verify text-embedding-3-large or text-embedding-ada-002 deployment |
| Closure extraction playbook | JPS infrastructure in BFF | ✓ Reuse existing JPS playbook execution |

## Cost-savings opportunities (immediate)

| Item | Estimated savings | Effort | Risk |
|---|---|---|---|
| Dev App Service Plan P2v3 → P1v3 | ~$140/mo | Low | Verify dev workload fits in P1v3 (check current CPU/memory) |
| Dev AI Search 2 replicas → 1 replica | ~$125/mo | Low | 1 replica is fine for non-HA dev. Slight rebalance during change. |
| Foundry hub utilization audit | Variable | Medium | If hub + project are unused, deleting them frees ~$0-50/mo + supporting resources |
| `spaarke-servicebus-dev` (Basic, SharePointEmbedded RG) | Negligible | Low | If unused legacy, delete; if used, document why two Service Bus instances |
| `spaarke-bot-dev` (Bot Service F0) | $0 (free tier) | Trivial | If unused, delete for hygiene |
| Auto-managed RGs (`ai_appi-...-managed`, default RGs) | Variable | Low | Some are auto-created; deleting them through Azure causes parent resources to recreate |

**Estimated immediate savings: ~$265/mo** from the two compute downsizings alone, low-risk.

## Cost / utilization questions needing deeper investigation

These can't be answered from resource lists; need usage metrics or stakeholder input:

1. **OpenAI token consumption** by deployment — which models are actually in use? Are there idle deployments?
2. **AI Search index sizes + storage utilization** in dev vs. prod — are indexes oversized or undersized for current data?
3. **Cosmos DB RU/s configuration + actual consumption** — is `spe-cosmos-dev-ai` overprovisioned?
4. **App Service plan utilization** (CPU, memory, request rate) — is P2v3 actually needed in dev?
5. **Document Intelligence usage** — is dev account active or idle?
6. **Foundry hub + project** — is it being used at all? If not, this represents the largest cleanup opportunity.
7. **Service Bus message throughput** — does demo bus actually serve prod workloads (separation concern)?

These would be best answered via Azure Cost Management dashboards or by running utilization queries — out of scope for this initial inventory.

## What needs to be provisioned for the Insights Engine

When the design.md is approved, the following new resources are needed per environment (dev + prod minimum; ideally per-tenant later):

| Resource | Purpose | SKU recommendation |
|---|---|---|
| **Cosmos DB account (Gremlin API)** | Insight Graph | Start with serverless or autoscale 400-4000 RU/s |
| **Function App + plan** | Dataverse → AI Search sync, scheduled indexers, closure extraction triggers | Premium plan (EP1) for warm starts; or Consumption if cold-start acceptable |
| **Additional AI Search indexes** | `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions` | Same Search service (multi-index); standard tier sufficient |
| **App Insights connection from Functions** | Correlated tracing | Use existing App Insights, configured per environment |

Per the updated ADR-001, all of this is in-scope for Azure Functions usage (out-of-band integration). Implementation pattern: Bicep modules added to existing per-environment deployment templates.

## Spaarke Demo Environment (separate complete Spaarke environment)

> Subscription: `2ff9ee48-6f1d-4664-865c-f11868dd1b50`
> RG: `rg-spaarke-demo` (westus2)
> Purpose: complete Spaarke environment for demos, isolated from dev/prod resources

Demo is a fully self-contained Spaarke environment — its own BFF, AI Search, OpenAI, Doc Intel, Service Bus, Redis, KV, App Insights, Storage. This is the model for what a **per-tenant deployment** looks like in production. Useful as the reference shape when designing the Bicep tenant deployment unit.

### Compute hosting (Demo)

| Resource | SKU | Notes |
|---|---|---|
| `spaarke-demo-plan` | **B1** (Basic) | Demo-appropriate sizing; lower than dev (P2v3) and prod (P1v3) |
| `spaarke-bff-demo` | (App Service) | The Demo BFF deployment |
| `WestUS2Plan` | Y1 (Consumption) | Hosts the two Functions below — **proves Functions ARE deployable in Spaarke environments** (existing pattern, just not yet used for AI) |

### Existing Function Apps in Demo (important precedent)

| Resource | Plan | Purpose (inferred — verify) |
|---|---|---|
| `spaarke-linkedin-refresh` | Y1 Consumption | Likely marketing-site LinkedIn data refresh |
| `spaarke-content-reminder` | Y1 Consumption | Likely a notification/reminder job |

**Implication**: Spaarke already operates Azure Functions on Consumption plan in this environment. The Insights Engine sync Functions are an extension of an established Function deployment pattern, not a brand-new operational concern. (Note: these existing Functions are on Consumption Y1, not Flex Consumption — Phase 1 will need to verify whether Flex Consumption is available in westus2 and chosen as the new standard.)

### AI services (Demo)

| Resource | Kind | SKU | Region |
|---|---|---|---|
| `spaarke-openai-demo` | OpenAI | S0 | westus3 |
| `spaarke-search-demo` | AI Search | standard, **1 replica / 1 partition** | westus2 |
| `spaarke-docintel-demo` | FormRecognizer | S0 | westus2 |

Demo Search has 1 replica (vs. dev's wasteful 2) — this is the right pattern for non-HA environments.

### Data + state (Demo)

| Resource | Type | Notes |
|---|---|---|
| `spaarke-demo-cache` | Redis | Demo caching |
| `sprkdemosa` | Storage Standard_LRS | Demo storage |
| `spaarke-demo-sbus` | Service Bus Standard | Demo messaging |
| ❌ No Cosmos DB | — | Demo has NO Cosmos. Implication: when Insights Engine ships to per-tenant including Demo, a Cosmos NoSQL account needs provisioning per environment. |

### Observability + security (Demo)

| Resource | Purpose |
|---|---|
| `spaarke-demo-insights` | App Insights |
| `spaarke-demo-logs` | Log Analytics |
| `sprk-demo-kv` | Key Vault |

### Demo cost-savings flags

Demo SKUs are appropriately sized for a demo environment (B1 BFF, 1-replica Search). The two existing Functions on Consumption Y1 cost effectively nothing when idle. No significant savings opportunities here; the pattern is correct.

## Reconciliation across environments

| Environment | Subscription | Primary RG | BFF Plan | Search Replicas | Cosmos | Has AI Foundry? |
|---|---|---|---|---|---|---|
| Dev | Spaarke Dev | `spe-infrastructure-westus2` | **P2v3** ⚠️ | 2 ⚠️ | `spe-cosmos-dev-ai` (NoSQL) | Yes (`sprkspaarkedev-aif-hub`) |
| Demo | Spaarke Demo | `rg-spaarke-demo` | B1 | 1 | None | No |
| Prod | Spaarke Dev (yes, prod RG is in dev sub) | `rg-spaarke-platform-prod` | P1v3 | 2 (HA-appropriate) | None | No |
| "demo-prod" (intermediate?) | Spaarke Dev | `rg-spaarke-demo-prod` | None | None | None | No |

Open question worth verifying with the team: **what is `rg-spaarke-demo-prod` for?** It has Redis, Service Bus, KV, Storage but NO AI services and NO compute. It's not the demo environment (that's the separate sub above). It's not prod. It might be a staging or shared-services RG. Worth clarifying before tenant deployment design.

## Next steps for inventory work

1. ✅ Demo environment inventoried (this update)
2. ❌ ~~SPRK Power Platform 1~~ — out of scope per project owner
3. ❌ ~~Spaarke Legal Rules Solution~~ — out of scope per project owner
4. Pull Cost Management dashboard data for the last 30 days to confirm savings estimates
5. Audit Foundry hub utilization (Azure ML workspace's run history in dev)
6. Clarify `rg-spaarke-demo-prod` purpose (above table open question)
7. Confirm what `spaarke-linkedin-refresh` and `spaarke-content-reminder` Functions do (Demo env)
8. Run an `az graph` query to inventory tag coverage (helps future cost attribution)
