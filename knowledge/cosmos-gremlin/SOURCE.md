# SOURCE — cosmos-gremlin

> Provenance for Cosmos DB Gremlin curation.

**Curated**: 2026-05-19
**Curator**: researcher subagent (Insights Engine pre-design research)
**Refresh cadence**: monthly (see `knowledge/REFRESH-LOG.md`)

---

## Source documents

This topic is a **reference-only** topic — no curated samples (yet). The README.md is the substantive content; this SOURCE.md records the documentation pages consulted.

| Page | URL | Fetched / accessed | Relevance |
|---|---|---|---|
| Cosmos DB for Gremlin overview | https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview | 2026-05-19 | Status, capabilities, "consider NoSQL/Fabric" redirect banners |
| Data partitioning in Cosmos DB for Gremlin | https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/partitioning | 2026-05-19 | Partition key semantics, edge storage with source vertex, traversal direction implications |
| Cosmos DB for NoSQL vector search | https://learn.microsoft.com/en-us/azure/cosmos-db/vector-search | 2026-05-19 | Vector index types (flat / quantizedFlat / DiskANN), comparison to Gremlin (which has no vector) |
| Cosmos DB for Gremlin landing page | https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/ | 2026-05-19 | TOC and sub-articles (quickstart, write queries, modeling, security, limits) |
| Graph in Microsoft Fabric | https://learn.microsoft.com/en-us/fabric/graph/overview | 2026-05-19 | Alternative path that Microsoft now recommends for OLAP graph workloads |

## Gaps / open

- **Partitioning doc has content date 2019-06-24** — substantively unchanged in 5+ years. The technical content is still accurate but reflects Microsoft's reduced investment cadence. Monitor for replacement guidance.
- **No vector search in Gremlin.** This is the single biggest gap. If we need vector co-located with graph, Cosmos NoSQL with adjacency-list documents is the alternative, not Gremlin.
- **No first-party "graph + vector + ACL" sample on Azure** as of this curation date. Spaarke is building this combination ourselves.

## Samples not curated (deliberate)

We deliberately did not copy any sample code into this folder. The README.md is the substantive Spaarke-specific commentary; for canonical sample code we point at `https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/how-to-write-queries` and the quickstart pages. If a future project requires more depth, copy a sample with proper provenance per the knowledge base conventions.
