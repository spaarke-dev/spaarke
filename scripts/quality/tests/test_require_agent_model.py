"""Must-fire / must-not-fire controls for scripts/quality/require-agent-model.py.

Run: python -m unittest discover -s scripts/quality/tests
"""
import csv
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "require-agent-model.py")
SPEND_REPORT = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                            "ai-cost", "spend-report.py")
# Every run writes its launch log under a temp folder, never to the real ~/.claude/spend-archive file.
_LOG_DIR = tempfile.TemporaryDirectory()
DEFAULT_LOG = os.path.join(_LOG_DIR.name, "default-launches.csv")


def run(event, project_dir, log=None):
    env = dict(os.environ, CLAUDE_PROJECT_DIR=project_dir, SPAARKE_AGENT_LAUNCH_LOG=log or DEFAULT_LOG)
    out = subprocess.run([sys.executable, HOOK], input=json.dumps(event) if not isinstance(event, str) else event,
                         capture_output=True, text=True, env=env, timeout=20)
    return out.returncode, out.stdout.strip()


def denied(stdout):
    if not stdout:
        return False
    return json.loads(stdout)["hookSpecificOutput"]["permissionDecision"] == "deny"


class RequireAgentModel(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        agents = os.path.join(cls.tmp.name, ".claude", "agents")
        os.makedirs(agents)
        with open(os.path.join(agents, "reviewer.md"), "w", encoding="utf-8") as f:
            f.write("---\nname: reviewer\ndescription: x\nmodel: opus\neffort: high\n---\nbody\n")
        with open(os.path.join(agents, "inheritor.md"), "w", encoding="utf-8") as f:
            f.write("---\nname: inheritor\ndescription: x\nmodel: inherit\n---\nbody\n")
        with open(os.path.join(agents, "nomodel.md"), "w", encoding="utf-8") as f:
            f.write("---\nname: nomodel\ndescription: x\n---\nbody\n")
        # Claude Code matches agents by frontmatter name and scans recursively; files may carry a BOM.
        with open(os.path.join(agents, "mapper-file.md"), "w", encoding="utf-8") as f:
            f.write("---\nname: mapper\ndescription: x\nmodel: sonnet\n---\nbody\n")
        os.makedirs(os.path.join(agents, "sub"))
        with open(os.path.join(agents, "sub", "nested.md"), "w", encoding="utf-8-sig") as f:
            f.write("---\r\nname: nested\r\ndescription: x\r\nmodel: haiku\r\n---\r\nbody\r\n")

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def check(self, event, expect_deny):
        code, out = run(event, self.tmp.name)
        self.assertEqual(code, 0)
        self.assertEqual(denied(out), expect_deny, out)
        if expect_deny:
            self.assertIn("agent-cost.md", out)
            self.assertIn("Do not ask the user", out)

    # must fire
    def test_general_purpose_without_model(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "description": "d"}}, True)

    def test_task_alias_without_model(self):
        self.check({"tool_name": "Task", "tool_input": {"prompt": "p", "subagent_type": "Explore"}}, True)

    def test_definition_without_model(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "nomodel"}}, True)

    def test_definition_inherit(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "inheritor"}}, True)

    def test_workflow_agent_without_model(self):
        script = "export const meta = {name:'x'}\nconst a = await agent(`do ${x}`, {label: 'a'})\nconst b = await agent('y', {model: 'sonnet'})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, True)

    # must not fire
    def test_explicit_model(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "sonnet"}}, False)

    def test_definition_with_model(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "reviewer"}}, False)

    def test_fork(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "fork"}}, False)

    def test_workflow_all_models(self):
        script = "const r = await parallel(xs.map(x => () => agent(`a ${x}`, {label: 'l', model: 'sonnet', schema: S})))\nawait agent('v', {model: 'opus'})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, False)

    def test_workflow_named_not_inspectable(self):
        self.check({"tool_name": "Workflow", "tool_input": {"name": "saved-one"}}, False)

    def test_other_tool(self):
        self.check({"tool_name": "Bash", "tool_input": {"command": "agent()"}}, False)

    def test_malformed_input_fails_open(self):
        self.check("not json", False)

    # review findings (2026-10-09)
    def test_definition_matched_by_frontmatter_name(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "mapper"}}, False)

    def test_nested_bom_crlf_definition(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "nested"}}, False)

    def test_unknown_definition_fires_with_accurate_reason(self):
        code, out = run({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "nosuch"}}, self.tmp.name)
        self.assertTrue(denied(out))
        self.assertIn("No agent definition named 'nosuch'", out)

    def test_fixed_model_built_ins_allowed(self):
        self.check({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "claude-code-guide"}}, False)

    def test_workflow_comment_is_not_a_call(self):
        script = "// agent(prompt, opts) wraps the call\n/* agent(x) */\nawait agent(\"x\", {model: \"sonnet\"})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, False)

    def test_workflow_agent_type_with_model_definition(self):
        script = "await agent('x', {agentType: 'reviewer', label: 'r'})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, False)

    def test_workflow_agent_type_without_model_definition(self):
        script = "await agent('x', {agentType: 'nomodel'})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, True)

    def test_workflow_model_shorthand(self):
        script = "const model = 'opus'\nawait agent('x', {label: 'a', model})"
        self.check({"tool_name": "Workflow", "tool_input": {"script": script}}, False)


def read_log(path):
    with open(path, encoding="utf-8", newline="") as fh:
        return list(csv.DictReader(fh))


class LaunchLog(unittest.TestCase):
    """The launch log (one CSV line per ALLOWED launch) and its independence from the decision."""

    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        agents = os.path.join(cls.tmp.name, ".claude", "agents")
        os.makedirs(agents)
        with open(os.path.join(agents, "reviewer.md"), "w", encoding="utf-8") as f:
            f.write("---\nname: reviewer\ndescription: x\nmodel: opus\neffort: high\n---\nbody\n")

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def log_path(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        return os.path.join(d.name, "nested", "agent-launches.csv")   # folder does not exist yet

    def test_allowed_launch_is_logged_with_fields(self):
        log = self.log_path()
        ev = {"tool_name": "Agent", "session_id": "sess-1", "tool_use_id": "toolu_1",
              "transcript_path": "C:/Users/x/.claude/projects/c--code-files-spaarke-wt-demo-r1/sess-1.jsonl",
              "tool_input": {"prompt": "SECRET PROMPT TEXT", "model": "sonnet", "subagent_type": "implementer",
                             "description": "implement task 042"}}
        code, out = run(ev, self.tmp.name, log)
        self.assertEqual((code, out), (0, ""))
        rows = read_log(log)
        self.assertEqual(len(rows), 1)
        r = rows[0]
        self.assertEqual((r["session_id"], r["project"], r["tool"], r["subagent_type"], r["model"], r["description"],
                          r["tool_use_id"]),
                         ("sess-1", "demo-r1", "Agent", "implementer", "sonnet", "implement task 042", "toolu_1"))
        self.assertEqual(r["transcript_path"], ev["transcript_path"])
        self.assertRegex(r["ts_utc"], r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
        with open(log, encoding="utf-8") as fh:
            self.assertNotIn("SECRET PROMPT TEXT", fh.read())      # never the prompt

    def test_header_written_once_and_lines_appended(self):
        log = self.log_path()
        for i in range(3):
            run({"tool_name": "Task", "tool_input": {"prompt": "p", "model": "haiku", "description": f"d{i}"}},
                self.tmp.name, log)
        with open(log, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        self.assertEqual(len(lines), 4)
        self.assertTrue(lines[0].startswith("ts_utc,session_id,transcript_path,project,tool,subagent_type,model,effort,description"))
        self.assertEqual(sum(1 for l in lines if l.startswith("ts_utc,")), 1)

    def test_existing_log_is_appended_to_without_a_second_header(self):
        log = self.log_path()
        os.makedirs(os.path.dirname(log))
        with open(log, "w", encoding="utf-8", newline="") as fh:       # another launch already created it
            fh.write("ts_utc,session_id,transcript_path,project,tool,subagent_type,model,effort,description,tool_use_id\n"
                     "2026-01-01T00:00:00Z,s,,p,Agent,,sonnet,,earlier,t0\n")
        code, out = run({"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "haiku", "description": "later"}},
                        self.tmp.name, log)
        self.assertEqual((code, out), (0, ""))
        with open(log, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        self.assertEqual(len(lines), 3)
        self.assertEqual(sum(1 for l in lines if l.startswith("ts_utc,")), 1)
        self.assertEqual([r["description"] for r in read_log(log)], ["earlier", "later"])

    def test_parallel_first_launches_write_one_header(self):
        log = self.log_path()
        env = dict(os.environ, CLAUDE_PROJECT_DIR=self.tmp.name, SPAARKE_AGENT_LAUNCH_LOG=log)
        ev = json.dumps({"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "haiku", "description": "x"}})
        procs = [subprocess.Popen([sys.executable, HOOK], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, text=True, env=env) for _ in range(6)]
        for pr in procs:
            pr.communicate(ev, timeout=30)
            self.assertEqual(pr.returncode, 0)
        with open(log, encoding="utf-8") as fh:
            lines = fh.read().splitlines()
        self.assertEqual(sum(1 for l in lines if l.startswith("ts_utc,")), 1)
        self.assertTrue(lines[0].startswith("ts_utc,"))
        self.assertEqual(len(lines), 7)

    def test_definition_supplies_model_and_effort(self):
        log = self.log_path()
        run({"tool_name": "Agent", "tool_input": {"prompt": "p", "subagent_type": "reviewer", "description": "review"}},
            self.tmp.name, log)
        r = read_log(log)[0]
        self.assertEqual((r["model"], r["effort"]), ("opus", "high"))

    def test_description_truncated_and_single_line(self):
        log = self.log_path()
        desc = "line one\nline two " + "x" * 300
        run({"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "sonnet", "description": desc}}, self.tmp.name, log)
        r = read_log(log)[0]
        self.assertEqual(len(r["description"]), 120)
        self.assertNotIn("\n", r["description"])

    def test_workflow_launch_logged(self):
        log = self.log_path()
        script = "await agent('y', {model: 'sonnet'})"
        run({"tool_name": "Workflow", "tool_input": {"script": script, "description": "fan-out"}}, self.tmp.name, log)
        r = read_log(log)[0]
        self.assertEqual((r["tool"], r["description"]), ("Workflow", "fan-out"))

    # not logged
    def test_denied_launch_is_not_logged(self):
        log = self.log_path()
        code, out = run({"tool_name": "Agent", "tool_input": {"prompt": "p", "description": "d"}}, self.tmp.name, log)
        self.assertTrue(denied(out))
        self.assertFalse(os.path.exists(log))

    def test_other_tool_and_malformed_input_not_logged(self):
        log = self.log_path()
        run({"tool_name": "Bash", "tool_input": {"command": "ls"}}, self.tmp.name, log)
        run("not json", self.tmp.name, log)
        self.assertFalse(os.path.exists(log))

    # logging never changes the decision
    def test_unwritable_log_does_not_change_allow(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        blocker = os.path.join(d.name, "afile")
        with open(blocker, "w") as f:
            f.write("x")
        bad = os.path.join(blocker, "sub", "launches.csv")        # a file where a folder is needed
        code, out = run({"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "sonnet"}}, self.tmp.name, bad)
        self.assertEqual((code, out), (0, ""))

    def test_unwritable_log_does_not_change_deny(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        code, out = run({"tool_name": "Agent", "tool_input": {"prompt": "p"}}, self.tmp.name, d.name)  # a folder, not a file
        self.assertEqual(code, 0)
        self.assertTrue(denied(out))

    def test_unwritable_log_reports_on_stderr_only(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        env = dict(os.environ, CLAUDE_PROJECT_DIR=self.tmp.name, SPAARKE_AGENT_LAUNCH_LOG=d.name)
        p = subprocess.run([sys.executable, HOOK], input=json.dumps(
            {"tool_name": "Agent", "tool_input": {"prompt": "p", "model": "sonnet"}}),
            capture_output=True, text=True, env=env, timeout=20)
        self.assertEqual((p.returncode, p.stdout), (0, ""))
        self.assertIn("launch not logged", p.stderr)

    # the folder -> project rule is shared with spend-report
    def test_project_name_matches_spend_report(self):
        def load(path, name):
            spec = importlib.util.spec_from_file_location(name, path)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)
            return mod
        hook, sr = load(HOOK, "hook"), load(SPEND_REPORT, "sr")
        for folder in ["c--code-files-spaarke-wt-demo-r1", "C--code-files-spaarke", "c--code-files-spaarke-website",
                       "C--wtf2", "other-folder", "C--code-files-spaarke-WT-Mixed"]:
            self.assertEqual(hook.project_name(folder), sr.project_name(folder), folder)


if __name__ == "__main__":
    unittest.main()
