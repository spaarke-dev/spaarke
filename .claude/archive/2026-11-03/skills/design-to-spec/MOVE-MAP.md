# design-to-spec restructure — move map (2026-11-03)

Original: `.claude/skills/design-to-spec/SKILL.md` (819 lines / ~29 KB), archived verbatim at `.claude/archive/2026-11-03/skills/design-to-spec/SKILL.md` (byte-identical, checked with `cmp`).

## Line counts

| File | Before | After |
|---|---|---|
| `SKILL.md` | 819 | 246 (under the 500 cap; no `length-justification` needed) |
| `references/spec-template.md` | — | 150 (`*-template.md`, TOC-exempt) |
| `references/skill-integration.md` | — | 64 |
| `examples/clarification-questions.md` | — | 68 (opens with `## Contents` anyway) |

`python scripts/quality/check-skill-length.py --root <throwaway repo> --baseline /dev/null` → 0 over-cap skills, 0 reference files missing a contents list.

Every step that runs every time stays inline: Steps 0 (new number for the Plan Mode banner), 1, 2, 2.5, 3, 4, 5 and 6, with all MUST/MUST NOT rules, the gap-type table, the question format, the ADR mapping, the required-section rules and the review gate. Moved out: the spec.md template body (filled every run, behind a "read and fill" directive), worked interview examples, and lifecycle background.

## Section map (original line range → destination)

| Original section (lines) | Destination | Notes |
|---|---|---|
| Frontmatter (1–9) | SKILL.md frontmatter | Triggers unchanged (`description`, `appliesTo`, `tags`, `techStack`). Added `name: design-to-spec` (template field). `last-reviewed` → 2026-11-03. **Changed**: `exemplar` → `projects/spaarkeai-word-add-in-r1/spec.md` (judgment call 5) |
| Header blockquote: Last Reviewed / Reviewed By / Exemplar rationale / Phase 2a audit note (13–17) | SKILL.md header (Category, Last Reviewed 2026-11-03, Exemplar + "step numbers are a cited contract") | **Dropped**: "Reviewed By" and the "Phase 2a audit note". Reason: audit history belongs in git/CHANGELOG, and the note's recommendation (extract the embedded template) is what this restructure does. Exemplar rationale rewritten for the new exemplar |
| Prerequisites → Claude Code Effort & Output Configuration (21–30) | SKILL.md "Execution Mode, Model and Settings" (3 bullets) | **Condensed**. Kept: planning on opus/fable at effort `high` (now cites agent-cost.md, which says `high`, not "high or max"); `CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000` only as the fix for truncated output; the researcher-subagent pointer. **Dropped**: "Updated 2026-05-17", "Opus 4.8 / Fable 5", "obsolete on Opus 4.6+" (model version numbers go stale; one "do not set MAX_THINKING_TOKENS" line remains, matching project-pipeline), the PowerShell env-var snippet, and the "root CLAUDE.md §15 (Knowledge Repository)" label (§15 is now "Rapidly changing platform topics"; the pointer kept, label dropped) |
| Permission Mode: Plan Mode banner (32–57) | SKILL.md **Step 0: Plan Mode Confirmation** | Banner turned into prose, matching project-pipeline Step 0's wording. Same behaviour: analysis read-only in Plan Mode, ask to switch to Accept Edits before the first write (spec.md, Step 4); non-interactive clause kept. **Fixed**: heading said "RECOMMENDED" while the body said "MUST … ENFORCED"; now one rule (STOP until on, interactive). **Added** (judgment call 2): what to do with unanswered questions when a non-interactive run is authorized to write |
| Purpose / Key Features / Why This Skill Exists (61–75) | SKILL.md "What It Produces" | Condensed to one paragraph; every feature and reason kept. "Tier 1 Component Skill" → `references/skill-integration.md` diagram |
| When to Use (77–82) | SKILL.md "When to Use" | Same four triggers, as three bullets |
| Input/Output (84–94) | SKILL.md "What It Produces" + Step 1 search order | Inputs are the Step 1 search list; output and "both files kept" in What It Produces and Constraints |
| Workflow Position diagram (96–118) | `references/skill-integration.md` "Workflow position" | Merged with the Integration diagram (752–766), which said the same; added the upstream `use-case-to-design` box (its skill names this one as its downstream) |
| Step 1 (124–141) | SKILL.md Step 1 | Same search order. The "If design document not found" error block (723–733) is folded in as the NOT FOUND branch (two lines; a reference file would cost more than it saves) |
| Step 2 (145–190) | SKILL.md Step 2 | Same five elements, compacted; report block kept. The "If design doc too vague" error block (735–748) folded in as a three-option paragraph (same options: clarify / proceed / stop) |
| Step 2.5 (194–342) | SKILL.md Step 2.5 (rules, gap-type table, grouping, format, wait, incorporate) + `examples/clarification-questions.md` (the seven worked examples and the sample output block) | The seven gap types and their question patterns stay inline as a table (used every run). Worked examples are illustrative → `examples/`. **Fixed**: "Flag assumptions in spec.md 'Questions/Clarifications' section" — no such section exists; the template's section is "Assumptions" |
| Step 3 (346–378) | SKILL.md Step 3 | ADR mapping kept verbatim. **Added** (judgment call 3): "Anything not listed → `.claude/adr/INDEX.md`" (the list was presented as exhaustive; 50 ADRs exist, e.g. auth ADR-028, compose ADR-049, modal ADR-050); item 3 "note each design choice that pushes against a MUST rule → ADR Tensions row" (makes explicit where the mandatory section's content comes from); and the §11 Grep/Glob exception (judgment call 4). The "Scope limitation" DO/DON'T list kept as prose; "adr-aware (implicit)" from Integration (768–769) moved here |
| Step 4 inline spec template (382–529) | `references/spec-template.md` (body) + SKILL.md Step 4 (directive + required-sections table with rules) | **Judgment call 1** below. Headings kept verbatim. Hot-path guidance amended (see judgment call 6) |
| Step 5 (533–567) | SKILL.md Step 5 | Action block and output block merged into one report (same fields, same y/edit/done gate). **Added**: the conditional hot-path warning line (judgment call 6) |
| Next Steps After This Skill: CORRECT / INCORRECT (571–599) | MUST NOT rule in SKILL.md Constraints + Step 6 line; full explanation and the ❌ examples → `references/skill-integration.md` "Why not call the component skills directly" | The rule is inline; the "what project-pipeline performs" bullet list moved. "Task decomposition (50-200+ task files)" softened to "task decomposition" (the count is project-specific) |
| Step 6 (603–614) | SKILL.md Step 6 | Same two branches |
| "## spec.md Template" second copy (618–717) | **Merged** into `references/spec-template.md` | Was a near-duplicate of the Step 4 template ("keep both in sync"). Merge rules: Step 4 headings win where they differed ("MUST Rules (from ADRs)" not "MUST Rules"; "Existing Patterns to Follow" not "Existing Patterns"; "External Dependencies" not "External"; "ADR Tensions (per CLAUDE.md §6.5)" not "… — MANDATORY)") because the exemplar and recent specs use them; the bottom copy contributed "❌ MUST NOT" and the "surface at design time … rather than silent violations" sentence. Nothing in the bottom copy was lost |
| Error Handling (721–748) | SKILL.md Steps 1 and 2 | See those rows |
| Integration with Other Skills (752–773) | `references/skill-integration.md` "Skills involved" + SKILL.md References | **Changed**: "project-setup — consumes spec.md (if called directly)" contradicted the MUST NOT on calling it directly → "only as called by project-pipeline" |
| Success Criteria (777–789) | SKILL.md Success Criteria | All ten items kept (some merged per line); added portfolio sync and step pointers |
| Failure Modes & Recovery table (793–800) | SKILL.md Gotchas (all four rows) | Row 1 kept. Row 2 reworded (judgment call 7). Row 3 kept. Row 4: "Run `/adr-check` against the generated spec … OR open an RFC to update the ADR" → record it in ADR Tensions with path A/B/C. Reason: adr-check validates code changes, and the repo's process is root CLAUDE.md §6.5 (Path B = amendment), not an RFC. Added two gotchas from this restructure: hot-path only in spec.md; "none found" without a search |
| Footer "For Claude Code: … Always preserve the original design document" (804) | **Dropped** | Duplicate of the MUST in Constraints and the Success Criteria item |
| Portfolio Hook appendix (808–819) | SKILL.md end of Step 4 ("Portfolio sync") + history → `references/skill-integration.md` | Behaviour verbatim in meaning (sync, set In Progress, silent/warn/no-op). Placed at the end of Step 4 because the original says "after spec.md is written"; at "end of skill" it would run after a Step 6 pipeline handoff. `docs/guides/HOW-TO-INITIATE-NEW-PROJECT.md:503` ("At end: sync + set Status=In Progress") still holds |

## Externally cited step ids — all preserved

Grep of the whole repo (`.claude/`, `docs/`, `projects/`, root CLAUDE.md; archive excluded) for "design-to-spec … Step N" and "Step N of design-to-spec".

| Cited id | Cited by | Preserved as |
|---|---|---|
| Step 2.5 (gap-targeted interview) | `use-case-to-design/SKILL.md:45`; `projects/spaarke-matter-ui-enhancement-r1/spec.md:475`, `design.md:269`; spec template "Owner Clarifications" | `### Step 2.5: Gap-Targeted Clarification Interview`, same meaning |
| Step 3 (preliminary discovery, ADR constraints only) | `project-pipeline/SKILL.md:136`; `SKILL-INTERACTION-GUIDE.md:239/1061` (as "Preliminary"); `projects/spaarke-devops-project-tracking-r1/{spec,plan,CLAUDE}.md` | `### Step 3: Preliminary Technical Context Discovery`; still ADR constraints only. The single §11 Grep exception does not change what project-pipeline Step 2 says about it |
| "Step 1" (`commands/use-case-to-design.md:69`, `commands/design-to-spec.md:55`, HOW-TO-INITIATE-NEW-PROJECT) | — | These mean "step 1 of the project workflow", not this skill's Step 1. Unaffected |
| Spec section names consumed downstream | project-pipeline Steps 1 (ADR Tensions), 1.7 ("No ADR tensions surfaced"), 3 (hot-path); task-create 3.5.6 / code-review 6.6 (§11 table) | Verbatim in `references/spec-template.md`; listed by name in SKILL.md Step 4's table |

Step numbers added: **Step 0** (Plan Mode, previously an unnumbered banner). No existing number changed.

## Directives added (branch points)

| Where in SKILL.md | Directive |
|---|---|
| Step 4, first line | "Read `references/spec-template.md` and fill it to create `projects/{project-name}/spec.md`" (every run) |
| Step 2.5, after the format block | Pointer to `examples/clarification-questions.md` (illustrative; not needed to run the step) |
| Step 6 | "Why: `references/skill-integration.md`" (background) |

## Judgment calls to review

1. **Spec template moved to `references/spec-template.md`** although it is filled every run. Reasons: (a) the standard allows a `*-template.md` behind a "read and fill" directive; (b) it existed twice and drifted (four heading variants), and one file ends that; (c) the 2026-05 audit (AUDIT-FINDINGS-SKILLS.md row 15) recommended extracting it; (d) the part that is a contract stays inline: Step 4 lists every required section by its exact heading with its rule (ADR Tensions mandatory, hot-path when BFF/SpaarkeAi, §11 table or "modify-only", the "No ADR tensions surfaced" phrase). An agent that skipped the read would still produce every required section. Revert = paste the template back under Step 4 (SKILL.md would be ~390 lines, still under 500).
2. **Non-interactive writes** (Step 0): the original allowed writes "when the invoking prompt explicitly authorizes" them but did not say what to do with questions nobody can answer. Added: BLOCKING → Unresolved Questions, IMPORTANT → Assumptions. This follows the skill's own rule for skipped IMPORTANT questions.
3. **ADR list made non-exhaustive** (Step 3): one line pointing to `.claude/adr/INDEX.md` for unlisted resource types. The mapping itself is unchanged.
4. **§11 Grep exception in Step 3**: the original Step 3 said "DON'T search codebase", while the template's §11 table demanded an "Existing overlap (grep)" column and root CLAUDE.md §11 says "Check with Grep/Glob before saying 'none'". The exception allows only an overlap check per new component; pattern mining stays in project-pipeline Step 2.
5. **Exemplar changed** from `projects/ai-procedure-quality-r1/spec.md` to `projects/spaarkeai-word-add-in-r1/spec.md`. The old exemplar predates the mandatory sections: it has no ADR Tensions, Placement & New Components or Owner Clarifications, so it would fail project-pipeline Step 1 today. The new one (2026-10-05, 308 lines) has every template heading in template order.
6. **Hot-path location**: root CLAUDE.md §10, bff-extensions.md §H and project-pipeline Step 3 all require the `<hot-path-declaration>` in **design.md**; the original skill only seeded spec.md and claimed this pre-empts the Step 3 warning, which it does not. The template now says design.md holds the canonical copy and spec.md copies it; Step 5 reports when design.md lacks it. The skill still does not edit design.md (MUST leave it unchanged). The template's BFF trigger also gained `Spaarke.Core` / `Spaarke.Dataverse` (bff-extensions.md §H defines "BFF" that way).
7. **Gotcha "implementation details copied from design.md" reworded.** The original said "spec should be technology-neutral … move concrete file paths and class names to plan.md", which contradicts the template's own Affected Areas (file paths) and Existing Patterns (`ExampleEndpoints.cs`) sections and project-pipeline's use of them. Kept the intent (don't turn the design's class-level guesses into binding requirements), dropped the file-path clause.
8. **Plan Mode wording aligned** with project-pipeline Step 0 (same switch-before-first-write pattern); the behaviour was already the same.

## Problems in other files (not edited; report)

1. **project-pipeline Step 1 requires a "Technical approach" section** (`project-pipeline/SKILL.md:84`; `project-pipeline/references/error-recovery.md:25`), but this skill's template has never had one: it has "Technical Constraints". Step 2 extracts TECHNICAL APPROACH and the template gives it no home. Either add a `## Technical Approach` section to the template (the old exemplar had one) or change project-pipeline Step 1 to accept "Technical Constraints". Owner decision; the template is unchanged here.
2. **project-setup reads spec sections the template does not produce**: `project-setup/SKILL.md:158–159` copies "Problem"/"Background" and "Solution"/"Approach" sections. The template has Executive Summary and Technical Constraints. Align project-setup with the template headings.
3. **Hot-path block element names disagree**: `.claude/constraints/bff-extensions.md:312–318` (binding) uses `<bff-api>`, `<spaarke-ai>`, `<root-CLAUDE-md>` with "YES | NO — describe"; this template and `use-case-to-design/references/design-template.md:92` use `<bff>`, `<spaarkeai>`, `<root-claude-md>` with `Y|N`. Both forms are in project design docs (13 files with `<bff-api>`, 41 design.md with `<bff>`). No script parses either. Kept verbatim here as instructed; pick one and align both templates.
4. **Hot-path declaration location** (judgment call 6): the template previously told agents that seeding spec.md satisfies project-pipeline Step 3, which checks design.md. Fixed in this skill's text; no other file needs a change unless the owner prefers project-pipeline to accept spec.md.
5. **`.claude/commands/design-to-spec.md:36`** says "Follow the 4-step procedure"; the skill has Steps 0–6 including 2.5. Line 29 "Extended context recommended for complex specs (>2000 words)" is a leftover of the thinking-token prerequisite. Suggest "Follow the steps in the skill" and dropping line 29.
6. **`.claude/skills/task-create/SKILL.md:196`** names "Opus 4.8 / Fable 5" for planning; agent-cost.md and root CLAUDE.md §8.5 name the tier (opus / fable) without versions.
7. **`.claude/AUDIT-FINDINGS-SKILLS.md:145`** recommends extracting the template to `templates/spec.md.tmpl`; it is now `references/spec-template.md` (the current standard's location). Update the row if the audit file is still maintained.
