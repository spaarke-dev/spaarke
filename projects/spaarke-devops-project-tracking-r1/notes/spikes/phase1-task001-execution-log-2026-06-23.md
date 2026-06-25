# Task 001 Execution Log — 2026-06-23

## Outcome: ✅ SUCCESS

Added `Project` option to GitHub Project #2 Type field. Preserved all 6 existing options (Idea, Epic, Story, Task, Bug, Spike). Zero items affected.

## Pre-mutation state

- **Project #2 ID**: `PVT_kwHODW0Pv84BEgWu`
- **Type field ID**: `PVTSSF_lAHODW0Pv84BEgWuzg2HOQw`
- **Total fields**: 20
- **Total items**: 23

### Type field options (BEFORE)
| Old ID | Name |
|---|---|
| 5fd91262 | Idea |
| d83bde47 | Epic |
| 447f1695 | Story |
| fea02371 | Task |
| 0cab10bb | Bug |
| b4129a4b | Spike |

## Mutation

```graphql
mutation {
  updateProjectV2Field(input: {
    fieldId: "PVTSSF_lAHODW0Pv84BEgWuzg2HOQw"
    singleSelectOptions: [
      { name: "Idea",    color: GRAY, description: "" },
      { name: "Epic",    color: GRAY, description: "" },
      { name: "Story",   color: GRAY, description: "" },
      { name: "Task",    color: GRAY, description: "" },
      { name: "Bug",     color: GRAY, description: "" },
      { name: "Spike",   color: GRAY, description: "" },
      { name: "Project", color: GRAY, description: "Spaarke project (worktree-backed initiative)" }
    ]
  }) { ... }
}
```

## Post-mutation state

### Type field options (AFTER)
| New ID | Name | Description |
|---|---|---|
| 2a98fa3c | Idea | "" |
| 47842682 | Epic | "" |
| d0ee9af3 | Story | "" |
| 82398e01 | Task | "" |
| ca2544b4 | Bug | "" |
| eea0ded6 | Spike | "" |
| **2708f496** | **Project** | **"Spaarke project (worktree-backed initiative)"** |

## 🚨 CRITICAL LESSON LEARNED — Update task 010 (`/devops-portfolio-setup`) design

### What surfaced

The GitHub Projects v2 `updateProjectV2Field` mutation for `SINGLE_SELECT` fields:
- **Replaces the full option list** (does not patch additively)
- **Generates new internal option IDs** even for options with unchanged names
- The `ProjectV2SingleSelectFieldOptionInput` schema does NOT accept `id` (only `name`, `color`, `description`)

### What this means

- If items currently reference an option by ID, those references become orphaned after the mutation
- Name-match preservation is NOT a documented guarantee — empirical testing showed IDs always change

### How task 001 escaped harm

- 23 existing items: 0 had a `Type` value set (only 4 had `Area` field values; none had Type)
- So orphaning Type option IDs caused zero data loss
- This was luck, not skill — a project with items already classified by Type would have lost those classifications

### Required changes to task 010 + skill design

Add to `/devops-portfolio-setup` skill (task 010) AND propagate to any future single-select mutation skill:

1. **PRE-MUTATION**: Capture full item-level Type-value snapshot via GraphQL:
   ```graphql
   query {
     node(id: "{projectId}") {
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
   }
   ```
   Save to `notes/spikes/pre-mutation-item-snapshot-{date}.json`.

2. **MUTATION**: Execute as task 001 did.

3. **POST-MUTATION RECONCILIATION**:
   - Query the NEW option IDs (option ID → name map)
   - For each item in the pre-mutation snapshot that had Type=X, re-bind via `updateProjectV2ItemFieldValue` with the NEW option ID matching name "X".

This 3-step pattern (snapshot → mutate → reconcile) MUST be the skill's contract for any field-with-options modification.

### Project-level corollary

- Task 010 acceptance criteria need to include: "zero item-level field value loss across mutation"
- Task 008 (Phase 1 verify gate) should add: "all pre-mutation item Type values preserved"

## Acceptance criteria evaluation (task 001)

| Criterion | Status | Evidence |
|---|---|---|
| `Project` in Type options | ✅ PASS | option ID `2708f496`, name `Project` present |
| 6 existing options still present | ✅ PASS | Idea, Epic, Story, Task, Bug, Spike all present (new internal IDs) |
| No existing item's Type value changed | ✅ PASS | 0 items had Type before; 0 items have Type after; net zero change |
| Re-running is no-op | ⚠️ DEFERRED | Idempotency contract enforced by `/devops-portfolio-setup` skill (task 010) which will be the canonical idempotent re-runner. Manual re-run of this hand-driven task would re-execute the mutation (not currently idempotent without skill wrapper). |

## Next action

Proceed to task 002 (add 6 custom fields). Apply the snapshot → mutate → reconcile pattern as a precaution, even though those fields don't replace existing options.
