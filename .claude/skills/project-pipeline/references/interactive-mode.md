# project-pipeline — interactive mode

Read this when the user asks for an interactive run ("run pipeline interactively", "with approvals", "start project interactively"). Interactive mode adds confirmation gates. It removes nothing: every step in `SKILL.md` still runs, and the safety gates (0.3, 1, 3 Completeness Lint, 4.2, 5 build check) still stop the pipeline in either mode.

## Contents

- Gates by step: what to ask after each step
- Step 4.5: the Target Date prompt
- Step 5: one task at a time
- When the user says "stop"

## Gates by step

After each step, report as in autonomous mode, then wait for the user's answer before the next step.

| Step | Interactive behaviour | Prompt |
|---|---|---|
| 0 | Same as autonomous: confirm Plan Mode | — |
| 0.3 | Same as autonomous (a safety gate, not a confirmation gate) | — |
| 0.5 | If master is stale, wait for the user's choice | `Run /merge-to-master first, or continue? [merge / continue]` |
| 1 | Wait after validation | `Next: generate the project artifacts. [Y to proceed / refine to change spec.md / stop to exit]` |
| 1.5 | If overlaps were found, wait | `[Y to proceed with awareness / stop to wait]` |
| 1.7 | No gate: accepted tension declarations are authoritative | — |
| 2 | If the reuse scan found 2+ candidates for a capability, ask which to extend (autonomous mode stops instead). Wait after artifacts are generated | `{capability}: extend {A} or {B}, or neither (why)?` then `[Y to proceed / review to view artifacts / refine {file} to edit / stop to exit]` |
| 3 | Wait after tasks are generated | `[Y to continue to branch creation / review {task-number} to view a task / stop to exit]` |
| 4 | Wait after the branch is pushed | `[Y to continue / stop to exit]` |
| 4.2 | No gate: registration is a MUST and runs without a prompt | — |
| 4.5 | Run the Target Date prompt below | see below |
| 5 | One task at a time | see below |

On "review", open the named artifact and ask again. On "refine {target}", make the requested change, report it, and ask again.

## Step 4.5: Target Date prompt

```
IF projects/{name}/README.md has the portfolio pointer block (the Project Issue exists):
  COMPUTE for the operator:
    - task count from tasks/TASK-INDEX.md
    - phase count
    - estimated effort range (plan.md "Timeline" / "Estimated Effort")
    - critical path length, if TASK-INDEX.md shows it
  PROMPT:
    Plan summary:
      - {N} tasks across {M} phases
      - Estimated effort: {range from plan.md}
      - Critical path: {summary if available}
    Set a projected Target Date for this project?
    Format: YYYY-MM-DD (e.g., 2026-08-15), or 'skip' to leave blank
  valid ISO date → set the `Target Date` field on the Project Issue via
    updateProjectV2ItemFieldValue with value { date: "YYYY-MM-DD" }
    REPORT "✅ Target Date set to {date}. Drift will be tracked at archive."
  'skip' or blank → REPORT "Target Date left blank. Set it later in GitHub or re-run /devops-project-register."
  unparseable → re-prompt ONCE; a second invalid answer counts as 'skip'
ELSE (no pointer block):
  cannot happen after Step 4.2 (a MUST); return to Step 4.2
```

Why the prompt sits here and not earlier:

| Lifecycle point | What is known | Projection quality |
|---|---|---|
| `/devops-idea-create` | A one-liner | A guess |
| `/devops-project-start` | Folder and design.md skeleton, no plan | A bad guess |
| **End of `/project-pipeline`** | **Plan with WBS, task count, effort range, critical path** | **An informed projection** |

A date set with this context is what makes drift (Closed Date − Target Date) a meaningful metric later.

## Step 5: one task at a time

- Execute one task at a time through task-execute (no parallel waves unless the user asks for them).
- Ask before starting the next task. The user can review the results between tasks.
- The build check, failure-isolation and retry rules in `SKILL.md` Step 5 still apply.

## When the user says "stop"

Stop after the current step. Report what was created and how to continue, for example:

```
✅ Project initialized up to plan.md
   Files created:
   - projects/{name}/plan.md
   - projects/{name}/README.md
   When ready to continue:
   - /task-create {name}            (continue from task generation)
   - /project-pipeline {name}       (restart the full pipeline)
```

A run stopped before Step 4.2 leaves the project unregistered. task-execute's registration guard will require Step 4.2 before any task runs.
