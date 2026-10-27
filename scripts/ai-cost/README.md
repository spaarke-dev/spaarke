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
(AI Spend / AI Calls / AI Spend As Of), then each project's share of the invoiced actuals to AI Cost (allocated)
(see "Invoiced actuals" below). Normally driven by the `/project-spend-update` skill.

The board figures never go down. Transcripts are deleted over time, so a fresh figure that is lower
than the one already on the board is not written: the script prints
`KEEP <slug> (fresh < board; transcripts likely pruned)`. AI Spend and AI Calls are judged separately,
and AI Spend As Of is only updated when something is written. When the spend archive exists
(see "Durable archive" below), the figure per project is
max(transcript all-time total, archive all-time total). The decision lives in the pure function
`decide_write()` and is unit-tested. AI Spend As Of is written last, and only if at least one of the two
figure writes succeeded.

Board slug to transcript folder (`find_folder()` in `get-project-cost.py`), in order: exact `-wt-<slug>`
(case-insensitive); exact `-wt-<slug>` with a leading `spaarke-` stripped; last resort, a folder that
*ends* in `-<slug>` (extra prefix only), never one that merely contains the slug and never the exact
folder of another board slug. So `spaarke-daily-update-service` does not pick up the `-r2` folder. The
archive is looked up under the folder's project name, then the slug, then the slug minus `spaarke-`.

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
- Rows are keyed `(day, project, model, kind)`. For each day in the scanned window, each **(day, project)**
  group of fresh rows replaces that group's archived rows, **unless the fresh group is lower** (transcripts
  were deleted): fresh cost lower than archived, or fewer calls (a model with no price archives $0 with N
  calls). The archived group then stays (also when the project is absent from the fresh scan) and a
  `WARN archive: <day> <project>` line is printed. Another project growing the same day does not cover for
  it. Days outside the window are untouched, so running `--days 3` daily keeps growing the history.
  A change to project naming (e.g. a renamed worktree folder) leaves the old-name rows in place; they are
  not merged or removed.
- The archive always receives every project; `--project` only filters the report that is printed.
- The archive records its tz. A run with a different `--tz` is refused rather than mixing day boundaries.
  `tz=local` means the machine's zone at write time and is not re-checked if the zone later changes.
- If the archive cannot be written (locked file, permissions), the report is still printed, the line
  `spend-report: archive not written: <error>` goes to stderr, the previous archive and any temp file are
  left clean, and the exit code is 2.
- Concurrent `--archive` runs are not locked: the last writer wins.
- `--source archive` reports from the archive instead of the transcripts, in any `--format`. Without
  `--days/--since/--today` it covers the whole history; `--archive PATH` picks the file and nothing is
  written. The days are in the archive's tz.

## Invoiced actuals (`--actuals`)

The estimate prices this machine's transcripts at list price. What was actually paid comes from the invoices on the
claude.ai billing page: the Max plan fee on the same day each month, plus credit purchases (auto-recharge when the
balance runs low, and manual top-ups). There is no export for a Max plan, so the charges are typed by hand into a CSV
beside the archive:

```
%USERPROFILE%\.claude\spend-archive\invoices.csv
date,amount,kind,invoice_id,notes
2026-10-06,200.00,plan,B4B3290F-0149,Max 20x Oct 6-Nov 6
2026-10-09,450.74,credits,,
```

`date` is ISO; `amount` may carry `$` and commas, and a refund is negative; `kind` is `plan`, `credits` or `other`;
`invoice_id` and `notes` are optional. It lives outside the repo, like the archive.

```
python scripts/ai-cost/spend-report.py --actuals                     # text
python scripts/ai-cost/spend-report.py --actuals --format csv --out actuals.csv
python scripts/ai-cost/spend-report.py --actuals --project ebilling  # filters the allocation table only
```

`--invoices PATH` and `--archive PATH` pick the files. It reads the archive, not the transcripts, and covers the
whole history, so it takes no `--days`/`--since`/`--today` (and no `--format html`). Output:

- **By month**, with a year-to-date running total, and **by year**: plan / credits / other / actual, and the estimate.
- **By billing cycle**: a cycle starts on each `plan` charge and runs to the day before the next (the last one to the
  newest invoice or archived day; charges before the first plan charge form a leading cycle), with the ratio
  actual / estimate. Credits are prepaid, so a charge pays for later usage: **only cycles have a ratio**, never months
  or days. `*` marks a cycle the archive covers only partly (ratio overstated); `-` one before the archive.
- **Effective rate**: actual / estimate over the cycles the archive fully covers.
- **Allocated actual by project**: each cycle's invoiced total split by the projects' share of that cycle's estimate.
  A cycle with no estimate stays `unallocated`; a partly covered cycle is split whole. Allocated plus unallocated equals
  the invoiced total.

The invoices cover the whole account (claude.ai chat, other machines); the estimate covers this machine only.
`update-board-spend.py` writes each project's allocation to the board field `AI Cost (allocated)`, as computed (it may
go down: both inputs are durable, and a share of the open cycle shrinks when other projects grow). Without an invoice
list the field is not written.

## Model fit: was each piece of work run on the right model?

Waste runs both ways: a top-tier model on search or deploy-script work, and Sonnet on work that comes back
for fix rounds. `spend-report.py --model-fit` measures both from three sources.

```
python scripts/ai-cost/spend-report.py --days 7 --model-fit                 # text
python scripts/ai-cost/spend-report.py --days 7 --model-fit --format csv --out fit.csv
python scripts/ai-cost/spend-report.py --days 7 --model-fit --format html --open
python scripts/ai-cost/spend-report.py --model-fit --project unified-access   # json also works
```

`--days/--since/--today/--tz/--project/--format/--out/--open` behave as for the cost report. `--launch-log PATH`
and `--repo PATH` override the launch log and the repo whose task notes are read. It cannot be combined with
`--archive` or `--source archive` (it reads the transcripts). Costs are list-price estimates, as everywhere here.

**Launch log.** The `PreToolUse` hook `scripts/quality/require-agent-model.py` appends one CSV line per Agent,
Task or Workflow launch it allows (a denied launch is not logged) to
`%USERPROFILE%\.claude\spend-archive\agent-launches.csv` (override: `SPAARKE_AGENT_LAUNCH_LOG`; outside the repo).
Columns: `ts_utc, session_id, transcript_path, project, tool, subagent_type, model, effort, description, tool_use_id`.
`model` and `effort` are what the call or the agent definition set (`inherit` when neither did). The description
is cut to 120 characters. The prompt is never logged. A logging failure goes to stderr only and cannot change the
hook's decision. The file holds session ids and local paths (including the Windows username): never commit it
(~13 MB/year, no rotation). Nothing is logged for launches before the hook change, so a fresh log starts empty.

**Sub-agent transcripts.** Claude Code stores each sub-agent as
`~/.claude/projects/<project>/<session>/subagents/agent-<id>.jsonl` with a sibling `agent-<id>.meta.json` holding
`agentType`, `description`, `toolUseId` and the requested `model`. A launch is matched to a transcript, in this order:
the log's `tool_use_id` equal to the meta file's `toolUseId`; then the same session and description (and agent type
when both are known), earliest start at or after the launch; then, for a transcript with no meta description, the
same session and a start within 120 seconds after the launch. A transcript is used once. Workflow launches are
counted but not matched; the agents they spawn appear as transcripts.

**Table.** Work type x model, where work type is the agent type refined by description keywords:
`deploy*`; `review*`/`verif*`; `research*`/`investigat*`; `map`/`find`/`search*`/`inventor*`; `implement*`/`fix*`/`add`;
`test*`. Specific agent types keep their own category (code-mapper and Explore are *map*, adversarial-reviewer is
*review*, researcher is *research*); `implementer` is refined to *deploy* or *test* when the description says so; any
other type is decided by the keywords (*other* when none match). A row's model is the family (opus, fable, sonnet,
haiku) that made most of the transcript's calls. Columns: launches (transcripts), how many have a launch record,
total and per-launch cost, average calls per launch, average context per call, rework.

**Unmatched.** Never dropped. Launches with no transcript (still running, transcripts pruned, outside the window) and
transcripts with no launch record (launched before the log existed, or by a session without the hook) are counted in
the header. Unmatched transcripts stay in the table, classified from their meta file. Until the launch log has
accumulated, most rows say `logged 0`.

**Policy adherence.** The header shows main-session calls by model and sub-agent calls by model.

**Flags.** Judged within one work type, and only with at least 3 launches that have a rework record:
- `OVER`: opus/fable/mythos on *map* or *deploy* work; or a top-tier row whose rework rate is higher than or equal to
  the Sonnet row's for the same work type.
- `UNDER`: a Sonnet row with any escalation, or a rework rate 25 points or more above the work type's average over
  all models.

The rule constants are at the top of the model-fit section in `spend-report.py`.

### Rework signal

Task notes record how a task came out so the report can tell good model choices from costly ones. One line, in the
task's notes (`projects/<slug>/notes/*.md`, or the `<notes>` of the task POML):

```
rework: none | fix-round-<N> | escalated-to-<model>
```

- `none`: the first attempt passed review and verification.
- `fix-round-<N>`: it needed N rounds of fixes after review or a failed check.
- `escalated-to-<model>`: the work was re-run on a higher tier (for example `escalated-to-opus`).

The line must start the line (a list bullet, `>` or bold markers before it are fine). The task number comes from
`task <NNN>` after the value (`rework: fix-round-1 task 042`), else from the file name (`task-042-x.md`, `042-x.poml`).
If a task has several lines, the worst wins. A launch is attributed to a record by **project and task number**: the
launch description (the notes line's project is the folder under `projects/`) must contain `task 042` (also `task-042`
or `task #42`). A launch with no task number in its description, or a task with no record, has rework **unknown**. It is
shown as unknown and never guessed. Every launch for a task inherits the task's value, so name the task in the
description of the launches that belong to its rework (the implementer and its fix rounds), not of the single review.
Only the checkout the script runs from is read, so run it from a tree that has the notes merged.

## backfill-tasks-planned.py

The board field `Tasks Planned` (number) holds how many tasks a project started with; `Task Count` is what it has
now, so `Task Count / Tasks Planned` is scope growth. The script fills it from git history: the oldest committed
version of `projects/<slug>/tasks/TASK-INDEX.md` across all refs, counting distinct numbered task rows
(zero-padded: `| 001 |`, `| [001](..) |`, `| **001** |`; a bare `| 1 |` does not count), and at least the `tasks/*.poml` files present at that commit (an index
committed before its POMLs, or without numbered rows). Task Count itself counts `tasks/*.poml` (see the
`devops-project-sync` skill). A project first merged in one squash commit reports its size at that merge, so treat
small ratios as lower bounds.

```
python scripts/ai-cost/backfill-tasks-planned.py                  # dry run
python scripts/ai-cost/backfill-tasks-planned.py --apply          # write; creates the field if the board lacks it
python scripts/ai-cost/backfill-tasks-planned.py --apply --force  # also replace values already set
python scripts/ai-cost/backfill-tasks-planned.py <slug> ...       # only these
```

A non-empty value is never overwritten without `--force`. Board slugs map to folders by exact name, then without a
leading `x-` (retired projects) or `spaarke-`. Projects with no TASK-INDEX in history are reported as skipped. The run
prints the ten largest Task Count / Tasks Planned ratios. Set `Tasks Planned` going forward when a project's tasks are
first created (`project-pipeline`); do not edit it later.

## Tests

```
python -m unittest discover -s scripts/ai-cost/tests
python -m unittest discover -s scripts/quality/tests      # the launch-log hook
```

The HTML dashboard is a single self-contained file (data embedded as JSON; Chart.js loaded
from cdnjs). It defaults to the temp folder because it contains project names and costs;
do not commit it.
