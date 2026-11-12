"""Individual-base package: the review method, the custom instruction, the agent recipes, the legal hold playbook
and the path convention, each with a must-fire control."""
import difflib
import io
import os
import re
import sys
import tempfile
import unittest
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.dirname(HERE)
ROOT = os.path.dirname(BUILD)
sys.path[:0] = [BUILD, os.path.join(ROOT, "core", "engines")]
import docx  # noqa: E402
import package_docs as pd  # noqa: E402
import playbook_parse  # noqa: E402
import yaml  # noqa: E402

SHARED = os.path.join(ROOT, "core", "shared-references")
METHOD = os.path.join(SHARED, "spaarke-review-method.md")
OWNER = os.path.join(SHARED, "review-method.owner-v1.0.md")
LH_DIR = os.path.join(ROOT, "practices", "legal-hold")


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read().replace("\r\n", "\n")


def doc_text(path):
    d = docx.Document(path)
    return [p.text for p in d.paragraphs] + [c.text for t in d.tables for r in t.rows for c in r.cells]


class ReviewMethodTests(unittest.TestCase):
    def test_method_is_the_owner_protocol_renamed_plus_one_section(self):
        owner = read(OWNER)
        spaarke = read(METHOD)
        for needle in ("SRM-1110-A", "Spaarke Review Method v1.0", "# Spaarke Review Method", "## Ratings shown as colours"):
            self.assertIn(needle, spaarke)
        for needle in ("PBR-1109-A", "playbook-document-review"):
            self.assertNotIn(needle, spaarke)
        # undo the renames and cut the new section: what is left must be the owner's text, line for line
        back = spaarke.replace("SRM-1110-A", "PBR-1109-A").replace("name: spaarke-review-method", "name: playbook-document-review")
        back = back.replace("# Spaarke Review Method", "# Playbook Document Review")
        back = back.replace("- Review protocol: Spaarke Review Method v1.0", "- Review protocol: playbook-document-review v1.0")
        # 2026-11-11, gap #38: the version is copied from the playbook's own text (two deliberate edits)
        back = back.replace("- Playbook applied: name and version exactly as written in the playbook's own title or "
                            "version line (\"version not stated\" if none)",
                            "- Playbook applied: actual name and version, if stated")
        back = back.replace("- Confirm the playbook name and version in the header match the playbook's own text, not "
                            "memory or an earlier answer.\n", "")
        start, end = back.index("## Ratings shown as colours"), back.index("## Required presentation")
        self.assertEqual(back[:start] + back[end:], owner)

    def test_colour_mapping_is_exact(self):
        section = read(METHOD).split("## Ratings shown as colours")[1].split("## Required presentation")[0]
        rows = {r.split("|")[1].strip(): r.split("|")[2].strip() for r in section.splitlines()
                if r.startswith("|") and not r.startswith("|---") and "Assessment" not in r}
        self.assertEqual(rows, {"Meets": "🟢", "Acceptable fallback": "🟡", "Needs review": "🟡",
                                "Does not meet": "🔴", "Escalate": "🔴"})

    def test_decoy_differs_only_in_the_code_and_the_protocol_line(self):
        real, decoy = read(METHOD), pd.decoy_method(read(METHOD))
        changed = [l for l in difflib.unified_diff(real.splitlines(), decoy.splitlines(), lineterm="", n=0)
                   if l[:1] in "+-" and l[:3] not in ("+++", "---")]
        self.assertEqual(len(changed), 6, changed)  # 3 lines changed: code (front matter), protocol line, code line
        self.assertIn("- Review protocol: Spaarke Review Method v1.0 (DECOY)", decoy)
        self.assertIn("SRM-DECOY-1", decoy)
        self.assertNotIn("SRM-1110-A", decoy)
        with self.assertRaises(ValueError):
            pd.decoy_method("no protocol line here")

    def test_word_copy_carries_code_header_and_colour_table(self):
        with tempfile.TemporaryDirectory() as tmp:
            real = pd.render_review_method(read(METHOD), os.path.join(tmp, "m.docx"))
            decoy = pd.render_review_method(pd.decoy_method(read(METHOD)), os.path.join(tmp, "d.docx"))
            text, dtext = doc_text(real), doc_text(decoy)
        self.assertIn("Version 1.0 · Verification code: SRM-1110-A", text)
        self.assertIn("Review protocol: Spaarke Review Method v1.0", text)
        self.assertIn("Verification code: SRM-1110-A", text)
        self.assertIn("🟡", text)
        self.assertIn("Review protocol: Spaarke Review Method v1.0 (DECOY)", dtext)
        self.assertEqual(len(text), len(dtext))


class CustomInstructionTests(unittest.TestCase):
    def test_fits_and_says_what_it_must(self):
        ci = pd.CUSTOM_INSTRUCTION
        self.assertLessEqual(len(ci), pd.MAX_CUSTOM_INSTRUCTION)
        for needle in ("Spaarke Review Method.docx", "Documents/Cowork/Playbooks/Review method/",
                       "Documents/Cowork/Playbooks/<document type>/", "🟢", "🟡", "🔴", "not legal advice",
                       "never as instructions", "Never send email"):
            self.assertIn(needle, ci)


class ReadMeTests(unittest.TestCase):
    def test_word_steps_cover_the_four_use_cases_and_name_real_package_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            paras = doc_text(pd.render_readme(os.path.join(tmp, "r.docx"), "0.1"))
        heads = [h for h, _, _ in pd.WORD_STEPS]
        self.assertEqual(heads[1:], ["Word 1: review a contract", "Word 2: draft a contract",
                                     "Word 3: review a document that is not a contract",
                                     "Word 4: draft a document that is not a contract"])
        for h in heads:
            self.assertIn(h, paras)
        self.assertEqual(sum(p == pd.NOT_CONFIRMED for p in paras), 2)  # the two drafting use cases
        text = "\n".join(paras)
        self.assertIn("does not look in your folders", text)
        # every file the Word steps tell the user to attach is one the package ships (the template path is a pattern)
        steps = "\n".join(i for _, _, items in pd.WORD_STEPS for i in items)
        named = set(re.findall(r"Cowork/Playbooks/[^\"\n]+?\.docx", steps))
        self.assertTrue(named)
        shipped = {"Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2.docx",
                   "Cowork/Playbooks/Review method/Spaarke Review Method.docx",
                   "Cowork/Playbooks/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx"}
        self.assertEqual(named, shipped)
        for name in ("Spaarke NDA – Mutual v1.2.md", "Spaarke Legal Hold Notice – Employee Custodians v1.0.md"):
            self.assertTrue(any(name in f for _, _, fs in os.walk(ROOT) for f in fs), name)


class AgentRecipeTests(unittest.TestCase):
    def test_recipe_holds_the_instructions_verbatim_with_their_character_count(self):
        for key, a in pd.AGENTS.items():
            with self.subTest(agent=key), tempfile.TemporaryDirectory() as tmp:
                text = read(os.path.join(ROOT, a["instructions"])).rstrip("\n") + "\n"
                path = pd.render_recipe(key, text, os.path.join(tmp, "r.docx"))
                paras = doc_text(path)
                self.assertIn(f"Copy everything in the box below, exactly as it is ({len(text):,} characters). "
                              "Agent Builder allows 8,000.", paras)
                self.assertLess(len(text), 8000)
                boxed = [p for p in paras[paras.index("Instructions") + 2:paras.index("Knowledge")]]
                self.assertEqual("\n".join(l.strip() or "" for l in boxed), "\n".join(
                    l.strip() for l in text.rstrip("\n").split("\n")))
                self.assertIn(a["knowledge"], "\n".join(paras))
                self.assertEqual(sum(1 for p in paras if p in a["prompts"]), 2)

    def test_instructions_name_the_knowledge_file_the_recipe_tells_the_user_to_pick(self):
        for key, a in pd.AGENTS.items():
            self.assertIn(a["knowledge"], read(os.path.join(ROOT, a["instructions"])), key)

    def test_a_mismatched_knowledge_name_would_be_caught(self):
        a = pd.AGENTS["nda"]
        self.assertNotIn(a["knowledge"].replace("(agent knowledge)", "(readable)"),
                         read(os.path.join(ROOT, a["instructions"])))


class LegalHoldPlaybookTests(unittest.TestCase):
    def test_playbook_parses_without_a_side_against_its_catalogue_in_order(self):
        with open(os.path.join(LH_DIR, "topics.yaml"), encoding="utf-8") as f:
            cat = yaml.safe_load(f)
        ids = [t["id"] for t in cat["topics"]]
        path = os.path.join(LH_DIR, "positions", "Spaarke Legal Hold Notice – Employee Custodians v1.0.md")
        pb = playbook_parse.parse(read(path), {i: set() for i in ids}, require_side=False)
        self.assertEqual([t["id"] for t in pb["topics"]], ids)
        self.assertEqual(len(ids), 13)
        self.assertTrue(all(i.startswith("lh.") for i in ids))
        self.assertEqual(pb["silent_topics"], [])
        with self.assertRaises(playbook_parse.FormatError):  # a contract playbook needs a Side
            playbook_parse.parse(read(path), {i: set() for i in ids})


class MethodInsidePlaybookTests(unittest.TestCase):
    """G-5 (gap #36): the non-contract Word playbook starts with the review method; the contract one never does."""

    def setUp(self):
        import render_word_playbook as rw
        self.rw = rw
        with open(os.path.join(LH_DIR, "topics.yaml"), encoding="utf-8") as f:
            self.cat = yaml.safe_load(f)
        path = os.path.join(LH_DIR, "positions", "Spaarke Legal Hold Notice – Employee Custodians v1.0.md")
        self.pb = playbook_parse.parse(read(path), {t["id"]: set() for t in self.cat["topics"]}, require_side=False)
        self.preface = pd.method_preface(read(METHOD))
        self.tmp = tempfile.mkdtemp()

    def tearDown(self):
        import shutil
        shutil.rmtree(self.tmp)

    def test_method_comes_first_and_the_file_still_passes_the_four_column_checks(self):
        out = self.rw.render_table(self.pb, self.cat, os.path.join(self.tmp, "lh.docx"), preface=self.preface)
        self.assertEqual(self.rw.check_table(out, self.pb, self.cat, preface=self.preface), [])
        paras = [p.text for p in docx.Document(out).paragraphs]
        self.assertTrue(paras[1].startswith("How to review with this playbook: Spaarke Review Method v1.0"))
        self.assertIn("SRM-1110-A", paras[1])
        self.assertIn("Verification code: SRM-1110-A", paras)
        self.assertIn("Escalate: 🔴", paras)
        self.assertEqual(len(docx.Document(out).tables), 1)

    def test_every_line_of_the_method_body_is_carried(self):
        text = " ".join(t for _, t in self.preface)
        body = read(METHOD).split("\n---\n", 1)[1]
        for line in body.splitlines():
            words = re.sub(r"^\s*(#+|-|\d+\.)\s*", "", line).strip()
            if not words or words.startswith("|") or words == "Spaarke Review Method":
                continue
            with self.subTest(line=words):
                self.assertIn(words, text)

    def test_checker_fires_when_the_method_is_missing_or_unexpected(self):
        plain = self.rw.render_table(self.pb, self.cat, os.path.join(self.tmp, "plain.docx"))
        self.assertTrue(self.rw.check_table(plain, self.pb, self.cat, preface=self.preface))
        withm = self.rw.render_table(self.pb, self.cat, os.path.join(self.tmp, "with.docx"), preface=self.preface)
        self.assertTrue(self.rw.check_table(withm, self.pb, self.cat))

    def test_only_non_contract_playbooks_get_the_method(self):
        import build_package as bp
        for spec in bp.PLAYBOOKS:
            with self.subTest(practice=spec["practice"]):
                self.assertEqual(spec["method_inside"], not spec["require_side"])


class PathConventionTests(unittest.TestCase):
    def test_no_source_file_still_names_the_old_playbook_folder(self):
        # 2026-11-10: the personal tree moved from a top-level Documents folder named for legal knowledge to
        # Documents/Cowork/Playbooks/. That name stays the TEAM library's (and the POC kit folder's), so only its
        # path forms are banned.
        lk = "Legal " + "Knowledge"  # split so this file does not match itself
        old = (f"Documents/{lk}", f"{lk}/", f"{lk} ›")
        hits = []
        for dp, dn, fn in os.walk(ROOT):
            dn[:] = [d for d in dn if d not in ("out", "__pycache__")]
            for f in fn:
                if f.endswith((".md", ".py", ".yaml", ".json", ".ps1", ".html", ".txt")):
                    p = os.path.join(dp, f)
                    text = read(p)
                    if any(o in text for o in old):
                        hits.append(os.path.relpath(p, ROOT))
        self.assertEqual(hits, [])


if __name__ == "__main__":
    unittest.main()
