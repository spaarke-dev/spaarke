---
source: https://learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization
fetched: 2026-05-14
---

# Integrated Vectorization Overview - Azure AI Search

Integrated vectorization is an extension of the indexing and query pipelines in Azure AI Search. It adds the following capabilities:

- Vector encoding during indexer-driven indexing
- Vector encoding during queries

Data chunking isn't a hard requirement, but unless your raw documents are small, chunking is necessary for meeting the token input requirements of embedding models.

Vector conversions are one-way: nonvector-to-vector. For example, there's no vector-to-text conversion for queries or results, such as converting a vector result to a human-readable string, which is why indexes contain both vector and nonvector fields.

Integrated vectorization speeds up the development and minimizes maintenance tasks during data ingestion and query time because there are fewer operations that you have to implement manually.

## Using integrated vectorization during indexing

For integrated data chunking and vector conversions, you're taking a dependency on the following components:

- **An indexer**, which retrieves raw data from a supported data source and drives the pipeline engine.
- **A search index** to receive the chunked and vectorized content.
- **A skillset** configured for:
    - A chunking strategy: **Text Split skill**, **Document Layout skill**, **Azure Content Understanding skill**, or one of the document parsing modes.
    - An embedding skill, used to generate vector arrays, which can be any of the following:
        - **AzureOpenAIEmbedding skill**, attached to `text-embedding-ada-002`, `text-embedding-3-small`, `text-embedding-3-large` on Azure OpenAI.
        - **Custom skill** that points to another embedding model on Azure or on another site.
        - **Azure Vision multimodal embeddings skill** (preview) that points to the multimodal API for Azure Vision.
        - **AML skill** that points to select models in the Microsoft Foundry model catalog.

## Using integrated vectorization in queries

For text-to-vector conversion during queries, you take a dependency on these components:

- A query that specifies one or more vector fields.
- A text string that's converted to a vector at query time.
- A **vectorizer**, defined in the index schema, assigned to a vector field, and used automatically at query time to convert a text query to a vector. The vectorizer you set up must match the embedding model used to encode your content.

| Embedding skill | Vectorizer |
| --- | --- |
| AzureOpenAIEmbedding skill | Azure OpenAI vectorizer |
| Custom skill | Custom Web API vectorizer |
| Azure Vision multimodal embeddings skill (preview) | Azure Vision vectorizer |
| AML skill pointing to the model catalog in Foundry portal | Microsoft Foundry model catalog vectorizer |

## Availability and pricing

Integrated vectorization is available in all regions and tiers. However, if you're using skills and vectorizers for AI enrichment, regional requirements might apply.

If you're using a custom skill and an Azure hosting mechanism (such as an Azure function app, Azure Web App, or Azure Kubernetes), check the Azure product by region page for feature availability.

Data chunking (Text Split skill) is free and available on all Foundry Tools in all regions.

> Note: Some older search services created before January 1, 2019 are deployed on infrastructure that doesn't support vector workloads. If you try to add a vector field to a schema and get an error, it's a result of outdated services. In this situation, you must create a new search service to try out the vector feature.

## What scenarios can integrated vectorization support?

- Subdivide large documents into chunks, useful for vector and nonvector scenarios. For vectors, chunks help you meet the input constraints of embedding models. For nonvector scenarios, you might have a chat-style search app where GPT is assembling responses from indexed chunks. You can use vectorized or nonvectorized chunks for chat-style search.
- Build a vector store where all of the fields are vector fields, and the document ID (required for a search index) is the only string field. Query the vector store to retrieve document IDs, and then send the document's vector fields to another model.
- Combine vector and text fields for hybrid search, with or without semantic ranking. Integrated vectorization simplifies all of the scenarios supported by vector search.

## How to use integrated vectorization

For query-only vectorization:

1. Add a vectorizer to an index. It should be the same embedding model used to generate vectors in the index.
2. Assign the vectorizer to a vector profile, and then assign a vector profile to the vector field.
3. Formulate a vector query that specifies the text string to vectorize.

A more common scenario - data chunking and vectorization during indexing:

1. Create a data source connection to a supported data source for indexer-based indexing.
2. Create a skillset that calls Text Split skill for chunking and Azure OpenAI Embedding or another embedding skill to vectorize the chunks.
3. Create an index that specifies a vectorizer for query time, and assign it to vector fields.
4. Create an indexer to drive everything, from data retrieval, to skillset execution, through indexing. We recommend running the indexer on a schedule to pick up changed documents or any documents that were missed due to throttling.

Optionally, create secondary indexes for advanced scenarios where chunked content is in one index, and nonchunked in another index. Chunked indexes (or secondary indexes) are useful for RAG apps.

### Secure connections to vectorizers and models

If your architecture requires private connections that bypass the internet, you can create a shared private link connection to the embedding models used by skills during indexing and vectorizers at query time.

Shared private links only work for Azure-to-Azure connections. If you're connecting to OpenAI or another external model, the connection must be over the public internet.

For vectorization scenarios, you would use:

- `openai_account` for embedding models hosted on an Azure OpenAI resource.
- `sites` for embedding models accessed as a custom skill or custom vectorizer. The `sites` group ID is for App services and Azure functions, which you could use to host an embedding model that isn't one of the Azure OpenAI embedding models.

## Benefits

- No separate data chunking and vectorization pipeline. Code is simpler to write and maintain.
- Automate indexing end-to-end. When data changes in the source (such as in Azure Storage, Azure SQL, or Cosmos DB), the indexer can move those updates through the entire pipeline, from retrieval, to document cracking, through optional AI-enrichment, data chunking, vectorization, and indexing.
- Batching and retry logic is built in (non-configurable). Azure AI Search has internal retry policies for throttling errors that surface due to the Azure OpenAI endpoint maxing out on token quotas for the embedding model. We recommend putting the indexer on a schedule (for example, every 5 minutes) so the indexer can process any calls that are throttled by the Azure OpenAI endpoint despite of the retry policies.
- Projecting chunked content to secondary indexes. Secondary indexes are created as you would any search index (a schema with fields and other constructs), but they're populated in tandem with a primary index by an indexer. Content from each source document flows to fields in primary and secondary indexes during the same indexing run.

    Secondary indexes are intended for question and answer or chat style apps. The secondary index contains granular information for more specific matches, but the parent index has more information and can often produce a more complete answer. When a match is found in the secondary index, the query returns the parent document from the primary index. For example, assuming a large PDF as a source document, the primary index might have basic information (title, date, author, description), while a secondary index has chunks of searchable content.

## Limitations

Make sure you know the Azure OpenAI quotas and limits for embedding models. Azure AI Search has retry policies, but if the quota is exhausted, retries fail.

Azure OpenAI token-per-minute limits are per model, per subscription. Keep this in mind if you're using an embedding model for both query and indexing workloads. If possible, follow best practices. Have an embedding model for each workload, and try to deploy them in different subscriptions.

On Azure AI Search, remember there are service limits by tier and workloads.
