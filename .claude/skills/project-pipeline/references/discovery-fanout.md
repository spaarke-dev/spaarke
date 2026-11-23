# project-pipeline — fanning Step 2 discovery out to sub-agents

Read this before launching sub-agents (or workflow agents) for Step 2 Part 1 resource discovery. Discovery done in the main session needs none of it.

## Model and effort

- Every launch names a model; a `PreToolUse` hook (`scripts/quality/require-agent-model.py`) refuses one that does not.
- Search and inventory (where is X, which files implement Y): `code-mapper`, sonnet, effort `low` (`.claude/constraints/agent-cost.md`).
- Microsoft or AI platform questions the spec raises: `researcher` (its definition sets opus / high).
- Judging what the findings mean (which ADRs apply, which implementation is canonical) stays with the orchestrator at the planning tier.
- One scoped deliverable per agent. The concurrency caps in `.claude/settings.json` apply.

## Return schema

Have each discovery agent return its findings in this shape. The orchestrator can then merge them without parsing prose, and a missing field triggers a re-prompt instead of silently dropping a resource:

```json
{ "adrs":       [ { "id": "ADR-0NN", "path": ".claude/adr/…", "why": "…" } ],
  "skills":     [ { "name": "…", "path": ".claude/skills/…/SKILL.md" } ],
  "knowledge":  [ { "path": "docs/…", "section": "…" } ],
  "canonical":  [ { "path": "src/…", "reuse-as": "reference impl for X" } ],
  "scripts":    [ { "path": "scripts/…", "use": "…" } ],
  "schemaGaps": [ { "entity": "sprk_…", "gap": "missing field/entity → creation task needed" } ] }
```

In a Workflow script, pass this shape as the `schema` option on the discovery `agent()` calls. Validation then happens at the tool-call layer, and the agent retries on a mismatch. Background: `.claude/AUDIT-FINDINGS-PIPELINE-MODERNIZATION-2026-07-16.md` ("Dynamic Workflows").
