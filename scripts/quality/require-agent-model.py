#!/usr/bin/env python3
"""PreToolUse hook: every sub-agent and workflow agent states its model.

Owner rule (2026-10-09, .claude/constraints/agent-cost.md "Choosing a model and effort"):
no agent runs on a model nobody chose. A sub-agent launch passes `model`, or uses an agent
definition whose frontmatter sets `model:`; a workflow script names a model (or an `agentType`
whose definition sets one) in every `agent()` call. Otherwise this hook DENIES the call with a
reason Claude reads and acts on — it re-issues the call with a model picked from the policy
table. It never asks the user.

Fails open: unreadable input, an unknown tool or any internal error lets the call through.

Launch log (2026-10-13): every launch this hook ALLOWS appends one CSV line to
~/.claude/spend-archive/agent-launches.csv (override: SPAARKE_AGENT_LAUNCH_LOG), outside the repo.
Fields: utc timestamp, session id, transcript path, project, tool, subagent type, model, effort,
description (120 chars), tool_use_id (joins the sub-agent's meta file). Never the prompt.
`scripts/ai-cost/spend-report.py --model-fit` joins these lines to the sub-agent transcripts.
A logging failure goes to stderr only and never changes the decision or the exit code.
"""
import csv
import datetime
import io
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


def find_frontmatter(name, project_dir):
    """Frontmatter text (possibly "") of the agent definition whose `name:` (or file stem) is `name`; None if absent.

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
                return fm
    return None


def find_definition(name, project_dir):
    """(found, model) for the agent definition named `name`."""
    fm = find_frontmatter(name, project_dir)
    if fm is None:
        return False, None
    mm = MODEL_LINE.search(fm)
    return True, (mm.group(1).strip() if mm else None)


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


# --- launch log ---------------------------------------------------------------------------
# One CSV line per ALLOWED launch, outside the repo (it holds project names and descriptions).
# `scripts/ai-cost/spend-report.py --model-fit` reads it; keep the column names in step with it.
LAUNCH_LOG_ENV = "SPAARKE_AGENT_LAUNCH_LOG"
LAUNCH_COLUMNS = ["ts_utc", "session_id", "transcript_path", "project", "tool", "subagent_type",
                  "model", "effort", "description", "tool_use_id"]
DESCRIPTION_MAX = 120
EFFORT_LINE = re.compile(r"^effort:\s*['\"]?([^'\"\s#]+)", re.M)


def launch_log_path():
    return os.environ.get(LAUNCH_LOG_ENV) or os.path.join(
        os.path.expanduser("~"), ".claude", "spend-archive", "agent-launches.csv")


def project_name(folder):
    """Same folder -> project rule as scripts/ai-cost/spend-report.py project_name() (a test keeps them equal)."""
    low = folder.lower()
    pre = "c--code-files-spaarke-wt-"
    if low.startswith(pre):
        return folder[len(pre):]
    if low == "c--code-files-spaarke":
        return "spaarke (main)"
    if low.startswith("c--code-files-"):
        return folder[len("c--code-files-"):]
    if low.startswith("c--"):
        return "wt:" + folder[3:]
    return folder


def project_of(event, project_dir):
    """Project name as spend-report derives it. The transcript's own folder is exact; otherwise encode the
    directory the way Claude Code names project folders (every non-alphanumeric character becomes '-')."""
    tp = str(event.get("transcript_path") or "")
    if tp:
        parent = os.path.dirname(tp)
        if os.path.basename(os.path.dirname(parent)).lower() == "projects" and os.path.basename(parent):
            return project_name(os.path.basename(parent))
    return project_name(re.sub(r"[^A-Za-z0-9]", "-", project_dir))


def launch_record(event, tool, tool_input, project_dir):
    kind = str(tool_input.get("subagent_type") or "").strip()
    model = str(tool_input.get("model") or "").strip()
    effort = str(tool_input.get("effort") or "").strip()
    if kind and kind.lower() not in BUILT_IN and ":" not in kind:
        fm = find_frontmatter(kind, project_dir)       # a definition supplies model/effort the call omitted
        if fm is not None:
            if not model:
                mm = MODEL_LINE.search(fm)
                model = mm.group(1).strip() if mm else ""
            if not effort:
                em = EFFORT_LINE.search(fm)
                effort = em.group(1).strip() if em else ""
    if tool == "Workflow":
        desc = tool_input.get("description") or tool_input.get("name") or tool_input.get("scriptPath") or "workflow"
    else:
        desc = tool_input.get("description") or ""
    desc = " ".join(str(desc).split())[:DESCRIPTION_MAX]       # one line; never the prompt
    return [datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            str(event.get("session_id") or ""), str(event.get("transcript_path") or ""),
            project_of(event, project_dir), tool, kind, model or "inherit", effort, desc,
            str(event.get("tool_use_id") or "")]


def log_launch(event, tool, tool_input, project_dir):
    """Append one line to the launch log. Never raises and never prints to stdout: the hook's decision
    must not depend on whether the log could be written."""
    try:
        path = launch_log_path()
        rec = launch_record(event, tool, tool_input, project_dir)
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        def csv_line(fields):
            sink = io.StringIO()
            csv.writer(sink, lineterminator="\n").writerow(fields)
            return sink.getvalue()
        line = csv_line(rec)
        try:                                            # O_EXCL: exactly one launch creates the file and writes the header
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            fd = None
        if fd is not None:
            with os.fdopen(fd, "w", encoding="utf-8", newline="") as fh:
                fh.write(csv_line(LAUNCH_COLUMNS) + line)   # header + first line in one write
        else:
            with open(path, "a", encoding="utf-8", newline="") as fh:
                fh.write(line)                          # one write: a concurrent launch cannot split a line
    except Exception as ex:                             # stderr only
        print(f"require-agent-model: launch not logged: {ex}", file=sys.stderr)


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
        log_launch(event, tool, tool_input, project_dir)
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
