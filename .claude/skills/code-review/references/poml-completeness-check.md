# Step 6.7 — Task POML Completeness Check

Read by `code-review` Step 6.7 when the diff adds or modifies `projects/*/tasks/*.poml` files.

This is the PR-time half of the `task-create` Completeness Lint. It catches a hand-edited or hand-authored task POML that skipped the canonical metadata, which otherwise fails silently: a POML missing `<model-tier>` just falls back to a default at dispatch, and `project-pipeline` Step 5 and `/goal` then under-specify the wave. It closes the drift class in the 2026-07-16 template-drift finding (`projects/spaarkeai-compose-r3/notes/FINDING-poml-template-drift.md`).

```
FOR each added/modified projects/*/tasks/*.poml in the diff:
  REQUIRE present + non-empty: <model-tier>, <effort>, <rigor>, <parallel-group>, <parallel-safe>
  REQUIRE <steps> carries mode="directional|prescriptive"
  IF the task adds NEW surface (new .cs/.ts/.tsx, endpoint, DI registration, package, Dataverse column,
     or a <relevant-files> role="new"): REQUIRE a non-hollow <justification> (already checked in Step 6.6)
  IF tags include pcf|frontend|fluent-ui|e2e-test: REQUIRE <ui-tests>
  FLAG the deprecated field names <rigor-hint> / metadata-sibling <dependencies> (canonical: <rigor> / <deps>)

  Fastest path: run `pwsh scripts/Validate-TaskPoml.ps1 <tasks-dir>` and fold its findings in.
```

**Anti-patterns to flag**:

| Pattern | Severity |
|---|---|
| POML missing `<model-tier>` / `<effort>` / `<parallel-safe>` | WARNING (Critical if it's a BFF/auth/deploy task — dispatch tier matters) |
| `<steps>` with no `mode` attribute | WARNING |
| Deprecated `<rigor-hint>` or metadata-sibling `<dependencies>` used | WARNING (rename to `<rigor>` / `<deps>`) |
| PCF/frontend task with no `<ui-tests>` | WARNING |

See: `.claude/skills/task-create/SKILL.md` "Completeness Lint"; `.claude/templates/task-execution.template.md` (canonical skeleton).
