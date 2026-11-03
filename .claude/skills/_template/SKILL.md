<!--
TEMPLATE — How to use this:
  1. Copy this entire `_template/` folder to `.claude/skills/<your-skill-name>/`
  2. Rename `<your-skill-name>` to kebab-case (e.g., `pcf-deploy`, `ribbon-edit`)
  3. Fill in every PLACEHOLDER below
  4. Delete this HTML comment block
  5. Add an entry to `.claude/skills/INDEX.md`
  6. Add an entry to `.claude/CHANGELOG.md` under [Unreleased] → Added
  7. LENGTH — aim for under 200 lines; the cap is 500. The whole SKILL.md loads every time the skill runs.
     Over 500 only for a real reason, stated three ways: a `length-justification: <one sentence>` frontmatter key;
     directly under the title, "> **Read this file in full.** It is long on purpose: <reason>."; then a
     `## Contents` map (section → what it covers → when it applies). Enforced by: scripts/quality/check-skill-length.py
     (CI Tier 2, ratcheted by scripts/quality/skill-length.baseline.txt).
  8. WHAT STAYS INLINE — steps that run every time, MUST / MUST NOT rules, acceptance criteria, gotchas, and every
     step number another file cites (POMLs cite "task-create Step 3.5.5"; renumbering breaks them).
     WHAT MOVES OUT — a branch that applies only sometimes goes to `references/<name>.md`, with an explicit
     directive at the branch point: "If <condition>, read `references/<name>.md` before continuing." (a directive,
     not a "see also" — an agent skips a "see also"). Long examples go to `examples/`. Runnable code goes to a
     script the skill RUNS (`scripts/`), not a code block the agent re-types.
  9. A `references/` or `examples/` file over 100 lines opens with a `## Contents` list: Claude may preview only the
     top of a referenced file. Exempt: `*-template.md` files copied verbatim into outputs. Same guard as item 7.
  10. FREEDOM — state the goal and the constraints; script exact steps only where order or exactness matters
     (deploys, data writes, irreversible actions). Give a multi-step procedure a `- [ ]` checklist and a
     validate → fix → re-validate loop. Name the model only where it matters; model-specific settings go stale.
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

*To split this skill: keep trigger conditions, every-time steps, constraints, acceptance criteria and gotchas in this `SKILL.md`. Move conditional branches to `references/<name>.md` behind an explicit "If <condition>, read … before continuing" line, long examples to `examples/<name>.md`, and runnable code to a script the skill runs. The agent loads `SKILL.md` in full and opens supporting files only when the directive tells it to.*
