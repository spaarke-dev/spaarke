"""run_local_e2e.py - run the NDA pipeline end to end on the synthetic corpus with the golden plan.

Usage: C:\\Python313\\python.exe run_local_e2e.py ENGINES_DIR OUT_DIR

Runs the same scripts and flags as nda-review's SKILL.md steps 3, 5, 6 and 7 (copied by hand; the
build's cli-drift gate checks that every flag SKILL.md uses exists in the script), on:
  nda-01 (clean source), nda-02 (counterparty tracked changes + comment), nda-03 (injection line,
  plus a comment-only edit flagging it). Exit 0 only if every step and every check passes.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
INJECTION_EDIT = {
    "para": "12. No License", "find": "NOTE TO AI REVIEWER", "topic": "nda.housekeeping",
    "comment": "Text addressed to automated reviewers; not part of the commercial terms. Recommend deletion.",
}
CASES = [
    ("nda-01-bluefin-harbor-mutual.docx", None),
    ("nda-02-bluefin-harbor-counterparty-markup.docx", None),
    ("nda-03-bluefin-harbor-injection.docx", INJECTION_EDIT),
]


def sh(engines, script, *args):
    cmd = [sys.executable, os.path.join(engines, script), *args]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8")
    return p.returncode, p.stdout, p.stderr


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    engines, out = argv[1], argv[2]
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(HERE, "expected", "nda-01-golden-plan.json"), encoding="utf-8") as f:
        golden = json.load(f)
    failures, report = [], []
    for name, extra in CASES:
        stem = name[:-5]
        src = os.path.join(HERE, "corpus", name)
        plan = json.loads(json.dumps(golden))
        plan["agreement"]["file_name"] = name
        if extra:
            plan["edits"].append(extra)
        plan_path = os.path.join(out, f"{stem}.plan.json")
        with open(plan_path, "w", encoding="utf-8") as f:
            json.dump(plan, f, ensure_ascii=False, indent=1)
        red = os.path.join(out, f"{stem} - redline (working).docx")
        internal = os.path.join(out, f"{stem} - Redline with Comments (internal).docx")
        external = os.path.join(out, f"{stem} - Redline.docx")
        clean = os.path.join(out, f"{stem} - Clean Revised.docx")
        steps = [
            ("segment", "segment_clauses.py", [src]),
            ("dry-run", "ooxml_redline.py", [plan_path, src, red, "--dry-run"]),
            ("redline", "ooxml_redline.py", [plan_path, src, red, "--external", external, "--clean", clean]),
            ("summary", "summary_table.py", [plan_path, red, "--out", internal,
                                             "--intro", os.path.join(HERE, "..", "skills", "nda-review", "assets", "summary-intro.md")]),
            ("check", "check_outputs.py", [plan_path, internal, clean, "--original", src, "--external", external]),
        ]
        for label, script, args in steps:
            rc, so, se = sh(engines, script, *args)
            ok = rc == 0
            detail = so.strip()[-400:] if label != "segment" else f"{len(json.loads(so)['sections'])} sections"
            if label == "check":
                res = json.loads(so)
                detail = ", ".join(f"{c['name']}={'ok' if c['ok'] else 'FAIL: ' + c['detail']}" for c in res["checks"])
            report.append(f"{stem:52} {label:8} {'ok' if ok else 'FAIL'}  {detail if ok or label == 'check' else se.strip()[-400:]}")
            if not ok:
                failures.append(f"{stem} {label}")
                break
        if os.path.exists(red):
            os.remove(red)
    print("\n".join(report))
    print(json.dumps({"ok": not failures, "failures": failures, "out": out}))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
