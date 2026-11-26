"""Tests for xlsx_lite, review_visuals and library_grid. Each guard has a must-fire and a must-not-fire case."""
import contextlib
import io
import json
import os
import re
import sys
import tempfile
import unittest
import zipfile

from lxml import etree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import library_grid  # noqa: E402
import review_visuals  # noqa: E402
import xlsx_lite  # noqa: E402

TOPICS = {"practice": "nda", "version": "t", "topics": [
    {"id": "nda.term", "title": "Term", "sides": ["mutual"]},
    {"id": "nda.residuals", "title": "Residuals", "sides": ["mutual"]},
    {"id": "nda.remedies", "title": "Remedies", "sides": ["mutual"]},
]}


def review(**over):
    r = {"practice": "nda", "document": {"file_name": "a.docx"},
         "review_basis": {"side": "mutual", "posture": "balanced"},
         "topics": [
             {"topic": "nda.term", "rating": "YELLOW", "status": "fallback", "clause": "5", "quote": "five (5) years",
              "action": "edit", "priority": "Medium"},
             {"topic": "nda.residuals", "rating": "RED", "status": "deviation", "clause": "8",
              "quote": "retained in the unaided memory", "action": "escalate", "priority": "High"},
             {"topic": "nda.remedies", "rating": "GREEN", "status": "compliant", "action": "none"}],
         "provenance": {"skill": "nda-review", "skill_version": "t", "standards": "Test v1"}}
    r.update(over)
    return r


PLAN = {"author": "T", "date": "2026-11-05T00:00:00Z", "review_basis": {"side": "mutual", "posture": "balanced"},
        "edits": [{"para": "5.", "find": "five (5)", "replace": "three (3)", "topic": "nda.term", "comment": "c",
                   "summary": {"n": 1, "section": "5", "bookmark": "NDA_Sec5", "issue": "i", "revision": "r",
                               "priority": "Medium"}}]}


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.d = self.tmp.name
        self.topics = self.write("topics.json", TOPICS)

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, name, obj):
        p = os.path.join(self.d, name)
        with open(p, "w", encoding="utf-8") as f:
            json.dump(obj, f)
        return p

    def run_main(self, mod, args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = mod.main(args)
        return code, json.loads(out.getvalue())


class XlsxTests(Base):
    def test_workbook_parts_are_well_formed_and_chart_has_cache(self):
        wb = xlsx_lite.Workbook()
        s = wb.sheet("Sum: [x]")  # illegal sheet-name characters are replaced
        s.append(["k", "n"], style="header")
        s.append([{"v": "RED", "s": "RED"}, 2])
        s.append([{"v": "a & <b>", "link": "https://example.com/a b"}, 3])
        s.add_chart("t", (2, 1, 3, 1), [("n", (2, 2, 3, 2), "D03B3B")])
        p = wb.save(os.path.join(self.d, "w.xlsx"))
        with zipfile.ZipFile(p) as z:
            for n in z.namelist():
                if n.endswith((".xml", ".rels")):
                    etree.fromstring(z.read(n))  # raises on malformed XML
            chart = z.read("xl/charts/chart1.xml").decode()
            self.assertIn("<c:numCache>", chart)
            self.assertIn("'Sum- -x-'!$B$2:$B$3", chart)
            self.assertIn("xl/worksheets/_rels/sheet1.xml.rels", z.namelist())
            self.assertNotIn("<c:dPt>", chart)  # no per-point colours unless asked

    def test_point_colours_are_written_in_schema_order(self):
        wb = xlsx_lite.Workbook()
        s = wb.sheet("S")
        s.append(["k", "n"])
        s.append(["RED", 2])
        s.append(["GREEN", 3])
        s.add_chart("t", (2, 1, 3, 1), [("n", (2, 2, 3, 2), "5C5C58", ["D03B3B", "0CA30C"])])
        with zipfile.ZipFile(wb.save(os.path.join(self.d, "p.xlsx"))) as z:
            chart = z.read("xl/charts/chart1.xml").decode()
        etree.fromstring(chart.encode())
        self.assertEqual(chart.count("<c:dPt>"), 2)
        self.assertLess(chart.index("<c:invertIfNegative"), chart.index("<c:dPt>"))
        self.assertLess(chart.rindex("</c:dPt>"), chart.index("<c:cat>"))
        self.assertIn('<c:idx val="1"/><c:invertIfNegative val="0"/><c:bubble3D val="0"/><c:spPr><a:solidFill>'
                      '<a:srgbClr val="0CA30C"/>', chart)


class ReviewVisualsTests(Base):
    def test_consistent_review_and_plan_pass_and_html_has_no_script(self):
        html, xl = os.path.join(self.d, "d.html"), os.path.join(self.d, "a.xlsx")
        code, out = self.run_main(review_visuals, [self.write("r.json", review()), self.write("p.json", PLAN),
                                                   "--topics", self.topics, "--html", html, "--xlsx", xl])
        self.assertEqual(code, 0, out)
        self.assertEqual(out["counts"]["rating"], {"RED": 1, "YELLOW": 1, "GREEN": 1, "N/A": 0})
        self.assertEqual(out["counts"]["priority"]["Medium"], 1)
        with open(html, encoding="utf-8") as f:
            text = f.read().lower()
        self.assertNotIn("<script", text)
        self.assertNotIn("http://", text)
        # the Cowork preview runs scripts with internet access (POC gap #13): no event handlers either
        self.assertIsNone(re.search(r"<[^>]*\son[a-z]+\s*=", text))
        self.assertTrue(zipfile.is_zipfile(xl))
        with zipfile.ZipFile(xl) as z:
            c1 = z.read("xl/charts/chart1.xml").decode()
        self.assertIn('<a:srgbClr val="D03B3B"/>', c1)
        self.assertIn('<a:srgbClr val="0CA30C"/>', c1)

    def test_unknown_topic_is_bad_input(self):
        r = review()
        r["topics"][2]["topic"] = "nda.made-up"
        code, out = self.run_main(review_visuals, [self.write("r.json", r), self.write("p.json", PLAN), "--topics", self.topics])
        self.assertEqual(code, 2)
        self.assertIn("unknown topic", json.dumps(out))

    def test_missing_checklist_topic_is_bad_input(self):
        r = review()
        del r["topics"][2]
        code, out = self.run_main(review_visuals, [self.write("r.json", r), self.write("p.json", PLAN), "--topics", self.topics])
        self.assertEqual(code, 2)
        self.assertIn("nda.remedies", json.dumps(out))

    def test_plan_edit_on_topic_marked_none_is_inconsistent(self):
        r = review()
        r["topics"][0]["action"] = "none"
        code, out = self.run_main(review_visuals, [self.write("r.json", r), self.write("p.json", PLAN), "--topics", self.topics])
        self.assertEqual(code, 3)
        self.assertTrue(any("nda.term" in p for p in out["problems"]))

    def test_edit_action_without_plan_edit_is_inconsistent(self):
        plan = dict(PLAN, edits=[])
        code, out = self.run_main(review_visuals, [self.write("r.json", review()), self.write("p.json", plan), "--topics", self.topics])
        self.assertEqual(code, 3)


class LibraryGridTests(Base):
    def setup_docs(self, quote):
        lib = os.path.join(self.d, "lib")
        os.makedirs(lib)
        r = review()
        r["topics"][1]["quote"] = quote
        with open(os.path.join(lib, "a.review.json"), "w", encoding="utf-8") as f:
            json.dump(r, f)
        with open(os.path.join(lib, "a.clauses.json"), "w", encoding="utf-8") as f:
            json.dump({"sections": [{"text": "5. Term. This lasts five (5) years."},
                                    {"text": "8. Residuals. Information retained in the unaided   memory of staff."}]}, f)
        return lib

    def test_quotes_found_after_normalising(self):
        lib = self.setup_docs("Retained in the unaided memory")  # case, non-breaking space and spacing differ
        code, out = self.run_main(library_grid, [lib, "--topics", self.topics, "--clauses-dir", lib,
                                                 "--xlsx", os.path.join(self.d, "g.xlsx"), "--html", os.path.join(self.d, "g.html")])
        self.assertEqual(code, 0, out)
        self.assertEqual(out["citations"]["NOT FOUND"], 0)
        self.assertEqual(out["citations"]["verified"], 2)

    def test_paraphrased_quote_is_flagged(self):
        lib = self.setup_docs("staff may keep what they remember")
        code, out = self.run_main(library_grid, [lib, "--topics", self.topics, "--clauses-dir", lib])
        self.assertEqual(code, 0)
        self.assertEqual(out["not_found"], ["a.docx · nda.residuals"])

    def test_ellipsis_quote_checks_each_part(self):
        self.assertEqual(library_grid.check_quote("This lasts ... five (5) years", library_grid.norm("5. This lasts five (5) years.")), "verified")
        self.assertEqual(library_grid.check_quote("This lasts ... ten years", library_grid.norm("5. This lasts five (5) years.")), "NOT FOUND")

    def test_invalid_review_writes_nothing(self):
        lib = self.setup_docs("x")
        with open(os.path.join(lib, "b.review.json"), "w", encoding="utf-8") as f:
            json.dump(review(topics=[{"topic": "nda.term", "rating": "PURPLE", "status": "fallback"}]), f)
        xl = os.path.join(self.d, "g.xlsx")
        code, out = self.run_main(library_grid, [lib, "--topics", self.topics, "--xlsx", xl])
        self.assertEqual(code, 2)
        self.assertFalse(os.path.exists(xl))


if __name__ == "__main__":
    unittest.main()
