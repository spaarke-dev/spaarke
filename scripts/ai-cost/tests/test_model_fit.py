"""--model-fit for scripts/ai-cost/spend-report.py: launch/transcript matching, work types, rework, flags.

Synthetic fixtures only (a temp ~/.claude/projects tree, a temp launch log, a temp repo of task notes).
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
from helpers import call_line, run_cli

sr = helpers.load("spend-report.py", "spend_report")

UTC = timezone.utc
FOLDER = "c--code-files-spaarke-wt-demo-r1"
PROJECT = "demo-r1"
SESSION = "sess-1"
DAY = "2026-10-13"
SONNET, OPUS, FABLE = "claude-sonnet-5", "claude-opus-5-5", "claude-fable-5-1"
LOG_COLUMNS = ["ts_utc", "session_id", "transcript_path", "project", "tool", "subagent_type", "model", "effort",
               "description", "tool_use_id"]


def ts(minute, second=0, day=DAY):
    return f"{day}T10:{minute:02d}:{second:02d}Z"


class Fixture:
    """A throwaway Claude Code projects tree + launch log + repo of task notes."""

    def __init__(self, test):
        self.tmp = tempfile.TemporaryDirectory()
        test.addCleanup(self.tmp.cleanup)
        self.root = os.path.join(self.tmp.name, "projects")
        self.repo = os.path.join(self.tmp.name, "repo")
        self.log = os.path.join(self.tmp.name, "launches.csv")
        self.n = 0
        self.launch_rows = []

    def unit(self, agent_id, start_minute, model=SONNET, calls=2, meta=None, session=SESSION, folder=FOLDER):
        d = os.path.join(self.root, folder, session, "subagents")
        os.makedirs(d, exist_ok=True)
        lines = []
        for i in range(calls):
            self.n += 1
            lines.append(call_line(ts(start_minute, i), f"m{self.n}", model=model))
        with open(os.path.join(d, f"agent-{agent_id}.jsonl"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        if meta is not None:
            with open(os.path.join(d, f"agent-{agent_id}.meta.json"), "w", encoding="utf-8") as fh:
                json.dump(meta, fh)

    def main(self, model, calls, session=SESSION, folder=FOLDER):
        d = os.path.join(self.root, folder)
        os.makedirs(d, exist_ok=True)
        lines = []
        for i in range(calls):
            self.n += 1
            lines.append(call_line(ts(1, i), f"m{self.n}", model=model))
        with open(os.path.join(d, f"{session}.jsonl"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")

    def launch(self, minute, desc, kind="implementer", tool="Agent", tool_use_id="", model="sonnet", session=SESSION,
               project=PROJECT, day=DAY):
        self.launch_rows.append({"ts_utc": ts(minute, 0, day), "session_id": session, "transcript_path": "",
                                 "project": project, "tool": tool, "subagent_type": kind, "model": model,
                                 "effort": "", "description": desc, "tool_use_id": tool_use_id})

    def write_log(self):
        with open(self.log, "w", encoding="utf-8", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=LOG_COLUMNS)
            w.writeheader()
            w.writerows(self.launch_rows)

    def note(self, slug, name, text, sub="notes"):
        d = os.path.join(self.repo, "projects", slug, sub)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, name), "w", encoding="utf-8") as fh:
            fh.write(text)

    def report(self, projects=()):
        self.write_log()
        os.makedirs(self.repo, exist_ok=True)
        return sr.model_fit_report(DAY, DAY, UTC, "utc", list(projects), self.repo, self.log, self.root)


def meta(desc, kind="implementer", tool_use_id="", **kw):
    return dict({"agentType": kind, "description": desc, "toolUseId": tool_use_id, "model": "sonnet"}, **kw)


def row(rows, work_type, model):
    return next(r for r in rows if r["work_type"] == work_type and r["model"] == model)


class WorkTypes(unittest.TestCase):
    def test_specific_types_keep_their_category(self):
        self.assertEqual(sr.work_type("code-mapper", "implement the inventory"), ("code-mapper", "map"))
        self.assertEqual(sr.work_type("adversarial-reviewer", "deploy script check"), ("adversarial-reviewer", "review"))
        self.assertEqual(sr.work_type("Explore", "x"), ("explore", "map"))
        self.assertEqual(sr.work_type("researcher", ""), ("researcher", "research"))
        self.assertEqual(sr.work_type("claude-code-guide", "x"), ("claude-code-guide", "lookup"))

    def test_implementer_is_refined_by_deploy_and_test(self):
        self.assertEqual(sr.work_type("implementer", "Fix legacy error bodies"), ("implementer", "implement"))
        self.assertEqual(sr.work_type("implementer", "deploy the BFF to dev"), ("implementer", "deploy"))
        self.assertEqual(sr.work_type("implementer", "run the contract tests"), ("implementer", "test"))
        self.assertEqual(sr.work_type("implementer", "Build the thing"), ("implementer", "implement"))

    def test_general_purpose_is_decided_by_keywords(self):
        cases = {"Review the diff": "review", "verify claims": "review", "Find usages of X": "map",
                 "search for callers": "map", "inventory of endpoints": "map", "map the call flow": "map",
                 "Investigate the 403": "research", "research SPE limits": "research", "Add the endpoint": "implement",
                 "fix the null ref": "implement", "Deploy to demo": "deploy", "write tests": "test",
                 "do the thing": "other"}
        for desc, cat in cases.items():
            self.assertEqual(sr.work_type("", desc), ("general-purpose", cat), desc)
            self.assertEqual(sr.work_type("general-purpose", desc), ("general-purpose", cat), desc)

    def test_words_that_only_contain_a_keyword_do_not_match(self):
        self.assertEqual(sr.work_type("", "address the roadmap")[1], "other")   # add / map inside longer words

    def test_model_family(self):
        self.assertEqual([sr.model_family(m) for m in (SONNET, OPUS, FABLE, "claude-haiku-4-5", "x", "")],
                         ["sonnet", "opus", "fable", "haiku", "other", "other"])


class Matching(unittest.TestCase):
    def test_match_by_tool_use_id(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("same words", tool_use_id="toolu_A"))
        fx.unit("a2", 5, meta=meta("same words", tool_use_id="toolu_B"))
        fx.launch(5, "same words", tool_use_id="toolu_B")
        rows, s = fx.report()
        self.assertEqual((s["matched"], s["unmatched_launches"], s["unmatched_transcripts"]), (1, 0, 1))

    def test_match_by_session_and_description_without_tool_use_id(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("Fix the thing"))
        fx.unit("a2", 5, meta=meta("Another job"))
        fx.launch(5, "Fix the thing")
        rows, s = fx.report()
        self.assertEqual((s["matched"], s["unmatched_launches"], s["unmatched_transcripts"]), (1, 0, 1))

    def test_other_session_never_matches(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("Fix the thing"), session="other-session")
        fx.launch(5, "Fix the thing")
        rows, s = fx.report()
        self.assertEqual((s["matched"], s["unmatched_launches"], s["unmatched_transcripts"]), (0, 1, 1))

    def test_same_description_twice_pairs_in_start_order(self):
        fx = Fixture(self)
        fx.unit("late", 20, meta=meta("same job"))
        fx.unit("early", 5, meta=meta("same job"))
        fx.launch(4, "same job")
        fx.launch(19, "same job")
        fx.write_log()
        units, _mc, _st = sr.collect_model_fit(DAY, DAY, UTC, fx.root)
        launches, _t, _b = sr.read_launches(fx.log, DAY, DAY, UTC)
        self.assertEqual(sr.match_launches(launches, units), 2)
        self.assertEqual([l["unit"]["agent_id"] for l in launches], ["agent-early", "agent-late"])

    def test_nearest_start_time_when_there_is_no_meta(self):
        fx = Fixture(self)
        fx.unit("nometa", 5)                         # no meta file at all
        fx.launch(5, "whatever the description was")
        rows, s = fx.report()
        self.assertEqual((s["matched"], s["unmatched_launches"], s["unmatched_transcripts"]), (1, 0, 0))

    def test_nearest_start_time_has_a_limit(self):
        fx = Fixture(self)
        fx.unit("nometa", 30)                        # starts 25 minutes after the launch: not the same agent
        fx.launch(5, "whatever")
        rows, s = fx.report()
        self.assertEqual((s["matched"], s["unmatched_launches"], s["unmatched_transcripts"]), (0, 1, 1))

    def test_unmatched_are_counted_and_the_transcript_stays_in_the_table(self):
        fx = Fixture(self)
        fx.unit("orphan", 5, meta=meta("Review the diff", kind="adversarial-reviewer"))
        fx.launch(40, "launched but never ran")
        rows, s = fx.report()
        self.assertEqual((s["agent_launches"], s["unmatched_launches"], s["transcripts"], s["unmatched_transcripts"]),
                         (1, 1, 1, 1))
        r = row(rows, "adversarial-reviewer / review", "sonnet")
        self.assertEqual((r["launches"], r["logged"]), (1, 0))

    def test_workflow_launches_are_counted_not_matched(self):
        fx = Fixture(self)
        fx.unit("w1", 5, meta=meta("step one", kind="general-purpose"))
        fx.launch(5, "the workflow", kind="", tool="Workflow")
        rows, s = fx.report()
        self.assertEqual((s["workflow_launches"], s["agent_launches"], s["matched"], s["unmatched_launches"]), (1, 0, 0, 0))
        self.assertEqual(s["unmatched_transcripts"], 1)

    def test_missing_launch_log_means_every_transcript_is_unmatched(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("Fix the thing"))
        os.makedirs(fx.repo, exist_ok=True)
        rows, s = sr.model_fit_report(DAY, DAY, UTC, "utc", [], fx.repo, os.path.join(fx.tmp.name, "nope.csv"), fx.root)
        self.assertEqual((s["launch_log_exists"], s["agent_launches"], s["transcripts"], s["unmatched_transcripts"]),
                         (False, 0, 1, 1))
        self.assertEqual(len(rows), 1)

    def test_malformed_log_lines_are_skipped_and_counted(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("Fix the thing"))
        fx.launch(5, "Fix the thing")
        fx.write_log()
        with open(fx.log, "a", encoding="utf-8") as fh:
            fh.write("not-a-timestamp,s,,p,Agent,x,m,e,d,\n")
        os.makedirs(fx.repo, exist_ok=True)
        rows, s = sr.model_fit_report(DAY, DAY, UTC, "utc", [], fx.repo, fx.log, fx.root)
        self.assertEqual((s["log_lines"], s["log_lines_malformed"], s["matched"]), (2, 1, 1))

    def test_launches_outside_the_window_are_ignored(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("Fix the thing"))
        fx.launch(5, "Fix the thing", day="2026-10-01")
        rows, s = fx.report()
        self.assertEqual((s["agent_launches"], s["matched"]), (0, 0))


class Reworks(unittest.TestCase):
    def parse(self, files):
        fx = Fixture(self)
        for slug, name, text, *sub in files:
            fx.note(slug, name, text, *(sub or ["notes"]))
        return sr.parse_rework(fx.repo)

    def test_the_three_values(self):
        got = self.parse([("demo-r1", "task-001-a.md", "rework: none\n"),
                          ("demo-r1", "task-002-b.md", "- rework: fix-round-2\n"),
                          ("demo-r1", "task-003-c.md", "**rework**: escalated-to-opus\n")])
        self.assertEqual(got, {("demo-r1", 1): ("none", ""), ("demo-r1", 2): ("fix", "2"),
                               ("demo-r1", 3): ("escalated", "opus")})

    def test_task_id_from_the_line_beats_the_file_name(self):
        got = self.parse([("demo-r1", "summary.md", "rework: fix-round-1 task 042\nrework: none (task 43)\n")])
        self.assertEqual(got, {("demo-r1", 42): ("fix", "1"), ("demo-r1", 43): ("none", "")})

    def test_task_id_from_a_poml_file_name(self):
        got = self.parse([("demo-r1", "042-do-it.poml", "<notes>\n  rework: fix-round-1\n</notes>\n", "tasks")])
        self.assertEqual(got, {("demo-r1", 42): ("fix", "1")})

    def test_no_task_id_means_no_record(self):
        self.assertEqual(self.parse([("demo-r1", "summary.md", "rework: none\n")]), {})

    def test_the_worst_value_wins(self):
        got = self.parse([("demo-r1", "task-005-a.md", "rework: none\nrework: escalated-to-fable\nrework: fix-round-1\n")])
        self.assertEqual(got, {("demo-r1", 5): ("escalated", "fable")})

    def test_lines_that_only_mention_the_word_are_not_records(self):
        text = ("The rework: none format is described below.\n"
                "rework: maybe\nrework: fix-round\nrework: fix-round-x\nrework: escalated-to-\nno rework: none here\n")
        self.assertEqual(self.parse([("demo-r1", "task-007-a.md", text)]), {})

    def test_other_projects_do_not_collide(self):
        got = self.parse([("demo-r1", "task-001-a.md", "rework: none\n"), ("other-r2", "task-001-a.md", "rework: fix-round-1\n")])
        self.assertEqual(got[("demo-r1", 1)][0], "none")
        self.assertEqual(got[("other-r2", 1)][0], "fix")


class Attribution(unittest.TestCase):
    def setUp(self):
        self.fx = Fixture(self)

    def test_rework_is_attributed_by_project_and_task_id_in_the_description(self):
        fx = self.fx
        fx.unit("a1", 5, meta=meta("implement task 042"))
        fx.unit("a2", 10, meta=meta("implement task 043"))
        fx.unit("a3", 15, meta=meta("implement task 044"))
        fx.unit("a4", 20, meta=meta("implement the thing"))                  # no task id
        fx.launch(5, "implement task 042")
        fx.launch(10, "implement task 043")
        fx.launch(15, "implement task 044")                                  # task 044 has no record
        fx.note(PROJECT, "task-042-x.md", "rework: fix-round-1\n")
        fx.note(PROJECT, "task-043-x.md", "rework: none\n")
        fx.note("other-r2", "task-044-x.md", "rework: fix-round-3\n")        # another project's 044
        rows, s = fx.report()
        r = row(rows, "implementer / implement", "sonnet")
        self.assertEqual((r["launches"], r["rework_known"], r["rework_fix"], r["rework_escalated"]), (4, 2, 1, 0))
        self.assertEqual(r["rework_rate"], 0.5)

    def test_launch_description_wins_over_meta(self):
        fx = self.fx
        fx.unit("a1", 5, meta=meta("do task 009", tool_use_id="t1"))
        fx.launch(5, "do the thing", tool_use_id="t1")                # matched by id; the launch says no task id
        fx.write_log()
        fx.note(PROJECT, "task-009-x.md", "rework: escalated-to-opus\n")
        units, _m, _s = sr.collect_model_fit(DAY, DAY, UTC, fx.root)
        launches, _t, _b = sr.read_launches(fx.log, DAY, DAY, UTC)
        self.assertEqual(sr.match_launches(launches, units), 1)
        self.assertEqual(sr.unit_rework(units[0], sr.parse_rework(fx.repo)), "unknown")

    def test_spaarke_prefixed_slug_is_found(self):
        self.assertEqual(sr.unit_rework({"launch": None, "meta": {"description": "do task 7"}, "project": "spaarke-demo"},
                                        {("demo", 7): ("fix", "1")}), "fix")

    def test_unknown_is_shown_not_guessed(self):
        fx = self.fx
        fx.unit("a1", 5, meta=meta("implement task 042"))
        rows, s = fx.report()                                                 # no notes at all
        r = row(rows, "implementer / implement", "sonnet")
        self.assertEqual((r["rework_known"], r["rework_rate"]), (0, None))
        self.assertEqual(sr.rework_text(r), "unknown")


class Flags(unittest.TestCase):
    def fixture_with(self, units):
        """units: [(model, desc, kind, rework value or None)] - one transcript each, tasks numbered 1..n."""
        fx = Fixture(self)
        for i, (model, desc, kind, rw) in enumerate(units, start=1):
            fx.unit(f"u{i}", 2 + i, model=model, meta=meta(f"{desc} task {i:03d}", kind=kind))
            if rw:
                fx.note(PROJECT, f"task-{i:03d}-x.md", f"rework: {rw}\n")
        return fx

    def test_top_tier_on_mapping_or_deploy_is_over_provisioned(self):
        fx = self.fixture_with([(OPUS, "map the callers", "code-mapper", None), (FABLE, "deploy the BFF", "implementer", None)])
        rows, _ = fx.report()
        self.assertTrue(row(rows, "code-mapper / map", "opus")["flag"].startswith("OVER"))
        self.assertTrue(row(rows, "implementer / deploy", "fable")["flag"].startswith("OVER"))

    def test_sonnet_on_mapping_is_not_flagged(self):
        fx = self.fixture_with([(SONNET, "map the callers", "code-mapper", None), (SONNET, "deploy the BFF", "implementer", None)])
        rows, _ = fx.report()
        self.assertEqual([r["flag"] for r in rows], ["", ""])

    def test_top_tier_on_hard_work_without_data_is_not_flagged(self):
        fx = self.fixture_with([(OPUS, "implement the endpoint", "implementer", None),
                                (FABLE, "review the diff", "adversarial-reviewer", None)])
        rows, _ = fx.report()
        self.assertEqual([r["flag"] for r in rows], ["", ""])

    def test_over_when_sonnet_rework_is_as_low(self):
        units = [(OPUS, "implement", "implementer", "fix-round-1")] * 3 + [(SONNET, "implement", "implementer", "none")] * 3
        # opus: 3/3 rework; sonnet 0/3 -> sonnet as low (lower) -> opus over-provisioned
        rows, _ = self.fixture_with(units).report()
        flag = row(rows, "implementer / implement", "opus")["flag"]
        self.assertTrue(flag.startswith("OVER") and "sonnet rework 0%" in flag, flag)
        self.assertEqual(row(rows, "implementer / implement", "sonnet")["flag"], "")

    def test_not_over_when_sonnet_rework_is_higher(self):
        units = [(OPUS, "implement", "implementer", "none")] * 3 + [(SONNET, "implement", "implementer", "fix-round-1")] * 3
        rows, _ = self.fixture_with(units).report()
        self.assertEqual(row(rows, "implementer / implement", "opus")["flag"], "")

    def test_too_little_data_is_not_compared(self):
        units = [(OPUS, "implement", "implementer", "fix-round-1")] * 2 + [(SONNET, "implement", "implementer", "none")] * 2
        rows, _ = self.fixture_with(units).report()
        self.assertEqual(row(rows, "implementer / implement", "opus")["flag"], "")

    def test_under_when_sonnet_rework_is_well_above_the_type_average(self):
        units = [(SONNET, "implement", "implementer", "fix-round-1")] * 3 + [(OPUS, "implement", "implementer", "none")] * 3
        # sonnet 3/3 = 100%, type average 3/6 = 50% -> +50 points
        rows, _ = self.fixture_with(units).report()
        self.assertTrue(row(rows, "implementer / implement", "sonnet")["flag"].startswith("UNDER"))

    def test_not_under_when_close_to_the_average(self):
        units = ([(SONNET, "implement", "implementer", "fix-round-1")] * 2 + [(SONNET, "implement", "implementer", "none")] * 2
                 + [(OPUS, "implement", "implementer", "fix-round-1")] * 2 + [(OPUS, "implement", "implementer", "none")] * 2)
        rows, _ = self.fixture_with(units).report()
        self.assertEqual(row(rows, "implementer / implement", "sonnet")["flag"], "")

    def test_under_on_any_escalation(self):
        rows, _ = self.fixture_with([(SONNET, "implement", "implementer", "escalated-to-opus")]).report()
        flag = row(rows, "implementer / implement", "sonnet")["flag"]
        self.assertTrue(flag.startswith("UNDER") and "1 escalated" in flag, flag)


class Table(unittest.TestCase):
    def test_columns(self):
        fx = Fixture(self)
        fx.unit("a1", 5, model=SONNET, calls=2, meta=meta("fix a"))      # 2 calls x 1.1M tokens
        fx.unit("a2", 10, model=SONNET, calls=4, meta=meta("fix b"))
        fx.unit("a3", 15, model=OPUS, calls=1, meta=meta("fix c"))
        rows, s = fx.report()
        r = row(rows, "implementer / implement", "sonnet")
        self.assertEqual((r["launches"], r["calls"], r["avg_calls"]), (2, 6, 3.0))
        self.assertAlmostEqual(r["cost_total"], 6 * 3.0)                  # 3.00 per default call at sonnet-5 prices
        self.assertAlmostEqual(r["cost_per_launch"], 9.0)
        self.assertEqual(r["avg_ctx"], 1_000_000)
        self.assertEqual(row(rows, "implementer / implement", "opus")["launches"], 1)

    def test_a_transcript_uses_its_most_used_model(self):
        fx = Fixture(self)
        d = os.path.join(fx.root, FOLDER, SESSION, "subagents")
        os.makedirs(d)
        lines = [call_line(ts(5, 0), "x1", model=OPUS), call_line(ts(5, 1), "x2", model=SONNET),
                 call_line(ts(5, 2), "x3", model=SONNET)]
        with open(os.path.join(d, "agent-mixed.jsonl"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        rows, _ = fx.report()
        self.assertEqual([r["model"] for r in rows], ["sonnet"])

    def test_main_session_model_share(self):
        fx = Fixture(self)
        fx.main(OPUS, 6)
        fx.main(SONNET, 2, session="s2")
        fx.unit("a1", 5, model=SONNET, calls=3, meta=meta("fix a"))
        rows, s = fx.report()
        self.assertEqual(s["main_calls"], {"opus": 6, "sonnet": 2})
        self.assertEqual(s["subagent_calls"], {"sonnet": 3})
        text = sr.fit_text(rows, s, DAY, DAY, "UTC")
        self.assertIn("opus 75% (6)", text)

    def test_project_filter(self):
        fx = Fixture(self)
        fx.unit("a1", 5, meta=meta("fix a"))
        fx.unit("a2", 5, meta=meta("fix b"), folder="c--code-files-spaarke-wt-elsewhere")
        rows, s = fx.report(projects=["demo"])
        self.assertEqual(s["transcripts"], 1)

    def test_empty_window(self):
        fx = Fixture(self)
        rows, s = fx.report()
        self.assertEqual((rows, s["transcripts"]), ([], 0))
        self.assertIn("no sub-agent transcripts", sr.fit_text(rows, s, DAY, DAY, "UTC"))


class Cli(unittest.TestCase):
    def setUp(self):
        self.home = tempfile.TemporaryDirectory()
        self.addCleanup(self.home.cleanup)
        self.root = os.path.join(self.home.name, ".claude", "projects")
        self.now = datetime.now(UTC) - timedelta(minutes=2)
        iso = self.now.strftime("%Y-%m-%dT%H:%M:%SZ")
        d = os.path.join(self.root, FOLDER, SESSION, "subagents")
        os.makedirs(d)
        with open(os.path.join(d, "agent-a1.jsonl"), "w", encoding="utf-8") as fh:
            fh.write(call_line(iso, "c1", model=OPUS) + "\n" + call_line(iso, "c2", model=OPUS) + "\n")
        with open(os.path.join(d, "agent-a1.meta.json"), "w", encoding="utf-8") as fh:
            json.dump(meta("map task 042 callers", kind="code-mapper", tool_use_id="toolu_X"), fh)
        with open(os.path.join(self.root, FOLDER, SESSION + ".jsonl"), "w", encoding="utf-8") as fh:
            fh.write(call_line(iso, "mm1", model=OPUS) + "\n")
        self.repo = os.path.join(self.home.name, "repo")
        os.makedirs(os.path.join(self.repo, "projects", PROJECT, "notes"))
        with open(os.path.join(self.repo, "projects", PROJECT, "notes", "task-042-x.md"), "w", encoding="utf-8") as fh:
            fh.write("rework: none\n")
        self.log = os.path.join(self.home.name, "launches.csv")
        with open(self.log, "w", encoding="utf-8", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=LOG_COLUMNS)
            w.writeheader()
            w.writerow({"ts_utc": iso, "session_id": SESSION, "transcript_path": "", "project": PROJECT, "tool": "Agent",
                        "subagent_type": "code-mapper", "model": "opus", "effort": "", "tool_use_id": "toolu_X",
                        "description": "map task 042 callers"})

    def run_fit(self, *args, log=True):
        extra = ["--launch-log", self.log] if log else ["--launch-log", os.path.join(self.home.name, "none.csv")]
        return run_cli("spend-report.py", ["--model-fit", "--tz", "utc", "--days", "2", "--repo", self.repo] + extra + list(args),
                       self.home.name)

    def test_text(self):
        p = self.run_fit()
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("Model fit", p.stdout)
        self.assertIn("code-mapper / map", p.stdout)
        self.assertIn("OVER: opus on map work", p.stdout)
        self.assertIn("Main-session calls by model (policy adherence): opus 100% (1)", p.stdout)
        self.assertIn("matched to a transcript 1", p.stdout)
        self.assertIn("0/1 0%", p.stdout)                       # rework known for the launch: none

    def test_unmatched_is_shown_without_a_launch_log(self):
        p = self.run_fit(log=False)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("unmatched transcripts (no launch record) 1", p.stdout)
        self.assertIn("code-mapper / map", p.stdout)
        self.assertIn("not found", p.stdout)

    def test_csv(self):
        p = self.run_fit("--format", "csv")
        self.assertEqual(p.returncode, 0, p.stderr)
        rows = list(csv.DictReader(io.StringIO(p.stdout)))
        work = [r for r in rows if r["section"] == "work"]
        self.assertEqual(len(work), 1)
        self.assertEqual((work[0]["work_type"], work[0]["model"], work[0]["launches"], work[0]["logged"]),
                         ("code-mapper / map", "opus", "1", "1"))
        self.assertTrue(work[0]["flag"].startswith("OVER"))
        self.assertEqual(work[0]["tz"], "utc")
        self.assertTrue(any(r["section"] == "match" and r["work_type"] == "matched" and r["launches"] == "1" for r in rows))
        self.assertTrue(any(r["section"] == "main_calls" and r["model"] == "opus" for r in rows))

    def test_json(self):
        p = self.run_fit("--format", "json")
        d = json.loads(p.stdout)
        self.assertEqual(d["summary"]["matched"], 1)
        self.assertEqual(d["rows"][0]["work_type"], "code-mapper / map")

    def test_html(self):
        out = os.path.join(self.home.name, "fit.html")
        p = self.run_fit("--format", "html", "--out", out)
        self.assertEqual(p.returncode, 0, p.stderr)
        with open(out, encoding="utf-8") as fh:
            h = fh.read()
        self.assertIn("<title>Spaarke Model Fit</title>", h)
        self.assertIn("code-mapper / map", h)
        self.assertIn("OVER: opus on map work", h)

    def test_not_combined_with_the_archive(self):
        p = self.run_fit("--archive")
        self.assertEqual(p.returncode, 2)
        self.assertIn("--model-fit", p.stderr)

    def test_plain_report_is_unchanged(self):
        p = run_cli("spend-report.py", ["--tz", "utc", "--days", "2"], self.home.name)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("AI spend (est., list price)", p.stdout)
        self.assertNotIn("Model fit", p.stdout)


if __name__ == "__main__":
    unittest.main()
