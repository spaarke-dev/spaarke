---
description: Record deferred work / issues uncovered during project execution to both project notes and GitHub Issues
tags: [tracking, deferral, issues, governance]
techStack: [all]
appliesTo: ["any project", "deferred work", "newly uncovered issue"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-26
---

# project-defer-issue-tracking

> **Category**: Governance
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: spaarke-redis-cache-remediation-r1 (created in response to repeated "buried in notes/r7-backlog.md" surfacing problem)
> **Exemplar rationale**: Deferrals are per-project ephemeral state; the procedure + GitHub-issue format is what's reusable.

---

## Purpose

Make deferred work and newly-discovered issues **visible to the whole team** instead of letting them accumulate inside one project's `notes/` folder. The current pain: scope items dropped from a project to keep it ship-able get filed in `notes/r7-backlog.md` (or equivalent) — discoverable only if you already know to look. Anyone working on a different project misses them.

This skill enforces a **two-write rule**:
1. **Project-local** entry in `projects/{name}/notes/defer-issues.md` (the source of truth — full context, links, traceability)
2. **GitHub Issue** on the portfolio board ([project #2](https://github.com/users/spaarke-dev/projects/2)) with a short title + body that links back to the project entry

Both writes happen in the same skill call. The GitHub Issue is the surface; the project file is the substance.

---

## When to Use

### Explicit triggers

| User says | Behavior |
|---|---|
| "defer this" / "defer X to R{N}" / "log a deferral" | Run the full skill — both writes |
| "file an issue for X" / "track this as an issue" | Run the full skill — both writes |
| `/project-defer-issue-tracking` (or `/defer`) | Interactive prompt |

### Auto-triggers (Claude should self-invoke)

| Condition | Action |
|---|---|
| Spec scope item dropped from current project to keep it ship-able | Defer entry |
| Hypothesis raised + ruled out of current scope | Defer entry |
| Production / dev bug discovered that's NOT this project's responsibility | Issue entry |
| Telemetry / monitoring gap surfaced that needs follow-up | Issue entry |
| Refactor opportunity > 2hr that's NOT part of current spec | Defer entry |
| Anti-pattern / failure mode encountered + worked around (not fixed) | Issue entry |

### Do NOT use for

- Things you'll do later **this turn** — that's a todo, not a deferral
- Things the spec explicitly covers — that's task slippage, fix the task
- Quick TODOs in code (`// TODO: handle null`) — use code comments
- Decisions already made — those go to `current-task.md` Decisions Made

---

## Inputs Required

| Input | Required | Default |
|---|---|---|
| `title` | Yes | — |
| `kind` | Yes | `defer` or `issue` |
| `urgency` | Yes | `now` / `next-round` / `someday` |
| `description` | Yes | What it is + why we found it |
| `source` | Yes | What turned it up — spec section, PR comment, telemetry query, code line, etc. |
| `entry-points` | Yes | File paths / line numbers / commands so someone else can pick it up cold |
| `suggested-fix` | No | Hypothesized approach if known |
| `estimated-effort` | No | Hours / days |
| `blockers` | No | Anything that must happen first |
| `related-adrs` | No | ADR pointers if relevant |
| `related-issues` | No | Existing GitHub issues to link |

When invoked without all required inputs, prompt the user once. Don't write half-formed entries.

---

## Workflow

### Step 1: Identify the active project

```
project_path = current working directory inside projects/{name}/
   OR git branch matches work/{project-name}
   OR ask: "Which project does this belong to? [list /projects subdirs]"

IF no active project:
  ASK: "This isn't inside a project. File against the repo root or a specific project?"
  IF repo-root:
    project_path = "_global"  // entries go to notes/global-defer-issues.md at repo root
```

### Step 2: Determine the next ID

```
READ projects/{name}/notes/defer-issues.md (create if absent — see template at references/defer-issues-template.md)

EXTRACT existing IDs (e.g., DEF-001, ISS-001 — sequential within kind)

next_defer_id  = max(existing defer IDs) + 1
next_issue_id  = max(existing issue IDs) + 1

ID format: `{KIND}-{NNN}` — e.g., DEF-007, ISS-012
```

### Step 3: Append entry to project notes

Use the template at [`references/defer-issues-template.md`](references/defer-issues-template.md). Entry format (one per item, append to the appropriate section):

```markdown
### {KIND}-{NNN} — {Title}

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | {now / next-round / someday} |
| **Filed** | {YYYY-MM-DD} |
| **Source** | {What surfaced it} |
| **GitHub Issue** | {filled in by Step 4} |

**Description**

{1-3 paragraphs. What it is. Why it matters. Concrete failure mode that proves it's real (NOT "for future flexibility" — see CLAUDE.md §11).}

**Entry-points**

- {file path : line, or command, or KQL query — paste-runnable}

**Suggested fix** (if known)

{1-2 sentences. Hypothesis only. Reviewer should validate.}

**Estimated effort**: {hours / days, or "unknown — needs spike"}
**Blockers**: {bullet list or "none"}
**Related**: {ADR pointers, related issue numbers, sister project links}

---
```

### Step 4: File the GitHub Issue

```bash
gh issue create \
  --repo {owner}/{repo} \
  --title "[{KIND}-{NNN}] {Title}" \
  --label "{kind},{urgency},{project-name}" \
  --project "{portfolio-board}" \
  --body "{see body template below}"
```

**Body template**:
```
Filed from project `{project-name}` ({KIND}-{NNN}). Full context: `projects/{project-name}/notes/defer-issues.md#{kind-anchor}`.

## What

{1-paragraph summary lifted from the entry's Description}

## Why

{1-sentence concrete failure mode}

## Entry-points

{copied from entry — paste-runnable file:line / commands}

## Suggested fix (if known)

{copied from entry, or "none yet — needs investigation"}

## Estimated effort

{copied}

## Blockers / related

{copied}

---

Source surfaced in: {link to PR / commit / spec section that turned this up}
```

After `gh issue create` returns, copy the issue URL back into the project entry's **GitHub Issue** row in the table.

### Step 5: Verify both writes

```
✅ Verify projects/{name}/notes/defer-issues.md has the new entry with non-empty GitHub Issue URL
✅ Verify gh issue view {N} shows the body
✅ Report:
   Filed: {KIND}-{NNN}
   Title: {Title}
   GitHub: {URL}
   Notes: projects/{name}/notes/defer-issues.md
```

### Step 6: Update related project files (if applicable)

- If the deferral relates to a specific R# planning round, add a one-line pointer to that project's `current-task.md` under "Implementation Notes" or "Decisions Made"
- If the entry implies a follow-up project, append to `projects/_global-backlog.md` (create if absent — single repo-wide rollup)

---

## Status lifecycle

Entries are mutable; track status in the entry table + reflect in GitHub Issue labels:

| Status | What it means | GitHub Issue state |
|---|---|---|
| **Open** | Filed, not started | open, no `wip` label |
| **In Progress** | Someone is working on it | open, `wip` label |
| **Done** | Shipped — link to PR / commit | closed |
| **Won't Fix** | Decided not to do — link to decision record | closed, `wontfix` label |
| **Superseded** | Replaced by another entry — link to successor | closed, `superseded` label |

When closing a GitHub Issue, also flip the project notes entry status. The two MUST stay in sync; if they drift, the project notes file is the source of truth and the issue should be updated.

---

## Integration with other skills

| Skill | Hook |
|---|---|
| `project-setup` (called by `project-pipeline`) | Generated CLAUDE.md MUST include a "Deferrals & Issues" section pointing operators at this skill — see template addition below |
| `project-pipeline` Step 2.5 (enrich plan.md) | Add a "Tracked deferrals/issues" subsection that includes `gh issue list` filtered by `{project-name}` label |
| `push-to-github` Step 1.6 (NEW) | Before push: scan `notes/defer-issues.md` for entries with empty GitHub Issue URL. If found, prompt: "You have N defer/issue entries without GitHub issues. File them now? [y/n]" |
| `merge-to-master` Step 0.5 | Scan all entries with status=Open across all merged projects; report rollup |
| `task-execute` Step 9.5 | If quality gate uncovers an issue the task can't fix, prompt: "File as ISS-{NNN}?" |

---

## Project CLAUDE.md template addition

Generated project CLAUDE.md files MUST include this section after "Implementation Notes":

```markdown
---

## Deferrals & Issues — tracking obligation (read this)

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:

1. **`notes/defer-issues.md`** — source of truth (full context, links, traceability)
2. **GitHub Issues** on the portfolio board (visibility — others can see + claim)

### When to file

| Situation | Use |
|---|---|
| Spec scope item dropped to keep this project shippable | DEF-{NNN} |
| Refactor / cleanup > 2hr that's not in current spec | DEF-{NNN} |
| Production / dev bug uncovered outside this project's responsibility | ISS-{NNN} |
| Telemetry / monitoring gap requiring follow-up | ISS-{NNN} |
| Failure mode discovered + worked around (not fixed) | ISS-{NNN} |

### How to file

Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes to BOTH places in one step.

NEVER add an entry only to `notes/defer-issues.md` and skip the GitHub Issue. The whole point of this protocol is visibility. The `push-to-github` skill scans for entries without GitHub URLs and blocks push until they're filed.

### Status

See `notes/defer-issues.md` for the current rollup. Use `gh issue list --label {project-name}` for the visible-to-team view.
```

---

## Error handling

| Situation | Response |
|---|---|
| GitHub CLI not authenticated | `gh auth login` and retry |
| Portfolio board project ID unknown | Fall back to `--project "Spaarke Portfolio"` (default), warn user |
| Entry would duplicate an existing entry (same title or anchor) | Show existing entry, ask: update existing or file new? |
| Project not in `projects/` (ad-hoc work) | Write to `notes/global-defer-issues.md` at repo root; GitHub Issue gets `global` label instead of project label |
| Description includes "for future flexibility" / "improve testability" / "separation of concerns" without concrete failure mode | Refuse the write. Push back: "Per CLAUDE.md §11, a deferral must name a concrete behavior or contract that fails without it. Can you provide one?" |

---

## Failure modes & recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Entry in notes file but no GitHub Issue | User skipped Step 4 OR network failed | `push-to-github` Step 1.6 scans for empty URLs and blocks push until fixed. |
| GitHub Issue exists but project notes don't | Manual issue created without this skill | Periodic audit via `gh issue list --label {project-name}` vs notes file. Reconcile by adding missing entries. |
| Entry status Open in notes but linked issue Closed | Status drift | Notes file is source of truth — re-open issue OR flip notes to Done with PR link. |
| Entries pile up "someday" forever | No grooming process | Quarterly review: any "someday" untouched >180 days → Won't Fix with rationale. |

---

## Related skills

- `project-setup` — generates project CLAUDE.md (now includes the Deferrals & Issues section above)
- `project-pipeline` — orchestrator; calls `project-setup`
- `push-to-github` — pre-flight check at Step 1.6
- `merge-to-master` — rollup at Step 0.5
- `code-review` — may surface issues that should be filed here

---

## Tips for AI

- **Default to ISS, not DEF, for issues uncovered in other systems** (sister projects, prod bugs, telemetry gaps). DEF is for spec scope this project chose to drop.
- **One concrete failure mode per entry** — if you can't name one, demote to a TODO comment in code, don't file an entry.
- **Entry-points must be paste-runnable** — file:line, KQL query, or `cd path && command`. Future-you should be able to start in 30s without re-investigating.
- **Don't proliferate Open entries**. If 5+ Open entries accumulate without progress, raise to user: "Backlog is growing — groom Now."
- **GitHub Issue title format is `[{KIND}-{NNN}] {Title}`** — the prefix lets GitHub search find them all.
- **When closing, ALWAYS update both places.** Notes file first (source of truth), then issue.
