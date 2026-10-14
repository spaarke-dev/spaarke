"""Calendar-day bucketing, --tz and --today for scripts/ai-cost/spend-report.py.

Run: python -m unittest discover -s scripts/ai-cost/tests
"""
import csv
import io
import json
import os
import tempfile
import time
import unittest
from datetime import datetime, timedelta, timezone

import helpers
from helpers import call_line, write_transcript

sr = helpers.load("spend-report.py", "spend_report")

UTC_M5 = timezone(timedelta(hours=-5))
UTC_P10 = timezone(timedelta(hours=10))
FOLDER = "c--code-files-spaarke-wt-demo-r1"


def epoch(iso):
    return datetime.fromisoformat(iso.replace("Z", "+00:00")).timestamp()


class DayOf(unittest.TestCase):
    def test_utc_uses_the_utc_date(self):
        self.assertEqual(sr.day_of("2026-10-12T03:44:00.000Z", timezone.utc), "2026-10-12")

    def test_west_of_utc_is_still_the_previous_day(self):
        self.assertEqual(sr.day_of("2026-10-12T03:44:00.000Z", UTC_M5), "2026-10-11")

    def test_east_of_utc_is_already_the_next_day(self):
        self.assertEqual(sr.day_of("2026-10-11T15:00:00Z", UTC_P10), "2026-10-12")

    def test_local_matches_the_system_zone(self):
        # Independent path: time.localtime on the epoch, not astimezone().
        for iso in ("2026-01-15T02:00:00Z", "2026-07-15T02:00:00Z", "2026-10-12T03:44:00Z"):
            lt = time.localtime(epoch(iso))
            self.assertEqual(sr.day_of(iso, None), f"{lt.tm_year:04d}-{lt.tm_mon:02d}-{lt.tm_mday:02d}", iso)

    def test_naive_timestamp_is_treated_as_utc(self):
        self.assertEqual(sr.day_of("2026-10-12T03:44:00", UTC_M5), "2026-10-11")


class Window(unittest.TestCase):
    NOW = datetime(2026, 10, 12, 3, 44, tzinfo=timezone.utc)   # 23:44 on the 11th at UTC-4/-5

    def test_today_is_the_calendar_day_in_the_requested_tz(self):
        self.assertEqual(sr.resolve_window(None, None, True, timezone.utc, self.NOW), ("2026-10-12", "2026-10-12"))
        self.assertEqual(sr.resolve_window(None, None, True, UTC_M5, self.NOW), ("2026-10-11", "2026-10-11"))

    def test_default_is_fourteen_calendar_days_ending_today(self):
        self.assertEqual(sr.resolve_window(None, None, False, UTC_M5, self.NOW), ("2026-09-28", "2026-10-11"))

    def test_days_and_since(self):
        self.assertEqual(sr.resolve_window(3, None, False, timezone.utc, self.NOW), ("2026-10-10", "2026-10-12"))
        self.assertEqual(sr.resolve_window(None, "2026-10-01", False, timezone.utc, self.NOW), ("2026-10-01", "2026-10-12"))


class MtimeCutoff(unittest.TestCase):
    def test_cutoff_is_not_after_the_start_of_the_first_day_in_tz(self):
        # UTC+10: local 2026-10-12 00:00 is 2026-10-11T14:00Z, before UTC midnight of the 12th.
        start = epoch("2026-10-11T14:00:00Z")
        self.assertLessEqual(sr.mtime_cutoff("2026-10-12", UTC_P10), start)
        # The old cutoff (UTC midnight of the since day) would have been later than this file's mtime.
        old_cutoff = epoch("2026-10-12T00:00:00Z")
        self.assertGreater(old_cutoff, epoch("2026-10-11T15:00:00Z"))
        self.assertLess(sr.mtime_cutoff("2026-10-12", UTC_P10), epoch("2026-10-11T15:00:00Z"))

    def test_local_cutoff_never_after_local_midnight(self):
        lt = time.mktime((2026, 10, 12, 0, 0, 0, 0, 0, -1))
        self.assertLessEqual(sr.mtime_cutoff("2026-10-12", None), lt)


class Collect(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name
        self.addCleanup(self.tmp.cleanup)

    def days(self, rows):
        out = {}
        for (day, _p, _m, _k), r in rows.items():
            out[day] = out.get(day, 0) + r["calls"]
        return out

    def test_calls_are_bucketed_by_the_tz_day(self):
        write_transcript(self.root, FOLDER, "s1.jsonl", [
            call_line("2026-10-12T03:00:00.000Z", "m1"),    # 22:00 on the 11th at UTC-5
            call_line("2026-10-12T06:00:00.000Z", "m2"),    # 01:00 on the 12th at UTC-5
        ])
        rows, _ = sr.collect("2026-10-11", UTC_M5, self.root)
        self.assertEqual(self.days(rows), {"2026-10-11": 1, "2026-10-12": 1})
        rows, _ = sr.collect("2026-10-11", timezone.utc, self.root)
        self.assertEqual(self.days(rows), {"2026-10-12": 2})

    def test_file_with_requested_local_day_is_not_dropped_by_the_mtime_prefilter(self):
        # UTC+10, since local 2026-10-12. The only call is at 01:00 local on the 12th (15:00Z on the
        # 11th) and the file was last written at that moment. A UTC-midnight cutoff dropped it.
        ts = "2026-10-11T15:00:00.000Z"
        write_transcript(self.root, FOLDER, "s1.jsonl", [call_line(ts, "m1")], mtime=epoch(ts))
        rows, st = sr.collect("2026-10-12", UTC_P10, self.root)
        self.assertEqual(st["files"], 1)
        self.assertEqual(self.days(rows), {"2026-10-12": 1})

    def test_old_files_are_still_skipped(self):
        ts = "2026-08-01T15:00:00.000Z"
        write_transcript(self.root, FOLDER, "old.jsonl", [call_line(ts, "m1")], mtime=epoch(ts))
        rows, st = sr.collect("2026-10-12", UTC_P10, self.root)
        self.assertEqual(st["files"], 0)
        self.assertEqual(rows, {})

    def test_calls_before_the_window_are_excluded_and_duplicates_counted_once(self):
        now = epoch("2026-10-12T12:00:00Z")
        write_transcript(self.root, FOLDER, "a.jsonl", [
            call_line("2026-10-12T05:00:00Z", "m1"), call_line("2026-10-09T05:00:00Z", "m0"),
        ], mtime=now)
        write_transcript(self.root, FOLDER, "b.jsonl", [call_line("2026-10-12T05:00:00Z", "m1")], mtime=now)
        rows, _ = sr.collect("2026-10-12", timezone.utc, self.root)
        self.assertEqual(self.days(rows), {"2026-10-12": 1})


class Output(unittest.TestCase):
    def test_flat_rows_and_csv_carry_tz(self):
        rows = {("2026-10-11", "demo", "m", "main"): sr.new_row()}
        fr = sr.flat(rows, [], "utc")
        self.assertEqual(fr[0]["tz"], "utc")
        header = next(csv.reader(io.StringIO(sr.csv_report(fr))))
        self.assertEqual(header[-1], "tz")
        self.assertEqual(header[:4], ["day", "project", "model", "kind"])
        self.assertEqual(list(csv.DictReader(io.StringIO(sr.csv_report(fr))))[0]["tz"], "utc")

    def test_text_header_states_the_tz(self):
        txt = sr.text_report([], "2026-10-11", "2026-10-11", "local time (X, UTC-04:00)")
        self.assertIn("days in local time (X, UTC-04:00)", txt)
        self.assertNotIn(" UTC  total", txt)

    def test_html_embeds_tz(self):
        html = sr.html_report([], "2026-10-11", "2026-10-11", "local", "local time (X, UTC-04:00)")
        self.assertIn('"tz":"local"', html)
        self.assertIn("local time (X, UTC-04:00)", html)
        self.assertNotIn("UTC days", html)

    def test_tz_label(self):
        self.assertEqual(sr.tz_label("utc"), "UTC")
        self.assertTrue(sr.tz_label("local").startswith("local time ("))


class Cli(unittest.TestCase):
    """End to end against a sandbox ~/.claude/projects (USERPROFILE/HOME redirected)."""

    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.home = cls.tmp.name
        projects = os.path.join(cls.home, ".claude", "projects")
        now = datetime.now(timezone.utc)
        iso = lambda dt: dt.strftime("%Y-%m-%dT%H:%M:%S.000Z")
        write_transcript(projects, FOLDER, "s1.jsonl", [
            call_line(iso(now), "today-1"),
            call_line(iso(now - timedelta(days=2)), "old-1"),
        ])

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def run_cli(self, *args):
        return helpers.run_cli("spend-report.py", args, self.home)

    def test_today_is_exclusive_with_days_and_since(self):
        for extra in (["--days", "3"], ["--since", "2026-10-01"]):
            r = self.run_cli("--today", *extra)
            self.assertEqual(r.returncode, 2, r.stderr)
            self.assertIn("--today", r.stderr)

    def test_today_json_has_only_today_and_tz(self):
        r = self.run_cli("--today", "--format", "json")
        self.assertEqual(r.returncode, 0, r.stderr)
        d = json.loads(r.stdout)
        self.assertEqual(d["tz"], "local")
        self.assertEqual(d["since"], d["until"])
        self.assertEqual({x["day"] for x in d["rows"]}, {d["since"]})
        self.assertEqual(sum(x["calls"] for x in d["rows"]), 1)
        self.assertTrue(all(x["tz"] == "local" for x in d["rows"]))

    def test_default_window_includes_older_calls_and_utc_flag(self):
        r = self.run_cli("--tz", "utc", "--days", "5", "--format", "csv")
        self.assertEqual(r.returncode, 0, r.stderr)
        rows = list(csv.DictReader(io.StringIO(r.stdout)))
        self.assertEqual(sum(int(x["calls"]) for x in rows), 2)
        self.assertEqual({x["tz"] for x in rows}, {"utc"})

    def test_text_states_the_tz(self):
        r = self.run_cli("--today")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("days in local time (", r.stdout)


if __name__ == "__main__":
    unittest.main()
