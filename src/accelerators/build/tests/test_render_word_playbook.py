"""Word copy (four-column contract-review playbook shape): the checker passes on what the renderer
writes, and fires on a fifth column, an edited cell, a missing row and a heading-layout file."""
import os
import shutil
import sys
import tempfile
import unittest
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.dirname(HERE)
ROOT = os.path.dirname(BUILD)
sys.path[:0] = [BUILD, os.path.join(ROOT, "core", "engines")]
import docx  # noqa: E402
import build_accelerator  # noqa: E402
import playbook_parse  # noqa: E402
import render_word_playbook as rw  # noqa: E402

BASELINE = os.path.join(ROOT, "practices", "nda", "positions", "baseline-mutual.md")


class WordTableTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp()
        cls.cat = build_accelerator.generate_topic_files(os.path.join(ROOT, "practices", "nda"), cls.tmp)
        sides = {t["id"]: set(t["sides"]) for t in cls.cat["topics"] if t.get("status") != "retired"}
        with open(BASELINE, encoding="utf-8") as f:
            cls.pb = playbook_parse.parse(f.read(), sides)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp)

    def render(self, name, topic_ids="none"):
        return rw.render_table(self.pb, self.cat, os.path.join(self.tmp, name), topic_ids)

    def test_rendered_copy_passes_for_every_id_placement(self):
        for placement in rw.TOPIC_ID_PLACEMENTS:
            with self.subTest(placement=placement):
                path = self.render(f"ok-{placement}.docx", placement)
                self.assertEqual(rw.check_table(path, self.pb, self.cat, placement), [])

    def test_shape_matches_microsoft(self):
        d = docx.Document(self.render("shape.docx"))
        self.assertEqual(len(d.tables), 1)
        t = d.tables[0]
        self.assertEqual(tuple(c.text for c in t.rows[0].cells), rw.COLUMNS)
        self.assertEqual(len(t.rows) - 1, len(self.pb["topics"]) + len(self.pb["silent_topics"]))
        body = [p for p in d.paragraphs if p.text.strip()]
        self.assertEqual([p.style.name for p in body], ["Title", "Normal"])  # concise title + one scope paragraph
        self.assertNotIn("nda.", "".join(c.text for r in t.rows for c in r.cells))  # default: no ids
        self.assertNotIn("MUST", "".join(p.text for p in d.paragraphs))  # no run-rule narrative (backlog 3-5)

    def test_name_scope_and_example_text(self):
        self.assertEqual(rw.word_name(self.pb), "NDA - Mutual (Spaarke baseline) v0.1.0")
        self.assertEqual(rw.word_name({"title": "Acme NDA – Mutual v1.1", "meta": {"version": "1.1"}}),
                         "Acme NDA – Mutual v1.1")
        scope = rw.scope_paragraph(self.pb)
        self.assertTrue(scope.startswith("These are balanced positions"))
        self.assertIn("Side: mutual (apply every position to both parties)", scope)
        self.assertEqual(rw.example_text(", or that a reasonable person"), "…or that a reasonable person")
        self.assertEqual(rw.example_text("Each party is responsible"), "Each party is responsible")
        self.assertEqual(rw.example_text(None), "")

    def test_instructions_carry_fallback_and_escalation_and_fallback_stays_in_compromise(self):
        # gap #30: Word keeps only Rules (Instructions) + Examples, so the fallback is repeated in Instructions
        rows = {r[0][0]: r for r in rw.table_rows(self.pb, self.cat)}
        topic = next(t for t in self.pb["topics"] if t.get("escalate") and t.get("fallback"))
        title = next(t["title"] for t in self.cat["topics"] if t["id"] == topic["id"])
        _, instr, _, comp = rows[title]
        self.assertEqual(instr[0], topic["preferred"])
        self.assertEqual(instr[1], f"{rw.FALLBACK_LABEL}: {topic['fallback']}")
        self.assertEqual(instr[2], f"Escalate if: {topic['escalate']}")
        self.assertEqual(comp, [topic["fallback"]])
        no_fb = next(t for t in self.pb["topics"] if not t.get("fallback"))
        self.assertFalse(any(p.startswith("Acceptable fallback") for p in rw.instruction_paragraphs(no_fb, "none")))

    def test_if_missing_is_parsed_rendered_and_survives_import(self):
        # gap #31: absence topics carry an imperative "If missing" line; the importer reads our copy back
        import playbook_import
        tid = self.pb["topics"][0]["id"]
        text = ("# Playbook: Test NDA – Mutual v9.9\nVersion: 9.9 | Side: mutual\n\n"
                f"## topic: {tid}\nPreferred: Keep it.\nFallback: Accept less.\nProhibited / escalate: Nothing at all.\n"
                "If missing: Add the clause from the Example Text.\nModel language: \"The clause.\"\nPriority: High\n")
        sides = {t["id"]: set(t["sides"]) for t in self.cat["topics"] if t.get("status") != "retired"}
        pb = playbook_parse.parse(text, sides)
        self.assertEqual(pb["topics"][0]["if_missing"], "Add the clause from the Example Text.")
        instr = rw.instruction_paragraphs(pb["topics"][0], "none")
        self.assertEqual(instr, ["Keep it.", "If missing: Add the clause from the Example Text.",
                                 f"{rw.FALLBACK_LABEL}: Accept less.", "Escalate if: Nothing at all."])
        # non-contract type (X14): Side may be omitted only when the caller says so
        no_side = text.replace(" | Side: mutual", "")
        with self.assertRaises(playbook_parse.FormatError):
            playbook_parse.parse(no_side, sides)
        nc = playbook_parse.parse(no_side, {tid: set()}, require_side=False)
        self.assertNotIn("side", nc["meta"])
        self.assertEqual(nc["silent_topics"], [])
        pref, esc, missing = playbook_import.split_instructions(" ".join(instr))
        self.assertEqual((pref, missing), ("Keep it.", "Add the clause from the Example Text."))
        self.assertEqual(esc, "Nothing at all.")

    def test_checker_fires_on_edited_cell_and_missing_row(self):
        path = self.render("edited.docx")
        d = docx.Document(path)
        d.tables[0].rows[1].cells[1].text = "Accept anything."
        d.tables[0]._tbl.remove(d.tables[0].rows[-1]._tr)
        d.save(path)
        problems = rw.check_table(path, self.pb, self.cat)
        self.assertTrue(any("cell text differs" in p for p in problems), problems)
        self.assertTrue(any("topic rows, expected" in p for p in problems), problems)

    def test_checker_fires_on_fifth_column(self):
        path = self.render("five.docx")
        d = docx.Document(path)
        d.tables[0].add_column(docx.shared.Inches(1))
        d.save(path)
        problems = rw.check_table(path, self.pb, self.cat)
        self.assertTrue(any("header row" in p for p in problems), problems)

    def test_checker_fires_on_heading_layout(self):
        path = rw.render(self.pb, self.cat, os.path.join(self.tmp, "headings.docx"))
        self.assertIn("expected exactly 1 table, found 0", rw.check_table(path, self.pb, self.cat))

    def test_readable_copy_has_no_tables_and_one_heading_per_topic(self):
        path = rw.render_readable(self.pb, self.cat, os.path.join(self.tmp, "readable.docx"))
        self.assertEqual(rw.check_readable(path, self.pb, self.cat), [])
        d = docx.Document(path)
        self.assertEqual(len(d.tables), 0)
        heads = [p.text for p in d.paragraphs if p.style.name == "Heading 2"]
        self.assertEqual(len(heads), len(self.pb["topics"]) + len(self.pb["silent_topics"]))
        first = self.pb["topics"][0]
        title = next(t["title"] for t in self.cat["topics"] if t["id"] == first["id"])
        self.assertEqual(heads[0], f"1. {title} ({first['id']})")
        body = [p.text for p in d.paragraphs if p.text.strip()]
        self.assertEqual([p.style.name for p in d.paragraphs[:2]], ["Title", "Normal"])
        self.assertTrue(body[1].startswith("Version 0.1.0"), body[1])
        self.assertIn(f"Instructions: {first['preferred']}", body)

    def test_readable_copy_lists_only_the_fields_a_topic_has(self):
        blocks = dict(rw.readable_blocks(self.pb, self.cat))
        full = next(t for t in self.pb["topics"] if t.get("escalate") and t.get("fallback") and t.get("model_language"))
        bare = next(t for t in self.pb["topics"] if not t.get("fallback") and not t.get("if_missing"))
        labels = lambda t: [lbl for lbl, _ in next(f for h, f in blocks.items() if f"({t['id']})" in h)]  # noqa: E731
        self.assertEqual(labels(full)[0], "Instructions")
        self.assertIn(rw.FALLBACK_LABEL, labels(full))
        self.assertIn("Escalate if", labels(full))
        self.assertIn("Example text", labels(full))
        self.assertNotIn(rw.FALLBACK_LABEL, labels(bare))
        self.assertNotIn("If missing", labels(bare))

    def test_readable_copy_sets_a_real_created_date(self):
        path = rw.render_readable(self.pb, self.cat, os.path.join(self.tmp, "dated.docx"))
        created = docx.Document(path).core_properties.created
        self.assertGreaterEqual(created.year, 2026)  # python-docx's own default is 2013-12-23
        fixed = datetime(2026, 11, 10, 9, 30, tzinfo=timezone.utc)
        path = rw.render_readable(self.pb, self.cat, os.path.join(self.tmp, "fixed.docx"), created=fixed)
        self.assertEqual(docx.Document(path).core_properties.created, fixed)
        self.assertEqual(docx.Document(path).core_properties.title, rw.word_name(self.pb))

    def test_readable_checker_fires_on_a_table_an_edited_paragraph_and_a_missing_heading(self):
        path = rw.render_readable(self.pb, self.cat, os.path.join(self.tmp, "bad.docx"))
        d = docx.Document(path)
        d.add_table(rows=1, cols=2)
        d.paragraphs[4].text = "Instructions: Accept anything."  # first topic's labelled paragraph or heading area
        h2 = [p for p in d.paragraphs if p.style.name == "Heading 2"][-1]
        h2._p.getparent().remove(h2._p)
        d.save(path)
        problems = rw.check_readable(path, self.pb, self.cat)
        self.assertTrue(any("no tables" in p for p in problems), problems)
        self.assertTrue(any("headings differ" in p for p in problems), problems)
        table_layout = rw.render_table(self.pb, self.cat, os.path.join(self.tmp, "table.docx"))
        self.assertTrue(rw.check_readable(table_layout, self.pb, self.cat))  # the four-column copy is not readable

    def test_parser_keeps_the_introduction(self):
        self.assertTrue(self.pb["intro"].startswith("These are balanced positions for a mutual NDA"))
        self.assertTrue(self.pb["intro"].endswith("reported as silent."))


if __name__ == "__main__":
    unittest.main()
