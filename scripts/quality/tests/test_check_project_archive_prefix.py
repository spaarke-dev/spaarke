"""Must-fire / must-not-fire controls for scripts/quality/check-project-archive-prefix.sh.

Run: python -m unittest discover -s scripts/quality/tests

Unit cases pipe paths on stdin with a temp baseline. The git cases build a throwaway repo, stage a real
rename/add and pipe `git diff --cached` into the script exactly as .husky/pre-commit does.
"""
import os
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "check-project-archive-prefix.sh")
SH = shutil.which("sh") or shutil.which("bash")

BASELINE = "# comment line\n\nx-old-project-r1\nx-Mixed-Case\n"


def check(paths, baseline_file):
    out = subprocess.run([SH, SCRIPT, baseline_file], input="\n".join(paths) + "\n",
                         capture_output=True, text=True, timeout=20)
    return out.returncode, out.stderr


def git(cwd, *args):
    return subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "core.autocrlf=false", *args],
                          cwd=cwd, capture_output=True, text=True, check=True).stdout


class ArchivePrefixScript(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.baseline = os.path.join(cls.tmp.name, "baseline.txt")
        with open(cls.baseline, "w", encoding="utf-8") as f:
            f.write(BASELINE)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    # --- must fire ---
    def test_new_x_folder_fires_and_names_folder(self):
        code, err = check(["projects/x-foo/README.md"], self.baseline)
        self.assertEqual(code, 1)
        self.assertIn("projects/x-foo", err)
        self.assertIn("/devops-project-archive", err)

    def test_uppercase_prefix_fires(self):
        code, err = check(["projects/X-foo/a.md"], self.baseline)
        self.assertEqual(code, 1)
        self.assertIn("projects/X-foo", err)

    def test_baseline_name_is_exact_match(self):
        code, _ = check(["projects/x-old-project-r1-copy/a.md"], self.baseline)
        self.assertEqual(code, 1)
        code, _ = check(["projects/x-old/a.md"], self.baseline)
        self.assertEqual(code, 1)

    def test_commented_baseline_entry_does_not_whitelist(self):
        code, _ = check(["projects/x-comment-only/a.md"], self.baseline)
        self.assertEqual(code, 1)

    def test_one_bad_among_good_fires_once_per_folder(self):
        code, err = check(["projects/normal/a.md", "projects/x-foo/a.md", "projects/x-foo/b.md", "src/a.cs"], self.baseline)
        self.assertEqual(code, 1)
        self.assertEqual(err.count("projects/x-foo\n"), 1)

    def test_crlf_input_still_fires(self):
        out = subprocess.run([SH, SCRIPT, self.baseline], input="projects/x-foo/a.md\r\n", capture_output=True, text=True)
        self.assertEqual(out.returncode, 1)

    # --- must not fire ---
    def test_edit_inside_baselined_folder_passes(self):
        self.assertEqual(check(["projects/x-old-project-r1/notes/a.md", "projects/x-Mixed-Case/.archived"], self.baseline)[0], 0)

    def test_normal_project_passes(self):
        self.assertEqual(check(["projects/spaarke-foo-r1/README.md"], self.baseline)[0], 0)

    def test_x_inside_name_passes(self):
        self.assertEqual(check(["projects/spaarke-x-ray/README.md", "projects/box-x-1/a.md", "projects/xfoo/a.md"], self.baseline)[0], 0)

    def test_file_named_x_dash_at_projects_root_or_elsewhere_passes(self):
        self.assertEqual(check(["projects/x-readme.md", "docs/projects/x-foo/a.md", "src/projects/x-foo/a.md"], self.baseline)[0], 0)

    def test_empty_input_passes(self):
        self.assertEqual(check([], self.baseline)[0], 0)

    def test_missing_baseline_is_exit_2(self):
        self.assertEqual(check(["projects/x-foo/a.md"], os.path.join(self.tmp.name, "nope.txt"))[0], 2)

    # --- real git rename, piped the way .husky/pre-commit does ---
    def _repo(self):
        repo = tempfile.mkdtemp(dir=self.tmp.name)
        git(repo, "init", "-q")
        for rel in ("projects/my-project/README.md", "projects/x-old-project-r1/README.md"):
            os.makedirs(os.path.join(repo, os.path.dirname(rel)), exist_ok=True)
            with open(os.path.join(repo, rel), "w", encoding="utf-8") as f:
                f.write("line one\nline two\nline three\nline four\n")
        git(repo, "add", "-A")
        git(repo, "commit", "-q", "-m", "init")
        return repo

    def _staged_check(self, repo):
        paths = git(repo, "-c", "core.quotePath=false", "diff", "--cached", "--name-only", "-M", "--diff-filter=AR")
        return check(paths.splitlines(), self.baseline)

    def test_staged_rename_to_x_fires(self):
        repo = self._repo()
        git(repo, "mv", "projects/my-project", "projects/x-my-project")
        code, err = self._staged_check(repo)
        self.assertEqual(code, 1)
        self.assertIn("projects/x-my-project", err)

    def test_staged_new_x_file_fires(self):
        repo = self._repo()
        os.makedirs(os.path.join(repo, "projects/x-foo"))
        with open(os.path.join(repo, "projects/x-foo/README.md"), "w") as f:
            f.write("x\n")
        git(repo, "add", "projects/x-foo/README.md")
        self.assertEqual(self._staged_check(repo)[0], 1)

    def test_staged_edit_in_baselined_folder_passes(self):
        repo = self._repo()
        with open(os.path.join(repo, "projects/x-old-project-r1/README.md"), "a") as f:
            f.write("more\n")
        with open(os.path.join(repo, "projects/x-old-project-r1/.archived"), "w") as f:
            f.write("Archived\n")
        git(repo, "add", "-A")
        self.assertEqual(self._staged_check(repo)[0], 0)

    def test_staged_edit_in_normal_project_passes(self):
        repo = self._repo()
        with open(os.path.join(repo, "projects/my-project/README.md"), "a") as f:
            f.write("more\n")
        git(repo, "add", "-A")
        self.assertEqual(self._staged_check(repo)[0], 0)


if __name__ == "__main__":
    unittest.main()
