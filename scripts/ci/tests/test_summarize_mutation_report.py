"""Controls for scripts/ci/summarize-mutation-report.py (the mutation-nightly alert verdict).

#1821: Stryker's `since` mode re-tests every mutant covered by a test that changed in the window, so its report
carries mutants on lines nobody changed. The summariser must count only mutants on changed lines when given --since,
report the others as a count, and fail closed (count everything) when it cannot read a file's diff.

Each test builds a throwaway git repo: `Svc.cs` at commit A, then line 3 changed and a line appended at commit B.
Run: python -m unittest discover -s scripts/ci/tests -v
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "summarize-mutation-report.py"
SRC = "src/Svc.cs"
V1 = "line1\nline2\nline3\nline4\nline5\n"
V2 = "line1\nline2\nline3 CHANGED\nline4\nline5\nline6 ADDED\n"


def git(repo, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True).stdout.strip()


def mutant(mid, status, start, end=None):
    return {"id": str(mid), "mutatorName": "Equality mutation", "replacement": "x", "status": status,
            "location": {"start": {"line": start, "column": 1}, "end": {"line": end or start, "column": 5}}}


class SummarizeMutationReportTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        git(self.repo, "init", "-q")
        git(self.repo, "config", "user.email", "ci@example.invalid")
        git(self.repo, "config", "user.name", "ci")
        git(self.repo, "config", "core.autocrlf", "false")
        (self.repo / "src").mkdir()
        (self.repo / SRC).write_text(V1, encoding="utf8", newline="\n")
        git(self.repo, "add", ".")
        git(self.repo, "commit", "-q", "-m", "A")
        self.since = git(self.repo, "rev-parse", "HEAD")
        (self.repo / SRC).write_text(V2, encoding="utf8", newline="\n")
        git(self.repo, "commit", "-q", "-am", "B")

    def tearDown(self):
        self._tmp.cleanup()

    def run_summary(self, mutants, since=None):
        # Stryker.NET keys files by absolute path (see the script's docstring).
        key = (self.repo / SRC).as_posix()
        report = {"schemaVersion": "1", "files": {key: {"language": "cs", "source": V2, "mutants": mutants}}}
        rpath = self.root / "report.json"
        rpath.write_text(json.dumps(report), encoding="utf8")
        out = {n: self.root / f"{n}.txt" for n in ("md", "alert", "verdict")}
        cmd = [sys.executable, str(SCRIPT), "--report", f"bff={rpath}=src", "--repo-root", str(self.repo),
               "--out-md", str(out["md"]), "--out-alert", str(out["alert"]), "--out-verdict", str(out["verdict"])]
        if since is not None:
            cmd += ["--since", since]
        # The script prints the markdown (→, —) to stdout; a Windows console codepage cannot encode it.
        env = dict(os.environ, PYTHONIOENCODING="utf-8")
        proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf8", env=env)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        return (out["verdict"].read_text(encoding="utf8"), out["alert"].read_text(encoding="utf8"),
                out["md"].read_text(encoding="utf8"), proc.stderr)

    # must-fire: a survivor on a changed line fails the night, whatever else is in the report
    def test_survivor_on_a_changed_line_is_failing_and_listed(self):
        verdict, alert, _, _ = self.run_summary(
            [mutant(1, "Survived", 3), mutant(2, "Survived", 1), mutant(3, "Killed", 6)], since=self.since)
        self.assertEqual(verdict, "failing")
        self.assertIn(f"`{SRC}:3`", alert)
        self.assertNotIn(f"`{SRC}:1`", alert, "line 1 did not change: not alerted on")
        self.assertIn("| 1 | 1 | 0 | 50.0% |", alert, "only the two mutants on changed lines are scored")
        self.assertIn("Not counted: 1 mutant(s)", alert)

    def test_uncovered_mutant_on_an_added_line_is_failing(self):
        verdict, alert, _, _ = self.run_summary([mutant(1, "NoCoverage", 6)], since=self.since)
        self.assertEqual(verdict, "failing")
        self.assertIn(f"`{SRC}:6` — **NoCoverage**", alert)

    def test_a_mutant_spanning_a_changed_line_counts(self):
        verdict, _, _, _ = self.run_summary([mutant(1, "Survived", 2, 3)], since=self.since)
        self.assertEqual(verdict, "failing")

    # must-not-fire: survivors only on lines the window did not change (#1821)
    def test_survivors_only_on_unchanged_lines_are_ok_and_counted_as_outside(self):
        verdict, alert, md, _ = self.run_summary(
            [mutant(1, "Survived", 1), mutant(2, "NoCoverage", 4), mutant(3, "Killed", 3)], since=self.since)
        self.assertEqual(verdict, "ok")
        self.assertEqual(alert.strip(), "", "an ok night posts no alert body")
        self.assertIn("Not counted: 2 mutant(s)", md)
        self.assertIn("Every active mutant on the changed lines was killed.", md)

    # fail closed: an unreadable diff counts the whole file rather than turning the night green
    def test_unreadable_diff_counts_every_mutant(self):
        verdict, alert, _, err = self.run_summary([mutant(1, "Survived", 1)], since="no-such-ref")
        self.assertEqual(verdict, "failing")
        self.assertIn(f"`{SRC}:1`", alert)
        self.assertIn("cannot read the diff", err)

    def test_without_since_every_active_mutant_counts(self):
        verdict, alert, _, _ = self.run_summary([mutant(1, "Survived", 1), mutant(2, "Killed", 3)])
        self.assertEqual(verdict, "failing")
        self.assertIn(f"`{SRC}:1`", alert)
        self.assertNotIn("Not counted", alert)


if __name__ == "__main__":
    unittest.main()
