---
name: spaarke-customer-stamp-pricing-2026-08-12
description: List/retail pricing snapshot (Aug 2026) for the full Spaarke Model 2 Azure + M365 stack — App Service S1, Key Vault, Blob, Service Bus, Redis, Cosmos DB, Azure OpenAI, AI Search S1, Doc Intelligence, App Insights, SignalR, Content Safety, Power Apps Premium, Dataverse capacity, SPE, M365 E3/E5. West US 2 / East US 2.
metadata:
  type: reference
---

Full report: `projects/customer-provisioning-orchestration-r1/notes/pricing-research-2026-08-12.md`.

**Biggest fixed-floor line item**: Azure AI Search Standard S1 at ~$245/mo per SU. Multi-x larger than any other single Azure line.

**Baseline empty-stamp**: ~$388/mo Azure with dev Redis (Basic C0), ~$777/mo with prod Redis (Premium P1). Excludes Power Apps Premium ($20/user/mo), M365 seats (customer-owned), and any usage-based AI/SPE meters.

**Rough per-customer envelope (100 users, 500 GB SPE, moderate AI)**: ~$3,500–$4,500/mo — dominated by Power Apps Premium at $2K/mo, then Azure floor, then AI usage, then SPE storage.

### Key numbers snapshot (list)
- App Service S1 Linux: ~$69/mo (some sources $56.94; nail down on calculator)
- Key Vault Standard: $0.03/10K ops, no fixed base — effectively free at Spaarke scale
- Blob LRS Hot: $0.018/GB/mo + ~$0.055/10K writes
- Service Bus Standard: $9.81/mo base + 13M ops free
- Redis Basic C0: ~$16/mo · Premium P1 (VNet): ~$405/mo
- Cosmos DB Serverless: $0.25/1M RU + $0.25/GB/mo
- Azure OpenAI gpt-4o: $2.50 in / $1.25 cached in / $10.00 out per 1M tokens
- Azure OpenAI gpt-4o-mini: $0.15 in / $0.60 out per 1M tokens
- text-embedding-3-large: $0.13/1M tokens (Azure has NO batch discount on embeddings even though OpenAI direct does)
- AI Search S1: **~$245/mo per SU** (older $73.73 quotes are stale)
- Document Intelligence prebuilt-layout: $10/1K pages
- Log Analytics: $2.30/GB ingested, $0.10/GB/mo interactive retention past 31 free days, $0.02/GB/mo archive
- SignalR Standard: ~$48/mo per unit (1K concurrent conns)
- Content Safety Prompt Shields (S0): $0.38/1K text records
- Power Apps Premium: $20/user/mo list, $12 at 2K+ seats
- Dataverse add-ons: DB $40/GB/mo · File $2/GB/mo · Log $10/GB/mo
- SPE storage: ~$0.20/GB/mo (= $0.0067/GB/day); API transaction rates NOT published inline — need to read from Cost Management on a live container
- M365 E3: **$39/user/mo** (up from $36 as of 2026-07-01) · E5: **$60** (up from $57) · Business Standard: **$14** (up from $12.50) — commercial suite ~8–12% list increase went into effect July 2026

### Notable structural changes since early 2025
- M365 commercial suite +8–12% list price 2026-07-01
- Power Apps per-app plan retired Jan 2026 for most channels (EA/CSP direct still have it)
- Azure Cache for Redis retirement 2028-04-30 → migrate to Managed Redis
- Dataverse file capacity doubled for D365 SKUs April 2026 (reduces file add-on line item)
- gpt-4o (2024-08-06) prices stable since Oct 2024 cut; no further gpt-4o family reductions in 2026

### Uncertainty flags (open)
- App Service S1 Linux exact rate — split between $56.94 and $69/mo across sources; need calculator confirmation
- SPE API transaction $/1K — not published on MS Learn meters page; requires live Cost Management readout
- Content Safety West US 2 / East US 2 meter availability — verify before committing
- gpt-5 region availability (West US 3, NOT West US 2) — if Spaarke uses gpt-5 (per `azure-openai-reasoning-models-2026-07.md`), the region-lock story for the whole stamp changes

### Pricing volatility watch (numbers to re-verify)
- Azure OpenAI token prices (Azure has cut these mid-year historically)
- M365 seat prices (Microsoft has now shown willingness to raise list — next window early 2027?)
- SPE meter rates (still relatively new product — pricing model could shift)
- AI Search S1 rate (large fixed floor; if this changed materially it would move the whole envelope)

### Related memories
- [[azure-openai-reasoning-models-2026-07]] — gpt-5 pricing + region availability
- [[azure-managed-redis-2026-06-26]] — successor to Cache for Redis after 2028 retirement
