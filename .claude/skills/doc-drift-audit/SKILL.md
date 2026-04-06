# doc-drift-audit

---
description: Detect documentation drift — stale references in docs and .claude/ that no longer match current code. Compact diff-based alternative to full-repo audits.
tags: [audit, documentation, drift-detection, quality]
techStack: [all]
appliesTo: ["audit doc drift", "check doc accuracy", "verify documentation", "project transition audit", "find stale references"]
alwaysApply: false
---

> **Category**: Quality
> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Created in**: R2 lessons-learned (after finding ~40 drift bugs via full-repo audit)

---

## Purpose

Detect documentation drift — stale file paths, deleted class references, broken cross-links, and outdated patterns in `docs/` and `.claude/` subdirectories. Provides a **compact, diff-based audit** that runs in minutes rather than hours.

## When to Use

### Explicit Triggers
- User says "audit doc drift", "check for stale docs", "project transition audit"
- Starting a new project iteration (r2→r3, r3→r4, etc.)
- Before merging a long-running branch to master
- Quarterly maintenance (recommended cadence)
- Explicitly invoked with `/doc-drift-audit`

### Auto-Trigger Integration
- `project-pipeline` Step 0.7: Run on docs changed since last project
- `project-continue` when resuming after a long gap

## Distinct from other skills

| Skill | Focus |
|---|---|
| `code-review` | PR code quality (security, perf, maintainability) |
| `adr-check` | Code compliance with ADR constraints |
| `repo-cleanup` | File organization, orphans, ephemeral files |
| `ai-procedure-maintenance` | Propagating new ADRs/skills/patterns |
| **`doc-drift-audit`** (this) | **Doc content drift** — stale refs, broken paths |

## Prerequisites

- Git repository with commit history
- Known baseline commit (default: previous project completion, or `git merge-base` with master)

## Inputs

| Input | Required | Default | Notes |
|---|---|---|---|
| `--from` | No | Previous project tag, or `HEAD~50` | Baseline commit |
| `--to` | No | `HEAD` | Current state |
| `--scope` | No | `all` | `all`, `docs`, `.claude`, or specific dir |

## Workflow

### Step 1: Compute Scope

```bash
# Determine baseline
FROM_SHA=${--from:-$(git merge-base master HEAD)}
TO_SHA=${--to:-HEAD}

# Get changed files in scope
git diff --name-only $FROM_SHA..$TO_SHA -- src/ docs/ .claude/patterns/ .claude/constraints/

REPORT: "Auditing drift in {N} files changed between {FROM} and {TO}"
```

### Step 2: Classify Changes

Group changed files by type:
- **Source code changes**: `src/**` — may have moved/renamed classes, changing what docs should reference
- **Doc changes**: `docs/**` — may have introduced new refs that need validation
- **Pattern/constraint changes**: `.claude/patterns/**`, `.claude/constraints/**` — pointer files that must stay accurate

### Step 3: Build Audit Task List

For each category, generate audit checks:

**Source code changes → check docs reference them correctly**:
- For each deleted file in src/: grep docs/ and .claude/ for references → flag stale
- For each renamed class/file: grep for old name → flag stale
- For each new class: check if it should be added to a pattern/architecture doc

**Doc changes → validate internal references**:
- For each file path mentioned in a changed doc: check it exists
- For each class name mentioned: grep src/ for existence
- For each markdown link: check target resolves

**Pattern/constraint changes → validate pointer accuracy**:
- For each "Read These Files" entry: verify file exists
- For each class name: verify via grep
- For each ADR reference: verify docs/adr/ADR-NNN-*.md exists

### Step 4: Execute Audit (Parallel Agents)

For large diffs, spawn parallel sub-agents:
- Agent A: Source → doc references
- Agent B: Doc → code references
- Agent C: Pattern/constraint pointer validation

Each agent produces findings. Main session aggregates.

**CAUTION**: Per R2 lessons, agents cannot write to `.claude/` — only main session can. Agents audit only; main session applies fixes.

### Step 5: Classify Findings

| Severity | Example | Action |
|---|---|---|
| **Critical** | Doc references deleted class used in production | Auto-fix or flag for immediate attention |
| **High** | Pattern file points to renamed file | Auto-fix path update |
| **Medium** | Broken markdown link in doc | Auto-fix if successor is clear |
| **Low** | Last Reviewed stamp older than 90 days on changed file | Suggest refresh |
| **Info** | New src file not referenced in any pattern | Suggest adding pointer |

### Step 6: Apply Safe Fixes

Auto-fix these without prompting:
- Renamed file path updates (where old → new is unambiguous)
- Broken links where successor is obvious (old deleted + new with same semantic)
- Last Reviewed stamp refresh (update to current date)

Prompt user for:
- Ambiguous references (multiple candidates)
- Deleted content with no obvious successor
- Cross-doc impact (e.g., INDEX.md entry to remove)

### Step 7: Report

Produce a compact report:

```
=== Doc Drift Audit Report ===
Baseline: {FROM_SHA} ({tag or date})
Current: {TO_SHA}
Scope: {N} files changed

Findings:
  Critical: {N} (see details)
  High:     {N} (X auto-fixed, Y flagged)
  Medium:   {N} (X auto-fixed)
  Low:      {N} (stamps to refresh)
  Info:     {N} (suggestions)

Auto-fixed: {N} files
Flagged for review: {N} items

Details in: projects/{current-project}/notes/drift-audit-{date}.md
```

### Step 8: Update Last Reviewed Stamps

For files that were audited and verified clean: update their `Last Reviewed` stamp to today's date.

For files that were auto-fixed: update `Last Reviewed` AND add a note to the fix commit.

### Step 9: Commit Audit Results

```bash
git add -A
git commit -m "chore(docs): drift audit $(date +%F) — fixed N stale refs, refreshed M stamps"
```

## Methodology Notes

This skill implements the **compact version** of R2's full-repo audit. Key principles:

1. **Diff-based, not exhaustive**: Only audits files changed since baseline. R2's full-repo scan is reserved for major milestones (annual reviews, acquisitions).

2. **Parallel by default**: Uses Claude Code task agents to audit categories simultaneously. Typically completes in 10-30 minutes.

3. **Auto-fix conservatively**: Only auto-fixes when successor is unambiguous. Everything else flagged for human review.

4. **Stamps as audit history**: Uses existing `Last Reviewed` stamps (added in R2) as the audit log. No separate changelog needed.

5. **Main-session writes only**: Sub-agents cannot write to `.claude/` (permission boundary is intentional). Agents return findings, main session applies fixes.

## Failure Modes & Recovery

| Issue | Recovery |
|---|---|
| Baseline commit not found | Use `git merge-base master HEAD` as fallback |
| Too many changes (>500 files) | Recommend using full-repo audit instead |
| Agent hits permission denial | Documented expected behavior — main session applies fix |
| Ambiguous reference (multiple successors) | Flag for user, include both candidates in report |

## Success Criteria

A successful audit:
- Completes in <30 minutes for typical project diffs (<200 files)
- Auto-fixes at least 50% of findings
- Produces an actionable report for the remaining items
- Updates Last Reviewed stamps on verified files
- Leaves a commit trail in the project's notes/

## Integration Points

| Skill | Integration |
|---|---|
| `project-pipeline` | Can invoke as Step 0.7 (after master staleness check) |
| `project-continue` | Can invoke when resuming after >1 month gap |
| `merge-to-master` | Can invoke before merge to catch drift early |
| `adr-check` | Complementary — adr-check validates code, this validates docs |
| `code-review` | Complementary — code-review validates PR code, this validates doc refs |

## Related Resources

- R2 methodology: `projects/ai-procedure-refactoring-r2/notes/lessons-learned.md`
- R2 findings: `projects/ai-procedure-refactoring-r2/notes/verification-report.md` (if still present)
- Context Layer Hierarchy: Root `CLAUDE.md` "Architecture Discovery" section

## Tips for AI

- **Always run diff-based first** — full-repo audits are expensive, reserve for major milestones
- **Parallel agents speed this up dramatically** — for >50 files, use 3-4 agents by category
- **Respect the `.claude/` write boundary** — agents audit, main session writes
- **Be conservative with auto-fixes** — when in doubt, flag for human review
- **Last Reviewed stamps are the audit log** — refresh them on verified files, use them to find stale content
- **Report succinctly** — humans won't read a 50-page audit; prioritize by severity

---

*Skill created during ai-procedure-refactoring-r2 to codify the drift-detection methodology for ongoing use.*
