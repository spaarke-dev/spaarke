"""Billing-guideline compliance test: guideline documents GL-01..GL-06, their answer keys and the gap report.

Reads guideline_content.py (the text), ground-truth/rule-sets/RS-0n.json (what each guideline must state), production_key.PRODUCTION_RULES
(the production rule bodies) and guidelines/production-key/summary.json (finding counts). Never writes to ground-truth/.

  python -I generator/build_guidelines.py            check, then write guidelines/GL-0n.md, guidelines/keys/GL-0n.json, guidelines/gap-report.md
  python -I generator/build_guidelines.py --render   also write local/guidelines/GL-0n.docx and .pdf (needs python-docx and reportlab)

Run from the evaluation-set folder, after production_key.py. Deterministic: the same inputs give byte-identical committed output.
The check fails (exit 1) when a rule-set rule has no clause, a clause cites an unknown rule, a clause omits a parameter value or its
consequence, a provision has no reason, or the rate schedule disagrees with the rule set.
"""
import collections
import json
import sys
from pathlib import Path

GEN = Path(__file__).resolve().parent
sys.path.insert(0, str(GEN))  # -I leaves the script folder off sys.path; these are our own modules

import guideline_content as G  # noqa: E402
from production_key import NE_BUDGET, NE_CAP_CODE, NE_CAP_UNIT, NE_KEYWORD, NE_PERIOD, NE_PO, PRODUCTION_RULES, bodies, production_consequence  # noqa: E402

ROOT = GEN.parent
GT = ROOT / "ground-truth"
OUT = ROOT / "guidelines"
LOCAL = ROOT / "local" / "guidelines"
REASONS = {"not-billing", "judgement", "needs-new-type"}
# Short labels for the gap report; a reason string can join two of these with "; ".
GAPS = {NE_PO: "PO number on the invoice", NE_PERIOD: "Invoice received too long after the billing period end",
        NE_BUDGET: "Invoice total over the matter budget", NE_CAP_UNIT: "Expense cap on a unit rate (per page)",
        NE_CAP_CODE: "Expense cap limited to one expense code", NE_KEYWORD: "Non-billable work named by task, not by code"}
CONSEQUENCE_WORDS = {"Reduce": "reduced", "Disallow": "disallowed", "Flag": "referred", "Reject": "returned unpaid"}


def cap_text(x):
    return f"${x:,.2f}"


def load_rule_sets():
    return {p.stem: json.loads(p.read_text(encoding="utf-8")) for p in sorted((GT / "rule-sets").glob("RS-*.json"))}


def clauses(gl):
    for s in gl["sections"]:
        for c in s["clauses"]:
            yield s, c


def rate_clause(gl, rs):
    rates = next((r["params"]["rates"] for r in rs["rules"] if r["rule_type"] == "ApprovedRate"), None)
    if not rates:
        return None
    name = gl.get("rates_appendix_name", "Appendix A")
    return name, [(t, rates[t]) for t in G.RATE_ORDER if t in rates]


def expected_tokens(rule):
    """Strings the clause text must contain for this rule's parameters (case-insensitive)."""
    p, t = rule["params"], rule["rule_type"]
    out = []
    if t == "TimeIncrement":
        out.append(f"({p['increment_hours']} hour)")
    elif t == "DailyHoursCap":
        out.append(f"{p['max_hours_per_timekeeper_per_day']} hours")
    elif t == "NonBillableActivity":
        out += p.get("keywords", [])
    elif t == "ExpenseRule":
        out += p.get("non_reimbursable", [])
        for code, cap in p.get("caps", {}).items():
            out += [code, cap_text(cap)]
    elif t == "InvoiceAge":
        for k in ("max_days_after_period_end", "max_days_work_age"):
            if k in p:
                out.append(f"{p[k]} days")
        if "max_days_after_period_end" in p:
            out.append("end of the")
    elif t == "Duplicate":
        out += ["same timekeeper, date, hours and description"]
    elif t == "RequiredData":
        if p.get("codes_required"):
            out.append("task code")
        if "po_number" in p.get("fields", []):
            out.append("PO number")
    elif t == "AiObservation":
        out += [{"block_billing": "block", "vague_description": "vague"}[o] for o in p["observations"]]
    elif t == "ApprovedTimekeeper":
        out.append("approved")
    elif t == "ApprovedRate":
        out.append("maximum")
    elif t == "CoreRecordWindow":
        if p.get("require_within_matter_dates"):
            out.append("dates")
        if p.get("budget_cap"):
            out.append("budget")
    return out


def check(rule_sets):
    problems = []
    for gl in G.GUIDELINES:
        rs = rule_sets[gl["rule_set_id"]]
        by_id = {r["rule_id"]: r for r in rs["rules"]}
        cited = collections.defaultdict(list)
        numbers = set()
        for s, c in clauses(gl):
            where = f"{gl['id']} {c['n']}"
            if not c["n"].startswith(s["n"] + "."):
                problems.append(f"{where}: clause number is outside section {s['n']}")
            if c["n"] in numbers:
                problems.append(f"{where}: clause number repeats")
            numbers.add(c["n"])
            if c["rules"] and c["unmapped"]:
                problems.append(f"{where}: a clause is either a rule or an unmapped provision")
            if c["unmapped"] and c["unmapped"] not in REASONS:
                problems.append(f"{where}: unknown reason {c['unmapped']}")
            if (c["rules"] or c["unmapped"]) and not c["para"]:
                problems.append(f"{where}: no paraphrase")
            for rid in c["rules"]:
                if rid not in by_id:
                    problems.append(f"{where}: cites {rid}, which {rs['id']} does not have")
                    continue
                cited[rid].append(c)
                rule = by_id[rid]
                low = c["text"].lower()
                for tok in expected_tokens(rule):
                    if tok.lower() not in low:
                        problems.append(f"{where}: {rid} text lacks '{tok}'")
                if CONSEQUENCE_WORDS[rule["consequence"]] not in low:
                    problems.append(f"{where}: {rid} text does not state its consequence ({rule['consequence']})")
                for other in CONSEQUENCE_WORDS.values():
                    if other != CONSEQUENCE_WORDS[rule["consequence"]] and other in low and len(c["rules"]) == 1:
                        problems.append(f"{where}: {rid} text also uses '{other}', another consequence's word")
        for rid in by_id:
            if not cited[rid]:
                problems.append(f"{gl['id']}: {rid} ({by_id[rid]['rule_type']}) has no clause")
            if rid not in PRODUCTION_RULES[rs["id"]]:
                problems.append(f"{gl['id']}: {rid} has no production mapping in production_key.PRODUCTION_RULES")
        rc = rate_clause(gl, rs)
        if bool(rc) != bool(gl.get("rates_appendix")):
            problems.append(f"{gl['id']}: rate appendix flag disagrees with the rule set")
        if rc:
            name = rc[0]
            rate_clauses = [c for _, c in clauses(gl) if any(by_id.get(r, {}).get("rule_type") == "ApprovedRate" for r in c["rules"])]
            if not any(name in c["text"] for c in rate_clauses):
                problems.append(f"{gl['id']}: the ApprovedRate clause does not refer to {name}")
    return problems


def production_entry(rs_id, rule):
    spec = PRODUCTION_RULES[rs_id][rule["rule_id"]]
    rules = bodies(spec)
    ne = spec.get("not_expressible") if isinstance(spec, dict) else None
    status = "none" if not rules else ("partial" if ne else "full")
    consequence = production_consequence(rule["rule_type"], rule["consequence"]) if rules else None
    return {"status": status, "rules": rules, "consequence": consequence, "not_expressible": ne}


def key_of(gl, rs):
    by_id = {r["rule_id"]: r for r in rs["rules"]}
    rules, unmapped = [], []
    for s, c in clauses(gl):
        ref = {"section": c["n"], "heading": s["title"]}
        for rid in c["rules"]:
            r = by_id[rid]
            rules.append({"rule_id": rid, "rule_type": r["rule_type"], "params": r["params"], "consequence": r["consequence"],
                          "clause_ref": ref, "paraphrase": c["para"], "expressible": True,
                          "production": production_entry(rs["id"], r)})
        if c["unmapped"]:
            unmapped.append({"clause_ref": ref, "paraphrase": c["para"], "reason": c["unmapped"]})
    order = {r["rule_id"]: i for i, r in enumerate(rs["rules"])}
    rules.sort(key=lambda r: order[r["rule_id"]])
    rc = rate_clause(gl, rs)
    return {
        "schema": "ebilling-eval/1",
        "kind": "guideline-key",
        "id": f"{gl['id']}-KEY",
        "guideline_id": gl["id"],
        "rule_set_id": rs["id"],
        "set": "F",
        "meta": {
            "title": gl["title"], "issuer": gl["issuer"], "issuer_type": gl["issuer_type"], "version": gl["version"],
            "effective": gl["effective"], "generator_id": "build_guidelines.py", "licence": "Synthetic - authored for this evaluation set",
            "licence_status": "synthetic", "pii_note": "Fictional organisation; no personal data.", "label_method": "authored-from-rule-set",
            "storage": {"location": "committed", "path": f"guidelines/{gl['id']}.md"},
            "rendered": [f"local/guidelines/{gl['id']}.docx", f"local/guidelines/{gl['id']}.pdf"],
        },
        "profile": gl["profile"],
        "rate_schedule": {"name": rc[0], "currency": "USD", "unit": "hour", "maximum_rates": dict(rc[1])} if rc else None,
        "rules": rules,
        "unmapped_provisions": unmapped,
    }


def markdown(gl, rs):
    lines = [f"# {gl['title']}", "", f"**{gl['issuer']}** · Version {gl['version']} · Effective {gl['effective']}", "",
             "> Synthetic document written for Spaarke's billing-guideline evaluation. The organisation is fictional.", "",
             gl["intro"], ""]
    for s in gl["sections"]:
        lines += [f"## {s['n']}. {s['title']}", ""]
        for c in s["clauses"]:
            lines += [f"**{c['n']}** {c['text']}", ""]
    rc = rate_clause(gl, rs)
    if rc:
        lines += [f"## {rc[0]}: Maximum hourly rates", "",
                  "Maximum hourly rates by classification, in US dollars. The firm's approved rate schedule may set a lower rate "
                  "for an individual timekeeper.", "", "| Classification | Maximum hourly rate |", "|---|---|"]
        lines += [f"| {t} | ${v:,} |" for t, v in rc[1]]
        lines.append("")
    return "\n".join(lines)


# ------------------------------------------------------------------ gap report
def finding_gap(rs_id, finding):
    """The not-expressible reason for one Set F finding, from the finding's shape; None when production can state it."""
    spec = PRODUCTION_RULES[rs_id][finding["rule_id"]]
    rules = bodies(spec)
    ne = spec.get("not_expressible") if isinstance(spec, dict) else None
    if not ne:
        return None
    if not rules:
        return ne
    t = finding["rule_type"]
    if t == "CoreRecordWindow" and finding["scope"] == "invoice":
        return ne  # the budget half
    if t == "InvoiceAge" and finding["scope"] == "invoice":
        return ne  # the period-end half
    if t == "RequiredData" and finding["scope"] == "invoice":
        return ne  # the PO number
    return "partial"  # NonBillable keyword lines and capped expense lines: decided per line by production_key.py on the subset


def gap_parts(reason):
    parts = [g for g in GAPS if g in reason]
    assert parts, f"unknown not-expressible reason: {reason}"
    return parts


def gap_report(rule_sets, keys):
    out = ["# Billing guidelines: gap report (production R1 vs the guidelines)", "",
           "Generated by `generator/build_guidelines.py`. Read with `guidelines/README.md`.", "",
           "## 1. Rules per guideline", "",
           "\"Expressible\" is about the rule body only; §3 lists the consequences production cannot apply as written.", "",
           "| Guideline | Rule set | Rules | Fully expressible | Partly | Not expressible | Consequence changes | Unmapped provisions |",
           "|---|---|---|---|---|---|---|---|"]
    for k in keys:
        st = collections.Counter(r["production"]["status"] for r in k["rules"])
        changed = sum(1 for r in k["rules"] if r["production"]["consequence"] and r["production"]["consequence"] != r["consequence"])
        out.append(f"| {k['guideline_id']} | {k['rule_set_id']} | {len(k['rules'])} | {st['full']} | {st['partial']} | {st['none']} | "
                   f"{changed} | {len(k['unmapped_provisions'])} |")
    out += ["", "## 2. What production R1 cannot state", "",
            "Each row is one gap, with the rules it affects. \"Subset findings\" counts Set F expected findings in the 120-invoice "
            "test subset that production cannot raise (from `production_key.py`); \"All Set F findings\" counts them over all 600 "
            "invoices from the finding's shape, for rules production cannot state at all and for the invoice-level halves of "
            "partly expressible rules. Line findings of partly expressible rules (RS-03-R04, RS-05-R04, RS-04-R07) are counted on the "
            "subset only, so that column is a lower bound for the first and fourth rows.", "",
            "| Gap | Why production cannot state it | Rules | Subset findings | All Set F findings |", "|---|---|---|---|---|"]
    summary = json.loads((OUT / "production-key" / "summary.json").read_text(encoding="utf-8"))
    subset_by_reason = collections.Counter()
    for reason, n in summary["not_expressible"].items():
        for part in gap_parts(reason):
            subset_by_reason[part] += n
    affected = collections.defaultdict(list)
    for k in keys:
        for r in k["rules"]:
            ne = r["production"]["not_expressible"]
            if ne:
                for part in gap_parts(ne):
                    affected[part].append(r["rule_id"])
    all_f = collections.Counter()
    for p in sorted((GT / "compliance-invoices").glob("F-*.json")):
        inv = json.loads(p.read_text(encoding="utf-8"))
        for f in inv["expected_findings"]:
            g = finding_gap(inv["rule_set_id"], f)
            if g and g != "partial":
                for part in gap_parts(g):
                    all_f[part] += 1
    for reason in sorted(affected, key=lambda x: (-subset_by_reason[x], x)):
        whole = all_f[reason] if all_f[reason] else "subset only"
        out.append(f"| {GAPS[reason]} | {reason} | {', '.join(affected[reason])} | {subset_by_reason[reason]} | {whole} |")
    out += ["", "A finding can carry two reasons (RS-03-R06 and RS-06-R04 caps are both per-unit and per-code), so the subset "
            "column counts it under each.", "",
            "## 3. Consequences production applies differently", "",
            "Production allows Reject, Flag or SuggestAdjustment, and only some of them per rule type "
            "(`BillingRuleCatalogue.AllowedConsequences`). A correct drafter picks the nearest allowed one "
            "(`production_key.production_consequence`): Reduce and Disallow become SuggestAdjustment where the type offers an "
            "adjustment, otherwise Flag; ApprovedTimekeeper never rejects (D-89). Score the drafter's consequence against "
            "`production.consequence` in the key.", "",
            "| Guideline says | Production applies | Rules |", "|---|---|---|"]
    pairs = collections.defaultdict(list)
    for k in keys:
        for r in k["rules"]:
            pc = r["production"]["consequence"]
            if pc and pc != r["consequence"]:
                pairs[(r["consequence"], pc)].append(f"{r['rule_id']} {r['rule_type']}")
    for (gc, pc), rids in sorted(pairs.items()):
        out.append(f"| {gc} | {pc} | {', '.join(rids)} |")
    out += ["", "## 4. Subset scoring baseline (production key)", "",
            "| Rule type | Reproduced | Moved | Not expressible | Missed | Production only |", "|---|---|---|---|---|---|"]
    for t, c in sorted(summary["by_rule_type"].items()):
        out.append(f"| {t} | {c.get('reproduced', 0)} | {c.get('moved', 0)} | {c.get('not_expressible', 0)} | {c.get('missed', 0)} | "
                   f"{c.get('production_only', 0)} |")
    out += ["", "| Rule set | Invoices | Set F findings | Production findings | Clean invoices with a production finding |",
            "|---|---|---|---|---|"]
    for rs_id, c in sorted(summary["by_rule_set"].items()):
        out.append(f"| {rs_id} | {c['invoices']} | {c['set_f_findings']} | {c['production_findings']} | "
                   f"{c['clean_invoices_with_production_findings']} of 5 |")
    out += ["", "- **Moved**: DailyHoursCap findings production raises on the first line of the timekeeper-day, where Set F marks "
            "the line that crosses the cap. Score them by timekeeper-day.",
            "- **Production only**: findings production raises that Set F does not expect. ApprovedRate \"no approved rate\" "
            "findings fire beside every ApprovedTimekeeper finding, because a timekeeper who is not on the roster has no rate in "
            "force; score them as expected production behaviour (they are in `production_findings`).",
            "- **Missed**: InvoiceAge work-age findings production cannot reach because it measures from the invoice date, not the "
            "date the client received the invoice.", ""]
    return "\n".join(out)


# ------------------------------------------------------------------ rendering (local only)
def render(gl, rs):
    import docx
    from docx.shared import Pt
    from reportlab.lib import colors
    from reportlab.lib.pagesizes import LETTER
    from reportlab.lib.styles import getSampleStyleSheet
    from reportlab.lib.units import inch
    from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

    LOCAL.mkdir(parents=True, exist_ok=True)
    rc = rate_clause(gl, rs)
    meta = f"{gl['issuer']} · Version {gl['version']} · Effective {gl['effective']}"
    note = "Synthetic document written for Spaarke's billing-guideline evaluation. The organisation is fictional."

    d = docx.Document()
    d.styles["Normal"].font.name = "Calibri"
    d.styles["Normal"].font.size = Pt(11)
    d.core_properties.title = gl["title"]
    d.core_properties.author = gl["issuer"]
    d.add_heading(gl["title"], 0)
    d.add_paragraph(meta)
    d.add_paragraph(note).runs[0].italic = True
    d.add_paragraph(gl["intro"])
    for s in gl["sections"]:
        d.add_heading(f"{s['n']}. {s['title']}", 1)
        for c in s["clauses"]:
            p = d.add_paragraph()
            p.add_run(f"{c['n']}  ").bold = True
            p.add_run(c["text"])
    if rc:
        d.add_heading(f"{rc[0]}: Maximum hourly rates", 1)
        d.add_paragraph("Maximum hourly rates by classification, in US dollars. The firm's approved rate schedule may set a lower "
                        "rate for an individual timekeeper.")
        t = d.add_table(rows=1, cols=2)
        t.style = "Table Grid"
        t.rows[0].cells[0].text, t.rows[0].cells[1].text = "Classification", "Maximum hourly rate"
        for cls, v in rc[1]:
            row = t.add_row().cells
            row[0].text, row[1].text = cls, f"${v:,}"
    d.save(LOCAL / f"{gl['id']}.docx")

    ss = getSampleStyleSheet()
    flow = [Paragraph(gl["title"], ss["Title"]), Paragraph(meta, ss["Normal"]), Spacer(1, 6),
            Paragraph(f"<i>{note}</i>", ss["Normal"]), Spacer(1, 8), Paragraph(gl["intro"], ss["BodyText"])]
    for s in gl["sections"]:
        flow.append(Paragraph(f"{s['n']}. {s['title']}", ss["Heading2"]))
        for c in s["clauses"]:
            text = c["text"].replace("&", "&amp;").replace("<", "&lt;")
            flow.append(Paragraph(f"<b>{c['n']}</b>&nbsp;&nbsp;{text}", ss["BodyText"]))
    if rc:
        flow += [Paragraph(f"{rc[0]}: Maximum hourly rates", ss["Heading2"]),
                 Paragraph("Maximum hourly rates by classification, in US dollars. The firm's approved rate schedule may set a "
                           "lower rate for an individual timekeeper.", ss["BodyText"]), Spacer(1, 6)]
        tbl = Table([["Classification", "Maximum hourly rate"]] + [[c, f"${v:,}"] for c, v in rc[1]], colWidths=[2.6 * inch, 2 * inch])
        tbl.setStyle(TableStyle([("GRID", (0, 0), (-1, -1), 0.5, colors.grey), ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                                 ("ALIGN", (1, 1), (1, -1), "RIGHT")]))
        flow.append(tbl)
    SimpleDocTemplate(str(LOCAL / f"{gl['id']}.pdf"), pagesize=LETTER, title=gl["title"], author=gl["issuer"],
                      invariant=1).build(flow)


def main():
    rule_sets = load_rule_sets()
    problems = check(rule_sets)
    if problems:
        print("\n".join(problems))
        sys.exit(1)
    (OUT / "keys").mkdir(parents=True, exist_ok=True)
    keys = []
    for gl in G.GUIDELINES:
        rs = rule_sets[gl["rule_set_id"]]
        (OUT / f"{gl['id']}.md").write_text(markdown(gl, rs), encoding="utf-8", newline="\n")
        k = key_of(gl, rs)
        keys.append(k)
        (OUT / "keys" / f"{gl['id']}.json").write_text(json.dumps(k, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
        if "--render" in sys.argv:
            render(gl, rs)
        print(gl["id"], rs["id"], "rules", len(k["rules"]), "unmapped", len(k["unmapped_provisions"]))
    (OUT / "gap-report.md").write_text(gap_report(rule_sets, keys), encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
