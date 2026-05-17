---
source: https://learn.microsoft.com/en-us/azure/search/semantic-search-overview
fetched: 2026-05-14
---

# Semantic Ranking Overview - Azure AI Search

In Azure AI Search, *semantic ranker* is a feature that measurably improves search relevance by using Microsoft's language understanding models to rerank search results. Semantic ranker is also built into agentic retrieval. This article is a high-level introduction to help you understand the behaviors and benefits of semantic ranker.

Semantic ranker is a premium feature that's billed by usage, but you can use it for free subject to service limits for the free tier.

## What is semantic ranking?

Semantic ranker is a collection of query-side capabilities that improve the quality of an initial BM25-ranked or RRF-ranked search result for text-based queries, the text portion of vector queries, and hybrid queries. Semantic ranking extends the query execution pipeline in three ways:

- First, it always adds secondary ranking over an initial result set that was scored using BM25 or Reciprocal Rank Fusion (RRF). This secondary ranking uses multilingual, deep learning models adapted from Microsoft Bing to promote the most semantically relevant results.
- Second, it returns captions and optionally extracts answers in the response, which you can render on a search page to improve the user's search experience.
- Third, if you enable query rewrite, it expands an initial query string into multiple semantically similar query strings.

Secondary ranking and "answers" apply to the query response. Query rewrite is part of the query request.

Here are the capabilities of the semantic reranker:

| Capability | Description |
| --- | --- |
| L2 ranking | Uses the context or semantic meaning of a query to compute a new relevance score over preranked results. |
| Semantic captions and highlights | Extracts verbatim sentences and phrases from fields that best summarize the content, with highlights over key passages for easy scanning. Captions that summarize a result are useful when individual content fields are too dense for the search results page. Highlighted text elevates the most relevant terms and phrases so that users can quickly determine why a match was considered relevant. |
| Semantic answers | An optional and extra substructure returned from a semantic query. It provides a direct answer to a query that looks like a question. It requires that a document has text with the characteristics of an answer. |
| Query rewrite | Using text queries or the text portion of a vector query, semantic ranker creates up to 10 variants of the query, perhaps correcting typos or spelling errors, or rephrasing a query using generated synonyms. The rewritten query runs on the search engine. The results are scored using BM25 or RRF scoring, and then rescored by semantic ranker. |

## How semantic ranker works

Semantic ranker takes a query and results, then sends them to language understanding models hosted by Microsoft. It scans for better matches.

Consider the term "capital". It has different meanings depending on whether the context is finance, law, geography, or grammar. Through language understanding, the semantic ranker detects context and promotes results that fit query intent.

Semantic ranking uses a lot of resources and time. To finish processing within the expected latency of a query operation, the system consolidates and reduces inputs to the semantic ranker.

Semantic ranking has three steps:

1. Collect and summarize inputs
2. Score results by using the semantic ranker
3. Output rescored results, captions, and answers

### How the system collects and summarizes inputs

In semantic ranking, the query subsystem passes search results as an input to summarization and ranking models. Because the ranking models have input size constraints and are processing intensive, search results must be sized and structured (summarized) for efficient handling.

1. The semantic ranker starts with a BM25-ranked result from a text query or an RRF-ranked result from a vector or hybrid query. The reranking exercise uses only text. Even if results include more than 50 results, only the top 50 results progress to semantic ranking. Typically, semantic ranking uses informational and descriptive fields.
2. For each document in the search result, the summarization model accepts up to 2,000 tokens, where a token is approximately 10 characters. The model assembles inputs from the "title", "keyword", and "content" fields listed in the semantic configuration.
3. The system trims excessively long strings to ensure the overall length meets the input requirements of the summarization step. This trimming exercise is why it's important to add fields to your semantic configuration in priority order.

    | Semantic field | Token limit |
    | --- | --- |
    | "title" | 128 tokens |
    | "keywords" | 128 tokens |
    | "content" | remaining tokens |

4. The summarization output is a summary string for each document, composed of the most relevant information from each field. The system sends summary strings to the ranker for scoring, and to machine reading comprehension models for captions and answers.

    As of November 2024, the maximum length of each generated summary string passed to the semantic ranker is 2,048 tokens. Previously, it was 256 tokens.

## How results are scored

The system scores results based on the caption and any other content from the summary string that fills out the 2,048 token length.

1. The system evaluates captions for conceptual and semantic relevance, relative to the query you provide.
2. The system assigns a **`@search.rerankerScore`** to each document based on the semantic relevance of the document for the given query. Scores range from 4 to 0 (high to low), where a higher score indicates higher relevance.

    | Score | Meaning |
    | --- | --- |
    | 4.0 | The document is highly relevant and answers the question completely. |
    | 3.0 | The document is relevant but lacks details that would make it complete. |
    | 2.0 | The document is somewhat relevant; it answers the question either partially or only addresses some aspects of the question. |
    | 1.0 | The document is related to the question, and it answers a small part of it. |
    | 0.0 | The document is irrelevant. |

3. The system lists matches in descending order by score and includes them in the query response payload. The payload includes answers, plain text and highlighted captions, and any fields that you marked as retrievable or specified in a select clause.

> Note: For any given query, the distributions of `@search.rerankerScore` can exhibit slight variations due to conditions at the infrastructure level. Ranking model updates can also affect the distribution. For these reasons, if you're writing custom code for minimum thresholds or setting the threshold property for vector and hybrid queries, don't make the limits too granular.

### Outputs of semantic ranker

From each summary string, the machine reading comprehension models find passages that are the most representative.

The outputs are:

- A **semantic caption** for the document. Each caption is available in a plain text version and a highlight version, and is frequently fewer than 200 words per document.
- An optional **semantic answer**, assuming you specified the `answers` parameter, the query was posed as a question, and a passage is found in the long string that provides a likely answer to the question.

Captions and answers are always verbatim text from your index. There's no generative AI model in this workflow that creates or composes new content.

## Semantic capabilities and limitations

What semantic ranker *can* do:

- Promote matches that are semantically closer to the intent of original query.
- Find strings to use as captions and answers. The response returns captions and answers, which you can render on a search results page.

What semantic ranker *can't* do is rerun the query over the entire corpus to find semantically relevant results. Semantic ranking reranks the existing result set, consisting of the top 50 results as scored by the default ranking algorithm. Furthermore, semantic ranker can't create new information or strings. The language models extract captions and answers verbatim from your content, so if the results don't include answer-like text, they won't produce one.

Although semantic ranking isn't beneficial in every scenario, certain content can benefit significantly from its capabilities. The language models in semantic ranker work best on searchable content that is information-rich and structured as prose. A knowledge base, online documentation, or documents that contain descriptive content see the most gains from semantic ranker capabilities.

## How semantic ranker uses synonym maps

If you enable support for synonym maps associated to a field in your search index, and include that field in the semantic ranker configuration, the semantic ranker automatically applies the configured synonyms during the reranking process.

## Availability and pricing

Semantic ranker is available in select regions. It can be used as a standalone feature or as a built-in component of agentic retrieval. Each feature is billed independently.

Semantic ranker offers a free plan (default) with a monthly free request allowance and a standard plan for pay-as-you-go pricing after the free allowance is consumed.

Charges for semantic ranker occur when query requests include `queryType=semantic` and the search string isn't empty (for example, `search=pet friendly hotels in New York`). If your search string is empty (`search=*`), you aren't charged, even if the queryType is set to semantic.

## How to get started

1. Check regional availability.
2. (Optional) Switch to the standard billing plan for usage beyond the free monthly quota.
3. Configure semantic ranker in a search index.
4. Set up queries to return semantic captions and highlights.
5. (Optional) Return semantic answers.
