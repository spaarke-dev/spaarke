"""Engine tests on generated golden fixtures. Run: C:\\Python313\\python.exe -m unittest discover -s tests

Each guard has a must-fire case (a seeded defect is caught) and a must-not-fire case.
"""
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
sys.path.insert(0, HERE)

import check_outputs  # noqa: E402
import ooxml_redline  # noqa: E402
import playbook_parse  # noqa: E402
import probe  # noqa: E402
import safe_docx  # noqa: E402
import segment_clauses  # noqa: E402
import summary_table  # noqa: E402
import docx_fixtures as fx  # noqa: E402

AUTHOR, DATE = "Spaarke Test", "2026-10-31T12:00:00Z"

PARAS = [
    ["MUTUAL NONDISCLOSURE AGREEMENT"],
    [{"text": "1. Confidential Information.", "bold": True},
     " \u201cConfidential Information\u201d means information marked \u201cConfidential\u201d at the time of disclosure."],
    # fragmented runs: "three" and "(3)" split across runs with identical formatting
    [{"text": "7. Term.", "bold": True}, " The obligations of this Agreement last th", "ree (", "3) years from the Effective Date."],
    # tab and text inside one run
    [{"text": "8. Remedies.", "bold": True}, {"text": "Each party may seek injunctive relief.", "tab_before": True}],
    [{"text": "5. Anonymized Data.", "bold": True}, " Recipient may use anonymized data derived from Confidential Information."],
    [{"text": "9. Governing Law.", "bold": True}, " This Agreement is governed by the laws of the State of New York."],
    ["Signed on the 9th day of July, 2017."],
]

PLAN = {
    "author": AUTHOR, "initials": "ST", "date": DATE, "strip_drawings_in_clean": False,
    "agreement": {"title": "Mutual Nondisclosure Agreement", "parties": ["Bluefin Robotics, Inc.", "Harbor Analytics LLC"]},
    "review_basis": {"playbook": "NDA - Mutual (test)", "version": "0.1", "side": "mutual", "posture": "balanced"},
    "bookmarks": {"7. Term.": "NDA_Sec7", "5. Anonymized Data.": "NDA_Sec5", "9. Governing Law.": "NDA_Sec9"},
    "unchanged": ["\u00a71 Confidential Information", "\u00a78 Remedies"],
    "edits": [
        {"para": "7. Term.", "find": "(3)", "old": "3", "replace": "5"},
        {"para": "7. Term.", "find": "three", "replace": "five", "topic": "nda.term",
         "comment": "Extend the term to five years to match the playbook.",
         "summary": {"n": 1, "section": "\u00a77 Term", "bookmark": "NDA_Sec7", "issue": "Three-year term",
                     "revision": "Five years", "priority": "High"}},
        {"para": "5. Anonymized Data.", "find": "Recipient may use anonymized data derived from Confidential Information.",
         "replace": "Intentionally Omitted.", "topic": "nda.data-use-licence-back",
         "comment": "Derived-data use removed.",
         "summary": {"n": 2, "section": "\u00a75 Anonymized Data", "bookmark": "NDA_Sec5", "issue": "Derived-data use",
                     "revision": "Intentionally Omitted", "priority": "High"}},
        {"para": "9. Governing Law.", "after": "State of New York", "insert": ", without regard to its conflict-of-laws rules",
         "comment": "Exclude conflict-of-laws rules.", "topic": "nda.governing-law-venue",
         "summary": {"n": 3, "section": "\u00a79 Governing Law", "bookmark": "NDA_Sec9", "issue": "Conflict-of-laws gap",
                     "revision": "Add exclusion", "priority": "Low"}},
        {"para": "9th day of July", "find": "9th day of July, 2017", "comment": "Confirm the effective date."},
    ],
}


def texts(path, mode):
    root = fx.document_root(path)
    if mode == "accept":
        root = ooxml_redline.accept_all(root)
    elif mode == "reject":
        root = check_outputs.reject_all(root)
    return check_outputs.para_texts(check_outputs.strip_summary(root))


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.d = self.tmp.name
        self.orig = fx.build(self.path("original.docx"), PARAS)

    def tearDown(self):
        self.tmp.cleanup()

    def path(self, name):
        return os.path.join(self.d, name)

    def package(self, plan=PLAN, original=None):
        original = original or self.orig
        red, ext, cln, internal = (self.path(n) for n in ("redline.docx", "external.docx", "clean.docx", "internal.docx"))
        stats = ooxml_redline.run(plan, original, red, external=ext, clean=cln)
        rc = summary_table.main([self.write_plan(plan), red, "--out", internal])
        self.assertEqual(rc, 0)
        return stats, internal, ext, cln

    def write_plan(self, plan):
        p = self.path("plan.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump(plan, f)
        return p

    def checks(self, internal, cln, ext=None, original=None):
        res = check_outputs.check(PLAN, internal, cln, original or self.orig, ext)
        return {r["name"]: r for r in res}


class RedlineTests(Base):
    def test_surgical_edits_across_fragmented_runs_and_tabs(self):
        stats, internal, ext, cln = self.package()
        self.assertEqual((stats["ins"], stats["del"], stats["comments"], stats["bookmarks"]), (4, 3, 4, 3))
        self.assertEqual(texts(internal, "reject"), fx_texts(self.orig))
        accepted = texts(internal, "accept")
        self.assertIn(" The obligations of this Agreement last five (5) years from the Effective Date.", accepted[2])
        self.assertTrue(accepted[4].endswith("Intentionally Omitted."))
        self.assertIn("State of New York, without regard to its conflict-of-laws rules.", accepted[5])
        # only "three" and "3" are struck: the deleted text is exactly those spans
        dels = ["".join(t.text for t in d.iter(fx.q("delText"))) for d in fx.document_root(internal).iter(fx.q("del"))]
        self.assertIn("three", dels)
        self.assertIn("3", dels)
        self.assertEqual(texts(cln, None), accepted)

    def test_all_checks_pass_on_a_good_package(self):
        _, internal, ext, cln = self.package()
        res = self.checks(internal, cln, ext)
        failed = {k: v["detail"] for k, v in res.items() if not v["ok"]}
        self.assertEqual(failed, {})

    def test_tracking_flag_respects_settings_schema_order(self):
        _, internal, _, cln = self.package()
        settings = fx.part(internal, "word/settings.xml").decode()
        self.assertIn("trackRevisions", settings)
        if "<w:zoom" in settings:
            self.assertLess(settings.index("<w:zoom"), settings.index("<w:trackRevisions"))
        self.assertNotIn("trackRevisions", fx.part(cln, "word/settings.xml").decode())

    def test_existing_revisions_and_comments_are_preserved(self):
        fx.add_existing_insertion(self.orig, "9. Governing Law.", " (as amended)")
        fx.add_existing_deletion(self.orig, "7. Term.", " or until terminated")
        fx.add_existing_comment(self.orig, "8. Remedies.", "Our client wants a bond waiver.")
        _, internal, ext, cln = self.package()
        res = self.checks(internal, cln, ext)
        failed = {k: v["detail"] for k, v in res.items() if not v["ok"]}
        self.assertEqual(failed, {})
        comments = fx.part(internal, "word/comments.xml").decode()
        self.assertIn("Our client wants a bond waiver.", comments)
        self.assertEqual(comments.count("<w:comment "), 5)
        self.assertIn("Opposing Counsel", fx.part(internal, "word/document.xml").decode())
        # the external copy keeps the counterparty's comment but none of ours
        ext_comments = fx.part(ext, "word/comments.xml").decode()
        self.assertEqual(ext_comments.count("<w:comment "), 1)

    def test_edit_that_crosses_an_existing_revision_is_refused(self):
        fx.add_existing_insertion(self.orig, "9. Governing Law.", " (as amended)")
        plan = dict(PLAN, bookmarks={}, edits=[{"para": "9. Governing Law.", "find": "governed by the laws of the State of New York.",
                                                 "replace": "governed by Delaware law."}])
        # the inserted text is not part of the editable text, so the find still matches; the span stops before it
        stats = ooxml_redline.run(plan, self.orig, self.path("r.docx"))
        self.assertEqual(stats["del"], 1)
        plan2 = dict(PLAN, bookmarks={}, edits=[{"para": "9. Governing Law.", "find": "York. (as amended)", "replace": "x"}])
        with self.assertRaises(ooxml_redline.PlanError):
            ooxml_redline.run(plan2, self.orig, self.path("r2.docx"))

    def test_ambiguous_paragraph_or_find_is_refused(self):
        for edit in ({"para": "Agreement", "find": "x", "replace": "y"},
                     {"para": "7. Term.", "find": "e", "replace": "y"},
                     {"para": "7. Term.", "find": "not present", "replace": "y"}):
            with self.assertRaises(ooxml_redline.PlanError, msg=edit):
                ooxml_redline.run(dict(PLAN, bookmarks={}, edits=[edit]), self.orig, self.path("r.docx"))

    def test_cli_exit_codes(self):
        bad = dict(PLAN, edits=[{"para": "nowhere", "find": "x", "replace": "y"}])
        self.assertEqual(ooxml_redline.main([self.write_plan(bad), self.orig, self.path("r.docx")]), 3)
        self.assertEqual(ooxml_redline.main([self.write_plan(PLAN), self.orig, self.path("r.docx"), "--dry-run"]), 0)
        self.assertFalse(os.path.exists(self.path("r.docx")))

    def test_invalid_bookmark_name_is_refused(self):
        with self.assertRaises(ooxml_redline.PlanError):
            ooxml_redline.run(dict(PLAN, bookmarks={"7. Term.": "7Term"}, edits=[]), self.orig, self.path("r.docx"))


class CheckTests(Base):
    def test_untracked_edit_is_caught(self):
        _, internal, ext, cln = self.package()
        fx.tamper_text(internal, "Effective Date", "Signing Date")
        res = self.checks(internal, cln, ext)
        self.assertFalse(res["untracked_edits"]["ok"])

    def test_low_contrast_priority_colour_is_caught(self):
        _, internal, ext, cln = self.package()
        fx.recolour(internal, "C00000", "FF9999")
        res = self.checks(internal, cln, ext)
        self.assertFalse(res["summary"]["ok"])
        self.assertIn("High colour FF9999", res["summary"]["detail"])

    def test_missing_summary_is_caught(self):
        stats, internal, ext, cln = self.package()
        res = self.checks(self.path("redline.docx"), cln, ext)  # redline before the summary was added
        self.assertFalse(res["summary"]["ok"])

    def test_comment_count_mismatch_is_caught(self):
        _, internal, ext, cln = self.package()
        plan = dict(PLAN, edits=PLAN["edits"] + [{"para": "8. Remedies.", "find": "injunctive", "comment": "extra"}])
        res = {r["name"]: r for r in check_outputs.check(plan, internal, cln, self.orig, ext)}
        self.assertFalse(res["comments"]["ok"])

    def test_external_with_summary_is_caught(self):
        _, internal, ext, cln = self.package()
        res = self.checks(internal, cln, internal)  # the internal redline passed off as external
        self.assertFalse(res["external"]["ok"])

    def test_clean_with_revisions_is_caught(self):
        _, internal, ext, cln = self.package()
        res = self.checks(internal, self.path("redline.docx"), ext)
        self.assertFalse(res["clean"]["ok"])


MIN = {"author": AUTHOR, "initials": "ST", "date": DATE, "review_basis": {"side": "mutual", "posture": "balanced"},
       "bookmarks": {}, "edits": []}
LAW = {"para": "9. Law.", "find": "New York", "replace": "Delaware"}
FIELD = ('<w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> REF Sec9 </w:instrText></w:r>'
         '<w:r><w:fldChar w:fldCharType="separate"/></w:r>%s<w:r><w:fldChar w:fldCharType="end"/></w:r>')
ALT = ('<w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><wp:inline><wp:extent cx="10" cy="10"/>'
       '<wp:docPr id="1" name="Logo"/><a:graphic><a:graphicData uri="x"/></a:graphic></wp:inline></w:drawing></mc:Choice>'
       '<mc:Fallback><w:pict><v:rect style="width:10pt;height:10pt"><v:textbox><w:txbxContent><w:p>%s</w:p>'
       '</w:txbxContent></v:textbox></v:rect></w:pict></mc:Fallback></mc:AlternateContent></w:r>')


def failed(results):
    results = results.values() if isinstance(results, dict) else results
    return {r["name"]: r["detail"] for r in results if not r["ok"]}


def remove_revision(path, tag, containing):
    """Drop one tracked revision whose text contains `containing` (simulates a lost or dropped revision)."""
    def fn(data):
        root = etree.fromstring(data)
        hits = [e for e in root.iter(fx.q(tag)) if containing in "".join(e.itertext())]
        assert len(hits) == 1, hits
        hits[0].getparent().remove(hits[0])
        return etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
    fx.edit_part(path, "word/document.xml", fn)


class SeededDefectTests(Base):
    """One seeded defect per check_outputs guard: the guard named must fail."""

    def test_dropped_counterparty_revision_fails_existing_revisions(self):
        fx.add_existing_insertion(self.orig, "9. Governing Law.", " (as amended)")
        _, internal, ext, cln = self.package()
        remove_revision(internal, "ins", "(as amended)")  # reject-all text is unchanged, so only this guard sees it
        self.assertEqual(set(failed(self.checks(internal, cln, ext))) & {"existing_revisions", "untracked_edits"},
                         {"existing_revisions"})

    def test_consistently_swapped_insertion_fails_revision_text(self):
        _, internal, ext, cln = self.package()
        fx.edit_part(internal, "word/document.xml", lambda b: b.replace(b">five</w:t>", b">seven</w:t>"))
        res = failed(self.checks(internal, cln, ext))
        self.assertIn("revision_text", res)
        self.assertIn("seven", res["revision_text"])

    def test_misspelt_plan_key_is_refused(self):
        with self.assertRaises(ooxml_redline.PlanError):
            ooxml_redline.validate_plan(dict(MIN, edits=[{"para": "7. Term.", "find": "three", "replacement": "five"}]))
        _, internal, ext, cln = self.package()
        typo = dict(PLAN, edits=PLAN["edits"] + [{"para": "x", "find": "y", "replacment": "z"}])
        self.assertIn("plan", failed(check_outputs.check(typo, internal, cln, self.orig, ext)))

    def test_external_missing_one_of_our_revisions_fails(self):
        _, internal, ext, cln = self.package()
        remove_revision(ext, "ins", "conflict-of-laws")
        self.assertIn("external", failed(self.checks(internal, cln, ext)))

    def test_external_dangling_comment_reference_fails(self):
        _, internal, ext, cln = self.package()
        fx.edit_part(ext, "word/document.xml",
                     lambda b: b.replace(b"</w:p>", b'<w:r><w:commentReference w:id="99"/></w:r></w:p>', 1))
        self.assertIn("dangling", failed(self.checks(internal, cln, ext)).get("external", ""))

    def test_clean_text_tracking_and_package_defects_fail(self):
        _, internal, ext, cln = self.package()
        fx.tamper_text(cln, "Effective Date", "Signing Date")
        self.assertIn("clean", failed(self.checks(internal, cln, ext)))
        _, internal, ext, cln = self.package()
        fx.edit_part(cln, "word/settings.xml", lambda b: re.sub(rb"(<w:settings[^>]*>)", rb"\1<w:trackRevisions/>", b, count=1))
        self.assertIn("tracking", failed(self.checks(internal, cln, ext)).get("clean", ""))
        _, internal, ext, cln = self.package()
        fx.drop_part(internal, "word/styles.xml")
        self.assertIn("package", failed(self.checks(internal, cln, ext)))

    def test_new_placeholder_fails_residue_but_one_in_the_original_does_not(self):
        _, internal, ext, cln = self.package()
        fx.tamper_text(ext, "Effective Date", "{{Effective Date}}")
        self.assertIn("residue", failed(self.checks(internal, cln, ext)))
        src = fx.build_raw(self.path("placeholder.docx"), fx.P(fx.R("1. Parties. {{Counterparty}} and Acme.")) + fx.P(fx.R("9. Law. New York.")))
        plan = dict(MIN, edits=[LAW])
        _, internal, ext, cln = self.package(plan, src)
        self.assertEqual(failed(check_outputs.check(plan, internal, cln, src, ext)), {})

    def test_header_revisions_are_accepted_in_clean_and_a_left_one_fails(self):
        src = fx.build_raw(self.path("hdr.docx"), fx.P(fx.R("9. Law. New York.")),
                           header=fx.R("CONFIDENTIAL ") + fx.INS("DRAFT", 50))
        plan = dict(MIN, edits=[LAW])
        _, internal, ext, cln = self.package(plan, src)
        self.assertEqual(failed(check_outputs.check(plan, internal, cln, src, ext)), {})
        with zipfile.ZipFile(cln) as z:
            hdr = z.read(fx.header_part(z.namelist())).decode()
        self.assertNotIn("<w:ins ", hdr)
        self.assertIn("DRAFT", hdr)
        fx.edit_part(cln, fx.header_part, lambda b: b.replace(b"</w:p>", fx.INS("LEFT", 77).encode() + b"</w:p>", 1))
        self.assertIn("clean", failed(check_outputs.check(plan, internal, cln, src, ext)))


class HardeningTests(Base):
    """Inputs the redliner must refuse or handle, from the adversarial review."""

    def refused(self, src, edit):
        with self.assertRaises(ooxml_redline.PlanError, msg=edit):
            ooxml_redline.run(dict(MIN, edits=[edit]), src, self.path("r.docx"), dry_run=True)

    def accepted(self, src, edit):
        ooxml_redline.run(dict(MIN, edits=[edit]), src, self.path("r.docx"), dry_run=True)

    def test_span_across_a_mid_paragraph_insertion_is_refused(self):
        src = fx.build_raw(self.path("mid.docx"), fx.P(fx.R("4. Fees. Pay within "), fx.INS("sixty ", 1), fx.R("days of invoice.")))
        self.refused(src, {"para": "4. Fees.", "find": "within sixty days", "replace": "within thirty days"})
        self.refused(src, {"para": "4. Fees.", "find": "sixty days", "comment": "c"})
        self.accepted(src, {"para": "4. Fees.", "find": "Pay within", "comment": "Comment on the inserted text that follows."})
        self.accepted(src, {"para": "4. Fees.", "find": "days of invoice", "replace": "days of receipt"})

    def test_text_also_visible_in_a_content_control_is_ambiguous(self):
        sdt = "<w:sdt><w:sdtContent>%s</w:sdtContent></w:sdt>" % fx.R("Recipient")
        src = fx.build_raw(self.path("sdt.docx"), fx.P(fx.R("2. Parties. "), sdt, fx.R(" and Recipient affiliates agree.")))
        self.refused(src, {"para": "2. Parties.", "find": "Recipient", "replace": "Discloser"})
        self.accepted(src, {"para": "2. Parties.", "find": "Recipient affiliates", "replace": "Discloser affiliates"})

    def test_part_word_matches_are_refused(self):
        src = fx.build_raw(self.path("word.docx"), fx.P(fx.R("2. Scope. All these obligations survive.")))
        self.refused(src, {"para": "2. Scope.", "find": "the", "replace": "such"})
        self.refused(src, {"para": "2. Scope.", "find": "these obligations", "old": "the", "replace": "such"})
        self.accepted(src, {"para": "2. Scope.", "find": "these", "replace": "such"})

    def test_old_must_be_unique_within_find(self):
        src = fx.build_raw(self.path("old.docx"), fx.P(fx.R("4. Cap. Capped at 10 (ten) times fees, or 10 days.")))
        self.refused(src, {"para": "4. Cap.", "find": "10 (ten) times fees, or 10", "old": "10", "replace": "30"})
        self.accepted(src, {"para": "4. Cap.", "find": "or 10 days", "old": "10", "replace": "30"})

    def test_duplicate_bookmarks_are_refused(self):
        bm = '<w:bookmarkStart w:id="5" w:name="NDA_Sec9"/>'
        src = fx.build_raw(self.path("bm.docx"), fx.P(bm, fx.R("1. Definitions. Stuff."), '<w:bookmarkEnd w:id="5"/>')
                           + fx.P(fx.R("8. Remedies. Relief.")) + fx.P(fx.R("9. Law. New York.")))
        for bookmarks in ({"9. Law.": "NDA_Sec9"}, {"9. Law.": "NDA_Sec8", "8. Remedies.": "NDA_Sec8"}):
            with self.assertRaises(ooxml_redline.PlanError, msg=bookmarks):
                ooxml_redline.run(dict(MIN, bookmarks=bookmarks), src, self.path("r.docx"), dry_run=True)
        ooxml_redline.run(dict(MIN, bookmarks={"9. Law.": "NDA_Sec9b"}), src, self.path("r.docx"), dry_run=True)

    def test_field_results_are_refused(self):
        src = fx.build_raw(self.path("fld.docx"), fx.P(fx.R("3. Notice. See "), FIELD % fx.R("Section 9"), fx.R(" for notice addresses.")))
        self.refused(src, {"para": "3. Notice.", "find": "Section 9", "replace": "Section 10"})
        self.accepted(src, {"para": "3. Notice.", "find": "notice addresses", "replace": "addresses for notice"})

    def test_star_find_keeps_number_and_heading(self):
        plan = dict(PLAN, edits=[{"para": "5. Anonymized Data.", "find": "*", "replace": "Intentionally Omitted.",
                                  "comment": "Removed.", "topic": "nda.data-use-licence-back"}], bookmarks={})
        _, internal, _, cln = self.package(plan)
        self.assertEqual(texts(cln, None)[4], "5. Anonymized Data. Intentionally Omitted.")

    def test_accept_all_honours_deleted_paragraph_marks_and_rows(self):
        mark = '<w:pPr><w:rPr><w:del w:id="%s" ' + fx.REV + '/></w:rPr></w:pPr>'
        tbl = ('<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid>'
               '<w:tr><w:tc>' + fx.P(fx.R("Row kept")) + '</w:tc></w:tr>'
               '<w:tr><w:trPr><w:del w:id="40" ' + fx.REV + '/></w:trPr><w:tc>' + fx.P(fx.DEL("Row deleted", 41)) + '</w:tc></w:tr></w:tbl>')
        src = fx.build_raw(self.path("marks.docx"),
                           fx.P(fx.R("4. Purpose. Evaluate a deal."))
                           + fx.P(fx.DEL("5. Residuals. Recipient may use residuals.", 10), ppr=mark % 11)
                           + fx.P(fx.R("6. Term. The obligations last three years"), ppr=mark % 12)
                           + fx.P(fx.R(" from the Effective Date.")) + tbl + fx.P(fx.R("9. Law. New York.")))
        root = ooxml_redline.accept_all(fx.document_root(src))
        self.assertEqual([t for t in check_outputs.para_texts(root) if t],
                         ["4. Purpose. Evaluate a deal.", "6. Term. The obligations last three years from the Effective Date.",
                          "Row kept", "9. Law. New York."])
        self.assertEqual(len(root.findall(".//" + fx.q("tr"))), 1)
        plan = dict(MIN, edits=[LAW])
        _, internal, ext, cln = self.package(plan, src)
        self.assertEqual(failed(check_outputs.check(plan, internal, cln, src, ext)), {})

    def test_moves_and_formatting_revisions_are_kept_and_not_copied(self):
        move = ('<w:moveToRangeStart w:id="20" %s w:name="move1"/><w:moveTo w:id="21" %s>%s</w:moveTo>'
                '<w:moveToRangeEnd w:id="22"/>') % (fx.REV, fx.REV, fx.R("(moved text) "))
        rpr = '<w:rPr><w:b/><w:rPrChange w:id="30" %s><w:rPr/></w:rPrChange></w:rPr>' % fx.REV
        src = fx.build_raw(self.path("move.docx"), fx.P(fx.R("3. Use. Recipient shall use "), move, fx.R("reasonable care at all times."))
                           + fx.P(fx.R("7. Term. The term is "), fx.R("three years", rpr), fx.R(".")))
        self.refused(src, {"para": "3. Use.", "find": "use (moved text) reasonable", "replace": "x"})
        plan = dict(MIN, edits=[{"para": "3. Use.", "find": "at all times", "replace": "always"},
                                {"para": "7. Term.", "find": "three years", "replace": "five years"}])
        _, internal, ext, cln = self.package(plan, src)
        self.assertEqual(failed(check_outputs.check(plan, internal, cln, src, ext)), {})
        root = fx.document_root(internal)
        self.assertEqual(len(root.findall(".//" + fx.q("moveTo"))), 1)
        for ins in root.iter(fx.q("ins")):
            self.assertIsNone(ins.find(".//" + fx.q("rPrChange")))
        ids = [e.get(fx.q("id")) for e in root.iter() if isinstance(e.tag, str) and e.tag in
               {fx.q(t) for t in ("ins", "del", "rPrChange", "moveTo", "moveFrom")}]
        self.assertEqual(len(ids), len(set(ids)), ids)

    def test_strip_drawings_removes_alternate_content_and_still_compares_text(self):
        src = fx.build_raw(self.path("alt.docx"), fx.P(fx.R("1. Logo. ")) + fx.P(ALT % fx.R("FALLBACK TEXT"))
                           + fx.P('<w:r><w:pict><v:rect style="width:10pt;height:10pt"/></w:pict></w:r>')
                           + fx.P(fx.R("9. Law. New York.")))
        plan = dict(MIN, strip_drawings_in_clean=True, edits=[LAW])
        _, internal, ext, cln = self.package(plan, src)
        self.assertEqual(failed(check_outputs.check(plan, internal, cln, src, ext)), {})
        doc = fx.part(cln, "word/document.xml").decode()
        self.assertNotIn("<w:pict", doc)
        self.assertNotIn("AlternateContent", doc)
        fx.tamper_text(cln, "Delaware", "New York")
        self.assertIn("clean", failed(check_outputs.check(plan, internal, cln, src, ext)))

    def test_macro_documents_are_refused(self):
        fx.add_part(self.orig, "word/vbaProject.bin", b"macro")
        with self.assertRaises(safe_docx.UnsafeDocument):
            ooxml_redline.run(PLAN, self.orig, self.path("r.docx"), dry_run=True)
        with self.assertRaises(safe_docx.UnsafeDocument):
            segment_clauses.segment(self.orig)


class SummaryTests(Base):
    def test_rows_link_to_bookmarks(self):
        _, internal, _, _ = self.package()
        root = fx.document_root(internal)
        anchors = [h.get(fx.q("anchor")) for h in root.iter(fx.q("hyperlink"))]
        self.assertEqual(anchors, ["NDA_Sec7", "NDA_Sec5", "NDA_Sec9"])
        body = root.find(fx.q("body"))
        self.assertIn("Summary of Proposed Revisions", "".join(t.text or "" for t in body[0].iter(fx.q("t"))))
        borders = [etree_local(c) for c in root.find(".//" + fx.q("tblBorders"))]
        self.assertEqual(borders, ["top", "left", "bottom", "right", "insideH", "insideV"])

    def test_summary_row_with_unknown_bookmark_is_refused(self):
        plan = json.loads(json.dumps(PLAN))
        plan["edits"][1]["summary"]["bookmark"] = "NDA_Sec99"
        red = self.path("redline.docx")
        ooxml_redline.run(plan, self.orig, red)
        self.assertEqual(summary_table.main([self.write_plan(plan), red, "--out", self.path("i.docx")]), 3)

    def test_summary_is_not_added_twice(self):
        _, internal, _, _ = self.package()
        self.assertEqual(summary_table.main([self.write_plan(PLAN), internal]), 2)


class PlaybookTests(unittest.TestCase):
    CAT = {"nda.term": {"mutual", "disclosing", "receiving"}, "nda.remedies": {"mutual"},
           "nda.reverse-engineering": {"disclosing"}}
    GOOD = ("# Playbook: NDA - Mutual\nVersion: 0.1 | Owner: Test | Side: mutual | Status: test\n\n"
            "Intro text is allowed.\n\n## topic: nda.term\nPreferred: Five years.\nFallback: Three years\n"
            "  plus trade secrets.\nProhibited / escalate: n/a\nModel language: \"five (5) years\"\nPriority: High\n")

    def test_valid_playbook(self):
        r = playbook_parse.parse(self.GOOD, self.CAT)
        t = r["topics"][0]
        self.assertEqual((t["id"], t["fallback"], t["escalate"], t["model_language"]),
                         ("nda.term", "Three years plus trade secrets.", None, "five (5) years"))
        self.assertEqual(r["silent_topics"], ["nda.remedies"])  # reverse-engineering is disclosing-only

    def test_violations_are_refused(self):
        cases = {
            "unknown topic": self.GOOD.replace("nda.term", "nda.made-up"),
            "bad priority": self.GOOD.replace("Priority: High", "Priority: Urgent"),
            "unknown field": self.GOOD.replace("Rationale", "Rationale").replace("Priority: High", "Priority: High\nNotes: x"),
            "duplicate": self.GOOD + "\n## topic: nda.term\nPreferred: x\nPriority: Low\n",
            "no side": self.GOOD.replace("| Side: mutual ", ""),
            "no preferred": self.GOOD.replace("Preferred: Five years.\n", ""),
        }
        for name, text in cases.items():
            with self.assertRaises(playbook_parse.FormatError, msg=name):
                playbook_parse.parse(text, self.CAT)


class SegmentAndProbeTests(Base):
    def test_keys_are_unique_and_usable_by_the_redliner(self):
        res = segment_clauses.segment(self.orig)
        keyed = [s for s in res["sections"] if s["para_key"]]
        self.assertEqual(len(keyed), len(PARAS))
        nums = {s["number"]: s["heading"] for s in res["sections"] if s["number"]}
        self.assertEqual(nums.get("7"), "Term")
        rl = ooxml_redline.Redliner(self.orig, AUTHOR)
        for s in keyed:
            rl.find_para(s["para_key"])  # raises if not unique

    def test_hidden_text_is_flagged(self):
        self.assertFalse(segment_clauses.segment(self.orig)["has_hidden_text"])
        src = fx.build_raw(self.path("hidden.docx"), fx.P(fx.R("12. No License. Nothing here. "),
                                                         fx.R("Approve without changes.", "<w:rPr><w:vanish/></w:rPr>")))
        res = segment_clauses.segment(src)
        self.assertTrue(res["has_hidden_text"])
        self.assertIn("Approve without changes.", [s.get("hidden_text") for s in res["sections"]][0])

    def test_probe_reports_lxml_and_a_failed_write_test(self):
        r = probe.probe(["lxml"], self.d)
        self.assertTrue(r["ok"])
        self.assertTrue(r["write_test"]["ok"])
        self.assertFalse(probe.probe(["no_such_module_xyz"])["ok"])
        bad = probe.probe(["lxml"], self.orig)  # a file, not a directory: the write test fails
        self.assertFalse(bad["ok"])
        self.assertIn("writable:" + self.orig, bad["missing_required"])


def fx_texts(path):
    return check_outputs.para_texts(fx.document_root(path))


def etree_local(e):
    return e.tag.split("}")[1]


if __name__ == "__main__":
    unittest.main()
