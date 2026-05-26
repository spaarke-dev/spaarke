# Spaarke Insights Engine — Comprehensive Design (r1)

> **Status**: DRAFT — pre-implementation. Subject to spike outcomes.
> **Last Updated**: 2026-05-19
> **Authors**: Spaarke Engineering
> **Companion documents**: [README.md](README.md) · [ai-inventory.md](ai-inventory.md) · [azure-inventory.md](azure-inventory.md)
> **Knowledge base**: [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) · [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/) · [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/) · [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/) · [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/)

---

## 1. Executive Summary

### 1.1 What this is

The **Spaarke Insights Engine** is the technical layer that realizes the Memory and Inference layers of the Legal IQ Stack. It is a back-end component responsible for transforming signals from systems of record (Dataverse, SharePoint Embedded, AI sessions) into context that can be honestly served to AI agents and end users across multiple surfaces (Context pane, form widgets, ribbon flyouts, Outlook/Teams add-ins, notifications, public API).

Its scope is bounded. It does NOT include:
- Source systems (Dataverse, SPE, AI session storage)
- Presentation surfaces (the Context pane, form UIs, etc.)
- General BFF endpoints (chat, document operations, analysis)

It DOES include:
- Derived artifact stores (vector index + entity graph)
- A synthesis layer that answers context questions on demand with grounded provenance
- Sync pipelines that keep derived stores in sync with sources
- Extraction pipelines that produce new derived artifacts (Observations) from milestones (e.g., matter closure)

### 1.2 The problem it solves

The IQ Stack marketing makes a specific claim: *"Based on 200 similar matters your department has handled, this one will likely cost $280K and take 14 months."* This is **organizational inference grounded in specific past matters with citable provenance** — distinct from generic AI hedging based on industry data.

Today's Spaarke stack can support **session-scoped context** (the Context pane shows playbook galleries, citations, progress) and **record-scoped lookups** (RAG against documents) — but it cannot deliver the compounding cross-matter intelligence the marketing promises. The Insights Engine is the bridge.

### 1.3 Honesty contract

Every artifact produced by the Engine carries provenance. Every Inference cites specific evidence. Every claim about evidence sufficiency is explicit. Empty states are first-class: when comparable data is insufficient, the response is *"need ~N more matters to answer reliably"* — never a silent fallback to generic AI.

### 1.4 What you'll find in this document

| Section | Content |
|---|---|
| §2 | Conceptual model — Fact / Observation / Inference taxonomy |
| §3 | Architecture — component map, request flow, tenant isolation |
| §4 | Substrate decisions — AI Search, graph, Live Facts |
| §5 | Synthesis layer — Insights Agent design |
| §6 | Data flow — Dataverse → indexes/graph, extraction, backfill |
| §7 | Privilege model — per-tenant + in-tenant access trimming |
| §8 | Surface integration — pane, widget, ribbon, Outlook |
| §9 | Packagability — Bicep, deployment, schema migration |
| §10 | AI inventory reconciliation — what existing services map to Engine components |
| §11 | Phased plan — what ships when |
| §12 | Open questions / deferred decisions |

---

## 2. Conceptual Model

### 2.1 The three-artifact taxonomy

Every piece of context the Engine produces is one of three things, each with a different trust profile, store, and presentation rule.

| Type | Source | Confidence | Lives in | Presented as |
|---|---|---|---|---|
| **Fact** | Deterministic computation over systems of record | 1.0 always | Live query OR materialized feature view | Stated directly. No hedging. |
| **Observation** | Probabilistic extraction by playbook/LLM at a milestone | 0.0–1.0 | Insight Index (AI Search) + Insight Graph references | Stated with confidence + evidence link |
| **Inference** | Synthesized on demand by the Insights Agent over Facts + Observations | 0.0–1.0 | Never authoritatively stored (cached only) | Stated with confidence, comparable set, reasoning |

#### 2.1.1 Examples to anchor the taxonomy

- **Fact**: `Matter M-1234 was pending 287 days` — computed from `closedDate - openedDate`. Always true given the source. State it directly.
- **Observation**: `Matter M-1234 outcome quality: favorable (0.92)` — produced by a closure-extraction playbook reading documents and decisions. Carries confidence and evidence. Cite the playbook + source docs.
- **Inference**: `Predicted cost for this new matter: ~$280K (confidence 0.74), based on 12 comparable matters` — synthesized at query time. Cite the 12 matters and their actual numbers. If only 3 are comparable, return "insufficient evidence" with the gap.

### 2.2 The artifact envelope

Every artifact — Fact, Observation, or Inference — uses the same envelope so surfaces can render them uniformly:

```json
{
  "id": "stable-identifier",
  "type": "fact | observation | inference",
  "subject": { "entityType": "matter", "entityId": "M-1234" },
  "predicate": "totalSpend | outcomeQuality | predictedCost | ...",
  "value": {
    "raw": <typed value>,
    "displayHint": "currency-usd | percentage | duration-days | enum | text"
  },
  "confidence": 0.0,
  "evidence": [
    { "refType": "fact-source", "ref": "dataverse://sprk_matter/M-1234#totalSpend" },
    { "refType": "document", "ref": "spe://drive/abc/item/xyz" },
    { "refType": "comparable-matter", "ref": "matter://M-0567" },
    { "refType": "playbook-run", "ref": "playbook://closure-extraction@v3/run-2026-04-12" }
  ],
  "asOf": "2026-05-19T08:30:00Z",
  "validFrom": "2026-05-19T08:30:00Z",
  "validTo": null,
  "producedBy": {
    "kind": "query | playbook | agent",
    "id": "playbook://closure-extraction@v3 | query://matter-duration | agent://insights-v1",
    "version": "v3"
  },
  "scope": {
    "tenantId": "tenant-acme",
    "matterId": "M-1234",
    "clientId": null,
    "practiceArea": "ip-licensing",
    "jurisdiction": "us-ca",
    "year": 2024
  },
  "embedding": [0.123, ...],
  "tenantId": "tenant-acme"
}
```

Notes:
- `tenantId` is a top-level field (not just inside `scope`) so it can be a filterable index field at the substrate level.
- `producedBy.version` is **mandatory for Observations** — enables re-extraction when extraction playbooks improve (per knowledge research: *"store `playbookId@version` on every Observation to enable re-extraction when playbooks ship v2"*).
- `embedding` is populated for Observations and Inferences; null for Facts (which are typically retrieved by direct filter, not similarity).
- `validFrom` / `validTo` model temporal validity — Facts age (e.g., "total spend as of 2026-05-19" might differ from "total spend as of 2026-06-30").

### 2.3 Why this split is architecturally load-bearing

1. **Different stores**: Facts can live in a live query or simple document store. Observations need vector + structured. Inferences need an agent — they're never persisted as truth.
2. **Different APIs**: Facts return `{value}`. Observations and Inferences return `{value, confidence, evidence[], asOf, ...}`.
3. **Different UI rules**: Facts display directly. Observations/Inferences must show provenance + confidence + evidence count + an empty/insufficient-evidence state.
4. **Different governance**: Facts inherit lineage from their source query. Observations need extraction-quality monitoring. Inferences need evidence-sufficiency rules.
5. **Composition**: Inferences USE Facts as inputs. "Predicted cost" composes historical "totalSpend" Facts. Facts are foundational.

Without this split, "intelligence" silently mixes things-we-know with things-we-guess — which is the trick the IQ Stack article explicitly disavows.

### 2.4 Provenance is the API contract

Every artifact carries its evidence. The surface receives `{value, evidence[]}` as a minimum; rendering provenance is non-optional. If a surface cannot display provenance, it cannot display Inferences (only Facts, which need none beyond their source query).

This single rule prevents most ways the system could become dishonest.

---

## 3. Architecture

### 3.1 Where the Insights Engine sits

```
┌─────────────────────────────────────────────────────────────────────────┐
│  SURFACES (consumers — outside the Engine)                              │
│  Context pane │ Form widget │ Ribbon flyout │ Outlook │ Teams           │
│  Notification │ Workspace card │ Email digest │ Public API              │
└──────────────────────────────┬──────────────────────────────────────────┘
                               │ InsightRequest / InsightResponse
                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  ─── SPAARKE INSIGHTS ENGINE ──────────────────────────────────────────│
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ InsightsResolverService  (BFF — Sprk.Bff.Api)                   │   │
│  │ • Question router (catalog lookup | ad-hoc routing)             │   │
│  │ • Signal fetcher (parallel; AI Search + Graph + Live Facts)     │   │
│  │ • Analyzer dispatch (deterministic query | LLM | playbook)      │   │
│  │ • Provenance assembler                                          │   │
│  │ • Cache + freshness policy (per-question TTL)                   │   │
│  │ • Privilege trimming (accessibleMatterSet enforcement)          │   │
│  └────────────────────────────┬────────────────────────────────────┘   │
│                               ▼                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ Insights Agent  (custom in BFF; uses existing IChatClient)      │   │
│  │ • Tool-call pipeline (Microsoft.Extensions.AI                   │   │
│  │   UseFunctionInvocation)                                        │   │
│  │ • Two-tier memory (Redis: user_profile + chat_summary)          │   │
│  │ • Insight tools: FindComparableMatters, GetMatterFacts,         │   │
│  │   AssessEvidenceSufficiency, RetrieveByGraph, ...               │   │
│  │ • Grounded synthesis with evidence-sufficiency rules            │   │
│  └────────────────────────────┬────────────────────────────────────┘   │
│                               │                                         │
│         ┌─────────────────────┼─────────────────────┐                   │
│         ▼                     ▼                     ▼                   │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐            │
│  │ Insight Index  │  │ Insight Graph  │  │ Live Fact      │            │
│  │ (AI Search)    │  │ (Cosmos —      │  │ Resolver       │            │
│  │ • vector +     │  │  see §4.2)     │  │ • Direct       │            │
│  │   metadata     │  │ • entities +   │  │   Dataverse    │            │
│  │ • per-tenant   │  │   typed edges  │  │   queries      │            │
│  │   indexes      │  │ • artifact     │  │ • on-demand    │            │
│  │                │  │   refs         │  │                │            │
│  └────────────────┘  └────────────────┘  └────────────────┘            │
│         ▲                     ▲                                         │
│         │  Sync + Extraction Pipelines                                  │
│         │  (Azure Functions — Flex Consumption)                         │
│  ┌──────┴────────────────────────────────────────────────────────┐    │
│  │ • Dataverse → Insight Index sync                              │    │
│  │   - Service Bus topic (real-time)                             │    │
│  │   - Timer-triggered reconciliation (change-tracking)          │    │
│  │ • Closure-extraction trigger (matter status → JPS playbook)   │    │
│  │ • Scheduled re-indexer (embedding refresh, version migration) │    │
│  └───────────────────────────────────────────────────────────────┘    │
│                                                                         │
└──────────┬─────────────────────────────────┬───────────────────────────┘
           │                                 │
           ▼                                 ▼
┌─────────────────────┐         ┌──────────────────────────────────────┐
│ SIGNALS (sources —  │         │ SHARED SUBSYSTEMS                    │
│ outside the Engine) │         │ Service Bus │ App Insights │         │
│ Dataverse │ SPE     │         │ Key Vault │ Managed Identity         │
│ AI sessions │ etc.  │         │                                      │
└─────────────────────┘         └──────────────────────────────────────┘
```

### 3.2 Component summary

| Component | Lives in | Purpose | Reuses existing? |
|---|---|---|---|
| **InsightsResolverService** | `Sprk.Bff.Api/Services/Insights/` (new) | Question router + signal fetcher + provenance assembler + cache + trimming | NEW |
| **Insights Agent** | `Sprk.Bff.Api/Services/Insights/Agent/` (new) | Tool-driven grounded synthesis | Reuses `IChatClient` + tool framework |
| **Insight Index** | Azure AI Search (existing service, new indexes) | Vector + metadata retrieval of Observations + materialized Facts | Existing AI Search service |
| **Insight Graph** | Cosmos DB (new account, see §4.2) | Entity + typed-edge traversal | NEW resource |
| **Live Fact Resolver** | `Sprk.Bff.Api/Services/Insights/Facts/` (new) | Cheap on-read Dataverse queries | Reuses `IDataverseService` |
| **Sync Functions** | New Azure Function App | Dataverse → Insight Index sync (Service Bus + Timer) | Pattern from existing `DataverseIndexSyncService` |
| **Extraction Functions** | Same Function App | Triggers closure-extraction JPS playbook on matter milestone events | Calls existing `PlaybookExecutionEngine` via BFF or directly |
| **Re-indexer Function** | Same Function App | Scheduled: embedding refresh, schema migration, version-bump re-extraction | NEW |

### 3.3 Request flow — typical Inference query

A surface asks "predict cost and flag risks for this new IP licensing matter against Counterparty Acme":

1. **Surface** → `POST /api/insights/ask` with `{question, subject, scope, surfaceHint}`.
2. **InsightsResolverService** receives `InsightRequest`. Resolves user's `accessibleMatterSet` from cache (per-session, sourced from existing access control system).
3. **Question router** classifies: known question template (catalog hit) OR ad-hoc (route to Insights Agent directly).
4. **Insights Agent** is invoked with the question + context. It uses tools to gather evidence:
   - `FindComparableMatters({practiceArea, dealRange, counterparty})` → vector + filter retrieval against `insight-matters` index, returns N artifacts (each with embedded numeric facts).
   - `GetMatterFacts({matterIds: [...]})` → batch live-fact lookup for current state of returned matters.
   - `TraverseGraph({startNode: 'party:acme', relations: ['OPPOSED→Matter', 'INVOLVED_PARTY→Matter'], maxHops: 2})` → graph traversal returns connected matters trimmed by accessibleMatterSet.
   - `AssessEvidenceSufficiency({comparableCount, minRequired: 12})` → returns sufficient/insufficient with gap analysis.
5. **Synthesis**: Agent produces an Inference artifact with `{value, confidence, evidence[], reasoning}`. If insufficient → returns `{type: 'insufficient_evidence', value: null, ...}` with gap explanation.
6. **InsightsResolverService** caches result (TTL per question type), assembles full provenance, returns `InsightResponse` to surface.

### 3.4 Request flow — typical Fact query

Surface asks "how long has this matter been pending":

1. **Surface** → `POST /api/insights/ask` with `{question: 'matter-duration', subject: 'matter:M-1234'}`.
2. **InsightsResolverService**: question router recognizes deterministic question.
3. **Live Fact Resolver**: `closedDate - openedDate`, single Dataverse query. `confidence: 1.0, evidence: [dataverse://sprk_matter/M-1234]`.
4. Cached briefly (e.g., 5 minutes) for repeat calls.
5. Returned to surface.

No Agent involvement. No retrieval. Single round-trip.

### 3.5 Tenant isolation model

**Decision**: Physical per-tenant isolation. Each customer tenant gets:
- Own AI Search service (or shared service with per-tenant indexes — see §7 for the tradeoff)
- Own Cosmos account for the Insight Graph
- Own Function App (per-tenant UAMI, per-tenant Service Bus topic — knowledge research confirms this is the recommended ISV pattern via Flex Consumption + tenant-list-as-configuration)
- Own subset of App Service / shared resources where economically reasonable

For Spaarke r1 with a small number of tenants, **tenant-list-as-configuration in Bicep** is sufficient. Past ~10 tenants, evolve to a tenant-list-as-data control plane (Phase 2 — knowledge research notes this inflection).

---

## 4. Substrate Decisions

### 4.1 Vector + structured retrieval — Azure AI Search

#### 4.1.1 Decision

Use the existing Azure AI Search service (already at `spaarke-search-prod` and `spaarke-search-dev`). Add new indexes for Insight artifacts. Reuse existing patterns from `spaarke-rag-references` (small structured docs + embedding + idempotency).

#### 4.1.2 New indexes

| Index | Contents | Document size | Embedding |
|---|---|---|---|
| `insight-matters` | Closure summaries, outcome facts, party/deal facts, key dates per matter | Small (1-5KB structured + summary) | Yes |
| `insight-decisions` | Decision points + rationale extracted from sessions/documents | Small | Yes |
| `insight-risks` | Observed risk patterns and signals | Small | Yes |
| `insight-sessions` | Notable findings from AI sessions ("save to insights" action) | Small | Yes |

All indexes share the **artifact envelope** schema from §2.2. Different `predicate` ranges, different vector content (the embedding embeds a representative text composition of the artifact), but a unified retrieval contract.

#### 4.1.3 Index schema (illustrative — `insight-matters`)

```jsonc
{
  "name": "insight-matters",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true, "filterable": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "type", "type": "Edm.String", "filterable": true },
    { "name": "subjectEntityType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "subjectEntityId", "type": "Edm.String", "filterable": true },
    { "name": "predicate", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "valueRaw", "type": "Edm.String", "searchable": true },
    { "name": "valueNumeric", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "confidence", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "asOf", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },

    // Scope (filter facets)
    { "name": "scope_practiceArea", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "scope_jurisdiction", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "scope_clientId", "type": "Edm.String", "filterable": true },
    { "name": "scope_matterId", "type": "Edm.String", "filterable": true },
    { "name": "scope_year", "type": "Edm.Int32", "filterable": true, "facetable": true },
    { "name": "scope_dealSizeBucket", "type": "Edm.String", "filterable": true, "facetable": true },

    // Provenance
    { "name": "producedBy_kind", "type": "Edm.String", "filterable": true },
    { "name": "producedBy_id", "type": "Edm.String", "filterable": true },
    { "name": "producedBy_version", "type": "Edm.String", "filterable": true },

    // Composed embedding content (what gets vectorized)
    { "name": "content", "type": "Edm.String", "searchable": true, "analyzer": "en.microsoft" },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "default-hnsw" },

    // Evidence references (stored as JSON for surface display)
    { "name": "evidenceJson", "type": "Edm.String", "retrievable": true, "searchable": false }
  ],
  "vectorSearch": {
    "profiles": [{ "name": "default-hnsw", "algorithm": "hnsw-default" }],
    "algorithms": [{ "name": "hnsw-default", "kind": "hnsw", "hnswParameters": { "m": 4, "efConstruction": 400 } }]
  },
  "semantic": {
    "configurations": [{
      "name": "insight-semantic",
      "prioritizedFields": {
        "contentFields": [{ "fieldName": "content" }, { "fieldName": "valueRaw" }]
      }
    }]
  }
}
```

**Per knowledge research**: use `vectorFilterMode=preFilter` for tenantId + scope filtering. This is mandatory for ACL trimming with high-cardinality groups.

#### 4.1.4 Embedding model — decided: text-embedding-3-large

We commit to **`text-embedding-3-large` (3072 dimensions)** for Insight artifact embeddings.

Rationale: legal text has rich semantic relationships; recall on "find comparable matters" is critical for the IQ Stack's headline claim. The cost differential vs. -small (~6.5x per token; 2x storage) is real but not material at the scale we operate, and the "no reason to knowingly underbuild" principle applies for a foundational choice that's expensive to change later.

**Update**: the example schema in §4.1.3 should read `"dimensions": 3072`.

Cost implications to monitor:
- At 10M artifacts/tenant: ~120GB of vector storage. Drives the S1→S2 tier consideration sooner. Plan for tier bump as a Phase 2 ops task.
- Embedding generation cost: ~$0.13 per 1M tokens (vs $0.02 for -small). At extraction throughput, this is a per-tenant operational cost line worth tracking in Cost Management.

#### 4.1.5 Tier sizing (current + future)

Current: `standard` (S1) — fine for r1 development and early pilot.

Future: Per knowledge research, **S2 (~$1,000/mo per tenant service)** is the realistic minimum for ~10M artifacts. Plan tier bump as Insight indexes grow. For physical-per-tenant model, this is a per-tenant cost that scales linearly with customers — material for pricing decisions.

### 4.2 Graph — Cosmos NoSQL with adjacency-list (committed)

#### 4.2.1 Terminology — we ARE building a real graph

Distinguish two things:
- **Graph data model**: vertices + typed edges + traversal. This is what we're building.
- **Graph database product** (Cosmos Gremlin, Neo4j, TigerGraph): purpose-built engines with graph-native query languages and deep-traversal optimizers.

**Cosmos NoSQL with adjacency-list IS a real graph implementation.** It's a graph data model on a document database. What it lacks is graph-native query syntax (Gremlin/Cypher) and in-engine optimization for 5+ hop algorithms — neither of which we need. What it gains us:

- **Vector co-location on the same node documents** — a `Person` vertex can carry an embedding, making "find people semantically similar" a single-store operation. Gremlin can't do this.
- **Microsoft's primary product investment direction** — better tooling, better SDKs, better doc cadence
- **Lower regional friction** — Gremlin has documented region-creation issues
- **One Cosmos API surface** for the team to learn, not two

The risk is real but bounded: if Phase 2+ ever needs deep multi-hop algorithmic queries (PageRank-style network analysis, community detection), adjacency-list gets painful. Mitigation: the `IInsightGraph` abstraction (see §4.2.2) keeps consumers typed in terms of named traversal patterns. Implementation swap is a contained refactor.

**Decision**: commit to **Cosmos NoSQL adjacency-list** for the Insight Graph in r1.

#### 4.2.2 The `IInsightGraph` abstraction

```csharp
public interface IInsightGraph
{
    // Vertex operations
    Task<InsightVertex> UpsertVertexAsync(InsightVertex vertex, CancellationToken ct);
    Task<InsightVertex?> GetVertexAsync(string vertexId, CancellationToken ct);
    Task DeleteVertexAsync(string vertexId, CancellationToken ct);

    // Edge operations
    Task UpsertEdgeAsync(InsightEdge edge, CancellationToken ct);
    Task DeleteEdgeAsync(string fromVertexId, string edgeType, string toVertexId, CancellationToken ct);

    // Traversals (named patterns — NOT exposing Gremlin-specific traversal syntax)
    Task<IReadOnlyList<InsightVertex>> FindConnectedEntitiesAsync(
        GraphTraversalSpec spec, CancellationToken ct);
    Task<IReadOnlyList<MatterRef>> FindMattersInvolvingPartyAsync(
        string partyId, GraphTraversalScope scope, CancellationToken ct);
    // ... other named patterns
}
```

Implementations:
- `CosmosNoSqlInsightGraph` (Phase 1 spike, primary candidate)
- `CosmosGremlinInsightGraph` (alternative if spike reveals NoSQL is wrong for our traversal patterns)

#### 4.2.3 Cosmos NoSQL adjacency-list pattern

Documents per vertex with embedded edge collections:

```jsonc
// Vertex document
{
  "id": "matter:M-1234",
  "tenantId": "tenant-acme",
  "vertexType": "Matter",
  "properties": {
    "displayName": "...",
    "practiceArea": "ip-licensing",
    "jurisdiction": "us-ca",
    "openedDate": "2024-03-12",
    "status": "closed"
  },
  "outgoingEdges": [
    { "edgeType": "INVOLVED_PARTY", "targetVertexId": "party:acme", "edgeProperties": { "role": "opposing" } },
    { "edgeType": "WORKED_ON", "targetVertexId": "person:smith.j", "edgeProperties": { "role": "lead-counsel" } },
    { "edgeType": "VENUE", "targetVertexId": "jurisdiction:us-ca", "edgeProperties": {} }
  ],
  "incomingEdges": [
    { "edgeType": "INVOLVED_PARTY", "sourceVertexId": "party:acme" },
    // ... mirrored for efficient reverse lookup
  ],
  "artifactRefs": [
    "insight-matter://M-1234#closure-summary",
    "insight-matter://M-1234#outcome-facts"
  ]
}
```

Partition key: `tenantId` (per knowledge research recommendation — supports per-tenant cost attribution and clean physical isolation).

#### 4.2.4 Graph schema — domain entities and edges

**Vertices**:
- `Matter` — every matter that's been opened
- `Party` — every counterparty, client, related party
- `Person` — counsel (ours + opposing), judges, partners, associates, clients
- `Firm` — opposing counsel firms, expert firms
- `Document` — significant documents (not every file; ones referenced by Observations)
- `Issue` / `Claim` — issue types or claims
- `Jurisdiction` — courts, regulatory bodies
- `Outcome` — outcome categories
- `Playbook` — JPS playbook (for traceability — "which playbook produced this Observation")

**Edges** (typed, with attributes):
- `(Matter)-[INVOLVED_PARTY {role}]->(Party)` — opposing | client | related
- `(Person)-[REPRESENTED]->(Party)` in a Matter (note: 3-way; modeled as `Person → MatterRole vertex` if the role context matters often, else as edge with edge properties)
- `(Person)-[ADJUDICATED]->(Matter)` — judges
- `(Person)-[WORKED_ON {role}]->(Matter)` — our lawyers (counsel | partner | associate)
- `(Person)-[BELONGS_TO]->(Firm)`
- `(Matter)-[INVOLVED_ISSUE]->(Issue)`
- `(Matter)-[VENUE]->(Jurisdiction)`
- `(Matter)-[RESULTED_IN]->(Outcome)`
- `(Matter)-[RELATED_TO]->(Matter)` — deal series, parent transactions, appeals
- `(Matter)-[REFERENCES]->(Document)`
- `(Person)-[SAME_AS {confidence}]->(Person)` — probabilistic identity (see §6.5)

**Identity resolution (refined per earlier conversation)**:
- No canonical registry requiring human curation
- Probabilistic graph: `SAME_AS` edges with confidence; multiple candidate nodes can coexist
- AI-driven matching at ingestion; periodic re-evaluation as more signal accumulates
- Scope by relevance: only invest resolution effort on entities meeting a threshold (appears in N+ matters or N+ outbound edges)
- Surface duplicates honestly: queries that aggregate across likely-duplicates emit `mayIncludeDuplicates: N` flag

### 4.3 Live Facts — direct Dataverse queries

#### 4.3.1 Decision

Cheap, deterministic computations stay as live Dataverse queries. No materialization for these. Examples:

- Matter duration: `closedDate - openedDate`
- Total spend: aggregate query on financial records
- Current status, fields, related counts
- Days since last activity

#### 4.3.2 Materialized Facts (when to materialize)

Expensive aggregates that are queried often get materialized in the `insight-matters` index alongside Observations. Examples:

- Per-matter outcome metrics computed at closure (cost, duration, win rate)
- Per-client/practice-area rollups (computed nightly or on-event)
- Embedding-friendly summarizations of Facts that join with Observations during retrieval

These are still Facts (`confidence: 1.0`), just stored in the index for retrieval speed.

#### 4.3.3 Implementation

`LiveFactResolverService` in BFF:

```csharp
public class LiveFactResolverService
{
    Task<InsightArtifact> GetMatterDurationAsync(string matterId, CancellationToken ct);
    Task<InsightArtifact> GetMatterTotalSpendAsync(string matterId, CancellationToken ct);
    Task<InsightArtifact> GetMatterStatusAsync(string matterId, CancellationToken ct);
    // ... typed methods per known Fact predicate
}
```

Cached briefly (5-minute TTL via existing Redis `RequestCache` / `IDistributedCache`).

---

## 5. Synthesis Layer — Insights Agent

### 5.1 Decision: custom BFF agent, not Foundry-hosted

Already decided in conversation; knowledge research confirms with caveats:

- Foundry's agent + memory primitives are now publicly documented (April 2026); we **borrow patterns** but don't host there
- Foundry-hosted is the right choice for *separate* surfaces (multi-day matter diligence, durable workflows, HITL); NOT for the Insights Agent
- Custom BFF agent reuses existing `IChatClient` + UseFunctionInvocation pipeline + tool framework (per AI inventory §4)

### 5.2 Two-tier memory pattern (borrowed from Foundry)

Per knowledge research, Foundry uses a two-tier model: `user_profile` (static) + `chat_summary` (contextual). We adopt this via Redis (since we're not in Foundry):

| Tier | Contents | TTL | Backed by |
|---|---|---|---|
| **User profile** | User's role, practice areas they work in, preferred analytical depth, often-asked questions | 30 days, sliding | Redis (`insights:profile:{tenantId}:{userId}`) |
| **Chat summary** | Last N turns of the active Insights session (when chat-driven), accumulated context | Session lifetime, sliding | Redis (`insights:session:{tenantId}:{sessionId}`) |

These are inputs to the Agent's context — they don't bypass retrieval. Memory shapes the question and the synthesis style; evidence still comes from indexes/graph/facts.

### 5.3 Tools the Insights Agent exposes (initial set)

| Tool | Purpose | Returns |
|---|---|---|
| `FindComparableMatters` | Vector + filter retrieval over `insight-matters` | List of `MatterArtifact` with similarity scores |
| `GetMatterFacts` | Live-fact lookup for one or more matters | Map of `matterId → {factPredicate → value}` |
| `RetrieveByGraph` | Named graph traversals (e.g., "matters involving party X", "judges who ruled on issue Y") | List of vertex refs + edge context |
| `GetObservations` | Retrieve Observations matching a predicate + scope filter | List of Observation artifacts |
| `AssessEvidenceSufficiency` | Check if comparable set is large/diverse enough for an Inference | `{sufficient: bool, count: N, threshold: M, gap: '...'}` |
| `ComposeInference` | Final synthesis tool — wraps the conclusion in proper artifact envelope with evidence | Inference artifact |

Each tool follows the existing `IAiToolHandler` pattern (per AI inventory §4.9).

### 5.4 Evidence-sufficiency rules — non-negotiable

Every Inference question carries (in the question catalog) an evidence-sufficiency rule:

```jsonc
{
  "questionId": "predict-matter-cost",
  "tier": "inference",
  "requiredEvidence": {
    "comparableMatters": { "min": 12, "preferred": 25 },
    "scopeMatchOn": ["practiceArea"],  // must match these
    "scopeMatchPreferOn": ["counterparty", "jurisdiction"],  // bonus
    "freshness": "comparables from last 3 years preferred"
  },
  "insufficientResponse": {
    "type": "insufficient_evidence",
    "message": "Only {actual} comparable matters found; need ~{required} for a reliable estimate.",
    "actionableGap": "Need more {practiceArea} matters with comparable {dealSize}."
  }
}
```

The Agent's `AssessEvidenceSufficiency` tool reads this rule and either proceeds with synthesis or returns the structured insufficient-evidence response.

**Why this matters**: this is the architectural enforcement of the honesty contract. Without explicit sufficiency rules per question, the Agent will default to "make something up with hedging language" — the exact behavior the marketing claims to differ from.

### 5.5 Safety controls

- **Streaming + verification** (already a Spaarke pattern from the unification design): Agent streams response; safety verifier checks claims against retrieved evidence within 200ms of completion. Unverified claims flagged.
- **Strip retrieved content on cross-matter pivot** (already a Spaarke rule): when user pivots Matter context mid-session, prior retrieved content is cleared from conversation history. Conclusions remain.
- **Tool-call gating** (extends existing `PendingPlanManager`): for write-back operations (e.g., "save this Inference back to the insight-sessions index"), require explicit user approval.

---

## 6. Data Flow

### 6.1 Architecture per knowledge research + auth constraints

The canonical Microsoft pattern for Dataverse → Azure AI Search sync is:

- **Service Bus topic** as real-time backbone (Dataverse → ... → Service Bus → Functions)
- **Timer-triggered Function** over Dataverse change-tracking for reconciliation
- Webhooks are TRIGGERS ONLY — handlers must re-fetch the Dataverse record (256KB payload truncation + out-of-order delivery)
- Microsoft 365 Copilot connectors target Microsoft Graph, not our AI Search — orthogonal

**Critical auth constraint** (confirmed via Microsoft docs review): **Dataverse's native Azure integration to Service Bus uses SAS authorization only.** It does NOT support Entra ID / Managed Identity for the webhook → Service Bus hop. This is documented at:
- [Configure Azure integration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/configure-azure-integration)
- [Configure SAS integration walkthrough](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/walkthrough-configure-azure-sas-integration)

Spaarke's security standard requires no SAS keys on the bus. Therefore we use the recommended intermediate-component pattern: **Dataverse webhook → HTTP-triggered Function (Entra OAuth auth) → Service Bus topic (Managed Identity / Azure RBAC).** The intake Function is the auth-trust boundary.

We do NOT use Dataverse plugins or Power Automate for this integration. Plugins would have the same SAS limitation; Power Automate adds operational complexity without solving the auth problem.

### 6.2 Real-time sync flow (auth-correct)

```
┌──────────────────┐
│  Dataverse       │
│  (matter, party, │
│   document, ...) │
└────────┬─────────┘
         │ change event → webhook (HTTPS POST)
         │ Auth on this hop:
         │   • TRANSITIONAL: clientState (shared secret in header)
         │                   — copy from BFF webhook handlers,
         │                     do NOT recreate
         │   • TARGET: HMAC-SHA256 signature validation
         │             — lands with Phase C task 044
         ▼
┌─────────────────────────────────────────────────────────────┐
│ Function: DataverseWebhookIntake (HTTPTrigger)              │
│  AUTH TRUST BOUNDARY — only entry point that accepts        │
│  external traffic. Everything past here is internal.        │
│                                                             │
│  1. Validate clientState / HMAC signature on the request    │
│     (reuse BFF webhook validation code; see Phase C #044)   │
│  2. Validate webhook payload shape + originating entity     │
│  3. Publish to Service Bus topic using UAMI                 │
│     (DefaultAzureCredential; Azure RBAC — no SAS keys)      │
│                                                             │
│  Note: For ANY non-webhook endpoint on this Function App    │
│  (e.g., admin endpoints), use Microsoft.Identity.Web JWT    │
│  validation — same pattern as Sprk.Bff.Api (see §6.2.3).    │
└────────┬────────────────────────────────────────────────────┘
         │ Service Bus SDK call with DefaultAzureCredential (UAMI)
         ▼
┌──────────────────────────────┐
│ Service Bus Topic            │
│ "spaarke-dataverse-          │
│  changes-{tenant}"           │
│ Auth: Azure RBAC via UAMI    │
│ Subscribers (UAMI):          │
│  - insights-sync             │
│  - records-sync (existing)   │
│  - other consumers           │
└────────┬─────────────────────┘
         │ ServiceBusTrigger (UAMI)
         ▼
┌─────────────────────────────────────┐
│ Function: InsightsSyncFunction      │
│  1. Re-fetch full Dataverse record  │
│     (DefaultAzureCredential; per    │
│      Phase C task 042 — no          │
│      ClientSecretCredential)        │
│  2. Project to InsightArtifact      │
│     envelope                        │
│  3. Push to AI Search index (UAMI;  │
│     vectorFilterMode=preFilter)     │
│  4. Update graph vertex/edges       │
│     (UAMI to Cosmos)                │
│  5. Emit milestone events           │
│     (status → closed → SBus)        │
└────────┬────────────────────────────┘
         │
         ▼
   AI Search + Graph + (if milestone) Service Bus message → Extraction Function
```

#### 6.2.1 Auth summary — secrets in the pipeline

| Hop | Auth mechanism (target) | Auth mechanism (transitional) | Secret type |
|---|---|---|---|
| Dataverse → Intake Function | **HMAC-SHA256** signature (Phase C #044) | **clientState** shared secret in header | Shared secret (transitional only); none at target |
| Intake Function → Service Bus | Azure RBAC via UAMI | (same) | None |
| Service Bus → consumer Functions | Azure RBAC via UAMI | (same) | None |
| Consumer Functions → Dataverse (re-fetch) | Azure RBAC via UAMI (Phase C #042) | DefaultAzureCredential | None at target |
| Consumer Functions → AI Search | Azure RBAC via UAMI | (same) | None |
| Consumer Functions → Cosmos | Azure RBAC via UAMI | (same) | None |

**Net at target state**: zero SAS keys and zero long-lived secrets anywhere in the pipeline.

**Net at transitional state**: a single shared `clientState` secret on the Dataverse → Intake hop, stored in Key Vault, rotated programmatically. This is the only secret in the pipeline and it disappears when Phase C task 044 lands.

#### 6.2.2 Auth answers — resolved (no longer open)

| # | Question | Resolution |
|---|---|---|
| **AUTH-1** | Does `@spaarke/auth` validate inbound Bearer tokens at the Function/API edge? | **No.** `@spaarke/auth` is **client-side TypeScript only** (`src/client/shared/Spaarke.Auth/`, `@spaarke/auth@2.0.0`). It uses MSAL.js to acquire tokens for outbound calls to the BFF — it does NOT validate them. Inbound JWT validation lives in `Sprk.Bff.Api` via `Microsoft.Identity.Web`'s `AddMicrosoftIdentityWebApi`. |
| **AUTH-2** | Is `@spaarke/auth` a NuGet package or a service? | **Neither — it's a workspace-local npm package.** A .NET Function App **cannot** reference it. The Function uses the same .NET stack as `Sprk.Bff.Api`: install `Microsoft.Identity.Web` (NuGet) and call `AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))`. |
| **AUTH-3** | Per-tenant validation (correct tenant + audience)? | **Yes — via `Microsoft.Identity.Web` config, not `@spaarke/auth`.** `AzureAd:TenantId` is an explicit tenant GUID (**NOT** `common` or `organizations` — Phase C task 047 fixes the template that still says `common`). `AzureAd:ClientId` is the Function's own app registration ID; only tokens minted FOR this audience are accepted. `Microsoft.Identity.Web` validates `iss`, `aud`, `tid`, signature, expiry by default. Per-tenant deployment threat model (D-AUTH-5) is the Spaarke convention. |
| **AUTH-4** | Established pattern for "Dataverse calling our endpoint"? | **Yes, mid-upgrade.** For webhook endpoints (Dataverse webhook → external HTTP), current pattern is **clientState** (shared secret in header), being replaced by **HMAC-SHA256** signature validation in Phase C task 044. **Copy from the BFF's webhook handler — do NOT recreate validation logic.** For OBO/user-context calls: validate via `Microsoft.Identity.Web` (same as AUTH-1). For app-only/S2S: managed identity (`DefaultAzureCredential`) per Phase C tasks 041–042 — **no `ClientSecretCredential` in new Functions**. |

#### 6.2.3 Canonical inbound JWT validation pattern (for non-webhook endpoints)

The Function App will have some non-webhook endpoints (admin, manual trigger, status). These use Microsoft.Identity.Web JWT validation — same pattern as `Sprk.Bff.Api`. Reference implementation: [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) + [`Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs).

```csharp
// Program.cs — Insights Engine intake Function (mirrors Sprk.Bff.Api)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Webhook endpoints — DO NOT require JWT; they use clientState/HMAC validation
app.MapPost("/intake/dataverse-webhook", DataverseWebhookHandler);  // no .RequireAuthorization()

// Admin/management endpoints — DO require JWT
app.MapPost("/intake/admin/reconcile", ReconcileHandler).RequireAuthorization();
app.MapGet("/intake/admin/health-detailed", HealthHandler).RequireAuthorization();
```

```jsonc
// appsettings.json — NOT appsettings.template.json (template uses 'common' placeholder
// per Phase C task 047 — must be fixed before deployment)
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<the customer tenant's GUID — explicit, NOT 'common'>",
    "ClientId": "<intake Function's own app registration ID>"
  }
}
```

**Critical: do NOT call out to a separate "auth service" for JWT validation.** JWT validation is a local crypto check (signature, exp, iss, aud, tid); `Microsoft.Identity.Web` does it correctly and caches AAD metadata. An "auth service" hop adds latency with no security benefit.

#### 6.2.4 Outbound auth — managed identity only

Per Phase C tasks 041 (Graph outbound) and 042 (Dataverse outbound), **all new Functions use `DefaultAzureCredential` from day one. No `ClientSecretCredential`.**

```csharp
// Outbound to Dataverse (record re-fetch)
var credential = new DefaultAzureCredential();
var dataverseClient = new ServiceClient(new Uri(dataverseUrl), tokenProviderFunction, ...);

// Outbound to Service Bus
var sbusClient = new ServiceBusClient(namespace, new DefaultAzureCredential());

// Outbound to AI Search
var searchClient = new SearchClient(endpoint, indexName, new DefaultAzureCredential());

// Outbound to Cosmos
var cosmosClient = new CosmosClient(accountEndpoint, new DefaultAzureCredential());
```

In Bicep, the Function's UAMI gets role assignments to each target resource. Locally, `DefaultAzureCredential` falls back to Azure CLI / Visual Studio credentials.

#### 6.2.5 Authoritative references for auth patterns

| Concern | Source |
|---|---|
| Inbound JWT validation reference implementation | [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs), [`AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs) |
| Per-tenant config + non-`common` TenantId fix | Phase C task 047 (in flight) — fixing `appsettings.template.json` |
| Webhook signature pattern (current `clientState`, future HMAC) | Phase C task 044 (in flight) |
| OBO server-side flow | [`.claude/patterns/auth/obo-flow.md`](../../.claude/patterns/auth/obo-flow.md) |
| Managed identity for outbound (Graph + Dataverse) | Phase C tasks 041 (Graph), 042 (Dataverse) |
| Active auth design canon (until ADR-027 lands) | [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) |

**Coordination requirement**: the Insights Engine Phase 1 work must coordinate with the `@spaarke/auth` Phase C effort. Specifically:
- Adopt task 047's per-tenant TenantId pattern from day one (no `common`)
- Use task 044's HMAC validation when it lands; ship initially with `clientState` copied from BFF webhook handlers
- Inherit the managed-identity-only outbound discipline from tasks 041–042

### 6.3 Reconciliation flow

Timer trigger nightly (or more frequent):

```
┌──────────────────────────────────────┐
│ Function: InsightsReconciliation     │
│  (TimerTrigger: "0 30 2 * * *")      │
│                                      │
│  1. Query Dataverse change-tracking  │
│     for records modified since last  │
│     successful sync timestamp        │
│  2. For each: re-fetch + re-project  │
│     + upsert to Search + graph       │
│  3. Compare Search index counts vs.  │
│     Dataverse counts; alert on drift │
│  4. Update last-sync timestamp       │
└──────────────────────────────────────┘
```

Catches missed events, schema drift, late-binding edges. Idempotent — re-running on a healthy index is a no-op.

### 6.4 Closure-extraction flow

Triggered by Service Bus message when a matter reaches a milestone (closure, settlement, judgment):

```
┌──────────────────────────────────────┐
│ Function: ClosureExtractionTrigger   │
│ ServiceBusTrigger:                   │
│   "spaarke-matter-milestones-{tenant}"│
│                                      │
│  1. Load matter context              │
│  2. Invoke closure-extraction        │
│     JPS playbook (via BFF API or     │
│     direct PlaybookExecutionEngine   │
│     call — see §6.4.1)               │
│  3. Playbook output (Observations)   │
│     flows through                    │
│     DeliverToIndexNodeExecutor →     │
│     AI Search                        │
│  4. Graph vertices/edges updated     │
│     based on extracted facts         │
└──────────────────────────────────────┘
```

#### 6.4.1 BFF call vs. direct playbook engine call

Two options for how the Function invokes the closure-extraction playbook:

| Option | Pros | Cons |
|---|---|---|
| **A. Function calls BFF API endpoint** | Single playbook invocation path; auth + observability mirror BFF | Couples to BFF availability; HTTP call adds latency |
| **B. Function directly invokes `PlaybookExecutionEngine`** | Independent of BFF; faster | Duplicates DI wiring; risk of drift between BFF and Function playbook execution |

**Recommendation: Option A (BFF API call)** — keeps a single playbook execution path. The Function is a thin trigger; the actual work runs in the BFF. This is the right separation for r1; revisit if BFF latency becomes a bottleneck.

### 6.5 Identity resolution flow (probabilistic, no human curation)

When sync or extraction encounters a person/org name:

```
1. Normalize name (trim, case-fold, common-abbrev expansion)
2. Search graph for existing vertices of same type with similar name
   (fuzzy match + bar number + email domain + prior co-occurrences)
3. Scoring:
   - High-confidence match (≥ 0.85): create SAME_AS edge automatically
   - Medium (0.5–0.85): create new vertex with proposedSameAs ref;
     periodic batch job re-scores as more signal accumulates
   - Low (< 0.5): create new vertex with no link
4. Scope by relevance:
   - Only invest resolution effort on entities meeting threshold
     (appears in N+ matters or N+ outbound edges)
   - Long-tail mentions stay as standalone vertices
5. Queries follow SAME_AS edges (configurable confidence threshold,
   default 0.7+)
6. Aggregate Insights flag possible duplicates:
   - "Acme Corp has appeared in 8 matters (may include 2 likely
     duplicate references)"
```

### 6.6 Backfill

For initial provisioning (new tenant) or recovery (failed reconciliation):

- **Bulk extraction Function** with manual trigger via Admin endpoint
- Iterates Dataverse records in batches (avoid throttling)
- Skips records already at current playbook version (`producedBy.version` check on existing Observations)
- Emits progress events for monitoring

### 6.7 Schema evolution

When `insight-matters` schema needs a field added or `closure-extraction` playbook ships v4:

1. **Versioned indexes**: new index name (`insight-matters-v2`) created via Bicep
2. **Dual-write** during migration: sync writes to both v1 and v2
3. **Re-extract**: scheduled Function iterates v1, re-extracts at new playbook version, writes to v2
4. **Cutover**: search reads switch to v2 (config flag)
5. **Decommission v1** after grace period

For Observations specifically: the `producedBy.version` field enables targeted re-extraction. Don't blindly re-extract; query for `producedBy.version < currentPlaybookVersion`.

---

## 7. Privilege Model

### 7.1 Two principles

#### Principle 1: Graph + index contain everything; queries trim at execution

The substrate stores the union of all accessible data for the tenant. Every query layer applies the user's `accessibleMatterSet` filter at execution time. This is slower than per-user pre-materialized views but is correct under access changes (granting access doesn't require rebuilding views).

#### Principle 2: Counts are less sensitive than IDs

It's acceptable to say "Acme Corp has appeared in 8 of your firm's prior matters" even when the user can only access 3 of them — provided "click to see them" only returns the 3.

This distinguishes:
- **Aggregate Insights** (counts, ranges, distributions): trim post-aggregation
- **Specific Insights** (linked matter IDs, document refs): trim pre-aggregation

The artifact envelope's `value.displayHint` field signals which category an artifact is in.

### 7.2 Per-tenant isolation (physical)

Each tenant has its own AI Search service or per-tenant indexes within a shared service, own Cosmos account, own Function App. The privilege boundary is **physical**: cross-tenant data leakage is structurally impossible because there's no path between them.

| Resource | Per-tenant deployment |
|---|---|
| AI Search service | Per-tenant service (preferred for legal clients) OR per-tenant indexes in shared service (acceptable with strict `tenantId` preFilter) |
| Cosmos account | Per-tenant account (clean partition + cost attribution) |
| Function App | Per-tenant app with per-tenant UAMI |
| Service Bus topic | Per-tenant topic |
| Application Insights | Shared with workspace-based separation; correlation IDs include `tenantId` |

### 7.3 In-tenant access trimming

Within a tenant, users see different matter subsets. The `accessibleMatterSet` is sourced from the existing access control system (Dataverse security model or unified access control project) and cached per session.

#### 7.3.1 In AI Search queries

Every query includes a filter:

```
filter: tenantId eq '{tenantId}' and 
        search.in(scope_matterId, '{accessibleMatterCsv}', ',')
```

With `vectorFilterMode=preFilter` (per knowledge research) for vector queries.

#### 7.3.2 In graph traversals

Every traversal hops include matter-set filter at vertex-touch points:

```
TraverseGraph spec includes:
  matterFilter: accessibleMatterSet
  unaccessibleHandling: 'redactReference' | 'omit' | 'countOnly'
```

`'countOnly'` lets aggregate queries return counts without leaking IDs.

#### 7.3.3 In Inference synthesis

The Agent's tools all receive `accessibleMatterSet` as part of the tool context. Tools fail-safe: if a tool cannot trim correctly, it returns an error rather than potentially over-disclosing.

### 7.4 Cross-matter pivot rule (extension of existing rule)

When a user pivots Matter context mid-session (Matter A → Matter B), retrieved evidence about Matter A is stripped from conversation history. This is already a Spaarke rule for documents ([design.md:925](../spaarke-ai-platform-unification-r2/design.md#L925)); we extend it to graph evidence:

- Specific entity refs from prior matter: cleared
- Aggregate conclusions: retained (they don't contain matter-specific privileged details)
- User notified: *"Switching matter context — prior matter details cleared from context."*

### 7.5 Auditability

Every Insight request and response is logged to the existing audit log (per [unification design.md §5.5](../spaarke-ai-platform-unification-r2/design.md)):

- User ID, tenant ID
- Question asked (sanitized)
- Artifacts returned (IDs only, not values)
- Evidence cited (refs)
- Confidence + sufficiency result

Append-only Cosmos with immutable policy. This is a compliance artifact, not a debugging tool.

---

## 8. Surface Integration

### 8.1 Surface-agnostic delivery

The Engine produces `InsightResponse` payloads. Each surface knows how to render the response in its container. The Engine doesn't know about specific surfaces; surfaces don't know about the Engine's internals.

### 8.2 `InsightResponse` contract

```jsonc
{
  "requestId": "uuid",
  "asOf": "2026-05-19T08:30:00Z",
  "answers": [
    {
      "artifact": <full Insight artifact envelope per §2.2>,
      "displayHints": {
        "title": "Predicted cost",
        "subtitle": "Based on 12 comparable matters",
        "primary": "$280,000",
        "secondary": "Confidence: 74%",
        "drilldownAvailable": true,
        "evidenceCount": 12
      }
    }
  ],
  "freshness": "live | cached-{ttlSeconds}",
  "warnings": [],  // e.g., "mayIncludeDuplicates: 2"
  "diagnostics": {
    "tier": "inference",
    "questionId": "predict-matter-cost",
    "evidenceSufficient": true,
    "trimmedFromAccess": 0  // count of artifacts trimmed by accessibleMatterSet
  }
}
```

### 8.3 Surface-specific rendering

| Surface | Renders | Specific notes |
|---|---|---|
| **Context pane** | Stratified by tier (Record / History / Matter Memory / Org Intelligence) with explicit tier labels. Each Insight shows value + provenance + drill-through | Per earlier conversation: tier labels are mandatory; "intelligence panel" framing is dropped |
| **Form widget** | Single Insight as a card on the record form (e.g., "Predicted cost" card on the matter form) | Compact mode; click to expand provenance |
| **Ribbon flyout** | One-shot Insight in a popover | Aggregate-only by default (counts, ranges); detailed Insights require pane navigation |
| **Outlook add-in** | Insights about the matter the email pertains to | Subset of pane content; "View more in Spaarke" CTA |
| **Notification** | Push of newly-derived Insights (closure summary ready, risk signal detected) | Lightweight; deep-link to pane |
| **Public API** | `GET /api/insights/...` for partner consumers | Same envelope; same provenance rules |

### 8.4 The Context pane — stratified surface

Per the earlier strategic conversation, the Context pane shows four tiers explicitly labeled, with different evidence rules:

| Tier | Source | Honest because… |
|---|---|---|
| 1. Record | Direct Dataverse (Live Facts) | Data, nothing claimed |
| 2. History | Audit log + work history | Receipts — every item has timestamp + actor |
| 3. Matter Memory | MatterMemoryService + Observations scoped to this matter | Each fact has provenance to the source action |
| 4. Organizational Intelligence | Insights Engine (Inferences over Observations + materialized Facts) | Cited to specific past matters; similarity criteria visible; honest empty states |

The pane renders responses from the Engine grouped by tier. The pane is one consumer; the same Engine responses flow to other surfaces unchanged.

### 8.5 SSE event integration (extends unification design)

The current unification design defines `context_update` SSE events. We generalize:

- `insight_response` — full `InsightResponse` payload (broader than `context_update`; consumable by any surface)
- `insight_highlight` — cross-pane jump (e.g., from a comparable-matter reference → open that matter)
- `insight_invalidate` — surface should refresh (e.g., a matter status changed, dependent Insights are stale)

These coexist with existing `context_update` events during migration; eventually `context_update` becomes a deprecated alias for `insight_response`.

---

## 9. Packagability — Bicep + Deployment

### 9.1 Per-tenant deployment unit

Everything in the Engine is deployable per tenant via Bicep. The deployment unit includes:

- AI Search indexes (provisioned + initial schema)
- Cosmos account (Insight Graph)
- Function App (with all functions: sync, reconciliation, extraction, re-indexing)
- Service Bus topic + subscriptions
- Per-tenant UAMI
- Key Vault references for secrets
- App Insights connection
- Bicep parameters per tenant (tenant ID, region, search tier, Cosmos throughput, etc.)

### 9.2 Bicep module layout (proposed)

```
infra/insights/
├── main.bicep                         # entry — composes modules
├── parameters/
│   ├── tenant-acme.json
│   ├── tenant-beta.json
│   └── ...
├── modules/
│   ├── search-indexes.bicep           # AI Search indexes + schemas
│   ├── cosmos-graph.bicep             # Cosmos account + database + container
│   ├── functions.bicep                # Function App (Flex Consumption)
│   ├── servicebus-topic.bicep         # Sync topic + subscriptions
│   ├── managed-identity.bicep         # UAMI + role assignments
│   ├── keyvault-secrets.bicep         # Secret references
│   └── monitoring.bicep               # App Insights connection
└── schemas/
    ├── insight-matters.index.json     # AI Search index schema
    ├── insight-decisions.index.json
    ├── insight-risks.index.json
    └── insight-sessions.index.json
```

### 9.3 Index schemas as code

Each AI Search index is defined as JSON in the repo (`schemas/*.json`). Bicep deploys via a deployment script that calls the AI Search Management API. Versioning of schemas (`insight-matters.v2.json`) supports the schema evolution flow from §6.7.

### 9.4 Deployment script flow

```
1. Validate Bicep parameters (tenant ID, region availability)
2. Provision resources (AI Search service if not shared, Cosmos, Functions, SBus)
3. Deploy AI Search indexes from schema JSONs
4. Set up Cosmos containers + indexing policies
5. Configure Function App settings + secrets via Key Vault refs
6. Set up Service Bus topic + subscriptions
7. Run initial backfill (admin endpoint trigger)
8. Verify: smoke-test endpoints (`/healthz/insights`), assert indexes exist with expected schema
```

### 9.5 Multi-tenant control plane (Phase 2)

For r1 with a small number of tenants, **tenant-list-as-configuration** is sufficient (each tenant has a Bicep parameters file; CI deploys when files change).

Per knowledge research, evolve to a **tenant-list-as-data control plane** around ~10 tenants. Design TBD; not Phase 1.

### 9.6 Per-environment vs. per-tenant

Within a tenant, you still have dev / staging / prod environments. Bicep parameters file naming captures both axes: `tenant-acme.dev.json`, `tenant-acme.prod.json`. The Bicep is identical; only parameters differ.

---

## 10. AI Inventory Reconciliation

### 10.1 What the Engine reuses (from `ai-inventory.md`)

| Insights Engine component | Existing service | Notes |
|---|---|---|
| Dataverse sync pattern | `DataverseIndexSyncService` (in `Services/RecordMatching/`) | Reuse `EntityConfig` model; migrate to event-driven Function for new indexes |
| Indexing pipeline | `RagIndexingPipeline` | Reuse for chunked Observations |
| Index admin (idempotent re-indexing) | `ReferenceIndexingService` pattern | Mirror for `insight-*` indexes |
| Retrieval (hybrid + semantic) | `RagService` + `RagQueryBuilder` + `EmbeddingCache` | Wrap with `InsightRetrievalService` for typed access |
| Chunking | `SemanticDocumentChunker` + `TextChunkingService` | Reuse as-is |
| Record search | `RecordSearchService` | Template for new `insight-matters` retrieval |
| OpenAI client | `IOpenAiClient` + `OpenAiClient` | Reuse |
| Playbook execution | `PlaybookExecutionEngine` + `INodeExecutor` registry | Closure-extraction IS a playbook ending in `DeliverToIndexNodeExecutor` |
| Tool framework | `IAiToolHandler` + `IToolHandlerRegistry` + `ToolExecutionContext` | Insights Agent tools follow this pattern |
| Chat / agent factory | `SprkChatAgentFactory` + `IChatClient` + UseFunctionInvocation | Insights Agent reuses the pipeline |

### 10.2 What's NEW

| Component | Why new |
|---|---|
| `InsightsResolverService` | The orchestration layer doesn't exist |
| `Insights Agent` (specific instance) | Existing chat agent factory creates conversational agents; the Insights Agent has a different tool set and synthesis profile |
| `IInsightGraph` + `CosmosNoSqlInsightGraph` | No existing graph in the stack |
| `LiveFactResolverService` | Existing services compose dataverse queries case-by-case; we centralize the Fact production |
| Insight indexes (`insight-matters`, etc.) | New schemas |
| Sync Function App | New deployment artifact (no existing Function Apps; new with the narrowed ADR-001) |
| Question catalog (Foundry agent skills or BFF config) | No existing structured catalog of context questions |
| `IInsightArtifactStore` (abstraction over AI Search indexes for Insight artifacts) | Wraps `SearchClient` with Insight-specific operations |

### 10.3 What gets migrated (post-MVP)

| Today | Future state | Why migrate |
|---|---|---|
| `PlaybookIndexingBackgroundService` (BFF hosted service, ADR-001 workaround) | Move to a Function | Pure out-of-band integration; ADR-001 now permits |
| `DataverseIndexSyncService` (BFF HTTP-coupled) | Migrate triggering to Service Bus + Function | Decouple from BFF lifecycle; align with new sync infrastructure |
| `spaarke-records-index` (no tenantId field) | Add tenantId + backfill | Multi-tenant correctness; pattern established by Insight indexes |

These are NOT blockers for r1; they're cleanup that happens once the Engine is stable.

### 10.4 What gets retired

None known. The AI inventory found no obvious dead code in the AI subsystem.

---

## 11. Phased Plan

### 11.1 Spike phase — RESOLVED via discussion (no formal spikes required)

Three potential spikes were considered and resolved through architectural discussion (see [decisions.md](decisions.md) once authored):

| Original spike | Resolution |
|---|---|
| **S1: Cosmos NoSQL vs Gremlin** | **Resolved by reasoning**: commit to Cosmos NoSQL adjacency-list. Our traversal patterns are 2-3 hops with filters (not deep graph algorithms); vector co-location is a meaningful benefit; Gremlin's strategic signals are too uncertain. `IInsightGraph` abstraction preserves swap path. See §4.2. |
| **S2: Embedding model** | **Resolved by principle**: commit to `text-embedding-3-large` (3072 dim). "No reason to knowingly underbuild" for a foundational choice that's expensive to migrate later. Cost differential is real but not material at our scale. See §4.1.4. |
| **S3: Dataverse → Service Bus auth** | **Resolved by Microsoft documentation review**: Dataverse native Azure integration uses SAS only — no Entra/MI for the webhook → Service Bus hop. Adopt the intermediate-Function pattern: Dataverse → HTTP Function (Entra) → Service Bus (UAMI). Result: zero SAS keys in pipeline. See §6.2. |

Net: skip directly to Phase 1. Decisions documented in §4 and §6.

### 11.2 Phase 1 — Foundation (8-12 weeks)

**Goal**: Sync wiring + one end-to-end Inference question working.

Deliverables:
1. Function App (Flex Consumption) provisioned via Bicep in dev environment, with own app registration + UAMI
2. **Intake Function** (HTTPTrigger) — the auth trust boundary
   - Webhook endpoints: clientState validation initially; HMAC-SHA256 when Phase C task 044 lands. **Copy the validation code from BFF webhook handlers — do not recreate.**
   - Non-webhook endpoints: `Microsoft.Identity.Web` JWT validation, mirroring `Sprk.Bff.Api/Program.cs`
   - `AzureAd:TenantId` is the explicit tenant GUID (never `common`) — coordinate with Phase C task 047
3. Dataverse webhook registration (via plugin registration tool, NOT Power Automate, NOT custom plugin assemblies) pointing at the intake Function with `clientState` configured
4. Service Bus topic + subscriptions (UAMI role assignments via Bicep)
5. `InsightsSyncFunction` (ServiceBusTrigger, UAMI) + `InsightsReconciliation` (TimerTrigger) — covers `sprk_matter` initially
6. All outbound calls use `DefaultAzureCredential` (no `ClientSecretCredential`) — aligns with Phase C tasks 041–042
7. `insight-matters` index provisioned with schema (with tenantId; 3072-dim vectors using text-embedding-3-large)
8. `IInsightGraph` interface + `CosmosNoSqlInsightGraph` implementation
9. Graph populated from sync (Matter + Party vertices + INVOLVED_PARTY edges)
10. `LiveFactResolverService` with first 3-5 Facts (matterDuration, totalSpend, status, daysSinceLastActivity)
11. `InsightsResolverService` skeleton in `Sprk.Bff.Api/Services/Insights/`
12. `Insights Agent` with 3 tools (`FindComparableMatters`, `GetMatterFacts`, `AssessEvidenceSufficiency`)
13. One question: `predict-matter-cost` with evidence-sufficiency rule
14. `POST /api/insights/ask` endpoint
15. Context pane rendering (one Insight card)

**Phase 1 coordination requirements** (must align before / during Function work):
- Phase C task 044 (HMAC webhook validation) — copy when landed; ship with `clientState` initially
- Phase C task 047 (non-`common` TenantId in templates) — Insights Engine adopts the fix from day one
- Phase C tasks 041 + 042 (managed identity outbound) — inherit the discipline; no `ClientSecretCredential`

Acceptance criteria:
- Asking "predict cost for matter M-X" returns either an Inference with citations OR an honest insufficient-evidence response
- All trimming + provenance + tenantId enforcement verified
- Bicep deploys to a clean tenant in < 30 minutes
- **Zero SAS keys** in any part of the pipeline (the transitional `clientState` is the only shared secret and is gone after Phase C #044)
- Function App auth setup matches `Sprk.Bff.Api/Program.cs` pattern exactly for non-webhook endpoints
- All outbound credential acquisition uses `DefaultAzureCredential`

### 11.3 Phase 2 — Expansion (8-12 weeks after Phase 1)

- Additional Insight indexes (`insight-decisions`, `insight-risks`, `insight-sessions`)
- Closure-extraction JPS playbook (the compounding loop comes online)
- Additional graph entities (Person, Firm, Judge, Issue, Jurisdiction)
- Additional question templates (10+)
- Additional surfaces (form widget, ribbon flyout)
- Multi-tenant: parameter files for 2-3 customer tenants

### 11.4 Phase 3 — Production polish

- Cost / utilization monitoring (the inventory recommendations from `azure-inventory.md`)
- Schema evolution tooling (v2 index migration UI/scripts)
- Bulk re-extraction workflow
- Tenant onboarding automation (the control-plane evolution from §9.5)
- Public API + partner consumers
- Post-MVP migrations from §10.3

---

## 12. Open Questions / Deferred Decisions

### 12.1 Open (need answers before/during Phase 1)

| # | Question | Owner | When |
|---|---|---|---|
| O1 | Will the closure-extraction playbook be JPS or a new specialized format? | Architecture | Before Phase 1 design freeze |
| O2 | Does `accessibleMatterSet` come from the unified access control project, or do we maintain our own source? | Architecture | Before Phase 1 sync wiring |
| O3 | What's the minimum `comparableMatters.min` per question — set globally or per question? | Product | Before first Inference question ships |
| O4 | Outlook/Teams surface integration — Phase 2 or Phase 1? | Product | During Phase 1 |

**Auth questions (AUTH-1 to AUTH-4) — RESOLVED** in §6.2.2. The remaining auth-related work is coordination with Phase C tasks 044, 047, 041, 042 (see §6.2.5).

### 12.2 Deferred (can answer post-MVP)

| # | Question | When |
|---|---|---|
| D1 | Add graph layer for true multi-hop traversal queries (vs. adjacency-list)? | Reassess after Phase 1 spike |
| D2 | When to bump AI Search to S2 tier? | When indexed artifacts approach 5M per tenant |
| D3 | Move `PlaybookIndexingBackgroundService` from BackgroundService to Function | Phase 3 cleanup |
| D4 | Migrate `spaarke-records-index` to add tenantId | Phase 3 cleanup |
| D5 | Tenant control plane (config → data) | When tenant count approaches 10 |
| D6 | Add Foundry-hosted agent for separate multi-day diligence surfaces | When those surfaces are designed |
| D7 | Cross-tenant insights (e.g., industry benchmarks) — only if explicitly opted-in by tenants | Possibly Phase 3+; major privacy/legal work |

### 12.3 Hard "do not do" decisions

For clarity, things we have decided NOT to do:

- **Do not host the Insights Agent in Foundry** (per §5.1)
- **Do not use Durable Functions** (ADR-001 — orchestration via Service Bus + state machine)
- **Do not put document content in cross-matter aggregates** (privilege leakage — §7.4)
- **Do not let any new index lack a `tenantId` field** (§4.1, §10.3)
- **Do not require human curation of identity resolution** (§4.2.4, §6.5)
- **Do not return generic AI hedging when Inference evidence is insufficient** (§5.4 — explicit insufficient-evidence response)

---

## 13. References

### 13.1 Internal documents

- [README.md](README.md) — Project README
- [ai-inventory.md](ai-inventory.md) — AI subsystem inventory (DI-anchored)
- [azure-inventory.md](azure-inventory.md) — Azure resource inventory
- [Spaarke AI Platform Unification (r2) design](../spaarke-ai-platform-unification-r2/design.md) — the design that surfaced the need for this Engine
- [ADR-001](../../docs/adr/ADR-001-minimal-api-and-workers.md) — BFF runtime + Function permissions (commit `84cec9f9`)
- [ADR-013](../../docs/adr/ADR-013-ai-architecture.md) — AI architecture
- [`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`](../../.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md) — active auth design canon (until ADR-027 lands)
- [`.claude/patterns/auth/obo-flow.md`](../../.claude/patterns/auth/obo-flow.md) — OBO server-side flow pattern
- [`Sprk.Bff.Api/Program.cs`](../../src/server/api/Sprk.Bff.Api/Program.cs) — reference inbound JWT validation setup
- [`Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs) — reference authorization wiring
- [Legal IQ Stack article](https://spaarke.com/why-spaarke/the-iq-stack) — the marketing positioning this Engine honestly delivers

### 13.2 Knowledge base (researcher-authored, 2026-05-19)

- [knowledge/azure-ai-search/](../../knowledge/azure-ai-search/) (with `insights-engine-supplement.md`)
- [knowledge/cosmos-gremlin/](../../knowledge/cosmos-gremlin/)
- [knowledge/azure-functions-isv/](../../knowledge/azure-functions-isv/)
- [knowledge/dataverse-sync/](../../knowledge/dataverse-sync/)
- [knowledge/foundry-memory-patterns/](../../knowledge/foundry-memory-patterns/)

### 13.3 External (current MS guidance, accessed 2026-05-19)

- Azure AI Search vector search: `learn.microsoft.com/en-us/azure/search/vector-search-overview` (updated 2026-05-13)
- AI Search integrated vectorization: `learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization` (updated 2026-03-18)
- Cosmos Gremlin overview: `learn.microsoft.com/en-us/azure/cosmos-db/gremlin/overview`
- Cosmos vector search: `learn.microsoft.com/en-us/azure/cosmos-db/vector-search`
- Azure Functions Flex Consumption: `learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan`
- Multi-tenant deployment: `learn.microsoft.com/en-us/azure/architecture/guide/multitenant/approaches/deployment-configuration` (updated 2026-04-30)
- Dataverse webhooks: `learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks` (updated 2026-04-01)
- Dataverse Azure integration: `learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration` (updated 2026-04-01)
- Foundry agent memory (concepts): `learn.microsoft.com/en-us/azure/foundry/agents/concepts/what-is-memory` (updated 2026-05-19)
- Foundry agent memory (usage): `learn.microsoft.com/en-us/azure/foundry/agents/how-to/memory-usage` (updated 2026-05-18)
