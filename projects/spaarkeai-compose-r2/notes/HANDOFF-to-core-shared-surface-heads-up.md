# HANDOFF → core (redesign-r2): shared-surface heads-up before the master merges collide

> **From**: spaarkeai-compose-r2 (`work/spaarkeai-compose-r2` @ `ad5d0aed3`, 20 ahead of master, 0 behind).
> **To**: spaarke-ai-architecture-redesign-r2 (core / "rebuild-r2").
> **Date**: 2026-07-10.
> **Why**: Phase-9 E2E remediation (compose tasks 100/102/104/101/103) is complete on our branch. In closing it we **extended three surfaces that are core's territory or shared**. Core is running ahead **in its own branch** but has **not landed on master since our shared merge-base `c300ab12d` (E-20, 2026-07-09)**. So neither branch can see the other's newest via master yet — and when both merge, whoever goes second eats the conflict. This note is so that collision is planned, not discovered.

---

## TL;DR recommendation

- **Do NOT cherry-pick compose→core branch-to-branch.** Two active branches pulling from each other tangles history and double-resolves the same conflicts. **Master is the single integration channel.**
- **Order the two master merges core-first** — foundation before consumer, the same pattern that worked for E-20. Core lands its foundation on master; compose-r2 then merges master in and adapts on *our* side (correct dependency direction — core never has to understand compose wiring).
- **One decision for core**: the `StoredSession` contract extension below is *your* foundation surface. Please decide whether to **absorb/bless it into the foundation now** (then we drop our copy on merge — cleanest) or just sequence the merge.

---

## The three shared touchpoints

### 1. Session-store contract — `StoredSession.cs` + `ChatSessionManager.cs`  ⚠️ highest-stakes
- **What we changed (task 102)**: added `AnchoredAnnotations` + `DefinedTermsTracking` fields to `StoredSession` and mapped them **both directions** (persist + restore) in `ChatSessionManager`'s Cosmos warm-tier map/unmap. Action history rides the already-persisted `Outputs` ledger (no new field). All additive.
- **Why it matters to core**: this is the session-store contract — **core's foundation**. If core is evolving `StoredSession`/`ChatSessionManager` in parallel (likely), our additive fields will conflict, and a persistence-contract conflict is high-stakes (data survives across the Redis TTL via these fields).
- **Ask**: absorb these two collections into the foundation `StoredSession` (own the contract), or tell us the merge order and we resolve on our side.

### 2. Dispatch test infrastructure — `DispatchSessionEndpointTestFixture` + `StubOpenAiClient`
- **What we changed (task 101)**: additive `LastPrompt` capture on the shared `StubOpenAiClient`; reused `DispatchSessionEndpointTestFixture` as-is for the new compose `/dispatch` contract test (`ComposeDispatchEndpointContractTests`).
- **Why it matters to core**: this is core's dispatch test infra. If core reshapes the fixture or the stub, the additive `LastPrompt` and our new test file may conflict. Low-severity but flagging.

### 3. Shared event bus — `Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`
- **What we changed (task 104)**: added **6 typed `compose_*` discriminants** to the bus channel unions (additive; zero `as any`; no channels added; existing `compose_selection_offer`/`compose_assistant_insert` runtime contracts untouched).
- **Why it matters to core**: shared bus contract in `Spaarke.AI.Widgets`. If core touches the same unions, additive-merge should be clean but worth knowing.

---

## Concrete conflict-risk inventory (for whoever merges second)

| File | compose-r2 change | Likely core overlap | Severity |
|---|---|---|---|
| `Services/Ai/Sessions/StoredSession.cs` | +2 annotation collection fields | HIGH if core evolves the session store | **high** |
| `Services/Ai/Chat/ChatSessionManager.cs` | +map/unmap for those fields | HIGH if core evolves warm-tier mapping | **high** |
| `Api/ComposeEndpoints.cs` | Load query-binding + annotations routes + subscription origin call | compose-owned; low core overlap | low |
| dispatch test fixture / `StubOpenAiClient` | additive `LastPrompt` | MED if core reshapes dispatch test infra | med |
| `Spaarke.AI.Widgets/.../PaneEventTypes.ts` | +6 typed compose_* discriminants | LOW (additive union) | low |

Everything else compose-r2 touched (compose widgets, SpaarkeAi panes, `ComposeToolbar`, `EntityCreationService` client, `ui-components/services/index.ts` cleanGuid barrel) is compose-feature or already-on-master surface — negligible core overlap. (Note: the cleanGuid barrel export is already on master via `0c4c85d39` and auto-reconciled when we merged master in.)

---

## What compose-r2 is doing next (so core can time its master landing)
- Staging a **Tier-1 sandbox UAT** of the getting-started vertical from our branch (does NOT require a master merge; see `UAT-tier1-getting-started.md`).
- **Holding our master merge** until Tier-1 UAT is green **and** we've heard core's preference on the `StoredSession` contract (item 1). If core wants foundation-first, land it and we'll rebase onto it.

**Contact point**: reply in this project's notes or via the compose-r2 owner. No action needed from core except the item-1 decision + a rough master-landing timeline.
