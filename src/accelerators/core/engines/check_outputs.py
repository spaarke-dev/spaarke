"""check_outputs.py - blocking checks on a redline package before it is delivered.

Python 3 standard library + lxml only. Imports ooxml_redline.py and safe_docx.py from the same folder.

Usage:
  python check_outputs.py PLAN.json INTERNAL.docx CLEAN.docx --original ORIGINAL.docx
         [--external EXTERNAL.docx]

Checks (each reported as {"name", "ok", "detail"}):
  plan               the plan passes the closed-set contract (no unknown or misspelt keys)
  package            every output opens; relationship targets exist; comments part registered
  untracked_edits    original text == redline with every revision rejected (summary ignored)
  existing_revisions every revision (insert, delete, move, formatting) already in the original
                     is still present, unchanged
  revision_text      our insertions and deletions carry exactly the text the plan asks for
  comments           our comments == the plan's comments (count and text); every reference resolves
  summary            summary present; rows == plan rows; bookmark names unique; every internal link
                     resolves; priority colours >= 4.5:1
  clean              no revisions or comments in any story (body, headers, footers, notes); text ==
                     an independent "accept all" of the redline (with the same drawing strip);
                     tracking off; no summary
  external           (if given) no summary; none of our comments; comments intact; same revisions
  residue            no {{placeholders}} beyond those already in the original
Exit codes: 0 all checks pass, 1 a check failed, 2 bad input.
"""
import argparse
import copy
import json
import os
import posixpath
import re
import sys
import zipfile
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from lxml import etree
    import safe_docx
    from ooxml_redline import PlanError, STORY_PART, q, strip_drawings, validate_plan, W
except ImportError as ex:
    print(json.dumps({"error": f"cannot import dependencies: {ex}"}), file=sys.stderr)
    sys.exit(2)

SUMMARY_BOOKMARK = "Sprk_Summary"
BACKGROUNDS = {"white": "FFFFFF", "alternate row": "F2F2F2"}
REVISION_TAGS = ("ins", "del", "moveFrom", "moveTo")
PROPERTY_REVISIONS = ("rPrChange", "pPrChange", "sectPrChange", "tblPrChange", "trPrChange", "tcPrChange")


class Pkg:
    def __init__(self, path):
        self.path = path
        self.parts = safe_docx.read_parts(path)
        self.doc = safe_docx.parse(self.parts["word/document.xml"])
        self.comments = safe_docx.parse(self.parts["word/comments.xml"]) if "word/comments.xml" in self.parts else None

    def comment_ids(self):
        return set() if self.comments is None else {c.get(q("id")) for c in self.comments.findall(q("comment"))}

    def comment_texts(self, ids):
        out = Counter()
        if self.comments is None:
            return out
        for c in self.comments.findall(q("comment")):
            if c.get(q("id")) in ids:
                out["".join(t.text or "" for t in c.iter(q("t")))] += 1
        return out

    def stories(self):
        return {n: safe_docx.parse(d) for n, d in self.parts.items() if STORY_PART.match(n)}


def strip_summary(root):
    root = copy.deepcopy(root)
    body = root.find(q("body"))
    start = [b for b in root.iter(q("bookmarkStart")) if b.get(q("name")) == SUMMARY_BOOKMARK]
    if not start:
        return root
    bid = start[0].get(q("id"))
    end = [b for b in root.iter(q("bookmarkEnd")) if b.get(q("id")) == bid]

    def top(e):
        while e.getparent() is not body:
            e = e.getparent()
        return e
    first, last = top(start[0]), top(end[0]) if end else top(start[0])
    kids = list(body)
    for e in kids[kids.index(first):kids.index(last) + 1]:
        body.remove(e)
    return root


def reject_all(root):
    root = copy.deepcopy(root)
    for tag in ("ins", "moveTo"):
        for e in list(root.iter(q(tag))):
            if e.getparent() is not None and e.getparent().tag != q("rPr"):
                e.getparent().remove(e)
    for tag in ("del", "moveFrom"):
        for e in list(root.iter(q(tag))):
            parent = e.getparent()
            if parent is None or parent.tag in (q("rPr"), q("trPr")):
                continue
            idx = parent.index(e)
            for ch in list(e):
                parent.insert(idx, ch)
                idx += 1
            parent.remove(e)
    for e in root.iter(q("delText")):
        e.tag = q("t")
    return root


def _text(el, include_deleted=False):
    s = []
    tags = (q("t"), q("tab"), q("br")) + ((q("delText"),) if include_deleted else ())
    for e in el.iter(*tags):
        if e.tag in (q("t"), q("delText")):
            s.append(e.text or "")
        else:
            s.append("\t" if e.tag == q("tab") else "\n")
    return "".join(s)


def para_texts(root):
    return [_text(p, include_deleted=True) for p in root.find(q("body")).iter(q("p"))]


def accepted_texts(root):
    """Independent oracle for "accept all": works on text, never on the tree surgery the engine uses."""
    out, carry = [], None
    for p in root.find(q("body")).iter(q("p")):
        if any(a.tag == q("tr") and a.find("%s/%s" % (q("trPr"), q("del"))) is not None for a in p.iterancestors()):
            continue
        parts = []
        for e in p.iter(q("t"), q("tab"), q("br")):
            if any(a.tag in (q("del"), q("moveFrom")) for a in e.iterancestors()):
                continue
            parts.append((e.text or "") if e.tag == q("t") else ("\t" if e.tag == q("tab") else "\n"))
        text = (carry or "") + "".join(parts)
        carry = None
        nxt = p.getnext()
        if p.find("%s/%s/%s" % (q("pPr"), q("rPr"), q("del"))) is not None and nxt is not None and nxt.tag == q("p"):
            carry = text
            continue
        out.append(text)
    if carry is not None:
        out.append(carry)
    return out


def first_diff(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            return f"paragraph {i}: expected {x[:80]!r}, found {y[:80]!r}"
    if len(a) != len(b):
        return f"paragraph count {len(a)} vs {len(b)}"
    return None


def revisions(root):
    """Multiset of content revisions (tag, author, date, text) and set of property revisions."""
    sig = Counter()
    for tag in REVISION_TAGS:
        for e in root.iter(q(tag)):
            if e.getparent() is not None and e.getparent().tag in (q("rPr"), q("trPr")):
                sig[(tag + "-mark", e.get(q("author")), e.get(q("date")), "")] += 1
                continue
            sig[(tag, e.get(q("author")), e.get(q("date")), _text(e, include_deleted=True))] += 1
    props = {(tag, e.get(q("author")), e.get(q("date"))) for tag in PROPERTY_REVISIONS for e in root.iter(q(tag))}
    return sig, props


def luminance(hex6):
    def ch(v):
        v = v / 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (int(hex6[i:i + 2], 16) for i in (0, 2, 4))
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)


def contrast(fg, bg):
    hi, lo = sorted((luminance(fg), luminance(bg)), reverse=True)
    return (hi + 0.05) / (lo + 0.05)


def expected_text(plan):
    """What our revisions must contain: inserted texts, deleted texts, and the number of '*' deletions."""
    ins, dels, wild = Counter(), Counter(), 0
    for e in plan.get("edits", []):
        if "after" in e:
            ins[e["insert"]] += 1
        elif "replace" in e:
            if e["find"] == "*":
                wild += 1
            else:
                dels[re.sub(r"[\t\n]", "", e.get("old", e["find"]))] += 1
            if e["replace"]:
                ins[e["replace"]] += 1
    return ins, dels, wild


def comment_integrity(pkg):
    refs = {e.get(q("id")) for e in pkg.doc.iter(q("commentReference"))}
    for root in pkg.stories().values():
        refs |= {e.get(q("id")) for e in root.iter(q("commentReference"))}
    ids = pkg.comment_ids()
    return refs - ids, ids - refs


def check(plan, internal, clean, original, external=None):
    results = []

    def add(name, ok, detail=""):
        results.append({"name": name, "ok": bool(ok), "detail": detail})

    try:
        validate_plan(plan)
        add("plan", True, "plan matches the contract")
    except PlanError as ex:
        add("plan", False, str(ex))

    pk = {"internal": internal, "clean": clean, "original": original}
    if external:
        pk["external"] = external
    pkgs, problems = {}, []
    for label, path in pk.items():
        try:
            p = Pkg(path)
        except (OSError, KeyError, ValueError, zipfile.BadZipFile, etree.XMLSyntaxError) as ex:
            problems.append(f"{label}: {ex}")
            continue
        pkgs[label] = p
        rels = p.parts.get("word/_rels/document.xml.rels")
        if rels is not None:
            for r in safe_docx.parse(rels):
                if r.get("TargetMode") == "External":
                    continue
                t = r.get("Target")
                target = t.lstrip("/") if t.startswith("/") else posixpath.normpath(posixpath.join("word", t))
                if target not in p.parts:
                    problems.append(f"{label}: relationship target {t} missing")
        if p.comments is not None and b"/word/comments.xml" not in p.parts.get("[Content_Types].xml", b""):
            problems.append(f"{label}: comments.xml not registered in [Content_Types].xml")
    add("package", not problems, "; ".join(problems) or "all outputs open and are consistent")
    if any(k not in pkgs for k in ("internal", "clean", "original")):
        return results
    red, cln, org = pkgs["internal"], pkgs["clean"], pkgs["original"]
    red_body = strip_summary(red.doc)

    diff = first_diff(para_texts(reject_all(org.doc)), para_texts(reject_all(red_body)))
    add("untracked_edits", diff is None, diff or "every change in the redline is a tracked change")

    (org_revs, org_props), (red_revs, red_props) = revisions(org.doc), revisions(red_body)
    missing = org_revs - red_revs
    lost_props = org_props - red_props
    add("existing_revisions", not missing and not lost_props,
        f"{sum(missing.values())} original revision(s) altered or lost; property revisions lost: {sorted(lost_props)}"
        if missing or lost_props else f"{sum(org_revs.values())} original revision(s) untouched")

    ours = red_revs - org_revs
    got_ins = Counter({k[3]: v for k, v in ours.items() if k[0] == "ins"})
    got_del = Counter()
    for k, v in ours.items():
        if k[0] == "del":
            got_del[re.sub(r"[\t\n]", "", k[3])] += v
    want_ins, want_del, wild = expected_text(plan)
    other = [k for k in ours if k[0] not in ("ins", "del")]
    extra_del = got_del - want_del
    rt_ok = got_ins == want_ins and not (want_del - got_del) and sum(extra_del.values()) == wild and not other
    add("revision_text", rt_ok,
        f"insertions {sum(got_ins.values())}/{sum(want_ins.values())}, deletions {sum(got_del.values())}/"
        f"{sum(want_del.values()) + wild}; unexpected insertions {sorted((got_ins - want_ins).elements())[:3]}; "
        f"missing deletions {sorted((want_del - got_del).elements())[:3]}; other new revisions {other[:3]}")

    new_ids = red.comment_ids() - org.comment_ids()
    want_c = Counter(e["comment"] for e in plan.get("edits", []) if e.get("comment"))
    got_c = red.comment_texts(new_ids)
    dangling, unanchored = comment_integrity(red)
    add("comments", got_c == want_c and not dangling and not unanchored,
        f"new comments {sum(got_c.values())}/{sum(want_c.values())}, text match {got_c == want_c}; "
        f"dangling references {sorted(dangling)}; unanchored comments {sorted(unanchored)}")

    s_problems = []
    names = [b.get(q("name")) for b in red.doc.iter(q("bookmarkStart"))]
    dup = [n for n, c in Counter(names).items() if c > 1]
    if dup:
        s_problems.append(f"duplicate bookmark names {dup}")
    if SUMMARY_BOOKMARK not in names:
        s_problems.append("internal redline has no summary page")
    else:
        body = red.doc.find(q("body"))
        start = next(b for b in red.doc.iter(q("bookmarkStart")) if b.get(q("name")) == SUMMARY_BOOKMARK)
        while start.getparent() is not body:
            start = start.getparent()
        summary_tbl = next((e for e in start.itersiblings() if e.tag == q("tbl")), None)
        body_rows = len(summary_tbl.findall(q("tr"))) - 1 if summary_tbl is not None else 0
        want_rows = sum(1 for e in plan.get("edits", []) if e.get("summary"))
        if body_rows != want_rows:
            s_problems.append(f"summary rows {body_rows}/{want_rows}")
        if summary_tbl is not None:
            for r in summary_tbl.iter(q("r")):
                c = r.find("%s/%s" % (q("rPr"), q("color")))
                t = "".join(x.text or "" for x in r.iter(q("t")))
                if c is not None and t in ("High", "Medium", "Low"):
                    for bg_name, bg in BACKGROUNDS.items():
                        ratio = contrast(c.get(q("val")), bg)
                        if ratio < 4.5:
                            s_problems.append(f"{t} colour {c.get(q('val'))} is {ratio:.2f}:1 on {bg_name}")
    for h in red.doc.iter(q("hyperlink")):
        a = h.get(q("anchor"))
        if a and a not in names:
            s_problems.append(f"link to missing bookmark {a}")
    add("summary", not s_problems, "; ".join(sorted(set(s_problems))) or "summary present, linked and legible")

    c_problems = []
    strip = bool(plan.get("strip_drawings_in_clean"))
    stories = {"word/document.xml": cln.doc, **cln.stories()}
    for name, root in stories.items():
        for tag in REVISION_TAGS + PROPERTY_REVISIONS + ("commentRangeStart", "commentReference"):
            if root.find(".//" + q(tag)) is not None:
                c_problems.append(f"clean copy {name} contains w:{tag}")
        if strip and any(root.find(".//" + t) is not None for t in
                         (q("drawing"), q("pict"), "{http://schemas.openxmlformats.org/markup-compatibility/2006}AlternateContent")):
            c_problems.append(f"clean copy {name} still has drawings")
    if cln.comments is not None:
        c_problems.append("clean copy has a comments part")
    if SUMMARY_BOOKMARK in {b.get(q("name")) for b in cln.doc.iter(q("bookmarkStart"))}:
        c_problems.append("clean copy contains the summary page")
    if b"trackRevisions" in cln.parts.get("word/settings.xml", b""):
        c_problems.append("clean copy has tracking switched on")
    oracle_root = strip_drawings(copy.deepcopy(red_body)) if strip else red_body
    diff = first_diff(accepted_texts(oracle_root), para_texts(cln.doc))
    if diff:
        c_problems.append("clean text differs from the redline with all changes accepted: " + diff)
    add("clean", not c_problems, "; ".join(c_problems) or "clean copy is the redline with every change accepted")

    if "external" in pkgs:
        ext = pkgs["external"]
        e_problems = []
        if SUMMARY_BOOKMARK in {b.get(q("name")) for b in ext.doc.iter(q("bookmarkStart"))}:
            e_problems.append("external redline contains the summary page")
        if ext.comment_ids() - org.comment_ids():
            e_problems.append("external redline contains review comments")
        e_dangling, e_unanchored = comment_integrity(ext)
        if e_dangling or e_unanchored:
            e_problems.append(f"external comments broken: dangling {sorted(e_dangling)}, unanchored {sorted(e_unanchored)}")
        if revisions(ext.doc) != (red_revs, red_props):
            e_problems.append("external revisions differ from the internal redline")
        add("external", not e_problems, "; ".join(e_problems) or "external redline: revisions only")

    base = len(re.findall(r"\{\{|\}\}", "".join(para_texts(org.doc))))
    leftovers = []
    for label, p in pkgs.items():
        if label == "original":
            continue
        n = len(re.findall(r"\{\{|\}\}", "".join(para_texts(p.doc))))
        if n > base:
            leftovers.append(label)
    add("residue", not leftovers, f"new unfilled placeholders in {leftovers}" if leftovers else "no new placeholders")
    return results


def main(argv=None):
    ap = argparse.ArgumentParser(description="Blocking checks on a redline package.")
    ap.add_argument("plan")
    ap.add_argument("internal")
    ap.add_argument("clean")
    ap.add_argument("--original", required=True)
    ap.add_argument("--external")
    a = ap.parse_args(argv)
    try:
        with open(a.plan, encoding="utf-8") as f:
            plan = json.load(f)
    except (OSError, ValueError) as ex:
        print(json.dumps({"error": f"cannot read plan: {ex}"}), file=sys.stderr)
        return 2
    results = check(plan, a.internal, a.clean, a.original, a.external)
    ok = all(r["ok"] for r in results)
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps({"ok": ok, "checks": results}, ensure_ascii=False, indent=1))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
