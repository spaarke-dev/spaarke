# Design Note — Re-scoped 011 + 012: Advisory grounded-reasoning capabilities (Option A)

> **Status**: APPROVED (owner chose Option A, 2026-08-16) — plan of record for the re-scoped E1 spine.
> **Author**: task-execute (main session), spaarkeai-assistant-enhancements-r4
> **Supersedes**: the original 011 framing ("add a predicate to the top-level `AgentToolProjection.PreFilter`"), which was proven ADR-039-incompatible — see the escalation record in `current-task.md`.
> **Owner intent (verbatim)**: *"we want the LLM to exercise its value in helping present viable and logical alternatives … but still working within the framework that the Assistant recommendations are logical and accurate."*

---

## 1. Why the original 011 could not be built as scoped

Traced end-to-end (2026-08-16):

- `AgentToolProjection.PreFilter` / `AgentToolCatalogProjector.ResolveToolsAsync` are called in **exactly one place**: `SprkChatAgentFactory.CreateAgentAsync` — the **single top-level Text-path turn** (the one probabilistic dispatch decider). There is **no nested per-capability tool-projecting turn**.
- At that top-level turn **no single Action is selected** — the model is mid-decision. Narrowing the projection to *one* Action's `GroundedToolAllowList` there requires deciding "the user wants task-agenda" **before** the model runs = intent detection = **ADR-039 MUST-NOT** (a second decider).
- `output_determinism: advisory` is consumed **only on the linear path** (`ActionRunner`). `list-tasks` is `actionType:0` (linear `AiAnalysis`, `allowstools=false`) → one-line ack + `surface_launch`. Dispatch (`BindingCapabilityTool → SessionDispatchOrchestrator → ActionRunner`/coded-workflow) **never re-projects tools**. `spaarke.grid_overview` / `spaarke.daily_briefing_overview` are handler tools mounted **only** in the top-level turn.

⇒ FR-02's *"only the allow-listed tools mount **for that capability's turn**"* presupposes a **per-capability tool-calling turn that does not exist**. Option A builds exactly that turn.

## 2. Mechanism (Option A) — the advisory grounded-reasoning turn

When the one top-level decider selects an **advisory tool-calling capability** (an Action with `output_determinism: advisory` AND a non-empty `GroundedToolAllowList`), its dispatch runs a **nested bounded agent turn** instead of `ActionRunner`'s single completion:

```
Top-level Text-path turn  (ONE dispatch decider — unchanged)
   │  model picks the list-tasks(advisory) BindingCapabilityTool
   ▼
SessionDispatchOrchestrator.DispatchAsync
   │  binding → Action resolves as advisory + GroundedToolAllowList = {grid_overview, daily_briefing_overview}
   ▼
NEW: AdvisoryCapabilityRunner  (nested bounded turn)
   • tool set  = ONLY the allow-listed GROUNDED READ tools (grid_overview, daily_briefing_overview)
   •            = NO BindingCapabilityTools, NO refusal tool  → structurally cannot select a capability
   • prompt    = the Action's systemPrompt (call the tools, narrate a cited summary + recommendation)
   • model     = ADR-016 Reasoning tier, temp ~0.2–0.3 (advisory)
   • grounding = every fact cited to a tool result; no fabrication (advisory-mode MUST)
   ▼
streams the grounded narration  →  emits surface_launch (opens the Tasks tab, once)
```

The nested turn reuses the existing agent loop + `AgentToolCatalogProjector` + `AgentToolProjection` — it is **not** a new engine. The only genuinely new element is the small runner that builds a tool-**scoped** turn for an already-selected advisory Action.

## 3. Why this is ADR-039-compliant (the load-bearing argument)

ADR-039's "exactly ONE probabilistic decider" is about **dispatch** — *which capability runs* (the audit's evil was ten intent-detection surfaces selecting capabilities). The nested advisory turn:

- **Selects no capability.** Its mounted tool set is ONLY grounded READ tools from the Action's declared allow-list — zero `BindingCapabilityTool`/refusal tools. It structurally cannot dispatch. It is a **grounded reasoning executor**, not a dispatch decider.
- **Narrows deterministically.** The allow-list is applied *after* the Action is already chosen by binding id (deterministic, exactly like the Click path). No utterance inspection selects the tool set — the Action's catalog data does. This is the ADR-039-sanctioned **deterministic pre-filter (context scoping)**, keyed off a structural fact (the resolved Action), not a classifier.
- **Stays grounded.** Advisory mode (2026-07-25 amendment) already permits reasoning/synthesis/recommendation *provided* every fact is source-cited and every recommendation is traceable to grounded material. The nested turn inherits all other invariants (budget/ADR-016, the one confirmation gate on side effects, ledger store-before-render, eval coverage).

**ADR Tensions entry (add to `spec.md`)** — Path A (project-scoped exception), ADR-039:
> The advisory grounded-reasoning capability runs a nested bounded turn scoped by its per-Action `GroundedToolAllowList` to grounded READ tools only. This adds **no** dispatch decider and **no** intent-detection: the nested turn selects no capability, its tool set is deterministic catalog data applied to an already-selected Action, and every fact stays cited. The one probabilistic **dispatch** decider remains the top-level Text-path turn.

If `adr-check`/`code-review` rejects the nested function-calling loop as a "second probabilistic surface," the **fallback (Option A′)** is deterministic tool orchestration: the runner calls both allow-listed tools in code, then runs ONE advisory completion (no tool-calling) that reasons/narrates over the assembled grounded results. Same user value, zero nested loop; the allow-list becomes the runner's call manifest. (Preferred primary is the nested turn because it generalizes to future advisory capabilities that reason about *which* of their allow-listed tools to use — the fuller "AI-enabled" behavior the owner asked for.)

## 3a. Implementation seam refinement (2026-08-16, during 011 build)

While implementing, the end-to-end code trace confirmed the natural task seam falls **between the deterministic projection primitive and the nested-turn machinery** — matching the pre-existing 010/011 boundary recorded in `current-task.md` ("011 = thread the allow-list into `AgentToolFilterContext` + the deterministic `AgentToolProjection.PreFilter` narrowing predicate"). So the §4 list is split across 011/012 at that seam (directional-mode adaptation; Option A is delivered in full across 011+012 — only the internal cut moves, and the note's §5 already anticipated it: "define the enum/flag in 011, consume in 012"):

- **Task 011 (DONE)** = the deterministic **projection primitive** — items 1, 2, 5 below. `AgentToolFilterContext.AdvisoryToolAllowList` (null-inert structural fact) + `AgentToolProjection.PreFilter` narrowing (drop `BindingCapabilityTool` + `RefusalCapabilityTool`, keep only allow-listed grounded handler tools when non-null) + unit tests. Pure, deterministic, fully unit-tested in isolation; satisfies **all six** of 011's acceptance criteria. This is the ADR-039 boundary artifact 011 is chartered to deliver.
- **Task 012** = the **nested-turn machinery** — items 3, 4 below (`AdvisoryCapabilityRunner` + `SessionDispatchOrchestrator` routing + threading `OutputDeterminism`/`GroundedToolAllowList` into the dispatch-path Action resolution) **plus** authoring the advisory `list-tasks` Action (§5). The runner is only meaningful — and only integration-testable — with a real advisory Action to run, so it lands with its consumer. This mirrors exactly how **010** shipped `GroundedToolAllowList` as inert catalog DATA for 011 to consume: 010 (data) → 011 (projection primitive) → 012 (runner that sets `AdvisoryToolAllowList` on the filter context + runs the nested turn).

Consumer chain for the 011 primitive: the task-012 runner reads the resolved Action's `advisory` + non-empty `GroundedToolAllowList`, constructs an `AgentToolFilterContext` with `AdvisoryToolAllowList` set, and runs the nested bounded turn — at which point the 011 `PreFilter` narrowing fires. Until 012 wires that setter the field is inert (grep-verifiable zero producers), exactly as 010's field was inert until 011.

## 4. Task 011 re-scope — what to build

1. **`AgentToolFilterContext`** (`AgentToolProjection.cs`): add optional `IReadOnlyCollection<string>? AdvisoryToolAllowList = null` (structural fact; mirrors the `OpenTabContextTypes` null-inert convention → every existing construction site is byte-identical).
2. **`AgentToolProjection.PreFilter`**: when `AdvisoryToolAllowList` is **non-null**, keep ONLY handler tools (`ToolHandlerToAIFunctionAdapter`) whose tool-id/name ∈ the allow-list, and **drop all `BindingCapabilityTool` + `RefusalCapabilityTool`** (enforces "no capability-selection in the nested turn"). Null (every non-advisory turn) = inert, unchanged behavior. Pure predicate; no utterance inspection.
3. **`AdvisoryCapabilityRunner`** (new, small): given a resolved advisory Action (+ its allow-list + systemPrompt + reasoning-tier/temp) and the session context, build a nested scoped agent turn (reuse `SprkChatAgentFactory` machinery via an advisory-scoped overload/param that threads `AdvisoryToolAllowList` and the systemPrompt override, and skips the Binding/refusal projection), run it, stream the narration, then emit the `surface_launch`.
4. **`SessionDispatchOrchestrator`**: when a dispatched Action is advisory + non-empty allow-list, route to `AdvisoryCapabilityRunner` instead of the linear `ActionRunner` completion. All other dispatches unchanged.
5. **Unit tests** (`tests/unit/Sprk.Bff.Api.Tests/`): (a) PreFilter with a non-null allow-list mounts ONLY the allow-listed handler tools and drops capability/refusal tools; (b) PreFilter with null allow-list is byte-identical to today; (c) an advisory Action routes to the nested runner; a non-advisory Action does not; (d) negative: no `BindingCapabilityTool` survives an advisory turn (no second decider).

**Determinism carrier**: `AnalysisAction` already has `GroundedToolAllowList` (task 010) and must expose `OutputDeterminism` to the chat/dispatch path (confirm it is materialized there; the chat path currently reads advisory only on the linear side — thread it through the dispatch resolution).

## 5. Task 012 re-scope — coupling

- `list-tasks` stops being `actionType:0` ack-only. Author it as the advisory tool-calling capability: `output_determinism: advisory`, ADR-016 Reasoning tier, temp ~0.2–0.3, `GroundedToolAllowList = {spaarke.grid_overview, spaarke.daily_briefing_overview}`, and a `systemPrompt` that instructs: call both grounded tools (grid_overview with the My-Tasks configId + OBO `today`; daily_briefing_overview), narrate a grounded summary (counts + top items, EACH cited) + a recommendation, then open the Tasks surface. No fabrication, no duplicate tab.
- Binding keeps the `surface_launch` disposition (opens the grid) + adds `chipTransitions` for the E2 follow-on cards.
- The Action's execution kind must signal "advisory tool-calling" (not the linear `AiAnalysis` executor) so `SessionDispatchOrchestrator` routes it to `AdvisoryCapabilityRunner`. Exact `actionType`/disposition value TBD during 011 impl (define the enum/flag in 011, consume in 012).

## 6. What the user sees (acceptance north-star)

Today: *"Opening your open tasks."* (thin ack). After: a grounded, cited summary + a prioritized recommendation ("clear the overdue Miller response first, then the two due today, depo prep before the NDA") + the Tasks tab opens once. Every count/name/date cited to a tool result; the prioritization is the LLM's reasoning. Grounding boundary (no invented tasks, no out-of-allow-list tools) is the feature that keeps it *logical and accurate*.

## 7. Obligations / guardrails carried in

- **ADR-039 fidelity (NFR-03)** — the §3 argument; verified at Step 9.5 adr-check.
- **BFF §10** — measure compressed publish per BFF task; ≤60 MB; baseline 44.96 MB.
- **Eval (FR-10)** — E1 eval cases land in task 013 (golden utterances: grounded summary present, every number cited, no fabrication, Tasks opens once, no second-decider surface).
- **Coordination** — `/conflict-check` before the 011/012 PR (compose-r5/r6 + assistant-r3 share the Chat projection files; no open PR overlaps them today).
