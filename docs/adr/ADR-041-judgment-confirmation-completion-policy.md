# ADR-041: Judgment, Confirmation & Completion Policy

- **Status**: **Proposed** (2026-07-09, authored at spec time of `spaarke-ai-architecture-redesign-r2`). **Promotion condition**: moves to **Accepted** when gate **G-R2-A** passes (task 049 operator browser UAT on spaarkedev1). Do NOT mark Accepted before that gate. Mirrors the ADR-039/040 promotion-gate convention.
- **Deciders**: Operator + `spaarke-ai-architecture-redesign-r2` design review (2026-07-08 design v0.4)
- **Concise version**: [`.claude/adr/ADR-041-judgment-confirmation-completion-policy.md`](../../.claude/adr/ADR-041-judgment-confirmation-completion-policy.md) (the operational MUST/MUST-NOT surface — binding)
- **Builds on**: ADR-039 (grounded execution, closed catalogs, ONE dispatch protocol) · ADR-040 (session ledger — the confirmation-state + completion-output carrier)

## Context

ADR-039 established that every AI output is grounded (a capability output, a cited tool chain, a confirmation, or a refusal) and that a **bounded agent turn** is the only probabilistic decider. ADR-040 gave the platform an append-only, addressable, typed **session ledger**. What neither ADR governed was the **judgment layer that sits on top of dispatch**: how the agent should behave when it *could* act but isn't certain (resourcefulness), when it *must* pause for human confirmation (friction), and how it reports that an action *actually completed* (truthful completion).

R1 steered all three with prompt text. That is exactly the failure mode ADR-039 warns against — behavior that lives only in a directive drifts:

- **Resourcefulness (D-F0)**: R1 had no doctrine for "verify before you act, degrade gracefully, never dead-end." The model would either over-refuse (asking permission for free reads) or fabricate (claiming a step ran when it did not).
- **Confirmation friction (D-F1)**: the R3-1 confirm-loop bug happened because "confirm once then execute" lived only in a directive, so the model re-asked. Blanket declared-class gating (R1) over-confirmed low-risk writes and under-confirmed context-sensitive ones.
- **Completion truthfulness (D-F2)**: capability outputs were streamed and forgotten. A "created the record" claim could render before (or without) the write; a document-create card could show "done" while indexing was still queued.

ADR-041 codifies the three doctrines as **binding platform policy** so the implementing tasks (030 D-F0; 032/033/034 D-F1; 035/036 D-F2) have a citable authority rather than a directive. It is principle-level: it does not re-implement those tasks, it states the invariants they must satisfy.

## Decision

The platform adopts a three-part judgment policy. Each part is a doctrine with binding MUST/MUST-NOT rules (full surface in the concise version).

### D-F0 — Resourcefulness Doctrine (the preamble)

The agent is **resourceful before it is deferential**. Given a task it should: **decompose** it, **inventory** what it already has (ledger, context envelope, catalogs), **verify before acting** (reads are free — D-F0(b)), **act or approximate** with the best grounded option, and **deliver partial value plus an explicit next step** rather than refusing outright. The ladder is: **verify → act → degrade → refuse-with-affordance** — a refusal is a last resort and always carries a concrete affordance (e.g. a deep link to the surface that *can* do the thing).

The load-bearing invariant is the **read/write safety asymmetry**: reads and searches are always free and never gated (a resourceful agent explores freely); **writes and side effects are deterministic and gated**. Therefore **the degradation ladder operates entirely BELOW the side-effect line** — degrading, approximating, or "trying harder" may never weaken a confirmation gate or a hard block. An agent that cannot complete a write does not lower the bar to complete it; it degrades the *response* (partial value + affordance), not the *authorization*.

### D-F1 — Confirmation Policy v2 (friction)

Confirmation is a **deterministic gate-engine policy over (risk-tier × request-origin × argument-completeness)**, replacing R1's blanket declared-class gating. Two invariants frame it:

1. **Risk classification is catalog-declared DATA, never runtime LLM judgment.** The sub-tier and its risk factors (reversibility, external visibility, deadline impact, confidentiality/privilege impact, record-of-truth impact) are declared properties on the catalog row — the ADR-039 `side_effect_class` pattern, extended. A runtime model-judged risk classification would be the second intent mechanism ADR-039 bans.
2. **Origin classification is deterministic and fail-closed.** Undecidable ⇒ `inferred` ⇒ confirm. The model never decides its own request's origin.

**The risk-tier table (catalog-declared):**

| Tier | Class | Explicit + complete | Otherwise |
|---|---|---|---|
| 0 | Read / search / explain | Execute (always — D-F0(b)) | Execute |
| 1 | Draft-only, no system mutation | Execute | Execute |
| 2a | Private/internal **reversible** create | Execute + ✅ card with Undo chip | Confirm |
| 2b | Matter-scoped system-of-record create/update | Execute (Undo chip) | Confirm — ONE dialog |
| 2c | Document creation / versioning | Preview/confirm (r2 minimum; revisit post-G-R2-A) | Confirm |
| 3 | Legal-operational risk (deadline, obligation, assignment to another user, client/matter status) | Always dialog | Always dialog |
| 4 | External / irreversible (email SEND, filing, delete/supersede) | Always dialog | Always dialog |

**Overlay precedence (strict top-to-bottom; first that fires decides, before the tier row is consulted):**

1. **Injection-suspect always wins** — `dispatchUncertain`, content-safety flags, or untrusted-doc origin ⇒ dialog + suspicion surfaced, regardless of tier/origin.
2. **Safety-perimeter degradation** — when PromptShield fails open (timeout/429/5xx), the turn's gated **writes degrade to confirm-required**; **reads stay fail-open** (D-F0(b)).
3. **Incomplete args** ⇒ ONE elicitation turn, then re-evaluate from the top.
4. **Origin** (deterministic classifier — Click ⇒ explicit; document/tool-result ⇒ inferred; utterance naming the enumerated action+invocation ⇒ explicit; else inferred).
5. **Tier row** — the catalog-declared class decides behavior given origin + completeness.

**The six ruled edge cases (E-1..E-6)** — binding rows:

| ID | Edge case | Ruling |
|---|---|---|
| E-1 | Bare affirmation ("go ahead") after a model proposal | Explicit **IFF** the immediately-preceding model turn proposed exactly ONE concrete action with complete args; else inferred. The gate ledger binds the affirmation to the proposal. |
| E-2 | Explicitness across intervening turns | Survives model-only intermediate turns for the SAME (capability, args); **any intervening USER turn resets** it. |
| E-3 | Origin vs injection | Layered, never merged. The origin classifier **never reads document-derived content as a user utterance**; the injection overlay runs after and can override an `explicit` result. |
| E-4 | One utterance, N side effects | Explicit for the enumerated set only; model-added extras are inferred and gate on their own. |
| E-5 | Elicitation answer origin | Inherits the original request's origin from the gate ledger; does not re-classify. |
| E-6 | `dispatchUncertain` on an explicit request | Suspicion wins ⇒ dialog, even on an otherwise-explicit complete request (overlay 1). |

**Confirmation-state invariant (ADR-040):** confirmation state is a **Gate-ledger property** — a second ask for the same request is **structurally impossible** (this is what kills the R3-1 loop; it is not enforced by prompt text). A doomed call is validated **before** suspending into a dialog (gate pre-suspend validation) so the user sees an honest refusal-with-affordance rather than Confirm → ❌.

### D-F2 — Completion Policy (truthful completion)

Every side-effect path renders its outcome as a structured **OutcomeCard** (ADR-040 `Output` disposition), never as ad-hoc prose. The card is composed **after** the ledger write (store-before-render, ADR-040) from a stored `SessionOutput` key — so a completion claim cannot precede or exist without the stored evidence.

- **Single disposition surface**: outcome composition rides the one universal disposition surface (the output router) plus the gate-resume path — no second rendering mechanism (ADR-039).
- **Job-aware completion**: for asynchronous/job-backed work, status is **derived from the job aggregate**, and the ONLY path to `Succeeded` is a fully-completed aggregate. A record created but not yet indexed renders `Partial`, never `Succeeded` (a bare document row may not claim done).
- **UI-action truthfulness**: a claim that a UI action happened (e.g. "opened the tab") is confirmed by a client ack, not asserted on the server write; absent the ack within a bounded window, the tool fails honestly.

## Consequences

- The implementing tasks cite ADR-041 rather than carrying behavior in directives. Prompt directives may *express* the doctrine for the model, but the *enforcement* is in code (the gate engine, the ledger, the completion composer) — directives are not the source of truth.
- Risk tiers and their factors become catalog-authoring obligations (through the triple-twin hoist), not code changes — new capabilities declare their tier as DATA.
- The doctrine is testable: the origin-classification eval family (E-1..E-6) and the resourcefulness eval family join the golden-utterance suite as merge gates.

## Alternatives considered (rejected)

- **Keep steering with prompt text** — rejected: this is the drift ADR-039 exists to prevent; R3-1 and the fabrication cases are the evidence.
- **Runtime model-judged risk** — rejected: it is a second intent-detection mechanism (ADR-039 violation) and non-deterministic where determinism is a safety property.
- **A separate completion state store** — rejected: violates ADR-040 (no second session-state store); the ledger already carries outputs.

## Promotion note

Authored **Proposed** at spec time. The **Accepted** flip is gated on **G-R2-A** (task 049 operator browser UAT, spaarkedev1). Evidence at promotion: the D-F0 doctrine directive + resourcefulness eval family (030/031), the Policy v2 gate engine + origin eval family + pre-suspend validation (032/033/034), and the Completion Engine + OutcomeCard + job-aware completion (035/036/037/038) all shipped and green, plus the G-R2-A UAT pass.

## Known open item at authoring time (2026-07-09)

The Policy v2 gate **engine** (task 032, `ConfirmationPolicyEngine`) is a published PRODUCER — consumed by Compose r2 and exercised by the origin eval family — but it currently has **0 core production call-sites**: the core's own live gate runs on the pre-existing suspend floor plus task-034 pre-suspend validation, and task 042 reused the existing gated path. Wiring the engine into the core's own live gate (at the Binding-dispatch/resume surface) is not yet assigned in the WBS. This ADR codifies the policy the engine *implements*; wiring it live in the core is a follow-up decision (accept-as-Compose-consumed-seam / add a wire-up task / documented deferral). This does not affect the doctrine's correctness or the Compose-consumed seam.
