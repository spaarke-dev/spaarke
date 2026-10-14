"""SessionStart hook (matchers: startup, resume): show open quality alerts at the start of every session.

Quality alerts are GitHub issues labelled `quality-alert`. The nightly workflows open and auto-close them
through .github/actions/quality-alert (docs/procedures/ci-cd-workflow.md, "Quality alerts"). An open alert
means a nightly check is failing now. This banner puts that in front of the owner and the agent at session
start, so nobody has to remember to look at the issue list.

Speed: hooks must stay well under 5 s, so this never waits on the network. It reads a cache file and, when
the cache is older than 30 minutes, starts a detached `gh` refresh whose result the NEXT session shows.
It never fails the session: every path exits 0 and errors go to stderr.

Test: --cache <path> points at a fixture; --no-refresh skips the background refresh.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

REFRESH_AFTER_SECONDS = 30 * 60
DEFAULT_CACHE = os.path.join(tempfile.gettempdir(), "spaarke-quality-alerts.json")

# Runs detached. Writes to a per-process temp file and renames it, so a reader never sees a half-written
# cache, concurrent refreshes cannot collide, and a failed `gh` call leaves the previous cache in place.
# gh emits UTF-8, so decode it explicitly (the console code page would turn emoji into mojibake) and give the
# gh child CREATE_NO_WINDOW (the detached parent has no console, so Windows would otherwise open one).
REFRESH_SCRIPT = r"""
import os, shutil, subprocess, sys
cache = sys.argv[1]
tmp = "%s.%d.tmp" % (cache, os.getpid())
flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
try:
    r = subprocess.run(
        [shutil.which("gh") or "gh", "issue", "list", "--repo", "spaarke-dev/spaarke", "--label", "quality-alert",
         "--state", "open", "--json", "number,title,updatedAt"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60, creationflags=flags)
    if r.returncode == 0:
        with open(tmp, "w", encoding="utf-8", newline="") as f:
            f.write(r.stdout)
        os.replace(tmp, cache)
finally:
    try:
        os.remove(tmp)
    except OSError:
        pass
"""


def start_refresh(cache: str) -> None:
    if shutil.which("gh") is None:
        return
    flags = 0
    if os.name == "nt":
        flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    subprocess.Popen(
        [sys.executable, "-c", REFRESH_SCRIPT, cache],
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        creationflags=flags, close_fds=True,
    )


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default=DEFAULT_CACHE)
    ap.add_argument("--no-refresh", action="store_true")
    args = ap.parse_args()

    try:
        stale = not os.path.exists(args.cache) or time.time() - os.path.getmtime(args.cache) > REFRESH_AFTER_SECONDS
        if stale and not args.no_refresh:
            start_refresh(args.cache)

        if not os.path.exists(args.cache):
            return
        with open(args.cache, encoding="utf-8") as f:
            alerts = json.load(f)
        if not alerts:
            return

        as_of = time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(args.cache)))
        lines = [f"  #{a['number']} {a['title']}" for a in sorted(alerts, key=lambda a: a["number"])]
        msg = (f"⚠️ {len(alerts)} open quality alert(s) (as of {as_of}):\n" + "\n".join(lines)
               + "\nList: gh issue list --label quality-alert")
        out = {
            "systemMessage": msg,
            "hookSpecificOutput": {
                "hookEventName": "SessionStart",
                "additionalContext": msg + "\nThese are failing nightly quality checks. Mention them to the "
                                           "user at a natural point; do not start fixing them unless asked.",
            },
        }
        sys.stdout.reconfigure(encoding="utf-8")
        print(json.dumps(out, ensure_ascii=False))
    except Exception as e:  # never fail the session
        print(f"quality-alert-banner: {e}", file=sys.stderr)


if __name__ == "__main__":
    main()
    sys.exit(0)
