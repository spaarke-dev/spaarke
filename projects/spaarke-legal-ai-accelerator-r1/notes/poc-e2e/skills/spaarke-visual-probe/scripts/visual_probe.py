"""visual_probe.py - T-P13: write one sample of every visual format, and report what the sandbox has.

Throwaway POC probe (runbook Act 3 step 1; solution-packs note §5.6 items 1-3, 7, 8). Standard library
only for the core samples; optional libraries are used when present so their output can be compared.

Usage: python visual_probe.py OUT_DIR
Writes into OUT_DIR:
  risk.png                 bar chart drawn with the standard library (zlib PNG)
  risk-matplotlib.png      same chart via matplotlib (only if present)
  risk.svg                 same chart as SVG
  dashboard-static.html    HTML + CSS + inline SVG, no script
  dashboard-script.html    inline script (button counter), fetch() to the internet, a CDN <script>;
                           the page reports on itself which of these ran
  chart.xlsx               native Excel bar chart (stdlib writer, xlsx_lite.py)
  chart-openpyxl.xlsx      native chart via openpyxl (only if present)
  chart.pptx               native PowerPoint chart via python-pptx (only if present)
  chat-samples.md          Markdown table, Mermaid block, LaTeX - to paste into chat
  probe-report.json        modules, tools and every file written (or why not)
Exit codes: 0 always (a probe reports; it does not fail).
"""
import importlib
import json
import os
import platform
import shutil
import struct
import subprocess
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

DATA = [("RED", 6, (208, 59, 59)), ("YELLOW", 7, (250, 178, 25)), ("GREEN", 11, (12, 163, 12))]
MODULES = ["lxml", "docx", "openpyxl", "pptx", "matplotlib", "pandas", "PIL", "plotly", "pypdf", "pdfplumber", "numpy"]
TOOLS = ["node", "soffice", "libreoffice", "pdftoppm", "pandoc", "chromium", "google-chrome", "tesseract"]


def version(name):
    try:
        m = importlib.import_module(name)
        return str(getattr(m, "__version__", "present"))
    except Exception:
        return None


def png(path, w=480, h=200):
    px = [[(252, 252, 251)] * w for _ in range(h)]
    vmax = max(v for _, v, _ in DATA)
    for i, (_, v, rgb) in enumerate(DATA):
        y0, bw = 30 + i * 55, int((w - 140) * v / vmax)
        for y in range(y0, y0 + 30):
            for x in range(110, 110 + bw):
                px[y][x] = rgb
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in px)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def svg():
    vmax = max(v for _, v, _ in DATA)
    bars = "".join(
        f'<text x="100" y="{52 + i * 55}" text-anchor="end" font-family="Segoe UI,sans-serif" font-size="14">{k}</text>'
        f'<rect x="110" y="{30 + i * 55}" width="{340 * v / vmax:.0f}" height="30" rx="3" fill="rgb{rgb}"/>'
        f'<text x="{116 + 340 * v / vmax:.0f}" y="{52 + i * 55}" font-family="Segoe UI,sans-serif" font-size="14">{v}</text>'
        for i, (k, v, rgb) in enumerate(DATA))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="480" height="200" viewBox="0 0 480 200">'
            f'<rect width="480" height="200" fill="#fcfcfb"/>{bars}</svg>')


STATIC = """<!doctype html><html><head><meta charset="utf-8"><title>T-P13 static dashboard</title>
<style>body{font:14px Segoe UI,sans-serif;margin:24px;background:#fcfcfb}.t{display:inline-block;border:1px solid #ddd;
border-radius:8px;padding:8px 14px;margin-right:8px}.t b{font-size:24px;display:block}</style></head><body>
<h1>T-P13 static dashboard (no script)</h1><div><span class="t"><b>6</b>RED</span><span class="t"><b>7</b>YELLOW</span>
<span class="t"><b>11</b>GREEN</span></div><h2>Topics by rating (inline SVG)</h2>{svg}
<p>If you can read the numbers and see three coloured bars, static HTML + SVG renders here.</p></body></html>"""

SCRIPTED = """<!doctype html><html><head><meta charset="utf-8"><title>T-P13 scripted dashboard</title>
<style>body{font:14px Segoe UI,sans-serif;margin:24px}td{padding:4px 10px;border-bottom:1px solid #ddd}</style>
<script src="https://cdnjs.cloudflare.com/ajax/libs/lodash.js/4.17.21/lodash.min.js"></script></head><body>
<h1>T-P13 scripted dashboard</h1>
<table><tr><td>Inline script ran</td><td id="inline">NO (scripts blocked)</td></tr>
<tr><td>Button click handled</td><td><button id="b">Click me</button> <span id="clicks">0</span></td></tr>
<tr><td>CDN script loaded (lodash)</td><td id="cdn">NO</td></tr>
<tr><td>fetch() to the internet</td><td id="net">not tried</td></tr></table>
<script>
document.getElementById('inline').textContent = 'YES';
var n = 0; document.getElementById('b').onclick = function(){ n++; document.getElementById('clicks').textContent = n; };
document.getElementById('cdn').textContent = (typeof _ !== 'undefined' && _.VERSION) ? 'YES (lodash ' + _.VERSION + ')' : 'NO';
var net = document.getElementById('net'); net.textContent = 'pending...';
fetch('https://httpbin.org/get').then(function(r){ net.textContent = 'YES (HTTP ' + r.status + ')'; })
  .catch(function(e){ net.textContent = 'NO (' + e + ')'; });
</script></body></html>"""

CHAT = """# T-P13 chat samples (paste each block into chat and record what renders)

## Markdown table
| Rating | Topics |
|---|---|
| RED | 6 |
| YELLOW | 7 |
| GREEN | 11 |

## Mermaid
```mermaid
flowchart LR
  A[NDA received] --> B{Playbook check}
  B -->|GREEN| C[Sign]
  B -->|YELLOW| D[Counsel review]
  B -->|RED| E[Escalate]
```

## LaTeX
Inline: $E = mc^2$. Block:
$$\\text{risk} = \\sum_{i=1}^{n} w_i \\cdot s_i$$
"""


def main(argv=None):
    argv = argv if argv is not None else sys.argv[1:]
    out = argv[0] if argv else "t-p13"
    os.makedirs(out, exist_ok=True)
    rep = {"python": platform.python_version(), "platform": platform.platform(),
           "modules": {m: version(m) for m in MODULES}, "tools": {t: shutil.which(t) for t in TOOLS}, "files": {}}
    if rep["tools"]["node"]:
        try:
            rep["tools"]["node_version"] = subprocess.run(["node", "--version"], capture_output=True, text=True, timeout=10).stdout.strip()
        except Exception as ex:
            rep["tools"]["node_version"] = f"error: {ex}"

    def attempt(name, fn):
        try:
            fn(os.path.join(out, name))
            rep["files"][name] = "written"
        except Exception as ex:
            rep["files"][name] = f"not written: {type(ex).__name__}: {ex}"

    attempt("risk.png", png)
    attempt("risk.svg", lambda p: open(p, "w", encoding="utf-8").write(svg()))
    attempt("dashboard-static.html", lambda p: open(p, "w", encoding="utf-8").write(STATIC.replace("{svg}", svg())))
    attempt("dashboard-script.html", lambda p: open(p, "w", encoding="utf-8").write(SCRIPTED))
    attempt("chat-samples.md", lambda p: open(p, "w", encoding="utf-8").write(CHAT))

    def xlsx_stdlib(p):
        import xlsx_lite
        wb = xlsx_lite.Workbook()
        ws = wb.sheet("Summary")
        ws.append(["Rating", "Topics"], style="header")
        for k, v, _ in DATA:
            ws.append([{"v": k, "s": k}, v])
        ws.widths = [14, 10]
        ws.add_chart("Topics by rating", (2, 1, 4, 1), [("Topics", (2, 2, 4, 2), "D03B3B")], bar_dir="bar")
        wb.save(p)
    attempt("chart.xlsx", xlsx_stdlib)

    if rep["modules"]["matplotlib"]:
        def mpl(p):
            import matplotlib
            matplotlib.use("Agg")
            import matplotlib.pyplot as plt
            fig, ax = plt.subplots(figsize=(4.8, 2))
            ax.barh([k for k, _, _ in DATA][::-1], [v for _, v, _ in DATA][::-1],
                    color=["#%02x%02x%02x" % c for _, _, c in DATA][::-1])
            fig.tight_layout()
            fig.savefig(p, dpi=150)
        attempt("risk-matplotlib.png", mpl)
    else:
        rep["files"]["risk-matplotlib.png"] = "skipped: matplotlib not present"

    if rep["modules"]["openpyxl"]:
        def opx(p):
            from openpyxl import Workbook
            from openpyxl.chart import BarChart, Reference
            wb = Workbook()
            ws = wb.active
            ws.append(["Rating", "Topics"])
            for k, v, _ in DATA:
                ws.append([k, v])
            ch = BarChart()
            ch.type, ch.title = "bar", "Topics by rating"
            ch.add_data(Reference(ws, min_col=2, min_row=1, max_row=4), titles_from_data=True)
            ch.set_categories(Reference(ws, min_col=1, min_row=2, max_row=4))
            ws.add_chart(ch, "D2")
            wb.save(p)
        attempt("chart-openpyxl.xlsx", opx)
    else:
        rep["files"]["chart-openpyxl.xlsx"] = "skipped: openpyxl not present"

    if rep["modules"]["pptx"]:
        def ppt(p):
            from pptx import Presentation
            from pptx.chart.data import CategoryChartData
            from pptx.enum.chart import XL_CHART_TYPE
            from pptx.util import Inches
            prs = Presentation()
            slide = prs.slides.add_slide(prs.slide_layouts[5])
            slide.shapes.title.text = "Topics by rating"
            cd = CategoryChartData()
            cd.categories = [k for k, _, _ in DATA]
            cd.add_series("Topics", [v for _, v, _ in DATA])
            slide.shapes.add_chart(XL_CHART_TYPE.BAR_CLUSTERED, Inches(1), Inches(1.6), Inches(8), Inches(4.5), cd)
            prs.save(p)
        attempt("chart.pptx", ppt)
    else:
        rep["files"]["chart.pptx"] = "skipped: python-pptx not present"

    with open(os.path.join(out, "probe-report.json"), "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)
    print(json.dumps(rep, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
