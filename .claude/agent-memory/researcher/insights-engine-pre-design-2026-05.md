---
name: insights-engine-pre-design-2026-05
description: Five-topic research pass for ai-spaarke-insights-engine-r1, covering Azure AI Search, Cosmos Gremlin, Azure Functions ISV patterns, Dataverse sync, and Foundry memory.
metadata:
  type: project
---

## 2026-05-19: Insights Engine pre-design research

**Question**: Research five Microsoft/Azure topics foundational to the Insights Engine — Azure AI Search (hybrid/vector/integrated vectorization), Cosmos Gremlin, Azure Functions for ISV multi-tenant, Dataverse → AI Search sync, Foundry memory patterns. Produce knowledge artifacts in `knowledge/`.

**Findings**: Created four new knowledge folders (`cosmos-gremlin/`, `azure-functions-isv/`, `dataverse-sync/`, `foundry-memory-patterns/`) and one supplement (`azure-ai-search/insights-engine-supplement.md`). Five major decisions documented:

1. **AI Search**: Use push-mode at ingest (Dataverse not a supported indexer source) + integrated vectorizer at query time. `vectorFilterMode=preFilter` is mandatory for ACL trimming with high-cardinality groups. Store `playbookId@version` on every Observation for re-extraction support.
2. **Cosmos Gremlin**: GA but Microsoft's investment is visibly moving (no vector, partitioning doc dates 2019). OK for r1 with `matterId` partition key + autoscale RU/s, but wrap in `IInsightGraph` interface for portability. Document migration triggers (formal deprecation, need for vector co-location, deep multi-hop traversal needs).
3. **Azure Functions**: Flex Consumption with 1 always-ready instance per tenant + per-tenant UAMI + per-tenant Service Bus topic. Bicep tenant-list-as-configuration for r1; design for tenant-list-as-data by Phase 2.
4. **Dataverse sync**: Service Bus topic for real-time + Timer Function for change-tracking reconcile. Microsoft 365 Copilot connectors do NOT replace this (target Microsoft Graph, not app AI Search). Webhook is a trigger only — always re-fetch from Dataverse for source of truth.
5. **Foundry memory**: Now publicly documented (2026-04). Custom BFF Insights Agent is correct architecture; Foundry-hosted is for *downstream* multi-day matter-diligence surfaces. Borrow Foundry's two-tier memory pattern (static user_profile + contextual chat_summary) for Insights Agent personalization via Redis.

**Sources**:
- `knowledge/azure-ai-search/insights-engine-supplement.md`
- `knowledge/cosmos-gremlin/README.md`
- `knowledge/azure-functions-isv/README.md`
- `knowledge/dataverse-sync/README.md`
- `knowledge/foundry-memory-patterns/README.md`
- 8 Microsoft Learn pages consulted (full URLs in each topic's SOURCE.md)
- `knowledge/REFRESH-LOG.md` updated with 2026-05-19 entry

**Why**: Insights Engine is in pre-implementation design; this research is foundational so the design.md doesn't repeat fundamentals.

**How to apply**: When the Insights Engine design or implementation tasks raise questions on any of the five topics, read the corresponding `knowledge/*/README.md` first. The supplement and READMEs include both findings and explicit Spaarke implications + open questions.

**Open questions** to revisit:
- Microsoft's strategic direction on Cosmos Gremlin (deprecation watch).
- Cosmos NoSQL adjacency-list pattern vs. Gremlin — worth a prototype spike before Phase 1 commits to Gremlin.
- Foundry memory GA pricing model (preview is free-API, paid-tokens-only).
- Whether the Insights Agent should expose A2A endpoint for future Foundry-hosted consumers.
