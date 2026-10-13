"""Must-fire / must-not-fire controls for scripts/quality/require-agent-model.py.

Run: python -m unittest discover -s scripts/quality/tests
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HOOK = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "require-agent-model.py")


def run(event, project_dir):
    env = dict(os.environ, CLAUDE_PROJECT_DIR=project_dir)
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


if __name__ == "__main__":
    unittest.main()
