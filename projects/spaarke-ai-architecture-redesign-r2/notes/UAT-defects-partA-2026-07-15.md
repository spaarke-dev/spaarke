# UAT Defects — Part A run, 2026-07-15

> **Run**: `CONSOLIDATED-UAT-CHECKLIST.md` Part A (judgment/confirmation/completion), operator, browser, spaarkedev1.
> **Environment**: BFF `spaarke-bff-dev` Healthy (compose round-9 build, 2026-07-14); PromptShield activated 2026-07-15; prerequisites P1–P4 green.
> **Outcome**: **Part A did NOT pass** → ADR-041/042 remain Proposed (not Accepted). Four defects + one Part-B blocker.
> **Design input (resolutions, for the next project)**: `projects/spaarkeai-assistant-enhancements-r1/notes/uat-failure-analysis-2026-07-15.md`.

---

## Defects

### DEF-A-01 — "create a follow-up task" creates a `sprk_event`, no association, no assignment (A1)
- **Symptom**: "create a follow-up task due Friday, assign it to me" → demanded a file to "ground" the task, then created a **`sprk_event`** record; **no association picker** (matter/project/invoice/none); **not assigned** to the user; many turns.
- **Root cause (catalog-verified)**: only two create capabilities live — `create-matter`, `create-task`. **No `create-todo`, no `create-event`**; the single generic `create-task` produces `sprk_event`. Binding disposition `Informational` → no gate/association picker. Assign-to-me (FR-B-06) not honored end-to-end. Spurious file-grounding requirement for a plain task.
- **Severity**: High (A1 acceptance criteria not met).
- **Disposition / owner**: **(B) route to a pre-seeded wizard** → `spaarkeai-assistant-enhancements-r1`. The `create-task`→`sprk_event` data-layer wrongness needs an owner regardless of A/B.

### DEF-A-02 — cannot distinguish "To Do" from "Event"; over-elicits; wrong record (A2)
- **Symptom**: "create a to do task" then explicit "a To Do not an Event" → over-elicited across ~8 turns, then created an **Event** (not a To Do), file **not attached**, **not assigned**.
- **Root cause**: same single `create-task`→`sprk_event`; "make a To Do" is un-routable (no `create-todo` capability). Structured fields elicited one-per-turn in chat.
- **Severity**: High.
- **Disposition / owner**: **(B)** dispatcher launches the correct pre-seeded wizard → `spaarkeai-assistant-enhancements-r1`. (Operator's own read during the run.)

### DEF-A-03 — "delete this task" also closed the Compose tab (A5)
- **Symptom**: delete removed the Event correctly but **also closed the unrelated Compose workspace tab**; assistant retained the file.
- **Root cause**: cross-surface side-effect — a chat action's completion tore down an unrelated Compose tab (tab-lifecycle; SpaarkeAi/compose keep-alive territory).
- **Severity**: Medium.
- **Disposition / owner**: **SpaarkeAi/compose surface** (post-compose-r2). No-collateral-teardown is also a dispatcher requirement in assistant-enhancements-r1.

### DEF-A-04 — "draft in Compose editor" claimed opened but tab didn't open (A6)
- **Symptom**: "draft a reporting letter to the client … in compose editor" → assistant said *"I have opened a draft … in the Compose editor,"* but **no Compose tab opened**.
- **Root cause**: UI-action truthfulness failure (fabrication) — asserted a UI action that didn't occur. D-F3 ack contract either doesn't cover this path or the deployed build predates it. Per A7 caveat, the Compose-open path is partly compose-r2's DEF-08 surface.
- **Severity**: High (A-X no-fabrication is an automatic-fail criterion; FR-A1-08).
- **Disposition / owner**: triage **core-ack vs compose (DEF-08)**. Ack-or-honest-failure is a hard invariant for assistant-enhancements-r1 too.

### DEF-A-05 — "create a new matter" dead-ends on closed value-set resolution (C1-adjacent)
- **Symptom**: drafted the matter; on confirm, **failed to resolve** practice area "Commercial Transactions" + matter type "Litigation" to exact values; **looped asking the user for exact labels**; couldn't open the record (creation failed, no GUID). Also proposed an incoherent practice-area/matter-type pair.
- **Root cause**: **the LLM is resolving a closed, system-owned value set** (option set / config lookup) via free text and failing; all-or-nothing commit; no picker fallback.
- **Severity**: High — the "live" create-matter (C1) has this flaw, so C1 as specced tests the wrong target.
- **Disposition / owner**: **deterministic constrained-field resolver + wizard hand-off** → `spaarkeai-assistant-enhancements-r1`.

## Blocker

### BLK-A-01 — Part B not runnable (record-bound unavailable)
- **Symptom**: operator reports "no such thing as record-bound at this point (that I'm aware of)."
- **Impact**: Part B (memory) is built on record-bound sessions (B1/B3/B5) → **cannot run** as written.
- **Action**: verify the record-bound launch path (ribbon EntityFormLaunch and/or `&entityType=&entityId=` URL param on the deployed SpaarkeAi build); confirm whether Part B is *blocked* or just *hard to invoke*.

---

## Disposition decision pending (operator)

Task/matter creation (DEF-A-01/02/05): **(A)** patch free-form chat creation (entity type, association picker, assign, option-set resolution) to meet the original criteria — vs **(B)** re-disposition the chat-creation criteria and route structured creation to a pre-seeded wizard in `spaarkeai-assistant-enhancements-r1`. **Recommendation: (B)** — do not bolt pickers/option-set resolution onto free-form chat. See the failure-analysis note for the full argument.

## Gate status

ADR-041 (Judgment/Confirmation/Completion) + ADR-042 (Memory): **remain Proposed.** Promotion to Accepted is blocked until Part A defects are worked (or re-dispositioned) and UAT re-runs clean, and Part B is unblocked + run.
