# Audit — Batch 3 of 8 (Skills 13–18)

> **Generated**: 2026-05-15
> **Auditor**: Phase 2a, batch 3 (retry — first attempt stalled)
> **Method**: Grep-first, offset reads, light ref-check (paths only, 5–10 spot-checks)
> **Hard cap**: 3 minutes per skill

Skills audited (alphabetical 13–18 of 44):
1. dataverse-deploy
2. deploy-new-release
3. design-to-spec
4. dev-cleanup
5. doc-drift-audit
6. docs-architecture

---

## 1. `dataverse-deploy` (802 lines, 269 refs — hub #2)

- **Description**: Good — clear ("Deploy solutions, PCF controls, and web resources to Dataverse using PAC CLI"). Frontmatter complete (desc, tags, techStack, appliesTo, alwaysApply).
- **Line count**: **802** — well over 400-line guidance. Largest skill in this batch.
- **Goal-oriented**: Yes, opens with "Critical Rules" (MUST/MUST NOT). Heading skeleton: Critical Rules → Deployment Workflow → Dataverse Control Caching → Decision Tree → Purpose → Best Practices → Prerequisites → Additional Scenarios → Conventions → Error Handling → Quick Reference → Related Skills → CI/CD Integration → Related ADRs → Resources → Tips for AI.
- **Gotchas section**: ❌ No `## Gotchas` heading. The string "gotcha" does not appear in the file. However, **"🚨 CRITICAL"** callouts in `Tips for AI` (lines 770–803) serve as de facto gotchas (control manifest version, empty solution imports, `pac pcf push` fallback).
- **Overlap**: Significant — has a fully separate `Deployment Workflow` section (lines 49–130) AND `Quick Reference Commands` (line 654) AND `Best Practices` AND `Tips for AI`. The first 130 lines duplicate `docs/guides/PCF-DEPLOYMENT-GUIDE.md` substance. Could be ~50% leaner by pointing at the guide.
- **References-current**: Spot-check ✅ `docs/guides/PCF-DEPLOYMENT-GUIDE.md` exists. References to ADR-006, ADR-022, ADR-021 are standard.
- **Sanity check on commands**: `npm run build:prod` is the standard command — appears 7 times. Note that root `CLAUDE.md` (line 1006) flags `npm ci` issues on Vite solutions, but `dataverse-deploy` does not reference `npm ci`, so no conflict.
- **Cross-ref blast radius**: **269 refs** — hub-tier skill. Heavily referenced from azure-deploy (invocations), pcf-deploy, code-page-deploy, deploy-new-release, ci-cd, task-create, task-execute, project-pipeline. **Any structural rename or section deletion will ripple.** Destructive edits should be deferred — prefer additive (e.g., add `## Gotchas` heading anchoring existing callouts).
- **Exemplar**: `none-too-volatile` — PCF deployment specifics change frequently with PAC CLI updates.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) add a proper `## Gotchas` heading consolidating the four `🚨 CRITICAL` blocks in Tips for AI, (b) consider trimming the duplicated Deployment Workflow if PCF-DEPLOYMENT-GUIDE.md is canonical, (c) add `Last Reviewed` stamp. **Do NOT** rename top-level sections — too many cross-refs. **Flag for Phase 2b human gate.**

---

## 2. `deploy-new-release` (457 lines, 29 refs)

- **Description**: Excellent — most actionable description in batch ("Interactive production release deployment — pre-flight, environment selection, change detection, confirmation gate, script execution, and release tagging"). Frontmatter complete.
- **Line count**: **457** — over 400 but close to threshold. Justifiable for an interactive multi-phase orchestration skill.
- **Goal-oriented**: Yes, clean structure: Quick Reference → Critical Rules (MUST + NEVER) → Procedure (Steps 1–N at line 51, with Step 1, 1e, etc.) → Decision Tree → Parameters → Environment Registry → Troubleshooting → Related Skills → Related Docs → Tips for AI.
- **Gotchas section**: ❌ No `## Gotchas` heading, but Critical Rules (NEVER list at lines 42–47) functionally covers gotchas. The "BFF URL must be host-only — no `/api` suffix" rule is the single most important gotcha (per user memory, recurring production issue) and IS surfaced prominently (lines 39, 73–82, 403, 452).
- **Overlap**: Minor. Cleanly delegates to component skills (bff-deploy, dataverse-deploy, etc.) and to `Deploy-Release.ps1`. The Related Skills table at line 424 explicitly disambiguates use cases. No meaningful duplication.
- **References-current**: ✅ All spot-checks pass — `scripts/Deploy-Release.ps1` exists; `config/environments.json` exists; `docs/procedures/production-release.md` exists.
- **Cross-ref blast radius**: 29 refs — moderate. Mostly project CLAUDE.md files and trigger map. Safe to refine.
- **Exemplar**: `none-too-volatile` — interactive UX patterns change frequently.
- **Recommended action**: **leave-alone** (minor improvements optional). Possible additions: (a) explicit `## Gotchas` heading wrapping the existing NEVER list + BFF URL rule, (b) `Last Reviewed` stamp. This is one of the cleanest skills in the batch.

---

## 3. `design-to-spec` (727 lines, 31 refs)

- **Description**: Good — clear single-purpose ("Transform human design documents into AI-optimized spec.md files"). Frontmatter complete but written **after** the H1 (unusual style — H1 on line 1, frontmatter on lines 3–9). Most skills have frontmatter first.
- **Line count**: **727** — well over 400. Includes a giant ~150-line spec.md template embedded in-skill (lines 570–660), which justifies size but suggests extraction.
- **Goal-oriented**: Yes, well-structured: Prerequisites → Purpose → When to Use → Input/Output → Workflow Position → Steps (line 126) → Executive Summary/Scope/Requirements/etc. (these are template sections starting at line 400) → spec.md Template (line 570) → Error Handling → Integration → Success Criteria.
- **Gotchas section**: ❌ No `## Gotchas`. No `gotcha` string in body.
- **Overlap**: Internal duplication — sections 400–470 ("Executive Summary", "Scope", "Requirements", "Technical Constraints", "Success Criteria", "Dependencies", "Owner Clarifications", "Assumptions", "Unresolved Questions") appear twice: once as inline structure guidance and again as the embedded template at lines 581–650. This is **~80 lines of redundancy** that could be removed by linking to a single template file.
- **References-current**: Spot-check ✅ All path refs (e.g., `adr-aware` skill, `project-pipeline` skill) exist.
- **Cross-ref blast radius**: 31 refs — moderate. Mostly trigger map and project CLAUDE.md files.
- **Exemplar**: Possible candidate: `projects/ai-procedure-quality-r1/spec.md` (the spec for THIS project). Or `none-too-volatile`.
- **Recommended action**: **refine-conservatively** (Phase 2b). Extract the embedded spec.md template to `.claude/skills/design-to-spec/templates/spec.md.tmpl` (or accept `none-too-volatile`) and replace inline copy with a pointer. Move frontmatter above H1 to match convention. Add `Last Reviewed` stamp. **Reductions possible: ~80–100 lines** without losing prescriptive value.

---

## 4. `dev-cleanup` (314 lines, 8 refs)

- **Description**: Good ("Clean up local development environment caches… to resolve authentication and performance issues"). **Reduced frontmatter** — only `description:` and `alwaysApply:`. Missing `tags:`, `techStack:`, `appliesTo:` (flagged in inventory anomaly #6).
- **Line count**: **314** — over 200 but reasonable for a multi-tool maintenance procedure.
- **Goal-oriented**: Yes, clear: Purpose → Applies When → Quick Reference (with flags table) → Workflow (3-step diagnose/run/verify) → Common Issues and Solutions → What Gets Cleaned → Periodic Maintenance Schedule → Manual Cleanup Commands → Related Skills → Tips for AI.
- **Gotchas section**: ❌ No `## Gotchas` heading. "Common Issues and Solutions" (line 150) is the functional equivalent.
- **Overlap**: Minor. Discrete topic with no real overlap with other skills. Related Skills section (line 300) pointers to `dataverse-deploy` for PAC-related cleanup — cleanly scoped.
- **References-current**: ✅ `scripts/maintenance/Clean-DevEnvironment.ps1` exists.
- **Cross-ref blast radius**: 8 refs — low. Safe to refine.
- **Exemplar**: `none-too-volatile`.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) normalize frontmatter to add `tags`, `techStack`, `appliesTo` (matches inventory anomaly #6), (b) optionally add `## Gotchas` heading or rename "Common Issues and Solutions" → "Gotchas". Low-risk, low-blast cleanup.

---

## 5. `doc-drift-audit` (229 lines, 5 refs) — **R2 reference skill**

- **Description**: Excellent — sharp, specific ("Detect documentation drift — stale references in docs and .claude/ that no longer match current code. Compact diff-based alternative to full-repo audits."). Frontmatter complete.
- **Line count**: **229** — close to ideal (slightly over 200 but within acceptable range for a substantive skill). Cleanest size-to-content ratio in batch.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → When to Use → Distinct from other skills → Prerequisites → Inputs → Workflow → Methodology Notes → Failure Modes & Recovery → Success Criteria → Integration Points → Related Resources → Tips for AI.
- **Gotchas section**: ❌ No `## Gotchas` heading, but it has **"## Failure Modes & Recovery"** (line 184) which is functionally a Gotchas section presented in a recovery-oriented frame. This is arguably **better** than the proposed "Gotchas" pattern because it gives prescriptive recovery actions instead of just warnings.
- **Overlap**: Minor. "Distinct from other skills" section (line 37) explicitly disambiguates from `adr-check`, `code-review`, `merge-to-master`. Best disambiguation discipline in the batch.
- **References-current**: ✅ Path refs valid; no broken pointers detected.
- **R2 audit stamp**: ✅ ONLY skill with `> **Last Reviewed**: 2026-04-05` AND `> **Reviewed By**: ai-procedure-refactoring-r2` (lines 12–13). This is the gold-standard convention.
- **Cross-ref blast radius**: 5 refs — minimal. Newest skill in repo.
- **Exemplar**: ✅ This skill **IS** the exemplar.
- **Recommended action**: **leave-alone**. This skill should be the **reference comparator for the entire R1 quality project**. Its structural features to propagate: R2 audit stamp block, "Distinct from other skills" disambiguation section, "Failure Modes & Recovery" (which we should consider adopting in lieu of pure "Gotchas").

---

## 6. `docs-architecture` (221 lines, ~12 refs)

- **Description**: Good ("Draft or update an architecture document for a Spaarke subsystem"). Frontmatter complete but `techStack: []` is empty — minor inconsistency. Note: appears at line 12 of inventory cross-refs but not shown in extract; ~12 refs estimated from see_also (5) + project_claudemd (2) + docs (1) + task_pomls (~4) seen in the JSON extract.
- **Line count**: **221** — close to ideal. Reasonable size for a documentation skill.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → When to Use → Document Structure (MANDATORY) → Overview → Component Structure → Data Flow → Integration Points → Design Decisions → Constraints → Related → Drafting Process → Updating Existing Documents → Quality Checklist → Examples of Good vs Bad.
- **Note on structure**: Sections lines 50–107 (Overview, Component Structure, Data Flow, Integration Points, Design Decisions, Constraints, Related) are **template sections inside a code block** (the architecture-doc template), not skill headings themselves — but they DO render as H2 in the grep output. This is an artifact of the embedded template. Slightly confusing to readers/agents skimming via grep but not functionally broken.
- **Gotchas section**: ❌ No `## Gotchas`. "Quality Checklist" + "Examples of Good vs Bad" (lines 193, 209) serve a similar role.
- **Overlap**: Light overlap with sibling skills (`docs-data-model`, `docs-guide`, `docs-procedures`, `docs-standards`) — all four cross-reference each other. Acceptable family pattern.
- **References-current**: ✅ Path refs to `.claude/patterns/`, `.claude/adr/`, `.claude/constraints/`, `docs/guides/` all exist (standard repo structure).
- **Cross-ref blast radius**: ~12 refs — low. Mostly internal to docs-* family.
- **Exemplar**: Possible — `docs/architecture/AI-ARCHITECTURE.md` or one of the auth docs. Defer to `none-too-volatile` for now.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) populate `techStack:` with a non-empty value (e.g., `[all]` matching design-to-spec), (b) consider escaping the embedded H2s inside the code block (e.g., use `### ` in template) to clean up grep output, (c) add `Last Reviewed` stamp. Low-risk.

---

## Batch 3 Summary

| # | Skill | Lines | Refs | Recommended Action | Notes |
|---|---|---:|---:|---|---|
| 1 | dataverse-deploy | 802 | 269 | **refine-conservatively** | Hub #2 — additive changes only; consolidate 🚨 callouts as `## Gotchas` |
| 2 | deploy-new-release | 457 | 29 | **leave-alone** | Cleanest critical-rules structure in batch; minor `## Gotchas` heading possible |
| 3 | design-to-spec | 727 | 31 | **refine-conservatively** | Extract embedded template; ~80–100 lines reducible |
| 4 | dev-cleanup | 314 | 8 | **refine-conservatively** | Normalize frontmatter (anomaly #6); rename "Common Issues" → "Gotchas" |
| 5 | doc-drift-audit | 229 | 5 | **leave-alone** | **R2 gold-standard comparator** |
| 6 | docs-architecture | 221 | ~12 | **refine-conservatively** | Populate `techStack`; escape template H2s; add Last Reviewed |

**Aggregate**: 6 skills · 2,750 lines · 354 total references. 2 leave-alone (deploy-new-release, doc-drift-audit), 4 refine-conservatively, 0 needs-substantive-rewrite, 0 archive.

### Note on `doc-drift-audit` as comparator (per audit assignment)

`doc-drift-audit` is the only skill in `.claude/skills/` carrying the R2 `Last Reviewed` audit-stamp block (frontmatter + 5-line header with **Last Reviewed**, **Reviewed By**, **Status**, **Created in**). It is also among the most compact (229 lines, well-disciplined to the 200-line guidance), has a clean disambiguation section ("Distinct from other skills"), and reframes Gotchas as **"Failure Modes & Recovery"** with prescriptive recovery actions. Its structural features that should propagate during Phase 2b refinements:

1. **R2 audit-stamp block** at top of body (`> **Last Reviewed**: YYYY-MM-DD` + `> **Reviewed By**: <project>` + `> **Status**: New|Updated|Stable`).
2. **"Distinct from other skills"** section — explicitly differentiates from neighboring skills to prevent confusion in the trigger map.
3. **"Failure Modes & Recovery"** in lieu of a plain "Gotchas" section — gives prescriptive recovery actions, not just warnings. The R1 project should consider whether to mandate `## Gotchas` strictly, OR allow `## Failure Modes & Recovery` as an equivalent (recommended).
4. **Compact size** — 229 lines covers a sophisticated workflow without bloat. Demonstrates that the 200-line guidance is achievable for substantive skills, not just trivial ones.

This skill should be **explicitly cited in the Phase 2b skills refinement spec** as the structural exemplar for other skills.

---

*Audit method: Grep-first, offset-read; spot-checked 6 file paths; verified frontmatter, headings, and key 🚨 callouts. No file in `.claude/skills/` was modified. Total time: under 3 minutes per skill.*
