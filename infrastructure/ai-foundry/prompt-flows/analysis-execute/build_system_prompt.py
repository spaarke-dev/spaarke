"""
Build system prompt by combining action prompt with skills instructions.
"""
from promptflow.core import tool


@tool
def build_system_prompt(
    action_system_prompt: str,
    skills_instructions: str,
    output_format: str
) -> str:
    """
    Builds the system prompt for document analysis.

    Args:
        action_system_prompt: The base system prompt from the analysis action
        skills_instructions: Combined instructions from selected skills
        output_format: The requested output format (markdown, structured_json)

    Returns:
        Complete system prompt string
    """
    parts = [action_system_prompt]

    # Add skills as instructions section
    if skills_instructions and skills_instructions.strip():
        parts.append("\n## Instructions\n")
        for line in skills_instructions.strip().split("\n"):
            if line.strip():
                parts.append(f"- {line.strip()}")
        parts.append("")

    # Add output format instruction
    parts.append("\n## Output Format\n")
    if output_format == "structured_json":
        parts.append(
            "Provide your analysis as a valid JSON object with appropriate keys "
            "for the analysis type. Include 'summary', 'key_findings', and 'details' fields."
        )
    else:
        parts.append(
            "Provide your analysis in Markdown format with appropriate headings and structure. "
            "Use clear, professional language suitable for business communication."
        )

    return "\n".join(parts)
