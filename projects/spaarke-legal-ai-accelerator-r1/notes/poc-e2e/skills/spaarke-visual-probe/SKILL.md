---
name: spaarke-visual-probe
description: Throwaway Spaarke test (T-P13) that writes one sample of each visual format - PNG, SVG, static and scripted HTML, Excel and PowerPoint charts, Markdown, Mermaid and LaTeX - and reports which Python libraries and tools the sandbox has. Use for "run the Spaarke visual probe" or "T-P13" only. Do NOT use for any legal review, for building real charts or dashboards, or for anything a user asks without naming this probe.
license: Proprietary
metadata:
  category: analysis
  publisher: Spaarke
  version: 0.0.1-probe
---

# Spaarke visual probe (T-P13, throwaway)

A test of what Cowork can display. Not legal content; not legal advice. Do exactly these steps and
report what you see; do not improve or restyle the samples.

1. Run `python scripts/visual_probe.py t-p13` and show the JSON it prints, summarised as a table:
   module or tool, present (version) or absent.
2. Save every file in `t-p13/` to the user's Cowork output folder, under `Test/T-P13/`.
3. In chat, try to show `risk.png` and `risk.svg` inline. Say plainly whether each one displayed in
   the chat itself or only as a file.
4. Paste the three blocks from `chat-samples.md` into chat as they are (the Markdown table, the
   Mermaid block and the LaTeX), so the user can see how chat renders them.
5. List each file with one line on what the user should check when opening it in the preview:
   - `dashboard-static.html`: tiles and three coloured bars visible?
   - `dashboard-script.html`: the page reports for itself whether the inline script ran, the button
     works, the CDN script loaded and the internet fetch succeeded. The fetch and the CDN request
     happen only if the preview runs scripts; the Python script itself makes no network calls.
   - `chart.xlsx`, `chart-openpyxl.xlsx`: is the bar chart drawn in the preview, or only after
     download?
   - `chart.pptx`: is the chart drawn?
   - `risk-matplotlib.png`: present only if matplotlib exists.
6. Stop. Do not run anything else.
