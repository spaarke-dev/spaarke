"""Task 005 coding comparison: gpt-5.4-mini vs gpt-5.6-luna on the same lines and a fixed code list.

usage: python coding.py run MODEL CORPUS     MODEL in {gpt-5.4-mini, gpt-5.6-luna}; CORPUS in {A, B}
       python coding.py score

Inputs are ground-truth lines (narrative, title as billed, hours), so coding is measured apart from
extraction. Codes in the prompt come from closed lists; the output schema restricts them to an enum (FR-22).
  A = set A fee applications cleared for model use (A-03, A-04, A-06, A-07): bankruptcy task codes
      (scripts/seed-data/ebilling/utbms-bankruptcy.csv, task 018) scored against the FILED code. Filed codes
      outside the 21-code list are mapped to their parent (B145 -> B140, B155 -> B150, B115 -> B110,
      B161/B164 -> B160); phase-only codes (B200, B280) and categories with no UTBMS code
      ("SETTLEMENT AND COMPROMISE") are not scored. A-07 prints category names, mapped by name.
  B = synthetic set B spike items B-0001..B-0139 (B-0149 / B-0150 excluded for size): litigation task codes and
      activity codes (generator refdata, A101-A112) scored against intended_codes, the independent answer key.
Both models run at reasoning_effort low, batch 25 lines per call (FR-48 chunk), strict JSON schema.
Records: runs/coding-<model>-<corpus>.jsonl (per call: lines, latency, usage); predictions in the
git-ignored local folder (they carry line numbers only, but stay with the inputs).
"""
from __future__ import annotations

import csv
import json
import statistics
import sys
import time

from common import (AOAI_API, LOCAL_RAW, OPENAI_ENDPOINT, PROJECT, RESULTS, RUNS, http, load_full_gt, load_gt,
                    pii_cleared, utcnow)

REPO = PROJECT.parents[1]
SEED = REPO / "scripts" / "seed-data" / "ebilling"
BATCH = 25
A_ITEMS = ["A-03", "A-04", "A-06", "A-07"]
B_ITEMS = ["B-%04d" % i for i in list(range(1, 25)) + [26, 31, 43, 55, 67, 79, 91, 103, 115, 127, 139]]
A07_MAP = {"CASE ADMINISTRATION": "B110", "ASSET ANALYSIS AND RECOVERY": "B120",
           "ASSET DISPOSITION/PRESERVATION": "B130", "RELIEF FROM STAY PROCEEDING/ADEQUATE PROTECTION": "B140",
           "FEE/EMPLOYMENT APPLICATION": "B160", "BUSINESS OPERATIONS": "B210", "FINANCING": "B230",
           "CLAIMS ADMINISTRATION AND OBJECTION": "B310"}
PARENT = {"B145": "B140", "B155": "B150", "B115": "B110", "B161": "B160", "B164": "B160"}
ACTIVITY = {"A101": "Plan and prepare for", "A102": "Research", "A103": "Draft/Revise", "A104": "Review/Analyze",
            "A105": "Communicate (within legal team)", "A106": "Communicate (with client)",
            "A107": "Communicate (opponents/other outside counsel)", "A108": "Communicate (other external)",
            "A109": "Appear For/Attend", "A110": "Manage Data/Files/Documentation", "A111": "Other",
            "A112": "Billable Travel Time"}
MODELS = {"gpt-5.4-mini": "gpt-5.4-mini", "gpt-5.6-luna": "gpt-5.6-luna"}


def code_table(name):
    rows = list(csv.DictReader((SEED / name).open(encoding="utf-8")))
    return {r["code"]: (r["description"], r.get("guidance_text") or "") for r in rows if r["code_type"] == "Task"}


def corpus(c):
    """-> (lines [{key, n, narrative, title, hours, task, activity}], task codes, activity codes|None)"""
    out = []
    if c == "A":
        tasks = code_table("utbms-bankruptcy.csv")
        for iid in A_ITEMS:
            gt = load_gt(iid)
            assert pii_cleared(gt), iid
            full = load_full_gt(iid)
            for ln in full["lines"]:
                if ln["line_type"] != "fee" or not ln.get("codes") or not ln.get("narrative"):
                    continue
                filed = ln["codes"][0]
                code = A07_MAP.get(filed) if iid == "A-07" else PARENT.get(filed, filed)
                if code not in tasks:
                    continue
                out.append({"key": f"{iid}:{ln['n']}", "narrative": ln["narrative"], "title": ln.get("title"),
                            "hours": ln.get("hours"), "task": code, "activity": None})
        return out, tasks, None
    tasks = code_table("utbms-litigation.csv")
    for iid in B_ITEMS:
        gt = load_gt(iid)
        for ln in gt["lines"]:
            if ln["line_type"] != "fee":
                continue
            ic = gt["intended_codes"].get(str(ln["n"])) or []
            t = next((x for x in ic if x.startswith("L")), None)
            a = next((x for x in ic if x.startswith("A")), None)
            if t:
                out.append({"key": f"{iid}:{ln['n']}", "narrative": ln["narrative"], "title": ln.get("title"),
                            "hours": ln.get("hours"), "task": t, "activity": a})
    return out, tasks, ACTIVITY


SYSTEM = ("You assign UTBMS codes to law-firm time entries for invoice review. For each line, choose the single best "
          "task code from the task code list using the code descriptions and guidance; when an activity code list is "
          "given, also choose the single best activity code. Use only codes from the lists. Judge from the narrative; "
          "the timekeeper title and hours are context. Give a confidence between 0 and 1 that your task code is the "
          "one the firm should have billed.")


def schema(tasks, acts):
    item = {"n": {"type": "integer"}, "task_code": {"type": "string", "enum": sorted(tasks)},
            "confidence": {"type": "number"}}
    if acts:
        item["activity_code"] = {"type": "string", "enum": sorted(acts)}
    return {"type": "object", "properties": {"lines": {"type": "array", "items": {
        "type": "object", "properties": item, "required": list(item), "additionalProperties": False}}},
        "required": ["lines"], "additionalProperties": False}


def prompt(batch, tasks, acts):
    tl = "\n".join(f"{k} {d}" + (f" -- {g}" if g else "") for k, (d, g) in sorted(tasks.items()))
    al = ("\n\nActivity codes:\n" + "\n".join(f"{k} {d}" for k, d in sorted(acts.items()))) if acts else ""
    lines = [{"n": i + 1, "narrative": ln["narrative"], "timekeeper_title": ln["title"], "hours": ln["hours"]}
             for i, ln in enumerate(batch)]
    return f"Task codes:\n{tl}{al}\n\nLines:\n{json.dumps(lines, ensure_ascii=False)}"


def run(model, c):
    lines, tasks, acts = corpus(c)
    sch = schema(tasks, acts)
    preds = {}
    outp = LOCAL_RAW / "coding" / f"{model}-{c}.json"
    outp.parent.mkdir(parents=True, exist_ok=True)
    runf = RUNS / f"coding-{model}-{c}.jsonl"
    print(f"{utcnow()} {model} corpus {c}: {len(lines)} lines", flush=True)
    for b0 in range(0, len(lines), BATCH):
        batch = lines[b0:b0 + BATCH]
        body = {"messages": [{"role": "system", "content": SYSTEM},
                             {"role": "user", "content": prompt(batch, tasks, acts)}],
                "response_format": {"type": "json_schema", "json_schema": {"name": "LineCodes", "schema": sch, "strict": True}},
                "reasoning_effort": "low", "max_completion_tokens": 16000}
        t0 = time.time()
        st, _, res = http("POST", f"{OPENAI_ENDPOINT}/openai/deployments/{MODELS[model]}/chat/completions?api-version={AOAI_API}",
                          body, timeout=300)
        rec = {"batch": b0 // BATCH, "lines": len(batch), "latency_s": round(time.time() - t0, 2), "http": st}
        if st == 200:
            u = res["usage"]
            rec["usage"] = {"prompt_tokens": u.get("prompt_tokens"), "completion_tokens": u.get("completion_tokens"),
                            "cached_tokens": (u.get("prompt_tokens_details") or {}).get("cached_tokens"),
                            "reasoning_tokens": (u.get("completion_tokens_details") or {}).get("reasoning_tokens")}
            rec["model"] = res.get("model")
            try:
                out = json.loads(res["choices"][0]["message"]["content"])["lines"]
                for o in out:
                    if 1 <= o["n"] <= len(batch):
                        preds[batch[o["n"] - 1]["key"]] = {k: o.get(k) for k in ("task_code", "activity_code", "confidence")}
                rec["returned"] = len(out)
            except (ValueError, KeyError, TypeError):
                rec["error"] = "unparseable"
        else:
            rec["error"] = (res or {}).get("error", {}).get("code") if isinstance(res, dict) else str(st)
        with runf.open("a", encoding="utf-8") as f:
            f.write(json.dumps(rec) + "\n")
        print(rec["batch"], rec["http"], rec["latency_s"], rec.get("error", ""), flush=True)
    outp.write_text(json.dumps(preds), encoding="utf-8")


def price(model, u):
    p = json.loads((PROJECT / "notes" / "spikes" / "extraction" / "prices.json").read_text())["per_million_tokens"][model]
    cached = u.get("cached_tokens") or 0
    return ((u["prompt_tokens"] - cached) * p["input"] + cached * p["cached_input"] + u["completion_tokens"] * p["output"]) / 1e6


def score():
    out = {}
    for c in ("A", "B"):
        lines, _, acts = corpus(c)
        for model in MODELS:
            pf = LOCAL_RAW / "coding" / f"{model}-{c}.json"
            rf = RUNS / f"coding-{model}-{c}.jsonl"
            if not pf.exists():
                continue
            preds = json.loads(pf.read_text(encoding="utf-8"))
            recs = [json.loads(x) for x in rf.read_text(encoding="utf-8").splitlines() if x.strip()]
            ok = [r for r in recs if r.get("http") == 200]
            t_ok = sum(1 for ln in lines if (preds.get(ln["key"]) or {}).get("task_code") == ln["task"])
            res = {"lines": len(lines), "predicted": sum(1 for ln in lines if ln["key"] in preds),
                   "task_top1": round(100 * t_ok / len(lines), 1)}
            # phase = code family (B1xx administration, L1xx case assessment, ...): a near-miss inside the family
            ph_ok = sum(1 for ln in lines if ((preds.get(ln["key"]) or {}).get("task_code") or "")[:2] == ln["task"][:2])
            res["task_phase_top1"] = round(100 * ph_ok / len(lines), 1)
            if acts:
                a_ok = sum(1 for ln in lines if (preds.get(ln["key"]) or {}).get("activity_code") == ln["activity"])
                res["activity_top1"] = round(100 * a_ok / len(lines), 1)
            hi = [(preds[ln["key"]]["task_code"] == ln["task"]) for ln in lines
                  if ln["key"] in preds and (preds[ln["key"]].get("confidence") or 0) >= 0.9]
            res["conf_ge_090"] = {"n": len(hi), "wrong": hi.count(False),
                                  "wrong_rate": round(100 * hi.count(False) / len(hi), 1) if hi else None}
            lo = [(preds[ln["key"]]["task_code"] == ln["task"]) for ln in lines
                  if ln["key"] in preds and (preds[ln["key"]].get("confidence") or 0) < 0.9]
            res["conf_lt_090_accuracy"] = round(100 * lo.count(True) / len(lo), 1) if lo else None
            lat = [r["latency_s"] for r in ok]
            res["calls"] = len(recs)
            res["failed_calls"] = len(recs) - len(ok)
            res["latency_per_call_p50_s"] = round(statistics.median(lat), 1) if lat else None
            res["latency_per_call_p90_s"] = round(sorted(lat)[int(0.9 * (len(lat) - 1))], 1) if lat else None
            res["latency_per_line_ms"] = round(1000 * sum(lat) / sum(r["lines"] for r in ok), 0) if ok else None
            cost = sum(price(model, r["usage"]) for r in ok)
            res["cost_per_line_usd"] = round(cost / sum(r["lines"] for r in ok), 6) if ok else None
            res["cost_per_1000_lines_usd"] = round(1000 * cost / sum(r["lines"] for r in ok), 3) if ok else None
            res["tokens"] = {k: sum((r["usage"].get(k) or 0) for r in ok) for k in ("prompt_tokens", "completion_tokens", "reasoning_tokens", "cached_tokens")}
            out[f"{model}/{c}"] = res
            print(model, c, json.dumps(res))
    for c in ("A", "B"):
        lines, _, _ = corpus(c)
        pp = [LOCAL_RAW / "coding" / f"{m}-{c}.json" for m in MODELS]
        if all(x.exists() for x in pp):
            a, b = (json.loads(x.read_text(encoding="utf-8")) for x in pp)
            agree = [ln for ln in lines if (a.get(ln["key"]) or {}).get("task_code") == (b.get(ln["key"]) or {}).get("task_code")]
            out[f"agreement/{c}"] = {"models_agree": round(100 * len(agree) / len(lines), 1),
                                     "accuracy_when_agree": round(100 * sum(1 for ln in agree if a[ln["key"]]["task_code"] == ln["task"]) / len(agree), 1) if agree else None}
            print("agreement", c, out[f"agreement/{c}"])
    RESULTS.mkdir(exist_ok=True)
    (RESULTS / "coding.json").write_text(json.dumps(out, indent=1), encoding="utf-8")


if __name__ == "__main__":
    if sys.argv[1] == "run":
        run(sys.argv[2], sys.argv[3])
    else:
        score()
