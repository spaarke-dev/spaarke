# Spaarke AI Assistant Enhancements R1 — "Follow-Through"

> **Portfolio**: [Project #649](https://github.com/spaarke-dev/spaarke/issues/649) · Parent [Epic #421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · [Board (Project #2)](https://github.com/users/spaarke-dev/projects/2) · Type: AI · Status: **Complete**

> **Status**: ✅ **COMPLETE (2026-07-23)** — all 25 tasks done; deployed + smoke-verified to dev; eval gate green (92/92); /test-diet clean (`notes/test-diet-report.md`); deferrals filed ([#684](https://github.com/spaarke-dev/spaarke/issues/684); R1.5 delivered via `spaarke-notification-spine-r1`). Owner UAT of the final batch (suggestions/dismiss/"add a task"/membership) remains open but does not gate close-out. All 7 graduation criteria met (below).
> **Branch / worktree**: `work/spaarkeai-assistant-enhancements-r1` · `C:/code_files/spaarke-wt-spaarkeai-assistant-enhancements-r1`
> **Created**: 2026-07-15
> **Owner**: Ralph Schroeder
> **Source**: [`design.md`](design.md) → [`spec.md`](spec.md) (R1 only; R1.5 designed, not decomposed)

## What this delivers

Repositions the Spaarke Assistant from a reactive "ask-me-anything" text box into a grounded **dispatcher** that reliably finishes the operator's likely next step. **R1 is reactive-first** — it fixes the highest-value, most-broken flows surfaced in the R2 live-UAT ([`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md)):

1. **Structured creation** — draft-in-chat → commit-in-a-**pre-seeded wizard** for **create-matter / create-to-do / create-event** (no more dead-ends, wrong entity types, or one-field-per-turn elicitation).
2. **Deterministic constrained-field resolver** — the LLM never guesses a system-owned value set (option sets, lookups); "smart pre-seed" hands the wizard defaulted dropdowns.
3. **Action-outcome truthfulness** — every action claim is ack-gated or fails honestly; no collateral pane/tab teardown.
4. **User Model** — an AI-readable stated profile (`sprk_userprofile`) injected into the one agent turn to personalize suggestions.
5. **Assistant tool drop-down** — Quick Start (existing wizard library) + My Assistant questionnaire.
6. **`sprk_risk` gate-wiring** + **grounding-predicate** + **Suggested-Next-Steps** cards.

~80% of the Next-Best-Action machinery already ships under ADR-039; R1 **extends the shipped catalog**, it does not build a new pipeline.

## Out of R1 (designed, not decomposed here)

- **R1.5 — full proactive-push capability**: server-initiated push (Azure SignalR + durable outbox + Daily-Briefing producer) so the Assistant surfaces grounded work while the user is idle. Architected as a general notification spine. See [`design.md`](design.md) §14.1a/§14.1b/§12.5/§15.4.

## Graduation criteria (R1 done when) — ✅ ALL MET (2026-07-23)

1. ✅ "Create a matter / to-do / event" each produce the **right entity** via a pre-seeded wizard, no dead-end — tasks 002/012/013/014 (surface-launch create-matter/task/todo/event/project; smart pre-seed via the 010 resolver enrichment).
2. ✅ Closed-set fields resolve via the constrained-field resolver; a nonsensical pair cannot commit — task 010; **proven in the 051 eval** (`IncoherentPracticeAreaMatterType_CannotCommit…`: CREATE-MATTER@v1 emits independent string LABELS, allowstools=false).
3. ✅ No fabricated action claims; a delete does not tear down unrelated tabs — task 020 (ack-gated truthfulness + no-collateral-teardown).
4. ✅ `sprk_risk` gates as designed (Always Confirm → suggestion-that-launches; Confirm-When-Uncertain reads the single-turn signal) — tasks 021/022 (dispatch-spine seam-tested).
5. ✅ Profiled turn carries the stated profile within the amended token budget; byte-stability + golden-utterance eval green; `AgentToolFilterContext` carries no profile/memory member (preference≠permission) — tasks 030/031/032; **operationally proven in the 051 eval** (`ProfileInjection_DoesNotFlip…`, in the merge gate).
6. ✅ Tool drop-down (Quick Start + My Assistant) present; My Assistant writes the profile + seeds memory; SNS cards render post-dispatch — tasks 040/041/042/043.
7. ✅ Publish-size ≤60 MB; no new HIGH CVE — task 053 (48.80 MB compressed incl PDBs; R1 added zero packages; the one pre-existing HIGH Kiota CVE was remediated repo-wide to 1.22.0).

## Key artifacts

| File | Purpose |
|---|---|
| [`design.md`](design.md) | Full design — as-built-aligned, reviewed (4 Fable agents + researcher + UAT). Source of truth. |
| [`spec.md`](spec.md) | AI-optimized R1 spec: 25 FRs / 7 NFRs / ADR-tensions / success criteria. |
| [`plan.md`](plan.md) | Phased WBS for task decomposition. |
| [`CLAUDE.md`](CLAUDE.md) | Project execution context (ADRs, seams, constraints, rigor). |
| [`notes/uat-failure-analysis-2026-07-15.md`](notes/uat-failure-analysis-2026-07-15.md) | R2 UAT evidence base for the create-flow fixes. |

## Prerequisites (owner-completed 2026-07-15)

- `sprk_userprofile` schema created + verified in spaarkedev1 (8 columns + `sprk_systemuser` lookup + alternate key + N:N to `sprk_practicearea_ref`). See spec FR-E1.

## Next step

Run **`/task-create projects/spaarkeai-assistant-enhancements-r1`** (fresh session recommended for context headroom) to decompose `plan.md` into POML task files, then execute after review. **Do not auto-execute** — R1 touches the BFF hot path + dispatch spine (high blast radius); review the task breakdown first.
