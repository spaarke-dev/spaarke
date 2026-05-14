# SOURCE — foundry-iq

> **Curated**: 2026-05-14
> **Curator**: Claude Code (sub-agent, knowledge-base-setup-r1)
> **Refresh cadence**: monthly (first business day)
> **Topic focus**: **Foundry IQ knowledge bases** — managed knowledge layer for grounded
> retrieval (knowledge sources, knowledge bases, agentic retrieval). Distinct from the
> sibling `foundry-agent-service/` topic, which covers the agent runtime (MCP tool binding,
> approval modes, hosted agents).

## Diff from `knowledge/foundry-agent-service/`

| Aspect | `foundry-agent-service/` | `foundry-iq/` (this topic) |
| --- | --- | --- |
| Layer | Agent runtime — Foundry Agent Service | Managed knowledge layer — Foundry IQ |
| Primary SDK surface | `azure.ai.agents` / `azure.ai.projects` | `azure.search.documents.indexes` (KnowledgeBase, KnowledgeSource) + `azure.search.documents.knowledgebases` (retrieval) |
| Sample focus | MCP tool binding, approval modes (HITL), hosted agents, A2A | Knowledge base creation over Blob / AI Search / SharePoint, retrieval with citations, agent grounding wiring |
| Overlapping concept | Agent grounding (consumer side) | Knowledge base (producer side) — sample `agent-grounding-wiring/` here is the bridge |

The two topics are complementary: agents from `foundry-agent-service/` ground themselves
on knowledge bases curated under `foundry-iq/`.

## Source repositories

| Repo | Commit SHA (2026-05-14) | Notes |
|---|---|---|
| [`microsoft/iq-series`](https://github.com/microsoft/iq-series) | `4e0c79e0692f7d5e5669f76a880af35435928389` | **Canonical first-party Foundry IQ learning series.** Three episode cookbooks (Jupyter): (1) KB over AI Search index + agent wiring; (2) data pipeline with multiple knowledge source types (Blob, Web); (3) querying multi-source KBs. Maintained by Microsoft, premiered March 2026. |
| [`Azure-Samples/sharepoint-foundryIQ-secure-sync`](https://github.com/Azure-Samples/sharepoint-foundryIQ-secure-sync) | `2afa0beed107671e1fc4c39afb4ccfda5ae8c1dd` | First-party sample for the SharePoint→Blob ACL-aware ingestion path **plus** a doc explaining indexed-vs-remote SharePoint knowledge sources with code blocks for both. The "remote" approach is the Copilot Retrieval API path the directive calls out. |
| [`MSFT-Innovation-Hub-India/FoundryIQ-kb-Sample1`](https://github.com/MSFT-Innovation-Hub-India/FoundryIQ-kb-Sample1) | `f809a547f6a12520e5060a66c9b69e96fcea824d` | Microsoft Innovation Hub (India) sample. Crisp, narrow Python scripts for `create_knowledge_sources.py` and `create_kb.py` plus a `kb_query_service.py` with reference/citation handling. The cleanest standalone KB-over-AI-Search example we found. |
| [`RobertEichenseer/AgenticAI.FoundryIQ`](https://github.com/RobertEichenseer/AgenticAI.FoundryIQ) | `46be5e8b744efcc8e1060ab8cc6f6ca7f066ca7c` | Community sample (Microsoft employee). **C# / .NET polyglot notebook** showing the agentic grounding wiring. Included to cover the .NET side; all other curated samples are Python. |

## Repos referenced but not curated from

| Repo | Status | Reason |
|---|---|---|
| `Azure-Samples/azure-ai-foundry` (directive) | **404 on 2026-05-14** | Already replaced in batch 2 by `Azure-Samples/ai-foundry-agents-samples` for the agent topic. For Foundry IQ specifically, `microsoft/iq-series` is the better first-party source. |
| `Azure-Samples/azureai-samples` (directive) | Cloned in batch 2; not re-pulled here | Broad Azure AI samples; the agent-relevant subdirectories were already pulled for `foundry-agent-service/`. No additional KB-creation-specific samples beyond what `iq-series` already covers. |
| `Azure-Samples/adaptive-rag-workbench` | Inspected, not pulled | Multi-agent agentic-RAG workbench using AI Search agentic retrieval + Semantic Kernel + LangGraph; useful as a higher-level architecture reference but too large/complex for a curated minimum subset. |

## Curated files

```
foundry-iq/
├── SOURCE.md                                                  (this file)
├── NOTES.md                                                   (stub — pending senior engineer annotation)
├── docs/
│   ├── what-is-foundry-iq.md                                  snapshot — Foundry IQ concept doc
│   ├── create-knowledge-base.md                               snapshot — Create a knowledge base (how-to)
│   └── knowledge-source-types.md                              snapshot — Knowledge source overview + selection logic
└── samples/
    ├── kb-over-ai-search/
    │   ├── create_knowledge_sources.py                        Knowledge sources over 4 AI Search indexes + Bing web source
    │   ├── create_kb.py                                       Knowledge base wiring 5 sources, AOAI model, low reasoning effort
    │   ├── kb_query_service.py                                Retrieval client with citation/reference handling
    │   └── requirements.txt                                   (from upstream)
    ├── kb-over-blob-and-web/
    │   ├── README.md                                          (from upstream — episode 2 cookbook overview)
    │   └── foundry-iq-cookbook.ipynb                          Notebook — search index KS, blob KS (indexer pipeline), web KS
    ├── kb-with-sharepoint-remote/
    │   └── agentic-retrieval-foundry-iq.md                    Indexed-vs-remote SharePoint KS comparison + code blocks for both
    └── agent-grounding-wiring/
        ├── README.md                                          (from upstream — episode 1 cookbook overview)
        ├── foundry-iq-cookbook.ipynb                          Python — KB + Foundry Agent grounding via AIProjectClient
        └── FoundryIq-csharp.ipynb                             C# polyglot — KnowledgeSource → KnowledgeBase → agent grounding
```

## File-by-file provenance

### `samples/kb-over-ai-search/`

- **Source**: `MSFT-Innovation-Hub-India/FoundryIQ-kb-Sample1` at `f809a547f6`
- **Paths**: `ops/create_knowledge_sources.py`, `ops/create_kb.py`, `kb_query_service.py`, `requirements.txt`
- **Demonstrates** (deliverable: *KB over Azure AI Search*):
  - `create_knowledge_sources.py` — creates four `SearchIndexKnowledgeSource` objects pointing at existing AI Search indexes, plus a `WebKnowledgeSource` (Bing) with optional domain allow/block lists. Uses `SearchIndexClient.create_or_update_knowledge_source(...)`.
  - `create_kb.py` — creates a `KnowledgeBase` that references all 5 sources, wires an `AzureOpenAIVectorizerParameters` model, sets `retrieval_instructions` to route per-source, sets `output_mode=ANSWER_SYNTHESIS`, and uses `KnowledgeRetrievalLowReasoningEffort`. Uses `KnowledgeSourceReference(name=...)`.
  - `kb_query_service.py` — `KnowledgeBaseRetrievalClient` with reasoning-effort + output-mode normalization, per-source `SearchIndexKnowledgeSourceParams` (with `include_references`, `include_reference_source_data`, `always_query_source`), web-source citation stripping, structured response (`answers` / `citations` / `timing` / `activity`).

### `samples/kb-over-blob-and-web/`

- **Source**: `microsoft/iq-series` at `4e0c79e069`, path `2-Foundry-IQ-Building-the-Data-Pipeline-with-Knowledge-Sources/cookbook/`
- **Demonstrates** (deliverable: *KB over Azure Blob (indexed source)*):
  - Notebook walks through (1) Understanding KS types (indexed vs. remote); (2) Creating a search index and uploading sample product data; (3) Creating an indexed KS over AI Search; (4) Creating a **Blob Storage KS** with the automated indexer pipeline; (5) Creating a **Web KS** (real-time public info via Bing); (6) Combining sources in one KB; (7) Querying across sources and inspecting the activity log; (8) Security/governance.
  - This is the canonical first-party example of `azureBlob` as a knowledge source — Foundry IQ auto-generates the data source, skillset, indexer, and index from the KS definition alone.

### `samples/kb-with-sharepoint-remote/`

- **Source**: `Azure-Samples/sharepoint-foundryIQ-secure-sync` at `2afa0beed1`, path `docs/agentic-retrieval-foundry-iq.md`
- **Demonstrates** (deliverable: *SharePoint as a remote source — Copilot Retrieval API path*):
  - Side-by-side comparison of **indexed SharePoint** (Blob sync + OCR + custom chunking + dual-layer ACLs) vs **remote SharePoint** (Copilot Retrieval API queries SharePoint on-the-fly, runs under user identity via `x-ms-query-source-authorization`).
  - Code blocks for both: `azureSearchIndex` KS pointing at a SharePoint-sourced index, and `sharePoint` (remote) KS with a `filterExpression` to scope by SiteID.
  - Permission enforcement across three layers: SharePoint permissions → Purview/RMS intersection → agentic-retrieval query-time filter. Mentions native Purview sensitivity label integration (preview, `2025-11-01-preview`) as a fourth layer.
  - Knowledge base composition example using `knowledgeSources` array referencing both kinds. Closing example wires the KB into a Foundry agent via MCP tool.
- **Choice rationale**: This was the only curated sample we found on 2026-05-14 that showed the **remote SharePoint** path concretely. Microsoft Learn's `agentic-knowledge-source-how-to-sharepoint-remote` how-to is referenced but not pulled (preview surface URL behavior is unreliable; the doc references in `docs/knowledge-source-types.md` cover the API name).

### `samples/agent-grounding-wiring/`

- **Sources**:
  - Python notebook: `microsoft/iq-series` at `4e0c79e069`, path `1-Foundry-IQ-Unlocking-Knowledge-for-Agents/cookbook/`
  - C# notebook: `RobertEichenseer/AgenticAI.FoundryIQ` at `46be5e8b74`, path `src/FoundryIq.ipynb`
- **Demonstrates** (deliverable: *Wiring a knowledge base into an agent's grounding config*):
  - **Python** (`foundry-iq-cookbook.ipynb`) — episode 1 walkthrough: (1) Create KS over AI Search index; (2) Create KB pairing data with an LLM for agentic retrieval; (3) Query KB and inspect synthesized answers with citations; (4) Connect Foundry IQ to Foundry Agent Service so an agent grounds responses in your data. Uses `AIProjectClient` from `azure.ai.projects`.
  - **C#** (`FoundryIq-csharp.ipynb`) — polyglot notebook showing the same agentic-grounding wiring in .NET. Step-by-step KnowledgeSource → KnowledgeBase → agent grounding. References `Azure.Search.Documents` SDK.

## Gaps

| Pattern (per directive) | Status | Notes |
|---|---|---|
| KB over Azure Blob (indexed) | **COVERED** | `samples/kb-over-blob-and-web/foundry-iq-cookbook.ipynb` — full first-party walkthrough. |
| KB over Azure AI Search | **COVERED** | `samples/kb-over-ai-search/create_knowledge_sources.py` + `create_kb.py` — clean standalone scripts. Also covered in `kb-over-blob-and-web/` notebook. |
| SharePoint as remote source (Copilot Retrieval API) | **PARTIAL** | `samples/kb-with-sharepoint-remote/agentic-retrieval-foundry-iq.md` has the canonical comparison + code blocks, but the **runnable** sample in that repo is the indexed (Blob-sync) path. A standalone Python sample creating just a `remoteSharePoint` KS was not found on 2026-05-14. The how-to URL `agentic-knowledge-source-how-to-sharepoint-remote` is referenced in `docs/knowledge-source-types.md` but the page itself was not snapshotted (preview surface, frequent path changes — defer to next refresh). |
| Wiring KB into agent grounding | **COVERED** | Two flavors: Python (iq-series episode 1) and C# (Eichenseer polyglot notebook). |
| Permission filtering deep-dive (Purview/RMS intersection, native sensitivity labels preview) | **REFERENCED** | The `kb-with-sharepoint-remote/` doc covers the three-layer model. A standalone sample wasn't pulled — `sharepoint-foundryIQ-secure-sync` has 2,000+ lines of secure-sync code we deliberately did NOT curate (out of scope and exceeds budget). |
| OneLake as a knowledge source | **GAP** | Not curated. `indexedOneLake` is listed as a supported type in `docs/knowledge-source-types.md` but no first-party `iq-series` episode or compact sample for OneLake was found on 2026-05-14. Watch for Fabric IQ overlap at next refresh. |
| End-to-end retrieval pipeline tutorial | **REFERENCED, not pulled** | `learn.microsoft.com/azure/search/agentic-retrieval-how-to-create-pipeline` is referenced in `docs/what-is-foundry-iq.md` but not snapshotted (within reasonable budget). Consider pulling at next refresh if engineers report missing end-to-end context. |

## Refresh notes

- **Dead URL fixes**: 2 of 3 directive-listed Microsoft Learn URLs returned 404 on 2026-05-14:
  - `/azure/ai-foundry/concepts/knowledge` → canonical `/azure/foundry/agents/concepts/what-is-foundry-iq` (under `/agents/`, not `/concepts/`)
  - `/azure/ai-foundry/agents/knowledge-base-create` → canonical `/azure/search/agentic-retrieval-how-to-create-knowledge-base` (under `/azure/search/`, not `/azure/ai-foundry/agents/`)
  - `/azure/ai-foundry/agents/knowledge-source-types` → canonical `/azure/search/agentic-knowledge-source-overview` (same pattern — under `/azure/search/`)
- **Dead repo fix**: `Azure-Samples/azure-ai-foundry` (directive) → use `microsoft/iq-series` as the primary Foundry IQ source. `Azure-Samples/ai-foundry-agents-samples` (the agent-topic replacement) does not have Foundry IQ knowledge-base-specific samples.
- **Foundry IQ is a relatively new managed surface** (Ignite 2025 launch, March 2026 episode series start). Public samples on 2026-05-14 are heavily concentrated in `microsoft/iq-series` + a handful of Microsoft Innovation Hub samples. Expect this knowledge area to evolve significantly at each monthly refresh.
- **Next refresh focus**:
  - `iq-series` episodes 4-5 if Work IQ / Fabric IQ episodes have shipped (per repo README, they are "coming soon").
  - Standalone `remoteSharePoint` KS sample if Microsoft publishes one.
  - OneLake KS sample.
  - `Azure-Samples/sharepoint-foundryIQ-secure-sync` — check for new `samples/` subdirectories specific to native Purview sensitivity label enforcement (preview).
