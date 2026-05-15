> ⚠️ STUB — senior engineer review pending

# NOTES — azure-ai-search

Project-specific commentary on azure-ai-search. Annotate from real Spaarke project experience; don't fabricate. Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

---

## 1. How this fits Spaarke's architecture

## When to use AI Search directly vs. through Foundry IQ knowledge base

_TODO: Decision rubric. Direct AI Search SDK from the BFF when: we need precise control over retrieval, custom filters from claims, deterministic latency, Redis caching of results (ADR-009). Foundry IQ knowledge source when: we want managed multi-source retrieval, agentic retrieval semantics built into Foundry Agent Service workflows, less code to maintain. Note: Foundry IQ can itself parent an AI Search index as an *indexed knowledge source* — so these aren't always either/or. Reference `knowledge/foundry-iq/NOTES.md` once that's annotated._

## Parallel-ingestion pattern (file -> SPE + AI Search)

_TODO: Document the canonical Spaarke upload path. On file upload: (1) BFF writes the file to SharePoint Embedded for storage and SPE-side permissions, (2) BFF separately enqueues an indexing job that streams the file through Document Intelligence (layout) -> chunking -> Azure OpenAI embedding -> AI Search index. Two indexes co-exist deliberately: SPE holds the canonical bytes and ACL; AI Search holds the retrieval-optimized projection. Note the gotchas: ACL drift between SPE and AI Search (mitigate by re-projecting permission fields when SPE permissions change), and the deletion contract (when SPE deletes, AI Search must also delete or filter out). Reference the actual BFF service that owns this dual write._

---

## 2. How we build with it

## Spaarke's AI Search index schema and ingestion pipeline

_TODO: Reconcile this folder's samples with Spaarke's actual index schema. Reference `docs/architecture/RAG-ARCHITECTURE.md` and `docs/architecture/RAG-CONFIGURATION.md` (assumed to exist — verify and link). Document the canonical field set (id, content, contentVector, sourcepage/sourcefile, matter/category/oids/groups, sensitivity-label fields), which fields are filterable/sortable/facetable, the vector dimension (1536 vs 3072 depending on embedding-3 variant), and the analyzer/language config. Note whether Spaarke uses a single primary index vs primary + chunked secondary index per `vector-search-integrated-vectorization.md`._

## Structure-aware chunking for legal documents

_TODO: Explain why naive fixed-window splitting destroys legal-document semantics (clauses split across windows, definitions detached from references, table rows fragmented). Document the Document Intelligence layout-model path (Markdown output preserves headings/sections/tables) and how Spaarke uses it — whether via the custom-skill pattern in `samples/document-intelligence-skillset/` or via the built-in `#Microsoft.Skills.Util.DocumentIntelligenceLayoutSkill`. Note any Spaarke-specific tuning (heading levels used as chunk boundaries, max chunk size in tokens, overlap, what to do with tables — keep intact vs explode rows)._

## Hybrid retrieval defaults (BM25 + vector + semantic reranking)

_TODO: Document Spaarke's standard retrieval recipe. Default `k` for vector neighbors, default `top` for keyword, whether semantic ranker is always on or only above some token budget, what semantic configuration name we use, which fields participate in the semantic config (`title`/`keywords`/`content` ordering — note the 128/128/remaining token limits in `docs/semantic-search-overview.md`). Reference the C# pattern in `samples/hybrid-search-dotnet/Program.cs`. Call out where Spaarke deviates: e.g. do we use query rewrite? Do we set a `@search.rerankerScore` minimum threshold?_

## Permission filtering at query time vs. coarse filtering at index time

_TODO: This is the critical decision. Document the trade-offs:_

- _Query-time `Filter` on `oids/any(o: o eq 'user-oid') or groups/any(g: g eq 'group-id')` — single index, OData filter composed per request from the caller's token claims, late-binding (permission changes reflect immediately). Per-document storage of `oids`/`groups` arrays. This is the `azure-search-openai-demo` pattern (refactored in newer commits but still the canonical OData shape)._
- _Index-time partitioning by matter/tenant — multiple indexes or filter by `matter` field; coarser, but cheaper at query time and simpler to reason about for tenancy._
- _Document which Spaarke uses (likely both: tenant/matter at index time, fine-grained ACL at query time) and reference the actual BFF code path that builds the filter._
- _Note: filter pushdown happens after vector retrieval by default — for high-cardinality ACLs consider `vectorFilterMode=preFilter` (see Azure AI Search docs)._

## ADR-009 cache layer over AI Search results

_TODO: Document how the Redis-first cache (per ADR-009) wraps retrieval results. What's the cache key shape — `(indexName, queryText, vector-or-hash, filter-string, top, userOid)`? Note that the cache key MUST include the permission-filter inputs (oids/groups from the caller) or stale ACL-violating hits could leak across users. TTL? Invalidation triggers? Reference the actual cache wrapper class in `Sprk.Bff.Api`._

---

## Quick references for skill authors

- Want to add embeddings + hybrid retrieval to a new feature? Start with `samples/hybrid-search-dotnet/Program.cs` (query shape) and `samples/integrated-vectorization-dotnet/Program.cs` (index/indexer wiring).
- Want to implement query-time permission filtering? Start with `samples/rag-csharp/SearchClientExtensions.cs` — note the `Filter` string composition pattern.
- Want to add structure-aware chunking for a new content type? Read `samples/document-intelligence-skillset/setup_search_service.py` for the skillset shape, then translate to the BFF's existing indexer-management code.
- Need to understand what semantic ranker can and can't do? `docs/semantic-search-overview.md` is authoritative as of the fetch date.
