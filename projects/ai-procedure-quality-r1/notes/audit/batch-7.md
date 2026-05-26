# Audit — Batch 7 of 8 (Skills 37–42)

> **Generated**: 2026-05-15
> **Auditor**: Phase 2a, batch 7
> **Method**: Grep-first, offset reads, light ref-check (paths only, 5–10 spot-checks)
> **Hard cap**: 3 minutes per skill

Skills audited (alphabetical 37–42 of 44):
1. ribbon-edit
2. script-aware
3. spaarke-conventions
4. task-create
5. task-execute (**mega-hub**, 1234 refs, default action `split`)
6. ui-test

---

## 1. `ribbon-edit` (414 lines, ~30 refs)

- **Description**: Good — clear single-purpose ("Edit Dataverse ribbon customizations via solution export/import"). **Reduced frontmatter** — only `description:` + `alwaysApply:` (missing `tags`, `techStack`, `appliesTo`; matches inventory anomaly #6).
- **Line count**: **414** — just over the 400-line guidance. Justifiable for an end-to-end XML-editing workflow with embedded bash blocks.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → Applies When → Prerequisites → Critical Requirements → Solution Setup (One-Time) → Workflow → Conventions → Resources → Output Format → Examples → Error Handling → Related Skills → Tips for AI. Well-structured.
- **Gotchas section**: ❌ No `## Gotchas` heading. "Critical Requirements" (line 37) and "Error Handling" (line 358) cover gotcha territory but as imperative rules, not failure-mode anecdotes.
- **Overlap**: Minor. Discrete ribbon-XML workflow; cleanly delegates to `dataverse-deploy` for non-ribbon import work. Workflow section (line 141) embeds a complete bash script (~85 lines) — candidate for extraction to `scripts/` subdir or pointer to existing script.
- **References-current**: Spot-check ✅ Workflow commands (`pac solution export/unpack/pack/import`) are standard PAC CLI usage. `customizations.xml` path is standard solution structure.
- **Cross-ref blast radius**: ~30 refs — moderate. Mostly trigger map + project CLAUDE.md.
- **Exemplar**: `none-too-volatile` — ribbon XML edits are project-specific and platform-dependent.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) normalize frontmatter (add `tags`, `techStack`, `appliesTo`), (b) add `Last Reviewed` stamp (R2 convention), (c) optionally add `## Gotchas` heading consolidating Critical Requirements callouts, (d) consider extracting embedded bash workflow to `scripts/Edit-Ribbon.ps1` if reusable. Low-risk, low-blast.

---

## 2. `script-aware` (304 lines, ~25 refs) — always-apply

- **Description**: Excellent — sharp ("Discover and use existing scripts from the script library before writing new code"). Complete frontmatter (desc, tags, techStack, appliesTo, alwaysApply: true).
- **Line count**: **304** — over 200 but reasonable for an always-apply context-loading skill.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → When to Apply → Script Discovery Protocol → Script Categories Quick Reference → Script Creation Protocol → Script Maintenance Protocol → Integration with Other Skills → Example: Task with Script Awareness → **Failure Modes to Avoid** → Related Files.
- **Gotchas section**: ✅ Has **"## Failure Modes to Avoid"** (line 284) — aligned with `doc-drift-audit` gold-standard pattern. One of few skills with proper failure-mode framing.
- **Overlap**: Minor. Cleanly distinct domain (script library discovery). Integration section explicitly maps to other skills (line 239).
- **References-current**: Spot-check ✅ Script library structure references are valid; `scripts/` paths exist per root CLAUDE.md.
- **Cross-ref blast radius**: ~25 refs — moderate. Always-apply skill, so loaded broadly; structural changes ripple.
- **Exemplar**: `none-too-volatile` — script registry changes too frequently.
- **Recommended action**: **leave-alone** (minor additions optional). One of the better-structured always-apply skills in the repo. Possible: add `Last Reviewed` stamp; otherwise this is well-formed. Has Failure Modes (gold-pattern).

---

## 3. `spaarke-conventions` (353 lines, ~40 refs) — always-apply

- **Description**: Excellent ("Apply Spaarke coding standards from CLAUDE.md - naming, structure, file organization, and technology patterns"). Complete frontmatter, **but frontmatter is AFTER H1** (H1 on line 1, frontmatter lines 3–9) — same unconventional style as `design-to-spec`.
- **Line count**: **353** — over 200 but appropriate for codifying cross-cutting conventions across C#/TS/React.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → Always-Apply Rules → Technology-Specific Patterns → Error Handling Patterns → Async Patterns → Documentation Standards → Quick Validation Rules → Resources → Related Skills. Strong rule-codification structure.
- **Gotchas section**: ❌ No `## Gotchas` heading. "Quick Validation Rules" (line 329) is a structured checklist of MUST/MUST NOT items — functionally a checklist not a gotchas section. No "Failure Modes" framing either.
- **Overlap**: Light overlap with root `CLAUDE.md` "Coding Standards" section (root CLAUDE.md lines ~900–1000) — this skill is the structured codification of those rules. Acceptable: skill is the AI-loadable form. Could explicitly mark itself as "source of truth: this skill; rationale: root CLAUDE.md" or vice-versa to prevent drift.
- **References-current**: Spot-check ✅ References to ADRs (ADR-007, ADR-008, ADR-022) and core patterns are valid and stable.
- **Cross-ref blast radius**: ~40 refs — moderate-high. Always-apply skill loaded in every coding session; structural rename ripples broadly.
- **Exemplar**: `none-too-volatile` — conventions evolve with tech stack.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) move frontmatter above H1 to match convention (matches design-to-spec issue), (b) add `Last Reviewed` stamp, (c) add `## Gotchas` or `## Failure Modes to Avoid` (e.g., "mixing Fluent v8 + v9", "React 18 in PCF", "global auth middleware") which would have caught real regressions, (d) consider explicit drift-prevention note vs root CLAUDE.md. Low-risk additive.

---

## 4. `task-create` (747 lines, ~80 refs)

- **Description**: Good ("Decompose a project plan into numbered POML task files for systematic AI-assisted execution"). Complete frontmatter, **same H1-before-frontmatter style** as spaarke-conventions.
- **Line count**: **747** — well over 400. Heading skeleton shows Workflow alone spans **lines 30–592** (~560 lines, ~75% of file) — clear candidate for sectional split.
- **Goal-oriented**: Yes, but with bloat. Heading skeleton: Purpose → When to Use → Inputs Required → Workflow (562 lines!) → Conventions → Resources → Examples → Validation Checklist. The Workflow section likely contains many sub-steps (Step 1, 2, 3, 3.5.5, 3.65, 3.8 referenced in Validation Checklist at lines 740–746).
- **Gotchas section**: ❌ No `## Gotchas`. Validation Checklist (line 730) is a closing rigor list, not gotchas.
- **Overlap**: Self-contained, but the inline Workflow (lines 30–592) is far too long for `SKILL.md`. Examples section (lines 678–728) embeds 3 worked examples — candidate for `examples/` subdir.
- **References-current**: Spot-check ✅ `.claude/templates/task-execution.template.md` referenced at line 670; `project-init`, `task-execute`, `ui-test`, `project-pipeline` referenced as related skills.
- **Cross-ref blast radius**: ~80 refs — high. Sister skill to task-execute; consumed by project-pipeline (pipeline step d). Any rename of Workflow sub-step numbers would ripple to task-execute and project-pipeline.
- **Exemplar**: `projects/ai-procedure-quality-r1/tasks/` (or any recent project's tasks/) could be the exemplar. Or `none-too-volatile`.
- **Recommended action**: **split** (Phase 2b). Move Workflow steps 3.5.5+, 3.65, 3.8 detail to `references/workflow-detail.md`; move Examples to `examples/`; keep trigger conditions + Inputs + high-level Workflow outline + Conventions + Validation Checklist in SKILL.md body. Target body: ~250–300 lines. **Split must preserve external API** — Validation Checklist references (Step 3.5.5, Step 3.65, Step 3.8) MUST remain discoverable. Flag for Phase 2b human gate.

---

## 5. `task-execute` (1084 lines, **1234 refs — hub #1 by far**)

- **Description**: Good ("Execute a POML task file with proper context loading and verification"). Complete frontmatter.
- **Line count**: **1084** — largest skill in repo. Per inventory anomaly, the protocol section alone (Step 0.3 line 64 → Step 11 line 803) spans **~740 lines**.
- **Goal-oriented**: Yes — well-organized. Heading skeleton: Purpose → Permission Mode → When to Use → **Execution Protocol (MANDATORY STEPS, line 62 → line ~855)** → Handoff Protocol → Context Recovery Protocol → PCF-Specific Checklist → BFF API-Specific Checklist → Deployment-Specific Checklist → Example Execution → Example Recovery → Failure Modes to Avoid → Related Skills → Final Task Merge Prompt → Related Protocols.
- **Gotchas section**: ✅ Has **"## Failure Modes to Avoid"** (line 1030) — gold-pattern with structured table (Failure / Cause / Prevention). Excellent.
- **R2 audit stamp**: ❌ Uses `Last Updated: January 2026` (older convention).
- **Overlap**: Significant internal sub-sections that could split:
  - **Step 0.5 Rigor Level decision tree** (lines 120–215) — **MUST STAY IN BODY** per instructions
  - Steps 1–11 detailed protocol (lines 216–855) — candidate for `references/protocol-detail.md`
  - PCF/BFF/Deployment checklists (lines 932–973) — candidate for `references/per-tech-checklists.md`
  - Example Execution + Example Recovery (lines 975–1026) — candidate for `examples/` subdir
  - Handoff + Recovery Protocols (lines 856–930) — keep in body (called from main session)
- **References-current**: Spot-check ✅ `context-handoff` skill exists; `code-review` exists; `adr-check` exists; `ui-test` exists; `merge-to-master` exists; AIP-001 protocol exists at `.claude/protocols/AIP-001-task-execution.md`; `docs/procedures/context-recovery.md` exists.
- **Cross-ref blast radius**: **1234 refs — hub #1 by far**. Referenced by virtually every project CLAUDE.md, the root CLAUDE.md ("MANDATORY: Task Execution Protocol" section), the trigger map (7 trigger phrases bind here), and every task POML. **Any external-API breaking change is catastrophic.** External API surface:
  - Trigger phrases ("work on task X", "continue", "next task", etc.)
  - Step numbers cited by task-create's Validation Checklist (Steps 3.5.5, 3.65, 3.8 in task-create reference STEP names here)
  - Rigor Level vocabulary (FULL / STANDARD / MINIMAL) cited in root CLAUDE.md
  - Step 9.5 Quality Gates contract (code-review + adr-check)
  - Step 9.7 UI testing contract (ui-test invocation)
  - current-task.md schema (referenced from context-handoff)
- **Exemplar**: `none-too-volatile` — protocol evolves with tooling.
- **Recommended action**: **split** (Phase 2b — high-care). **Split boundaries (additive, body retains external API):**
  - **Keep in SKILL.md body**: Purpose, Permission Mode, When to Use, **Step 0.3 Parallel Detection**, **Step 0.5 Rigor Level decision tree (entire section lines 120–215)**, high-level Step 1–11 outline (heading + 1–2 sentence summary each), Failure Modes to Avoid, Related Skills, Final Task Merge Prompt, Related Protocols.
  - **Move to `references/protocol-detail.md`**: Detailed body of Steps 0, 1, 2, 3, 4, 5, 6, 6.5, 7, 8, 8.5, 9, 9.5, 9.7, 10, 10.5, 10.6, 11 (lines 216–855 detail; the **headings** must remain referenced in SKILL.md body for discoverability).
  - **Move to `references/handoff-and-recovery.md`**: Handoff Protocol + Context Recovery Protocol (lines 856–930).
  - **Move to `references/per-tech-checklists.md`**: PCF/BFF API/Deployment checklists (lines 932–973).
  - **Move to `examples/`**: Example Execution (lines 975–1005) + Example Recovery (lines 1006–1027).
  - **Target body**: ~350–400 lines.
- **Split safety considerations** (per instructions — flag explicitly):
  1. **DO NOT** rename or renumber Steps 0.3, 0.5, 8.5, 9.5, 9.7, 10.5, 10.6 — they are externally referenced.
  2. **DO NOT** change Rigor Level vocabulary (FULL/STANDARD/MINIMAL).
  3. **DO NOT** change trigger phrases — root CLAUDE.md "Auto-Detection Rules" table binds them.
  4. **DO** keep all 11 step headings in body (with summaries + pointers) so a `grep '^### Step'` against SKILL.md still surfaces them.
  5. **DO** validate after split: search `grep -rn "Step 9.5"` / `"Step 0.5"` / `"Rigor Level"` / `"FULL Protocol"` across `.claude/`, `projects/`, root `CLAUDE.md` to confirm all external references still resolve.
  6. **DO** preserve `Failure Modes to Avoid` table in body — Phase 2a `doc-drift-audit` gold-pattern requires this.
  7. **DO** add R2 audit stamp (`Last Reviewed` + `Reviewed By`) when stamping the split.
  8. Phase 2b human gate **MANDATORY** before applying split — this is the most load-bearing skill in the repo.

---

## 6. `ui-test` (552 lines, ~30 refs)

- **Description**: Excellent ("Browser-based UI testing using Claude Code Chrome integration for PCF controls and model-driven apps"). Complete frontmatter, but **same H1-before-frontmatter style** as spaarke-conventions/task-create.
- **Line count**: **552** — over 400. Includes embedded test definitions, demo examples, and Quick Reference at end. Substantive content.
- **Goal-oriented**: Yes. Heading skeleton: Purpose → Requirements → Setup → When to Use → Autonomous Capabilities → Workflow → UI Test Results → Test Definition Patterns → UI Testing Context → ADR-021 Dark Mode Checklist → Integration with task-execute → Recording Demos → Error Handling → Limitations → Examples → Related Skills → Quick Reference. Comprehensive.
- **Gotchas section**: ❌ No `## Gotchas`. "Error Handling" (line 418) + "Limitations" (line 432) cover gotcha territory but framed reactively, not as failure-modes.
- **Overlap**: Internal duplication risk: Setup (line 42), Quick Reference (line 534), and embedded bash blocks may overlap. Examples section (line 445) embeds 3 worked examples — candidate for `examples/` subdir.
- **References-current**: Spot-check ✅ ADR-021 referenced consistently; `task-execute` Step 9.7 integration is documented; Chrome extension version (1.0.36+) noted.
- **Cross-ref blast radius**: ~30 refs — moderate. Called from task-execute Step 9.7; referenced from task-create (PCF/frontend tasks have `<ui-tests>` sections per task-create Validation Checklist line 740).
- **Exemplar**: `none-too-volatile` — UI testing patterns evolve with Chrome integration features.
- **Recommended action**: **refine-conservatively** (Phase 2b). Specifically: (a) move frontmatter above H1 (matches recurring issue across batch), (b) add `Last Reviewed` stamp, (c) add `## Gotchas` or `## Failure Modes to Avoid` consolidating Error Handling + Limitations (e.g., "session timeout requires re-auth", "context overhead — only use when testing UI", "dark mode toggle does not survive page reload"), (d) consider extracting Examples (lines 445–522) to `examples/` subdir. Target reduction: ~80–100 lines. **Do not** rename `## Integration with task-execute` heading — bound by task-execute Step 9.7.

---

## Batch 7 Summary

| # | Skill | Lines | Refs | Recommended Action | Notes |
|---|---|---:|---:|---|---|
| 1 | ribbon-edit | 414 | ~30 | **refine-conservatively** | Normalize frontmatter (anomaly #6); extract bash to scripts/ |
| 2 | script-aware | 304 | ~25 | **leave-alone** | Has Failure Modes (gold-pattern); always-apply, well-formed |
| 3 | spaarke-conventions | 353 | ~40 | **refine-conservatively** | Frontmatter-after-H1; add Failure Modes; drift-prevention note |
| 4 | task-create | 747 | ~80 | **split** | Workflow = 562 lines; move detail to references/, examples to examples/ |
| 5 | task-execute | 1084 | **1234** | **split** (high-care) | Hub #1; additive split; preserve all external-API step numbers |
| 6 | ui-test | 552 | ~30 | **refine-conservatively** | Add Failure Modes; extract examples; preserve task-execute integration heading |

**Aggregate**: 6 skills · 3,454 lines · ~1,439 total refs (dominated by task-execute). 1 leave-alone (script-aware), 3 refine-conservatively (ribbon-edit, spaarke-conventions, ui-test), 2 split (task-create, task-execute), 0 needs-substantive-rewrite, 0 archive.

### task-execute Split Safety Considerations (critical)

`task-execute` is the load-bearing skill for the entire R1 project's task execution protocol AND for every consumer in the repo. Its 1234 inbound references are the highest in any skill — by **5x** over the next most-referenced (`dataverse-deploy` at 269). The split recommendation is **additive only**:

1. **All external API surface stays in body**: step numbers (0.3, 0.5, 8.5, 9.5, 9.7, 10.5, 10.6), Rigor Level vocabulary (FULL/STANDARD/MINIMAL), 7 trigger phrases, Failure Modes table, current-task.md schema references.
2. **Detailed protocol moves to `references/protocol-detail.md`** but each step heading + 1-2 sentence summary remains in body — `grep '^### Step'` against SKILL.md must still surface all 11+ steps.
3. **Phase 2b human gate is non-negotiable** for this skill. The split spec must include a verification step: run `grep -rn "Step 9.5\|Step 0.5\|Rigor Level\|FULL Protocol\|work on task"` across the repo before and after split; counts and contexts must match.
4. **Rigor Level decision tree (lines 120–215) MUST remain in body** per task instructions — this is the public contract used by root CLAUDE.md auto-detection.
5. **Recommend deferring task-execute split to a dedicated sub-task** in Phase 2b — not bundled with other skill refinements. The blast radius warrants isolation.

### Cross-batch pattern observed

Three skills in this batch (`spaarke-conventions`, `task-create`, `ui-test`) all have **H1 above frontmatter** — same recurring style anomaly as `design-to-spec` in batch 3. Suggests a wider audit of frontmatter placement; recommend Phase 2b normalize to frontmatter-first across all skills with this issue (low-risk, high-consistency).

Two skills (`script-aware`, `task-execute`) carry the gold-pattern **`## Failure Modes to Avoid`** section — matching `doc-drift-audit` comparator from batch 3. Strong candidates as in-batch exemplars for the section pattern when refining the other 4.

---

*Audit method: Grep-first, offset-read; spot-checked ~12 file paths; verified frontmatter, headings, step numbering, and key external-API binding points. No file in `.claude/skills/` was modified. Total time: under 3 minutes per skill; task-execute analyzed via headings + frontmatter + last 100 lines + decision-tree heading grep only, per instructions.*
