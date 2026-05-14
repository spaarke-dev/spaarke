"""Shared utilities for issuing knowledge base queries.

This module centralizes the KnowledgeBaseRetrievalClient setup so both the
console app and the web app can reuse the same session-oriented client and
response-shaping logic.
"""

from __future__ import annotations

import json
import os
import time
from functools import lru_cache
from typing import Any, Dict, List, Optional, Tuple

from dotenv import load_dotenv
from azure.core.credentials import AzureKeyCredential
from azure.search.documents.knowledgebases import KnowledgeBaseRetrievalClient
from azure.search.documents.knowledgebases.models import (
    KnowledgeBaseMessage,
    KnowledgeBaseMessageTextContent,
    KnowledgeBaseRetrievalRequest,
    KnowledgeRetrievalLowReasoningEffort,
    KnowledgeRetrievalMediumReasoningEffort,
    KnowledgeRetrievalMinimalReasoningEffort,
    KnowledgeRetrievalOutputMode,
    SearchIndexKnowledgeSourceParams,
)

load_dotenv()


class KBConfigurationError(RuntimeError):
    """Raised when mandatory configuration is missing."""


@lru_cache(maxsize=1)
def _load_settings() -> Dict[str, Any]:
    search_url = os.getenv("search_url")
    api_key = os.getenv("search_api_key")
    if not search_url or not api_key:
        raise KBConfigurationError(
            "Both 'search_url' and 'search_api_key' must be configured in the environment."
        )

    return {
        "search_url": search_url,
        "api_key": api_key,
        "knowledge_base_name": os.getenv("knowledge_base_name", "kb-phonepe-insurance"),
        "index_insurance": os.getenv("index_insurance", "ks-phonepe-insurance"),
        "index_retail": os.getenv("index_retail", "contoso-retail-index"),
        "index_gaming": os.getenv("index_gaming", "contoso-gaming-index"),
        "index_financials": os.getenv("index_financials", "knowledgesource-1nykaa-financials"),
    }


@lru_cache(maxsize=1)
def _get_kb_client() -> KnowledgeBaseRetrievalClient:
    settings = _load_settings()
    return KnowledgeBaseRetrievalClient(
        endpoint=settings["search_url"],
        knowledge_base_name=settings["knowledge_base_name"],
        credential=AzureKeyCredential(settings["api_key"]),
    )


_REASONING_FACTORIES: Dict[str, Any] = {
    "minimal": KnowledgeRetrievalMinimalReasoningEffort,
    "low": KnowledgeRetrievalLowReasoningEffort,
    "medium": KnowledgeRetrievalMediumReasoningEffort,
}

_REASONING_CANONICAL: Dict[str, str] = {key: key for key in _REASONING_FACTORIES}

_OUTPUT_MODE_MAP: Dict[str, Tuple[str, KnowledgeRetrievalOutputMode]] = {
    "extractivedata": ("extractiveData", KnowledgeRetrievalOutputMode.EXTRACTIVE_DATA),
    "answersynthesis": ("answerSynthesis", KnowledgeRetrievalOutputMode.ANSWER_SYNTHESIS),
}


def _normalize_reasoning_choice(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None

    normalized = value.strip().lower()
    if not normalized:
        return None

    if normalized not in _REASONING_FACTORIES:
        raise ValueError(
            "retrievalReasoningEffort must be one of: minimal, low, or medium."
        )
    return _REASONING_CANONICAL[normalized]


def _normalize_output_mode(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None

    normalized = value.strip().lower()
    if not normalized:
        return None

    if normalized not in _OUTPUT_MODE_MAP:
        raise ValueError(
            "knowledgeRetrievalOutputMode must be either extractiveData or answerSynthesis."
        )

    return _OUTPUT_MODE_MAP[normalized][0]


def _build_request(
    question: str,
    reasoning_choice: Optional[str] = None,
    output_mode_choice: Optional[str] = None,
    query_mode: str = "per-source",
) -> KnowledgeBaseRetrievalRequest:
    settings = _load_settings()
    
    request_kwargs: Dict[str, Any] = {
        "messages": [
            KnowledgeBaseMessage(
                role="user",
                content=[KnowledgeBaseMessageTextContent(text=question.strip())],
            )
        ],
        "include_activity": True,
    }

    # Build request based on query mode
    if query_mode == "per-source":
        # Original approach: specify per-source parameters with references
        source_params = [
            SearchIndexKnowledgeSourceParams(
                knowledge_source_name=settings["index_insurance"],
                include_references=True,
                include_reference_source_data=True,
                always_query_source=False,
            ),
            SearchIndexKnowledgeSourceParams(
                knowledge_source_name=settings["index_retail"],
                include_references=True,
                include_reference_source_data=True,
                always_query_source=False,
            ),
            SearchIndexKnowledgeSourceParams(
                knowledge_source_name=settings["index_gaming"],
                include_references=True,
                include_reference_source_data=True,
                always_query_source=False,
            ),
            SearchIndexKnowledgeSourceParams(
                knowledge_source_name=settings["index_financials"],
                include_references=True,
                include_reference_source_data=True,
                always_query_source=False,
            ),
        ]
        request_kwargs["knowledge_source_params"] = source_params
    else:
        # KB-level approach: override default reasoning effort at request level
        # No per-source params needed - uses KB defaults
        # Set request limits for KB-level override
        request_kwargs["max_runtime_in_seconds"] = 30
        request_kwargs["max_output_size"] = 6000

    if reasoning_choice:
        request_kwargs["retrieval_reasoning_effort"] = _REASONING_FACTORIES[
            reasoning_choice
        ]()

    if output_mode_choice:
        request_kwargs["output_mode"] = _OUTPUT_MODE_MAP[
            output_mode_choice.lower() if output_mode_choice else ""
        ][1]

    return KnowledgeBaseRetrievalRequest(**request_kwargs)


def _get_web_reference_indices(result: Any) -> set:
    """Get the set of reference indices that are web sources."""
    web_indices = set()
    references = getattr(result, "references", None)
    if references:
        for idx, reference in enumerate(references):
            source_type = getattr(reference, "type", "unknown")
            if source_type == "web":
                web_indices.add(idx)
    return web_indices


def _remove_web_citation_markers(text: str, web_indices: set) -> str:
    """Remove citation markers for web sources from the text."""
    import re
    
    # Pattern to match citation markers like [ref_id:0], [ref_id:1], etc.
    def replace_citation(match):
        ref_id = int(match.group(1))
        # Remove the marker if it's a web reference
        if ref_id in web_indices:
            return ""
        return match.group(0)
    
    # Replace citation markers
    cleaned = re.sub(r'\[ref_id:(\d+)\]', replace_citation, text)
    
    # Clean up any double spaces left by removal
    cleaned = re.sub(r'  +', ' ', cleaned)
    
    # Clean up any spaces before punctuation
    cleaned = re.sub(r' +([.,;:!?])', r'\1', cleaned)
    
    return cleaned.strip()


def _extract_answer_texts(result: Any) -> List[str]:
    texts: List[str] = []
    web_indices = _get_web_reference_indices(result)
    
    if getattr(result, "response", None):
        for response_item in result.response:
            if getattr(response_item, "content", None):
                parts: List[str] = []
                for content_item in response_item.content:
                    text_value = getattr(content_item, "text", None)
                    if text_value:
                        # Remove web citation markers from the text
                        cleaned_text = _remove_web_citation_markers(text_value.strip(), web_indices)
                        parts.append(cleaned_text)
                if parts:
                    texts.append("\n\n".join(parts))
    return texts


def _clean_content(content: Optional[str]) -> Optional[str]:
    if not content:
        return None
    return content.replace("\r\n", "\n").replace("\t", "  ").strip()


def _format_reference(idx: int, reference: Any) -> Dict[str, Any]:
    source_type = getattr(reference, "type", "unknown")
    formatted: Dict[str, Any] = {
        "id": idx,
        "type": source_type,
        "title": None,
        "url": None,
        "citationText": None,
        "note": None,
        "document": None,
        "relevanceScore": getattr(reference, "reranker_score", None),
    }

    source_data = getattr(reference, "source_data", None)
    additional_props = getattr(reference, "additional_properties", None)
    
    # Debug logging for azureBlob type
    if source_type == "azureBlob":
        print(f"\n{'='*80}")
        print(f"DEBUG: Azure Blob Reference #{idx}")
        print(f"{'='*80}")
        print(f"Source Type: {source_type}")
        print(f"\nReference object attributes:")
        print(f"  - dir(reference): {[attr for attr in dir(reference) if not attr.startswith('_')]}")
        
        # Print actual attribute values
        print(f"\nAttribute Values:")
        print(f"  - id: {getattr(reference, 'id', None)}")
        print(f"  - blob_url: {getattr(reference, 'blob_url', None)}")
        print(f"  - reranker_score: {getattr(reference, 'reranker_score', None)}")
        print(f"  - activity_source: {getattr(reference, 'activity_source', None)}")
        
        print(f"\nSource Data:")
        if source_data:
            print(f"  Type: {type(source_data)}")
            if isinstance(source_data, dict):
                print(f"  Content: {json.dumps(source_data, indent=2)}")
            else:
                print(f"  Content: {source_data}")
                print(f"  Attributes: {[attr for attr in dir(source_data) if not attr.startswith('_')]}")
        else:
            print(f"  source_data is None")
        print(f"\nAdditional Properties:")
        if additional_props:
            print(f"  Type: {type(additional_props)}")
            if isinstance(additional_props, dict):
                print(f"  Content: {json.dumps(additional_props, indent=2)}")
            else:
                print(f"  Content: {additional_props}")
        else:
            print(f"  additional_properties is None")
        
        # Try as_dict() method to see all data
        print(f"\nFull reference.as_dict():")
        try:
            ref_dict = reference.as_dict()
            print(f"{json.dumps(ref_dict, indent=2)}")
        except Exception as e:
            print(f"  Error calling as_dict(): {e}")
        
        print(f"{'='*80}\n")

    if source_type == "web":
        if isinstance(source_data, dict):
            formatted["title"] = source_data.get("name", "Web Source")
            formatted["url"] = source_data.get("url")
            formatted["note"] = (
                source_data.get("snippet")
                or "Web Knowledge Source references omit extractive snippets by design."
            )
        else:
            formatted["note"] = "Web Knowledge Source references omit extractive snippets by design."
    elif source_type == "searchIndex":
        if isinstance(additional_props, dict):
            formatted["document"] = additional_props.get("title")
            if not formatted["title"]:
                formatted["title"] = additional_props.get("title")
        if isinstance(source_data, dict):
            formatted["title"] = formatted["title"] or source_data.get("title")
            formatted["url"] = source_data.get("url")
            formatted["citationText"] = _clean_content(source_data.get("content"))
    else:
        if isinstance(source_data, dict):
            formatted["note"] = str(source_data)

    return formatted


def _format_references(result: Any) -> List[Dict[str, Any]]:
    formatted: List[Dict[str, Any]] = []
    references = getattr(result, "references", None)
    if not references:
        return formatted

    # Build a list of valid (non-web) references
    valid_references: List[Tuple[int, Any]] = []
    for idx, reference in enumerate(references):
        source_type = getattr(reference, "type", "unknown")
        if source_type != "web":
            valid_references.append((idx, reference))
    
    # Format only the valid references with re-indexed IDs
    for new_idx, (original_idx, reference) in enumerate(valid_references):
        formatted.append(_format_reference(new_idx, reference))
    
    return formatted


def execute_kb_query(
    question: str,
    retrieval_reasoning_effort: Optional[str] = None,
    output_mode: Optional[str] = None,
    query_mode: str = "per-source",
) -> Dict[str, Any]:
    """Execute a single KB query and return structured data for UI layers.
    
    Args:
        question: The question to ask the knowledge base
        retrieval_reasoning_effort: Optional reasoning effort level (minimal, low, medium)
        output_mode: Optional output mode (extractiveData, answerSynthesis)
        query_mode: Query mode - either "per-source" (specify params per knowledge source)
                   or "kb-level" (override KB defaults at request level)
    """

    if not question or not question.strip():
        raise ValueError("Question text is required.")

    reasoning_choice = _normalize_reasoning_choice(retrieval_reasoning_effort)
    output_mode_choice = _normalize_output_mode(output_mode)

    request_timing_start = time.perf_counter()
    request = _build_request(question, reasoning_choice, output_mode_choice, query_mode)
    request_prep_time = time.perf_counter() - request_timing_start

    retrieval_start = time.perf_counter()
    result = _get_kb_client().retrieve(request)
    retrieval_time = time.perf_counter() - retrieval_start

    processing_start = time.perf_counter()
    answers = _extract_answer_texts(result)
    citations = _format_references(result)
    processing_time = time.perf_counter() - processing_start

    total_time = request_prep_time + retrieval_time + processing_time
    settings = _load_settings()

    return {
        "question": question.strip(),
        "answers": answers,
        "citations": citations,
        "timing": {
            "total": total_time,
            "requestPreparation": request_prep_time,
            "kbRetrieval": retrieval_time,
            "responseProcessing": processing_time,
        },
        "metadata": {
            "knowledgeBaseName": settings["knowledge_base_name"],
            "searchEndpoint": settings["search_url"],
            "queryMode": query_mode,
            "requestOverrides": {
                "retrievalReasoningEffort": reasoning_choice,
                "knowledgeRetrievalOutputMode": output_mode_choice,
            },
        },
        "activity": getattr(result, "activity", None),
    }


def get_kb_configuration() -> Dict[str, Any]:
    """Expose key configuration values for UI layers."""

    settings = _load_settings()
    return {
        "searchEndpoint": settings["search_url"],
        "knowledgeBaseName": settings["knowledge_base_name"],
        "indexes": [
            settings["index_insurance"],
            settings["index_retail"],
            settings["index_gaming"],
            settings["index_financials"],
        ],
    }