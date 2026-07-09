# ACK → redesign-r2 (core): execution-foundation response — confirmed, with ONE operand-home question before I build B2

> **From**: spaarkeai-compose-r2 (Ralph Schroeder) · **To**: spaarke-ai-architecture-redesign-r2 (platform core) · **Date**: 2026-07-09
> **Re**: your `HANDOFF-response-to-compose-r2-execution-foundation.md` (core accepts ownership + confirms B1–B6, Phase E).
> **TL;DR**: All confirmed and appreciated — especially converging the two completion engines (that's better than my min-now/migrate-later proposal). **One concrete gap I hit doing the due-diligence you asked for as forcing consumer**: `ContextEnvelope` v1 has no home for a capability's *primary input/operand*, and the operand is *volatile*. Please resolve that (§3) before I build the 5 actions against it — otherwise "build against the envelope" has an undefined seam for `selectionText`.

---

## 1. Deltas acknowledged

- **B5 → core** (ConsumerTypes registration as a boot-reconciliation invariant, E-42) — ack, thank you; agreed it's a platform health property, not consumer work.
- **B2 → `ContextEnvelope`, not a `runtimeInput` throwaway** — ack. I will NOT build a transitional arg-forwarding shape. Converging `ActionRunner` + `AiCompletionNodeExecutor` onto one input-resolution model is the right call and removes the migration boundary I'd offered to accept.
- **Move 3 in ADR-043** (deterministic `ActionKind` + supersession-write on the declarative spine; document-mutation folded in as taxonomy, not a third spine) — ack; that cleanly unblocks FR-17 undo. I'll review the ADR-043 draft when you circulate it.
- **Forcing consumer for E-11** — confirmed. My 5 args-text actions are the first non-file inputs on the seam; I'll validate them against the envelope path.

## 2. Confirmed: I will build the 5 actions against `ContextEnvelope` (task 015)

…for their **context** (host record / matter, ledger tail, memory) — subject to the §3 resolution below for their **operand**. This is the ultimate-capability win: `compose-draft-alternative` grounded in Business (matter/playbook schema) + Memory (prior turns + governed memory) is exactly the envelope's value. Consume-as-published, no local variants — stands.

## 3. THE gap to resolve before I build — `ContextEnvelope` has no operand slice, and the operand is volatile

I read the frozen `ContextEnvelope` v1 (`Services/Ai/PublicContracts/ContextEnvelope.cs`). Its six slices are **all grounding context**:

| Slice | Carries | Stability |
|---|---|---|
| User | current-turn *message* + caller contact + prefs | StablePrefix |
| Workspace | environment facts (clock) + host context | StablePrefix |
| Business | host-record identity + Dataverse schema card | StablePrefix |
| Memory | ledger tail + memory-item **references** | VolatileTail |
| Organizational / Semantic | provider interface — empty in r2 | — |

**None is a home for a capability's primary input — the text the action operates *on*.** Compose's load-bearing inputs are operands, not context:

| Action | Operand | Is it "context"? |
|---|---|---|
| explain-clause / compare-to-playbook / draft-alternative | `selectionText` (the selected clause) | No — it's the subject of the operation |
| summarize-word-changes | `changesText` (tracked-change set) | No |
| defined-terms | `documentText` (open-document text) | No |

Two concrete problems, both structural, not cosmetic:

1. **No slice maps.** `selectionText` is not the user's message (User), not environment facts (Workspace), not record identity/schema (Business), not a ledger/memory reference (Memory). The `## Input` section in `PromptSchemaRenderer` — the natural render target for an operand — is fed by `runtimeInput` (a separate param), and **no envelope slice feeds `## Input`**. So "resolve action inputs into envelope slices, consumed via `## Input`" has no wire today.
2. **Stability collides.** The operand is **volatile** — it changes on every action invocation. User/Workspace/Business are `StablePrefix` (byte-stable across turns, the NFR-04 prompt-cache invariant). Putting `selectionText` in a stable slice would break that invariant; it needs a *volatile / per-turn input* home, distinct from both the stable context prefix and the ledger tail.

**What I need from core (either is fine — your contract call):**
- **(a)** Add an additive **primary-input / operand slice** to `ContextEnvelope` (v1.1, additive-only per your tolerant-reader rule) — a volatile, per-turn slice that carries the action's `sprk_inputschema`-typed input and renders as `## Input`. Cleanest; keeps "one input model = the envelope" literally true. Compose's `selectionText`/`changesText`/`documentText` become that slice.
- **(b)** Keep the operand as a first-class **per-turn input channel** *alongside* the envelope (envelope = context; input = the typed args → `## Input`), and say so explicitly — in which case "build against the envelope" means *context via envelope + operand via the input channel*, and the `runtimeInput` mechanism isn't a throwaway but the sanctioned operand path.

I recommend **(a)** — it makes the envelope the single input contract you intend, and the volatile slice is a natural sibling to the volatile Memory tail. But it's your seam to own; I just need the operand's home pinned so the 5 actions bind to a real shape rather than a `## Input` with no envelope source.

## 4. Sequencing ack + one dependency note

Your Phase E order works for me: E-00 (ADR-043) → E-10/E-11 (Binder+ActionRunner = B2) → E-20 (single-source disposition = B1) → E-30 (deterministic-kind + supersession = B3) → E-40 (vertical-slice KEEP) → E-42 (ConsumerTypes). My flagship unblock at **E-11 + E-20 + E-30**, confirmed.
- **Dependency note**: the §3 operand-home decision gates **E-10/E-11** for me specifically — the Binder can't resolve compose's inputs into the envelope until the operand slice/channel exists. Please fold §3 into E-00/ADR-043 so E-11 lands buildable for the forcing consumer.

## 5. What compose does now (while core drafts Phase E)
- Hold compose code (016/046/034/047 re-scoped as consumers of the fixed foundation) — no building against an unsettled input shape.
- Already on master + correct as authored: 042 rows, 045 eval, 033 client redline (`usePendingRedline` re-materializes from current ledger state — B4 client half done), 061 ledger query.
- Ready to: review ADR-043, bind the 5 actions to the envelope operand contract the moment §3 is decided, and add the consumer-side vertical-slice test on E-40's KEEP category.

*Contact: Ralph Schroeder. Source of truth: platform assessment + `HANDOFF-to-redesign-r2-ai-execution-foundation.md` + this ack.*
