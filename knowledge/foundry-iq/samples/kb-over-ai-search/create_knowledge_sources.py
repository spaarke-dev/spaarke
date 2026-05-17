"""Create Knowledge Sources for Azure AI Search.

This script creates knowledge sources that will be referenced by a knowledge base.
It creates two types of knowledge sources:
1. Search Index Knowledge Sources - Point to existing Azure AI Search indexes
2. Web Knowledge Source - Provides real-time web search via Bing

Knowledge sources are reusable objects that define where to search for information.
They must be created before being referenced in a knowledge base.

Environment Variables Required:
    search_url: Azure AI Search service endpoint
    search_api_key: API key for Azure AI Search
    index_insurance: Name of the insurance FAQ index (optional)
    index_retail: Name of the retail index (optional)
    index_gaming: Name of the gaming index (optional)
    index_financials: Name of the Nykaa financials index (optional)

Usage:
    python create_knowledge_sources.py

Output:
    Creates 5 knowledge sources:
    - Insurance FAQ knowledge source
    - Retail knowledge source
    - Gaming knowledge source
    - Nykaa financials knowledge source
    - Bing web search knowledge source
"""

import os
from dotenv import load_dotenv
from azure.core.credentials import AzureKeyCredential
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import (
    SearchIndexKnowledgeSource, 
    SearchIndexKnowledgeSourceParameters,
    WebKnowledgeSource,
    WebKnowledgeSourceParameters
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

# Use API key for search authentication
search_credential = AzureKeyCredential(search_api_key)

# Create search index client
index_client = SearchIndexClient(endpoint=search_url, credential=search_credential)

print(f"Creating knowledge sources in: {search_url}\n")

# Define the search index knowledge sources to create
search_index_sources = [
    {
        "name": index_insurance,
        "index_name": index_insurance,
        "description": "Knowledge source for Contoso Insurance FAQ"
    },
    {
        "name": index_retail,
        "index_name": index_retail,
        "description": "Knowledge source for Contoso Retail"
    },
    {
        "name": index_gaming,
        "index_name": index_gaming,
        "description": "Knowledge source for Contoso Gaming"
    },
    {
        "name": index_financials,
        "index_name": index_financials,
        "description": "Knowledge source for Nykaa financial performance data"
    }
]

# Create search index knowledge sources
print("Creating search index knowledge sources...")
for ks_info in search_index_sources:
    try:
        # Create SearchIndexKnowledgeSource with parameters
        ks_params = SearchIndexKnowledgeSourceParameters(search_index_name=ks_info["index_name"])
        
        knowledge_source = SearchIndexKnowledgeSource(
            name=ks_info["name"],
            description=ks_info["description"],
            search_index_parameters=ks_params
        )
        
        result = index_client.create_or_update_knowledge_source(knowledge_source)
        print(f"  ✓ Knowledge source '{ks_info['name']}' created successfully")
    except Exception as e:
        print(f"  ✗ Error creating knowledge source '{ks_info['name']}': {str(e)}")

# Create web knowledge source for Bing search
print("\nCreating web knowledge source...")
try:
    # Create Web Knowledge Source without domain restrictions (searches entire web)
    web_knowledge_source = WebKnowledgeSource(
        name="bing-web-search-ks",
        description="Bing web search knowledge source for real-time web data",
        encryption_key=None,
        web_parameters=WebKnowledgeSourceParameters(
            domains=None  # Set to None for unrestricted Bing search
            # To restrict to specific domains, use:
            # domains=WebKnowledgeSourceDomains(
            #     allowed_domains=[{"address": "learn.microsoft.com", "include_subpages": True}],
            #     blocked_domains=[{"address": "bing.com", "include_subpages": False}]
            # )
        )
    )
    
    result = index_client.create_or_update_knowledge_source(web_knowledge_source)
    print(f"  ✓ Web knowledge source '{web_knowledge_source.name}' created successfully")
    print("     Note: This knowledge source searches the entire public internet via Bing.")
except Exception as e:
    print(f"  ✗ Error creating web knowledge source: {str(e)}")

print("\n" + "="*80)
print("Knowledge sources creation completed!")
print("="*80)
print("\nNext step: Run 'python ops/create_kb.py' to create the knowledge base.")
