#!/usr/bin/env python3
"""PreToolUse hook: every sub-agent and workflow agent states its model.

Owner rule (2026-10-09, .claude/constraints/agent-cost.md "Choosing a model and effort"):
no agent runs on a model nobody chose. A sub-agent launch passes `model`, or uses an agent
definition whose frontmatter sets `model:`; a workflow script names a model (or an `agentType`
whose definition sets one) in every `agent()` call. Otherwise this hook DENIES the call with a
reason Claude reads and acts on — it re-issues the call with a model picked from the policy
table. It never asks the user.

Fails open: unreadable input, an unknown tool or any internal error lets the call through.
"""
import json
import os
import re
import sys

POLICY = ".claude/constraints/agent-cost.md (Choosing a model and effort)"
# Built-ins that inherit the session model unless the call names one.
BUILT_IN = {"general-purpose", "explore", "plan", "claude", "output-style-setup"}
# Built-ins that run on a fixed model of their own (claude-code-guide: Haiku; statusline-setup: Sonnet).
FIXED_MODEL_BUILT_IN = {"claude-code-guide", "statusline-setup"}

FRONTMATTER = re.compile(r"---\s*\r?\n(.*?)\r?\n---", re.S)
NAME_LINE = re.compile(r"^name:\s*['\"]?([^'\"\r\n#]+?)['\"]?\s*$", re.M)
MODEL_LINE = re.compile(r"^model:\s*['\"]?([^'\"\s#]+)", re.M)


def find_definition(name, project_dir):
    """(found, model) for the agent definition whose frontmatter `name:` (or file stem) is `name`.

    Claude Code identifies agents by frontmatter `name:` and scans the agents folders recursively.
    """
    homes = [os.path.join(project_dir, ".claude", "agents"), os.path.join(os.path.expanduser("~"), ".claude", "agents")]
    want = name.lower()
    for home in homes:
        for root, _dirs, files in os.walk(home):
            for fn in files:
                if not fn.endswith(".md"):
                    continue
                try:
                    with open(os.path.join(root, fn), encoding="utf-8-sig", errors="ignore") as fh:
                        text = fh.read()
                except OSError:
                    continue
                m = FRONTMATTER.match(text)
                fm = m.group(1) if m else ""
                nm = NAME_LINE.search(fm)
                ident = (nm.group(1) if nm else os.path.splitext(fn)[0]).strip().lower()
                if ident != want:
                    continue
                mm = MODEL_LINE.search(fm)
                return True, (mm.group(1).strip() if mm else None)
    return False, None


def definition_has_model(name, project_dir):
    found, model = find_definition(name, project_dir)
    return found and bool(model) and model.lower() != "inherit"


def agent_call_problem(tool_input, project_dir):
    if str(tool_input.get("model") or "").strip():
        return None
    kind = str(tool_input.get("subagent_type") or "general-purpose").strip()
    low = kind.lower()
    if low == "fork" or ":" in kind or low in FIXED_MODEL_BUILT_IN:
        return None  # forks inherit by design; plugin agents carry their own definition; fixed-model built-ins
    if low in BUILT_IN:
        return f"This '{kind}' agent call passes no `model`."
    found, model = find_definition(kind, project_dir)
    if found and model and model.lower() != "inherit":
        return None
    if not found:
        return f"No agent definition named '{kind}' was found, and this call passes no `model`."
    return f"The '{kind}' agent definition sets no `model:` (or `inherit`), and this call passes no `model`."


def strip_comments(script):
    """Remove // and /* */ comments, keeping strings and template literals intact."""
    out, i, n, quote = [], 0, len(script), None
    while i < n:
        c = script[i]
        if quote:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(script[i + 1])
                i += 2
                continue
            if c == quote:
                quote = None
            i += 1
            continue
        if c in "'\"`":
            quote = c
            out.append(c)
            i += 1
            continue
        if script.startswith("//", i):
            j = script.find("\n", i)
            i = n if j < 0 else j
            continue
        if script.startswith("/*", i):
            j = script.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def split_agent_calls(script):
    """Yield the argument text of every agent(...) call in a (comment-free) workflow script."""
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
            with open(full, encoding="utf-8-sig", errors="ignore") as fh:
                script = fh.read()
        except OSError:
            return None
    if not script:
        return None  # a saved named workflow: not inspectable here

    def named(args):
        if re.search(r"\bmodel\s*[:,}]", args):
            return True
        at = re.search(r"\bagentType\s*:\s*['\"`]([^'\"`]+)", args)
        return bool(at) and definition_has_model(at.group(1), project_dir)

    calls = list(split_agent_calls(strip_comments(script)))
    missing = [i + 1 for i, args in enumerate(calls) if not named(args)]
    if not missing:
        return None
    which = ", ".join(str(n) for n in missing[:8])
    return (f"agent() call(s) #{which} of {len(calls)} in this workflow script name no `model` "
            "(add `model: '...'` to each call's options, or `agentType:` an agent definition that sets one).")


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
              " and re-issue the call: e.g. the `implementer` definition or sonnet for a scoped implementation or "
              "re-check, opus for planning or root cause, `adversarial-reviewer` (fable) for the one independent "
              "review, `code-mapper` for search. Do not ask the user; decide.")
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
