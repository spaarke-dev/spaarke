# SOURCE — dataverse-sync

> Provenance for Dataverse → Azure AI Search sync pattern research.

**Curated**: 2026-05-19
**Curator**: researcher subagent (Insights Engine pre-design research)
**Refresh cadence**: monthly (see `knowledge/REFRESH-LOG.md`)

---

## Source documents

This topic is a **reference-only** topic — no curated samples (yet). The README.md is the substantive content; this SOURCE.md records the documentation pages consulted.

| Page | URL | Fetched / accessed | Relevance |
|---|---|---|---|
| Use Webhooks to Create External Handlers for Server Events | https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks | 2026-05-19 | Webhook auth options, payload format, 256 KB threshold, retry semantics |
| Azure Service Bus Integration for Dataverse | https://learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration | 2026-05-19 | Service Bus contracts (queue, one-way, two-way, REST, topic, event hubs), async system jobs, 192 KB threshold |
| Microsoft 365 Copilot connectors overview | https://learn.microsoft.com/en-us/microsoftsearch/connectors-overview | 2026-05-19 | Why these don't replace custom sync (target Microsoft Graph, not AI Search) |
| Microsoft Search API in Microsoft Graph overview | https://learn.microsoft.com/en-us/graph/search-concept-overview | 2026-05-19 | Distinction between Microsoft Search and application AI Search |
| Azure AI Search Data Sources Gallery | https://learn.microsoft.com/en-us/azure/search/search-data-sources-gallery | 2026-05-19 | Confirms no built-in Dataverse indexer |
| Microsoft Graph search connector sample | https://github.com/microsoftgraph/msgraph-search-connector-sample | 2026-05-19 | Reference for the Graph-connector path (not our chosen path but useful context) |

## Gaps / open

- **No first-party "Dataverse → Azure AI Search" reference architecture exists.** This is genuinely an ISV-builds-it space. Spaarke is doing standard work here, just not work Microsoft has published a canonical pattern for.
- **Dataverse change tracking docs** were referenced but not deep-fetched in this curation pass. Worth pulling into curation when the sync wiring project (`phase-1-sync-wiring`) starts.
- **Dataverse Service Bus integration with Microsoft Entra-based auth (vs. SAS)** — supported but newer; specific docs on the MI flow are worth a focused research session.

## Samples not curated (deliberate)

No sample code copied. When Phase 1 sync-wiring kicks off, candidates to curate:
- `microsoftgraph/msgraph-search-connector-sample` — Graph connector sample (anti-pattern reference: what we're NOT doing).
- An Azure Functions Service Bus + AI Search example from `Azure-Samples/`.
- The Dataverse Web API pagination + change-tracking sample.
