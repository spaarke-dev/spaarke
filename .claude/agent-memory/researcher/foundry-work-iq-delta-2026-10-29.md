---
name: foundry-work-iq-delta-2026-10-29
description: Oct-2026 delta over foundry-iq-work-iq-state-2026-09-21 — Foundry IQ unchanged (no new Search API version), GPT-6 family GA in Foundry, Routines GA + Copilot harness preview, Work IQ MCP `retrieve` tool (SPE, preview), Dataverse-in-Work-IQ deprioritised, Agent Framework 1.26
metadata:
  type: project
---

# Foundry + Work IQ delta (2026-09-21 → 2026-10-29)

Read with [[foundry-iq-work-iq-state-2026-09-21]] (baseline). Baseline judgments all **hold**: Foundry IQ = buy planner only / keep ingestion; SPE-as-remote-KS non-starter for ISV; Work IQ user-turn only.

## Foundry IQ / AI Search — no material change
- No new Search **data-plane** api-version after `2026-08-01-preview` (api-versions page 2026-08-31). Only control-plane `Search Management 2026-09-01-preview` (whats-new 2026-10-09).
- `2026-04-01` GA still "minimal, extractive" (create-KB 2026-08-12, upd 2026-09-17). Planner/synthesis/`retrievalInstructions`/`filterAddOn`/permission trimming all still preview.
- Still 12 KS kinds, **no `sharePointEmbedded`**; remote SharePoint still preview, same-tenant, Copilot licence/PAYG (2026-10-01).
- Planner model list ends at `gpt-5.6-*` — **GPT-6 not yet a planner model**.

## Foundry models / Agent Service
- **GPT-6 GA in Foundry**: Astra 09-03 ($10/$50 per 1M), **Luna 09-22 ($0.10/$0.50)**, GPT-6.1 Sol 09-29 ($2/$10) (Foundry blog 2026-10-15; Learn models page 2026-10-08). Luna = new cheap AOAI tier → MAI not needed for cost tiering.
- MAI-Thinking-1 still preview, GlobalStandard only, quota 0 default (2026-08-12).
- Claude (Opus 5.5) in Foundry announced 09-24, no GA label; via Azure Marketplace, Messages API; **CSP subscriptions not supported** (Learn 2026-09-21).
- Pricing: from 2026-09-01 Standard premiums for post-Sep-1 models; EU Data Zone + non-US regional PTU increases (blog 2026-10-15) → check EU stamp cost model.
- **Routines GA 09-24** (scheduled agents, "agent identity" run mode); A2A, Tool Search, toolboxes-for-hosted-agents GA; hosted agents unlabelled-preview, background runs use agent Entra identity (Learn 2026-10-19). **Copilot harness public preview 10-28** — free to 2026-12-01 then $0.0994/vCPU-h + $0.0118/GiB-h. Memory still preview.

## Work IQ
- App-only still **not supported** (Foundry Work IQ tool page upd 2026-10-23; permissions 2026-06-17).
- **New MCP `retrieve` tool — public preview 2026-10-13**: grounding markdown + `retrievalHits`; "supports SharePoint Embedded content that the signed-in user can access", scoped by URL / SharePoint IDs. Delegated; runs in customer tenant (so the search-service-same-tenant blocker does not apply) but cannot apply Spaarke's tenant/group filters and only sees content the *user* can reach via SPE container permissions. Prereqs (discoverability/PAYG/Copilot licence) not stated. Fetch prefixes unchanged.
- Work IQ SP now **auto-created on first use** (MCP overview 2026-10-05) — Foundry page still says Global Admin provisions it (doc drift).
- **Dataverse in Work IQ: "roadmap source… isn't a near-term priority. No delivery timeline is committed"** (retrieve page 2026-10-13). Drop plans depending on Copilot reasoning over Spaarke Dataverse via Work IQ.
- Context/Workspaces still no endpoint pages; credit rates unchanged ($0.01/credit still [Reported] only).

## Agent Framework
- **.NET 1.26.0 (2026-10-23), Python 1.23.0 (2026-10-22)**. Breaking: 1.23.0 (tool changes between runs, Azure.AI.Projects 3.0.0-beta.3, OpenAI 2.14.0, MEAI 10.10.1, config-key allow list, approval-response binding enforced); 1.25.0 Cosmos chat metadata decoupled from partition key; 1.26.0 Docker param rename. Approvals hardening (auto-reject unanswered, duplicate IDs rejected). Weekly cadence — pin.
- BFF today: MEAI 10.3.0, Azure.AI.Projects 1.0.0-beta.8, Azure.Search.Documents 12.0.0, no Agent Framework package.

## Open
- Are Spaarke users SPE container members, or does the BFF act app-only? Decides whether Work IQ `retrieve` sees any Spaarke documents.
- Retrieval API `sharePointEmbedded` data source v1.0 — unverified (pages 404'd).
- Re-check after **Ignite 17–20 Nov 2026** [Reported] for a new Search API version / Foundry IQ GA scope.
