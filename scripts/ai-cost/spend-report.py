#!/usr/bin/env python3
"""On-demand AI-spend report from local Claude Code transcripts.

One pass over ~/.claude/projects/**/*.jsonl, aggregated into rows of
(day, project, model, main|subagent). Reuses pricing (PRICE/price_for) from
get-project-cost.py and applies the same cost rules (dedup on
(message.id, requestId), skip <synthetic>, cache-write 5m=1.25x / 1h=2x input,
speed=="fast" = 2x).

Days are UTC (the transcript timestamp date), same as get-project-cost.py.

Usage:
  python spend-report.py [--days N | --since YYYY-MM-DD] [--project SUBSTR ...]
                         [--format text|json|csv|html] [--out PATH] [--open]
"""
import argparse, csv, glob, importlib.util, io, json, os, sys, tempfile, time
from collections import defaultdict
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("gpc", os.path.join(HERE, "get-project-cost.py"))
gpc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(gpc)

BIG_CTX = 200_000
FIELDS = ["calls", "input", "output", "cache_read", "cache_write", "cost_cache_read",
          "cost_cache_write", "cost_input", "cost_output", "ctx", "big"]


def project_name(folder):
    """Readable project name from a Claude Code project folder name."""
    low = folder.lower()
    pre = "c--code-files-spaarke-wt-"
    if low.startswith(pre):
        return folder[len(pre):]
    if low == "c--code-files-spaarke":
        return "spaarke (main)"
    if low.startswith("c--code-files-"):
        return folder[len("c--code-files-"):]
    if low.startswith("c--"):  # short worktrees such as C--wtf2
        return "wt:" + folder[3:]
    return folder


def new_row():
    return dict.fromkeys(FIELDS, 0)


def collect(since_day):
    """Single pass. Returns (rows keyed (day, project, model, kind), stats)."""
    rows = defaultdict(new_row)
    seen = set()
    cutoff = datetime.strptime(since_day, "%Y-%m-%d").replace(tzinfo=timezone.utc).timestamp()
    nfiles = 0
    root = gpc.CLAUDE_PROJECTS
    for f in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
        try:
            if os.path.getmtime(f) < cutoff:
                continue
        except OSError:
            continue
        nfiles += 1
        folder = os.path.relpath(f, root).split(os.sep)[0]
        proj = project_name(folder)
        kind = "subagent" if (os.sep + "subagents" + os.sep in f or
                              os.path.basename(f).startswith("agent-")) else "main"
        with open(f, encoding="utf-8", errors="ignore") as fh:
            for line in fh:
                if '"usage"' not in line:
                    continue
                try:
                    e = json.loads(line)
                except Exception:
                    continue
                m = e.get("message") or {}
                u = m.get("usage")
                if not u or e.get("type") != "assistant" or m.get("model") == "<synthetic>":
                    continue
                ts = e.get("timestamp")
                if not ts:
                    continue
                k = (m.get("id"), e.get("requestId"))
                if k[0] and k in seen:
                    continue
                seen.add(k)
                day = ts[:10]
                if day < since_day:
                    continue
                model = m.get("model", "") or "unknown"
                pin, pout, pcr = gpc.price_for(model)
                inp = u.get("input_tokens", 0) or 0
                out = u.get("output_tokens", 0) or 0
                cr = u.get("cache_read_input_tokens", 0) or 0
                cc = u.get("cache_creation") or {}
                w5 = cc.get("ephemeral_5m_input_tokens")
                w1 = cc.get("ephemeral_1h_input_tokens")
                if w5 is None and w1 is None:
                    w5, w1 = u.get("cache_creation_input_tokens", 0) or 0, 0
                w5 = w5 or 0
                w1 = w1 or 0
                sp = 2 if u.get("speed") == "fast" else 1
                ctx = inp + cr + w5 + w1
                r = rows[(day, proj, model, kind)]
                r["calls"] += 1
                r["input"] += inp
                r["output"] += out
                r["cache_read"] += cr
                r["cache_write"] += w5 + w1
                r["cost_cache_read"] += sp * cr * pcr / 1e6
                r["cost_cache_write"] += sp * (w5 * pin * 1.25 + w1 * pin * 2) / 1e6
                r["cost_input"] += sp * inp * pin / 1e6
                r["cost_output"] += sp * out * pout / 1e6
                r["ctx"] += ctx
                r["big"] += ctx > BIG_CTX
    return rows, {"files": nfiles}


def total_cost(r):
    return r["cost_cache_read"] + r["cost_cache_write"] + r["cost_input"] + r["cost_output"]


def flat(rows, projects):
    out = []
    for (day, proj, model, kind), r in sorted(rows.items()):
        if projects and not any(p.lower() in proj.lower() for p in projects):
            continue
        d = {"day": day, "project": proj, "model": model, "kind": kind}
        d.update({k: (round(v, 6) if isinstance(v, float) else v) for k, v in r.items()})
        out.append(d)
    return out


def agg(rows, key):
    g = defaultdict(new_row)
    for r in rows:
        a = g[key(r)]
        for f in FIELDS:
            a[f] += r[f]
    return g


def text_report(rows, since, until):
    L = []
    tot = agg(rows, lambda r: "all")["all"]
    T = total_cost(tot) or 1e-9
    L.append(f"AI spend (est., list price) {since}..{until} UTC  total ${total_cost(tot):,.0f}  calls {tot['calls']:,}")
    L.append("")
    L.append(f"{'day':<11}{'calls':>7}{'sub%':>6}{'avg ctx':>9}{'>200k%':>8}{'est $':>9}{'cacheRd$':>9}{'cacheWr$':>9}{'out$':>7}")
    byday = agg(rows, lambda r: r["day"])
    sub_day = agg([r for r in rows if r["kind"] == "subagent"], lambda r: r["day"])
    for day in sorted(byday):
        r = byday[day]
        c = r["calls"]
        s = sub_day[day]["calls"] if day in sub_day else 0
        L.append(f"{day:<11}{c:>7}{100*s/c:>5.0f}%{r['ctx']/c/1000:>8.0f}k{100*r['big']/c:>7.0f}%"
                 f"{total_cost(r):>9,.0f}{r['cost_cache_read']:>9,.0f}{r['cost_cache_write']:>9,.0f}{r['cost_output']:>7,.0f}")
    byp = agg(rows, lambda r: r["project"])
    bym = agg(rows, lambda r: r["model"])
    byk = agg(rows, lambda r: r["kind"])
    L.append("")
    L.append(f"{'Top projects':<44}{'calls':>8}{'est $':>9}{'share':>7}")
    for p, r in sorted(byp.items(), key=lambda kv: -total_cost(kv[1]))[:8]:
        L.append(f"{p[:43]:<44}{r['calls']:>8}{total_cost(r):>9,.0f}{100*total_cost(r)/T:>6.0f}%")
    L.append("")
    L.append(f"{'By model':<44}{'calls':>8}{'est $':>9}{'share':>7}")
    for p, r in sorted(bym.items(), key=lambda kv: -total_cost(kv[1])):
        L.append(f"{p[:43]:<44}{r['calls']:>8}{total_cost(r):>9,.0f}{100*total_cost(r)/T:>6.0f}%")
    L.append("")
    L.append(f"{'Main vs sub-agent':<44}{'calls':>8}{'est $':>9}{'share':>7}")
    for p, r in sorted(byk.items()):
        L.append(f"{p:<44}{r['calls']:>8}{total_cost(r):>9,.0f}{100*total_cost(r)/T:>6.0f}%")
    L.append("")
    L.append("Biggest drivers")
    if tot["calls"]:
        sc = byk.get("subagent", new_row())
        L.append(f"- sub-agents = {100*sc['calls']/tot['calls']:.0f}% of calls, ${total_cost(sc):,.0f} ({100*total_cost(sc)/T:.0f}% of cost)")
        L.append(f"- cache writes = {100*tot['cost_cache_write']/T:.0f}% of cost; cache reads {100*tot['cost_cache_read']/T:.0f}%; output {100*tot['cost_output']/T:.0f}%")
        L.append(f"- avg context {tot['ctx']/tot['calls']/1000:.0f}k per call; {100*tot['big']/tot['calls']:.0f}% of calls over 200k")
        tp = max(byp.items(), key=lambda kv: total_cost(kv[1]))
        L.append(f"- top project {tp[0]}: ${total_cost(tp[1]):,.0f} ({100*total_cost(tp[1])/T:.0f}%)")
        tm = max(bym.items(), key=lambda kv: total_cost(kv[1]))
        L.append(f"- top model {tm[0]}: ${total_cost(tm[1]):,.0f} ({100*total_cost(tm[1])/T:.0f}%)")
    return "\n".join(L) + "\n"


def csv_report(flatrows):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=["day", "project", "model", "kind"] + FIELDS)
    w.writeheader()
    for r in flatrows:
        w.writerow(r)
    return buf.getvalue()


HTML = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Spaarke AI Spend</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--fg:#1c2330;--mute:#667085;--line:#e3e6eb;
--c1:#3b6fd4;--c2:#e08a2e;--c3:#2f9e6b;--c4:#a35bd0;--c5:#d4496b;--c6:#2aa3b8;--c7:#8a8f98;--c8:#b5a024;--c9:#5b6cd0}
@media (prefers-color-scheme:dark){:root{--bg:#12151b;--card:#1b2029;--fg:#e6e9ef;--mute:#9aa3b2;--line:#2a313d;
--c1:#6c9bff;--c2:#f0a550;--c3:#4cc590;--c4:#c186ee;--c5:#f06b8b;--c6:#4cc4d8;--c7:#9aa0aa;--c8:#d6c23f;--c9:#8b98f0}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
.wrap{max-width:1200px;margin:0 auto;padding:16px}
header{display:flex;flex-wrap:wrap;gap:12px;align-items:center;justify-content:space-between;margin-bottom:12px}
h1{font-size:20px;margin:0}.sub{color:var(--mute);font-size:12px}
select{background:var(--card);color:var(--fg);border:1px solid var(--line);border-radius:8px;padding:6px 10px;font:inherit;max-width:100%}
.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-bottom:12px}
.kpi,.panel{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px;min-width:0}
.kpi .l{color:var(--mute);font-size:12px}.kpi .v{font-size:22px;font-weight:650;margin-top:2px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,460px),1fr));gap:12px}
.panel h2{font-size:14px;margin:0 0 8px}
.cv{position:relative;height:260px}
.tw{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{padding:6px 8px;border-bottom:1px solid var(--line);text-align:right;white-space:nowrap}
th:first-child,td:first-child{text-align:left;white-space:normal;word-break:break-word}
th{cursor:pointer;color:var(--mute);font-weight:600;user-select:none}
footer{color:var(--mute);font-size:12px;margin-top:14px}
</style></head><body><div class="wrap">
<header><div><h1>Spaarke AI Spend</h1><div class="sub" id="range"></div></div>
<label class="sub">Project <select id="proj"></select></label></header>
<div class="kpis" id="kpis"></div>
<div class="grid">
<div class="panel"><h2>$ per day by cost component</h2><div class="cv"><canvas id="c1"></canvas></div></div>
<div class="panel"><h2>$ per day by project (top 8)</h2><div class="cv"><canvas id="c2"></canvas></div></div>
<div class="panel"><h2>Calls per day, main vs sub-agent</h2><div class="cv"><canvas id="c3"></canvas></div></div>
<div class="panel"><h2>Average context per call (k tokens)</h2><div class="cv"><canvas id="c4"></canvas></div></div>
<div class="panel"><h2>$ by model</h2><div class="cv"><canvas id="c5"></canvas></div></div>
<div class="panel"><h2>Projects</h2><div class="tw"><table id="tbl"><thead><tr>
<th data-k="project">Project</th><th data-k="calls">Calls</th><th data-k="cost">Est. $</th><th data-k="share">Share</th><th data-k="ctx">Avg ctx</th></tr></thead><tbody></tbody></table></div></div>
</div>
<footer>Estimated at list price from local Claude Code transcripts on this machine; not an invoice. Other machines and claude.ai usage are not included.
<span id="gen"></span></footer></div>
<script id="data" type="application/json">__DATA__</script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.4.1/chart.umd.min.js"></script>
<script>
const D=JSON.parse(document.getElementById('data').textContent);
const R=D.rows;const $=id=>document.getElementById(id);
const cs=n=>getComputedStyle(document.documentElement).getPropertyValue(n).trim();
const col=i=>cs('--c'+((i%9)+1));
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;');
const cost=r=>r.cost_cache_read+r.cost_cache_write+r.cost_input+r.cost_output;
const usd=v=>'$'+Math.round(v).toLocaleString();
$('range').textContent=D.since+' to '+D.until+' (UTC days)';
$('gen').textContent=' Generated '+D.generated+'.';
const projs=[...new Set(R.map(r=>r.project))].sort();
$('proj').innerHTML='<option value="">All projects</option>'+projs.map(p=>'<option>'+esc(p)+'</option>').join('');
let charts=[],sortK='cost',sortD=-1;
function days(){const a=[],s=new Date(D.since+'T00:00:00Z'),e=new Date(D.until+'T00:00:00Z');for(let d=s;d<=e;d=new Date(d.getTime()+864e5))a.push(d.toISOString().slice(0,10));return a}
function render(){
 const p=$('proj').value,rows=p?R.filter(r=>r.project===p):R,ds=days();
 const sum=(f,rs)=>rs.reduce((a,r)=>a+f(r),0);
 const T=sum(cost,rows),calls=sum(r=>r.calls,rows),sub=sum(r=>r.kind==='subagent'?r.calls:0,rows);
 const cw=sum(r=>r.cost_cache_write,rows),ctx=sum(r=>r.ctx,rows);
 const last=rows.filter(r=>r.day===D.until);
 const k=[['Window spend',usd(T)],['Avg per day',usd(T/ds.length)],['Last day ('+D.until.slice(5)+')',usd(sum(cost,last))],
 ['Sub-agent calls',calls?Math.round(100*sub/calls)+'%':'-'],['Cache-write share',T?Math.round(100*cw/T)+'%':'-'],
 ['Avg context/call',calls?Math.round(ctx/calls/1000)+'k':'-']];
 $('kpis').innerHTML=k.map(x=>'<div class="kpi"><div class="l">'+x[0]+'</div><div class="v">'+x[1]+'</div></div>').join('');
 charts.forEach(c=>c.destroy());charts=[];
 const opts=(stack,fmt)=>({responsive:true,maintainAspectRatio:false,interaction:{mode:'index',intersect:false},
  plugins:{legend:{labels:{color:cs('--fg'),boxWidth:12}}},scales:{x:{stacked:stack,ticks:{color:cs('--mute'),maxRotation:60},grid:{color:cs('--line')}},
  y:{stacked:stack,ticks:{color:cs('--mute'),callback:fmt},grid:{color:cs('--line')}}}});
 const comp=[['Cache read','cost_cache_read'],['Cache write','cost_cache_write'],['Input','cost_input'],['Output','cost_output']];
 charts.push(new Chart($('c1'),{type:'bar',data:{labels:ds,datasets:comp.map((c,i)=>({label:c[0],backgroundColor:col(i),data:ds.map(d=>sum(r=>r.day===d?r[c[1]]:0,rows))}))},options:opts(true,usd)}));
 const pt={};rows.forEach(r=>pt[r.project]=(pt[r.project]||0)+cost(r));
 const top=Object.keys(pt).sort((a,b)=>pt[b]-pt[a]).slice(0,8);
 const ps=top.map((n,i)=>({label:n,backgroundColor:col(i),data:ds.map(d=>sum(r=>r.day===d&&r.project===n?cost(r):0,rows))}));
 if(Object.keys(pt).length>8)ps.push({label:'other',backgroundColor:col(8),data:ds.map(d=>sum(r=>r.day===d&&!top.includes(r.project)?cost(r):0,rows))});
 charts.push(new Chart($('c2'),{type:'bar',data:{labels:ds,datasets:ps},options:opts(true,usd)}));
 charts.push(new Chart($('c3'),{type:'line',data:{labels:ds,datasets:[['Main','main',0],['Sub-agent','subagent',1]].map(x=>({label:x[0],borderColor:col(x[2]),backgroundColor:col(x[2]),tension:.25,data:ds.map(d=>sum(r=>r.day===d&&r.kind===x[1]?r.calls:0,rows))}))},options:opts(false,v=>v.toLocaleString())}));
 charts.push(new Chart($('c4'),{type:'line',data:{labels:ds,datasets:[{label:'Avg context (k)',borderColor:col(3),backgroundColor:col(3),tension:.25,
  data:ds.map(d=>{const c=sum(r=>r.day===d?r.calls:0,rows);return c?Math.round(sum(r=>r.day===d?r.ctx:0,rows)/c/1000):null})}]},options:opts(false,v=>v+'k')}));
 const mt={};rows.forEach(r=>mt[r.model]=(mt[r.model]||0)+cost(r));const mk=Object.keys(mt).sort((a,b)=>mt[b]-mt[a]);
 charts.push(new Chart($('c5'),{type:'doughnut',data:{labels:mk,datasets:[{data:mk.map(m=>Math.round(mt[m])),backgroundColor:mk.map((_,i)=>col(i)),borderColor:cs('--card')}]},
  options:{responsive:true,maintainAspectRatio:false,plugins:{legend:{position:'bottom',labels:{color:cs('--fg'),boxWidth:12}}}}}));
 const tb={};rows.forEach(r=>{const t=tb[r.project]=tb[r.project]||{project:r.project,calls:0,cost:0,ctx:0};t.calls+=r.calls;t.cost+=cost(r);t.ctx+=r.ctx});
 const tl=Object.values(tb).map(t=>({...t,share:T?t.cost/T:0,ctx:t.calls?t.ctx/t.calls:0}));
 tl.sort((a,b)=>sortK==='project'?sortD*a.project.localeCompare(b.project):sortD*(a[sortK]-b[sortK]));
 $('tbl').tBodies[0].innerHTML=tl.map(t=>'<tr><td>'+esc(t.project)+'</td><td>'+t.calls.toLocaleString()+'</td><td>'+usd(t.cost)+'</td><td>'+Math.round(100*t.share)+'%</td><td>'+Math.round(t.ctx/1000)+'k</td></tr>').join('');
}
$('proj').onchange=render;
document.querySelectorAll('th[data-k]').forEach(th=>th.onclick=()=>{const k=th.dataset.k;sortD=sortK===k?-sortD:(k==='project'?1:-1);sortK=k;render()});
render();
</script></body></html>
"""


def html_report(flatrows, since, until):
    data = {"since": since, "until": until,
            "generated": datetime.now().strftime("%Y-%m-%d %H:%M"), "rows": flatrows}
    blob = json.dumps(data, separators=(",", ":")).replace("</", "<\\/")
    return HTML.replace("__DATA__", blob)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--days", type=int, default=14)
    ap.add_argument("--since")
    ap.add_argument("--project", action="append", default=[])
    ap.add_argument("--format", choices=["text", "json", "csv", "html"], default="text")
    ap.add_argument("--out")
    ap.add_argument("--open", action="store_true")
    a = ap.parse_args()
    today = datetime.now(timezone.utc).date()
    since = a.since or (today - timedelta(days=a.days - 1)).isoformat()
    until = today.isoformat()
    t0 = time.time()
    rows, st = collect(since)
    fr = flat(rows, a.project)
    if a.format == "text":
        out = text_report(fr, since, until)
    elif a.format == "json":
        out = json.dumps({"since": since, "until": until, "rows": fr}, indent=1)
    elif a.format == "csv":
        out = csv_report(fr)
    else:
        out = html_report(fr, since, until)
        if not a.out:
            a.out = os.path.join(tempfile.gettempdir(), "spaarke-ai-spend.html")
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="") as fh:
            fh.write(out)
        print(f"wrote {a.out} ({len(fr)} rows, {st['files']} files, {time.time()-t0:.1f}s)", file=sys.stderr)
        if a.format == "html" and a.open:
            import webbrowser
            webbrowser.open("file:///" + os.path.abspath(a.out).replace("\\", "/"))
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(out)
        print(f"({st['files']} files, {time.time()-t0:.1f}s)", file=sys.stderr)


if __name__ == "__main__":
    main()
