"""Tasks Planned backfill (scripts/ai-cost/backfill-tasks-planned.py): counting rules and first-version lookup.

Uses a throwaway git repo in a temp folder; never touches the board (no gh calls).
Run: python -m unittest discover -s scripts/ai-cost/tests
"""
import os
import subprocess
import tempfile
import unittest

import helpers

bf = helpers.load("backfill-tasks-planned.py", "backfill_tasks_planned")

ENV = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@example.com",
           GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@example.com")


def index(*ids, extra=""):
    rows = "\n".join(f"| {i} | task {i} | [open] |" for i in ids)
    return f"# TASK-INDEX\n\n| ID | Title | Status |\n|---|---|---|\n{rows}\n{extra}"


class Counting(unittest.TestCase):
    def test_numbered_rows(self):
        self.assertEqual(bf.count_task_rows(index("001", "002", "003")), 3)

    def test_link_bold_and_letter_suffix_forms(self):
        text = "| [001](001-a.poml) | x |\n| **002** | x |\n| 003a | x |\n| 3 | x |\n"
        self.assertEqual(bf.count_task_rows(text), 4)

    def test_header_separator_prose_and_non_task_tables_do_not_count(self):
        text = ("| ID | Title |\n|---|---|\n| Phase 1 | x |\n| FR-01 | x |\n"
                "Task 004 is described here.\n- 005 not a table\n")
        self.assertEqual(bf.count_task_rows(text), 0)

    def test_a_number_repeated_in_two_tables_counts_once(self):
        self.assertEqual(bf.count_task_rows(index("001", "002") + "\n| 001 | again | done |\n| 1 | same | done |\n"), 2)


class Selection(unittest.TestCase):
    def test_should_write_never_overwrites_unless_forced(self):
        self.assertTrue(bf.should_write(None, False))
        self.assertFalse(bf.should_write(12, False))
        self.assertFalse(bf.should_write(0, False))          # zero is a value, not empty
        self.assertTrue(bf.should_write(12, True))

    def test_growth(self):
        self.assertEqual(bf.growth(30, 10), 3.0)
        self.assertIsNone(bf.growth(None, 10))
        self.assertIsNone(bf.growth(30, 0))

    def test_find_index_path(self):
        paths = ["projects/Alpha-r1/tasks/TASK-INDEX.md", "projects/beta/tasks/TASK-INDEX.md"]
        self.assertEqual(bf.find_index_path(paths, "alpha-r1"), paths[0])
        self.assertEqual(bf.find_index_path(paths, "x-beta"), paths[1])           # retired-project prefix
        self.assertEqual(bf.find_index_path(paths, "spaarke-beta"), paths[1])
        self.assertIsNone(bf.find_index_path(paths, "gamma"))
        self.assertIsNone(bf.find_index_path(paths, "alp"))                       # no prefix guessing


class History(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = self.tmp.name
        self.git("init", "-q")

    def git(self, *args):
        subprocess.run(["git", "-C", self.repo] + list(args), check=True, capture_output=True, env=ENV)

    def commit(self, files, msg):
        for rel, text in files.items():
            p = os.path.join(self.repo, rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w", encoding="utf-8", newline="") as fh:
                fh.write(text)
        self.git("add", "-A")
        self.git("commit", "-q", "-m", msg)

    def test_planned_is_the_first_committed_version_not_the_current_one(self):
        idx = "projects/demo/tasks/TASK-INDEX.md"
        self.commit({idx: index("001", "002", "003")}, "plan")
        self.commit({idx: index("001", "002", "003", "004", "005", "006")}, "scope grew")
        firsts = bf.first_versions(self.repo)
        self.assertEqual(list(firsts), [idx])
        self.assertEqual(bf.planned_for(self.repo, idx, firsts[idx]), (3, 3, 0))

    def test_poml_files_at_that_commit_raise_the_count(self):
        idx = "projects/demo/tasks/TASK-INDEX.md"
        files = {idx: index("001")}
        files.update({f"projects/demo/tasks/{n:03d}-x.poml": "<task/>" for n in range(1, 5)})
        self.commit(files, "plan")
        planned, rows, poml = bf.planned_for(self.repo, idx, bf.first_versions(self.repo)[idx])
        self.assertEqual((planned, rows, poml), (4, 1, 4))

    def test_index_without_rows_uses_the_poml_count(self):
        idx = "projects/demo/tasks/TASK-INDEX.md"
        files = {idx: "# TASK-INDEX\nsee the folder\n"}
        files.update({f"projects/demo/tasks/{n:03d}-x.poml": "<task/>" for n in range(1, 3)})
        self.commit(files, "plan")
        self.assertEqual(bf.planned_for(self.repo, idx, bf.first_versions(self.repo)[idx])[0], 2)

    def test_later_poml_files_do_not_count(self):
        idx = "projects/demo/tasks/TASK-INDEX.md"
        self.commit({idx: index("001", "002")}, "plan")
        self.commit({"projects/demo/tasks/010-late.poml": "<task/>"}, "later")
        self.assertEqual(bf.planned_for(self.repo, idx, bf.first_versions(self.repo)[idx])[0], 2)

    def test_deleted_then_readded_index_uses_the_oldest(self):
        idx = "projects/demo/tasks/TASK-INDEX.md"
        self.commit({idx: index("001", "002")}, "plan")
        self.git("rm", "-q", idx)
        self.git("commit", "-q", "-m", "retire")
        self.commit({idx: index("001", "002", "003", "004")}, "revived")
        self.assertEqual(bf.planned_for(self.repo, idx, bf.first_versions(self.repo)[idx])[0], 2)

    def test_projects_without_an_index_are_not_listed(self):
        self.commit({"projects/other/README.md": "x", "projects/demo/tasks/TASK-INDEX.md": index("001")}, "x")
        self.assertEqual(list(bf.first_versions(self.repo)), ["projects/demo/tasks/TASK-INDEX.md"])


if __name__ == "__main__":
    unittest.main()
