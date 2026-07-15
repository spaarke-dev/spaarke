# Current Task State — Assistant Enhancements R1 ("Follow-Through")

| Field | Value |
|---|---|
| **Active task** | none — **task decomposition pending** |
| **Status** | Planning artifacts generated (README, plan.md, CLAUDE.md, spec.md, design.md). Task POML files NOT yet created. |
| **Next action** | Run **`/task-create projects/spaarkeai-assistant-enhancements-r1`** (fresh session recommended for context) to decompose [`plan.md`](plan.md) into POML tasks. Then review the breakdown BEFORE executing (BFF hot-path / dispatch-spine = high blast radius). |
| **Branch** | `work/spaarkeai-assistant-enhancements-r1` (synced with origin/master 2026-07-15; 12-commit merge, clean, seams intact) |
| **Scope** | R1 only (reactive create-flow core + User Model + tool drop-down + risk-wiring + grounding-guard). **R1.5 (proactive push / Azure SignalR) designed, not decomposed.** |

## Prerequisites status

- ✅ `sprk_userprofile` schema created + verified in spaarkedev1 (8 columns + `sprk_systemuser` lookup + alt key + N:N to `sprk_practicearea_ref`).
- ✅ Registered in `projects/INDEX.md`.
- ◻️ Authoring reviewer named (owner) — recorded in spec.
- ◻️ (R1 execution) confirm exact `sprk_matter` practice-area / matter-type field shapes for the resolver (FR-B1) at task time.

## Open decisions carried into execution (non-blocking)

- Finalize `sprk_primaryrole` global-set binding (values present; bind-to-global optional).
- Amended `EnvelopeBudget.User` value — size from rendered profile fragment (NFR-01).
- Constrained-field resolver placement — `Services/Ai/PublicContracts/` vs new component (Placement Justification at task time).

## Decisions log (design→plan)

All owner decisions + the four design-time open questions are resolved and recorded in [`spec.md`](spec.md) Owner Clarifications + [`design.md`](design.md) revision log. redesign-r2 verified complete/merged/archived → **R1 is self-contained, no cross-project coordination.**
