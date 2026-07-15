# BLOCKED — Task 002 (author create-todo + create-event capabilities)

> **Raised**: 2026-07-15 during task 002 execution (autonomous wave run). **Escalation trigger**: task 002's `<escalation>` ("if sprk_event or the sprk_todo regarding contract differs from what ADR-024/the wizard entity expects, STOP and escalate rather than inventing a field shape") — fired. Per CLAUDE.md §6 / §6.5.
> **Status**: task 002 = ⛔ blocked, awaiting owner decision. Blocks the whole Phase 2 structured-creation core (010/012/013/014 premises depend on the resolution).

## What I found in the live catalog (spaarkedev1, `sprk_playbookconsumer`)

The dispatch catalog already ships **`create-matter`** (UC-B-6) and **`create-task`** (UC-H-1), both enabled. Reading their `sprk_tooldescription` reveals the **as-built create pattern**, which diverges from this project's spec in two material ways.

### Conflict 1 — "To Do" target entity

- **Spec FR-A2 / design**: distinct **`create-todo` → `sprk_todo`** and **`create-event` → `sprk_event`**; explicit "To Do" writes `sprk_todo` (per the ADR-024 first-class-todo model).
- **As-built**: the shipped **`create-task`** capability writes **`sprk_event`** with `sprk_eventtype_ref = "Task"` (recordId `124f5fc9-98ff-f011-8406-7c1e525abd8b`). There is **no** capability that writes `sprk_todo`.
- **Both tables exist** (`sprk_todo` "To Do" AND `sprk_event` "Event") — so this is a genuine modeling choice that was made one way in the catalog and specified the other way here. Not a missing table.

### Conflict 2 — wizard hand-off vs. direct `dataverse.create_record`

- **Spec premise (FR-A1/A3; tasks 012/013/014)**: "draft-in-chat → **commit-in-a-pre-seeded `Create*Wizard`**; the wizard owns the gated write." An entry-payload envelope launches the wizard.
- **As-built**: `create-matter` / `create-task` are `disposition = Informational`, `surfaces = assistant`. They **DRAFT a proposal, then have the LLM call the `dataverse.create_record` tool directly**, using that tool's **confirmation dialog** as the approval step. **No wizard is launched**; there is no disposition/surface in these rows that launches a pre-seeded wizard.
- Implication: the "~80% already ships; just extend the catalog" framing in the spec **undersells the gap** — the wizard-launch hand-off is a **net-new pattern**, not an extension of the shipped create capabilities. (This is consistent with the R2 UAT create-flow failures the project targets — but the *fix shape* is the open decision.)

## Why this blocks (not a silent pick)

Task 002 cannot author `create-todo`/`create-event` "mirroring create-matter" without first resolving: (a) does "To Do" write `sprk_todo` or stay `sprk_event`(Task)? and (b) do the R1 create capabilities **launch a wizard** (the spec's pattern, requiring tasks 012/013/014) or **harden the shipped direct-`create_record` path** (smaller, but abandons the wizard premise)? Picking silently would either invent a field shape (trigger's forbidden case) or quietly rewrite the whole Phase-2 approach.

## Resolution options (owner decision)

| # | Option | Scope impact |
|---|---|---|
| **A** | **Wizard hand-off, `sprk_todo` target** (spec as written): new `create-todo`→`sprk_todo`, `create-event`→`sprk_event`; capabilities launch pre-seeded wizards; retire/repoint the shipped `create-task`. | Largest. Net-new wizard-launch mechanism (012/013/014 as specced) + reconcile/retire `create-task`. True to the design; most work. |
| **B** | **Keep direct-`create_record`, add `sprk_todo` split** (extend as-built): split the shipped `create-task` into `create-todo`(→`sprk_todo`) + `create-event`(→`sprk_event`), keep the LLM-calls-`create_record` + confirmation-dialog pattern; drop the wizard hand-off (retire/park tasks 012/013/014). | Medium. Fixes UC-1..3 entity-correctness cheaply; abandons the "commit-in-wizard" UX the design argued for. |
| **C** | **Hybrid**: keep direct-`create_record` for the R1 core (fast UC fix), model the wizard hand-off as an additive R1.5 surface. | Medium; re-scopes 012/013/014 to a later pass. |
| **D** | **Keep `sprk_event`(Task) model** (don't introduce `sprk_todo` for create): rename `create-task`→`create-event` semantics, author a clearer To-Do-vs-Event ambiguity, no new entity target. | Smallest; contradicts spec FR-A2 + the ADR-024 first-class-todo intent — would need a spec/design amendment (§6.5 path B). |

**Recommendation**: **C** (or B) for R1 velocity — the R2 UAT failures were about *dead-ends / wrong entity*, which the direct-`create_record` path plus a clean `sprk_todo`/`sprk_event` split fixes without inventing the wizard-launch mechanism mid-project. Reserve the wizard hand-off (A / tasks 012-014) for a deliberate follow-on. But this reverses a core design decision, so it's the owner's call.

## What is NOT blocked

Non-Phase-2 tasks whose premises don't depend on this: **020** (action truthfulness), **040/043** (drop-down, SNS cards), **030/031/032** (User Model), **021/022** (risk gate — though 021 depends on 002 for the catalog risk value; can proceed against existing capabilities). If you want to keep momentum while deciding, I can run **030 (User Model producer)** or **020 (truthfulness)** next — both are independent of this conflict.
