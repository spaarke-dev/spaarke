# UAT Feedback — Consolidation Bucket (HOLD until milestone)

> **Status**: NOT actioned now (operator direction 2026-07-09). Captured for consolidation at a later milestone as a set of bugs + refinements/enhancements. Focus meanwhile = finish the spec'd tasks to 100%.
> **Do NOT let this drive interrupts** — it's a backlog, not a work queue, until the operator opens the refinement milestone.

## Round 1 — G-R2-A unbound standalone assistant (spaarkedev1, 2026-07-09)

Scorecard: S3/S4/S5/S6 PASS (confirmation/completion/friction model works). S1/S2/create-matter FAIL. Two root causes + enhancements.

### Root cause 1 — file-centric input grounding (VALIDATES the Phase E finding)
- **S1**: "create a task…" → Assistant demanded *"document context / session files"* to create a task. Wrong — a task grounds on a **Matter/Project**, not a document. Even given a matter ID ("REAL-2026-123456.01") it still insisted on session files.
- **create-matter**: grounded on the uploaded engagement letter, then couldn't complete (matter-type/practice-area lookup + tool-call budget).
- **Fix home**: Phase E `ContextBinder`/operand model + **correct the create-task/create-matter capability input schemas + descriptions** to declare their real inputs (details + matter/parent ref, with record lookup) instead of demanding files. This is the "engine built to the first consumer (summarize: file→LLM)" disease, now proven in the live product.

### Root cause 2 — ambiguity clarification is a soft spot
- **S2**: "create a to do task document" → Assistant just picked "to-do task" + asked for details; did NOT surface the to-do/task/document ambiguity. Layer-1 "agent asks when torn" is prompt-driven and didn't fire. Needs a directive nudge + eval cases (no deterministic ambiguity detector exists).

### Enhancements (works, but better)
- **S4** email draft+handoff works ("Draft email created… No email was sent" + Open record). Better: "Open record" → the **Email Send wizard** (with attach-file support), not a plain record tab.
- **S6** refusal + Document-Upload deep-link works. The link should **open the wizard**, not land in a new tab.
- **create-matter** hit **tool-call budget limits** resolving lookups → ties to budget work (task 054); lookup resolution needs headroom. Also should ask for a matter/parent + offer record lookup rather than free-text ID entry.
- General (Part 2 — input affordances): wire the next-step/consumer chips ("Select a Matter", "Upload File") to **real record pickers** that feed the selection back into the turn; use structured elicitation (matter lookup, date picker) instead of free-text. Mechanism exists (`consumer_chips` / `ChipTransitions` + record-modal-selection pattern) — needs wiring.

### Control-surface map (for the refinement loop, when opened)
- **Educate the Assistant**: catalog DATA (input schemas + `sprk_description` + risk tiers, GitOps/Model 1) → primary; directives (sparingly, per D-F0 anti-pin-accretion); **eval families** = lock behavior as a tested contract.
- **Refinement loop**: adjust catalog description/input-schema (+ directive if truly cross-cutting) → add eval case → verify green → redeploy → re-test.
- **Input affordances**: `consumer_chips`/`ChipTransitions` + OutcomeCard next-step chips + `RecordNavigationModalShell`/record-modal-selection pattern.
