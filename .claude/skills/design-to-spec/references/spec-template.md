<!-- spec.md template for design-to-spec Step 4. Copy everything below this comment into
projects/{project-name}/spec.md. Replace every {placeholder}; delete the {guidance} lines once the section is
filled. Keep the section headings verbatim: project-pipeline Steps 1, 1.7 and 3, task-create and code-review
read them by name. -->

# {Project Name} - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: {YYYY-MM-DD}
> **Source**: {design-document-filename}

## Executive Summary

{2-3 sentences from the extracted PURPOSE: what this project delivers and why.}

## Scope

### In Scope
{Bulleted list of features/changes}
- {Feature/change 1}

### Out of Scope
{Explicit exclusions. If the design names none, ask the owner (Step 2.5) rather than leaving this empty.}
- {Explicit exclusion 1}

### Affected Areas
{Code areas, with file paths where known}
- `src/server/api/...` - {description}
- `src/client/pcf/...` - {description}

## Requirements

### Functional Requirements
{Numbered list, each with clear acceptance criteria}
1. **FR-01**: {requirement} - Acceptance: {criteria}
2. **FR-02**: {requirement} - Acceptance: {criteria}

### Non-Functional Requirements
{Performance, security, accessibility, etc.}
- **NFR-01**: {requirement}

## Technical Constraints

### Applicable ADRs
{ADRs that MUST be followed, from Step 3}
- **ADR-001**: Minimal API pattern required
- **ADR-008**: Use endpoint filters for authorization

### MUST Rules (from ADRs)
{Extracted key constraints}
- ✅ MUST {constraint}
- ❌ MUST NOT {constraint}

### Existing Patterns to Follow
{Reference to canonical implementations}
- See `src/server/api/.../ExampleEndpoints.cs` for endpoint pattern
- See `.claude/patterns/api/` for detailed patterns

## Placement & New Components (per CLAUDE.md §10 / §11)

{Seed the governance the downstream pipeline (project-pipeline, task-create) will enforce, so it is not
back-filled later. Two parts:}

### Hot-Path Declaration
{REQUIRED if this project touches the BFF (`src/server/api/Sprk.Bff.Api/**`, or `Spaarke.Core` / `Spaarke.Dataverse`
per `.claude/constraints/bff-extensions.md` §H) or SpaarkeAi (`src/solutions/SpaarkeAi/**`). The canonical copy
belongs in design.md (root CLAUDE.md §10); project-pipeline Step 3 emits a HARD WARNING if design.md lacks it.
Copy it here; if design.md has none, say so in the Step 5 report.}

```xml
<hot-path-declaration>
  <bff>Y|N</bff>
  <spaarkeai>Y|N</spaarkeai>
  <ci-workflows>Y|N</ci-workflows>
  <skill-directives>Y|N</skill-directives>
  <root-claude-md>Y|N</root-claude-md>
</hot-path-declaration>
```

{If BFF=Y: add a one-line Placement Justification per major new component citing
`.claude/constraints/bff-extensions.md`, and note the ≤60 MB publish-size ceiling applies per task. For
background, scheduled or event-driven work, name the host chosen under ADR-052 and the signals behind it.}

### New Components (§11 three-question gate)
{For each NEW service/abstraction/endpoint/DI-registration/package/Dataverse-column this design introduces,
answer in one sentence each — task-create Step 3.5.6 and code-review Step 6.6 will re-check these.
The overlap cell comes from the Step 3 reuse scan; its searches and candidates are in `notes/reuse-scan.md`:}

| New component | Existing overlap (`notes/reuse-scan.md`) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| {name} | {file:line, or "none found" + the searches run} | {Yes→extend / No + why} | {behavior/contract that fails — NOT "flexibility"} |

{If this project adds NO new surface (modify-only), state: "No new components — modify-only."}

## ADR Tensions (per CLAUDE.md §6.5)

{This section is MANDATORY. Surface anticipated conflicts between this project's design and existing ADR rules,
so they are resolved as A (exception) / B (amendment) / C (comply) rather than discovered as silent violations at
code review. The bar is "a MUST/MUST NOT rule the design seems to push against" — not minor stylistic deviations.}

{For each tension, declare a resolution path:}

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-XXX | "MUST/MUST NOT ..." | {why strict compliance is sub-optimal here} | A / B / C | {why this path} |

{If no tensions are anticipated, state explicitly:}
> No ADR tensions surfaced at design time. All listed ADRs (above) apply
> without exception. This section may be updated if tensions emerge during
> implementation.

{See CLAUDE.md §6.5 for the three resolution paths (A exception / B amendment / C comply).}

## Success Criteria

{Numbered list with verification method}
1. [ ] {criterion} - Verify by: {method}
2. [ ] {criterion} - Verify by: {method}

## Dependencies

### Prerequisites
{What must exist/be done first}
- {Prerequisite}

### External Dependencies
{APIs, services, approvals needed}
- {External dependency}

## Owner Clarifications

{Answers captured from the Step 2.5 interview - CRITICAL for implementation. Step 5 reuse questions (a capability
with 2+ existing candidates in `notes/reuse-scan.md`) go here too, topic `Reuse: {capability}`.}

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| {topic} | {question asked} | {owner's response} | {implementation decision made} |

## Assumptions

{Items where the owner did not specify - proceeding with stated assumptions}

- **{topic}**: Assuming {value/behavior} - affects {component/decision}

## Unresolved Questions

{Still blocking, or need answers during implementation}
- [ ] {question} - Blocks: {what this blocks}

---

*AI-optimized specification. Original design: {filename}*
