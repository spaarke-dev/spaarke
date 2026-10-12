# scripts/ai-cost

Estimated AI spend from local Claude Code transcripts (`~/.claude/projects/**/*.jsonl`),
priced at list price. Not an invoice; other machines and claude.ai usage are not included.
Pricing and the cost rules (dedup on `(message.id, requestId)`, skip `<synthetic>`,
cache-write 5m = 1.25x and 1h = 2x input, `speed == "fast"` = 2x) live in `get-project-cost.py`.

## get-project-cost.py

All-time cost for one or more project slugs (matches the `...-wt-<slug>` transcript folder).
Prints one JSON line per slug.

```
python scripts/ai-cost/get-project-cost.py unified-access-control-r2
```

## update-board-spend.py

Pushes the per-project totals from `get-project-cost.py` to the GitHub portfolio board
(AI Spend / AI Calls / AI Spend As Of). Normally driven by the `/project-spend-update` skill.

## spend-report.py

On-demand report and dashboard. One pass over the transcripts, aggregated by
day x project x model x main/sub-agent. Days are calendar days in `--tz`: `local` (the default;
this machine's timezone, DST-aware) or `utc`. Every output states the tz, and csv/json carry it
(csv: last column `tz`; json: top-level `tz` and per row).

```
python scripts/ai-cost/spend-report.py                      # last 14 local days, text
python scripts/ai-cost/spend-report.py --today              # today, local 00:00 to now
python scripts/ai-cost/spend-report.py --days 7
python scripts/ai-cost/spend-report.py --days 7 --tz utc    # UTC days (the old behaviour)
python scripts/ai-cost/spend-report.py --since 2026-09-01 --project unified-access-control
python scripts/ai-cost/spend-report.py --format csv --out spend.csv
python scripts/ai-cost/spend-report.py --format json
python scripts/ai-cost/spend-report.py --format html --open # %TEMP%\spaarke-ai-spend.html
```

Options: `--days N` (default 14), `--since YYYY-MM-DD` or `--today` (the current calendar day in
`--tz`; cannot be combined with `--days`/`--since`); `--tz local|utc`; `--project SUBSTR` (repeatable);
`--format text|json|csv|html`; `--out PATH`; `--open` (html: open in the default browser).

## Tests

```
python -m unittest discover -s scripts/ai-cost/tests
```

The HTML dashboard is a single self-contained file (data embedded as JSON; Chart.js loaded
from cdnjs). It defaults to the temp folder because it contains project names and costs;
do not commit it.
