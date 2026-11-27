"""Must-fire / must-not-fire controls for scripts/quality/runaway-processes.py.

Snapshots are synthetic (the --snapshot seam), so these run anywhere, including Linux CI.
Run: python -m unittest discover -s scripts/quality/tests
"""
import argparse
import datetime as dt
import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import redirect_stdout
from unittest import mock

SCRIPT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "runaway-processes.py")


def load():
    spec = importlib.util.spec_from_file_location("runaway_processes", SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


rp = load()

SID = "3b2fb0d4-90fd-4d45-a4c8-6d2a27fc0bbc"
OTHER_SID = "11111111-2222-3333-4444-555555555555"
CLAUDE_EXE = r"C:\Users\u\AppData\Roaming\Claude\claude-code\2.1.330\x\claude.exe"
APP_EXE = r'"C:\Program Files\WindowsApps\Claude_6.1_x64__abc\app\Claude.exe"'
BASH_TOOL = ('"C:\\Program Files\\Git\\bin\\bash.exe" -c "source /c/Users/u/.claude/shell-snapshots/snapshot-bash-1.sh '
             '2>/dev/null || true && eval \'%s\' < /dev/null"')
WORKTREE = "C:\\code_files\\spaarke-wt-demo"
SAMPLE = 15
HOUR = 3600


def iso(epoch):
    return dt.datetime.fromtimestamp(epoch, dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


class Snap:
    """Builds a two-sample snapshot. rate = cores used during the sample; avg = cores over the process's life."""

    def __init__(self):
        self.now = time.time()
        self.t0 = self.now - SAMPLE
        self.rows = []
        self.add(1, 0, "explorer.exe", "C:\\WINDOWS\\Explorer.EXE", age=100 * HOUR)
        self.add(2, 1, "claude.exe", APP_EXE, age=99 * HOUR)
        self.add(3, 2, "claude.exe", APP_EXE + " --type=utility", age=99 * HOUR)

    def add(self, pid, ppid, name, cmd, age, rate=0.0, avg=None):
        created = self.now - age
        total = (avg if avg is not None else rate) * age
        self.rows.append(dict(pid=pid, ppid=ppid, name=name, cmd=cmd, created=iso(created),
                              cpu2=int(total * 1e7), cpu1=int(max(total - rate * SAMPLE, 0) * 1e7)))
        return self

    def session(self, pid, sid=SID, age=50 * HOUR):
        cmd = CLAUDE_EXE + " --output-format stream-json" + (" --resume=%s" % sid if sid else "")
        return self.add(pid, 3, "claude.exe", cmd, age=age)

    def build(self):
        def one(r, cpu):
            return dict(pid=r["pid"], ppid=r["ppid"], name=r["name"], cmd=r["cmd"], created=r["created"], cpu=cpu)
        return {"t0": iso(self.t0), "t1": iso(self.now),
                "first": [one(r, r["cpu1"]) for r in self.rows], "second": [one(r, r["cpu2"]) for r in self.rows]}


def touch(path, age_s):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if not os.path.exists(path):
        open(path, "w").close()
    t = time.time() - age_s
    os.utime(path, (t, t))


class Base(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.projects = os.path.join(self.dir.name, "projects")
        self.tasks = os.path.join(self.dir.name, "tasks")
        os.makedirs(os.path.join(self.projects, "C--code-files-spaarke-wt-demo"))
        self.cache = os.path.join(self.dir.name, "report.json")

    def tearDown(self):
        self.dir.cleanup()

    def transcript(self, sid=SID, idle_s=0, cwd=WORKTREE):
        path = os.path.join(self.projects, "C--code-files-spaarke-wt-demo", sid + ".jsonl")
        with open(path, "w", encoding="utf-8") as f:
            f.write(json.dumps({"type": "user", "cwd": cwd}) + "\n")
            f.write(json.dumps({"type": "assistant", "cwd": cwd + "\\src"}) + "\n")
        touch(path, idle_s)

    def classify(self, snap, hot=None):
        return rp.classify(snap.build(), self.projects, hot or {}, tasks_dir=self.tasks)

    def verdicts(self, report):
        return {e["root_pid"]: e["verdict"] for e in report["entries"]}


class MustFire(Base):
    def test_orphan_python_stdin_spinning_is_safe_to_stop_listed_once_at_top(self):
        s = Snap()
        s.add(100, 999, "bash.exe", BASH_TOOL % "python - <<'EOF'", age=40 * HOUR)
        s.add(101, 100, "python.exe", "C:\\Python313\\python.exe -", age=40 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "safe-to-stop"})
        member = report["entries"][0]["processes"][0]
        self.assertEqual(member["pid"], 101)
        self.assertIn("leak pattern: python-stdin", member["reasons"])
        self.assertEqual(report["entries"][0]["tree_pids"], [101, 100])

    def test_orphan_spinning_without_pattern_is_likely_leaked(self):
        s = Snap().add(100, 999, "node.exe", "node build.js %s\\x" % WORKTREE, age=2 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "likely-leaked"})
        self.assertEqual(report["entries"][0]["owner"]["worktree_hint"], WORKTREE)

    def test_orphan_of_ended_idle_session_is_stoppable(self):
        self.transcript(idle_s=5 * HOUR)                                  # no claude.exe for SID: session ended
        s = Snap().add(100, 999, "python.exe",
                       "python - C:\\Users\\u\\AppData\\Local\\Temp\\claude\\f\\%s\\scratchpad\\in.txt" % SID,
                       age=5 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "safe-to-stop"})
        self.assertEqual(report["entries"][0]["owner"]["session_id"], SID)

    def test_orphan_idle_emulator_tree_is_one_idle_leftover(self):
        s = Snap()
        s.add(200, 999, "bash.exe", "bash.exe /c/nvm4w/nodejs/npx --yes -p azurite azurite-table", age=68 * HOUR)
        s.add(201, 200, "node.exe", "node npx-cli.js azurite-table", age=68 * HOUR)
        s.add(202, 201, "cmd.exe", "cmd.exe /d /s /c azurite-table", age=68 * HOUR)
        s.add(203, 202, "node.exe", "node azurite/dist/src/table/main.js", age=68 * HOUR)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {200: "idle-leftover"})
        self.assertEqual(sorted(m["pid"] for m in report["entries"][0]["processes"]), [200, 201, 202, 203])

    def test_recently_hot_process_flagged_once_seen_hot_15_min_earlier(self):
        s = Snap().add(100, 999, "node.exe", "node x.js", age=10 * HOUR, rate=1.0, avg=0.05)
        first = self.classify(s)
        self.assertEqual(self.verdicts(first), {})                      # first sighting: could be a burst
        key = list(first["hot"])[0]
        self.assertEqual(self.verdicts(self.classify(s, hot={key: s.now - 20 * 60})), {100: "likely-leaked"})

    def test_cmd_orphan_with_claude_marker_counts(self):
        s = Snap().add(100, 999, "cmd.exe",
                       "cmd /c C:\\Users\\u\\AppData\\Local\\Temp\\claude\\abc\\%s\\scratchpad\\loop.cmd" % OTHER_SID,
                       age=2 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "likely-leaked"})
        self.assertEqual(report["entries"][0]["owner"]["session_id"], OTHER_SID)


class LiveSessionsAreNeverStoppable(Base):
    """A live claude.exe may be waiting on its own work, so its processes are at most `check`."""

    def test_child_of_idle_live_session_is_check_even_with_leak_pattern(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10)
        s.add(100, 10, "bash.exe", BASH_TOOL % "until grep -q done out.txt; do sleep 5; done", age=3 * HOUR)
        s.add(102, 100, "node.exe", "node spin.js", age=3 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "check"})
        owner = report["entries"][0]["owner"]
        self.assertEqual(owner["session_id"], SID)
        self.assertEqual(owner["cwd"], WORKTREE)                         # where it started, not the later cd

    def test_session_waiting_on_background_task_is_active(self):
        self.transcript(idle_s=2 * HOUR)
        touch(os.path.join(self.tasks, "f", SID, "tasks", "b1.output"), 60)
        s = Snap().session(10)
        s.add(100, 10, "bash.exe", BASH_TOOL % "dotnet test", age=3 * HOUR)
        s.add(101, 100, "testhost.exe", "testhost.exe", age=3 * HOUR, rate=2.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy"})

    def test_session_waiting_on_subagent_is_active(self):
        self.transcript(idle_s=2 * HOUR)
        touch(os.path.join(self.projects, "C--code-files-spaarke-wt-demo", SID, "subagents", "agent-1.jsonl"), 60)
        s = Snap().session(10)
        s.add(100, 10, "bash.exe", BASH_TOOL % "npm test", age=3 * HOUR)
        s.add(101, 100, "node.exe", "node jest.js", age=3 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy"})

    def test_session_without_resume_is_named_from_its_childrens_paths(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10, sid=None)
        s.add(100, 10, "bash.exe", BASH_TOOL % ("cat > C:/Users/u/AppData/Local/Temp/claude/f/%s/scratchpad/a.txt"
                                                % SID), age=1 * HOUR)
        s.add(101, 100, "node.exe", "node spin.js", age=1 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "check"})
        self.assertEqual(report["entries"][0]["owner"]["session_id"], SID)

    def test_live_session_without_any_id_is_check(self):
        s = Snap().session(10, sid=None)
        s.add(100, 10, "bash.exe", BASH_TOOL % "node spin.js", age=1 * HOUR)
        s.add(101, 100, "node.exe", "node spin.js", age=1 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "check"})

    def test_orphan_in_live_sessions_worktree_belongs_to_that_session(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node %s\\server.js" % WORKTREE, age=8 * HOUR, rate=1.0)
        s.add(110, 999, "node.exe", "node %s\\dev.js --port 5173" % WORKTREE, age=8 * HOUR)   # idle server
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "check"})          # not likely-leaked, and 110 not listed
        self.transcript(idle_s=60)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy"})

    def test_one_member_linked_to_a_live_session_makes_the_tree_check(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node runner.js", age=8 * HOUR, rate=1.0)              # no evidence: leaked
        s.add(101, 100, "python.exe", "python %s\\job.py" % WORKTREE, age=8 * HOUR, rate=1.0)  # the session's
        self.assertEqual(self.verdicts(self.classify(s)), {100: "check"})

    def test_quiet_member_of_an_active_session_makes_the_tree_busy(self):
        self.transcript(idle_s=60)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node runner.js", age=8 * HOUR, rate=1.0)              # spinning, no evidence
        s.add(101, 100, "python.exe", "python %s\\job.py" % WORKTREE, age=0.1 * HOUR)      # quiet, the session's
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "busy"})
        self.assertIn("python.exe 101: its session is active", report["entries"][0]["protected"])

    def test_quiet_member_of_an_idle_live_session_makes_the_tree_check(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node runner.js", age=8 * HOUR, rate=1.0)
        s.add(101, 100, "python.exe", "python %s\\job.py" % WORKTREE, age=8 * HOUR)        # quiet
        self.assertEqual(self.verdicts(self.classify(s)), {100: "check"})

    def test_quiet_unlinked_member_leaves_the_tree_stoppable(self):
        self.transcript(idle_s=60)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node runner.js", age=8 * HOUR, rate=1.0)
        s.add(101, 100, "python.exe", "python C:\\elsewhere\\job.py", age=8 * HOUR)        # quiet, no evidence
        self.assertEqual(self.verdicts(self.classify(s)), {100: "likely-leaked"})

    def test_worktree_match_respects_path_boundaries(self):
        self.transcript(idle_s=2 * HOUR, cwd="C:\\code_files\\spaarke")    # main checkout, not the -wt- folder
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node %s\\server.js" % WORKTREE, age=8 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "likely-leaked"})

    def test_detached_job_of_active_session_is_busy(self):
        self.transcript(idle_s=60)
        s = Snap()
        s.add(100, 999, "bash.exe",
              "bash.exe /c/Users/u/AppData/Local/Temp/claude/55e/%s/scratchpad/shards.sh" % SID, age=1 * HOUR)
        s.add(101, 100, "testhost.exe", "testhost.exe --runtimeconfig x", age=0.3 * HOUR, rate=1.7, avg=2.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy"})

    def test_child_of_active_session_is_busy(self):
        self.transcript(idle_s=60)
        s = Snap().session(10)
        s.add(100, 10, "bash.exe", BASH_TOOL % "python - <<'EOF'", age=1 * HOUR)
        s.add(101, 100, "python.exe", "python -", age=1 * HOUR, rate=1.0)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "busy"})
        self.assertIn("its session is active", report["entries"][0]["processes"][0]["reasons"])


class MustNotFire(Base):
    def test_build_server_git_and_deploy_are_protected(self):
        s = Snap()
        s.add(100, 999, "VBCSCompiler.exe", "VBCSCompiler.exe -pipename:x", age=6 * HOUR, rate=8.0, avg=3.0)
        s.add(110, 999, "dotnet.exe", "dotnet MSBuild.dll /nologo /nodemode:1 /nodeReuse:true", age=2 * HOUR,
              rate=1.0)
        s.add(120, 999, "git.exe", "git.exe gc --aggressive", age=1 * HOUR, rate=1.0)
        s.add(130, 999, "bash.exe", BASH_TOOL % "az webapp deploy --name x", age=1 * HOUR)
        s.add(131, 130, "python.exe", "python az.py", age=1 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy", 110: "busy", 120: "busy", 130: "busy"})

    def test_idle_protected_descendant_makes_the_tree_busy(self):
        s = Snap()
        s.add(100, 999, "bash.exe", BASH_TOOL % "dotnet test", age=10 * HOUR)
        s.add(101, 100, "dotnet.exe", "dotnet test", age=10 * HOUR, rate=1.0)
        s.add(102, 101, "VBCSCompiler.exe", "VBCSCompiler.exe -pipename:y", age=10 * HOUR)   # idle, shared
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "busy"})
        self.assertTrue(any("VBCSCompiler" in p or "vbcscompiler" in p for p in report["entries"][0]["protected"]))

    def test_tree_with_a_claude_session_inside_is_busy(self):
        s = Snap()
        s.add(100, 999, "pwsh.exe", "pwsh -NoExit -WorkingDirectory %s" % WORKTREE, age=10 * HOUR)
        s.add(101, 100, "claude.exe", CLAUDE_EXE, age=9 * HOUR)
        report = self.classify(s)
        self.assertEqual(self.verdicts(report), {100: "busy"})

    def test_idle_build_server_is_not_a_leftover(self):
        s = Snap().add(100, 999, "VBCSCompiler.exe", "VBCSCompiler.exe -pipename:x", age=30 * HOUR)
        self.assertEqual(self.verdicts(self.classify(s)), {})

    def test_mcp_server_of_idle_session_is_protected(self):
        self.transcript(idle_s=5 * HOUR)
        s = Snap().session(10)
        s.add(100, 10, "node.exe", "node C:\\mcp\\server.js --stdio", age=40 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {100: "busy"})

    def test_vendor_cmd_orphan_without_claude_evidence_is_ignored(self):
        s = Snap()
        s.add(100, 999, "cmd.exe", '"C:\\WINDOWS\\system32\\cmd.exe" /C "C:\\Program Files\\AMD\\AMDRSServ.exe"',
              age=70 * HOUR)
        s.add(101, 999, "cmd.exe", "", age=50 * HOUR, rate=1.0)              # unreadable command line
        self.assertEqual(self.verdicts(self.classify(s)), {})

    def test_user_terminal_and_desktop_app_processes_are_ignored(self):
        s = Snap()
        s.add(100, 1, "WindowsTerminal.exe", "wt.exe", age=10 * HOUR)
        s.add(101, 100, "pwsh.exe", "pwsh", age=10 * HOUR)
        s.add(102, 101, "node.exe", "node server.js", age=10 * HOUR, rate=1.0)
        s.add(103, 3, "node.exe", "node app-helper.js", age=10 * HOUR, rate=1.0)
        self.assertEqual(self.verdicts(self.classify(s)), {})

    def test_young_or_quiet_processes_are_not_spinning(self):
        s = Snap()
        s.add(100, 999, "node.exe", "node young.js", age=5 * 60, rate=1.0)
        s.add(110, 999, "node.exe", "node burst-earlier.js", age=2 * HOUR, rate=0.0, avg=0.9)
        self.assertEqual(self.verdicts(self.classify(s)), {})

    def test_reused_parent_pid_does_not_make_an_orphan_a_session_child(self):
        self.transcript(idle_s=60)                                        # an ACTIVE session now owns pid 10
        s = Snap()
        s.add(10, 3, "claude.exe", CLAUDE_EXE + " --resume=" + SID, age=1 * HOUR)
        s.add(100, 10, "python.exe", "python -", age=20 * HOUR, rate=1.0)  # older than its "parent"
        self.assertEqual(self.verdicts(self.classify(s)), {100: "safe-to-stop"})

    def test_scanner_excludes_its_own_tree(self):
        s = Snap()
        s.add(100, 999, "python.exe", "python runaway-processes.py", age=1 * HOUR, rate=1.0)
        s.add(101, 100, "pwsh.exe", "pwsh -EncodedCommand x", age=1 * HOUR, rate=1.0)
        report = rp.classify(s.build(), self.projects, {}, exclude_root=100)
        self.assertEqual(self.verdicts(report), {})


class Banner(Base):
    def run_banner(self):
        out = subprocess.run([sys.executable, SCRIPT, "--cache", self.cache, "banner", "--no-refresh"],
                             capture_output=True, text=True, encoding="utf-8", timeout=30)
        return out.returncode, out.stdout.strip()

    def scan_to_cache(self, snap):
        path = os.path.join(self.dir.name, "snap.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(snap.build(), f)
        out = subprocess.run([sys.executable, SCRIPT, "--cache", self.cache, "--projects-dir", self.projects,
                              "--tasks-dir", self.tasks, "--snapshot", path, "scan"],
                             capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(out.returncode, 0, out.stderr)
        return out.stdout

    def leak(self):
        s = Snap()
        s.add(100, 999, "bash.exe", BASH_TOOL % "python - <<'EOF'", age=40 * HOUR)
        s.add(101, 100, "python.exe", "python -", age=40 * HOUR, rate=1.0)
        return s

    def test_flagged_processes_reach_user_and_agent(self):
        text = self.scan_to_cache(self.leak())
        self.assertIn("SAFE TO STOP  pid 100", text)
        self.assertIn("kill 100", text)
        code, out = self.run_banner()
        self.assertEqual(code, 0)
        payload = json.loads(out)
        self.assertIn("1 safe to stop", payload["systemMessage"])
        self.assertIn("pid 100 bash.exe", payload["systemMessage"])
        self.assertNotIn("stale", payload["systemMessage"])
        self.assertIn("only after the user says yes", payload["hookSpecificOutput"]["additionalContext"])

    def test_stale_report_is_dated_and_marked(self):
        self.scan_to_cache(self.leak())
        report = rp.read_cache(self.cache)
        report["scanned_at"] = iso(time.time() - 50 * HOUR)
        rp.write_cache(self.cache, report)
        payload = json.loads(self.run_banner()[1])
        self.assertIn("50 h old", payload["systemMessage"])
        self.assertIn("stale", payload["systemMessage"])

    def test_failed_scan_is_shown(self):
        rp.write_cache(self.cache, {"error": "process snapshot failed: timeout", "failed_at": iso(time.time())})
        payload = json.loads(self.run_banner()[1])
        self.assertIn("scan failed", payload["systemMessage"])
        self.assertIn("timeout", payload["systemMessage"])

    def test_failed_scan_records_error_and_keeps_report(self):
        self.scan_to_cache(self.leak())
        bad = os.path.join(self.dir.name, "bad.json")
        with open(bad, "w") as f:
            f.write("not json")
        out = subprocess.run([sys.executable, SCRIPT, "--cache", self.cache, "--snapshot", bad, "scan"],
                             capture_output=True, text=True, encoding="utf-8", timeout=30)
        self.assertEqual(out.returncode, 1)
        report = rp.read_cache(self.cache)
        self.assertIn("error", report)
        self.assertEqual(len(report["entries"]), 1)

    def test_only_busy_prints_nothing(self):
        self.scan_to_cache(Snap().add(100, 999, "VBCSCompiler.exe", "VBCSCompiler.exe", age=6 * HOUR, rate=8.0,
                                      avg=3.0))
        self.assertEqual(self.run_banner(), (0, ""))

    def test_missing_or_corrupt_cache_never_fails_the_session(self):
        self.assertEqual(self.run_banner(), (0, ""))
        with open(self.cache, "w") as f:
            f.write("not json")
        self.assertEqual(self.run_banner(), (0, ""))

    def test_only_one_background_scan_at_a_time(self):
        with mock.patch.object(rp.subprocess, "Popen") as popen:
            self.assertTrue(rp.start_background_scan(self.cache))
            self.assertFalse(rp.start_background_scan(self.cache))     # lock held
            self.assertEqual(popen.call_count, 1)
            lock = self.cache + ".lock"
            touch(lock, rp.LOCK_STALE + 60)                              # a crashed scan's lock expires
            self.assertTrue(rp.start_background_scan(self.cache))
            self.assertEqual(popen.call_count, 2)


class Kill(Base):
    """cmd_kill rescans (take_snapshot replaced) and names every PID it stops (taskkill replaced)."""

    def leak_and_server(self):
        s = Snap()
        s.add(100, 999, "bash.exe", BASH_TOOL % "python - <<'EOF'", age=40 * HOUR)
        s.add(101, 100, "python.exe", "python -", age=40 * HOUR, rate=1.0)
        s.add(200, 999, "VBCSCompiler.exe", "VBCSCompiler.exe", age=6 * HOUR, rate=8.0, avg=3.0)
        return s

    def kill(self, pids, snap, force=False):
        args = argparse.Namespace(cache=self.cache, projects_dir=self.projects, tasks_dir=self.tasks, snapshot=None,
                                  pids=pids, force=force)
        calls = []

        def fake_run(cmd, **kw):
            calls.append(cmd)
            return subprocess.CompletedProcess(cmd, 0, "SUCCESS", "")

        buf = io.StringIO()
        with mock.patch.object(rp, "take_snapshot", return_value=snap.build()), \
                mock.patch.object(rp.subprocess, "run", side_effect=fake_run), redirect_stdout(buf):
            rc = rp.cmd_kill(args)
        return rc, calls, buf.getvalue()

    def test_stoppable_tree_is_stopped_by_explicit_pids_children_first(self):
        rc, calls, _ = self.kill([100], self.leak_and_server())
        self.assertEqual(rc, 0)
        self.assertEqual(calls, [["taskkill", "/PID", "101", "/PID", "100", "/F"]])   # no /T

    def test_protected_and_unlisted_pids_are_refused(self):
        rc, calls, out = self.kill([200, 101], self.leak_and_server())
        self.assertEqual((rc, calls), (1, []))
        self.assertIn("busy (protected)", out)
        self.assertIn("does not list it", out)

    def test_verdict_is_rechecked_now_not_read_from_the_cache(self):
        s = self.leak_and_server()
        rp.write_cache(self.cache, rp.classify(s.build(), self.projects, {}, tasks_dir=self.tasks))
        self.transcript(idle_s=60)                    # since the scan, a live session took the worktree...
        s.session(10)
        s.rows = [r for r in s.rows if r["pid"] not in (100, 101)]
        s.add(100, 10, "bash.exe", BASH_TOOL % "python - <<'EOF'", age=40 * HOUR)   # ...and owns pid 100
        s.add(101, 100, "python.exe", "python -", age=40 * HOUR, rate=1.0)
        rc, calls, out = self.kill([100], s)
        self.assertEqual((rc, calls), (1, []))
        self.assertIn("busy", out)

    def test_reused_root_pid_is_refused(self):
        s = Snap()
        s.add(100, 999, "bash.exe", "bash.exe new shell", age=60)          # pid 100 now a 1-minute-old process
        s.add(101, 100, "python.exe", "python -", age=40 * HOUR, rate=1.0)  # the old leak, now its own top
        rc, calls, out = self.kill([100], s)
        self.assertEqual((rc, calls), (1, []))
        self.assertIn("does not list it", out)

    def test_check_needs_force(self):
        self.transcript(idle_s=2 * HOUR)
        s = Snap().session(10)
        s.add(100, 10, "bash.exe", BASH_TOOL % "node spin.js", age=3 * HOUR)
        s.add(101, 100, "node.exe", "node spin.js", age=3 * HOUR, rate=1.0)
        rc, calls, out = self.kill([100], s)
        self.assertEqual((rc, calls), (1, []))
        self.assertIn("check with session", out)
        rc, calls, _ = self.kill([100], s, force=True)
        self.assertEqual((rc, calls), (0, [["taskkill", "/PID", "101", "/PID", "100", "/F"]]))

    def test_tree_with_a_quiet_member_of_an_active_session_is_refused(self):
        self.transcript(idle_s=60)
        s = Snap().session(10)
        s.add(100, 999, "node.exe", "node runner.js", age=8 * HOUR, rate=1.0)
        s.add(101, 100, "python.exe", "python %s\\job.py" % WORKTREE, age=0.1 * HOUR)
        rc, calls, out = self.kill([100], s)
        self.assertEqual((rc, calls), (1, []))
        self.assertIn("busy", out)

    def test_force_never_stops_an_unlisted_pid(self):
        s = self.leak_and_server().add(400, 1, "notepad.exe", "notepad.exe notes.txt", age=2 * HOUR)
        rc, calls, out = self.kill([400, 101], s, force=True)
        self.assertEqual((rc, calls), (1, []))
        self.assertEqual(out.count("does not list it"), 2)

    def test_claude_session_or_tree_holding_one_is_refused_even_with_force(self):
        s = self.leak_and_server().session(10)
        s.add(300, 999, "pwsh.exe", "pwsh %s" % WORKTREE, age=10 * HOUR)
        s.add(301, 300, "claude.exe", CLAUDE_EXE, age=9 * HOUR)
        rc, calls, out = self.kill([10, 300], s, force=True)
        self.assertEqual((rc, calls), (1, []))
        self.assertEqual(out.count("Claude session"), 2)


if __name__ == "__main__":
    unittest.main()
