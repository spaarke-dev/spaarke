#!/usr/bin/env python3
"""PreToolUse hook: every sub-agent and workflow agent states its model.

Owner rule (2026-10-09, .claude/constraints/agent-cost.md "Choosing a model and effort"):
no agent runs on a model nobody chose. A sub-agent launch passes `model`, or uses an agent
definition whose frontmatter sets `model:`; a workflow script names a model in every
`agent()` call. Otherwise this hook DENIES the call with a reason Claude reads and acts on —
it re-issues the call with a model picked from the policy table. It never asks the user.

Fails open: unreadable input, an unknown tool or any internal error lets the call through.
"""
import json
import os
import re
import sys

POLICY = ".claude/constraints/agent-cost.md (Choosing a model and effort)"
BUILT_IN = {"general-purpose", "explore", "plan", "claude-code-guide", "statusline-setup", "claude"}


def frontmatter_model(name, project_dir):
    """The `model:` of an agent definition named `name`, or None."""
    homes = [os.path.join(project_dir, ".claude", "agents"), os.path.join(os.path.expanduser("~"), ".claude", "agents")]
    for home in homes:
        path = os.path.join(home, name + ".md")
        if not os.path.isfile(path):
            continue
        try:
            text = open(path, encoding="utf-8", errors="ignore").read()
        except OSError:
            return None
        m = re.match(r"---\s*\n(.*?)\n---", text, re.S)
        if not m:
            return None
        mm = re.search(r"^model:\s*([^\s#]+)", m.group(1), re.M)
        return mm.group(1).strip().strip("'\"") if mm else None
    return None


def agent_call_problem(tool_input, project_dir):
    if str(tool_input.get("model") or "").strip():
        return None
    kind = str(tool_input.get("subagent_type") or "general-purpose").strip()
    if kind == "fork" or ":" in kind:  # forks inherit by design; plugin agents carry their own definition
        return None
    if kind.lower() not in BUILT_IN:
        model = frontmatter_model(kind, project_dir)
        if model and model.lower() != "inherit":
            return None
        return (f"The '{kind}' agent definition sets no `model:` (or `inherit`), and this call passes no `model`.")
    return f"This '{kind}' agent call passes no `model`."


def split_agent_calls(script):
    """Yield the argument text of every agent(...) call in a workflow script."""
    for m in re.finditer(r"(?<![\w.$])agent\s*\(", script):
        i, depth, quote = m.end(), 1, None
        while i < len(script) and depth:
            c = script[i]
            if quote:
                if c == "\\":
                    i += 1
                elif c == quote:
                    quote = None
            elif c in "'\"`":
                quote = c
            elif c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
            i += 1
        yield script[m.end():i - 1]


def workflow_problem(tool_input, project_dir):
    script = tool_input.get("script") or ""
    path = tool_input.get("scriptPath")
    if not script and path:
        full = path if os.path.isabs(path) else os.path.join(project_dir, path)
        try:
            script = open(full, encoding="utf-8", errors="ignore").read()
        except OSError:
            return None
    if not script:
        return None  # a saved named workflow: not inspectable here
    missing = [i + 1 for i, args in enumerate(split_agent_calls(script)) if not re.search(r"\bmodel\s*:", args)]
    if not missing:
        return None
    which = ", ".join(str(n) for n in missing[:8])
    return f"agent() call(s) #{which} in this workflow script name no `model` (add `model: '...'` to each call's options)."


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    tool = event.get("tool_name") or ""
    tool_input = event.get("tool_input") or {}
    project_dir = os.environ.get("CLAUDE_PROJECT_DIR") or event.get("cwd") or os.getcwd()
    try:
        if tool in ("Agent", "Task"):
            problem = agent_call_problem(tool_input, project_dir)
        elif tool == "Workflow":
            problem = workflow_problem(tool_input, project_dir)
        else:
            return 0
    except Exception:
        return 0
    if not problem:
        return 0
    reason = (problem + " Choose the model (and effort) for this work from " + POLICY +
              " and re-issue the call: e.g. sonnet for a scoped implementation or re-check, opus/fable for planning, "
              "root-cause or the independent review, haiku for mechanical listing. Do not ask the user; decide.")
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
