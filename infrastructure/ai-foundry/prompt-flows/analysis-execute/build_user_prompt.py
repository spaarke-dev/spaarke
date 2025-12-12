"""
Build user prompt with document content and knowledge context.
"""
from promptflow.core import tool


@tool
def build_user_prompt(
    document_text: str,
    knowledge_context: str
) -> str:
    """
    Builds the user prompt for document analysis.

    Args:
        document_text: The extracted text from the document to analyze
        knowledge_context: Inline knowledge or RAG-retrieved context

    Returns:
        Complete user prompt string
    """
    parts = []

    # Add document section
    parts.append("# Document to Analyze\n")
    parts.append(document_text)
    parts.append("")

    # Add knowledge context if provided
    if knowledge_context and knowledge_context.strip():
        parts.append("\n# Reference Materials\n")
        parts.append(knowledge_context.strip())
        parts.append("")

    # Add instruction
    parts.append("\n---\n")
    parts.append("Please analyze the document above according to the instructions provided.")

    return "\n".join(parts)
