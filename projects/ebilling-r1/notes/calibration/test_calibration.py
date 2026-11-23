"""Checks for the 161 calibration harness (stdlib unittest; run: python -I -m unittest -v test_calibration from this folder, or
python -I test_calibration.py). Scaffolding class (ADR-038): these prove the harness, they do not guard product code.

Every guard has a must-fire control (a case the harness must flag) and a must-not-fire control (a case it must leave alone).
The 046 replica is pinned to the C# test vectors in CombinedConfidenceCalculatorTests.cs.
"""
from __future__ import annotations

import copy
import json
import os
import sys
import unittest
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "spikes" / "extraction"))

import calib_lib as cl  # noqa: E402
import score_corrections as sc  # noqa: E402
import score_findings as sf  # noqa: E402
import score_run as sr  # noqa: E402
import common  # noqa: E402

S = cl.Settings()


def o(analyzer, correct=True, field="fee.hours", **kw):
    base = {"source": "sprk_invoice_v1", "version": "2025-11-01", "item": "X", "field": field, "line": 1, "correct": correct,
            "analyzer": analyzer, "combined": None, "selfReport": None, "arithmetic": "NotApplicable", "reference": "NotApplicable"}
    base.update(kw)
    return base


class Combine046(unittest.TestCase):
    """Pinned to CombinedConfidenceCalculatorTests.cs."""

    def test_all_perfect_is_one(self):
        self.assertEqual(cl.combine(o(1, selfReport=1, arithmetic="Passed", reference="Matched"), S), 1.0)

    def test_mixed_is_weighted_average(self):
        self.assertAlmostEqual(cl.combine(o(0.9, selfReport=0.8, arithmetic="Passed", reference="Matched"), S), 0.93, places=9)

    def test_failed_arithmetic_caps(self):
        self.assertEqual(cl.combine(o(1, selfReport=1, arithmetic="Failed", reference="Matched"), S), 0.5)

    def test_cap_is_a_ceiling_not_a_replacement(self):          # must-not-fire: a 0.25 field is not raised to 0.50
        self.assertAlmostEqual(cl.combine(o(0.2, arithmetic="Failed", reference="Matched"), S), 0.25 / 0.90, places=9)

    def test_self_report_never_raises(self):
        a = cl.combine(o(0.6, selfReport=1.0, arithmetic="Passed", reference="Matched"), S)
        b = cl.combine(o(0.6, selfReport=0.6, arithmetic="Passed", reference="Matched"), S)
        self.assertEqual(a, b)

    def test_not_rounded(self):
        self.assertLess(cl.combine(o(0.89996), S), 0.90)

    def test_analyzer_only_equals_analyzer(self):
        self.assertAlmostEqual(cl.combine(o(0.77), S), 0.77)

    def test_settings_validate_range(self):
        with self.assertRaises(ValueError):
            cl.Settings(arithmetic_fail_cap=1.2).validate()
        S.validate()


class Bins(unittest.TestCase):
    def test_threshold_is_inclusive(self):                      # a value AT 0.90 is trusted
        obs = [o(0.90, True), o(0.90, False), o(0.8999, False)]
        w = cl.wrong_at_or_above(obs, 0.90)
        self.assertEqual((w["n"], w["wrong"]), (2, 1))

    def test_bins_partition_all_values(self):
        obs = [o(c / 100) for c in range(0, 101, 5)]
        self.assertEqual(sum(b["n"] for b in cl.bin_table(obs)), len(obs))

    def test_reliability_gap_sign(self):
        obs = [o(0.95, False)] * 4 + [o(0.95, True)]
        pt = cl.reliability_curve(obs)["points"][0]
        self.assertLess(pt["gap"], 0)                            # over-confident bin: accuracy below mean confidence

    def test_never_pooled_across_versions(self):
        obs = [o(0.9), o(0.9, version="2026-06-01-preview")]
        self.assertEqual(sorted(cl.by_group(obs)), ["sprk_invoice_v1@2025-11-01", "sprk_invoice_v1@2026-06-01-preview"])

    def test_sweep_monotone(self):
        obs = [o(c / 100, correct=c > 80, combined=c / 100) for c in range(50, 100)]
        rows = cl.field_sweep(obs)
        self.assertTrue(all(a["trusted"] >= b["trusted"] for a, b in zip(rows, rows[1:])))
        self.assertEqual(len(rows), 30)
        self.assertEqual((rows[0]["threshold"], rows[-1]["threshold"]), (0.70, 0.99))

    def test_grid_stays_in_range_and_sorted(self):
        obs = [o(0.9, c, arithmetic=a, reference="Matched", selfReport=0.9) for c in (True, False) for a in ("Passed", "Failed")]
        rows = cl.settings_grid(obs, S)
        self.assertTrue(all(0 <= x <= 1 for r in rows for x in r["weights"] + r["caps"]))
        self.assertEqual([r["trusted_wrong"] for r in rows], sorted(r["trusted_wrong"] for r in rows))


def finding(rule_type, consequence, reads, scope="line", reason=None, item="F1"):
    return {"item": item, "ruleType": rule_type, "consequence": consequence, "scope": scope, "reason": reason, "readFields": reads}


class Rejection(unittest.TestCase):
    conf = {("F1", 1, "Hours"): 0.95, ("F1", 1, "Rate"): 0.95}

    def test_fires_when_eligible_and_all_fields_reliable(self):
        f = finding("ExpenseRule", "Reject", [{"line": 1, "field": "Hours"}])
        self.assertEqual(cl.would_reject(f, self.conf, 0.90), (True, ""))

    def test_field_below_threshold_holds(self):               # raising the threshold removes the rejection
        f = finding("ExpenseRule", "Reject", [{"line": 1, "field": "Hours"}])
        self.assertEqual(cl.would_reject(f, self.conf, 0.96)[1], "field_below_threshold")

    def test_hold_only_type_never_rejects(self):
        f = finding("ApprovedTimekeeper", "Reject", [{"line": 1, "field": "Hours"}])
        self.assertEqual(cl.would_reject(f, self.conf, 0.7)[1], "type_holds_only")

    def test_not_eligible_type_and_flag_consequence(self):
        self.assertFalse(cl.would_reject(finding("TimeIncrement", "Reject", [{"line": 1, "field": "Hours"}]), self.conf, 0.7)[0])
        self.assertFalse(cl.would_reject(finding("ExpenseRule", "Flag", [{"line": 1, "field": "Hours"}]), self.conf, 0.7)[0])

    def test_duplicate_rejects_at_invoice_level_only(self):
        r = [{"line": 1, "field": "Hours"}]
        self.assertFalse(cl.would_reject(finding("Duplicate", "Reject", r, scope="line"), self.conf, 0.7)[0])
        self.assertTrue(cl.would_reject(finding("Duplicate", "Reject", r, scope="invoice"), self.conf, 0.7)[0])

    def test_approved_rate_only_on_rate_above_approved(self):
        r = [{"line": 1, "field": "Rate"}]
        self.assertFalse(cl.would_reject(finding("ApprovedRate", "Reject", r, reason="NoApprovedRate"), self.conf, 0.7)[0])
        self.assertTrue(cl.would_reject(finding("ApprovedRate", "Reject", r, reason="RateAboveApproved"), self.conf, 0.7)[0])

    def test_ai_assigned_input_never_gates(self):
        c = {("F1", 1, "BilledTaskCode"): 0.99}
        self.assertEqual(cl.would_reject(finding("RequiredData", "Reject", [{"line": 1, "field": "BilledTaskCode"}]), c, 0.7)[1],
                         "ai_assigned_input")

    def test_no_read_fields_no_rejection(self):
        self.assertEqual(cl.would_reject(finding("RequiredData", "Reject", []), self.conf, 0.7)[1], "no_read_fields")

    def test_sweep_counts_misread_caused(self):
        f = finding("ExpenseRule", "Reject", [{"line": 1, "field": "Hours"}])
        truth_wrong = {("F1", 1, "Hours"): False}
        truth_ok = {("F1", 1, "Hours"): True}
        low = {("F1", 1, "Hours"): 0.85}
        rows = cl.rejection_sweep([f], truth_wrong, {("F1", 1, "Hours"): 0.95})
        self.assertEqual({r["threshold"]: r["misread_caused"] for r in rows}[0.90], 1)        # must-fire
        self.assertEqual({r["threshold"]: r["misread_caused"] for r in rows}[0.96], 0)        # raised threshold clears it
        self.assertEqual(cl.rejection_sweep([f], truth_ok, {("F1", 1, "Hours"): 0.95})[20]["misread_caused"], 0)   # must-not-fire
        self.assertEqual(cl.rejection_sweep([f], truth_wrong, low)[20]["rejects"], 0)


class Corrections(unittest.TestCase):
    @staticmethod
    def rows(n, version="2025-11-01", outcome=sc.CONFIRMED, label=True, conf=0.95):
        return [{"fieldName": "Hours", "aiConfidence": conf, "outcomeType": outcome, "isLabel": label,
                 "sourceCode": "sprk_invoice_v1", "sourceVersion": version} for _ in range(n)]

    def test_bulk_and_unreviewed_never_labels(self):
        ex = {"run": "t", "rows": self.rows(40, outcome=sc.ACCEPTED_BULK, label=True) + self.rows(5, outcome=sc.UNREVIEWED, label=False),
              "unreviewed": [{"sourceCode": "sprk_invoice_v1", "sourceVersion": "2025-11-01", "count": 7}]}
        g = sc.score_corrections(ex)["groups"]["sprk_invoice_v1@2025-11-01"]
        self.assertEqual(g["labels"], 0)
        self.assertEqual((g["bulk_accepted_rows"], g["unreviewed_values"], g["inconsistent_label_flag"]), (40, 12, 40))

    def test_under_30_labels_is_insufficient_and_over_is_sufficient(self):
        few = sc.score_corrections({"rows": self.rows(29)})["groups"]["sprk_invoice_v1@2025-11-01"]
        many = sc.score_corrections({"rows": self.rows(30)})["groups"]["sprk_invoice_v1@2025-11-01"]
        self.assertFalse(few["sufficient"])
        self.assertIn("note", few)
        self.assertTrue(many["sufficient"])

    def test_split_by_version_never_pooled(self):
        ex = {"rows": self.rows(30, "2025-11-01") + self.rows(31, "2026-06-01-preview", outcome=sc.CORRECTED)}
        g = sc.score_corrections(ex)["groups"]
        self.assertEqual({k: v["labels"] for k, v in g.items()}, {"sprk_invoice_v1@2025-11-01": 30, "sprk_invoice_v1@2026-06-01-preview": 31})
        self.assertEqual(g["sprk_invoice_v1@2026-06-01-preview"]["wrong_rate_pct"], 100.0)

    def test_corrected_is_wrong_confirmed_is_right(self):
        ex = {"rows": self.rows(3, outcome=sc.CORRECTED) + self.rows(1, outcome=sc.CONFIRMED)}
        self.assertEqual(sc.score_corrections(ex)["groups"]["sprk_invoice_v1@2025-11-01"]["wrong_rate_pct"], 75.0)


class RunExport(unittest.TestCase):
    """End to end on a committed synthetic ground-truth item: a perfect export, then one wrong hours value."""

    @classmethod
    def setUpClass(cls):
        cls.gt = common.load_gt("B-0001")
        cls.full = cls.gt

    def export(self, wrong_line=None):
        lines = []
        for ln in [x for x in self.gt["lines"] if x["line_type"] in ("fee", "expense")][:6]:
            e = {"n": ln["n"], "type": ln["line_type"], "date": ln["date"], "timekeeper": ln.get("timekeeper"), "hours": ln.get("hours"),
                 "rate": ln.get("rate"), "amount": ln["amount"], "narrative": ln.get("narrative"),
                 "fields": {k: {"confidence": 0.96, "combined": 0.96} for k in ("Date", "TimekeeperAsBilled", "Hours", "Rate", "Amount")},
                 "signals": {"Hours": {"arithmetic": "Passed", "reference": "NotApplicable"}}}
            if ln["n"] == wrong_line:
                e["hours"] = (e["hours"] or 1) + 3
            lines.append(e)
        return {"run": "t1", "source": {"analyzerId": "sprk_invoice_v1", "apiVersion": "2025-11-01"}, "settings": {"threshold": 0.9},
                "invoices": [{"id": "B-0001", "header": {"FirmInvoiceNumber": {"value": self.gt["header"]["firm_invoice_number"],
                                                                                  "confidence": 0.99, "combined": 0.99}},
                              "lines": lines, "findings": []}]}

    def test_perfect_export_has_no_wrong_values(self):
        rep = sr.score_export(self.export(), lambda i: self.gt, lambda i: self.full)
        self.assertGreater(rep["counts"]["values"], 5)
        self.assertEqual(rep["combined"]["overall"]["at_or_above_threshold"]["wrong"], 0)

    def test_wrong_value_at_high_confidence_is_counted(self):      # must-fire
        first = [x for x in self.gt["lines"] if x["line_type"] == "fee"][0]["n"]
        rep = sr.score_export(self.export(wrong_line=first), lambda i: self.gt, lambda i: self.full)
        self.assertGreaterEqual(rep["combined"]["overall"]["at_or_above_threshold"]["wrong"], 1)
        self.assertIn("fee.hours", rep["combined"]["fields"])


class Findings(unittest.TestCase):
    keys = sf.load_keys(sf.DEFAULT_KEYS)

    def perfect_export(self, only_rs="RS-02"):
        invs = []
        cons = self.keys[only_rs]["production_consequences"]
        for inv in self.keys[only_rs]["invoices"]:
            invs.append({"id": inv["id"], "findings": [
                {"ruleId": f["rule_id"], "ruleType": f["type"], "lineRefs": f.get("line_refs") or [], "scope": f["scope"],
                 "consequence": cons[f["rule_id"]]} for f in inv["production_findings"]]})
        return {"run": "k", "invoices": invs}

    def test_perfect_export_scores_perfectly_and_clean_baseline_holds(self):
        r = sf.score_findings(self.perfect_export(), self.keys)
        self.assertEqual((r["fp"], r["fn"], r["consequence_wrong"], r["clean_with_finding"]), (0, 0, 0, 0))
        self.assertTrue(r["all_types_meet_target"])
        self.assertEqual(r["clean_invoices"], 5)

    def test_finding_on_clean_invoice_is_false_positive(self):          # must-fire
        ex = self.perfect_export()
        clean = next(i for i in self.keys["RS-02"]["invoices"] if i["clean"])["id"]
        next(i for i in ex["invoices"] if i["id"] == clean)["findings"].append(
            {"ruleId": "RS-02-R03", "ruleType": "TimeIncrement", "lineRefs": [1], "scope": "line", "consequence": "Flag"})
        r = sf.score_findings(ex, self.keys)
        self.assertEqual((r["clean_with_finding"], r["fp"]), (1, 1))
        self.assertFalse(r["clean_within_budget"])

    def test_unrostered_approved_rate_is_expected_not_false_positive(self):    # must-not-fire
        r = sf.score_findings(self.perfect_export("RS-06"), self.keys)
        self.assertGreater(r["production_only_expected"], 0)
        self.assertEqual(r["production_only_expected"], r["production_only_found"])
        self.assertEqual(r["fp"], 0)

    def test_set_f_consequence_word_is_not_the_production_consequence(self):   # Reduce / Disallow would be wrong; must fire
        ex = self.perfect_export()
        for inv in ex["invoices"]:
            for f in inv["findings"]:
                if f["ruleType"] == "ApprovedRate":
                    f["consequence"] = "Reduce"
        self.assertGreater(sf.score_findings(ex, self.keys)["consequence_wrong"], 0)

    def test_set_f_moved_finding_scored_by_production_not_set_f(self):
        # the 'moved' DailyHoursCap findings sit at different line refs in Set F and production; production's must match
        r = sf.score_findings(self.perfect_export(), self.keys)
        self.assertEqual(r["by_rule_type"]["DailyHoursCap"]["recall"], 1.0)


class RenderedTables(unittest.TestCase):
    """POML acceptance: a bin table and a wrong-rate-at-threshold for every header field and for line date, timekeeper, hours, rate, amount
    and task code, for GA and preview separately."""
    REQUIRED = ["header." + f for f in sr.HEADER_MAP.values() if f != "po_number"] + [
        "fee.date", "fee.timekeeper", "fee.hours", "fee.rate", "fee.amount", "fee.task_code"]

    def test_every_required_field_is_in_each_arm(self):
        for arm in ("c", "cp"):
            d = json.loads((HERE / "results" / f"analyzer-baseline-{arm}.json").read_text(encoding="utf-8"))
            for fld in self.REQUIRED:
                self.assertIn(fld, d["fields"], f"{arm}: {fld}")
                self.assertEqual(len(d["fields"][fld]["bins"]), len(cl.BINS))
                self.assertIn("wrong_rate", d["fields"][fld]["at_or_above_threshold"])

    def test_results_are_never_pooled_and_carry_no_text(self):
        for arm, version in (("c", "2025-11-01"), ("cp", "2026-06-01-preview")):
            d = json.loads((HERE / "results" / f"analyzer-baseline-{arm}.json").read_text(encoding="utf-8"))
            self.assertEqual(d["version"], version)
            blob = json.dumps(d)
            self.assertNotRegex(blob, r"narrative|\$\d")        # ADR-015 spot check: no narrative key, no amounts


@unittest.skipUnless(os.environ.get("CALIB_LOCAL"), "set CALIB_LOCAL to the evaluation-set/local folder holding spike-005 raw results")
class AnalyzerBaselineReproduces005(unittest.TestCase):
    def test_ga_headline_matches_extraction_spike_section_3(self):
        import analyzer_baseline as ab
        local = Path(os.environ["CALIB_LOCAL"]).resolve()
        common.LOCAL, common.LOCAL_RAW = local, local / "spike-005"
        obs, _ = ab.observations("c")
        core = [x for x in obs if not x["field"].endswith("_code")]
        w = cl.wrong_at_or_above(core, 0.90)
        self.assertEqual((len(core), w["n"], w["wrong"]), (7512, 619, 6))        # 005: 7,512 values; 619 at >= 0.90; 6 wrong


if __name__ == "__main__":
    unittest.main(verbosity=2)
