#!/usr/bin/env python3
"""Summarise Stryker.NET mutation reports for .github/workflows/mutation-nightly.yml.

Reads one or more mutation-testing-elements JSON reports (Stryker's `json` reporter),
prints a per-file table (killed / survived / no-coverage / score) and every surviving
or uncovered mutant with file:line, and writes:

  --out-md       full markdown, for $GITHUB_STEP_SUMMARY
  --out-alert    shorter markdown, for the quality-alert issue body
  --out-verdict  one word: `failing` when any file has a surviving or no-coverage
                 mutant, `ok` otherwise

A mutant that survives, or is not covered by any test, on a changed line of a
security-critical file is the signal. Stryker's `since` mode alone does NOT give that:
it also re-tests every mutant covered by a test file that changed in the window, so a
day that edits a test of a large file re-tests (and reports) most of that file, lines
nobody touched included (#1821: the 2026-11-23 window changed 14 lines of
ChildAccessPermissionReconciler.cs and Stryker reported 145 active mutants in it, none
on a changed line). So with --since, a mutant is counted only when one of its lines is
on the new side of `git diff <since>` for its file; every other active mutant is
reported as a count ("outside the window"), not alerted on. If the diff of a file
cannot be read, all of that file's active mutants are counted (an unreadable diff must
not turn a failing night green). Without --since every active mutant is counted.

Paths: Stryker.NET 5 keys each file by its ABSOLUTE path on the runner (and may leave `..` in it, e.g.
`/home/runner/work/x/x/src/server/api/Sprk.Bff.Api/../../shared/Contracts/A.cs`). Such a key is normalised and made
relative to --repo-root (default: $GITHUB_WORKSPACE, else the current directory). Only a RELATIVE key is joined to the
report's SOURCE_ROOT. Prefixing SOURCE_ROOT to an absolute key printed every path twice (issue #1821).

The table lists only files with at least one active mutant; Stryker's report also carries every other file of the
project (all mutants `Ignored` in since-mode), which are counted in one line instead of a row each.

Usage:
  summarize-mutation-report.py --report LABEL=path/to/mutation-report.json=SOURCE_ROOT ... [--repo-root DIR] [--since SHA]

Controls: scripts/ci/tests/test_summarize_mutation_report.py.
"""
import argparse
import json
import os
import posixpath
import re
import subprocess
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


_HUNK = re.compile(r"^@@ -\S+ \+(\d+)(?:,(\d+))? @@", re.M)


def changed_lines(repo_root: str, since: str, repo_path: str):
    """New-side line numbers changed in `repo_path` since `since` (working tree, as Stryker diffs), or None if unreadable."""
    try:
        out = subprocess.run(
            ["git", "-C", repo_root, "diff", "--no-color", "--no-ext-diff", "-U0", since, "--", repo_path],
            capture_output=True, text=True, encoding="utf8", check=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError) as exc:
        print(f"warning: cannot read the diff of {repo_path} since {since} ({exc}); counting all its mutants",
              file=sys.stderr)
        return None
    lines = set()
    for m in _HUNK.finditer(out):
        start, count = int(m.group(1)), int(m.group(2) if m.group(2) is not None else 1)
        lines.update(range(start, start + count))
    return lines


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
    ap.add_argument("--since", default="",
                    help="the Stryker `since` target: count only mutants on lines changed since it (see the docstring)")
    args = ap.parse_args()

    rows = []       # (repo path, killed, survived, uncovered, ignored, outside the window)
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
            mutants = data.get("mutants", [])
            window = None
            if args.since and any(m.get("status") in ACTIVE for m in mutants):
                window = changed_lines(args.repo_root, args.since, repo_path)
            killed = survived = uncovered = ignored = outside = 0
            for m in mutants:
                status = m.get("status")
                loc = m.get("location") or {}
                first = loc.get("start", {}).get("line", 0)
                last = max(first, loc.get("end", {}).get("line", first))
                if status in ACTIVE and window is not None and not any(n in window for n in range(first, last + 1)):
                    outside += 1
                elif status in KILLED:
                    killed += 1
                elif status in SURVIVED or status in UNCOVERED:
                    if status in SURVIVED:
                        survived += 1
                    else:
                        uncovered += 1
                    src = source_lines[first - 1].strip() if 0 < first <= len(source_lines) else ""
                    findings.append((repo_path, first, status, m.get("mutatorName", "?"), m.get("replacement") or "", src))
                else:
                    ignored += 1
            rows.append((repo_path, killed, survived, uncovered, ignored, outside))

    failing = any(r[2] or r[3] for r in rows)
    verdict = "failing" if failing else "ok"
    Path(args.out_verdict).write_text(verdict, encoding="utf8")

    active_rows = [r for r in rows if r[1] + r[2] + r[3] > 0]
    inactive = len(rows) - len(active_rows)
    outside_rows = [(r[0], r[5]) for r in rows if r[5]]

    def table():
        out = ["| File | Killed | Survived | No coverage | Score |", "|---|---:|---:|---:|---:|"]
        for path, k, s, u, _, _ in active_rows:
            out.append(f"| `{path}` | {k} | {s} | {u} | {100.0 * k / (k + s + u):.1f}% |")
        if not active_rows:
            out.append("| _no file in the reports has an active mutant on a changed line_ | | | | |")
        if inactive:
            out += ["", f"{inactive} other file(s) in the report had no active mutant"
                    + (" on a changed line." if args.since else ".")]
        if outside_rows:
            total = sum(n for _, n in outside_rows)
            out += ["", f"Not counted: {total} mutant(s) Stryker re-tested on lines that did not change since "
                    f"`{args.since[:10]}` (a test covering them changed), in "
                    + ", ".join(f"`{p}` ({n})" for p, n in outside_rows)
                    + ". Their results are in the `mutation-report` artifact."]
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
