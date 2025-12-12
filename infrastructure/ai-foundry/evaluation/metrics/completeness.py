"""
Custom metric: Completeness
Checks if the analysis output contains all required sections based on the action type.
"""
import re
from promptflow.core import tool


# Expected sections by action type
ACTION_SECTIONS = {
    "summarize": ["summary", "key points", "conclusion"],
    "review_agreement": ["parties", "terms", "obligations", "risks", "recommendations"],
    "extract_entities": ["entities", "relationships"],
    "classify_document": ["classification", "confidence", "reasoning"],
    "default": ["summary", "analysis", "conclusion"]
}


@tool
def completeness(
    output: str,
    action_type: str = "default"
) -> float:
    """
    Evaluates if the analysis output contains all expected sections.

    Args:
        output: The analysis output to evaluate
        action_type: The type of analysis action performed

    Returns:
        Score between 0 and 1 indicating completeness
    """
    if not output or not output.strip():
        return 0.0

    # Get expected sections for this action type
    expected_sections = ACTION_SECTIONS.get(
        action_type.lower().replace(" ", "_"),
        ACTION_SECTIONS["default"]
    )

    output_lower = output.lower()
    found_sections = 0

    for section in expected_sections:
        # Check if section appears as heading or in content
        # Look for heading patterns like "## Summary" or "**Summary**" or "Summary:"
        patterns = [
            rf'#{1,3}\s*{section}',  # Markdown heading
            rf'\*\*{section}\*\*',    # Bold text
            rf'{section}:',           # Label with colon
            rf'\b{section}\b',        # Word boundary match
        ]

        for pattern in patterns:
            if re.search(pattern, output_lower, re.IGNORECASE):
                found_sections += 1
                break

    # Calculate completeness score
    if len(expected_sections) == 0:
        return 1.0

    return found_sections / len(expected_sections)
