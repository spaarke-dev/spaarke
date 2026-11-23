# Integration Points — who calls code-review, and what they do with the report

Read when `code-review` was invoked by another skill and you need to know how the caller uses the report. The callers own these procedures; if this file and the caller disagree, the caller's SKILL.md is right.

## 1. task-execute (Step 9.5 — Quality Gates)

After code implementation and before task completion:
```
AFTER all implementation steps complete:
  RUN /code-review on files modified in this task
  CLASSIFY every finding per task-execute Step 9.5 (fix-now F1–F4 / known-limit K1–K4) —
    act on the class, not the severity label
  FIX every F-class finding before marking the task complete (fixing is never capped);
    file and report an F-class defect outside the task's scope
  RE-RUN /code-review on the fix scope only (fix diff + direct callers and callees) until no F-class finding remains
  RUN /adr-check on modified files
  THEN proceed to task completion
```

Tasks that modify `tests/**`, or are tagged `testing`, `test-reset`, `deletion` or `integration-test`, run this gate at every rigor level (root CLAUDE.md §8).

## 2. push-to-github (Step 1 — Pre-flight Checks)

Before committing changes:
```
RUN quality checks (ask user first):
  -> Execute linting on changed files
  -> Execute /code-review on changed files
  -> Execute /adr-check on changed files
  -> Report any issues found
```

## 3. Project wrap-up (task 090)

Final quality validation before project completion:
```
RUN /code-review on entire project scope
RUN /repo-cleanup to audit and clean ephemeral files
VERIFY all issues addressed before marking project complete
```
