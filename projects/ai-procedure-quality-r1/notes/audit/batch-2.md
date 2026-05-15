# Batch 2 Audit — Skills 7-12 (ci-cd → dataverse-create-schema)

> **Generated**: 2026-05-15
> **Auditor**: task 021 (RETRY, fast-path matching batch-6 pattern)
> **Method**: Grep-driven structural assessment; targeted reads with offset/limit; **no full-body reads of skills > 400 lines**.
> **URL ref-checks**: deferred for speed (paths only).

---

## 1. `ci-cd` (378 lines) — **frontmatter anomaly**

- **Frontmatter**: **NONE.** File opens with `# CI/CD Pipeline Skill` (line 1) then `> **Category**: Operations` + `> **Last Updated**: January 2026` (lines 3-4). No `description:`, `tags:`, `techStack:`, `appliesTo:`, or `alwaysApply:` block. Inventory anomaly #1 confirmed. Missing `exemplar` and `Last Reviewed`.
- **Structure**: 11 H2 headings — Purpose, Key Terminology, Applies When, Quick Reference, GitHub Workflows Overview (line 82 — main body), Workflow Integration Points (162), CI/CD Workflow Diagram (199), Troubleshooting (260), Required Secrets (301), Integration with Skills (330), Related Skills (342), Tips for AI (352). Clean overall shape.
- **Gotchas section**: NONE explicit, but `## Troubleshooting` (260) and `## Tips for AI` (352) serve as de-facto gotchas.
- **Goal-oriented vs prescriptive**: heavily reference-style ("document and interact with..."). Reads as a documentation skill more than an action skill.
- **Cross-refs**: 78 total references — moderate blast radius. Heavily referenced from 8 sibling skills (`adr-check`, `azure-deploy`, `dataverse-deploy`, `pull-from-github`, `push-to-github`, `task-create`), 1 trigger map entry in CLAUDE.md, and from many task POMLs across CI/CD-related projects (`ai-procedure-refactoring-r2`, `code-quality-and-assurance-r1`, etc.).
- **Path refs (spot)**: contains documentation references (architecture/INDEX.md, ci-cd-architecture.md), not concrete script paths — paths-by-reference rather than paths-to-test. `references-verified: pass-by-template`.
- **Distinct purpose**: YES — only skill describing GitHub Actions workflows; no overlap with deploy skills (which DO the deployment) or push-to-github (which submits the change).
- **Exemplar**: `none-too-volatile` (workflows evolve quarterly; no single canonical pipeline).
- **Recommended action**: `refine-in-place` — add the standard frontmatter block (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`) before the H1; add `Last Reviewed` stamp; add `exemplar: none-too-volatile`. Body at 378 lines is fine. Consider promoting "Troubleshooting" content to a proper `## Gotchas` section.

---

## 2. `code-page-deploy` (297 lines)

- **Frontmatter**: complete (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`). Missing `exemplar`. Uses `Last Updated: February 2026` (not R2 `Last Reviewed`).
- **Structure**: 10 H2 — Quick Reference, Path Map — DocumentRelationshipViewer (line 40 — uses real PCF as concrete reference), 🚨 Critical: Clear Build Cache Before EVERY Build (75 — strong directive), Two Build Pipelines (129), Deployment (192), **Anti-Patterns (DO NOT)** (233 — explicit gotchas), Troubleshooting (246), How to Add a New Code Page (262), Related Skills (277), Related ADRs (287). **Best-shaped skill in batch.**
- **Gotchas section**: YES de-facto — `## Anti-Patterns (DO NOT)` at line 233. Inventory's "no skill has Gotchas section" criterion may need to recognize this synonym (and `code-page-deploy` is one of the few that does).
- **Goal-oriented vs prescriptive**: well-balanced. Strong directive sections (cache-clear MANDATORY) backed by concrete path map.
- **Cross-refs**: 128 total references — high blast radius. Strongly referenced from 4 sibling skills (`bff-deploy`, `deploy-new-release`, `pcf-deploy`, `power-page-deploy`), 2 trigger map entries (CLAUDE.md:717, 740), and ~75 task POMLs across many UI projects. Any change must preserve the path-map + two-build-pipeline semantics.
- **Path refs (spot)**: `src/client/code-pages/` (EXISTS), `src/solutions/` (EXISTS), `scripts/Deploy-AllWebResources.ps1` (EXISTS). `references-verified: pass`.
- **Distinct purpose**: YES — only skill for React 18 standalone-dialog deployment. Clearly separated from pcf-deploy (form-bound), bff-deploy (API), power-page-deploy (Vite/Power Pages).
- **Exemplar**: real path candidate — DocumentRelationshipViewer is named as the canonical case. Recommend `exemplar: src/client/code-pages/DocumentRelationshipViewer/` if it remains the reference implementation. Otherwise `none-too-volatile`.
- **Recommended action**: `refine-in-place` — light touch. Add `exemplar:` (preferred: real path to DocumentRelationshipViewer); add `Last Reviewed` stamp; consider renaming "Anti-Patterns (DO NOT)" to "Gotchas" to match the standard heading. Body at 297 lines is well-structured.

---

## 3. `code-review` (846 lines) — **hub #3 with 242 refs; high blast radius**

- **Frontmatter**: **minimal** — only `description` + `alwaysApply: false` (placed AFTER H1, lines 3-6). Missing `tags`, `techStack`, `appliesTo`, `exemplar`. Inventory flagged this skill as inconsistent.
- **Structure**: 11 H2 — Purpose, When to Use, Inputs Required, Workflow (line 29 — **600+ line monolithic procedural body** spanning to line 649), Code Review Report (649), Conventions (722), Integration Points (746), Resources (784), Examples (800), Validation Checklist (835). The Workflow block (29-649 = 620 lines) is the bulk and contains all the substance.
- **Gotchas section**: NONE. (Grep returned 1 incidental mention only.)
- **Goal-oriented vs prescriptive**: heavily prescriptive with 600+ lines of inline procedure — large code-example density. Per audit rules (>50 lines of code examples = candidate for split), this is well above threshold.
- **Cross-refs**: **242 total references — highest in batch and #3 hub overall.** Referenced from 2 invocation sites (`adr-check`, `repo-cleanup`), 16 sibling `see_also` references, 7 trigger map entries in CLAUDE.md, settings.local.json (×2), 21 project CLAUDE.md files, 47 docs entries, and 70+ task POMLs (every `090-project-wrap-up.poml` invokes it). Any restructure MUST preserve trigger phrases and slash-command surface (`/code-review`).
- **Path refs (spot)**: workflow refers to `git diff`, generic paths; `references/` subdirectory exists (1 file per inventory). `references-verified: pass-by-template`.
- **Distinct purpose**: YES vs `adr-check` (architecture-only) — code-review is multi-dimensional (security, performance, style, ADR compliance). Differentiation explicitly called out in Purpose paragraph.
- **Exemplar**: `none-too-volatile` (review outputs are per-PR; no single canonical example).
- **Recommended action**: **`split`** — given 846 lines, 600+ in Workflow alone, and `references/` subdirectory already exists. Move the procedural Workflow body to `.claude/skills/code-review/references/review-procedure.md`. Keep in `SKILL.md`: complete frontmatter (NEW), Purpose, When to Use, Inputs Required, *summary table* of review dimensions with pointers into `references/`, Code Review Report template, Conventions, Integration Points, Validation Checklist. Target SKILL.md size: ~250-300 lines. Add `## Gotchas` during the split. Also normalize frontmatter (currently minimal). **HIGH-PRIORITY due to 242 cross-refs** — split must be tested carefully.

---

## 4. `conflict-check` (268 lines)

- **Frontmatter**: complete (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`). Missing `exemplar`. Uses `Last Updated: January 2026` (not R2 `Last Reviewed`).
- **Structure**: 8 H2 — Purpose, Applies When, Workflow (39 — main body), Quick Commands (137), Integration Points (158), Decision Tree (195), Example Output (221), Related Skills (259). Clean shape.
- **Gotchas section**: NONE (0 grep hits).
- **Goal-oriented vs prescriptive**: well-balanced. Purpose is concrete (detect file conflicts between parallel PRs); workflow is prescriptive with gh-cli commands.
- **Cross-refs**: 12 total — **lowest blast radius in batch.** Referenced from 3 sibling skills (`merge-to-master`, `project-pipeline`, `worktree-setup` ×2), 1 trigger map (CLAUDE.md:342), and 6 references in `docs/procedures/parallel-claude-sessions.md`. No project CLAUDE.md or task POML references.
- **Path refs (spot)**: uses `gh pr list`, `git diff` style commands, not file paths. `references-verified: pass-by-template`.
- **Distinct purpose**: YES — sole skill for parallel-PR conflict detection; complements `merge-to-master` and `worktree-setup` rather than overlapping.
- **Exemplar**: `none-too-volatile` (conflicts are per-session; no canonical example).
- **Recommended action**: `refine-in-place` — add `## Gotchas` (e.g., "Don't trust `gh pr list` cache; force refresh before checking"), `Last Reviewed` stamp, `exemplar: none-too-volatile`. Low-risk skill due to low blast radius.

---

## 5. `context-handoff` (352 lines)

- **Frontmatter**: complete (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`). Missing `exemplar`. Uses `Last Updated: January 2026`.
- **Structure**: 13 H2 — Purpose, When to Use, Workflow (47 — main body), **Quick Recovery (READ THIS FIRST)** (113), Full State (Detailed) (131), Quick Recovery Format (165), **Quick Recovery (READ THIS FIRST)** (176 — DUPLICATE H2 from line 113), Integration with Other Skills (196), Proactive Checkpoint Guidelines (236), Error Handling (251), Examples (263), Related Skills (316), Tips for AI (326), Post-Compaction Recovery (338). **Anomaly: duplicate `## Quick Recovery (READ THIS FIRST)` headings at lines 113 and 176.**
- **Gotchas section**: NONE explicit (0 grep hits), but `## Tips for AI` (326) and `## Error Handling` (251) serve as de-facto gotchas.
- **Goal-oriented vs prescriptive**: goal-oriented (Purpose: save state for recovery). Workflow is concrete with file-format examples.
- **Cross-refs**: 59 total references — moderate blast radius. Heavily invoked from `task-execute` (2 invocations + 15 see_also entries — context-handoff is the checkpoint mechanism used during task-execute Step 8). 10 trigger map entries in CLAUDE.md (lines 531-536, 561, 714, 787-788). Referenced from 9 project CLAUDE.md files and 19 entries in `docs/procedures/context-recovery.md`. **Critical for the task-execute orchestration loop.**
- **Path refs (spot)**: `projects/{name}/current-task.md` — templated. `references-verified: pass-by-template`.
- **Distinct purpose**: YES — sole skill for pre-compaction state save. `task-execute` calls it; not the other way around.
- **Exemplar**: `none-too-volatile` (handoffs are per-session).
- **Recommended action**: `refine-in-place` — fixes: (a) resolve duplicate `## Quick Recovery (READ THIS FIRST)` H2 sections (lines 113 vs 176) — merge or rename one; (b) add `## Gotchas` (e.g., "Don't checkpoint after partial Edit failures; flush state first"); (c) add `Last Reviewed` stamp; (d) add `exemplar: none-too-volatile`. Body at 352 lines is fine after consolidation.

---

## 6. `dataverse-create-schema` (777 lines) — **🚨 MISSING REFERENCE SCRIPTS — flag substantive-rewrite**

- **Frontmatter**: complete (`description`, `tags`, `techStack`, `appliesTo`, `alwaysApply: false`). Frontmatter placed AFTER H1 (lines 3-9). Missing `exemplar`. No `Last Updated` or `Last Reviewed` stamp.
- **Structure**: 17 H2 — Purpose, When to Use, Pre-Validation via MCP (40), Prerequisites (71), Core Patterns (104), Attribute Type Definitions (181), Entity Operations (326), Relationship Operations (426), Global Option Set Operations (519), Publishing Customizations (576), Post-Deployment Verification via MCP (606), Script Execution Order (631), **Reference Scripts (651 — BROKEN, see below)**, Common Errors and Solutions (683), Verification (695), Integration with Other Skills (722), ADR Compliance (732), Example: Complete Entity Creation (740).
- **Gotchas section**: NONE explicit, but `## Common Errors and Solutions` (683) is a strong de-facto Gotchas section.
- **Goal-oriented vs prescriptive**: heavily prescriptive — 777 lines, ~600 of which are PowerShell code examples (Attribute Type Definitions, Entity Operations, Relationship Operations, etc.). **Far above 50-line code-example threshold** → category triggers `needs-substantive-rewrite` per audit rules.
- **Cross-refs**: only **4 total references** — lowest in batch and unusually low for a 777-line skill. Just 1 trigger map entry (CLAUDE.md:750) and 3 docs entries (DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md ×2, DATAVERSE-MCP-INTEGRATION-GUIDE.md). **The skill is huge but rarely consumed** — possibly already superseded by direct MCP tool usage. Worth questioning whether the skill is still earning its keep.
- **🚨 MISSING REFERENCE SCRIPTS (verified)**: 3 of 4 reference scripts named in `## Reference Scripts` (lines 651-680) do NOT EXIST in the repo:
  - ❌ MISSING: `projects/ai-node-playbook-builder/scripts/Deploy-PlaybookNodeSchema.ps1`
  - ❌ MISSING: `projects/ai-node-playbook-builder/scripts/Fix-PlaybookNodeAttributes.ps1`
  - ❌ MISSING: `projects/ai-node-playbook-builder/scripts/Create-NNRelationships.ps1`
  - ✅ EXISTS: `scripts/Deploy-ChartDefinitionEntity.ps1`
  This is exactly F-1 failure mode: "Skill says X but X is wrong" — broken reference paths from an archived/removed project.
- **Path refs (spot)**: 75% broken (3/4 reference scripts missing). `references-verified: FAIL`.
- **Distinct purpose**: YES vs `dataverse-deploy` (solution import) and MCP tools (per-record CRUD) — schema-create-schema covers metadata-creation patterns. But low cross-ref count suggests overlap with MCP tools in practice.
- **Exemplar**: `none-too-volatile` (and certainly NOT any of the named missing scripts).
- **Recommended action**: **`needs-substantive-rewrite`** — multiple drivers: (a) 3 of 4 reference scripts MISSING (broken `## Reference Scripts` section must be reauthored against current repo state); (b) 777 lines with ~600 of PowerShell code examples — far over split-recommendation threshold; (c) lowest cross-ref count in batch (4) — possibly stale skill; (d) overlap with MCP tools needs reassessment per `CLAUDE.md` MCP integration section (lists 12 dataverse MCP tools that may now cover this skill's purpose). Recommend: (1) verify with humans whether skill is still needed given MCP tools, (2) if kept, split code examples into `references/`, repair the Reference Scripts section, add explicit "When to use MCP tools vs this skill" guidance.

---

## Batch 2 Summary

**Count by recommended action:**

| Action | Count | Skills |
|---|---|---|
| `refine-in-place` | 4 | ci-cd, code-page-deploy, conflict-check, context-handoff |
| `split` | 1 | code-review (242 refs — high-priority, must preserve trigger surface) |
| `needs-substantive-rewrite` | 1 | dataverse-create-schema (missing reference scripts + 777 LOC + low cross-ref) |
| `archive` / `merge` / `leave-as-is` / `consolidate` | 0 | — |

**Top concerns (called out):**

1. **🚨 `dataverse-create-schema` — broken reference scripts.** 3 of 4 scripts named in `## Reference Scripts` do NOT exist:
   - `projects/ai-node-playbook-builder/scripts/Deploy-PlaybookNodeSchema.ps1` MISSING
   - `projects/ai-node-playbook-builder/scripts/Fix-PlaybookNodeAttributes.ps1` MISSING
   - `projects/ai-node-playbook-builder/scripts/Create-NNRelationships.ps1` MISSING
   - Only `scripts/Deploy-ChartDefinitionEntity.ps1` exists.
   This is a textbook F-1 failure mode ("Skill says X but X is wrong"). Combined with the skill's unusually low cross-ref count (4 total), this skill needs human review to decide: rewrite vs. archive vs. merge into MCP-tool guidance.

2. **`code-review` is hub #3 with 242 references** — any split must be carefully coordinated with all 21 project CLAUDE.md files, 7 CLAUDE.md trigger entries, 16 sibling skills, and 70+ task POMLs. The `references/` subdirectory already exists (1 file per inventory), so the split mechanism is in place.

3. **`ci-cd` has no frontmatter at all** — already known per inventory anomaly #1. Adds frontmatter as part of `refine-in-place`.

4. **`context-handoff` has duplicate `## Quick Recovery (READ THIS FIRST)` H2 sections** at lines 113 and 176 — structural anomaly to fix during refine-in-place.

5. **`code-page-deploy` has the best shape in batch** — explicit `## Anti-Patterns (DO NOT)` section already serves as Gotchas. Suggest renaming to `## Gotchas` to standardize the heading convention.

**Frontmatter completeness (across batch):** complete=4, minimal=1 (code-review), none=1 (ci-cd). `Last Reviewed` R2 stamp: 0/6. `exemplar:` field: 0/6.

**`Last Updated` vs `Last Reviewed`:** 4 of 6 skills carry `Last Updated: January 2026` or `February 2026`; `code-review` carries `Last Updated: March 13, 2026`; `dataverse-create-schema` has no stamp at all. None use the R2 `Last Reviewed: YYYY-MM-DD` convention.
