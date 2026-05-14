> ⚠️ STUB — senior engineer review pending

# NOTES — foundry-iq

This file is a placeholder for senior-engineer annotations on the curated Foundry IQ
samples. The structure below mirrors the **`NOTES.md` guidance** in
`projects/coding-knowledge-base-setup-r1/SPAARKE-KNOWLEDGE-BASE-SETUP.md` (Phase 2.6). Each
section has a `_TODO_` hint instead of speculative content.

When annotating: replace each `_TODO_` line with concrete guidance drawn from real Spaarke
project work. Honest stub entries are more valuable than fabricated insight.

---

## Indexed sources vs. remote sources — currency vs. control trade-off

_TODO: Explain when Spaarke should choose **indexed** knowledge sources (Azure Blob, AI Search index, OneLake) vs. **remote** sources (Bing web, remote SharePoint via Copilot Retrieval API). Hint: indexed sources give chunking control, vector embeddings, OCR, custom scoring profiles, and low latency, but freshness lags the sync interval. Remote sources give real-time content and skip an indexing layer but lose chunking control, OCR, vector search, and add per-query latency. Reference `samples/kb-with-sharepoint-remote/agentic-retrieval-foundry-iq.md` for the SharePoint-specific decision matrix._

## Hybrid retrieval, semantic reranking, permission filtering

_TODO: Document Spaarke's settings for the three retrieval primitives Foundry IQ exposes:_

- _**Hybrid retrieval**: keyword + vector queries — what's the default in the curated samples, when to override._
- _**Semantic reranking**: L2 reranking after parallel subquery execution — when is reranking essential vs. when does it add latency without proportional relevance gain._
- _**Permission filtering**: ACL synchronization for indexed sources + identity propagation via `x-ms-query-source-authorization` for remote SharePoint. How does Spaarke wire its Entra identity through. Cross-reference the three-layer enforcement model in `samples/kb-with-sharepoint-remote/agentic-retrieval-foundry-iq.md` (SharePoint → Purview/RMS intersection → query-time filter)._

## Citation handling in retrieval responses

_TODO: Annotate the citation-handling pattern in `samples/kb-over-ai-search/kb_query_service.py`:_

- _What does the structured response (`answers` / `citations` / `timing` / `activity`) look like for each KS type (`searchIndex`, `web`, `azureBlob`)._
- _How does Spaarke surface citations to the user — full source URLs, relevance scores, click-through behavior._
- _The sample includes a `_remove_web_citation_markers` step to strip `[ref_id:N]` markers for web references. Is this the right default for Spaarke's UI, or do we keep web citations and render them differently from indexed-source citations._
- _Reranker score thresholds — when do we filter low-confidence citations out of the response shown to the user._

## When to use Foundry IQ vs. direct AI Search

_TODO: Decision criteria for Spaarke:_

- _When to query a Foundry IQ **knowledge base** (multi-source, agentic, citations, permission-aware) vs. when to query an **AI Search index directly** (single source, sub-100ms latency, application-code retrieval)._
- _The `retrievalReasoningEffort` knob (`minimal` / `low` / `medium`) is where the cost/latency vs. relevance trade-off lives. What does Spaarke pick as a default and when do callers override._
- _Cite the agentic-retrieval overview in `docs/what-is-foundry-iq.md` and the selection-logic section in `docs/knowledge-source-types.md` (alwaysQuery, name, description, retrievalInstructions, outputMode)._

## Spaarke's pattern — three retrieval surfaces

_TODO: Document Spaarke's intended composition for the legal-tech domain:_

- _**Foundry IQ knowledge bases** for **golden documents** — curated playbooks, exemplar contracts, legal research where curation matters and the corpus is small/stable._
- _**Direct AI Search index queries** for **application-code retrieval** — fast lookups inside Spaarke's BFF API where the agent isn't in the loop._
- _**SPE substrate index** for **default agent grounding on matter documents** — per-matter document corpus indexed automatically as files land in SharePoint Embedded._

_Note (from the directive): "Spaarke's pattern: Foundry IQ knowledge bases for golden documents (playbooks, exemplar contracts, legal research) where curation matters; direct AI Search index for application-code queries; SPE substrate index for default agent grounding on matter documents."_

_TODO: Add the routing logic — how does the Spaarke agent decide which surface to call. Does it follow `retrievalInstructions` set on the KB, or does it use a separate router/orchestrator. Reference any Spaarke-side ADR if one exists._

---

## Common pitfalls

_TODO: Senior engineer to fill in real-world pitfalls hit during early adoption._

Candidate topics worth checking against actual usage:

- _API version drift — 2025-11-01-preview vs 2026-04-01 (GA) behaviors diverge significantly. `retrievalReasoningEffort`, `outputMode`, `retrievalInstructions`, and `models` for non-web sources are **preview-only** per `docs/create-knowledge-base.md`. If a sample stops working after a stable-package upgrade, the most likely cause is the GA API silently dropping a preview-only property._
- _Region availability — agentic retrieval requires specific regions (see `search-region-support` in Microsoft Learn)._
- _Tenant policy blocking key-based access on storage accounts (called out in `samples/kb-over-blob-and-web/README.md`)._
- _SDK package: `azure-search-documents==11.7.0b2` for preview features. The iq-series samples pin this version._

## Cross-references

- Companion topic: `knowledge/foundry-agent-service/` — the agent-runtime side (how an agent **consumes** the knowledge base wired up here).
- Companion topic: `knowledge/azure-ai-search/` — direct AI Search index queries (application-code retrieval path).
- Companion topic: `knowledge/sharepoint-embedded/` — the SPE substrate index for default matter-document grounding.
- Companion topic: `knowledge/work-iq/` — collaboration context layer (different surface, complementary not duplicative).
