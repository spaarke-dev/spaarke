#!/usr/bin/env python3
"""On-demand AI-spend report from local Claude Code transcripts.

One pass over ~/.claude/projects/**/*.jsonl, aggregated into rows of
(day, project, model, main|subagent). Reuses pricing (PRICE/price_for) from
get-project-cost.py and applies the same cost rules (dedup on
(message.id, requestId), skip <synthetic>, cache-write 5m=1.25x / 1h=2x input,
speed=="fast" = 2x).

Days are calendar days in --tz (default: the machine's local timezone, DST-aware;
--tz utc buckets by the UTC date of the transcript timestamp). Every output states the tz.

Usage:
  python spend-report.py [--days N | --since YYYY-MM-DD | --today] [--tz local|utc]
                         [--project SUBSTR ...] [--format text|json|csv|html]
                         [--out PATH] [--open] [--archive [PATH]] [--source transcripts|archive]
                         [--model-fit [--launch-log PATH] [--repo PATH]]
  python spend-report.py --actuals [--invoices PATH] [--archive PATH] [--project SUBSTR ...] [--format text|json|csv]

--actuals: what was invoiced (a hand-kept CSV, default %USERPROFILE%\\.claude\\spend-archive\\invoices.csv) against
the archived estimate, by month, billing cycle and year, with each cycle's invoices allocated to projects.

--model-fit: was each piece of work run on the right model? Matches the agent launch log
(%USERPROFILE%\\.claude\\spend-archive\\agent-launches.csv, written by scripts/quality/require-agent-model.py)
to the sub-agent transcripts and tabulates work type x model (see scripts/ai-cost/README.md).

Durable archive: transcripts are deleted by Claude Code after a retention period, so
`--archive [PATH]` upserts this run's days into a CSV (default
%USERPROFILE%\\.claude\\spend-archive\\spend-daily.csv) and never lets a day's total go down.
`--source archive` reports from that file instead of the transcripts.
"""
import argparse, csv, glob, html, importlib.util, io, json, os, re, sys, tempfile, time
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


# A transcript file's mtime is its LAST write, so a file whose mtime is before the start of the
# first requested day cannot hold calls from that day. The start of the day is measured in the
# bucketing tz (local midnight can be hours before UTC midnight), and the cutoff is moved back by
# MTIME_SLACK_S to absorb DST shifts and clock skew. Reading a few extra files is cheap; dropping
# one that holds requested calls is not.
MTIME_SLACK_S = 86400


# --- timezone handling --------------------------------------------------------------------
# `tz` is a tzinfo, or None for the machine's local zone (resolved by the system, DST-aware).

def resolve_tz(name):
    return timezone.utc if name == "utc" else None


def in_tz(dt, tz):
    return dt.astimezone(tz) if tz is not None else dt.astimezone()


def day_of(ts, tz):
    """Calendar day (YYYY-MM-DD) of an ISO-8601 transcript timestamp, in tz."""
    dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return in_tz(dt, tz).date().isoformat()


def today_in(tz, now=None):
    return in_tz(now or datetime.now(timezone.utc), tz).date()


def tz_label(name, now=None):
    """Human-readable description of the day boundaries, for headers."""
    if name == "utc":
        return "UTC"
    n = in_tz(now or datetime.now(timezone.utc), None)
    z = n.strftime("%z")
    off = f"UTC{z[:3]}:{z[3:5]}" if z else "UTC"
    return f"local time ({n.tzname()}, {off})"


def day_start_epoch(day, tz):
    """Epoch seconds of 00:00 on `day` (YYYY-MM-DD) in tz."""
    naive = datetime.strptime(day, "%Y-%m-%d")
    aware = naive.replace(tzinfo=tz) if tz is not None else naive.astimezone()
    return aware.timestamp()


def mtime_cutoff(since_day, tz):
    return day_start_epoch(since_day, tz) - MTIME_SLACK_S


def resolve_window(days, since, today, tz, now=None):
    """(since_day, until_day) as YYYY-MM-DD strings, both in tz."""
    t = today_in(tz, now)
    if today:
        return t.isoformat(), t.isoformat()
    if since:
        return since, t.isoformat()
    return (t - timedelta(days=(14 if days is None else days) - 1)).isoformat(), t.isoformat()


def call_figures(u, model):
    """Token counts and list-price cost parts of one assistant `usage` record (rules in the module doc)."""
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
    return {"input": inp, "output": out, "cache_read": cr, "cache_write": w5 + w1,
            "cost_cache_read": sp * cr * pcr / 1e6,
            "cost_cache_write": sp * (w5 * pin * 1.25 + w1 * pin * 2) / 1e6,
            "cost_input": sp * inp * pin / 1e6, "cost_output": sp * out * pout / 1e6,
            "ctx": inp + cr + w5 + w1}


def is_subagent_file(f):
    return os.sep + "subagents" + os.sep in f or os.path.basename(f).startswith("agent-")


def collect(since_day, tz=None, root=None):
    """Single pass. Returns (rows keyed (day, project, model, kind), stats).

    Days are calendar days in tz (None = machine local zone)."""
    rows = defaultdict(new_row)
    seen = set()
    cutoff = mtime_cutoff(since_day, tz)
    nfiles = 0
    root = root or gpc.CLAUDE_PROJECTS
    for f in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
        try:
            if os.path.getmtime(f) < cutoff:
                continue
        except OSError:
            continue
        nfiles += 1
        folder = os.path.relpath(f, root).split(os.sep)[0]
        proj = project_name(folder)
        kind = "subagent" if is_subagent_file(f) else "main"
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
                try:
                    day = day_of(ts, tz)
                except (ValueError, OverflowError, OSError):
                    continue
                if day < since_day:
                    continue
                model = m.get("model", "") or "unknown"
                fg = call_figures(u, model)
                r = rows[(day, proj, model, kind)]
                r["calls"] += 1
                r["input"] += fg["input"]
                r["output"] += fg["output"]
                r["cache_read"] += fg["cache_read"]
                r["cache_write"] += fg["cache_write"]
                r["cost_cache_read"] += fg["cost_cache_read"]
                r["cost_cache_write"] += fg["cost_cache_write"]
                r["cost_input"] += fg["cost_input"]
                r["cost_output"] += fg["cost_output"]
                r["ctx"] += fg["ctx"]
                r["big"] += fg["ctx"] > BIG_CTX
    return rows, {"files": nfiles}


def total_cost(r):
    return r["cost_cache_read"] + r["cost_cache_write"] + r["cost_input"] + r["cost_output"]


def flat(rows, projects, tz_name="local"):
    out = []
    for (day, proj, model, kind), r in sorted(rows.items()):
        if projects and not any(p.lower() in proj.lower() for p in projects):
            continue
        d = {"day": day, "project": proj, "model": model, "kind": kind}
        d.update({k: (round(v, 6) if isinstance(v, float) else v) for k, v in r.items()})
        d["tz"] = tz_name
        out.append(d)
    return out


def agg(rows, key):
    g = defaultdict(new_row)
    for r in rows:
        a = g[key(r)]
        for f in FIELDS:
            a[f] += r[f]
    return g


def text_report(rows, since, until, tzl="UTC"):
    L = []
    tot = agg(rows, lambda r: "all")["all"]
    T = total_cost(tot) or 1e-9
    L.append(f"AI spend (est., list price) {since}..{until}, days in {tzl}  total ${total_cost(tot):,.0f}  calls {tot['calls']:,}")
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


# `tz` is last so the positions of the pre-existing columns do not move.
CSV_COLUMNS = ["day", "project", "model", "kind"] + FIELDS + ["tz"]


def csv_report(flatrows):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=CSV_COLUMNS)
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
$('range').textContent=D.since+' to '+D.until+' (days in '+D.tz_label+')';
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


def html_report(flatrows, since, until, tz_name="local", tzl="UTC"):
    data = {"since": since, "until": until, "tz": tz_name, "tz_label": tzl,
            "generated": datetime.now().strftime("%Y-%m-%d %H:%M"), "rows": flatrows}
    blob = json.dumps(data, separators=(",", ":")).replace("</", "<\\/")
    return HTML.replace("__DATA__", blob)


# --- durable archive ----------------------------------------------------------------------
# Claude Code deletes old transcripts, so the per-day rows are also kept in a CSV outside the repo
# (it holds project names and costs). Same columns as `--format csv`, so one file feeds a dashboard.

DEFAULT_ARCHIVE = os.path.join(os.path.expanduser("~"), ".claude", "spend-archive", "spend-daily.csv")
INT_FIELDS = ["calls", "input", "output", "cache_read", "cache_write", "ctx", "big"]
COST_FIELDS = ["cost_cache_read", "cost_cache_write", "cost_input", "cost_output"]
COST_EPS = 1e-6      # float noise only; a real deletion drops a day by far more


class ArchiveError(Exception):
    pass


def flat_cost(r):
    return sum(r[k] for k in COST_FIELDS)


def row_key(r):
    return (r["day"], r["project"], r["model"], r["kind"])


def sort_flat(rows):
    return sorted(rows, key=row_key)       # day, then project (then model, kind)


def day_range(since, until):
    d, end, out = datetime.strptime(since, "%Y-%m-%d"), datetime.strptime(until, "%Y-%m-%d"), []
    while d <= end:
        out.append(d.strftime("%Y-%m-%d"))
        d += timedelta(days=1)
    return out


def read_archive(path):
    """Archived rows as flat dicts ([] if the file does not exist). Raises ArchiveError if malformed."""
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8", newline="") as fh:
        rd = csv.DictReader(fh)
        missing = [c for c in CSV_COLUMNS if c not in (rd.fieldnames or [])]
        if missing:
            raise ArchiveError(f"{path} is not a spend archive (missing columns: {', '.join(missing)})")
        rows = []
        for n, rec in enumerate(rd, start=2):
            try:
                r = {k: rec[k] for k in ("day", "project", "model", "kind", "tz")}
                r.update({k: int(rec[k]) for k in INT_FIELDS})
                r.update({k: float(rec[k]) for k in COST_FIELDS})
            except (TypeError, ValueError) as ex:
                raise ArchiveError(f"{path} line {n}: {ex}")
            rows.append(r)
    return rows


def check_archive_tz(rows, tz_name, path):
    """Refuse to mix day boundaries: every archived row must use the run's tz."""
    found = sorted({r["tz"] for r in rows} - {tz_name})
    if found:
        raise ArchiveError(
            f"{path} holds days bucketed in tz '{', '.join(found)}' but this run uses '{tz_name}'. "
            f"Mixing bucket boundaries would double-count or drop calls; re-run with --tz {found[0]} "
            f"or point --archive at a different file.")


def merge_archive(archived, fresh, covered_days):
    """Upsert fresh rows into archived rows, keyed (day, project, model, kind).

    Never-shrink is judged per (day, project): within each covered day, the archived rows of a project
    stay when the fresh rows of that project (absent = none) cost less than the archived ones, or carry
    fewer calls (a model with no price archives $0 with N calls). Otherwise the fresh rows replace them.
    Another project growing the same day does not cover for one whose transcripts vanished. Days outside
    `covered_days` are untouched. Returns (rows sorted by day then project, kept) where kept lists
    (day, project, fresh_cost, archived_cost) for every (day, project) whose archived rows were kept."""
    old_g, new_g = defaultdict(list), defaultdict(list)
    for r in archived:
        old_g[(r["day"], r["project"])].append(r)
    for r in fresh:
        new_g[(r["day"], r["project"])].append(r)
    covered = set(covered_days) | {d for d, _ in new_g}
    merged, kept = [], []
    for key in set(old_g) | set(new_g):
        old, new = old_g.get(key, []), new_g.get(key, [])
        if key[0] not in covered:
            merged.extend(old)
            continue
        old_c, new_c = sum(map(flat_cost, old)), sum(map(flat_cost, new))
        old_n, new_n = sum(r["calls"] for r in old), sum(r["calls"] for r in new)
        if old and (new_c < old_c - COST_EPS or new_n < old_n):
            merged.extend(old)
            kept.append((key[0], key[1], new_c, old_c))
        else:
            merged.extend(new)
    return sort_flat(merged), sorted(kept)


def write_archive(path, rows):
    """Write atomically (temp file in the same folder, then replace); creates the folder."""
    d = os.path.dirname(os.path.abspath(path))
    os.makedirs(d, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=d, prefix=".spend-archive.", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as fh:
            fh.write(csv_report(rows))
            fh.flush()
            os.fsync(fh.fileno())
        os.replace(tmp, path)
    except BaseException:
        if os.path.exists(tmp):
            os.remove(tmp)
        raise


# --- invoiced actuals (--actuals) ---------------------------------------------------------
# The estimate prices this machine's transcripts at list price; the invoices are what was paid. They are typed in by
# hand from the claude.ai billing page into a CSV beside the archive (there is no export for a Max plan). The plan fee
# is charged on the same day each month; every other charge is a credit purchase, mostly auto-recharge when the
# balance runs low. Credits are prepaid, so a charge pays for LATER usage: actuals are compared with the estimate per
# billing cycle and per month, never per day.

DEFAULT_INVOICES = os.path.join(os.path.expanduser("~"), ".claude", "spend-archive", "invoices.csv")
INVOICE_COLUMNS = ["date", "amount", "kind", "invoice_id", "notes"]
INVOICE_KINDS = ("plan", "credits", "other")


def read_invoices(path):
    """Invoice rows sorted by date ([] if the file does not exist). Raises ArchiveError if malformed.

    `amount` may carry `$` and thousands commas; a negative amount (refund) is kept as is."""
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        missing = [c for c in ("date", "amount", "kind") if c not in (rd.fieldnames or [])]
        if missing:
            raise ArchiveError(f"{path} is not an invoice list (missing columns: {', '.join(missing)})")
        rows = []
        for n, rec in enumerate(rd, start=2):
            try:
                day = datetime.strptime((rec["date"] or "").strip(), "%Y-%m-%d").strftime("%Y-%m-%d")
                amount = float((rec["amount"] or "").strip().replace("$", "").replace(",", ""))
            except (TypeError, ValueError) as ex:
                raise ArchiveError(f"{path} line {n}: {ex}")
            kind = (rec["kind"] or "").strip().lower()
            if kind not in INVOICE_KINDS:
                raise ArchiveError(f"{path} line {n}: kind '{rec['kind']}' is not one of {', '.join(INVOICE_KINDS)}")
            rows.append({"date": day, "amount": amount, "kind": kind,
                         "invoice_id": (rec.get("invoice_id") or "").strip(), "notes": (rec.get("notes") or "").strip()})
    return sorted(rows, key=lambda r: r["date"])


def prev_day(day):
    return (datetime.strptime(day, "%Y-%m-%d") - timedelta(days=1)).strftime("%Y-%m-%d")


def billing_cycles(invoices, until):
    """[(start, end)]: each plan charge starts a cycle that runs to the day before the next plan charge; the last
    one runs to `until`. Charges before the first plan charge form a leading cycle."""
    starts = sorted({r["date"] for r in invoices if r["kind"] == "plan"})
    if invoices and (not starts or invoices[0]["date"] < starts[0]):
        starts.insert(0, invoices[0]["date"])
    return [(s, prev_day(starts[i + 1]) if i + 1 < len(starts) else max(until, s)) for i, s in enumerate(starts)]


def calendar_months(first, until):
    """[(YYYY-MM, start, end)] for every month from `first`'s to `until`'s, the last one cut at `until`."""
    out, y, m = [], int(first[:4]), int(first[5:7])
    while f"{y:04d}-{m:02d}" <= until[:7]:
        nxt = (y + (m == 12), m % 12 + 1)
        end = prev_day(f"{nxt[0]:04d}-{nxt[1]:02d}-01")
        out.append((f"{y:04d}-{m:02d}", f"{y:04d}-{m:02d}-01", min(end, until)))
        y, m = nxt
    return out


def coverage(start, end, archive_first):
    """How much of a period the archive covers: full (it starts on or after the first archived day), partial, none."""
    if archive_first is None or end < archive_first:
        return "none"
    return "full" if start >= archive_first else "partial"


def period_rows(periods, invoices, est_by_day, archive_first):
    """One row per (label, start, end): invoiced plan / credits / other, actual, estimate, ratio, coverage."""
    out = []
    for label, s, e in periods:
        inv = [r for r in invoices if s <= r["date"] <= e]
        row = {"period": label, "start": s, "end": e, "charges": len(inv)}
        row.update({k: round(sum(r["amount"] for r in inv if r["kind"] == k), 2) for k in INVOICE_KINDS})
        row["actual"] = round(sum(r["amount"] for r in inv), 2)
        row["est"] = round(sum(v for d, v in est_by_day.items() if s <= d <= e), 2)
        row["ratio"] = round(row["actual"] / row["est"], 4) if row["est"] else None
        row["coverage"] = coverage(s, e, archive_first)
        out.append(row)
    return out


def allocate(invoices, arows, cycles):
    """Split each billing cycle's invoiced total across projects by their share of that cycle's estimate.

    Returns ({project: {"est", "allocated"}}, unallocated $). A cycle with no estimate (before the archive) stays
    unallocated. A partly covered cycle is split whole, by the share of the days the archive has."""
    est = [defaultdict(float) for _ in cycles]
    for r in arows:
        for i, (s, e) in enumerate(cycles):
            if s <= r["day"] <= e:
                est[i][r["project"]] += flat_cost(r)
                break
    out, unallocated = {}, 0.0
    for i, (s, e) in enumerate(cycles):
        actual = sum(r["amount"] for r in invoices if s <= r["date"] <= e)
        total = sum(est[i].values())
        if total <= 0:
            unallocated += actual
            continue
        for p, v in est[i].items():
            if v <= 0:
                continue
            t = out.setdefault(p, {"est": 0.0, "allocated": 0.0})
            t["est"] += v
            t["allocated"] += actual * v / total
    return ({p: {k: round(v, 2) for k, v in t.items()} for p, t in out.items()}, round(unallocated, 2))


def actuals_report(invoices, arows):
    """Everything --actuals prints, as one dict. Pure function of the invoice list and the archive rows."""
    est_by_day = defaultdict(float)
    for r in arows:
        est_by_day[r["day"]] += flat_cost(r)
    days = sorted(d for d, v in est_by_day.items() if v > 0)
    archive_first = days[0] if days else None
    if not invoices:
        return {"archive_first": archive_first, "until": days[-1] if days else None, "months": [], "cycles": [],
                "years": [], "rate": None, "projects": {}, "unallocated": 0.0}
    until = max([invoices[-1]["date"]] + days[-1:])
    cycles = billing_cycles(invoices, until)
    months = period_rows(calendar_months(invoices[0]["date"], until), invoices, est_by_day, archive_first)
    ytd = {}
    for m in months:
        ytd[m["period"][:4]] = ytd.get(m["period"][:4], 0.0) + m["actual"]
        m["ytd"] = round(ytd[m["period"][:4]], 2)
        m["ratio"] = None        # credits bought late in one month pay for the next: only a billing cycle has a ratio
    years = period_rows([(y, f"{y}-01-01", min(f"{y}-12-31", until)) for y in sorted(ytd)],
                        invoices, est_by_day, archive_first)
    crow = period_rows([(f"{s}..{e}", s, e) for s, e in cycles], invoices, est_by_day, archive_first)
    full = [c for c in crow if c["coverage"] == "full"]
    rate = None
    if full and sum(c["est"] for c in full):
        a, x = sum(c["actual"] for c in full), sum(c["est"] for c in full)
        rate = {"since": full[0]["start"], "actual": round(a, 2), "est": round(x, 2), "ratio": round(a / x, 4)}
    projects, unallocated = allocate(invoices, arows, cycles)
    return {"archive_first": archive_first, "until": until, "months": months, "cycles": crow, "years": years,
            "rate": rate, "projects": projects, "unallocated": unallocated}


def _est(r):
    return f"{'-':>9}" if r["coverage"] == "none" else f"{r['est']:>9,.0f}"


def _ratio(r):
    if r["ratio"] is None:
        return f"{'-':>7}"
    return f"{r['ratio']:>6.2f}" + ("*" if r["coverage"] == "partial" else " ")


def actuals_text(rep, projects_filter, inv_path, arch_path, tzl):
    L = [f"AI spend: invoiced actuals vs list-price estimate (invoices {inv_path}; archive {arch_path}, "
         f"from {rep['archive_first'] or '-'}, days in {tzl})"]
    if not rep["months"]:
        return L[0] + "\nno invoices yet: add the billing page's charges to the invoice list (see scripts/ai-cost/README.md)\n"
    head = f"{'plan':>8}{'credits':>9}{'other':>7}{'actual':>9}{'est $':>9}"
    L += ["", f"{'By month':<10}{head}{'year to date':>14}"]
    for m in rep["months"]:
        L.append(f"{m['period']:<10}{m['plan']:>8,.0f}{m['credits']:>9,.0f}{m['other']:>7,.0f}{m['actual']:>9,.0f}"
                 f"{_est(m)}{m['ytd']:>14,.0f}")
    for y in rep["years"]:
        L.append(f"{y['period'] + ' total':<10}{y['plan']:>8,.0f}{y['credits']:>9,.0f}{y['other']:>7,.0f}"
                 f"{y['actual']:>9,.0f}{y['est']:>9,.0f}")
    L.append("Months have no ratio: credits bought late in one month pay for the next month's usage.")
    L += ["", f"{'By billing cycle':<24}{head}{'ratio':>7}{'charges':>9}"]
    for c in rep["cycles"]:
        L.append(f"{c['period']:<24}{c['plan']:>8,.0f}{c['credits']:>9,.0f}{c['other']:>7,.0f}{c['actual']:>9,.0f}"
                 f"{_est(c)}{_ratio(c)}{c['charges']:>9}")
    L.append("* the archive covers only part of the period, so the ratio is overstated; - no estimate (before the archive)")
    L.append("")
    if rep["rate"]:
        r = rep["rate"]
        L.append(f"Effective rate, fully covered cycles since {r['since']}: ${r['actual']:,.0f} invoiced / "
                 f"${r['est']:,.0f} estimated = {r['ratio']:.2f} (each estimated dollar cost {100 * r['ratio']:.0f} cents)")
    else:
        L.append("Effective rate: no billing cycle is fully covered by the archive yet")
    rows = sorted(rep["projects"].items(), key=lambda kv: -kv[1]["allocated"])
    if projects_filter:
        rows = [kv for kv in rows if any(p.lower() in kv[0].lower() for p in projects_filter)]
    alloc_total = sum(t["allocated"] for t in rep["projects"].values())
    L += ["", f"{'Allocated actual by project':<44}{'est $':>9}{'allocated $':>13}{'share':>7}"]
    for p, t in rows:
        L.append(f"{p[:43]:<44}{t['est']:>9,.0f}{t['allocated']:>13,.0f}{100 * t['allocated'] / (alloc_total or 1e-9):>6.0f}%")
    L.append(f"{'unallocated (cycles with no estimate)':<44}{'':>9}{rep['unallocated']:>13,.0f}")
    L.append("Each cycle's invoiced total is split by the projects' share of that cycle's estimate.")
    return "\n".join(L) + "\n"


ACTUALS_CSV_COLUMNS = ["section", "period", "start", "end", "plan", "credits", "other", "actual", "est", "ratio",
                       "coverage", "charges", "ytd"]


def actuals_csv(rep, projects_filter):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=ACTUALS_CSV_COLUMNS, extrasaction="ignore")
    w.writeheader()
    for section in ("months", "cycles", "years"):
        for r in rep[section]:
            w.writerow(dict(r, section=section[:-1]))
    for p, t in sorted(rep["projects"].items(), key=lambda kv: -kv[1]["allocated"]):
        if not projects_filter or any(x.lower() in p.lower() for x in projects_filter):
            w.writerow({"section": "project", "period": p, "actual": t["allocated"], "est": t["est"]})
    w.writerow({"section": "unallocated", "actual": rep["unallocated"]})
    return buf.getvalue()


# --- model fit (--model-fit) --------------------------------------------------------------
# Was each piece of work run on the right model? Claude Code keeps every sub-agent's transcript in
# <project>/<session>/subagents/agent-<id>.jsonl with a sibling agent-<id>.meta.json that carries the
# launch description, agentType, toolUseId and the requested model. The PreToolUse hook
# scripts/quality/require-agent-model.py logs each allowed launch (agent-launches.csv); a launch is matched to
# its transcript, the work is classified, and cost/size/rework are tabulated per work type x model.

DEFAULT_LAUNCH_LOG = os.path.join(os.path.expanduser("~"), ".claude", "spend-archive", "agent-launches.csv")
DESC_MAX = 120            # the hook truncates descriptions to this
MATCH_SLACK_S = 5         # a transcript may start a moment before the hook logged the launch
MATCH_WINDOW_S = 120      # nearest-start-time fallback: transcript must start this soon after the launch
MIN_N = 3                 # fewest rework-known launches a row needs before a flag may compare it
UNDER_MARGIN = 0.25       # "well above the type's average" = this many points (0.25 = 25 pp)
TOP_TIER = ("opus", "fable", "mythos")

# Work categories: the first keyword match in the description wins. Specific agent types keep the
# category they imply; only `implementer` is refined (a deploy or test run is not implementation).
CATEGORY_KEYWORDS = [
    ("deploy", re.compile(r"\bdeploy\w*", re.I)),
    ("review", re.compile(r"\b(review\w*|verif\w*)", re.I)),
    ("research", re.compile(r"\b(research\w*|investigat\w*)", re.I)),
    ("map", re.compile(r"\b(map|mapping|find|search\w*|inventor\w*)\b", re.I)),
    ("implement", re.compile(r"\b(implement\w*|fix\w*|add)\b", re.I)),
    ("test", re.compile(r"\btest\w*", re.I)),
]
TYPE_CATEGORY = {"code-mapper": "map", "explore": "map", "adversarial-reviewer": "review",
                 "implementer": "implement", "researcher": "research", "plan": "plan",
                 "claude-code-guide": "lookup"}

REWORK_LINE = re.compile(r"^[\s>*\-`#]*\**rework\**:\s*\**\s*(none|fix-round-(\d+)|escalated-to-([A-Za-z0-9._-]+))"
                         r"(?![A-Za-z0-9_-])(?!\s*\|)(.*)$", re.I)
TASK_REF = re.compile(r"\btask[\s#_-]*(\d{1,4})\b", re.I)
REWORK_RANK = {"none": 0, "fix": 1, "escalated": 2}


def model_family(model):
    m = (model or "").lower()
    for fam in ("opus", "fable", "mythos", "sonnet", "haiku"):
        if fam in m:
            return fam
    return "other"


def work_type(kind, description):
    """(subagent type, category) for a launch. A blank type is the general-purpose built-in."""
    k = (kind or "").strip().lower() or "general-purpose"
    cat = next((name for name, rx in CATEGORY_KEYWORDS if rx.search(description or "")), None)
    if k in TYPE_CATEGORY and not (k == "implementer" and cat in ("deploy", "test")):
        cat = TYPE_CATEGORY[k]
    return k, cat or "other"


def iso_epoch(ts):
    dt = datetime.fromisoformat(ts.replace("Z", "+00:00"))
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.timestamp()


def norm_desc(s):
    return " ".join(str(s or "").split())[:DESC_MAX]


def subagent_session(relparts):
    """Session id of a sub-agent transcript: the folder above `subagents/` (<project>/<session>/subagents/...)."""
    if "subagents" in relparts:
        i = relparts.index("subagents")
        if i >= 1:
            return relparts[i - 1]
    return relparts[1] if len(relparts) > 2 else ""


def read_meta(jsonl_path):
    p = jsonl_path[:-len(".jsonl")] + ".meta.json"
    try:
        with open(p, encoding="utf-8") as fh:
            m = json.load(fh)
        return m if isinstance(m, dict) else {}
    except (OSError, ValueError):
        return {}


def collect_model_fit(since_day, until_day, tz=None, root=None):
    """One pass over the transcripts. Returns (units, main_calls, stats).

    units: one dict per sub-agent transcript that started in the window (cost/calls/context, the model it
    mostly used, its meta file). main_calls: {model family: calls} of main-session transcripts in the window."""
    root = root or gpc.CLAUDE_PROJECTS
    cutoff = mtime_cutoff(since_day, tz)
    seen, units, main_calls, nfiles = set(), [], defaultdict(int), 0
    for f in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
        try:
            if os.path.getmtime(f) < cutoff:
                continue
        except OSError:
            continue
        nfiles += 1
        rel = os.path.relpath(f, root).split(os.sep)
        sub = is_subagent_file(f)
        u_ = {"calls": 0, "cost": 0.0, "ctx": 0, "start": None, "fam": defaultdict(int)} if sub else None
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
                try:
                    ep = iso_epoch(ts)
                    day = day_of(ts, tz)
                except (ValueError, OverflowError, OSError):
                    continue
                fam = model_family(m.get("model", "") or "unknown")
                if sub:
                    fg = call_figures(u, m.get("model", "") or "unknown")
                    u_["calls"] += 1
                    u_["cost"] += fg["cost_cache_read"] + fg["cost_cache_write"] + fg["cost_input"] + fg["cost_output"]
                    u_["ctx"] += fg["ctx"]
                    u_["fam"][fam] += 1
                    if u_["start"] is None or ep < u_["start"]:
                        u_["start"] = ep
                        u_["start_day"] = day
                elif since_day <= day <= until_day:
                    main_calls[fam] += 1
        if sub and u_["calls"] and since_day <= u_["start_day"] <= until_day:
            fam = max(sorted(u_["fam"]), key=lambda x: u_["fam"][x])      # most-used family; ties by name
            units.append({"file": f, "folder": rel[0], "project": project_name(rel[0]),
                          "session": subagent_session(rel), "agent_id": os.path.basename(f)[:-len(".jsonl")],
                          "meta": read_meta(f), "start": u_["start"], "calls": u_["calls"], "cost": u_["cost"],
                          "ctx": u_["ctx"], "family": fam, "launch": None})
    return units, dict(main_calls), {"files": nfiles}


def read_launches(path, since_day, until_day, tz=None):
    """(launches in the window, total data lines read, malformed lines skipped). Missing file = ([], 0, 0)."""
    out, total, bad = [], 0, 0
    try:
        fh = open(path, encoding="utf-8", newline="")
    except OSError:
        return out, 0, 0
    with fh:
        for rec in csv.DictReader(fh):
            total += 1
            try:
                ep = iso_epoch(rec["ts_utc"])
                day = day_of(rec["ts_utc"], tz)
            except (KeyError, TypeError, ValueError, AttributeError):
                bad += 1
                continue
            if not (since_day <= day <= until_day):
                continue
            session = (rec.get("session_id") or "").strip()
            if not session and rec.get("transcript_path"):
                session = os.path.splitext(os.path.basename(rec["transcript_path"]))[0]
            out.append({"ts": ep, "session": session, "project": rec.get("project") or "",
                        "tool": rec.get("tool") or "", "kind": (rec.get("subagent_type") or "").strip(),
                        "model": rec.get("model") or "", "effort": rec.get("effort") or "",
                        "description": norm_desc(rec.get("description")),
                        "tool_use_id": (rec.get("tool_use_id") or "").strip(), "unit": None})
    out.sort(key=lambda l: l["ts"])
    return out, total, bad


def match_launches(launches, units):
    """Pair each Agent/Task launch with its sub-agent transcript (sets launch["unit"] and unit["launch"]).

    1. tool_use_id equal to the meta file's toolUseId (exact).
    2. Same session, same description (and the same agent type when both are known); of several, the
       transcript that started first at/after the launch.
    3. Same session, transcript with no meta description: the nearest start within MATCH_WINDOW_S after the launch.
    A transcript is used once. Returns the number of pairs. Workflow launches are not matched: their
    agents appear as transcripts of their own."""
    cand = [l for l in launches if l["tool"] != "Workflow"]

    def link(l, u):
        l["unit"], u["launch"] = u, l

    def first_after(l, pool):
        ok = [u for u in pool if u["launch"] is None and u["start"] >= l["ts"] - MATCH_SLACK_S]
        return min(ok, key=lambda u: u["start"]) if ok else None

    by_tool = {u["meta"].get("toolUseId"): u for u in units if u["meta"].get("toolUseId")}
    for l in cand:
        u = by_tool.get(l["tool_use_id"]) if l["tool_use_id"] else None
        if u is not None and u["launch"] is None:
            link(l, u)

    def kind_of(s):
        return (s or "").strip().lower() or "general-purpose"

    for l in cand:
        if l["unit"] is not None:
            continue
        pool = [u for u in units if u["session"] == l["session"]
                and norm_desc(u["meta"].get("description")) == l["description"]
                and (not u["meta"].get("agentType") or kind_of(u["meta"]["agentType"]) == kind_of(l["kind"]))]
        u = first_after(l, pool)
        if u is not None:
            link(l, u)

    for l in cand:
        if l["unit"] is not None:
            continue
        pool = [u for u in units if u["session"] == l["session"] and not u["meta"].get("description")
                and u["start"] <= l["ts"] + MATCH_WINDOW_S]
        u = first_after(l, pool)
        if u is not None:
            link(l, u)
    return sum(1 for l in cand if l["unit"] is not None)


def parse_rework(repo_root):
    """{(project slug lower-case, task number): (class, detail)} from `rework:` lines in task notes.

    Files read: projects/*/notes/*.md and projects/*/tasks/*.poml (+ .md). Format per line:
    `rework: none | fix-round-<N> | escalated-to-<model>`, optionally followed by `task <NNN>`; without that the
    task number comes from the file name (`task-042-x.md`, `042-x.poml`). The worst value per task wins."""
    out = {}
    pats = [os.path.join(repo_root, "projects", "*", "notes", "*.md"),
            os.path.join(repo_root, "projects", "*", "tasks", "*.poml"),
            os.path.join(repo_root, "projects", "*", "tasks", "*.md")]
    for pat in pats:
        for f in sorted(glob.glob(pat)):
            try:
                with open(f, encoding="utf-8", errors="ignore") as fh:
                    text = fh.read()
            except OSError:
                continue
            if "rework" not in text.lower():
                continue
            slug = os.path.basename(os.path.dirname(os.path.dirname(f))).lower()
            name = os.path.basename(f)
            fm = TASK_REF.search(name) or re.match(r"(\d{1,4})-", name)       # task-042-x.md | 042-x.poml
            file_task = int(fm.group(1)) if fm else None
            for line in text.splitlines():
                m = REWORK_LINE.match(line)
                if not m:
                    continue
                tm = TASK_REF.search(m.group(4))
                tid = int(tm.group(1)) if tm else file_task
                if tid is None:
                    continue
                if m.group(1).lower() == "none":
                    val = ("none", "")
                elif m.group(2):
                    val = ("fix", m.group(2))
                else:
                    val = ("escalated", m.group(3))
                old = out.get((slug, tid))
                if old is None or REWORK_RANK[val[0]] >= REWORK_RANK[old[0]]:
                    out[(slug, tid)] = val
    return out


def unit_rework(unit, rework):
    """'none' | 'fix' | 'escalated' | 'unknown' for a transcript: the task id comes from the launch description
    (else the meta description) and the project from the transcript folder; no id or no record = unknown."""
    desc = unit["launch"]["description"] if unit["launch"] else norm_desc(unit["meta"].get("description"))
    m = TASK_REF.search(desc or "")
    if not m:
        return "unknown"
    for slug in gpc.slug_candidates(unit["project"]):
        v = rework.get((slug, int(m.group(1))))
        if v:
            return v[0]
    return "unknown"


def unit_work(unit):
    if unit["launch"]:
        return work_type(unit["launch"]["kind"], unit["launch"]["description"])
    return work_type(unit["meta"].get("agentType"), norm_desc(unit["meta"].get("description")))


def build_fit_rows(units, rework):
    g = {}
    for u in units:
        kind, cat = unit_work(u)
        r = g.setdefault((kind, cat, u["family"]), {
            "work_type": f"{kind} / {cat}", "subagent_type": kind, "category": cat, "model": u["family"],
            "launches": 0, "logged": 0, "cost_total": 0.0, "calls": 0, "ctx": 0,
            "rework_known": 0, "rework_fix": 0, "rework_escalated": 0, "flag": ""})
        r["launches"] += 1
        r["logged"] += u["launch"] is not None
        r["cost_total"] += u["cost"]
        r["calls"] += u["calls"]
        r["ctx"] += u["ctx"]
        rw = unit_rework(u, rework)
        if rw != "unknown":
            r["rework_known"] += 1
            r["rework_fix"] += rw == "fix"
            r["rework_escalated"] += rw == "escalated"
    rows = list(g.values())
    for r in rows:
        r["cost_per_launch"] = r["cost_total"] / r["launches"]
        r["avg_calls"] = r["calls"] / r["launches"]
        r["avg_ctx"] = r["ctx"] / r["calls"] if r["calls"] else 0
        r["rework_rate"] = ((r["rework_fix"] + r["rework_escalated"]) / r["rework_known"]) if r["rework_known"] else None
    flag_rows(rows)
    by_wt = defaultdict(float)
    for r in rows:
        by_wt[r["work_type"]] += r["cost_total"]
    rows.sort(key=lambda r: (-by_wt[r["work_type"]], r["work_type"], -r["cost_total"], r["model"]))
    return rows


def pct(x):
    return f"{100 * x:.0f}%"


def flag_rows(rows):
    """Sets row["flag"] to 'OVER: ...' or 'UNDER: ...' (or ""), judged within each work type."""
    by_wt = defaultdict(list)
    for r in rows:
        by_wt[r["work_type"]].append(r)
    for rs in by_wt.values():
        known = sum(r["rework_known"] for r in rs)
        avg = sum(r["rework_fix"] + r["rework_escalated"] for r in rs) / known if known else None
        sonnet = next((r for r in rs if r["model"] == "sonnet"), None)
        for r in rs:
            why = []
            if r["model"] in TOP_TIER:
                if r["category"] in ("map", "deploy"):
                    why.append(f"{r['model']} on {r['category']} work")
                if (sonnet and sonnet["rework_known"] >= MIN_N and r["rework_known"] >= MIN_N
                        and sonnet["rework_rate"] <= r["rework_rate"]):
                    why.append(f"sonnet rework {pct(sonnet['rework_rate'])} <= {pct(r['rework_rate'])}")
                if why:
                    r["flag"] = "OVER: " + "; ".join(why)
            elif r["model"] == "sonnet":
                if r["rework_escalated"]:
                    why.append(f"{r['rework_escalated']} escalated")
                if r["rework_known"] >= MIN_N and avg is not None and r["rework_rate"] - avg >= UNDER_MARGIN:
                    why.append(f"rework {pct(r['rework_rate'])} vs type avg {pct(avg)}")
                if why:
                    r["flag"] = "UNDER: " + "; ".join(why)


def fit_summary(launches, units, bad_lines, total_lines, log_exists, log_path, main_calls):
    cand = [l for l in launches if l["tool"] != "Workflow"]
    sub_calls = defaultdict(int)
    for u in units:
        sub_calls[u["family"]] += u["calls"]
    return {"launch_log": log_path, "launch_log_exists": log_exists, "log_lines": total_lines,
            "log_lines_malformed": bad_lines, "launches_in_window": len(launches),
            "workflow_launches": len(launches) - len(cand), "agent_launches": len(cand),
            "transcripts": len(units), "matched": sum(1 for l in cand if l["unit"] is not None),
            "unmatched_launches": sum(1 for l in cand if l["unit"] is None),
            "unmatched_transcripts": sum(1 for u in units if u["launch"] is None),
            "main_calls": dict(main_calls), "subagent_calls": dict(sub_calls)}


def share_text(calls):
    tot = sum(calls.values())
    if not tot:
        return "none"
    return ", ".join(f"{k} {pct(v / tot)} ({v:,})" for k, v in sorted(calls.items(), key=lambda kv: -kv[1]))


def rework_text(r):
    if not r["rework_known"]:
        return "unknown"
    return f"{r['rework_fix'] + r['rework_escalated']}/{r['rework_known']} {pct(r['rework_rate'])}"


def fit_text(rows, summ, since, until, tzl):
    L = [f"Model fit {since}..{until}, days in {tzl}",
         "",
         f"Main-session calls by model (policy adherence): {share_text(summ['main_calls'])}",
         f"Sub-agent calls by model:                       {share_text(summ['subagent_calls'])}",
         "",
         f"Agent launches logged {summ['agent_launches']}  matched to a transcript {summ['matched']}  "
         f"unmatched launches {summ['unmatched_launches']}  (+ {summ['workflow_launches']} workflow launches; their agents are transcripts)",
         f"Sub-agent transcripts {summ['transcripts']}  with a launch record {summ['matched']}  "
         f"unmatched transcripts (no launch record) {summ['unmatched_transcripts']}  -- still counted in the table below",
         ("Launch log: " + summ["launch_log"] + ("" if summ["launch_log_exists"] else "  (not found: every transcript is unmatched)")
          + (f"  [{summ['log_lines_malformed']} malformed lines skipped]" if summ["log_lines_malformed"] else "")),
         "", f"{'work type / model':<46}{'n':>5}{'logged':>7}{'est $':>9}{'$/launch':>9}{'calls':>7}{'ctx':>7}{'rework':>13}  flag"]
    last = None
    for r in rows:
        if r["work_type"] != last:
            L.append(r["work_type"])
            last = r["work_type"]
        L.append(f"  {r['model']:<44}{r['launches']:>5}{r['logged']:>7}{r['cost_total']:>9,.0f}{r['cost_per_launch']:>9,.1f}"
                 f"{r['avg_calls']:>7.0f}{r['avg_ctx'] / 1000:>6.0f}k{rework_text(r):>13}  {r['flag']}")
    if not rows:
        L.append("(no sub-agent transcripts in this window)")
    L += ["", "n = sub-agent transcripts; logged = those matched to a launch record; calls = average per launch; ctx = average per call;",
          "rework = (fix rounds + escalations) / launches with a `rework:` line in task notes; unknown = no task id or no line."]
    return "\n".join(L) + "\n"


FIT_CSV_COLUMNS = ["section", "work_type", "subagent_type", "category", "model", "launches", "logged", "cost_total",
                   "cost_per_launch", "avg_calls", "avg_ctx", "rework_known", "rework_fix", "rework_escalated",
                   "rework_rate", "flag", "tz"]


def fit_csv(rows, summ, tz_name):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=FIT_CSV_COLUMNS)
    w.writeheader()
    for r in rows:
        d = {k: r.get(k, "") for k in FIT_CSV_COLUMNS}
        for k in ("cost_total", "cost_per_launch", "avg_calls", "avg_ctx"):
            d[k] = round(r[k], 4)
        d["rework_rate"] = "" if r["rework_rate"] is None else round(r["rework_rate"], 4)
        d.update(section="work", tz=tz_name)
        w.writerow(d)
    for sec, key in (("main_calls", "main_calls"), ("subagent_calls", "subagent_calls")):
        for fam, n in sorted(summ[key].items()):
            w.writerow({"section": sec, "model": fam, "launches": n, "tz": tz_name})
    for label in ("agent_launches", "matched", "unmatched_launches", "workflow_launches", "transcripts",
                  "unmatched_transcripts"):
        w.writerow({"section": "match", "work_type": label, "launches": summ[label], "tz": tz_name})
    return buf.getvalue()


FIT_HTML = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Spaarke Model Fit</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--fg:#1c2330;--mute:#667085;--line:#e3e6eb;--over:#b54708;--under:#b42318}
@media (prefers-color-scheme:dark){:root{--bg:#12151b;--card:#1b2029;--fg:#e6e9ef;--mute:#9aa3b2;--line:#2a313d;--over:#f0a550;--under:#f06b8b}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
.wrap{max-width:1200px;margin:0 auto;padding:16px}
h1{font-size:20px;margin:0}.sub{color:var(--mute);font-size:12px;margin:2px 0 12px}
.panel{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px;margin-bottom:12px;min-width:0}
.panel h2{font-size:14px;margin:0 0 8px}.tw{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{padding:6px 8px;border-bottom:1px solid var(--line);text-align:right;white-space:nowrap}
th:first-child,td:first-child,td.f,th.f{text-align:left}
th{color:var(--mute);font-weight:600}
tr.wt td{font-weight:650;background:color-mix(in srgb,var(--line) 40%,transparent)}
.over{color:var(--over)}.under{color:var(--under)}
p{margin:4px 0}
</style></head><body><div class="wrap">
<h1>Spaarke Model Fit</h1><div class="sub">__RANGE__</div>
<div class="panel"><h2>Policy adherence and matching</h2>__SUMMARY__</div>
<div class="panel"><h2>Work type by model</h2><div class="tw"><table><thead><tr>
<th>Work type / model</th><th>n</th><th>Logged</th><th>Est. $</th><th>$/launch</th><th>Avg calls</th><th>Avg ctx</th><th>Rework</th><th class="f">Flag</th></tr></thead>
<tbody>__ROWS__</tbody></table></div></div>
<div class="sub">n = sub-agent transcripts; logged = matched to a launch record; rework = (fix rounds + escalations) / launches with a
<code>rework:</code> line in task notes (unknown = no task id or no line). Estimated at list price from local transcripts; not an invoice.</div>
</div></body></html>
"""


def fit_html(rows, summ, since, until, tzl):
    e = html.escape
    body, last = [], None
    for r in rows:
        if r["work_type"] != last:
            body.append(f'<tr class="wt"><td colspan="9">{e(r["work_type"])}</td></tr>')
            last = r["work_type"]
        cls = "over" if r["flag"].startswith("OVER") else "under" if r["flag"].startswith("UNDER") else ""
        body.append(f'<tr><td>&nbsp;&nbsp;{e(r["model"])}</td><td>{r["launches"]}</td><td>{r["logged"]}</td>'
                    f'<td>${r["cost_total"]:,.0f}</td><td>${r["cost_per_launch"]:,.1f}</td><td>{r["avg_calls"]:.0f}</td>'
                    f'<td>{r["avg_ctx"] / 1000:.0f}k</td><td>{e(rework_text(r))}</td><td class="f {cls}">{e(r["flag"])}</td></tr>')
    if not rows:
        body.append('<tr><td colspan="9">No sub-agent transcripts in this window.</td></tr>')
    s = [f'<p>Main-session calls by model: {e(share_text(summ["main_calls"]))}</p>',
         f'<p>Sub-agent calls by model: {e(share_text(summ["subagent_calls"]))}</p>',
         f'<p>Agent launches logged {summ["agent_launches"]}; matched {summ["matched"]}; unmatched launches '
         f'{summ["unmatched_launches"]} (+ {summ["workflow_launches"]} workflow launches).</p>',
         f'<p>Sub-agent transcripts {summ["transcripts"]}; with a launch record {summ["matched"]}; unmatched '
         f'transcripts {summ["unmatched_transcripts"]} (still in the table).</p>',
         f'<p>Launch log: {e(summ["launch_log"])}{"" if summ["launch_log_exists"] else " (not found)"}</p>']
    out = FIT_HTML.replace("__RANGE__", e(f"{since} to {until} (days in {tzl})")).replace("__SUMMARY__", "".join(s))
    return out.replace("__ROWS__", "".join(body))


def model_fit_report(since, until, tz, tz_name, projects, repo_root, launch_log, root=None):
    """Everything --model-fit prints: (rows, summary). Pure function of the transcripts, the launch log and the notes."""
    units, main_calls, st = collect_model_fit(since, until, tz, root)
    launches, total, bad = read_launches(launch_log, since, until, tz)
    if projects:
        keep = lambda p: any(x.lower() in p.lower() for x in projects)
        units = [u for u in units if keep(u["project"])]
        launches = [l for l in launches if keep(l["project"])]
    match_launches(launches, units)
    rows = build_fit_rows(units, parse_rework(repo_root))
    summ = fit_summary(launches, units, bad, total, os.path.exists(launch_log), launch_log, main_calls)
    summ["files"] = st["files"]
    return rows, summ


def filter_flat(flatrows, projects):
    if not projects:
        return list(flatrows)
    return [r for r in flatrows if any(p.lower() in r["project"].lower() for p in projects)]


def fail(msg):
    print(f"spend-report: {msg}", file=sys.stderr)
    sys.exit(2)


def emit(out, a, nrows, note, t0):
    """Write the rendered report to --out, or stdout."""
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="") as fh:
            fh.write(out)
        print(f"wrote {a.out} ({nrows} rows, {note}, {time.time()-t0:.1f}s)", file=sys.stderr)
        if a.format == "html" and a.open:
            import webbrowser
            webbrowser.open("file:///" + os.path.abspath(a.out).replace("\\", "/"))
    else:
        # newline="": no "\n" -> "\r\n" translation, so csv (already CRLF) is not doubled to "\r\r\n"
        # on Windows and stdout matches what --out writes.
        sys.stdout.reconfigure(encoding="utf-8", newline="")
        sys.stdout.write(out)
        print(f"({note}, {time.time()-t0:.1f}s)", file=sys.stderr)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--days", type=int, help="last N calendar days incl. today (default 14)")
    ap.add_argument("--since", help="first day, YYYY-MM-DD")
    ap.add_argument("--today", action="store_true",
                    help="the current calendar day only, 00:00 to now (in --tz); not with --days/--since")
    ap.add_argument("--tz", choices=["local", "utc"],
                    help="timezone whose calendar days are used for bucketing (default: local; "
                         "with --source archive, the archive's own tz)")
    ap.add_argument("--project", action="append", default=[])
    ap.add_argument("--format", choices=["text", "json", "csv", "html"], default="text")
    ap.add_argument("--out")
    ap.add_argument("--open", action="store_true")
    ap.add_argument("--archive", nargs="?", const=DEFAULT_ARCHIVE, metavar="PATH",
                    help="upsert this run's days into a durable CSV archive (default path "
                         f"{DEFAULT_ARCHIVE.replace('%', '%%')}); a (day, project) is never replaced by a lower total. "
                         "With --source archive, selects the file to read instead")
    ap.add_argument("--source", choices=["transcripts", "archive"], default="transcripts",
                    help="where rows come from: the local transcripts (default) or the archive, "
                         "which also covers days whose transcripts were deleted")
    ap.add_argument("--model-fit", action="store_true",
                    help="work type x model table: cost, size and rework per kind of work, flagging over- and "
                         "under-provisioned models; uses the agent launch log and task-note `rework:` lines")
    ap.add_argument("--launch-log", metavar="PATH",
                    help=f"--model-fit: the agent launch log (default $SPAARKE_AGENT_LAUNCH_LOG or "
                         f"{DEFAULT_LAUNCH_LOG.replace('%', '%%')})")
    ap.add_argument("--repo", metavar="PATH", default=os.path.dirname(os.path.dirname(HERE)),
                    help="--model-fit: repo root whose projects/*/notes and tasks hold the `rework:` lines (default: this repo)")
    ap.add_argument("--actuals", action="store_true",
                    help="invoiced actuals vs the archived estimate, by month, billing cycle and year, with each "
                         "cycle's invoices allocated to projects; reads the invoice list and the archive (--archive "
                         "PATH picks the archive), covers the whole history")
    ap.add_argument("--invoices", metavar="PATH",
                    help=f"--actuals: the invoice list (default {DEFAULT_INVOICES.replace('%', '%%')})")
    a = ap.parse_args()
    if a.today and (a.days is not None or a.since):
        ap.error("--today cannot be combined with --days or --since")
    if a.model_fit and (a.archive or a.source == "archive"):
        ap.error("--model-fit reads the transcripts; it cannot be combined with --archive or --source archive")
    if a.actuals and (a.model_fit or a.days is not None or a.since or a.today or a.format == "html"):
        ap.error("--actuals covers the whole history from the archive; it takes no --days/--since/--today, "
                 "no --model-fit and no --format html")
    if a.since:
        try:
            datetime.strptime(a.since, "%Y-%m-%d")
        except ValueError:
            ap.error("--since must be YYYY-MM-DD")
    t0 = time.time()
    archive_failed = False
    archive_path = a.archive or DEFAULT_ARCHIVE
    tz_name = a.tz or "local"
    note = ""

    if a.model_fit:
        tz = resolve_tz(tz_name)
        since, until = resolve_window(a.days, a.since, a.today, tz)
        log = a.launch_log or os.environ.get("SPAARKE_AGENT_LAUNCH_LOG") or DEFAULT_LAUNCH_LOG
        rows, summ = model_fit_report(since, until, tz, tz_name, a.project, a.repo, log)
        tzl = tz_label(tz_name)
        if a.format == "text":
            out = fit_text(rows, summ, since, until, tzl)
        elif a.format == "json":
            out = json.dumps({"tz": tz_name, "tz_label": tzl, "since": since, "until": until,
                              "summary": summ, "rows": rows}, indent=1)
        elif a.format == "csv":
            out = fit_csv(rows, summ, tz_name)
        else:
            out = fit_html(rows, summ, since, until, tzl)
            if not a.out:
                a.out = os.path.join(tempfile.gettempdir(), "spaarke-model-fit.html")
        emit(out, a, len(rows), f"{summ['files']} files", t0)
        return

    if a.actuals:
        inv_path = a.invoices or DEFAULT_INVOICES
        try:
            invoices = read_invoices(inv_path)
            arows = read_archive(archive_path)
        except ArchiveError as ex:
            fail(str(ex))
        tz_name = arows[0]["tz"] if arows else tz_name
        rep = actuals_report(invoices, arows)
        if a.format == "text":
            out = actuals_text(rep, a.project, inv_path, archive_path, tz_label(tz_name))
        elif a.format == "json":
            out = json.dumps(dict(rep, tz=tz_name, invoices=inv_path, archive=archive_path), indent=1)
        else:
            out = actuals_csv(rep, a.project)
        emit(out, a, len(invoices), f"{len(invoices)} invoices, {len(arows)} archive rows", t0)
        return

    if a.source == "archive":
        if not os.path.exists(archive_path):
            fail(f"no archive at {archive_path} (create it with --archive)")
        try:
            arows = read_archive(archive_path)
            if not a.tz and arows:
                tz_name = arows[0]["tz"]
            check_archive_tz(arows, tz_name, archive_path)
        except ArchiveError as ex:
            fail(str(ex))
        tz = resolve_tz(tz_name)
        if a.days is None and not a.since and not a.today:      # whole history
            days_present = sorted({r["day"] for r in arows})
            since = days_present[0] if days_present else today_in(tz).isoformat()
            until = days_present[-1] if days_present else since
        else:
            since, until = resolve_window(a.days, a.since, a.today, tz)
        fr = filter_flat([r for r in arows if since <= r["day"] <= until], a.project)
        note = f"archive {archive_path}"
    else:
        tz = resolve_tz(tz_name)
        since, until = resolve_window(a.days, a.since, a.today, tz)
        arows = []
        if a.archive:                       # fail before the (slow) scan if the archive is unusable
            try:
                arows = read_archive(archive_path)
                check_archive_tz(arows, tz_name, archive_path)
            except ArchiveError as ex:
                fail(str(ex))
        rows, st = collect(since, tz)
        full = flat(rows, [], tz_name)      # the archive always gets every project, whatever --project says
        fr = filter_flat(full, a.project)
        note = f"{st['files']} files"
        if a.archive:
            merged, kept = merge_archive(arows, full, day_range(since, until))
            try:
                write_archive(archive_path, merged)
            except OSError as ex:           # the report is still worth printing; the exit code says it failed
                print(f"spend-report: archive not written: {ex}", file=sys.stderr)
                archive_failed = True
            for day, project, new_t, old_t in kept:
                print(f"WARN archive: {day} {project}: fresh ${new_t:,.2f} < archived ${old_t:,.2f}; kept the "
                      f"archived rows (transcripts were probably deleted)", file=sys.stderr)
            if not archive_failed:
                print(f"archive {archive_path}: {len(merged)} rows, {len(day_range(since, until))} days scanned, "
                      f"{len(kept)} (day, project) groups kept at the archived total", file=sys.stderr)

    tzl = tz_label(tz_name)
    if a.format == "text":
        out = text_report(fr, since, until, tzl)
    elif a.format == "json":
        out = json.dumps({"tz": tz_name, "tz_label": tzl, "source": a.source,
                          "since": since, "until": until, "rows": fr}, indent=1)
    elif a.format == "csv":
        out = csv_report(fr)
    else:
        out = html_report(fr, since, until, tz_name, tzl)
        if not a.out:
            a.out = os.path.join(tempfile.gettempdir(), "spaarke-ai-spend.html")
    emit(out, a, len(fr), note, t0)
    if archive_failed:
        sys.exit(2)


if __name__ == "__main__":
    main()
