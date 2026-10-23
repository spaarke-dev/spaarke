#!/usr/bin/env python
"""Context-checkpoint hooks: prompt a /context-handoff before auto-compaction, and leave a mechanical trail.

Claude cannot see its own context usage, and auto-compaction (at `autoCompactWindow`, 400K here; see
.claude/constraints/agent-cost.md rule 6) summarises the conversation whether or not working state was saved.
One script, three modes, all registered in .claude/settings.json:

  warn       PostToolUse + UserPromptSubmit. Reads the context size of the latest main-session call from the
             transcript (input + cache-creation + cache-read tokens, the same sum /context and the statusLine use)
             and, the first time it crosses 85% of the auto-compact window, tells Claude to run /context-handoff
             now. A last warning follows at 95%. Each level fires once; both re-arm when usage falls below 60%
             (that is, after compaction). Sub-agent tool calls (`agent_id` in the input) are ignored.
  precompact PreCompact. Writes a mechanical snapshot (branch, uncommitted files, unpushed and recent commits, the
             project's current-task.md age, whether the warning fired) to a per-session file. It never blocks:
             the docs say a blocked proactive compaction is skipped silently, they do not say the reason reaches
             Claude or that compaction is retried, and on the context-limit recovery path a block fails the request.
  reinject   SessionStart (compact). Prints that snapshot, then deletes it. It complements
             .claude/hooks/reinject-project-state.ps1, which re-injects current-task.md itself.

The window is CLAUDE_CODE_AUTO_COMPACT_WINDOW, else the first settings file (project local, project, user) that sets
it, where `modelSettings.<model>.autoCompactWindow` wins over the top-level key. With no window configured the warning
does nothing (the real limit is unknown); a window above the model's own limit (400K on a 200K model) means it never
fires. Usage recorded before the latest compaction is ignored. Known limits: settings are resolved per file, not merged
first (no file here sets both keys); two parallel tool calls could each fire the same level (not reproduced). Fails open: every path exits 0 and errors are swallowed.
Test seams: SPRK_CONTEXT_STATE_DIR (state + snapshot folder), SPRK_USER_SETTINGS (user settings file).
Enforced by: this hook; controls in scripts/quality/tests/test_context_checkpoint.py.
"""
import json
import os
import re
import subprocess
import sys
import tempfile
import time

WARN_LEVELS = (85, 95)
REARM_BELOW = 60
TAIL_BYTES = (512 * 1024, 8 * 1024 * 1024)  # read the end of the transcript; widen once if no main-session call found
SNAPSHOT_MAX_AGE_S = 6 * 3600
FALLBACK_MATCH_S = 600  # a snapshot from another session id in the same cwd counts only if this recent


def state_dir():
    d = os.environ.get("SPRK_CONTEXT_STATE_DIR") or os.path.join(tempfile.gettempdir(), "claude-context-checkpoint")
    os.makedirs(d, exist_ok=True)
    return d


def safe_id(session_id):
    return re.sub(r"[^A-Za-z0-9_-]", "_", session_id or "unknown")[:80]


def last_main_usage(transcript_path):
    """(tokens, model) of the latest main-session assistant call, or (None, None)."""
    size = os.path.getsize(transcript_path)
    for tail in TAIL_BYTES:
        with open(transcript_path, "rb") as f:
            f.seek(max(0, size - tail))
            lines = f.read().split(b"\n")
        for raw in reversed(lines):
            if b'"compact_boundary"' in raw or b'"isCompactSummary"' in raw:
                try:
                    entry = json.loads(raw)
                except ValueError:
                    entry = {}
                if entry.get("subtype") == "compact_boundary" or entry.get("isCompactSummary"):
                    return None, None  # compacted since the last call: the usage before it is stale
            if b'"assistant"' not in raw or b'"usage"' not in raw:
                continue
            try:
                entry = json.loads(raw)
            except ValueError:
                continue  # the first line of the tail is usually cut
            if entry.get("type") != "assistant" or entry.get("isSidechain"):
                continue
            message = entry.get("message") or {}
            usage = message.get("usage")
            if not isinstance(usage, dict):
                continue
            tokens = sum(int(usage.get(k) or 0) for k in
                         ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens"))
            if tokens == 0 or message.get("model") == "<synthetic>":
                continue  # an API-error placeholder, not a real call
            return tokens, message.get("model")
        if size <= tail:
            break
    return None, None


def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def auto_compact_window(project_dir, model):
    env = os.environ.get("CLAUDE_CODE_AUTO_COMPACT_WINDOW")
    if env and env.strip().isdigit():
        return int(env)
    user = os.environ.get("SPRK_USER_SETTINGS") or os.path.join(os.path.expanduser("~"), ".claude", "settings.json")
    files = []
    if project_dir:
        files += [os.path.join(project_dir, ".claude", "settings.local.json"),
                  os.path.join(project_dir, ".claude", "settings.json")]
    files.append(user)
    for path in files:
        settings = read_json(path)
        if not isinstance(settings, dict):
            continue
        per_model = ((settings.get("modelSettings") or {}).get(model) or {}) if model else {}
        value = per_model.get("autoCompactWindow", settings.get("autoCompactWindow"))
        if isinstance(value, int) and value > 0:
            return value
    return None


def load_state(sid):
    state = read_json(os.path.join(state_dir(), sid + ".json"))
    return state if isinstance(state, dict) else {}


def save_state(sid, state):
    with open(os.path.join(state_dir(), sid + ".json"), "w", encoding="utf-8") as f:
        json.dump(state, f)


def warn(data):
    if data.get("agent_id"):
        return  # a sub-agent's tool call: its context is not the session's
    transcript = data.get("transcript_path")
    if not transcript or not os.path.isfile(transcript):
        return
    tokens, model = last_main_usage(transcript)
    window = auto_compact_window(os.environ.get("CLAUDE_PROJECT_DIR") or data.get("cwd"), model)
    if not tokens or not window:
        return
    pct = 100.0 * tokens / window
    sid = safe_id(data.get("session_id"))
    state = load_state(sid)
    fired = int(state.get("fired_level") or 0)
    if pct < REARM_BELOW:
        if fired:
            save_state(sid, {"fired_level": 0})
        return
    due = [lvl for lvl in WARN_LEVELS if pct >= lvl and lvl > fired]
    if not due:
        return
    level = max(due)
    save_state(sid, {"fired_level": level, "fired_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                     "fired_pct": round(pct)})
    lead = "FINAL WARNING: auto-compaction is imminent." if level >= WARN_LEVELS[-1] else "Checkpoint now."
    message = (
        f"{lead} Context is at {pct:.0f}% of the {window // 1000}K auto-compact window ({tokens // 1000}K tokens); "
        f"compaction will summarise this conversation at about {window // 1000}K. Before any other work, run "
        "/context-handoff: in a project, rewrite projects/<name>/current-task.md (active task and step, uncommitted "
        "files, open owner questions, the exact next action); outside a project, update your session-resume memory "
        "file. Then continue the task. (Hook: scripts/quality/context-checkpoint.py; CLAUDE.md section 5.)"
    )
    event = data.get("hook_event_name") or "PostToolUse"
    print(json.dumps({"hookSpecificOutput": {"hookEventName": event, "additionalContext": message}}))


def git(cwd, *args):
    try:
        out = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, timeout=4,
                             encoding="utf-8", errors="replace")
        return out.stdout.rstrip() if out.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def project_task_file(root, branch):
    candidates = []
    m = re.match(r"^work/(.+)$", branch or "")
    if m:
        candidates.append(m.group(1))
    m = re.match(r"^spaarke-wt-(.+)$", os.path.basename(root.rstrip("/\\")))
    if m:
        candidates.append(m.group(1))
    for name in candidates:
        path = os.path.join(root, "projects", name, "current-task.md")
        if os.path.isfile(path):
            return path
    return None


def precompact(data):
    cwd = data.get("cwd") or os.getcwd()
    now = time.time()
    lines = [f"- Taken: {time.strftime('%Y-%m-%d %H:%M:%SZ', time.gmtime(now))} (trigger: {data.get('trigger')})",
             f"- cwd: {cwd}"]
    root = git(cwd, "rev-parse", "--show-toplevel")
    if root:
        branch = git(root, "rev-parse", "--abbrev-ref", "HEAD")
        lines.append(f"- Branch: {branch}")
        if git(root, "rev-parse", "--abbrev-ref", "@{u}"):
            lines.append(f"- Unpushed commits: {git(root, 'rev-list', '--count', '@{u}..HEAD') or '?'}")
        else:
            lines.append("- Upstream: none")
        porcelain = git(root, "status", "--porcelain")
        status = [s for s in (porcelain or "").split("\n") if s]
        lines.append(f"- Uncommitted entries: {len(status) if porcelain is not None else '? (git status failed)'}")
        lines += [f"    {s}" for s in status[:30]]
        if len(status) > 30:
            lines.append(f"    ... and {len(status) - 30} more")
        log = git(root, "log", "-5", "--format=%h %cr %s")
        if log:
            lines.append("- Recent commits:")
            lines += [f"    {s}" for s in log.split("\n")]
        task = project_task_file(root, branch)
        if task:
            age_min = (now - os.path.getmtime(task)) / 60
            lines.append(f"- {os.path.relpath(task, root)} last written {age_min:.0f} min ago"
                         + (" (STALE: no checkpoint this stretch; rebuild it from git)" if age_min > 120 else ""))
    state = load_state(safe_id(data.get("session_id")))
    if state.get("fired_level"):
        lines.append(f"- Checkpoint prompt fired at {state.get('fired_pct')}% ({state.get('fired_at')})")
    else:
        lines.append("- Checkpoint prompt did not fire before this compaction")
    snapshot = {"cwd": os.path.normcase(os.path.abspath(cwd)), "text": "\n".join(lines)}
    with open(os.path.join(state_dir(), safe_id(data.get("session_id")) + ".snapshot.json"), "w",
              encoding="utf-8") as f:
        json.dump(snapshot, f)


def reinject(data):
    folder = state_dir()
    sid = safe_id(data.get("session_id"))
    now = time.time()
    path = os.path.join(folder, sid + ".snapshot.json")
    if not os.path.isfile(path):
        # The docs do not promise the session id survives compaction: accept a very recent one from the same cwd.
        cwd = os.path.normcase(os.path.abspath(data.get("cwd") or os.getcwd()))
        recent = [os.path.join(folder, n) for n in os.listdir(folder) if n.endswith(".snapshot.json")]
        recent = [p for p in recent if now - os.path.getmtime(p) < FALLBACK_MATCH_S
                  and (read_json(p) or {}).get("cwd") == cwd]
        path = max(recent, key=os.path.getmtime) if recent else None
    if not path or now - os.path.getmtime(path) > SNAPSHOT_MAX_AGE_S:
        return
    snapshot = read_json(path) or {}
    if snapshot.get("text"):
        print("## Pre-compaction snapshot (mechanical; written by scripts/quality/context-checkpoint.py)")
        print("Verify against git before acting on it. current-task.md / your checkpoint is the source of the plan.")
        print(snapshot["text"])
    os.remove(path)


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # a piped stdout is cp1252 on Windows
        data = json.loads(sys.stdin.read() or "{}")
        if not isinstance(data, dict):
            return
        {"warn": warn, "precompact": precompact, "reinject": reinject}.get(mode, lambda _d: None)(data)
    except Exception:  # noqa: BLE001 — a hook must never break the session
        pass


if __name__ == "__main__":
    main()
    sys.exit(0)
