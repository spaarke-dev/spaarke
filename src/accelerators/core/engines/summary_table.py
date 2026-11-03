"""summary_table.py - insert the page-1 "Summary of Proposed Revisions" into a redline.

Python 3 standard library + lxml only. Produces the INTERNAL redline: never send it externally.

Usage:
  python summary_table.py PLAN.json REDLINE.docx [--out INTERNAL.docx] [--intro summary-intro.md]

Reads from the plan:
  agreement {title, parties[]}, provenance (the standards + skill line, shown under the subtitle),
  review_basis {playbook, version, side, posture},
  edits[].summary {n, section, bookmark, issue, revision, priority}, unchanged[] (provisions
  reviewed with no change).
The intro file may use {{review_basis}}, {{side}}, {{posture}}, {{revision_count}} placeholders.

Layout (POC Appendix E): Arial; title 14 pt bold navy 1F3864; italic subtitle; intro; table
# | Section | Issue | Proposed Revision | Priority with grid 400/1900/2600/3100/880 twips,
navy header row repeated on each page, 8.5 pt body, alternate rows F2F2F2, horizontal rules
A6A6A6, cell margins 30/70 twips, Section cell hyperlinked to its bookmark, contrast-safe
priority colours; italic "no change" note; page break. Everything is wrapped in the bookmark
"Sprk_Summary" so check_outputs.py can find (and ignore) it.
Exit codes: 0 ok, 2 bad input, 3 plan inconsistent with the document.
"""
import argparse
import json
import os
import shutil
import sys
import tempfile
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from lxml import etree
    import safe_docx
except ImportError as _ex:
    print(json.dumps({"error": f"cannot load lxml/safe_docx: {_ex}"}), file=sys.stderr)
    sys.exit(2)

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
NAVY, RULE, ALT, LINK = "1F3864", "A6A6A6", "F2F2F2", "0563C1"
# High/Medium/Low pass 4.5:1 on white and on F2F2F2 (check_outputs.py verifies)
PRIORITY_COLOURS = {"High": "C00000", "Medium": "7F6000", "Low": "375623"}
GRID = [400, 1900, 2600, 3100, 880]
HEADERS = ["#", "Section", "Issue", "Proposed Revision", "Priority"]
SUMMARY_BOOKMARK = "Sprk_Summary"
DEFAULT_INTRO = ("This page is an internal review aid. It lists each proposed revision, linked to the clause, "
                 "based on {{review_basis}}, for the {{side}} party with a {{posture}} posture. "
                 "Remove this page and the margin comments before sending the agreement to the other side.")


class PlanMismatch(Exception):
    """The plan's summary rows do not fit the document (exit 3)."""


def q(tag):
    return "{%s}%s" % (W, tag)


def el(tag, attrs=None, children=()):
    e = etree.Element(q(tag))
    for k, v in (attrs or {}).items():
        e.set(q(k), str(v))
    for c in children:
        e.append(c)
    return e


def rpr(size_half_pts=17, bold=False, italic=False, colour=None, underline=False):
    kids = [el("rFonts", {"ascii": "Arial", "hAnsi": "Arial", "cs": "Arial"})]
    if bold:
        kids.append(el("b"))
    if italic:
        kids.append(el("i"))
    if colour:
        kids.append(el("color", {"val": colour}))
    kids.append(el("sz", {"val": size_half_pts}))
    kids.append(el("szCs", {"val": size_half_pts}))
    if underline:
        kids.append(el("u", {"val": "single"}))
    return el("rPr", children=kids)


def run(text, **fmt):
    t = el("t")
    t.text = text
    t.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    return el("r", children=[rpr(**fmt), t])


def para(runs, after=80, keep_next=False):
    ppr = [el("spacing", {"before": 0, "after": after})]
    if keep_next:
        ppr.insert(0, el("keepNext"))
    return el("p", children=[el("pPr", children=ppr)] + list(runs))


def cell(width, content_runs, fill=None):
    tcpr = [el("tcW", {"w": width, "type": "dxa"})]
    if fill:
        tcpr.append(el("shd", {"val": "clear", "color": "auto", "fill": fill}))
    p = el("p", children=[el("pPr", children=[el("spacing", {"before": 0, "after": 0})])] + list(content_runs))
    return el("tc", children=[el("tcPr", children=tcpr), p])


def border(side):
    return el(side, {"val": "single", "sz": 4, "space": 0, "color": RULE})


def nil(side):
    return el(side, {"val": "nil"})


def build_table(rows):
    # tblBorders children in schema order: top, left, bottom, right, insideH, insideV
    borders = el("tblBorders", children=[border("top"), nil("left"), border("bottom"), nil("right"),
                                         border("insideH"), nil("insideV")])
    margins = el("tblCellMar", children=[el("top", {"w": 30, "type": "dxa"}), el("left", {"w": 70, "type": "dxa"}),
                                         el("bottom", {"w": 30, "type": "dxa"}), el("right", {"w": 70, "type": "dxa"})])
    tblpr = el("tblPr", children=[el("tblW", {"w": sum(GRID), "type": "dxa"}), borders,
                                  el("tblLayout", {"type": "fixed"}), margins,
                                  el("tblLook", {"val": "04A0", "firstRow": 1, "lastRow": 0, "firstColumn": 0,
                                                 "lastColumn": 0, "noHBand": 0, "noVBand": 1})])
    grid = el("tblGrid", children=[el("gridCol", {"w": w}) for w in GRID])
    tbl = el("tbl", children=[tblpr, grid])
    head = el("tr", children=[el("trPr", children=[el("tblHeader"), el("cantSplit")])])
    for w, h in zip(GRID, HEADERS):
        head.append(cell(w, [run(h, bold=True, colour="FFFFFF")], fill=NAVY))
    tbl.append(head)
    for i, r in enumerate(rows):
        fill = ALT if i % 2 else None
        tr = el("tr", children=[el("trPr", children=[el("cantSplit")])])
        tr.append(cell(GRID[0], [run(str(r["n"]))], fill))
        link = el("hyperlink", {"anchor": r["bookmark"], "history": 1},
                  children=[run(r["section"], colour=LINK, underline=True)])
        tr.append(cell(GRID[1], [link], fill))
        tr.append(cell(GRID[2], [run(r["issue"])], fill))
        tr.append(cell(GRID[3], [run(r["revision"])], fill))
        tr.append(cell(GRID[4], [run(r["priority"], bold=True, colour=PRIORITY_COLOURS[r["priority"]])], fill))
        tbl.append(tr)
    return tbl


def collect_rows(plan, bookmark_names):
    rows = [dict(e["summary"]) for e in plan.get("edits", []) if e.get("summary")]
    errors = []
    for r in rows:
        for k in ("n", "section", "bookmark", "issue", "revision", "priority"):
            if r.get(k) in (None, ""):
                errors.append(f"summary row {r.get('n')!r} is missing {k!r}")
        if r.get("priority") not in PRIORITY_COLOURS:
            errors.append(f"summary row {r.get('n')!r} priority {r.get('priority')!r} is not High/Medium/Low")
        if r.get("bookmark") and r["bookmark"] not in bookmark_names:
            errors.append(f"summary row {r.get('n')!r} links to bookmark {r.get('bookmark')!r}, which the document lacks")
    ns = [r.get("n") for r in rows]
    if ns != list(range(1, len(rows) + 1)):
        errors.append(f"summary rows must be numbered 1..{len(rows)} in plan order; got {ns}")
    return rows, errors


def fill_intro(template, plan, count):
    rb = plan.get("review_basis", {})
    if rb.get("playbook"):
        basis = f"the {rb['playbook']} playbook (version {rb.get('version') or 'unversioned'})"
    elif rb.get("source") == "spaarke-baseline":
        basis = f"the Spaarke baseline positions, not tailored to your organisation (version {rb.get('version') or 'unversioned'})"
    else:
        basis = "general practice (no firm playbook available)"
    values = {"review_basis": basis, "side": rb.get("side", "unspecified"),
              "posture": rb.get("posture", "balanced"), "revision_count": str(count)}
    for k, v in values.items():
        template = template.replace("{{%s}}" % k, v)
    return template.strip()


def insert_summary(doc, plan, intro):
    body = doc.find(q("body"))
    names = {b.get(q("name")) for b in doc.iter(q("bookmarkStart"))}
    if SUMMARY_BOOKMARK in names:
        raise ValueError("document already has a summary page")
    rows, errors = collect_rows(plan, names)
    if errors:
        raise PlanMismatch(errors)
    ag = plan.get("agreement", {})
    block = [para([run("Summary of Proposed Revisions", size_half_pts=28, bold=True, colour=NAVY)], after=60)]
    sub = ag.get("title", "Agreement")
    if ag.get("parties"):
        sub += " – " + " and ".join(ag["parties"])
    block.append(para([run(sub, size_half_pts=18, italic=True)], after=60))
    if plan.get("provenance"):
        block.append(para([run(plan["provenance"], size_half_pts=16, italic=True, colour="404040")], after=60))
    block.append(para([run(fill_intro(intro, plan, len(rows)), size_half_pts=17)], after=120))
    block.append(build_table(rows))
    unchanged = plan.get("unchanged") or []
    if unchanged:
        block.append(para([run("Reviewed with no change: " + "; ".join(unchanged) + ".", size_half_pts=16,
                               italic=True)], after=0))
    brk = el("r", children=[el("br", {"type": "page"})])
    block.append(el("p", children=[brk]))
    first = body[0]
    for b in block:
        first.addprevious(b)
    bid = str(max([int(v) for v in doc.xpath("//@w:id", namespaces={"w": W}) if v.lstrip("-").isdigit()] + [0]) + 1)
    start_p = block[0]
    start_p.insert(1, el("bookmarkStart", {"id": bid, "name": SUMMARY_BOOKMARK}))
    block[-1].append(el("bookmarkEnd", {"id": bid}))
    return len(rows)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Insert the page-1 summary of proposed revisions.")
    ap.add_argument("plan")
    ap.add_argument("redline")
    ap.add_argument("--out", help="write here instead of updating REDLINE in place")
    ap.add_argument("--intro", help="intro template (assets/summary-intro.md)")
    a = ap.parse_args(argv)
    try:
        with open(a.plan, encoding="utf-8") as f:
            plan = json.load(f)
        intro = DEFAULT_INTRO
        if a.intro:
            with open(a.intro, encoding="utf-8") as f:
                intro = f.read()
        parts = safe_docx.read_parts(a.redline)
        doc = safe_docx.parse(parts["word/document.xml"])
        count = insert_summary(doc, plan, intro)
    except PlanMismatch as ex:
        print(json.dumps({"error": "plan does not fit the document", "details": ex.args[0]}), file=sys.stderr)
        return 3
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, etree.XMLSyntaxError) as ex:
        print(json.dumps({"error": str(ex)}), file=sys.stderr)
        return 2
    parts["word/document.xml"] = etree.tostring(doc, xml_declaration=True, encoding="UTF-8", standalone=True)
    target = a.out or a.redline
    with tempfile.NamedTemporaryFile(delete=False, suffix=".docx") as tmp:
        tmp_path = tmp.name
    with zipfile.ZipFile(tmp_path, "w", zipfile.ZIP_DEFLATED) as zo:
        for n in sorted(parts, key=lambda n: n != "[Content_Types].xml"):
            zo.writestr(n, parts[n])
    shutil.move(tmp_path, target)
    print(json.dumps({"rows": count, "out": target}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
