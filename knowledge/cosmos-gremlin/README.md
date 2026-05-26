# Cosmos DB Gremlin — Insights Engine reference (2026-05)

> **Status**: Researched 2026-05-19 for `projects/ai-spaarke-insights-engine-r1/`.
> **Curator**: researcher subagent (auto-research, not yet senior-engineer-reviewed).
> **Refresh cadence**: monthly per [`knowledge/REFRESH-PROCEDURE.md`](../REFRESH-PROCEDURE.md).

This topic exists because the Insights Engine uses Cosmos DB Gremlin for the typed entity-relationship graph (the "Insight Graph" — see [`projects/ai-spaarke-insights-engine-r1/README.md`](../../projects/ai-spaarke-insights-engine-r1/README.md)). The fundamentals here are platform-current as of 2026-05 but rest on a Cosmos product whose strategic direction is shifting (see Status).

---

## Status as of 2026-05

**Cosmos DB for Apache Gremlin is GA and supported**, but its Microsoft Learn overview page now opens with two redirect banners: one pointing high-scale graph workloads at *Azure Cosmos DB for NoSQL* (vector + JSON + can be queried in graph-like patterns), and one pointing OLAP / Apache Gremlin migrations at *Graph in Microsoft Fabric*. The Gremlin API itself shows no formal deprecation announcement, but Microsoft's product investment for new graph workloads is visibly going elsewhere — the overview page was last refreshed for content July 2025, the partitioning doc hasn't been substantively revised since 2019, and *no vector search support exists in the Gremlin API* (vector is NoSQL-only). For Spaarke this is a meaningful strategic risk: we'd be building on a platform that still works but is no longer the recommended path for new graph workloads.

## Key URLs consulted (2026-05-19)

- [Cosmos DB for Gremlin overview](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview) — last updated 2026-04-27 (mostly metadata; content date 2025-07-22)
- [Data partitioning in Cosmos DB for Gremlin](https://learn.microsoft.com/en-us/azure/cosmos-db/gremlin/partitioning) — content date 2019-06-24 (not refreshed in years)
- [Cosmos DB for NoSQL vector search](https://learn.microsoft.com/en-us/azure/cosmos-db/vector-search) — last updated 2026-05-04
- [Microsoft Q&A: Is Cosmos DB Gremlin API deprecated?](https://learn.microsoft.com/en-gb/answers/questions/3145158/is-cosmos-db-gremlin-api-deprecated) — community thread referencing regional creation constraints
- [Graph in Microsoft Fabric overview](https://learn.microsoft.com/en-us/fabric/graph/overview) — alternative path for OLAP graph
- [Cosmos DB capacity calculator](https://cosmos.azure.com/capacitycalculator/) — RU/s sizing tool

## Findings

### 1. Current state of Cosmos Gremlin (deprecation signals)

There is no public deprecation notice. Cosmos Gremlin remains a GA Cosmos API alongside NoSQL, MongoDB (RU), Cassandra, and Table. **However**, the soft signals are clear:

- The overview page leads with *"Looking for a high-scale database with 99.999% SLA? Consider Cosmos DB for NoSQL"* — Microsoft steering people away from Gremlin for new workloads.
- The partitioning best-practices doc (`partitioning.md`) was last substantively revised in **June 2019**.
- No vector search support has been added to the Gremlin API; vector is NoSQL-only.
- Some users report regional unavailability for new Gremlin account creation in certain North America regions (per Q&A thread above) — Microsoft attributes this to capacity/quota, not deprecation, but it suggests reduced investment.
- The TinkerPop-compatibility surface is partial (see the `support.md` doc on the same site) — not every Gremlin step works at full Cosmos scale.

**Recommendation**: If we choose Gremlin, treat it as a known-good legacy substrate. Don't bet new strategic features on it. Have a documented migration story to either Cosmos NoSQL with materialized adjacency lists or to Apache AGE on Postgres if Microsoft signals stronger deprecation in the next 12–18 months.

### 2. Vertex / edge schema patterns for a domain graph

The Insights Engine graph stores legal-domain entities (matters, parties, documents, decisions, outcomes) and typed relationships between them. The recommended pattern:

- **Vertices represent entities**, with all entity properties embedded as vertex properties (the "property-embedded vertices" approach Microsoft recommends). Don't normalize properties into child vertices unless they're independently queryable or shared across entities.
- **Edges represent typed relationships** with their own properties (timestamp, confidence, derived_from playbook+version). Edge labels carry the semantic relationship type (`REPRESENTED_BY`, `BELONGS_TO_MATTER`, `RESOLVED_BY`, etc.).
- **Partition key choice is irreversible** and dictates whether queries are intra- or cross-partition. For Spaarke: partition by **`matterId`** at the vertex level. Matter is the unit of access control and the natural query boundary ("show me everything connected to matter X"). Cross-matter graph traversals are rare and acceptable as cross-partition queries.
- **Edges are stored on the source vertex's partition**, with a copy of the target vertex's partition key and ID. This means `out()` traversals stay scoped to the source partition (cheap), but `in()` traversals fan out to all partitions (expensive). Model relationships with traversal direction in mind. If you need to query "what matters reference this document?" frequently, store the edge with the document as the source — even if conceptually the matter "owns" the document.

Canonical vertex example:

```groovy
g.addV('Document')
 .property('id', 'doc-001')
 .property('matterId', 'matter-12345')           // partition key
 .property('title', 'NDA - Acme Corp')
 .property('artifactType', 'nda')
 .property('createdAt', '2026-04-12')
```

Canonical edge example (Document → cited Authority):

```groovy
g.V(['matter-12345', 'doc-001'])
 .addE('CITES')
 .property('confidence', 0.93)
 .property('extractedBy', 'playbook-citation-extract@1.2')
 .property('extractedAt', '2026-05-01')
 .to(g.V(['matter-12345', 'authority-007']))
```

### 3. Performance and RU/s sizing

Key Cosmos Gremlin performance characteristics:

- Cosmos **doesn't cache the graph in memory between queries** — every traversal step reads from storage. Multi-hop traversals therefore cost more RU than they would on JanusGraph/Neo4j, where the working set stays in RAM. Architect with this in mind: prefer shallow, partition-scoped traversals over deep multi-hop queries.
- **Always specify the partition key value when starting a traversal.** `g.V('id')` without partition-key qualification triggers a cross-partition lookup. Use `g.V(['matterId-value', 'vertex-id'])` (tuple form) or `g.V('vertex-id').has('matterId', 'matterId-value')`.
- **Avoid `inE()` / `in()` traversals.** They fan out to all partitions. If you need to walk an edge "the wrong direction" frequently, add a reverse edge at write time.
- **Partition size limit**: 20 GB per logical partition. For a Spaarke matter graph, this is essentially never a constraint (one matter would have to have hundreds of thousands of vertices to hit it).

**RU/s sizing for 1M–10M vertices per tenant**:

| Tenant size | Vertices + edges (est.) | Storage (est.) | Provisioned RU/s recommendation | Approx monthly cost (East US 2) |
|---|---|---|---|---|
| Small (50 matters, 20k entities) | ~100k items | ~1 GB | Autoscale 1k–10k RU/s | ~$60–$80 idle, scales up under load |
| Mid (500 matters, 500k entities) | ~2M items | ~10 GB | Autoscale 4k–40k RU/s | ~$300–$400 average |
| Large (2,000 matters, 5M+ entities) | ~10M+ items | ~50–100 GB | Autoscale 10k–100k RU/s, monitor and tune | ~$700–$1,200 average |

Sizing assumptions: standard graph reads ~3–10 RU per simple vertex fetch with partition key, ~50–500 RU per multi-hop traversal depending on depth. Use the **autoscale** model not standard provisioned — graph workloads have spiky read patterns (synthesis-time bursts when the Insights Agent walks the graph for an inference). Minimum autoscale floor is 10% of max, so a 10k–100k autoscale container costs the same as 10k provisioned when idle.

### 4. Query patterns for "find connected entities"

Insights Engine query patterns map onto Gremlin idioms:

- **"What's connected to this matter?"** — partition-scoped fanout. Cheap.
  ```groovy
  g.V().has('matterId', 'matter-12345').out().path()
  ```
- **"Find similar matters by shared parties and authorities"** — multi-hop, but bounded by partition + similarity heuristics. Run in app code by fetching anchor matter's parties + authorities, then issuing parallel partition-scoped lookups across candidate matter partitions.
- **"Who has worked on cases of this type?"** — index-style query better answered by a parallel projection in Azure AI Search than by graph traversal. Reach for the graph only for relationship-shape queries; reach for AI Search for "find by attributes" queries.

The Insights Engine's design correctly scopes the graph to **typed relationships only**. Use Azure AI Search for retrieval-by-content; use Gremlin for relationship traversal; never duplicate.

### 5. Cosmos NoSQL with vector + graph-like patterns vs. Gremlin

This is the most strategically important question. The 2026 state:

- **Cosmos NoSQL has GA vector search** (flat / quantizedFlat / DiskANN indexes, up to 4096 dimensions). DiskANN is Microsoft Research's own algorithm and is the current performance leader. Vector search composes with WHERE clauses, so you get filtered vector search natively.
- **Cosmos NoSQL doesn't have native graph traversal**. You'd model adjacency as document properties (`document.outgoingEdges: [{type, targetId, props}]`) and walk the graph in application code or via multi-query SDK patterns. No `g.V().out().out()` shorthand.
- **Practical implications**: For Spaarke's graph use cases (shallow traversal, partition-scoped) the app-code adjacency-list pattern on Cosmos NoSQL would work, and would unlock vector search co-located with graph data. For deep multi-hop traversals, this becomes painful — you'd be re-implementing a graph engine.

**Decision criterion**: If the graph workload is mostly 1–3 hops scoped to a matter, Cosmos NoSQL with adjacency-list documents is the lower-risk choice for 2026+. If we'll regularly do 4+ hop traversals, stay on Gremlin and accept the strategic risk.

### 6. Comparison: Cosmos Gremlin vs. PostgreSQL + Apache AGE vs. Neo4j Aura

| Dimension | Cosmos Gremlin | Postgres + Apache AGE | Neo4j Aura on Azure |
|---|---|---|---|
| **Microsoft-native** | Yes | No (third-party extension on Azure Database for PostgreSQL) | No (Neo4j is a separate vendor; Aura on Azure is a marketplace offer) |
| **Strategic momentum** | Weakening (no vector, doc not refreshed) | Strong in open-source graph community; AGE is solidly maintained | Strong (Neo4j is the dominant graph product) |
| **Query language** | Apache Gremlin (TinkerPop, partial impl) | openCypher via AGE | Native Cypher (fullest impl) |
| **Latency at 1M-10M vertices** | Higher (no in-memory cache, RU-based) | Lower (Postgres buffer cache) | Lowest (purpose-built engine) |
| **Cost model** | RU/s autoscale, storage per GB | vCore + storage on Postgres | Per-instance pricing, generally highest |
| **Vector co-located** | No | Yes (pgvector + AGE in same DB) | Yes (Neo4j has vector indexes) |
| **HA / global distribution** | Multi-region native | Azure-region replicas, no multi-master | Aura tiers; not as deeply Azure-native |
| **Operational burden** | Lowest (fully managed) | Medium (managed Postgres, but AGE extension to maintain) | Low (Neo4j manages it) |
| **For an Azure-native ISV building a legal-tech product** | OK but risky on the deprecation axis | Most pragmatic if we want vector + graph in one store | Best-engineered graph experience, but adds a non-Microsoft vendor to the stack |

**Honest recommendation for Spaarke**: Cosmos Gremlin is fine for r1. The cost of migrating later is bounded because the Insights Engine wraps the graph behind an internal interface. But document the migration trigger criteria clearly: (a) Microsoft formally deprecates Gremlin, or (b) we need vector co-located with graph data, or (c) we need deep multi-hop traversals that the RU model makes uneconomical.

## Implications for Spaarke Insights Engine

1. **Partition key = `matterId`** is the right call. It aligns with the access control boundary, keeps traversals scoped, and makes per-tenant deletion (matter close-out) trivial.
2. **Always store edges with `extractedBy: playbook@version`** so that Observations and the graph derived from them can be re-extracted when a playbook ships v2. This is the same versioning concern as the AI Search Observation index.
3. **Plan for the graph to be re-buildable from Facts + Observations.** Don't store anything in the graph that isn't reconstructible. The graph is a materialized derivation, not a source of truth.
4. **Use autoscale provisioned throughput**, not standard provisioned. Insights Engine query patterns are bursty (synthesis walks the graph at agent inference time, then idles).
5. **Build a thin internal interface** (`IInsightGraph` or similar) around the Gremlin driver. Then if/when we migrate to Cosmos NoSQL adjacency-lists or Neo4j, the BFF doesn't need to change.

## Open questions

- **Will Microsoft formally deprecate Cosmos Gremlin in the next 24 months?** Soft signals say "investment is moving" but no announcement exists. Worth asking a Microsoft account team for guidance before committing to Gremlin at scale.
- **Is there a Spaarke use case requiring 4+ hop traversal?** Worth scoping in the design phase. If yes, the case for Neo4j strengthens significantly.
- **Cosmos NoSQL hierarchical partition keys + vector** — there's a note in the vector search doc that customers using hierarchical partition keys with vectors should contact `cosmossearch@microsoft.com`. Interesting capability if we ever consider co-locating graph + vector on Cosmos NoSQL.
- **Bicep deployment** of a per-tenant Cosmos Gremlin account is straightforward, but the *graph schema* (vertex labels, edge labels) is not deployed declaratively — it emerges from the first writes. Need a schema-bootstrap script as part of tenant onboarding.
