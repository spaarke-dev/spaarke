---
name: cosmos-gremlin-direction
description: Cosmos DB Gremlin shows soft signals of reduced Microsoft investment (no formal deprecation, but doc cadence + lack of vector support is meaningful); document migration triggers when committing to Gremlin.
metadata:
  type: reference
---

# Cosmos Gremlin product direction signal

As of 2026-05-19, Cosmos DB for Apache Gremlin is GA and supported — *no formal deprecation* has been announced. But the following signals indicate Microsoft is steering new graph workloads elsewhere:

1. The Gremlin overview page opens with two redirect banners: high-scale graph → Cosmos DB for NoSQL; OLAP/migration → Graph in Microsoft Fabric.
2. The data partitioning doc has content date **2019-06-24** — not substantively refreshed in 5+ years.
3. No vector search support has been added to the Gremlin API; vector is NoSQL-only.
4. Community Q&A reports of regional unavailability for new Gremlin accounts in some North America regions (Microsoft attributes to capacity, but suggests reduced investment).
5. The TinkerPop-compatibility surface is partial (`support.md` documents Gremlin steps that don't work).

**Recommendation pattern**: If a Spaarke project considers Cosmos Gremlin, document explicit migration triggers up front: (a) Microsoft formally deprecates; (b) need for vector co-located with graph data; (c) deep multi-hop traversals become uneconomical under RU model. Wrap the graph driver behind a thin internal interface so swap-out is bounded.

**Alternatives to know**:
- Cosmos DB for NoSQL with adjacency-list documents + vector co-located (DiskANN/quantizedFlat). Best Microsoft-native answer for new workloads needing graph-like + vector.
- Apache AGE on Azure Database for PostgreSQL — vector (pgvector) + graph (openCypher via AGE) in one DB; not Microsoft-built but supported.
- Neo4j Aura on Azure — best-engineered graph experience, but introduces non-Microsoft vendor.

**Why**: Avoid steering Spaarke into a deprecating platform without explicit acknowledgement and an exit plan.

**How to apply**: When any Spaarke design considers Cosmos Gremlin, reference `knowledge/cosmos-gremlin/README.md` (which has the full comparison + migration triggers). Don't recommend Gremlin without naming the strategic risk.
