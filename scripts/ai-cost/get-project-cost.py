"""Compute all-time estimated AI cost for a given Spaarke project slug, from local
Claude Code transcripts. Usage: python project_cost.py <slug> [<slug> ...]
Prints one JSON line per slug: {slug, folder, cost, calls, output_tokens, days_active, found}
"""
import json, os, glob, sys
from datetime import datetime
from collections import defaultdict

CLAUDE_PROJECTS = os.path.expanduser(r"~\.claude\projects")

PRICE = {
    "opus-5-5": (4, 20, 0.20), "fable-5-1": (10, 50, 0.25), "fable-5": (10, 50, 1.00),
    "mythos": (10, 50, 0.25), "opus-5": (5, 25, 0.50), "opus-4": (5, 25, 0.50),
    "sonnet-5": (2, 10, 0.20), "sonnet-4": (3, 15, 0.30), "haiku": (1, 5, 0.10),
}
def price_for(model):
    m = model or ""
    for k in ["opus-5-5","fable-5-1","fable-5","mythos","opus-5","opus-4","sonnet-5","sonnet-4","haiku"]:
        if k in m: return PRICE[k]
    return (0, 0, 0)

def find_folder(slug):
    """Match the Claude Code project folder for a given repo project slug."""
    cands = []
    want = slug.lower()
    for d in os.listdir(CLAUDE_PROJECTS):
        dl = d.lower()
        if dl == f"c--code-files-spaarke-wt-{want}" or dl.endswith(f"-wt-{want}"):
            cands.append(d)
    if cands:
        return cands[0]
    # fallback: fuzzy contains
    for d in os.listdir(CLAUDE_PROJECTS):
        if want in d.lower():
            cands.append(d)
    return cands[0] if cands else None

def cost_for_folder(folder):
    seen = set()
    tot_cost = 0.0; calls = 0; out_tok = 0
    min_ts = max_ts = None
    path = os.path.join(CLAUDE_PROJECTS, folder)
    for f in glob.glob(os.path.join(path, "**", "*.jsonl"), recursive=True):
        with open(f, encoding="utf-8", errors="ignore") as fh:
            for line in fh:
                if '"usage"' not in line:
                    continue
                try:
                    e = json.loads(line)
                except Exception:
                    continue
                msg = e.get("message") or {}
                u = msg.get("usage")
                if not u or e.get("type") != "assistant":
                    continue
                model = msg.get("model", "")
                if model == "<synthetic>":
                    continue
                ts = e.get("timestamp")
                if not ts:
                    continue
                key = (msg.get("id"), e.get("requestId"))
                if key[0] and key in seen:
                    continue
                seen.add(key)
                dt = datetime.fromisoformat(ts.replace("Z","+00:00"))
                min_ts = dt if min_ts is None or dt < min_ts else min_ts
                max_ts = dt if max_ts is None or dt > max_ts else max_ts
                pin, pout, pcr = price_for(model)
                inp = u.get("input_tokens",0) or 0
                out = u.get("output_tokens",0) or 0
                cr = u.get("cache_read_input_tokens",0) or 0
                cc = u.get("cache_creation") or {}
                w5 = cc.get("ephemeral_5m_input_tokens"); w1 = cc.get("ephemeral_1h_input_tokens")
                cwt = u.get("cache_creation_input_tokens",0) or 0
                if w5 is None and w1 is None: w5, w1 = cwt, 0
                w5 = w5 or 0; w1 = w1 or 0
                speed = 2 if u.get("speed")=="fast" else 1
                tot_cost += speed*(inp*pin + out*pout + cr*pcr + w5*pin*1.25 + w1*pin*2)/1e6
                calls += 1; out_tok += out
    days = (max_ts - min_ts).days + 1 if min_ts else 0
    return dict(cost=round(tot_cost,2), calls=calls, output_tokens=out_tok, days_active=days,
                first=min_ts.strftime("%Y-%m-%d") if min_ts else None,
                last=max_ts.strftime("%Y-%m-%d") if max_ts else None)

if __name__ == "__main__":
    slugs = sys.argv[1:]
    for slug in slugs:
        folder = find_folder(slug)
        if not folder:
            print(json.dumps({"slug": slug, "found": False}))
            continue
        r = cost_for_folder(folder)
        r.update(slug=slug, folder=folder, found=True)
        print(json.dumps(r))
