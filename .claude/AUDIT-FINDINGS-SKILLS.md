# Skills Audit Findings — Phase 2a

> **🚪 REVIEWER SIGN-OFF REQUIRED BEFORE PHASE 2b**
>
> This document consolidates the audit of all 44 skills under `.claude/skills/` against the 7 best practices + F-15.x reference checks. **No skill has been modified.** Phase 2b destructive actions (split / merge / remove) cannot begin until the per-skill rows in §4 carry a ✅ or ❌ from the reviewer.
>
> **Created**: 2026-05-15 by project `ai-procedure-quality-r1` (task 028)
> **Sources**: 8 batch reports at `projects/ai-procedure-quality-r1/notes/audit/batch-{1..8}.md` · inventory at `projects/ai-procedure-quality-r1/notes/inventory/skills.md` · cross-ref map at `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.json`
> **Honesty pledge**: Skills already in good shape are marked `leave-alone` — no problems fabricated. Where a skill is genuinely problematic, the issue is named with evidence.

---

## 1. Executive Summary

44 skills audited. Recommended actions:

| Action | Count | Definition |
|---|---:|---|
| `refine-in-place` | 30 | Minor refinements (add Gotchas heading, Last Reviewed stamp, exemplar frontmatter, normalize MUST/MUST NOT) — bulk-fixable in Phase 2b |
| `split` | 5 | Body > 400 lines AND has natural subdir extraction candidates |
| `needs-substantive-rewrite` | 4 | Structural issues + content correctness problems requiring focused dedicated work |
| `leave-alone` | 3 | Already exemplary; should be propagated as reference patterns |
| `leave-alone-justified` | 2 | Over-target but operationally dense and well-structured; no refactor warranted |
| `merge` | 0 | No merge candidates surfaced |
| `remove` | 0 | The 1 orphan (`add-reference-to-index`) is a low-frequency-but-genuine workflow — keep |

**Status of the project's load-bearing concern**: The audit hit **multiple AP-1 anti-patterns** (skills that confidently prescribe something that is now wrong) — exactly the failure class this project was created to address. See §3.1.

---

## 2. Reading guide

For each skill in §4 the audit provides:
- **Action** (one of the 7 above)
- **Line count tier** (PASS ≤200 · OVER-TARGET 201-400 with justification · SPLIT >400)
- **Cross-ref load** (number of inbound references — high values = high blast radius for destructive actions)
- **Key issues** (1-3 bullets summarizing what to fix)
- **Sign-off row** for ✅ / ❌ + reviewer notes

§3 covers cross-cutting findings that touch many skills at once. §5 explains Phase 2b execution sequencing.

---

## 3. Cross-cutting findings

### 3.1 AP-1 hits — "skill says X but X is wrong"

These match the `AP-1` anti-pattern documented in [`FAILURE-MODES.md#AP-1`](FAILURE-MODES.md#ap-1). All must be remediated in Phase 2b alongside the structural changes:

| Skill | AP-1 hit | Evidence | Severity |
|---|---|---|---|
| `azure-deploy` | BFF deploy section (lines 152-278) carries 2025-era guidance with NO mention of the May 2026 silent-failure hardening — no hash-verify, no Linux cold-start tolerance | An agent following this skill today would follow superseded instructions | **HIGH** — exact AP-1 class |
| `azure-deploy` | 2 broken workflow filename references — `deploy-staging.yml` and `deploy-to-azure.yml` neither exist in `.github/workflows/` | Light ref-check FAIL | **HIGH** |
| `project-pipeline` | Prerequisites (lines 17-20) prescribe `MAX_THINKING_TOKENS=50000` as required, but root CLAUDE.md states this is **IGNORED on Opus 4.6** | Direct contradiction with adaptive-thinking section of root CLAUDE.md | **MEDIUM** — stale guidance |
| `adr-aware` | References a renamed skill `design-to-project` (line 169) that no longer exists; should be `design-to-spec` | Cross-ref FAIL | LOW (one stale name) |
| `dataverse-create-schema` | 3 of 4 reference scripts named in `## Reference Scripts` do NOT exist — `Deploy-PlaybookNodeSchema.ps1`, `Fix-PlaybookNodeAttributes.ps1`, `Create-NNRelationships.ps1` all under archived `projects/ai-node-playbook-builder/scripts/` | 75% broken refs | **HIGH** |
| `jps-action-create` | 5 broken path references to `projects/ai-json-prompt-schema-system/notes/jps-conversions/` — directory was renamed/archived to `projects/x-ai-json-prompt-schema-system/` | 5× broken refs | **HIGH** |
| `pcf-deploy` | 2 broken ADR paths: `ADR-006-pcf-over-webresources.md` (actual: `ADR-006-prefer-pcf-over-webresources.md`); `ADR-021-fluent-design-system.md` (actual: `ADR-021-fluent-ui-design-system.md`) | Light ref-check FAIL | MEDIUM |
| `jps-scope-refresh` ↔ `jps-playbook-design` | Singular vs plural Dataverse table-name drift: one uses `sprk_analysisaction`, the other uses `sprk_analysisactions` | Internal inconsistency | LOW |

**`pcf-deploy` AP-1 verification — PASS**: The original AP-1 ("NEVER use `build:prod`") fix from 2026-05-14 is confirmed correctly in place. The skill now prescribes `pcf-scripts build --buildMode production` with an explicit "Bundle Size & Production Mode (CRITICAL — verified 2026-05-14)" section. The 2 remaining ADR-path issues are a separate concern.

### 3.2 Universal gaps (apply to most or all 44 skills)

| Gap | Count | Fix |
|---|---:|---|
| No explicit `## Gotchas` heading | 41 / 44 | Bulk Phase 2b task: add heading; for skills with existing equivalent content (`## Tips for AI`, `## Error Handling`, `## Anti-Patterns (DO NOT)`, `## Failure Modes to Avoid`) — rename or duplicate the heading; per-batch findings note where existing content can be promoted |
| No `exemplar:` frontmatter | 44 / 44 | Bulk task: add field with either a real path or `none-too-volatile` + rationale (rationale fields filled in per-skill rows in §4) |
| No `Last Reviewed: YYYY-MM-DD` stamp (R2 convention) | 43 / 44 | Bulk task: replace existing `Last Updated:` lines with `Last Reviewed: 2026-05-15`. Only `doc-drift-audit` already uses the R2 convention |
| H1 placed before YAML frontmatter | 4 | Skills `design-to-spec`, `spaarke-conventions`, `task-create`, `ui-test` (also `add-reference-to-index`, `project-pipeline`, `project-setup`, `dataverse-create-schema`) — move frontmatter above H1 |
| Reduced frontmatter (missing tags/techStack/appliesTo) | 9 | `ci-cd` (zero frontmatter), `code-review`, `adr-check`, `dev-cleanup`, `power-page-deploy`, `pull-from-github`, `push-to-github`, `repo-cleanup`, `ribbon-edit` |

### 3.3 Gold-standard exemplars (propagate these patterns)

Three skills passed the audit cleanly AND demonstrate patterns the rest should adopt:

| Skill | Pattern to propagate | Already used by |
|---|---|---|
| `doc-drift-audit` | R2 audit-stamp block (`Last Reviewed` + `Reviewed By` + `Status`); "Distinct from other skills" disambiguation section; "Failure Modes & Recovery" framing (prescriptive, not just warnings) | Only doc-drift-audit (created in R2) |
| `script-aware` | `## Failure Modes to Avoid` section with structured (Failure / Cause / Prevention) table | Itself + `task-execute` |
| `bff-deploy` | Incident-grounded documentation: date, symptom, root-cause hypothesis, resolution, long-term plan all cited inline | Itself only |

**Decision (reviewer-approved 2026-05-16)**: Canonical heading is **`## Failure Modes & Recovery`** — most descriptive for Claude Code prompting; matches the doc-drift-audit gold-standard exactly. Phase 2b refinements adopt this heading universally. `## Gotchas` and `## Failure Modes to Avoid` will be migrated to this canonical form during Wave 2b-A. Spec F-15 will be updated to reflect this standardization.

### 3.4 Family overlap assessments

| Family | Skills | Verdict | Rationale |
|---|---|---|---|
| `docs-*` | docs-architecture, docs-data-model, docs-guide, docs-procedures, docs-standards (5 total) | **DO NOT MERGE** | Well-factored: each owns one `docs/` subdirectory with explicit "NOT" exclusions pointing at the right sibling. Boundaries match the R2 doc taxonomy. |
| `jps-*` | jps-action-create, jps-playbook-audit, jps-playbook-design, jps-scope-refresh, jps-validate (5 total) | **DO NOT MERGE** | Distinct verbs (create / audit / design / refresh / validate). Tier 1 components vs Tier 2 orchestrator. Recommended: add "JPS Family at a Glance" pointer block to each in Phase 2b; resolve singular/plural Dataverse table-name drift. |
| `project-*` | project-continue, project-pipeline, project-setup (3 total) | **DO NOT MERGE** | Distinct verbs: resume / orchestrate-initialize / artifact-generate-only. `project-setup` is correctly marked AI-internal. |
| `github` | pull-from-github, push-to-github (2 total) | **DO NOT MERGE** | Distinct directions (inbound vs outbound); merging would create 800+ line skill and break trigger routing. |
| `worktree-*` | worktree-setup, worktree-sync (2 total) | **DO NOT MERGE** | Distinct lifecycle stages (one-shot vs recurring). Same logic as the github pair. |

### 3.5 Deterministic rules to extract to settings.json / hooks

Phase 4a will introduce validators. The following currently-inline checks should move out of skill prose once those validators exist:

| Currently in skill | Should move to |
|---|---|
| `adr-aware` resource-type → ADR mapping table | Stays inline (it's context, not automation) |
| `ai-procedure-maintenance` path-consistency checks (lines 282-286) | Replace with reference to `scripts/quality/Find-SkillReferenceDrift.ps1` once Phase 4a task 066 ships |
| `pcf-deploy` expected bundle sizes table | Stays inline AND becomes input to `scripts/quality/Check-BundleSizeDrift.ps1` once task 063 ships |
| `adr-check` ADR-specific grep patterns | Skill body has start of `references/adr-validation-rules.md` — extend that |

### 3.6 Structural anomalies caught (not in any single skill's audit)

- `context-handoff` has **duplicate `## Quick Recovery (READ THIS FIRST)` H2 sections** at lines 113 and 176 — silent drift; one needs merging or renaming
- `repo-cleanup` has **three `## Repository Cleanup Report` H2 sections** (lines 231, 394, 425) — same fragmentation
- `project-setup` has **two `## Resources` H2 sections** (lines 410 and 509)
- `adr-check` has **duplicate "Be thorough" tip lines** at 265-266 (copy-paste)
- `ai-procedure-maintenance` has **U+FFFD replacement character** on line 79 (corrupted em-dash)
- `jps-action-create` has **two Step 6 sections** with different content (offer-next-steps at 157, refresh-scope-index at 237)
- `push-to-github` has **PR-body template content** rendered as H2 sections (lines 341-453) — fragments the skill structure
- `project-pipeline`, `project-setup`, `push-to-github`, `repo-cleanup` all embed template content as H2 inside SKILL.md body — should move to `references/` subdirs

---

## 4. Per-skill recommendations (alphabetical) — REVIEWER SIGN-OFF ROWS

### Legend

- **L** = body line count
- **R** = total inbound cross-references
- **Tier**: PASS (≤200) · O-T (201-400, over-target with justification) · SPLIT (>400)

For each skill the reviewer should mark the **Sign-off** cell as ✅ (approved as recommended) or ❌ (rejected — see Notes) and add any notes inline.

| # | Skill | L | R | Tier | Action | Key fixes | Sign-off |
|---|---|---:|---:|---|---|---|:---:|
| 1 | `add-reference-to-index` | 171 | 0 | PASS | **refine-in-place** | Add `## Gotchas`; add `exemplar: none-too-volatile` (AI Search index too volatile to maintain a known-good rebuild); add Last Reviewed; move frontmatter above H1. **Keep skill** — orphan status reflects low call frequency, not low value | ✅ |
| 2 | `adr-aware` | 270 | 26+ | O-T | **refine-in-place** | Fix stale `design-to-project` reference (→ `design-to-spec`); add `## Gotchas`; reconcile two mapping tables so `jobs.md` appears in both; add Last Reviewed; `exemplar: none-too-volatile` | ✅ |
| 3 | `adr-check` | 270 | 209 | O-T | **refine-in-place** | Add `## Gotchas`; fix line 265-266 tip duplication; normalize frontmatter; add `exemplar: tests/Spaarke.ArchTests/`; add Last Reviewed. **Hub #5 — additive only** | ✅ |
| 4 | `ai-procedure-maintenance` | 363 | 5 | O-T | **refine-in-place** | Fix U+FFFD on line 79; add `## Gotchas`; align "Automation Opportunity" script name with spec F-15.4 (`Validate-SkillReferences.ps1`); add Last Reviewed; `exemplar: none-too-volatile` | ✅ |
| 5 | `azure-deploy` | 615 | 17 | **SPLIT** | **split** | **AP-1 hit** — BFF section is stale relative to May 2026 hardening. SPLIT plan: (a) reduce to Infrastructure + Key Vault only (~250 lines), (b) **remove** BFF section entirely (defer to `bff-deploy`), (c) move Office Add-ins to `azure-deploy/examples/office-addins.md` (or new `office-addins-deploy` skill), (d) fix 2 broken workflow filename refs, (e) update 6 see-also references | ✅ |
| 6 | `bff-deploy` | 298 | 24+ | O-T | **leave-alone-justified** | Gold-standard incident-grounded skill. Cosmetic only: title existing content as `## Gotchas`; move "Future Migration: WEBSITE_RUN_FROM_PACKAGE" (~70 lines) to `references/run-from-package-migration.md`; add Last Reviewed (2026-05-14 — the AP-1 fix date); add `exemplar: scripts/Deploy-BffApi.ps1` | ✅ |
| 7 | `ci-cd` | 378 | 78 | O-T | **refine-in-place** | **Missing entire frontmatter block** (inventory anomaly #1). Add full frontmatter; add Last Reviewed; add `exemplar: none-too-volatile`. Body OK at 378 lines | ✅ |
| 8 | `code-page-deploy` | 297 | 128 | O-T | **refine-in-place** | Best-shaped skill in batch. Add `exemplar: src/client/code-pages/DocumentRelationshipViewer/`; add Last Reviewed; rename `## Anti-Patterns (DO NOT)` → `## Gotchas` for consistency | ✅ |
| 9 | `code-review` | 846 | 242 | **SPLIT** | **split** | **Hub #3 — 242 refs — additive only.** Move 600+ line Workflow body to `references/review-procedure.md`; complete frontmatter; add `## Gotchas`; preserve trigger phrases and `/code-review` slash-command surface. Target body ~250-300 lines | ✅ |
| 10 | `conflict-check` | 268 | 12 | O-T | **refine-in-place** | Add `## Gotchas`; Last Reviewed; `exemplar: none-too-volatile`. Lowest blast radius in batch | ✅ |
| 11 | `context-handoff` | 352 | 59 | O-T | **refine-in-place** | **Resolve duplicate `## Quick Recovery (READ THIS FIRST)` H2 at lines 113 + 176**; add `## Gotchas`; Last Reviewed; `exemplar: none-too-volatile`. Critical for task-execute checkpoint loop | ✅ |
| 12 | `dataverse-create-schema` | 777 | 4 | **SPLIT** | **needs-substantive-rewrite** | **DECISION 2026-05-16: keep skill, lighter rewrite scope.** The intent (Tier 1 Web-API pattern for schema work where PAC CLI falls short) is valuable. Fixes: (a) rewrite `## Reference Scripts` (lines 651-680) to point at REAL existing scripts — `scripts/Create-PlaybookTriggerFields.ps1` (extend existing entity), `scripts/Deploy-ReportingSchema.ps1` (multi-entity), `scripts/Deploy-ChartDefinitionEntity.ps1` (entity + custom fields), `scripts/Create-RegistrationRequestSchema.ps1`; drop the 3 broken references to non-existent `projects/ai-node-playbook-builder/scripts/*`. (b) Extract ~600 lines of inline PowerShell to `references/` (option-set patterns / entity patterns / relationship patterns) — body target ~300 lines. (c) Add `## Failure Modes & Recovery` heading; add `exemplar: scripts/Deploy-ChartDefinitionEntity.ps1`; add Last Reviewed. **No new scripts to build** — repo already has the patterns | ✅ |
| 13 | `dataverse-deploy` | 802 | 269 | **SPLIT** | **refine-in-place** (conservative) | **Hub #2 — 269 refs — additive only.** Consolidate 4 `🚨 CRITICAL` callouts under proper `## Gotchas`; trim duplicate Deployment Workflow if `docs/guides/PCF-DEPLOYMENT-GUIDE.md` is canonical; Last Reviewed. **Do NOT** rename top-level sections | ✅ |
| 14 | `deploy-new-release` | 457 | 29 | **SPLIT** | **leave-alone** | Cleanest critical-rules structure in batch; BFF URL gotcha well-surfaced. Optional: add `## Gotchas` heading wrapping NEVER list; Last Reviewed | ✅ |
| 15 | `design-to-spec` | 727 | 31 | **SPLIT** | **refine-in-place** (conservative) | ~80-100 lines of redundancy from embedded spec.md template — extract to `templates/spec.md.tmpl` and point at it; move frontmatter above H1; Last Reviewed | ✅ |
| 16 | `dev-cleanup` | 314 | 8 | O-T | **refine-in-place** | Normalize frontmatter (anomaly #6 — add `tags`/`techStack`/`appliesTo`); rename "Common Issues and Solutions" → `## Gotchas`; Last Reviewed | ✅ |
| 17 | `doc-drift-audit` | 229 | 5 | O-T | **leave-alone** | **R2 GOLD-STANDARD COMPARATOR.** Only skill with R2 `Last Reviewed` block. Features to propagate to other skills (see §3.3) | ✅ |
| 18 | `docs-architecture` | 221 | ~12 | O-T | **refine-in-place** | Populate empty `techStack: []`; escape template H2s inside code block; Last Reviewed; `exemplar:` (candidate: `docs/architecture/AI-ARCHITECTURE.md`) | ✅ |
| 19 | `docs-data-model` | 95 | 37 | PASS | **refine-in-place** | Stamp + Gotchas stub + `exemplar:` (candidate: `docs/data-model/sprk_matter-related-tables.md`) + reshape soft "Drafting Rules" as MUST/MUST NOT | ✅ |
| 20 | `docs-guide` | 263 | 59 | O-T | **leave-alone-justified** | Hub of docs-* family (6 see-also inbound). Cosmetic only: Last Reviewed; `## Gotchas` stub; `exemplar: docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md`. **Do NOT split or merge** | ✅ |
| 21 | `docs-procedures` | 91 | 22 | PASS | **refine-in-place** | Same treatment as docs-data-model; `exemplar:` candidate `docs/procedures/testing-and-code-quality.md` | ✅ |
| 22 | `docs-standards` | 82 | 18 | PASS | **refine-in-place** | Same treatment; `exemplar:` candidate `docs/standards/CODING-STANDARDS.md` | ✅ |
| 23 | `jps-action-create` | 282 | 17 | O-T | **needs-substantive-rewrite** | **DECISION 2026-05-16: Option A — move canonical examples to `.claude/skills/jps-action-create/examples/`.** Concrete tasks: (a) move all 10 JSON files from `projects/x-ai-json-prompt-schema-system/notes/jps-conversions/` (ACT-001 through ACT-008 + `clause-analyzer.json` + `compare-documents.json` + `document-profiler.json` if present) into `examples/`; (b) update 5 path references in this skill's body; (c) update cross-ref hits in `jps-playbook-design` and `jps-validate` to match the new location; (d) merge duplicate Step 6 sections (offer-next-steps vs refresh-scope-index); (e) add `## Failure Modes & Recovery`; add `exemplar: examples/document-profiler.json` (post-move); add Last Reviewed | ✅ |
| 24 | `jps-playbook-audit` | 273 | 3 | O-T | **refine-in-place** | Add `## Gotchas`; `exemplar: none-too-volatile`; reshape Conventions as MUST/MUST NOT. **Optional: usage-audit** — 3 inbound refs only, no see_also from other skills — confirm active usage; archive under NF-1 if never used | ✅ |
| 25 | `jps-playbook-design` | 516 | 32+ | **SPLIT** | **needs-substantive-rewrite** | SPLIT body to <400 lines (Steps 4-5 scope/model → `references/`; Patterns 1-4 → `references/playbook-design-patterns.md`; error tables → `references/`). Combine with Last Reviewed + Gotchas + exemplar decision + MUST/MUST NOT normalization | ✅ |
| 26 | `jps-scope-refresh` | 127 | 5+ | PASS | **refine-in-place** | Last Reviewed; `## Gotchas` stub; `exemplar: none-too-volatile`; **resolve singular/plural Dataverse drift vs jps-playbook-design** | ✅ |
| 27 | `jps-validate` | 265 | 7+ | O-T | **refine-in-place** | Last Reviewed; `## Gotchas`; `exemplar: projects/<...>/jps-conversions/document-profiler.json` (verify path after jps-action-create rewrite resolves the renamed directory); normalize MUST/MUST NOT | ✅ |
| 28 | `merge-to-master` | 359 | 30+ | O-T | **refine-in-place** | Last Reviewed; `## Gotchas` (lift "Tips for AI" content); `exemplar: none-too-volatile`; unified MUST/MUST NOT block near top | ✅ |
| 29 | `pcf-deploy` | 410 | 35+ | **SPLIT** | **needs-substantive-rewrite** | **AP-1 fix VERIFIED** (build:prod is now correct). But: SPLIT body (extract XML templates ~60L + troubleshooting + path map); **fix 2 broken ADR links**; resolve schema-name convention drift; Last Reviewed: 2026-05-14; `## Gotchas` with explicit `See FAILURE-MODES.md#AP-1` cross-ref; `exemplar: src/client/pcf/SemanticSearchControl/` | ✅ |
| 30 | `power-page-deploy` | 278 | 12+ | O-T | **refine-in-place** | Normalize frontmatter (minimal); Last Reviewed; `## Gotchas` promoting inline ⚠️ warnings (especially line 68 — different deploy target); `exemplar: src/client/external-spa/`; unified MUST block | ✅ |
| 31 | `project-continue` | 530 | 33 | **SPLIT** | **refine-in-place** | Add `## Gotchas` (don't skip ADR loading at Step 5; master staleness check); Last Reviewed; `exemplar: none-too-volatile`. Body well-structured at 530 lines | ✅ |
| 32 | `project-pipeline` | 943 | 116 | **SPLIT** | **split** | **AP-1 hit** — stale `MAX_THINKING_TOKENS` claim contradicts root CLAUDE.md. Move 12-step Pipeline Steps detail (520 lines) to `references/pipeline-steps.md`; keep frontmatter + Prerequisites + Purpose + summary table + Error Handling in body. Target ~250-300 lines | ✅ |
| 33 | `project-setup` | 656 | 24 | **SPLIT** | **refine-in-place** (with content extraction) | Merge duplicate `## Resources` (lines 410 + 509); move 4 template-content H2 sections to `references/claudemd-template.md`; add `## Gotchas` ("Don't invoke directly; use project-pipeline"); Last Reviewed; `exemplar: none-too-volatile`. Body could shrink to ~400 lines | ✅ |
| 34 | `pull-from-github` | 286 | 14 | O-T | **refine-in-place** | Normalize frontmatter (add `tags`/`techStack`/`appliesTo`); explicit `## Gotchas`; Last Reviewed; `exemplar: none-too-volatile` | ✅ |
| 35 | `push-to-github` | 534 | 60 | **SPLIT** | **refine-in-place** (with content extraction) | Move PR-template H2 sections (lines 341-453) to `references/pr-template.md`; normalize frontmatter; add `## Gotchas` (untracked file check, never merge before CI green); Last Reviewed; `exemplar: none-too-volatile`. Body could shrink to ~400 lines | ✅ |
| 36 | `repo-cleanup` | 502 | 215 | **SPLIT** | **refine-in-place** (with content extraction) | **Hub #4 — 215 refs — careful.** Consolidate 3 duplicate `## Repository Cleanup Report` H2s (move templates to `references/cleanup-report.md`); normalize frontmatter; explicit `## Gotchas`; verify `docs/ai-knowledge/catalogs/` reference still applies after R2 doc refactor; Last Reviewed | ✅ |
| 37 | `ribbon-edit` | 414 | ~30 | **SPLIT** | **refine-in-place** | Normalize frontmatter (anomaly #6); Last Reviewed; optionally `## Gotchas` from Critical Requirements; consider extracting embedded bash workflow to `scripts/` if reusable. Body just over 400 — could justify | ✅ |
| 38 | `script-aware` | 304 | ~25 | O-T | **leave-alone** | **Gold-pattern Failure Modes section already present.** Optional: add `Last Reviewed` stamp. Always-apply skill — well-formed | ✅ |
| 39 | `spaarke-conventions` | 353 | ~40 | O-T | **refine-in-place** | Move frontmatter above H1; Last Reviewed; `## Gotchas` or `## Failure Modes to Avoid` (mixing Fluent v8+v9, React 18 in PCF, global auth middleware); drift-prevention note vs root CLAUDE.md | ✅ |
| 40 | `task-create` | 747 | ~80 | **SPLIT** | **split** | Move Workflow steps 3.5.5+/3.65/3.8 detail (562 lines) to `references/workflow-detail.md`; move Examples to `examples/`; preserve Validation Checklist step number references for task-execute callers. Target body ~250-300 lines | ✅ |
| 41 | `task-execute` | 1084 | **1234** | **SPLIT** | **split (high-care)** | **HUB #1 — 1,234 refs — 5× the next-highest. Dedicated isolated Phase 2b sub-task with MANDATORY human gate.** Additive split: body keeps Step 0.3, **entire Step 0.5 Rigor decision tree (lines 120-215)**, 7 trigger phrases, FULL/STANDARD/MINIMAL vocab, Failure Modes table, step-heading summaries. Detail moves to `references/protocol-detail.md` + `references/handoff-and-recovery.md` + `references/per-tech-checklists.md` + `examples/`. Pre/post-split `grep` verification mandatory (Step 9.5 / Step 0.5 / Rigor Level / FULL Protocol counts must match) | ✅ |
| 42 | `ui-test` | 552 | ~30 | **SPLIT** | **refine-in-place** | Move frontmatter above H1; Last Reviewed; `## Gotchas` (session timeout, dark-mode-doesn't-survive-reload); extract Examples (lines 445-522) to `examples/`. **Do NOT rename** `## Integration with task-execute` heading — task-execute Step 9.7 binds it | ✅ |
| 43 | `worktree-setup` | 733 | 12 | **SPLIT** | **refine-in-place** | `## Gotchas` (lift from Isolation Rules + Troubleshooting); `exemplar: none-too-volatile`; Last Reviewed: 2026-05-15. Optional split: "Parallel Claude Code Sessions" section (~135L) to `references/parallel-sessions.md` | ✅ |
| 44 | `worktree-sync` | 417 | 13 | **SPLIT** | **refine-in-place** | Rename "Tips for AI" → `## Gotchas` (content already excellent); add bullet about `wc -l` PowerShell-vs-bash portability; `exemplar: none-too-volatile`; Last Reviewed | ✅ |

---

## 5. Phase 2b execution sequencing (recommendation)

Phase 2b is parallel-safe-FALSE — main session only, because writes target `.claude/skills/` and the canonical pattern is "sub-agents read, main session writes." The recommended sequence:

### Wave 2b-A — Bulk refinement (low-risk, additive)

One main-session task processes the 30 `refine-in-place` skills (excluding the 4 with content-extraction sub-tasks called out below). The pattern is mechanical:

1. Add `## Gotchas` heading (or rename existing equivalent: `## Failure Modes to Avoid` / `## Anti-Patterns` / `## Tips for AI`)
2. Add `exemplar:` frontmatter field with the value recommended in §4
3. Replace `Last Updated:` → `Last Reviewed: 2026-05-15`
4. For skills with reduced frontmatter (anomaly #6): add `tags`, `techStack`, `appliesTo`, `alwaysApply: false`
5. For skills with frontmatter-after-H1: move frontmatter above H1
6. For specific named drift (stale skill names, duplicate H2s, U+FFFD characters): fix as listed in §4

Run `scripts/quality/Find-SkillReferenceDrift.ps1` after each batch of ~10 — must exit 0 before continuing.

### Wave 2b-B — `refine-in-place` with content extraction (4 skills)

Each gets a focused mini-task because content moves to `references/` or `examples/` subdirs:

- `project-setup` — merge duplicate `## Resources`; extract template content
- `push-to-github` — extract PR-template H2s to `references/pr-template.md`
- `repo-cleanup` — consolidate 3 duplicate `## Repository Cleanup Report` H2s; verify `docs/ai-knowledge/catalogs/` reference
- `bff-deploy` — move "Future Migration: WEBSITE_RUN_FROM_PACKAGE" to `references/run-from-package-migration.md`

### Wave 2b-C — `split` actions (5 skills, ordered by blast radius)

Each is a dedicated task. Lowest blast radius first to validate the pattern; highest last:

1. **`azure-deploy`** (17 refs) — also removes stale BFF section + fixes 2 broken workflow refs (AP-1)
2. **`code-review`** (242 refs) — move 600+ line Workflow body; preserve `/code-review` slash + trigger phrases
3. **`project-pipeline`** (116 refs) — also fixes `MAX_THINKING_TOKENS` AP-1
4. **`task-create`** (~80 refs) — preserve Validation Checklist step numbers
5. **`task-execute`** (1,234 refs) — **dedicated isolated sub-task with mandatory human gate**; pre/post-split grep verification

After EACH split, `scripts/quality/Find-SkillReferenceDrift.ps1` MUST exit 0.

### Wave 2b-D — `needs-substantive-rewrite` (4 skills)

Each is a focused dedicated session, not bulk-refined. Order by independence:

1. **`pcf-deploy`** — repair 2 broken ADR paths + split + add Gotchas+AP-1 link + exemplar + schema-name convention
2. **`jps-action-create`** — repair 5 broken paths to renamed `x-ai-json-prompt-schema-system/` + merge duplicate Step 6
3. **`jps-playbook-design`** — split + Last Reviewed + Gotchas + exemplar + MUST/MUST NOT normalization
4. **`dataverse-create-schema`** — **REQUIRES PRIOR HUMAN DECISION**: rewrite vs archive vs merge into MCP-tool guidance. 3 of 4 reference scripts missing; only 4 inbound refs; 777 lines

### What stays untouched

3 skills are `leave-alone` (doc-drift-audit, script-aware, deploy-new-release) and 2 are `leave-alone-justified` (bff-deploy, docs-guide) — they receive only the universal-gap cosmetic additions (Last Reviewed stamp, optional Gotchas rename, exemplar field) and otherwise are NOT refactored.

---

## 6. Open questions — REVIEWER RESOLUTIONS 2026-05-16

| # | Question | Reviewer decision (2026-05-16) |
|---|---|---|
| 1 | **`add-reference-to-index` orphan** — keep or remove? | ✅ **KEEP** — orphan status reflects low call frequency, not low value. Refine-in-place per row 1. |
| 2 | **`dataverse-create-schema` rewrite vs archive?** | ✅ **REWRITE (lighter scope)** — intent is valuable. See row 12 for the locked-in fix list: point Reference Scripts at real existing scripts; extract inline PowerShell to `references/`. No new scripts to build. |
| 3 | **`jps-playbook-audit` usage check** — refine or archive? | ✅ **REFINE-IN-PLACE** — keep in case the skill becomes more actively used. No usage-audit-driven archival. |
| 4 | **Canonical "failure modes" heading** — `## Gotchas` or `## Failure Modes to Avoid` or `## Failure Modes & Recovery`? | ✅ **STANDARDIZE ON `## Failure Modes & Recovery`** — most descriptive for Claude Code prompting; matches doc-drift-audit gold-standard. Wave 2b-A migrates all skills to this heading. Spec F-15 updated to reflect. |

---

## 7. Reviewer sign-off section

> **Instructions**: For each skill in §4, mark the Sign-off column as ✅ (approved) or ❌ (rejected with notes). When all 44 rows have a mark, append the reviewer's name and date below. Phase 2b execution begins after that signature.

| Field | Value |
|---|---|
| Reviewer | Spaarke dev (project owner) |
| Date signed | 2026-05-16 |
| Override notes | Row 12 (`dataverse-create-schema`): scope reduced from full rewrite to "fix Reference Scripts + extract inline PowerShell to references/" — see row 12 notes. Row 23 (`jps-action-create`): Option A confirmed — move 10 example JSONs to `.claude/skills/jps-action-create/examples/`. All other 42 rows approved as audit-recommended. |
| Open Questions §6 (1-4) | All 4 resolved in §6 table above. Q1: keep orphan. Q2: lighter rewrite. Q3: refine-in-place. Q4: standardize on `## Failure Modes & Recovery`. |

**HUMAN GATE 1 PASSED 2026-05-16.** Phase 2b execution authorized per the wave sequencing in §5. The canonical heading decision (`## Failure Modes & Recovery`) applies project-wide; spec F-15 to be updated in lockstep with Wave 2b-A.

---

## Appendix A — Method and constraints

- **8 batch reports** at `projects/ai-procedure-quality-r1/notes/audit/batch-{1..8}.md` (~1,836 lines combined)
- **Skill inventory** at `projects/ai-procedure-quality-r1/notes/inventory/skills.md`
- **Cross-reference map** at `projects/ai-procedure-quality-r1/notes/inventory/skill-cross-refs.json` (44 skills, 3,685 references across 7 surfaces)
- **Reference checks performed** per F-15.3:
  - **Light** (mandatory): all 44 skills — file paths, skill name resolution
  - **Medium** (recommended): performed for ops-heavy skills under 400 lines; skills exceeding the cap were flagged `needs-substantive-rewrite` for focused rewrite rather than bulk refinement
  - **Heavy** (opt-in): not exercised in this audit phase
- **Honesty rule observed**: 5 of 44 skills came back `leave-alone` or `leave-alone-justified` — no problems fabricated where none existed

## Appendix B — Counts cross-check vs §1 Executive Summary

| Action | §1 count | §4 row count | Match? |
|---|---:|---:|:---:|
| refine-in-place | 30 | 30 | ✅ |
| split | 5 | 5 (rows 5, 9, 32, 40, 41) | ✅ |
| needs-substantive-rewrite | 4 | 4 (rows 12, 23, 25, 29) | ✅ |
| leave-alone | 3 | 3 (rows 14, 17, 38) | ✅ |
| leave-alone-justified | 2 | 2 (rows 6, 20) | ✅ |
| merge | 0 | 0 | ✅ |
| remove | 0 | 0 | ✅ |
| **Total** | **44** | **44** | ✅ |

---

*Phase 2a complete 2026-05-15. Awaiting Human Gate 1 sign-off before Phase 2b begins. See [`CHANGELOG.md`](CHANGELOG.md) for the entry once this audit's outcomes ship.*
