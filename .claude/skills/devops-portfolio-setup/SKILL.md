---
description: One-shot idempotent bootstrap of GitHub Project #2 portfolio schema (Type=Project option, 6 custom fields, 7 labels, 3 issue templates). Codifies Phase 1 of spaarke-devops-project-tracking-r1.
tags: [devops, github-project, portfolio, bootstrap, idempotent, foundation]
techStack: [gh-cli, graphql, yaml]
appliesTo: ["/devops-portfolio-setup", "bootstrap portfolio", "setup portfolio schema"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-portfolio-setup

> **Category**: DevOps / Portfolio
> **Tier**: Component (called by setup workflows; also user-invocable via `/devops-portfolio-setup`)
> **Last Reviewed**: 2026-06-23
> **Reviewed By**: Phase 2 task 010 of `spaarke-devops-project-tracking-r1`

## Prerequisites

- `gh` CLI v2.40+ authenticated as the GitHub Project owner (`gh auth status`)
- Token scopes: `repo`, `project`, `read:org` (verify with `gh auth status`)
- `python3` available (for snapshot parsing + reconciliation logic)
- GitHub Project #2 ("Spaarke Core") reachable at https://github.com/users/spaarke-dev/projects/2
- This skill modifies `.github/ISSUE_TEMPLATE/*.yml` — operator should review the diff before committing

## Purpose

One-shot, idempotent bootstrap of the GitHub Project #2 portfolio schema so any clean repo + Project can be brought to a known-good state in seconds. Encodes everything Phase 1 (tasks 001–005) did manually:

1. Extend `Type` field with `Project` option (preserve existing 6)
2. Add 6 custom fields (`Project Type`, `Worktree Path`, `Project Folder`, `Task Count`, `Tasks Completed`, `Project Status`) with exact FR-02 schemas
3. Create 7 repository labels (`epic`, `project`, `backlog`, `worktree:active`, `worktree:archived`, `on-hold`, `cancelled`)
4. Land 3 issue templates (`epic.yml`, `project.yml`, `idea.yml`) at `.github/ISSUE_TEMPLATE/`

Re-running on an already-bootstrapped project is a no-op (zero API mutations, zero file changes).

## 🚨 CRITICAL: Snapshot → Mutate → Reconcile pattern

**Empirically verified during Phase 1 task 001 (2026-06-23):** the GitHub Projects v2 `updateProjectV2Field` mutation REPLACES the entire option list AND generates new internal option IDs for every option — even unchanged ones. Items currently bound to old option IDs lose their references.

This skill MUST therefore implement a 3-step pattern for ANY mutation against a single-select field that has existing items referencing it:

1. **Snapshot**: query all items and capture their current `Type` / `Status` field values (item ID → option name).
2. **Mutate**: run `updateProjectV2Field` to add the new option (or change the option list).
3. **Reconcile**: for each captured item, re-bind to the NEW option ID matching the original option name via `updateProjectV2ItemFieldValue`.

For initial bootstrap on a project with zero classified items (as was the case 2026-06-23), the reconcile step is a no-op. For re-runs or production projects with classified items, the reconcile step is load-bearing.

## Workflow

### Step 0: Pre-flight checks

```bash
gh auth status                                # Verify auth
gh project view 2 --owner spaarke-dev         # Verify reachability
mkdir -p .github/ISSUE_TEMPLATE                # Ensure dir exists
```

If any check fails, STOP and report.

### Step 1: Capture full Project #2 baseline (SNAPSHOT)

Save to `notes/spikes/portfolio-setup-baseline-{YYYY-MM-DD}.json`:

```bash
# Field schema
gh project field-list 2 --owner spaarke-dev --format json \
  > notes/spikes/portfolio-setup-baseline-fields-{date}.json

# Item field values (CRITICAL for reconciliation)
gh api graphql -f query='
query {
  node(id: "PVT_kwHODW0Pv84BEgWu") {
    ... on ProjectV2 {
      items(first: 100) {
        nodes {
          id
          fieldValues(first: 30) {
            nodes {
              ... on ProjectV2ItemFieldSingleSelectValue {
                field { ... on ProjectV2SingleSelectField { name } }
                optionId
                name
              }
            }
          }
        }
      }
    }
  }
}' > notes/spikes/portfolio-setup-baseline-items-{date}.json
```

### Step 2: Idempotency pre-check for each mutation

For each target mutation, query current state and skip if already in target state:

- **Type=Project option**: query Type field options; skip mutation if `Project` already present
- **6 custom fields**: query field-list; create only missing fields
- **7 labels**: `gh label list --json name`; create only missing labels
- **3 issue templates**: check file existence; write only missing files

Idempotency check is the difference between a destructive re-run and a true no-op.

### Step 3: Type field extension (with reconciliation)

If `Project` option not present:

```python
# Compose mutation preserving ALL existing option names
options_to_send = existing_type_options + [
    {"name": "Project", "color": "GRAY", "description": "Spaarke project (worktree-backed initiative)"}
]
# Execute updateProjectV2Field with full options list
# Capture NEW option IDs from response
```

Then RECONCILE per the snapshot:
```python
for item_id, original_type_name in snapshot["type_assignments"].items():
    new_option_id = new_option_name_to_id_map[original_type_name]
    if new_option_id != snapshot["type_assignments_old_ids"][item_id]:
        # Re-bind
        execute updateProjectV2ItemFieldValue(item_id, type_field_id, new_option_id)
```

### Step 4: Create 6 custom fields (additive only)

For each missing field, execute `createProjectV2Field` mutation per the schemas in `notes/phase1-field-ids.md`:

- `Project Type` — SINGLE_SELECT with 8 options (`Module, UI, Infrastructure, Cleanup, Data, Process, AI, Mixed`)
- `Worktree Path` — TEXT
- `Project Folder` — TEXT
- `Task Count` — NUMBER
- `Tasks Completed` — NUMBER
- `Project Status` — SINGLE_SELECT with 5 options (`Planned, In Progress, On Hold, Completed, Cancelled`)

Field creation is purely additive — no reconciliation needed because new fields have no existing item references.

### Step 5: Create 7 labels

For each missing label, run `gh label create <name> --description "<desc>" --color <hex>`. Colors per the project's color convention. Existing labels skip silently.

### Step 6: Land 3 issue templates

For each missing template file, write to `.github/ISSUE_TEMPLATE/{epic,project,idea}.yml`. Templates land on the current branch — they only appear in the GitHub "New Issue" picker once merged to the default branch. Document this caveat in the skill's user output.

### Step 7: Save final state snapshot + report

Save `notes/spikes/portfolio-setup-final-{date}.json` (post-mutation field-list + item field-values).

Print summary:
```
Portfolio setup complete:
  - Type field options: 7 (added: Project)
  - Custom fields: 6 added (0 already present)
  - Labels: 7 added (0 already present)
  - Issue templates: 3 written (0 already present)
  - Items reconciled: 0 (no pre-existing Type assignments)
  - Snapshots saved: notes/spikes/portfolio-setup-{baseline,final}-{date}.json
```

If any sub-step was a no-op, the output should explicitly say so.

## Outputs

- GitHub Project #2 schema: 26+ fields, Type field with 7+ options
- Repository: 7 portfolio labels
- `.github/ISSUE_TEMPLATE/`: 3 YAML form templates
- `notes/spikes/portfolio-setup-baseline-{date}.json` (pre-mutation)
- `notes/spikes/portfolio-setup-final-{date}.json` (post-mutation)
- Single-line confirmation log (per NFR-03 when called by hooks; verbose default when user-invoked)

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| `gh auth status` fails | Token expired or missing scopes | Re-authenticate; verify `repo`, `project`, `read:org` scopes |
| `updateProjectV2Field` succeeds but reconciliation fails mid-loop | Rate limit hit during reconcile loop | Re-run the skill — idempotency check on Step 3 detects already-extended Type field; reconcile resumes from current state vs. baseline snapshot |
| Issue template YAML parses on YAML lint but fails on GitHub-side schema | YAML form schema drift | Test by submitting one test Issue from each template (smoke test); fix YAML; re-run skill |
| Skill re-run after manual UI changes that diverge from spec | Operator made out-of-band changes | Re-running enforces the spec schema. Manual changes lost if they violated the spec. |
| `notes/spikes/` directory doesn't exist | Project scaffolding incomplete | Run from a properly-scaffolded project folder (see `project-pipeline` skill) |
| Snapshot capture incomplete (items page > 100) | Project has > 100 items | Implement pagination via `endCursor` in the GraphQL query. Phase 1 launched with 23 items; pagination becomes load-bearing at 100+ |
| Reconciliation re-binds items to wrong option | Snapshot-to-new-option name match failed | Verify the snapshot's `name` field matches the new option set EXACTLY (case-sensitive). Investigate baseline JSON before mutation. |

## Related Skills

- `/devops-epic-create` — uses the Type=Epic option this skill ensures
- `/devops-idea-create` — uses the label `backlog` this skill creates
- `/devops-project-start` — depends on field IDs this skill establishes
- `/devops-project-sync` — uses the 6 fields this skill creates
- `project-pipeline` — invokes this skill at first-run if the project hasn't been bootstrapped

## Reference

- Critical lesson: `projects/spaarke-devops-project-tracking-r1/notes/spikes/phase1-task001-execution-log-2026-06-23.md`
- Field IDs: `projects/spaarke-devops-project-tracking-r1/notes/phase1-field-ids.md`
- Spec: `projects/spaarke-devops-project-tracking-r1/spec.md` FR-01..FR-06
