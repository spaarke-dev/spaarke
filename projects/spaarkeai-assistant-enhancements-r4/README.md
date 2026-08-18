# SpaarkeAI Assistant Enhancements R4

> **From deterministic launcher to grounded proactive assistant** — close the gap between what the Assistant *promises* and what it *delivers*, and build the feedback loop that lets it improve.

## What this delivers

R4 introduces a **grounded-recommend capability tier** — authored on the *existing* single agent-turn decider using ADR-039's `advisory` output mode + a per-Action bounded grounded-tool allow-list (mirroring `sprk_allowsknowledge`; enforced by the sanctioned deterministic pre-filter, **no new dispatcher**). It powers:

- **E1** — a **task-agenda capability**: "what do I need to do today" → a *grounded* summary + recommendation from already-shipped tools (`spaarke.grid_overview` My-Tasks + `spaarke.daily_briefing_overview`, both OBO, both cited), then opens Tasks. (Fixes P1.)
- **E2** — **capability-backed follow-ons** (no dead-end promises) + the OBO-identity wording fix + Briefing/Smart-To-Do follow-on cards. (Fixes P2.)
- **E3** — the **feedback→memory loop**: a `preference` fact type + a governed narrow-allow-list preference-producer so standing directives ("always summarize my tasks") bias behavior within injection-defense bounds. (Fixes P3.)
- **E4** — a client-only flex-chain fix for the "Open in Compose" viewport clip (D9). (Fixes P4.)

**Central thesis**: separate *grounded facts* (always tool-grounded, never fabricated) from *free recommendations* (the LLM may reason, chain grounded tools, prioritize, and proactively guide) — the closed catalog + fact-grounding stay non-negotiable; reasoning latitude over grounded results is restored.

## Owner decisions (2026-08-13)

- **Build approach** → reuse the existing single decider (advisory mode + pre-filter bounded tools); **no new executor**.
- **Preference steering** → narrow closed allow-list → pre-turn tool hints only (never grants a capability or alters a fact).
- **Agenda surfaces** → Tasks only + inline grounded summary + Briefing/Smart-To-Do follow-on cards *if not already open*.
- **Operator queue** → **out of system scope** (CX/product-owner exercise).
- **E3 memory ownership** → **redesign-r2 is closed; all work contained in R4.**
- **Advisory tier** → ADR-016 Reasoning tier, temp ~0.2–0.3.

## Status

- **Phase**: Tasks generated — **execution owner-gated (not auto-started)**.
- **Created**: 2026-08-13.
- **Predecessor**: spaarkeai-assistant-enhancements-r3 (shipped + deployed to dev 2026-08-11).

## Graduation criteria

1. **P1 DoD** — "what do I need to do today" → grounded, cited summary + recommendation + Tasks opens; no thin ack, no fabricated data, no duplicate tab.
2. **Advisory fidelity** — the capability mounts only its allow-listed grounded tools; no classifier/second dispatch surface added.
3. **P2 DoD** — no follow-on promises an unwired action; "Help me prioritize my tasks" works or is absent; no flow asks for the user's id.
4. **Follow-on cards** — Briefing/Smart To Do cards appear only when their tab is closed and open the right surface.
5. **P3 loop** — an explicit "do this every time" directive persists as a governed `preference` item and biases the FR-01 capability next turn; off-allow-list directives have no tool-selection effect.
6. **P4/D9 DoD** — handoff §6 checklist passes (modal + full-page + widget + resize + long/empty, light+dark).
7. **BFF hygiene** — publish ≤60 MB, no new HIGH CVE.

## Key files

- [`spec.md`](spec.md) — AI-optimized spec (12 FRs / 9 NFRs / 3 ADR tensions).
- [`design.md`](design.md) — the R4 design seed (grounded proactive assistant).
- [`plan.md`](plan.md) — WBS + parallel groups + hot-path coordination.
- [`CLAUDE.md`](CLAUDE.md) — AI context (load first).
- [`current-task.md`](current-task.md) — active task state.
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + dependencies + parallel groups.
- [`notes/assistant-viewport-clipping-open-in-compose-handoff.md`](notes/assistant-viewport-clipping-open-in-compose-handoff.md) — D9 diagnosis recipe + fix pattern.
- [`notes/behavior-gap-register.md`](notes/behavior-gap-register.md) — the standing behavior-gap register (P5; seeds P1–P4).

## ⚠️ Coordination

BFF=Y, SpaarkeAi=Y. `/conflict-check` before every BFF / `ConversationPane` / `SprkChat` PR. Live overlap remains with **compose-r5/r6** (ConversationPane/SprkChat — D9) and **assistant-r3** (SprkChatAgentFactory/AgentToolProjection). The memory files (`Services/Ai/Memory`, `ContextBinder`) have **no live contender** now that redesign-r2 is closed — R4 owns them. Keep the reactive card surface distinct from the ADR-047 spine. Publish ≤60 MB (baseline ~49.63 MB incl. PDBs).
