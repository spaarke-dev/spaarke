#!/usr/bin/env python3
"""Write ground-truth/ocg/E-OCG-NN.json and ground-truth/rate-cards/E-RC-NN.json (set E).

Inputs: sources.json (registry), ocg_rules_*.py and ratecards.py (the AI-drafted, paraphrased expected-rule lists and
structured rates), the local documents (read only for the PII scan and the page count). Run with the standard library:
    python -I generator/build_ground_truth_e.py
Evaluation tooling only; label_method is "ai-drafted-pending-review" until a legal-operations reviewer signs the lists off.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ES = os.path.dirname(HERE)
sys.path.insert(0, HERE)

import ocg_rules_a as A  # noqa: E402
import ocg_rules_a2 as A2  # noqa: E402
import ocg_rules_b1 as B1  # noqa: E402
import ocg_rules_b2 as B2  # noqa: E402
import ocg_rules_b3 as B3  # noqa: E402
import pii_scan  # noqa: E402
import ratecards  # noqa: E402

OCG = {}
for m in (A.OCG_A, A2.OCG_A2, B1.OCG_B1, B2.OCG_B2, B3.OCG_B3):
    OCG.update(m)
LABEL = "ai-drafted-pending-review"


def write_json(path, obj):
    """One line per rule/rate/unmapped element; everything else compact (same style as gen_eval_set.write_json)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    comp = lambda o: json.dumps(o, ensure_ascii=False, separators=(",", ":"))
    parts = []
    for k, v in obj.items():
        if k in ("rules", "rates", "unmapped_provisions") and isinstance(v, list):
            body = (",".join([chr(10) + "  " + comp(x) for x in v]))
            parts.append(" " + json.dumps(k) + ":[" + body + ""+chr(10)+" ]" if v else " " + json.dumps(k) + ":[]")
        else:
            parts.append(" " + json.dumps(k) + ":" + comp(v))
    with open(path, "w", encoding="utf-8", newline=chr(10)) as fh:
        fh.write("{"+chr(10)+"" + (","+chr(10)+"").join(parts) + ""+chr(10)+"}"+chr(10)+"")



def pdf_pages(path):
    if not path.lower().endswith(".pdf"):
        return None
    out = subprocess.run(["pdftotext", path, "-"], capture_output=True, timeout=120).stdout.decode("utf-8", errors="ignore")
    return out.count("\f") or None


def disposition(counts):
    if counts.get("ssn") or counts.get("card_number") or counts.get("dob_label"):
        return "REVIEW: identifier-like strings found; do not use in a prompt until checked"
    return "Business and agency contact details only; no private-person data expected. Safe for local evaluation use."


def meta_for(src, kind_label, path_rel):
    local = os.path.join(ES, src["local_path"])
    scan = pii_scan.scan(local)
    c = scan["counts"]
    nz = {k: v for k, v in c.items() if v}
    pii_note = ("Public document from an agency or court; counts only, no matched text recorded. "
                + ("Hits: " + ", ".join(f"{k} {v}" for k, v in nz.items()) + ". " if nz else "No pattern hits. ")
                + "Hits are agency contact details, addresses and generic role words, not private individuals.")
    scan["disposition"] = disposition(c)
    return ({"title": src["title"], "source_url": src["source_url"], "licence": src["licence"],
             "licence_status": src["licence_status"], "pii_note": pii_note, "pii_scan": scan,
             "storage": {"location": "local-only", "path": src["local_path"]}, "spike": False, "label_method": LABEL},
            local)


def main():
    with open(os.path.join(HERE, "sources.json"), encoding="utf-8") as fh:
        reg = json.load(fh)
    n_rules = n_unmapped = 0
    for src in reg["ocg"]:
        rid = src["id"]
        spec = OCG[rid]
        meta, local = meta_for(src, "ocg", f"ground-truth/ocg/{rid}.json")
        rules = []
        for i, r in enumerate(spec["rules"], 1):
            d = {"rule_id": f"{rid}-R{i:02d}"}
            d.update(r)
            rules.append(d)
        body = {"schema": "ebilling-eval/1", "kind": "ocg", "id": rid, "set": "E", "meta": meta,
                "document": {"issuer_type": spec["issuer_type"], "pages": pdf_pages(local) or spec["pages"], "retrieved": reg["retrieved"]},
                "rules": rules, "unmapped_provisions": spec["unmapped"]}
        write_json(os.path.join(ES, "ground-truth", "ocg", rid + ".json"), body)
        n_rules += len(rules)
        n_unmapped += len(spec["unmapped"])
    n_rates = 0
    for src in reg["rate_cards"]:
        rid = src["id"]
        spec = ratecards.RC[rid]
        meta, local = meta_for(src, "rate-card", f"ground-truth/rate-cards/{rid}.json")
        rules = []
        for i, r in enumerate(spec["rules"], 1):
            d = {"rule_id": f"{rid}-R{i:02d}"}
            d.update(r)
            rules.append(d)
        body = {"schema": "ebilling-eval/1", "kind": "rate-card", "id": rid, "set": "E", "meta": meta,
                "document": {"issuer_type": spec["issuer_type"], "currency": spec["currency"], "retrieved": reg["retrieved"],
                             "pages": pdf_pages(local)},
                "rates": spec["rates"], "rules": rules, "unmapped_provisions": spec["unmapped"]}
        write_json(os.path.join(ES, "ground-truth", "rate-cards", rid + ".json"), body)
        n_rates += len(spec["rates"])
    print(f"ocg {len(reg['ocg'])} files, {n_rules} rules, {n_unmapped} unmapped; rate cards {len(reg['rate_cards'])} files, {n_rates} rates")


if __name__ == "__main__":
    main()
