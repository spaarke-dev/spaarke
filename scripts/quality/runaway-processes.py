#!/usr/bin/env python
"""Find processes that Claude Code sessions left running, name the session or worktree that started them, and say
whether they are safe to stop.

Why: the Bash and PowerShell tools time out and move on, but a child they started can keep running. Leftovers have
spun a core each for days (python fed a script on stdin, unbounded `until ... sleep` polls, orphaned dotnet-format
BuildHosts) until every build and test on the machine crawled (.claude/FAILURE-MODES.md G-22, G-26).

The rule this script must never break: it does not offer to stop a process that is some session's live work.

How it decides. Two process snapshots SAMPLE_SECONDS apart (Win32_Process, read through PowerShell) give each
process's CPU rate now; total CPU / age gives its lifetime rate. Each process's parents are walked up; a parent created
after its child is a reused PID, so the chain stops there.
  * Reaching a Claude Code `claude.exe`: the process belongs to that live session. `--resume=<id>` names the session
    (for a new session without it, a `Temp\\claude\\<folder>\\<id>\\` path on a child's command line does). A live
    session may be waiting on its own background task or subagent, so its processes are never more than `check`.
  * A missing parent before any `claude.exe`, with a dev tool at the top of the chain: an orphan. A session path or a
    worktree path in the chain's command lines links it to sessions (a worktree path links it to every live session
    started in that worktree). A cmd/PowerShell orphan with no such evidence is skipped: Windows and vendor software
    start those too.
  * Anything else (explorer.exe, a service, a user's terminal, the desktop app) was not started by Claude Code.
A session is active when its transcript (~/.claude/projects/<folder>/<id>.jsonl), its subagent transcripts
(<id>/**/*.jsonl) or its background-task output (%TEMP%\\claude\\<folder>\\<id>\\tasks\\*) changed in the last 30 min.

Verdicts. Each tree is listed once, at its top process.
  safe-to-stop   spinning, a known leak pattern, an orphan with no live or active session linked to it
  likely-leaked  spinning, an orphan with no live or active session linked to it
  check          spinning, and its session is alive but idle (or cannot be read): ask before stopping
  busy           spinning but protected: its session is active, or the tree holds a deploy, git, a build server, a
                 session's MCP server or a claude.exe
  idle-leftover  using no CPU, alive >= 6 h, an orphan with no live or active session (an emulator, a dev server, a
                 `tail -f` keep-alive)
"Spinning" (is_spinning): alive >= 15 min, half a core or more now, and either half a core or more over its whole life
or half a core or more in a scan at least 15 min earlier.

Nothing is stopped automatically. `kill <pid>` scans again first (about 10 s), then stops the tree only if the PID is
still the top of a safe-to-stop, likely-leaked or idle-leftover tree; `check` and `busy` need --force. Even with
--force it never stops a PID the fresh scan does not list, a claude.exe or the caller's own shell. A tree takes the
most restrictive verdict of its members (any busy member: busy; else any check member: check). It names every PID it stops (no `taskkill /T`, which would
also take children it never checked). Agents run it only after the user says yes.

Usage:
  python scripts/quality/runaway-processes.py [scan] [--json]    scan now (about 20 s) and print the report
  python scripts/quality/runaway-processes.py kill PID [PID ...]  stop listed trees (the user's yes first)
  python scripts/quality/runaway-processes.py banner             SessionStart hook: show the cached report; refresh
                                                                 it in the background when older than 10 min
Every scan writes the report to --cache (default: %TEMP%\\spaarke-runaway-processes.json); a failed scan records its
error there and the banner shows it.
Test seams: --snapshot <json> replaces the live snapshot; --projects-dir and --tasks-dir replace the transcript and
task-output folders.
Enforced by: this script and its SessionStart banner; controls in scripts/quality/tests/test_runaway_processes.py.
"""

import argparse
import base64
import datetime as dt
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

SAMPLE_SECONDS = 15
KILL_SAMPLE_SECONDS = 5
SPIN_CORE = 0.5                # half a core
IDLE_CORE = 0.1                # below this a leftover counts as idle
SPIN_MIN_AGE = 15 * 60         # alive (or seen hot) at least this long
SESSION_IDLE = 30 * 60         # no session file written for this long: the session is idle
LEFTOVER_AGE = 6 * 3600
REFRESH_AFTER = 10 * 60
STALE_REPORT = 30 * 60
LOCK_STALE = 5 * 60
SNAPSHOT_TIMEOUT = SAMPLE_SECONDS + 150

DEFAULT_CACHE = os.path.join(tempfile.gettempdir(), "spaarke-runaway-processes.json")
DEFAULT_PROJECTS = os.path.join(os.path.expanduser("~"), ".claude", "projects")
DEFAULT_TASKS = os.path.join(tempfile.gettempdir(), "claude")
SELF = os.path.abspath(__file__)

UUID = r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
RESUME_RE = re.compile(r"--(?:resume|session-id)[= ](" + UUID + ")", re.I)
# a session's scratchpad or task-output folder: ...\Temp\claude\<folder>\<session id>\...
SESSION_PATH_RE = re.compile(r"[\\/]claude[\\/][^\\/\s\"']+[\\/](" + UUID + r")(?![0-9a-f-])", re.I)
WORKTREE_RE = re.compile(
    r"(?<![\w])([a-z]:[\\/](?:code_files[\\/][^\\/\s\"';|&]+|tmp[\\/][^\\/\s\"';|&]+|wt[^\\/\s\"';|&]*))", re.I)

# Programs a tool call starts. Only these count as an orphan at the top of a broken chain.
DEV_TOOLS = {
    "node.exe", "python.exe", "pythonw.exe", "py.exe", "pwsh.exe", "powershell.exe", "bash.exe", "sh.exe",
    "cmd.exe", "dotnet.exe", "cat.exe", "sleep.exe", "tail.exe", "timeout.exe", "grep.exe", "sed.exe", "gawk.exe",
    "git.exe", "esbuild.exe", "msbuild.exe", "vbcscompiler.exe", "npm.exe", "uv.exe", "uvx.exe", "func.exe",
    "testhost.exe", "vstest.console.exe",
}
NEVER_LIST = {"conhost.exe", "claude.exe"}
# Orphans topped by these need evidence that Claude Code started them (a session path, a worktree path, a marker).
WEAK_TOPS = {"cmd.exe", "powershell.exe", "pwsh.exe"}
CLAUDE_MARKER_RE = re.compile(r"shell-snapshots|CLAUDE_CODE_|[\\/]Temp[\\/]claude[\\/]|[\\/]\.claude[\\/]", re.I)

LEAK_PATTERNS = [
    # python reading its program from stdin (`python - <<EOF`); the stdin never arrives and it spins (G-22, G-26)
    ("python-stdin", re.compile(r"\bpython[0-9.]*(?:\.exe)?\"?\s+-(?:\s|$)", re.I)),
    # an `until`/`while` poll with a sleep: unbounded unless it has its own exit
    ("poll-loop", re.compile(r"\b(?:until|while)\b.*?;\s*do\b.*?\bsleep\b", re.I | re.S)),
    ("tail-follow", re.compile(r"\btail(?:\.exe)?\s+(?:-\w*f\b|--follow)", re.I)),
    # dotnet-format's MSBuild workspace host, left behind when dotnet format is killed
    ("format-buildhost", re.compile(r"BuildHost", re.I)),
]
DEPLOY_RE = re.compile(
    r"(?<![\w.-])(?:az|azd|pac|swa|func)(?:\.cmd|\.exe)?[\"']?\s+[a-z]"
    r"|(?:Deploy|Provision|Publish)-[\w-]+\.ps1|bff-deploy|dataverse-deploy|code-page-deploy", re.I)
BUILD_SERVER_RE = re.compile(r"[/-]nodemode:", re.I)
# How a Claude Code session starts a tool call (Bash tool, PowerShell tool). Any other child is an MCP server/helper.
TOOL_SHELL_RE = re.compile(r"shell-snapshots|CLAUDE_CODE_SHELL_LAUNCHER|-EncodedCommand", re.I)

ORDER = ["safe-to-stop", "likely-leaked", "check", "busy", "idle-leftover"]
LABEL = {"safe-to-stop": "SAFE TO STOP", "likely-leaked": "LIKELY LEAKED", "check": "CHECK WITH SESSION",
         "busy": "BUSY (protected)", "idle-leftover": "IDLE LEFTOVER"}
ACT_ON = ("safe-to-stop", "likely-leaked", "check")
STOPPABLE = ("safe-to-stop", "likely-leaked", "idle-leftover")


# ---------------------------------------------------------------------------------------------------------- snapshot

PS_SNAPSHOT = r"""
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$fmt = 'yyyy-MM-ddTHH:mm:ss.ffffffZ'
$props = 'ProcessId', 'ParentProcessId', 'Name', 'CommandLine', 'CreationDate', 'KernelModeTime', 'UserModeTime'
function Get-Snap {
  # WMI can fail one enumeration on a loaded machine (a process exits mid-query); retry before giving up.
  for ($i = 1; $i -le 3; $i++) {
    try {
      return @(Get-CimInstance -ClassName Win32_Process -Property $props | ForEach-Object {
        [pscustomobject]@{
          pid = [int]$_.ProcessId; ppid = [int]$_.ParentProcessId; name = $_.Name; cmd = $_.CommandLine
          created = if ($_.CreationDate) { $_.CreationDate.ToUniversalTime().ToString($fmt) } else { $null }
          cpu = [int64]$_.KernelModeTime + [int64]$_.UserModeTime
        } })
    } catch {
      if ($i -eq 3) { throw }
      Start-Sleep -Milliseconds 750
    }
  }
}
try {
  $t0 = (Get-Date).ToUniversalTime().ToString($fmt); $a = Get-Snap
  if (__SECONDS__ -gt 0) { Start-Sleep -Seconds __SECONDS__ }
  $t1 = (Get-Date).ToUniversalTime().ToString($fmt); $b = Get-Snap
  [pscustomobject]@{ t0 = $t0; t1 = $t1; first = $a; second = $b } | ConvertTo-Json -Depth 4 -Compress
} catch {
  [Console]::Error.WriteLine("Win32_Process query failed: " + $_.Exception.Message)
  exit 1
}
"""


def take_snapshot(seconds):
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if not shell:
        raise RuntimeError("PowerShell not found; this scanner reads Win32_Process and runs on Windows only")
    script = PS_SNAPSHOT.replace("__SECONDS__", str(int(seconds)))
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    r = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       timeout=SNAPSHOT_TIMEOUT, creationflags=flags, stdin=subprocess.DEVNULL)
    if r.returncode != 0:
        raise RuntimeError("process snapshot failed: " + r.stderr.strip()[:500])
    return json.loads(r.stdout)


def parse_time(s):
    return dt.datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()


def iso_now():
    return dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


# ------------------------------------------------------------------------------------------------------------ model

class Proc:
    __slots__ = ("pid", "ppid", "name", "cmd", "created", "created_raw", "cpu_s", "age", "cur", "life", "key")

    def __init__(self, raw, now):
        self.pid = int(raw["pid"])
        self.ppid = int(raw.get("ppid") or 0)
        self.name = (raw.get("name") or "").lower()
        self.cmd = raw.get("cmd") or ""
        self.created_raw = raw.get("created")
        self.created = parse_time(raw["created"]) if raw.get("created") else 0.0
        self.cpu_s = int(raw.get("cpu") or 0) / 1e7
        self.age = max(now - self.created, 0.0) if self.created else 0.0
        self.life = self.cpu_s / self.age if self.age >= 1 else 0.0
        self.cur = 0.0
        self.key = "%d@%s" % (self.pid, self.created_raw)


def is_parent(par, child):
    """par is child's real parent: it exists, is not child itself, and is not a later process that reused the PID."""
    return (par is not None and par.pid != child.pid
            and not (par.created and child.created and par.created > child.created))


def build_procs(snapshot):
    t0, now = parse_time(snapshot["t0"]), parse_time(snapshot["t1"])
    first = {"%s@%s" % (r["pid"], r.get("created")): int(r.get("cpu") or 0) / 1e7 for r in snapshot.get("first") or []}
    procs = {}
    for raw in snapshot.get("second") or []:
        p = Proc(raw, now)
        if p.key in first and now > t0:
            p.cur = max(p.cpu_s - first[p.key], 0.0) / (now - t0)
        procs[p.pid] = p
    children = {}
    for p in procs.values():
        par = procs.get(p.ppid)
        if is_parent(par, p):
            children.setdefault(par.pid, []).append(p)
    return procs, children, now


def subtree(root, children):
    """root and every live descendant, children before their parents (the order to stop them in)."""
    out, seen, stack = [], set(), [(root, False)]
    while stack:
        p, expanded = stack.pop()
        if expanded:
            out.append(p)
            continue
        if p.pid in seen:
            continue
        seen.add(p.pid)
        stack.append((p, True))
        stack.extend((c, False) for c in children.get(p.pid, []))
    return out


def is_claude_session(p):
    return p.name == "claude.exe" and "\\windowsapps\\" not in p.cmd.lower() and "--type=" not in p.cmd


def is_claude_app(p):
    return p.name == "claude.exe" and not is_claude_session(p)


def ancestors(p, procs):
    """Walk up. Returns (chain nearest-first, end): end is the owning claude session Proc, 'app' (the desktop app's
    own process tree) or 'top' (the last process in chain, or p itself, has no live parent)."""
    chain, cur, seen = [], p, {p.pid}
    while True:
        par = procs.get(cur.ppid)
        if not is_parent(par, cur) or par.pid in seen:
            return chain, "top"
        if is_claude_session(par):
            return chain, par
        if is_claude_app(par):
            return chain, "app"
        chain.append(par)
        seen.add(par.pid)
        cur = par


def norm_path(path):
    return path.replace("/", "\\").rstrip("\\").lower()


def same_tree(a, b):
    return a == b or a.startswith(b + "\\") or b.startswith(a + "\\")


def first_cwd(path):
    """The folder the session started in (its worktree). Later `cwd` values follow every `cd` the session made."""
    try:
        with open(path, "rb") as f:
            head = f.read(262144).decode("utf-8", errors="replace")
        m = re.search(r'"cwd"\s*:\s*"((?:[^"\\]|\\.)*)"', head)
        return json.loads('"%s"' % m.group(1)) if m else None
    except (OSError, ValueError):
        return None


def mtime(path):
    try:
        return os.path.getmtime(path)
    except OSError:
        return None


def session_info(sid, dirs, now, memo):
    """cwd from the session transcript; idle_s from the newest of its transcript, subagent transcripts and
    background-task output (a session waiting on a background task or subagent writes only those)."""
    if sid in memo:
        return memo[sid]
    projects_dir, tasks_dir = dirs
    main = glob.glob(os.path.join(glob.escape(projects_dir), "*", sid + ".jsonl"))
    files = main + glob.glob(os.path.join(glob.escape(projects_dir), "*", sid, "**", "*.jsonl"), recursive=True)
    if tasks_dir:
        files += glob.glob(os.path.join(glob.escape(tasks_dir), "*", sid, "tasks", "*"))
    times = [t for t in (mtime(f) for f in files) if t is not None]
    info = {"id": sid, "cwd": first_cwd(max(main, key=lambda f: mtime(f) or 0)) if main else None,
            "idle_s": max(now - max(times), 0.0) if times else None}
    memo[sid] = info
    return info


def is_active(info):
    return info["idle_s"] is not None and info["idle_s"] < SESSION_IDLE


def hint_session(cmds):
    return next((m.group(1).lower() for c in cmds for m in [SESSION_PATH_RE.search(c)] if m), None)


def leak_patterns(p, chain):
    cmds = [p.cmd] + [a.cmd for a in chain]
    hits = [name for name, rx in LEAK_PATTERNS if any(rx.search(c) for c in cmds)]
    if p.name == "cat.exe":
        hits.append("cat-waiting")   # a cat alive for minutes is waiting on stdin or a pipe nobody writes
    return hits


def protected_tags(p, chain, helper=False):
    tags = []
    if any(DEPLOY_RE.search(c) for c in [p.cmd] + [a.cmd for a in chain]):
        tags.append("deploy")
    if p.name == "git.exe":
        tags.append("git")
    if p.name == "vbcscompiler.exe" or BUILD_SERVER_RE.search(p.cmd):
        tags.append("build-server")
    if helper:
        tags.append("session-helper")
    return tags


def is_spinning(p, hot_since, now):
    if p.age < SPIN_MIN_AGE or p.cur < SPIN_CORE:
        return False
    return p.life >= SPIN_CORE or (hot_since is not None and now - hot_since >= SPIN_MIN_AGE)


def classify(snapshot, projects_dir, previous_hot, exclude_root=None, tasks_dir=None):
    """Pure: snapshot + session folders + previous hot map -> report dict."""
    procs, children, now = build_procs(snapshot)
    dirs = (projects_dir, tasks_dir)
    excluded = {p.pid for p in subtree(procs[exclude_root], children)} if exclude_root in procs else set()
    hot = {p.key: previous_hot.get(p.key, now) for p in procs.values() if p.cur >= SPIN_CORE}
    memo = {}

    # Pass 1: every process's owner chain, and the id of every live session.
    owners = {p.pid: ancestors(p, procs) for p in procs.values()}
    session_sid = {}
    for p in procs.values():
        m = RESUME_RE.search(p.cmd) if is_claude_session(p) else None
        if m:
            session_sid[p.pid] = m.group(1).lower()
    for p in procs.values():                      # a new session has no --resume; its children's paths name it
        chain, end = owners[p.pid]
        if isinstance(end, Proc) and end.pid not in session_sid:
            sid = hint_session([p.cmd] + [a.cmd for a in chain])
            if sid:
                session_sid[end.pid] = sid
    live_sids = set(session_sid.values())
    live_cwds = []
    for sid in live_sids:
        info = session_info(sid, dirs, now, memo)
        if info["cwd"]:
            live_cwds.append((norm_path(info["cwd"]), sid))

    rows, links = {}, {}
    for p in procs.values():
        if p.pid in excluded or p.name in NEVER_LIST:
            continue
        chain, end = owners[p.pid]
        cmds = [p.cmd] + [a.cmd for a in chain]
        hint_sid = hint_session(cmds)
        hint_wt = next((m.group(1) for c in cmds for m in [WORKTREE_RE.search(c)] if m), None)
        top = chain[-1] if chain else p
        helper = False
        if isinstance(end, Proc):
            helper = not TOOL_SHELL_RE.search(top.cmd)
            owner = {"kind": "session", "claude_pid": end.pid}
            sids = [session_sid[end.pid]] if end.pid in session_sid else []
        elif end == "top":
            if top.name not in DEV_TOOLS:
                continue                                 # explorer, services, a user terminal: not ours
            # Windows and vendor software also start cmd/PowerShell children and exit (e.g. a driver helper).
            if (top.name in WEAK_TOPS and not hint_sid and not hint_wt
                    and not any(CLAUDE_MARKER_RE.search(c) for c in cmds)):
                continue
            owner = {"kind": "orphan"}
            sids = [hint_sid] if hint_sid else []
            if hint_wt:
                sids += [s for cwd, s in live_cwds if same_tree(cwd, norm_path(hint_wt)) and s not in sids]
        else:
            continue                                     # the desktop app's own processes

        infos = [session_info(s, dirs, now, memo) for s in sids]
        active = any(is_active(i) for i in infos)
        linked_live = owner["kind"] == "session" or any(s in live_sids for s in sids)
        if infos:
            shown = min(infos, key=lambda i: i["idle_s"] if i["idle_s"] is not None else float("inf"))
            owner.update(session_id=shown["id"], cwd=shown["cwd"], idle_s=shown["idle_s"])
            if len(infos) > 1:
                owner["other_sessions"] = [i["id"] for i in infos if i is not shown]
        owner["live"] = linked_live
        owner["worktree_hint"] = hint_wt
        links[p.pid] = (active, linked_live)          # kept for every process, flagged or not (tree pass below)
        unowned = not active and not linked_live

        spinning = is_spinning(p, hot.get(p.key), now)
        tags = protected_tags(p, chain, helper)
        leaks = leak_patterns(p, chain)
        verdict = None
        if spinning:
            if tags or active:
                verdict = "busy"
            elif unowned:
                verdict = "safe-to-stop" if leaks else "likely-leaked"
            else:
                verdict = "check"
        elif (p.cur < IDLE_CORE and p.age >= LEFTOVER_AGE and unowned and not tags and p.name in DEV_TOOLS):
            verdict = "idle-leftover"
        if verdict is None:
            continue

        reasons = []
        if active:
            reasons.append("its session is active")
        elif linked_live and verdict == "check":
            reasons.append("its session is alive (it may be waiting on this)")
        reasons += ["protected: " + t for t in tags]
        reasons += ["leak pattern: " + l for l in leaks]
        entry = rows.setdefault(top.pid, {
            "root_pid": top.pid, "root_name": top.name, "root_created": top.created_raw,
            "root_cmd": top.cmd[:400], "owner": owner, "processes": [], "verdict": verdict, "protected": [],
        })
        entry["processes"].append({
            "pid": p.pid, "name": p.name, "cmd": p.cmd[:400], "age_h": round(p.age / 3600, 1),
            "core_now": round(p.cur, 2), "core_avg": round(p.life, 2), "cpu_h": round(p.cpu_s / 3600, 1),
            "verdict": verdict, "reasons": reasons})
        if ORDER.index(verdict) < ORDER.index(entry["verdict"]):
            entry["verdict"] = verdict

    entries = list(rows.values())
    for e in entries:
        # Stopping the root stops its whole live tree, so judge the whole tree, not just the flagged members.
        tree = subtree(procs[e["root_pid"]], children)
        e["tree_pids"] = [m.pid for m in tree]
        flagged = {m["pid"] for m in e["processes"]}
        tree_live = False
        for m in tree:
            if m.name == "claude.exe":
                e["protected"].append("%s %d: a Claude session runs inside this tree" % (m.name, m.pid))
                continue
            tags = [t for t in protected_tags(m, owners[m.pid][0]) if t != "session-helper"]
            if tags:
                e["protected"].append("%s %d: %s" % (m.name, m.pid, ", ".join(tags)))
            # A quiet member (below the CPU or age thresholds, so not flagged) still belongs to its session.
            m_active, m_live = links.get(m.pid, (False, False))
            if m_active and m.pid not in flagged:
                e["protected"].append("%s %d: its session is active" % (m.name, m.pid))
            tree_live = tree_live or m_live
        # The most restrictive member decides: one process linked to a live session makes the whole tree `check`.
        member_verdicts = {m["verdict"] for m in e["processes"]}
        if e["protected"] or "busy" in member_verdicts:
            e["verdict"] = "busy"
        elif "check" in member_verdicts or tree_live:
            e["verdict"] = "check"
        e["processes"].sort(key=lambda m: -m["core_now"])
    entries.sort(key=lambda e: (ORDER.index(e["verdict"]), -sum(m["core_now"] for m in e["processes"])))
    total = sum(p.cur for p in procs.values() if p.pid != 0)    # pid 0 is the System Idle Process
    return {
        "scanned_at": snapshot["t1"], "cores": os.cpu_count() or 1, "cores_busy": round(total, 1),
        "entries": entries, "hot": hot,
    }


# ----------------------------------------------------------------------------------------------------------- output

def session_text(o):
    idle = o.get("idle_s")
    state = ("no session files found" if idle is None else
             "active" if idle < SESSION_IDLE else
             "idle %d min" % (idle // 60) if idle < 3 * 3600 else "idle %.0f h" % (idle / 3600))
    alive = "" if o.get("live") or o["kind"] == "session" else ", ended"
    return "session %s (%s%s)" % (o["session_id"][:8], state, alive)


def owner_text(o):
    if o["kind"] == "orphan":
        s = "orphan (the process that started it has exited)"
        if o.get("session_id"):
            s += ", linked to " + session_text(o)
            if o.get("other_sessions"):
                s += " and %d other session(s) in the same worktree" % len(o["other_sessions"])
    elif o.get("session_id"):
        s = session_text(o)
    else:
        s = "live Claude session (pid %d, session id unknown)" % o["claude_pid"]
    where = o.get("cwd") or o.get("worktree_hint")
    if where:
        s += "; " + where
    return s


def report_time(report):
    """'13:40' for a fresh report, '2026-11-24 13:40, 50 h old' for a stale one."""
    at = parse_time(report["scanned_at"])
    age = time.time() - at
    local = dt.datetime.fromtimestamp(at)
    if age <= STALE_REPORT:
        return local.strftime("%H:%M"), False
    return "%s, %.0f %s old" % (local.strftime("%Y-%m-%d %H:%M"), age / 3600 if age >= 3600 else age / 60,
                                "h" if age >= 3600 else "min"), True


def render(report):
    when, _ = report_time(report)
    head = "Runaway process scan %s: processes using %.1f of %d cores." % (when, report["cores_busy"], report["cores"])
    if not report["entries"]:
        return head + "\nNothing flagged."
    counts = {v: sum(1 for e in report["entries"] if e["verdict"] == v) for v in ORDER}
    lines = [head, ", ".join("%d %s" % (counts[v], LABEL[v].lower()) for v in ORDER if counts[v]), ""]
    for e in report["entries"]:
        lines.append("%s  pid %d %s — %s" % (LABEL[e["verdict"]], e["root_pid"], e["root_name"], owner_text(e["owner"])))
        for m in e["processes"][:6]:
            lines.append("    pid %-6d %-18s %4.2f core now, %4.2f avg, %5.1f h old  %s" % (
                m["pid"], m["name"], m["core_now"], m["core_avg"], m["age_h"],
                ("(" + "; ".join(m["reasons"]) + ")") if m["reasons"] else ""))
            lines.append("      " + (m["cmd"][:160] or "(no command line)"))
        if len(e["processes"]) > 6:
            lines.append("    ... %d more flagged in this tree" % (len(e["processes"]) - 6))
        for p in e["protected"][:4]:
            lines.append("    protected in tree: " + p)
        if e["verdict"] in STOPPABLE:
            lines.append("    stop the tree (%d processes): python scripts/quality/runaway-processes.py kill %d"
                         % (len(e.get("tree_pids", [])), e["root_pid"]))
        elif e["verdict"] == "check":
            lines.append("    ask the owning session or the user first; then: "
                         "python scripts/quality/runaway-processes.py kill --force %d" % e["root_pid"])
        elif any("build-server" in r for r in e["protected"]) or any(
                "build-server" in r for m in e["processes"] for r in m["reasons"]):
            lines.append("    build server: when no build is running, `dotnet build-server shutdown` stops it cleanly")
        lines.append("")
    return "\n".join(lines).rstrip()


def write_cache(path, report):
    tmp = "%s.%d.tmp" % (path, os.getpid())
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False)
        os.replace(tmp, path)
    finally:
        try:
            os.remove(tmp)
        except OSError:
            pass


def read_cache(path):
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else None
    except (OSError, ValueError):
        return None


# --------------------------------------------------------------------------------------------------------- commands

def load_snapshot(args, seconds):
    if args.snapshot:
        with open(args.snapshot, encoding="utf-8") as f:
            return json.load(f)
    return take_snapshot(seconds)


def cmd_scan(args):
    lock = args.cache + ".lock"
    try:
        snap = load_snapshot(args, SAMPLE_SECONDS)
        previous = read_cache(args.cache) or {}
        report = classify(snap, args.projects_dir, previous.get("hot") or {},
                          exclude_root=None if args.snapshot else os.getpid(), tasks_dir=args.tasks_dir)
        write_cache(args.cache, report)
    except Exception as e:
        failed = read_cache(args.cache) or {}
        failed.update(error=str(e)[:300], failed_at=iso_now())
        write_cache(args.cache, failed)
        print("runaway-processes: scan failed: %s" % e, file=sys.stderr)
        return 1
    finally:
        if args.background:
            try:
                os.remove(lock)
            except OSError:
                pass
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    elif not args.background:
        sys.stdout.reconfigure(encoding="utf-8")
        print(render(report))
    return 0


def cmd_kill(args):
    """Scan again, then stop only trees that are still stoppable. Every PID is named; nothing unexamined is stopped."""
    try:
        snap = load_snapshot(args, KILL_SAMPLE_SECONDS)
    except Exception as e:
        print("Could not read the process list (%s); nothing stopped." % e)
        return 1
    previous = read_cache(args.cache) or {}
    report = classify(snap, args.projects_dir, previous.get("hot") or {},
                      exclude_root=None if args.snapshot else os.getpid(), tasks_dir=args.tasks_dir)
    procs, children, _ = build_procs(snap)
    mine, cur = {os.getpid()}, procs.get(os.getpid())
    while cur is not None and is_parent(procs.get(cur.ppid), cur) and cur.ppid not in mine:
        mine.add(cur.ppid)
        cur = procs[cur.ppid]
    entries = {e["root_pid"]: e for e in report["entries"]}
    rc = 0
    for pid in args.pids:
        live = procs.get(pid)
        if live is None:
            print("pid %d: already gone." % pid)
            continue
        tree = subtree(live, children)
        if any(m.pid in mine or m.name == "claude.exe" for m in tree):
            print("pid %d: refused — a Claude session or this command's own shell is in its tree." % pid)
            rc = 1
            continue
        e = entries.get(pid)
        if e is None:                                   # --force overrides a verdict, never a missing one
            print("pid %d %s: refused — a fresh scan does not list it as the top of a flagged tree." % (pid, live.name))
            rc = 1
            continue
        if e["verdict"] not in STOPPABLE and not args.force:
            why = sorted({r for m in e["processes"] for r in m["reasons"]} | set(e["protected"]))
            print("pid %d %s: refused — %s (%s). Pass --force only with the user's yes."
                  % (pid, live.name, LABEL[e["verdict"]].lower(), "; ".join(why)))
            rc = 1
            continue
        cmd = ["taskkill"]
        for m in tree:
            cmd += ["/PID", str(m.pid)]
        cmd.append("/F")
        r = subprocess.run(cmd, capture_output=True, text=True, errors="replace", timeout=60)
        print("pid %d %s (%d processes): %s" % (pid, live.name, len(tree),
                                                (r.stdout or r.stderr).strip().replace("\n", " | ")))
        if r.returncode != 0:
            rc = 1
        report["entries"] = [x for x in report["entries"] if x["root_pid"] != pid]
    if not args.snapshot:
        write_cache(args.cache, report)
    return rc


def start_background_scan(cache):
    lock = cache + ".lock"
    try:
        if os.path.exists(lock) and time.time() - os.path.getmtime(lock) >= LOCK_STALE:
            os.remove(lock)
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)   # only one scan at a time
        os.write(fd, str(os.getpid()).encode())
        os.close(fd)
    except OSError:
        return False
    flags = 0
    if os.name == "nt":
        flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW
    subprocess.Popen([sys.executable, SELF, "--cache", cache, "scan", "--background"],
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=flags, close_fds=True)
    return True


def banner_message(report):
    lines = []
    failed_at = report.get("failed_at")
    if report.get("error") and (not report.get("scanned_at") or failed_at > report["scanned_at"]):
        lines.append("⚠️ Runaway-process scan failed at %s: %s" % (
            dt.datetime.fromtimestamp(parse_time(failed_at)).strftime("%Y-%m-%d %H:%M"), report["error"]))
    if report.get("scanned_at"):
        act = [e for e in report.get("entries", []) if e["verdict"] in ACT_ON]
        leftovers = [e for e in report.get("entries", []) if e["verdict"] == "idle-leftover"]
        if act or leftovers:
            when, stale = report_time(report)
            parts = ["%d %s" % (n, LABEL[v].lower())
                     for v in ACT_ON for n in [sum(1 for e in act if e["verdict"] == v)] if n]
            if leftovers:
                parts.append("%d idle leftover(s)" % len(leftovers))
            lines.append("⚠️ Runaway processes (scan %s, %.1f of %d cores busy): %s" % (
                when, report["cores_busy"], report["cores"], ", ".join(parts)))
            for e in act[:5]:
                core = sum(m["core_now"] for m in e["processes"])
                age = max(m["age_h"] for m in e["processes"])
                lines.append("  pid %d %s — %.1f core, up to %.1f h — %s — %s" % (
                    e["root_pid"], e["root_name"], core, age, owner_text(e["owner"]), LABEL[e["verdict"]].lower()))
            if stale:
                lines.append("  (stale: PIDs may have changed; rescan before acting)")
    if not lines:
        return None
    lines.append("Details: python scripts/quality/runaway-processes.py")
    return "\n".join(lines)


def cmd_banner(args):
    try:
        report = read_cache(args.cache)
        stale = report is None or time.time() - (mtime(args.cache) or 0) > REFRESH_AFTER
        if stale and not args.no_refresh and os.name == "nt":
            start_background_scan(args.cache)
        if report is None:
            return 0
        msg = banner_message(report)
        if not msg:
            return 0
        out = {
            "systemMessage": msg,
            "hookSpecificOutput": {
                "hookEventName": "SessionStart",
                "additionalContext": msg + "\nThese processes were started by Claude Code sessions and are not this "
                "session's work. Mention them to the user at a natural point. Stop one only after the user says "
                "yes, with `python scripts/quality/runaway-processes.py kill <pid>` (it rescans and refuses anything "
                "no longer stoppable). A 'check with session' entry belongs to a live session: ask the user, and "
                "stop it only with `kill --force <pid>` after their yes.",
            },
        }
        sys.stdout.reconfigure(encoding="utf-8")
        print(json.dumps(out, ensure_ascii=False))
    except Exception as e:  # never fail the session
        print("runaway-processes banner: %s" % e, file=sys.stderr)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--cache", default=DEFAULT_CACHE)
    ap.add_argument("--projects-dir", default=DEFAULT_PROJECTS)
    ap.add_argument("--tasks-dir", default=DEFAULT_TASKS)
    ap.add_argument("--snapshot", help=argparse.SUPPRESS)
    ap.add_argument("--json", action="store_true", help=argparse.SUPPRESS)
    sub = ap.add_subparsers(dest="command")
    scan = sub.add_parser("scan", help="scan now and print the report (default)")
    scan.add_argument("--json", action="store_true", default=argparse.SUPPRESS, help="print JSON")
    scan.add_argument("--background", action="store_true", help=argparse.SUPPRESS)
    kill = sub.add_parser("kill", help="stop listed process trees")
    kill.add_argument("pids", nargs="+", type=int)
    kill.add_argument("--force", action="store_true",
                      help="also stop a listed check/busy tree (never an unlisted PID, a claude.exe or your own shell)")
    banner = sub.add_parser("banner", help="SessionStart hook")
    banner.add_argument("--no-refresh", action="store_true")
    args = ap.parse_args(argv)
    if args.command == "kill":
        return cmd_kill(args)
    if args.command == "banner":
        return cmd_banner(args)
    args.background = getattr(args, "background", False)
    return cmd_scan(args)


if __name__ == "__main__":
    sys.exit(main())
