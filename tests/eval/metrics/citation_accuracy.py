"""
Custom metric: Citation Accuracy (NDA-REVIEW)

Grades task 020's NDA-REVIEW Action output against the closed set authored in
tests/eval/cases/. For every finding in {overallRisk, flaggedSections[...]}, this metric checks:

  1. quotedText is a VERBATIM excerpt of the source NDA's documentText (whitespace-normalized
     comparison only -- no paraphrase tolerance, per the Action's systemPrompt requirement that
     quotedText be "copied from documentText (not paraphrased)").
  2. standardRef resolves to a real clause of the Spaarke NDA standard (B1-B16), per
     projects/ai-advanced-capabilities-nda-r1/notes/spaarke-nda-standard-baseline.md Part B.

A finding that fails either check is a citation-accuracy failure: a hallucinated quote (invariant
#1) or an invented/garbled standard-clause reference (invariant #2). Per ADR-039 advisory grounding
rules, both are prohibited -- "any fact ... you cannot resolve to one of the two grounding sources
... is prohibited and has no place in your output."

Follows the repo's existing promptflow custom-metric convention
(infrastructure/ai-foundry/evaluation/metrics/format_compliance.py,
infrastructure/ai-foundry/evaluation/metrics/completeness.py: a @tool-decorated function taking the
model output and returning a float in [0, 1]) but the `@tool` import is guarded so this script runs
standalone (CLI + offline unit test) in an environment without the promptflow SDK installed --
this project's live eval run is env-blocked (no Azure OpenAI credentials), so the offline path is
the one exercised in-repo today.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Optional

try:
    from promptflow.core import tool
except ImportError:  # promptflow SDK not installed in this environment -- degrade to no-op decorator
    def tool(fn):
        return fn


# The closed clause vocabulary from the Spaarke NDA standard baseline (B1-B16).
# Kept in sync manually with notes/spaarke-nda-standard-baseline.md Part B and the
# NDA-REVIEW Action's systemPrompt CLAUSE-BY-CLAUSE STANDARD block (infra/dataverse/actions/nda-review.action.json).
VALID_STANDARD_REFS = {f"B{n}" for n in range(1, 17)}

_STANDARD_REF_TOKEN = re.compile(r"^\s*(B(?:1[0-6]|[1-9]))\b")


def _normalize_whitespace(text: str) -> str:
    """Collapse all whitespace runs to single spaces and strip. This is the ONLY normalization
    applied -- no case-folding, no punctuation stripping -- so the verbatim-quote check stays
    faithful to the Action's "verbatim excerpt ... not paraphrased" requirement while tolerating
    incidental whitespace/line-wrap differences introduced by text extraction."""
    return re.sub(r"\s+", " ", text or "").strip()


def _extract_standard_ref_token(standard_ref: str) -> Optional[str]:
    """Pull the leading Bn token off a standardRef string like 'B3 - Definition of Confidential
    Information'. Returns None if the string doesn't start with a recognizable Bn token."""
    match = _STANDARD_REF_TOKEN.match(standard_ref or "")
    return match.group(1) if match else None


def check_finding(finding: dict, document_text: str) -> dict:
    """Evaluate one flaggedSections[] entry's citation accuracy against the source NDA text.

    Returns:
        {
          "sectionRef": str | None,
          "quote_verbatim": bool,
          "standard_ref_valid": bool,
          "passed": bool,             # both checks must pass
          "reasons": list[str],       # human-readable failure reasons (empty if passed)
        }
    """
    reasons: list[str] = []
    quoted_text = finding.get("quotedText", "")
    standard_ref = finding.get("standardRef", "")

    normalized_doc = _normalize_whitespace(document_text)
    normalized_quote = _normalize_whitespace(quoted_text)
    quote_verbatim = bool(normalized_quote) and normalized_quote in normalized_doc
    if not quote_verbatim:
        reasons.append(
            "quotedText not found verbatim (whitespace-normalized) in the source documentText"
        )

    ref_token = _extract_standard_ref_token(standard_ref)
    standard_ref_valid = ref_token in VALID_STANDARD_REFS if ref_token else False
    if not standard_ref_valid:
        reasons.append(
            f"standardRef '{standard_ref}' does not resolve to a real B1-B16 clause of the "
            "Spaarke NDA standard"
        )

    return {
        "sectionRef": finding.get("sectionRef"),
        "quote_verbatim": quote_verbatim,
        "standard_ref_valid": standard_ref_valid,
        "passed": quote_verbatim and standard_ref_valid,
        "reasons": reasons,
    }


@tool
def citation_accuracy(output: str, document_text: str) -> float:
    """Promptflow custom-metric entry point (matches the repo's format_compliance.py /
    completeness.py signature convention: takes the model output, returns a float score).

    Args:
        output: JSON string (or dict) of the NDA-REVIEW Action output
                 {overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel, explanation,
                 standardRef}]}.
        document_text: the source NDA text the Action was run against (the documentText input).

    Returns:
        Fraction of flaggedSections findings whose citation is valid (quote verbatim AND
        standardRef real), in [0.0, 1.0]. An NDA with zero findings (clean NDA, or a correctly
        declined non-NDA/negative case) returns 1.0 -- vacuously accurate: nothing was fabricated.
        Malformed/unparseable output returns 0.0.
    """
    try:
        parsed = json.loads(output) if isinstance(output, str) else output
    except (json.JSONDecodeError, TypeError):
        return 0.0

    if not isinstance(parsed, dict):
        return 0.0

    findings = parsed.get("flaggedSections", [])
    if not findings:
        return 1.0

    results = [check_finding(f, document_text) for f in findings]
    return sum(1 for r in results if r["passed"]) / len(results)


def score_report(output: dict, document_text: str) -> dict:
    """Non-promptflow helper used by the offline fixture test and the CLI entry point below.
    Returns the full per-finding breakdown alongside the aggregate score (the promptflow `@tool`
    function above only returns the float, per the custom-metric contract)."""
    findings = output.get("flaggedSections", []) if isinstance(output, dict) else []
    results = [check_finding(f, document_text) for f in findings]
    score = 1.0 if not results else sum(1 for r in results if r["passed"]) / len(results)
    return {
        "score": score,
        "total_findings": len(results),
        "passed_count": sum(1 for r in results if r["passed"]),
        "failed": [r for r in results if not r["passed"]],
        "all_results": results,
    }


def _main() -> None:
    """CLI entry point: python citation_accuracy.py <nda-review-output.json> <source-document>

    Prints the full score_report as JSON and exits 0 if score >= the rubric threshold (0.95,
    see tests/eval/legal-eval-config.yaml rubric.dimensions.citation_accuracy), else exits 1.
    """
    if len(sys.argv) != 3:
        print("Usage: python citation_accuracy.py <output.json> <document.txt>")
        sys.exit(2)

    output_path, doc_path = Path(sys.argv[1]), Path(sys.argv[2])
    output = json.loads(output_path.read_text(encoding="utf-8"))
    document_text = doc_path.read_text(encoding="utf-8")

    report = score_report(output, document_text)
    print(json.dumps(report, indent=2))
    sys.exit(0 if report["score"] >= 0.95 else 1)


if __name__ == "__main__":
    _main()
