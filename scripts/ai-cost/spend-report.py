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

Durable archive: transcripts are deleted by Claude Code after a retention period, so
`--archive [PATH]` upserts this run's days into a CSV (default
%USERPROFILE%\\.claude\\spend-archive\\spend-daily.csv) and never lets a day's total go down.
`--source archive` reports from that file instead of the transcripts.
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
                try:
                    day = day_of(ts, tz)
                except (ValueError, OverflowError, OSError):
                    continue
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

    For each covered day the fresh rows replace the archived ones, unless the fresh total cost is lower
    than the archived total (transcripts were deleted): then the archived rows stay. Days outside
    `covered_days` are untouched. Returns (rows sorted by day then project, kept) where kept lists
    (day, fresh_total, archived_total) for every day whose archived rows were kept."""
    by_day_old, by_day_new = defaultdict(list), defaultdict(list)
    for r in archived:
        by_day_old[r["day"]].append(r)
    for r in fresh:
        by_day_new[r["day"]].append(r)
    covered = set(covered_days) | set(by_day_new)
    merged, kept = [], []
    for day in set(by_day_old) | covered:
        old, new = by_day_old.get(day, []), by_day_new.get(day, [])
        if day not in covered:
            merged.extend(old)
            continue
        old_t, new_t = sum(map(flat_cost, old)), sum(map(flat_cost, new))
        if old and new_t < old_t - COST_EPS:
            merged.extend(old)
            kept.append((day, new_t, old_t))
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

def filter_flat(flatrows, projects):
    if not projects:
        return list(flatrows)
    return [r for r in flatrows if any(p.lower() in r["project"].lower() for p in projects)]


def fail(msg):
    print(f"spend-report: {msg}", file=sys.stderr)
    sys.exit(2)


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
                         f"{DEFAULT_ARCHIVE.replace('%', '%%')}); a day is never replaced by a lower total. "
                         "With --source archive, selects the file to read instead")
    ap.add_argument("--source", choices=["transcripts", "archive"], default="transcripts",
                    help="where rows come from: the local transcripts (default) or the archive, "
                         "which also covers days whose transcripts were deleted")
    a = ap.parse_args()
    if a.today and (a.days is not None or a.since):
        ap.error("--today cannot be combined with --days or --since")
    if a.since:
        try:
            datetime.strptime(a.since, "%Y-%m-%d")
        except ValueError:
            ap.error("--since must be YYYY-MM-DD")
    t0 = time.time()
    archive_path = a.archive or DEFAULT_ARCHIVE
    tz_name = a.tz or "local"
    note = ""

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
            write_archive(archive_path, merged)
            for day, new_t, old_t in kept:
                print(f"WARN archive: {day}: fresh ${new_t:,.2f} < archived ${old_t:,.2f}; kept the archived rows "
                      f"(transcripts for that day were probably deleted)", file=sys.stderr)
            print(f"archive {archive_path}: {len(merged)} rows, {len(day_range(since, until))} days scanned, "
                  f"{len(kept)} kept at the archived total", file=sys.stderr)

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
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="") as fh:
            fh.write(out)
        print(f"wrote {a.out} ({len(fr)} rows, {note}, {time.time()-t0:.1f}s)", file=sys.stderr)
        if a.format == "html" and a.open:
            import webbrowser
            webbrowser.open("file:///" + os.path.abspath(a.out).replace("\\", "/"))
    else:
        # newline="": no "\n" -> "\r\n" translation, so csv (already CRLF) is not doubled to "\r\r\n"
        # on Windows and stdout matches what --out writes.
        sys.stdout.reconfigure(encoding="utf-8", newline="")
        sys.stdout.write(out)
        print(f"({note}, {time.time()-t0:.1f}s)", file=sys.stderr)


if __name__ == "__main__":
    main()
