# project-pipeline restructure — move map (2026-11-03)

Original: `.claude/skills/project-pipeline/SKILL.md` (1,204 lines / ~56 KB), archived verbatim at `.claude/archive/2026-11-03/skills/project-pipeline/SKILL.md`.

## Line counts

| File | Before | After |
|---|---|---|
| `SKILL.md` | 1,204 | 341 (under the 500 cap; no `length-justification` needed) |
| `references/interactive-mode.md` | — | 88 |
| `references/error-recovery.md` | — | 74 |
| `references/skill-integration.md` | — | 66 |
| `references/discovery-fanout.md` | — | 26 |
| `references/draft-pr-template.md` | — | 26 (`*-template.md`, TOC-exempt) |
| `examples/usage-examples.md` | — | 123 (opens with `## Contents`) |
| `examples/step-reports.md` | — | 116 (opens with `## Contents`) |

Every step that runs every time stays inline: Steps 0, 0.3, 0.5, 1, 1.5, 1.7, 2, 3, 4, 4.2 and 5. So do all MUST/MUST NOT rules and gates. Moved out: interactive-mode gates, the Step 4.5 prompt (interactive only), error messages, examples, sample reports, the discovery fan-out schema (conditional), the draft-PR body (optional), and integration notes.

## Section map (original line range → destination)

| Original section (lines) | Destination | Notes |
|---|---|---|
| Frontmatter (1–9) | SKILL.md frontmatter | Triggers unchanged (`description`, `appliesTo`, tags). Added `name: project-pipeline` (template field). `last-reviewed` → 2026-11-03 |
| `> Last Reviewed / Reviewed By / Exemplar rationale / Justified length` (13–16) | SKILL.md header (Category, Last Reviewed 2026-11-03, Exemplar) | **Dropped**: "Reviewed By" and "Justified length (943 lines)". Reason: audit history belongs in git and CHANGELOG, and the length justification is void at 341 lines. Exemplar rationale kept, plus a note that step numbers are a cited contract |
| Prerequisites → Effort & Output Configuration (18–58) | SKILL.md "Execution Mode, Model and Settings" (2 bullets) | **Condensed**. Kept: planning on opus/fable at effort `high` (now cites agent-cost.md, which says `high`, not "high or max"), and `CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000` as the fix when output truncates. **Dropped**: the MAX_THINKING_TOKENS history and "Opus 4.6+" notes (no script sets it; one line "do not set it" remains); the "Why Output Tokens is Critical" bullets (they restated Steps 2–3); the PowerShell verify snippet. The "Example Context Load" (AI Doc Intelligence R1 numbers) → `examples/step-reports.md` "Scale reference" |
| Permission Mode: Plan Mode banner (60–85) | SKILL.md Step 0 | Banner turned into prose; the non-interactive clause is kept verbatim in meaning. **Changed (judgment call)**: the original required Plan Mode for Steps 0–3 and a switch before Step 4, but Steps 2–3 write files and Plan Mode blocks writes, so as written they could not run. Now Plan Mode covers the read-only work (0–1.7 and Step 2 Part 1); the switch to Accept Edits is asked for before the first write (Step 2 Part 2). Matches design-to-spec's pattern and `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md` ("switch … when prompted") |
| Purpose + Key Features + Execution Mode + Interactive Override (89–103) | SKILL.md "What It Produces" + "Execution Mode…" | Interactive override → directive to `references/interactive-mode.md`. Added: the safety gates stop in both modes (consolidates the per-step "Autonomous/Interactive" lines) |
| When to Use (105–109) | SKILL.md "When to Use" | + pointer to /design-to-spec when only a design doc exists |
| Step 0 (113–126) | SKILL.md Step 0 | Same |
| Step 0.3 (130–177) | SKILL.md Step 0.3 (checks a–f) + `examples/step-reports.md` (output) | Check (b): "commit/stash" → "commit (not stash)". Reason: root CLAUDE.md §16, the stash stack is shared by every worktree. Check (a): names `work/{project-name}` |
| Step 0.5 (181–210) | SKILL.md Step 0.5 | Interactive prompt → interactive-mode.md table |
| Step 1 (214–236) | SKILL.md Step 1 | Same rules; directive to error-recovery.md when spec.md is missing |
| Step 1.7 (238–293) | SKILL.md Step 1.7, physically after 1.5 | **Reordered** to numeric order (1 → 1.5 → 1.7 → 2). No citation depends on physical order; 1.7 still runs before Step 2 as its text requires. The "Output to User" block at 280–293 was actually Step 1's output (it says "SPEC.md validated"), misplaced when 1.7 was inserted → `examples/step-reports.md` "Step 1" |
| Step 1.5 (297–376) | SKILL.md Step 1.5 (logic) + `examples/step-reports.md` (overlap report) + interactive-mode.md (wait) | Decision tree condensed to two bullets |
| Step 2 pre-step: projects/INDEX.md hot-path (383–391) | SKILL.md Step 2 pre-step | Check and warning stay in Step 2. **Moved**: "append the new project's row" → Step 4 (needs the branch and worktree path; `projects/INDEX.md` itself says the row is added at "project-pipeline Step 4"). Provenance tag (CICD-061/FR-C04) dropped (git history) |
| Step 2 Part 1 discovery (393–472) | SKILL.md Step 2 Part 1 items 1–7 | Item 7 (Dataverse) made tool-name-neutral (`mcp__dataverse__*`); the original `list_tables`/`describe_table` names differ by server version. **Added to item 5**: record the existing component each new surface overlaps with, and flag unjustified project-level surface. Reason: root CLAUDE.md §11 says it is "Enforced at project-pipeline Step 2", but the original Step 2 had no such check (judgment call, see below). STRUCTURED OUTPUT schema + Workflow note → `references/discovery-fanout.md` (applies only when discovery is fanned out), behind a directive. "DIFFERENCE from design-to-spec Step 3" → one sentence in the Part 1 lead |
| Step 2 Part 2 artifacts (474–503) | SKILL.md Step 2 Part 2 | **Dropped**: "VERIFY 🚨 MANDATORY: Task Execution Protocol section exists in CLAUDE.md; if missing add it". Reason: the current project-setup template (`claudemd-template.md`) has no such section by design ("this file holds only what is specific to this project"); root CLAUDE.md §4 owns task-execution routing. Re-adding it would contradict project-setup. Replaced by filling the template's real sections (§7 Applicable ADRs, §2 ADR tensions). Directive to error-recovery.md on failure |
| Step 2 output (505–533) | `examples/step-reports.md` "Step 2" | Inline: a one-line "Report:" spec |
| Step 3 (537–694) | SKILL.md Step 3 | **Restructured**: "run task-create; it is authoritative" for decomposition, numbering, POML structure, deployment tasks, UI tests, wrap-up, parallel groups, /goal and TASK-INDEX. The **full canonical field list stays inline** (the 2026-07-16 audit fixed the producer/consumer gap *in this list*; Step 5 consumes it), as do placement justification, the hot-path-declaration HARD WARNING (cited as "Step 3" by bff-extensions.md and design-to-spec), the Completeness Lint gate, and the Tasks Planned board rule. **Dropped as duplicates of task-create** (authoritative there): the `<ui-tests>` XML example (= task-create 3.65, which is fuller), the numbering list (= task-create Step 3), the TASK-INDEX Parallel Groups table format (= task-create Step 5), and the wrap-up task's content (= task-create 3.7). Step 3 output → step-reports.md. Interactive prompt "[Y to start task 001]" was wrong (Step 4 comes next) → "continue to branch creation" in interactive-mode.md |
| Step 4 (698–768) | SKILL.md Step 4 + `references/draft-pr-template.md` (PR body, the embedded "## Status / ## Quick Links" block) + step-reports.md (output) | **Changed (judgment call)**: branch `feature/{project-name}` → `work/{project-name}`, used as-is when already checked out. Reason: worktree-setup, devops-project-start, the compaction hook (root CLAUDE.md §16), Step 0.5's `origin/work/*` audit and `projects/INDEX.md` all use `work/`; the remote has 191 `work/` branches against 11 legacy `feature/` ones. Creating `feature/` from a project worktree would split the work off the branch the hooks track. Commit trailer "🤖 Generated with Claude Code" → "the attribution lines the session requires". "WHY create branch at this point" → draft-pr-template.md closing line. Directive to error-recovery.md if the commit is rejected |
| Step 4.2 (775–811) | SKILL.md Step 4.2 | Kept nearly verbatim (MUST). The "why" paragraph condensed; full history → skill-integration.md |
| Step 4.5 (815–865) | SKILL.md Step 4.5 (stub: autonomous skips; return-to-4.2 guard) + `references/interactive-mode.md` "Step 4.5" (prompt, parse rules, "why here" table) | Conditional: runs only in interactive mode |
| Step 5 (869–964) | SKILL.md Step 5 | All rules inline: dispatch, concurrency 6, `.claude/` boundary, /goal (MUST NOT auto-complete), build check between waves, failure isolation, structured outcome, retry rule (cited by task-create), context management. **Changed**: "dispatch with model = <model-tier> AND effort = <effort>" → model per call, effort from the definition or a workflow. Reason: `.claude/constraints/agent-cost.md` "How effort is set" says the Agent tool takes `model` only and names project-pipeline Step 5 explicitly. Added: every launch names a model (hook), and the harness cap `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS`. EXAMPLE flow → `examples/usage-examples.md` Example 5. INTERACTIVE MODE → interactive-mode.md |
| Note on current-task.md lifecycle (966–970) | `references/skill-integration.md` | Owned by project-setup, task-execute and context-handoff; not a pipeline action |
| Error Handling (974–1018) | `references/error-recovery.md` | Directives at Steps 1, 2 Part 2 and 3. The spec-missing list gained "ADR Tensions" (Step 1 requires it) |
| Usage Examples 1, 2, 2 (dup), 3 (1022–1115) | `examples/usage-examples.md` Examples 1–4 | Duplicate "Example 2" renumbered (2 → 2, second 2 → 3, 3 → 4). Example 1 updated to show 0.3, 1.7, 4.2 and the `work/` branch |
| Integration with Existing Skills + Orchestration Pattern + Why component skills (1119–1157) | `references/skill-integration.md` | Added script-aware and devops-project-register/sync rows. Inline SKILL.md keeps a component-skill list under References. "Result: … human confirmation at each major step" dropped: autonomous is the default |
| Success Criteria (1161–1179) | SKILL.md Success Criteria | Consolidated from 16 to 9 lines. Every original item is covered; added 0.3/0.5/1.7 and the Completeness Lint |
| Failure Modes & Recovery table (1183–1192) | Rows 1, 4, 5 → SKILL.md Gotchas; rows 2, 3 → error-recovery.md; row 6 dropped | Row 4 "re-run on Opus with effort max" → "planning tier, effort high" (agent-cost.md: `max` is "prone to overthinking"). Row 3's "Step 2.2" → "Step 2 Part 2" (there is no Step 2.2). Row 6 (stale MAX_THINKING_TOKENS) **dropped**: no script sets it (grep), and the "do not set" line in Execution Mode covers it |
| Footer "For Claude Code: recommended entry point… human-in-loop confirmation" (1196) | **Dropped** | Contradicts the autonomous default; "recommended entry point" is implied by When to Use |
| Portfolio Hook appendix (1200–1204) | Re-run behaviour → SKILL.md Step 4.2 (sync is silent; failure warns, does not block); history → skill-integration.md | — |

## Externally cited step ids — all preserved

Grep of the whole repo (`.claude/`, `docs/`, `projects/`, `scripts/`, root CLAUDE.md; archive excluded).

| Cited id | Cited by (examples) | Preserved as |
|---|---|---|
| Step 0 | — (internal) | `### Step 0: Plan Mode Confirmation` |
| Steps 0–3 (planning tier) | `.claude/constraints/agent-cost.md:48`, `task-create:196`, CHANGELOG | Steps 0–3 still analysis/planning; Execution Mode says planning on opus/fable at `high` |
| Step 0.3 | `projects/ci-cd-unit-test-remediation-r1/tasks/000-preflight-baseline-build.poml` | `### Step 0.3: Pre-Flight Checks` (same checks) |
| Step 0.5 | `merge-to-master` (audit before Step 1) | `### Step 0.5: Master Staleness Check` |
| Step 1 | project-setup, SKILL-INTERACTION-GUIDE, archived design-to-project | `### Step 1: Validate SPEC.md` |
| Step 1.5 | `docs/procedures/parallel-claude-sessions.md:200`, SKILL-INTERACTION-GUIDE:593, project notes | `### Step 1.5: Overlap Detection` |
| Step 1.7 | CHANGELOG:2068, ontology-platform-r1 notes/POML, auth-v4 TASK-INDEX | `### Step 1.7: Process ADR Tensions` (rationale-concreteness check kept verbatim in meaning) |
| Step 2 (+ Part 1 / Part 2) | root CLAUDE.md §11, project-setup:62/374, design-to-spec:377, conflict-check:185, commands, archived design-to-project | `### Step 2`, with **Part 1** / **Part 2** labels kept. §11 citation now backed by Part 1 item 5 |
| Step 3 | bff-extensions.md:323 (hot-path HARD WARNING), design-to-spec:448, AUDIT-FINDINGS 2026-07-16 (field list) | `### Step 3`; the warning and the full field list are inline |
| Step 4 | SKILL-INTERACTION-GUIDE:190, `projects/INDEX.md` (row append) | `### Step 4: Create Feature Branch` (now also appends the INDEX.md row) |
| Step 4.2 | task-execute:273, worktree-setup:760, project notes | `### Step 4.2: Register the project on GitHub (MUST)` |
| Step 4.5 | — (internal) | `### Step 4.5` stub + interactive-mode.md |
| Step 5 (dispatch, 6-agent cap, retry rule, build between waves, /goal) | task-execute:74/84/218, task-create:230/584, code-review:809, agent-cost.md:59, Validate-TaskPoml.ps1:21, project TASK-INDEX/plan files | `### Step 5`; every cited rule inline |

## Dangling citations found (not in the original either; not fixed here, report)

| Citation | Where | Status |
|---|---|---|
| "project-pipeline Step 0.7: Run [doc-drift-audit] on docs changed since last project" | `.claude/skills/doc-drift-audit/SKILL.md:37` | No Step 0.7 has ever existed (`git log -S "Step 0.7"` on this file is empty). Either remove the claim in doc-drift-audit or add the step. Adding it is new ceremony; owner decision |
| "project-pipeline Step 2.5 (enrich plan.md): Tracked deferrals/issues subsection" | `.claude/skills/project-defer-issue-tracking/SKILL.md:262` | No Step 2.5 exists. At project start no issues exist yet (the label is created in Step 4.2), so the hook adds nothing. Recommend removing the row there |

## Cross-file inconsistencies found (outside this skill; report)

- `.claude/skills/task-execute/SKILL.md:218` says Step 5 dispatches with `model` **and** `effort`, "so both are automatic". agent-cost.md says the Agent tool takes `model` only. The new Step 5 follows agent-cost.md, so task-execute:218 should be corrected to match.
- `.claude/commands/project-pipeline.md` still says "human confirmation between each" step and "Follow all 5 steps", and lists the branch as `work/` or `feature/`. It should say autonomous by default, Steps 0–5 including 4.2, and `work/{project-name}`.
- `projects/INDEX.md` Maintenance Contract says the row is appended at "project-pipeline (Step 4 — worktree setup)". The skill now appends at Step 4, which matches; only the "worktree setup" label is loose.
- `dataverse-create-schema`, `dataverse-deploy` and `docs-data-model` cite `mcp__dataverse__list_tables` / `describe_table`. The connected server in this session exposes `describe`, `search` and `read_query`, so the tool names vary by server version.

## Directives added (branch points)

| Where in SKILL.md | Directive |
|---|---|
| Execution Mode, "Interactive on request" | If the user asks for an interactive run, read `references/interactive-mode.md` before Step 0 |
| Step 1 | If `spec.md` does not exist, read `references/error-recovery.md` ("spec.md missing") before continuing |
| Step 2 Part 1 | If you fan discovery out to sub-agents, read `references/discovery-fanout.md` before launching them |
| Step 2 Part 2 | If `project-setup` fails or plan.md lacks a usable Phase Breakdown, read `references/error-recovery.md` ("Artifact generation failed") |
| Step 3 | If task generation fails or produces no task files, read `references/error-recovery.md` ("Task decomposition failed") |
| Step 4 | If you open the draft PR, take its title and body from `references/draft-pr-template.md`; if the commit or push is rejected, read `references/error-recovery.md` ("Commit rejected") |
| Step 4.5 | Interactive: read `references/interactive-mode.md` ("Step 4.5") and run the Target Date prompt |
| Step 5 lead | Interactive mode runs one task at a time (`references/interactive-mode.md`) |

`examples/` are listed under References only: they are illustrative and are never needed to run a step.

## Judgment calls to review

1. **Plan Mode scope** (Step 0): narrowed from "Steps 0–3" to "read-only work, then switch before the first write". The original was not executable as written. Revert if the owner wants the user to approve once, after Step 3, with the writes batched; that would need ExitPlanMode wording.
2. **`feature/` → `work/`** (Step 4): fixes a convention conflict. The original would have created a second branch inside a project worktree.
3. **§11 check in Step 2** (Part 1, item 5): makes root CLAUDE.md's "Enforced at project-pipeline Step 2" true. Without it, that citation is false. It adds one discovery line, not a gate.
4. **Step 5 effort**: no longer claims per-call effort (agent-cost.md). task-execute:218 needs the matching fix.
5. **Step 3 delegates to task-create** but keeps the field list inline. This keeps the 2026-07-16 producer/consumer fix where the audit placed it. The XML/table examples that duplicated task-create were dropped.
6. **INDEX.md row append moved from Step 2 to Step 4** (matches `projects/INDEX.md`'s own contract).
7. **CLAUDE.md "MANDATORY Task Execution Protocol" check dropped** (contradicts the current project-setup template).
