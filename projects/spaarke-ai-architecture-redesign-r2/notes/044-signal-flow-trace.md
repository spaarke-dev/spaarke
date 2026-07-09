# Task 044 — Signal-Flow Trace: wiring `ConfirmationPolicyEngine` into the live core gate

> **Status**: **ESCALATION — task blocked at Step 0 (real-signal trace) per its own `<escalation>` trigger.**
> **Author**: task-execute (opus @ xhigh), 2026-07-09
> **Purpose**: Deliverable #2 of task 044. Documents the REAL source of every `PolicyEvaluationContext` input the live gate would need to feed `ConfirmationPolicyEngine.Evaluate`. One input — **origin** — has **no producer anywhere in `src/`**, which is the escalation the task pre-authorized ("do NOT ship an always-Inferred default and call it done").

---

## 0. Where the gate is invoked (the call-site map)

- **Wrap-site**: `SprkChatAgentFactory.CreateAgentAsync` lines 673–682 — every projected typed-handler tool whose `sprk_analysistool` row declares `write`/`communicate` (`PendingPlanManager.RequiresConfirmation`) is wrapped in `SideEffectGateAIFunction`.
- **Live decision**: `SideEffectGateAIFunction.InvokeCoreAsync` (`SideEffectGateAIFunction.cs:158`) — fires when the LLM invokes that wrapped tool **inside the agent-turn loop** (`ChatEndpoints` POST `/sessions/{id}/messages` → `agent.SendMessageAsync`). Today it decides **suspend-vs-execute purely by declared class** (the wrap-site `RequiresConfirmation` filter) and **always suspends** every write/communicate into `PendingPlanManager.SuspendInvocationAsync`.
- **Structural fact about this call-site**: it is reached **only on the text path**. Click-path invocations (`invoke(bindingId, args)`) go through `SessionDispatchOrchestrator` / `BindingCapabilityTool`, **never** through `SideEffectGateAIFunction`. So the *one* origin the classifier can label `Explicit` "by construction" — `GateRequestSource.Click` — **cannot occur here**.

---

## 1. Per-input REAL-source trace

| Engine input (`PolicyEvaluationContext`) | REAL source reachable from the call-site? | Where it comes from |
|---|---|---|
| **Risk tier** (`RiskProfile` → `RiskTierResolver.Resolve`) | ✅ **REAL & wireable** | `adapter.Tool.Configuration` (the `sprk_configuration` JSON on `AnalysisTool`, `IScopeResolverService.cs:772`) → `GateRiskProfile.FromConfiguration(...)` → `RiskTierResolver.Resolve`. Catalog DATA (ADR-039). The `adapter` (`ToolHandlerToAIFunctionAdapter`) is the inner function of the gate and exposes `.Tool`. |
| **Completeness** (`ArgsComplete`) | ✅ **REAL & wireable** | The tool's declared JSON schema `required[]` (`adapter.Tool.JsonSchema`) vs the LLM-supplied `AIFunctionArguments` keys at `InvokeCoreAsync`; or `adapter.ValidateForGate(arguments)` (already called for the 034 pre-suspend check). |
| **Overlay: `DispatchUncertain`** | ⚠️ **Structurally N/A here (honestly false)** | The loop-as-dispatcher has no dispatcher-confidence signal for a direct typed-handler tool call (the LLM simply calls the tool). `false` is the honest value, not a stub — but it is never `true` on this path, so E-6 cannot be exercised at this gate. |
| **Overlay: `ContentSafetyFlagged` / `SafetyPerimeterDegraded`** | ⚠️ **Signal EXISTS but is NOT threaded here** | `SafetyPipelineMiddleware` / `AgentContentSafetyMiddleware` compute shield-degradation + content-safety per turn, but the result is **not** propagated to `SideEffectGateAIFunction`. Threading it is additional (bounded) work; **not** the blocker. |
| **Ledger status** (`Ledger` + `GateId` → `GetCurrentGateStatus`) | ✅ **REAL & wireable** | `ChatSessionManager.GetSessionAsync(...).Gates` (resolvable from the gate's fresh scope, same as `PendingPlanManager`). ADR-040 append-only ledger. |
| **ORIGIN** (`Origin` → `RequestOriginClassifier.Classify`) | ❌ **NO REAL PRODUCER ANYWHERE IN `src/`** | See §2. This is the blocker. |

---

## 2. Origin has no producer — the blocker (evidence)

`RequestOriginClassifier.Classify(GateOriginRequest)` is fail-closed: it returns `Explicit` **only** via one of five structural facts, and returns `Inferred` for everything else. Each Explicit-producing fact was traced to its would-be producer:

| Classifier branch → `Explicit` | Required structural fact | Producer in `src/`? |
|---|---|---|
| `Source == Click` | Click-path invocation reaches this gate | ❌ **Impossible** — Clicks route through `SessionDispatchOrchestrator`, not `SideEffectGateAIFunction`. |
| **E-4** `UtteranceEnumeratesCapability` | "the user's utterance named THIS capability's action verb + its enumerated invocation" — a structural fact from *the dispatch's enumeration* | ❌ **None.** In the loop-as-dispatcher, the LLM's tool selection *is* the enumeration, but distinguishing user-driven from model-added/doc-injected (E-3) requires either a **banned runtime NLP re-parse** (ADR-039 "the model NEVER decides its own request's origin") or a dispatch-enumeration signal that **is not produced for typed handlers**. |
| **E-1** bare-affirmation → `PrecedingProposal{count:1, complete:true}` | The immediately-preceding model turn's proposal, recorded structurally | ❌ **None.** No ledger entry records model proposals. `SessionGate`, `SessionOutput`, `SessionToolChain`, `SessionWidgetEvent`, `SessionContextFingerprint` — none carry a proposal/concrete-action-count. `ElicitationTurnRouter.ClassifyBareAffirmation` gives the *affirmation* half deterministically, but the *proposal* half has no data. |
| **E-5** elicitation-answer → `OriginalRequestOrigin` | The original request's origin, recorded on its Gate ledger entry | ❌ **None.** `SessionGate` (`SessionLedgerEntries.cs:269`) has **no `origin` field**. There is nothing to inherit. |
| **E-2** model-initiated → `PriorExplicitForSameCapabilityArgs` | A prior Explicit classification for the same `(capability, args)` + a "user-turn-since" flag | ❌ **None.** Nothing records prior origin or the `(capability,args)` identity / user-turn-since flag. |

**Repo-wide confirmation**: `grep` for `GateOriginRequest | PolicyEvaluationContext | ConfirmationPolicyEngine | RequestOriginClassifier | GateProposalContext` across all of `src/` returns **only the definition files themselves** (`Services/Ai/Chat/Gate/*`) plus doc-comment mentions in `SideEffectGateAIFunction` / `PendingPlanManager` / `ElicitationTurnRouter`. **Zero live producers. Zero call-sites of `.Evaluate`.** This matches ADR-041's own "Known open item" (`docs/adr/ADR-041-…md:94–96`): the engine "currently has 0 core production call-sites."

**The engine's unit test proves the gap by construction**: `ConfirmationPolicyEngineTests` builds `GateOriginRequest { Source = UserUtterance, UtteranceEnumeratesCapability = true }` **by hand** (`ConfirmationPolicyEngineTests.cs:48–52`). The Explicit signal exists **only** as a hand-set test literal — exactly the faked origin the task says the live integration tests must *not* rely on.

---

## 3. Consequence — why honest wiring cannot satisfy the acceptance criteria

Fed only the signals that are real, `RequestOriginClassifier.Classify` returns **`Inferred` for 100 %** of invocations at this gate (no Click, no proposal record, no ledger origin, no enumeration signal, and re-deriving one is ADR-039-banned). The engine would therefore project:

- Tier 2a/2b → **`ConfirmDialog`** (never `ExecuteWithUndo`) — **identical** to today's always-suspend floor for the write/communicate tools this gate wraps.
- Tier 3/4 → `ConfirmDialog` (same as today).
- Incomplete args → `Elicit` (the one new behavior — but the typed-handler gate has no elicitation surface; elicitation lives on the Binding `capture_mode` path).

So the headline acceptance criterion — **"explicit + complete Tier-2b executes with NO dialog," proven on the real gate path** (POML Step 4 test (i) / acceptance criterion 3) — **cannot be produced by any real signal.** The only way to make that test pass would be to **hardcode/default origin to `Explicit`**, which the operator mandate and the task's `no-shim` constraint explicitly forbid. Per the task's `<escalation>` trigger, the correct action is to **STOP and escalate**, not ship an always-Inferred (or hardcoded-Explicit) default.

---

## 4. What the real origin producers would require (scope for the escalation)

To make an `Explicit` origin observable end-to-end at this gate, ONE of these new producers must exist (all beyond "wire the already-built engine into the existing gate"):

1. **Turn-provenance envelope threading** — carry a structural `GateRequestSource` from the request boundary (`ChatEndpoints`) through `CreateAgentAsync` into the gate. For the text path this still only distinguishes `UserUtterance` vs `ModelInitiated`; it does **not** by itself yield E-4 Explicit.
2. **A deterministic dispatch-enumeration signal** for typed-handler tools (which capability the *user's utterance* enumerated) — without a banned model re-parse this needs a structured selection record the loop does not emit today.
3. **Proposal recording in the ADR-040 ledger** (a new `SessionProposal`-shaped entry with concrete-action-count + args-complete) to make **E-1** deterministic, **plus** a `GateRequestOrigin` field on `SessionGate` to make **E-5/E-2** inheritance real.
4. **A frontend/Click marker** on the chat message envelope (out of this task's scope; a client change) so a user-confirmed proposal arrives as a structural Click.

Any of 1–4 is a distinct build with its own design + tests — not a call-site rewire.

---

## 5. Recommendation

Resolve via **CLAUDE.md §6.5** (see the task report). ADR-041's own known-open-item enumerates the three legitimate resolutions: *accept-as-Compose-consumed-seam / add a wire-up (producer) task / documented deferral*. Recommended: **split** — (a) a small task that wires the **REAL** inputs (tier + completeness + ledger + overlay threading) so the engine becomes the single decider with a **fail-closed `Inferred`** floor (behavior-preserving; removes the "two deciders" risk), and (b) a **producer task** (item 3 above: proposal-ledger entry + `SessionGate.Origin` field + turn-provenance threading) that makes `Explicit` real and lets the FR-A1-03 browser-UAT "explicit executes with no dialog" become observable. Item (b) is the actual content behind the task's headline; it was not in the WBS.
