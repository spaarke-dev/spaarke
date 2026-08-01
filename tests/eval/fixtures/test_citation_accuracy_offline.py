"""
Offline verification for tests/eval/metrics/citation_accuracy.py.

Proves the metric parses/runs and correctly discriminates pass/fail WITHOUT any live Azure OpenAI
call (this project's live eval run is env-blocked -- see tests/eval/README.md "Running the live
eval"). Plain-assert script, no pytest/promptflow dependency required, so it runs anywhere Python
3.10+ is available:

    python tests/eval/fixtures/test_citation_accuracy_offline.py

Exits 0 and prints "ALL OFFLINE CHECKS PASSED" on success; raises AssertionError (nonzero exit) on
any check failure.
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

_METRICS_DIR = Path(__file__).resolve().parent.parent / "metrics"
sys.path.insert(0, str(_METRICS_DIR))

from citation_accuracy import citation_accuracy, score_report  # noqa: E402

_CASES_DIR = Path(__file__).resolve().parent.parent / "cases"
_FIXTURES_DIR = Path(__file__).resolve().parent


def _load_fixture() -> tuple[dict, str]:
    output = json.loads((_FIXTURES_DIR / "sample-nda-review-output.json").read_text(encoding="utf-8"))
    document_text = (_CASES_DIR / "nda-02-narrow-ci-oneway.md").read_text(encoding="utf-8")
    return output, document_text


def test_mixed_fixture_discriminates_pass_and_fail() -> None:
    output, document_text = _load_fixture()
    report = score_report(output, document_text)

    assert report["total_findings"] == 3, f"expected 3 findings, got {report['total_findings']}"
    assert report["passed_count"] == 1, f"expected 1 passing finding, got {report['passed_count']}"
    assert len(report["failed"]) == 2, f"expected 2 failing findings, got {len(report['failed'])}"
    assert math.isclose(report["score"], 1 / 3, rel_tol=1e-9), (
        f"expected score == 1/3, got {report['score']}"
    )

    correct, fabricated_quote, fabricated_ref = report["all_results"]

    # Finding 0: real B3 citation, verbatim quote -- must PASS both checks.
    assert correct["quote_verbatim"] is True, "correct finding's verbatim quote check should pass"
    assert correct["standard_ref_valid"] is True, "correct finding's standardRef should resolve to B3"
    assert correct["passed"] is True

    # Finding 1: fabricated quote -- must fail the quote-verbatim check specifically.
    assert fabricated_quote["quote_verbatim"] is False, "fabricated quote must fail verbatim check"
    assert fabricated_quote["passed"] is False
    assert any("not found verbatim" in r for r in fabricated_quote["reasons"])

    # Finding 2: verbatim quote but invented standardRef -- must fail the ref-validity check only.
    assert fabricated_ref["quote_verbatim"] is True, "this quote IS verbatim -- only the ref is bad"
    assert fabricated_ref["standard_ref_valid"] is False, "invented standardRef must fail"
    assert fabricated_ref["passed"] is False
    assert any("does not resolve to a real B1-B16 clause" in r for r in fabricated_ref["reasons"])

    print("[PASS] mixed fixture: score=1/3, 1 pass / 2 fail, correct failure reasons attributed")


def test_promptflow_tool_entrypoint_matches_score_report() -> None:
    """The @tool-decorated citation_accuracy() function (the promptflow custom-metric contract:
    takes JSON-string output, returns a float) must agree with score_report()'s aggregate score."""
    output, document_text = _load_fixture()
    output_json_string = json.dumps(output)

    tool_score = citation_accuracy(output_json_string, document_text)
    report_score = score_report(output, document_text)["score"]

    assert math.isclose(tool_score, report_score, rel_tol=1e-9), (
        f"@tool entrypoint score {tool_score} != score_report score {report_score}"
    )
    print(f"[PASS] promptflow @tool entrypoint agrees with score_report: {tool_score:.4f}")


def test_clean_nda_with_zero_findings_is_vacuously_accurate() -> None:
    """A clean NDA (nda-01) with an empty flaggedSections array must score 1.0 -- nothing was
    fabricated because nothing was claimed. This is the expected shape for NEG-01 (non-NDA
    decline) and for a genuinely clean NDA like nda-01."""
    clean_output = {"overallRisk": "Low", "flaggedSections": []}
    document_text = (_CASES_DIR / "nda-01-clean-mutual.md").read_text(encoding="utf-8")

    report = score_report(clean_output, document_text)
    assert report["score"] == 1.0, f"expected vacuous 1.0 score for zero findings, got {report['score']}"
    assert report["total_findings"] == 0
    assert report["failed"] == []

    tool_score = citation_accuracy(json.dumps(clean_output), document_text)
    assert tool_score == 1.0

    print("[PASS] zero-finding output scores 1.0 (vacuously accurate) via both entrypoints")


def test_malformed_output_scores_zero() -> None:
    """Unparseable output (e.g. a truncated/garbled model response) must score 0.0, not raise --
    the harness must degrade gracefully on malformed model output rather than crashing the eval run."""
    document_text = (_CASES_DIR / "nda-01-clean-mutual.md").read_text(encoding="utf-8")

    assert citation_accuracy("{not valid json", document_text) == 0.0
    assert citation_accuracy(None, document_text) == 0.0  # type: ignore[arg-type]

    print("[PASS] malformed/unparseable output scores 0.0 without raising")


def test_findings_carry_split_flaggedclause_and_assessment_not_explanation() -> None:
    """FR-05 (agreements-r1 task 002): the Action output schema splits the single 'explanation'
    blob into two discrete fields — flaggedClause (grounded fact) + assessment (reasoned judgment).
    Every fixture finding must carry both, must NOT carry the legacy 'explanation' key, and NEITHER
    field may embed an inline 'grounded fact'/'judgment' marker (the split IS the fact/judgment
    distinction, so downstream consumers — memo FR-13, Word export FR-15, FR-16 materializer — never
    string-parse a marker out of prose)."""
    output, _ = _load_fixture()
    findings = output["flaggedSections"]
    assert findings, "fixture must have findings to check the split shape"

    banned_markers = ("grounded fact —", "grounded fact -", "advisory judgment —",
                      "advisory judgment -", "assessment —", "flagged clause —")
    for i, f in enumerate(findings):
        assert "explanation" not in f, f"finding[{i}] still carries the legacy 'explanation' blob"
        assert isinstance(f.get("flaggedClause"), str) and f["flaggedClause"].strip(), \
            f"finding[{i}] must carry a non-empty flaggedClause (grounded fact)"
        assert isinstance(f.get("assessment"), str) and f["assessment"].strip(), \
            f"finding[{i}] must carry a non-empty assessment (reasoned judgment)"
        for field in ("flaggedClause", "assessment"):
            low = f[field].lower()
            for marker in banned_markers:
                assert marker not in low, (
                    f"finding[{i}].{field} embeds an inline '{marker.strip()}' marker — the "
                    "flaggedClause/assessment split carries the fact/judgment distinction structurally"
                )

    print("[PASS] every finding carries discrete flaggedClause + assessment (FR-05 split), no legacy "
          "explanation blob, no inline fact/judgment markers")


def main() -> None:
    test_mixed_fixture_discriminates_pass_and_fail()
    test_promptflow_tool_entrypoint_matches_score_report()
    test_clean_nda_with_zero_findings_is_vacuously_accurate()
    test_malformed_output_scores_zero()
    test_findings_carry_split_flaggedclause_and_assessment_not_explanation()
    print("\nALL OFFLINE CHECKS PASSED")


if __name__ == "__main__":
    main()
