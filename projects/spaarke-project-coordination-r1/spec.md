# spaarke-project-coordination-r1 — AI Implementation Specification

> **Status**: Ready for Implementation (rev 2, 2026-10-17)
> **Created**: 2026-10-15
> **Source**: `design.md` rev 3, `notes/pilot-issue-triage-2026-10-15.md`, `notes/naming-spike-2026-10-17.md`, `revision-instructions-coordination-r1.md`
> **Scope**: repo-wide process and tooling — skills, hooks, scripts, CI, Board #2. No BFF, Dataverse schema or product UI change.
> **Governing rule (owner, 2026-10-17)**: keep the process well structured but as minimal as necessary, and add nothing that constrains Claude Code's cross-session messaging. Specify mechanics fully. Write **no** guidance anywhere on when, whether, how often or in what form to message: Claude Code's built-in judgment already does this well, and any written guidance is a nudge (owner, 2026-10-17).

## Changes in rev 2 (review without a diff tool)

| FR | Change | One line |
|---|---|---|
| FR-00 | **Added** | Messaging configuration: project addresses, inbound settings, version floor, `-p` workers |
| FR-25 | **Added** | Worktree write boundary (E4), extending `guard-worktree-links.py` |
| FR-26 | **Added** | Cross-session messaging measurement, so suppression is detectable |
| FR-05 | Rewritten | The constraint file is three sections (mechanics, record, triage); it contains **no** text on when to message; restated inbound limits removed |
| Phase 0 | **Added checkpoint** | Messaging changes are proved first: baseline, apply FR-00 alone, re-measure after 14 days; phases 3–5 start only if it passes |
| FR-06 | Rewritten | F1 with a Large correct fix → owner, urgent, same turn |
| FR-13 | Rewritten | The registry lives in README frontmatter, not INDEX.md columns (owner, 2026-10-17) |
| FR-14 | Rewritten | Signals 1–2 required; signal 3 behind a flag (default off); design-prose and dev-log signals removed; volume count folded in |
| FR-15 | Rewritten | `/design-to-spec` invocation replaced by one checklist line |
| FR-17 | Rewritten | Coordination issues only for pre-code overlaps; the PR is the record for landed code |
| FR-18 | Rewritten | Adds FR-18b (optional inbox-socket notice) |
| FR-20 | Rewritten | Step 0 resolves the *item*; messaging is not tied to the step |
| FR-22 | Rewritten | `ack` label only, applied by the owning project's session; no comment parsing, no 24 h escalation |
| FR-16 | Moved | Phase 3 → phase 5 (after the report-only period); restores NFR-05 for phase 3 |
| FR-19 | **Removed** | Folded into FR-14 as one report line |
| FR-24 | **Deferred** | Status digest: post-r1, conditional |
| FR-01–04, 07–12, 21, 23 | Unchanged in substance | FR-04 wording only |

## Executive Summary

Spaarke sessions already message each other unprompted, and that is the most valuable coordination mechanism the team has. It fails for mechanical reasons: held messages expire, addresses change on restart, and truncated names collide. When the other project has no session running, nothing reaches it at all. This project fixes the messaging configuration, gives Claude the lookup data to know whom to tell, and adds a minimal durable layer for the cases messaging can't cover:
- a deploy ancestry guard and dev log;
- a standard triage prompt for findings;
- per-project lifecycle and owned areas;
- a deterministic detector;
- a start-of-session catch-up banner;
- a critical-area acknowledgement in CI.

Nothing in it tells a session when to message.

## Scope

### In Scope

| Phase | Deliverable | FRs |
|---|---|---|
| 0 | Messaging configuration; baseline messaging measurement | FR-00, FR-26 |
| 1 | Deploy guard; dev log; raw-command hook; worktree write boundary | FR-01 – FR-04, FR-25 |
| 2 | Constraint file; triage at Step 9.5; issue hygiene (pilot L1–L5) | FR-05 – FR-12 |
| 3 | Registry frontmatter; detector signals 1–2, report-only | FR-13 – FR-15 |
| 4 | Pre-code coordination issues; `SessionStart` banner | FR-17, FR-18, FR-18b |
| 5 | Step 0 / Step 9 checks; critical-area acknowledgement in Router; `/conflict-check` reuses the detector | FR-16, FR-20 – FR-23 |

Between phases, the owner checkpoints from design §9 apply. They don't gate task generation, and each phase is independently revertible (NFR-05).

**Phase 0 goes first and is proved before anything else touches messaging.**
1. Take the FR-26 baseline.
2. Apply FR-00 only: settings, addresses and the version floor.
3. Re-measure after 14 days.

The checkpoint passes when cross-session sends don't drop, unprompted sends don't shift toward item-prompted ones, and no held, expired or misaddressed delivery notices remain. Phases 1 and 2 don't touch messaging and may run during the 14 days. Phases 3–5 (registry, detector, records, Router acknowledgement) start only after the checkpoint passes. If it fails, the owner decides whether to revert FR-00 and whether phases 3–5 go ahead at all.

### Out of Scope

- Any BFF, `Spaarke.Core`, `Spaarke.Dataverse`, Dataverse schema or product UI change.
- **Any rule about when or how often to message.** That includes batching or limiting instructions, message templates, "message only if" conditions, and messaging steps inside skills.
- Phase 6, the owner's status digest (FR-24, deferred).
- Detector signal 3 (planned POML overlap) enabled by default. Signal 4 (design-prose parsing) and signal 5 (dev log as an overlap signal).
- Channels (research preview). Cross-machine messaging (`isolatePeerMachines`).
- Applying the owner's decisions on uac-r2's pilot recommendations (uac-r2's wrap-up).
- Creating Board #2 views through the API (pilot L6: done by hand).
- Any numeric task budget.
- Guarding Office add-in (SWA) or Dataverse web-resource deploys. Those deploys log only.

### Affected Areas

| Path | Change | Phase |
|---|---|---|
| User settings guidance (`.claude/constraints/cross-session.md` §B) and a `SessionStart` check script under `scripts/quality/` | Messaging-config and address check | 0 |
| `scripts/quality/` (new measurement script) | FR-26 | 0 |
| `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-WorkspaceBff.ps1`, `scripts/provisioning/Deploy-ControlPlane.ps1`, `.github/workflows/deploy-bff-api.yml` | Ancestry guard + dev log | 1 |
| `scripts/Deploy-OfficeAddins.ps1`, web-resource deploy scripts | Dev-log append only | 1 |
| `scripts/` (new shared guard / dev-log helper) | FR-01, FR-03 | 1 |
| `scripts/quality/guard-worktree-links.py` (+ its tests) | Write-boundary rules (FR-25) | 1 |
| `.claude/settings.json` | `PreToolUse` raw-command hook; `SessionStart` checks and banner | 1, 0/4 |
| `.claude/constraints/cross-session.md` (new); root `CLAUDE.md` (about 3 lines + one §17 row) | FR-05 | 2 |
| `.claude/skills/task-execute/SKILL.md` | Step 9.5 triage, Step 10 tracking-issue close (2); Step 0, Step 9 (5) | 2, 5 |
| `.claude/skills/task-create/SKILL.md`, wrap-up `090` template | Issues in task files; `<coordination>` line; close-out | 2, 3, 5 |
| `.claude/skills/project-defer-issue-tracking/SKILL.md` | Owning project + Code Area; rulings on the issue | 2 |
| `.claude/skills/project-pipeline/SKILL.md` and the README template it generates | Registry frontmatter at init | 3 |
| `projects/*/README.md` of active projects | Bootstrap frontmatter | 3 |
| `.claude/skills/design-to-spec/SKILL.md` | One checklist line | 3 |
| `.claude/skills/devops-project-sync/SKILL.md`, `.claude/skills/devops-project-archive/SKILL.md` | Mirror lifecycle to Board #2 Status | 3 |
| `scripts/` (new detector); nightly CI job | FR-14, FR-15 | 3 |
| `scripts/quality/` banner (extends `quality-alert-banner.py`) | FR-18 | 4 |
| `.github/workflows/ci-router.yml` | Critical-area acknowledgement | 5 |
| `.claude/skills/conflict-check/SKILL.md` | Reuse the detector | 5 |

Not changed: `projects/INDEX.md` (rev 1 planned columns there; superseded by C-8), project `CLAUDE.md` files.

## Corrections (verified against code, docs and the spike)

design.md rev 3 incorporates K-1 to K-6. This table is kept for traceability.

| # | Earlier text said | Verified fact | Consequence |
|---|---|---|---|
| K-1 | The deploy guard reads `1.0.0+<sha>` from `/ping` (design rev 2 §5.5, §14) | `/ping` returns `pong`; `/status` returns a hard-coded `"1.0.2"` (`src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:105-123`). The SHA is in the deployed DLL's ProductVersion, which is how R3 checked it (`projects/spaarke-SPA-external-access-platform-r3/notes/task-038-deploy-record.md:110,134`). | Read the DLL through Kudu VFS (`scripts/Deploy-BffApi.ps1:582-591`). No BFF change and no anonymous version disclosure. |
| K-2 | Guard `Deploy-BffApi.ps1` | `scripts/Deploy-WorkspaceBff.ps1:66,147` deploys to the same `spaarke-bff-dev` app with raw `az webapp deploy` | Guard every App Service path (FR-02) |
| K-3 | The owning project is the Parent link (design rev 2 §5.7) | Pilot L7 | The Owning Project field is used instead |
| K-4 | Lifecycle in `projects/INDEX.md` | INDEX.md is recency-scoped (30-day rule, rows removed on archive), has prose cells, and every project edits it | README frontmatter (C-8) |
| K-5 | E4 has no requirement (review §1.11) | `scripts/quality/guard-worktree-links.py` already guards links between worktrees and deletes of a worktree root (the 2026-10-13 incident). Plain writes and destructive git into a sibling are not guarded. | FR-25 extends that hook |
| K-6 | `claude --name` from `worktree-setup` names sessions (review §1.4) | `worktree-setup` doesn't launch Claude, and the sessions are VS Code extension sessions. Default addresses carry a suffix that changes every start, even on resume. A hook's `sessionTitle` sets the title but not the address (spike, 2.1.300 headless). | FR-00 decides the mechanism by a short verification first |
| K-7 | Minimum version 2.1.234 on Windows (review §1.4) | 2.1.234 is when messaging itself reached Windows; idle notices need 2.1.236 everywhere. The `claude` on `PATH` here is **2.1.6** (npm global); the extension carries 2.1.300. | The floor is 2.1.236 for every `claude` the machine runs |

## Requirements

### Functional Requirements

#### Phase 0 — Messaging configuration

**FR-00 — Messaging works by default.**

*Addresses.* Every session's messaging address identifies its project uniquely (the project slug, e.g. `unified-access-control-r2`). The mechanism is chosen by the first acceptance step, in this order of preference:
1. **Automatic:** a `SessionStart` hook returns `sessionTitle: <project>`. Use this only if an interactive session shows that the title becomes the address. The spike showed it doesn't in headless 2.1.300.
2. **One action per session:** the VS Code "Rename Session Tab" command, or `/rename <project>`. Use this if it becomes the address. The `SessionStart` check prints one line naming the exact rename when the address doesn't match.
3. **Lookup fallback:** if neither sets the address, the registry (FR-13) records each project's worktree folder, and the constraint file §A says how to resolve a project to a `ListAgents` row. The two folder pairs that collide after truncation are listed by the check.

CLI and `-p` sessions use `--name <project>` (spike: exact and stable across restarts).

*Settings.* User settings carry `crossSessionInbound: "accept"` and `dialogExpiry: "never"`. Both are user-scope, so the repo can't set them. The `SessionStart` check warns with the exact JSON when either is missing, and is silent otherwise. `-p` workers that may receive messages pass `{"crossSessionInbound":"accept"}` in `--settings`.

*Version.* The floor is 2.1.236 for every `claude` on the machine, `PATH` included, recorded in the constraint file §A. The check warns when the `PATH` binary is below it.

Acceptance:
- (a) **Verification step:** rename one interactive VS Code session and read `ListAgents` from another. Repeat with a hook-set `sessionTitle`. Record the result in `notes/naming-spike-2026-10-17.md` and choose mechanism 1, 2 or 3.
- (b) Two active same-machine worktree sessions find each other by project slug (mechanism 1 or 2) or by the documented lookup (mechanism 3).
- (c) A message between sessions in different permission modes is delivered without an approval dialog.
- (d) A session restarted under the same address receives a message sent to that address.
- (e) The check runs in under 2 s with no network calls.

Negatives:
- No settings scope contains a `permissions.deny` entry naming `SendMessage` or `ListAgents`.
- `isolatePeerMachines` is not set.
- No hook, skill or constraint text limits or batches messages.

#### Phase 1 — Guard the shared environment

**FR-01 — Live-build ancestry guard (shared).**
One shared function, used by every .NET App Service deploy path, runs before any upload:
- It downloads the app's main assembly from Kudu VFS, using the operator's existing management token, as `Deploy-BffApi.ps1:582-584` does.
- It reads ProductVersion and takes the SHA after `+`.
- It runs `git fetch origin`, then requires `git merge-base --is-ancestor <liveSha> <deploySha>`.

Acceptance:
- (a) Live SHA is an ancestor → proceed and log "includes live `<sha>`".
- (b) Live SHA is not an ancestor → refuse with a non-zero exit, naming the SHA, the branches that contain it (`git branch -r --contains`) and `-Supersede`.
- (c) Live SHA unknown to origin → refuse ("the live build is not on any pushed branch").
- (d) DLL unreadable, or no `+sha` → refuse unless `-Supersede`.
- (e) `-Supersede "<reason>"` with a non-empty reason → proceed and log the reason; an empty reason is rejected.
- (f) Deploy commit equals the live commit → proceed.
- Negative: the guard only reads; it never modifies the live app.

**FR-02 — Wire the guard into every App Service deploy path.**
`Deploy-BffApi.ps1`, `Deploy-WorkspaceBff.ps1`, `provisioning/Deploy-ControlPlane.ps1` and `deploy-bff-api.yml` call FR-01.

Acceptance:
- Each refuses a non-ancestor deploy against a stubbed live version.
- No `az webapp deploy` runs before the guard passes.
- A slot deploy checks the target slot. A swap checks production first.
- Negative: SWA and web-resource deploys aren't guarded, but they do log (FR-03).

**FR-03 — Dev environment log (pinned issue).**
One comment per deploy, app-setting write or role change made by a script. Each records: app, environment, slot, branch, deploy SHA, prior live SHA, actor (`git config user.name` / `GITHUB_ACTOR`), UTC time, supersede reason.

Acceptance:
- The comment body is a fenced key/value block that FR-18 can parse.
- A failed log write warns and the deploy still completes.
- Negative: no secret, token or setting value is logged; setting names only.

**FR-04 — Raw-command hook.**
A `PreToolUse` hook returns `ask` on `az webapp deploy`, `az webapp config appsettings set|delete` and `az role assignment create|delete` typed directly. Invoking the guarded scripts doesn't trigger it.

Acceptance:
- Under 5 s, no network calls.
- Must-fire fixtures: each raw form.
- Must-not-fire fixtures: the guarded scripts, `appsettings list`, `az webapp log tail`, the phrase inside an `echo` or a commit message.
- "Enforced by:" is recorded.

**FR-25 — Worktree write boundary (E4).**
Extend `scripts/quality/guard-worktree-links.py`. It returns `ask` on `Write`, `Edit`, and destructive Bash/PowerShell (`rm`, `Remove-Item`, `git checkout --`, `git clean`, `git reset --hard`) whose target resolves inside **another** registered worktree, the main checkout included. It keeps the hook's existing rules and its fail-open stance (an unparseable command is allowed).

Acceptance:
- Must-fire: `Write` into a sibling worktree; `rm -rf` of a path in a sibling; `git clean` / `git reset --hard` run against a sibling; `Edit` of a file in the main checkout from a worktree session.
- Must-not-fire: writes inside the session's own worktree; scratchpad, temp, `~/.claude/**`; short-path worktrees outside every registered worktree root; reads anywhere.
- Under 5 s. Controls live in `scripts/quality/tests/`.
- Record that the sandbox was evaluated and rejected: it doesn't run on native Windows and doesn't cover the file tools.

#### Phase 2 — Findings: decide and dispose

**FR-05 — `.claude/constraints/cross-session.md`.**
Exactly three sections. It contains **no** text on when, whether, how often or in what form to message another session. Claude Code's built-in judgment decides that.

**§A — Mechanics** (exact and complete; facts only, no advice):
- the address convention and the FR-00 mechanism chosen;
- the user settings JSON;
- the `-p` worker `--settings` value;
- the version floor;
- idle notices (`notify_when_idle`, main conversation only, local sessions only, 12 h lifetime);
- the inbox socket (`CLAUDE_CODE_MESSAGING_SOCKET`, plus the token line on native Windows);
- that WSL 2 and native Windows sessions can't reach each other;
- where each project's owned areas are recorded (README frontmatter, FR-13) and how to read them.

**§B — Record:**
- when a GitHub item is created *in addition to* a message (it must outlive the session, be acted on later, or need an owner decision);
- lifecycle meanings;
- the "how the detector reaches a project" table (design §5.3);
- the critical-area globs;
- the `ack` rule (FR-22).

**§C — Triage prompt:** design rev 3 §5.6.1.

On inbound limits, one line only: "Claude Code's own inbound limits apply (a received message can't approve, change configuration or run commands); the `SendMessage` tool's own instructions cover permission boundaries; see the cross-session messaging docs."

Root `CLAUDE.md` gets about 3 lines + one §17 row.

Acceptance:
- The constraint file is the source of truth for the prompt and may be revised at the phase 2 checkpoint.
- Root `CLAUDE.md` grows by no more than 5 lines net.
- Negatives:
  - Neither the constraint file nor any other file in `.claude/**` or root `CLAUDE.md` that this project edits states when, whether, how often or in what form to message another session.
  - No project `CLAUDE.md` gets new content.

**FR-06 — Triage at `task-execute` Step 9.5.**
Every finding is answered against the six-line prompt and mapped to one action:
- **Fix now:** in task, no approval. Applies to F1 when its correct fix is Small or Medium.
- **F1 with a Large correct fix:** owner, flagged urgent, in the same turn.
- **Fix now (tell):** new task + notice.
- **Ask owner:** 🔔 with value / cost / recommendation.
- **Record vs files:** issue per FR-09.
- **Note:** small-fixes list.
- **Other project:** issue for that project.
- **Close:** evidence per FR-10.

Acceptance:
- The task notes record the six answers and the decision.
- Negatives:
  - An F1 is never Note, Record or Close.
  - Scope grows only on Fix now, qualifying Fix now (tell), owner-approved items and F1.

**FR-07 — (L1) Rulings live on the issue.** A ruling or "not impacting" call about an issue is written as a comment on it and Triage Decision is set. This extends the `project-defer-issue-tracking` two-write rule. Acceptance: a ruling found only in notes is a code-review defect.

**FR-08 — (L2) Tracking issues are tasks.** `task-execute` Step 10 closes the task's tracking issue, with a link to the commit or PR. Triage skips tracking issues. Acceptance: a completed task's tracking issue is closed. Negative: referenced issues aren't closed.

**FR-09 — (L3, L4) Owner and area on every issue.** Filed issues carry the project label (the bare project name, created by `/project-pipeline` Step 4.2), Owning Project and Code Area (body + field). Acceptance: the filing skill checks all three after filing, and retries or reports.

**FR-10 — (L5) Verified closes.** A Close is applied only after verifying the evidence: the fix SHA is reachable from `origin/master`, or the duplicate is linked. The evidence is written in the closing comment. Negative: a recommendation alone is never evidence.

**FR-11 — Issues come back to the work.** `task-create` lists open issues whose Code Area matches the task's files as "fix it here if small". Acceptance: matching issues appear; tracking and closed issues are excluded.

**FR-12 — Wrap-up disposition.** `090` gives every owned issue one disposition (fixed with evidence / handed to a named project / closed with the owner's OK) and lists issues older than 60 days with no decision. Acceptance: 090 can't complete with an undisposed owned issue. The four Board #2 views exist (made by hand).

#### Phase 3 — Registry and detector (report-only)

**FR-13 — Registry frontmatter.**
Each project's `README.md` starts with YAML frontmatter: `lifecycle` (`active` | `paused` | `closed`), `owned_areas` (repo globs, may be empty), and, only if FR-00 selects mechanism 3, `worktree_folder`. Transitions:
- `project-pipeline` writes `active` at init;
- 090 writes `closed` and hands over owned areas (FR-23);
- the owner sets `paused`;
- `/devops-project-sync` mirrors lifecycle to Board #2 Status.

Bootstrap: a session drafts frontmatter for every active project, and the owner corrects it.

Acceptance:
- The detector and banner read frontmatter only.
- Every bootstrapped project parses with zero errors.
- Two active projects declaring the same glob are reported.
- Negatives: `projects/INDEX.md` isn't edited by this FR; a worktree existing on disk never changes lifecycle.

**FR-14 — Detector (deterministic, no AI).**
Signals:
1. Files changed by two active projects (required).
2. A change in another project's owned area (required).
3. Planned overlap from pending POML file lists — behind a flag, default **off**, enabled only by owner decision after the report-only period.

Sources: pushed `origin/work/*` branches, open PRs, committed POMLs, and sibling worktrees on disk when run locally. Each hit is labelled `remote` / `local`. The ignore list is seeded with `Program.cs`, `Directory.Packages.props` and lock files. Output is JSON plus markdown. The report also shows the count of open `coord` issues and marks it above 15 (configurable).

Acceptance:
- Identical inputs → byte-identical output (no timestamps in the JSON).
- Fixtures: must-fire for signals 1 and 2 (and 3 when the flag is on); must-not-fire for ignored files, paused/closed projects and a project overlapping itself.
- Negative: report-only — it opens no issues and fails no build in phase 3.

**FR-15 — Invocation.**
On demand, nightly CI (report artifact), `task-create` (`<coordination>` line on overlapping tasks) and pre-PR. `/design-to-spec` gains one checklist line, "List the active projects whose owned areas this design touches.", and doesn't run the detector.

Acceptance: a detector failure degrades to ⚠️ and never blocks the host skill.

#### Phase 4 — Records for sessions that aren't running

**FR-17 — Pre-code coordination issues.**
Opened only for overlaps found **before code exists** (signal 3 when on, or a recorded design overlap) that are in a critical area or need an agreement or owner decision.
- One issue per point, with a fixed key (sorted project pair + area).
- Labels: `coord` plus both project labels (`<a>`, `<b>`; the label is the bare project name, per `/project-pipeline` Step 4.2).
- Updated, never duplicated.
- Auto-closed with a reason when the overlap disappears.

Acceptance:
- A second run on unchanged state makes zero mutations.
- A resolved overlap is closed.
- Negatives: no issue for a non-critical, no-decision overlap; no `coord` issue for an overlap that already has an open PR (the PR carries `review:<owner>`, FR-21).

**FR-18 — `SessionStart` banner.**
This extends the `quality-alert-banner.py` background-cache pattern. For the session's project, identified as `reinject-project-state.ps1` does, it shows:
- open `coord` issues;
- open `review:<project>` PRs;
- open owner decisions the project raised;
- the last 5 dev-log entries.

Acceptance:
- It never waits on the network.
- It is silent when there is nothing to show.
- A session restarted after a `coord` issue was opened sees it at start.

**FR-18b — Live notice (optional, after FR-18 ships).**
When the background refresh finds a new `coord` issue or `review:<me>` PR, it posts one line to the running session's inbox socket. On native Windows this needs the `CLAUDE_CODE_MESSAGING_TOKEN` auth line first.

Acceptance:
- The running session shows the notice without a restart.
- No approval dialog appears (with FR-00's settings).
- Nothing is posted when nothing changed.

#### Phase 5 — Resolve records; critical areas

**FR-16 — `/conflict-check` reuses the detector.** After the report-only period and the owner's confirmation, `/conflict-check`'s file-overlap step calls the detector. The hot-path watchlist stays. Acceptance: one implementation of file-overlap detection exists.

**FR-20 — `task-execute` Step 0.**
Open coordination items on the task's files are resolved **on the item** before implementing: a comment, "no impact" with a reason, or awaiting the owner. Step 0 says nothing about messaging.

Acceptance:
- The task notes record how each item was resolved.
- Negatives:
  - Step 0 is the only place `task-execute` mentions coordination items.
  - No step makes a message conditional on an item.
  - A paused or closed project is never waited on.

**FR-21 — Step 9 diff check.** The detector re-runs on the actual diff. A PR touching another active project's owned area gets `review:<owner>`. Acceptance: an unplanned owned-area touch is labelled. Negative: the project's own areas aren't labelled.

**FR-22 — Critical-area acknowledgement in `Router`.**
`Router` (`.github/workflows/ci-router.yml`) fails a PR that touches a critical area (C-5) owned by **another active** project until the PR carries the `ack` label. The **owning project's session** applies `ack` after reviewing the change (C-10: autonomous). Elsewhere the check is advisory.

Acceptance (fixtures):
- an un-acked critical-area PR → fail;
- an acked one → pass;
- a non-critical owned area → advisory;
- a paused or closed owner → never blocks;
- a push that changes critical-area files after `ack` was applied removes the label (A-4).

Negatives: no ruleset change; Router's behaviour is unchanged for PRs with no critical-area touch; no comment parsing.

**FR-23 — Owned-area handover at close.** 090 hands each owned glob to a named active project or releases it, and closes or reassigns open `coord` issues. Acceptance: no `closed` project lists owned areas.

#### Deferred

**FR-24 — Owner's status digest.** Post-r1 and conditional: build only if Board #2 views plus the detector report prove insufficient. Rev-1 text: extend `/devops-portfolio-status --snapshot` with active-project state, coordination points, triage inbox count, ageing issues, tasks against plan and recent dev changes; read-only.

#### Cross-phase

**FR-26 — Messaging measurement.**
A report-only script in `scripts/quality/` counts, per day and per project, **cross-session** sends and inbound cross-session messages in local transcripts (`~/.claude/projects/**/*.jsonl`):
- sends are `SendMessage` results addressed to another Claude session, not subagents;
- inbound messages are `<cross-session-message>` blocks.

It also classifies each send's context as unprompted vs following a detector or banner item.

It runs:
- in phase 0 (baseline: the prior 14 days);
- 14 days after FR-00 is applied (the phase 0 checkpoint), adding a count of `[Cross-session delivery notice]` held, expired and refused results;
- 28 days after phase 2;
- after phase 5.

Acceptance:
- Deterministic output.
- No transcript content is copied into the report beyond counts and classifications.
- Negative: it changes no transcript or setting.

### Non-Functional Requirements

- **NFR-01 — No new blocking outside critical areas and deploys.** The only new blocking checks are FR-01 and FR-22. Everything else is advisory or degrades to ⚠️.
- **NFR-02 — Enforcement ladder (§16).** Every new guard (FR-00 check, FR-01, FR-04, FR-14, FR-22, FR-25) ships with must-fire and must-not-fire controls and records "Enforced by:". Hooks stay under 5 s, make no network calls, and use `ask` rather than `deny`.
- **NFR-03 — Idempotency.** The detector, coordination-issue sync and banner make zero mutations on a rerun against unchanged state.
- **NFR-04 — Deterministic.** The detector, guard and measurement script contain no model calls.
- **NFR-05 — Reversible by phase.** Each phase reverts by reverting its PRs, without breaking earlier phases.
- **NFR-06 — Small PRs on shared files**, after `/conflict-check`.
- **NFR-07 — Agent isolation (L8).** Read-only agents work in their own worktree. Model and effort are stated per `.claude/constraints/agent-cost.md`.
- **NFR-08 — No secret exposure** in the dev log, reports, banner or notices.
- **NFR-09 — Minimal structure.** Every artefact this project adds answers §11 Q3 with a concrete failure. The project adds no text about when or how to message.

## Technical Constraints

### Applicable ADRs and Standards

- **Root CLAUDE.md §16** (enforcement ladder), **§11** (component justification), **§3** (all `.claude/**` edits are main-session-only → those tasks are `parallel-safe:false`), **§9** (no secrets).
- **ADR-038 / `docs/standards/TEST-ARCHITECTURE.md`** — tests for scripts, hooks and the Router step, using the fixtures named in each FR.
- **ADR-028** — not touched. The Kudu read reuses the operator's existing management token.

### MUST Rules

- ✅ MUST prove FR-00 at the phase 0 checkpoint before phases 3–5 start.
- ✅ MUST read the live version from the DLL via Kudu. MUST NOT add a version to any anonymous endpoint.
- ✅ MUST guard every shared .NET App Service deploy path, `Deploy-WorkspaceBff.ps1` included.
- ✅ MUST use `ask`, not `deny`, for new hooks.
- ❌ MUST NOT add any text about when, whether, how often or in what form to message, including encouragement, examples or limits.
- ❌ MUST NOT deny `SendMessage` / `ListAgents` or set `isolatePeerMachines`.
- ❌ MUST NOT block on a paused or closed project.
- ❌ MUST NOT change the master ruleset.
- ❌ MUST NOT add content to project `CLAUDE.md` files.

### Existing Patterns to Follow

- Kudu VFS download: `scripts/Deploy-BffApi.ps1:582-599`.
- Background-cache `SessionStart` banner: `scripts/quality/quality-alert-banner.py`.
- Project identification: `.claude/hooks/reinject-project-state.ps1`.
- Narrow `PreToolUse` guard with controls: `scripts/quality/guard-worktree-links.py` + `scripts/quality/tests/test_guard_worktree_links.py`.
- Idempotent board sync: `.claude/skills/devops-project-sync/SKILL.md`.
- Issue open/auto-close from CI: `.github/actions/quality-alert/action.yml`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>       <!-- deploy-bff-api.yml (FR-02), nightly detector (FR-15), ci-router.yml (FR-22) -->
  <skill-directives>Y</skill-directives> <!-- cross-session.md; task-execute, task-create, project-pipeline, design-to-spec, conflict-check, devops-* -->
  <root-claude-md>Y</root-claude-md>   <!-- ~3 lines + one §17 row (FR-05) -->
</hot-path-declaration>
```

No BFF code is touched, so §10's publish-size rule doesn't apply. The nightly detector runs in GitHub Actions (ADR-052: CI-hosted).

### New Components (§11 three-question gate)

| New component | Existing overlap | Extend instead? | Cost of doing nothing |
|---|---|---|---|
| Constraint file `cross-session.md` | Step 9.5 finding classes; `project-defer-issue-tracking` | Extends both; holds the shared text they cite | Triage rules drift across skills; scope decided ad hoc (E6) |
| `SessionStart` messaging-config / address check | `quality-alert-banner.py`, `reinject-project-state.ps1` | Added to the existing start-up scripts | Held messages expire and addresses change on restart, so messages are lost (E2, K-6) |
| README registry frontmatter | INDEX.md hot-path columns | Per-project field in an existing file; no shared-file contention | Owners of an area can't be looked up (E5); paused/closed projects vanish from INDEX.md (K-4) |
| Deploy ancestry guard + dev-log helper | `Deploy-BffApi.ps1` Kudu hash check | Extends the Kudu read; shared because four paths need it (K-2) | A deploy removes another project's live fix (E1) |
| Dev environment log issue | None | — | Dev changes have no shared record (E3) |
| Raw-command hook | `guard-worktree-links.py` (other commands) | Same pattern; a separate matcher keeps one policy per hook | Raw `az webapp deploy` bypasses the guard |
| Worktree write boundary | `guard-worktree-links.py` | **Extended** | A session overwrites or deletes a peer's uncommitted work (E4) |
| Detector | `/conflict-check` | Neither reads owned areas or local worktrees; `/conflict-check` moves onto it (FR-16) | Overlaps with no live session found by luck (§1) |
| Coordination banner (+ FR-18b) | `quality-alert-banner.py` | Same pattern, a second source | A restarted session misses records made while it was down (E2) |
| Router acknowledgement step | `Router` | Folded in; no new required check | A PR merges into another project's auth or access code unseen (E5) |
| Messaging measurement script | None | — | Suppression of messaging by the written process would go unnoticed (objective 0) |
| Board #2 fields and views | Board #2 portfolio fields | Extends the board (fields exist since 2026-10-15) | Triage decisions have no shared queue (E6) |

Deferred and not built: the status digest (FR-24).

## ADR Tensions (per CLAUDE.md §6.5)

> No ADR tensions surfaced at design time. All listed ADRs and standards apply without exception. K-1 removes the one anonymous-endpoint change the design came close to.

Coordination dependencies (not tensions; agree before the dependent tasks merge):
- **D-1** — `ci-router.yml` (FR-22): Router's `docs_only` skip "stays with `ci-cd-unit-test-remediation-r1`". Agree the FR-22 change with that project, or with the owner if it is closed.
- **D-2** — rev 1's INDEX.md contract change is withdrawn (C-8), so there is no FR-C02 dependency.
- **D-3** — every active project uses the edited skills (NFR-06).

## Success Criteria

1. [ ] **The E1 replay is refused.** Verify: a guard test with a non-ancestor stub exits non-zero, naming the SHA; a live attempt from a branch lacking the live commit is refused.
2. [ ] **Every App Service deploy path is guarded.** Verify: no `az webapp deploy` in the four paths precedes the guard; a fixture per path.
3. [ ] **Shared deploys leave dev-log entries.** Verify: one parseable comment per test deploy type.
4. [ ] **Triage respects on-issue rulings.** Verify: pilot method on a project-labelled set; no "Fix now" contradicts an on-issue ruling; tracking issues skipped.
5. [ ] **Messaging is reliable.** Verify: FR-00 acceptance (b)–(d) on two live worktree sessions.
6. [ ] **A restarted session sees what it missed.** Verify: open a `coord` issue, start a session in that project, and the banner lists it without network wait.
7. [ ] **The critical-area gate blocks and unblocks autonomously.** Verify: un-acked fixture PR fails Router; the owning project's session applies `ack`; it passes; a later critical-area push removes `ack`.
8. [ ] **Noise stays bounded.** Verify: after the report-only period, open `coord` ≤ 15, or the report marks it.
9. [ ] **Close-out leaves nothing undisposed.** Verify: this project's 090 leaves zero undisposed owned issues and zero owned areas.
10. [ ] **Autonomous messaging doesn't decrease, and delivery improves.** Verify: FR-26 baseline vs the phase 0 checkpoint (no drop in sends; held/expired/misaddressed notices gone), then 28 days after phase 2 and after phase 5. A drop in cross-session sends, or a shift to detector-prompted messages only, is a defect against objective 0. It blocks the next phase until the wording that caused it is found and removed. Raw pre-script indicator, 14 days to 2026-10-17: 4,396 `SendMessage` calls (subagents included) and 3,767 inbound cross-session message blocks across 1,776 transcripts.
11. [ ] **The process stayed minimal.** Verify: at 090, every added artefact still has a §11 Q3 answer, and a grep of the files this project changed finds no wording about when or how to message.

## Dependencies

### Prerequisites

- Board #2 triage fields exist; the four views are made by hand before FR-12 is accepted.
- `/project-pipeline` Step 4.2 registration (PR #1983, merged).
- `gh` authenticated wherever the deploy scripts and the detector run.
- The `claude` on `PATH` updated to ≥ 2.1.236, or the npm global 2.1.6 removed (operator, FR-00).

### External

- Azure CLI management-token access to Kudu for the guarded App Services.
- D-1 agreed before FR-22 merges.

## Owner Clarifications

| # | Topic | Question | Answer | Impact |
|---|---|---|---|---|
| C-1 | Phase scope | Spec cover gated phases? | Spec and task all phases (2026-10-15); phase 6 now proposed deferred, see C-9 | Checkpoints between phases |
| C-2 | Registry | Where do lifecycle and owned areas live? | ~~INDEX.md columns~~ — **superseded by C-8** | — |
| C-3 | Deploy guard | Which paths? | All .NET App Service deploys; SWA and web resources log only | FR-01 – FR-03 |
| C-4 | Dev log | Issue or file? | Pinned GitHub issue | FR-03 |
| C-5 | Critical areas | Which? | Access control, Auth, Ownership + secure records, Dataverse write path | FR-05 §B, FR-17, FR-22 |
| C-6 | Gate wiring | How does the ack block? | Folded into `Router`; no ruleset change | FR-22 |
| C-7 | Detector source | Remote only, or local too? | Pushed branches, PRs, committed POMLs, plus local worktrees when run locally | FR-14 |
| C-8 | Registry storage (K-4) | Shared file or per project? | **README frontmatter per project** (2026-10-17) | FR-13; INDEX.md untouched |
| C-9 | Phase 6 | Defer the status digest? | **Recommended, to be confirmed** | FR-24 deferred |
| C-10 | Acknowledgement | Who applies `ack`? | **The owning project's session — autonomous** (2026-10-17) | FR-22 |
| C-11 | Session naming | Run the spike? | **Yes — run 2026-10-17**; results in `notes/naming-spike-2026-10-17.md`; interactive step pending (FR-00 a) | FR-00 |

## Assumptions

- **A-1 — Detector and checks in Python**, matching the existing hooks.
- **A-2 — Critical-area globs (initial; refined at task review):**
  - access control: `src/server/api/Sprk.Bff.Api/Services/Access/**`, `Infrastructure/ExternalAccess/**`, `Api/ExternalAccess/**`;
  - auth: `src/server/shared/Spaarke.Core/Auth/**`, `Infrastructure/Graph/GraphClientFactory.cs`, `.claude/adr/ADR-028*`, `.claude/constraints/auth.md`;
  - ownership and secure records: the ownership and secure-project code under `Services/Access/**` and the `sprk_externalrecordaccess` paths;
  - Dataverse write path: `Services/Dataverse/CoreAncestorResolver.cs` and the files named in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`.
- **A-3 — Unreadable live DLL fails closed**, overridable with `-Supersede`.
- **A-4 — A stale `ack` is removed** when a later push changes critical-area files. An autonomous acknowledgement must cover the code that merges.
- **A-5 — Ignore-list seed:** `Program.cs`, `Directory.Packages.props`, `package-lock.json`, `packages.lock.json`.
- **A-6 — Dev-log retention:** one pinned issue, not rotated in r1.

## Unresolved Questions

- [ ] **FR-00 (a): does the VS Code rename, or an interactive hook `sessionTitle`, become the messaging address?** Blocks only the choice between mechanisms 1, 2 and 3. It takes about 30 seconds by hand. The headless "hook title ≠ address" result may be a Claude Code defect; report it with `/feedback`.
- [ ] **D-1: Router change ownership.** Blocks the FR-22 merge only.
- [ ] **Dataverse role-privilege writes** have no single CLI signature, so FR-04 catches `az` role writes only; Dataverse role changes rely on FR-03 logging. Blocks nothing.
- [ ] **Owning Project text vs single-select.** Blocks nothing; affects FR-09 validation.

---

*AI-optimized specification. Source design: `design.md` rev 3 (rev 2 at `058df8f13`).*
