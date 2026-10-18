# Design: spaarke-project-coordination-r1 (project coordination and finding triage)

> **Status:** design rev 3, 2026-10-17. `spec.md` governs where the two differ.
> **Origin:** drafted in the uac-r2 session as `projects/unified-access-control-r2/notes/proposal-cross-session-coordination.md` (rev 2), with owner direction given across that session. The uac-r2 pilot on 208 open issues ran on 2026-10-15; its results and lessons are in `notes/pilot-issue-triage-2026-10-15.md`.
> **Scope:** repo-wide process and tooling (skills, hooks, scripts, CI, Board #2). No BFF, Dataverse or product UI change.
> **Delivered early:** Board #2 triage fields (2026-10-15); PR #1983 makes GitHub registration a MUST in `/project-pipeline` (Step 4.2).

## Change log

**Rev 3 (2026-10-17).** Revised against owner objective 0 (below) after an external review (`revision-instructions-coordination-r1.md`), a check of the Claude Code documentation, and a naming spike (`notes/naming-spike-2026-10-17.md`). The guiding rule: *keep the process well structured but as minimal as necessary, and add nothing that constrains how sessions message each other.*

- Added objective 0 and §5.0 (messaging mechanics). Messaging is the primary live channel; this project fixes its configuration and adds no rule about when to message.
- No guidance text on messaging at all. The review's proposed permissive paragraph was dropped: out-of-the-box judgment works, and any guidance is a nudge (owner, 2026-10-17). Phase 0 is proved by measurement before phases 3–5 start.
- Rewrote the principles "a message is a doorbell" and "detection is a floor", which still framed messaging as conditional. Added "messaging is configured so it works".
- Corrected §3 against the docs. Hold expiry and inbound holds are settings, and addresses are names.
- Moved the "reaching another project" table to §5.3. It governs detector output only.
- Registry: lifecycle and owned areas live in each project README's frontmatter (owner decision, 2026-10-17), replacing the INDEX.md columns.
- Detector: kept signals 1 and 2; signal 3 (planned POML overlap) is off by default; removed design-prose parsing and the dev-log signal.
- An open PR is the coordination record for landed code. Coordination issues are opened for pre-code overlaps only.
- Acknowledgement is the `ack` label, applied by the owning project's session (autonomous; owner decision, 2026-10-17). Removed comment parsing and the 24 h escalation.
- Added §5.9, the worktree write boundary (E4), extending the existing `guard-worktree-links.py` hook.
- Phase 6 (status page) deferred.
- Corrected the deploy guard. It reads the live DLL's version through Kudu, not `/ping` (which returns `pong`).

Rev 2: `git show 058df8f13:projects/spaarke-project-coordination-r1/design.md`.

## 1. What we want

0. **Sessions message each other on their own, and nothing in this process reduces that.** Cross-session messaging (`SendMessage` / `ListAgents`) is the primary live coordination channel. Claude already decides on its own to message another session when something it learned, changed or decided affects that session's work. This project does three things:
   - (a) removes the configuration friction that makes those messages fail;
   - (b) gives Claude the lookup data it needs to know *whom* to tell;
   - (c) adds a durable record and a detector for cases where the other project has no session running.

   It adds no rule about *when* to message.
1. **Coordination between projects that happens systematically, not by luck.** Overlaps that no running session is positioned to notice are found by a process and recorded where the affected project will see them. The owner doesn't identify or orchestrate them.
2. **Continuous code-quality curation without scope creep.** Every finding is identified, defined and flagged, and nothing is passed off onto a list where it gets lost. But findings must not turn into endless new tasks (one project grew from about 75 to about 200 tasks).
3. **No added complexity, cost or risk to delivery.** Every change must be a clear improvement. Anything that adds friction without clear value is left out or kept advisory.

## 2. Principles

- **A message stands on its own; GitHub is the record for what must outlive the session.** Claude messages another session whenever it has something that session could use, with or without a GitHub item. A GitHub item is created in addition when the thing must survive a restart, be acted on later, or be decided by the owner.
- **The process never tells a session when to speak.** Sessions message each other on their own judgment. The detector, coordination items and the start-of-session banner exist for the project whose session isn't running. Nothing in a skill, hook or constraint file makes a message conditional on a detector hit, an open item or a numbered step.
- **Messaging is configured so it works.** Sessions have stable project addresses, inbound messages are accepted, held messages don't expire, and a minimum Claude Code version is pinned. A message that is held, dropped or misaddressed teaches Claude that messaging fails, and Claude then stops trying. Configuration is the first enforcement mechanism.
- **Coordinate with a project, not a session.** Durable coordination is addressed to a project, through its board item and labels. Whether the other session happens to be running doesn't matter.
- **Enforce with code, not prose**, following the root `CLAUDE.md` §16 enforcement ladder. Advisory first; a hard gate only where a miss costs a security defect or a live regression.
- **Minimal structure.** Specify mechanics fully, and write no guidance on when or how to message. Claude Code's built-in judgment already does this well, and every added sentence of guidance is a nudge. Reuse what exists: task files (POMLs, which already list exact files), the F/K finding classes, the §11 three questions, Board #2, the `devops-*` skills and the `SessionStart` hooks.

## 3. What Claude Code provides (checked against the docs, 2026-10-17)

| Feature | Fit |
|---|---|
| Cross-session messaging (`SendMessage` / `ListAgents`, `@name` mentions, idle notices) | **The primary live channel; Claude uses it unprompted.** It isn't durable: there is no queue or replay, so a message to a session that isn't running is lost. The 5-minute hold expiry and the approval hold for a receiver in a different permission mode are **settings** (`dialogExpiry`, `crossSessionInbound`), fixed in §5.0. Sessions are addressed by name. Idle notices (`notify_when_idle`, v2.1.236+) replace polling when one session waits on another. Hooks and scripts can post into a session's inbox socket (`CLAUDE_CODE_MESSAGING_SOCKET`; on native Windows they must authenticate with `CLAUDE_CODE_MESSAGING_TOKEN`). WSL 2 and native Windows sessions on one machine can't reach each other. |
| Agent teams (experimental) | Scoped to one session, so it can't coordinate independent projects. |
| Channels (research preview) | Push external events into a running session. Not adopted in r1: the inbox socket covers local hooks, and the banner covers restarts. |
| Remote Control, Agent view | Not session-to-session coordination. Agent view gives visibility only. |
| Hooks, permission rules, settings | **The enforcement tools we use.** |

There is no built-in cross-project coordinator. We build one from GitHub plus hooks, scripts and existing skills.

## 4. Evidence (2026-10-15)

| # | What happened | Root cause |
|---|---|---|
| E1 | R3's redeploy removed SPE admin's live fix #1910 from dev | Nothing checked, before deploying, that the live build was included in the new one |
| E2 | Three messages were lost: two because the recipient restarted, one held until it expired | Hold expiry left at its 5-minute default; addresses change when a session restarts (§5.0) |
| E3 | An app setting and a role privilege were changed on dev with no shared record | No dev change log |
| E4 | Another session deleted 70 files in a peer's worktree; another session's edits block the main checkout | The link-and-delete path is now guarded (`scripts/quality/guard-worktree-links.py`, 2026-10-13); plain writes into another worktree are not (§5.9) |
| E5 | PRs said "none of your files" but edited UAC's files | No ownership lookup |
| E6 | uac-r2 has 184 tasks, added month by month; about 121 issues mentioning uac-r2 are open and 32 closed | Findings become tasks (no scope decision), and filed issues have no disposition |

What worked and is kept:
- sessions messaging each other unprompted;
- commit-pinned reviews, with re-checks scoped to the fix diff;
- verdicts as PR comments;
- owner decisions routed through the owning project;
- R3's manual live-version check before deploying.

## 5. Design

### 5.0 Messaging mechanics

- **Every session's address identifies its project.** The default address is the working folder's name, truncated, plus a suffix that changes on every start, even on resume (spike). Truncation also makes two projects indistinguishable (`spaarke-wt-spaarkeai-assistant-enhanceme-*` is both r3 and r4). The address is set to the project slug:
  - **CLI sessions:** `--name <project>`. The spike shows this is stable across restarts.
  - **VS Code sessions:** the extension's rename. Whether a rename becomes the messaging address is verified first (spec FR-00). The spike showed that a `SessionStart` hook's `sessionTitle` sets the title but not the address.
- **Inbound policy (user settings; single-operator machine):**
  - `crossSessionInbound: "accept"` and `dialogExpiry: "never"`. Both are user-scope settings, so the repo can't set them. A `SessionStart` check warns when they are missing.
  - Any `-p` worker that could receive messages is started with `crossSessionInbound` set to `accept` in its `--settings` value.
  - `isolatePeerMachines` stays unset (no cross-machine sessions are in use).
- **Minimum version:** 2.1.236 (idle notices), and that applies to every `claude` the machine runs. The `claude` on `PATH` must meet it too.
- **Whom to tell:** each project's owned areas (§5.1) are lookup data. When Claude wants to know who is affected by a change under `Services/Access/**`, it reads the map and messages that project's session by name. The map answers "who", never "whether".
- **Waiting:** a session waiting on another local session's work asks for an idle notice (`notify_when_idle`) instead of polling.
- **Bursts and loops** are throttled by Claude Code itself. No instruction to batch or limit messages is written anywhere.

### 5.1 Project registry and status: the foundation

- **GitHub registration is a MUST at `/project-pipeline`** (owner, 2026-10-15; PR #1983). The pipeline doesn't finish until the project has a Project Issue on Board #2, the portfolio pointer block in `projects/{name}/README.md`, a project label named `<name>` (Step 4.2), and the Board #2 fields filled.
- **Registry frontmatter in each project's `README.md`** (owner, 2026-10-17):

  ```yaml
  ---
  lifecycle: active        # active | paused | closed
  owned_areas:
    - src/server/api/Sprk.Bff.Api/Services/Access/**
  ---
  ```

  - `project-pipeline` sets `active`.
  - The wrap-up task (`090`) sets `closed` and hands over owned areas.
  - The owner sets `paused` ("pause project X").
  - `/devops-project-sync` mirrors the lifecycle to Board #2 Status.
  - Each project owns its own entry, so there is no shared-file contention.
  - `projects/INDEX.md` is not used for this. A worktree existing on disk means nothing; only `lifecycle` counts.

### 5.2 Detect coordination points

A deterministic script (no AI) runs on demand, nightly, at `task-create` and at the pre-PR step. It reads pushed `origin/work/*` branches, open PRs, and, when run locally, sibling worktrees on disk.

| Signal | Source | Status |
|---|---|---|
| 1. The same files changed by two active projects | Changed files on active branches and open PRs | Required |
| 2. A change landing in another project's owned area | Owned areas (§5.1) | Required |
| 3. Planned overlap before code exists | File lists in pending POMLs across active projects | Off by default. Turned on by owner decision if the report-only period shows signal 1/2 hits that the pending POMLs would have predicted |

An ignore list covers files that always collide harmlessly, such as `Program.cs`, `Directory.Packages.props` and lock files. Git's merge check already handles real conflicts there.

`/design-to-spec` gains one checklist line: *list the active projects whose owned areas this design touches.*

### 5.3 Surface them

- Most overlaps go only into the detector report.
- **For landed code, the PR is the record.** It gets the `review:<owner>` label, plus the Router acknowledgement in critical areas (§5.4).
- **A coordination issue is opened only for pre-code overlaps** (planned work or designs) that are in a critical area or need an agreement or an owner decision. Rules:
  - One issue per point, with a fixed key (the two projects plus the area). The detector updates it and never duplicates it.
  - Labels: `coord` plus both project labels (`<a>`, `<b>`; the label is the bare project name, per `/project-pipeline` Step 4.2).
  - It is **closed automatically** when the overlap disappears.
- **How the detector reaches a project.** *This governs detector output only. A session messages any live session it chooses, regardless of this table.*

  | Other project is | Detector behaviour |
  |---|---|
  | Active | Record (PR label or coordination issue); shown at its next start |
  | Paused | Record only, never blocking |
  | Closed | Never contacted; its owned areas were handed over at close |

- **A `SessionStart` banner** shows each session the open records for its project from a background cache, using the same pattern as `quality-alert-banner.py`. It lists coordination issues, `review:<project>` PRs, owner decisions the project raised, and the last dev-log entries. A restarted session sees what it missed. Optionally, after the banner ships, the background refresh can post a one-line notice into the running session's own inbox socket when a new record appears, so a live session learns of it without a restart.
- The report shows the count of open `coord` issues and marks it when it passes 15.

### 5.4 Ensure records are resolved

- **`task-execute` Step 0:** open coordination points on the task's files are resolved on the item before implementing: a comment, "no impact" with a reason, or awaiting the owner. Messaging the other project's session is something the session does whenever it is useful, here and at every other point in the task. This step only makes sure the **item** isn't left open.
- **`task-execute` Step 9:** the detector re-runs on the actual diff, which catches touches that weren't planned. A PR touching another project's owned area gets the `review:<owner>` label.
- **CI:** in **critical areas only** (access control, auth, ownership and secure records, the Dataverse write path), `Router` fails until the PR carries the `ack` label. The **owning project's session** applies it after reviewing, so no owner orchestration is needed. Elsewhere this is advisory.
- **Never block on a paused or closed project.**

### 5.5 Guard the shared dev environment

- Every script that deploys to a shared .NET App Service checks the live build first: `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-WorkspaceBff.ps1` (the same `spaarke-bff-dev` app), `scripts/provisioning/Deploy-ControlPlane.ps1`, and the CI deploy workflow. The check works like this:
  - It reads the live DLL's ProductVersion (`1.0.0+<sha>`) through Kudu, as `Deploy-BffApi.ps1` already does for hash verification.
  - It refuses unless that commit is an ancestor of the commit being deployed.
  - `-Supersede "<reason>"` overrides and records the reason.
- Every deploy, app-setting change and role change on dev appends to a pinned **"Dev environment log"** issue.
- A `PreToolUse` hook prompts (`ask`) on a raw `az webapp deploy`, an app-settings write or a role write that bypasses the guarded scripts.

### 5.6 Findings: detect, decide, dispose

Finding things stays exactly as now: F/K classes, and every finding recorded. What changes is the **decision**, made with one standard prompt (§5.6.1) instead of a numeric task budget. The owner's direction (2026-10-15) was no fixed percentage limit; decide by value, fit and cost.

#### 5.6.1 The standard triage prompt

> **Triage this finding before acting on it.** Answer each item in one line.
>
> 1. **What breaks if we do nothing?** Name a concrete failure: who sees it, and when. "Cleaner" or "more flexible" is not an answer (§11 Q3).
> 2. **Class:** F1 (security, data loss, broken real path) / F2 (compounding maintainability) / F3 (missing test on important behaviour) / F4 (cosmetic).
> 3. **Fit with our solution posture:** aligned with our ADRs and the project's design, or does it need a design or ADR decision first?
> 4. **Cost and risk:**
>    - **Small:** fits the current task or one short task; touches files already in play; no schema, API-contract, auth-model or new-component change.
>    - **Medium:** a self-contained task.
>    - **Large:** multiple tasks, or it changes a contract, schema or the security model.
> 5. **Timing:** does it get more expensive or riskier if it waits? (now / can wait)
> 6. **Owner:** this task / this project / another project (named).
>
> **Decide:**
> - **Fix now (no approval):** F1 whose correct fix is Small or Medium, or Small + aligned + in this task's files.
> - **Ask the owner, flagged urgent, in the same turn:** F1 whose correct fix is Large.
> - **Fix now as a new task, telling the owner (not asking):** Small + aligned + a concrete failure, but outside this task's files.
> - **Ask the owner:** Medium or Large, needs a decision, or valuable without a concrete failure. Give one line each for value, cost and recommendation.
> - **Record against its files:** a concrete failure that can wait and isn't Small. An issue tied to its files (§5.6.2).
> - **Note:** F4 with no concrete failure. One line on the project's small-fixes list.
> - **Another project's area:** an issue for that project.
> - **Close (for existing issues):** already fixed, obsolete or a duplicate. Give the evidence.

> **How it gets fixed: the right fix, not the quick one.** This applies to every decision above, and to the cost in question 4.
> - **Fix the root cause.** A fix that silences a symptom, a test, a warning or a reviewer leaves the cause in place.
> - **Do it the technically correct, best-practice way.** Follow our ADRs, standards and the nearest good existing pattern. Never take the easiest or quickest route because it is easy or quick.
> - **Use the least code that is correct.** Prefer extending what exists (§11). Add no layer, abstraction, option, flag or special case unless it does something concrete for the solution. Complexity that adds no value is itself a defect.
> - **Cost means the cost of the correct fix.** A finding is "Small" only when the correct fix is small. When only a shortcut would be small, the finding is Medium or Large, and it goes to the owner rather than becoming a shortcut.

"Fix now, telling the owner" encodes the owner's usual answer to reasonable, small work. Scope only grows on items the owner actually approved, plus F1.

#### 5.6.2 Issues that come back to the work

- Every filed issue records its **owning project** (label + Owning Project field) and its **files or area** (body + Code Area field).
- Owner rulings about an issue are written **on the issue** (pilot L1).
- At `task-create`, open issues in files the task will touch surface in the task, as "fix it here if it's small". Most issues should get fixed this way, as a side effect of work already happening.
- At project close, the wrap-up task gives **every** issue the project owns a disposition: fixed / handed to a named successor / closed with the owner's OK.
- Task tracking issues are closed by `task-execute` when the task completes, and triage skips them (pilot L2).

### 5.7 The triage board (Board #2, "Spaarke Core")

Reuse the existing board. The fields were added on 2026-10-15:

| Field | Values |
|---|---|
| Finding Class | F1 / F2 / F3 / F4 |
| Fix Cost | Small / Medium / Large |
| Triage Recommendation | Fix now / Fix now (tell) / Ask owner / Record vs files / Note / Other project / Close |
| Triage Decision | Do now / Next / Later / Won't do |
| Code Area | free text (paths or area) |
| Triage Reason | free text |
| Owning Project | free text (replaces sub-issue parenting; pilot L7) |

| View | Filter | Purpose |
|---|---|---|
| Triage inbox | Recommendation set, Decision empty | What the owner reviews |
| By project | Grouped by Owning Project | Each project's queue |
| Ageing | Older than 60 days, no decision | Stale items |
| Do now | Decision = Do now | The work queue projects pick up |

Views are made once by hand: the API can't create them (pilot L6).

**Cadence:** a session pre-fills the recommendation with one-line reasons, and the owner confirms or overrides in bulk.

### 5.8 The owner's status page — deferred

Not in r1. It gets built only if Board #2 views plus the detector report prove insufficient after the report-only period. Reference text from rev 2: a read-only digest, generated on demand. It would show each active project's state, open coordination points, the triage inbox count, ageing issues, tasks against plan (visibility only) and the last dev changes. It would change nothing and approve nothing.

### 5.9 Worktree write boundary (E4)

Extend `scripts/quality/guard-worktree-links.py` (the existing `PreToolUse` hook for links and root deletes). It returns `ask` on these operations when their target resolves inside **another** registered worktree, the main checkout included:
- `Write` and `Edit`;
- destructive Bash/PowerShell (`rm`, `Remove-Item`, `git checkout --`, `git clean`, `git reset --hard`).

It allows:
- the session's own worktree;
- paths outside every worktree (scratchpad, temp, the short-path measurement worktrees sessions create, `~/.claude`);
- reads anywhere.

The Claude Code sandbox was evaluated and rejected: it doesn't run on native Windows and doesn't cover the file tools.

## 6. Walkthrough: one project from design to close

| Step (existing skill) | What's added |
|---|---|
| Throughout | Sessions message whoever their work affects, whenever they judge it useful. Nothing below changes that. |
| `/design-to-spec` | One checklist line: list the active projects whose owned areas this design touches. |
| `/project-pipeline` | **MUST** register on GitHub (§5.1). README frontmatter: `lifecycle: active`, owned areas declared. |
| `task-create` | The detector compares changed files and owned areas across active projects. Overlapping tasks get a `<coordination>` line. Open issues in the task's files surface (§5.6.2). |
| `task-execute` Step 0 | Open coordination items on the task's files are resolved (§5.4). |
| During the task | Every finding goes through the triage prompt (§5.6.1). |
| `task-execute` Step 9 | The detector runs on the actual diff; `review:<owner>` label where needed. |
| CI and merge | Critical areas only: `Router` requires the owning project's `ack`. |
| Deploy | The live-version check and the dev log (§5.5). |
| Wrap-up (`090`) | `lifecycle: closed`. Owned areas handed over. Every owned issue given a disposition. Coordination issues closed or reassigned. |

**Bootstrap for running projects:** add frontmatter to each active project's README (drafted by a session, corrected by the owner). Mark stopped projects paused or closed. Run the detector report-only.

## 7. Where the rules live

| Place | Content |
|---|---|
| Root `CLAUDE.md` | About 3 lines pointing to the constraint file, plus one §17 trigger row |
| `.claude/constraints/cross-session.md` (new) | §A mechanics (facts only, no advice on messaging); §B the record (when a GitHub item is also created, lifecycle meanings, critical-area globs, `ack` rule); §C the triage prompt |
| Skills (`design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, wrap-up) | The steps in §6 |
| Hooks and CI | Messaging-config and naming check, the start-up banner, the deploy guard, the write boundary, the critical-area acknowledgement |
| Project `CLAUDE.md` | **Nothing new.** Lifecycle and owned areas live in README frontmatter. |

## 8. Risks to current behaviour, and their guards

| Risk | Guard |
|---|---|
| **Written rules narrow messaging to the listed cases** | No text anywhere says when or how to message. No skill, hook or step makes a message conditional. Cross-session `SendMessage` volume is measured at baseline, at the phase 0 checkpoint, and after phases 2 and 5 (spec success criterion 10). |
| Waiting replaces progress | Only critical areas block, never on a paused or closed project |
| Over-structuring the process | Mechanics only; phase 6 deferred; signal 3 off by default; issues only for pre-code points |
| Issue sprawl | Report-first, PRs as the record for landed code, one issue per point, auto-close, ignore lists, the volume mark |
| Triage overhead | One prompt, answered in six lines; the owner sees only "ask owner" items plus a bulk inbox |

## 9. Rollout

| Phase | Deliverable | Checkpoint |
|---|---|---|
| 0 | Baseline messaging measurement, then messaging configuration (§5.0) alone. Already done: registration MUST, Board #2 fields | **After 14 days: sends didn't drop and delivery failures are gone?** Phases 3–5 wait for this; phases 1–2 don't touch messaging and may run meanwhile |
| 1 | Deploy guard, dev log, raw-command hook, worktree write boundary | — |
| 2 | Constraint file, triage at Step 9.5, issue hygiene (§5.6.2) | Messaging volume re-measured |
| 3 | Registry frontmatter; detector signals 1 and 2, **report-only** | After 1–2 weeks: is the signal worth surfacing? Turn on signal 3? |
| 4 | Coordination issues (pre-code) and the `SessionStart` banner | Proceed or stop |
| 5 | Step 0/9 checks; critical-area acknowledgement in Router; `/conflict-check` reuses the detector | Messaging volume re-measured |
| 6 | Owner's status page | **Deferred** |

Each phase is reversible and stops wherever the value stops.

## 10. Pilot: uac-r2's open issues (ran 2026-10-15)

Full results: `notes/pilot-issue-triage-2026-10-15.md`.

**Run:** 208 open issues were triaged by four Sonnet agents, taking about 8 minutes and about 470k tokens. All are on Board #2 with Class, Cost, Recommendation, Reason, Code Area and Owning Project filled in. Decisions wait for the owner.

| Recommendation | Count |
|---|---|
| Other project | 64 |
| Close | 50 |
| Ask owner | 33 |
| Record vs files | 20 |
| Fix now (tell) | 16 |
| Fix now | 15 |
| Note | 10 |

**Lessons that change the design:**
- **L1. Rulings kept only in notes are invisible to triage.** Decisions must be written on the issue itself (§5.6.2).
- **L2. Task tracking issues are not findings.** 55 of the 208 were tracking issues. `task-execute` closes a task's tracking issue when the task completes, and triage skips them.
- **L3. Triage needs the project label.** Only 54 of the 208 carried the uac-r2 label, so triage must use the label, which is now created at registration.
- **L4. Every issue needs an owning project.** 19 issues had no clear owner, so every filed issue names its owning project.
- **L5. Closes need their evidence checked.** Each Close needs its evidence verified before the issue is closed.
- **L6. Views are made by hand.** The GitHub API can't create Project views.
- **L7. Owning Project replaces sub-issue parenting.** An issue can have only one parent, so the Owning Project field is used instead.
- **L8. Read-only agents still changed the repo.** A "read-only" triage agent detached the worktree's HEAD. Agents that only read must work in their own worktree.

## 11. Decisions and open questions

**Decided (owner):**
- 2026-10-15:
  - Coordinate through GitHub, and use Board #2.
  - No fixed percentage task budget; triage by value, fit and cost with the standard prompt.
  - GitHub registration is a MUST at `/project-pipeline`.
  - uac-r2 is the first pilot.
- 2026-10-15 (`/design-to-spec` interview):
  - Spec and task all phases.
  - The deploy guard covers all .NET App Services.
  - The dev log is a pinned issue.
  - Critical areas: access control, auth, ownership and secure records, the Dataverse write path.
  - The acknowledgement is folded into Router.
  - The detector also reads local worktrees.
- 2026-10-17:
  - Messaging is the primary channel, and nothing constrains it (objective 0).
  - The owning project's session applies `ack` (autonomous).
  - The registry lives in README frontmatter.

**Open:**
1. Confirm deferring phase 6.
2. VS Code session naming: does the extension's rename become the messaging address? (spec FR-00, first task)

## 12. New components (CLAUDE.md §11 gate)

| Component | Existing overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `.claude/constraints/cross-session.md` | `task-execute` Step 9.5 finding triage; `project-defer-issue-tracking` | It extends them; the file holds the shared rules those skills cite | The rules stay scattered and drift; sessions decide scope ad hoc (E6) |
| Messaging-config and naming check (`SessionStart`) | `quality-alert-banner.py`, `reinject-project-state.ps1` | Added to the existing start-up hook scripts | Held and expired messages, plus addresses that change on restart, lose messages (E2) |
| README registry frontmatter | `projects/INDEX.md` hot-path columns | INDEX.md is one shared, prose-heavy file; per-project frontmatter has no contention | No machine-readable owner per area; ownership can't be looked up (E5) |
| Coordination detector script | `/conflict-check` (open-PR overlap) | Neither reads owned areas or the local worktrees; `/conflict-check` is moved onto it in phase 5 | Overlaps keep being found by luck (§1) |
| `SessionStart` coordination banner | `quality-alert-banner.py` | Same pattern, a second data source | A restarted session misses records made while it was down (E2) |
| Deploy ancestry guard | `Deploy-BffApi.ps1` Kudu hash check; R3's manual check | It extends the Kudu read and is shared by four deploy paths | A deploy can remove another project's live fix (E1) |
| "Dev environment log" issue | None | — | Dev changes have no shared record (E3) |
| Worktree write boundary | `guard-worktree-links.py` | Extended, not duplicated | A session can still overwrite or delete a peer's uncommitted work (E4) |
| Cross-session messaging measurement script | None | — | Nobody would notice if the written process suppressed messaging (§8 first risk) |
| Board #2 triage fields and views | Board #2 portfolio fields | They extend the existing board | Triage decisions have no shared queue (E6) |

## 13. Placement and hot paths

- **BFF and `src/solutions/SpaarkeAi/**`:** not touched. There is no BFF publish-size impact and no BFF placement section is needed.
- **Shared files:**
  - `.claude/skills/{project-pipeline,task-create,task-execute,design-to-spec,conflict-check,devops-project-sync}/SKILL.md`
  - `.claude/settings.json` (hooks)
  - `scripts/Deploy-BffApi.ps1`, `scripts/Deploy-WorkspaceBff.ps1`, `scripts/provisioning/Deploy-ControlPlane.ps1`, `.github/workflows/deploy-bff-api.yml`
  - `.github/workflows/ci-router.yml` (phase 5)
  - `scripts/quality/guard-worktree-links.py`
- **Coordination:** every active project uses these files, so changes land as small PRs.

## 14. ADR tensions

None known. CLAUDE.md §16 (the enforcement ladder) is followed. ADR-028 is not touched: the deploy guard reads the live DLL through Kudu with the operator's existing management token, as `Deploy-BffApi.ps1` already does. No anonymous endpoint is changed.
