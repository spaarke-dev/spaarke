#!/usr/bin/env python3
"""Synthetic eBilling evaluation set generator (task 002, spec FR-02 sets B and F).

Commands (run from anywhere; paths are resolved relative to this file):
  data              write ground-truth/{synthetic-invoices,compliance-invoices,rule-sets}/*.json and summary.json
                    (needs: reportlab)
  render [IDS...]   render PDFs from the committed ground truth into local/synthetic/ (all B items by default;
                    pass ids such as F-0007 to render a compliance invoice). Scan noise needs pypdfium2, Pillow, numpy.
  verify            re-check counts, licence fields, and re-run the reference evaluator over every committed F file
  check [IDS...]    PDF-versus-ground-truth consistency: renders each item's clean PDF in memory and checks its text layer
                    against the ground truth (invoice number, matter, PO, dates, every line amount/hours/narrative, total
                    due), then checks every PDF under local/synthetic/ is fresh (render-manifest hash matches the current
                    ground-truth file, page count matches). Exit 1 on any disagreement. Run it after every render.

Determinism: every item derives from random.Random(f"{BASE_SEED}:{id}") so output is identical on every run.
Evaluation tooling only: no dependency on the BFF; not product code (decided at wrap-up task 190).
"""
import argparse
import collections
import glob
import hashlib
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import layout  # noqa: E402
import refdata as R  # noqa: E402
import rules as RU  # noqa: E402

BASE_SEED = 20261015
GT = os.path.join(ES, "ground-truth")
N_B, N_F_SEEDED, N_F_CLEAN = 150, 300, 300
N_SCAN = 48
BIG_ID, MID_ID = 150, 149
F_TEMPLATES = [i for i, t in enumerate(layout.TEMPLATES)
               if layout.code_capability(t) == "full" and t["group"] != "task"]
BUNDLE_KINDS = [{"cover": True, "remit": False, "statement": False}, {"cover": True, "remit": True, "statement": False},
                {"cover": False, "remit": False, "statement": True}]
BUNDLED_IDX = [7, 10, 19, 22, 31, 43, 55, 67, 79, 91, 103, 115, 127, 139]
HEADER_KEYS = ["firm", "firm_invoice_number", "invoice_date", "billing_period_start", "billing_period_end",
               "firm_matter_reference", "po_number", "fees_total", "expenses_total", "adjustments_total", "total_due",
               "currency"]
LICENCE = "Synthetic (project generator, no third-party rights)"
PII = "Synthetic; no personal data"


def rng_for(tag):
    return random.Random(f"{BASE_SEED}:{tag}")


def write_json(path, obj):
    """One line per element of 'lines'; everything else compact. Keeps diffs readable and files small."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    comp = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":"))
    parts = []
    for k, v in obj.items():
        if k in ("lines", "expected_findings", "rules") and isinstance(v, list):
            body = ",\n".join("  " + comp(x) for x in v)
            parts.append(f' {json.dumps(k)}:[\n{body}\n ]' if v else f' {json.dumps(k)}:[]')
        else:
            parts.append(f" {json.dumps(k)}:{comp(v)}")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("{\n" + ",\n".join(parts) + "\n}\n")


# ------------------------------------------------------------------ GT assembly
def timekeeper_rows(inv, tpl):
    return [{"name": t["name"], "title": t["title"], "rate": t["rate"], "billed_as": layout.billed_name(t["name"], tpl)}
            for t in sorted(inv["_tks"].values(), key=lambda x: x["name"])]


def sources_notes(tpl):
    cols = tpl["cols"]
    tk_src = "column" if "tk" in cols else ("group_header" if tpl["group"] == "timekeeper" else "legend")
    return {"timekeeper_source": tk_src,
            "rate_source": "line" if "rate" in cols else "legend",
            "title_source": "line" if "title" in cols else ("group_header" if tpl["group"] == "timekeeper" else "legend"),
            "codes_printed": layout.code_capability(tpl), "grouping": tpl["group"], "hours_format": tpl["hours_fmt"],
            "expenses_layout": "interleaved" if (tpl["exp_mode"] == "interleaved" and tpl["group"] == "none") else "separate",
            "page_total_rows": tpl["page_totals"], "summary_first": tpl["summary_first"], "orientation": tpl["orient"]}


def to_layout_inv(gt):
    """Rebuild the layout input from a (possibly page-less) ground-truth dict. Used by data and render alike."""
    inv = {"header": gt["header"], "firm": {"name": gt["header"]["firm"], "address": gt["document"]["letterhead"]},
           "_tks": {t["name"]: t for t in gt["timekeepers"]}, "lines": []}
    for l in gt["lines"]:
        d = dict(l)
        d["intended"] = gt.get("intended_codes", {}).get(str(l["n"]), [])
        inv["lines"].append(d)
    return inv


def paginate(gt):
    tpl = layout.TEMPLATES[int(gt["document"]["template"][1:]) - 1]
    inv = to_layout_inv(gt)
    pages, line_page, first, npages, parts = layout.layout_invoice(inv, tpl, gt["document"].get("bundle"))
    for l in gt["lines"]:
        l["page"] = line_page[l["n"]] + 1
    gt["document"]["pages"] = len(pages)
    gt["document"]["bundle_parts"] = parts
    gt["document"]["invoice_first_page"] = first + 1
    gt["document"]["invoice_pages"] = npages
    return pages, first, npages, tpl, inv


def gt_from_invoice(inv, rid, set_letter, tpl_idx, extra_doc, meta_extra):
    tpl = layout.TEMPLATES[tpl_idx]
    lines = []
    for l in inv["lines"]:
        lines.append({"n": l["n"], "line_type": l["line_type"], "date": l["date"], "timekeeper": l["timekeeper"],
                      "timekeeper_name": l["timekeeper_name"], "timekeeper_title": l["timekeeper_title"],
                      "title": l["title"], "hours": l["hours"], "rate": l["rate"], "amount": l["amount"],
                      "narrative": l["narrative"], "codes": l["codes"], "page": None,
                      "narrative_style": l["narrative_style"]})
    hdr = {k: inv["header"][k] for k in HEADER_KEYS}
    meta = {"title": f"Synthetic invoice {rid}",
            "generator_id": f"{layout.GENERATOR_ID} {rid}",
            "licence": LICENCE, "licence_status": "synthetic", "pii_note": PII, "pii_scan": None,
            "storage": {"location": "regenerate", "path": f"local/synthetic/{rid}.pdf"},
            "spike": False, "label_method": "generator"}
    meta.update(meta_extra)
    doc = {"letterhead": inv["firm"]["address"], "template": f"T{tpl_idx + 1:02d}", "scan_noise": False, "scan": None,
           "bundled": False, "bundle": None}
    doc.update(sources_notes(tpl))
    doc.update(extra_doc)
    gt = {"schema": "ebilling-eval/1", "kind": "invoice", "id": rid, "set": set_letter, "meta": meta, "header": hdr,
          "timekeepers": timekeeper_rows(inv, tpl), "lines": lines,
          "intended_codes": {str(l["n"]): l.get("intended") or [] for l in inv["lines"]}, "document": doc}
    if set_letter == "F":  # compliance items: the oracle needs no answer key for coding, and the file stays lean
        keep = {"letterhead": doc["letterhead"], "template": doc["template"], "scan_noise": False, "scan": None,
                "bundled": False, "bundle": None}
        gt["document"] = keep
    return gt


# ------------------------------------------------------------------ set B
def b_profile(rng):
    mix = rng.choice(["tidy", "tidy", "tidy", "mixed", "mixed", "sloppy"])
    styles = {"tidy": {"clear": 0.93, "vague": 0.03, "block_billed": 0.02, "non_english": 0.02},
              "mixed": {"clear": 0.70, "vague": 0.10, "block_billed": 0.08, "clerical": 0.05, "internal": 0.04,
                        "travel": 0.02, "non_english": 0.01},
              "sloppy": {"clear": 0.45, "vague": 0.20, "block_billed": 0.15, "clerical": 0.10, "internal": 0.05,
                         "travel": 0.03, "non_english": 0.02}}[mix]
    return {"styles": styles, "currency": rng.choices(["USD", "GBP", "EUR", "CAD"], [88, 4, 4, 4])[0],
            "code_mode": rng.choices(["task_activity", "task_only", "none"], [60, 25, 15])[0],
            "increment": rng.choices([0.1, 0.25, 0.5, 0.2], [70, 18, 4, 8])[0], "flat_fee": rng.random() < 0.06,
            "adjustment": rng.choice([None, None, None, 0.05, 0.10]), "po": rng.random() < 0.5, "mix": mix}


def make_b(i, scan_ids, spike_ids):
    rid = f"B-{i:04d}"
    rng = rng_for(rid)
    tpl_idx = (i - 1) % len(layout.TEMPLATES)
    prof = b_profile(rng)
    n_fee = int(min(45, max(6, rng.lognormvariate(2.7, 0.55))))
    n_exp = rng.randint(0, 8)
    if i == BIG_ID:
        n_fee, n_exp = 620, 30
    elif i == MID_ID:
        n_fee, n_exp = 230, 14
    inv = RU.build_invoice(rng, rid, "B", tpl_idx, rs=None, n_fee=n_fee, n_exp=n_exp, profile=prof)
    RU.finalize(inv)
    bundle = None
    if i in BUNDLED_IDX:
        bundle = BUNDLE_KINDS[BUNDLED_IDX.index(i) % 3]
    scan = None
    if i in scan_ids:
        from scan import scan_params
        scan = scan_params(rng)
    meta_extra = {"spike": i in spike_ids, "profile": {"quality_mix": prof["mix"], "hour_increment": prof["increment"],
                                                         "code_mode_requested": prof["code_mode"]}}
    extra_doc = {"scan_noise": scan is not None, "scan": scan, "bundled": bundle is not None, "bundle": bundle,
                 "features": [f for f, on in [("flat_fee_line", prof["flat_fee"]), ("adjustment_line", bool(prof["adjustment"])),
                                              ("non_usd", prof["currency"] != "USD"), ("no_po", not inv["header"]["po_number"])] if on]}
    gt = gt_from_invoice(inv, rid, "B", tpl_idx, extra_doc, meta_extra)
    paginate(gt)
    if i == BIG_ID:
        assert gt["document"]["pages"] > 20, gt["document"]["pages"]
    return gt


# ------------------------------------------------------------------ set F
def make_f(i, seeded, rule_sets):
    rid = f"F-{i:04d}"
    rs = rule_sets[(i - 1) % len(rule_sets)]
    for attempt in range(120):
        rng = rng_for(f"{rid}:{attempt}")
        tpl_idx = rng.choice(F_TEMPLATES)
        inv = RU.build_invoice(rng, rid, "F", tpl_idx, rs=rs, n_fee=rng.randint(7, 16))
        injected = []
        if seeded:
            k = rng.choices([1, 2, 3, 4, 5], [15, 22, 25, 22, 16])[0]
            types = sorted({r["rule_type"] for r in rs["rules"]})
            taken = set()
            for _ in range(k):
                t = rng.choice(types)
                if RU.inject(rng, inv, rs, t, taken):
                    injected.append(t)
            if not injected:
                continue
        RU.finalize(inv)
        findings = RU.evaluate(inv, rs)
        got = collections.Counter(f["rule_type"] for f in findings)
        if got != collections.Counter(injected):
            continue
        meta_extra = {"rule_set_id": rs["id"]}
        gt = gt_from_invoice(inv, rid, "F", tpl_idx, {}, meta_extra)
        gt["rule_set_id"] = rs["id"]
        gt["context"] = inv["context"]
        gt["expected_findings"] = findings
        gt["seeded_violations"] = len(findings)
        paginate(gt)
        gt.pop("intended_codes", None)
        for k in ("bundle_parts", "invoice_first_page", "invoice_pages", "scan_noise", "scan", "bundled", "bundle"):
            gt["document"].pop(k, None)
        return gt
    raise RuntimeError(f"{rid}: could not build a consistent invoice")


# ------------------------------------------------------------------ commands
def cmd_data(_):
    rule_sets = RU.make_rule_sets()
    RU.check_clean_bank(rule_sets)
    for rs in rule_sets:
        write_json(os.path.join(GT, "rule-sets", rs["id"] + ".json"), rs)

    all_idx = list(range(1, N_B + 1))
    r = rng_for("selection")
    candidates = [i for i in all_idx if i not in (BIG_ID, MID_ID)]
    scan_ids = set(r.sample(candidates, N_SCAN))
    scan_ids |= {BUNDLED_IDX[0], BUNDLED_IDX[1]} if len(scan_ids) < N_SCAN + 2 else set()
    # spike subset: largest, mid, all bundled, one per template, scanned, non-USD
    spike_ids = {BIG_ID, MID_ID} | set(BUNDLED_IDX)
    spike_ids |= {i for i in range(1, len(layout.TEMPLATES) + 1)}  # first 24 items cover templates T01..T24
    spike_ids |= set(sorted(scan_ids)[:8])

    sizes = collections.Counter()
    b_items = []
    for i in all_idx:
        gt = make_b(i, scan_ids, spike_ids)
        write_json(os.path.join(GT, "synthetic-invoices", gt["id"] + ".json"), gt)
        b_items.append(gt)
    f_items = []
    for i in range(1, N_F_SEEDED + N_F_CLEAN + 1):
        gt = make_f(i, i <= N_F_SEEDED, rule_sets)
        write_json(os.path.join(GT, "compliance-invoices", gt["id"] + ".json"), gt)
        f_items.append(gt)

    by_type = collections.Counter(f["rule_type"] for g in f_items for f in g["expected_findings"])
    summary = {
        "generator": layout.GENERATOR_ID, "base_seed": BASE_SEED,
        "set_B": {"invoices": len(b_items), "templates_used": len({g["document"]["template"] for g in b_items}),
                  "scan_noise": sum(g["document"]["scan_noise"] for g in b_items),
                  "bundled": sum(g["document"]["bundled"] for g in b_items),
                  "max_pages": max(g["document"]["pages"] for g in b_items),
                  "largest": max(b_items, key=lambda g: g["document"]["pages"])["id"],
                  "non_usd": sum(g["header"]["currency"] != "USD" for g in b_items),
                  "lines": sum(len(g["lines"]) for g in b_items), "spike_subset": sorted(g["id"] for g in b_items if g["meta"]["spike"])},
        "set_F": {"seeded_invoices": N_F_SEEDED, "clean_invoices": N_F_CLEAN,
                  "seeded_violations": sum(g["seeded_violations"] for g in f_items),
                  "by_rule_type": dict(sorted(by_type.items())), "rule_sets": [rs["id"] for rs in rule_sets],
                  "lines": sum(len(g["lines"]) for g in f_items)},
    }
    write_json(os.path.join(GT, "summary.json"), summary)
    print(json.dumps(summary, indent=1)[:2500])


def cmd_render(args):
    from reportlab.pdfgen import canvas  # noqa: F401
    ids = args.ids or [os.path.basename(p)[:-5] for p in sorted(glob.glob(os.path.join(GT, "synthetic-invoices", "*.json")))]
    out_dir = os.path.join(ES, "local", "synthetic")
    os.makedirs(out_dir, exist_ok=True)
    for rid in ids:
        sub = "synthetic-invoices" if rid.startswith("B-") else "compliance-invoices"
        with open(os.path.join(GT, sub, rid + ".json"), encoding="utf-8") as fh:
            gt = json.load(fh)
        pages, first, npages, tpl, inv = paginate(gt)
        dst = os.path.join(out_dir, rid + ".pdf")
        scan = gt["document"].get("scan")
        clean = dst if not scan else dst + ".clean.pdf"
        layout.draw_pdf(clean, inv, tpl, pages, first, npages)
        problems = text_problems(gt, clean)
        if problems:
            print(rid, "RENDER DISAGREES WITH GROUND TRUTH:", problems[:5])
            sys.exit(1)
        if scan:
            from scan import scanify
            scanify(clean, dst, scan, BASE_SEED + int(rid[2:]))
            os.remove(clean)
        manifest = load_render_manifest()
        manifest[rid] = {"gt_sha256": gt_sha(rid), "pages": gt["document"]["pages"], "scanned": bool(scan)}
        save_render_manifest(manifest)
        print(rid, gt["document"]["pages"], "pages", "scanned" if scan else "")


# ------------------------------------------------------------------ PDF versus ground truth
RENDER_MANIFEST = os.path.join(ES, "local", "synthetic", "render-manifest.json")


def gt_path(rid):
    return os.path.join(GT, "synthetic-invoices" if rid.startswith("B-") else "compliance-invoices", rid + ".json")


def gt_sha(rid):
    # Line endings are normalised so a checkout with core.autocrlf (CRLF) hashes the same as LF.
    with open(gt_path(rid), "rb") as fh:
        return hashlib.sha256(fh.read().replace(b"\r\n", b"\n")).hexdigest()


def load_render_manifest():
    try:
        with open(RENDER_MANIFEST, encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, ValueError):
        return {}


def save_render_manifest(m):
    os.makedirs(os.path.dirname(RENDER_MANIFEST), exist_ok=True)
    with open(RENDER_MANIFEST, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(m, fh, indent=1, sort_keys=True)


def pdf_pages_and_text(path):
    import pypdfium2 as pdfium
    doc = pdfium.PdfDocument(path)
    txt = "".join(doc[i].get_textpage().get_text_range() for i in range(len(doc)))
    n = len(doc)
    doc.close()
    return n, txt


def text_problems(gt, pdf_path):
    """Compare a text-layer PDF with its ground truth. Whitespace is ignored (wrapping splits narratives)."""
    n, raw = pdf_pages_and_text(pdf_path)
    t = "".join(raw.split())
    tl = t.lower()  # templates may print narratives in capitals
    tpl = layout.TEMPLATES[int(gt["document"]["template"][1:]) - 1]
    h = gt["header"]
    cur = h["currency"]
    out = []
    if n != gt["document"].get("pages", n):
        out.append(f"pages {n} != {gt['document']['pages']}")
    for k in ("firm_invoice_number", "firm_matter_reference", "po_number"):
        if h.get(k) and "".join(str(h[k]).split()) not in t:
            out.append(f"header {k} {h[k]!r} not in PDF")
    for k in ("total_due", "fees_total", "expenses_total"):
        if h.get(k) is not None and h[k] != 0 and f"{abs(h[k]):,.2f}" not in t:
            out.append(f"header {k} {h[k]} not in PDF")
    if h.get("invoice_date") and "".join(layout.fmt_date(h["invoice_date"], tpl).split()) not in t:
        out.append("invoice_date not in PDF")
    for l in gt["lines"]:
        if f"{abs(l['amount']):,.2f}" not in t:
            out.append(f"line {l['n']} amount {l['amount']} not in PDF")
        if l["line_type"] == "fee" and l.get("hours") is not None and "hours" in tpl["cols"]\
                and "".join(layout.fmt_hours(l["hours"], tpl).split()) not in t:
            out.append(f"line {l['n']} hours {l['hours']} not in PDF")
        frag = "".join(str(l["narrative"]).split())[:28]
        if frag and frag.lower() not in tl:
            out.append(f"line {l['n']} narrative not in PDF")
    return out


def cmd_check(args):
    from reportlab.pdfgen import canvas  # noqa: F401
    import tempfile
    ids = args.ids or sorted(os.path.basename(p)[:-5] for d in ("synthetic-invoices", "compliance-invoices")
                             for p in glob.glob(os.path.join(GT, d, "*.json")))
    bad = 0
    with tempfile.TemporaryDirectory() as tmp:
        for rid in ids:
            with open(gt_path(rid), encoding="utf-8") as fh:
                gt = json.load(fh)
            pages, first, npages, tpl, inv = paginate(gt)
            clean = os.path.join(tmp, rid + ".pdf")
            layout.draw_pdf(clean, inv, tpl, pages, first, npages)
            pr = text_problems(gt, clean)
            if pr:
                bad += 1
                print(rid, "RENDER vs GT:", pr[:5])
    manifest = load_render_manifest()
    local = sorted(glob.glob(os.path.join(ES, "local", "synthetic", "*.pdf")))
    for p in local:
        rid = os.path.basename(p)[:-4]
        if args.ids and rid not in args.ids:
            continue
        m = manifest.get(rid)
        if not m or not os.path.exists(gt_path(rid)):
            bad += 1
            print(rid, "STALE: PDF has no render-manifest entry or no ground truth; re-run render", rid)
            continue
        if m["gt_sha256"] != gt_sha(rid):
            bad += 1
            print(rid, "STALE: ground truth changed after this PDF was rendered; re-run render", rid)
            continue
        with open(gt_path(rid), encoding="utf-8") as fh:
            gt = json.load(fh)
        paginate(gt)
        if not m["scanned"]:
            pr = text_problems(gt, p)
            if pr:
                bad += 1
                print(rid, "PDF vs GT:", pr[:5])
        else:
            n, _ = pdf_pages_and_text(p)
            if n != gt["document"]["pages"]:
                bad += 1
                print(rid, f"PDF pages {n} != ground truth {gt['document']['pages']}")
    print(f"CHECK checked {len(ids)} ground-truth items and {len(local)} local PDFs:", "PASS" if not bad else f"FAIL ({bad})")
    sys.exit(1 if bad else 0)


def cmd_verify(_):
    ok = True
    rule_sets = {}
    for p in glob.glob(os.path.join(GT, "rule-sets", "*.json")):
        rs = json.load(open(p, encoding="utf-8"))
        rule_sets[rs["id"]] = rs
    seeded = clean = viol = 0
    types = collections.Counter()
    for p in sorted(glob.glob(os.path.join(GT, "compliance-invoices", "*.json"))):
        g = json.load(open(p, encoding="utf-8"))
        again = RU.evaluate(g, rule_sets[g["rule_set_id"]])
        if again != g["expected_findings"]:
            print("MISMATCH", g["id"])
            ok = False
        if g["expected_findings"]:
            seeded += 1
            viol += len(g["expected_findings"])
            types.update(f["rule_type"] for f in g["expected_findings"])
        else:
            clean += 1
    b = [json.load(open(p, encoding="utf-8")) for p in sorted(glob.glob(os.path.join(GT, "synthetic-invoices", "*.json")))]
    res = {"F_seeded": seeded, "F_clean": clean, "F_violations": viol, "F_types": dict(types), "B": len(b),
           "B_templates": len({g["document"]["template"] for g in b}),
           "B_scan_pct": round(100 * sum(g["document"]["scan_noise"] for g in b) / max(1, len(b)), 1),
           "B_bundled": sum(g["document"]["bundled"] for g in b), "B_max_pages": max(g["document"]["pages"] for g in b),
           "B_spike": sum(g["meta"]["spike"] for g in b)}
    print(json.dumps(res, indent=1))
    need = res["F_seeded"] >= 300 and res["F_clean"] >= 300 and res["B"] >= 150 and res["B_templates"] >= 20 \
        and res["B_bundled"] >= 3 and res["B_max_pages"] > 20 and len(types) == 11 and ok
    print("VERIFY", "PASS" if need else "FAIL")
    sys.exit(0 if need else 1)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("data").set_defaults(fn=cmd_data)
    rp = sub.add_parser("render")
    rp.add_argument("ids", nargs="*")
    rp.set_defaults(fn=cmd_render)
    sub.add_parser("verify").set_defaults(fn=cmd_verify)
    cp = sub.add_parser("check")
    cp.add_argument("ids", nargs="*")
    cp.set_defaults(fn=cmd_check)
    a = ap.parse_args()
    a.fn(a)
