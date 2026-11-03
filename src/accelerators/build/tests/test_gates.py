"""Gate tests: each lint rule must fire on a seeded violation and stay silent on a valid skill."""
import json
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import gates  # noqa: E402

TOPICS = {"nda.term", "nda.remedies"}
VALID_MD = """---
name: sample-skill
description: Review a sample agreement. Use for "review this sample". Do NOT use for anything else (use review-contract).
license: Proprietary
metadata:
  category: writing
  version: 0.1.0
---

# Sample
Draft for attorney review; not legal advice. See references/guardrails.md. Topic nda.term.
Run `python scripts/tool.py --mode fast`. Template assets/sample-nda.docx.
"""
TOOL_PY = 'import argparse\nimport json\nimport sibling\nfrom lxml import etree\nargparse.ArgumentParser().add_argument("--mode")\n'


class GateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.skill = os.path.join(self.tmp, "sample-skill")
        os.makedirs(os.path.join(self.skill, "references"))
        os.makedirs(os.path.join(self.skill, "scripts"))
        os.makedirs(os.path.join(self.skill, "assets"))
        self.put("SKILL.md", VALID_MD)
        self.put("references/guardrails.md", "Guardrails. Topic nda.remedies.")
        self.put("scripts/tool.py", TOOL_PY)
        self.put("scripts/sibling.py", "import os\n")
        self.put("assets/sample-nda.docx", "x")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def put(self, rel, text):
        p = os.path.join(self.skill, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)

    def rules(self):
        return {r for r, _ in gates.lint_skill(self.skill, TOPICS)}

    def md(self, old, new):
        self.put("SKILL.md", VALID_MD.replace(old, new))

    def test_valid_skill_passes(self):
        self.assertEqual(gates.lint_skill(self.skill, TOPICS), [])

    def test_extra_frontmatter_key(self):
        self.md("license: Proprietary", "license: Proprietary\nversion: 1.0")
        self.assertIn("frontmatter-keys", self.rules())

    def test_name_folder_and_format(self):
        self.md("name: sample-skill", "name: Sample_Skill")
        self.assertTrue({"name-format", "name-folder"} <= self.rules())

    def test_name_collision_and_trademark(self):
        for name, rule in (("review-contract", "name-collision"), ("copilot-nda", "trademark")):
            os.rename(self.skill, os.path.join(self.tmp, name))
            self.skill = os.path.join(self.tmp, name)
            self.md("name: sample-skill", f"name: {name}")
            self.assertIn(rule, self.rules(), name)

    def test_description_rules(self):
        self.md('Use for "review this sample". ', "")
        self.assertIn("description-triggers", self.rules())
        self.md("Do NOT use for anything else (use review-contract).", "")
        self.assertIn("description-routing", self.rules())
        self.md("Review a sample agreement.", "x" * 1100)
        self.assertIn("description-length", self.rules())

    def test_description_must_be_plain_line(self):
        line = 'description: Review a sample agreement. Use for "review this sample". Do NOT use for anything else (use review-contract).'
        value = line[len("description: "):]
        for bad in (f"description: >-\n  {value}", f"description: |\n  {value}", f"description: '{value}'"):
            self.md(line, bad)
            self.assertIn("description-plain", self.rules(), bad[:16])

    def test_metadata_must_be_flat(self):
        self.md("  version: 0.1.0", "  version:\n    major: 0")
        self.assertIn("metadata-flat", self.rules())

    def test_license_required(self):
        self.md("license: Proprietary\n", "")
        self.assertIn("license", self.rules())

    def test_file_count_and_hidden_files(self):
        for i in range(20):
            self.put(f"references/r{i}.md", "x")
        self.put(".hidden", "x")
        self.put("scripts/__pycache__/tool.cpython-313.pyc", "x")
        r = self.rules()
        self.assertIn("file-count", r)
        self.assertIn("hidden-files", r)

    def test_file_size(self):
        with open(os.path.join(self.skill, "assets", "big.bin"), "wb") as f:
            f.write(b"0" * (gates.MAX_FILE + 1))
        self.assertIn("file-size", self.rules())

    def test_host_paths(self):
        for text in ("Profile at ~/.claude/legal.md", r"Saved to C:\Users\me\legal.md", "/home/me/legal.md"):
            self.put("references/x.md", text)
            self.assertIn("host-paths", self.rules(), text)

    def test_regex_is_not_a_host_path(self):
        self.put("scripts/sibling.py", 'import re\nTOPIC = re.compile(r"^##\\s+topic:\\s*(?P<id>\\S+)")\n')
        self.assertNotIn("host-paths", self.rules())

    def test_unknown_topic_id(self):
        self.put("references/x.md", "Topic nda.made-up applies. File sample-nda.docx is not a topic.")
        problems = [m for r, m in gates.lint_skill(self.skill, TOPICS) if r == "topic-ids"]
        self.assertEqual(len(problems), 1)
        self.assertIn("nda.made-up", problems[0])

    def test_topic_ids_of_other_practices_and_file_names(self):
        self.put("references/x.md", "See intake.made-up. Files nda.zip, report.xlsx, nda.csv are not topics.")
        problems = [m for r, m in gates.lint_skill(self.skill, TOPICS) if r == "topic-ids"]
        self.assertEqual(len(problems), 1, problems)
        self.assertIn("intake.made-up", problems[0])

    def test_missing_reference_in_companion_markdown(self):
        self.put("references/x.md", "Load references/absent.md first.")
        self.assertIn("missing-reference", self.rules())

    def test_reference_inside_a_longer_path_is_not_checked(self):
        self.put("references/x.md", "Upstream keeps it in my-scripts/other.py and docs/assets/logo.png.")
        self.assertNotIn("missing-reference", self.rules())

    def test_cli_drift(self):
        self.md("--mode fast", "--mode fast --bogus")
        problems = [m for r, m in gates.lint_skill(self.skill, TOPICS) if r == "cli-drift"]
        self.assertEqual(len(problems), 1, problems)
        self.assertIn("--bogus", problems[0])

    def test_guardrails_and_disclaimer(self):
        os.remove(os.path.join(self.skill, "references", "guardrails.md"))
        self.md("Draft for attorney review; not legal advice. ", "")
        r = self.rules()
        self.assertIn("guardrails", r)
        self.assertIn("disclaimer", r)

    def test_missing_reference(self):
        self.md("scripts/tool.py", "scripts/missing.py")
        self.assertIn("missing-reference", self.rules())

    def test_script_imports(self):
        self.put("scripts/tool.py", "import requests\n")
        self.assertIn("script-imports", self.rules())


class PluginGateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        os.makedirs(os.path.join(self.tmp, "skills", "a"))
        open(os.path.join(self.tmp, "skills", "a", "SKILL.md"), "w").close()
        for n in ("color.png", "outline.png"):
            open(os.path.join(self.tmp, n), "w").close()
        self.m = {"id": "x", "version": "0.1.0", "developer": {}, "name": {"short": "S"},
                  "description": {"short": "d", "full": "f"}, "icons": {"color": "color.png", "outline": "outline.png"},
                  "agentSkills": [{"folder": "./skills/a"}]}

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def lint(self):
        p = os.path.join(self.tmp, "manifest.json")
        with open(p, "w") as f:
            json.dump(self.m, f)
        return {r for r, _ in gates.lint_plugin(p)}

    def test_valid_plugin_passes(self):
        self.assertEqual(self.lint(), set())

    def test_violations(self):
        self.m["agentSkills"] = [{"folder": f"./skills/s{i}"} for i in range(21)]
        self.m["name"]["short"] = "x" * 31
        os.remove(os.path.join(self.tmp, "outline.png"))
        self.assertTrue({"plugin-skill-count", "plugin-skill-folder", "plugin-manifest", "plugin-icons"} <= self.lint())


if __name__ == "__main__":
    unittest.main()
