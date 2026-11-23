# project-pipeline — error recovery

Read the section that matches the failure. `SKILL.md` names the section at the step where the failure occurs. Fix the named cause before retrying; do not loop on an unexplained failure.

## Contents

- spec.md missing (Step 1)
- Artifact generation failed (Step 2 Part 2)
- Task decomposition failed (Step 3)
- Commit rejected (Step 4)

## spec.md missing (Step 1)

```
❌ SPEC.md not found at: projects/{project-name}/spec.md

Options:
1. If you have a human design document (design.md, .docx, .pdf):
   → Run: /design-to-spec projects/{project-name}
   → This turns the design document into an AI-optimized spec.md

2. Write spec.md manually with:
   - Executive Summary
   - Scope (In Scope / Out of Scope / Affected Areas)
   - Requirements (Functional / Non-Functional)
   - Technical Constraints
   - Success Criteria
   - ADR Tensions (root CLAUDE.md §6.5; "No ADR tensions surfaced" is a valid entry)

Then run: /project-pipeline projects/{project-name}

Need help writing spec.md? I can help with that first.
```

Stop the pipeline here; do not generate artifacts without a spec.

## Artifact generation failed (Step 2 Part 2)

`project-setup` failed, or plan.md was generated without a usable Phase Breakdown.

```
⚠️ plan.md generation encountered issues.

Options:
1. 'retry'  - generate again (after fixing the named cause, e.g. a missing spec section)
2. 'manual' - I'll help you write plan.md interactively
3. 'stop'   - exit the pipeline and address the issues manually
```

In autonomous mode, retry once only if the cause is named and fixed. Otherwise stop and report.

## Task decomposition failed (Step 3)

Task generation failed, or produced no task files.

Usual cause: plan.md has no phased WBS ("Phase Breakdown" section with deliverables), or its deliverables are not concrete enough for task-create to decompose.

```
⚠️ Task file generation encountered issues.

This usually means:
- plan.md needs more detail in its Phase Breakdown section
- deliverables are not concrete enough

Options:
1. 'refine plan'  - I'll help improve the plan.md Phase Breakdown (then re-run Step 3)
2. 'manual tasks' - create task files interactively (they must still pass the Completeness Lint)
3. 'stop'         - exit and address manually
```

To recover, add a "Phase Breakdown" with concrete deliverables to plan.md (Step 2 Part 2), then re-run Step 3.

## Commit rejected (Step 4)

The branch exists but there is no initial commit, usually because a pre-commit hook rejected it (often a commit-message format check). Read the hook's output, fix the message or the flagged file, and re-run Step 4. Do not bypass the hook with `--no-verify`.
