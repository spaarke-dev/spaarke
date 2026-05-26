# Batch 6 Audit — Skills 31-36 (project-continue → repo-cleanup)

> **Generated**: 2026-05-15
> **Auditor**: task 025 (RETRY, fast-path)
> **Method**: Grep-driven structural assessment; targeted reads with offset/limit; **no full-body reads of skills > 400 lines**.
> **URL ref-checks**: deferred for speed (paths only).

---

## 1. `project-continue` (530 lines)

- **Frontmatter**: complete (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`). Missing `exemplar`. Uses `Last Updated: January 2026` (not the R2 `Last Reviewed` stamp).
- **Structure**: 10 H2 headings — Purpose, When to Use, Prerequisites, Workflow (lines 48-340 = ~290 lines of step procedure), Guardrails, Error Handling, Examples (lines 403-475), Integration, Related Files, Tips for AI. Clean shape.
- **Gotchas section**: NONE (grep returned 0 hits for `Gotchas|Common mistakes|Anti-patterns`).
- **Goal-oriented vs prescriptive**: prescriptive (numbered Workflow steps), but the Purpose clearly states the *outcome* (resume project with full context loading + branch sync + ADR loading). Steps are appropriate for an orchestrator skill.
- **Cross-refs**: 33 total references. Notably referenced from `project-pipeline` and `worktree-sync`; `Integration with Other Skills` lists 7 sibling skills with concrete handoff semantics.
- **Path refs (spot)**: `projects/{name}/current-task.md`, `projects/{name}/tasks/TASK-INDEX.md`, `projects/{name}/CLAUDE.md`, `projects/{name}/CODE-INVENTORY.md` — all templated paths (no concrete file to test). Marked `references-verified: pass-by-template`.
- **Distinct purpose vs project-pipeline / project-setup**: YES — project-continue is the *resume* operation; pipeline is *initialize*; setup is *artifact-generate-only*. The three roles are non-overlapping (see batch summary below).
- **Exemplar**: `none-too-volatile` (workflow is generic across projects; no single canonical execution trace).
- **Recommended action**: `refine-in-place` — add a `## Gotchas` section (the obvious one: "Don't skip ADR loading at Step 5; don't forget master staleness check in worktrees"); add `Last Reviewed` stamp; add `exemplar: none-too-volatile` to frontmatter. Body is well-structured at 530 lines.

---

## 2. `project-pipeline` (943 lines) — **PRE-DETERMINED `split`**

- **Frontmatter**: present (description, tags, techStack, appliesTo, alwaysApply) but **placed AFTER the H1 line** (line 1 is `# project-pipeline`, frontmatter lines 3-9). This is unusual — convention is frontmatter first. Missing `exemplar`. Mentions `MAX_THINKING_TOKENS=50000` and `CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000` as **prerequisites** (lines 17-20) — note that root CLAUDE.md says Opus 4.6 **ignores** `MAX_THINKING_TOKENS`, so this is **stale** per CLAUDE.md adaptive thinking section. Flag.
- **Structure**: 10 H2 headings — Prerequisites, Purpose, When to Use, Pipeline Steps (lines 94-613 = **~520 lines** — the bulk), Status, Quick Links, Error Handling, Usage Examples (lines 783-879), Integration, Success Criteria. Pipeline Steps contains 12 H3 steps (0, 0.3, 0.5, 1, 1.5, 2, 3, 4, 5) — natural split boundaries.
- **Gotchas section**: NONE.
- **Goal-oriented vs prescriptive**: heavily prescriptive (12 numbered steps with sub-steps). For an orchestrator, this is appropriate — but 943 lines is well beyond Claude Code skill guidance.
- **Cross-refs**: **116 total references** — high blast radius. Referenced from CLAUDE.md trigger map, project-continue, multiple project CLAUDE.md files, docs/guides/. Any restructure must preserve trigger phrases and the `/project-pipeline` slash command surface.
- **Path refs (spot)**: `dotnet build src/server/api/Sprk.Bff.Api/` (line 131 — real path, valid); `src/client/pcf/`, `src/server/api/`, `src/solutions/`, `.claude/skills/INDEX.md`, `docs/guides/`, `.claude/patterns/` — all repo-real directories. `references-verified: pass`.
- **Distinct purpose vs project-continue / project-setup**: YES — pipeline = *orchestrate end-to-end initialize* (spec → branch → tasks → first execute), explicitly calls project-setup (Step 2) and task-create (Step 3).
- **Exemplar**: real path candidate exists — `projects/ai-procedure-quality-r1/` is itself a successful invocation. But the pipeline run isn't preserved as a transcript; flag as `none-too-volatile` for now.
- **Stale claim risk**: Prerequisites' `MAX_THINKING_TOKENS=50000` directive contradicts current CLAUDE.md "Opus 4.6: MAX_THINKING_TOKENS is IGNORED (adaptive thinking controls depth)". This is **exactly the F-1 failure mode** the project is targeting ("Skill says X but X is wrong").
- **Recommended action**: `split` — move Pipeline Steps detail (lines 94-613, the 520-line procedural body) to `.claude/skills/project-pipeline/references/pipeline-steps.md`. Keep in `SKILL.md`: frontmatter + Prerequisites + Purpose + When to Use + a *summary table* of the 12 steps with pointers into `references/pipeline-steps.md` + Error Handling + Integration + Success Criteria. Target SKILL.md size after split: ~250-300 lines. Also fix the stale `MAX_THINKING_TOKENS` claim during the split. Split boundaries are clean (each H3 step is self-contained).

---

## 3. `project-setup` (656 lines)

- **Frontmatter**: complete (after H1 same as project-pipeline — same shape anomaly). Missing `exemplar`.
- **Structure**: 18 H2 headings — Prerequisites, Purpose, **Developer Note** (good — clearly marks AI-internal use), Inputs Required, Workflow (lines 105-267 = ~160 lines), Project Status, Quick Reference, Context Loading Rules, MANDATORY Task Execution Protocol, Key Technical Constraints, Decisions Made, Implementation Notes, Resources (twice — anomaly), What This Skill Does NOT Do (good), Conventions, Integration with Other Skills, Examples (lines 532-625), Validation Checklist, Summary.
- **Gotchas section**: NONE.
- **Goal-oriented vs prescriptive**: prescriptive (Workflow has numbered steps). Has good clarity-of-scope sections (`Developer Note`, `What This Skill Does NOT Do`).
- **Cross-refs**: 24 total. Referenced from project-pipeline, CLAUDE.md trigger map, task-create. Lower blast radius.
- **Anomalies**: TWO `## Resources` H2 sections (lines 410 and 509). Should consolidate. Several H2 sections appear to be **template content for the generated CLAUDE.md** rather than skill content (Project Status, Key Technical Constraints, Decisions Made, Implementation Notes) — these are confusing inside the skill body.
- **Path refs (spot)**: `projects/{name}/spec.md`, generated artifact paths — all templated. `references-verified: pass-by-template`.
- **Distinct purpose vs project-pipeline / project-continue**: YES — setup is the *pure artifact-generator component*. It is explicitly NOT intended for direct developer use (clearly stated in Developer Note). Pipeline orchestrates and calls setup.
- **Exemplar**: `none-too-volatile`.
- **Recommended action**: `refine-in-place` — high priority. Fixes: (a) merge duplicate `## Resources`; (b) move the template-content sections (Project Status, Key Technical Constraints, Decisions Made, Implementation Notes) to a `references/claudemd-template.md` if they are template scaffolding for generated CLAUDE.md files; (c) add `## Gotchas` ("Don't invoke directly; use project-pipeline"); (d) add `Last Reviewed` + `exemplar: none-too-volatile`. After cleanup, body could shrink to ~400 lines.

---

## 4. `pull-from-github` (286 lines)

- **Frontmatter**: **minimal** — only `description` + `alwaysApply: false`. Missing `tags`, `techStack`, `appliesTo`, `exemplar`. Flagged as inconsistent in inventory.
- **Structure**: 10 H2 — Purpose, Applies When, Prerequisites, Worktree Support, Workflow, Stash Workflow, Common Scenarios, Error Handling, Related Skills, Tips for AI. Clean.
- **Gotchas section**: NONE (but `## Tips for AI` lines 273-287 contains several de-facto gotchas — e.g., "NEVER suggest `git checkout master` in a worktree"). Could rename or add.
- **Goal-oriented vs prescriptive**: well-balanced. Purpose is goal-oriented (safely pull, preserve work); Workflow is prescriptive but appropriately concrete for a git operation.
- **Cross-refs**: 14 total. Referenced from push-to-github, project-continue, CLAUDE.md trigger map. Modest blast radius.
- **Overlap with push-to-github**: distinct purpose (pull = inbound sync; push = outbound + PR). Some shared concepts (Worktree Support, stash patterns) but the workflows are separate operations. **Not a merge candidate.**
- **Path refs (spot)**: none significant (mostly `git` commands). `references-verified: n/a`.
- **Exemplar**: `none-too-volatile`.
- **Recommended action**: `refine-in-place` — small effort. Add `tags`, `techStack`, `appliesTo` to frontmatter (normalize per the inventory anomaly); add explicit `## Gotchas` section (consolidate from Tips for AI worktree warnings); add `Last Reviewed` + `exemplar: none-too-volatile`. Body is healthy at 286 lines.

---

## 5. `push-to-github` (534 lines)

- **Frontmatter**: **minimal** — only `description` + `alwaysApply: false`. Missing `tags`, `techStack`, `appliesTo`, `exemplar`.
- **Structure**: 15 H2 — Purpose, Applies When, Prerequisites, Worktree Support, When to Create a PR, Workflow (lines 126-340 = ~215 lines), Summary, Related, Changes, Testing, Checklist, Conventions, Error Handling, Related Skills, Quick Reference, Tips for AI.
- **Anomaly**: H2 sections `## Summary`, `## Related`, `## Changes`, `## Testing`, `## Checklist` (lines 341-453) appear to be **PR-body template content** that should be either inside a fenced code block or moved to `references/pr-template.md` — they fragment the skill structure and read as if they're skill sections when they're actually templates for generated PR descriptions.
- **Gotchas section**: NONE, but `## Tips for AI` is rich with de-facto gotchas (untracked file check at Step 1.5 is called "CRITICAL").
- **Goal-oriented vs prescriptive**: prescriptive workflow appropriate for a multi-step git+PR operation.
- **Cross-refs**: **60 total** — high. Referenced from many places (project-continue, pull-from-github, CLAUDE.md trigger map, multiple project CLAUDE.md files, docs).
- **Overlap with pull-from-github**: shared worktree concepts; otherwise distinct (push includes PR creation, untracked-file check, conventional commit guidance — none of which apply to pull). **Not a merge candidate.**
- **Path refs (spot)**: none significant.
- **Exemplar**: `none-too-volatile`.
- **Recommended action**: `refine-in-place` — medium effort. Fixes: (a) move PR-template sections (lines 341-453) to `references/pr-template.md` and reference from skill body; (b) normalize frontmatter (add `tags`, `techStack`, `appliesTo`); (c) add explicit `## Gotchas` (untracked file check, never merge before CI green, worktree detection); (d) add `Last Reviewed` + `exemplar: none-too-volatile`. After PR template extraction, body could shrink to ~400 lines.

---

## 6. `repo-cleanup` (502 lines) — **HUB #4 with 215 refs**

- **Frontmatter**: **minimal** — only `description` + `alwaysApply: false`. Missing `tags`, `techStack`, `appliesTo`, `exemplar`.
- **Structure**: 14 H2 — Purpose, Applies When, Workflow (lines 33-230 = ~200 lines), **Repository Cleanup Report** (lines 231-309 — appears 3x more at lines 394, 425!), Conventions, Resources, Integration Points, Examples (lines 386-457), Pre-Merge Repository Check, Error Handling, Related Skills, Tips for AI.
- **Anomaly**: THREE `## Repository Cleanup Report` H2 sections (lines 231, 394, 425). Likely intended as example output templates inside Examples, but they're at H2 level — same fragmentation issue as push-to-github.
- **Gotchas section**: NONE. "NOT applicable when" callout at lines 27-30 is gotcha-adjacent.
- **Goal-oriented vs prescriptive**: Purpose is goal-oriented (validate structure, enforce conventions). Workflow is heavily prescriptive (200 lines).
- **Cross-refs**: **215 total** — **HUB #4 in the entire skills graph**. Extreme blast radius. Referenced from nearly all project CLAUDE.md files (multiple), docs, settings, skill invocations. **Any destructive change requires careful migration.**
- **Path refs (spot)**: `docs/guides/REPOSITORY-NAVIGATION-GUIDE.md` (real file in repo), `src/client/pcf/`, `src/server/api/Sprk.Bff.Api/`, `.claude/skills/`, `docs/ai-knowledge/catalogs/`, `docs/_archive/`, `projects/.archive/` — all repo-real directories. `references-verified: pass`. **Note**: `docs/ai-knowledge/catalogs/` should be verified for current relevance (the R2 doc refactor moved AI-related material around).
- **Exemplar**: `none-too-volatile` (cleanup reports are per-project).
- **Recommended action**: `refine-in-place` — careful. With 215 refs, no removal/rename. Fixes: (a) consolidate the three `## Repository Cleanup Report` sections — move report templates to `references/cleanup-report.md`; (b) normalize frontmatter (add `tags`, `techStack`, `appliesTo`); (c) add explicit `## Gotchas` (don't run during active development, careful with `docs/ai-knowledge/` references that may be stale post-R2); (d) verify `docs/ai-knowledge/catalogs/` reference still applies (potential stale ref per R2 refactor); (e) add `Last Reviewed` + `exemplar: none-too-volatile`. Body could shrink to ~380 lines after template extraction.

---

# Batch 6 Summary

## Count by recommended action

| Action | Count | Skills |
|---|---|---|
| `refine-in-place` | 5 | project-continue, project-setup, pull-from-github, push-to-github, repo-cleanup |
| `split` | 1 | project-pipeline |
| `merge-with-X` | 0 | — |
| `remove` | 0 | — |
| `leave-alone` | 0 | — |
| `needs-substantive-rewrite` | 0 | — |

## Cross-batch flags (Phase 2b inputs)

1. **`project-pipeline` carries stale guidance.** Lines 17-20 prescribe `MAX_THINKING_TOKENS=50000`, but root CLAUDE.md (Opus 4.6 section) states this env var is IGNORED on Opus 4.6. This is **F-1 failure mode evidence** ("Skill says X but X is wrong") — exactly what this project's spec targets. Fix during split.
2. **Three skills (project-pipeline, project-setup, push-to-github, repo-cleanup) carry template content as H2 sections inside the skill body** — should be moved to `references/` subdirs.
3. **Five skills (project-pipeline, project-setup all good; pull-from-github, push-to-github, repo-cleanup) have inconsistent frontmatter** — the github/cleanup trio lack `tags`/`techStack`/`appliesTo`. Same as Anomaly 6 in skills inventory.
4. **All 6 skills lack `Gotchas` and `exemplar:` and `Last Reviewed` stamp.** Universal gap matching project-wide inventory.

## project-* mutual overlap assessment

The three project-* skills have **distinct, non-overlapping responsibilities**:

| Skill | Trigger | Role | Calls / Called-by |
|---|---|---|---|
| `project-pipeline` | New project from spec.md | Orchestrator: initialize end-to-end | Calls project-setup (Step 2), task-create (Step 3), task-execute (Step 5) |
| `project-setup` | (internal) | Component: artifact generation only | Called by project-pipeline |
| `project-continue` | Resume existing project | Orchestrator: resume + sync | Standalone; complements pipeline |

**Conclusion**: NO merge or removal warranted. The boundary is clear: *initialize* vs. *resume* vs. *generate-artifacts-only*. project-setup is correctly marked "AI internal use" via its `## ⚠️ Developer Note`. Refinements should preserve all three; the redundancy/repetition concern is internal (template content inside SKILL.md body), not inter-skill.

## pull/push-from-github overlap assessment

The two github skills are **distinct operations on different sides of the same git workflow**:

| Skill | Direction | Adds | Trigger |
|---|---|---|---|
| `pull-from-github` | Inbound | Rebase, stash, conflict resolution | "get latest", "git pull", "sync from remote" |
| `push-to-github` | Outbound | Untracked-file check, commit, PR creation, CI status | "commit and push", "create PR", "ready to merge" |

**Shared concepts**: worktree detection patterns, stash awareness, force-with-lease semantics. These could potentially live in a single `references/worktree-git-patterns.md` shared by both skills — minor refinement opportunity, not a merge.

**Conclusion**: NO merge warranted. The two skills are referenced separately throughout the repo (60 + 14 = 74 distinct refs), and merging would (a) break trigger-phrase routing in root CLAUDE.md, (b) bloat a single skill to ~800 lines, (c) confuse the inbound-vs-outbound git mental model.

---

## URL checks deferred

Per task instructions, URL ref-checks were skipped for speed. No HEAD requests issued. Re-check during Phase 2b implementation if any external links matter.

## Time spent

~3 minutes elapsed; 6/6 skills audited within the watchdog window.
