#!/usr/bin/env python
"""PreToolUse hook: do not run `python3` when it is the Microsoft Store alias stub.

On Windows, a Python.org install provides `python.exe` and `py.exe` but no `python3.exe`, so `python3` resolves to
`%LOCALAPPDATA%\\Microsoft\\WindowsApps\\python3.exe`, an App Execution Alias stub. The stub does not reliably
forward standard input: a script fed on stdin (`python3 - <<EOF`, `python3 - file ...`) never reaches the real
interpreter, which then sits at 100% CPU. That leaked processes for days (2026-10-13) and for 76 minutes in another
session (2026-10-15); see .claude/FAILURE-MODES.md G-22. `python` resolves to the real interpreter and is a drop-in
replacement.

This hook denies a Bash or PowerShell command that runs `python3` (or `python3.exe`) in command position, and only
when `python3` on this machine resolves into a `WindowsApps` folder. Where `python3` is a real interpreter (Linux,
macOS, a Windows machine with a python3 shim) it does nothing. Text that is not executed is ignored: quoted strings,
heredoc and here-string bodies, comments, and arguments (`grep python3 x`, `git commit -m "... python3 ..."`).

Known limits (misses, never false denies): `python3` inside a quoted `bash -c "..."` / `pwsh -Command "..."` string
or a script file; behind a prefix word with options (`env -i python3`, `xargs -I{} python3`); and after a `<<WORD`
that appears inside a single-line quoted string, which is read as a heredoc start. One false-deny shape remains:
double quotes nested inside `"$(printf "...")"` with `python3` at the start of an inner line.
Fails open: unreadable input or any internal error lets the call through silently. The test seam
SPRK_PYTHON3_RESOLVES_TO overrides the resolved path ("" = not found). Enforced by: this hook; controls in
scripts/quality/tests/test_guard_python3_stub.py.
"""
import json
import os
import re
import shutil
import sys

# A command word may be preceded by these wrappers (each optionally with one non-option argument for timeout).
PREFIX_WORDS = r"(?:(?:then|do|else|elif|time|exec|nohup|command|env|xargs|timeout\s+\S+)\s+)*"
ENV_ASSIGN = r"(?:[A-Za-z_][A-Za-z0-9_]*=\S*\s+)*"
# Start of a simple command: line start, or after ; & | ( ` { $( and PowerShell's call operator `&`.
COMMAND_START = r"(?:^|[;&|(`{\n]|\$\()\s*(?:&\s*)?"
PY3_WORD = r"python3(?:\.exe)?(?=$|[\s;&|)`])"
PY3_AT_COMMAND = re.compile(COMMAND_START + ENV_ASSIGN + PREFIX_WORDS + PY3_WORD, re.IGNORECASE | re.MULTILINE)
# PowerShell only: the right side of an assignment runs as a command (`$v = python3 -V`). In Bash, `x=python3`
# assigns a string, so this form is not checked there.
PS_ASSIGN_PY3 = re.compile(r"\$[A-Za-z_][\w:]*\s*=\s*(?:&\s*)?" + PY3_WORD, re.IGNORECASE)


def strip_unexecuted(command):
    """Remove text that is data, not commands: heredoc / here-string bodies, quoted strings, comments."""
    text = command
    # Bash heredoc bodies: keep the introducing line, drop everything up to the terminator line.
    out_lines, terminator = [], None
    for line in text.split("\n"):
        if terminator is not None:
            if line.strip() == terminator:
                terminator = None
            continue
        out_lines.append(line)
        m = re.search(r"<<-?\s*(['\"]?)([A-Za-z_][A-Za-z0-9_]*)\1", line)
        if m:
            terminator = m.group(2)
    text = "\n".join(out_lines)
    # PowerShell here-strings.
    text = re.sub(r"@'\r?\n.*?\r?\n'@", "''", text, flags=re.DOTALL)
    text = re.sub(r'@"\r?\n.*?\r?\n"@', '""', text, flags=re.DOTALL)
    # Quoted strings, in one pass so the leftmost quote wins, and spanning lines (a multi-line `git commit -m "..."`
    # or `--body "..."` is common). Single quotes have no escapes; double quotes allow backslash escapes.
    text = re.sub(r"'[^']*'|\"(?:\\.|[^\"\\])*\"", lambda m: m.group(0)[0] * 2, text, flags=re.DOTALL)
    # PowerShell block comments, then line comments (a # at line start or after whitespace, to end of line).
    text = re.sub(r"<#.*?#>", "", text, flags=re.DOTALL)
    text = re.sub(r"(^|\s)#[^\n]*", r"\1", text)
    return text


def python3_is_store_stub():
    override = os.environ.get("SPRK_PYTHON3_RESOLVES_TO")
    path = override if override is not None else (shutil.which("python3") or "")
    return path, bool(path) and "windowsapps" in path.replace("\\", "/").lower().split("/")


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if event.get("tool_name") not in ("Bash", "PowerShell"):
            return 0
        command = (event.get("tool_input") or {}).get("command")
        if not isinstance(command, str) or "python3" not in command.lower():
            return 0
        code = strip_unexecuted(command)
        hit = PY3_AT_COMMAND.search(code) or (event.get("tool_name") == "PowerShell" and PS_ASSIGN_PY3.search(code))
        if not hit:
            return 0
        stub_path, is_stub = python3_is_store_stub()
        if not is_stub:
            return 0
        real = shutil.which("python") or "python"
        reason = ("`python3` on this machine is the Microsoft Store alias stub (%s). It hangs on scripts passed on "
                  "stdin (`python3 - <<EOF`) and has left processes at 100%% CPU for hours. Use `python` instead "
                  "(%s): same command, with `python3` replaced by `python`. See .claude/FAILURE-MODES.md G-22."
                  % (stub_path, real))
    except Exception:
        return 0
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
