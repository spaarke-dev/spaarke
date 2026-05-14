"""Create or Update a Knowledge Base for Agentic Retrieval.

This script creates a knowledge base that orchestrates multiple knowledge sources
for agentic retrieval. The knowledge base connects to Azure OpenAI for answer
synthesis and query planning.

A knowledge base:
- References one or more knowledge sources (search indexes, web search, etc.)
- Connects to an Azure OpenAI LLM for reasoning and answer generation
- Defines retrieval instructions to help route queries to appropriate sources
- Configures answer synthesis and output format

Environment Variables Required:
    search_url: Azure AI Search service endpoint
    search_api_key: API key for Azure AI Search
    index_insurance: Name of the insurance FAQ index
    index_retail: Name of the retail index
    index_gaming: Name of the gaming index
    index_financials: Name of the Nykaa financials index
    az-openai_endpoint: Azure OpenAI endpoint URL
    az-openai-deployment: Azure OpenAI deployment name
    az-openai-model: Azure OpenAI model name (e.g., gpt-4o)
    az-openai-key: Azure OpenAI API key

Prerequisites:
    - Knowledge sources must be created first (run create_knowledge_sources.py)
    - Azure OpenAI deployment must be available
    - Semantic ranker must be enabled on the search service

Usage:
    python create_kb.py

Output:
    Creates or updates the 'contoso-multi-index-kb' knowledge base with:
    - 4 search index knowledge sources
    - 1 web search knowledge source
    - Azure OpenAI model configuration
"""

import os
from dotenv import load_dotenv
from azure.core.credentials import AzureKeyCredential
from azure.identity import DefaultAzureCredential
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import (
    KnowledgeBase,
    KnowledgeBaseAzureOpenAIModel,
    KnowledgeSourceReference,
    AzureOpenAIVectorizerParameters,
    KnowledgeRetrievalOutputMode,
    KnowledgeRetrievalLowReasoningEffort
)

# Load environment variables from .env file
load_dotenv()

# Get configuration from environment variables
search_url = os.getenv("search_url")
search_api_key = os.getenv("search_api_key")
index_insurance = os.getenv("index_insurance", "contoso-insurance-faq-index")
index_retail = os.getenv("index_retail", "contoso-retail-index")
index_gaming = os.getenv("index_gaming", "contoso-gaming-index")
index_financials = os.getenv("index_financials", "nykaa-financials-indexer")
aoai_endpoint = os.getenv("az-openai_endpoint")
aoai_deployment = os.getenv("az-openai-deployment")
aoai_model = os.getenv("az-openai-model")
aoai_api_key = os.getenv("az-openai-key")

# Use API key for search authentication
search_credential = AzureKeyCredential(search_api_key)

# Print connection info for debugging
print(f"Connecting to: {search_url}")
print(f"Using Azure OpenAI endpoint: {aoai_endpoint}")
print(f"Using deployment: {aoai_deployment}, model: {aoai_model}\n")

index_client = SearchIndexClient(endpoint=search_url, credential=search_credential)

aoai_params = AzureOpenAIVectorizerParameters(
    resource_url=aoai_endpoint,
    deployment_name=aoai_deployment,
    model_name=aoai_model,
    api_key=aoai_api_key
)

knowledge_base = KnowledgeBase(
    name = "contoso-multi-index-kb",
    description = "This knowledge base handles questions directed at four sample indexes and includes Bing web search for real-time information.",
    retrieval_instructions = (
        f"Use the {index_insurance} knowledge source for queries about Contoso Insurance policies, "
        f"use the {index_retail} knowledge source for queries about Contoso Retail, "
        f"use the {index_gaming} knowledge source for queries about Contoso Gaming, "
        f"use the {index_financials} knowledge source for queries about Nykaa's financial performance, "
        f"and use bing-web-search-ks for current events and real-time web information."
    ),
    answer_instructions = "Provide a two sentence concise and informative answer based on the retrieved documents.",
    output_mode = KnowledgeRetrievalOutputMode.ANSWER_SYNTHESIS,
    knowledge_sources = [
        KnowledgeSourceReference(name = index_insurance),
        KnowledgeSourceReference(name = index_retail),
        KnowledgeSourceReference(name = index_gaming),
        KnowledgeSourceReference(name = index_financials),
        KnowledgeSourceReference(name = "bing-web-search-ks"),
    ],
    models = [KnowledgeBaseAzureOpenAIModel(azure_open_ai_parameters = aoai_params)],
    encryption_key = None,
    retrieval_reasoning_effort = KnowledgeRetrievalLowReasoningEffort,
)

index_client.create_or_update_knowledge_base(knowledge_base)
print(f"Knowledge base '{knowledge_base.name}' created or updated successfully.")