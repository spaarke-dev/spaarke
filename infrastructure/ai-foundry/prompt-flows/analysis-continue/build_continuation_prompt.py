"""
Build continuation prompt with chat history and current working document.
"""
from typing import List, Dict, Any
from promptflow.core import tool


@tool
def build_continuation_prompt(
    working_document: str,
    chat_history: List[Dict[str, Any]],
    user_message: str,
    max_history_messages: int
) -> Dict[str, str]:
    """
    Builds the continuation prompt for analysis refinement.

    Args:
        working_document: The current working document (analysis result)
        chat_history: List of previous messages with role, content, timestamp
        user_message: The new user message requesting refinement
        max_history_messages: Maximum number of history messages to include

    Returns:
        Dictionary with system_prompt and messages for chat completion
    """
    parts = []

    # System prompt for continuation
    system_prompt = """You are an AI assistant helping refine and improve a document analysis.
You have access to the current working document and conversation history.
When the user requests changes, provide the COMPLETE updated analysis, not just the changes.
Maintain the same structure and format as the original analysis unless asked to change it.
Be concise but thorough in your responses."""

    # Build the context with working document
    parts.append("# Current Analysis\n")
    parts.append(working_document)
    parts.append("")

    # Add conversation history (respect limit, most recent first then reverse)
    if chat_history:
        # Sort by timestamp descending, take most recent, then reverse for chronological order
        sorted_history = sorted(
            chat_history,
            key=lambda x: x.get("timestamp", ""),
            reverse=True
        )[:max_history_messages]
        sorted_history.reverse()

        if sorted_history:
            parts.append("\n# Conversation History\n")
            for msg in sorted_history:
                role_label = "User" if msg.get("role") == "user" else "Assistant"
                parts.append(f"{role_label}: {msg.get('content', '')}")
                parts.append("")

    # Add new request
    parts.append("\n# New Request\n")
    parts.append(f"User: {user_message}")
    parts.append("")
    parts.append(
        "Please update the analysis based on this feedback. "
        "Provide the complete updated analysis, not just the changes."
    )

    return {
        "system_prompt": system_prompt,
        "user_prompt": "\n".join(parts)
    }
