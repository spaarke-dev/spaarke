---
description: Manually refresh the AI Spend (est.) / AI Calls / AI Spend As Of / AI Cost (allocated) fields on the portfolio board for one, several, or all projects, produce the on-demand AI spend report / HTML dashboard (by day, project, model, main vs sub-agent), and compare invoiced actuals with the estimate
tags: [devops, portfolio, cost, gh-cli]
techStack: [gh-cli, python]
appliesTo: ["/project-spend-update", "update project spend", "refresh AI cost", "sync project cost", "actual AI spend", "add invoices"]
alwaysApply: false
last-reviewed: 2026-10-27
---

# project-spend-update

> **Category**: DevOps / Portfolio
> **Tier**: On-demand utility — deliberately NOT wired into `task-execute` or any other hook.
> **Last Reviewed**: 2026-10-27 (invoiced actuals: `--actuals`, `AI Cost (allocated)`)

## Why this is a separate, manually-triggered skill

An earlier version of this wired the cost refresh into `devops-project-sync` Step 1, which fires automatically at `task-execute` Step 9.6 (once per completed task). That under-covers the actual spend pattern: `unified-access-control-r2`'s heaviest cost came from Workflow-tool-driven batches whose executor agents follow their own embedded instructions rather than the full `task-execute` protocol, so a task-completion-tied refresh would silently miss exactly the sessions most worth tracking. Owner direction (2026-10-04): decouple it, run it on demand, keep it simple (no attempt to distinguish metered-API from subscription/Max-plan usage — reconcile against the actual invoice manually at the portfolio level).

## Prerequisites

- `gh` CLI authenticated with project read/write scope
- Python 3 available on PATH
- The target project(s) already exist as `[Project]:` Type=Project items on the board (run `/devops-project-register` first if not — see `projects/INDEX.md` for the active registry)

## Purpose

Recompute estimated AI spend from local Claude Code transcripts and push it to the board, for whichever projects you name — not automatically, not on a schedule, only when invoked.

## When to Use

| User says | Behavior |
|---|---|
| `/project-spend-update` (no args) | Refresh every `Type=Project` item on the board |
| `/project-spend-update {slug} [{slug} ...]` | Refresh just the named project(s) |
| "update project spend" / "refresh AI cost" / "what's the current spend on {project}" | Same, scoped to what was asked |
| "spend report", "cost dashboard", "spend by day", "where is the AI spend going" | Step 4 only (report/dashboard); no board writes |
| "actual spend", "what did we really pay", "spend YTD", the owner sends invoice screenshots | Step 6 (invoiced actuals); then Step 1 if the board should carry the new allocation |

Not auto-triggered by anything — including `task-execute`, `devops-project-sync`, or project completion. If you want it current, run it.

## Workflow

### Step 0: Fold recent days into the durable archive

Transcripts are the only raw source and they can be deleted (Claude Code prunes them after `cleanupPeriodDays`, default 30 — this machine sets 3650 in `~/.claude/settings.json`; manual cleanup of `~/.claude/projects/` or a new machine loses them too — removing a git worktree does not). The archive at `%USERPROFILE%\.claude\spend-archive\spend-daily.csv` keeps every day once it has been captured, so capture first:

```bash
python scripts/ai-cost/spend-report.py --days 7 --archive     # upsert the last 7 local days; never shrinks a stored day
```

Use a wider `--days`/`--since` if the last run was longer ago. A day whose fresh total is lower than the archived one keeps the archived rows and prints `WARN archive: ...` (transcripts for that day were deleted) — relay that warning. A tz mismatch with the archive exits 2: rerun with the archive's `--tz`, never delete the archive to get past it.

### Step 1: Run the script

```bash
python scripts/ai-cost/update-board-spend.py                      # all projects
python scripts/ai-cost/update-board-spend.py unified-access-control-r2   # one project
```

This does, per project:
1. Match the project slug to a local Claude Code transcript folder (`~/.claude/projects/c--code-files-spaarke-wt-{slug}`, with fallback fuzzy matching)
2. If no folder matches and the archive has no rows for it → skip (leave existing field values untouched, do not write zero)
3. If a folder matches but has zero logged API calls and no archive rows → skip, same reason
4. Otherwise: rescan **all** transcripts for that folder from scratch, take the max of that and the project's archive total, and push `AI Spend (est.)`, `AI Calls`, `AI Spend As Of` = today
5. **Never lowers a board value.** Spend and calls are compared separately with the current board values; a lower fresh figure is kept as-is and printed as `KEEP <slug> (fresh < board; transcripts likely pruned)`. `AI Spend As Of` changes only when something was written.
6. Then, when the invoice list exists (Step 6), push `AI Cost (allocated)`: the project's share of the invoiced actuals. It is written as computed, even when lower (both inputs are durable; a share of the open cycle shrinks when other projects grow). The run ends with `AI Cost (allocated): N written, M failed (invoices through <date>)`; relay that date, since the allocation is only as current as the invoice list.

### Step 2: Report

Print the script's own summary line (`N updated, M skipped (no local data), K failed`) plus the per-project detail lines it already emits. Don't re-narrate what the script already printed — relay it.

### Step 3: Note staleness, don't silently claim freshness

If reporting a dashboard or total that includes this field elsewhere (e.g. via `/devops-portfolio-status`), check each project's `AI Spend As Of` date first. A project not refreshed by this skill since its last heavy work session will under-report — say so rather than presenting the total as current.


### Step 4: Spend report and dashboard (on request, or after Step 2 when asked "where is it going")

```bash
python scripts/ai-cost/spend-report.py --today                       # what was spent today (local calendar day, 00:00 to now)
python scripts/ai-cost/spend-report.py --days 14                      # text tables + "Biggest drivers"
python scripts/ai-cost/spend-report.py --days 30 --format html --open # dashboard (charts, project filter) in %TEMP%
python scripts/ai-cost/spend-report.py --project unified-access-control-r2 --days 7
python scripts/ai-cost/spend-report.py --since 2026-10-01 --format csv --out spend.csv
python scripts/ai-cost/spend-report.py --source archive --format html --open   # full history from the archive
```

"Today" means the calendar day, not the last 24 hours: use `--today`, not `--days 1`. Days are local calendar days by default (`--tz local`, DST-aware); `--tz utc` gives the old UTC days. Every output states its tz.

One pass over the local transcripts (~30–100 s), grouped by day (local), project, model and main vs sub-agent, with the cost split into cache read / cache write / input / output. Relay the "Biggest drivers" lines and the per-day table; for the dashboard give the file path. Read the drivers against `.claude/constraints/agent-cost.md` (sub-agent share, model mix, context size, cache-write share). Same pricing and dedup as `get-project-cost.py`; estimates at list price from this machine's transcripts only — not an invoice. The HTML stays outside the repo (it carries project costs).


### Step 5: Refresh the Spaarke Dev Metrics page (when asked, or after Step 1 when the owner wants the page current)

The private dashboard https://claude.ai/artifact/R4J6P8Adsk12XhJV8ypxjR reads one attached file, dataset `spend`. Refresh it:

1. Run Step 0 (archive capture), then `python scripts/ai-cost/spend-report.py --source archive --format csv --out <scratchpad>/ai-spend-daily.csv` — the full archived history, so the page never loses days when transcripts are pruned
2. Upload it: Artifact `publish` with `url` = the page, `file_path` = the CSV, `asset: true`. Note the returned `/_blob/<id>` url.
3. `ArtifactData` `get` `datasets/spend`, then `update` it, pinned with `if_version`, setting `source.url` to the new url, `source.name` to `ai-spend-daily.csv` and `updated` to `{at: <ISO now>, by: "Claude (spend-report.py)"}`.
4. Delete the previous asset (Artifact `delete` with `path` = its id) once nothing references it.

The page's calculations (`summary`, `daily`, `by_model`) recompute from the file, so no page edit is needed. Its windows are Today / 7 / 14 / 30 / 60 days / All, anchored on the newest day in the file, in local calendar days (the CSV's `tz` column). The data covers this machine's transcripts and archive only, at list price.


### Step 6: Invoiced actuals (when the owner sends invoices, or asks what was really paid)

The invoices are typed in by hand: there is no export for a Max plan. The owner sends screenshots of the claude.ai billing list (Date / Total / Status); the PDFs are not needed.

1. Add each new charge to `%USERPROFILE%\.claude\spend-archive\invoices.csv` (`date,amount,kind,invoice_id,notes`, ISO dates, outside the repo). `kind`: `plan` for the Max plan fee (the $200 charge on the 6th; when two $200 charges share the 6th, one is `plan`, the other `credits`), `credits` for every other charge (auto-recharge and manual top-ups), `other` for $0 or unusual lines. When screenshots overlap, a row repeated at the end of one and the start of the next is one charge; equal amounts on one day inside a screenshot are separate charges. Ask the owner when it is ambiguous, and note the answer in `notes`.
2. Run Step 0 so the archive is current, then:
   ```bash
   python scripts/ai-cost/spend-report.py --actuals                 # by month (+ year to date), by billing cycle, allocation
   python scripts/ai-cost/spend-report.py --actuals --format csv --out <scratchpad>/actuals.csv
   ```
3. Relay the month table (year-to-date actual spend), the effective-rate line and the top of the allocation. Ratios exist per billing cycle only: credits are prepaid, so a month can show a purchase with no usage or usage with no purchase. A `*` ratio is overstated (the archive covers only part of the cycle). The invoices cover the whole account (claude.ai chat and other machines too); the estimate covers this machine only.
4. Run Step 1 to put the new allocation on the board, if the owner wants it current.

## Outputs

- Up to 4 GitHub Project field mutations per project touched
- Terminal summary: updated / skipped / failed counts + per-project cost and call count, then the allocated lines

## What this does NOT do

- Does not distinguish metered-API spend from subscription/Max-plan usage (by design — see Why above)
- Does not fetch invoices: the owner sends them and they are typed into the invoice list (Step 6)
- Does not run automatically on any schedule, hook, or task completion
- Does not recover cost history that was never captured: days not in the archive whose transcripts are gone (old worktrees cleaned up, or work done before 2026-08-12 on this machine) stay blank, not zero. Once captured by Step 0 a day is kept
- Does not write costs into the repo — the archive, CSVs and HTML live outside it (`scripts/ai-cost/README.md`)

## Related Skills

- `/devops-project-sync` — syncs Task Count / Tasks Completed / Status fields (NOT cost — see its SKILL.md for why this was split out)
- `/devops-portfolio-status` — reads the `AI Spend (est.)` field for the dashboard rollup; does not refresh it
- `/devops-project-register` — must have run first for a project to have a board item this skill can update
- `/devops-project-archive` — Step 3 runs Step 0 + Step 1 for the closing project to record its final spend

## Reference

- Report/dashboard: `scripts/ai-cost/spend-report.py` (see `scripts/ai-cost/README.md`)
- Script: `scripts/ai-cost/get-project-cost.py` (per-project cost computation) + `scripts/ai-cost/update-board-spend.py` (board iteration + field writes)
- Board field IDs documented inline in `update-board-spend.py` (re-derive via `gh project field-list 2 --owner spaarke-dev --format json` if the board is ever rebuilt)
