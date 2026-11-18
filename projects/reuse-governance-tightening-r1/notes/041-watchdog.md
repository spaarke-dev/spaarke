# Task 041 - knowledge-refresh staleness watchdog

Check: `monthly/knowledge-refresh`, added to the `alerts` matrix in `.github/workflows/nightly-health.yml`.
Decided in the existing "Decide status" step (no new job, no new workflow, no scheduled agent). Alert wiring is the unchanged
"Raise or clear the alert" call, so `dry-run` stays on off the default branch.

Rule: newest `## YYYY-MM-DD` heading of any kind in `knowledge/REFRESH-LOG.md`; age > 35 days = failing, else ok.
A missing or unparsable log is failing (unknown date is not a pass). The step never exits non-zero for this check.

Changes beyond the matrix row:
- Checkout is now non-cone sparse (`/.github/actions/` + `/knowledge/REFRESH-LOG.md`) so the file is present without pulling all of `knowledge/`.
- The generic "Re-run the failing job..." / "All jobs ... succeeded." summaries are skipped for this check (`custom_summary`).

## Local verification (decide script extracted from the workflow, run under bash with GITHUB_OUTPUT; today = 2026-11-11)

| Log | Newest heading | status | Summary |
|---|---|---|---|
| real `knowledge/REFRESH-LOG.md` | 2026-08-26 | **failing** | newest entry dated 2026-08-26, 77 days ago (limit 35) |
| temp copy, 2026-08-26 heading rewritten | 2026-11-01 | **ok** | Newest refresh-log entry is 2026-11-01 (10 days ago, limit 35). |
| boundary temp copy | 2026-10-07 (35 d) | ok | 35 days ago, limit 35 |
| boundary temp copy | 2026-10-06 (36 d) | failing | 36 days ago, limit 35 |
| missing file | none | failing | file missing or no parsable heading |

All exit 0. Not run: `gh workflow run` (coordination rule), `actionlint` (not installed; YAML parsed with the `yaml` package instead,
matrix row and sparse-checkout inputs read back correctly). Existing-check regression run not possible locally (`jq` absent on this
machine; ubuntu runners have it) - that path is unchanged apart from the final `custom_summary` conditional.

Acceptance criterion 1 (workflow_dispatch run) is still to do after push: expect `[dry-run] gh issue create ... monthly/knowledge-refresh` in the
`Alert monthly/knowledge-refresh` job log and a green workflow.
Owner note: the alert fires on the first master run after merge (intended); `dry_run_alerts` is available on dispatch.
