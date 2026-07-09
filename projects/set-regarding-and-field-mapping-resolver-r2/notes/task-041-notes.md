# Task 041 — Admin Authoring Guide — Notes

> **Verdict**: COMPLETE (2026-07-09). Rigor: MINIMAL (docs task, no code).
> **Deliverable**: `docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`

## 1. Sources consulted

- `projects/set-regarding-and-field-mapping-resolver-r2/tasks/041-admin-authoring-guide.poml` (task file)
- `projects/set-regarding-and-field-mapping-resolver-r2/notes/task-030-notes.md` — the real seeded Matter→Event/Invoice/Report Card attorney matrix (rule IDs, field pairs, field-name divergence)
- `projects/set-regarding-and-field-mapping-resolver-r2/design.md` §5 (native-form authoring decision + component justification), §11 (documentation deliverables), §4.1/§4.1a (mapping-type semantics, lookup binding)
- `projects/set-regarding-and-field-mapping-resolver-r2/spec.md` FR-17 (documentation acceptance)
- `.claude/skills/docs-guide/SKILL.md` (mandatory guide structure + drafting rules)
- `src/server/api/Sprk.Bff.Api/Models/FieldMapping/FieldMappingRuleDto.cs` — confirmed the four `MappingType` string values (Copy/Default/Concat/Template) and the field semantics (`DefaultValue`, `Expression`) match design.md
- `projects/set-regarding-and-field-mapping-resolver-r2/tasks/013-default-concat-template-engines.poml` — confirmed as-built behavior for Default/Concat/Template (already `completed`): unresolved placeholder → warning + omit token (never left literal, never throws); Concat/Template targeting a lookup → warn + skip; one placeholder resolver serves both Concat and Template. Used this (not a guess) for the guide's "Writing a sprk_expression string" and mapping-type reference sections.
- `docs/guides/INDEX.md` — existing structure, added one row

## 2. What the guide covers (mapped to acceptance criteria)

| Acceptance criterion | Where in the guide |
|---|---|
| Native-form authoring only, no PCF (implied or referenced) | Intro paragraph + Step 1/Step 2 (profile form + subgrid) — explicitly states "There is no custom admin app or PCF control for this." |
| All four mapping types | "Configuration — Mapping-Type Reference" table: Copy, Default/Constant, Concat, Template, each with required fields and behavior |
| `sprk_expression` syntax | "Writing a sprk_expression Concat/Template string" section — `{sprk_field}` placeholder syntax, example `{sprk_matternumber} - {sprk_mattername}`, unresolved-token omit-with-warning behavior |
| Worked example matching seeded attorney matrix | "Worked Example" section — the exact 8-rule Matter→Event table from task-030-notes.md (rule 1-8, same field pairs, same execution order 1-8, all Copy/Lookup-Lookup), plus the Invoice/Report Card divergence note (Invoice renames + drops law-firm/external/internal; Report Card renames only lawfirm1) |

Also covered per the dispatch's explicit requirements list (items 1-6 in the task prompt): profile/rule authoring flow, mapping-type field requirements, expression syntax + token-resolution behavior, profile source/target as `sprk_recordtype_ref` lookups + `sprk_executionorder`/`sprk_isactive` semantics, the worked example, and a Troubleshooting table covering: profile inactive, missing `sprk_recordtype_ref` row (citing the real Report Card blocker resolution from task 030 §8 as a real precedent), rule inactive/misconfigured, lookup field-type flags not set, target field name mismatch (field-name divergence), unresolved expression placeholder, Concat/Template targeting a lookup, and the creation-time-vs-manual-push boundary.

## 3. Cross-link to task 040's architecture doc

`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` did **not** exist at the time this task finished (task 040 running in parallel, different file). Per dispatch instructions, the guide's "Related" section notes the doc conditionally rather than linking a file that doesn't exist yet — the main session should verify the doc exists post-merge and firm up the link (it should already resolve correctly once 040 lands, since the filename/path match design.md's expected location).

## 4. Boundaries respected

- Did not edit `TASK-INDEX.md` or `current-task.md` (explicit dispatch boundary).
- Did not touch `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` (task 040's file).
- Did not write to `.claude/`.
- Set `<status>completed</status>` and appended a completion note in the 041 POML (permitted).
- Added one row to `docs/guides/INDEX.md` (Workspace & Entity Creation section) per the docs-guide skill's Step 3.6 ("ADD to docs/guides/INDEX.md") — this is a docs-guide skill requirement, not a restricted `.claude/`/index-tracking file.

## 5. Follow-up for main session

- Confirm task 040's architecture doc lands at `docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md` and firm up/verify the cross-link in this guide's Related section once merged.
- Root `CLAUDE.md` §17 pointer-table update (spec.md FR-17 acceptance also requires this) is out of scope for this task (CLAUDE.md is not editable by a sub-agent) — main session should add a §17 pointer row for both the architecture doc and this admin guide once task 040 lands.
