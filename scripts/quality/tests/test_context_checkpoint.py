"""Must-fire / must-not-fire controls for scripts/quality/context-checkpoint.py.

Each test builds a fake transcript, settings files and state folder in a temp directory, so nothing depends on the
machine running them (CI runs them on windows-latest).
Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "context-checkpoint.py")
MODEL = "claude-test-model"
BOUNDARY = {"type": "system", "subtype": "compact_boundary"}
SUMMARY = {"type": "user", "isCompactSummary": True, "message": {"content": "summary"}}


def assistant(tokens, sidechain=False, model=MODEL):
    return {"type": "assistant", "isSidechain": sidechain, "message": {"model": model, "usage": {
        "input_tokens": 2, "cache_creation_input_tokens": 1000, "cache_read_input_tokens": tokens - 1002,
        "output_tokens": 50}}}


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.project = os.path.join(self.tmp, "proj")
        os.makedirs(os.path.join(self.project, ".claude"))
        self.write_settings({"autoCompactWindow": 400000})
        self.state = os.path.join(self.tmp, "state")
        self.transcript = os.path.join(self.tmp, "t.jsonl")
        self.env = dict(os.environ, CLAUDE_PROJECT_DIR=self.project, SPRK_CONTEXT_STATE_DIR=self.state,
                        SPRK_USER_SETTINGS=os.path.join(self.tmp, "no-user-settings.json"))
        self.env.pop("CLAUDE_CODE_AUTO_COMPACT_WINDOW", None)

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def write_settings(self, settings, name="settings.json"):
        with open(os.path.join(self.project, ".claude", name), "w", encoding="utf-8") as f:
            json.dump(settings, f)

    def write_transcript(self, *entries, filler=0):
        with open(self.transcript, "w", encoding="utf-8") as f:
            f.write(json.dumps({"type": "user", "message": {"content": "x" * filler}}) + "\n")
            for e in entries:
                f.write(json.dumps(e) + "\n")

    def run_hook(self, mode, payload=None, raw=None, cwd=None):
        data = {"session_id": "s1", "transcript_path": self.transcript, "cwd": cwd or self.project,
                "hook_event_name": "PostToolUse"}
        data.update(payload or {})
        out = subprocess.run([sys.executable, HOOK, mode], input=raw if raw is not None else json.dumps(data),
                             capture_output=True, text=True, encoding="utf-8", env=self.env, timeout=30,
                             cwd=cwd or self.project)
        self.assertEqual(out.returncode, 0, out.stderr)
        return out.stdout.strip()

    def warning(self, stdout, event="PostToolUse"):
        self.assertTrue(stdout, "expected a checkpoint prompt")
        out = json.loads(stdout)["hookSpecificOutput"]
        self.assertEqual(out["hookEventName"], event)
        self.assertIn("/context-handoff", out["additionalContext"])
        return out["additionalContext"]


class WarnMustFire(Base):
    def test_crossing_85_percent(self):
        self.write_transcript(assistant(345000))
        msg = self.warning(self.run_hook("warn"))
        self.assertIn("86%", msg)
        self.assertIn("400K", msg)

    def test_user_prompt_submit_event_name(self):
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn", {"hook_event_name": "UserPromptSubmit"}), event="UserPromptSubmit")

    def test_final_warning_at_95_after_85(self):
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))
        self.write_transcript(assistant(385000))
        self.assertIn("FINAL WARNING", self.warning(self.run_hook("warn")))

    def test_jump_straight_past_95_fires_final_once(self):
        self.write_transcript(assistant(390000))
        self.assertIn("FINAL WARNING", self.warning(self.run_hook("warn")))
        self.assertEqual(self.run_hook("warn"), "")

    def test_rearms_after_compaction(self):
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))
        self.write_transcript(assistant(120000))
        self.assertEqual(self.run_hook("warn"), "")
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))

    def test_env_window_overrides_settings(self):
        self.env["CLAUDE_CODE_AUTO_COMPACT_WINDOW"] = "200000"
        self.write_transcript(assistant(175000))
        self.assertIn("200K", self.warning(self.run_hook("warn")))

    def test_model_settings_window_wins_over_top_level(self):
        self.write_settings({"autoCompactWindow": 1000000, "modelSettings": {MODEL: {"autoCompactWindow": 300000}}})
        self.write_transcript(assistant(260000))
        self.assertIn("300K", self.warning(self.run_hook("warn")))

    def test_local_settings_win_over_project(self):
        self.write_settings({"autoCompactWindow": 300000}, name="settings.local.json")
        self.write_transcript(assistant(260000))
        self.assertIn("300K", self.warning(self.run_hook("warn")))

    def test_user_settings_used_when_project_has_none(self):
        self.write_settings({})
        user = os.path.join(self.tmp, "user.json")
        with open(user, "w", encoding="utf-8") as f:
            json.dump({"autoCompactWindow": 400000}, f)
        self.env["SPRK_USER_SETTINGS"] = user
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))

    def test_new_call_after_compaction_counts(self):
        self.write_transcript(assistant(385000), BOUNDARY, SUMMARY, assistant(345000))
        self.assertNotIn("FINAL", self.warning(self.run_hook("warn")))

    def test_synthetic_error_entry_does_not_hide_real_usage(self):
        synthetic = {"type": "assistant", "isApiErrorMessage": True, "message": {"model": "<synthetic>", "usage": {
            "input_tokens": 0, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0, "output_tokens": 0}}}
        self.write_transcript(assistant(345000), synthetic)
        self.warning(self.run_hook("warn", {"hook_event_name": "UserPromptSubmit"}), event="UserPromptSubmit")

    def test_usage_found_beyond_first_tail_window(self):
        # The latest main-session call is followed by > 512 KB of tool output.
        with open(self.transcript, "w", encoding="utf-8") as f:
            f.write(json.dumps(assistant(345000)) + "\n")
            f.write(json.dumps({"type": "user", "message": {"content": "y" * 700000}}) + "\n")
        self.warning(self.run_hook("warn"))


class WarnMustNotFire(Base):
    def test_below_threshold(self):
        self.write_transcript(assistant(330000))
        self.assertEqual(self.run_hook("warn"), "")

    def test_stale_usage_before_compaction_boundary(self):
        # First prompt after a /compact: the newest call predates the boundary, so its 96% is no longer true.
        self.write_transcript(assistant(385000), BOUNDARY)
        self.assertEqual(self.run_hook("warn", {"hook_event_name": "UserPromptSubmit"}), "")
        self.write_transcript(assistant(385000), SUMMARY)
        self.assertEqual(self.run_hook("warn", {"hook_event_name": "UserPromptSubmit"}), "")

    def test_fires_only_once_per_level(self):
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))
        self.write_transcript(assistant(352000))
        self.assertEqual(self.run_hook("warn"), "")

    def test_sub_agent_tool_call(self):
        self.write_transcript(assistant(345000))
        self.assertEqual(self.run_hook("warn", {"agent_id": "a1", "agent_type": "implementer"}), "")

    def test_sidechain_entries_ignored(self):
        self.write_transcript(assistant(100000), assistant(399000, sidechain=True))
        self.assertEqual(self.run_hook("warn"), "")

    def test_no_window_configured(self):
        self.write_settings({})
        self.write_transcript(assistant(900000))
        self.assertEqual(self.run_hook("warn"), "")

    def test_missing_transcript(self):
        self.assertEqual(self.run_hook("warn", {"transcript_path": os.path.join(self.tmp, "nope.jsonl")}), "")

    def test_transcript_without_usage(self):
        self.write_transcript({"type": "assistant", "message": {"content": "hi"}})
        self.assertEqual(self.run_hook("warn"), "")

    def test_bad_stdin(self):
        self.assertEqual(self.run_hook("warn", raw="not json"), "")
        self.assertEqual(self.run_hook("warn", raw="[1,2]"), "")
        self.assertEqual(self.run_hook("warn", raw=""), "")

    def test_unknown_mode(self):
        self.write_transcript(assistant(399000))
        self.assertEqual(self.run_hook("bogus"), "")

    def test_sessions_do_not_share_state(self):
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))
        self.warning(self.run_hook("warn", {"session_id": "s2"}))


class Snapshot(Base):
    def git_repo(self):
        repo = os.path.join(self.tmp, "repo")
        os.makedirs(repo)
        for args in (["init", "-q", "-b", "work/demo"], ["config", "user.email", "t@example.invalid"],
                     ["config", "user.name", "t"]):
            subprocess.run(["git", *args], cwd=repo, check=True)
        os.makedirs(os.path.join(repo, "projects", "demo"))
        with open(os.path.join(repo, "projects", "demo", "current-task.md"), "w") as f:
            f.write("state")
        subprocess.run(["git", "add", "-A"], cwd=repo, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "first commit"], cwd=repo, check=True)
        with open(os.path.join(repo, "dirty.txt"), "w") as f:
            f.write("x")
        return repo

    def test_snapshot_round_trip(self):
        repo = self.git_repo()
        self.write_transcript(assistant(345000))
        self.warning(self.run_hook("warn"))
        self.assertEqual(self.run_hook("precompact", {"hook_event_name": "PreCompact", "trigger": "auto"},
                                       cwd=repo), "")
        out = self.run_hook("reinject", {"hook_event_name": "SessionStart", "source": "compact"}, cwd=repo)
        self.assertIn("Pre-compaction snapshot", out)
        self.assertIn("Branch: work/demo", out)
        self.assertIn("?? dirty.txt", out)
        self.assertIn("first commit", out)
        self.assertIn("current-task.md last written", out)
        self.assertIn("Checkpoint prompt fired at 86%", out)
        # Consumed once.
        self.assertEqual(self.run_hook("reinject", {"source": "compact"}, cwd=repo), "")

    def test_non_ascii_snapshot_survives(self):
        repo = self.git_repo()
        subprocess.run(["git", "commit", "-q", "--allow-empty", "-m", "fix → résumé ✓ 🚀"], cwd=repo, check=True)
        self.run_hook("precompact", {"trigger": "auto"}, cwd=repo)
        out = self.run_hook("reinject", cwd=repo)
        self.assertIn("fix → résumé ✓ 🚀", out)
        self.assertIn("Branch: work/demo", out)

    def test_snapshot_outside_git(self):
        self.run_hook("precompact", {"trigger": "manual"})
        out = self.run_hook("reinject")
        self.assertIn("Checkpoint prompt did not fire", out)

    def test_new_session_id_same_cwd_recent(self):
        self.run_hook("precompact", {"trigger": "auto"})
        self.assertIn("Pre-compaction snapshot", self.run_hook("reinject", {"session_id": "s-new"}))

    def test_new_session_id_other_cwd_ignored(self):
        self.run_hook("precompact", {"trigger": "auto"})
        other = os.path.join(self.tmp, "other")
        os.makedirs(other)
        self.assertEqual(self.run_hook("reinject", {"session_id": "s-new"}, cwd=other), "")

    def test_stale_snapshot_ignored(self):
        self.run_hook("precompact", {"trigger": "auto"})
        path = os.path.join(self.state, "s1.snapshot.json")
        old = time.time() - 7 * 3600
        os.utime(path, (old, old))
        self.assertEqual(self.run_hook("reinject"), "")

    def test_reinject_without_snapshot(self):
        self.assertEqual(self.run_hook("reinject"), "")


class Budget(Base):
    def test_warn_is_fast_on_a_large_transcript(self):
        # 40 MB of tool output before the latest call: the hook reads only the tail.
        with open(self.transcript, "w", encoding="utf-8") as f:
            line = json.dumps({"type": "user", "message": {"content": "z" * 100000}}) + "\n"
            for _ in range(400):
                f.write(line)
            f.write(json.dumps(assistant(200000)) + "\n")
        start = time.time()
        self.assertEqual(self.run_hook("warn"), "")
        self.assertLess(time.time() - start, 2.0)


if __name__ == "__main__":
    unittest.main()
