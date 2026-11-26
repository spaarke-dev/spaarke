"""playbook_import.py - convert a four-column Word playbook into our Markdown playbook format.

The four-column format (Topic | Instructions | Example Text | Compromise Positions) is the one Microsoft
Legal for Cowork's `create-playbook` writes and Word's playbook import reads. This engine maps each row
to a topic id in the catalogue, so a precedent-built playbook can be scored, compared and reviewed by
topic like any Spaarke playbook. Standard library + lxml only; no network.

Usage:
  python playbook_import.py PLAYBOOK.docx --topics topics.json --out playbook.md
         [--report report.json] [--side mutual] [--baseline BASELINE.md] [--name NAME]

Mapping, in order (closed rules; anything uncertain is flagged for review, never silently accepted):
  1. an explicit topic id in the Topic cell, e.g. "Definition and marking (nda.definition-unmarked)";
  2. otherwise a keyword score between the row and each catalogue topic (title, question, look_for).
     Each topic goes to its best-scoring row; a row that wins several topics is "merged" (its text is
     copied to each and flagged); a row that wins none is "extra"; a topic no row wins is "missing".
  Gap rows ("no ... precedent position", "None of the ... precedents addresses") carry no position: a
  topic mapped only to a gap row is reported as "gap", not written as a position.

Fields: Instructions -> Preferred, minus sentences starting Escalate/Reject/Prohibited, which go to
Prohibited / escalate; Compromise Positions -> Fallback (in order); Example Text -> Model language;
Priority -> the catalogue default. With --baseline, catalogue topics that are missing or gap are filled
from that playbook and marked in their Rationale; baseline fallbacks the import lacks are suggested in
the report only.

Prints the report as JSON. The Markdown is validated with playbook_parse before it is written.
Exit codes: 0 ok; 2 bad input (unreadable file, no four-column table, side unknown);
3 the generated playbook fails the format contract (a defect in this engine).
"""
import argparse
import datetime
import json
import os
import re
import sys
import zipfile

from lxml import etree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import playbook_parse  # noqa: E402  (sibling engine)

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
HEADERS = ("topic", "instructions", "example text", "compromise positions")
SIDES = ("mutual", "disclosing", "receiving")
ID_RE = re.compile(r"\(\s*([a-z]+\.[a-z0-9-]+)\s*\)")
GAP_NAME_RE = re.compile(r"\(\s*no\b[^)]*\bposition\s*\)", re.I)
GAP_TEXT_RE = re.compile(r"^\s*(none of the|no)\b[^.]*\b(precedents?|sources?)\b[^.]*\baddress", re.I)
ESCALATE_RE = re.compile(r"^\s*(escalate|reject|prohibited|do not accept|never accept)\b\s*:?\s*", re.I)
NUMBER_RE = re.compile(r"^\s*(\d+|[a-z])[.)]\s+", re.I)
SENT_RE = re.compile(r"(?<=[.!?])\s+(?=[A-Z\"“(])")
STOP = set("""a an and any are as at be by for from if in into is it its not of on or other the this to
under with without that their than then there these those will would may must each either both party
parties agreement information confidential confidentiality nda acme retain keep counsel""".split())
MIN_SCORE = 6  # below this a keyword match is too weak to map
WEAK_SCORE = 12  # mapped, but below this the report asks for a check


class BadInput(Exception):
    pass


def q(tag):
    return "{%s}%s" % (W, tag)


def para_text(p):
    out = []
    for el in p.iter(q("t"), q("tab"), q("br")):
        out.append((el.text or "") if el.tag == q("t") else " ")
    return re.sub(r"\s+", " ", "".join(out)).strip()


def read_docx(path):
    """Return (paragraphs before the first table as (style, text), table rows as lists of cell paragraph lists)."""
    try:
        with zipfile.ZipFile(path) as z:
            root = etree.fromstring(z.read("word/document.xml"))
    except (OSError, KeyError, zipfile.BadZipFile, etree.XMLSyntaxError) as ex:
        raise BadInput(f"cannot read {path}: {ex}")
    body = root.find(q("body"))
    pre, tables = [], []
    for el in body:
        if el.tag == q("p") and not tables:
            style = el.find(f"{q('pPr')}/{q('pStyle')}")
            text = para_text(el)
            if text:
                pre.append((style.get(q("val")) if style is not None else "", text))
        elif el.tag == q("tbl"):
            rows = []
            for tr in el.findall(q("tr")):
                rows.append([[t for t in (para_text(p) for p in tc.iter(q("p"))) if t] for tc in tr.findall(q("tc"))])
            tables.append(rows)
    for rows in tables:
        if rows and len(rows[0]) >= 4:
            head = tuple(" ".join(c).strip().lower() for c in rows[0][:4])
            if head == HEADERS:
                return pre, rows[1:]
    raise BadInput("no table with the header Topic | Instructions | Example Text | Compromise Positions")


def tokens(text):
    out = set()
    for w in re.findall(r"[a-z][a-z0-9]+", (text or "").lower()):
        if len(w) < 3 or w in STOP:
            continue
        for suf in ("ations", "ation", "ions", "ion", "ing", "ies", "es", "s"):
            if w.endswith(suf) and len(w) - len(suf) >= 4:
                w = w[: -len(suf)]
                break
        out.add(w)
    return out


FALLBACK_SENT_RE = re.compile(r"^acceptable fallback\b[^:]*:", re.I)  # our Word copy repeats the fallback (gap #30)
IF_MISSING_RE = re.compile(r"^if missing\s*:\s*", re.I)
ESCALATE_LABEL_RE = re.compile(r"^(escalate(\s+if)?|prohibited[^:]{0,20})\s*:\s*", re.I)


def split_instructions(text):
    """Instructions cell -> (preferred, escalate, if_missing). A repeated "Acceptable fallback" line is dropped:
    the Compromise column is the fallback's source."""
    preferred, escalate, if_missing = [], [], []
    for s in SENT_RE.split(text or ""):
        s = s.strip()
        if not s or FALLBACK_SENT_RE.match(s):
            continue
        mm = IF_MISSING_RE.match(s)
        if mm:
            if_missing.append(s[mm.end():].strip())
            continue
        m = ESCALATE_RE.match(s)
        lab = ESCALATE_LABEL_RE.match(s)
        if m and lab:  # a labelled line ("Escalate if: …", "Prohibited: …"): keep only the position
            escalate.append(s[lab.end():].strip())
        elif m:
            escalate.append(s)
        else:
            preferred.append(s)
    if not preferred:  # never leave Preferred empty: the instructions as a whole are the position
        preferred, escalate = [text.strip()], escalate
    return " ".join(preferred), " ".join(escalate) or None, " ".join(if_missing) or None


def clean_name(name):
    return re.sub(r"\s+", " ", GAP_NAME_RE.sub("", ID_RE.sub("", name))).strip(" -–")


def score(row, topic):
    name_t = tokens(row["name"])
    text = " ".join([row["name"], row["instructions"], row["example"], row["compromise"]]).lower()
    text_t = tokens(text)
    title_t = tokens(" ".join([topic.get("title", ""), topic["id"].split(".", 1)[-1].replace("-", " ")]
                              + list(topic.get("aliases", []))))
    all_t = title_t | tokens(topic.get("question", "")) | tokens(" ".join(topic.get("look_for", [])))
    phrases = sum(1 for p in list(topic.get("look_for", [])) + list(topic.get("aliases", []))
                  if p and p.lower() in text)
    return 3 * len(title_t & name_t) + len(all_t & text_t) + 4 * phrases


def load_catalogue(path):
    try:
        with open(path, encoding="utf-8") as f:
            cat = json.load(f)
        return [t for t in cat["topics"] if t.get("status", "active") != "retired"]
    except (OSError, ValueError, KeyError) as ex:
        raise BadInput(f"cannot read topics catalogue {path}: {ex}")


def build_rows(raw_rows):
    rows = []
    for n, cells in enumerate(raw_rows, 1):
        cells = (cells + [[], [], [], []])[:4]
        name = " ".join(cells[0])
        if not name.strip():
            continue
        comp = [NUMBER_RE.sub("", c).strip() for c in cells[3] if c.strip()]
        rows.append({
            "row": n, "name": name, "label": clean_name(name),
            "instructions": " ".join(cells[1]), "example": " ".join(cells[2]), "compromise": " ".join(comp),
            "compromise_list": comp,
            "gap": bool(GAP_NAME_RE.search(name) or GAP_TEXT_RE.match(" ".join(cells[1]))),
            "explicit": ID_RE.findall(name),
        })
    return rows


def map_rows(rows, catalogue):
    ids = {t["id"] for t in catalogue}
    assigned = {}  # topic id -> (row index, method, score)
    for i, r in enumerate(rows):
        for tid in r["explicit"]:
            if tid in ids and tid not in assigned:
                assigned[tid] = (i, "id", None)
    explicit_rows = {v[0] for v in assigned.values()}
    for t in catalogue:
        if t["id"] in assigned:
            continue
        best = max(((score(r, t), i) for i, r in enumerate(rows) if i not in explicit_rows), default=(0, None))
        if best[1] is not None and best[0] >= MIN_SCORE:
            assigned[t["id"]] = (best[1], "keyword", best[0])
    by_row = {}
    for tid, (i, method, sc) in assigned.items():
        by_row.setdefault(i, []).append((tid, method, sc))
    return assigned, by_row


def one_line(text):
    return re.sub(r"\s+", " ", text or "").strip()


def render(title, meta, intro, topics):
    head = " | ".join(f"{k}: {v}" for k, v in meta if v)
    out = [f"# Playbook: {title}", head, ""]
    out += [one_line(p) for p in intro if p] + [""]
    for t in topics:
        out.append(f"## topic: {t['id']}")
        out.append(f"Preferred: {one_line(t['preferred'])}")
        out.append(f"Fallback: {one_line(t.get('fallback')) or 'n/a'}")
        out.append(f"Prohibited / escalate: {one_line(t.get('escalate')) or 'n/a'}")
        if t.get("if_missing"):
            out.append(f"If missing: {one_line(t['if_missing'])}")
        if t.get("model_language"):
            out.append(f"Model language: \"{one_line(t['model_language'])}\"")
        if t.get("rationale"):
            out.append(f"Rationale: {one_line(t['rationale'])}")
        out.append(f"Priority: {t['priority']}")
        out.append("")
    return "\n".join(out)


def convert(docx_path, catalogue, side=None, baseline=None, name=None, today=None):
    pre, raw = read_docx(docx_path)
    rows = build_rows(raw)
    if not rows:
        raise BadInput("the playbook table has no topic rows")
    src = os.path.basename(docx_path)
    title_p = next((t for s, t in pre if s.lower() in ("title", "heading1")), None) or (pre[0][1] if pre else None)
    title = name or title_p or os.path.splitext(src)[0]
    pre_text = " ".join(t for _, t in pre)
    version = (re.search(r"\bversion[: ]+v?(\d+(?:\.\d+)*)", pre_text, re.I) or [None, None])[1]
    if not side:
        m = re.search(r"represented side:\s*(mutual|disclosing|receiving)", pre_text, re.I)
        side = m.group(1).lower() if m else ("mutual" if re.search(r"\bmutual\b", title + " " + pre_text, re.I) else None)
    if side not in SIDES:
        raise BadInput("cannot tell the represented side from the document; pass --side mutual|disclosing|receiving")
    cat_by_id = {t["id"]: t for t in catalogue}
    relevant = [t for t in catalogue if side in t.get("sides", SIDES)]
    assigned, by_row = map_rows(rows, relevant)

    report_rows, topics, gap_topics = [], {}, []
    for i, r in enumerate(rows):
        hits = sorted(by_row.get(i, []))
        kind = "gap" if r["gap"] else ("extra" if not hits else ("merged" if len(hits) > 1 else "mapped"))
        report_rows.append({"row": r["row"], "name": r["label"], "kind": kind,
                            "topics": [h[0] for h in hits], "method": sorted({h[1] for h in hits}),
                            "scores": {h[0]: h[2] for h in hits if h[2] is not None}})
        for tid, method, sc in hits:
            if r["gap"]:
                gap_topics.append(tid)
                continue
            preferred, escalate, if_missing = split_instructions(r["instructions"])
            note = f"Imported from {src}, row {r['row']} ({r['label']})."
            if len(hits) > 1:
                note += " Merged row: also covers " + ", ".join(h[0] for h in hits if h[0] != tid) + "; check the wording."
            if method == "keyword":
                note += f" Mapped by keyword (score {sc}); confirm."
            topics[tid] = {"id": tid, "preferred": preferred, "escalate": escalate, "if_missing": if_missing,
                           "fallback": "; then ".join(r["compromise_list"]) or None,
                           "model_language": r["example"] or None, "rationale": note,
                           "priority": cat_by_id[tid].get("default_priority", "Medium")}

    missing = [t["id"] for t in relevant if t["id"] not in topics and t["id"] not in gap_topics]
    from_baseline, suggestions = [], []
    if baseline:
        base_by_id = {t["id"]: t for t in baseline["topics"]}
        for tid in gap_topics + missing:
            b = base_by_id.get(tid)
            if b and tid not in topics:
                topics[tid] = dict(b, rationale=(b.get("rationale") or "") +
                                   f" (Not set by {src}; taken from baseline {baseline['title']}.)")
                from_baseline.append(tid)
        for tid, t in topics.items():
            b = base_by_id.get(tid)
            if b and b.get("fallback") and not t.get("fallback") and tid not in from_baseline:
                suggestions.append({"topic": tid, "baseline_fallback": b["fallback"]})

    order = [t["id"] for t in catalogue]
    ordered = [topics[i] for i in order if i in topics]
    meta = [("Version", version or "0.1-import"), ("Owner", "Imported - assign an owner"),
            ("Effective", (today or datetime.date.today()).isoformat()), ("Side", side),
            ("Status", f"Imported from {src}; draft for counsel review")]
    kinds = {k: [x["row"] for x in report_rows if x["kind"] == k] for k in ("mapped", "merged", "gap", "extra")}
    intro = [f"Imported by playbook_import.py from {src}: {len(rows)} rows; {len(kinds['mapped'])} mapped, "
             f"{len(kinds['merged'])} merged, {len(kinds['gap'])} gap, {len(kinds['extra'])} extra. "
             "Positions keep the source wording. Check the import report before relying on this playbook."]
    intro += [t for s, t in pre if t != title_p]
    md = render(title, meta, intro, ordered)
    parsed = playbook_parse.parse(md, {t["id"]: set(t.get("sides", [])) for t in catalogue}, side)
    report = {
        "source": src, "title": title, "side": side, "rows": report_rows,
        "counts": {k: len(v) for k, v in kinds.items()} | {"topics_written": len(ordered)},
        "topics_gap": sorted(set(gap_topics) - set(from_baseline)),
        "topics_missing": sorted(set(missing) - set(from_baseline)),
        "topics_from_baseline": from_baseline,
        "fallback_suggestions": suggestions,
        "needs_review": sorted({x["row"] for x in report_rows if x["kind"] in ("merged", "extra")
                                or any(s < WEAK_SCORE for s in x["scores"].values())}),
        "silent_topics": parsed["silent_topics"],
    }
    return md, report


def main(argv=None):
    ap = argparse.ArgumentParser(description="Convert a four-column Word playbook to a Markdown playbook with topic ids.")
    ap.add_argument("docx", help="four-column playbook .docx (Topic | Instructions | Example Text | Compromise Positions)")
    ap.add_argument("--topics", required=True, help="topics.json catalogue")
    ap.add_argument("--out", required=True, help="Markdown playbook to write")
    ap.add_argument("--report", help="also write the import report JSON here")
    ap.add_argument("--side", choices=SIDES, help="represented side, when the document does not say")
    ap.add_argument("--baseline", help="Markdown playbook used to fill missing and gap topics")
    ap.add_argument("--name", help="playbook name (default: the document's title)")
    a = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    try:
        catalogue = load_catalogue(a.topics)
        baseline = None
        if a.baseline:
            with open(a.baseline, encoding="utf-8-sig") as f:
                baseline = playbook_parse.parse(f.read(), {t["id"]: set(t.get("sides", [])) for t in catalogue})
    except (BadInput, OSError, playbook_parse.FormatError) as ex:
        detail = ex.args[0] if isinstance(ex, playbook_parse.FormatError) else str(ex)
        print(json.dumps({"error": "bad input", "details": detail}, indent=1), file=sys.stderr)
        return 2
    try:
        md, report = convert(a.docx, catalogue, a.side, baseline, a.name)
    except BadInput as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 2
    except playbook_parse.FormatError as ex:
        print(json.dumps({"error": "generated playbook fails the format contract", "details": ex.args[0]},
                         indent=1), file=sys.stderr)
        return 3
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(md)
    if a.report:
        with open(a.report, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
    print(json.dumps(report, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
