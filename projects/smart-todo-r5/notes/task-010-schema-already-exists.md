# Task 010 — Finding: `sprk_priority` / `sprk_effort` already exist (verify-only completion)

> **Date**: 2026-08-15 · **Task**: 010 (Create sprk_priority + sprk_effort Choice columns) · **Outcome**: COMPLETE by pre-existence — **no schema write performed**.

## What was found

Step 1 pre-validation (`describe tables/sprk_todo`) showed **both target columns already exist on the live `sprk_todo` entity with the EXACT option labels/values the spec (FR-02/FR-03) requires**:

| Column | Live options (from `describe`) | Spec (FR-02/FR-03) | Match |
|---|---|---|---|
| `sprk_priority` | Urgent=100000000, High=100000001, Medium=100000002, Low=100000003 | identical | ✅ exact |
| `sprk_effort` | None=100000000, Very High=100000001, High=100000002, Medium=100000003, Low=100000004 | identical | ✅ exact |

`sprk_priorityscore` and `sprk_effortscore` both remain as `INT` (Number) fields — untouched, as required.

## Decision (per CLAUDE.md §2 code-wins + directional/prescriptive execution guidance)

The task's `<goal>` — "both Choice columns exist on sprk_todo, published, with the exact option labels/values" — is **already met**. Creating them again would either error ("already exists") or risk duplicate/renumber drift. The escalation trigger fires only when columns exist with **different** values; here they are **identical**, so this is a clean "already satisfied," not a conflict. Therefore: **no `create_table`/Web-API write was executed.** All 5 acceptance criteria verified against the live `describe` output.

## Acceptance criteria verification (against live `describe tables/sprk_todo`)

1. ✅ `sprk_priority` = Choice, exactly 4 options (Urgent/High/Medium/Low with the exact integers).
2. ✅ `sprk_effort` = Choice, exactly 5 options (None/Very High/High/Medium/Low with the exact integers).
3. ✅ `sprk_priorityscore` + `sprk_effortscore` unchanged `INT` fields.
4. ✅ Both columns are published (live metadata query reflects them).
5. ✅ Negative: no extra/undocumented options on either column (exhaustive match, not superset).

## Downstream implications

- **Task 011** (auto-score handler, Option B): schema is ready — proceed. Handler maps choice → the existing score fields.
- **Task 013** (RegardingResolver wiring): the FULL regarding field set already exists on `sprk_todo` (`sprk_regardinganalysis|budget|communication|contact|document|event|invoice|matter|organization|project|reportcard|servicerequest|workassignment` + denormalized `sprk_regardingrecordid|name|number|type|url`). So FR-04 is **form-wiring only**, not field creation — consistent with the project CLAUDE.md note.
- **Provenance**: columns were most likely created during smart-todo-r4 (or an earlier smart-todo effort) and never captured back into the R5 spec as "already done." No action needed beyond this note.

## No quality gates run

Task 010 performed **no code/logic/schema change** (verify-only). Per task-execute Step 9.5 skip clause (configuration/schema-only, nothing modified) and no `tests/**` touched, code-review + adr-check are not applicable.
