# SOURCE — azure-ai-search

> Provenance for curated Azure AI Search samples and reference docs.

**Curated**: 2026-05-14
**Curator**: ai-knowledge-base-setup-r1 (Claude Code agent)
**Refresh cadence**: monthly (see `knowledge/REFRESH-LOG.md`)

---

## Source repositories

| Repo | URL | Commit (2026-05-14) | License | Used for |
|---|---|---|---|---|
| `Azure/azure-search-vector-samples` | https://github.com/Azure/azure-search-vector-samples | `bfec9cf37d06306799c467c73c78d3cd39ef3973` | MIT | Integrated vectorization, hybrid + semantic queries, Document Intelligence custom skill |
| `Azure-Samples/azure-search-openai-demo-csharp` | https://github.com/Azure-Samples/azure-search-openai-demo-csharp | `7d702a68d37f8c93d902acf8a35088979a2e3fa3` | MIT | End-to-end C# RAG demo with category filtering at query time |

> The Python sibling repo `Azure-Samples/azure-search-openai-demo` was inspected (commit `95ce0c9484b338b3819914d0c1a1fa8d19a3ff9b`) but not curated here — the C# variant is closer to Spaarke's .NET 8 BFF and contains the same retrieval pattern. ACL-style permission filtering (`oids/any(...)`/`groups/any(...)`) in the Python repo has been refactored into an agentic-retrieval/knowledge-source path; the C# repo's `Filter` on `SearchOptions` is the canonical pattern we curate.

## Curated files

### `samples/integrated-vectorization-dotnet/` — Integrated vectorization indexer (.NET)

End-to-end C# console app that creates a data source over Azure Blob, an index with vector fields, an `AzureOpenAIEmbedding` skill, a vectorizer for query-time text-to-vector, and runs an indexer. Demonstrates the recommended path: let Azure AI Search call Azure OpenAI for embeddings at both index and query time.

| File | Source path | What it shows |
|---|---|---|
| `Program.cs` | `azure-search-vector-samples/demo-dotnet/DotNetIntegratedVectorizationDemo/Program.cs` | Index/skillset/data source/indexer creation; embedding skill + vectorizer pairing; vector query against the index. |
| `Configuration.cs` | same dir / `Configuration.cs` | Strongly-typed config binding (search endpoint, Azure OpenAI endpoint, embedding deployment, blob URL). |
| `DotNetIntegratedVectorizationDemo.csproj` | same dir / `.csproj` | Package versions for `Azure.Search.Documents`, `Azure.Identity` (the version pin matters for Vector* types). |
| `local.settings-sample.json` | same dir / `local.settings-sample.json` | Settings shape. |
| `readme.md` | same dir / `readme.md` | Run instructions and prerequisites. |

### `samples/hybrid-search-dotnet/` — Hybrid (vector + keyword + semantic) query in C#

C# console that issues a single search request combining a `VectorizableTextQuery` (text-to-vector at query time), a BM25 `search` term, and `QueryType.Semantic` for reranking. Demonstrates the full hybrid retrieval recipe with RRF and semantic L2 reranking on top.

| File | Source path | What it shows |
|---|---|---|
| `Program.cs` | `azure-search-vector-samples/demo-dotnet/DotNetVectorDemo/Program.cs` | `SearchOptions` with `QueryType.Semantic` + `SemanticSearch`; vector query with `KNearestNeighborsCount`; reading `SemanticSearch.Answers`, `Captions`, `@search.rerankerScore`. |
| `Configuration.cs` | same dir / `Configuration.cs` | Config binding. |
| `DotNetVector.csproj` | same dir / `.csproj` | Package pins. |
| `local.settings-sample.json` | same dir / `local.settings-sample.json` | Settings shape. |
| `readme.md` | same dir / `readme.md` | Index/skillset variants used by the sample. |

### `samples/document-intelligence-skillset/` — Structure-aware chunking via Document Intelligence layout

Custom Web API skill (Azure Function) that calls Document Intelligence's layout model, emits Markdown that preserves paragraph/heading/table structure, then is chunked by `MarkdownHeaderSplitter`. Companion `setup_search_service.py` wires the skill into a skillset alongside `AzureOpenAIEmbedding` and index projections (one parent doc -> many chunked child docs).

Python only — the layout-skill pattern is platform-side; the C# BFF only consumes the resulting index, so language parity isn't needed for this pattern.

| File | Source path | What it shows |
|---|---|---|
| `function_app.py` | `azure-search-vector-samples/demo-python/code/indexers/document-intelligence-custom-skill/api/functions/function_app.py` | Custom skill HTTP shape; calling `DocumentIntelligenceClient` with `OutputContentFormat.MARKDOWN`; emitting `markdown_document` + per-page metadata. |
| `setup_search_service.py` | same demo / `scripts/setup_search_service.py` | Skillset wiring: custom layout skill -> Text Split (markdown mode) -> AzureOpenAIEmbedding -> index projections to chunked index. |
| `function_requirements.txt` | same demo / `api/functions/requirements.txt` | Python deps for the Function. |
| `readme.md` | same demo / `readme.md` | Architecture and runbook. |

### `samples/rag-csharp/` — End-to-end C# RAG with query-time filtering

Subset of `azure-search-openai-demo-csharp` showing the retrieval-augmented generation loop: hybrid retrieval (vector + keyword + optional semantic reranking) with an OData `Filter` applied at query time. The filter is what enforces document-level access control by category in the demo; the same pattern extends to ACL fields like `oids` / `groups`.

| File | Source path | What it shows |
|---|---|---|
| `SearchClientExtensions.cs` | `azure-search-openai-demo-csharp/app/backend/Extensions/SearchClientExtensions.cs` | `QueryDocumentsAsync` extension: builds `SearchOptions` with `Filter`, `QueryType.Semantic`, semantic captions, vector queries; merges results into `SupportingContentRecord`s. **Read this first.** |
| `ReadRetrieveReadChatService.cs` | `azure-search-openai-demo-csharp/app/backend/Services/ReadRetrieveReadChatService.cs` | RAG orchestration: query rewriting -> retrieve via `QueryDocumentsAsync` -> compose grounded prompt -> Azure OpenAI chat completion -> citation extraction. |
| `RequestOverrides.cs` | `azure-search-openai-demo-csharp/app/shared/Shared/Models/RequestOverrides.cs` | Per-request override flags including `use_oid_security_filter` and `use_groups_security_filter` (canonical names for permission-filter overrides). |
| `Program.cs` | `azure-search-openai-demo-csharp/app/backend/Program.cs` | Minimal API wiring: DI registration, endpoint mapping, static UI hosting. |
| `ServiceCollectionExtensions.cs` | `azure-search-openai-demo-csharp/app/backend/Extensions/ServiceCollectionExtensions.cs` | DI registration for `SearchClient`, `OpenAIClient`, blob clients, and `ReadRetrieveReadChatService`. |

## Snapshotted reference docs

`docs/` mirrors of Microsoft Learn pages, fetched 2026-05-14. YAML frontmatter records `source` and `fetched`. Re-fetch monthly per the refresh procedure.

| File | Source URL |
|---|---|
| `docs/vector-search-overview.md` | https://learn.microsoft.com/en-us/azure/search/vector-search-overview |
| `docs/vector-search-integrated-vectorization.md` | https://learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization |
| `docs/semantic-search-overview.md` | https://learn.microsoft.com/en-us/azure/search/semantic-search-overview |
| `docs/search-howto-index-sharepoint-online.md` | https://learn.microsoft.com/en-us/azure/search/search-howto-index-sharepoint-online |

## Gaps / decisions

- **No Spaarke SPE-specific indexer sample.** Azure AI Search has a (preview) SharePoint Online indexer but does **not** have an SPE container indexer. Spaarke's pattern is parallel ingestion (file -> SPE for storage + AI Search for retrieval) driven by the BFF on upload. Document this in `NOTES.md` rather than fabricate a sample.
- **Permission filtering via `oids` / `groups`.** The `azure-search-openai-demo-csharp` repo exposes the override flags (`use_oid_security_filter`, `use_groups_security_filter`) but the current state of the C# code paths only implements category-style filtering in `SearchClientExtensions.cs`. The Python sibling repo has refactored ACL filtering to flow through agentic-retrieval/knowledge-source instead of building OData filters directly. Spaarke will likely implement ACL filtering by composing the `Filter` string itself; the `category ne 'X'` pattern in `SearchClientExtensions.cs` shows the shape.
- **Document Intelligence layout-skill sample is Python.** Microsoft has no first-party C# sample for the layout-skill chunking pattern as of this curation date; the platform side (Function/skillset) is language-agnostic, so the Python sample is the canonical reference.
