# CLAUDE.md — Assistant Enhancements R1 ("Follow-Through") Project Context

> Loads when working in this project. Complements root `CLAUDE.md` (which always wins on binding rules). Source of truth: [`design.md`](design.md) → [`spec.md`](spec.md).

## 🚨 MANDATORY: Task Execution Protocol

When executing any task in this project, you MUST invoke the **`task-execute`** skill — do NOT read POML files and implement manually. It loads knowledge (ADRs/constraints/patterns), tracks `current-task.md`, checkpoints every 3 steps, and runs the Step 9.5 quality gates (code-review + adr-check). Trigger phrases ("work on task X", "continue", "next task") → invoke `task-execute`. See root `CLAUDE.md` §4.

## What this project is

Reposition the Assistant into a grounded **dispatcher**. **R1 = reactive-first**: fix the broken create flows (draft-in-chat → pre-seeded wizard) + constrained-field resolver + action-truthfulness + User Model + tool drop-down + `sprk_risk` wiring + grounding-guard. **R1.5 (proactive push / Azure SignalR) is designed, NOT in this project's task set.** ~80% of the NBA machinery is shipped under ADR-039 — **extend the catalog, do not build a new pipeline.**

## Task set (decomposed 2026-07-15 — 25 tasks, 7 phases)

See [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for the full map (deps, model-tier, effort, parallel waves). Phases: **1** catalog/schema (001–003) · **2** structured-creation core (010–014) · **3** truthfulness/risk (020–022) · **4** User Model (030–032) · **5** Assistant surface (040–044) · **6** authoring/eval/hardening (050–054) · **7** wrap-up (090). Start at **001**. **Do NOT auto-execute** (BFF hot-path + dispatch spine). Dispatch-spine tasks (010, 021, 022, 030, 044) are **sequential / main-session** (seam-test DoD). All waves **goal-eligible: NO**.

## Binding constraints (MUST / MUST NOT)

- ✅ Resolve closed, system-owned value sets **deterministically against metadata** (the constrained-field resolver); ❌ NEVER let the LLM emit a final closed-set value.
- ❌ NEVER introduce a second intent mechanism (classifier, reranker, vector router, keyword map, lexicon). The User Model may ONLY (a) bias the one agent turn via `ContextBinder.userFragment` and (b) deterministically reorder already-grounded chips for display. (ADR-039.)
- ✅ Ack-gate every action claim or fail honestly; ❌ NEVER make optimistic UI claims. (P5 / FR-C1.)
- ✅ Inject the stated profile via `ContextBinder.userFragment`; ❌ do NOT implement `IOrganizationalContextProvider` (wrong scope; deferred).
- ✅ Grounding is a pure predicate (removes-the-impossible); ❌ NEVER gate by hardcoded tool-name lists.
- ✅ Reuse the shipped dispatch seam (`POST /api/ai/chat/sessions/{id}/dispatch`); ❌ NO new BFF dispatch endpoint (compose invariant).
- ✅ **preference ≠ permission** — the profile biases/reorders; it never grants a capability (grounding still gates).
- ✅ Consume `Services/Ai/PublicContracts` seams — **NO fork** of `Services/Ai` internals; `/conflict-check` before BFF PRs.

## Applicable ADRs

ADR-039 (grounded/closed-catalog — most-cited), ADR-040 (ledger), ADR-041 (ConfirmationPolicyEngine/ack), ADR-042 (memory), ADR-043 (input-resolution; dispatch-spine → `tests/integration/seam/**` DoD), ADR-038 (testing), ADR-032 (null-object), ADR-024 (`sprk_todo`). See [`plan.md`](plan.md) for the loaded set. ADR-tensions (5) in [`spec.md`](spec.md).

## Key seams (verified 2026-07-15)

| Concern | File |
|---|---|
| Stated-profile injection | `Services/Ai/Context/ContextBinder.cs` (`userFragment` / `ResolveUserMemoryFragmentAsync`) |
| Grounding predicate | `Services/Ai/Chat/AgentToolProjection.cs` (`PreFilter`, `AgentToolFilterContext`) + `SprkChatAgentFactory.cs` |
| Risk gate | `Services/Ai/Chat/Gate/` + `PendingPlanManager.cs` (`RequiresConfirmation`) |
| Catalog | `Services/Ai/PublicContracts/` (`ConsumerRoutingService`, `BindingCapabilityTool`, `ChipTransition`) |
| SNS / chips (client) | `ConsumerChips` / `useConsumerChips` |
| Drop-down / wizard library (client) | `ContextPaneMenu`/`WorkspacePaneMenu`, `GetStartedCardsWidget`, `CreateMatterWizardWidget` + `wizard_step` |
| Dataverse | `sprk_userprofile` (created), `sprk_practicearea_ref` (resolver target), `sprk_todo`/`sprk_event` |

## Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff-api>YES — Services/Ai: stated-profile producer (ContextBinder.userFragment), sprk_risk gate-wiring (PendingPlanManager), grounding predicate (AgentToolProjection/SprkChatAgentFactory), constrained-field resolver</bff-api>
  <spaarke-ai>YES — tool drop-down, My Assistant questionnaire, SNS cards, wizard entry-payload hand-off, ack-gated action reporting</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-CLAUDE-md>NO</root-CLAUDE-md>
</hot-path-declaration>
```

Registered in [`projects/INDEX.md`](../INDEX.md). Coordinate `Services/Ai` PRs via `/conflict-check` (email-r4 W5, daily-update-r5). Publish-size ≤60 MB (baseline ~49.63 MB incl. PDBs).

## Execution model (per root §8.5)

Sonnet 5 @ `high` default. Use **`opus`/`xhigh`** for the dispatch-spine + resolver tasks (2a resolver, 3b/3c gate-wiring, 5e PreFilter, 4a/4c hot chat-path) — high blast radius, hard reasoning. Dispatch-spine tasks are **sequential/main-session** (seam-test DoD), never parallelized with each other.

## Rigor

FULL (code-review + adr-check at Step 9.5) for all BFF/dispatch/`.cs`/`.tsx` tasks and any `tests/**` task (TEST-MODIFYING override). Wrap-up runs `/test-diet`.
