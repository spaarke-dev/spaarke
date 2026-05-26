<!--
TEMPLATE — How to use this:
  1. Copy this entire `_template/` folder to `.claude/skills/<your-skill-name>/`
  2. Rename `<your-skill-name>` to kebab-case (e.g., `pcf-deploy`, `ribbon-edit`)
  3. Fill in every PLACEHOLDER below
  4. Delete this HTML comment block
  5. Add an entry to `.claude/skills/INDEX.md`
  6. Add an entry to `.claude/CHANGELOG.md` under [Unreleased] → Added
  7. Body must stay UNDER 200 lines (ideal target). Cap is 400 lines with justification stamped in the Gotchas section.
  8. Reference subdirectories (`references/`, `examples/`, `scripts/`) for detail — keep SKILL.md tight.
-->
---
name: <your-skill-name>
description: <Trigger-leading. Lead with the phrase a developer would actually say or do, then the action verb. Example — "When working on Dataverse table schemas, plugins, or Web API queries: guides schema design, migration patterns, and MCP-tool usage." NOT — "Helps with database schema work."> 
tags: [<comma-separated topic tags — e.g., dataverse, schema, plugins>]
techStack: [<e.g., dataverse, dotnet, pcf, react>]
appliesTo: ["<glob patterns where this skill is relevant — e.g., src/server/api/**/*.cs, src/solutions/**/CustomControls/*>"]
alwaysApply: false
exemplar: <one of: a real path like ".claude/skills/pcf-deploy/examples/canonical-deploy.md" OR the literal string "none-too-volatile" — see below>
last-reviewed: YYYY-MM-DD
---

# <Your Skill Title>

> **Category**: <Operations | Code-quality | Discovery | Infrastructure | Project-orchestration>
> **Last Reviewed**: YYYY-MM-DD

## When to Use

<2-4 sentences. Lead with the trigger conditions. What is the agent doing or what is the user asking when this skill should activate? Be specific. Avoid "helps with X" framings; instead say "When the user is doing X, or the task touches Y."

If there are auto-detection rules (file types touched, commands run, project state observed) — list them as a short bullet list.>

## What to Achieve

<Goal-oriented. What outcome should the agent reach by following this skill? Trust the agent's judgment within the constraints below — do NOT script step-by-step instructions unless a step is non-obvious.>

- <Achievement 1>
- <Achievement 2>
- <Achievement 3>

## Constraints (MUST / MUST NOT)

<Hard rules. These are non-negotiable. If a constraint must be relaxed for a specific case, the skill body should say so.>

- **MUST**: <e.g., "Run `dotnet format` before claiming the task is complete.">
- **MUST**: <e.g., "Use the shared `@spaarke/auth` library for any new auth integration.">
- **MUST NOT**: <e.g., "NEVER commit `appsettings.local.json`.">
- **MUST NOT**: <e.g., "NEVER use mocked databases in integration tests — see FAILURE-MODES.md#G-X for the reason.">

<If a "NEVER" rule is included, it MUST cite evidence — link to a FAILURE-MODES entry, an ADR, or a commit SHA. Absolute claims without evidence age badly (see FAILURE-MODES.md#AP-1).>

## Acceptance Criteria

<How does the agent know it has succeeded? A short checklist the agent can mentally verify.>

- [ ] <Criterion 1 — verifiable>
- [ ] <Criterion 2 — verifiable>
- [ ] <Criterion 3 — verifiable>

## Reference Exemplar

<Per project decision: exemplars are opt-in. Choose ONE:>

**Option A** — A real, currently-canonical example:
- See `<path>` — <one sentence on what makes this the exemplar>

**Option B** — Opt out with rationale:
- `exemplar: none-too-volatile` — <one sentence explaining why an exemplar would impose unacceptable maintenance cost (e.g., "the canonical implementation changes monthly as platform APIs shift")>

<Phase 2a audit accepts BOTH options. Opting out is fine; pretending an exemplar exists when it's stale is worse than no exemplar.>

## Gotchas

<Highest-signal section. Failure modes the agent should know about. If you don't have any yet, mark a stub: "No gotchas surfaced yet — append when issues are found." Don't leave the section absent.>

- **<Gotcha title>**: <one-paragraph description of what trips people up + how to avoid>. Evidence: <commit SHA or FAILURE-MODES anchor>.
- **<Anti-pattern title>**: <one-paragraph description of what LOOKS RIGHT but isn't>. Evidence: <commit SHA or FAILURE-MODES anchor>.

<Cross-reference relevant entries in `.claude/FAILURE-MODES.md` if this skill is implicated by any cross-cutting failure pattern.>

## References

<Pointers to deeper context. Keep this section short — link, don't inline.>

- **Code entry points**: `<path/to/file.cs:lineno>` — <what this code shows about the skill's domain>
- **ADRs**: [ADR-XXX](../../../docs/adr/ADR-XXX-<slug>.md) — <relevance>
- **Patterns**: `.claude/patterns/<topic>/<name>.md` — <what pattern applies>
- **Examples** (in this skill's subdirectory): `examples/<name>.md`
- **Detailed references** (in this skill's subdirectory): `references/<name>.md`

---

*To split this skill: keep trigger conditions + constraints + acceptance criteria + gotchas in this `SKILL.md`. Move long examples to `examples/<name>.md`, long reference content to `references/<name>.md`, and any sidecar tooling to `scripts/<name>.ps1`. The agent loads `SKILL.md` first and dereferences supporting files only when needed.*
