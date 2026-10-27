"""Never-go-down rule for scripts/ai-cost/update-board-spend.py. Decision logic only: nothing here
talks to GitHub (set_field / board_projects are replaced with fakes).

Run: python -m unittest discover -s scripts/ai-cost/tests
"""
import contextlib
import io
import os
import shutil
import tempfile
import unittest
from unittest import mock

import helpers

ubs = helpers.load("update-board-spend.py", "update_board_spend")
sr = ubs._sr


class DecideWrite(unittest.TestCase):
    def test_higher_or_equal_is_written_lower_is_not(self):
        self.assertEqual(ubs.decide_write(120.0, 50, 100.0, 40), (True, True))
        self.assertEqual(ubs.decide_write(100.0, 40, 100.0, 40), (True, True))     # equal refreshes As Of
        self.assertEqual(ubs.decide_write(80.0, 30, 100.0, 40), (False, False))

    def test_each_figure_is_judged_on_its_own(self):
        self.assertEqual(ubs.decide_write(80.0, 50, 100.0, 40), (False, True))
        self.assertEqual(ubs.decide_write(120.0, 30, 100.0, 40), (True, False))

    def test_unset_board_value_is_always_written(self):
        self.assertEqual(ubs.decide_write(0.0, 0, None, None), (True, True))
        self.assertEqual(ubs.decide_write(5.0, 3, None, 10), (True, False))


class BoardNumber(unittest.TestCase):
    def test_matches_gh_lowercased_first_letter(self):
        item = {"aI Spend (est.)": 5226.06, "aI Calls": 33847, "status": "Active"}
        self.assertEqual(ubs.board_number(item, "AI Spend (est.)"), 5226.06)
        self.assertEqual(ubs.board_number(item, "AI Calls"), 33847)

    def test_missing_or_non_numeric_is_none(self):
        self.assertIsNone(ubs.board_number({"status": "Active"}, "AI Calls"))
        self.assertIsNone(ubs.board_number({"aI Calls": "n/a"}, "AI Calls"))
        self.assertIsNone(ubs.board_number({"aI Calls": True}, "AI Calls"))
        self.assertEqual(ubs.board_number({"aI Calls": 0}, "AI Calls"), 0)         # zero is a value


class BestTotals(unittest.TestCase):
    def test_max_per_figure(self):
        t = {"cost": 100.0, "calls": 50}
        a = {"cost": 120.0, "calls": 40}
        self.assertEqual(ubs.best_totals(t, a), {"cost": 120.0, "calls": 50})

    def test_either_side_alone_and_neither(self):
        t = {"cost": 7.0, "calls": 2}
        self.assertEqual(ubs.best_totals(t, None), t)
        self.assertEqual(ubs.best_totals(None, t), t)
        self.assertIsNone(ubs.best_totals(None, None))


class ArchiveTotals(unittest.TestCase):
    def test_sums_every_day_per_project_case_insensitively(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "a.csv")
            rows = []
            for day, proj, cost, calls in (("2026-10-01", "Alpha-R1", 3.0, 2), ("2026-10-02", "Alpha-R1", 4.5, 3),
                                           ("2026-10-02", "beta", 1.0, 1)):
                r = dict.fromkeys(sr.CSV_COLUMNS, 0)
                r.update(day=day, project=proj, model="m", kind="main", tz="utc", calls=calls, cost_output=cost)
                r.update({k: 0.0 for k in sr.COST_FIELDS if k != "cost_output"})
                rows.append(r)
            sr.write_archive(path, rows)
            self.assertEqual(ubs.archive_totals(path),
                             {"alpha-r1": {"cost": 7.5, "calls": 5}, "beta": {"cost": 1.0, "calls": 1}})

    def test_missing_archive_is_empty(self):
        self.assertEqual(ubs.archive_totals(os.path.join(tempfile.gettempdir(), "no-such-archive.csv")), {})


class UpdateOne(unittest.TestCase):
    """update_one with fake transcripts + a recording set_field."""

    def setUp(self):
        self.writes = []
        self.transcript = {"cost": 100.0, "calls": 40}
        self.folder = "c--code-files-spaarke-wt-alpha-r1"
        patches = [
            mock.patch.object(ubs, "find_folder", lambda slug, known=None: self.folder),
            mock.patch.object(ubs, "cost_for_folder", lambda f: dict(self.transcript)),
            mock.patch.object(ubs, "set_field", self.fake_set_field),
        ]
        for p in patches:
            p.start()
            self.addCleanup(p.stop)

    def fake_set_field(self, item_id, field_id, flag, value):
        self.writes.append((field_id, value))
        return True, ""

    def fields(self):
        names = {ubs.F_SPEND: "spend", ubs.F_ASOF: "asof", ubs.F_CALLS: "calls"}
        return {names[f]: v for f, v in self.writes}

    def test_higher_fresh_writes_all_three_fields(self):
        status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertEqual((status, kept), ("updated", []))
        self.assertEqual(self.fields(), {"spend": 100.0, "asof": "2026-10-12", "calls": 40})

    def test_lower_fresh_writes_nothing_and_leaves_as_of_alone(self):
        self.transcript = {"cost": 60.0, "calls": 20}
        status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertEqual(status, "kept")
        self.assertEqual(self.writes, [])
        self.assertEqual(kept, ["spend", "calls"])

    def test_partial_write_keeps_the_lower_figure_and_updates_as_of(self):
        self.transcript = {"cost": 100.0, "calls": 20}
        status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertEqual((status, kept), ("updated", ["calls"]))
        self.assertEqual(self.fields(), {"spend": 100.0, "asof": "2026-10-12"})

    def test_archive_total_wins_when_higher_than_the_surviving_transcripts(self):
        self.transcript = {"cost": 60.0, "calls": 20}
        archive = {"alpha-r1": {"cost": 150.0, "calls": 70}}
        status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30, archive)
        self.assertEqual(status, "updated")
        self.assertEqual(self.fields(), {"spend": 150.0, "asof": "2026-10-12", "calls": 70})

    def test_archive_alone_is_enough_when_all_transcripts_are_gone(self):
        self.folder = None
        archive = {"alpha-r1": {"cost": 150.0, "calls": 70}}
        status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", None, None, archive)
        self.assertEqual(status, "updated")
        self.assertEqual(self.fields()["spend"], 150.0)

    def test_still_skips_projects_with_no_data_anywhere(self):
        self.folder = None
        self.assertEqual(ubs.update_one("alpha-r1", "ITEM", "d", 1.0, 1, {})[0], "no-local-history")
        self.folder = "c--code-files-spaarke-wt-alpha-r1"
        self.transcript = {"cost": 0.0, "calls": 0}
        self.assertEqual(ubs.update_one("alpha-r1", "ITEM", "d", 1.0, 1, {})[0], "no-usage-found")

    def test_as_of_is_written_last_and_only_after_a_figure_write_succeeded(self):
        order = []
        def fake(item_id, field_id, flag, value):
            order.append(field_id)
            return True, ""
        with mock.patch.object(ubs, "set_field", fake):
            ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertEqual(order, [ubs.F_SPEND, ubs.F_CALLS, ubs.F_ASOF])

    def test_as_of_not_written_when_every_figure_write_failed(self):
        calls = []
        def fake(item_id, field_id, flag, value):
            calls.append(field_id)
            return False, "boom"
        with mock.patch.object(ubs, "set_field", fake):
            status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertTrue(status.startswith("edit-failed"))
        self.assertNotIn(ubs.F_ASOF, calls)

    def test_as_of_written_when_one_figure_succeeded_and_one_failed(self):
        calls = []
        def fake(item_id, field_id, flag, value):
            calls.append(field_id)
            return field_id != ubs.F_SPEND, "no"
        with mock.patch.object(ubs, "set_field", fake):
            status, r, kept = ubs.update_one("alpha-r1", "ITEM", "2026-10-12", 90.0, 30)
        self.assertTrue(status.startswith("edit-failed"))
        self.assertEqual(calls[-1], ubs.F_ASOF)

    def test_failed_edit_is_reported(self):
        with mock.patch.object(ubs, "set_field", lambda *a: (False, "boom")):
            status, r, kept = ubs.update_one("alpha-r1", "ITEM", "d", None, None)
        self.assertTrue(status.startswith("edit-failed"))
        self.assertIn("boom", status)


class SlugToFolder(unittest.TestCase):
    """Board slug -> transcript folder -> archive key, against a sandbox ~/.claude/projects."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        for d in ("c--code-files-spaarke-wt-SPA-external-access-platform-r3",
                  "c--code-files-spaarke-wt-daily-update-service-r2",
                  "c--code-files-spaarke-wt-alpha-r1",
                  "c--code-files-spaarke-wt-big-alpha-r1"):
            os.makedirs(os.path.join(self.tmp.name, d))
        p = mock.patch.object(ubs._gpc, "CLAUDE_PROJECTS", self.tmp.name)
        p.start()
        self.addCleanup(p.stop)

    def test_leading_spaarke_prefix_is_stripped_for_the_folder_and_the_archive_key(self):
        slug = "spaarke-SPA-external-access-platform-r3"
        folder = ubs.find_folder(slug)
        self.assertEqual(folder, "c--code-files-spaarke-wt-SPA-external-access-platform-r3")
        archive = {"spa-external-access-platform-r3": {"cost": 9.0, "calls": 4}}
        self.assertEqual(ubs.archive_lookup(archive, slug, folder), {"cost": 9.0, "calls": 4})
        self.assertEqual(ubs.archive_lookup(archive, slug, None), {"cost": 9.0, "calls": 4})   # no folder: slug candidates

    def test_a_slug_never_fuzzy_matches_another_release(self):
        self.assertIsNone(ubs.find_folder("spaarke-daily-update-service"))
        self.assertIsNone(ubs.find_folder("daily-update-service"))
        self.assertEqual(ubs.find_folder("daily-update-service-r2"), "c--code-files-spaarke-wt-daily-update-service-r2")

    def test_exact_match_is_unaffected(self):
        self.assertEqual(ubs.find_folder("alpha-r1"), "c--code-files-spaarke-wt-alpha-r1")
        self.assertEqual(ubs.find_folder("ALPHA-R1"), "c--code-files-spaarke-wt-alpha-r1")      # case-insensitive
        self.assertEqual(ubs.find_folder("big-alpha-r1"), "c--code-files-spaarke-wt-big-alpha-r1")

    def test_exact_beats_the_stripped_form(self):
        os.makedirs(os.path.join(self.tmp.name, "c--code-files-spaarke-wt-spaarke-alpha-r1"))
        self.assertEqual(ubs.find_folder("spaarke-alpha-r1"), "c--code-files-spaarke-wt-spaarke-alpha-r1")

    def test_prefix_only_fuzzy_hit_is_a_last_resort_and_skips_another_boards_exact_folder(self):
        os.makedirs(os.path.join(self.tmp.name, "C--wtf2-gamma-r1"))
        self.assertEqual(ubs.find_folder("gamma-r1"), "C--wtf2-gamma-r1")
        # `alpha-r1` is exact for alpha-r1, but `big-alpha-r1` ends in it: with big-alpha-r1 a known
        # board slug, a folder-less `alpha-r1` lookup must not borrow big-alpha-r1's folder.
        shutil.rmtree(os.path.join(self.tmp.name, "c--code-files-spaarke-wt-alpha-r1"))
        self.assertEqual(ubs.find_folder("alpha-r1"), "c--code-files-spaarke-wt-big-alpha-r1")
        self.assertIsNone(ubs.find_folder("alpha-r1", ["alpha-r1", "big-alpha-r1"]))


class MainOutput(unittest.TestCase):
    def test_keep_line_and_summary(self):
        pairs = [("I1", "alpha-r1", 500.0, 900), ("I2", "beta-r1", 10.0, 5)]
        totals = {"c--code-files-spaarke-wt-alpha-r1": {"cost": 100.0, "calls": 40},
                  "c--code-files-spaarke-wt-beta-r1": {"cost": 20.0, "calls": 9}}
        writes = []
        out = io.StringIO()
        with mock.patch.object(ubs, "board_projects", lambda: pairs), \
                mock.patch.object(ubs, "archive_totals", lambda: {}), \
                mock.patch.object(ubs, "allocated_totals", lambda: ({}, None)), \
                mock.patch.object(ubs, "find_folder", lambda s, known=None: "c--code-files-spaarke-wt-" + s), \
                mock.patch.object(ubs, "cost_for_folder", lambda f: dict(totals[f])), \
                mock.patch.object(ubs, "set_field", lambda *a: (writes.append(a) or (True, ""))), \
                mock.patch("sys.argv", ["update-board-spend.py"]), \
                contextlib.redirect_stdout(out):
            ubs.main()
        text = out.getvalue()
        self.assertIn("KEEP alpha-r1 (fresh < board; transcripts likely pruned)", text)
        self.assertIn("OK       beta-r1", text)
        self.assertIn("1 updated, 1 kept (board already higher)", text)
        self.assertIn("AI Cost (allocated): not written", text)
        # set_field(item_id, field_id, flag, value): only beta-r1 (I2) was written; alpha-r1 (I1) not at all.
        self.assertEqual({w[0] for w in writes}, {"I2"})
        self.assertEqual({w[1] for w in writes}, {ubs.F_SPEND, ubs.F_ASOF, ubs.F_CALLS})
        self.assertEqual(len(writes), 3)

    def test_allocated_is_written_even_when_lower_and_only_for_projects_with_an_allocation(self):
        pairs = [("I1", "alpha-r1", 500.0, 900), ("I2", "beta-r1", None, None)]
        writes = []
        out = io.StringIO()
        with mock.patch.object(ubs, "board_projects", lambda: pairs), \
                mock.patch.object(ubs, "archive_totals", lambda: {}), \
                mock.patch.object(ubs, "allocated_totals", lambda: ({"alpha-r1": 12.5}, "2026-10-23")), \
                mock.patch.object(ubs, "find_folder", lambda s, known=None: "c--code-files-spaarke-wt-" + s), \
                mock.patch.object(ubs, "cost_for_folder", lambda f: {"cost": 1.0, "calls": 1}), \
                mock.patch.object(ubs, "set_field", lambda *a: (writes.append(a) or (True, ""))), \
                mock.patch("sys.argv", ["update-board-spend.py"]), \
                contextlib.redirect_stdout(out):
            ubs.main()
        alloc = [w for w in writes if w[1] == ubs.F_ALLOC]
        self.assertEqual(alloc, [("I1", ubs.F_ALLOC, "--number", 12.5)])   # alpha's spend was KEEP, allocated still written
        self.assertIn("AI Cost (allocated): 1 written, 0 failed (invoices through 2026-10-23)", out.getvalue())


if __name__ == "__main__":
    unittest.main()
