"""Durable archive (--archive, --source archive) for scripts/ai-cost/spend-report.py.

Run: python -m unittest discover -s scripts/ai-cost/tests
"""
import csv
import io
import json
import os
import tempfile
import unittest
from datetime import datetime, timedelta, timezone

import helpers
from helpers import call_line, write_transcript

sr = helpers.load("spend-report.py", "spend_report")

FOLDER_A = "c--code-files-spaarke-wt-alpha-r1"
FOLDER_B = "c--code-files-spaarke-wt-beta-r1"


def row(day, project="alpha-r1", model="claude-sonnet-5", kind="main", cost=3.0, calls=1, tz="utc"):
    """A flat archive row whose total cost is `cost` (all of it output cost)."""
    r = {"day": day, "project": project, "model": model, "kind": kind, "tz": tz}
    r.update(dict.fromkeys(sr.INT_FIELDS, 0))
    r.update(dict.fromkeys(sr.COST_FIELDS, 0.0))
    r["calls"] = calls
    r["cost_output"] = cost
    return r


class Merge(unittest.TestCase):
    def test_fresh_replaces_archived_day_when_not_lower(self):
        old = [row("2026-10-10", cost=3.0), row("2026-10-10", project="beta-r1", cost=1.0)]
        new = [row("2026-10-10", cost=5.0)]
        merged, kept = sr.merge_archive(old, new, ["2026-10-10"])
        self.assertEqual(kept, [])
        self.assertEqual([(r["project"], r["cost_output"]) for r in merged], [("alpha-r1", 5.0)])

    def test_equal_total_is_replaced(self):
        old = [row("2026-10-10", cost=4.0, calls=1)]
        new = [row("2026-10-10", cost=4.0, calls=2)]
        merged, kept = sr.merge_archive(old, new, ["2026-10-10"])
        self.assertEqual(kept, [])
        self.assertEqual(merged[0]["calls"], 2)

    def test_lower_fresh_total_keeps_archived_rows_and_reports_the_day(self):
        old = [row("2026-10-10", cost=3.0), row("2026-10-10", project="beta-r1", cost=2.0)]
        new = [row("2026-10-10", cost=1.5)]
        merged, kept = sr.merge_archive(old, new, ["2026-10-10"])
        self.assertEqual(merged, sr.sort_flat(old))
        self.assertEqual(kept, [("2026-10-10", 1.5, 5.0)])

    def test_covered_day_with_no_fresh_rows_is_kept(self):
        old = [row("2026-10-09", cost=3.0)]
        merged, kept = sr.merge_archive(old, [], ["2026-10-08", "2026-10-09", "2026-10-10"])
        self.assertEqual(merged, old)
        self.assertEqual([k[0] for k in kept], ["2026-10-09"])

    def test_days_outside_the_scan_are_untouched(self):
        old = [row("2026-09-01", cost=9.0), row("2026-10-10", cost=1.0)]
        new = [row("2026-10-10", cost=2.0)]
        merged, kept = sr.merge_archive(old, new, ["2026-10-10"])
        self.assertEqual([(r["day"], r["cost_output"]) for r in merged], [("2026-09-01", 9.0), ("2026-10-10", 2.0)])

    def test_new_days_are_added_and_output_is_sorted_by_day_then_project(self):
        old = [row("2026-10-11", project="zeta"), row("2026-10-09", project="beta")]
        new = [row("2026-10-10", project="beta"), row("2026-10-10", project="alpha"), row("2026-10-12")]
        merged, _ = sr.merge_archive(old, new, ["2026-10-10", "2026-10-11", "2026-10-12"])
        self.assertEqual([(r["day"], r["project"]) for r in merged],
                         [("2026-10-09", "beta"), ("2026-10-10", "alpha"), ("2026-10-10", "beta"),
                          ("2026-10-11", "zeta"), ("2026-10-12", "alpha-r1")])

    def test_merge_is_idempotent(self):
        new = [row("2026-10-10", cost=2.0), row("2026-10-11", cost=1.0)]
        once, _ = sr.merge_archive([], new, ["2026-10-10", "2026-10-11"])
        twice, kept = sr.merge_archive(once, new, ["2026-10-10", "2026-10-11"])
        self.assertEqual(once, twice)
        self.assertEqual(kept, [])


class Files(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = os.path.join(self.tmp.name, "sub", "spend-daily.csv")

    def raw(self):
        with open(self.path, "rb") as fh:
            return fh.read()

    def test_roundtrip_and_columns_equal_csv_format(self):
        rows = [row("2026-10-10", cost=1.234567, calls=7)]
        sr.write_archive(self.path, rows)      # creates the missing folder
        with open(self.path, encoding="utf-8", newline="") as fh:
            header = next(csv.reader(fh))
        self.assertEqual(header, sr.CSV_COLUMNS)
        self.assertEqual(header, next(csv.reader(io.StringIO(sr.csv_report(rows)))))
        self.assertEqual(sr.read_archive(self.path), rows)

    def test_write_is_atomic_and_leaves_no_temp_files(self):
        sr.write_archive(self.path, [row("2026-10-10")])
        before = self.raw()
        orig = sr.csv_report
        sr.csv_report = lambda rows: (_ for _ in ()).throw(RuntimeError("boom"))
        try:
            with self.assertRaises(RuntimeError):
                sr.write_archive(self.path, [row("2026-10-11")])
        finally:
            sr.csv_report = orig
        self.assertEqual(self.raw(), before)                            # old archive intact
        self.assertEqual(os.listdir(os.path.dirname(self.path)), ["spend-daily.csv"])

    def test_missing_file_reads_as_empty(self):
        self.assertEqual(sr.read_archive(self.path), [])

    def test_foreign_csv_is_refused(self):
        os.makedirs(os.path.dirname(self.path))
        with open(self.path, "w", encoding="utf-8") as fh:
            fh.write("a,b\n1,2\n")
        with self.assertRaises(sr.ArchiveError):
            sr.read_archive(self.path)

    def test_tz_mismatch_is_refused(self):
        with self.assertRaises(sr.ArchiveError) as cm:
            sr.check_archive_tz([row("2026-10-10", tz="utc")], "local", self.path)
        self.assertIn("local", str(cm.exception))
        self.assertIn("utc", str(cm.exception))
        sr.check_archive_tz([row("2026-10-10", tz="local")], "local", self.path)   # same tz: fine
        sr.check_archive_tz([], "local", self.path)                                 # empty: fine


class Cli(unittest.TestCase):
    """End to end against a sandbox home (~/.claude/projects and the default archive path)."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.home = self.tmp.name
        self.projects = os.path.join(self.home, ".claude", "projects")
        self.now = datetime.now(timezone.utc)
        self.arch = os.path.join(self.tmp.name, "out", "spend-daily.csv")

    def iso(self, days_ago):
        return (self.now - timedelta(days=days_ago)).strftime("%Y-%m-%dT%H:%M:%S.000Z")

    def day(self, days_ago):
        return (self.now - timedelta(days=days_ago)).date().isoformat()

    def seed(self, n_today=1):
        write_transcript(self.projects, FOLDER_A, "s1.jsonl",
                         [call_line(self.iso(0), f"t{i}") for i in range(n_today)] + [call_line(self.iso(1), "y1")])
        write_transcript(self.projects, FOLDER_B, "s1.jsonl", [call_line(self.iso(1), "b1")])

    def run_cli(self, *args):
        return helpers.run_cli("spend-report.py", args, self.home)

    def read_arch(self):
        with open(self.arch, encoding="utf-8", newline="") as fh:
            return fh.read()

    def test_second_run_is_idempotent(self):
        self.seed()
        r1 = self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        self.assertEqual(r1.returncode, 0, r1.stderr)
        first = self.read_arch()
        r2 = self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        self.assertEqual(r2.returncode, 0, r2.stderr)
        self.assertEqual(self.read_arch(), first)
        rows = list(csv.DictReader(io.StringIO(first)))
        self.assertEqual(sum(int(x["calls"]) for x in rows), 3)
        self.assertEqual([(x["day"], x["project"]) for x in rows], sorted((x["day"], x["project"]) for x in rows))

    def test_default_path_is_under_the_user_profile_and_is_created(self):
        self.seed()
        r = self.run_cli("--tz", "utc", "--days", "2", "--archive")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(os.path.exists(os.path.join(self.home, ".claude", "spend-archive", "spend-daily.csv")))

    def test_never_shrinks_when_transcripts_are_gone(self):
        self.seed(n_today=3)
        self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        full = self.read_arch()
        for d in (FOLDER_A, FOLDER_B):                      # "Claude Code deleted the transcripts"
            os.remove(os.path.join(self.projects, d, "s1.jsonl"))
        r = self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(self.read_arch(), full)
        self.assertIn("WARN archive", r.stderr)
        self.assertIn("probably deleted", r.stderr)

    def test_partial_deletion_keeps_the_higher_archived_day(self):
        self.seed(n_today=3)
        self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        before = self.read_arch()
        write_transcript(self.projects, FOLDER_A, "s1.jsonl", [call_line(self.iso(0), "t0")])   # 3 calls -> 1
        r = self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        self.assertEqual(self.read_arch(), before)
        self.assertIn(self.day(0), r.stderr)

    def test_project_filter_does_not_narrow_the_archive(self):
        self.seed()
        r = self.run_cli("--tz", "utc", "--days", "3", "--project", "alpha", "--archive", self.arch)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual({x["project"] for x in csv.DictReader(io.StringIO(self.read_arch()))}, {"alpha-r1", "beta-r1"})

    def test_tz_mismatch_refuses_and_leaves_the_file_alone(self):
        self.seed()
        self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        before = self.read_arch()
        r = self.run_cli("--tz", "local", "--days", "3", "--archive", self.arch)
        self.assertEqual(r.returncode, 2)
        self.assertIn("utc", r.stderr)
        self.assertIn("Mixing bucket boundaries", r.stderr)
        self.assertEqual(self.read_arch(), before)

    def test_source_archive_reports_history_without_transcripts(self):
        self.seed()
        self.run_cli("--tz", "utc", "--days", "3", "--archive", self.arch)
        for d in (FOLDER_A, FOLDER_B):
            os.remove(os.path.join(self.projects, d, "s1.jsonl"))
        # Whole history, csv: identical rows to the archive.
        r = self.run_cli("--source", "archive", "--archive", self.arch, "--format", "csv")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.replace("\r\n", "\n"), self.read_arch().replace("\r\n", "\n"))
        self.assertNotIn("\n\n", r.stdout)       # stdout must not turn CRLF into CR CR LF (blank lines)
        # Filtered by --days / --today / --project, json.
        r = self.run_cli("--source", "archive", "--archive", self.arch, "--today", "--format", "json")
        d = json.loads(r.stdout)
        self.assertEqual((d["tz"], d["source"], d["since"], d["until"]), ("utc", "archive", self.day(0), self.day(0)))
        self.assertEqual(sum(x["calls"] for x in d["rows"]), 1)
        r = self.run_cli("--source", "archive", "--archive", self.arch, "--project", "beta", "--format", "csv")
        rows = list(csv.DictReader(io.StringIO(r.stdout)))
        self.assertEqual({x["project"] for x in rows}, {"beta-r1"})
        # A non-matching explicit --tz is refused, a text report states the archive's tz.
        self.assertEqual(self.run_cli("--source", "archive", "--archive", self.arch, "--tz", "local").returncode, 2)
        t = self.run_cli("--source", "archive", "--archive", self.arch)
        self.assertIn("days in UTC", t.stdout)

    def test_source_archive_without_a_file_fails_clearly(self):
        r = self.run_cli("--source", "archive", "--archive", self.arch)
        self.assertEqual(r.returncode, 2)
        self.assertIn("no archive", r.stderr)


if __name__ == "__main__":
    unittest.main()
