# HANDOFF RESPONSE → spaarkeai-compose-r2: AI Execution Foundation — core ACCEPTS + confirms B1–B6

**From**: spaarke-ai-architecture-redesign-r2 (platform core) · **To**: spaarkeai-compose-r2 (Ralph Schroeder) · **Date**: 2026-07-09
**Re**: your `HANDOFF-to-redesign-r2-ai-execution-foundation.md` + the platform assessment.
**Status**: Core has **accepted ownership** and will fix the foundation **fully in redesign-r2** (operator directive: "we don't have an AI solution if this isn't fully addressed"). Full plan: core's `ai-execution-foundation-remediation-plan.md` (new **Phase E**).

---

## 1. Your assessment — confirmed

We independently verified all three findings against master (not just accepted the narrative): the triplicated disposition lists (admit-gate `SessionDispatchOrchestrator:224` + router switch + `ToLedgerValue`), the input-poor `ActionRunner` (`RunAsync(action, DocumentText, …)`), the two-completion-engine asymmetry (`AiCompletionNodeExecutor` has `inputBinding`→`runtimeInput`; `ActionRunner` doesn't), the unbuilt `ContextBinder` (task 053), and the stubbed side-effect router legs. **Your governance point is the sharpest and we've adopted it**: "done" meant *contract shape exists*, not *vertical slice works* — that's exactly how 016/042 shipped green while 422-broken. The vertical-slice KEEP test becomes a definition-of-done (see B6).

## 2. ONE material difference from your proposal — read this, it affects how you build B2

You recommended **"NOT unify the two engines (unify = R8+)"** and a **min-`runtimeInput`-now / envelope-later** sequence for input resolution. **Core is going further**: per operator direction, we are **converging the two *completion* engines** (`ActionRunner` + `AiCompletionNodeExecutor`) onto **one input-resolution model — `ContextBinder`/`ContextEnvelope`** — in r2. (We are NOT touching the agent-loop tool-handler spine — that split stays; only the two redundant completion engines converge.)

**Why this matters for you**: there is **no throwaway `runtimeInput`-only shape to build against and later migrate off**. B2 ships as the **`ContextEnvelope` path directly** — `ContextBinder` resolves your action inputs into envelope slices, and `PromptSchemaRenderer` consumes them via `## Input`. **Build your 5 actions against the `ContextEnvelope` input contract (task 015, frozen), not a transitional arg-forwarding shape.** We will ship the Binder incrementally (selection/document slices first — your load-bearing case), but the *contract you consume is the envelope from day one*. This removes the migration boundary you offered to accept — you won't need it.

## 3. B1–B6 — confirmation + foundation/consumer ownership

| Req | Confirmed? | Owner | Notes |
|---|---|---|---|
| **B1** compose disposition dispatchable end-to-end (Click + Text/loop) | ✅ | **CORE** | Delivered by Move 2 (single-source `DispositionRoutability` — admit follows "router can route it", killing the drift that half-landed your promotion). Your `BindingDisposition.Compose` + `ToLedgerValue` + `OutputRouter` case are accepted + on master. |
| **B2** selection/open-document input on the canonical seam | ✅ | **CORE** (you = forcing consumer) | Move 1. Ships as the **`ContextEnvelope`** contract (see §2), not `runtimeInput`-throwaway. Your 5 args-text actions are the first non-file inputs — you validate the seam. |
| **B3** deterministic action-kind / sanctioned supersession-write (undo) | ✅ | **CORE** | Move 3 + ADR-043. Admits a deterministic `ActionKind` through `SessionDispatchOrchestrator:209` + a supersession-write leg (retraction = superseding empty compose output, no LLM). Unblocks your FR-17. |
| **B4** supersession semantics through dispatch (ADR-040 highest-turn-wins) | ✅ | **CORE** (write path) / you (client re-materialize, already shipped) | Falls out of B1–B3; your `usePendingRedline` (task 033) already re-materializes. |
| **B5** ConsumerTypes registration + health parity | ✅ | **CORE** (changed from your handoff) | We're taking this as a **boot-reconciliation invariant** (E-42), not compose work — it's a platform health property, not consumer-specific. ~10 lines; lands before/with the catalog deploy. You don't own it. |
| **B6** vertical-slice acceptance bar | ✅ | **CORE** defines the category (`tests/integration/seam/**`) as a KEEP path + definition-of-done; **you** add the consumer-side slice on top | This is the process fix that prevents recurrence. Adopted platform-wide. |

## 4. Sequencing (so you unblock in the right order)

Core Phase E order: **E-00 ADR-043 (spine boundary + deterministic-kind decision)** → **E-10/E-11 (ContextBinder + ActionRunner wired = B2)** → **E-20 (single-source disposition = B1)** → **E-30 (deterministic-kind + supersession = B3)** → **E-40 (vertical-slice KEEP = B6)** → **E-42 (ConsumerTypes = B5)**. Then core's memory wave consumes the fixed foundation.

Your immediate unblock (draft→pending→accept/reject→undo/replace + the 4 read-only actions) lands at **E-11 + E-20 + E-30**. "Replace" needs nothing special once B1/B2 land (re-dispatch of draft-alternative).

## 5. Answers to your four asks
1. **B1–B6 confirmed** with the ownership table above (two deltas: B5 → core; B2 → envelope not runtimeInput).
2. **Sequencing**: no min-vs-target split — B2 is the envelope from day one; we ship Binder slices incrementally but the contract is stable. **Build against `ContextEnvelope`.**
3. **Move 3** decided in ADR-043: deterministic `ActionKind` + sanctioned supersession-write on the declarative spine (your "document-mutation class" idea is folded in as the capability *taxonomy*, realized via the deterministic kind rather than a third spine). We'll share ADR-043 draft for your review before it's Accepted.
4. **Owner + intake**: core owns the shared execution engine (named in ADR-043 + CLAUDE.md §10); deferral re-parenting rule added so cross-cutting slices can't re-orphan.

## 6. What we need from you (confirm back)
- **Ack the B5 → core and B2 → envelope-not-runtimeInput deltas** (so you don't build a transitional input shape).
- **Confirm you'll build the 5 actions against `ContextEnvelope`** (task 015 frozen contract) as their input, with core shipping Binder slices for selection/document/changes first.
- **Stay the forcing consumer** for E-11 validation — your args-text actions are the first real test of the seam.
- **Review the ADR-043 draft** when core circulates it (it decides B3/Move 3, which gates your undo).
- Everything else in your "what compose commits" list stands and is appreciated (consume-as-published, zero endpoints, consumer-side vertical-slice test, owner-hygiene).

*Contact: redesign-r2 core. Source of truth: `ai-execution-foundation-remediation-plan.md` (Phase E).*
