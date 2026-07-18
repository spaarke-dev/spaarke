# START HERE — messaging-communication-app-r2 (new session kickoff)

> Read this first, then [`CLAUDE.md`](CLAUDE.md) and [`design.md`](design.md). Created 2026-07-18 at worktree setup.

## Where you are

- **Worktree**: `c:/code_files/spaarke-wt-messaging-communication-app-r2` · branch `work/messaging-communication-app-r2` (from master `f53e49ede`, 0 behind at creation).
- **Phase**: DRAFT DESIGN. No spec/plan/tasks yet. `design.md` is authored and grounded in a real 5-part resource audit.
- **R1 is done**: `messaging-communication-app-r1` is complete, merged to master, and archived (Issue #654 closed). Its lessons + binding constraints are in [`CLAUDE.md`](CLAUDE.md) and `../messaging-communication-app-r1/notes/lessons-learned.md`.

## The one thing blocking spec

**Q3 — coordination confirm.** Everything else is locked (Q1/Q2/Q4/Q5 — see design §10). Before `/design-to-spec`, confirm with the owner:

> Can R2 upgrade `ai-spaarke-ai-workspace-UI-r2`'s already-shipped `sprk_gridconfiguration`
> (`e1826c4c-9575-f111-ab0e-7ced8ddc4a05`) + `communications-list` widget **in place**, or is that
> project mid-flight and owns those artifacts (→ R2 forks/coordinates differently)?

That project is an **active worktree** (`c:/code_files/spaarke-wt-ai-spaarke-ai-workspace-UI-r2`) — a quick `git log`/PR check there will inform the answer.

## Then, in order

1. **Q3 answered** → run `/design-to-spec` on `design.md`.
2. → `/project-pipeline` (scaffolds spec/plan/tasks, hot-path declaration check, registers on Portfolio #2 under Epic #431 EMAIL & MESSAGING).
3. Execute waves via `task-execute` per the plan.

## Scope at a glance (design.md is authoritative)

- **Surface 1** — record-threads view (regarding-mode Timeline) on **all 11** entity forms (NEW; `by-regarding` BFF endpoint).
- **Surface 2** — standalone All-Communications page (**ship it**; DataGrid shell).
- **Surface 3** — rich `communications-list` workspace widget (upgrade the thin one in place — pending Q3).
- **CC-1** thread regarding (typed lookups + new Lookup discriminator; **no** category/tags).
- **CC-2** `sprk_communicationparticipant` junction + person filter (**in scope**, schema ADR).
- **CC-3** read endpoints (`by-regarding` + filtered `query`) — extend the blessed impersonation read path.
- **CC-4** auto-threading policy.
- **CC-5** compose-form enrichment (Subject/topic + structured recipients + Cc/Bcc) — the convergence point for CC-1 naming + CC-2 index.

## Don't

- Don't re-run the resource investigation — it's archived in `notes/r2-resource-investigation.md`.
- Don't add a second reads access mechanism or a second grid config for `sprk_communication`.
- Don't relitigate the locked decisions without a new owner directive.
