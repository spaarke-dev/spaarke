"""Tests for playbook_import. Each guard has a must-fire and a must-not-fire case."""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

import docx

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import playbook_import as pi  # noqa: E402
import playbook_parse  # noqa: E402

CAT = {"practice": "nda", "version": "t", "topics": [
    {"id": "nda.term", "title": "Term and survival", "question": "How long do obligations last?",
     "look_for": ["years after", "survive"], "aliases": ["Duration of confidentiality obligations"],
     "sides": ["mutual"], "default_priority": "High"},
    {"id": "nda.exclusion-prior-possession", "title": "Exclusion - prior possession",
     "question": "How must the recipient show it already had the information?",
     "look_for": ["known to the Receiving Party", "written records"], "sides": ["mutual"], "default_priority": "Medium"},
    {"id": "nda.exclusion-third-party", "title": "Exclusion - third-party source",
     "question": "When is information received from a third party excluded?",
     "look_for": ["from a third party", "rightfully received"], "sides": ["mutual"], "default_priority": "Medium"},
    {"id": "nda.residuals", "title": "Residuals", "question": "May information retained in memory be used?",
     "look_for": ["residuals", "unaided memory"], "sides": ["mutual"], "default_priority": "High"},
]}
HEAD = ("Topic", "Instructions", "Example Text", "Compromise Positions")


def make(path, rows, title="Test NDA Playbook", pre=("Represented side: mutual.",), header=HEAD):
    d = docx.Document()
    d.add_heading(title, level=1)
    for p in pre:
        d.add_paragraph(p)
    t = d.add_table(rows=1, cols=4)
    for i, h in enumerate(header):
        t.rows[0].cells[i].text = h
    for r in rows:
        cells = t.add_row().cells
        for i, v in enumerate(r):
            if isinstance(v, list):  # several paragraphs in one cell
                cells[i].text = v[0]
                for extra in v[1:]:
                    cells[i].add_paragraph(extra)
            else:
                cells[i].text = v
    d.save(path)
    return path


BASELINE = """# Playbook: Baseline
Version: 1.0 | Side: mutual

## topic: nda.residuals
Preferred: No residuals clause.
Prohibited / escalate: Any residuals clause.
Priority: High

## topic: nda.term
Preferred: 3 years after disclosure.
Fallback: 2 years after disclosure.
Priority: High
"""


class PlaybookImportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.cat = CAT["topics"]

    def path(self, name):
        return os.path.join(self.tmp, name)

    def test_explicit_ids_round_trip_fields(self):
        p = make(self.path("a.docx"), [
            ("Term and survival (nda.term)", "Protect for 3 years after each disclosure. Escalate: Obligations end with the agreement.",
             "three (3) years after each disclosure", ["1. Two (2) years after disclosure.", "2. Eighteen months."]),
        ])
        md, rep = pi.convert(p, self.cat)
        parsed = playbook_parse.parse(md, {t["id"]: set(t["sides"]) for t in self.cat})
        t = parsed["topics"][0]
        self.assertEqual(t["id"], "nda.term")
        self.assertEqual(t["preferred"], "Protect for 3 years after each disclosure.")
        self.assertEqual(t["escalate"], "Obligations end with the agreement.")
        self.assertEqual(t["fallback"], "Two (2) years after disclosure.; then Eighteen months.")
        self.assertEqual(rep["rows"][0]["method"], ["id"])
        self.assertEqual(rep["needs_review"], [])

    def test_merged_and_extra_rows_flagged(self):
        p = make(self.path("b.docx"), [
            ("Exclusions from Confidential Information",
             "Keep prior knowledge shown by written records, known to the Receiving Party before disclosure, and "
             "information rightfully received from a third party without a duty of confidentiality.", "", ""),
            ("Purpose", "Fill the Purpose with a specific business purpose.", "", ""),
        ])
        _, rep = pi.convert(p, self.cat)
        kinds = {r["name"]: r for r in rep["rows"]}
        self.assertEqual(kinds["Exclusions from Confidential Information"]["kind"], "merged")
        self.assertEqual(set(kinds["Exclusions from Confidential Information"]["topics"]),
                         {"nda.exclusion-prior-possession", "nda.exclusion-third-party"})
        self.assertEqual(kinds["Purpose"]["kind"], "extra")  # must not be forced onto a topic
        self.assertIn(2, rep["needs_review"])
        self.assertIn("nda.term", rep["topics_missing"])

    def test_alias_maps_differently_named_row(self):
        p = make(self.path("c.docx"), [
            ("Duration of confidentiality obligations", "Hold obligations at three (3) years after each disclosure.", "", ""),
        ])
        _, rep = pi.convert(p, self.cat)
        self.assertEqual(rep["rows"][0]["topics"], ["nda.term"])

    def test_gap_row_not_written_unless_baseline(self):
        rows = [("Residuals (no Acme precedent position)",
                 "None of the Acme precedents addresses a residuals clause. Escalate any proposal to counsel.", "", ""),
                ("Term and survival (nda.term)", "Protect for 3 years after each disclosure.", "", "")]
        p = make(self.path("d.docx"), rows)
        md, rep = pi.convert(p, self.cat)
        self.assertEqual(rep["topics_gap"], ["nda.residuals"])
        self.assertNotIn("## topic: nda.residuals", md)
        base = playbook_parse.parse(BASELINE, {t["id"]: set(t["sides"]) for t in self.cat})
        md2, rep2 = pi.convert(p, self.cat, baseline=base)
        self.assertIn("## topic: nda.residuals", md2)
        self.assertIn("taken from baseline Baseline", md2)
        self.assertEqual(rep2["topics_from_baseline"], ["nda.residuals"])
        self.assertEqual(rep2["fallback_suggestions"], [{"topic": "nda.term", "baseline_fallback": "2 years after disclosure."}])

    def test_no_four_column_table_is_bad_input(self):
        p = make(self.path("e.docx"), [("x", "y", "z", "w")], header=("Clause", "Position", "Notes", "Other"))
        with self.assertRaises(pi.BadInput):
            pi.convert(p, self.cat)
        err = io.StringIO()
        with contextlib.redirect_stderr(err), contextlib.redirect_stdout(io.StringIO()):
            code = pi.main([p, "--topics", self.write_cat(), "--out", self.path("e.md")])
        self.assertEqual(code, 2)
        self.assertFalse(os.path.exists(self.path("e.md")))

    def test_side_required_when_document_is_silent(self):
        p = make(self.path("f.docx"), [("Term and survival (nda.term)", "Three years.", "", "")],
                 title="Playbook", pre=())
        with self.assertRaises(pi.BadInput):
            pi.convert(p, self.cat)
        md, rep = pi.convert(p, self.cat, side="mutual")
        self.assertEqual(rep["side"], "mutual")

    def test_cli_writes_valid_markdown_and_report(self):
        p = make(self.path("g.docx"), [("Term and survival (nda.term)", "Three years. Reject: one year.", "", "")])
        with contextlib.redirect_stdout(io.StringIO()):
            code = pi.main([p, "--topics", self.write_cat(), "--out", self.path("g.md"), "--report", self.path("g.json")])
        self.assertEqual(code, 0)
        with open(self.path("g.md"), encoding="utf-8") as f:
            playbook_parse.parse(f.read(), {t["id"]: set(t["sides"]) for t in self.cat})
        with open(self.path("g.json"), encoding="utf-8") as f:
            self.assertEqual(json.load(f)["counts"]["topics_written"], 1)

    def write_cat(self):
        path = self.path("topics.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(CAT, f)
        return path


if __name__ == "__main__":
    unittest.main()
