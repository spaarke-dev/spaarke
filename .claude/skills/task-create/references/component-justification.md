# task-create — component justification detail (Step 3.5.6)

> **Parent skill**: [task-create](../SKILL.md) Step 3.5.6. Read this when a task adds new surface or materially grows an existing component.
> **Last Reviewed**: 2026-11-03

The gate itself — the `<justification>` element, the three questions, the rewrite/demote/proceed logic and its scope — is in SKILL.md Step 3.5.6. This file holds the worked anti-patterns and the component-complexity check.

## Anti-patterns the gate catches

Real examples from `chat-routing-redesign-r1` (moved verbatim from root CLAUDE.md §11 on 2026-10-07):

- ❌ "Delete LegalWorkspace `CreateRecordStep.tsx` as dead code per OC-R4-05" — the retirement doc actually preserves it as library; cost-of-doing-nothing was assumed wrongly.
- ❌ "Add new `sprk_playbookcode` lookup keys" — `sprk_playbookid` already exists as the immutable opaque ID; the existing-question went unanswered.
- ❌ "Build 8 retrieval tool handlers" — 7 of 8 fail the extension test for the MVP use case; one excellent handler beats five that partially overlap.

Cost of the rule: one paragraph per new component. Cost of its absence: shipped scope creep.

## Component-complexity check

Standard: [`docs/standards/COMPONENT-COMPLEXITY.md`](../../../../docs/standards/COMPONENT-COMPLEXITY.md). For any task that creates a new component OR **materially grows an existing one**, evaluate complexity and cohesion, not line count:

- If the task would add a **second (or Nth) responsibility** to an already multi-purpose component, design the task to **extract the diverging responsibility into the right seam** up front — don't accrete another concern onto a god-class. Prefer extending a *cohesive* component; don't manufacture thin components to dodge a size number.
- A large file is **not** a defect by itself. A large, single-responsibility component (state machine, exhaustive mapping, generated code) is legitimate; note it in the task if relevant.
- When an existing component's responsibilities have genuinely diverged, prefer a **deliberate decomposition task** over reactively splitting mid-feature.
- There is no hard LOC gate: the retired god-class ratchet was replaced by this authoring/review guidance plus a non-blocking observation report, `scripts/report-large-server-files.ps1`.
