"""Must-fire / must-not-fire controls for scripts/quality/check-skill-length.py.

Run: python -m unittest discover -s scripts/quality/tests

Each case builds a throwaway git repo with .claude/skills/<name>/ files and runs the script against it.
"""
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "check-skill-length.py")


def git(cwd, *args):
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args], cwd=cwd,
                   capture_output=True, text=True, check=True)


def body(lines, frontmatter="name: s\ndescription: d\n", head=""):
    return f"---\n{frontmatter}---\n{head}" + "".join(f"line {i}\n" for i in range(lines))


JUSTIFIED_HEAD = "# s\n> **Read this file in full.** It is long on purpose.\n## Contents\n- Step 1\n"


class SkillLengthGuard(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.baseline = os.path.join(self.root, "baseline.txt")
        git(self.root, "init", "-q")

    def tearDown(self):
        self.tmp.cleanup()

    def put(self, rel, text):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        git(self.root, "add", rel)

    def run_guard(self, *extra):
        out = subprocess.run([sys.executable, SCRIPT, "--root", self.root, "--baseline", self.baseline, *extra],
                             capture_output=True, text=True, timeout=30)
        return out.returncode, out.stdout

    # --- must not fire -------------------------------------------------------
    def test_short_skill_passes(self):
        self.put(".claude/skills/a/SKILL.md", body(100))
        self.assertEqual(self.run_guard()[0], 0)

    def test_exactly_at_cap_passes(self):
        self.put(".claude/skills/a/SKILL.md", body(500 - 4))  # 4 frontmatter lines
        self.assertEqual(self.run_guard()[0], 0)

    def test_justified_long_skill_passes(self):
        fm = "name: s\ndescription: d\nlength-justification: every step runs every time\n"
        self.put(".claude/skills/a/SKILL.md", body(700, fm, JUSTIFIED_HEAD))
        self.assertEqual(self.run_guard()[0], 0)

    def test_baselined_skill_that_does_not_grow_passes(self):
        self.put(".claude/skills/a/SKILL.md", body(600))
        with open(self.baseline, "w") as f:
            f.write("# header\na|700\n")
        self.assertEqual(self.run_guard()[0], 0)

    def test_template_and_archived_skills_are_ignored(self):
        self.put(".claude/skills/_template/SKILL.md", body(900))
        self.put(".claude/skills/_archived/SKILL.md", body(900))
        self.assertEqual(self.run_guard()[0], 0)

    def test_long_reference_with_contents_passes(self):
        self.put(".claude/skills/a/references/r.md", "# R\n## Contents\n- one\n" + "x\n" * 300)
        self.assertEqual(self.run_guard()[0], 0)

    def test_long_template_reference_is_exempt(self):
        self.put(".claude/skills/a/references/design-template.md", "x\n" * 300)
        self.assertEqual(self.run_guard()[0], 0)

    # --- must fire -----------------------------------------------------------
    def test_long_unjustified_skill_fails(self):
        self.put(".claude/skills/a/SKILL.md", body(600))
        code, out = self.run_guard()
        self.assertEqual(code, 1)
        self.assertIn("SKILL.md is 604 lines", out)

    def test_justification_key_alone_is_not_enough(self):
        fm = "name: s\ndescription: d\nlength-justification: reasons\n"
        self.put(".claude/skills/a/SKILL.md", body(600, fm))
        self.assertEqual(self.run_guard()[0], 1)

    def test_empty_justification_fails(self):
        fm = "name: s\ndescription: d\nlength-justification:\n"
        self.put(".claude/skills/a/SKILL.md", body(600, fm, JUSTIFIED_HEAD))
        self.assertEqual(self.run_guard()[0], 1)

    def test_baselined_skill_that_grows_fails(self):
        self.put(".claude/skills/a/SKILL.md", body(600))
        with open(self.baseline, "w") as f:
            f.write("a|550\n")
        code, out = self.run_guard()
        self.assertEqual(code, 1)
        self.assertIn("grew from 550 to 604", out)

    def test_long_reference_without_contents_fails(self):
        self.put(".claude/skills/a/references/r.md", "# R\n" + "x\n" * 300)
        code, out = self.run_guard()
        self.assertEqual(code, 1)
        self.assertIn("references/r.md", out)

    def test_update_baseline_then_pass(self):
        self.put(".claude/skills/a/SKILL.md", body(600))
        self.assertEqual(self.run_guard("--update-baseline")[0], 0)
        with open(self.baseline) as f:
            self.assertIn("a|604", f.read())
        self.assertEqual(self.run_guard()[0], 0)


if __name__ == "__main__":
    unittest.main()
