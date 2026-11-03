---
name: design-to-spec
description: Transform human design documents into AI-optimized spec.md files
tags: [project-init, design, spec, planning, transformation]
techStack: [all]
appliesTo: ["projects/*/design.md", "projects/*/design.docx", "transform spec", "design to spec"]
alwaysApply: false
exemplar: projects/spaarkeai-word-add-in-r1/spec.md
last-reviewed: 2026-11-03
---

# design-to-spec

> **Category**: Project-orchestration
> **Last Reviewed**: 2026-11-03
> **Exemplar**: `projects/spaarkeai-word-add-in-r1/spec.md` follows the current template section for section, including Placement & New Components, Hot-Path Declaration and ADR Tensions. Other skills and project files cite this skill's step numbers (Step 2.5, Step 3), so never renumber them.

## When to Use

- The user says "transform spec", "design to spec" or "create AI spec", or invokes `/design-to-spec {project-path}`.
- A design document exists at `projects/{project-name}/design.md` (or `.docx`, `.pdf`), or the user supplies the design as text.
- Before `/project-pipeline`: this skill writes the `spec.md` that the pipeline consumes.

## What It Produces

Human design documents are narrative. Implementing agents need explicit constraints, file paths, ADR references and scope boundaries. This skill reads the design, asks the owner targeted questions about its gaps, and writes `projects/{project-name}/spec.md`: scope, numbered requirements with acceptance criteria, ADR constraints, the governance seeds the pipeline enforces (placement, hot-path, §11 new components, ADR tensions), owner clarifications, assumptions and unresolved questions. The design document stays beside it, unchanged.

## Execution Mode, Model and Settings

- **Model and effort**: run on opus, or fable for the hardest designs, at effort `high` (`.claude/constraints/agent-cost.md`; root CLAUDE.md §8.5).
- **Output length**: if spec.md comes back truncated, set `CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000` and restart the session. Do not set `MAX_THINKING_TOKENS`; current models ignore it.
- **Platform questions** (Microsoft / AI platform behaviour the design depends on): use the `researcher` subagent (root CLAUDE.md §15).

## Constraints (MUST / MUST NOT)

- **MUST** run the Step 2.5 interview on the gaps in THIS design. No generic checklist questions.
- **MUST** emit every required spec.md section with its heading verbatim (Step 4), including ADR Tensions when there are none. project-pipeline Step 1 stops on a spec without it.
- **MUST** record owner answers under Owner Clarifications, unanswered IMPORTANT questions under Assumptions, and anything still blocking under Unresolved Questions.
- **MUST** leave the design document unchanged as a project artifact. spec.md is a new file beside it.
- **MUST NOT** do comprehensive resource discovery here. Step 3 loads ADR constraints only; project-pipeline Step 2 does the full pass.
- **MUST NOT** run `/project-setup` or `/task-create` after this skill. project-pipeline calls them after its resource discovery; called directly, project-setup misses discovery and task-create has no plan.md.
- **MUST NOT** write spec.md before the analysis has been presented (Step 0).

## Steps

### Step 0: Plan Mode Confirmation

Plan Mode enforces "understand before write". Without it, spec.md gets written from an incomplete analysis.

- **Interactive session**: ask the user to confirm Plan Mode is on (the ⏸ indicator; Shift+Tab cycles modes). If it is not on, STOP until it is.
- In Plan Mode, run the read-only work: Steps 1–3, including the Step 2.5 interview. Present the extracted elements, the owner's answers, the applicable ADRs and the planned spec.md sections as the plan. Before writing spec.md (Step 4), ask the user to leave Plan Mode for Accept Edits: "Analysis complete — please switch to Accept Edits mode to write spec.md."
- **Non-interactive session** (headless / `-p`, nobody to press Shift+Tab): state that Plan Mode cannot be confirmed. Run Steps 1–3 read-only and output the analysis and the Step 2.5 questions in the transcript. spec.md is written in an interactive session, or only when the invoking prompt explicitly authorizes writes. In that case, record unanswered BLOCKING questions under Unresolved Questions and unanswered IMPORTANT ones under Assumptions.

### Step 1: Locate Design Document

```
SEARCH, in order:
  1. projects/{project-name}/design.md
  2. projects/{project-name}/design.docx
  3. projects/{project-name}/design.pdf
  4. projects/{project-name}/*.md   (only if there is exactly one non-spec markdown file)
FOUND     → READ it; report "Found design document: {filename} ({word-count} words)"
NOT FOUND → report "No design document found at projects/{project-name}/ (looked for design.md, design.docx,
            design.pdf)" and ASK for one of: a file path ("the design is at {path}"), pasted content, or
            "help me write the design"
```

### Step 2: Extract Core Elements

```
EXTRACT from the design document:
1. PURPOSE              — the problem it solves; the business value; the users/stakeholders
2. SCOPE                — what is IN scope (explicit features/changes); what is OUT (explicit exclusions);
                          system boundaries
3. REQUIREMENTS         — functional (what it must do); non-functional (performance, security, …);
                          technical constraints
4. SUCCESS CRITERIA     — how we know it is done; acceptance criteria; quality gates
5. TECHNICAL APPROACH   — if present: architecture decisions, technology choices, integration points
FLAG every missing element for Step 2.5
```

Report:

```
📋 Extracted from design document:
✅ Found:
   - Purpose: [summary]
   - Scope: {X} features in scope
   - Requirements: {Y} functional, {Z} non-functional
   - Success criteria: {N} criteria identified
⚠️ Gaps identified - proceeding to targeted clarification...
```

**Design too vague** (no concrete requirements, no success criteria, or no scope boundaries): tell the user which of the three are missing and offer: `clarify` (answer questions to fill the gaps, i.e. Step 2.5), `proceed` (generate spec.md with the assumptions noted), or `stop` (exit and improve the design document).

### Step 2.5: Gap-Targeted Clarification Interview

Ask **specific, actionable questions** derived from the gaps found in THIS design. Each question must change an implementation decision. They are not a generic checklist; they probe undefined terms, implicit assumptions, missing behaviour, conflicting or ambiguous requirements, and unclear scope boundaries.

Every question:
- ✅ quotes or references the EXACT text or concept that is unclear
- ✅ explains WHY the answer matters (implementation impact)
- ✅ offers concrete options where applicable
- ✅ can be answered in 1–2 sentences

| Gap type | The gap | Question pattern |
|---|---|---|
| 1. Undefined terms | Design uses "{term}" without defining it | "You mention '{term}' — what's the specific threshold/value? This determines [implementation choice]." |
| 2. Implicit assumptions | Design assumes {behavior} without stating it | "The design seems to assume {assumption}. Is this correct? If not, [alternative approach] would be needed." |
| 3. Unspecified behavior | Design mentions {feature} but not how it works | "When {specific scenario}, what should happen? Options: [A] … or [B] …" |
| 4. Missing error handling | Happy path only for {feature} | "What should happen when {specific failure}? Options: [A] show error, [B] retry, [C] fall back to {alternative}" |
| 5. Conflicting requirements | {Requirement A} conflicts with {Requirement B} | "'{A}' and '{B}' seem to conflict. Which takes priority? This affects [implementation]." |
| 6. Scope boundary unclear | Unclear if {capability} is in or out | "Is {capability} in scope for this release? The design mentions it but doesn't explicitly include it." |
| 7. Missing quantitative requirements | No numbers for {performance/scale} | "What's the expected {metric}? This determines [architectural choice]." |

Group the questions by impact and present each in this format:

```
🔴 BLOCKING (must answer before spec): a wrong assumption here means a wrong implementation
🟡 IMPORTANT (should answer; can proceed with a noted assumption): affects the approach, has a reasonable default

  📍 Context:  "{exact quote or reference from design}"
  ❓ Question: "{specific question}"
  💡 Impact:   "{why this matters for implementation}"
  🔘 Options:  [A] {option} [B] {option}        (if applicable)
  ⚠️ Assuming: {default}                         (IMPORTANT only)

Close with: "Please answer the BLOCKING questions. For IMPORTANT, reply with answers or 'ok' to accept assumptions."
```

Worked examples of each gap type and a full sample question set: `examples/clarification-questions.md`.

**Wait for the user**: answers to the BLOCKING questions are required; answers to IMPORTANT ones are optional.

```
FOR EACH answer:
  → update the extracted requirements with the concrete value
  → note the source: "Per owner clarification: {answer}"
  → remove it from the gaps list; add it to the spec.md Requirements and the Owner Clarifications table
IF the user skips IMPORTANT questions → proceed with the stated assumptions; list each under spec.md "Assumptions"
```

### Step 3: Preliminary Technical Context Discovery

Enrich the spec with **architectural constraints only**, not implementation patterns. Use `adr-aware` to load them.

```
1. MAP resource types in the extracted content to ADR constraints:
   - API endpoints → ADR-001, ADR-008, ADR-010, ADR-019
   - PCF controls → ADR-006, ADR-011, ADR-012, ADR-021
   - Record invariants / create-update paths / any plugin idea → ADR-002 (no plugins; server-side write path WP-1…WP-8)
   - Storage → ADR-005, ADR-007, ADR-009
   - AI features → ADR-013, ADR-014, ADR-015, ADR-016
   - Background / scheduled / event-driven work → ADR-052 (where it runs), ADR-004 (queue), ADR-036 (schedule), ADR-017
   - Anything not listed → find the governing ADRs in .claude/adr/INDEX.md
2. EXTRACT key constraints: read each applicable ADR's MUST / MUST NOT rules; note architectural boundaries and
   technology choices
3. NOTE every design choice that pushes against a MUST / MUST NOT rule → it becomes an ADR Tensions row (Step 4)
OUTPUT: the applicable ADRs and their key constraints, for spec.md
```

**Scope limitation**: this is preliminary discovery for spec enrichment only. Identify which ADRs apply and extract their MUST / MUST NOT rules. Do not search the codebase for implementation patterns, load guides or knowledge docs, or find code examples: comprehensive resource discovery happens in project-pipeline Step 2. One exception: for each new component the design introduces, run a quick Grep/Glob for an existing component it overlaps with, because the §11 table in Step 4 asks for it and root CLAUDE.md §11 forbids "none" without a check.

### Step 4: Generate Structured spec.md

Read `references/spec-template.md` and fill it to create `projects/{project-name}/spec.md`. Keep every heading verbatim; project-pipeline Steps 1, 1.7 and 3 read them by name. The required sections and their rules:

| Section | Rule |
|---|---|
| Executive Summary | 2–3 sentences from PURPOSE |
| Scope — In Scope / Out of Scope / Affected Areas | Out of Scope is explicit; if the design names no exclusions, ask (Step 2.5). Affected Areas lists code areas, with paths where known |
| Requirements — Functional / Non-Functional | `FR-NN` each with acceptance criteria; `NFR-NN` |
| Technical Constraints — Applicable ADRs / MUST Rules (from ADRs) / Existing Patterns to Follow | From Step 3 |
| Placement & New Components (per CLAUDE.md §10 / §11) | **Hot-Path Declaration**: required if the project touches the BFF or SpaarkeAi. Its canonical copy is in design.md (root CLAUDE.md §10; project-pipeline Step 3 checks design.md); copy it here. If BFF = Y, one Placement Justification line per major new component (`.claude/constraints/bff-extensions.md`, ≤ 60 MB publish size); for background work, the ADR-052 host. **New Components (§11 three-question gate)**: one table row per new surface, or "No new components — modify-only." |
| ADR Tensions (per CLAUDE.md §6.5) | **MANDATORY.** One row per tension with Path A / B / C and a concrete rationale, or the explicit "No ADR tensions surfaced at design time." statement (project-pipeline Step 1.7 reads that phrase) |
| Success Criteria | Each with a verification method |
| Dependencies — Prerequisites / External Dependencies | |
| Owner Clarifications | The Step 2.5 answers: topic, question, answer, impact |
| Assumptions | Each unanswered IMPORTANT question, with the component it affects |
| Unresolved Questions | Each with what it blocks |

**Portfolio sync** (after spec.md is written): if `projects/{project-name}/README.md` contains the `> **Portfolio**:` pointer block, invoke `/devops-project-sync`, and if the Project Issue's `Project Status` is not `In Progress`, set it to `In Progress` (a spec exists, so the project is moving). Print one line on success: `✅ Portfolio synced: #N — Status=In Progress`. A failure warns (⚠️) and does not block this skill. No pointer block → the project is not registered; skip silently.

### Step 5: Present for Review

Output the spec.md content, then:

```
✅ spec.md generated:
   - {X} words
   - {Y} functional requirements
   - {Z} ADRs referenced
   - {N} unresolved questions flagged
   - {hot-path line, only if the project touches the BFF/SpaarkeAi and design.md has no <hot-path-declaration>:
      "⚠️ design.md lacks a <hot-path-declaration>; project-pipeline Step 3 will warn until the owner adds it"}

📄 File created: projects/{project-name}/spec.md

📋 Next Steps:
   1. Review spec.md for accuracy
   2. Resolve any flagged questions
   3. Run: /project-pipeline {project-name}

Please review the spec.md. Any changes needed before proceeding to project-pipeline?
[Y to proceed to project-pipeline / edit to make changes / done to finish]
```

**Wait for the user**: `y` (Step 6 handoff) | `edit` (make the changes, then repeat Step 5) | `done` (Step 6 stop).

### Step 6: Handoff to project-pipeline (Optional)

```
IF 'y'    → "Starting project-pipeline with generated spec.md..." and INVOKE project-pipeline projects/{project-name}
IF 'done' → "spec.md ready at projects/{project-name}/spec.md. Run /project-pipeline {project-name} when ready."
```

The next step is always `/project-pipeline`, never `/project-setup` or `/task-create` (Constraints). Why: `references/skill-integration.md`.

## Success Criteria

- [ ] Design document ingested and parsed; core elements extracted (purpose, scope, requirements, criteria) (Steps 1–2)
- [ ] **Gap-targeted interview conducted**: specific questions asked for the gaps found (Step 2.5)
- [ ] **Owner clarifications captured** in spec.md; **assumptions documented** for unanswered questions
- [ ] Applicable ADRs and their constraints identified; tensions declared (Step 3)
- [ ] spec.md generated with every required section, headings verbatim (Step 4)
- [ ] Unresolved questions flagged with what each blocks
- [ ] Portfolio synced, if the project is registered (Step 4)
- [ ] User reviewed and approved spec.md (Step 5)
- [ ] Original design document preserved, unchanged, beside spec.md

## Gotchas

- **Vague spec → flailing tasks.** A design's ambiguities that are not surfaced as Unresolved Questions become a spec the pipeline cannot decompose. Always include the Unresolved Questions section, and have the owner answer it BEFORE `/project-pipeline`.
- **Design implementation detail copied as requirements.** When the design's author anticipated the implementation (class names, method shapes, step-by-step code), copying it into Requirements turns a guess into a binding requirement. Requirements state behaviour; Affected Areas and Existing Patterns name code areas and the canonical examples to follow; class-level design is left to plan.md, which project-pipeline generates.
- **Lost design document.** Opening design.md to "tidy it" while writing the spec loses the source. Preserve it verbatim; if it is lost, recover it from git history.
- **A requirement contradicts an ADR.** The design's author may not know the ADR. Do not proceed silently: record it in ADR Tensions with a path (A exception / B amendment / C comply, root CLAUDE.md §6.5) and let the owner choose. A Path B amendment merges before or with the dependent code.
- **Hot-path declaration only in spec.md.** project-pipeline Step 3 and root CLAUDE.md §10 look for it in design.md. A copy in spec.md does not stop the warning; report the gap (Step 5) rather than editing the owner's design.
- **"none found" without a search.** The §11 "Existing overlap" column needs a Grep/Glob behind it (Step 3 exception). An unchecked "none" is exactly what task-create Step 3.5.6 and code-review Step 6.6 reject later.

## References

- spec.md template (read and filled in Step 4): `references/spec-template.md`
- Lifecycle position, related skills, why not to call component skills directly, portfolio-hook history: `references/skill-integration.md`
- Worked clarification questions: `examples/clarification-questions.md`
- Related skills: `project-pipeline` (next), `use-case-to-design` (upstream), `adr-aware`, `devops-project-sync`
- Governance: root CLAUDE.md §6.5 (ADR tensions), §10 (BFF placement, hot-path), §11 (component justification); `.claude/constraints/bff-extensions.md`
