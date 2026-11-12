#!/usr/bin/env python3
"""Summarise Stryker.NET mutation reports for .github/workflows/mutation-nightly.yml.

Reads one or more mutation-testing-elements JSON reports (Stryker's `json` reporter),
prints a per-file table (killed / survived / no-coverage / score) and every surviving
or uncovered mutant with file:line, and writes:

  --out-md       full markdown, for $GITHUB_STEP_SUMMARY
  --out-alert    shorter markdown, for the quality-alert issue body
  --out-verdict  one word: `failing` when any file has a surviving or no-coverage
                 mutant, `ok` otherwise

Stryker is run with `since` enabled, so only mutants on lines changed in the window are
active; every other mutant is `Ignored` and not counted. A mutant that survives, or is
not covered by any test, on a changed line of a security-critical file is the signal.

Paths: Stryker.NET 5 keys each file by its ABSOLUTE path on the runner (and may leave `..` in it, e.g.
`/home/runner/work/x/x/src/server/api/Sprk.Bff.Api/../../shared/Contracts/A.cs`). Such a key is normalised and made
relative to --repo-root (default: $GITHUB_WORKSPACE, else the current directory). Only a RELATIVE key is joined to the
report's SOURCE_ROOT. Prefixing SOURCE_ROOT to an absolute key printed every path twice (issue #1821).

The table lists only files with at least one active mutant; Stryker's report also carries every other file of the
project (all mutants `Ignored` in since-mode), which are counted in one line instead of a row each.

Usage:
  summarize-mutation-report.py --report LABEL=path/to/mutation-report.json=SOURCE_ROOT ... [--repo-root DIR]
"""
import argparse
import json
import os
import posixpath
import re
import sys
from pathlib import Path

KILLED = {"Killed", "Timeout"}
SURVIVED = {"Survived"}
UNCOVERED = {"NoCoverage"}
ACTIVE = KILLED | SURVIVED | UNCOVERED
MAX_LISTED = 100


def md_code(text: str, limit: int = 90) -> str:
    text = " ".join(text.split())
    if len(text) > limit:
        text = text[: limit - 1] + "…"
    return "`" + text.replace("`", "'").replace("|", "\\|") + "`"


_ABSOLUTE = re.compile(r"^(/|[A-Za-z]:/)")


def repo_relative(fname: str, root: str, repo_root: str) -> str:
    """The repo-relative path of a report file key (see the module docstring)."""
    path = fname.replace("\\", "/")
    if not _ABSOLUTE.match(path):
        return posixpath.normpath(posixpath.join(root, path) if root else path)
    path = posixpath.normpath(path)
    base = posixpath.normpath(repo_root.replace("\\", "/")).rstrip("/")
    # A Windows drive path compares case-insensitively; a POSIX one does not.
    windows = bool(re.match(r"^[A-Za-z]:/", path))
    same = path.lower().startswith(base.lower() + "/") if windows else path.startswith(base + "/")
    return path[len(base) + 1:] if same else path


def load(spec: str):
    label, path, root = (spec.split("=", 2) + ["", ""])[:3]
    return label, Path(path), root.strip("/")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", action="append", default=[], help="LABEL=report.json=SOURCE_ROOT")
    ap.add_argument("--out-md", required=True)
    ap.add_argument("--out-alert", required=True)
    ap.add_argument("--out-verdict", required=True)
    ap.add_argument("--repo-root", default=os.environ.get("GITHUB_WORKSPACE") or os.getcwd(),
                    help="checkout root that absolute report paths are made relative to")
    args = ap.parse_args()

    rows = []       # (repo path, killed, survived, uncovered, ignored)
    findings = []   # (repo path, line, status, mutator, replacement, source line)
    missing = []

    for spec in args.report:
        label, path, root = load(spec)
        if not path.is_file():
            missing.append(f"{label}: {path}")
            continue
        report = json.loads(path.read_text(encoding="utf8"))
        for fname, data in sorted(report.get("files", {}).items()):
            repo_path = repo_relative(fname, root, args.repo_root)
            source_lines = (data.get("source") or "").splitlines()
            killed = survived = uncovered = ignored = 0
            for m in data.get("mutants", []):
                status = m.get("status")
                if status in KILLED:
                    killed += 1
                elif status in SURVIVED or status in UNCOVERED:
                    if status in SURVIVED:
                        survived += 1
                    else:
                        uncovered += 1
                    line = (m.get("location") or {}).get("start", {}).get("line", 0)
                    src = source_lines[line - 1].strip() if 0 < line <= len(source_lines) else ""
                    findings.append((repo_path, line, status, m.get("mutatorName", "?"), m.get("replacement") or "", src))
                else:
                    ignored += 1
            rows.append((repo_path, killed, survived, uncovered, ignored))

    failing = any(r[2] or r[3] for r in rows)
    verdict = "failing" if failing else "ok"
    Path(args.out_verdict).write_text(verdict, encoding="utf8")

    active_rows = [r for r in rows if r[1] + r[2] + r[3] > 0]
    inactive = len(rows) - len(active_rows)

    def table():
        out = ["| File | Killed | Survived | No coverage | Score |", "|---|---:|---:|---:|---:|"]
        for path, k, s, u, _ in active_rows:
            out.append(f"| `{path}` | {k} | {s} | {u} | {100.0 * k / (k + s + u):.1f}% |")
        if not active_rows:
            out.append("| _no file in the reports has an active mutant_ | | | | |")
        if inactive:
            out += ["", f"{inactive} other file(s) in the report had no active mutant (nothing on a changed line)."]
        return out

    def listing(limit):
        out = []
        for path, line, status, mutator, repl, src in findings[:limit]:
            out.append(f"- `{path}:{line}` — **{status}** — {mutator}: {md_code(src)} → {md_code(repl)}")
        if len(findings) > limit:
            out.append(f"- …and {len(findings) - limit} more (see the `mutation-report` artifact)")
        return out

    md = ["# Nightly mutation testing (changed security code)", ""]
    if missing:
        md += ["> Missing report(s): " + ", ".join(missing), ""]
    md += table()
    md += ["", f"**Verdict: {verdict}.** "
           + ("Surviving or uncovered mutants were found on changed lines." if failing
              else "Every active mutant on the changed lines was killed."), ""]
    if findings:
        md += ["## Surviving and uncovered mutants", ""] + listing(MAX_LISTED) + [""]
    md += ["Full HTML/JSON reports: the `mutation-report` artifact of this run."]
    Path(args.out_md).write_text("\n".join(md) + "\n", encoding="utf8")

    alert = []
    if failing:
        alert += ["Mutation testing of the security-critical files changed in the last window found mutants that no test kills.", ""]
        alert += table() + [""]
        alert += ["### Surviving and uncovered mutants", ""] + listing(40)
        alert += ["", "A surviving mutant on a changed line means the tests would still pass if that line were broken."]
    Path(args.out_alert).write_text("\n".join(alert) + "\n", encoding="utf8")

    print("\n".join(md))
    return 0


if __name__ == "__main__":
    sys.exit(main())
