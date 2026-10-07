# Template: Project-Scoped CLAUDE.md

> **Parent skill**: [project-setup](../SKILL.md)
> **Purpose**: The scaffold for `projects/{name}/CLAUDE.md`. Copy the template body, replace `{placeholders}`, and keep the guidance below in mind as the project runs.
> **Rewritten 2026-10-07** (claude-md-cleanup-r1). The previous template copied ~5 KB of repo-wide rules (task-execute trigger table, multi-file decomposition, execution model) into every project. Those live in root `CLAUDE.md` and the skills, which are always available, so the copies were removed.

---

## Guidance: what a project CLAUDE.md is for

A project's `CLAUDE.md` is its **operating manual**. Every session working in the project reads it on every task.

**The test of a good one:** a session that reads only this file plus `current-task.md` can act correctly in the project without asking:
- what is in and out of scope;
- which rules bind here;
- what the owner has decided;
- who else shares the code;
- what must not be touched without approval;
- which traps to avoid.

**Effectiveness comes first; size second.** The file loads only within its project, so it can carry more than root `CLAUDE.md`. It must not become a history log, because history buries the rules a session needs.

**What does not belong here, and where it goes instead:**

| Content | Goes to |
|---|---|
| Session history, "what landed", completed-task narratives | Git commit messages; task notes |
| Status counts, branch heads, SHAs, "N of M done" | `current-task.md`, `tasks/TASK-INDEX.md`, git |
| Full decision rationale and debate | `notes/decisions.md` (this file keeps the one-line binding outcome + link) |
| Superseded or withdrawn rules | `notes/decisions.md`, marked superseded with the date |
| Repo-wide rules | A pointer to the root `CLAUDE.md` section or the constraint file (don't copy) |
| ADR text | A link to the ADR, plus this project's tension or exception if any |
| A lesson that applies beyond this project | Promote it to `.claude/FAILURE-MODES.md` or the owning constraint; leave a one-line pointer |

**Size.** Aim for ≤ 15–20 KB; past ~25 KB, prune. This is a signal, not a cap: a project with many genuine binding rules can be larger. The usual bloat is a decisions table that has turned into a log, standing items nobody re-checked, and sections that read as history.

**Keeping it effective:**
1. **Add** an entry when a directive, decision or gotcha will outlive the current task. Make it dated, one line, and put it in the right section. Never put it in `current-task.md`, which is rewritten every checkpoint.
2. **Prune at every phase boundary**, or about every 10 tasks:
   - re-verify each standing item against code and live state (code wins);
   - move superseded items to `notes/decisions.md`;
   - fix contradictions immediately.
3. **When a rule changes, update it here in the same commit**, and mark the old version withdrawn in `notes/decisions.md`. A stale rule in a project file keeps steering sessions after the repo has moved on.
4. **Promote** a rule that turns out to be repo-wide, and replace it here with a pointer.
5. **At project close** (090 wrap-up), promote durable lessons to repo docs. The file stays as the project's record.

---

## Template content

```markdown
# {Project Name} — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

{One paragraph: what this project delivers, and what is explicitly out of scope.}

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

<!-- MUST / MUST NOT rules from spec.md, one line each, citing the FR/NFR id. -->
- {MUST … (FR-NN)}
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5):
- {ADR-NNN — path A/B — one line — link to the spec/design section}

## 3. Owner directives and standing decisions

<!-- Only decisions that still constrain current work. One dated line each, linking to its rationale in notes/decisions.md. -->
- {YYYY-MM-DD — decision — see notes/decisions.md#anchor}

## 4. Coordination

<!-- Other projects or worktrees sharing files or contracts; who owns what; hand-offs owed or expected. -->
- {project — shared files/contracts — who owns what}

## 5. Environment and live actions

<!-- Environments used; what needs explicit owner OK; deploy path; resources to keep or never delete. -->
- {…}

## 6. Gotchas — do not re-learn

<!-- Traps that cost time, each with its fix. One dated line each. -->
- {YYYY-MM-DD — trap — fix}

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md`
- Applicable ADRs: {list}
- Related projects: {list}
```
