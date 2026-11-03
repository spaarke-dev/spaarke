"""probe.py - report what the sandbox can run, before a skill relies on it.

Python 3 standard library only. Every skill runs it first and stops if a required item is missing.

Usage:
  python probe.py [--require lxml,docx] [--write-test DIR]

Output (JSON on stdout):
  {"python": "3.12.3", "platform": "...", "modules": {"lxml": "5.2.1", "docx": null, ...},
   "tools": {"soffice": "/usr/bin/soffice", "pdftoppm": null}, "write_test": {"dir": ..., "ok": true},
   "missing_required": [], "ok": true}
Exit codes: 0 every required item present, 1 something required is missing.
"""
import argparse
import importlib
import json
import os
import platform
import shutil
import sys
import tempfile

MODULES = ["lxml", "docx", "openpyxl", "yaml", "PIL", "pypdf"]
TOOLS = ["soffice", "libreoffice", "pdftoppm", "pandoc"]


def module_version(name):
    try:
        m = importlib.import_module(name)
    except Exception:  # any import failure means "not usable here"
        return None
    return str(getattr(m, "__version__", None) or getattr(m, "VERSION", None) or "present")


def write_test(directory):
    try:
        os.makedirs(directory, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=directory, delete=True) as f:
            f.write(b"probe")
        return {"dir": os.path.abspath(directory), "ok": True}
    except OSError as ex:
        return {"dir": directory, "ok": False, "error": str(ex)}


def probe(require=(), write_dir=None):
    result = {
        "python": platform.python_version(),
        "platform": platform.platform(),
        "cwd": os.getcwd(),
        "modules": {m: module_version(m) for m in MODULES},
        "tools": {t: shutil.which(t) for t in TOOLS},
    }
    have = {k for k, v in result["modules"].items() if v} | {k for k, v in result["tools"].items() if v}
    result["missing_required"] = [r for r in require if r not in have]
    if write_dir:
        result["write_test"] = write_test(write_dir)
        if not result["write_test"]["ok"]:
            result["missing_required"].append(f"writable:{write_dir}")
    if sys.version_info < (3, 8):
        result["missing_required"].append("python>=3.8")
    result["ok"] = not result["missing_required"]
    return result


def main(argv=None):
    ap = argparse.ArgumentParser(description="Report the sandbox's Python, libraries and tools.")
    ap.add_argument("--require", default="lxml", help="comma-separated modules/tools that must be present")
    ap.add_argument("--write-test", metavar="DIR", help="check that DIR is writable")
    a = ap.parse_args(argv)
    req = [r.strip() for r in a.require.split(",") if r.strip()]
    r = probe(req, a.write_test)
    print(json.dumps(r, indent=1))
    return 0 if r["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
