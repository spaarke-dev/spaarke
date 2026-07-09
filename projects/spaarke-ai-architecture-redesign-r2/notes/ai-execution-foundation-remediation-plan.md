# AI Execution Foundation — Remediation Plan (DRAFT for operator review)

> **Author**: redesign-r2 core · **Date**: 2026-07-09 · **Status**: DRAFT — awaiting operator review
> **Inputs**: compose-r2 handoff (`HANDOFF-to-redesign-r2-ai-execution-foundation.md`) + independent code verification (this doc) + operator directive ("we don't have an AI solution if this isn't fully addressed… fix it fully and completely in r2").
> **Bottom line**: the finding is real and verified. The fix is bounded and belongs in r2. The one decision that sets the scope is **converge the two completion engines vs. keep the incremental straddle** — I recommend converge (§2), which matches the operator's instinct and, done right, is not the R8-scale unification compose feared.

---

## 1. What I verified (code, not narrative)

All three compose findings check out against the code on master:

| Finding | Verified at | Verdict |
|---|---|---|
| Disposition capability is **triplicated + drift-prone** | admit-gate `SessionDispatchOrchestrator.cs:224` (`Informational or WorkProduct`) + `OutputRouter` switch + `ToLedgerValue` — 3 hand-maintained lists; comment admits it's a manual pre-run mirror | CONFIRMED — this is why the compose promotion half-landed (422) |
| Canonical engine is **input-poorest** | `IActionRunner.RunAsync(action, DocumentText, …)` — takes literal document text, errors if empty; `PromptSchemaRenderer.runtimeInput` (`## Input`) exists but the dispatch path never feeds it | CONFIRMED |
| **Two completion engines, asymmetric** | `AiCompletionNodeExecutor` (Nodes/) resolves `inputBinding` → `runtimeInput`; `ActionRunner` (LinearConsumers/) does not. The "canonical" one is the weaker one | CONFIRMED — the inversion is real |
| Envelope/Binder **designed but unbuilt** | `ContextEnvelope` v1 frozen (task 015); `ContextBinder` = task 053, **not-started**, consumed by nothing on the hot path | CONFIRMED |
| Side effects live on a **parallel spine** | record/email/notification/overlay `OutputRouter` legs throw NotImplemented; real side effects execute via agent-loop tool handlers (`side_effect_class`) | CONFIRMED |

**Governance root cause (compose's sharpest point, and correct):** "done" was defined as *contract shape exists*, not *vertical slice works*. That's exactly how tasks 016/042 shipped "green" while 422-broken, and how the gate-wiring + routing-promotion both orphaned. The process fix (a vertical-slice KEEP test as definition-of-done) is as important as the code fix.

## 2. THE decision that sets scope — converge vs. straddle

Compose recommends: Move 1 (input resolution, **min `runtimeInput` / target envelope**), Move 2 (single-source disposition), Move 3 (deterministic/interactive capability home) — and explicitly says **"NOT unify the two engines (ADR-039 keeps them split; unify = R8+)."**

**I partially disagree, and I think the operator's instinct ("no reason to straddle two engine models") is right — with one precision.** There are actually **three** execution surfaces, not two, and they are not equal:

1. **Agent-loop tool-handler spine** (`side_effect_class`, gated) — the LLM calling tools mid-turn. This is genuinely a distinct concern (interactive reasoning-loop tool use). **KEEP separate.** This is the "split" ADR-039 legitimately protects.
2. **`ActionRunner`** (linear dispatch) — prompt → LLM → structured output, input-poor.
3. **`AiCompletionNodeExecutor`** (playbook nodes) — prompt → LLM → structured output, input-rich.

**#2 and #3 are the same primitive** ("resolve declared input → render prompt → LLM → structured output") built twice with asymmetric capability. *That* is the pointless straddle — not #1. Compose's "min `runtimeInput` then maybe migrate to envelope later" **continues** the straddle (two input paths, a migration boundary, a shape that changes under them). The operator is right: there's no reason for two completion engines.

**My recommendation (the precise version of "fix it fully"):**
- **Converge the two completion engines (#2 + #3) onto ONE input-resolution model** — `ContextBinder` becomes THE input resolver; both dispatch Actions and playbook nodes consume it; `runtimeInput`/`## Input` is the single rendering seam. Do this **in r2**, not R8. This is bounded (it's a shared-abstraction extraction, not a rewrite) if we build the Binder as the seam and migrate both consumers behind their existing interfaces.
- **Keep the agent-loop tool spine (#1) separate** — but ensure it shares the disposition→ledger→OutcomeCard convergence layer (already true post-task-035).
- **Realize the stubbed disposition legs** so the declarative spine can actually route side effects (record/email/notification/overlay/compose) — a maker declares a capability without hand-writing a tool handler.

Net: "fully fixed" = **the declarative contract is fully realized by ONE completion engine + ONE disposition registry + a home for deterministic/interactive capabilities**, governed by a vertical-slice bar. It is NOT "merge the agent loop into the dispatch engine" (that genuinely is out of scope and unnecessary). This distinction is the thing to ratify before we plan tasks.

## 3. Remediation plan — new Phase E: AI Execution Foundation

Sequenced so compose unblocks early, but the target state ships in r2 (no deferred straddle). Foundation = core; consumer = compose.

### Move 0 — the boundary ADR (gates everything; author FIRST)
- **E-00 · ADR-043 "AI Capability Execution Spine"** (core, opus/fable): codify the three surfaces (§2), the convergence rule (completion engines share ContextBinder; all spines converge at disposition→ledger→OutcomeCard), where deterministic/interactive capabilities live (Move 3 decision — my lean: a **deterministic ActionKind** admitted through the declarative seam + a sanctioned supersession-write, NOT a third spine), and the **named engine owner + intake**. Proposed→Accepted at the Phase-E gate. *Answers compose's ask #3 + #4.*

### Move 1 — one input-resolution model (B2, the load-bearing one)
- **E-10 · Build `ContextBinder` (re-scope task 053)** as THE input resolver: Action's declared inputs (`selectionText`/`documentText`/`changesText`/`fileIds`/`ledger_resolution`) → `ContextEnvelope` slices. Realizes ADR-040 "reads by ledger_resolution reference." Also becomes the writer for task-038's dark `AppendContextFingerprintAsync`.
- **E-11 · Wire `ActionRunner` to the Binder** — relax the no-file hard stop (`ActionRunner.cs:78`); resolve inputs via ContextBinder → `runtimeInput`/`## Input`. **This is compose B2.** Ship the `runtimeInput` path and the envelope path as ONE path (Binder produces the envelope; renderer consumes runtimeInput) — no min-vs-target straddle.
- **E-12 · Migrate `AiCompletionNodeExecutor` onto the shared Binder/renderer seam** behind its existing interface (playbook features — daily-briefing, insights — must not regress; vertical-slice tests are the safety net). This is the convergence (§2).

### Move 2 — single-source disposition (B1)
- **E-20 · One `DispositionRoutability` registry** — admit-gate derives from "can OutputRouter route this?"; delete the 3 hand-maintained lists (`SessionDispatchOrchestrator:224`, router switch source-of-truth, `ToLedgerValue`). Kills the drift class.
- **E-21 · Realize the stubbed `OutputRouter` legs** needed in r2 (record/email/notification/overlay as scoped; compose already built). Decide per-leg: declarative-spine-routed vs. agent-loop-only. *email stays draft+handoff per the r2 confirmation-model decision — no auto-send.*

### Move 3 — deterministic/interactive capability home (B3)
- **E-30 · Admit a deterministic ActionKind through the seam** (`SessionDispatchOrchestrator:209`) + a sanctioned supersession-write leg (retraction = superseding empty compose output, no LLM). Per the E-00 ADR. **Unblocks compose FR-17 undo.**

### Governance (prevents recurrence — ship with the phase)
- **E-40 · Vertical-slice KEEP test category** `tests/integration/seam/**` (consumer → dispatch → stored `SessionOutput` → render/frame) — added to ADR-038 KEEP paths + CLAUDE.md §10 as a definition-of-done for any dispatch/execution change. **This is compose B6 and the single highest-leverage fix.**
- **E-41 · Deferral re-parenting rule + named engine owner** in CLAUDE.md §10/§11 — deferred cross-cutting slices file against an owning task; the execution engine has a named owner + intake (from E-00).
- **E-42 · ConsumerTypes registration + health parity** (compose B5) — ~10 lines; core owns it as foundation (it's a boot-reconciliation invariant, not compose-specific). Lands before/with the catalog deploy.

## 4. Sequencing vs. the rest of r2

- **This phase does NOT block the G-R2-A close** (deploy + operator UAT) — judgment is orthogonal. That can proceed in parallel.
- **This phase SHOULD precede / absorb part of the memory wave.** `memory.write` (task 057) is itself a side-effecting disposition — it wants the single-sourced disposition registry (E-20) + the deterministic-capability decision (E-30). Building 057 before E-20/E-30 would add a *fourth* hand-maintained disposition path. So: **E-00 → E-10/E-11 → E-20 → E-30/E-40 first**, then the memory wave consumes the fixed foundation.
- Rough size: ~9 foundation tasks (Phase E) + re-scope of 053. This is a real phase — a week-plus — but it is the difference between "shipped r2" and "shipped a functioning AI system," per the operator directive.

## 5. What compose-r2 (and others) must do

**Compose-r2** (they've already committed to most of this):
- Consume the foundation **as published** — no local variants, zero new AI dispatch endpoints, dispatch only through the shipped seam (their charter §3.4 + ADR-039 §7.2). Confirmed in their handoff.
- **Be the forcing consumer for E-11/E-12** (B2) — their 5 args-text actions are the first real non-file inputs; they validate the Binder seam.
- Re-scope their chain to consumers of core's leg (016/046/034/047); ship the **consumer-side vertical-slice test** on top of E-40.
- Keep owner-hygiene (no version suffix in action codes / Binding names / mirror filenames).
- **Open item to confirm with them:** B5 (ConsumerTypes) — I propose **core owns it** (E-42) as a boot-invariant, not compose. Confirm.

**Other projects / blast-radius check:**
- **daily-briefing-r5 + insights** ride `AiCompletionNodeExecutor` (the input-rich engine) — E-12's migration must not regress them; they are the regression-safety consumers for the convergence. Coordinate the E-12 migration window with any active daily-briefing/insights work via `projects/INDEX.md` + `/conflict-check`.
- Any project authoring new Action+Binding rows should pause net-new dispatch-path capabilities until E-20 (single-source disposition) lands, to avoid adding to the drift.

## 6. Open decisions for operator review

1. **Ratify §2**: converge the two *completion* engines (E-10/E-11/E-12) in r2, keep the agent-loop tool spine separate. (My rec: yes — it's your "no reason to straddle," made precise. Compose's min-`runtimeInput`-then-migrate is the straddle continuing.)
2. **Move 3 direction** (E-00/E-30): deterministic ActionKind + sanctioned supersession-write on the declarative spine (my lean) vs. compose's "distinct document-mutation capability class." These may converge; the ADR decides.
3. **Scope confirmation**: Phase E (~9 tasks) inserted **before the memory wave**, G-R2-A close proceeds in parallel. OK?
4. **Foundation-vs-compose ownership**: confirm B5 (ConsumerTypes) → core; all of B1/B2/B3/B6 → core foundation; consumer vertical-slice test → compose.
5. **ADR count**: this adds ADR-043 (execution spine). r2 now authors ADR-041 (judgment), ADR-042 (memory), ADR-043 (execution). Acceptable?
