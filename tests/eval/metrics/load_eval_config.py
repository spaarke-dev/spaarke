"""
Loader/validator for tests/eval/legal-eval-config.yaml.

Proves the eval config PARSES and is internally consistent (every case's documentPath resolves,
every plantedIssue references a real B1-B16 clause, the case set matches the closed-set shape the
task requires: 6 nda cases + the 3 named negative/authorization cases). Run standalone:

    python tests/eval/metrics/load_eval_config.py

Exits 0 and prints a summary on success; raises on the first structural problem found.
"""
from __future__ import annotations

import sys
from pathlib import Path

import yaml

_EVAL_ROOT = Path(__file__).resolve().parent.parent
_CONFIG_PATH = _EVAL_ROOT / "legal-eval-config.yaml"

_VALID_STANDARD_REFS = {f"B{n}" for n in range(1, 17)}
_REQUIRED_NEGATIVE_CASE_IDS = {"NEG-01-non-nda", "NEG-02-unreadable", "NEG-03-unauthorized"}


def load_config() -> dict:
    with _CONFIG_PATH.open(encoding="utf-8") as f:
        return yaml.safe_load(f)


def validate(config: dict) -> dict:
    """Returns a summary dict; raises AssertionError on the first structural violation."""
    cases = config.get("cases", [])
    assert cases, "legal-eval-config.yaml has no cases"

    nda_cases = [c for c in cases if c["type"] == "nda"]
    negative_cases = [c for c in cases if c["type"] != "nda"]

    assert len(nda_cases) == 6, f"expected 6 NDA cases, found {len(nda_cases)}"

    found_negative_ids = {c["caseId"] for c in negative_cases}
    missing = _REQUIRED_NEGATIVE_CASE_IDS - found_negative_ids
    assert not missing, f"closed set is missing required negative/authorization cases: {missing}"

    total_planted = 0
    for case in cases:
        doc_path = case.get("documentPath")
        if doc_path:
            resolved = _EVAL_ROOT / doc_path
            assert resolved.is_file(), f"{case['caseId']}: documentPath {doc_path} does not exist"

        for issue in case.get("plantedIssues") or []:
            ref = issue["standardRef"]
            assert ref in _VALID_STANDARD_REFS, (
                f"{case['caseId']}: plantedIssue standardRef '{ref}' is not a real B1-B16 clause"
            )
            total_planted += 1

    rubric = config.get("rubric")
    assert rubric, "legal-eval-config.yaml has no rubric section"
    dims = {d["name"] for d in rubric.get("dimensions", [])}
    required_dims = {
        "planted_issue_coverage",
        "citation_accuracy",
        "hallucination_guard",
        "overall_risk_band_accuracy",
    }
    assert required_dims.issubset(dims), f"rubric missing dimensions: {required_dims - dims}"

    return {
        "total_cases": len(cases),
        "nda_cases": len(nda_cases),
        "negative_cases": sorted(found_negative_ids),
        "total_planted_issues": total_planted,
        "rubric_dimensions": sorted(dims),
    }


def main() -> None:
    config = load_config()
    summary = validate(config)
    print("legal-eval-config.yaml: PARSED + VALIDATED")
    for key, value in summary.items():
        print(f"  {key}: {value}")


if __name__ == "__main__":
    main()
