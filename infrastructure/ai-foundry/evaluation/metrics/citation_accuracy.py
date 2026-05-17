"""
Custom metric: Citation Accuracy
Checks if cited legal cases and statutes in the response are real and correctly formatted.

Legal citation accuracy is critical for AI-assisted legal work. Hallucinated case citations
or incorrectly formatted statute references undermine the reliability of legal analysis.

Scoring Logic:
  - 1.0: All detected citations match expected legal citation formats
  - 0.5: Some citations match expected formats (partial — at least one valid, at least one invalid)
  - 0.0: No valid citations found when citations were expected (has_citations=True)
        OR citation-like strings are present but none match any valid format

Recognized Citation Formats:
  Case Law (Federal Reporters):
    - Federal Supplement (District Courts): "NNN F.Supp. NNN", "NNN F.Supp.2d NNN", "NNN F.Supp.3d NNN"
    - Federal Reporter (Circuit Courts): "NNN F.2d NNN", "NNN F.3d NNN", "NNN F.4th NNN"
    - US Supreme Court: "NNN U.S. NNN", "NNN S.Ct. NNN", "NNN L.Ed.2d NNN"
    - Federal Appendix (unpublished): "NNN F.App'x NNN", "NNN F. App'x NNN"
    - Bankruptcy Reporter: "NNN B.R. NNN"
  Statutes:
    - US Code: "NN U.S.C. § NNN", "NN U.S.C. §§ NNN-NNN"
    - Code of Federal Regulations: "NN C.F.R. § NNN.NN"
    - Public Laws: "Pub. L. No. NNN-NNN"
  State Cases:
    - Atlantic Reporter: "NNN A.2d NNN", "NNN A.3d NNN"
    - Pacific Reporter: "NNN P.2d NNN", "NNN P.3d NNN"
    - Southwest Reporter: "NNN S.W.2d NNN", "NNN S.W.3d NNN"
    - Northeast Reporter: "NNN N.E.2d NNN", "NNN N.E.3d NNN"
    - Southeast Reporter: "NNN S.E.2d NNN", "NNN S.E.2d NNN"

Unit Test Examples (docstring):
    >>> # All citations valid — score 1.0
    >>> sample = {
    ...     "output": "See Brown v. Board, 347 U.S. 483 (1954); 42 U.S.C. § 1983.",
    ...     "has_citations": True
    ... }
    >>> metric = CitationAccuracyMetric()
    >>> metric.evaluate(sample)
    1.0

    >>> # No citations when expected — score 0.0
    >>> sample = {"output": "The law is clear on this point.", "has_citations": True}
    >>> metric = CitationAccuracyMetric()
    >>> metric.evaluate(sample)
    0.0

    >>> # No citations, none expected — score 1.0 (vacuously valid)
    >>> sample = {"output": "General legal context applies here.", "has_citations": False}
    >>> metric = CitationAccuracyMetric()
    >>> metric.evaluate(sample)
    1.0

    >>> # Partial: one valid, one malformed — score 0.5
    >>> sample = {
    ...     "output": "See Smith v. Jones (2019) and 42 U.S.C. § 1983.",
    ...     "has_citations": True
    ... }
    >>> metric = CitationAccuracyMetric()
    >>> metric.evaluate(sample)
    0.5
"""

import re
from abc import ABC, abstractmethod
from promptflow.core import tool


class BaseMetric(ABC):
    """
    Abstract base class for all custom evaluation metrics.
    Subclasses must implement the evaluate(sample) method.
    """

    @abstractmethod
    def evaluate(self, sample: dict) -> float:
        """
        Evaluate a single sample and return a score.

        Args:
            sample: Dictionary containing at minimum 'output' (str) and
                    metric-specific fields for evaluation context.

        Returns:
            Score between 0.0 (worst) and 1.0 (best).
        """
        ...


# ---- Compiled citation regexes -----------------------------------------------
# Federal Reporter patterns: volume REPORTER page  (e.g. "347 U.S. 483")
_CASE_PATTERNS = [
    # US Supreme Court
    re.compile(r'\b\d+\s+U\.S\.\s+\d+'),
    re.compile(r'\b\d+\s+S\.Ct\.\s+\d+'),
    re.compile(r'\b\d+\s+L\.Ed\.2d\s+\d+'),
    # Federal Circuits
    re.compile(r'\b\d+\s+F\.\d+(?:d|th|rd|st)?\s+\d+'),   # F.2d / F.3d / F.4th
    re.compile(r'\b\d+\s+F\.Supp(?:\.\d+[a-z]*)?\s+\d+'), # F.Supp / F.Supp.2d / F.Supp.3d
    re.compile(r"\b\d+\s+F\.App'?x\s+\d+"),                # Federal Appendix
    re.compile(r'\b\d+\s+B\.R\.\s+\d+'),                   # Bankruptcy Reporter
    # State reporters
    re.compile(r'\b\d+\s+A\.\d+[a-z]*\s+\d+'),             # Atlantic
    re.compile(r'\b\d+\s+P\.\d+[a-z]*\s+\d+'),             # Pacific
    re.compile(r'\b\d+\s+S\.W\.\d+[a-z]*\s+\d+'),          # Southwest
    re.compile(r'\b\d+\s+N\.E\.\d+[a-z]*\s+\d+'),          # Northeast
    re.compile(r'\b\d+\s+S\.E\.\d+[a-z]*\s+\d+'),          # Southeast
]

# Statute patterns
_STATUTE_PATTERNS = [
    re.compile(r'\b\d+\s+U\.S\.C\.\s+§§?\s*\d+'),          # US Code
    re.compile(r'\b\d+\s+C\.F\.R\.\s+§\s*\d+'),            # Code of Federal Regulations
    re.compile(r'\bPub\.\s*L\.\s*No\.\s*\d+-\d+'),         # Public Laws
]

_ALL_CITATION_PATTERNS = _CASE_PATTERNS + _STATUTE_PATTERNS

# Heuristic: citation-like fragments that look like citations but may be malformed.
# These are looser patterns used to detect "attempted" citations.
_CITATION_LIKE_PATTERN = re.compile(
    r'\b\d+\s+[A-Z][A-Z.\']+\s+\d+'      # volume REPORTER page (loose)
    r'|§§?\s*\d+'                          # section symbol
    r'|\bU\.S\.C\b'                        # US Code reference
    r'|\bC\.F\.R\b',                       # CFR reference
    re.IGNORECASE,
)


class CitationAccuracyMetric(BaseMetric):
    """
    Evaluates whether cited legal cases and statutes in the AI response use
    recognised, correctly-formatted citation styles.

    Scoring:
      - 1.0  All detected citations conform to a recognised citation format.
      - 0.5  At least one citation conforms and at least one does not (partial).
      - 0.0  Citations were expected (has_citations=True) but none found,
             or citation-like strings are present but none match any valid format.

    When has_citations is False and the output contains no citation strings the
    metric returns 1.0 (vacuously correct — the response correctly omits citations).
    """

    def evaluate(self, sample: dict) -> float:
        """
        Evaluate citation accuracy for a single response sample.

        Args:
            sample: Dictionary with keys:
                - output (str): The AI response text to evaluate.
                - has_citations (bool): Whether the question/task requires citations.
                  When True and no citations are found the score is 0.0.
                  When False the metric only penalises hallucinated malformed citations.

        Returns:
            float: Score in [0.0, 1.0].
        """
        output: str = sample.get("output", "") or ""
        has_citations: bool = bool(sample.get("has_citations", False))

        if not output.strip():
            # Empty response: score 0 if citations were expected, 1 if not
            return 0.0 if has_citations else 1.0

        valid_count = _count_valid_citations(output)
        attempted_count = _count_attempted_citations(output)

        if has_citations:
            if valid_count == 0 and attempted_count == 0:
                # No citations at all — fail
                return 0.0
            if valid_count == 0 and attempted_count > 0:
                # Attempted citations but none are correctly formatted
                return 0.0
            if valid_count == attempted_count:
                # All detected citations are valid
                return 1.0
            # Some valid, some not
            return 0.5
        else:
            # Citations not required — any malformed attempted citations are a penalty
            if attempted_count > 0 and valid_count == 0:
                return 0.0  # Hallucinated/malformed citation-like strings
            if attempted_count > 0 and valid_count < attempted_count:
                return 0.5  # Mixed: some valid, some not, when not required
            # No citations or all citations happen to be valid format
            return 1.0


def _count_valid_citations(text: str) -> int:
    """Return total count of non-overlapping valid citation matches."""
    total = 0
    for pattern in _ALL_CITATION_PATTERNS:
        total += len(pattern.findall(text))
    return total


def _count_attempted_citations(text: str) -> int:
    """
    Return count of citation-like fragments (loose heuristic).
    Counts all matches of the broad heuristic pattern to detect attempted citations
    that may be malformed.
    """
    return len(_CITATION_LIKE_PATTERN.findall(text))


# ---- PromptFlow @tool entry point (matches format_compliance.py pattern) ------

@tool
def citation_accuracy(output: str, has_citations: bool = False) -> float:
    """
    PromptFlow tool entry point for citation accuracy metric.

    Evaluates whether legal citations in the AI response are correctly formatted.

    Args:
        output: The AI response text to evaluate.
        has_citations: Whether the prompt/task expected the response to include citations.
                       Set to True for tasks like case research or statute lookup.

    Returns:
        Score between 0.0 and 1.0 indicating citation accuracy.
        1.0 = all citations correctly formatted (or no citations when none expected)
        0.5 = partially correct (some valid, some malformed)
        0.0 = citations expected but absent, or all citation-like strings malformed
    """
    metric = CitationAccuracyMetric()
    return metric.evaluate({"output": output, "has_citations": has_citations})
