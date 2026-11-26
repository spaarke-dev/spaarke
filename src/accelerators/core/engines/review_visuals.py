"""review_visuals.py - review dashboard (static HTML + SVG) and audit pack (.xlsx) for one review.

Python 3 standard library only (+ sibling xlsx_lite). No script, no network, no CDN in the HTML:
Copilot previews may block scripts, and every number must be readable without them.

Usage:
  python review_visuals.py REVIEW.json EDIT-PLAN.json --topics topics.json
         [--html dashboard.html] [--xlsx audit.xlsx] [--doc-link URL]

REVIEW.json: the topic-by-topic review result (references/review-result.schema.json): one entry per
checklist topic with rating GREEN / YELLOW / RED / N/A, status, clause, quote, basis, note, action,
priority. EDIT-PLAN.json: the plan the redline was built from. Every number in the outputs is
derived from these two files; the script checks that they agree before writing anything.

Output (JSON on stdout): {"ok": bool, "counts": {...}, "problems": [...], "warnings": [...], "outputs": [...]}
Exit codes: 0 ok, 2 bad input (unreadable or invalid review/plan), 3 review and plan disagree.
"""
import argparse
import datetime
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xlsx_lite  # noqa: E402

VERSION = "0.1.2-poc"
RATINGS = ["RED", "YELLOW", "GREEN", "N/A"]
STATUSES = {"compliant", "fallback", "deviation", "missing", "not-applicable", "undetermined", "silent"}
ACTIONS = {"none", "edit", "comment", "escalate"}
PRIORITIES = ["High", "Medium", "Low"]
# status palette (fixed; never reused for series): critical / warning / good / neutral
COLOR = {"RED": "d03b3b", "YELLOW": "fab219", "GREEN": "0ca30c", "N/A": "8a8a86",
         "High": "d03b3b", "Medium": "fab219", "Low": "8a8a86"}
ICON = {"RED": "✕", "YELLOW": "!", "GREEN": "✓", "N/A": "–"}


class BadInput(Exception):
    pass


# ---------- validation (shared with library_grid.py) ----------
def load_json(path, what):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except (OSError, ValueError) as ex:
        raise BadInput(f"{what}: cannot read {path}: {ex}") from None


def topic_catalogue(path):
    cat = load_json(path, "topics")
    return {t["id"]: t for t in cat.get("topics", []) if t.get("status") != "retired"}


def validate_review(review, catalogue, require_all=True):
    """Return (problems, warnings) for one review-result object."""
    probs, warns = [], []
    if not isinstance(review, dict) or not isinstance(review.get("topics"), list):
        return ["review has no 'topics' list"], warns
    doc = review.get("document") or {}
    if not doc.get("file_name"):
        probs.append("document.file_name missing")
    side = (review.get("review_basis") or {}).get("side")
    seen = set()
    for i, t in enumerate(review["topics"]):
        tid = t.get("topic") if isinstance(t, dict) else None
        where = f"topics[{i}] ({tid})"
        if tid not in catalogue:
            probs.append(f"{where}: unknown topic id (not in topics.json)")
            continue
        if tid in seen:
            probs.append(f"{where}: topic listed twice")
        seen.add(tid)
        if t.get("rating") not in RATINGS:
            probs.append(f"{where}: rating must be one of {RATINGS}")
        if t.get("status") not in STATUSES:
            probs.append(f"{where}: status must be one of {sorted(STATUSES)}")
        if t.get("action", "none") not in ACTIONS:
            probs.append(f"{where}: action must be one of {sorted(ACTIONS)}")
        if t.get("priority") is not None and t.get("priority") not in PRIORITIES:
            probs.append(f"{where}: priority must be one of {PRIORITIES}")
        if t.get("rating") in ("RED", "YELLOW") and t.get("status") != "missing" and not t.get("quote"):
            warns.append(f"{where}: {t.get('rating')} with no quote")
        if t.get("rating") == "RED" and t.get("action", "none") == "none":
            warns.append(f"{where}: RED with action 'none'")
    if require_all:
        expected = {k for k, v in catalogue.items() if not side or side in v.get("sides", [side])}
        missing = sorted(expected - seen)
        if missing:
            probs.append(f"topics not reviewed (every checklist topic needs an entry): {', '.join(missing)}")
    return probs, warns


def edit_kind(e):
    if "after" in e:
        return "insertion"
    if "replace" not in e:
        return "comment only"
    return "deletion" if e["replace"] == "" else "replacement"


def cross_check(review, plan):
    probs = []
    by_topic = {t.get("topic"): t for t in review.get("topics", []) if isinstance(t, dict)}
    planned = {}
    for i, e in enumerate(plan.get("edits", [])):
        tid = e.get("topic")
        if not tid:
            probs.append(f"plan edit {i}: no topic id")
            continue
        planned.setdefault(tid, []).append(e)
        if tid not in by_topic:
            probs.append(f"plan edit {i}: topic {tid} is not in the review")
        elif by_topic[tid].get("action", "none") == "none":
            probs.append(f"plan edit {i}: topic {tid} has edits but the review says action 'none'")
    for tid, t in by_topic.items():
        if t.get("action") == "edit" and tid not in planned:
            probs.append(f"review topic {tid}: action 'edit' but the plan has no edit for it")
    return probs, planned


# ---------- HTML helpers (shared with library_grid.py) ----------
CSS = """
:root{--bg:#fcfcfb;--ink:#1b1b1a;--ink2:#5c5c58;--line:#e3e2de;--card:#ffffff;--grid:#ecebe7;
--red:#d03b3b;--yellow:#fab219;--green:#0ca30c;--na:#8a8a86;
--red-t:#f6d3d3;--yellow-t:#fde6b0;--green-t:#d6f0d6;--na-t:#ecebe7;--red-i:#8b1a1a;--yellow-i:#5a3d00;--green-i:#0b5e0b;--na-i:#4a4a47}
@media (prefers-color-scheme:dark){:root{--bg:#1a1a19;--ink:#ecebe7;--ink2:#a9a8a3;--line:#3a3a37;--card:#232322;--grid:#2e2e2c;
--red-t:#4a1f1f;--yellow-t:#4a3a12;--green-t:#163a16;--na-t:#33332f;--red-i:#f3b0b0;--yellow-i:#fbd98a;--green-i:#a6e3a6;--na-i:#cfcfca}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.45 "Segoe UI",system-ui,sans-serif}
main{max-width:1200px;margin:0 auto;padding:24px 16px 48px}h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 10px}
.sub{color:var(--ink2);margin:2px 0}.note{color:var(--ink2);font-size:12px}
.tiles{display:flex;flex-wrap:wrap;gap:12px;margin:18px 0}.tile{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px 14px;min-width:120px}
.tile b{display:block;font-size:26px;line-height:1.1}.tile span{color:var(--ink2);font-size:12px}
.row{display:flex;flex-wrap:wrap;gap:16px}.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 14px;flex:1 1 360px;overflow-x:auto}
table{border-collapse:collapse;width:100%}th,td{text-align:left;vertical-align:top;padding:6px 8px;border-bottom:1px solid var(--line)}
th{font-size:12px;color:var(--ink2);font-weight:600}td.q{font-style:italic;color:var(--ink2);max-width:380px}
.chip{display:inline-block;padding:1px 8px;border-radius:10px;font-weight:600;font-size:12px;white-space:nowrap}
.c-RED{background:var(--red-t);color:var(--red-i)}.c-YELLOW{background:var(--yellow-t);color:var(--yellow-i)}
.c-GREEN{background:var(--green-t);color:var(--green-i)}.c-NA{background:var(--na-t);color:var(--na-i)}
svg text{fill:var(--ink);font:12px "Segoe UI",system-ui,sans-serif}svg .muted{fill:var(--ink2)}svg .grid{stroke:var(--grid)}
.heat{overflow-x:auto}.heat table{width:auto}.heat td,.heat th{border:2px solid var(--bg);padding:0;text-align:center}
.heat th.v{writing-mode:vertical-rl;transform:rotate(180deg);height:150px;padding:4px 2px;font-weight:500}
.heat td.cell{width:30px;height:28px;font-weight:700;font-size:12px}.heat td.doc{text-align:left;padding:4px 8px;white-space:nowrap;position:sticky;left:0;background:var(--card)}
a{color:inherit}
"""


def esc(s):
    return ("" if s is None else str(s)).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def chip(rating):
    cls = "NA" if rating == "N/A" else rating
    return f'<span class="chip c-{cls}">{ICON.get(rating, "")} {esc(rating)}</span>'


def hbar_svg(rows, title):
    """rows = [(label, value, rgb, tooltip)]; one series, so no legend; values labelled at bar ends."""
    cap = 30
    short = lambda s: s if len(s) <= cap else s[:cap - 1].rstrip() + "…"  # noqa: E731
    rows = [(short(str(label)), v, rgb, tip or f"{label}: {v}") for label, v, rgb, tip in rows]
    lw, bw, rh = min(16 + 7 * max([len(r[0]) for r in rows] + [4]), 230), 330, 30
    vmax = max([v for _, v, _, _ in rows] + [1])
    h = rh * len(rows) + 8
    parts = [f'<svg role="img" aria-label="{esc(title)}" viewBox="0 0 {lw + bw + 50} {h}" width="100%" '
             f'style="max-width:{lw + bw + 50}px">']
    for i, (label, v, rgb, tip) in enumerate(rows):
        y = 4 + i * rh
        w = 0 if not v else max(3, bw * v / vmax)
        parts.append(f'<g><title>{esc(tip or f"{label}: {v}")}</title>'
                     f'<rect x="0" y="{y}" width="{lw + bw + 50}" height="{rh}" fill="transparent"/>'
                     f'<text x="{lw - 8}" y="{y + 19}" text-anchor="end">{esc(label)}</text>'
                     f'<rect x="{lw}" y="{y + 6}" width="{w:.1f}" height="16" rx="3" fill="#{rgb}"/>'
                     f'<text x="{lw + w + 6:.1f}" y="{y + 19}">{v}</text></g>')
    parts.append(f'<line class="grid" x1="{lw}" y1="0" x2="{lw}" y2="{h}"/></svg>')
    return "".join(parts)


def page(title, body):
    return (f'<!doctype html><html lang="en"><head><meta charset="utf-8">'
            f'<meta name="viewport" content="width=device-width,initial-scale=1"><title>{esc(title)}</title>'
            f'<style>{CSS}</style></head><body><main>{body}</main></body></html>')


def tile(value, label):
    return f'<div class="tile"><b>{esc(value)}</b><span>{esc(label)}</span></div>'


# ---------- one review ----------
def counts_for(review, plan):
    rating = {r: 0 for r in RATINGS}
    for t in review["topics"]:
        rating[t["rating"]] = rating.get(t["rating"], 0) + 1
    changes = [e for e in plan.get("edits", []) if e.get("summary")]
    prio = {p: sum(1 for e in changes if e["summary"].get("priority") == p) for p in PRIORITIES}
    esc_topics = [t for t in review["topics"] if t.get("action") == "escalate"]
    return {"rating": rating, "changes": len(changes), "edits": len(plan.get("edits", [])),
            "priority": prio, "escalations": len(esc_topics)}


def ordered_topics(review, catalogue):
    order = list(catalogue)
    return sorted(review["topics"], key=lambda t: (RATINGS.index(t["rating"]), order.index(t["topic"])))


def render_html(review, plan, catalogue, counts, doc_link=None):
    doc = review.get("document", {})
    prov = review.get("provenance", {})
    basis = review.get("review_basis", {})
    name = doc.get("title") or doc.get("file_name")
    link = f' · <a href="{esc(doc_link)}">open the agreement</a>' if doc_link else ""
    b = [f"<h1>NDA review dashboard</h1><p class='sub'><b>{esc(name)}</b>"
         f"{' · ' + esc(', '.join(doc.get('parties', []))) if doc.get('parties') else ''}{link}</p>",
         f"<p class='sub'>{esc(prov.get('standards') or plan.get('provenance') or '')} · Skill: "
         f"{esc(prov.get('skill', 'nda-review'))} v{esc(prov.get('skill_version', ''))} · Side: {esc(basis.get('side'))}"
         f" · Approach: {esc(basis.get('posture'))}</p>",
         "<p class='note'>Draft for attorney review; not legal advice. Numbers come from the edit plan and the "
         "review result that built the redline.</p>"]
    r = counts["rating"]
    b.append('<div class="tiles">' + tile(r["RED"], "RED topics") + tile(r["YELLOW"], "YELLOW topics")
             + tile(r["GREEN"], "GREEN topics") + tile(counts["changes"], "changes in the redline")
             + tile(counts["escalations"], "escalations") + "</div>")
    rating_rows = [(k, r[k], COLOR[k], f"{k}: {r[k]} topic(s)") for k in RATINGS if k != "N/A" or r[k]]
    prio_rows = [(p, counts["priority"][p], COLOR[p], f"{p} priority: {counts['priority'][p]} change(s)") for p in PRIORITIES]
    b.append('<div class="row"><div class="card"><h2 style="margin-top:0">Topics by rating</h2>'
             + hbar_svg(rating_rows, "Topics by rating") + '</div><div class="card"><h2 style="margin-top:0">'
             'Changes by priority</h2>' + hbar_svg(prio_rows, "Changes by priority") + "</div></div>")
    escs = [t for t in review["topics"] if t.get("action") == "escalate"]
    if escs:
        b.append("<h2>Escalations</h2><div class='card'><table><tr><th>Topic</th><th>Rating</th><th>Clause</th><th>Why</th></tr>"
                 + "".join(f"<tr><td>{esc(catalogue[t['topic']]['title'])}</td><td>{chip(t['rating'])}</td>"
                           f"<td>{esc(t.get('clause'))}</td><td>{esc(t.get('note') or t.get('basis'))}</td></tr>" for t in escs)
                 + "</table></div>")
    rows = []
    for e in plan.get("edits", []):
        s = e.get("summary")
        if not s:
            continue
        rows.append((s["n"], f"<tr><td>{s['n']}</td><td>{esc(s.get('section'))}</td>"
                             f"<td>{esc(catalogue.get(e.get('topic'), {}).get('title', e.get('topic')))}</td>"
                             f"<td>{chip_prio(s.get('priority'))}</td><td>{esc(s.get('issue'))}</td>"
                             f"<td>{esc(s.get('revision'))}</td><td>{esc(edit_kind(e))}</td></tr>"))
    if rows:
        b.append("<h2>Changes in the redline</h2><div class='card'><table><tr><th>#</th><th>Section</th><th>Topic</th>"
                 "<th>Priority</th><th>Issue</th><th>Revision</th><th>Kind</th></tr>"
                 + "".join(x for _, x in sorted(rows)) + "</table></div>")
    b.append("<h2>Every topic</h2><div class='card'><table><tr><th>Topic</th><th>Rating</th><th>Status</th>"
             "<th>Clause</th><th>Quote</th><th>Playbook basis</th><th>Action</th></tr>")
    for t in ordered_topics(review, catalogue):
        b.append(f"<tr><td>{esc(catalogue[t['topic']]['title'])}<br><span class='note'>{esc(t['topic'])}</span></td>"
                 f"<td>{chip(t['rating'])}</td><td>{esc(t.get('status'))}</td><td>{esc(t.get('clause'))}</td>"
                 f"<td class='q'>{esc(t.get('quote'))}</td><td>{esc(t.get('basis'))}</td><td>{esc(t.get('action', 'none'))}</td></tr>")
    b.append("</table></div>")
    if review.get("open_questions"):
        b.append("<h2>Open questions</h2><ul>" + "".join(f"<li>{esc(q)}</li>" for q in review["open_questions"]) + "</ul>")
    b.append(f"<p class='note'>Generated {datetime.datetime.now().strftime('%Y-%m-%d %H:%M')} by review_visuals.py v{VERSION}.</p>")
    return page(f"NDA review - {name}", "".join(b))


def chip_prio(p):
    cls = {"High": "RED", "Medium": "YELLOW"}.get(p, "NA")
    return f'<span class="chip c-{cls}">{esc(p)}</span>'


def render_xlsx(review, plan, catalogue, counts, path, doc_link=None):
    wb = xlsx_lite.Workbook()
    doc, prov = review.get("document", {}), review.get("provenance", {})
    s = wb.sheet("Summary")
    s.append([{"v": "NDA review - audit pack", "s": "title"}])
    s.append(["Agreement", {"v": doc.get("title") or doc.get("file_name"), "link": doc_link} if doc_link
              else doc.get("title") or doc.get("file_name")])
    s.append(["Standards", prov.get("standards") or plan.get("provenance")])
    s.append(["Skill", f"{prov.get('skill', 'nda-review')} v{prov.get('skill_version', '')}"])
    s.append(["Generated", datetime.datetime.now().strftime("%Y-%m-%d %H:%M")])
    s.append(["Note", "Draft for attorney review; not legal advice."])
    s.append([])
    h1 = s.append(["Rating", "Topics"], style="header")
    for k in RATINGS:
        s.append([{"v": k, "s": "grey" if k == "N/A" else k}, counts["rating"][k]])
    s.append([])
    h2 = s.append(["Priority", "Changes"], style="header")
    for p in PRIORITIES:
        s.append([p, counts["priority"][p]])
    s.append(["Escalations", counts["escalations"]], style="bold")
    s.widths = [16, 70]
    # same per-bar colours as the dashboard; charts stacked so both show beside the table
    s.add_chart("Topics by rating", (h1 + 1, 1, h1 + 4, 1),
                [("Topics", (h1 + 1, 2, h1 + 4, 2), "5C5C58", [COLOR[k].upper() for k in RATINGS])],
                anchor=(3, 1), size=(7, 14), bar_dir="bar")
    s.add_chart("Changes by priority", (h2 + 1, 1, h2 + 3, 1),
                [("Changes", (h2 + 1, 2, h2 + 3, 2), "5C5C58", [COLOR[p].upper() for p in PRIORITIES])],
                anchor=(3, 16), size=(7, 14), bar_dir="bar")

    t = wb.sheet("Topics")
    t.append(["Topic ID", "Topic", "Rating", "Status", "Clause", "Quote", "Playbook basis", "Note", "Action", "Priority"],
             style="header")
    for x in ordered_topics(review, catalogue):
        t.append([x["topic"], catalogue[x["topic"]]["title"], {"v": x["rating"], "s": "grey" if x["rating"] == "N/A" else x["rating"]},
                  x.get("status"), x.get("clause"), x.get("quote"), x.get("basis"), x.get("note"), x.get("action", "none"),
                  x.get("priority")], style="wrap")
    t.widths, t.freeze, t.autofilter = [28, 26, 10, 13, 9, 50, 40, 40, 10, 9], (1, 2), True

    c = wb.sheet("Change log")
    c.append(["#", "Section", "Topic ID", "Priority", "Issue", "Revision", "Kind", "Text before", "Text after", "Comment"],
             style="header")
    for e in plan.get("edits", []):
        sm = e.get("summary") or {}
        before = e.get("old") or (None if e.get("find") == "*" else e.get("find")) or ("(whole clause)" if e.get("find") == "*" else e.get("after"))
        after = e.get("insert") if "after" in e else e.get("replace")
        c.append([sm.get("n"), sm.get("section"), e.get("topic"), sm.get("priority"), sm.get("issue"), sm.get("revision"),
                  edit_kind(e), before, after, e.get("comment")], style="wrap")
    c.widths, c.freeze, c.autofilter = [5, 10, 28, 9, 36, 36, 13, 40, 40, 50], (1, 0), True

    q = wb.sheet("Escalations and questions")
    q.append(["Kind", "Topic", "Clause", "Detail"], style="header")
    for x in review["topics"]:
        if x.get("action") == "escalate":
            q.append(["Escalation", catalogue[x["topic"]]["title"], x.get("clause"), x.get("note") or x.get("basis")], style="wrap")
    for oq in review.get("open_questions", []):
        q.append(["Open question", None, None, oq], style="wrap")
    q.widths = [15, 30, 10, 90]
    wb.save(path)


def main(argv=None):
    ap = argparse.ArgumentParser(description="Review dashboard (HTML) and audit pack (xlsx) from review + plan.")
    ap.add_argument("review")
    ap.add_argument("plan")
    ap.add_argument("--topics", required=True)
    ap.add_argument("--html")
    ap.add_argument("--xlsx")
    ap.add_argument("--doc-link", help="URL of the agreement (team site or OneDrive), linked from both outputs")
    a = ap.parse_args(argv)
    try:
        catalogue = topic_catalogue(a.topics)
        review, plan = load_json(a.review, "review"), load_json(a.plan, "plan")
        probs, warns = validate_review(review, catalogue)
        if probs:
            raise BadInput("; ".join(probs))
    except BadInput as ex:
        print(json.dumps({"ok": False, "problems": [str(ex)]}, indent=1))
        return 2
    xprobs, _ = cross_check(review, plan)
    counts = counts_for(review, plan)
    result = {"ok": not xprobs, "counts": counts, "problems": xprobs, "warnings": warns, "outputs": []}
    if xprobs:
        print(json.dumps(result, indent=1, ensure_ascii=False))
        return 3
    if a.html:
        with open(a.html, "w", encoding="utf-8") as f:
            f.write(render_html(review, plan, catalogue, counts, a.doc_link))
        result["outputs"].append(a.html)
    if a.xlsx:
        render_xlsx(review, plan, catalogue, counts, a.xlsx, a.doc_link)
        result["outputs"].append(a.xlsx)
    print(json.dumps(result, indent=1, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
