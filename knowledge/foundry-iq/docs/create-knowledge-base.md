---
source: https://learn.microsoft.com/en-us/azure/search/agentic-retrieval-how-to-create-knowledge-base
canonical_source: https://learn.microsoft.com/en-us/azure/search/agentic-retrieval-how-to-create-knowledge-base
fetched: 2026-05-14
ms_date: 2026-04-24
ms_git_commit: 83eec1eacf37b8761d441357f468b41e15c49020
note: |
  Directive URL was https://learn.microsoft.com/en-us/azure/ai-foundry/agents/knowledge-base-create
  which 404s on 2026-05-14. The "create a knowledge base" how-to lives under
  Azure AI Search, not under ai-foundry/agents. The page is heavily zone-pivoted
  (Python / .NET / REST) and tab-pivoted (2025-11-01-preview / 2026-04-01) — this
  snapshot keeps the Python and REST samples plus the property tables.
---

# Create a knowledge base (Azure AI Search)

> **Note (Microsoft)**: This agentic retrieval feature is generally available in the
> **2026-04-01** REST API version via programmatic access. The Azure portal and Microsoft
> Foundry portal continue to provide preview-only access to all agentic retrieval features.

In Azure AI Search, a *knowledge base* is a top-level object that orchestrates agentic
retrieval. It defines which knowledge sources to query and the default behavior for
retrieval operations. At query time, the retrieve method targets the knowledge base to run
the configured retrieval pipeline.

You can create a knowledge base in a Foundry IQ workload in the Microsoft Foundry (new)
portal. You also need a knowledge base in any agentic solutions that you create using the
Azure AI Search APIs.

A knowledge base specifies:

- One or more knowledge sources that point to searchable content.
- An optional LLM for query planning, answer synthesis, or web content summarization.
  Supported tasks vary by API version and knowledge source type.
- Custom properties that control routing, source selection, and object encryption.

## Prerequisites

- Azure AI Search in any region that provides agentic retrieval. If you're using a managed
  identity for role-based access to deployed models, your search service must be on the
  Basic pricing tier or higher.
- One or more knowledge sources. If you plan to use the 2026-04-01 API version to create
  your knowledge base, your knowledge sources must be generally available.
- (Conditional) Azure OpenAI with a supported LLM deployment. For both the
  2025-11-01-preview and 2026-04-01 API versions, the LLM is **required if your knowledge
  base includes a web knowledge source**. For the 2025-11-01-preview only, the LLM is
  optional for all other knowledge source types. **2026-04-01 doesn't support an LLM for
  non-web knowledge sources.**
- Permission to create and use objects on Azure AI Search. We recommend role-based access.
  **Search Service Contributor** can create and manage a knowledge base. **Search Index
  Data Reader** can run queries.

### Supported models

Use one of the following LLMs from Azure OpenAI in Foundry Models:

`gpt-4o`, `gpt-4o-mini`, `gpt-4.1`, `gpt-4.1-mini`, `gpt-4.1-nano`, `gpt-5`, `gpt-5-mini`,
`gpt-5-nano`, `gpt-5.1`, `gpt-5.2`, `gpt-5.4`, `gpt-5.4-mini`, `gpt-5.4-nano`.

### Python package versions

- For 2025-11-01-preview features: `pip install azure-search-documents --pre`
- For 2026-04-01 features: `pip install azure-search-documents`

## Check for existing knowledge bases

```python
# List knowledge bases by name
from azure.core.credentials import AzureKeyCredential
from azure.search.documents.indexes import SearchIndexClient

index_client = SearchIndexClient(endpoint="search_url", credential=AzureKeyCredential("api_key"))

for kb in index_client.list_knowledge_bases():
    print(f"  - {kb.name}")
```

```python
# Get a knowledge base definition
import json

kb = index_client.get_knowledge_base("knowledge_base_name")
print(json.dumps(kb.as_dict(), indent=2))
```

Example response:

```json
{
  "name": "my-kb",
  "description": "A sample knowledge base.",
  "retrievalInstructions": null,
  "answerInstructions": null,
  "outputMode": null,
  "knowledgeSources": [{ "name": "my-blob-ks" }],
  "models": [],
  "encryptionKey": null,
  "retrievalReasoningEffort": { "kind": "low" }
}
```

> The response schema reflects the API version you used to create the knowledge base. A
> knowledge base created with the generally available 2026-04-01 API version returns a
> narrower definition than the 2025-11-01-preview.

## Create a knowledge base — Python (2025-11-01-preview)

```python
# Create a knowledge base
from azure.core.credentials import AzureKeyCredential
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import (
    KnowledgeBase,
    KnowledgeBaseAzureOpenAIModel,
    KnowledgeSourceReference,
    AzureOpenAIVectorizerParameters,
    KnowledgeRetrievalOutputMode,
    KnowledgeRetrievalLowReasoningEffort,
)

index_client = SearchIndexClient(endpoint="search_url", credential=AzureKeyCredential("api_key"))

aoai_params = AzureOpenAIVectorizerParameters(
    resource_url="aoai_endpoint",
    api_key="aoai_api_key",
    deployment_name="aoai_gpt_deployment",
    model_name="aoai_gpt_model",
)

knowledge_base = KnowledgeBase(
    name="my-kb",
    description="This knowledge base handles questions directed at two unrelated sample indexes.",
    retrieval_instructions=(
        "Use the hotels knowledge source for queries about where to stay, "
        "otherwise use the earth at night knowledge source."
    ),
    answer_instructions="Provide a two sentence concise and informative answer based on the retrieved documents.",
    output_mode=KnowledgeRetrievalOutputMode.ANSWER_SYNTHESIS,
    knowledge_sources=[
        KnowledgeSourceReference(name="hotels-ks"),
        KnowledgeSourceReference(name="earth-at-night-ks"),
    ],
    models=[KnowledgeBaseAzureOpenAIModel(azure_open_ai_parameters=aoai_params)],
    encryption_key=None,
    retrieval_reasoning_effort=KnowledgeRetrievalLowReasoningEffort(),
)

index_client.create_or_update_knowledge_base(knowledge_base)
```

## Create a knowledge base — Python (2026-04-01 GA)

```python
# Create a knowledge base — 2026-04-01 (GA)
from azure.core.credentials import AzureKeyCredential
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import KnowledgeBase, KnowledgeSourceReference

index_client = SearchIndexClient(endpoint="search_url", credential=AzureKeyCredential("api_key"))

knowledge_base = KnowledgeBase(
    name="my-kb",
    description="This knowledge base handles questions directed at two unrelated sample indexes.",
    knowledge_sources=[
        KnowledgeSourceReference(name="hotels-ks"),
        KnowledgeSourceReference(name="earth-at-night-ks"),
    ],
    encryption_key=None,
)

index_client.create_or_update_knowledge_base(knowledge_base)
```

## Create a knowledge base — REST (2025-11-01-preview)

```http
PUT {{search-url}}/knowledgebases/{{knowledge-base-name}}?api-version=2025-11-01-preview
Content-Type: application/json
api-key: {{search-api-key}}

{
  "name": "my-kb",
  "description": "This knowledge base handles questions directed at two unrelated sample indexes.",
  "retrievalInstructions": "Use the hotels knowledge source for queries about where to stay, otherwise use the earth at night knowledge source.",
  "answerInstructions": null,
  "outputMode": "answerSynthesis",
  "knowledgeSources": [
    { "name": "hotels-ks" },
    { "name": "earth-at-night-ks" }
  ],
  "models": [
    {
      "kind": "azureOpenAI",
      "azureOpenAIParameters": {
        "resourceUri": "{{model-provider-url}}",
        "apiKey": "{{model-api-key}}",
        "deploymentId": "gpt-4.1-mini",
        "modelName": "gpt-4.1-mini"
      }
    }
  ],
  "encryptionKey": null,
  "retrievalReasoningEffort": { "kind": "low" }
}
```

> **Important**: The 2026-04-01 API version only accepts generally available knowledge
> source types and supports minimal, extractive retrieval. Preview-only capabilities, such
> as query planning, answer synthesis, and configurable reasoning effort, aren't supported.
> **For full functionality, use the 2025-11-01-preview.**

## Knowledge base properties (2025-11-01-preview, Python)

| Name | Description | Type | Required |
| --- | --- | --- | --- |
| `name` | The name of the knowledge base. Must be unique within the knowledge bases collection. | String | Yes |
| `description` | A description of the knowledge base. The LLM uses the description to inform query planning. | String | No |
| `retrieval_instructions` | A prompt for the LLM to determine whether a knowledge source should be in scope for a query. Include this when you have multiple knowledge sources. Influences both knowledge source selection and query formulation. | String | No |
| `answer_instructions` | Custom instructions to shape synthesized answers. Default is null. | String | No |
| `output_mode` | Valid values are `answer_synthesis` (LLM-formulated answer) or `extracted_data` (full results passed to an LLM downstream). | String | No |
| `knowledge_sources` | One or more supported knowledge sources. | Array | Yes |
| `models` | Required for web knowledge sources. Optional for other types. Specifies a supported LLM for query planning or answer synthesis. | Array | No |
| `encryption_key` | A customer-managed key. | Object | No |
| `retrieval_reasoning_effort` | Level of LLM-related query processing. Valid values: `minimal`, `low` (default), `medium`. | Object | No |

## Knowledge base properties (2026-04-01 GA, Python)

| Name | Description | Type | Required |
| --- | --- | --- | --- |
| `name` | The name of the knowledge base. | String | Yes |
| `description` | A description of the knowledge base. | String | No |
| `knowledge_sources` | One or more **generally available** knowledge source types. | Array | Yes |
| `models` | Required for web knowledge sources. Specifies a supported LLM used to summarize/preprocess web content. | Array | No |
| `encryption_key` | A customer-managed key. | Object | No |

## Delete a knowledge base

```python
index_client.delete_knowledge_base("knowledge_base_name")
```
