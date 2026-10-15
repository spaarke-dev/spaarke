"""Shared helpers for the scripts/ai-cost tests.

The scripts have hyphens in their file names (repo convention), so they are loaded by path.
Run all tests:  python -m unittest discover -s scripts/ai-cost/tests
"""
import importlib.util
import json
import os
import sys

AI_COST = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load(filename, modname):
    spec = importlib.util.spec_from_file_location(modname, os.path.join(AI_COST, filename))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def script(filename):
    return os.path.join(AI_COST, filename)


def call_line(ts, msg_id, model="claude-sonnet-5", inp=1_000_000, out=100_000, req=None):
    """One assistant transcript line. With sonnet-5 pricing (2/10 per Mtok) the defaults cost $3.00."""
    return json.dumps({
        "type": "assistant", "timestamp": ts, "requestId": req or "req-" + msg_id,
        "message": {"id": msg_id, "model": model,
                    "usage": {"input_tokens": inp, "output_tokens": out}},
    })


def write_transcript(root, folder, name, lines, mtime=None):
    d = os.path.join(root, folder)
    os.makedirs(d, exist_ok=True)
    p = os.path.join(d, name)
    with open(p, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    if mtime is not None:
        os.utime(p, (mtime, mtime))
    return p


def run_cli(script_name, args, home, timeout=60):
    """Run a script as a subprocess with ~ pointing at `home` (so ~/.claude is a sandbox)."""
    import subprocess
    env = dict(os.environ, USERPROFILE=home, HOME=home)
    return subprocess.run([sys.executable, script(script_name)] + list(args),
                          capture_output=True, text=True, encoding="utf-8", env=env, timeout=timeout)
