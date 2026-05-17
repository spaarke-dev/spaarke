# Skills Inventory — `.claude/skills/`

> **Generated**: 2026-05-14
> **Task**: 001-inventory-skills (Phase 0, Wave 0-A)
> **Scope**: Read-only inventory of every skill folder containing a `SKILL.md` file
> **Excludes**: `_archived/`, `_templates/`
> **Source**: `c:\code_files\spaarke\.claude\skills\`

---

## Summary

| Metric | Count |
|---|---|
| **Total skills inventoried** | 44 |
| Skills missing a `## Gotchas` (or equivalent heading) section | 44 |
| Skills missing a `Last Reviewed` stamp (R2 audit-stamp convention) | 43 |
| Skills missing an `exemplar:` frontmatter field | 44 |
| Skills over 200 lines | 39 |
| Skills over 400 lines | 19 |

**Notable observations**:
- Only `doc-drift-audit` carries the R2-style `> **Last Reviewed**: YYYY-MM-DD` stamp at the top of the body. All other skills use a `Last Updated:` line (different convention) — neither is the same as the audit-stamp convention the R2 refactoring established for `docs/architecture/`, `docs/standards/`, etc.
- **No skill** has a `## Gotchas` (or similar) section as a proper heading. The string "gotchas" appears in three skills (`project-setup`, `task-create`, `task-execute`) only as incidental references inside template content or comments — not as a dedicated section.
- **No skill** uses an `exemplar:` frontmatter field. The R1 design calls for opt-in exemplar declarations (`exemplar: <path>` or `exemplar: none-too-volatile`); none exist yet.
- **No skill** has a `references/`, `examples/`, or `scripts/` subdirectory, with two exceptions: `adr-check` and `code-review` each have a `references/` directory.
- **`ci-cd`** has no frontmatter at all (no `description:` line) — it begins directly with `# CI/CD Pipeline Skill`. This is an outlier and an anomaly worth flagging.
- Several skills (e.g. `adr-check`, `code-review`, `dev-cleanup`, `pull-from-github`, `push-to-github`, `repo-cleanup`, `ribbon-edit`) have `description:` and `alwaysApply:` but lack `tags:` / `techStack:` / `appliesTo:`.
- **`power-page-deploy`** has only `description:` (no other frontmatter fields).
- The largest skill is `task-execute` at **1084 lines**, followed by `project-pipeline` at **943 lines** and `code-review` at **846 lines** — all well over the 200-line guidance threshold mentioned in the POML.

---

## Inventory Table

| # | Skill | Lines | desc | tags | techStack | appliesTo | alwaysApply | exemplar | Last Reviewed | Gotchas section | refs/ | examples/ | scripts/ | description (first line) |
|---|---|---:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|---|
| 1 | add-reference-to-index | 171 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Index golden reference documents into the spaarke-rag-references AI Search index |
| 2 | adr-aware | 270 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Automatically load and apply relevant ADRs when creating or modifying resources |
| 3 | adr-check | 270 | Y | N | N | N | Y | N | N | N | Y (1) | N | N | Validate code changes against Architecture Decision Records (ADRs) |
| 4 | ai-procedure-maintenance | 363 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Maintain and update AI coding procedures when new elements are added |
| 5 | azure-deploy | 615 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Deploy Azure infrastructure, BFF API, Static Web Apps, and configure App Service settings |
| 6 | bff-deploy | 298 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Deploy the BFF API (Sprk.Bff.Api) to Azure App Service using the deployment script |
| 7 | ci-cd | 378 | N | N | N | N | N | N | N | N | N | N | N | _(no frontmatter — see Anomalies)_ |
| 8 | code-page-deploy | 297 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Build and deploy React Code Page web resources to Dataverse |
| 9 | code-review | 846 | Y | N | N | N | Y | N | N | N | Y (1) | N | N | Comprehensive code review covering security, performance, style, and ADR compliance |
| 10 | conflict-check | 268 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Detect potential file conflicts between active PRs and current work |
| 11 | context-handoff | 352 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Save working state before compaction or session end for reliable recovery |
| 12 | dataverse-create-schema | 777 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Create or update Dataverse schema components (entities, attributes, relationships, option sets) |
| 13 | dataverse-deploy | 802 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Deploy solutions, PCF controls, and web resources to Dataverse using PAC CLI |
| 14 | deploy-new-release | 457 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Interactive production release deployment — pre-flight, environment selection, change detection |
| 15 | design-to-spec | 727 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Transform human design documents into AI-optimized spec.md files |
| 16 | dev-cleanup | 314 | Y | N | N | N | Y | N | N | N | N | N | N | Clean up local development environment caches (Azure CLI, NuGet, npm, Git credentials) |
| 17 | doc-drift-audit | 229 | Y | Y | Y | Y | Y | N | **Y** | N | N | N | N | Detect documentation drift — stale references in docs and .claude/ that no longer match code |
| 18 | docs-architecture | 221 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Draft or update an architecture document for a Spaarke subsystem |
| 19 | docs-data-model | 95 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Draft or update a data model document — entity schemas, relationships, field mappings, JSON |
| 20 | docs-guide | 263 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Draft or update an operational guide for a Spaarke functional module |
| 21 | docs-procedures | 91 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Draft or update a development procedure — testing strategy, CI/CD workflow, code review checklists |
| 22 | docs-standards | 82 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Draft or update a standards document — cross-cutting coding conventions, anti-patterns |
| 23 | jps-action-create | 282 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Create a new JPS definition for an Analysis Action |
| 24 | jps-playbook-audit | 273 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Audit existing playbooks against current scope catalog and standards |
| 25 | jps-playbook-design | 516 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Design and deploy a complete AI playbook — from requirements through Dataverse deployment |
| 26 | jps-scope-refresh | 127 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Refresh the scope & model index from Dataverse so Claude Code has the latest catalog |
| 27 | jps-validate | 265 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Validate a JPS JSON file against schema and test rendering |
| 28 | merge-to-master | 359 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Merge completed branch work into master with safety checks and build verification |
| 29 | pcf-deploy | 410 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Build, pack, and deploy PCF controls to Dataverse via solution ZIP import |
| 30 | power-page-deploy | 278 | Y | N | N | N | N | N | N | N | N | N | N | Build and deploy a Vite/React SPA to Power Pages as a Code Site |
| 31 | project-continue | 530 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Continue a project after PR merge or new session with full context loading |
| 32 | project-pipeline | 943 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Automated pipeline from SPEC.md to ready-to-execute tasks — runs autonomously by default |
| 33 | project-setup | 656 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Generate project artifacts (README, PLAN, CLAUDE.md) from a design specification |
| 34 | pull-from-github | 286 | Y | N | N | N | Y | N | N | N | N | N | N | Pull latest changes from GitHub and sync local branch |
| 35 | push-to-github | 534 | Y | N | N | N | Y | N | N | N | N | N | N | Commit changes and push to GitHub following Spaarke git conventions |
| 36 | repo-cleanup | 502 | Y | N | N | N | Y | N | N | N | N | N | N | Repository hygiene audit — validates structure compliance and removes ephemeral files |
| 37 | ribbon-edit | 414 | Y | N | N | N | Y | N | N | N | N | N | N | Edit Dataverse ribbon customizations via solution export/import |
| 38 | script-aware | 304 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Discover and use existing scripts from the script library before writing new code |
| 39 | spaarke-conventions | 353 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Apply Spaarke coding standards from CLAUDE.md — naming, structure, file organization |
| 40 | task-create | 747 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Decompose a project plan into numbered POML task files for systematic AI-assisted execution |
| 41 | task-execute | 1084 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Execute a POML task file with proper context loading and verification |
| 42 | ui-test | 552 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Browser-based UI testing using Claude Code Chrome integration for PCF and model-driven apps |
| 43 | worktree-setup | 733 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Create and manage git worktrees for parallel project development |
| 44 | worktree-sync | 417 | Y | Y | Y | Y | Y | N | N | N | N | N | N | Guarantee worktree is fully synchronized — committed, pushed, merged to master, updated |

---

## Legend

- **Lines**: Total line count of `SKILL.md` (`wc -l`), body + frontmatter combined. Not body-only — see Anomalies note.
- **desc / tags / techStack / appliesTo / alwaysApply / exemplar**: Y if a key with that exact name (case-insensitive) appears in the first 25 lines of the file (covers both top-of-file and after-H1 frontmatter styles).
- **Last Reviewed**: Y only if the line `> **Last Reviewed**: ...` appears in the header area (R2 audit-stamp convention). The older `Last Updated:` convention is **not** counted as Last Reviewed.
- **Gotchas section**: Y only if a markdown heading line (`## Gotchas` or `# Gotchas`, case-insensitive) exists. Incidental mentions of the word "gotchas" inside body text or templates are **not** counted.
- **refs/ / examples/ / scripts/**: Y(N) means the subdirectory exists and contains N files (recursive count, top-level only).

---

## Anomalies

1. **`ci-cd` has no frontmatter at all.** The file begins with `# CI/CD Pipeline Skill` and never declares `description:`, `tags:`, `techStack:`, `appliesTo:`, or `alwaysApply:`. This is the only skill without any frontmatter. (Surfaced for Phase 2a quality audit.)

2. **`Last Reviewed` vs `Last Updated`.** Most skills carry a `> **Last Updated**: <month/year>` line in the header. Only `doc-drift-audit` uses the R2 audit-stamp convention `> **Last Reviewed**: YYYY-MM-DD`. The Phase 2a audit will need to decide whether to (a) treat `Last Updated` as a synonym, (b) require all skills to migrate to `Last Reviewed`, or (c) leave skills exempt from the stamp convention.

3. **Line counts include frontmatter.** The POML asks for body lines specifically. The counts in this table use `wc -l` on the whole file, which includes the 1–10 frontmatter lines. For most skills (200+ lines), the rounding error is negligible (<5%). Skills near the 200-line threshold (`docs-data-model` 95, `docs-procedures` 91, `docs-standards` 82, `jps-scope-refresh` 127, `add-reference-to-index` 171) are clearly well under 200 either way; nothing flips classification.

4. **Only two skills have `references/` subdirectories** (`adr-check`, `code-review`), each containing 1 file. **No skill has `examples/` or `scripts/` subdirectories.** This is consistent with the R1 design's observation that exemplars/references are currently sparse and need an opt-in framework.

5. **Two info files in the parent directory are not counted as skills.** `INDEX.md`, `RIGOR-LEVEL-IMPLEMENTATION.md`, and `SKILL-INTERACTION-GUIDE.md` are top-level reference docs under `.claude/skills/` and are excluded from the inventory (they are not skills).

6. **Inconsistent frontmatter shape.** 8 skills (`adr-check`, `code-review`, `ci-cd`, `dev-cleanup`, `power-page-deploy`, `pull-from-github`, `push-to-github`, `repo-cleanup`, `ribbon-edit`) have a reduced frontmatter (typically just `description:` + `alwaysApply:`), missing `tags:`, `techStack:`, and `appliesTo:`. These are candidates for normalization in Phase 2b.

---

## Files Read (Read-Only)

- All 44 `SKILL.md` files under `c:\code_files\spaarke\.claude\skills\<skill>/`
- No files were modified.
- No files were written outside `projects/ai-procedure-quality-r1/notes/inventory/`.
