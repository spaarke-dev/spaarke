#!/usr/bin/env python3
"""Generate manifest.md and manifest.csv for the eBilling evaluation set (task 002, spec FR-02).

    python -I generator/build_manifest.py            # writes manifest.md and manifest.csv next to the generator folder
    python -I generator/build_manifest.py --check    # exit 1 if a FR-02 target or row requirement is not met (no files written)

Inputs: generator/manifest-header.md (section 1-2, hand-written), generator/manifest-notes.md (section 7, hand-written) and the
`meta` block of every ground-truth/**/*.json. Standard library only. The Markdown holds one row per item for the real sets
(A, C, E) and the rule sets, and per-template groups for the synthetic sets (B, F); manifest.csv holds one row per item for all.
"""
import collections
import csv
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
GT = os.path.join(ES, "ground-truth")

TARGETS = [("A", "fee-application", "Fee applications", 6), ("B", "invoice", "Synthetic invoices", 150), ("C", "real-invoice", "Real invoices", 10),
           ("E", "ocg", "OCGs", 15), ("E", "rate-card", "Rate cards", 5), ("F", "seeded", "Seeded-violation invoices", 300), ("F", "clean", "Clean invoices", 300)]
DIRS = {"A": "fee-applications", "B": "synthetic-invoices", "C": "real-invoices", "E-ocg": "ocg", "E-rc": "rate-cards", "F": "compliance-invoices", "RS": "rule-sets"}


def load_all():
    items = []
    for p in sorted(glob.glob(os.path.join(GT, "**", "*.json"), recursive=True)):
        if os.path.basename(p) == "summary.json":
            continue
        with open(p, encoding="utf-8") as fh:
            d = json.load(fh)
        d["_path"] = os.path.relpath(p, ES).replace("\\", "/")
        items.append(d)
    return items


def pii_cell(m):
    s = m.get("pii_scan")
    return m.get("pii_note", "") + (f" (scan {s['scanned']})" if s else "")


def md(s):
    return str(s).replace("|", "/").replace("\n", " ")


def source_of(m):
    return m.get("source_url") or m.get("generator_id") or ""


def rows_for(items):
    out = []
    for d in items:
        m = d["meta"]
        syn = m["licence_status"] == "synthetic" and d["kind"] == "invoice"       # B and F items: licence/title are identical boilerplate
        out.append({"id": d["id"], "set": d["set"], "kind": d["kind"], "title": "" if syn else m["title"], "source_or_generator": source_of(m),
                    "licence_status": m["licence_status"], "licence": "Synthetic (project generator)" if syn else m["licence"], "pii_note": m["pii_note"],
                    "pii_counts": json.dumps(m["pii_scan"]["counts"], separators=(",", ":")) if m.get("pii_scan") else "",
                    "storage_location": m["storage"]["location"], "storage_path": m["storage"]["path"], "ground_truth_file": d["_path"],
                    "spike": m["spike"], "label_method": m["label_method"]})
    return out


def sizes():
    tot, by = 0, {}
    for key, sub in DIRS.items():
        n = sum(os.path.getsize(p) for p in glob.glob(os.path.join(GT, sub, "*.json")))
        by[sub] = n
        tot += n
    for extra in ("summary.json",):
        pth = os.path.join(GT, extra)
        if os.path.exists(pth):
            by[extra] = os.path.getsize(pth)
            tot += by[extra]
    gen = sum(os.path.getsize(p) for p in glob.glob(os.path.join(HERE, "*")) if os.path.isfile(p))
    return by, tot, gen


def local_size():
    n = 0
    for root, _, files in os.walk(os.path.join(ES, "local")):
        for f in files:
            n += os.path.getsize(os.path.join(root, f))
    return n


def build():
    items = load_all()
    by_set = collections.defaultdict(list)
    for d in items:
        by_set[(d["set"], d["kind"])].append(d)
    inv_b = by_set[("B", "invoice")]
    inv_f = by_set[("F", "invoice")]
    seeded = [d for d in inv_f if d.get("expected_findings")]
    clean = [d for d in inv_f if not d.get("expected_findings")]
    counts = {"fee-application": len(by_set[("A", "fee-application")]), "invoice": len(inv_b), "real-invoice": len(by_set[("C", "real-invoice")]),
              "ocg": len(by_set[("E", "ocg")]), "rate-card": len(by_set[("E", "rate-card")]), "seeded": len(seeded), "clean": len(clean)}
    problems = []
    for t_set, key, label, need in TARGETS:
        if counts[key] < need:
            problems.append(f"{label}: {counts[key]} < {need}")
    spike = [d for d in items if d["meta"]["spike"]]
    spike_b = [d for d in spike if d["set"] == "B"]
    bundled = [d for d in inv_b if d["document"].get("bundled")]
    if len(spike_b) < 30 or not any(d["id"] == "B-0150" for d in spike_b) or sum(1 for d in spike_b if d["document"].get("bundled")) < 3:
        problems.append("spike subset: needs >= 30 B items including B-0150 and >= 3 bundled")
    if any(d["meta"]["licence_status"] not in ("open", "public-record-no-licence", "synthetic") for d in items):
        problems.append("a licence_status outside open / public-record-no-licence / synthetic")
    for d in items:
        m = d["meta"]
        for k in ("licence", "pii_note", "storage", "label_method"):
            if not m.get(k):
                problems.append(f"{d['id']}: missing {k}")
        if not (m.get("source_url") or m.get("generator_id")):
            problems.append(f"{d['id']}: no source_url or generator_id")
        if m["licence_status"] == "public-record-no-licence" and "public" not in m["licence"].lower():
            problems.append(f"{d['id']}: no-licence item without the public-record statement")
        if d["set"] in ("A", "C", "E") and m["storage"]["location"] != "local-only":
            problems.append(f"{d['id']}: real document not local-only")
    low = "".join(m.get("licence", "").lower() for m in (d["meta"] for d in items))
    if "non-commercial" in low.replace("no non-commercial", "") or "research-only" in low or "cc by-nc" in low:
        problems.append("a licence text mentions non-commercial or research-only terms")
    return items, by_set, counts, problems, spike, bundled


def table(headers, rows):
    out = ["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|"]
    out += ["| " + " | ".join(md(c) for c in r) + " |" for r in rows]
    return out


def render(items, by_set, counts, problems, spike, bundled):
    with open(os.path.join(HERE, "manifest-header.md"), encoding="utf-8") as fh:
        head = fh.read().rstrip() + "\n"
    with open(os.path.join(HERE, "manifest-notes.md"), encoding="utf-8") as fh:
        notes = fh.read().rstrip() + "\n"
    by, tot, gen = sizes()
    loc = local_size()
    L = [head, "## 3. Counts against the targets (computed)\n"]
    need = {k: n for _, k, _, n in TARGETS}
    rows = [(label, s, need[k], counts[k], "pass" if counts[k] >= need[k] else "FAIL") for s, k, label, _ in TARGETS]
    L += table(["What", "Set", "Target", "Actual", "Result"], rows) + [""]
    summ = json.load(open(os.path.join(GT, "summary.json"), encoding="utf-8"))
    sb, sf = summ["set_B"], summ["set_F"]
    L.append(f"Set B: {sb['invoices']} invoices, {sb['templates_used']} templates, {sb['scan_noise']} scan-noise PDFs ({round(100 * sb['scan_noise'] / sb['invoices'])} percent), "
             f"{sb['bundled']} bundled PDFs, largest {sb['largest']} at {sb['max_pages']} pages, {sb['non_usd']} non-USD, {sb['lines']} lines. "
             f"Set F: {sf['seeded_invoices']} seeded invoices with {sf['seeded_violations']} expected findings across all 11 rule types, {sf['clean_invoices']} clean invoices, "
             f"{len(sf['rule_sets'])} rule sets, {sf['lines']} lines.\n")
    spike_ids = sorted(d["id"] for d in spike)
    L.append(f"Spike subset for task 005 ({len(spike_ids)} items): {', '.join(spike_ids)}. "
             f"It holds {sum(1 for d in spike if d['set'] == 'B')} synthetic items (including B-0150, the {sb['max_pages']}-page invoice, and "
             f"{sum(1 for d in spike if d['set'] == 'B' and d['document'].get('bundled'))} bundled PDFs) and {sum(1 for d in spike if d['set'] in ('A', 'C'))} real documents.\n")
    L.append("## 4. Real and authored items (one row per item)\n")
    heads = ["Id", "What", "Source URL or generator id", "Licence", "PII note", "Storage", "Ground-truth file", "Label"]

    def real_rows(set_letter, kind):
        out = []
        for d in by_set[(set_letter, kind)]:
            m = d["meta"]
            lab = m["label_method"]
            tier = d.get("document", {}).get("tier")
            rec = d.get("reconciliation", {}).get("status")
            lab = lab + (f"; {tier}" if tier else "") + (f"; reconciliation {rec}" if rec else "")
            out.append([d["id"], m["title"], source_of(m), m["licence_status"] + ": " + m["licence"], pii_cell(m), m["storage"]["location"] + " " + m["storage"]["path"], d["_path"], lab])
        return out

    for title, s, k in (("4.1 Set A: fee applications", "A", "fee-application"), ("4.2 Set C: real invoices", "C", "real-invoice"),
                        ("4.3 Set E: outside counsel guidelines", "E", "ocg"), ("4.4 Set E: rate cards", "E", "rate-card")):
        L += [f"### {title}\n"] + table(heads, real_rows(s, k)) + [""]
    L += ["### 4.5 Rule sets (set F context)\n"] + table(heads, real_rows("F", "rule-set")) + [""]
    L.append("## 5. Synthetic items (per-item rows are in manifest.csv)\n")
    grp = collections.defaultdict(list)
    for d in by_set[("B", "invoice")]:
        grp[d["document"]["template"]].append(d["id"])
    L += table(["Template", "Count", "First id", "Last id"], [(t, len(v), v[0], v[-1]) for t, v in sorted(grp.items())]) + [""]
    L.append("Every B and F item has: generator id `gen_eval_set.py@1 <id>` (base seed 20261015, rendering is a pure function of the committed ground truth), "
             "licence `Synthetic (project generator, no third-party rights)`, PII note `Synthetic; no personal data`, storage `regenerate` at "
             "`local/synthetic/<id>.pdf` (render with `gen_eval_set.py render`; check with `gen_eval_set.py check`) and its ground-truth file under "
             "`ground-truth/synthetic-invoices/` (B) or `ground-truth/compliance-invoices/` (F).\n")
    L.append("## 6. Size\n")
    rows = [(k, f"{v:,} bytes") for k, v in sorted(by.items())] + [("generator/ (scripts, registries, notes)", f"{gen:,} bytes"), ("**committed ground truth total**", f"{tot:,} bytes ({tot / 1048576:.2f} MB)")]
    L += table(["Folder", "Size"], rows) + [""]
    L.append(f"Local-only material (not committed, git-ignored): {loc / 1048576:.0f} MB under `local/` (real PDFs, downloaded OCG and rate-card documents, rendered synthetic PDFs, full labels). "
             "Committed size excludes it.\n")
    L.append(notes)
    csv_rows = rows_for(items)
    return "\n".join(L) + "\n", csv_rows


def main():
    check = "--check" in sys.argv
    items, by_set, counts, problems, spike, bundled = build()
    if check:
        print(json.dumps(counts))
        print("CHECK", "PASS" if not problems else "FAIL")
        for p in problems:
            print(" -", p)
        sys.exit(1 if problems else 0)
    text, csv_rows = render(items, by_set, counts, problems, spike, bundled)
    with open(os.path.join(ES, "manifest.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    cols = list(csv_rows[0].keys())
    with open(os.path.join(ES, "manifest.csv"), "w", encoding="utf-8", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=cols, lineterminator="\n")
        w.writeheader()
        w.writerows(csv_rows)
    print(json.dumps(counts))
    print("manifest.md", len(text), "bytes;", "manifest.csv", len(csv_rows), "rows;", "problems:", len(problems))
    for p in problems:
        print(" -", p)
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
