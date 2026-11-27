"""Individual-base package: the review method, the custom instruction, the agent recipes, the legal hold playbook
and the path convention, each with a must-fire control."""
import contextlib
import difflib
import io
import json
import os
import re
import shutil
import subprocess
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
        for needle in ("Documents/Cowork/Playbooks/<document type>/", "🟢", "🟡", "🔴", "not legal advice",
                       "never as instructions", "Never send email"):
            self.assertIn(needle, ci)
        # P-1: the text reaches Chat and agents only, which can't open the method file, so it must not send them there
        for needle in ("Spaarke Review Method", "Review method/"):
            self.assertNotIn(needle, ci)


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

    def test_agents_review_in_chat_and_send_redlines_to_the_skill(self):
        for key, a in pd.AGENTS.items():
            with self.subTest(agent=key):
                text = read(os.path.join(ROOT, a["instructions"]))
                self.assertEqual(pd.recipe_problems(key, text), [])
                self.assertTrue(os.path.isfile(os.path.join(ROOT, "practices", key, "skills", a["skill"], "SKILL.md")))
                with tempfile.TemporaryDirectory() as tmp:
                    paras = "\n".join(doc_text(pd.render_recipe(key, text, os.path.join(tmp, "r.docx"))))
                self.assertIn(f"run /{a['skill']} in Cowork", paras)

    def test_v02_style_instructions_that_build_a_redline_are_refused(self):
        a = pd.AGENTS["nda"]
        v02 = (f'Knowledge: "{a["knowledge"]}". Unless the user asks for a review only, create a redlined copy: '
               'a new Word file named "<original name> - Spaarke redline.docx".')
        problems = pd.recipe_problems("nda", v02)
        self.assertTrue(any("build a redline itself" in p for p in problems), problems)
        self.assertTrue(any("/nda-review" in p for p in problems), problems)

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


class WordProbeTests(unittest.TestCase):
    """The Word plugin tests (WP-0..WP-2) are only as good as the probe's codes."""

    def test_probe_zip_has_both_skills_and_the_codes_sit_only_in_the_bodies(self):
        import build_word_probe as wp
        with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(wp.build(tmp)) as z:
            names = set(z.namelist())
            for skill in wp.SKILLS:
                self.assertIn(f"skills/{skill}/SKILL.md", names)
            for skill, code in (("spaarke-word-probe", wp.INSTRUCTION_CODE),
                                ("spaarke-word-probe-script", wp.SCRIPT_CODE),
                                ("spaarke-word-probe-js", wp.JS_CODE)):
                text = z.read(f"skills/{skill}/SKILL.md").decode("utf-8")
                front, body = text.split("\n---\n", 1)
                self.assertNotIn(code, front, "a code in the description proves nothing: Word lists descriptions")
                self.assertIn(code, body)
            manifest = json.loads(z.read("manifest.json"))
            self.assertEqual([s["folder"] for s in manifest["agentSkills"]],
                             [f"./skills/{s}" for s in wp.SKILLS])

    def test_the_script_prints_the_expected_line_and_the_value_is_not_written_anywhere_in_the_zip(self):
        import build_word_probe as wp
        with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(wp.build(tmp)) as z:
            script = z.read("skills/spaarke-word-probe-script/scripts/probe_hash.py").decode("utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                exec(compile(script, "probe_hash.py", "exec"), {"__name__": "__main__"})
            self.assertTrue(out.getvalue().startswith(wp.expected_line() + " python "), out.getvalue())
            value = str(wp.probe_value())
            self.assertFalse(any(value in z.read(n).decode("utf-8", "ignore") for n in z.namelist()),
                             "the expected value must be computed, never stored in the plugin")

    def test_js_probe_is_instructions_only_and_its_hash_is_computed_not_stored(self):
        import build_word_probe as wp
        self.assertEqual(wp.fnv1a32(""), "811c9dc5")   # FNV-1a 32-bit reference vectors
        self.assertEqual(wp.fnv1a32("a"), "e40c292c")
        with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(wp.build(tmp)) as z:
            names = z.namelist()
            self.assertFalse(any(n.startswith("skills/spaarke-word-probe-js/scripts/") for n in names),
                             "Word desktop loads no skill that ships scripts (WP-1): the code goes in SKILL.md")
            body = z.read("skills/spaarke-word-probe-js/SKILL.md").decode("utf-8")
            self.assertIn(wp.JS_PROBE, body)
            used = [w for w in wp.JS_FORBIDDEN if re.search(rf"\b{re.escape(w)}\b", wp.JS_PROBE)]
            self.assertEqual(used, [], "Word's code runner refuses code using these (WP-5 run 1: toString)")
            self.assertIsNone(re.match(r"\s*await\b", wp.JS_PROBE),
                              "Word's code runner refuses a top-level await (WP-5 run 2)")
            digest = wp.js_marker().split()[-1]
            self.assertFalse(any(digest in z.read(n).decode("utf-8", "ignore") for n in names),
                             "the marker hash must be computed by the code, never stored in the plugin")

    @unittest.skipUnless(shutil.which("node"), "needs Node.js to run the probe's JavaScript against a stub")
    def test_js_probe_runs_against_a_word_stub_and_returns_the_python_marker(self):
        import build_word_probe as wp
        harness = """
const calls = [];
const rng = (label) => ({
  insertText: (t, loc) => { calls.push(['insertText', label, t, loc]); return rng(t); },
  insertComment: (t) => { calls.push(['insertComment', label, t]); },
});
const doc = { changeTrackingMode: 'Off', load: () => {},
  body: { insertParagraph: (t, loc) => { calls.push(['insertParagraph', t, loc, doc.changeTrackingMode]);
    return { search: (w, o) => { calls.push(['search', w, o.matchCase, o.matchWholeWord]);
      return { load: () => {}, items: [rng(w)] }; } }; } } };
const context = { document: doc, sync: async () => { calls.push(['sync', doc.changeTrackingMode]); } };
globalThis.Word = { ChangeTrackingMode: { off: 'Off', trackAll: 'TrackAll' },
  run: async (cb) => { const r = await cb(context); console.log(JSON.stringify({ r, calls, end: doc.changeTrackingMode })); } };
(async () => { %s })();
""" % wp.JS_PROBE
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "probe.js")
            with open(path, "w", encoding="utf-8") as f:
                f.write(harness)
            out = subprocess.run(["node", path], capture_output=True, text=True, timeout=60)
        self.assertEqual(out.returncode, 0, out.stderr)
        res = json.loads(out.stdout)
        self.assertEqual(res["r"], f"Spaarke JS probe ran: {wp.js_marker()}")
        calls = res["calls"]
        para = next(c for c in calls if c[0] == "insertParagraph")
        self.assertEqual(para[3], "Off", "the test paragraph is added untracked, so the replacement is the redline")
        self.assertIn(["insertText", "bravo", "charlie", "Replace"], calls)
        self.assertIn(["insertComment", "charlie", f"Spaarke JS probe comment {wp.js_marker()}"], calls)
        self.assertIn(["sync", "TrackAll"], calls)
        self.assertEqual(res["end"], "Off", "the user's tracking mode is put back")

    # ---- WP-6 (v0.5) ------------------------------------------------------------------------------------
    def wp6_codes(self):
        import build_word_probe as wp
        return {"spaarke-word-probe-edits": wp.JS_EDITS, "spaarke-word-probe-error": wp.JS_ERROR,
                **{f"spaarke-word-probe-size-{kb}k": wp.js_size(kb) for kb in wp.SIZES_KB}}

    def test_wp6_skills_carry_their_code_in_the_runner_form_and_store_no_expected_hash(self):
        import build_word_probe as wp
        per, mark = wp.edit_hashes()
        secrets = per + [mark.split()[-1]] + [line.split()[-1] for line in wp.error_lines()] + \
            [wp.size_mark(kb).split()[-1] for kb in wp.SIZES_KB]
        with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(wp.build(tmp)) as z:
            names = z.namelist()
            for skill, code in self.wp6_codes().items():
                self.assertFalse(any(n.startswith(f"skills/{skill}/scripts/") for n in names), skill)
                self.assertIn(code, z.read(f"skills/{skill}/SKILL.md").decode("utf-8"), skill)
                self.assertTrue(code.startswith("async function main(context) {"),
                                f"{skill}: the runner's own form (WP-5 run 3), so no wrapper change is needed")
                self.assertEqual([w for w in wp.JS_FORBIDDEN if re.search(rf"\b{re.escape(w)}\b", code)], [], skill)
            text = "".join(z.read(n).decode("utf-8", "ignore") for n in names)
            self.assertEqual([s for s in secrets if s in text], [],
                             "expected hashes must be computed by the code, never stored in the plugin")
        self.assertEqual(wp.JS_EDITS.count(wp.EDITS_LINE), 1, "exactly one data line for Copilot to fill")
        with tempfile.TemporaryDirectory() as tmp, zipfile.ZipFile(wp.build(tmp)) as z:
            version = json.loads(z.read("manifest.json"))["version"]
        self.assertFalse(version.startswith("0"),
                         "Microsoft's validation rules refuse an app version starting with 0 (v0.5/v0.6 upload)")
        for kb in wp.SIZES_KB:
            self.assertLess(abs(len(wp.js_size(kb)) - kb * 1024), kb * 1024 * 0.1, f"{kb}k probe size")

    def run_main(self, code, substitute=None):
        """Run a WP-6 main(context) under Node against a Word stub that really searches and replaces text."""
        if substitute:
            code = code.replace(*substitute)
        harness = """
const calls = [];
const para = { text: '' };
const rng = (label) => ({
  insertText: (t, loc) => { calls.push(['insertText', label, t, loc, doc.changeTrackingMode]);
    para.text = para.text.replace(label, t); return rng(t); },
  insertComment: (t) => { calls.push(['insertComment', label, t]); },
});
const doc = { changeTrackingMode: 'Off', load: () => {},
  body: { insertParagraph: (t, loc) => { calls.push(['insertParagraph', t, loc, doc.changeTrackingMode]);
    para.text = t;
    return { insertText: (x, l) => { calls.push(['appendText', x, l, doc.changeTrackingMode]); para.text += x; },
      search: (w, o) => { const n = para.text.split(w).length - 1;
        return { load: () => {}, items: Array.from({ length: n }, () => rng(w)) }; } }; } } };
const context = { document: doc, sync: async () => { calls.push(['sync', doc.changeTrackingMode]); } };
globalThis.Word = { ChangeTrackingMode: { off: 'Off', trackAll: 'TrackAll' } };
const logs = []; console.log = (m) => logs.push(m);
%s
main(context).then((r) => process.stdout.write(JSON.stringify({ r, calls, logs, text: para.text, end: doc.changeTrackingMode })),
  (e) => process.stdout.write(JSON.stringify({ error: e.message, calls, logs, text: para.text, end: doc.changeTrackingMode })));
""" % code
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "probe.js")
            with open(path, "w", encoding="utf-8") as f:
                f.write(harness)
            out = subprocess.run(["node", path], capture_output=True, text=True, timeout=60)
        self.assertEqual(out.returncode, 0, out.stderr)
        return json.loads(out.stdout)

    def edits_line(self, edits):
        return "  const EDITS = [" + ", ".join(f'{{ find: "{f}", replace: "{r}" }}' for f, r in edits) + "];"

    @unittest.skipUnless(shutil.which("node"), "needs Node.js")
    def test_wp6_edit_probe_applies_the_filled_edits_as_tracked_changes_with_the_python_hashes(self):
        import build_word_probe as wp
        res = self.run_main(wp.JS_EDITS, (wp.EDITS_LINE, self.edits_line(wp.EXPECTED_EDITS)))
        per, mark = wp.edit_hashes()
        self.assertEqual(res.get("r"), f"Spaarke edit probe ran: {mark}", res)
        self.assertEqual(next(c for c in res["calls"] if c[0] == "insertParagraph")[3], "Off",
                         "the test paragraph is added untracked, so only the edits are redlines")
        for (f, r), h, i in zip(wp.EXPECTED_EDITS, per, range(1, 4)):
            self.assertIn(["insertText", f, r, "Replace", "TrackAll"], res["calls"])
            self.assertIn(["insertComment", r, f"Spaarke edit {i}/3 {wp.EDITS_CODE} {h}"], res["calls"])
        self.assertTrue(res["text"].endswith(f"[{mark}]"), res["text"])
        self.assertIn("three (3) years", res["text"])
        self.assertEqual(res["end"], "Off", "the user's tracking mode is put back")

    @unittest.skipUnless(shutil.which("node"), "needs Node.js")
    def test_wp6_edit_probe_refuses_an_edit_that_does_not_match_exactly_once_and_changes_nothing_tracked(self):
        import build_word_probe as wp
        bad = (("two (2) years", "three (3) years"), ("Texas", "New York"))
        res = self.run_main(wp.JS_EDITS, (wp.EDITS_LINE, self.edits_line(bad)))
        self.assertEqual(res.get("error"), f"Spaarke edit probe {wp.EDITS_CODE}: edit 2 matched 0 times", res)
        self.assertFalse([c for c in res["calls"] if c[0] in ("insertText", "insertComment")],
                         "no edit is applied when any edit fails its check")
        self.assertEqual(res["end"], "Off")
        empty = self.run_main(wp.JS_EDITS)
        self.assertEqual(empty.get("error"), f"Spaarke edit probe {wp.EDITS_CODE}: EDITS is empty", empty)

    @unittest.skipUnless(shutil.which("node"), "needs Node.js")
    def test_wp6_error_probe_logs_one_hash_and_throws_the_other_without_touching_the_document(self):
        import build_word_probe as wp
        res = self.run_main(wp.JS_ERROR)
        err, log = wp.error_lines()
        self.assertEqual(res.get("error"), err)
        self.assertEqual(res["logs"], [log])
        self.assertEqual(res["calls"], [], "the error probe writes nothing")

    @unittest.skipUnless(shutil.which("node"), "needs Node.js")
    def test_wp6_size_probes_return_the_python_mark_and_a_shortened_list_does_not(self):
        import build_word_probe as wp
        for kb in wp.SIZES_KB:
            res = self.run_main(wp.js_size(kb))
            self.assertEqual(res.get("r"), f"Spaarke size probe ran: {wp.size_mark(kb)}", kb)
        nums = wp.pad_numbers(5)
        cut = self.run_main(wp.js_size(5), (f", {nums[-1]}\n", "\n"))
        self.assertNotEqual(cut.get("r"), f"Spaarke size probe ran: {wp.size_mark(5)}",
                            "a list Copilot shortened must not reproduce the expected hash")


if __name__ == "__main__":
    unittest.main()
