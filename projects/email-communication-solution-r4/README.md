# Email Communication Solution R4

> **Portfolio**: [Project #642](https://github.com/spaarke-dev/spaarke/issues/642) · Epic [#431 EMAIL & MESSAGING](https://github.com/spaarke-dev/spaarke/issues/431) · [Board #2](https://github.com/users/spaarke-dev/projects/2)

> **Status**: 🚀 Ready for implementation — spec + plan + 45 tasks generated (`/design-to-spec` → `/project-pipeline`, 2026-07-14). Begin with Wave 0 task 001 via `task-execute`.
> **Date**: 2026-07-14
> **Type**: Architecture assessment + design charter (feeds `/design-to-spec` → `/project-pipeline`)
> **Author**: Senior platform review (Claude Code, with 4 parallel code/research audits)

## What this project is

R4 is the **unified Communication Intelligence project**. It **absorbs R3** (see design.md §0.6): R3 (`email-communication-solution-r3`) was fully designed and decomposed into 79 tasks but **never executed — zero code landed**. Rather than run two overlapping projects and hand-coordinate their four shared surfaces (the `sprk_communication` schema, the Communication ADR, the Code Page, and the server send-path changes), R4 is the single project covering **both** R3's send-side client consolidation **and** R4's receive-side intelligence.

Where R2 unified the server-side communication pipeline, R4 (now including R3's scope) answers four questions:

1. What email capabilities do we actually have today, and does the architecture need updating?
2. What did Microsoft change (mid-2026) in Graph, Work IQ, and the Outlook add-in platform that forces our hand?
3. What state is the Spaarke Outlook add-in in?
4. How should we match **incoming** email to related records (matter, project, invoice, service request, work assignment, event, contact, account) — deterministically first, AI-assisted second?

## Documents

| File | Purpose |
|---|---|
| [`design.md`](design.md) | **The main deliverable.** Current-state assessment, Microsoft-platform delta, Outlook add-in review, and the proposed Email Association Engine architecture + phased plan. |
| [`README.md`](README.md) | This file. |

## Relationship to prior iterations

| Project | Delivered | Status |
|---|---|---|
| `x-email-communication-solution-r1` | R1 server foundation (early) | Superseded by R2 |
| `email-communication-solution-r2` | Server-side Communication Service (Graph subscriptions, OBO send, `.eml` archival, `IncomingAssociationResolver`) | ✅ Completed 2026-03 |
| `x-email-communication-solution-r3` | Client-side consolidation (`<EmailComposer />`, `sendCommunication()`, ADR-033) | **SUPERSEDED — absorbed into R4** (designed, 79 tasks, never executed; design preserved at [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md)) |
| `x-email-to-document-automation` / `-r2` | Native `email`-activity → `sprk_document` path | Partly built; async path incomplete (see design.md §2.3) |
| `spaarke-email-intelligence-module` | Email Triage product concept (July 10) | Concept; **cites stale components** — reconciled in design.md §2.5 |
| **`email-communication-solution-r4`** | **This** — incoming-email association engine + platform-currency updates | Pre-spec |

## Next step

Pipeline complete. Artifacts: [`spec.md`](spec.md) (27 FRs, 8 NFRs) · [`plan.md`](plan.md) (8-wave WBS) · [`CLAUDE.md`](CLAUDE.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) (45 tasks).

Begin execution: say **"work on task 001"** (or "continue"). W0 blocks all waves; W1‖W2 run in parallel after W0; **W5 is gated on task 050** (Services/Ai coordination with `spaarke-ai-architecture-redesign-r2`). Run `/conflict-check` before every BFF PR.
</content>
</invoke>
