"""segment_clauses.py - map a .docx into numbered sections with unique paragraph keys.

Python 3 standard library + lxml only.

Usage:
  python segment_clauses.py ORIGINAL.docx [--min-key 12] > clauses.json

Output (JSON on stdout):
  {"paragraphs": N, "has_tracked_changes": bool (any revision, incl. moves and formatting, in the body,
   headers, footers or notes), "revised_parts": [...], "has_comments": bool,
   "has_hidden_text": bool (sections then carry "hidden_text": text formatted as hidden),
   "sections": [{"index": 7, "number": "10", "heading": "Term", "para_key": "Term. This Agreement",
                 "auto_numbered": false, "text": "Term. This Agreement shall ..."}]}

`para_key` is the shortest leading text (at least --min-key characters, extended a word at
a time) that occurs in exactly one paragraph, so it can be used as an edit plan's "para".
Paragraphs whose key cannot be made unique get "para_key": null and a stderr warning.
Exit codes: 0 ok, 2 bad input.
"""
import argparse
import json
import os
import re
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from lxml import etree
    import safe_docx
except ImportError as _ex:
    print(json.dumps({"error": f"cannot load lxml/safe_docx: {_ex}"}), file=sys.stderr)
    sys.exit(2)

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def q(tag):
    return "{%s}%s" % (W, tag)


NUM_RE = re.compile(
    r"^\s*(?:(?:section|article|clause)\s+)?"
    r"(?P<num>\d+(?:\.\d+)*\.?|[ivxlc]+\.|\([a-z0-9]{1,4}\)|[A-Z]\.)\s+", re.I)
HEADING_RE = re.compile(r"^(?P<h>[A-Z][^.:]{1,60}?)[.:]\s")


REVISION_TAGS = ("ins", "del", "moveFrom", "moveTo", "rPrChange", "pPrChange", "sectPrChange", "tblPrChange",
                 "trPrChange", "tcPrChange")
STORY_PART = re.compile(r"^word/(header\d*|footer\d*|footnotes|endnotes)\.xml$")


def load(path):
    parts = safe_docx.read_parts(path)
    doc = safe_docx.parse(parts["word/document.xml"])
    revised = [n for n, d in parts.items() if (n == "word/document.xml" or STORY_PART.match(n))
               and any(safe_docx.parse(d).find(".//" + q(t)) is not None for t in REVISION_TAGS)]
    return doc, "word/comments.xml" in parts, revised


def hidden_text(p):
    """Text in runs formatted as hidden (w:vanish): invisible to the attorney, visible to the model."""
    out = []
    for r in p.iter(q("r")):
        v = r.find("%s/%s" % (q("rPr"), q("vanish")))
        if v is not None and v.get(q("val"), "true") not in ("0", "false"):
            out.append("".join(t.text or "" for t in r.iter(q("t"))))
    return "".join(out)


def visible_text(p):
    out = []
    for el in p.iter(q("t"), q("tab"), q("br")):
        if any(a.tag == q("del") for a in el.iterancestors()):
            continue
        if el.tag == q("t"):
            out.append(el.text or "")
        else:
            out.append("\t" if el.tag == q("tab") else "\n")
    return "".join(out)


def full_text(p):  # what ooxml_redline.find_para matches against: whitespace collapsed
    raw = "".join((t.text or "") if t.tag in (q("t"), q("delText")) else " "
                  for t in p.iter(q("t"), q("delText"), q("tab"), q("br")))
    return re.sub(r"\s+", " ", raw)


def unique_key(i, texts, min_len):
    t = texts[i].strip()
    if not t:
        return None
    start = texts[i].index(t[0])
    words = re.finditer(r"\S+", texts[i][start:])
    end = 0
    for m in words:
        end = m.end()
        key = texts[i][start:start + end]
        if len(key) < min_len and end < len(texts[i]) - start:
            continue
        if sum(1 for t2 in texts if key in t2) == 1:
            return key
    return None


def segment(path, min_key=12):
    doc, has_comments, revised_parts = load(path)
    body = doc.find(q("body"))
    paras = list(body.iter(q("p")))  # same set the redliner searches, so keys are unique there too
    fulls = [full_text(p) for p in paras]
    counters = {}
    sections = []
    for i, p in enumerate(paras):
        if any(a.tag == q("txbxContent") for a in p.iterancestors()):
            continue  # text boxes are counted for uniqueness but not listed as clauses
        vis = visible_text(p).strip()
        if not vis:
            continue
        num, auto = None, False
        numpr = p.find("%s/%s" % (q("pPr"), q("numPr")))
        if numpr is not None:
            nid = numpr.find(q("numId"))
            lvl = numpr.find(q("ilvl"))
            nid = nid.get(q("val")) if nid is not None else "0"
            lvl = int(lvl.get(q("val"))) if lvl is not None else 0
            if nid != "0":
                levels = counters.setdefault(nid, [0] * 9)
                levels[lvl] += 1
                for k in range(lvl + 1, 9):
                    levels[k] = 0
                num = ".".join(str(levels[k]) for k in range(lvl + 1) if levels[k]) or None
                auto = True
        m = NUM_RE.match(vis)
        rest = vis
        if m and not auto:
            num = m.group("num").rstrip(".")
            rest = vis[m.end():]
        h = HEADING_RE.match(rest)
        heading = h.group("h").strip() if h else None
        key = unique_key(i, fulls, min_key)
        if key is None:
            print(json.dumps({"warning": f"paragraph {i} has no unique key", "text": vis[:60]}), file=sys.stderr)
        sec = {"index": i, "number": num, "heading": heading, "para_key": key, "auto_numbered": auto, "text": vis}
        hidden = hidden_text(p)
        if hidden.strip():
            sec["hidden_text"] = hidden  # invisible in Word: treat as suspect, tell the user
        sections.append(sec)
    return {"paragraphs": len(paras), "has_tracked_changes": bool(revised_parts), "revised_parts": revised_parts,
            "has_comments": has_comments, "has_hidden_text": any("hidden_text" in s for s in sections),
            "sections": sections}


def main(argv=None):
    ap = argparse.ArgumentParser(description="Map a .docx into sections with unique paragraph keys.")
    ap.add_argument("docx")
    ap.add_argument("--min-key", type=int, default=12)
    a = ap.parse_args(argv)
    try:
        result = segment(a.docx, a.min_key)
    except (OSError, ValueError, zipfile.BadZipFile, etree.XMLSyntaxError) as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 2
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
