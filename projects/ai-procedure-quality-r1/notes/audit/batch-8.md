# Phase 2a Audit — Batch 8 of 8 (worktree skills)

> **Auditor**: parallel agent (task 027)
> **Date**: 2026-05-15
> **Skills**: worktree-setup, worktree-sync (alphabetical 43–44 of 44)
> **Honesty**: Both skills are in genuinely good shape — high-value, well-structured, low overlap. No fabricated problems.

---

## 1. worktree-setup

**File**: `.claude/skills/worktree-setup/SKILL.md` (733 lines)
**Cross-refs (from cross-refs.json)**: total 12 (3 see-also from `conflict-check` + `worktree-sync`; 2 trigger-map entries in root CLAUDE.md; 2 settings.local.json entries; 5 in `docs/procedures/`; 0 task POMLs; not orphaned).

### Structural assessment (vs. 7 best practices)

| Practice | Status | Notes |
|---|---|---|
| **Description (precise)** | ✅ PASS | "Create and manage git worktrees for parallel project development" — concrete, action-verbed, scope clear |
| **Frontmatter complete** | ✅ PASS | desc, tags, techStack, appliesTo, alwaysApply all present |
| **Line count tier (F-15.1)** | ⚠️ OVER-TARGET-justified | 733 lines, > 400 split threshold. BUT content is 6 operational workflows (A–F) + parallel-sessions runbook — each is procedural reference material consulted on demand. Justifies tier `leave-alone-justified` OR `SPLIT-recommended` (move "Parallel Claude Code Sessions" section, lines 585–719 ≈135 lines, to `references/parallel-sessions.md`). Body shrinks to ~600 lines — still over but operationally cohesive. |
| **Goal-oriented (not tutorial)** | ✅ PASS | Workflows have clear decision trees, error branches, and verification steps |
| **Gotchas section** | ❌ MISSING | No `## Gotchas` heading. Implicit gotchas live in "Isolation Rules" + "Troubleshooting" tables (lines 561–582) — these cover the same ground but aren't labeled Gotchas |
| **Overlap with other skills** | ⚠️ MINOR | Overlaps with `pull-from-github`/`push-to-github` only in the "End-of-Task Workflow" (lines 650–669) which duplicates push patterns. The core worktree-create/remove operations are genuinely unique. Overlap with `worktree-sync` is by design: setup creates, sync maintains. |
| **Deterministic rules (MUST/MUST NOT)** | ⚠️ PARTIAL | Has decision trees with hard rules ("IF git-dir is not '.git' → STOP"), but no MUST/MUST NOT bullets summarizing top constraints. Isolation Rules table (line 561) is effectively the deterministic rule set |
| **References current** | ✅ PASS | "Last Updated: January 2026" (newer reference at line 733). Related Skills section names 5 skills — all 5 exist (verified via cross-refs.json: project-pipeline, push-to-github, pull-from-github, repo-cleanup, conflict-check) |

### Light ref-check (paths + skills)
- `C:\code_files\spaarke` (main repo path) — exists (current cwd)
- `spaarke-wt-{project-name}` naming — convention, no fixed path to verify
- Related skills `project-pipeline`, `push-to-github`, `pull-from-github`, `repo-cleanup`, `conflict-check` — all 5 exist in `.claude/skills/`
- No external URLs to verify

### Medium ref-check (commands)
- `git worktree add ../path -b branch` — ✅ valid syntax
- `git worktree add ../path branch` (no -b, line 232 onward) — ✅ valid for tracking existing remote branch
- `git worktree list` — ✅ valid
- `git worktree remove <path>` and `git worktree prune` — ✅ valid
- `git fetch origin master:master` (line 95, no-checkout fast-forward) — ✅ valid and useful idiom
- `git rev-parse --git-dir` returning `.git` to detect main repo — ✅ correct
- `git push --force-with-lease` (line 642, 666) — ✅ correct (safer than --force)
- `gh pr list --json number,title,files` (line 675) — ✅ valid gh CLI syntax

No command-syntax errors found. The skill's operational guidance is accurate.

### Cross-ref impact (refactoring safety)
- 12 inbound references; renaming or removing this skill would require updates in:
  - 1 see-also in `conflict-check` (line 261)
  - 2 see-also lines in `worktree-sync` (389, 400)
  - 2 root CLAUDE.md trigger-map lines (712, 785)
  - 2 settings.local.json entries (342, 343)
  - 5 docs/procedures lines
- Refactor risk: LOW (rename feasible). Removal risk: HIGH (well-integrated).

### Exemplar
`exemplar: none-too-volatile` — worktree operations target user-named ephemeral directories (`spaarke-wt-{project-name}`); no stable reference to rebuild. Maintenance cost would exceed value.

### Recommended action
**`refine-in-place`** — add three things in one focused edit:
1. Insert a `## Gotchas` section between "Isolation Rules" and "Troubleshooting" (lines ~570) summarizing the 4–5 top traps already implicit in Isolation Rules + Troubleshooting (e.g., "Don't run two Claude sessions on same branch", "Don't edit root *.sln in parallel", "Always `git fetch origin master:master` before creating worktree").
2. Add `exemplar: none-too-volatile` to frontmatter with a one-line rationale.
3. Replace `Last Updated: January 2026` with `Last Reviewed: 2026-05-15` per R2 convention.
4. (Optional / future) Move "Parallel Claude Code Sessions" subsection to `references/parallel-sessions.md` to drop body under 600 lines. Not required; document if deferred.

---

## 2. worktree-sync

**File**: `.claude/skills/worktree-sync/SKILL.md` (417 lines)
**Cross-refs (from cross-refs.json)**: total 13 (8 see-also: `merge-to-master`, `project-continue`, `project-pipeline`, `push-to-github`; 3 trigger-map entries; 2 in `docs/procedures/`; 0 task POMLs; not orphaned).

### Structural assessment

| Practice | Status | Notes |
|---|---|---|
| **Description (precise)** | ✅ PASS | "Guarantee worktree is fully synchronized — committed, pushed, merged to master, updated from master" — outcome-focused, distinguishes from worktree-setup |
| **Frontmatter complete** | ✅ PASS | desc, tags, techStack `[all]`, appliesTo, alwaysApply all present |
| **Line count tier** | ⚠️ OVER-TARGET-justified | 417 lines, just over 400 split threshold. Body is six numbered Steps (0–6) + Error Handling + Integration tables + Tips. Splitting would fragment a single-flow procedure. Tier: `leave-alone-justified`. |
| **Goal-oriented (not tutorial)** | ✅ PASS | Strong: 3 operating modes (Full/Push/Update), each Step has explicit fetch + assess + verify pattern. "CRITICAL RULE" callouts are imperative |
| **Gotchas section** | ❌ MISSING | No `## Gotchas` heading. However, "Tips for AI" (line 404) is functionally an excellent gotchas section — covers fetch-before-compare, --force-with-lease, merge-vs-rebase default, honest reporting. Just needs renaming or duplicate heading |
| **Overlap with other skills** | ✅ INTENTIONAL/HEALTHY | Step 1 overlaps with `push-to-github` (commit/push), Step 3 with `merge-to-master`, Step 5 with `pull-from-github`. But the Integration table (line 384) explicitly states this skill *replaces* the incomplete worktree handling in those skills with a verified flow. Overlap is the skill's value-add, not duplication. |
| **Deterministic rules (MUST/MUST NOT)** | ✅ PASS | Several CRITICAL RULE callouts (line 115, 362), "ABSOLUTE RULE" in Tips (line 406), "NEVER `--force`" rules. Explicit deterministic guidance |
| **References current** | ✅ PASS | "Last Updated: March 2026". Related Skills (lines 394–400) names 5 skills — all exist. Integration with Other Skills table (line 384) — all 5 exist |

### Light ref-check
- All 5 Related Skills exist: `merge-to-master`, `push-to-github`, `pull-from-github`, `project-continue`, `worktree-setup`
- All 5 Integration skills exist (same list + `task-execute`)
- No external URLs

### Medium ref-check (commands)
- `git fetch origin` — ✅ valid (used as the mandatory first step in every mode)
- `git rev-parse --is-inside-work-tree` — ✅ valid
- `git rev-parse --git-common-dir` (line 88, 410) — ✅ correct; this DOES differ from `--git-dir` inside a worktree (worktree's git-dir is `.git/worktrees/<name>`, common-dir is the main `.git`). The Tip at line 413 is technically accurate.
- `git status --porcelain | wc -l` — ✅ valid (works in PowerShell with WSL or git-bash; on raw PowerShell would need `(git status --porcelain | Measure-Object -Line).Lines`). Skill assumes bash semantics; flag in gotchas if not already
- `git rev-list --count origin/{branch}..HEAD` and reverse — ✅ valid
- `git merge origin/master --no-edit` — ✅ valid
- `git rebase origin/master` + `git push --force-with-lease` — ✅ correct
- `git rev-parse --short HEAD` — ✅ valid
- `git branch --show-current` — ✅ valid (Git 2.22+)

One minor portability issue: `git status --porcelain | wc -l` (line 92, 338) assumes `wc` is on PATH. On native PowerShell it isn't. Worth a one-line gotcha note. Not a bug — the skill works as intended in bash and in git-bash.

### Cross-ref impact
- 13 inbound references. Removing would break 8 see-also references, 3 trigger-map entries. Removal risk: HIGH (deeply integrated).

### Exemplar
`exemplar: none-too-volatile` — branch state is per-session, not a stable reproducible artifact.

### Recommended action
**`refine-in-place`** — three focused changes:
1. Rename "Tips for AI" (line 404) to `## Gotchas` (or add `## Gotchas` as a new heading and move Tips bullets under it). Content is already excellent gotchas material; just needs the canonical heading.
2. Add a single bullet to gotchas about `wc -l` portability (PowerShell vs bash).
3. Add `exemplar: none-too-volatile` to frontmatter with rationale.
4. Replace `Last Updated: March 2026` with `Last Reviewed: 2026-05-15`.

---

## Merge-vs-distinct assessment

**Question**: Should `worktree-setup` and `worktree-sync` be merged?

**Verdict**: **DO NOT MERGE** — keep them distinct. Same pattern as pull-from-github vs push-to-github (batch 6) — distinct verbs, distinct lifecycle stages.

**Evidence**:
- **Different verbs / lifecycle stages**: `worktree-setup` covers create/list/remove/reuse/reset (one-shot lifecycle events). `worktree-sync` covers ongoing bidirectional state synchronization (recurring per-session operation).
- **Different trigger phrases**: setup triggers on "create/setup/new/reuse/reset worktree"; sync triggers on "sync/update/pull master/are we current". No overlap in user intent.
- **Different audiences**: setup is invoked rarely (once per project at start, once at end). sync is invoked many times per project (every work session, before merges, on demand).
- **Different content density**: setup is 6 workflows of decision trees; sync is one 6-step linear verified flow with 3 mode variants. Merging would create a 1100+ line skill mixing one-time setup with ongoing operations — exactly the dilution problem F-15.1 warns against.
- **Different integration points**: setup integrates with `project-pipeline` (project start) and `repo-cleanup` (project end). sync integrates with `merge-to-master`, `push-to-github`, `project-continue` (mid-project).
- **Cross-refs confirm distinction**: 12 + 13 = 25 inbound refs across 11 distinct skill/doc files; ZERO refs treat them as interchangeable. They reference each other (sync → setup at line 389; setup uses `git worktree add` patterns sync depends on).

**Keep separate. Like pull-from-github vs push-to-github (batch 6 confirmed): distinct verbs in a git operation taxonomy is a strength, not duplication.**

---

## Batch 8 Summary

**Skills audited**: 2 (worktree-setup, worktree-sync)

**Count by recommended action**:
- `refine-in-place`: 2 (both)
- `split`: 0
- `merge`: 0
- `archive`: 0
- `leave-alone`: 0
- `needs-substantive-rewrite`: 0
- `leave-alone-justified`: 0 (both are refine-in-place, not pure leave-alone)

**Common refinement themes across batch**:
1. Both missing `## Gotchas` heading — but both have functionally equivalent content (Tips for AI in sync; Isolation Rules + Troubleshooting in setup). Pure header rename / minor reorg.
2. Both missing `exemplar:` frontmatter — both correctly resolve as `none-too-volatile` (worktree state is per-session, not a stable reproducible artifact).
3. Both use `Last Updated:` instead of R2 `Last Reviewed:` convention.
4. Neither is over-target enough to require `split`; sync is at 417 (just over) and setup at 733 (well over) but content is operationally cohesive — split only optional for setup.

**Setup-vs-sync merge assessment**: **DO NOT MERGE**. Distinct verbs (create vs synchronize), distinct lifecycle stages (one-shot vs recurring), distinct trigger phrases, distinct audiences, distinct integration points. Same conclusion as batch-6's pull/push verdict — separate skills are the right design.

**Honesty check**: Both skills are genuinely high-quality. The refinements recommended are minor structural (heading rename, frontmatter field, date-stamp convention) — not substantive content rewrites. No problems fabricated.

**References verified**: PASS (all 10 named related/integration skills exist; all commands valid; only minor `wc -l` PowerShell portability note worth adding).
