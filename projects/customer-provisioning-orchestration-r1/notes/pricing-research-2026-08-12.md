# Pricing Research — Spaarke Customer Deploy (2026-08-12)

> Verified via: Azure Pricing Calculator + Microsoft Learn + Microsoft product pricing pages, with cross-check against multiple third-party pricing summaries (2026 dated).
> Region: US West 2 / East US 2 unless noted. Azure retail pricing between these two regions is identical for every SKU in this table (differences only appear in specialty SKUs — GPU VMs, some Cosmos multi-region features — none of which apply to Spaarke's Model 2 stamp).
> Currency: USD, list / retail (no EA / CSP discounts applied).
> Note on sources: many Azure pricing pages render numbers dynamically via a region selector — the source URL is authoritative but a human review requires selecting the region on the live page. Where the number was quoted in a third-party 2026-dated summary (CloudZero, Pump, Amnic, Finout, SAMexpert, etc.), that source is cited as a corroborator, NOT as the primary source.

---

## 1. Azure infrastructure per-customer (Model 2 dedicated)

| Resource | SKU | Rate | Source (primary) | Corroborator | Notes |
|---|---|---|---|---|---|
| App Service Plan (Linux) | Standard **S1** (1 vCore, 1.75 GB) | ~$69.35/mo list (~$0.095/hr). Multiple 2026 summaries quote **$56.94/mo** for Linux S1 (Linux is ~15–18% cheaper than Windows S1); the Windows S1 published figure is ~$73.00/mo. **UNVERIFIED-EXACT** — pick the number by loading the calculator for your region. | [Azure App Service Linux pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/) | [nicheelab 2026 App Service pricing](https://nicheelab.com/en/articles/azure/app-service-plan-design/) | S1 is the smallest tier with auto-scale + slots. Use **P0v3** or **B2** to trade cost/features. Reserved-instance discounts up to 55% for 3-yr. |
| Key Vault | Standard | **$0.03 per 10,000 operations**; **no per-secret / per-cert storage fee**; certificate renewal requests billed per request | [Azure Key Vault pricing](https://azure.microsoft.com/en-us/pricing/details/key-vault/) | [CostBench 2026](https://costbench.com/software/secrets-management/azure-key-vault/), [Infisical 2026 guide](https://infisical.com/blog/azure-key-vault-pricing) | Effectively free at Spaarke scale (a customer stamp will do <1M ops/mo → <$3/mo). No fixed monthly base. |
| Storage Account (blob) | Standard **LRS**, Hot, GPv2 | **$0.018/GB/mo** capacity; transactions: **~$0.055/10K writes**, **~$0.0044/10K reads**, **~$0.055/10K list**, **~$0.0044/10K delete** (Hot-tier). | [Azure Blob Storage pricing](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/) | [Sedai 2025-26 guide](https://sedai.io/blog/azure-blob-storage-pricing), [CloudZero](https://www.cloudzero.com/blog/azure-blob-storage-pricing/) | Egress separate. GRS/RA-GRS ~2× storage cost. Not the SPE bucket — this is Spaarke's app blob store. |
| Service Bus | Standard | **$9.81/mo base per subscription** (not per namespace) + **first 13M ops/mo included** + **~$0.80/M additional ops** | [Azure Service Bus pricing](https://azure.microsoft.com/en-us/pricing/details/service-bus/) | multiple 2026 summaries quote "$10 base" (rounded) | Premium tier ~$668/mo per MU — required for VNet integration + higher throughput. |
| Cache for Redis | Basic **C0** (250 MB) | **~$16.00/mo** (~$0.022/hr) | [Azure Cache for Redis pricing](https://azure.microsoft.com/en-us/pricing/details/cache/) | [CloudPriceCheck 2026](https://cloudpricecheck.com/azure/cache-for-redis-pricing) | No SLA, no replication — dev/test only. **Retirement announced 2028-04-30**; migration target is Azure Managed Redis. |
| Cache for Redis | Premium **P1** (6 GB, VNet-capable) | **~$405/mo** (~$0.556/hr) | [Azure Cache for Redis pricing](https://azure.microsoft.com/en-us/pricing/details/cache/) | [Dragonfly 2026 guide](https://www.dragonflydb.io/guides/azure-redis-pricing) | Required if you need VNet injection / clustering / high-availability. See also researcher memory `azure-managed-redis-2026-06-26.md` for Managed Redis (successor product) pricing shape. |
| Cosmos DB | Serverless | **$0.25 per 1M RU** consumed; **$0.25/GB/mo** transactional storage | [Azure Cosmos DB Serverless pricing](https://azure.microsoft.com/en-us/pricing/details/cosmos-db/serverless/) | [Intercept 2026 guide](https://intercept.cloud/en-gb/blogs/azure-cosmos-db-pricing) | Free tier: 400 RU/s + 25 GB. Provisioned throughput floor ~$24/mo (400 RU/s). |
| Azure OpenAI (S0 PAYG) | See §2 | See §2 | [Azure OpenAI pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/) | | |
| Azure AI Search | Standard **S1**, 1 SU | **$245.28/mo** per Search Unit (list; ~$0.336/hr). Older figure of **$73.73/mo** was for S1 without replicas — retired in current pricing. | [Choose a pricing model — MS Learn](https://learn.microsoft.com/en-us/azure/search/search-sku-tier) | [TrustRadius Azure AI Search 2026](https://www.trustradius.com/products/azure-ai-search/pricing) | The largest single fixed-floor line item. Basic tier ~$76/mo/SU is cheaper but capped at 2 GB index + no HA. |
| Document Intelligence | S0 | **$10.00 per 1,000 pages** for `prebuilt-layout` (0–1M pages/mo tier); custom models ~$50/1K pages | [Azure AI Document Intelligence pricing](https://azure.microsoft.com/en-us/pricing/details/form-recognizer/) | [DocuOCR 2026 pricing](https://docuocr.com/blog/azure-document-intelligence-pricing), verified 2026-07 vs retail API | Free F0 tier: 500 pages/mo. Read model ~$1.50/1K pages; prebuilt models vary. |
| Application Insights + Log Analytics | Pay-as-you-go (PerGB2018) | **$2.30/GB ingested** (first 5 GB/mo free per App Insights component); **~$0.10/GB/mo** interactive retention past 31 days free; **~$0.02/GB/mo** archive tier | [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/) | [MonitoringCost.com 2026](https://monitoringcost.com/azure-monitor-cost), [Pump 2026 guide](https://www.pump.co/blog/azure-monitor-pricing/) | Commitment tiers (100 GB/day min) save ~15–30%. Sentinel adds ~$4.30/GB on top. |
| SignalR Service | Standard **S1**, 1 unit (1K concurrent conns) | **~$1.61/day per unit** = **~$48.30/mo per unit** | [Azure SignalR pricing](https://azure.microsoft.com/en-us/pricing/details/signalr-service/) | [Ably 2025 comparison](https://ably.com/topic/azure-signalr-pricing) | Free tier: 20 conns / 20K messages/day. First 1M messages/unit/day free. Scale by unit (up to 100/instance). |
| Content Safety | S0 (Standard) | **$0.38 per 1,000 text records** (1K chars/record; Prompt Shields + moderation billed on this meter). Free F0: 5,000 text records/mo total. | [Azure AI Content Safety pricing](https://azure.microsoft.com/en-us/pricing/details/content-safety/) | [Oreate AI 2026 analysis](https://www.oreateai.com/blog/demystifying-azure-ai-content-safety-pricing-keeping-your-digital-spaces-clean/84e401553234165a4b26dca410a2e1cd) | Image moderation ~$1.50/1K images. Regionally restricted — verify West US 2 / East US 2 availability. |

---

## 2. Azure OpenAI token pricing (Global Standard / PAYG, S0)

| Model | Deployment date | Input $/1M | Cached input $/1M | Output $/1M |
|---|---|---|---|---|
| **gpt-4o** | `2024-08-06` | **$2.50** | **$1.25** (50% cache discount) | **$10.00** |
| **gpt-4o-mini** | `2024-07-18` | **$0.15** | **$0.075** | **$0.60** |
| **text-embedding-3-large** | current | **$0.13** | n/a | n/a (embeddings have no output tokens) |

Sources:
- Primary: [Azure OpenAI Service pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/) (US West 2 / East US 2 use Global Standard pricing — verify region selector)
- Corroborator: [Future AGI gpt-4o (2024-08-06) calculator](https://futureagi.com/llm-cost-calculator/azure-openai/gpt-4o-2024-08-06/), [CloudZero 2026 Azure OpenAI guide](https://www.cloudzero.com/blog/azure-openai-pricing/), [Amnic 2026 pricing analysis](https://amnic.com/blogs/understanding-the-true-cost-of-azure-openai)

### Discounts to model in

- **Batch API discount**: **50% off** input + output for batched jobs (up to 24-hr turnaround) on the models Azure exposes for Batch. Applies to gpt-4o family, NOT embeddings on Azure (per multiple 2026 sources — Azure has NOT exposed the batch discount for embedding models even though OpenAI direct has). Confirm on the pricing page for the exact model in your region.
- **Prompt caching discount**: **50% off** input tokens on cached prefixes (already reflected in "cached input" column above). Automatic, no config beyond keeping prompts stable.
- **Provisioned Throughput (PTU)**: Fixed hourly cost per PTU (e.g., gpt-4o ~$1/PTU/hr regional; PTU minimums by model). Only economical at sustained high throughput. NOT recommended for Spaarke Model 2 stamps unless a single customer exceeds ~$5K/mo in PAYG spend.

**Note re: gpt-5 family**: Per researcher memory `azure-openai-reasoning-models-2026-07.md`, `gpt-5` and `gpt-5-mini` are the current reasoning tier and **`gpt-5` is deployable in West US 3, not West US 2**. If Spaarke uses gpt-5 (advised for legal reasoning tasks), the region-lock story changes — this is a follow-up.

---

## 3. Microsoft licensing

| Product | SKU | Rate | Source | Notes |
|---|---|---|---|---|
| Power Apps | **Premium** (formerly "per-user") | **$20/user/mo** list; **$12/user/mo** at 2,000+ seats | [Power Apps pricing](https://www.microsoft.com/en/power-platform/products/power-apps/pricing) | Includes Dataverse + premium connectors + 500 AI Builder credits. Required for Spaarke since it uses custom Dataverse tables + premium connectors. |
| Power Apps | **per-app** ($5/user/app/mo) | **RETIRED January 2026** for most channels (still available EA/CSP direct — see SAMexpert). Do not model as available for new Spaarke customers unless via EA. | [SAMexpert per-app retirement](https://samexpert.com/power-apps-per-app-plan-retired/) | Historical rate: $5/user/app/mo. |
| Dataverse | Database capacity add-on | **$40/GB/mo** | [Dataverse licensing — MS negotiations](https://microsoftnegotiations.com/blog/dataverse-capacity-licensing) | Above tenant-included capacity. Pooled at tenant level. |
| Dataverse | File capacity add-on | **$2/GB/mo** | same | |
| Dataverse | Log capacity add-on | **$10/GB/mo** | same | Log = auditing / plugin trace logs. |
| SharePoint Embedded | Storage (active) | **~$0.20/GB/mo** (= $0.0067/GB/day) | [SPE billing meters — MS Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/meters), [SPE pay-as-you-go billing — MS Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/billing) | 4 meters: active storage, archive storage, API transactions, egress. |
| SharePoint Embedded | API transactions (Class A / Class B) | Metered per transaction — **exact per-1K rate UNVERIFIED**; MS Learn meters page is the authoritative source but the rate table is not published inline (requires Partner Center / Azure pricing calculator with a container spun up). | [SPE billing meters — MS Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/meters) | Class A ≈ write/mutation, Class B ≈ read. Egress metered separately. **Follow-up**: get from an existing SPE customer bill or Azure Cost Management. |
| Microsoft 365 | **E3** | **$39/user/mo** (up from $36 as of 2026-07-01) | [Redmond Mag 2025-12 pricing announcement](https://redmondmag.com/articles/2025/12/08/microsoft-365-suite-pricing-to-increase-next-year.aspx), [SAMexpert 2026-07 rate change](https://samexpert.com/microsoft-365-july-2026-price-increase/) | Includes Exchange Online Plan 2, SPO Plan 2, Teams (in most regions), Intune. Base tier for Spaarke enterprise deployments. |
| Microsoft 365 | **E5** | **$60/user/mo** (up from $57 as of 2026-07-01) | same | Adds Purview / Defender / Copilot-adjacent features. Required for customers who need advanced compliance holds around Spaarke's document store. |
| Microsoft 365 | **Business Standard** | **$14/user/mo** (up from $12.50 as of 2026-07-01) | same | ≤300 seats; suitable for SMB/Model-1 customers. Includes Exchange + SPO + Teams. |
| Dataverse | Production environment | **Free** to create; requires **1 GB DB / 1 GB file** capacity to exist. Capacity comes from tenant pool (5 GB DB, 20 GB file baseline via any Power Apps Premium license) OR from add-on SKUs above. | [Power Platform licensing FAQs — MS Learn](https://learn.microsoft.com/en-us/power-platform/admin/powerapps-flow-licensing-faq) | Model 2 = **1 dedicated Production environment per customer**. Baseline tenant capacity is shared across all customers on that tenant — so per-customer capacity math must account for overage across the fleet. |
| Dataverse | Sandbox environment | **Free**; consumes tenant capacity but at reduced defaults (also cannot be used for prod traffic per licensing terms) | same | |
| Dataverse | Trial / Developer environment | **Free** for dev only; expires. Not for production. | same | Suitable for Model 1 shared-tier trial deployments if paired with SPE for content. |

---

## 4. Notable pricing changes since ~early 2025

- **M365 commercial suite: ~8–12% list price increase effective 2026-07-01.** E3 $36→$39, E5 $57→$60, Business Standard $12.50→$14. Announced Dec 2025. Model the new numbers for any customer conversation from July 2026 onward. Source: [SAMexpert 2026-07 price increase](https://samexpert.com/microsoft-365-july-2026-price-increase/).
- **Power Apps per-app plan retired January 2026** for most channels (Microsoft Product Terms; EA/CSP direct still have it). Prior recommendation "use per-app for cheap per-user math" no longer valid; default to Premium at $20/user/mo. Source: [SAMexpert](https://samexpert.com/power-apps-per-app-plan-retired/).
- **Azure Cache for Redis retirement: 2028-04-30.** Migration target is Azure Managed Redis (different SKU codes, different pricing shape — see researcher memory `azure-managed-redis-2026-06-26.md`). If Spaarke's per-customer stamp uses Redis, plan the migration into Year 2 of the multi-year cost model. Source: [Microsoft Learn retirement notice, cited in multiple 2026 sources](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/).
- **Dataverse file capacity doubled for many D365 SKUs effective April 2026** (many tenants got ~2× included file capacity at no cost). Reduces the file-capacity add-on line item for existing D365 customers. Source: [Inogic 2026-03 announcement](https://www.inogic.com/blog/2026/03/microsoft-is-doubling-your-dataverse-file-storage-what-every-dynamics-365-admin-should-do-before-april-15/).
- **Azure OpenAI: gpt-4o (2024-08-06) prices have been stable at $2.50 in / $10.00 out since Oct 2024** (the price cut from the earlier `2024-05-13` model). No further reductions in 2026 as of this writing; gpt-4o-mini is unchanged at $0.15 in / $0.60 out. Prompt caching discount added throughout 2025 and is now automatic on all gpt-4o family models.
- **Azure AI Content Safety Prompt Shields** are now billed via the same $0.38/1K text records meter as text moderation (previously separate). Confirm on the pricing page.

---

## 5A. Rolled-up baseline monthly (Model 2, empty environment, no user usage)

Assumptions: 1 customer stamp; 1 Production Dataverse env (baseline capacity); 1 SPE tenant; 0 GB SPE content; 0 tokens consumed; 0 documents scanned; App Service running 24×7; Redis Basic C0 (dev-safe); AI Search S1 (production-safe); Content Safety only free tier used.

| Line item | Monthly floor $ (list) | Notes |
|---|---|---|
| App Service Plan S1 Linux | ~$69 | Fixed floor, 24×7 |
| Azure AI Search S1 (1 SU) | ~$245 | **Largest single fixed line** |
| Azure Cache for Redis Basic C0 | ~$16 | Dev tier only; use P1 (~$405) for prod-HA |
| Azure SignalR Standard 1 unit | ~$48 | Fixed floor, 24×7 |
| Service Bus Standard | ~$10 | Base + 13M ops included |
| Storage Account (empty) | ~$0 | Pay-per-use only |
| Key Vault (empty) | ~$0 | Pay-per-op only |
| Cosmos DB Serverless (empty) | ~$0 | Pay-per-use only |
| Azure OpenAI (no calls) | ~$0 | PAYG |
| Document Intelligence (no calls) | ~$0 | PAYG |
| Content Safety (F0 free tier) | ~$0 | 5K records/mo free |
| App Insights + Log Analytics (assume 5 GB/mo ingested, retention 31 days) | ~$0 (within 5 GB App Insights free grant) | Any real traffic pushes this to ~$25–$100/mo |
| **Azure infrastructure floor (dev-Redis)** | **~$388/mo** | Empty environment, no usage |
| **Azure infrastructure floor (prod-Redis P1)** | **~$777/mo** | Substitute Premium P1 for Basic C0 |

**Excluded from Azure floor** (both belong to the customer's M365 tenant, not Spaarke's Azure subscription):
- Power Apps Premium: **$20/user/mo × N users** (or E5 which bundles at $60/user/mo)
- M365 E3/E5: customer's existing spend, but required for Exchange/Graph integration
- SPE storage: **~$0.20/GB/mo** — for a 500 GB customer, ~$100/mo storage + variable API costs

**Realistic total baseline for a mid-size customer (100 users, 500 GB SPE, moderate AI usage):**
- Azure infra: ~$777/mo (with P1 Redis)
- Power Apps Premium: 100 × $20 = **$2,000/mo**
- SPE storage: ~$100/mo (storage-only; API + egress additive)
- Azure OpenAI: **highly variable** — as a rough anchor, 100 users × 10 doc-drafting sessions/mo × ~50K tokens = 50M tokens ≈ **$500–$1,000/mo** at gpt-4o mix
- Document Intelligence: 100 users × 20 docs × 10 pages = 20K pages × $10/1K = ~$200/mo

**Rough envelope: ~$3,500–$4,500/mo per mid-size customer stamp** (Azure infra + Power Apps + SPE + AI), before M365 base licensing (which the customer already owns).

---

## 5B. Model 1 (shared trial/SMB tier) — resource segregation and cost breakdown

**Design premise** (from design.md D3 v3 + §3A): Model 1 shares the three fixed-floor resources (App Service Plan, Azure OpenAI, AI Search) across multiple trial/SMB tenants with logical tenant isolation, while keeping everything else per-customer dedicated. Cost math is structured as **(a) shared platform floor** (one-time fixed for Spaarke, allocated to customers via business decision below) + **(b) per-customer variable** (charged/allocated directly to the trial customer).

### Resource-by-resource: shareable vs. must-be-dedicated

| Resource | Model 1 disposition | Isolation mechanism | Rationale |
|---|---|---|---|
| **App Service Plan** | 🟢 SHARED | One plan hosts the multitenant BFF; every request carries `tenantId` claim | Fixed floor; per-request CPU/mem cost is negligible vs. plan floor |
| **App Service (BFF)** | 🟢 SHARED | Multitenant BFF (single instance, per-request tenant scoping) | Same reasoning as plan |
| **Azure OpenAI (S0)** | 🟢 SHARED (metered per D19) | One AOAI resource; per-tenant token attribution via APIM or app-level custom metric | No idle cost; per-token spend attributed to tenant; `tokenBudgetMonthlyUSD` caps trial spend |
| **AI Search (S1)** | 🟢 SHARED | Shared indexes with **`tenantId` filter mandatory on every query** (already the pattern per design.md §8) | $245/mo fixed floor; per-tenant index-storage overhead trivial |
| **SignalR (S1)** | 🟢 SHARED | Per-tenant channels/groups | Fixed floor per unit; capacity scales in 1K-conn units |
| **Service Bus (Standard)** | 🟢 SHARED | Shared namespace; per-tenant subscription filter OR shared queue with `tenantId` message property | 13M ops/mo included covers many trials |
| **Document Intelligence (S0)** | 🟢 SHARED | Stateless PAYG; per-tenant page-count metered via App Insights custom event | No state to isolate |
| **Content Safety (S0)** | 🟢 SHARED | Stateless PAYG | No state to isolate |
| **App Insights + Log Analytics** | 🟢 SHARED | `tenantId` custom dimension on every log/metric; per-tenant Kusto filters for dashboards | Shared workspace, per-tenant query pattern |
| **Redis Cache (Basic C0)** | 🟡 SHARED with **key-prefix segregation** (recommended for trial) OR per-customer for stricter isolation | Key prefix `t:{tenantId}:...`; enforced by cache facade layer | Basic C0 = $16/mo total; if per-customer, becomes $16/customer/mo; Model 1 default = shared |
| **Cosmos DB (serverless)** | 🟡 SHARED account, **per-tenant partition key `/tenantId`** | Cosmos-native partition isolation | Serverless has no per-account floor; per-tenant partition data metered ($0.25/GB storage + $0.25/1M RU) — trivial for trial |
| **Storage Account (blobs)** | 🔴 DEDICATED per customer | Separate storage account per customer | Compliance boundary for temp/document-processing scratch; cheap ($0 idle) so no economic reason to share |
| **Key Vault** | 🔴 DEDICATED per customer | Separate KV per customer | Customer secrets/certs boundary; cheap ($0 idle) |
| **User-Assigned Managed Identity** | 🔴 DEDICATED per customer (or per trial-tenant group) | Separate UAMI | Identity boundary for RBAC and cross-resource authentication |
| **Entra app config** | 🔴 DEDICATED per customer (multitenant BFF app + per-customer redirect URI overrides + per-customer secret) | Per-customer app registration OR shared multitenant app with per-tenant registered instance | Per D2 — required for either model |
| **Dataverse environment** | 🔴 DEDICATED per customer | One env per customer, non-negotiable | Identity + data boundary; Power Apps license carries baseline capacity |
| **SharePoint Embedded container** | 🔴 DEDICATED per customer | One container per customer BU (ADR-005) | Data residency + access boundary |

### Shared platform floor (one-time fixed for Spaarke, then allocated)

These are the costs Spaarke pays regardless of how many Model 1 customers exist. Allocation to customers is a **business/pricing decision separate from infrastructure**.

| Resource | Monthly $ (list) | Notes |
|---|---|---|
| App Service Plan S1 Linux | ~$69 | Fixed floor 24×7 |
| App Service (BFF) | $0 | Included in plan |
| Azure AI Search Standard S1 (1 SU) | ~$245 | **Largest single fixed line**; single-SU sufficient for early trials |
| SignalR Standard S1 (1 unit) | ~$48 | Only if Notifications spine enabled |
| Service Bus Standard | ~$10 | Baseline + 13M ops included |
| Azure OpenAI (S0, no idle cost) | $0 (fixed) | Per-token spend is per-customer (below); no plan floor |
| Document Intelligence (S0, no idle cost) | $0 (fixed) | Per-page spend is per-customer (below) |
| Content Safety (S0, no idle cost) | $0 (fixed) | Per-transaction spend is per-customer (below) |
| Redis Basic C0 (shared) | ~$16 | Only if shared (see disposition table above) |
| Cosmos DB serverless account | $0 (fixed) | Per-tenant partition data is per-customer (below) |
| App Insights + Log Analytics baseline | ~$0 (within 5 GB/mo free per component) | Any real traffic pushes shared workspace ingestion to ~$25–$100/mo total across all tenants |
| **Total shared platform floor** | **~$388/mo** | Grows with SignalR, ingestion volume; capped until AI Search / Service Bus / App Service hit scale limits |

**Allocation math** (deliberately left as a business decision):

| # of Model 1 customers on the platform | Allocation per customer (naive equal-share of $388) |
|---|---|
| 5 | ~$78 |
| 10 | ~$39 |
| 25 | ~$16 |
| 50 | ~$8 |
| 100 | ~$4 |

Alternative allocation strategies (choose per pricing model):
- **Equal-share amortization** (above): simplest, but new customers benefit from earlier customers' subsidy
- **Volume-tier allocation**: charge each Model 1 customer a fixed platform-access fee ($X/mo) that covers marginal contribution; treat under-allocation as CAC investment
- **Usage-weighted allocation**: split shared floor proportional to per-tenant AI Search index size + AI query volume (fair for AI Search + OpenAI at least); leaves App Service/SignalR as fixed CAC
- **CAC absorb**: don't allocate the platform floor at all until customer converts to Model 2; treat it as trial acquisition cost

### Per-customer variable (charged/allocated directly)

Costs that scale with a specific Model 1 customer's usage or presence:

| Line item | Typical trial customer $/mo | Notes |
|---|---|---|
| Storage Account (dedicated, empty/near-empty for trial) | ~$0–$2 | ~10 GB scratch typical |
| Key Vault (dedicated, ops-based) | ~$0–$1 | Minimal ops for a trial |
| Cosmos partition (per-tenant data) | ~$2–$8 | Sessions + prompts + audit for 5–10 trial users |
| Azure OpenAI (metered via D19, capped by `tokenBudgetMonthlyUSD`) | ~$50–$200 | Typical trial cap; can be lower for demos |
| Document Intelligence (light usage) | ~$5–$20 | 500–2K pages/mo for trial exploration |
| Content Safety (light usage, F0 covers 5K records/mo free) | ~$0 | F0 typically sufficient for trials |
| Redis (if per-customer C0) | ~$16 | Only if not shared per disposition table |
| App Insights ingestion (per-tenant share, if allocated) | ~$0–$5 | Trial-scale telemetry rarely exceeds 5 GB free |
| SPE storage (trial data, ~10 GB) | ~$2 | $0.20/GB × 10 GB |
| Power Apps Premium (Spaarke pays per D5, 5 trial users × $20) | **$100** | Dominates the per-customer bill |
| **Per-customer variable subtotal** | **~$160–$355/mo per trial** | Dominated by Power Apps + Azure OpenAI |

### Model 1 all-in per-customer envelope

- **Per-customer variable**: ~$160–$355/mo (trial-typical)
- **Platform allocation** (business decision — see table above): $4–$78/mo depending on pool size and allocation strategy
- **Realistic trial customer total**: **~$165–$430/mo** (5–10 users, capped tokens, moderate use)

**Break-even for the shared platform floor**: at $388/mo fixed and $100/customer/mo Power Apps + typical trial variable, **the marginal cost of adding one more Model 1 customer is ~$160–$355/mo**. The $388 fixed floor is amortized once ≥5–10 paying trials exist; at scale (25+) it becomes negligible per customer.

### How this compares to Model 2

| | Model 1 (shared trial) | Model 2 (dedicated) |
|---|---|---|
| Shared platform floor Spaarke pays | ~$388/mo (fixed, once) | $0 (fully allocated to customer) |
| Per-customer marginal cost | ~$160–$355/mo | ~$777/mo Azure infra + $2K+ Power Apps (100 users) + AI + SPE |
| Total per-customer (typical) | **~$165–$430/mo** | **~$3,500–$4,500/mo** |
| Isolation posture | Logical (tenant filter on every query) | Physical (dedicated stamp) |
| Suitable for | Trials, SMB, low-compliance | Regulated legal, enterprise |
| Break-even Spaarke perspective | ≥5–10 paying trials cover shared floor | Immediate (usage-passthrough) |

**Interpretation**: Model 1 is **~10× cheaper per customer** than Model 2 at typical sizes, primarily because (a) Spaarke absorbs the $388 shared platform floor as CAC, (b) fewer users mean less Power Apps license spend, and (c) trial token budgets cap AI spend. The trade-off is logical-only isolation — acceptable for trials/SMB but not for regulated legal.

---

## 6. Uncertainty flags

- **App Service S1 Linux exact monthly $**: Multiple 2026 sources cite $56.94/mo; the traditional Windows S1 figure has been ~$73/mo and Linux is typically ~15% cheaper (~$62/mo). I used **$69** in the roll-up as a defensible mid-point but a human should confirm on the calculator with West US 2 selected. Difference is <$15/mo — not material to the customer conversation but should be nailed down before publishing.
- **SPE API transaction rate per 1K**: The MS Learn meters page confirms 4 meters exist (active storage, archive storage, transactions, egress) but does not publish the transaction $/rate inline. To get it authoritatively you need either (a) an existing SPE bill, (b) Azure Cost Management on a live container, or (c) Partner Center pricing lookup for the meter IDs. Recommendation: **spin up an SPE container in the dev tenant and read the meter rates from Cost Management** — should take 15 min. Until then, model transaction cost as ~10% of storage cost (industry rule-of-thumb) as a placeholder.
- **Azure OpenAI batch discount on embeddings**: Third-party 2026 sources say Azure does NOT offer the 50% batch discount on embedding models even though OpenAI direct does. Not clearly stated on the Azure pricing page. If embeddings volume matters to the cost model, confirm directly with the Azure OpenAI pricing team or a Microsoft account rep.
- **gpt-5 region availability**: Per researcher memory `azure-openai-reasoning-models-2026-07.md`, gpt-5 is in **West US 3**, not West US 2. If Spaarke's cost model assumes gpt-5 for legal reasoning, the region-lock story for the whole stamp changes. Not addressed in this note — this is a design-level question.
- **Content Safety region availability in West US 2 / East US 2**: Content Safety is regionally restricted (some meters not available in all regions). Verify the specific meter set required for Prompt Shields is available in the target region before committing to it in the cost model. See researcher memory notes for confirmation of West US 2 availability.
- **Dataverse capacity — included vs. add-on math**: Per-tenant included capacity (5 GB DB, 20 GB file, entitled via any Power Apps Premium license) is shared across ALL environments on that tenant. For Model 2 (dedicated stamp per customer), each customer's tenant gets its own included capacity — but if Spaarke hosts multiple customers on the SAME tenant (e.g., Model 1), you must account for capacity pooling across all customer environments. The $40/$2/$10 add-on rates are what you pay once included capacity is exhausted.
- **Prices assumed here are LIST**. Real customer economics change materially with an Enterprise Agreement (typical 15–40% off Azure list, up to 60% off M365 seats at scale), CSP, or Microsoft Cloud Solution Provider markup / discount. Flag "list pricing, EA discounts not modeled" in customer-facing decks.

---

## Sources consulted (authoritative)

Microsoft Learn / azure.microsoft.com / microsoft.com:
- [Azure App Service Linux pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/)
- [Azure Key Vault pricing](https://azure.microsoft.com/en-us/pricing/details/key-vault/)
- [Azure Blob Storage pricing](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/)
- [Azure Service Bus pricing](https://azure.microsoft.com/en-us/pricing/details/service-bus/)
- [Azure Cache for Redis pricing](https://azure.microsoft.com/en-us/pricing/details/cache/)
- [Azure Cosmos DB Serverless pricing](https://azure.microsoft.com/en-us/pricing/details/cosmos-db/serverless/)
- [Azure OpenAI Service pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/)
- [Azure AI Search pricing — MS Learn tier chooser](https://learn.microsoft.com/en-us/azure/search/search-sku-tier)
- [Azure AI Document Intelligence pricing](https://azure.microsoft.com/en-us/pricing/details/form-recognizer/)
- [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/)
- [Azure SignalR Service pricing](https://azure.microsoft.com/en-us/pricing/details/signalr-service/)
- [Azure AI Content Safety pricing](https://azure.microsoft.com/en-us/pricing/details/content-safety/)
- [Power Apps pricing (microsoft.com)](https://www.microsoft.com/en/power-platform/products/power-apps/pricing)
- [Power Platform licensing FAQ — MS Learn](https://learn.microsoft.com/en-us/power-platform/admin/powerapps-flow-licensing-faq)
- [SharePoint Embedded billing meters — MS Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/meters)
- [SharePoint Embedded pay-as-you-go billing — MS Learn](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/billing)

Third-party 2026-dated corroborators (used to fill numbers the MS pages render dynamically):
- [CloudZero 2026 Azure OpenAI pricing guide](https://www.cloudzero.com/blog/azure-openai-pricing/)
- [Future AGI gpt-4o (2024-08-06) calculator](https://futureagi.com/llm-cost-calculator/azure-openai/gpt-4o-2024-08-06/)
- [Amnic 2026 Azure OpenAI pricing analysis](https://amnic.com/blogs/understanding-the-true-cost-of-azure-openai)
- [SAMexpert M365 2026-07 price increase](https://samexpert.com/microsoft-365-july-2026-price-increase/)
- [SAMexpert Power Apps per-app retirement](https://samexpert.com/power-apps-per-app-plan-retired/)
- [Microsoft Negotiations Dataverse capacity guide](https://microsoftnegotiations.com/blog/dataverse-capacity-licensing)
- [CostBench Azure Key Vault 2026](https://costbench.com/software/secrets-management/azure-key-vault/)
- [Ably Azure SignalR pricing analysis](https://ably.com/topic/azure-signalr-pricing)
- [MonitoringCost.com Azure Monitor 2026](https://monitoringcost.com/azure-monitor-cost)
- [CloudPriceCheck Azure Cache for Redis 2026](https://cloudpricecheck.com/azure/cache-for-redis-pricing)
- [DocuOCR Azure Document Intelligence 2026](https://docuocr.com/blog/azure-document-intelligence-pricing)
