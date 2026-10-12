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

The board figures never go down. Transcripts are deleted over time, so a fresh figure that is lower
than the one already on the board is not written: the script prints
`KEEP <slug> (fresh < board; transcripts likely pruned)`. AI Spend and AI Calls are judged separately,
and AI Spend As Of is only updated when something is written. When the spend archive exists
(see "Durable archive" below), the figure per project is
max(transcript all-time total, archive all-time total). The decision lives in the pure function
`decide_write()` and is unit-tested.

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

### Durable archive

Claude Code deletes old transcripts, so history older than its retention period disappears.
`--archive [PATH]` upserts the days of this run into a CSV that survives that.

```
python scripts/ai-cost/spend-report.py --days 3 --archive                  # default path
python scripts/ai-cost/spend-report.py --days 30 --archive D:\spend.csv
python scripts/ai-cost/spend-report.py --source archive --format html --open   # full history
python scripts/ai-cost/spend-report.py --source archive --since 2026-09-01 --project foo --format csv
```

- Default path: `%USERPROFILE%\.claude\spend-archive\spend-daily.csv` (outside the repo: it holds project
  names and costs; the folder is created). Columns are exactly `--format csv`:
  `day,project,model,kind,calls,input,output,cache_read,cache_write,cost_cache_read,cost_cache_write,cost_input,cost_output,ctx,big,tz`.
  Sorted by day, then project. Written atomically (temp file + replace).
- Rows are keyed `(day, project, model, kind)`. For each day in the scanned window the fresh rows replace
  that day's archived rows, **unless the fresh total cost for the day is lower than the archived total**
  (transcripts were deleted): the archived rows then stay and a `WARN archive:` line is printed.
  Days outside the window are untouched, so running `--days 3` daily keeps growing the history.
- The archive always receives every project; `--project` only filters the report that is printed.
- The archive records its tz. A run with a different `--tz` is refused rather than mixing day boundaries.
- `--source archive` reports from the archive instead of the transcripts, in any `--format`. Without
  `--days/--since/--today` it covers the whole history; `--archive PATH` picks the file and nothing is
  written. The days are in the archive's tz.

## Tests

```
python -m unittest discover -s scripts/ai-cost/tests
```

The HTML dashboard is a single self-contained file (data embedded as JSON; Chart.js loaded
from cdnjs). It defaults to the temp folder because it contains project names and costs;
do not commit it.
