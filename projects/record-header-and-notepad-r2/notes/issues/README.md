# Schema-drift issues — for review

Three standalone issue documents produced by the 2026-08-24 schema verification sweep during `record-header-and-notepad-r2`. They are **not** in R2's scope; they are written to be evaluated on their own merits and, if approved, promoted into focused fix projects.

Grouped by record / component type, as requested.

| Issue | Area | Severity | Failure mode | Rough effort |
|---|---|---|---|---|
| [ISSUE-event-schema-drift.md](ISSUE-event-schema-drift.md) | **Event** — side pane, shared `EventTypeService`, one AI node doc string | High | Side pane cannot load any event (400, caught → error state) | 0.25–0.5 d |
| [ISSUE-daily-briefing-schema-drift.md](ISSUE-daily-briefing-schema-drift.md) | **Daily Briefing** — `DailyBriefingCollector` | High | **Silent** — flagged Projects and Events vanish from every briefing with no user-visible error | 0.25 d |
| [ISSUE-work-assignment-schema-drift.md](ISSUE-work-assignment-schema-drift.md) | **Work Assignment** — create endpoint | Medium-High | HTTP 500 whenever a matter or due date is supplied | 0.25–1 d |
| [ISSUE-output-mapping-unreachable.md](ISSUE-output-mapping-unreachable.md) | **BFF AI** — `PlaybookService` / `OutputOrchestratorService` | High | **Silent** — every playbook `outputMapping` is dead config; `extraction.aiSummary` has no consumer at all | scoping first, then TBD |

## Common root cause

All three are the same failure: **Dataverse column names hard-coded in TypeScript/C# with nothing verifying they exist.** Seven bad column names across six files, none caught by a build, a test, or a review.

The single highest-value follow-up is not any individual rename — it is a check that every shipped `$select` / `ColumnSet` / field-visibility list resolves against live entity metadata, so the next drift fails in CI rather than in production. Each issue doc proposes a local version; a shared one would be better.

## How these were verified

`az account get-access-token --resource https://spaarkedev1.crm.dynamics.com` → Dataverse Web API, executing each shipped query **verbatim** and reading the response code. Every claim in these documents is reproducible; none rests on reading code alone. Two of the three were confirmed as live HTTP 400s.

## Suggested next step

Review, then for each approved item either `/devops-idea-create` (capture as a backlog Idea) or `/design-to-spec` if it is going straight to a project. The Daily Briefing one is the cheapest fix with the largest correctness payoff and has no open design questions blocking it — a reasonable first candidate.

## Note on scope history

These were briefly folded into R2's scope on 2026-08-24 (design.md §9.1) and then pulled back out, per owner direction, in favour of separate evaluation. `design.md` §9.1 is retained as the discovery record and points here.
