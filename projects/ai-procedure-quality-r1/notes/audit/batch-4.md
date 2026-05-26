# Phase 2a Skills Audit — Batch 4 (skills 19–24)

> **Generated**: 2026-05-15
> **Task**: 023-audit-skills-batch-4
> **Scope**: 6 skills (alphabetic positions 19–24 of 44)
> **Mode**: READ-ONLY on `.claude/skills/`. No source code changes.
> **Honesty**: Findings reflect the files as-read. Skills already in good shape are recommended `leave-alone`.

Skills in this batch:

1. `docs-data-model`
2. `docs-guide`
3. `docs-procedures`
4. `docs-standards`
5. `jps-action-create`
6. `jps-playbook-audit`

This batch contains **4 sibling documentation skills** (`docs-data-model`, `docs-guide`, `docs-procedures`, `docs-standards`) plus **2 JPS skills** that pair naturally with `jps-playbook-design` (batch 5) and `jps-scope-refresh`/`jps-validate` (already audited or in other batches). Cross-family overlap is assessed explicitly in the Batch Summary.

---

## Skill 19 — `docs-data-model`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Draft or update a data model document — entity schemas, relationships, field mappings, and JSON field contracts" — clear and trigger-leading. |
| Body line count (incl. frontmatter) | **95 lines** | **PASS** — well under 200. |
| Goal-oriented framing | ⚠️ Mostly | Has a `## Purpose` section + "NOT" list. Lacks a dedicated `## When to Use` section that the canonical scaffold mandates (the trigger phrases are folded into "When to Use" already — actually present at line 27–32). Goal framing is satisfied. |
| `## Gotchas` section (as heading) | ❌ Absent | No dedicated heading. |
| Constraints / MUST-MUST-NOT block | ❌ Absent | Drafting Rules section behaves like soft guidance; lacks the explicit MUST/MUST NOT shape of the canonical template. |
| Deterministic rules | ✅ Mostly | "Drafting Rules" (lines 89–96) are concrete (alternate-keys must be documented, JSON schema contract documented, etc.). |
| `Last Reviewed` stamp (R2 convention) | ❌ Absent | Uses `Last Updated: April 2026`. |
| `exemplar:` frontmatter | ❌ Absent | Not opted in or out. |
| Overlap with sibling skills | ⚠️ Material | See Batch Summary — partially differentiated against docs-architecture/docs-guide via "NOT" list but functional charter is narrow. |
| References current | ✅ | Document Structure templates reference `docs/data-model/` which exists with 18+ entity docs (verified `c:/code_files/spaarke/docs/data-model/`). |

### Light reference check (mandatory)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | Only path referenced is `docs/data-model/` (line 18) — directory exists with 21 files. |
| URLs | n/a | No URLs in body. |
| Dependent skill names | ✅ Pass | References `/docs-guide`, `/docs-architecture` — both exist. |

### Medium reference check (commands / code refs)

| Check | Status | Notes |
|---|---|---|
| Commands | n/a | No commands prescribed. |
| Code refs | ✅ Pass | Mentions `FetchXML and WebAPI queries` and `pac modelbuilder` conceptually; no specific file pointers to verify. |

### Cross-ref impact for any merge/split/remove

- **Referenced by** (from `skill-cross-refs.json` lines 1784–1834): 0 invocations, 0 see_also from other skills, 0 trigger_map, 2 references in `projects/ai-procedure-refactoring-r2/CLAUDE.md`, 30+ POML references in `ai-procedure-refactoring-r2` tasks. **Total 37 references** — entirely contained in one historic project's tasks + that project's CLAUDE.md. No live skill consumers.
- Worth noting: `docs-guide` lines 21, 23 reference back to `docs-data-model`; `docs-data-model` lines 21–22 reference `docs-architecture` and `docs-guide`. Tight intra-family graph.
- Merge into `docs-architecture` is plausible (both touch dataverse modeling) but only after assessing data-model corpus volume (21 files) is too large to fold into architecture descriptions.

### Exemplar recommendation

- `exemplar: c:/code_files/spaarke/docs/data-model/sprk_matter-related-tables.md` (a comprehensive entity-relationship doc that follows the prescribed structure).

### Recommended action

**`refine-in-place`** — add `Last Reviewed` stamp (replace `Last Updated`), add a `## Gotchas` section (even if "No gotchas surfaced yet" stub), add `exemplar:` frontmatter pointing at one of the 21 existing data-model docs, and replace the soft "Drafting Rules" with explicit MUST/MUST NOT shape per `_template/SKILL.md`. Body would grow ~30 lines but stay well under 200.

---

## Skill 20 — `docs-guide`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Draft or update an operational guide for a Spaarke functional module" — clear. |
| Body line count | **263 lines** | **OVER-TARGET** — between 201 and 400. F-15.1 tier: justified content density (mandatory structure template is verbose; 5 quality-checklist sections; examples). Recommend leaving size as-is, NOT splitting. |
| Goal-oriented framing | ✅ Yes | "How to do things" charter is explicit, with "NOT" exclusions and a mandatory structure. |
| `## Gotchas` section | ❌ Absent | No dedicated heading. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | "DO NOT INCLUDE" block (lines 167–172) and "RULES" blocks function as constraints but aren't in the canonical MUST/MUST NOT shape. |
| Deterministic rules | ✅ Good | Mandatory structure (lines 38–119), drafting process (lines 120–192), quality checklist (lines 234–248) all deterministic. |
| `Last Reviewed` stamp | ❌ Absent | Uses `Last Updated: April 2026`. |
| `exemplar:` frontmatter | ❌ Absent | |
| Overlap with sibling skills | ⚠️ Material | See Batch Summary. |
| References current | ✅ | References `docs/guides/` (exists, 30+ files), `CLAUDE.md`, `scripts/`. |

### Light reference check

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `docs/guides/`, `CLAUDE.md`, `scripts/`, `docs/architecture/`, `.claude/patterns/`, `.claude/adr/`, `docs/guides/INDEX.md` — all exist. |
| URLs | n/a | No URLs. |
| Dependent skill names | ✅ Pass | `/docs-architecture` exists. |

### Medium reference check

| Check | Status | Notes |
|---|---|---|
| Example values | ✅ Pass | References `spe-api-dev-67e2xz` (matches root CLAUDE.md), `Redis:Enabled` config (consistent with existing options patterns), `az keyvault secret show` (standard Az CLI). |
| Length tier guidance | ✅ Self-consistent | Says simple guides 100–200 lines, complex up to 1200; this is internally consistent. |

### Cross-ref impact

- **Referenced by**: 0 invocations, **6 see_also** entries (from `docs-architecture`, `docs-data-model` ×2, `docs-procedures`, `docs-standards`) — this skill is the **hub of the docs-* family**, every sibling points to it. Plus 50+ POML references and 1 entry in `docs/procedures/AI-CODING-PROCEDURES-GUIDE.md:205`. **Total 59 references** — the most-referenced docs skill.
- Removing or renaming would cascade into 4 sibling skills + dozens of historical tasks. Safest action is refinement, not consolidation.

### Exemplar recommendation

- `exemplar: c:/code_files/spaarke/docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — a representative operational guide that follows the prescribed structure (or alternatively `DATAVERSE-MCP-INTEGRATION-GUIDE.md`). Either reasonable.

### Recommended action

**`leave-alone-justified`** — body at 263 lines exceeds the 200-line soft target but is operationally dense and well-structured; per F-15.1, over-target-but-warranted skills qualify for `leave-alone-justified`. Recommended cosmetic additions only: `Last Reviewed` stamp, `exemplar:` frontmatter, and a `## Gotchas` section stub. **Do not split or merge** — this is the hub of the docs-* family.

---

## Skill 21 — `docs-procedures`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Draft or update a development procedure — testing strategy, CI/CD workflow, code review checklists, dependency management" — clear and bound. |
| Body line count | **91 lines** | **PASS** — well under 200. |
| Goal-oriented framing | ✅ Yes | Has a clear charter and a sharp distinction-from-guides paragraph (line 26: "procedures are about the development process itself, while guides are about operating or configuring the product"). |
| `## Gotchas` section | ❌ Absent | |
| Constraints (MUST/MUST NOT) | ❌ Absent | "Drafting Rules" function as guidance, not hard constraints. |
| Deterministic rules | ✅ Mostly | 5 drafting rules (lines 85–91) are concrete. |
| `Last Reviewed` stamp | ❌ Absent | Uses `Last Updated`. |
| `exemplar:` frontmatter | ❌ Absent | |
| Overlap with sibling skills | ⚠️ Material | See Batch Summary. |
| References current | ✅ | `docs/procedures/` exists with 9 files (verified). |

### Light reference check

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `docs/procedures/`, `.claude/skills/`, `.claude/adr/` — all exist. |
| URLs | n/a | No URLs. |
| Dependent skill names | ✅ Pass | References `/docs-guide`, `/docs-architecture`, `/docs-standards`, `/code-review`, `/adr-check` — all 5 exist. |

### Medium reference check

| Check | Status | Notes |
|---|---|---|
| Commands | n/a | No commands prescribed. |
| Code refs | ✅ Pass | Conceptual references only. |

### Cross-ref impact

- **Referenced by**: 0 invocations, 0 see_also, 2 CLAUDE.md project refs, 20 POML references in `ai-procedure-refactoring-r2/tasks/` (5 task files). **Total 22 references** — historical project consumption only.
- Tight cross-pointing to siblings: lines 21 (docs-guide), 23 (docs-standards), 22 (docs-architecture). Removing or consolidating would require updating 3 siblings.

### Exemplar recommendation

- `exemplar: c:/code_files/spaarke/docs/procedures/testing-and-code-quality.md` (or `ci-cd-workflow.md`) — both follow the procedure structure.

### Recommended action

**`refine-in-place`** — same as `docs-data-model`: add `Last Reviewed` stamp, `## Gotchas` stub, `exemplar:` frontmatter, and reshape "Drafting Rules" as MUST/MUST NOT block per `_template/SKILL.md`. Stays under 200 lines.

---

## Skill 22 — `docs-standards`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Draft or update a standards document — cross-cutting coding conventions, anti-patterns, and integration contracts" — sharp. |
| Body line count | **82 lines** | **PASS** — the smallest skill in this batch. |
| Goal-oriented framing | ✅ Yes | Charter explicit, "NOT" exclusions present. |
| `## Gotchas` section | ❌ Absent | |
| Constraints (MUST/MUST NOT) | ⚠️ Partial | "Drafting Rules" (lines 78–83) include "every rule must be verifiable" — a meta-constraint but not the canonical shape. Body sample (lines 49–53) DOES demonstrate MUST/MUST NOT in a template — this is good. |
| Deterministic rules | ✅ Good | "Every rule must be verifiable" and "Keep total length under 300 lines" are sharp and testable. |
| `Last Reviewed` stamp | ❌ Absent | |
| `exemplar:` frontmatter | ❌ Absent | |
| Overlap with sibling skills | ⚠️ Material | See Batch Summary. |
| References current | ✅ | `docs/standards/` exists with 6 files. |

### Light reference check

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `docs/standards/`, `.claude/adr/` — exist. |
| URLs | n/a | No URLs. |
| Dependent skill names | ✅ Pass | References `/docs-architecture`, `/docs-guide` — exist. |

### Medium reference check

| Check | Status | Notes |
|---|---|---|
| Commands | n/a | None prescribed. |
| ADR linkage | ✅ Pass | Body example template encourages linking ADRs; no specific ADR cited so nothing to verify. |

### Cross-ref impact

- **Referenced by**: 0 invocations, **1 see_also** from `docs-procedures:23`, 2 project CLAUDE.md refs, 15 POML refs in `ai-procedure-refactoring-r2/tasks/`. **Total 18 references** — the lowest-referenced docs-* skill.
- Removing would cascade only to `docs-procedures:23` (single see_also). Lowest-impact removal in the docs-* family if ever consolidated.

### Exemplar recommendation

- `exemplar: c:/code_files/spaarke/docs/standards/CODING-STANDARDS.md` (or `ANTI-PATTERNS.md`, `INTEGRATION-CONTRACTS.md`) — all three follow the prescribed structure.

### Recommended action

**`refine-in-place`** — same treatment as `docs-procedures`: stamp, gotchas stub, exemplar, MUST/MUST NOT reshape. Body would still be ~115 lines, well under 200.

---

## Skill 23 — `jps-action-create`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Create a new JPS definition for an Analysis Action" — clear. |
| Body line count | **282 lines** | **OVER-TARGET** (201–400) — operationally dense (JSON schema template at lines 60–121 is the main bulk; 3 worked examples; error-handling table). Justifiable under F-15.1. |
| Goal-oriented framing | ✅ Yes | "Tier 1 Component Skill" charter is explicit; "Why This Skill Exists" present. |
| `## Gotchas` section | ❌ Absent — but has Tips/Error Handling | Lines 256–263 (Error Handling table) and lines 275–283 (Tips for AI) substitute. The R1 template asks for a dedicated `## Gotchas` heading. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | Step-3 "Rules" list (lines 122–130) functions as MUST/MUST NOT but is bullets, not block-form. ADR-015 callout is concrete. |
| Deterministic rules | ✅ Strong | Schema validation checklist (Step 4, lines 133–145) is fully deterministic. |
| `Last Reviewed` stamp | ❌ Absent | No date stamp in body. |
| `exemplar:` frontmatter | ❌ Absent | |
| Anomaly: Step 6 appears twice | ⚠️ Yes | Lines 157–163 (offer-next-steps Step 6) and lines 237–251 (refresh-scope-index Step 6). Same number, two different steps. |
| References current | ❌ **Multiple BROKEN** | See Light ref check below — directory `projects/ai-json-prompt-schema-system/` was renamed to `projects/x-ai-json-prompt-schema-system/`. |

### Light reference check (FAILS)

| Reference type | Status | Notes |
|---|---|---|
| File paths | ❌ **FAIL — multiple broken** | (1) `projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json` (line 53) — directory is now `projects/x-ai-json-prompt-schema-system/notes/jps-conversions/` per glob. (2) Same broken root in line 54 (`clause-analyzer.json`). (3) Same broken root in line 149 (save path). (4) Same broken root in line 169 (conventions). (5) Line 181 in the output-format template. **Total 5 broken path references** to a directory that was archived (likely during repo cleanup that prefixed it with `x-`). |
| URLs | ⚠️ One URL | `https://spaarke.com/schemas/prompt/v1` — unverifiable from local filesystem; assumed valid as it's the JPS `$schema` constant. |
| Dependent skill names | ✅ Pass | References `jps-playbook-design`, `jps-scope-refresh`, `jps-validate`, `dataverse-deploy` — all exist. |
| Referenced doc | ✅ Pass | `docs/guides/JPS-AUTHORING-GUIDE.md` (line 47) — verified exists. |
| Referenced scripts | ✅ Pass | `scripts/Seed-JpsActions.ps1` (line 161) — verified exists. `scripts/Refresh-ScopeModelIndex.ps1` (line 244) — verified exists. |

### Medium reference check

Stopped at the 10-min cap once the broken-path pattern was confirmed — this skill requires substantive rewrite to update paths, not bulk-audit refinement.

### Cross-ref impact

- **Referenced by**: 0 invocations, **13 see_also** entries (mostly from `jps-playbook-design`, plus `jps-scope-refresh`, `jps-validate`, `jps-playbook-audit:259`), 2 CLAUDE.md trigger-map entries (lines 719, 791), 2 doc refs. **Total 17 references**. Live and widely referenced across the JPS family.
- Renaming the broken paths is non-destructive — just an Edit replacing `ai-json-prompt-schema-system` with `x-ai-json-prompt-schema-system` in 5 places. BUT: if the intent of the `x-` prefix was archival/quarantine, the canonical-pattern files should likely be **moved out of the archive and into a stable location** before the skill is rewritten. This is a project-side decision, not an audit decision.

### Exemplar recommendation

- After path fix: `exemplar: projects/x-ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json` (the simple-extraction reference). The skill body already names this as "gold standard" (line 277).

### Recommended action

**`needs-substantive-rewrite`** — per F-15.3, this is exactly the kind of skill the spec calls out: structure is fine, but 5 path references point to a renamed/archived directory, and Step 6 is duplicated. Bulk-audit refinement is the wrong tool. Recommend a focused follow-up session that (a) decides where the canonical JPS examples should live, (b) updates the 5 path references in lockstep, (c) merges or renumbers the two Step 6 sections, and (d) adds the `Last Reviewed` stamp, `## Gotchas` heading, and `exemplar:` frontmatter at the same time.

---

## Skill 24 — `jps-playbook-audit`

### Structural

| Check | Result | Notes |
|---|---|---|
| `description` line | ✅ Present | "Audit existing playbooks against current scope catalog and standards, then recommend or apply updates" — clear. |
| Body line count | **273 lines** | **OVER-TARGET** (201–400). Operationally dense — 7-step workflow, 4 audit-check categories, audit-mode table, error-handling table, 7 AI-tips. Justifiable under F-15.1. |
| Goal-oriented framing | ✅ Yes | "Tier 2 Orchestrator Skill" charter with "Why This Skill Exists". |
| `## Gotchas` section | ❌ Absent — has Tips/Error Handling | Same substitution as `jps-action-create`. |
| Constraints (MUST/MUST NOT) | ⚠️ Implicit | Step 6 "Apply Updates" has implicit guardrails ("never auto-apply red-X errors"), but they're scattered (lines 232–238 Conventions, line 270 Tips). |
| Deterministic rules | ✅ Strong | Compliance check structure (lines 81–116) is fully deterministic with classification rubric (🔴/🟡/🟢/✅). |
| `Last Reviewed` stamp | ❌ Absent | |
| `exemplar:` frontmatter | ❌ Absent | This is a candidate for `exemplar: none-too-volatile` — playbook audits run against live Dataverse data that changes constantly. |
| References current | ✅ Mostly | See checks below. |

### Light reference check

| Reference type | Status | Notes |
|---|---|---|
| File paths | ✅ Pass | `.claude/catalogs/scope-model-index.json` (line 37) — verified exists. `projects/{project}/notes/playbook-audit-{date}.md` (line 215, 234) — pattern path, not a specific file. |
| URLs | ✅ Pass | `https://spaarkedev1.crm.dynamics.com` (line 52) — matches root CLAUDE.md Dataverse Dev env. |
| Dependent skill names | ✅ Pass | References `jps-playbook-design`, `jps-scope-refresh`, `jps-action-create`, `jps-validate`, `dev-cleanup` — all exist. |

### Medium reference check

| Check | Status | Notes |
|---|---|---|
| Dataverse Web API endpoints | ✅ Pass | Endpoint structure (lines 52–78) matches Dataverse Web API v9.2 conventions. URLs use `$select`, `$filter`, `$expand` correctly. Relationship names (e.g. `sprk_playbooknode_skill`, `sprk_analysisplaybook_action`) are plausible Dataverse N:N intersect names — not verified against live metadata but syntactically correct. |
| Command line | ✅ Pass | `az account get-access-token --resource ...` (line 52) is standard. |

### Cross-ref impact

- **Referenced by**: 0 invocations, 0 see_also from other skills, 2 CLAUDE.md trigger-map entries (lines 720, 792), 1 doc ref. **Total 3 references** — the lowest reference count of any skill in the batch.
- Low-impact for any refactor — but also low evidence the skill is actively used. **Honesty flag**: this skill may be aspirational. No usage telemetry in repo. Worth asking whether it's been invoked once since creation.

### Exemplar recommendation

- `exemplar: none-too-volatile` — audit runs against live Dataverse data and an evolving scope catalog. Maintaining a frozen exemplar output would not be valuable.

### Recommended action

**`refine-in-place`** — Add `Last Reviewed` stamp, `## Gotchas` heading (move the "Tips for AI" anti-patterns into it), `exemplar: none-too-volatile` with rationale, and reshape Conventions block as MUST/MUST NOT. **Body stays well under 400 lines.** Optionally flag for `usage-audit` — if no developer has invoked this skill in 6+ months, consider archival.

---

## Batch 4 Summary

### Per-skill action recommendations

| # | Skill | Action | Lines | Reason |
|---|---|---|---:|---|
| 19 | `docs-data-model` | `refine-in-place` | 95 | Stamp + gotchas + exemplar + MUST/MUST NOT reshape; stays under 200. |
| 20 | `docs-guide` | `leave-alone-justified` | 263 | Over-target but operationally dense; hub of docs-* family (6 see_also inbound). Cosmetic stamps only. |
| 21 | `docs-procedures` | `refine-in-place` | 91 | Same cosmetic treatment as data-model. |
| 22 | `docs-standards` | `refine-in-place` | 82 | Same cosmetic treatment; smallest skill in batch. |
| 23 | `jps-action-create` | **`needs-substantive-rewrite`** | 282 | 5 broken path references to renamed `x-ai-json-prompt-schema-system/`. Step 6 duplicated. Per F-15.3, do not bulk-refine. |
| 24 | `jps-playbook-audit` | `refine-in-place` (+ optional usage-audit) | 273 | Cosmetic stamps + `exemplar: none-too-volatile`. Low inbound reference count — confirm active usage. |

### Mutual overlap among `docs-*` skills (REQUIRED ASSESSMENT)

The 4 `docs-*` skills in this batch + `docs-architecture` (audited in batch 3) form a **tight family of 5 sibling skills**, each scoped to one `docs/` subdirectory.

**Overlap matrix** (extracted from each skill's "NOT" exclusions and Purpose statements):

| Skill | Owns | Explicitly excludes (delegates to) |
|---|---|---|
| `docs-architecture` (batch 3) | `docs/architecture/` — component structures, data flows, design decisions | `/docs-guide` (procedures), `/docs-guide` (how-tos), code (API/schema), `.claude/patterns/` (inline examples) |
| `docs-data-model` | `docs/data-model/` — entity schemas, relationships, JSON contracts | `/docs-guide` (how to query), `/docs-architecture` (design decisions), `/docs-guide` (admin) |
| `docs-guide` | `docs/guides/` — procedures, configuration, deployment, troubleshooting | `/docs-architecture` (structure, data flow, design rationale), `.claude/patterns/` (code patterns), `.claude/adr/` (ADR constraints) |
| `docs-procedures` | `docs/procedures/` — testing strategy, CI/CD, code review, dependency mgmt | `/docs-guide` (feature procedures), `/docs-architecture` (system), `/docs-standards` (conventions) |
| `docs-standards` | `docs/standards/` — cross-cutting conventions, anti-patterns, integration contracts | `/docs-architecture` (module-specific), `/docs-guide` (step-by-step), `.claude/adr/` (ADRs) |

**Verdict on overlap**:
- Each skill has a **clearly delineated charter** with explicit "NOT" exclusions pointing at the right sibling. The boundaries match the actual `docs/` directory taxonomy adopted during R2.
- The boundary between `docs-guide` (feature procedures) and `docs-procedures` (dev-process procedures) is the most subtle. `docs-procedures` line 26 has a clean one-sentence distinction: "procedures are about the development process itself, while guides are about operating or configuring the product." This is adequate.
- The boundary between `docs-data-model` and `docs-architecture` (when documenting an entity model) is also subtle: `docs-data-model` line 21 delegates "Design decisions about the data model" to `docs-architecture`. Whether developers in practice respect this is unclear — but the documented intent is clean.
- **No two skills compete for the same `docs/` subdirectory.** They are siblings, not duplicates.

**Recommendation**: Do **NOT** merge the docs-* skills. They are well-factored. The cosmetic gaps (no stamps, no `## Gotchas` heading, no `exemplar:` frontmatter, soft "Drafting Rules" instead of MUST/MUST NOT) are bulk-refinable across all 5 in a single Phase 2b task. After cosmetic refinement, the family will be exemplary documentation skills.

**Caveat for cross-batch**: `docs-architecture` (batch 3, 221 lines) should be assessed identically. If batch 3 recommended merging it with `docs-data-model`, this batch disagrees — they own different `docs/` subdirectories with non-overlapping content patterns and should remain separate.

### Mutual overlap among `jps-*` skills (cross-batch caveat)

`jps-action-create` (this batch) and `jps-playbook-audit` (this batch) are **NOT mutually overlapping**:
- `jps-action-create` creates a **single JPS definition** for one Analysis Action (Tier 1 Component).
- `jps-playbook-audit` reviews **multiple existing playbooks** in Dataverse and recommends updates (Tier 2 Orchestrator).

They explicitly cross-reference each other (action-create line 161 points at playbook-design; playbook-audit line 259 points at action-create). The relationships are tiered (action-create + playbook-design + scope-refresh + validate are sibling Tier 1 skills under the playbook-design orchestrator). No merge candidate within this batch.

**Cross-batch caveat for batch 5**: `jps-playbook-design` (likely in batch 5) is the orchestrator that calls `jps-action-create` (this batch). When batch 5 audits playbook-design, it should:
1. Verify the see_also references from playbook-design → action-create are still valid after action-create's path fixes.
2. NOT propose merging action-create into playbook-design — they are intentionally separated (Tier 1 component vs. Tier 2 orchestrator per the JPS skill's own tier framing).

### Cross-batch action signals for the main session

| Signal | Action for main session |
|---|---|
| `jps-action-create` has 5 broken paths to `projects/x-ai-json-prompt-schema-system/` | Open a substantive-rewrite ticket for this skill; do NOT bulk-refine in Phase 2b. |
| All 6 skills lack the canonical `## Gotchas` heading | Bulk-fix in a single Phase 2b task with one `Add Gotchas heading + Last Reviewed stamp + exemplar frontmatter` edit pattern. |
| Sibling docs-* skills cross-reference each other 8 times | When applying any rename/merge to a docs-* skill, verify all 8 cross-references in lockstep. |
| `jps-playbook-audit` has only 3 inbound references and no see_also from other skills | Optional follow-up: confirm whether the skill has been invoked since creation. If never used, consider archival under NF-1 (reversibility). |

### Skills inventory math verification

The 6 skills audited in this batch occupy alphabetic positions 19–24 of the 44 skills inventoried in `notes/inventory/skills.md`. Line counts match the inventory:

| Skill | Inventory line count | Confirmed |
|---|---:|:-:|
| docs-data-model | 95 | ✅ |
| docs-guide | 263 | ✅ |
| docs-procedures | 91 | ✅ |
| docs-standards | 82 | ✅ |
| jps-action-create | 282 | ✅ |
| jps-playbook-audit | 273 | ✅ |

---

*Audit completed 2026-05-15. Findings preserved for Phase 2b human review per F-3 / NF-3.*
