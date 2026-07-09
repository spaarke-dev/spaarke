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

### Design direction — AI-friendly inline create ("invert the wizard") (operator 2026-07-09)

**Operator directive**: the `Create*Wizard` is *just a UI on top of its services*. AI Assistant users do NOT expect a bulky wizard popup. Instead embed an AI-friendly inline **form** that collects the (very few) required fields in the chat stream.

**Architecture — invert, don't reuse, the wizard.** A `Create*Wizard` conflates 3 separable layers:
1. **Service layer** — `matterService.ts`/`projectService.ts` + field-mapping framework (creation-time assigned-resource inheritance; Copy/Default/Concat/Template). Context-agnostic. **KEEP = the reuse.**
2. **Input-collection UI** — the multi-step paged form. **REPLACE with a compact inline `ActionFormCard`.**
3. **Host shell** — code-page wrapper (`navigateTo`, auth bootstrap, `sprk_creatematterwizard`). **DROP — assistant never launches a code page.**

**`ActionFormCard` = the input dual of `OutcomeCard`.** For a create (risk ~2b, Dataverse write) it collapses THREE currently-separate things into ONE surface:
- **disambiguation** (matter vs project vs to-do) → fixes S2
- **input collection** (few required fields, pre-filled from inferred context/uploaded file) → replaces broken free-text Q&A ("asked more questions… couldn't do it")
- **confirmation-before-execute** → the form *is* the gate (aligns with ambiguity-not-origin confirmation model)

Folds create-record into the **existing** gate + ContextBinder `## Input` model — NOT a parallel mechanism. Execution = **hybrid** (client card → BFF Action → existing wizard service). Field-mapping runs **server-side at execution** so creation-time inheritance is untouched.

**Key open decision — field-schema source of truth.** Recommend **mirror-first JPS input schema per create Action** (the Phase E `## Input`/ContextBinder pattern), NOT importing wizard TS field-lists into chat. Rationale: keeps creates in the **closed-catalog Action model** ("will be closed catalog bound"), gives client a *declarative* schema to render the card with zero wizard-UI coupling, field-mapping still fills the rest server-side. "Very few fields" (matter = name + regarding-client) makes it tractable. Alternatives considered/rejected: (A) derive from wizard TS field-lists = re-couples client; (C) Dataverse required-metadata introspection = over-collects, ignores field-mapping defaults.

**Reserved seam**: ADR-043 Action Engine seam already reserves this. Build = refinement milestone (not spec'd r2); r2 foundation (gate, ContextBinder, closed catalog, ADR-043) is being built to *accommodate* it — consumer surfaces + validates, does not drive.

### Control-surface map (for the refinement loop, when opened)
- **Educate the Assistant**: catalog DATA (input schemas + `sprk_description` + risk tiers, GitOps/Model 1) → primary; directives (sparingly, per D-F0 anti-pin-accretion); **eval families** = lock behavior as a tested contract.
- **Refinement loop**: adjust catalog description/input-schema (+ directive if truly cross-cutting) → add eval case → verify green → redeploy → re-test.
- **Input affordances**: `consumer_chips`/`ChipTransitions` + OutcomeCard next-step chips + `RecordNavigationModalShell`/record-modal-selection pattern.
