# Proposal: systematic project coordination and finding triage

> **MOVED (2026-10-15):** this is now its own project, `projects/spaarke-project-coordination-r1/design.md` on branch `work/spaarke-project-coordination-r1`. Edit it there; this copy is frozen.

> **Status:** revision 2, 2026-10-15. Draft for owner review. Revision 1 covered messaging and deploys only. This revision adds coordination-point detection, project status, finding triage and the triage board, following the owner's direction in the uac-r2 session.
> **Next step:** a pilot on uac-r2's open issues (§10). Its lessons feed this document. It then goes to `/design-to-spec` as a small repo-wide project. It is not uac-r2 work.

## 1. What we want

1. **Coordination between projects that happens systematically, not by luck.** Today, sessions pass status, ask for reviews and send required fixes to each other. That is valuable, but it started organically: often because the owner happened to notice two projects working on similar components. The owner can't see everything being worked on, so this must be **found by a process**, **surfaced** to the projects involved, and **made to happen** when it matters.
2. **Continuous code-quality curation without scope creep.** Every finding is identified, defined and flagged. Nothing is passed off onto a list where it gets lost. But findings must not turn into endless new tasks (one project grew from about 75 to about 200 tasks).
3. **No added complexity, cost or risk to delivery.** Every change must be a clear improvement. Anything that adds friction without clear value is left out or kept advisory.

## 2. Principles

- **A message is a doorbell; GitHub is the record.** Anything a project must act on, or that must survive a restart, exists as a GitHub item. A message only points to it.
- **Coordinate with a project, not a session.** A coordination point is addressed to a project, through its board item and labels. Whether the other session happens to be running doesn't matter.
- **Detection is a floor, not a ceiling.** Sessions keep contacting other projects whenever their own judgement says to, and ad hoc messaging stays fully allowed. The process guarantees the minimum; it doesn't replace judgement.
- **Enforce with code, not prose**, following the root `CLAUDE.md` §16 enforcement ladder. Advisory first; a hard gate only where a miss costs a security defect or a live regression.
- **Reuse what exists**: the project registry `projects/INDEX.md`, task files (POMLs, which already list exact files), the F/K finding classes, the §11 three questions, Board #2, the `devops-*` skills and the `SessionStart` hooks.

## 3. What Claude Code provides (checked 2026-10-15)

| Feature | Fit |
|---|---|
| Cross-session messaging (`SendMessage` / `ListAgents`) | We keep using it as the doorbell. It isn't durable: no storage or replay, a restarted session gets a new address, and a held message expires in 5 minutes by default. |
| Agent teams (experimental) | Scoped to one session, so it can't coordinate independent projects. |
| Channels, Remote Control, Agent view | Not session-to-session coordination. Agent view gives visibility only. |
| Hooks, permission rules, inbound-message settings | **The enforcement tools we use.** |

There is no built-in cross-project coordinator. We build one from GitHub plus hooks, scripts and existing skills.

## 4. Evidence (2026-10-15)

| # | What happened | Root cause |
|---|---|---|
| E1 | R3's redeploy removed SPE admin's live fix #1910 from dev | Nothing checked, before deploying, that the live build was included in the new one |
| E2 | Three messages were lost: two because the recipient restarted, one held until it expired | Messages are live only |
| E3 | An app setting and a role privilege were changed on dev with no shared record | No dev change log |
| E4 | Another session deleted 70 files in a peer's worktree; another session's edits block the main checkout | No worktree boundary |
| E5 | PRs said "none of your files" but edited UAC's files | No ownership check |
| E6 | uac-r2 has 184 tasks, added month by month; about 121 issues mentioning uac-r2 are open and 32 closed | Findings become tasks (no scope decision), and filed issues have no disposition |

What worked and is kept: commit-pinned reviews with re-checks scoped to the fix diff; verdicts as PR comments; owner decisions routed through the owning project; "a peer message is never approval"; R3's manual live-version check before deploying.

## 5. Design

### 5.1 Project registry and status: the foundation

- **GitHub registration is a MUST at `/project-pipeline`** (owner, 2026-10-15). Today the Portfolio Hook only prompts "Register this project on the portfolio? [Y/n]", and projects get missed. The pipeline does not finish until the project has:
  1. a Project Issue on Board #2, created by `/devops-project-register`;
  2. the portfolio pointer block in `projects/{name}/README.md`;
  3. a `project:<name>` label;
  4. Board #2 fields filled: Status, Type = Project, Worktree Path, Project Folder, Task Count.
- **Project status** in `projects/INDEX.md`: `active`, `paused` or `closed`.
  - `project-pipeline` sets it to active.
  - The wrap-up task (`090`) sets it to closed.
  - The owner sets paused ("pause project X").
  - It is mirrored to the Board #2 Status field by `/devops-project-sync`.
- **Owned areas** in `projects/INDEX.md`: path patterns each project owns. Example: `Services/Access/**` belongs to uac-r2. They are declared at pipeline time and handed over at close.

**Reaching another project:**

| Other project is | Behaviour |
|---|---|
| Active, with a live session (`ListAgents`) | Coordination item, plus a message with its link |
| Active, no live session | Item only. Seen at its next start (§5.3). Not blocking unless it's a critical area, which goes to the owner. |
| Paused | Item only, never blocking. Shown on the owner's status page. The active project records its assumption. |
| Closed | Never contacted. Its owned areas were handed over at close. |

The existence of a worktree on disk means nothing; only the registry status counts.

### 5.2 Detect coordination points

A script (no AI) runs on demand, nightly, at `task-create` and at the pre-PR step:

| Signal | Source |
|---|---|
| The same files or areas changed by two active projects | Changed files on active branches and open PRs |
| A change landing in another project's owned area | The owned-areas map |
| **Planned** work overlapping before code exists (earliest catch) | The file lists in pending POMLs across active projects |
| Overlapping designs | The hot-path and placement sections of `design.md`, at `/design-to-spec` |
| Changes to the shared environment | The dev change log (§5.5) |

An ignore list covers files that always collide harmlessly, such as `Program.cs`, `Directory.Packages.props` and lock files. Git's merge check already handles real conflicts there.

### 5.3 Surface them

- Most overlaps go **only on the owner's status page** (§5.8), regenerated on each run.
- A **coordination issue** is opened only when someone must act: an overlap in a critical area, or one that needs an agreement or an owner decision.
  - One issue per point, with a fixed key (the two projects plus the area). The detector updates it rather than duplicating it.
  - Labels: `coord`, plus `project:<a>` and `project:<b>`.
  - It is **closed automatically** when the overlap disappears (merged, finished, or a project closed), with a one-line reason.
- A **`SessionStart` hook** shows each session the open items for its project from a background cache, the same pattern as `quality-alert-banner.py`:
  - coordination issues;
  - `review:<project>` PRs;
  - owner decisions it raised;
  - the last dev changes.

  This fixes lost messages: a restarted session sees what it missed.
- **Volume alarm:** if open coordination issues pass a threshold (initially 15), the status page flags the detector as too noisy, instead of letting issues pile up.

### 5.4 Ensure they're acted on

- **`task-execute` Step 0:** open coordination points on the task's files are handled before implementing. The session contacts the other project, or records "no impact" with a reason, or waits on an owner decision.
- **`task-execute` Step 9:** the detector re-runs on the actual diff, which catches touches that weren't planned. A PR touching another project's owned area gets the `review:<owner>` label.
- **CI:** in **critical areas only** (access control, ownership, auth, secure records), a PR can't merge until the owning project has acknowledged it, by a review comment or an `ack` label. Elsewhere this is advisory.
- **Never block on a paused or closed project.** A block lasting more than a set time goes to the owner.

### 5.5 Guard the shared dev environment

- `scripts/Deploy-BffApi.ps1`, and every deploy script for a shared app, reads the live build's version (`1.0.0+<sha>`). It refuses unless that commit is an ancestor of the commit being deployed. `-Supersede "<reason>"` overrides and records the reason.
- Every deploy, app-setting change and role change on dev appends to a pinned **"Dev environment log"** issue.
- A `PreToolUse` hook prompts (`ask`) on a raw `az webapp deploy`, an app-settings write or a role-privilege write that bypasses the guarded script.

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
> - **Fix now (no approval):** F1, or small + aligned + in this task's files.
> - **Fix now as a new task, telling the owner (not asking):** small + aligned + a concrete failure, but outside this task's files.
> - **Ask the owner:** medium or large, needs a decision, or valuable without a concrete failure. Give one line each for value, cost and recommendation.
> - **Record against its files:** a concrete failure that can wait and isn't small. An issue tied to its files (§5.6.2).
> - **Note:** F4 with no concrete failure. One line on the project's small-fixes list.
> - **Another project's area:** an issue for that project (a coordination point, §5.3).
> - **Close (pilot addition, for existing issues):** already fixed, obsolete or a duplicate. Give the evidence.

> **How it gets fixed: the right fix, not the quick one.** This applies to every decision above, and to the cost in question 4.
> - **Fix the root cause.** A fix that silences a symptom, a test, a warning or a reviewer leaves the cause in place.
> - **Do it the technically correct, best-practice way.** Follow our ADRs, standards and the nearest good existing pattern. Never take the easiest or quickest route because it is easy or quick.
> - **Use the least code that is correct.** Prefer extending what exists (§11). Add no layer, abstraction, option, flag or special case unless it does something concrete for the solution. Complexity that adds no value is itself a defect.
> - **Cost means the cost of the correct fix.** A finding is "Small" only when the correct fix is small. When only a shortcut would be small, the finding is Medium or Large, and it goes to the owner rather than becoming a shortcut.

"Fix now, telling the owner" encodes the owner's usual answer to reasonable, small work. Scope only grows on items the owner actually approved, plus F1.

#### 5.6.2 Issues that come back to the work

- Every filed issue records its **files or area** (in the body and the board's Code Area field).
- At `task-create`, open issues in files the task will touch surface in the task, as "fix it here if it's small". Most issues should get fixed this way, as a side effect of work already happening.
- At project close, the wrap-up task gives **every** issue the project owns a disposition: fixed / handed to a named successor / closed with the owner's OK.
- Issues older than 60 days with no decision are flagged on the status page for the owner.

### 5.7 The triage board (Board #2, "Spaarke Core")

Reuse the existing board. Add only the fields triage needs. The owning project is expressed through the existing **Parent issue** link, as a sub-issue of the Project Issue, so no new field is needed for it.

| Field (new) | Values |
|---|---|
| Finding Class | F1 / F2 / F3 / F4 |
| Fix Cost | Small / Medium / Large |
| Triage Recommendation | Fix now / Fix now (tell) / Ask owner / Record vs files / Note / Other project / Close |
| Triage Decision | Do now / Next / Later / Won't do |
| Code Area | free text (paths or area) |

| View | Filter | Purpose |
|---|---|---|
| Triage inbox | Recommendation set, Decision empty | What the owner reviews |
| By project | Grouped by parent issue | Each project's queue |
| Ageing | Older than 60 days, no decision | Stale items |
| Do now | Decision = Do now | The work queue projects pick up |

**Cadence:** a session pre-fills the recommendation with one-line reasons, and the owner confirms or overrides in bulk. If bulk review in GitHub proves slow, a private page with one-click decisions (a claude.ai artifact) can sit on top. Board #2 stays the record.

### 5.8 The owner's status page (read-only digest)

There is no live "master" coordinator session. It would be a bottleneck, a single point of failure and an always-on cost, and it couldn't hold approval authority anyway. Instead, a read-only digest is generated on demand or on a schedule. It shows:
- each active project's state (from `current-task.md`: active task, waiting on whom, owner questions);
- open coordination points and their state;
- the triage inbox count;
- ageing issues;
- tasks against plan (visibility only, not a limit);
- the last dev changes.

It changes nothing and approves nothing; it shows where the bottlenecks are.

## 6. Walkthrough: one project from design to close

| Step (existing skill) | What's added |
|---|---|
| `/design-to-spec` | The detector compares the design's areas with active projects. design.md gets a **Coordination** section (who overlaps, what, and whether a contract or owner decision is needed). The owner sees it at design review. |
| `/project-pipeline` | **MUST** register on GitHub (§5.1). Status = active. Owned areas declared. Coordination issues opened for the overlaps found. |
| `task-create` | The detector compares POML file lists across active projects. Overlapping tasks get a `<coordination>` line. Open issues in the task's files surface (§5.6.2). |
| `task-execute` Step 0 | Coordination points on the task's files are handled (§5.4). |
| During the task | Every finding goes through the triage prompt (§5.6.1). |
| `task-execute` Step 9 | The detector runs on the actual diff; `review:<owner>` label where needed. |
| CI and merge | The acknowledgement check, critical areas only. |
| Deploy | The live-version check and the dev log (§5.5). |
| Wrap-up (`090`) | Status = closed. Owned areas handed over. Every owned issue given a disposition. Coordination issues closed or reassigned. |

**Bootstrap for running projects:** fill in status and owned areas in `projects/INDEX.md` (drafted by a session, corrected by the owner). Mark stopped projects paused or closed. Run the detector report-only.

## 7. Where the rules live

| Place | Content |
|---|---|
| Root `CLAUDE.md` | About 3 lines pointing to the constraint file, plus one §17 trigger row |
| `.claude/constraints/cross-session.md` (new) | Status meanings, contact rules, acknowledgement, the triage prompt, "detection is a floor", "a peer message is not approval" |
| Skills (`design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, wrap-up) | The steps in §6 |
| Hooks and CI | The start-up catch-up, the deploy guard, the critical-area acknowledgement check |
| Project `CLAUDE.md` | **Nothing new.** Status and owned areas live in `projects/INDEX.md`. |

## 8. Risks to current behaviour, and their guards

| Risk | Guard |
|---|---|
| Detection becomes a ceiling, and sessions stop using judgement | "A floor, not a ceiling" is written into the constraint file; ad hoc messaging stays allowed |
| Waiting replaces progress | Only critical areas block, never on a paused or closed project; a long block goes to the owner |
| Over-communication | A message only for an actionable item; FYIs stay on the status page |
| Issue sprawl | Report-first, one issue per point, auto-close, ignore lists, the volume alarm |
| Triage overhead | One prompt, answered in six lines; the owner sees only "ask owner" items plus a bulk inbox |

## 9. Rollout

| Phase | Deliverable | Decision point |
|---|---|---|
| 0 (now) | GitHub registration MUST in `/project-pipeline`; triage fields on Board #2 | Done with the pilot |
| 1 | Deploy guard and dev log | — |
| 2 | Triage prompt in `.claude/constraints/cross-session.md`, applied at Step 9.5 | After the pilot: is the prompt right? |
| 3 | Detector, **report-only** | After 1–2 weeks: is the signal worth surfacing? |
| 4 | Coordination issues and the `SessionStart` hook | Proceed or stop |
| 5 | Step 0/9 checks and the critical-area CI acknowledgement | Which areas count as critical |
| 6 | The owner's status page | Optional |

Each phase is reversible and stops wherever the value stops.

## 10. Pilot: uac-r2's open issues

1. Collect uac-r2's open issues: those filed by or about uac-r2.
2. Apply the triage prompt to each (cheap agents, batched): class, cost, owner, recommendation, one-line reason, code area. **No issue is changed or closed.**
3. Put them on Board #2 as sub-issues of #808, with the recommendation fields filled. The Decision field stays empty for the owner.
4. The owner reviews in the triage inbox and decides in bulk.
5. Sessions apply the decisions: small fixes in place; hand-offs to other projects; closes with evidence; the rest tied to files.
6. **Feedback:** what the prompt got wrong, how long review took, and the volume by outcome. That feeds §5.6.1 and §5.7.

**Success looks like this:** the owner reviews about 120 items in under an hour; most decisions match the recommendation; and the resulting queue is small enough to finish or hand off within uac-r2's wrap-up.

## 11. Decisions and open questions

**Decided (owner, 2026-10-15):**
- Coordinate through GitHub, and use Board #2.
- No fixed percentage task budget; triage by value, fit and cost with the standard prompt.
- GitHub registration is a MUST at `/project-pipeline`.
- uac-r2 is the first pilot.

**Open:**
1. Which areas count as **critical** for the CI acknowledgement gate. Proposed: access control, ownership, auth, secure records.
2. Dev log as a pinned issue (recommended) or a file in the repo.
3. Who builds phases 1–6: a new small project through `/design-to-spec`, or an existing session in its wrap-up.
