"""Write the spike-only analyzer definitions (derived from infrastructure/content-understanding/sprk-invoice-v1.json).

Spike only (D-53): these are never deployed from infrastructure/ or scripts/. Created by deploy_spike_analyzers.py.
"""
import copy
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[4]
V1 = json.loads((REPO / "infrastructure" / "content-understanding" / "sprk-invoice-v1.json").read_text(encoding="utf-8"))
OUT = HERE / "analyzers"
OUT.mkdir(exist_ok=True)

# Activity codes as the evaluation-set generator uses them (refdata.ACTIVITY, ABA UTBMS 1997 A101-A111 + A112 travel).
ACTIVITY = {"A101": "Plan and prepare for", "A102": "Research", "A103": "Draft/Revise", "A104": "Review/Analyze",
            "A105": "Communicate (within legal team)", "A106": "Communicate (with client)",
            "A107": "Communicate (opponents/other outside counsel)", "A108": "Communicate (other external)",
            "A109": "Appear For/Attend", "A110": "Manage Data/Files/Documentation", "A111": "Other",
            "A112": "Billable Travel Time"}


def write(name, d):
    (OUT / f"{name}.json").write_text(json.dumps(d, indent=2) + "\n", encoding="utf-8")


# c-prime: identical schema, new id, created on 2026-06-01-preview
cp = copy.deepcopy(V1)
cp["analyzerId"] = "sprk_invoice_v1p"
cp["description"] = "SPIKE 005 arm c-prime: copy of sprk_invoice_v1 created on 2026-06-01-preview. " + V1["description"]
cp["tags"] = {"project": "ebilling-r1", "schemaVersion": "v1", "spike": "005"}
write("sprk_invoice_v1p", cp)

# classify experiment: copy of v1 + ActivityCodeClassified (classify) on each time entry
cls = copy.deepcopy(V1)
cls["analyzerId"] = "sprk_invoice_v1_cls"
cls["description"] = "SPIKE 005 classify experiment: copy of sprk_invoice_v1 with a classify activity-code field."
cls["tags"] = {"project": "ebilling-r1", "schemaVersion": "v1-cls", "spike": "005"}
props = cls["fieldSchema"]["fields"]["TimeEntries"]["items"]["properties"]
props["ActivityCodeClassified"] = {
    "type": "string", "method": "classify",
    "description": "The UTBMS activity code that best describes the work in this time entry's narrative, "
                   "chosen from the list even when the row prints no activity code.",
    "enum": list(ACTIVITY), "enumDescriptions": ACTIVITY}
write("sprk_invoice_v1_cls", cls)

# bundled-PDF routers (GA and preview): classify + segment, invoice segments routed to the custom analyzer
def router(aid, target, tag):
    return {
        "analyzerId": aid,
        "description": f"SPIKE 005 bundled-PDF router ({tag}): splits a file into cover letter / invoice / other "
                       f"segments and routes invoice segments to {target}.",
        "baseAnalyzerId": "prebuilt-document",
        "config": {"returnDetails": True, "enableOcr": True, "enableLayout": True, "enableSegment": True,
                   "omitContent": False,
                   "contentCategories": {
                       "invoice": {"description": "A law-firm invoice or statement for legal services: invoice number, "
                                                  "fee time entries, expenses and totals due.", "analyzerId": target},
                       "cover_letter": {"description": "A cover or transmittal letter, remittance advice or email "
                                                       "that accompanies an invoice but is not itself the invoice."},
                       "other": {"description": "Any other page that is neither an invoice nor its cover letter."}}},
        "models": {"completion": "gpt-5.2", "embedding": "text-embedding-3-large"},
        "processingLocation": "geography",
        "tags": {"project": "ebilling-r1", "spike": "005"}}


write("sprk_invoice_router", router("sprk_invoice_router", "sprk_invoice_v1", "GA"))
write("sprk_invoice_router_p", router("sprk_invoice_router_p", "sprk_invoice_v1p", "preview"))
print("written", sorted(p.name for p in OUT.iterdir()))
