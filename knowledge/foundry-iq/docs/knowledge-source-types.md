---
source: https://learn.microsoft.com/en-us/azure/search/agentic-knowledge-source-overview
canonical_source: https://learn.microsoft.com/en-us/azure/search/agentic-knowledge-source-overview
fetched: 2026-05-14
ms_date: 2026-05-05
ms_git_commit: 3295a3c449bc576e333be568340202f25e4721f6
note: |
  Directive URL was https://learn.microsoft.com/en-us/azure/ai-foundry/agents/knowledge-source-types
  which 404s on 2026-05-14. Source types live under Azure AI Search docs, not under
  ai-foundry/agents. This page is the canonical type reference + selection-logic source.
---

# What is a Knowledge Source? (Azure AI Search)

> **Note (Microsoft)**: Some agentic retrieval features are generally available in the
> **2026-04-01** REST API version via programmatic access. The Azure portal and Microsoft
> Foundry portal continue to provide preview-only access to all agentic retrieval features.

A knowledge source specifies the content used for agentic retrieval. It either
encapsulates a search index populated by external data, or it's a direct connection to a
remote target such as Bing or SharePoint that's queried directly. A knowledge source is a
required definition in a knowledge base.

- Create a knowledge source as a top-level resource on your search service. Each knowledge
  source points to exactly one data structure, either a search index that meets the
  criteria for agentic retrieval or a supported external resource.
- Reference one or more knowledge sources in a knowledge base. In an agentic retrieval
  pipeline, you can query against multiple knowledge sources in a single request.
  Subqueries are generated for each knowledge source. Top results are returned in the
  retrieval response.
- For certain knowledge sources, you can use a knowledge source definition to generate a
  full indexer pipeline (data source, skillset, indexer, and index) that works for
  agentic retrieval. Instead of creating multiple objects manually, the information in
  the knowledge source is used to generate all objects, including a populated, chunked,
  and searchable index.

## Working with a knowledge source

- **Creation path**: first create a knowledge source, then create a knowledge base.
- **Deletion path**: update or delete knowledge bases to remove references to a knowledge
  source, and then delete the knowledge source last.
- A knowledge source, its index, and the knowledge base must all exist on the same search
  service. External content is either accessed over the public internet (Bing) or in a
  Microsoft tenant (remote SharePoint).

## Supported knowledge sources

| Kind | Indexed or remote | Notes |
| --- | --- | --- |
| `"searchIndex"` | Indexed | Wraps an existing index. |
| `"azureBlob"` | Indexed | Generates an indexer pipeline that pulls from a blob container. |
| `"indexedOneLake"` | Indexed | Generates an indexer pipeline that pulls from a lakehouse. |
| `"indexedSharePoint"` (preview) | Indexed | Generates an indexer pipeline that pulls from a SharePoint site. |
| `"remoteSharePoint"` (preview) | Remote | Retrieves content directly from SharePoint via the Copilot Retrieval API. |
| `"web"` | Remote | Retrieves real-time grounding data from Microsoft Bing. |

**Indexed knowledge sources** point to a target index on Azure AI Search. Query execution
is local to the search engine on your search service. Keyword (full text search), vector,
and hybrid query capabilities are used for retrieving data from indexed knowledge sources.

**Remote knowledge sources** are accessed at query time. The agentic retrieval engine
calls the retrieval APIs that are native to the platform (Bing or SharePoint APIs).

All retrieved content, whether indexed or remote, is pulled into the ranking pipeline in
Azure AI Search where it's scored for relevance, merged (assuming multiple queries),
reranked, and returned in the retrieval response.

## Creating knowledge sources

Create knowledge sources as standalone objects. Then, specify them in a knowledge base
within a `knowledgeSources` array.

To create objects on a search service, you need **Search Service Contributor** permissions.
If you're using a knowledge source that creates an indexer pipeline, you also need
**Search Index Data Contributor** permissions to load an index.

How-to pages on Microsoft Learn (under `/azure/search/`):

- `agentic-knowledge-source-how-to-search-index` — wraps an existing index
- `agentic-knowledge-source-how-to-blob` — generates an indexer pipeline from blob
- `agentic-knowledge-source-how-to-onelake` — generates an indexer pipeline from OneLake
- `agentic-knowledge-source-how-to-sharepoint-indexed` — generates indexer pipeline from SharePoint
- `agentic-knowledge-source-how-to-sharepoint-remote` — queries SharePoint directly via Copilot Retrieval
- `agentic-knowledge-source-how-to-web` — connects to Bing's public endpoint

## Using knowledge sources

You can explicitly control knowledge source usage by setting `alwaysQuery` on the knowledge
source definition or through steering instructions used during query planning. Steering
instructions refer to descriptions on an index, or explicit retrieval instructions in the
knowledge source, that provide guidance on when to use the index.

Query planning happens when you use a **low or medium** retrieval reasoning effort from
the LLM. For a **minimal** reasoning effort, all knowledge sources listed in the knowledge
base are in scope for every query. For **low and medium**, the knowledge base and the LLM
can determine at query time which knowledge sources are likely to provide the best search
corpus.

Knowledge source selection logic is based on these factors:

- Is `alwaysQuery` set? If yes, the knowledge source is always used on every query.
- The `name` of the knowledge source.
- The `description` of an index, assuming an indexed knowledge source.
- The `retrievalInstructions` specified in the retrieve action or in the knowledge base
  definition. It's similar to a prompt. You can specify brevity, tone, and formatting as a
  retrieval instruction.
- `outputMode` on a knowledge base also affects query output and what goes in the response.

### Use a retrieval reasoning effort to control LLM usage

Not all solutions benefit from LLM query planning and execution. If simplicity and speed
outweigh the benefits the LLM query planning and context engineering provide, specify a
**minimal** reasoning effort to prevent LLM processing in your pipeline.

For **low and medium**, the level of LLM processing is either a balanced or maximal
approach that improves relevance.

> **Note (Microsoft)**: If you used `attemptFastPath` in the previous preview, that approach
> is now replaced by `retrievalReasoningEffort` set to `minimal`.
