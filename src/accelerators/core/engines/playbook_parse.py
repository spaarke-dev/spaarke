"""playbook_parse.py - parse a Markdown legal playbook into JSON and enforce the format contract.

Python 3 standard library only.

Usage:
  python playbook_parse.py PLAYBOOK.md --topics topics.json [--side mutual] > playbook.json

Format (see references/playbook-format.md):
  # Playbook: NDA - Mutual
  Version: 1.0 | Owner: <name> | Effective: 2026-11-01 | Jurisdiction default: Delaware | Side: mutual | Status: <text>

  ## topic: nda.term
  Preferred: ...
  Fallback: ...
  Prohibited / escalate: ...
  If missing: ...          (optional: what to do when the agreement has no clause on the topic)
  Model language: "..."
  Rationale: ...
  Priority: High

Rules (closed sets; ambiguous input is rejected, never guessed):
  - every topic id must exist in the catalogue; duplicates are errors;
  - field labels are exactly those above; each topic needs Preferred and Priority;
  - Priority is High, Medium or Low;
  - header needs Version and Side; Side is mutual, disclosing or receiving.
Catalogue topics the playbook does not cover are reported as "silent" (the skill must say so).
Exit codes: 0 ok, 2 bad input, 3 playbook violates the format.
"""
import argparse
import json
import re
import sys

FIELDS = {
    "preferred": "preferred",
    "fallback": "fallback",
    "prohibited / escalate": "escalate",
    "if missing": "if_missing",
    "model language": "model_language",
    "rationale": "rationale",
    "priority": "priority",
}
HEADER_KEYS = {"version": "version", "owner": "owner", "effective": "effective",
               "jurisdiction default": "jurisdiction_default", "side": "side", "status": "status"}
SIDES = {"mutual", "disclosing", "receiving"}
PRIORITIES = {"High", "Medium", "Low"}
TOPIC_RE = re.compile(r"^##\s+topic:\s*(?P<id>\S+)\s*$")
FIELD_RE = re.compile(r"^(?P<label>[A-Za-z][A-Za-z /]*?):\s*(?P<value>.*)$")


class FormatError(Exception):
    pass


def parse(text, catalogue_ids, side_filter=None, require_side=True):
    """require_side=False for a non-contract document type (no parties' sides, backlog X14): Side may be omitted,
    and silent topics are then computed over the whole catalogue."""
    errors, lines = [], text.splitlines()
    title, meta, topics, cur, field = None, {}, [], None, None
    intro = []  # free text between the header and the first topic (scope / introduction)
    for n, raw in enumerate(lines, 1):
        line = raw.rstrip()
        if not line.strip():
            field = None  # a field ends at a blank line
            continue
        if line.startswith("# ") and title is None:
            m = re.match(r"^#\s+Playbook:\s*(.+)$", line)
            if not m:
                errors.append(f"line {n}: title must be '# Playbook: <name>'")
            title = m.group(1).strip() if m else line[2:].strip()
            continue
        if title is not None and not meta and cur is None and (
                line.lower().startswith("version:") or "|" in line):
            for part in line.split("|"):
                if ":" not in part:
                    errors.append(f"line {n}: header part {part.strip()!r} has no label")
                    continue
                k, v = part.split(":", 1)
                key = HEADER_KEYS.get(k.strip().lower())
                if key is None:
                    errors.append(f"line {n}: unknown header label {k.strip()!r}")
                else:
                    meta[key] = v.strip()
            continue
        tm = TOPIC_RE.match(line)
        if tm:
            cur = {"id": tm.group("id"), "line": n}
            topics.append(cur)
            field = None
            continue
        if line.startswith("#"):
            errors.append(f"line {n}: unexpected heading {line[:40]!r} (topics use '## topic: <id>')")
            cur, field = None, None
            continue
        if cur is None:
            if title is not None:
                intro.append(line.strip())  # free text before the first topic is allowed (introduction)
            continue
        fm = FIELD_RE.match(line)
        if fm and fm.group("label").strip().lower() in FIELDS:
            field = FIELDS[fm.group("label").strip().lower()]
            if field in cur:
                errors.append(f"line {n}: topic {cur['id']} repeats field {fm.group('label')!r}")
            cur[field] = fm.group("value").strip()
        elif fm and field is None:
            errors.append(f"line {n}: unknown field label {fm.group('label')!r} in topic {cur['id']}")
        elif field is not None:
            cur[field] = (cur[field] + " " + line.strip()).strip()
        else:
            errors.append(f"line {n}: text outside a field in topic {cur['id']}")

    if title is None:
        errors.append("missing '# Playbook: <name>' title")
    for k in ("version", "side") if require_side else ("version",):
        if k not in meta:
            errors.append(f"header is missing {k!r}")
    if meta.get("side") and meta["side"].lower() not in SIDES:
        errors.append(f"side {meta['side']!r} is not one of {sorted(SIDES)}")
    seen = set()
    for t in topics:
        tid = t["id"]
        if tid in seen:
            errors.append(f"line {t['line']}: duplicate topic {tid}")
        seen.add(tid)
        if tid not in catalogue_ids:
            errors.append(f"line {t['line']}: unknown topic id {tid} (not in topics catalogue)")
        if not t.get("preferred"):
            errors.append(f"line {t['line']}: topic {tid} has no Preferred position")
        if t.get("priority") not in PRIORITIES:
            errors.append(f"line {t['line']}: topic {tid} priority {t.get('priority')!r} is not High/Medium/Low")
        t["model_language"] = t.get("model_language", "").strip().strip('"“”') or None
        for f in ("fallback", "escalate", "if_missing", "rationale"):
            t.setdefault(f, None)
            if t[f] and t[f].strip().lower() in ("n/a", "none", "-"):
                t[f] = None
    if errors:
        raise FormatError(errors)
    side = meta["side"].lower() if meta.get("side") else None
    for_side = side_filter or side
    relevant = {i for i, sides in catalogue_ids.items() if for_side is None or for_side in sides}
    silent = sorted(relevant - seen)
    for t in topics:
        t.pop("line", None)
    if side:
        meta["side"] = side
    return {"title": title, "meta": meta, "intro": " ".join(intro), "topics": topics, "silent_topics": silent}


def load_catalogue(path):
    with open(path, encoding="utf-8") as f:
        cat = json.load(f)
    return {t["id"]: set(t.get("sides", [])) for t in cat["topics"] if t.get("status", "active") != "retired"}


def main(argv=None):
    ap = argparse.ArgumentParser(description="Parse and validate a Markdown legal playbook.")
    ap.add_argument("playbook")
    ap.add_argument("--topics", required=True, help="topics.json catalogue shipped in the skill's references/")
    ap.add_argument("--side", choices=sorted(SIDES), help="report silent topics for this side")
    a = ap.parse_args(argv)
    try:
        with open(a.playbook, encoding="utf-8-sig") as f:
            text = f.read()
        cat = load_catalogue(a.topics)
    except (OSError, ValueError, KeyError) as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 2
    try:
        result = parse(text, cat, a.side)
    except FormatError as ex:
        print(json.dumps({"error": "playbook format", "details": ex.args[0]}, indent=1), file=sys.stderr)
        return 3
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
