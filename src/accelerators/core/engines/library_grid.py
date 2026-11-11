"""library_grid.py - assemble per-document reviews into a library grid: documents x playbook topics.

Python 3 standard library only (+ sibling review_visuals, xlsx_lite). The model reviews one document
at a time and writes `<document stem>.review.json` (same shape as review.json in nda-review); this
script validates every file, checks each quote against the document's own text, and writes:
  - an Excel grid (rows = documents, columns = topics, cell = rating + clause; detail, by-topic chart);
  - a static HTML heatmap (no script) with counts, the documents needing attention and the top issues.

Usage:
  python library_grid.py REVIEWS_DIR --topics topics.json [--clauses-dir DIR]
         [--xlsx grid.xlsx] [--html dashboard.html] [--base-url URL] [--title TEXT]

--clauses-dir holds `<document stem>.clauses.json` from segment_clauses.py. Quotes are checked
against that text: "verified", "NOT FOUND", or "unchecked" (no text, e.g. a PDF).
--base-url: folder URL the documents live in (team site or OneDrive); each row links to its file.

Output (JSON on stdout): {"ok": bool, "documents": n, "cells": {...}, "citations": {...}, "problems": [...],
"warnings": [...], "outputs": [...]}
Exit codes: 0 ok, 2 bad input (a review file is unreadable or invalid; nothing is written).
"""
import argparse
import datetime
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import review_visuals as rv  # noqa: E402
import xlsx_lite  # noqa: E402

VERSION = rv.VERSION
LETTER = {"RED": "R", "YELLOW": "Y", "GREEN": "G", "N/A": "–"}


def norm(s):
    s = unicodedata.normalize("NFKC", s or "").lower()
    s = re.sub(r"[‘’‚‛`´]", "'", s)
    s = re.sub(r"[“”„‟]", '"', s)
    s = re.sub(r"[‐-―−]", "-", s)
    return re.sub(r"\s+", " ", s).strip()


def check_quote(quote, text):
    if text is None:
        return "unchecked"
    parts = [norm(p) for p in re.split(r"\.\.\.|…|\[\.\.\.\]", quote or "") if norm(p).strip(" .,;:\"'")]
    if not parts:
        return "unchecked"
    return "verified" if all(p.strip(" .,;:\"'") in text for p in parts) else "NOT FOUND"


def url_quote(s):
    return "".join(c if re.match(r"[A-Za-z0-9._~/-]", c) else "".join(f"%{b:02X}" for b in c.encode("utf-8")) for c in s)


def load_reviews(folder, catalogue):
    docs, probs, warns = [], [], []
    files = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".review.json")) \
        if os.path.isdir(folder) else []
    if not files:
        probs.append(f"no *.review.json files in {folder}")
    for path in files:
        try:
            r = rv.load_json(path, "review")
        except rv.BadInput as ex:
            probs.append(str(ex))
            continue
        p, w = rv.validate_review(r, catalogue)
        probs += [f"{os.path.basename(path)}: {x}" for x in p]
        warns += [f"{os.path.basename(path)}: {x}" for x in w]
        r["_path"] = path
        docs.append(r)
    return docs, probs, warns


def attach_citations(docs, clauses_dir):
    totals = {"verified": 0, "NOT FOUND": 0, "unchecked": 0}
    for d in docs:
        text = None
        if clauses_dir:
            stem = os.path.splitext(d["document"]["file_name"])[0]
            cpath = os.path.join(clauses_dir, stem + ".clauses.json")
            if os.path.isfile(cpath):
                cl = rv.load_json(cpath, "clauses")
                text = norm(" ".join(s.get("text", "") for s in cl.get("sections", [])))
        for t in d["topics"]:
            t["_cite"] = check_quote(t.get("quote"), text) if t.get("quote") else "no quote"
            if t["_cite"] in totals:
                totals[t["_cite"]] += 1
    return totals


def doc_link(d, base_url):
    if d["document"].get("url"):
        return d["document"]["url"]
    if base_url:
        return base_url.rstrip("/") + "/" + url_quote(d["document"]["file_name"])
    return None


def doc_counts(d):
    c = {k: 0 for k in rv.RATINGS}
    for t in d["topics"]:
        c[t["rating"]] += 1
    return c


def render_html(docs, catalogue, title, base_url, totals):
    topics = list(catalogue)
    docs = sorted(docs, key=lambda d: (-doc_counts(d)["RED"], -doc_counts(d)["YELLOW"], d["document"]["file_name"]))
    all_cells = [t for d in docs for t in d["topics"]]
    by = {k: sum(1 for t in all_cells if t["rating"] == k) for k in rv.RATINGS}
    standards = sorted({d.get("provenance", {}).get("standards", "") for d in docs} - {""})
    checked = totals["verified"] + totals["NOT FOUND"]
    b = [f"<h1>{rv.esc(title)}</h1>",
         f"<p class='sub'>{len(docs)} documents x {len(topics)} playbook topics · {rv.esc('; '.join(standards))}</p>",
         "<p class='note'>Draft for attorney review; not legal advice. Each cell is the model's classification of one "
         "document against one playbook position; quotes are checked against the document text by script.</p>",
         '<div class="tiles">' + rv.tile(len(docs), "documents") + rv.tile(by["RED"], "RED cells")
         + rv.tile(by["YELLOW"], "YELLOW cells") + rv.tile(by["GREEN"], "GREEN cells")
         + rv.tile(f"{totals['verified']}/{checked}" if checked else "n/a", "quotes found in the text")
         + rv.tile(totals["unchecked"], "quotes not checkable") + "</div>"]
    red_by_topic = sorted(((catalogue[t]["title"], sum(1 for d in docs for x in d["topics"] if x["topic"] == t and x["rating"] == "RED"))
                           for t in topics), key=lambda x: -x[1])
    top = [(n, v, rv.COLOR["RED"], f"{n}: RED in {v} document(s)") for n, v in red_by_topic if v][:10]
    attention = [(os.path.splitext(d["document"]["file_name"])[0], doc_counts(d)["RED"], rv.COLOR["RED"],
                  f"{d['document']['file_name']}: {doc_counts(d)['RED']} RED, {doc_counts(d)['YELLOW']} YELLOW") for d in docs
                 if doc_counts(d)["RED"]][:10]
    b.append('<div class="row"><div class="card"><h2 style="margin-top:0">Topics most often RED</h2>'
             + (rv.hbar_svg(top, "Topics most often RED") if top else "<p>No RED cells.</p>")
             + '</div><div class="card"><h2 style="margin-top:0">Documents with most RED topics</h2>'
             + (rv.hbar_svg(attention, "Documents with most RED topics") if attention else "<p>No RED cells.</p>") + "</div></div>")
    # heatmap
    h = ['<h2>Heatmap</h2><div class="card heat"><table><tr><th></th>'
         + "".join(f'<th class="v" title="{rv.esc(t)}">{rv.esc(catalogue[t]["title"])}</th>' for t in topics) + "</tr>"]
    for d in docs:
        name = d["document"]["file_name"]
        link = doc_link(d, base_url)
        label = f'<a href="{rv.esc(link)}">{rv.esc(name)}</a>' if link else rv.esc(name)
        cells = {t["topic"]: t for t in d["topics"]}
        row = [f'<tr><td class="doc">{label}</td>']
        for tid in topics:
            t = cells.get(tid)
            if not t:
                row.append('<td class="cell c-NA" title="not reviewed">?</td>')
                continue
            cls = "NA" if t["rating"] == "N/A" else t["rating"]
            mark = LETTER[t["rating"]] + ("*" if t["_cite"] == "NOT FOUND" else "")
            tip = (f"{name} · {catalogue[tid]['title']}: {t['rating']} ({t.get('status')})"
                   f"{' · ' + t['clause'] if t.get('clause') else ''}"
                   f"{' · ' + chr(8220) + t['quote'] + chr(8221) if t.get('quote') else ''}"
                   f"{' · ' + t['note'] if t.get('note') else ''} · quote {t['_cite']}")
            row.append(f'<td class="cell c-{cls}" title="{rv.esc(tip)}">{mark}</td>')
        h.append("".join(row) + "</tr>")
    h.append("</table></div><p class='note'>R = RED, Y = YELLOW, G = GREEN. * = the quote was not found in the document "
             "text. Hover a cell for the clause, quote and note.</p>")
    b.append("".join(h))
    missing = [(d["document"]["file_name"], t) for d in docs for t in d["topics"] if t["_cite"] == "NOT FOUND"]
    if missing:
        b.append("<h2>Quotes not found in the document</h2><div class='card'><table><tr><th>Document</th><th>Topic</th>"
                 "<th>Rating</th><th>Quote</th></tr>" + "".join(
                     f"<tr><td>{rv.esc(n)}</td><td>{rv.esc(catalogue[t['topic']]['title'])}</td><td>{rv.chip(t['rating'])}</td>"
                     f"<td class='q'>{rv.esc(t.get('quote'))}</td></tr>" for n, t in missing) + "</table></div>")
    b.append(f"<p class='note'>Generated {datetime.datetime.now().strftime('%Y-%m-%d %H:%M')} by library_grid.py v{VERSION}.</p>")
    return rv.page(title, "".join(b))


def render_xlsx(docs, catalogue, title, base_url, path):
    topics = list(catalogue)
    docs = sorted(docs, key=lambda d: d["document"]["file_name"])
    wb = xlsx_lite.Workbook()
    g = wb.sheet("Grid")
    g.append(["Document", "RED", "YELLOW", "GREEN"] + [catalogue[t]["title"] for t in topics], style="header")
    for d in docs:
        c, cells = doc_counts(d), {t["topic"]: t for t in d["topics"]}
        link = doc_link(d, base_url)
        row = [{"v": d["document"]["file_name"], "link": link} if link else d["document"]["file_name"], c["RED"], c["YELLOW"], c["GREEN"]]
        for tid in topics:
            t = cells.get(tid)
            if not t:
                row.append({"v": "not reviewed", "s": "grey"})
                continue
            v = t["rating"] + (f" · {t['clause']}" if t.get("clause") else "") + (" · quote NOT FOUND" if t["_cite"] == "NOT FOUND" else "")
            row.append({"v": v, "s": "grey" if t["rating"] == "N/A" else t["rating"]})
        g.append(row)
    g.widths, g.freeze, g.autofilter = [44, 6, 8, 7] + [16] * len(topics), (1, 1), True

    dt = wb.sheet("Detail")
    dt.append(["Document", "Topic ID", "Topic", "Rating", "Status", "Clause", "Quote", "Quote check", "Note", "Playbook basis"],
              style="header")
    for d in docs:
        for t in d["topics"]:
            dt.append([d["document"]["file_name"], t["topic"], catalogue[t["topic"]]["title"],
                       {"v": t["rating"], "s": "grey" if t["rating"] == "N/A" else t["rating"]}, t.get("status"), t.get("clause"),
                       t.get("quote"), t["_cite"], t.get("note"), t.get("basis")], style="wrap")
    dt.widths, dt.freeze, dt.autofilter = [40, 28, 26, 9, 12, 8, 50, 12, 40, 40], (1, 1), True

    bt = wb.sheet("By topic")
    bt.append(["Topic", "RED", "YELLOW", "GREEN", "N/A"], style="header")
    for tid in topics:
        vals = [t["rating"] for d in docs for t in d["topics"] if t["topic"] == tid]
        bt.append([catalogue[tid]["title"]] + [vals.count(k) for k in rv.RATINGS])
    n = len(topics)
    bt.widths = [34, 7, 8, 8, 6]
    bt.add_chart("Documents by rating, per topic", (2, 1, n + 1, 1),
                 [("RED", (2, 2, n + 1, 2), "D03B3B"), ("YELLOW", (2, 3, n + 1, 3), "FAB219"), ("GREEN", (2, 4, n + 1, 4), "0CA30C")],
                 anchor=(6, 1), size=(10, max(16, n + 4)), bar_dir="bar", stacked=True)

    ds = wb.sheet("Documents")
    ds.append(["Document", "RED", "YELLOW", "GREEN", "Quotes verified", "Quotes not found", "Standards", "Reviewed"], style="header")
    for d in docs:
        c, link = doc_counts(d), doc_link(d, base_url)
        ds.append([{"v": d["document"]["file_name"], "link": link} if link else d["document"]["file_name"], c["RED"], c["YELLOW"], c["GREEN"],
                   sum(1 for t in d["topics"] if t["_cite"] == "verified"), sum(1 for t in d["topics"] if t["_cite"] == "NOT FOUND"),
                   d.get("provenance", {}).get("standards"), d.get("provenance", {}).get("run_at")])
    ds.widths = [44, 6, 8, 7, 10, 10, 40, 20]
    ab = wb.sheet("About")
    ab.append([{"v": title, "s": "title"}])
    ab.append(["Generated", datetime.datetime.now().strftime("%Y-%m-%d %H:%M") + f" by library_grid.py v{VERSION}"])
    ab.append(["Note", "Draft for attorney review; not legal advice. Cells are model classifications against the playbook; "
                       "quotes checked against the document text by script ('unchecked' = no text available, e.g. PDF)."])
    ab.widths = [14, 120]
    wb.save(path)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Assemble per-document reviews into a library grid (xlsx + HTML heatmap).")
    ap.add_argument("reviews_dir")
    ap.add_argument("--topics", required=True)
    ap.add_argument("--clauses-dir")
    ap.add_argument("--xlsx")
    ap.add_argument("--html")
    ap.add_argument("--base-url")
    ap.add_argument("--title", default="NDA library review")
    a = ap.parse_args(argv)
    try:
        catalogue = rv.topic_catalogue(a.topics)
    except rv.BadInput as ex:
        print(json.dumps({"ok": False, "problems": [str(ex)]}, indent=1))
        return 2
    docs, probs, warns = load_reviews(a.reviews_dir, catalogue)
    if probs:
        print(json.dumps({"ok": False, "documents": len(docs), "problems": probs, "warnings": warns}, indent=1, ensure_ascii=False))
        return 2
    totals = attach_citations(docs, a.clauses_dir)
    cells = {k: sum(1 for d in docs for t in d["topics"] if t["rating"] == k) for k in rv.RATINGS}
    result = {"ok": True, "documents": len(docs), "cells": cells, "citations": totals, "problems": [], "warnings": warns,
              "not_found": [f"{d['document']['file_name']} · {t['topic']}" for d in docs for t in d["topics"] if t["_cite"] == "NOT FOUND"],
              "outputs": []}
    if a.xlsx:
        render_xlsx(docs, catalogue, a.title, a.base_url, a.xlsx)
        result["outputs"].append(a.xlsx)
    if a.html:
        with open(a.html, "w", encoding="utf-8") as f:
            f.write(render_html(docs, catalogue, a.title, a.base_url, totals))
        result["outputs"].append(a.html)
    print(json.dumps(result, indent=1, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
