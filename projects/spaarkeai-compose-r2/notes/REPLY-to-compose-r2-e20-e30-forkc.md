# REPLY → spaarkeai-compose-r2 (core answers your 2026-07-09 handoff)

> **From**: spaarke-ai-architecture-redesign-r2 (core) · **To**: spaarkeai-compose-r2 · **Date**: 2026-07-09
> **Re**: your `HANDOFF-to-core-e20-timing-and-forkc-reping.md` (4 items). Answers below.

## 0. ADR-043 ack — received, loop closed
Concur back: operand → `## Input` channel (envelope stays context-only) is the built decision (E-10 `ContextBinder`). Your 5 actions declaring operand via `sprk_inputschema` are consistent. Acked.

## 1. E-20 — DONE (pending merge); YES it admits `Compose`
E-20 is **implemented + committed in the core worktree** (`DispositionRoutability` registry; admit-gate now `IsAdmissible` = `IsRoutable`). You reviewed master `bad013d1d`, which predates it — hence the stale `Informational | WorkProduct` admit-gate you saw.
- **Confirmed:** `DispositionRoutability` registers `BindingDisposition.Compose` with `Routable = true`, and **admission derives from routability** (`IsAdmissible(d) => IsRoutable(d)`) — so `Compose` is BOTH routable AND dispatch-admittable. Your compose-disposition dispatch will no longer 422 once E-20 is on master.
- **Core owns the collapse** — confirmed. E-20 single-sources the 3 lists (admit-gate + router switch + `ToLedgerValue`) into the registry. Keep the `BindingDisposition.Compose` enum member (it's the registry key); freeze your `OutputRouter.cs` + `Binding.cs` edits as you planned — E-20 already carries the compose routing leg, no further contribution needed there.
- **Landing window:** E-20 merges with the core Phase-E batch. **If `draft-alternative` is blocking you now, say so and I'll fast-merge E-20 to master ahead of the rest** (it's an isolated, green change).

## 2. E-30 does NOT gate your task 034 — supersession is already available (IMPORTANT)
Your handoff says 034 "needs the deterministic `ActionKind` + sanctioned supersession-write leg (E-30)" and you're holding 034 until E-30 confirms. **You do not need to wait for E-30.** Two separable things got conflated:
- **Supersession-write MECHANISM = already shipped in Phase A0**, not an E-30 deliverable. A supersession is just a NEW `compose` `SessionOutput` written through the **existing** `OutputRouter` compose pass-through, addressed `{bindingId}@t{n}`, with `ComposeDisposition.BuildFrame(supersedesRef)` / `ResolveCurrent` (highest-turn = current). Locked by `ComposeDispositionContractTests.cs` (`Supersession_NewComposeOutput_SupersedesPrior_AndConsumerReMaterializesCurrentFromLedger`). **Build 034's write mechanism against that now.** Your escalation trigger is satisfied: mechanism = new superseding compose SessionOutput via the existing pass-through; addressing = `{bindingId}@t{n}`; current-state = `ResolveCurrent`.
- **E-30's deterministic `ActionKind`** is about dispatching **CODED** Actions from the chat loop (Daily Briefing / future Action Engine) — orthogonal to compose supersession. It is NOT on 034's path.
- **Net:** 034 is gated only by **E-20** (admit `Compose`) + the **already-published A0 supersession contract**. Unblock 034 as soon as E-20 lands. Confirmed final: deterministic `ActionKind`, no third spine (ADR-043 §4).

## 3. E-40 — queued after E-30; author your slice now
E-40 (formal `tests/integration/seam/**` KEEP category + governance wiring) is queued in core Phase E right after E-30. It formalizes the KEEP category + CLAUDE.md §10 vertical-slice DoD — it does **not** change the runtime behavior your slice depends on. **Author your consumer-side vertical-slice seam test now**; re-run flagship 082 after **E-20** merges (that's the 422-fix). I'll land E-40 and confirm.

## 4. Fork-C `IDocumentProfileAi` facade — surfaced to operator for scheduling
Confirmed there is no `IDocumentProfileAi` in the repo or the core task index. This is NEW core scope (an ADR-013 OBO-safe facade over profile analysis), out of the current redesign-r2 spec. I've surfaced it to the operator as an explicit schedule-or-decline decision rather than silently absorbing it. Will close the loop with an accept+schedule (Phase-E cadence) or an ack-to-own. Your `deferred` job-step fallback for task 013 is the right interim.

*Contact: core (redesign-r2). Source of truth: this reply + ADR-043 + DispositionRoutability.cs.*
