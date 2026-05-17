# Skill Cross-Reference Map

> **Generated**: 2026-05-14
> **Task**: 006-build-skill-crossref-map (Phase 0, Wave 0-B)
> **Project**: ai-procedure-quality-r1
> **Inputs**: notes/inventory/skills.md (44 skills), 7 cross-reference surfaces
> **Companion**: skill-cross-refs.json (machine-readable, consumed by Phase 4a validators)

---

## Summary

| Metric | Count |
|---|---|
| Skills mapped | 44 |
| Skills existing (`SKILL.md` present) | 44 |
| Total reference rows across all 7 surfaces | 3685 |
| Orphan skills (0 references anywhere) | 1 |
| Broken references (name mentioned but skill doesn't exist) | 0 |
| Task POMLs scanned (surface 7) | 857 |

**Hub skills** (top 5 most-referenced — changes have high blast radius):

- **task-execute** — 1234 references
- **dataverse-deploy** — 269 references
- **code-review** — 242 references
- **repo-cleanup** — 215 references
- **adr-check** — 209 references

---

## How To Read This Map

Each row shows reference counts per surface. Cell values are line-anchored paths (`file:line`).
Surfaces:

1. **Invocations** — Inside another `SKILL.md`, an explicit invoke/call/use phrase referring to this skill.
2. **See-Also** — Inside another `SKILL.md`, a generic reference OR appearance in a Related/References section.
3. **Trigger Map** — Lines in root `CLAUDE.md` (trigger phrase tables, auto-detection rules, skill lists).
4. **Settings** — `.claude/settings.json` or `.claude/settings.local.json` (permission allow-lists, hook commands).
5. **Project CLAUDE.md** — Per-project context files under `projects/*/CLAUDE.md`.
6. **Docs** — `docs/**` references (architecture, ADR, guide, procedure files).
7. **Task POMLs** — `projects/*/tasks/*.poml` references (in `<knowledge>`, `<steps>`, `<tools>`, etc.).

**Caveat**: Some skill names overlap common English (`code-review`, `ci-cd`, `ui-test`, `task-execute`).
The matcher uses literal word-boundary matching, so phrases like 'code review the PR' DO match `code-review`.
Review the high-count surfaces for these skills before drawing conclusions.

---

## Cross-Reference Matrix (Counts)

| # | Skill | Exists | 1: Invocations | 2: See-Also | 3: Trigger Map | 4: Settings | 5: Project CLAUDE | 6: Docs | 7: Task POMLs | **Total** |
|---|---|:-:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | `add-reference-to-index` | Y | 0 | 0 | 0 | 0 | 0 | 0 | 0 | **0** |
| 2 | `adr-aware` | Y | 0 | 26 | 3 | 0 | 13 | 2 | 0 | **44** |
| 3 | `adr-check` | Y | 0 | 37 | 7 | 0 | 25 | 43 | 97 | **209** |
| 4 | `ai-procedure-maintenance` | Y | 0 | 1 | 2 | 0 | 0 | 2 | 0 | **5** |
| 5 | `azure-deploy` | Y | 0 | 6 | 3 | 2 | 0 | 5 | 73 | **89** |
| 6 | `bff-deploy` | Y | 0 | 7 | 1 | 1 | 2 | 9 | 57 | **77** |
| 7 | `ci-cd` | Y | 0 | 8 | 1 | 0 | 1 | 11 | 57 | **78** |
| 8 | `code-page-deploy` | Y | 0 | 4 | 2 | 0 | 4 | 8 | 110 | **128** |
| 9 | `code-review` | Y | 2 | 24 | 7 | 2 | 23 | 48 | 136 | **242** |
| 10 | `conflict-check` | Y | 0 | 4 | 1 | 0 | 0 | 7 | 0 | **12** |
| 11 | `context-handoff` | Y | 2 | 17 | 10 | 2 | 9 | 19 | 0 | **59** |
| 12 | `dataverse-create-schema` | Y | 0 | 0 | 1 | 0 | 0 | 3 | 0 | **4** |
| 13 | `dataverse-deploy` | Y | 4 | 38 | 4 | 2 | 6 | 15 | 200 | **269** |
| 14 | `deploy-new-release` | Y | 0 | 0 | 3 | 0 | 1 | 5 | 20 | **29** |
| 15 | `design-to-spec` | Y | 0 | 6 | 7 | 2 | 3 | 13 | 0 | **31** |
| 16 | `dev-cleanup` | Y | 0 | 5 | 2 | 0 | 0 | 1 | 0 | **8** |
| 17 | `doc-drift-audit` | Y | 0 | 0 | 1 | 0 | 0 | 4 | 0 | **5** |
| 18 | `docs-architecture` | Y | 0 | 5 | 0 | 0 | 2 | 1 | 163 | **171** |
| 19 | `docs-data-model` | Y | 0 | 0 | 0 | 0 | 2 | 0 | 35 | **37** |
| 20 | `docs-guide` | Y | 0 | 6 | 0 | 0 | 2 | 1 | 50 | **59** |
| 21 | `docs-procedures` | Y | 0 | 0 | 0 | 0 | 2 | 0 | 20 | **22** |
| 22 | `docs-standards` | Y | 0 | 1 | 0 | 0 | 2 | 0 | 15 | **18** |
| 23 | `jps-action-create` | Y | 0 | 13 | 2 | 0 | 0 | 2 | 0 | **17** |
| 24 | `jps-playbook-audit` | Y | 0 | 0 | 2 | 0 | 0 | 1 | 0 | **3** |
| 25 | `jps-playbook-design` | Y | 0 | 11 | 2 | 0 | 0 | 4 | 0 | **17** |
| 26 | `jps-scope-refresh` | Y | 0 | 6 | 2 | 0 | 0 | 2 | 0 | **10** |
| 27 | `jps-validate` | Y | 0 | 5 | 2 | 0 | 0 | 0 | 5 | **12** |
| 28 | `merge-to-master` | Y | 0 | 20 | 4 | 0 | 4 | 4 | 30 | **62** |
| 29 | `pcf-deploy` | Y | 0 | 5 | 1 | 1 | 0 | 10 | 22 | **39** |
| 30 | `power-page-deploy` | Y | 0 | 1 | 3 | 0 | 0 | 2 | 1 | **7** |
| 31 | `project-continue` | Y | 0 | 14 | 3 | 2 | 3 | 11 | 0 | **33** |
| 32 | `project-pipeline` | Y | 0 | 62 | 12 | 2 | 10 | 27 | 3 | **116** |
| 33 | `project-setup` | Y | 0 | 16 | 4 | 0 | 0 | 4 | 0 | **24** |
| 34 | `pull-from-github` | Y | 0 | 8 | 2 | 2 | 0 | 2 | 0 | **14** |
| 35 | `push-to-github` | Y | 0 | 24 | 2 | 2 | 2 | 14 | 16 | **60** |
| 36 | `repo-cleanup` | Y | 0 | 17 | 3 | 0 | 0 | 19 | 176 | **215** |
| 37 | `ribbon-edit` | Y | 0 | 2 | 3 | 2 | 5 | 1 | 61 | **74** |
| 38 | `script-aware` | Y | 0 | 4 | 2 | 0 | 1 | 0 | 0 | **7** |
| 39 | `spaarke-conventions` | Y | 0 | 7 | 2 | 0 | 1 | 18 | 3 | **31** |
| 40 | `task-create` | Y | 0 | 37 | 4 | 0 | 7 | 3 | 15 | **66** |
| 41 | `task-execute` | Y | 3 | 63 | 28 | 2 | 279 | 22 | 837 | **1234** |
| 42 | `ui-test` | Y | 1 | 8 | 0 | 0 | 0 | 8 | 6 | **23** |
| 43 | `worktree-setup` | Y | 0 | 3 | 2 | 2 | 0 | 5 | 0 | **12** |
| 44 | `worktree-sync` | Y | 0 | 8 | 3 | 0 | 0 | 2 | 0 | **13** |

---

## Detailed References (Per Skill)

Path samples are truncated to first 5 with overflow count noted.

### 1. `add-reference-to-index` — 0 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 0 | — |
| 7: Task POMLs | 0 | — |

### 2. `adr-aware` — 44 total references

> **Ambiguity note**: Specific name; less ambiguity

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 26 | .claude/skills/ai-procedure-maintenance/SKILL.md:135<br>.claude/skills/ai-procedure-maintenance/SKILL.md:171<br>.claude/skills/ai-procedure-maintenance/SKILL.md:294<br>.claude/skills/ai-procedure-maintenance/SKILL.md:295<br>.claude/skills/ai-procedure-maintenance/SKILL.md:309<br>… (+21 more, total 26) |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:500<br>CLAUDE.md:733<br>CLAUDE.md:759 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 13 | projects/ai-sprk-chat-workspace-companion/CLAUDE.md:45<br>projects/sdap-office-integration/CLAUDE.md:43<br>projects/sdap-secure-project-module/CLAUDE.md:43<br>projects/spaarke-daily-update-service/CLAUDE.md:41<br>projects/x-ai-RAG-pipeline/CLAUDE.md:43<br>… (+8 more, total 13) |
| 6: Docs | 2 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:77<br>docs/procedures/context-recovery.md:559 |
| 7: Task POMLs | 0 | — |

### 3. `adr-check` — 209 total references

> **Ambiguity note**: Specific name; less ambiguity

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 37 | .claude/skills/adr-aware/SKILL.md:13<br>.claude/skills/adr-aware/SKILL.md:17<br>.claude/skills/adr-aware/SKILL.md:181<br>.claude/skills/adr-aware/SKILL.md:182<br>.claude/skills/adr-aware/SKILL.md:266<br>… (+32 more, total 37) |
| 3: Root CLAUDE.md | 7 | CLAUDE.md:126<br>CLAUDE.md:350<br>CLAUDE.md:433<br>CLAUDE.md:599<br>CLAUDE.md:704<br>… (+2 more, total 7) |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 25 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:69<br>projects/ai-procedure-quality-r1/CLAUDE.md:77<br>projects/code-quality-and-assurance-r1/CLAUDE.md:53<br>projects/events-workspace-apps-UX-r1/CLAUDE.md:177<br>projects/sdap-office-integration/CLAUDE.md:75<br>… (+20 more, total 25) |
| 6: Docs | 43 | docs/adr/ADR-VALIDATION-PROCESS.md:107<br>docs/adr/ADR-VALIDATION-PROCESS.md:118<br>docs/adr/ADR-VALIDATION-PROCESS.md:231<br>docs/adr/ADR-VALIDATION-PROCESS.md:237<br>docs/adr/ADR-VALIDATION-PROCESS.md:343<br>… (+38 more, total 43) |
| 7: Task POMLs | 97 | projects/ai-sprk-chat-platform-enhancement-r2/tasks/090-project-wrap-up.poml:17<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/090-project-wrap-up.poml:23<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/090-project-wrap-up.poml:32<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/090-project-wrap-up.poml:37<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/090-project-wrap-up.poml:50<br>… (+92 more, total 97) |

### 4. `ai-procedure-maintenance` — 5 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 1 | .claude/skills/doc-drift-audit/SKILL.md:44 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:711<br>CLAUDE.md:784 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:240<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:502 |
| 7: Task POMLs | 0 | — |

### 5. `azure-deploy` — 89 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 6 | .claude/skills/bff-deploy/SKILL.md:295<br>.claude/skills/ci-cd/SKILL.md:336<br>.claude/skills/ci-cd/SKILL.md:346<br>.claude/skills/deploy-new-release/SKILL.md:430<br>.claude/skills/dev-cleanup/SKILL.md:302<br>… (+1 more, total 6) |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:706<br>CLAUDE.md:736<br>CLAUDE.md:778 |
| 4: Settings | 2 | .claude/settings.local.json:288<br>.claude/settings.local.json:289 |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 5 | docs/guides/CONFIGURATION-MATRIX.md:360<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:27<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:289<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:419<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:334 |
| 7: Task POMLs | 73 | projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:30<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:45<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:30<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:35<br>projects/sdap-SPE-admin-app/tasks/072-phase2-deploy.poml:19<br>… (+68 more, total 73) |

### 6. `bff-deploy` — 77 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 7 | .claude/skills/code-page-deploy/SKILL.md:282<br>.claude/skills/code-page-deploy/SKILL.md:297<br>.claude/skills/code-page-deploy/SKILL.md:35<br>.claude/skills/deploy-new-release/SKILL.md:360<br>.claude/skills/deploy-new-release/SKILL.md:428<br>… (+2 more, total 7) |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:738 |
| 4: Settings | 1 | .claude/settings.local.json:405 |
| 5: Project CLAUDE.md | 2 | projects/ai-sprk-chat-workspace-companion/CLAUDE.md:150<br>projects/x-ai-semantic-search-ui-r3/CLAUDE.md:230 |
| 6: Docs | 9 | docs/guides/DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md:185<br>docs/guides/DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md:194<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:23<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:33<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:415<br>… (+4 more, total 9) |
| 7: Task POMLs | 57 | projects/ai-procedure-quality-r1/tasks/013-create-failure-modes-catalog.poml:39<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:26<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:45<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:26<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:35<br>… (+52 more, total 57) |

### 7. `ci-cd` — 78 total references

> **Ambiguity note**: Common term; many docs reference 'CI/CD' generally not the skill

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 8 | .claude/skills/adr-check/SKILL.md:194<br>.claude/skills/azure-deploy/SKILL.md:524<br>.claude/skills/dataverse-deploy/SKILL.md:685<br>.claude/skills/pull-from-github/SKILL.md:269<br>.claude/skills/push-to-github/SKILL.md:385<br>… (+3 more, total 8) |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:839 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 1 | projects/code-quality-and-assurance-r1/CLAUDE.md:59 |
| 6: Docs | 11 | docs/architecture/INDEX.md:104<br>docs/architecture/ci-cd-architecture.md:167<br>docs/guides/REPOSITORY-NAVIGATION-GUIDE.md:31<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:456<br>docs/procedures/DEPENDENCY-MANAGEMENT.md:209<br>… (+6 more, total 11) |
| 7: Task POMLs | 57 | projects/ai-procedure-quality-r1/tasks/003-inventory-workflows.poml:6<br>projects/ai-procedure-quality-r1/tasks/070-diagnose-failing-workflows.poml:6<br>projects/ai-procedure-quality-r1/tasks/071-add-actionlint.poml:6<br>projects/ai-procedure-quality-r1/tasks/072-pin-actions-to-shas.poml:6<br>projects/ai-procedure-refactoring-r2/tasks/051-create-ci-cd-architecture.poml:13<br>… (+52 more, total 57) |

### 8. `code-page-deploy` — 128 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 4 | .claude/skills/bff-deploy/SKILL.md:298<br>.claude/skills/deploy-new-release/SKILL.md:431<br>.claude/skills/pcf-deploy/SKILL.md:395<br>.claude/skills/power-page-deploy/SKILL.md:40 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:717<br>CLAUDE.md:740 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 4 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:67<br>projects/ai-sprk-chat-workspace-companion/CLAUDE.md:150<br>projects/x-ai-semantic-search-ui-r3/CLAUDE.md:227<br>projects/x-ai-spaarke-platform-enhancments-r2/CLAUDE.md:56 |
| 6: Docs | 8 | docs/architecture/code-pages-architecture.md:110<br>docs/architecture/ui-dialog-shell-architecture.md:116<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:177<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:25<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:417<br>… (+3 more, total 8) |
| 7: Task POMLs | 110 | projects/ai-analysis-workspace-sprkchat-integration/tasks/042-update-deployment-scripts.poml:15<br>projects/ai-analysis-workspace-sprkchat-integration/tasks/042-update-deployment-scripts.poml:19<br>projects/ai-analysis-workspace-sprkchat-integration/tasks/042-update-deployment-scripts.poml:25<br>projects/ai-analysis-workspace-sprkchat-integration/tasks/042-update-deployment-scripts.poml:32<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:28<br>… (+105 more, total 110) |

### 9. `code-review` — 242 total references

> **Ambiguity note**: Common term; many docs reference 'code review' generally not the skill

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 2 | .claude/skills/adr-check/SKILL.md:25<br>.claude/skills/repo-cleanup/SKILL.md:29 |
| 2: See-Also (other SKILL.md) | 24 | .claude/skills/adr-aware/SKILL.md:177<br>.claude/skills/adr-check/SKILL.md:192<br>.claude/skills/ci-cd/SKILL.md:338<br>.claude/skills/doc-drift-audit/SKILL.md:210<br>.claude/skills/doc-drift-audit/SKILL.md:41<br>… (+19 more, total 24) |
| 3: Root CLAUDE.md | 7 | CLAUDE.md:126<br>CLAUDE.md:349<br>CLAUDE.md:433<br>CLAUDE.md:599<br>CLAUDE.md:703<br>… (+2 more, total 7) |
| 4: Settings | 2 | .claude/settings.local.json:314<br>.claude/settings.local.json:315 |
| 5: Project CLAUDE.md | 23 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:68<br>projects/ai-procedure-quality-r1/CLAUDE.md:76<br>projects/code-quality-and-assurance-r1/CLAUDE.md:52<br>projects/events-workspace-apps-UX-r1/CLAUDE.md:177<br>projects/sdap-office-integration/CLAUDE.md:75<br>… (+18 more, total 23) |
| 6: Docs | 48 | docs/architecture/ci-cd-architecture.md:24<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:335<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:336<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:349<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:406<br>… (+43 more, total 48) |
| 7: Task POMLs | 136 | projects/ai-procedure-quality-r1/tasks/003-inventory-workflows.poml:27<br>projects/ai-procedure-quality-r1/tasks/075-audit-required-checks.poml:27<br>projects/ai-procedure-refactoring-r2/tasks/053-create-code-review-by-module.poml:14<br>projects/ai-procedure-refactoring-r2/tasks/053-create-code-review-by-module.poml:17<br>projects/ai-procedure-refactoring-r2/tasks/053-create-code-review-by-module.poml:26<br>… (+131 more, total 136) |

### 10. `conflict-check` — 12 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 4 | .claude/skills/merge-to-master/SKILL.md:340<br>.claude/skills/project-pipeline/SKILL.md:887<br>.claude/skills/worktree-setup/SKILL.md:719<br>.claude/skills/worktree-setup/SKILL.md:729 |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:342 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 7 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:472<br>docs/procedures/parallel-claude-sessions.md:10<br>docs/procedures/parallel-claude-sessions.md:264<br>docs/procedures/parallel-claude-sessions.md:270<br>docs/procedures/parallel-claude-sessions.md:475<br>… (+2 more, total 7) |
| 7: Task POMLs | 0 | — |

### 11. `context-handoff` — 59 total references

> **Ambiguity note**: Specific name; less ambiguity

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 2 | .claude/skills/task-execute/SKILL.md:517<br>.claude/skills/task-execute/SKILL.md:861 |
| 2: See-Also (other SKILL.md) | 17 | .claude/skills/task-execute/SKILL.md:1045<br>.claude/skills/task-execute/SKILL.md:24<br>.claude/skills/task-execute/SKILL.md:509<br>.claude/skills/task-execute/SKILL.md:535<br>.claude/skills/task-execute/SKILL.md:536<br>… (+12 more, total 17) |
| 3: Root CLAUDE.md | 10 | CLAUDE.md:531<br>CLAUDE.md:532<br>CLAUDE.md:533<br>CLAUDE.md:534<br>CLAUDE.md:535<br>… (+5 more, total 10) |
| 4: Settings | 2 | .claude/settings.local.json:294<br>.claude/settings.local.json:295 |
| 5: Project CLAUDE.md | 9 | projects/ai-procedure-quality-r1/CLAUDE.md:75<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:120<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:121<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:122<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:123<br>… (+4 more, total 9) |
| 6: Docs | 19 | docs/procedures/context-recovery.md:10<br>docs/procedures/context-recovery.md:24<br>docs/procedures/context-recovery.md:27<br>docs/procedures/context-recovery.md:354<br>docs/procedures/context-recovery.md:451<br>… (+14 more, total 19) |
| 7: Task POMLs | 0 | — |

### 12. `dataverse-create-schema` — 4 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:750 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 3 | docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md:5<br>docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md:758<br>docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:187 |
| 7: Task POMLs | 0 | — |

### 13. `dataverse-deploy` — 269 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 4 | .claude/skills/azure-deploy/SKILL.md:23<br>.claude/skills/azure-deploy/SKILL.md:24<br>.claude/skills/azure-deploy/SKILL.md:26<br>.claude/skills/azure-deploy/SKILL.md:591 |
| 2: See-Also (other SKILL.md) | 38 | .claude/skills/azure-deploy/SKILL.md:522<br>.claude/skills/bff-deploy/SKILL.md:296<br>.claude/skills/ci-cd/SKILL.md:337<br>.claude/skills/ci-cd/SKILL.md:347<br>.claude/skills/code-page-deploy/SKILL.md:283<br>… (+33 more, total 38) |
| 3: Root CLAUDE.md | 4 | CLAUDE.md:436<br>CLAUDE.md:707<br>CLAUDE.md:735<br>CLAUDE.md:779 |
| 4: Settings | 2 | .claude/settings.local.json:224<br>.claude/settings.local.json:93 |
| 5: Project CLAUDE.md | 6 | projects/x-ai-document-intelligence-r1/CLAUDE.md:44<br>projects/x-ai-document-intelligence-r2/CLAUDE.md:28<br>projects/x-ai-document-intelligence-r2/CLAUDE.md:37<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:72<br>projects/x-ai-spaarke-platform-enhancments-r2/CLAUDE.md:56<br>… (+1 more, total 6) |
| 6: Docs | 15 | docs/adr/ADR-022-pcf-platform-libraries.md:292<br>docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:188<br>docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:199<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:252<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:26<br>… (+10 more, total 15) |
| 7: Task POMLs | 200 | projects/ai-procedure-quality-r1/tasks/011-create-skill-template.poml:31<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:29<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:45<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:29<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:35<br>… (+195 more, total 200) |

### 14. `deploy-new-release` — 29 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:705<br>CLAUDE.md:751<br>CLAUDE.md:777 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 1 | projects/spaarke-production-release-procedure/CLAUDE.md:6 |
| 6: Docs | 5 | docs/procedures/production-release.md:835<br>docs/procedures/production-release.md:860<br>docs/procedures/production-release.md:865<br>docs/procedures/production-release.md:876<br>docs/procedures/production-release.md:908 |
| 7: Task POMLs | 20 | projects/spaarke-production-release-procedure/tasks/030-deploy-new-release-skill.poml:15<br>projects/spaarke-production-release-procedure/tasks/030-deploy-new-release-skill.poml:16<br>projects/spaarke-production-release-procedure/tasks/030-deploy-new-release-skill.poml:28<br>projects/spaarke-production-release-procedure/tasks/030-deploy-new-release-skill.poml:29<br>projects/spaarke-production-release-procedure/tasks/030-deploy-new-release-skill.poml:3<br>… (+15 more, total 20) |

### 15. `design-to-spec` — 31 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 6 | .claude/skills/project-pipeline/SKILL.md:377<br>.claude/skills/project-pipeline/SKILL.md:378<br>.claude/skills/project-pipeline/SKILL.md:743<br>.claude/skills/project-pipeline/SKILL.md:884<br>.claude/skills/project-pipeline/SKILL.md:896<br>… (+1 more, total 6) |
| 3: Root CLAUDE.md | 7 | CLAUDE.md:1159<br>CLAUDE.md:1166<br>CLAUDE.md:118<br>CLAUDE.md:362<br>CLAUDE.md:699<br>… (+2 more, total 7) |
| 4: Settings | 2 | .claude/settings.local.json:265<br>.claude/settings.local.json:266 |
| 5: Project CLAUDE.md | 3 | projects/x-ai-RAG-pipeline/CLAUDE.md:143<br>projects/x-ai-RAG-pipeline/CLAUDE.md:144<br>projects/x-ai-RAG-pipeline/CLAUDE.md:145 |
| 6: Docs | 13 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:109<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:149<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:327<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:346<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:39<br>… (+8 more, total 13) |
| 7: Task POMLs | 0 | — |

### 16. `dev-cleanup` — 8 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 5 | .claude/skills/jps-playbook-audit/SKILL.md:247<br>.claude/skills/jps-playbook-design/SKILL.md:485<br>.claude/skills/jps-scope-refresh/SKILL.md:108<br>.claude/skills/jps-scope-refresh/SKILL.md:119<br>.claude/skills/worktree-sync/SKILL.md:371 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:715<br>CLAUDE.md:789 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 1 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:464 |
| 7: Task POMLs | 0 | — |

### 17. `doc-drift-audit` — 5 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:840 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 4 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:226<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:299<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:500<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:521 |
| 7: Task POMLs | 0 | — |

### 18. `docs-architecture` — 171 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 5 | .claude/skills/docs-data-model/SKILL.md:22<br>.claude/skills/docs-guide/SKILL.md:21<br>.claude/skills/docs-guide/SKILL.md:22<br>.claude/skills/docs-procedures/SKILL.md:22<br>.claude/skills/docs-standards/SKILL.md:21 |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-refactoring-r2/CLAUDE.md:16<br>projects/ai-procedure-refactoring-r2/CLAUDE.md:46 |
| 6: Docs | 1 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:199 |
| 7: Task POMLs | 163 | projects/ai-procedure-refactoring-r2/tasks/006-restore-component-interactions.poml:16<br>projects/ai-procedure-refactoring-r2/tasks/006-restore-component-interactions.poml:26<br>projects/ai-procedure-refactoring-r2/tasks/006-restore-component-interactions.poml:38<br>projects/ai-procedure-refactoring-r2/tasks/006-restore-component-interactions.poml:54<br>projects/ai-procedure-refactoring-r2/tasks/006-restore-component-interactions.poml:60<br>… (+158 more, total 163) |

### 19. `docs-data-model` — 37 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-refactoring-r2/CLAUDE.md:19<br>projects/ai-procedure-refactoring-r2/CLAUDE.md:49 |
| 6: Docs | 0 | — |
| 7: Task POMLs | 35 | projects/ai-procedure-refactoring-r2/tasks/004-create-entity-relationship-model.poml:16<br>projects/ai-procedure-refactoring-r2/tasks/004-create-entity-relationship-model.poml:26<br>projects/ai-procedure-refactoring-r2/tasks/004-create-entity-relationship-model.poml:35<br>projects/ai-procedure-refactoring-r2/tasks/004-create-entity-relationship-model.poml:49<br>projects/ai-procedure-refactoring-r2/tasks/004-create-entity-relationship-model.poml:55<br>… (+30 more, total 35) |

### 20. `docs-guide` — 59 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 6 | .claude/skills/docs-architecture/SKILL.md:21<br>.claude/skills/docs-architecture/SKILL.md:22<br>.claude/skills/docs-data-model/SKILL.md:21<br>.claude/skills/docs-data-model/SKILL.md:23<br>.claude/skills/docs-procedures/SKILL.md:21<br>… (+1 more, total 6) |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-refactoring-r2/CLAUDE.md:17<br>projects/ai-procedure-refactoring-r2/CLAUDE.md:47 |
| 6: Docs | 1 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:205 |
| 7: Task POMLs | 50 | projects/ai-procedure-refactoring-r2/tasks/055-create-configuration-matrix.poml:16<br>projects/ai-procedure-refactoring-r2/tasks/055-create-configuration-matrix.poml:25<br>projects/ai-procedure-refactoring-r2/tasks/055-create-configuration-matrix.poml:33<br>projects/ai-procedure-refactoring-r2/tasks/055-create-configuration-matrix.poml:48<br>projects/ai-procedure-refactoring-r2/tasks/055-create-configuration-matrix.poml:54<br>… (+45 more, total 50) |

### 21. `docs-procedures` — 22 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-refactoring-r2/CLAUDE.md:20<br>projects/ai-procedure-refactoring-r2/CLAUDE.md:50 |
| 6: Docs | 0 | — |
| 7: Task POMLs | 20 | projects/ai-procedure-refactoring-r2/tasks/005-enhance-testing-procedures.poml:16<br>projects/ai-procedure-refactoring-r2/tasks/005-enhance-testing-procedures.poml:26<br>projects/ai-procedure-refactoring-r2/tasks/005-enhance-testing-procedures.poml:37<br>projects/ai-procedure-refactoring-r2/tasks/005-enhance-testing-procedures.poml:52<br>projects/ai-procedure-refactoring-r2/tasks/005-enhance-testing-procedures.poml:58<br>… (+15 more, total 20) |

### 22. `docs-standards` — 18 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 1 | .claude/skills/docs-procedures/SKILL.md:23 |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-refactoring-r2/CLAUDE.md:18<br>projects/ai-procedure-refactoring-r2/CLAUDE.md:48 |
| 6: Docs | 0 | — |
| 7: Task POMLs | 15 | projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:16<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:24<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:37<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:50<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:56<br>… (+10 more, total 15) |

### 23. `jps-action-create` — 17 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 13 | .claude/skills/jps-playbook-audit/SKILL.md:259<br>.claude/skills/jps-playbook-design/SKILL.md:127<br>.claude/skills/jps-playbook-design/SKILL.md:28<br>.claude/skills/jps-playbook-design/SKILL.md:308<br>.claude/skills/jps-playbook-design/SKILL.md:390<br>… (+8 more, total 13) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:719<br>CLAUDE.md:791 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/guides/JPS-AUTHORING-GUIDE.md:1263<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:389 |
| 7: Task POMLs | 0 | — |

### 24. `jps-playbook-audit` — 3 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 0 | — |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:720<br>CLAUDE.md:792 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 1 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:381 |
| 7: Task POMLs | 0 | — |

### 25. `jps-playbook-design` — 17 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 11 | .claude/skills/jps-action-create/SKILL.md:161<br>.claude/skills/jps-action-create/SKILL.md:246<br>.claude/skills/jps-action-create/SKILL.md:25<br>.claude/skills/jps-action-create/SKILL.md:262<br>.claude/skills/jps-action-create/SKILL.md:268<br>… (+6 more, total 11) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:721<br>CLAUDE.md:793 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 4 | docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:189<br>docs/guides/JPS-AUTHORING-GUIDE.md:1098<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:373<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:508 |
| 7: Task POMLs | 0 | — |

### 26. `jps-scope-refresh` — 10 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 6 | .claude/skills/jps-action-create/SKILL.md:269<br>.claude/skills/jps-playbook-audit/SKILL.md:246<br>.claude/skills/jps-playbook-audit/SKILL.md:25<br>.claude/skills/jps-playbook-audit/SKILL.md:258<br>.claude/skills/jps-playbook-audit/SKILL.md:266<br>… (+1 more, total 6) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:722<br>CLAUDE.md:794 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:190<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:395 |
| 7: Task POMLs | 0 | — |

### 27. `jps-validate` — 12 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 5 | .claude/skills/jps-action-create/SKILL.md:162<br>.claude/skills/jps-action-create/SKILL.md:270<br>.claude/skills/jps-playbook-audit/SKILL.md:260<br>.claude/skills/jps-playbook-design/SKILL.md:494<br>.claude/skills/jps-scope-refresh/SKILL.md:118 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:723<br>CLAUDE.md:795 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 0 | — |
| 7: Task POMLs | 5 | projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:122<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:40<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:47<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:65<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:98 |

### 28. `merge-to-master` — 62 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 20 | .claude/skills/doc-drift-audit/SKILL.md:208<br>.claude/skills/project-continue/SKILL.md:483<br>.claude/skills/project-continue/SKILL.md:499<br>.claude/skills/project-pipeline/SKILL.md:167<br>.claude/skills/project-pipeline/SKILL.md:181<br>… (+15 more, total 20) |
| 3: Root CLAUDE.md | 4 | CLAUDE.md:716<br>CLAUDE.md:745<br>CLAUDE.md:746<br>CLAUDE.md:790 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 4 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:88<br>projects/ai-procedure-quality-r1/CLAUDE.md:79<br>projects/ai-sprk-chat-platform-enhancement-r2/CLAUDE.md:14<br>projects/ai-sprk-chat-workspace-companion/CLAUDE.md:13 |
| 6: Docs | 4 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:171<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:184<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:499<br>docs/procedures/parallel-claude-sessions.md:10 |
| 7: Task POMLs | 30 | projects/ai-analysis-workspace-sprkchat-integration/tasks/090-project-wrap-up.poml:21<br>projects/ai-analysis-workspace-sprkchat-integration/tasks/090-project-wrap-up.poml:37<br>projects/ai-analysis-workspace-sprkchat-integration/tasks/090-project-wrap-up.poml:49<br>projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:63<br>projects/ai-procedure-quality-r1/tasks/090-project-wrap-up.poml:21<br>… (+25 more, total 30) |

### 29. `pcf-deploy` — 39 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 5 | .claude/skills/bff-deploy/SKILL.md:297<br>.claude/skills/code-page-deploy/SKILL.md:281<br>.claude/skills/code-page-deploy/SKILL.md:297<br>.claude/skills/code-page-deploy/SKILL.md:34<br>.claude/skills/power-page-deploy/SKILL.md:39 |
| 3: Root CLAUDE.md | 1 | CLAUDE.md:739 |
| 4: Settings | 1 | .claude/settings.local.json:407 |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 10 | docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:24<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:416<br>docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md:97<br>docs/guides/HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md:173<br>docs/guides/PCF-DEPLOYMENT-GUIDE.md:5<br>… (+5 more, total 10) |
| 7: Task POMLs | 22 | projects/ai-procedure-quality-r1/tasks/013-create-failure-modes-catalog.poml:35<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:27<br>projects/ai-procedure-refactoring-r2/tasks/002-create-anti-patterns.poml:45<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:27<br>projects/ai-procedure-refactoring-r2/tasks/056-create-deployment-verification.poml:35<br>… (+17 more, total 22) |

### 30. `power-page-deploy` — 7 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 1 | .claude/skills/deploy-new-release/SKILL.md:432 |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:718<br>CLAUDE.md:741<br>CLAUDE.md:780 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md:392<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:359 |
| 7: Task POMLs | 1 | projects/x-ui-dialog-shell-standardization/tasks/034-deploy-verify-phase4.poml:20 |

### 31. `project-continue` — 33 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 14 | .claude/skills/context-handoff/SKILL.md:212<br>.claude/skills/context-handoff/SKILL.md:228<br>.claude/skills/context-handoff/SKILL.md:231<br>.claude/skills/context-handoff/SKILL.md:320<br>.claude/skills/context-handoff/SKILL.md:346<br>… (+9 more, total 14) |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:713<br>CLAUDE.md:744<br>CLAUDE.md:786 |
| 4: Settings | 2 | .claude/settings.local.json:337<br>.claude/settings.local.json:338 |
| 5: Project CLAUDE.md | 3 | projects/x-ai-document-intelligence-r3/CLAUDE.md:131<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:139<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:151 |
| 6: Docs | 11 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:119<br>docs/procedures/context-recovery.md:10<br>docs/procedures/context-recovery.md:25<br>docs/procedures/context-recovery.md:27<br>docs/procedures/context-recovery.md:38<br>… (+6 more, total 11) |
| 7: Task POMLs | 0 | — |

### 32. `project-pipeline` — 116 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 62 | .claude/skills/conflict-check/SKILL.md:160<br>.claude/skills/conflict-check/SKILL.md:263<br>.claude/skills/design-to-spec/SKILL.md:116<br>.claude/skills/design-to-spec/SKILL.md:26<br>.claude/skills/design-to-spec/SKILL.md:381<br>… (+57 more, total 62) |
| 3: Root CLAUDE.md | 12 | CLAUDE.md:1159<br>CLAUDE.md:1166<br>CLAUDE.md:118<br>CLAUDE.md:379<br>CLAUDE.md:397<br>… (+7 more, total 12) |
| 4: Settings | 2 | .claude/settings.local.json:267<br>.claude/settings.local.json:268 |
| 5: Project CLAUDE.md | 10 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:150<br>projects/ai-procedure-quality-r1/CLAUDE.md:26<br>projects/ai-procedure-quality-r1/CLAUDE.md:27<br>projects/ai-sprk-chat-workspace-analysis-r1/CLAUDE.md:101<br>projects/events-smart-todo-kanban-r2/CLAUDE.md:62<br>… (+5 more, total 10) |
| 6: Docs | 27 | docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md:191<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:112<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:123<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:137<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:193<br>… (+22 more, total 27) |
| 7: Task POMLs | 3 | projects/ai-procedure-quality-r1/tasks/006-build-skill-crossref-map.poml:17<br>projects/ai-procedure-quality-r1/tasks/066-validator-crossref-drift.poml:17<br>projects/x-ai-playbook-node-builder-r2/tasks/090-project-wrap-up.poml:42 |

### 33. `project-setup` — 24 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 16 | .claude/skills/design-to-spec/SKILL.md:545<br>.claude/skills/design-to-spec/SKILL.md:698<br>.claude/skills/design-to-spec/SKILL.md:707<br>.claude/skills/project-pipeline/SKILL.md:24<br>.claude/skills/project-pipeline/SKILL.md:384<br>… (+11 more, total 16) |
| 3: Root CLAUDE.md | 4 | CLAUDE.md:498<br>CLAUDE.md:701<br>CLAUDE.md:731<br>CLAUDE.md:772 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 4 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:75<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:93<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:94<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:57 |
| 7: Task POMLs | 0 | — |

### 34. `pull-from-github` — 14 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 8 | .claude/skills/ci-cd/SKILL.md:345<br>.claude/skills/merge-to-master/SKILL.md:331<br>.claude/skills/merge-to-master/SKILL.md:339<br>.claude/skills/project-continue/SKILL.md:479<br>.claude/skills/project-continue/SKILL.md:84<br>… (+3 more, total 8) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:709<br>CLAUDE.md:782 |
| 4: Settings | 2 | .claude/settings.local.json:165<br>.claude/settings.local.json:166 |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/procedures/ci-cd-workflow.md:997<br>docs/procedures/parallel-claude-sessions.md:499 |
| 7: Task POMLs | 0 | — |

### 35. `push-to-github` — 60 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 24 | .claude/skills/adr-check/SKILL.md:193<br>.claude/skills/ci-cd/SKILL.md:208<br>.claude/skills/ci-cd/SKILL.md:334<br>.claude/skills/ci-cd/SKILL.md:344<br>.claude/skills/code-review/SKILL.md:762<br>… (+19 more, total 24) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:710<br>CLAUDE.md:783 |
| 4: Settings | 2 | .claude/settings.local.json:142<br>.claude/settings.local.json:143 |
| 5: Project CLAUDE.md | 2 | projects/ai-procedure-quality-r1/CLAUDE.md:78<br>projects/x-ai-document-intelligence-r3/CLAUDE.md:74 |
| 6: Docs | 14 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:157<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:498<br>docs/procedures/ci-cd-workflow.md:1058<br>docs/procedures/ci-cd-workflow.md:183<br>docs/procedures/ci-cd-workflow.md:194<br>… (+9 more, total 14) |
| 7: Task POMLs | 16 | projects/code-quality-and-assurance-r2/tasks/090-project-wrap-up.poml:14<br>projects/code-quality-and-assurance-r2/tasks/090-project-wrap-up.poml:25<br>projects/sdap-SPE-admin-app/tasks/090-project-wrap-up.poml:55<br>projects/x-ai-document-intelligence-r3/tasks/090-project-wrap-up.poml:54<br>projects/x-ai-document-intelligence-r4/tasks/090-project-wrap-up.poml:31<br>… (+11 more, total 16) |

### 36. `repo-cleanup` — 215 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 17 | .claude/skills/ai-procedure-maintenance/SKILL.md:359<br>.claude/skills/code-review/SKILL.md:778<br>.claude/skills/code-review/SKILL.md:798<br>.claude/skills/doc-drift-audit/SKILL.md:43<br>.claude/skills/merge-to-master/SKILL.md:332<br>… (+12 more, total 17) |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:1155<br>CLAUDE.md:1178<br>CLAUDE.md:774 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 19 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:329<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:404<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:87<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:322<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:507<br>… (+14 more, total 19) |
| 7: Task POMLs | 176 | projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:14<br>projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:30<br>projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:49<br>projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:62<br>projects/ai-m365-copilot-integration/tasks/031-project-wrap-up.poml:77<br>… (+171 more, total 176) |

### 37. `ribbon-edit` — 74 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 2 | .claude/skills/azure-deploy/SKILL.md:523<br>.claude/skills/dataverse-deploy/SKILL.md:682 |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:708<br>CLAUDE.md:737<br>CLAUDE.md:781 |
| 4: Settings | 2 | .claude/settings.local.json:115<br>.claude/settings.local.json:137 |
| 5: Project CLAUDE.md | 5 | projects/x-ai-document-intelligence-r2/CLAUDE.md:29<br>projects/x-ai-document-intelligence-r2/CLAUDE.md:38<br>projects/x-document-checkout-viewer/CLAUDE.md:137<br>projects/x-document-checkout-viewer/CLAUDE.md:29<br>projects/x-document-checkout-viewer/CLAUDE.md:6 |
| 6: Docs | 1 | docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md:381 |
| 7: Task POMLs | 61 | projects/ai-sprk-chat-workspace-analysis-r1/tasks/042-wire-refresh-button.poml:19<br>projects/ai-sprk-chat-workspace-analysis-r1/tasks/042-wire-refresh-button.poml:25<br>projects/sdap-file-upload-document-r2/tasks/031-update-ribbon-commands.poml:20<br>projects/spaarke-mda-darkmode-theme-r2/tasks/020-add-ribbon-missing-entities.poml:19<br>projects/spaarke-mda-darkmode-theme-r2/tasks/020-add-ribbon-missing-entities.poml:27<br>… (+56 more, total 61) |

### 38. `script-aware` — 7 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 4 | .claude/skills/project-continue/SKILL.md:265<br>.claude/skills/project-pipeline/SKILL.md:348<br>.claude/skills/task-execute/SKILL.md:1049<br>.claude/skills/task-execute/SKILL.md:369 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:501<br>CLAUDE.md:760 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 1 | projects/x-ai-document-intelligence-r1/CLAUDE.md:46 |
| 6: Docs | 0 | — |
| 7: Task POMLs | 0 | — |

### 39. `spaarke-conventions` — 31 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 7 | .claude/skills/code-review/SKILL.md:794<br>.claude/skills/dataverse-deploy/SKILL.md:683<br>.claude/skills/project-continue/SKILL.md:264<br>.claude/skills/push-to-github/SKILL.md:491<br>.claude/skills/ribbon-edit/SKILL.md:388<br>… (+2 more, total 7) |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:734<br>CLAUDE.md:761 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 1 | projects/x-document-checkout-viewer/CLAUDE.md:54 |
| 6: Docs | 18 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:78<br>docs/standards/CODING-STANDARDS.md:150<br>docs/standards/CODING-STANDARDS.md:220<br>docs/standards/CODING-STANDARDS.md:236<br>docs/standards/CODING-STANDARDS.md:35<br>… (+13 more, total 18) |
| 7: Task POMLs | 3 | projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:14<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:25<br>projects/ai-procedure-refactoring-r2/tasks/001-create-coding-standards.poml:39 |

### 40. `task-create` — 66 total references

> **Ambiguity note**: Common term; can appear in task creation discussions

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 37 | .claude/skills/adr-aware/SKILL.md:173<br>.claude/skills/ai-procedure-maintenance/SKILL.md:137<br>.claude/skills/ai-procedure-maintenance/SKILL.md:173<br>.claude/skills/ai-procedure-maintenance/SKILL.md:295<br>.claude/skills/ai-procedure-maintenance/SKILL.md:309<br>… (+32 more, total 37) |
| 3: Root CLAUDE.md | 4 | CLAUDE.md:499<br>CLAUDE.md:702<br>CLAUDE.md:732<br>CLAUDE.md:773 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 7 | projects/ai-procedure-quality-r1/CLAUDE.md:37<br>projects/sdap-office-integration/CLAUDE.md:25<br>projects/x-email-to-document-automation-r2/CLAUDE.md:13<br>projects/x-email-to-document-automation-r2/CLAUDE.md:24<br>projects/x-events-and-workflow-automation-r1/CLAUDE.md:13<br>… (+2 more, total 7) |
| 6: Docs | 3 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:76<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:95<br>docs/procedures/testing-and-code-quality.md:1437 |
| 7: Task POMLs | 15 | projects/x-ai-document-intelligence-r1/tasks/090-project-wrap-up.poml:47<br>projects/x-ai-document-intelligence-r2/tasks/090-project-wrap-up.poml:43<br>projects/x-ai-document-relationship-visuals/tasks/090-project-wrap-up.poml:31<br>projects/x-ai-document-relationship-visuals/tasks/090-project-wrap-up.poml:32<br>projects/x-ai-document-relationship-visuals/tasks/090-project-wrap-up.poml:33<br>… (+10 more, total 15) |

### 41. `task-execute` — 1234 total references

> **Ambiguity note**: Common term; can appear in task execution discussions

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 3 | .claude/skills/project-continue/SKILL.md:312<br>.claude/skills/project-setup/SKILL.md:309<br>.claude/skills/project-setup/SKILL.md:313 |
| 2: See-Also (other SKILL.md) | 63 | .claude/skills/ai-procedure-maintenance/SKILL.md:136<br>.claude/skills/ai-procedure-maintenance/SKILL.md:172<br>.claude/skills/ai-procedure-maintenance/SKILL.md:294<br>.claude/skills/ai-procedure-maintenance/SKILL.md:309<br>.claude/skills/ai-procedure-maintenance/SKILL.md:310<br>… (+58 more, total 63) |
| 3: Root CLAUDE.md | 28 | CLAUDE.md:410<br>CLAUDE.md:418<br>CLAUDE.md:425<br>CLAUDE.md:429<br>CLAUDE.md:446<br>… (+23 more, total 28) |
| 4: Settings | 2 | .claude/settings.local.json:169<br>.claude/settings.local.json:170 |
| 5: Project CLAUDE.md | 279 | projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:110<br>projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:116<br>projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:118<br>projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:119<br>projects/ai-analysis-workspace-sprkchat-integration/CLAUDE.md:120<br>… (+274 more, total 279) |
| 6: Docs | 22 | docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:237<br>docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:348<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:143<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:516<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:79<br>… (+17 more, total 22) |
| 7: Task POMLs | 837 | projects/ai-procedure-quality-r1/tasks/006-build-skill-crossref-map.poml:17<br>projects/ai-procedure-quality-r1/tasks/066-validator-crossref-drift.poml:17<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/001-dataverse-schema-extensions.poml:119<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/002-jps-schema-extensions.poml:134<br>projects/ai-sprk-chat-platform-enhancement-r2/tasks/003-playbook-embeddings-index.poml:139<br>… (+832 more, total 837) |

### 42. `ui-test` — 23 total references

> **Ambiguity note**: Common term; may refer to 'UI testing' generally

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 1 | .claude/skills/task-execute/SKILL.md:678 |
| 2: See-Also (other SKILL.md) | 8 | .claude/skills/project-pipeline/SKILL.md:519<br>.claude/skills/project-pipeline/SKILL.md:890<br>.claude/skills/project-pipeline/SKILL.md:909<br>.claude/skills/task-create/SKILL.md:280<br>.claude/skills/task-create/SKILL.md:674<br>… (+3 more, total 8) |
| 3: Root CLAUDE.md | 0 | — |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 8 | docs/procedures/testing-and-code-quality.md:1427<br>docs/procedures/testing-and-code-quality.md:1447<br>docs/procedures/testing-and-code-quality.md:149<br>docs/procedures/testing-and-code-quality.md:1490<br>docs/procedures/testing-and-code-quality.md:1525<br>… (+3 more, total 8) |
| 7: Task POMLs | 6 | projects/x-ai-azure-search-module/tasks/023-e2e-testing-dataverse.poml:41<br>projects/x-events-and-workflow-automation-r1/tasks/060-e2e-test-event-creation.poml:51<br>projects/x-events-and-workflow-automation-r1/tasks/061-e2e-test-field-mapping-autoapply.poml:50<br>projects/x-events-and-workflow-automation-r1/tasks/062-e2e-test-refresh-from-parent.poml:48<br>projects/x-events-and-workflow-automation-r1/tasks/063-e2e-test-update-related-push.poml:48<br>… (+1 more, total 6) |

### 43. `worktree-setup` — 12 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 3 | .claude/skills/conflict-check/SKILL.md:261<br>.claude/skills/worktree-sync/SKILL.md:389<br>.claude/skills/worktree-sync/SKILL.md:400 |
| 3: Root CLAUDE.md | 2 | CLAUDE.md:712<br>CLAUDE.md:785 |
| 4: Settings | 2 | .claude/settings.local.json:342<br>.claude/settings.local.json:343 |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 5 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:405<br>docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:509<br>docs/procedures/parallel-claude-sessions.md:10<br>docs/procedures/parallel-claude-sessions.md:496<br>docs/procedures/parallel-claude-sessions.md:521 |
| 7: Task POMLs | 0 | — |

### 44. `worktree-sync` — 13 total references

| Surface | Count | Sample Paths |
|---|---:|---|
| 1: Invocations (other SKILL.md) | 0 | — |
| 2: See-Also (other SKILL.md) | 8 | .claude/skills/merge-to-master/SKILL.md:255<br>.claude/skills/merge-to-master/SKILL.md:263<br>.claude/skills/merge-to-master/SKILL.md:265<br>.claude/skills/merge-to-master/SKILL.md:267<br>.claude/skills/merge-to-master/SKILL.md:342<br>… (+3 more, total 8) |
| 3: Root CLAUDE.md | 3 | CLAUDE.md:724<br>CLAUDE.md:747<br>CLAUDE.md:796 |
| 4: Settings | 0 | — |
| 5: Project CLAUDE.md | 0 | — |
| 6: Docs | 2 | docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:413<br>docs/procedures/parallel-claude-sessions.md:10 |
| 7: Task POMLs | 0 | — |

---

## Findings

### Orphan Skills

Skills with **0 references across all 7 surfaces** — candidates for `remove` recommendation in Phase 2b.
Verify before removal: a skill may be valid future infrastructure, or referenced via patterns the scanner missed.

- `add-reference-to-index` — no references found in any of the 7 surfaces.

### Hub Skills (High Blast Radius)

Top 5 most-referenced skills. **Phase 2b destructive actions on these MUST update every referencing surface.**

| Rank | Skill | Total Refs |
|---:|---|---:|
| 1 | `task-execute` | 1234 |
| 2 | `dataverse-deploy` | 269 |
| 3 | `code-review` | 242 |
| 4 | `repo-cleanup` | 215 |
| 5 | `adr-check` | 209 |

### Broken References

Patterns mentioning a skill name (via `` `name` skill ``, `Skill(name)`, `.claude/skills/name/`) where the named skill does NOT exist in `.claude/skills/`. These are existing rot to fix in Phase 2b.

_No broken references detected._

---

## Methodology Notes

1. **Skill list source**: `notes/inventory/skills.md` (44 skills, verified against `ls .claude/skills/` excluding `_archived`, `_templates`, and top-level `*.md` files).
2. **Self-references** in a skill's own `SKILL.md` are **excluded** from surface 1+2.
3. **Surface 1 vs 2 classification**: Surface 1 requires an explicit verb pattern (`invoke X`, `calls X skill`, `uses X skill`, `loads X skill`). All other in-body mentions go to surface 2 (or to a Related/See-Also section if explicitly headed).
4. **Word boundary**: Skill name matches require non-letter on both sides to avoid partial matches (e.g., `code-review` will not match `code-reviewer`).
5. **POML scan**: All 857 POML files containing any skill-name pattern were read line-by-line.
6. **Hooks in settings.json** that invoke `scripts/quality/*.sh` are counted under surface 4 if the path string contains the skill name.
7. **Limitations**: Snake-case false positives possible for short/common names like `ci-cd`, `code-review`, `ui-test`, `task-execute`. See the ambiguity note on those skills' detail sections.

---

## Output Files

- **This file**: `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.md`
- **JSON (validators)**: `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.json`

Both consumed by Phase 4a validators (`scripts/quality/Validate-SkillReferences.ps1` (065) and `Find-SkillReferenceDrift.ps1` (066)).
