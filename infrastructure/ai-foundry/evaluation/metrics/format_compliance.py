"""
Custom metric: Format Compliance
Checks if the analysis output follows the requested format (markdown or JSON).
"""
import json
import re
from promptflow.core import tool


@tool
def format_compliance(output: str, expected_format: str = "markdown") -> float:
    """
    Evaluates if the output complies with the expected format.

    Args:
        output: The analysis output to evaluate
        expected_format: Expected format ("markdown" or "json")

    Returns:
        Score between 0 and 1 indicating format compliance
    """
    if not output or not output.strip():
        return 0.0

    if expected_format == "json":
        return _check_json_format(output)
    else:
        return _check_markdown_format(output)


def _check_json_format(output: str) -> float:
    """Check if output is valid JSON with expected structure."""
    try:
        data = json.loads(output.strip())

        # Check for expected fields
        expected_fields = ["summary", "key_findings", "details"]
        present_fields = sum(1 for f in expected_fields if f in data)

        # Base score: valid JSON = 0.5, + 0.5 for having expected fields
        field_score = present_fields / len(expected_fields)
        return 0.5 + (0.5 * field_score)

    except json.JSONDecodeError:
        # Try to find JSON within the output
        json_match = re.search(r'```json\s*([\s\S]*?)\s*```', output)
        if json_match:
            try:
                json.loads(json_match.group(1))
                return 0.7  # JSON in code block
            except json.JSONDecodeError:
                return 0.3  # Has code block but invalid JSON
        return 0.0


def _check_markdown_format(output: str) -> float:
    """Check if output is well-formatted markdown."""
    score = 0.0
    checks = []

    # Check for headings
    has_headings = bool(re.search(r'^#+\s+.+', output, re.MULTILINE))
    checks.append(("headings", has_headings, 0.25))

    # Check for paragraphs (multiple lines of text)
    paragraphs = len(re.findall(r'\n\n.+', output))
    has_paragraphs = paragraphs >= 2
    checks.append(("paragraphs", has_paragraphs, 0.25))

    # Check for lists (bullet or numbered)
    has_lists = bool(re.search(r'^[\-\*\d\.]+\s+.+', output, re.MULTILINE))
    checks.append(("lists", has_lists, 0.25))

    # Check for reasonable length (at least 100 chars for meaningful analysis)
    has_content = len(output.strip()) >= 100
    checks.append(("content", has_content, 0.25))

    # Calculate score
    for name, passed, weight in checks:
        if passed:
            score += weight

    return score
