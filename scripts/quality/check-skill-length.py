#!/usr/bin/env python
"""Skill size guard (ratcheted) — the length rules in .claude/skills/_template/SKILL.md.

Rule 1: a .claude/skills/<name>/SKILL.md over CAP lines must justify its length: a non-empty
        `length-justification:` frontmatter key, a "Read this file in full" line, and a `## Contents` map.
        The whole SKILL.md is loaded every time the skill runs, so its size is paid on every invocation.
Rule 2: a skill's references/ or examples/ .md over REF_TOC_LINES lines starts with a `## Contents` list in
        its first TOC_WINDOW lines. Claude may preview only the top of a referenced file. Templates copied
        verbatim (`*-template.md`) are exempt: a contents list would be copied into every output.

RATCHET: Rule 1 violations recorded in the baseline ("skill|lines") are reported but pass while the file does
not grow past the recorded count. A new violation, or a baselined skill that grows, exits 1. Delete a baseline
line when the skill is fixed; --update-baseline rewrites it from the current findings (review them first).

Run: python scripts/quality/check-skill-length.py [--root DIR] [--baseline FILE] [--update-baseline]
Tests: scripts/quality/tests/test_check_skill_length.py
"""
import argparse
import datetime
import os
import re
import subprocess
import sys

CAP = 500
REF_TOC_LINES = 100
TOC_WINDOW = 40
DEFAULT_BASELINE = "scripts/quality/skill-length.baseline.txt"

SKILL_RE = re.compile(r"^\.claude/skills/([^/_][^/]*)/SKILL\.md$")
REF_RE = re.compile(r"^\.claude/skills/[^/_][^/]*/(?:references|examples)/[^/]+\.md$")
CONTENTS_RE = re.compile(r"^#{1,3}\s+(Contents|Table of contents|What this file contains)\b", re.I | re.M)


def tracked(root):
    out = subprocess.run(["git", "ls-files", ".claude/skills"], cwd=root, capture_output=True, text=True, check=True)
    return [p for p in out.stdout.splitlines() if p]


def read(root, rel):
    with open(os.path.join(root, rel), encoding="utf-8", errors="replace") as f:
        return f.read()


def frontmatter(text):
    m = re.match(r"^---\r?\n(.*?)\r?\n---", text, re.S)
    return m.group(1) if m else ""


def justified(text):
    has_key = re.search(r"^length-justification:[ \t]*\S", frontmatter(text), re.M)
    return bool(has_key and re.search(r"Read this file in full", text) and CONTENTS_RE.search(text))


def scan(root):
    """Return (over_cap: {skill: lines}, missing_toc: [path])."""
    over_cap, missing_toc = {}, []
    for rel in tracked(root):
        m = SKILL_RE.match(rel)
        if m:
            text = read(root, rel)
            n = len(text.splitlines())
            if n > CAP and not justified(text):
                over_cap[m.group(1)] = n
        elif REF_RE.match(rel) and not rel.endswith("-template.md"):
            lines = read(root, rel).splitlines()
            if len(lines) > REF_TOC_LINES and not CONTENTS_RE.search("\n".join(lines[:TOC_WINDOW])):
                missing_toc.append(rel)
    return over_cap, sorted(missing_toc)


def load_baseline(path):
    known = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith("#") and "|" in line:
                    skill, n = line.split("|", 1)
                    known[skill] = int(n)
    return known


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=None)
    ap.add_argument("--baseline", default=None)
    ap.add_argument("--update-baseline", action="store_true")
    args = ap.parse_args(argv)

    root = args.root or subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True,
                                       text=True, check=True).stdout.strip()
    baseline = args.baseline or os.path.join(root, DEFAULT_BASELINE)
    over_cap, missing_toc = scan(root)

    if args.update_baseline:
        with open(baseline, "w", encoding="utf-8", newline="\n") as f:
            f.write(f"# Skills over the {CAP}-line cap without a length justification, recorded "
                    f"{datetime.date.today():%Y-%m-%d} (ratchet baseline).\n"
                    "# Listed skills pass while they do not grow past the recorded count. Delete a line when fixed.\n"
                    "# Format: skill|lines\n")
            for skill in sorted(over_cap):
                f.write(f"{skill}|{over_cap[skill]}\n")
        print(f"Baseline rewritten: {len(over_cap)} entries -> {baseline}")
        return 0

    known = load_baseline(baseline)
    new = {s: n for s, n in over_cap.items() if s not in known}
    grew = {s: (known[s], n) for s, n in over_cap.items() if s in known and n > known[s]}
    fixed = sorted(s for s in known if s not in over_cap)

    print(f"Skills over {CAP} lines without justification: {len(over_cap)} "
          f"(baseline {len(known)}, new {len(new)}, grew {len(grew)}). "
          f"Reference files missing a contents list: {len(missing_toc)}.")
    if fixed:
        print(f"Fixed since the baseline (delete these lines from {baseline}): {', '.join(fixed)}")
    for s, n in sorted(new.items()):
        print(f"::error file=.claude/skills/{s}/SKILL.md::SKILL.md is {n} lines (cap {CAP}). Move conditional "
              "sections to references/, or add a length-justification (see .claude/skills/_template/SKILL.md).")
    for s, (was, n) in sorted(grew.items()):
        print(f"::error file=.claude/skills/{s}/SKILL.md::SKILL.md grew from {was} to {n} lines while over the "
              f"{CAP}-line cap (baselined). Shrink it, or justify its length.")
    for p in missing_toc:
        print(f"::error file={p}::Reference file is over {REF_TOC_LINES} lines and has no '## Contents' list "
              f"in its first {TOC_WINDOW} lines.")
    return 1 if (new or grew or missing_toc) else 0


if __name__ == "__main__":
    sys.exit(main())
