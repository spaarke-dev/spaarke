# Task 044 — Signal-Flow Trace: `ConfirmationPolicyEngine` LIVE on the core gate

> **Status**: **DELIVERED** (opus @ xhigh, 2026-07-09). The engine is now the single top-level decider on the live core gate path, driven by REAL (tier × completeness × confidence) signals under the operator's 2026-07-09 reframe.
> **Supersedes**: the prior ESCALATION version of this note (the origin-provenance blocker), archived below in §7. The re-scope removed the E-1..E-6 origin-producer dependency from the happy path; NO E-1..E-6 producer, `SessionGate.Origin` field, proposal-ledger, or utterance re-parse was built.

---

## 0. The reframe (why the happy path is no longer blocked)

The prior attempt correctly escalated: making an **`Explicit` origin** observable at this gate has no production producer and can't be built ADR-039-cleanly. The operator reframed the problem (2026-07-09):

- The gate decides **RISK**, not authorization provenance.
- **WRONG-CHOICE risk** (an ambiguous instruction → wrong capability) is caught by **LAYER 1** — the agent turn asking a clarifying question BEFORE any tool call (ADR-039's sanctioned intent decider), not by the gate.
- The `origin`/`confidence` slot the engine consumes is fed by the real **(declared-risk-DATA × ambiguity)** confidence, NOT by parsed authorization provenance.

So the happy path rides entirely on **TIER + COMPLETENESS**, both already real. The engine is reused **as-is** (tier table + overlay precedence unchanged); only the gate call-site wiring is new.

---

## 1. Per-input REAL source (as wired at `SideEffectGateAIFunction.InvokeCoreAsync` → `EvaluatePolicy`)

| Engine input (`PolicyEvaluationContext`) | REAL source (delivered) | Fake-if-absent? |
|---|---|---|
| **Risk tier** (`RiskProfile` → `RiskTierResolver.Resolve`) | ✅ `adapter.Tool.Configuration` (raw `sprk_configuration` JSON string) → `JsonDocument.Parse` → `GateRiskProfile.FromConfiguration` → `RiskTierResolver`. Catalog DATA (task-020 seed; the email row already declares `riskProfile.tier=1`). **Absent/unparseable/invalid-tier ⇒ fail-closed** (conservative Tier-2b + Inferred ⇒ ConfirmDialog = today's safe floor). | No — an un-migrated write can never auto-execute. |
| **Completeness** (`ArgsComplete`) | ✅ declared JSON-schema `required[]` (from `adapter.Tool.JsonSchema`) vs the LLM-supplied `AIFunctionArguments` keys (present + non-empty). | No. |
| **Origin / confidence** (`Origin` → `RequestOriginClassifier.Classify`) | ✅ DERIVED, not a buried constant: `Source=UserUtterance`, `UtteranceEnumeratesCapability = (declaredProfile != null && !dispatchUncertain)`. A gate-reaching typed-handler call is a COMMITTED capability selection by the agent turn; genuine ambiguity is diverted by layer 1 upstream. So `Explicit` **iff** the row declares real execute-eligible risk DATA AND no low-confidence signal fired — else fail-closed `Inferred`. | No — a null profile forces `Inferred`; a fired uncertainty signal forces `Inferred`. Origin=Explicit is INSUFFICIENT alone: tier must independently be a declared 2a/2b for auto-execute. |
| **Overlay: `DispatchUncertain`** | ✅ HONORED from an injected `Func<bool>? dispatchUncertaintyProbe` on the gate. **Production wires `null` today** (no live low-confidence producer reaches this gate; layer 1 covers ambiguity) → honestly unfired. The plumbing is real: a real signal flips the outcome, proven by the integration test that injects `() => true` and observes a confirm. | No — the value flows from the probe; the anti-shim test FAILS if it were hardcoded false. |
| **Overlay: `ContentSafetyFlagged` / `SafetyPerimeterDegraded`** | ⚠️ `false` (honest). A Prompt-Shields BLOCKED turn `yield break`s upstream and never reaches the loop; a fail-OPEN degradation is not yet propagated here. The overlay plumbing already honors a real signal — threading a producer is the documented follow-on (bounded). | Honest false, never a positive-verdict fabrication. |
| **Ledger status** | First evaluation of a fresh invocation ⇒ `GateId=null` ⇒ `ConfirmationState=None` (ADR-040 "no second ask" applies on the resume path, not this first pass). | n/a |

**No escalation was warranted**: TIER and COMPLETENESS are both real and sourceable (the escalation trigger was scoped to "tier or completeness cannot be sourced" — they can). The `dispatchUncertain` backstop lacking a strong live producer is explicitly NOT an escalation condition (layer 1 covers ambiguity).

---

## 2. How the engine drives behavior (the branch, `SideEffectGateAIFunction`)

`InvokeCoreAsync`: fail-closed store resolution → **034 pre-suspend validation (KEPT, ahead of everything — R5-E honest ❌ + zero writes)** → `EvaluatePolicy` → switch on `GateDecisionV2.Outcome`:

- `Execute` / `ExecuteWithUndo` → `ExecuteInlineAsync`: runs `_inner.InvokeAsync` (same OBO turn as a read), **store-before-render** a `loop@t{n}` `SessionOutput` (ADR-040), compose the task-035 `OutcomeCard` via `CompletionEngine.ComposeForGateAutoExecute` (Undo chip for reversible 2a/2b; server-composed record link), emit an `action_outcome` SSE, and return a grounded honesty-framed turn text. **Email (Communicate) = DRAFT + HANDOFF**: the draft executes (Tier 1), the OutcomeCard + grounded text carry the `entityrecord` review-and-send deep link, and the text says **NOT SENT** — the system never sends (auto-send deferred past r2).
- `Elicit` → `RenderElicit`: names the missing required arg(s); NOT executed, NOT suspended; the agent asks one question then re-invokes.
- `ConfirmDialog` (and fail-closed default) → `SuspendForConfirmationAsync`: the **unchanged** task-037 suspend mechanism (pending marker before `action_confirmation` render, ADR-040).
- `HonestBlock` → `RenderHonestBlock`.

**Replace-not-shadow**: the old declared-class-only "always suspend" decision is gone — `EvaluatePolicy` → engine is the single top-level decider. The declared-class filter survives ONLY at the wrap-site (`SprkChatAgentFactory`) as the "is this tool gated at all" selector (unchanged), which is orthogonal to the outcome decision.

**Layer 1** (`SprkChatAgentFactory.SideEffectHonestyDirective`): a new clause instructs the agent to ask ONE clarifying question when genuinely torn between capabilities, and to invoke directly when the request is clear — no new steering mechanism (extends the single directive, ADR-039/§11).

---

## 3. Integration proof (real gate path, engine NOT mocked)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ConfirmationPolicyGateLiveDecisionTests.cs` — real `SideEffectGateAIFunction` over the real `ConfirmationPolicyEngine`/`RiskTierResolver`, real `PendingPlanManager` + `ChatSessionManager` over the in-memory cache, real adapter over a spy handler:

1. clear+complete Tier-2b create → executes, no `action_confirmation`, no pending marker, `action_outcome` with an **Undo** chip, one `loop@t{n}` output.
2. email (Communicate, Tier-1) → drafts, `action_outcome` link label "Review and send" + `etn=sprk_communication`, grounded text "NOT SENT", no dialog, no pending marker.
3. ambiguity (layer 1) → `SideEffectHonestyDirective` contains the "GENUINELY torn → clarifying question → do NOT dispatch a guess" clause (the mechanism; behavior is golden-utterance eval).
4. incomplete args → "NEEDS MORE INFORMATION", names the missing arg, no execute, no pending marker.
5. irreversible (2b escalated to Tier 4 via `RiskTierResolver`) → exactly one pending marker + one `action_confirmation`, no execute.
6a. no declared riskProfile → fail-closed confirm, no execute.
6b. **ANTI-SHIM**: the SAME Tier-2b tool as (1) but `dispatchUncertaintyProbe: () => true` → confirms instead of executing (proves the ambiguity signal is honored, not faked — this test fails if the signal were hardcoded).
7. R5-E hard block → honest ❌ + affordance verbatim, `validation-failed` marker, **zero** `loop@t{n}` outputs (zero writes).

Plus deterministic engine-outcome cases in the origin eval family suite (`OriginClassificationEvalSuiteTests`, joins the same `Category=GoldenUtteranceEval` merge gate): `PolicyV2_ConfidentCompleteTier2bCreate_ExecutesWithUndo_NoDialog`, `…EmailDraftTier1_Executes_NoDialog`, `…IncompleteArgsTier2b_Elicits`, `…IrreversibleRecordOfTruth_EscalatesToTier4_ConfirmDialog`.

---

## 4. Files changed

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SideEffectGateAIFunction.cs` — engine wiring + execute/elicit/confirm/honest-block branch; `dispatchUncertaintyProbe` ctor param; inline execute + `loop@t{n}` output + OutcomeCard + `action_outcome` SSE + record-link builder.
- `src/server/api/Sprk.Bff.Api/Services/Ai/CompletionEngine.cs` — `ComposeForGateAutoExecute` (Undo/handoff chips over the same store-before-render contract).
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — `ChatSseActionOutcomeData` SSE payload.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — layer-1 clarify-when-torn directive clause.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ConfirmationPolicyGateLiveDecisionTests.cs` (new) + `tests/integration/contract/Eval/OriginClassificationEvalSuiteTests.cs` (4 added facts).

---

## 5. Follow-ons (documented, not shims)

- Thread a real `dispatchUncertain` producer (routing candidate-match count / content-safety fail-open) so the backstop can fire in production. The gate already honors it; only the producer is deferred.
- Client render of the `action_outcome` SSE + Undo executor wiring (the `dataverse.delete_record` compensating capability) — Compose r2 consumes the `OutcomeCard` contract this gate now emits.

---

## 6. Constraints honored

034 pre-suspend validation (R5-E honest ❌, zero writes) preserved ahead of the decision · R5-E hard block preserved · Tier-0/1 free reads (D-F0(b)) · fail-closed default (unknown/uncertain → confirm) · engine reused as-is (no projector edits) · no E-1..E-6 producer / origin field / utterance re-parse built · email never auto-sent.

---

## 7. Archived escalation (prior attempt — for provenance)

The prior version of this note documented that `RequestOriginClassifier`'s `Explicit` verdict had no production producer (no Click path to this gate, no proposal ledger, no `SessionGate.Origin`, and deriving it from the utterance is ADR-039-banned), so honest wiring resolved 100% `Inferred` and the "explicit executes no-dialog" criterion could not be met without a hardcode. That analysis drove the operator's 2026-07-09 reframe (this note), which severs the origin slot from authorization provenance and rebinds it to (declared-risk × ambiguity) confidence — making the happy path ride on the already-real tier + completeness signals. The E-1..E-6 machinery remains in the codebase (exercised by the origin eval family on constructed inputs) but is NOT a producer dependency of the live gate.
