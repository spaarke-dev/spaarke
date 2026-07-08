# Spaarke AI Architecture Redesign R2 — Design Charter

> **Status**: DRAFT v0.3 — 2026-07-08, ready for `/design-to-spec` (v0.2 amended per two independent design assessments + operator ratification 2026-07-08 — see §0.2: contract-first rule §3.4, Policy v2 tier sub-split, job-aware Completion Engine, memory governance depth, ContextEnvelope budgets, triple-twin hoist re-sequenced, §12 Q3 CLOSED)
> **Authors**: Operator charter (2026-07-07, verbatim priorities below) + Claude Fable 5 drafting
> **Parent epic**: #421 SPAARKE AI
> **Builds on**: `spaarke-ai-architecture-redesign-r1` (P0–P3 shipped; ADR-039 + ADR-040 **Accepted** and binding)
> **R1 completion state at drafting time**: **G-P3 CLOSED after 6 UAT rounds** (rounds 1–3 + round-4 fix wave + round-5 addendum incl. R5-E hard-block wave + round-6 script — all findings fixed or dispositioned); **P4 closing**. Anything r1 P4 does NOT close lands in §10 (inherited backlog) here.
> **Authoritative companions**:
> - Operator-reviewed assessment addendum: [`spaarke-ai-architecture-redesign-assessment.md`](spaarke-ai-architecture-redesign-assessment.md) (memory / context / workspace-intelligence / provider architecture — this charter operationalizes it)
> - Lived friction evidence: [`../spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md`](../spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round1-findings.md) · [`round2`](../spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round2-findings.md) · [`round3`](../spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round3-findings.md) · [`round4 (+round-5/6 addenda)`](../spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round4-findings.md)
> - As-built ground truth: [`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](../../docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §8.1 (v0.5 — nine as-built deltas incl. `SideEffectGateAIFunction`, `TypedHandlerResumeExecutor`, honesty-directive layer, document-creation refusal policy)
> - Binding foundations: [`docs/adr/ADR-039`](../../docs/adr/ADR-039-grounded-execution-closed-catalogs.md) · [`docs/adr/ADR-040`](../../docs/adr/ADR-040-session-ledger.md)
> - Compose surface today: [`projects/spaarkeai-compose-r1/design.md`](../spaarkeai-compose-r1/design.md) (working design, 2026-06-29 — pre-dates r1; see §8) · [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md)
> - v0.3 review inputs (operator-commissioned, 2026-07-08): [`r2-design-assessment.md`](r2-design-assessment.md) (findings F-1..F-7 + tasks T-1..T-8) · [`spaarke-ai-r2-assessment-recommendations.md`](spaarke-ai-r2-assessment-recommendations.md) (Amendments 1–8 + contract sketches §8 + wave order §6) — dispositioned per the response ratified 2026-07-08; contract sketches are SEEDS for /design-to-spec, not accepted schemas

---

## 0. Operator charter (verbatim, 2026-07-07)

> "r1 provides basic architecture and structure, but is coarse grain and not refined; r2 needs to focus on three core areas: (1) reducing the friction between user expectations and AI process (e.g., why does the user have to confirm when it has asked the AI to do something? why is there no visual 'yes that's done'? why is there no link to the record that is created? etc.) (2) 'memory' depth/breadth and persistence; and (3) Compose — that is the #1 most important feature — being able to open/edit or draft/edit documents."

### 0.1 v0.2 operator direction (2026-07-07, ratified)

1. **Add the judgment layer FIRST** — a Resourcefulness Doctrine (D-F0): r1's anti-fabrication hardening installed caution that generalized into passivity; the fix is strategy-level steering (verify → act → degrade gracefully → refuse LAST, always with an affordance), not more scenario pins.
2. **Re-cut delivery as platform-core + satellites** — THIS project is the core (judgment + memory, sole owner of BFF AI-internal plumbing); Compose ships as its own parallel project (**Compose r2**, absorbing this charter's Area-3 decisions); Daily Briefing hallucination remediation is an immediate fix wave; Insights Engine Widget refurbish is a satellite after core Phase A.
3. Ground the plan against the industry (§2) — the r1 chassis matches Harvey/Legora/Wordsmith/M365 Copilot; r2's areas are exactly their differentiating layers.

### 0.2 v0.3 amendment round (2026-07-08, ratified)

Two independent design assessments were commissioned and dispositioned (header links). The operator ratified the reviewed recommendation set in full, including four rulings where the assessments conflicted with each other or the charter:

- **R-1 (closes §12 Q3)**: the Policy v2 auto-execute line is ruled on the **sub-tier split** (2a/2b/2c — see D-F1), not on coarse "Tier ≤2": 2a auto-executes when explicit+complete; 2b auto-executes ONLY explicit+complete, else confirms; 2c previews/confirms in r2; Tier 3+ always dialogs; injection-suspect always wins.
- **R-2 (ingestion parity ownership)**: the **platform invariant** ("a bare `sprk_document` row is never a successful document operation" — full ingestion parity required) is owned + enforced by the CORE (gate policy + JobAwareCompletionState); the ingestion **pipeline implementation** stays in Compose r2 as chartered (§8). Wave-8-in-core from the second assessment is rejected — it would recreate the scope overlap the v0.2 re-cut eliminated.
- **R-3 (wave order)**: D-F0 stays FIRST and runs **in parallel with** the contract phase (§3.4) — the doctrine is prompt+eval work with no contract dependency and needs the longest UAT iteration runway. Contracts gate the Completion/Memory/Binder waves, not D-F0.
- **R-4 (Compose fidelity boundary)**: real requirement, but it is **Compose r2 charter content** — routed into the §8 handoff package, not core scope.

Additional ratified refinements: contract-first is a rule with **walking-skeleton** delivery (§3.4), not a paper-only waterfall gate; risk factors are **catalog-declared data**, never runtime LLM judgment (D-F1); token budgets are **measured against r1's actual prompt assembly first**, then fixed (D-M2); assessment contract sketches guide /design-to-spec but embed decisions the spec must still make.

---

## 1. Problem statement

R1 delivered the platform contract: one dispatch protocol over two closed catalogs (ADR-039), a session ledger as the composition carrier (ADR-040), one confirmation gate, honest refusal, and a flagship one-conversation journey that survived **six rounds** of browser UAT. **What it did not deliver is a refined experience.** Every G-P3 finding was fixed as a point-fix, but each fix is a *primitive* of a subsystem r2 must build properly:

| R1 lived evidence (G-P3 rounds 1–6) | Point fix shipped | The subsystem it implies (r2) |
|---|---|---|
| Model fabricated "task created" with no tool call (H6); drafting/creating conflation (R2-B); fabricated UI actions (R2-D) | Directive-layer honesty pins + result-text reframes | Deterministic completion evidence — outcomes proven by events, not steered by prompts (§7.1) |
| Confirm → silence, no record, no error (R2-A); post-confirm outcome invisible to the model (R2-C); confirm-loop asking again (R3-1); post-confirm chat re-ask persisting into round 5 (R5-B ruling re-confirmation) | ✅/❌ transcript persistence; once-only + same-turn steering text | Confirmation Policy v2 + first-class Completion UX (§7.1) |
| No record links; model INVENTED a `/WebResources/...` URL when asked for one (R4-3); raw handler-instruction text leaked into the ✅ transcript (R4-6) | Server-composed `[Open record]` links; `UserSummary` audience split | Completion Engine / OutcomeCard — the shipped link+summary primitives generalized to EVERY side-effect path (§7.1 D-F2) |
| "What's the link?" → model searched for a document-text lookalike instead of the host record (H7) | Host-identity context line | Context Binder — Business Context slice (§7.2) |
| Model needed live-schema entity maps pasted into tool descriptions (G-P2); lookup-column queries failed and were relayed as "column doesn't exist" (R5-C); no clock — "tomorrow" resolved to 6/13/2024 (R5-A); no assignee/regarding mapping (R5-B) | Description enrichment; `_value` lookup rewrite; `## Current Date` directive; per-table write contracts | Dataverse metadata + environment facts as first-class context, assembled ONCE by the Binder (§7.2) |
| Follow-up turns re-grounded only via last-8 ledger outputs; "portfolio" questions extrapolated from a prior turn's result instead of querying fresh (R5-C note) | Cache-stable ledger tail in prompt | Memory Service — conversation scope as ONE of five scopes, with retrieval policy (§7.2) |
| Entity-ambiguous "create a record from this document" → model GUESSED `sprk_document` → bare fileless row breaking widgets (R5-E) | HARD handler block + clarify-first steering | VERIFY-before-act + clarify-don't-guess = the judgment layer (§7.1 D-F0) |
| Honest refusals with NO path forward — the R3-4/R5-E document block names the Document Upload wizard but gives the user nothing to click | — (operator finding 2026-07-07) | Refusals carry actionable affordances (§7.1 D-F0(d)) |
| ExecutionTraceWidget empty after tool turns (R5-D) | Client replay buffer (per-page-load only) | Decision traceability incl. server trace read surface (§7.1 D-F4 + §10) |
| "Add to documents" → promise loop → broken fileless create (R3-4 → R5-E) | Honest refusal → hard block | Document-creation capability at FULL ingestion parity — transfers to Compose r2 (§8) |
| Summary renders complete-at-once (H5) | Documented as expected | Progressive render keeping ledger-write-before-render (§7.1 D-F5) |
| One bad catalog row 400'd the entire loop (H1) | Projection-time validator + health dimension | Carried forward as the resilience posture — r2 additions must meet the same bar |

**The v0.2 diagnosis the six rounds add**: three of the six UAT rounds hardened *honesty* — and it worked; the assistant no longer fabricates. But the accreted caution **generalized into passivity**: it now refuses, hedges, or asks where a resourceful assistant would verify, act, approximate, or hand the user a working next step. R1 solved "never lie"; r2's first job is "always help" — without reopening "never lie". That is D-F0, and it is deliberately the FIRST work item (§7.1).

The operator's framing is exact: r1 is coarse-grained. Users asked the AI to do something and then had to confirm it again; when it WAS done there was no visible "yes, that's done" (now partially fixed — R4-3/R4-6 primitives); the assistant forgot who/where/what it was working on unless hand-fed; and the #1 daily-work surface — open/draft/edit documents with the AI — exists only as an openable, now pre-seedable, Compose tab.

The assessment addendum supplies the architectural answer: **user-perceived intelligence = memory + context + reasoning + workspace awareness**, not model choice. R2 builds that — on top of, never around, ADR-039/040.

---

## 2. Industry grounding

Architecture-parity check (July 2026 state of the leaders):

| Layer | Harvey | Legora | Wordsmith | M365 Copilot | Spaarke r1 (as-built) |
|---|---|---|---|---|---|
| Bounded agent loop over tools | ✅ | ✅ | ✅ | ✅ | ✅ budget-8 loop (ADR-039) |
| Declared/closed tool registry | ✅ | ✅ | ✅ | ✅ (plugins/connectors) | ✅ two closed catalogs |
| Retrieval + grounding, citations enforced | ✅ | ✅ | ✅ | ✅ | ✅ AI Search + citation enforcement + honest refusal |
| Config-driven capabilities (not code-per-feature) | ✅ playbooks | ✅ | ✅ | ✅ declarative agents | ✅ Action + Binding rows |
| Approval gates on side effects | ✅ | ✅ | ✅ | ✅ | ✅ ONE gate, declared classes |
| Eval harness as merge gate | ✅ | ✅ | ✅ | ✅ | ✅ 64-case golden-utterance CI gate |

**Same chassis.** Nobody's differentiation is the loop — it is the layers above it, and those are exactly r2's areas: **judgment/steering depth** (Copilot steers STRATEGY — verify, act, degrade gracefully — not scenario pins; §7.1 D-F0), **memory/context** (Copilot's personalization + Work IQ; §7.2), and the **document surface** (Harvey/Legora live where lawyers work — the document; → Compose r2, §8). R1 bought the table stakes; r2 buys the differentiators.

**One-line mission**: *Copilot's judgment and transparency wrapped around Spaarke's execution and governance.*

---

## 3. Delivery structure — platform core + satellites (v0.2 re-cut)

R2 is NOT one monolithic project. The operator-ratified cut:

### 3.1 The core — THIS project (`spaarke-ai-architecture-redesign-r2`)

**Scope: judgment (D-F0/F1/F2/F3/F4/F5) + memory (D-M1..M4) ONLY.** The core is the **sole owner of BFF AI-internal plumbing** (`Services/Ai/` internals: gate engine + Policy v2, Context Binder, Memory Service, Completion Engine, directive layer, catalog governance, the disposition-enum extension). No satellite modifies AI internals; satellites consume published seams.

**Seam contract the core publishes early** (Phase A, so Compose r2 runs from day one): the `compose` disposition member + its SSE frame shape, the OutcomeCard contract, ContextEnvelope workspace slice, ledger provenance fields (`{bindingId}@t{n}` source refs), and the Policy v2 risk-tier table.

### 3.2 The satellites

| Satellite | Vehicle | Relationship to the core |
|---|---|---|
| **Compose r2** — the operator's #1 feature | **Separate project**, already in `/design-to-spec` as its own project; runs in **parallel from day one** in its own worktree | **Absorbs this charter's Area-3 decisions** (D-C1..C5 + the full-ingestion-parity document-creation capability — handoff package in §8). Consumes core seams (disposition member, dispatch, ledger provenance, Policy v2 tiers, OutcomeCard). Carries its own browser-UAT gate — **the former G-R2-C moves to that project**. |
| **Daily Briefing hallucination remediation** | **Immediate FIX WAVE, not a project** — grounding/citation repair on the BRIEF-NARRATE prompts + collector. **v0.3 (AI-ARCHITECTURE assessment rec 4)**: the fix wave also installs the PERMANENT mechanism — `GroundednessCheckService` gains a **policy, not just a score**: below threshold X the rendered output carries a visible grounding warning; below Y the disposition routes to a review state instead of rendering as authoritative; low-groundedness events feed the eval suite as regression candidates. Annotation-without-consequence was how briefing hallucinations reached users despite the service existing. | Runs as enumerated fix-wave tasks at r2 entry (default: core Wave 0); no new architecture beyond the threshold→action policy; uses the r1 catalog-data + eval-case fix pattern proven across G-P3 |
| **Insights Engine Widget r1 refurbish** | Satellite project **after core Phase A** | Routing already on Bindings (r1 task 040); the widget surface remains; refurbish rendering/UX on the new seams (OutcomeCard, ContextEnvelope) once they exist |

### 3.3 Coordination

Standard parallel-worktree discipline: hot-path registry ([`projects/INDEX.md`](../INDEX.md)) + `/conflict-check`; the core is the only project touching `Services/Ai/` internals; satellites touching the BFF do so only at declared consumer seams (per [`bff-extensions.md` §G](../../.claude/constraints/bff-extensions.md) single-hop dispatch). The core's seam-publication tasks are scheduled FIRST so Compose r2 is never blocked on unpublished contracts.

### 3.4 Contract-first rule (v0.3 — Phase A0)

Before implementing any r2 **feature that produces or consumes them**, the core publishes versioned contracts for: `ContextEnvelope v1` · `OutcomeCard v1` · `MemoryItem v1` · `GateDecision v2` · `TraceEvent v1` · `ComposeDisposition v1` · `JobAwareCompletionState v1` — each with versioning + tolerant-reader rules, example payloads, client rendering expectations, server persistence expectations, and failure/partial-completion states. **Satellites may only consume these contracts; they may not invent local variants.**

**Delivery form (ratified refinement)**: Phase A0 is **walking-skeleton, not paper-only** — each contract ships WITH one thin reference producer + consumer + a contract test (`tests/integration/contract/**`, ADR-038 KEEP path). The seven are not equal in maturity and the spec sizes them accordingly: `OutcomeCard`/`GateDecision v2` formalize shipped r1 primitives; `TraceEvent` largely NAMES the existing ledger ToolChain markers; `ComposeDisposition` = the already-planned §3.1 seam publication; `MemoryItem` and `JobAwareCompletionState` are genuinely new design. **Per R-3, D-F0 runs in parallel with A0** — the doctrine has no contract dependency; A0 gates the Completion Engine, Memory Service, and Context Binder waves.

The assessment's contract sketches ([`spaarke-ai-r2-assessment-recommendations.md` §8](spaarke-ai-r2-assessment-recommendations.md)) are /design-to-spec input seeds — several embed decisions the spec must still make (Undo expiry semantics, job statusUrl shape).

---

## 4. The delivered product (end-user terms — acceptance backbone)

Same doctrine as r1: every phase gates on a **user-verifiable browser UAT script** on spaarkedev1 (the r1 browser rule is retained verbatim — a passing curl or green test never satisfies a gate).

| Gate | Owner | The user can now… |
|---|---|---|
| **G-R2-A (Judgment + Friction)** | core | Say "create a follow-up task due Friday, assign it to me" and — because the request was explicit and complete — the task is **created without any confirmation dialog**, with a ✅, a **clickable record chip**, and next-step chips. An ambiguous or inferred write still confirms — **exactly once, in exactly one modality**. Ask for something partially blocked and the assistant **verifies state, does what it can, and hands over the rest**: extracted values, prepared content, and a **working deep link to the right surface** (e.g. the document block links the Document Upload page) — refusal is the LAST rung, and never a dead end. Every claimed UI action is backed by a real client event; failures render ❌ with the real reason; "how did you decide that?" opens the decision-traceability view (with live plan narration); long outputs render progressively. |
| **G-R2-B (Memory)** | core | Open the assistant on a matter and it already knows: who the user is, what record it's on (name, id, schema), what happened earlier in this conversation, what drafts/outputs exist in this workspace, and standing preferences — **without re-prompting**. Preferences stated once persist across sessions. The user can see and delete what the system remembers. A hostile document cannot write memory. |
| ~~G-R2-C (Compose)~~ | **→ Compose r2** | The assistant-driven document lifecycle gate (open → pre-seed → draft-into-editor → AI edit rounds → save-back with provenance) **moves verbatim to the Compose r2 project** as its flagship gate (§8). |
| **G-R2-D (Hardening)** | core | Everything above is boring: reliable, telemetered, eval-gated, publish-size verified, on a codebase not larger than r1 left it. Includes cross-satellite verification that no satellite forked an AI-internal seam. |

---

## 5. Design principles (binding for r2)

1. **Build ON ADR-039/040, never around them.** Every new r2 mechanism is expressible as: catalog rows, ledger entry types/readers, gate-engine policy, context assembly, or client rendering of stored entries. No second dispatch protocol, no parallel session cache, no routing config outside the Binding table.
2. **Determinism over steering — for side effects; initiative is free on reads.** R1's UAT rounds proved prompt directives are the weakest enforcement layer for *writes* (H6 → R2-B → R3-1 → R5-E were four rounds of re-steering, ending in a hard block). Where r1 steered writes, r2 mechanizes them. But the SAME evidence shows the inverse for reads: caution pins generalized into passivity. The v0.2 refinement: **reads/searches/verification are always safe and always encouraged; only side effects need determinism** (D-F0(b)).
3. **Structured memory, not embeddings** (assessment ruling). Memory items are explicit governed objects; semantic retrieval stays in Azure AI Search.
4. **Every side effect proves itself to the user.** Storage precedes rendering (ADR-040); rendering must include outcome + link + next steps. Extended in v0.2: *and every refusal proves it tried* — degradation ladder + affordance (D-F0).
5. **Hard cutover doctrine carries over** — no compat shims, no parallel-run (operator, r1).
6. **Core owns the AI internals** (§3.1) — satellites consume seams, never fork them.

---

## 6. Target architecture (summary)

```
Assistant (SpaarkeAi shell: Conversation | Workspace/Compose* | Context)
      │
      ▼
Reasoning Runtime  (r1's bounded agent turn — FORMALIZED, not rebuilt)
      ├─ Judgment layer (D-F0 doctrine: verify → act → degrade → refuse-with-affordance)
      ├─ Context Binder  ──►  ContextEnvelope {User, Workspace, Business, Memory, Organizational*, Semantic*}
      ├─ Tool Orchestrator (existing loop, budget-8, closed catalogs)
      ├─ Gate Engine (ONE gate + Confirmation Policy v2: risk-tier × request-origin)
      └─ Completion Engine (outcome events → ✅/❌ + record links + next-step chips + trace)
      │
      ▼
Spaarke Memory Service
      ├─ Conversation scope  = the Session Ledger (ADR-040 — substrate, not a new store)
      ├─ User scope          = governed memory objects (new — Cosmos)
      ├─ Workspace scope     = governed memory objects (new — Cosmos)
      ├─ Organizational scope* = provider interface only (Work IQ candidate — research)
      └─ Semantic scope*     = provider interface over existing Azure AI Search / SPE
      │
      ▼
Capabilities (Actions × Bindings) ── Tools (typed handlers) ── Dataverse · SPE · AI Search · Cosmos
```
(*) Organizational/Semantic = interface defined in r2, implementation deferred/researched (§9). Compose\* = the editor + lifecycle are **Compose r2** (satellite) building on core-published seams; the core owns only the seams (disposition member, SSE frames, provenance).

Nothing in this picture replaces an r1 component. The Reasoning Runtime IS `SprkChatAgentFactory` + `SessionDispatchOrchestrator` + the gate store, named and given two new collaborators (Context Binder, Completion Engine) and a rebuilt directive layer (D-F0). Conversation memory IS the ledger.

---

## 7. The core areas — decisions with rationale

### 7.1 Area 1 — Judgment + friction: user-expectation alignment

#### D-F0. Resourcefulness Doctrine — the judgment layer (FIRST work item of the project)

**Decision**: r2's first shipped work item is a **strategy-level judgment layer**, five components:

- **(a) Strategy meta-prompt** — ONE system-prompt strategy block replacing scenario-by-scenario steering pins where they generalize: *decompose the request → inventory the available tools → **VERIFY state before acting** (duplicate-check before any create; search before claiming absence) → act, or approximate with what is available → **always deliver partial value plus a concrete next step**.* The G-P3 rounds 1–6 pin accretion (honesty bullets, clarify rules, same-turn rules) is audited against this block: pins that are instances of the strategy fold in; genuinely scenario-specific contracts (per-table write contracts, tool-description guidance) stay catalog data.
- **(b) Read/write safety asymmetry rule** — reads, searches, metadata describes, and verification calls are **always free**: the model is told to use them liberally and never to ask permission for, hedge on, or skip a read. Only side effects need care — and those are governed deterministically (Policy v2, gates), not by model timidity.
- **(c) Graceful-degradation ladder** — full action → partial action → **structured assistance** (extracted values, prepared content, a deep link to the right surface with the work carried as far as possible) → refusal **LAST**.
- **(d) Refusals and blocks always carry actionable affordances** — every refusal or hard block must state what the user can do instead AND hand them a working affordance. Concrete first case (operator finding 2026-07-07): the R5-E `sprk_document` hard-block message should **deep-link the Document Upload code page** (pre-scoped to the host record where possible), not merely name the wizard.
- **(e) Resourcefulness eval family** — blocked-action scenarios scored on **partial-value delivery** (did it verify first? extract what it could? link the right surface? propose the next step?), joining the existing golden-utterance suite as a merge gate. Fabrication counter-cases are included so (a)–(c) can never be satisfied by inventing outcomes — resourcefulness and honesty are scored together. **v0.3 (pre-spec obligation, assessment F-1)**: because D-F0 is enforced by prompt + eval rather than the gate engine, *the eval family IS the enforcement mechanism* — it is specified BEFORE /design-to-spec in `notes/d-f0-eval-family-spec.md` with: scenario taxonomy (`blocked-write`, `partial-capability`, `read-hesitancy`, `absence-claim`, `fabrication-counter`), per-case scoring rubric (`verified_first`, `partial_value_delivered`, `affordance_present`, `no_fabrication` — **gate-critical at 100%**, `no_unneeded_confirm`; others ≥90% threshold subject to operator adjustment), and a ≥20-case baseline at family creation. The second assessment's ten legal-work **scenario evals** (matter-aware create, one-clarification ambiguity, blocked-create with extraction+link, Compose draft-revise-save round-trip, "what happened here" trace, memory-poisoning via upload, portfolio fresh-retrieval, ingestion-parity status, Tier-4 email confirm, deadline confirm+audit) layer above it as the E2E band, browser-verifiable where UI state matters.

**Rationale**: the M365 Copilot comparison (§2) — Microsoft steers **STRATEGY, not scenarios**; its assistants read broadly, verify, and degrade gracefully by default. R1's anti-fabrication hardening (three of six UAT rounds) installed caution that **generalized into passivity** — the honest-refusal muscle now fires where a resourceful assistant would help. Determinism-over-steering (principle 2) still governs side effects — nothing in D-F0 weakens a gate or a hard block; initiative on reads is free by construction, which is precisely why the doctrine is safe. D-F0 is FIRST because every other Area-1 decision (Policy v2, Completion UX, truthfulness, traceability) modulates the same judgment layer, and because it is cheap (prompt + eval family + affordance plumbing) relative to its user-perceived impact. Live plan-narration streaming (the "what I'm doing now" companion to this doctrine) folds into **D-F4**, not here.

#### D-F1. Confirmation Policy v2 (the headline friction fix)

**Decision**: Confirmation becomes a deterministic **gate-engine policy over (risk tier × request origin × argument completeness)**, replacing blanket declared-class gating. **v0.3 (ruling R-1 + assessment Amendment 3)**: legal side effects are NOT one "create records" class — Tier 2 splits into sub-tiers:

| Tier | Class | Examples | Explicit + complete | Otherwise |
|---|---|---|---|---|
| 0 | Read / search / explain | search matter, summarize known record, inspect metadata | Execute (always — D-F0(b)) | Execute |
| 1 | Draft-only, no system mutation | draft clause, prepare email draft text, compose summary text | Execute | Execute |
| **2a** | Private/internal **reversible** create | personal follow-up task, draft note | **Execute** + ✅ card with **Undo chip** | Confirm |
| **2b** | Matter-scoped system-of-record create/update | matter task, internal status update, record association | **Execute** (Undo chip) | **Confirm** — ONE dialog |
| **2c** | Document creation / versioning | save generated text as document, new version, promote draft | **Preview/confirm in r2** (revisit post-G-R2-A) | Confirm |
| 3 | Legal-operational risk | deadline, obligation, assignment to ANOTHER user, client/matter status | **Always dialog** | Always dialog |
| 4 | External / irreversible | email SEND, filing, delete/supersede, external commitment | **Always dialog** | Always dialog |

Overlays (precedence order): **injection-suspect always wins** (`dispatchUncertain`, content-safety flags, untrusted-doc-origin ⇒ dialog + suspicion surfaced) → **safety-perimeter degradation** (v0.3, AI-ARCHITECTURE assessment rec 2: when PromptShield fails open — timeout/429/5xx — the turn's *gated writes* degrade to **confirm-required** regardless of origin/tier; reads stay fail-open per D-F0(b); shield-coverage telemetry makes the fail-open rate a measured number, not an assumption) → incomplete args ⇒ ONE elicitation turn (existing 032 machinery) then re-evaluate → origin (explicit/inferred) → tier row. Inferred/model-initiated at Tier ≥ 2 always confirms — ONE dialog (`ActionConfirmationDialog`), never a chat-loop re-ask.

**Risk classification is catalog-declared DATA, never runtime LLM judgment** (v0.3 binding rule): the sub-tier and its risk factors (reversibility, external visibility, deadline impact, confidentiality/privilege impact, record-of-truth impact) are **declared properties on the catalog row** — exactly the ADR-039 `side_effect_class` pattern, extended. Any runtime model-judged risk classification would be the second intent mechanism ADR-039 bans.

**Rationale**: formalizes (a) the P2 `dispatchUncertain` seam (task 031) and (b) the operator's explicit-vs-auto-trigger ruling — **re-confirmed verbatim at G-P3 round 5** ("explicit user request should NOT require confirmation; dialog + the model's extra chat-ask both count as friction"; only a cheap same-turn steering pin shipped, the policy is r2 scope). The R3-1 confirm-loop happened because "confirm once, then execute" lived only in prompt text; v2 makes the gate engine track per-request confirmation state so a second ask is structurally impossible.

**Mechanism sketch** (coarse): request-origin determined deterministically — Click path always user-explicit by construction; Text path marks origin from turn structure (user's utterance names the capability's action verb + invocation in that same turn ⇒ explicit; model-initiated calls in later turns or from document content ⇒ inferred); fail-closed default to *inferred*. Confirmation state is a Gate-ledger property (ADR-040 `Gate` status transitions). Undo affordance for 2a/2b auto-executes: the ✅ card carries an "Undo" chip where the tool declares a compensating action. **Scope addition (R5-E residual)**: gate **pre-suspend validation** — run the handler's `ValidateChat` BEFORE suspending into a dialog, so a doomed call renders an honest ❌ (with D-F0(d) affordance) instead of Confirm→❌ (§10 row 16).

**v0.3 (pre-spec obligation, assessment F-4)**: the prose sketch above is NOT sufficient at spec time. The spec carries a **deterministic decision tree** (origin → completeness → tier → injection overlay → behavior) with these six edge cases as **ruled rows** (rulings adopted as reviewed): **E-1** bare "go ahead" affirmation = explicit IF the immediately-preceding model turn proposed exactly one concrete action with complete args (Gate ledger binds the affirmation to the proposal), else inferred · **E-2** explicitness survives model-only intermediate turns for the SAME capability+args; any intervening user turn resets · **E-3** origin classification and injection detection are **layered, never merged** — the origin classifier never reads document-derived content as user utterance (provenance flags on message segments); injection-suspect then overrides regardless of origin · **E-4** one utterance enumerating N side effects = explicit for the enumerated set; model-added extras are inferred · **E-5** an elicitation answer inherits the original request's origin (state in Gate ledger) · **E-6** `dispatchUncertain` on an otherwise-explicit request ⇒ suspicion wins ⇒ dialog. An **origin-classification eval family** is generated from this table.

**New ADR candidate: ADR-041 "Judgment, Confirmation & Completion Policy"** — principle-level; carries the D-F0 doctrine as its preamble plus the D-F1/D-F2 policy tables. Proposed → Accepted at G-R2-A.

#### D-F2. Completion UX — every side effect yields visible, linked, actionable proof

**Decision**: A **Completion Engine** (server) + **OutcomeCard** (client contract) so every side effect produces, in the transcript: ✅/❌ status · human summary · **deep link to the affected record** · optional next-step chips (from the Binding's declared transitions) · a trace reference.

**Rationale + ground truth (updated for round 4)**: the primitives now EXIST — R4-3 shipped server-composed `[Open record]` links (env-URL + etn + id; persisted durably; the model relays real links instead of inventing `/WebResources/...` paths) and R4-6 shipped the `UserSummary` audience split (user-facing outcome sentence vs model-facing summary). D-F2 generalizes these shipped primitives from the gated path to ALL side-effect paths (gated + auto-executed + event-path) as one disposition-level contract, adds next-step chips and the trace reference, and upgrades markdown links to the OutcomeCard component. Named follow-ups from the fix wave fold in here: with-appid record links (thread MDA appid via session HostContext) as polish (§10 row 19).

**v0.3 — the Completion Engine is JOB-AWARE** (assessment Amendment 2 — the ratified round's most important addition): document and AI side effects are frequently **asynchronous multi-step pipelines** ("save as document" = SPE upload → `sprk_document` row → association → profile analysis queued/running → indexing queued/running → available). OutcomeCard therefore represents queued / running / partial / completed / failed / poisoned / cancelled / retry-pending / user-action-required states — per-step where one request fans into several operations — via the `JobAwareCompletionState v1` contract (§3.4), integrating the EXISTING Job Contract / `ServiceBusJobProcessor` status rather than inventing a new job model. The user must be able to distinguish "the record exists" from "downstream analysis/indexing finished". This contract is also how the core enforces the **R-2 ingestion-parity invariant** (§8) without owning the pipeline. Guiding principle (assessment §11, adopted verbatim): *an action is not complete when the model says so — it is complete when the system has persisted the outcome, linked the affected object, exposed the status, and made the decision traceable.*

#### D-F3. UI-action truthfulness — claims backed by client acknowledgment

**Decision**: UI-affecting tools (open tab, open Compose, future navigation) complete their tool result only on a **client acknowledgment event** (ack over the existing session channel referencing the emitted frame id), or fail honestly on timeout. The R2-D fail-honest pattern is the floor; the ack is the ceiling. (Unchanged from v0.1.)

#### D-F4. Trust surface — Decision Traceability + live plan narration

**Decision**: Extend the r1 ExecutionTraceWidget into the assessment's **decision-traceability view**: user request → context slices used (ContextEnvelope summary) → memory items consulted → tools selected → gate/approval path → final outcome. Data: ledger `ToolChain` + `Gate` entries + a new ContextEnvelope fingerprint entry (identifiers/counts only — NFR-07). **v0.2 additions**: (i) **live plan narration** — the D-F0 strategy's steps stream as lightweight status narration during the turn ("checking for an existing task… creating… done"), rendered from real tool-chain events (never model-claimed) — folded here from the D-F0 discussion; (ii) the R5-D fix shipped a client replay buffer that dies on hard refresh — the **server ToolChain read surface** (restore payload or GET over the trace ledger) is in-scope here (§10 row 14).

#### D-F5. Progressive render — the store-then-render polish

**Decision**: Dispatched capability outputs render progressively while KEEPING the ledger-write-before-render invariant. Preferred: section-keyed streaming per amended ADR-037; fallback: client-side progressive reveal of the stored terminal chunk. Spec-time engineering call. (Unchanged from v0.1.)

### 7.2 Area 2 — Memory: depth, breadth, persistence

#### D-M1. Spaarke Memory Service — five scopes, structured objects, one service

**Decision**: A first-class **Memory Service** with the assessment's five scopes:

| Scope | Substrate | R2 status |
|---|---|---|
| **Conversation** | **The Session Ledger (ADR-040) — no new store.** Read/query facade over ledger entries + compacted digest | Ships (mostly exists) |
| **User** | New governed memory objects (preferences, drafting style, active areas) — Cosmos container | Ships |
| **Workspace** | New governed memory objects (prior drafts/outputs/decisions/open issues per matter-or-workspace) — Cosmos, keyed to Business Context | Ships |
| **Organizational** | Provider interface only (Work IQ = named future provider) | Interface + research |
| **Semantic** | Provider interface over EXISTING Azure AI Search + SPE retrieval | Interface (implementation exists) |

Memory items are **structured objects, not embeddings**, each carrying the full governance envelope — **extended in v0.3** (assessment Amendment 5) to: `tenantId, scope, owner, subjectType/subjectId, source (+ sessionId/turnId provenance ref), sourceTrustLevel, confidence, sensitivity, expiration, deletionPolicy, retentionClass, created, updated, createdBy`. Storage: Cosmos (assessment ruling — Dataverse stays business records).

**v0.3 governance requirements** (spec-time detail, legal-domain-critical): retention defaults + expiration behavior per `retentionClass`; user-visible **review/delete surface** for user-scope memory (G-R2-B); workspace-scope memory review honoring **matter/workspace authorization** (ethical-wall alignment); deletion propagation semantics; audit events on write/delete; **litigation-hold interaction** named explicitly at spec (a held matter's workspace memory is not user-deletable while held); sensitive-content classification via `sensitivity`.

**Organizational scope directionality** (v0.3, assessment F-7): the provider interface is **read-only INBOUND** — Spaarke *receives* organizational context (Work IQ candidate) through it. The outbound surface (Spaarke-as-MCP-server for Microsoft tool consumption — the settled inverse-consumption posture) is a **separate architectural seam, explicitly out of r2 scope**; named here only so the spec author does not conflate the two.

**Rationale**: unchanged from v0.1 — the assessment's central claim matched the r1 UAT experience exactly. Conversation memory IS the ledger (ADR-040's no-parallel-session-cache rule); the Memory Service adds scopes ABOVE the session.

**New ADR candidate: ADR-042 "Memory Architecture & Governance"** — scopes, governance envelope, write policy (D-M3), erasure semantics (ADR-015 Tier 3), the not-a-parallel-session-cache rule.

#### D-M2. Context Binder — context becomes intentional, not implied

**Decision**: A **Context Binder** assembles ONE `ContextEnvelope {User, Workspace, Business, Memory, Organizational, Semantic}` per turn — the canonical context contract for the Reasoning Runtime. Assembly is **cache-stable**: stable-prefix slices (identity, schema cards, environment facts, preferences) precede volatile slices (ledger tail).

The Binder **generalizes SIX r1 primitives** (migration map for Area 2 — extended for rounds 4–5):

| R1 primitive (shipped as a point fix) | Becomes ContextEnvelope slice |
|---|---|
| `BuildLedgerOutputsContext` (last-8 ledger outputs, cache-stable tail) | **Memory.Conversation** slice — plus a **retrieval policy**: portfolio-/aggregate-level questions bias to FRESH queries, never extrapolation from a prior turn's result (R5-C note) |
| Host-context identity line (H7) | **Business** slice — host record identity |
| **Host-record schema card** (G-P2 + R4-1/R5-B/R5-C per-table write contracts, lookup targets, `*_ref` maps — currently hand-mirrored across tool descriptions) | **Business** slice — Dataverse metadata as FIRST-CLASS context, assembled once by the Binder; honors the our-AI-Search-not-Dataverse-search ruling |
| **`BuildCurrentDateDirective` (R5-A)** — the clock, appended unconditionally | **Environment facts** in the stable prefix — clock now; user timezone threading is the named follow-up (§10 row 19) |
| **Caller identity → self-contact resolution (R5-B gap)** — "assign to me" currently resolved by model guesswork or honest omit | **User** slice — the Binder resolves the caller's contact deterministically (claims→contact, server-side) so self-assignment stops being a steering problem (§10 row 17) |
| Gate-outcome transcript persistence (R2-C) | **Memory.Conversation** slice — outcome events feed both the model and the Completion Engine |

**Rationale**: unchanged in essence — every slice has a proven r1 fragment; the Binder is consolidation + two new scopes. Rounds 4–5 strengthened the case: FOUR more context classes (clock, write contracts, lookup metadata, caller contact) each had to be point-fixed into descriptions or directives; the Binder is where they all belong.

**v0.3 — token budgets are a charter NFR, not a spec afterthought** (assessments F-2 + Amendment 6 — the two reviews' strongest convergence; the r2 failure mode here is not fabrication but context inflation eating working space and cache stability):

| Slice | Starting budget (tokens) | Stability class |
|---|---|---|
| Environment facts (clock, tz) | ≤ 50 | Stable prefix |
| User (identity, contact, preferences) | ≤ 300 | Stable prefix |
| Business (host identity + schema card + write contracts) | ≤ 1,200 | Stable prefix — **conditional, see determinism check below** |
| Workspace memory items | ≤ 600 | Semi-stable |
| Memory.Conversation (ledger tail) | ≤ 2,000 | Volatile tail |
| Organizational / Semantic | 0 in r2 (interface only) | n/a |
| **Envelope ceiling** | **≤ 4,200** | — |

**Measure-first rule (ratified)**: these are STARTING estimates — Phase 0 measures r1's actual as-built prompt assembly and the budgets are fixed against measurement, not adopted a priori. **Envelope rules**: full document text is NEVER copied into the envelope unless the invoked capability specifically requires it; ledger entries travel as references, not copied prior output; per-slice token counts are logged per turn (identifiers/counts only — NFR-07) and a **budget breach fails the eval run**. **Phase-0 determinism check (F-2/T-3)**: the Business slice's stable-prefix claim REQUIRES schema cards to render deterministically (stable property ordering, no timestamps, no per-request GUIDs) — verify against the actual Dataverse metadata assembly at discovery; if it cannot be made deterministic, the Business slice moves out of the stable prefix and the caching NFR is re-scoped honestly.

#### D-M3. Memory writes are side effects — with a poisoning threat model

**Decision**: Memory writes go through the SAME closed-catalog machinery as every other side effect: a `memory.write` typed tool with declared `side_effect_class`, subject to Policy v2 (explicit "remember that I prefer X" ⇒ execute + ✅; model-inferred capture ⇒ lightweight confirm or queue-for-review). **Untrusted content (uploaded-document text, tool results) can NEVER originate a memory write.** Memory reads surface provenance; users get a view-and-delete surface (G-R2-B). Eval-suite growth: memory-poisoning injection families.

**v0.3 — semantic-retrieval ↔ memory trust boundary** (assessment F-5): Semantic-scope retrieval results may themselves derive from untrusted indexed sources, so (i) retrieval results carry their **own provenance class** in the ContextEnvelope and are **never promoted** to User/Workspace memory implicitly; (ii) promotion requires an explicit `memory.write` call — itself Policy-v2-governed — whose resulting item records `source: semantic_retrieval` with the originating index/document reference; (iii) the Context Binder keeps memory slices and retrieval slices **structurally separate** (distinct slice keys, never merged into one context block); (iv) the memory-poisoning eval families gain cases where the injection vector is *retrieved* content, not only *uploaded* content.

#### D-M4. Workspace intelligence — deferred-but-shaped

**Decision**: the assessment's Workspace Intelligence layer does NOT ship as a subsystem in r2. Its cheap precursors do: next-step chips on OutcomeCards (D-F2) and workspace-scope memory items (D-M1). Full goal-tracking is named follow-on. (Unchanged from v0.1.)

---

## 8. Compose r2 charter handoff (Area 3 — TRANSFERS to the Compose r2 project)

Compose remains the operator's **#1 most important feature** — which is exactly why it ships as its **own parallel project** (§3.2) rather than serialized behind judgment + memory inside this one. The decisions below were drafted in v0.1 as this project's Area 3; per the v0.2 re-cut they **transfer verbatim into Compose r2's design.md** as its charter baseline. They are recorded here as the handoff package; **this project does not implement them** — it publishes the seams (§3.1).

**Handoff contents** (full v0.1 text preserved in git history at commit `14e0c8762`; summarized here):

- **D-C1 — Compose is the `compose-editor` workspace layout** (per `spaarkeai-compose-r1` design §4; layout record exists; the chat→layout-tab bridge SHIPPED in r1). Not a separate destination, app, or modal-first experience.
- **D-C2 — the five lifecycle legs**: **Open** (SHIPPED r1 round 2–3; D-F3 ack joins from the core) · **Pre-seed** (**SHIPPED in the G-P3 round-4 fix wave, R4-2** — real `sprk_document` rows resolve SPE pointers under user OBO and load into Compose, refresh-surviving; session-UPLOADED chat files can NEVER pre-seed until the document-creation capability lands — handled honestly today) · **Draft-into-editor** (the `compose` disposition member — **seam owned by the core**, editor materializes from the stored ledger entry, render-follows-store) · **AI edit rounds** (selection-aware refine via a `compose-selection`-scoped Action + Binding; hoists AnalysisWorkspace's `analysis.refine` prior art; ADR-040 P10 — selection travels as capability args, never screen-scrape) · **Save-back + provenance** (SPE new-version + `sprk_document` promotion-on-first-Save per compose-r1 §8; provenance = session id, capability, `{bindingId}@t{n}` refs).
- **Document-creation capability — bar set by R5-E: FULL ingestion parity.** "Save this summary as a new document" = not just SPE upload + `sprk_document` row, but the **complete ingestion pipeline the Document Upload wizard drives: SPE storage + document profile analysis + indexing** — otherwise the created document is the R5-E broken-widget orphan with extra steps. Tier-**2c** write under Policy v2. This converts r1's honest refusal (now hard block) into the real thing. **v0.3 ownership ruling (R-2)**: ingestion parity is a **PLATFORM INVARIANT owned by the core** — *a bare `sprk_document` row is never a successful document operation*; the core enforces it via gate policy + `JobAwareCompletionState` (minimum parity checklist: SPE file exists · `sprk_document` exists · valid storage pointer · parent association where applicable · valid access context · provenance stored · profile analysis queued-or-done · indexing queued-or-done where configured · status visible · failures recoverable-or-surfaced). Compose r2 implements the pipeline; the core verifies the invariant.
- **v0.3 addition — Compose fidelity boundary** (R-4; assessment Amendment 8, transfers with this package): Compose r2's design MUST carry an explicit boundary table declaring which editing needs the TipTap surface serves (AI first-draft, plain-text revision, clause rewrite, selection-aware refinement) vs which route to **Word for Web/Desktop** (tracked changes, comments, footnotes/cross-references, complex/final legal formatting, redline comparison — Word REQUIRED; "open in Word" is the pressure valve, per D-C3). Compose must not be judged against Word fidelity before it is ready — the boundary protects product trust.
- **D-C3 — editor scope discipline**: TipTap OOB only (compose-r1 §14 decisions re-affirmed, not re-opened); "open in Word" is the fidelity pressure valve; `DocumentCheckoutService` single-editor lock; co-editing/tracked-changes/add-in entry stay out.
- **D-C4 — `spaarkeai-compose-r1` disposition**: Compose r2 **absorbs and supersedes** it (never started; its TipTap/SPE/promotion/checkout decisions remain authoritative; its AI-dispatch vocabulary is stale — `IConsumerRoutingService`/`IInvokePlaybookAi`/consumer-type appsettings were deleted or re-based by r1; running it as-written would violate ADR-039). Its spike plan survives as Compose r2's Phase 0, with the dispatch smoke re-based on Bindings. Formality confirmation = open question §12 Q1.
- **D-C5 — AnalysisWorkspace convergence**: Compose becomes THE editor-centric surface; AnalysisWorkspace **retires at feature-parity** (selection-aware refine is the load-bearing case), hard-cutover with grep-zero verification, clearing its jest-ESM debt by deletion. Frozen until parity. Ruling = open question §12 Q2. Cross-project note: the freeze is enforced by the core (catalog governance); the retirement executes in Compose r2.
- **The former G-R2-C gate** (full lifecycle in one conversation, browser-verified on spaarkedev1) transfers as Compose r2's flagship gate, unchanged in content.

**What the core still owes Compose r2** (tracked as core FRs): the seam contract (§3.1) published in Phase A; D-F3 ack plumbing; Policy v2 tier classification for the save-back/creation writes; OutcomeCard rendering for Compose side effects.

---

## 9. Explicit non-goals (for THIS project — the core)

- **The Compose editor + document lifecycle** — Compose r2 satellite (§8). The core ships seams only.
- **Multi-agent orchestration** — architecture must not PREVENT it; r2 builds none of it.
- **Work IQ / Foundry IQ integration beyond research** — provider interfaces (D-M1) + possible researcher spike (§12 Q5); no runtime integration.
- **Fabric** — no role in r2 (assessment: analytics-only future).
- **Workspace-intelligence goal tracking** as a subsystem (D-M4 — precursors only).
- **Deep legal capabilities** beyond what the areas need — catalog rows after the platform.
- **New Dataverse tables for the manifest** (ADR-039 posture unchanged). New MEMORY storage is Cosmos, not Dataverse.
- **Re-opening r1's ratified architecture** — three paths, two catalogs, one ledger, one gate are settled.

---

## 10. Inherited backlog (r1 deferrals + G-P3 rounds 4–6 candidates → r2 disposition)

| # | Item (source) | R2 disposition |
|---|---|---|
| 1 | **Capability-discovery READ endpoint** for deterministic soft-slash launchers (gate-038 deferral) | Core Area 1 — ships with G-R2-A |
| 2 | **Document-creation capability** — bar raised by R5-E to **FULL ingestion parity** (SPE storage + document profile analysis + indexing — everything the Document Upload wizard drives; a bare `sprk_document` row is the defect, not the feature) | **Transfers to Compose r2** (§8 save-back/creation leg) |
| 3 | **Compose document pre-seeding** — **SHIPPED** in the G-P3 round-4 fix wave (R4-2) for real `sprk_document` rows | Residual (session-uploaded-file pre-seed) transfers with row 2 |
| 4 | **Legacy workspace tools verdict** (FR-P4-01: Get/Update/Close Workspace Tab + 4 artifact variants on the orphaned `IWorkspaceStateService` store) | If r1 P4 doesn't finish: core early Track-B — re-point or retire |
| 5 | **ADR-040 inline size-cap enforcement home** (048 ruling) | Takes r1's ruling; if r2 → memory/ledger hardening in G-R2-B phase |
| 6 | **create-task entity: `sprk_event(type=task)` vs `sprk_todo`** (048 ruling; catalog-data-only switch) | Takes r1's ruling; OutcomeCard links target the right entity either way |
| 7 | **Progressive render** (H5) | Core Area 1 — D-F5 |
| 8 | **office-addins SseClient keep-with-reason** (048 ruling) | Accept-as-ruled |
| 9 | **Task.Delay → TimeProvider probes** (r1 /defer) | Core Track-B hygiene sweep |
| 10 | **Test debt**: AnalysisWorkspace jest ESM (r1 /defer) + 3 SpaarkeAi failing suites (round-3) + **8 AI.Widgets failing suites (round-4, verified pre-existing at HEAD via git-stash A/B)**. *v0.3 note: the 4 KNOWN BFF unit failures were adjudicated + fixed post-charter (r1 close, PR #558 — master suite fully green); they are NOT r2 scope.* | AnalysisWorkspace debt clears via retire-at-parity (Compose r2, D-C5); SpaarkeAi + AI.Widgets suites = test-repair task in core Phase A |
| 11 | `Refresh-ScopeModelIndex.ps1` drift + dead App Service env keys (task 040 W-1) | Core Track-B hygiene sweep |
| 12 | **Playbook/embeddings orphans on spaarkedev1** (DAILY-BRIEFING-NARRATE `7b5a6ed3` + `spaarke-playbook-embeddings` index) | Expected closed by r1 P4; verify at r2 start |
| 13 | **Refusal-affordance links** (operator 2026-07-07: the R5-E `sprk_document` block names the Document Upload wizard but gives nothing to click) | Core — D-F0(d); first case = deep-link the Document Upload code page from the block message; G-R2-A |
| 14 | **Trace hard-refresh ledger read** (R5-D honest limitation: client replay buffer is per page load; no server ToolChain read surface — restore payload or GET) | Core — D-F4; G-R2-A |
| 15 | **Validator triple-twin hoist** — the guidance/contract text lives in three hand-maintained twins (live catalog row `sprk_description` ↔ handler `Metadata` description ↔ `infra/dataverse/` seed mirror); EVERY G-P3 fix wave updated all three by hand; hoist to one authored source with generated/validated mirrors (extend the `OpenAiFunctionSchemaValidator`/health-check machinery to enforce parity) | **Core — Phase A UNCONDITIONALLY, sequenced BEFORE any task that adds or modifies a catalog row** (v0.3 re-sequencing per assessment F-3 — r2 adds rows in ≥4 waves: `memory.*` tools, D-F0(d) affordance messages, briefing fix wave, Compose surface; the `memory.*` rows are authored THROUGH the hoisted source as its first consumers; acceptance = single-source edit propagates to all three surfaces with validator-enforced parity) |
| 16 | **Gate pre-suspend validation** (R5-E accepted residual: the confirmation dialog shows before `ValidateChat` rejection on the loop path — Confirm → honest ❌) | Core — folded into D-F1 Policy v2 scope |
| 17 | **Caller-contact self-assignment resolution** (R5-B: "assign to me" needs deterministic server-side claims→contact resolution, not model guesswork) | Core — D-M2 User slice; G-R2-B |
| 18 | **Portfolio context-bias** (R5-C note: portfolio-/aggregate-level questions must query fresh rather than extrapolate from the prior turn's result) | Core — D-M2 Memory.Conversation retrieval policy; G-R2-B |
| 19 | **Other rounds-4/6 named candidates** (enumerate at spec time): with-appid record links (R4-3, HostContext appid threading) · ledger-outputs → DRAFT-CORR input (R4-4 `outputRefs` dispatch-contract change) · user-timezone threading for date resolution (R5-A) · cataloged create-matter capability (R4-1 — a Binding + prompted Action like create-task) · session-history browse/delete (R4-5 note — conversation-memory surface) | Core — Areas 1/2 candidate rows; create-matter + history lean G-R2-A/B; appid + tz = polish |
| 20 | **Job-aware Completion Engine integration** (v0.3, assessment Amendment 2): side-effect OutcomeCards integrate async job status (`JobAwareCompletionState v1`) so document creation, analysis, indexing, and Compose save-back show durable multi-step progress + failure recovery | Core — D-F2 scope; contract in Phase A0 (§3.4); browser-verified at G-R2-A; load-bearing for the R-2 ingestion-parity invariant (§8) |
| 21 | **Audit-container partition re-key** (v0.3, AI-ARCHITECTURE assessment W-8): the permanent audit container partitioned by bare `/tenantId` will hit Cosmos's 20 GB logical-partition cap for a busy tenant — re-key hierarchically or time-bucketed (`/tenantId` + month, or synthetic) while the container is small; cheap now, painful migration later | Core — G-R2-D hardening row |
| 22 | **Matter-level retrieval ACL verification** (v0.3, AI-ARCHITECTURE assessment rec 5 — operator-DEFERRED into r2, not a pre-r2 priority): verify whether user-level matter walls are enforced in the AI Search filters at RETRIEVAL time (the load-bearing ethical-wall control) or only at the history-sanitization layer (defense-in-depth). Bounded read-only spike; if a gap exists it escalates as security-sensitive (own project, not core scope — the retrieval substrate is shared by all surfaces) | Core — Phase-0/G-R2-B-adjacent verification spike; escalation path pre-declared |

(Rows 4, 5, 6, 8, 12 are contingent on r1 P4-close rulings — the spec MUST re-check them at project-pipeline time.)

---

## 11. Constraints, hot paths, ADR posture

- **Hot-path declaration**: <hot-path-declaration> BFF=**Y** · SpaarkeAi=**Y** · ci-workflows=**N** · skill-directives=**Y** (jps-* skills gain memory-scope guidance + the D-F0 doctrine reference; the r1 round-1 pending `jps-action-create` checklist items — property-level `required` ban + `infra/dataverse/inputschemas/` mirror pointer + round-4 DRAFT-CORR example sync — land here if r1 P4 doesn't take them) · root-CLAUDE.md=**N** </hot-path-declaration>
- **Core-owns-AI-internals rule** (§3.1): this project is the ONLY active project modifying `Services/Ai/` internals; Compose r2 + Insights refurbish register in [`projects/INDEX.md`](../INDEX.md) and consume seams. `/conflict-check` enforces at PR time.
- **Placement justification** (CLAUDE.md §10): Memory Service, Context Binder, Completion Engine, gate-policy extension, and the seam surface live in `Sprk.Bff.Api` — same ADR-013 criteria as r1 (latency + transactional coupling with session/SSE state; the Binder runs inside the turn). Compose endpoints move to the Compose r2 project's own placement justification. New Azure dependency: one Cosmos container (existing account) for user/workspace memory. Publish-size per-task verification continues (ADR-029; r1 G-P3-close baseline ~46.8 MB; ceiling 60 MB).
- **Component justification** (CLAUDE.md §11): three-question template per new component at spec time; presumption is EXTEND (Reasoning Runtime = existing loop; conversation memory = ledger; trust surface = existing widget + shipped replay buffer; directive layer = existing factory). Net-new for the core is limited to: Memory Service + store, Context Binder, Completion Engine/OutcomeCard, `memory.*` tools, the D-F0 doctrine block + eval family.
- **Binding ADRs**: ADR-039 + ADR-040 (**Accepted — any tension goes through CLAUDE.md §6.5**); amended ADR-013/ADR-037; standing set (008, 009/014, 010, 015, 016, 018, 019, 028, 029, 030, 031, 032, 036, 038).
- **New ADR candidates** (authored in the core, promotion-gated like 039/040):
  - **ADR-041 Judgment, Confirmation & Completion Policy** (D-F0 + D-F1 + D-F2) — Proposed at spec, Accepted at G-R2-A.
  - **ADR-042 Memory Architecture & Governance** (D-M1 + D-M3) — Proposed at spec, Accepted at G-R2-B.
- **Anticipated ADR tensions** (→ spec.md §ADR Tensions):
  - ADR-040's no-parallel-session-cache rule vs the Memory Service — resolved by construction (conversation scope = ledger facade); state explicitly (Path C).
  - ADR-040 "disposition is the only rendering contract" vs the `compose` member — the core extends the ENUM (Path C; the extension is the compliant move); Compose r2 consumes it.
  - D-F0(b) "reads always free" vs the budget-8 loop bound — no tension: the budget stays; the doctrine changes model WILLINGNESS within the budget, not the bound.
  - No others anticipated; Policy v2 refines gate behavior WITHIN the one-gate rule.
- **Testing/eval**: ADR-038 pyramid; golden-utterance suite (64 cases at r1 close) grows per area — **resourcefulness family (D-F0(e))**, Policy-v2 origin-classification families, memory-poisoning families; eval green stays a merge gate (r1 NFR-02); catalog/schema additions pass the `OpenAiFunctionSchemaValidator` + `infra/dataverse/inputschemas/` mirror-first authoring; row-15 hoist reduces the three-mirror maintenance the six fix waves paid.
- **Security**: NFR-03 untrusted-input posture extends to memory writes (D-M3); OBO-everywhere unchanged; memory items are ADR-015 Tier 3 (user-owned, GDPR-erasable); the view-and-delete surface is part of G-R2-B. D-F0 never weakens a gate: the degradation ladder operates BELOW the side-effect line.

---

## 12. Open questions FOR THE OPERATOR (answer before /design-to-spec)

1. **Absorb-compose-r1 formality (D-C4)**: confirm `spaarkeai-compose-r1` is formally absorbed/superseded by **Compose r2** (its design remains baseline for undisturbed decisions; dispatch vocabulary re-based on ADR-039/040; its spikes become Compose r2 Phase 0; project folder archived via `/devops-project-archive`). *Recommend: absorb.*
2. **AnalysisWorkspace retire-at-parity (D-C5)**: approve freeze-now / retire-at-Compose-parity (core enforces the freeze; Compose r2 executes the hard-cutover deletion)? *Recommend: yes.*
3. ~~**Policy v2 auto-execute tier line (D-F1)**~~ — **CLOSED 2026-07-08 (ruling R-1, §0.2)**: ruled on the sub-tier split — 2a auto-executes explicit+complete (Undo chip); 2b auto-executes ONLY explicit+complete, else confirms; 2c previews/confirms in r2; Tier 3+ always dialogs; injection-suspect always wins. Ratification conditions recorded: fail-closed origin classification, Undo chips, pre-suspend validation, origin-classification eval family.
4. **Cosmos memory store (D-M1)**: new Cosmos container in the existing account for user/workspace memory; Dataverse for nothing memory-shaped? *Recommend: yes as assessed.*
5. **Work IQ / Foundry IQ research spike**: researcher-subagent spike + provider-interface definition inside the core, or drop even the research to a follow-on? *Recommend: interface definition yes (cheap, shapes D-M1); live research spike optional.*

*(Answered since v0.1 and removed: phase order — superseded by the §3 platform-core + satellites re-cut, which runs Compose in parallel instead of sequencing it; pending r1 gate-048 rulings — G-P3 closed and P4 is closing, so the spec re-checks §10's contingent rows at pipeline time rather than asking now.)*

---

## 13. Risks (top 6)

| Risk | Mitigation |
|---|---|
| Policy v2 auto-execute produces an unwanted write (removed dialog) | Deterministic fail-closed origin classification (undecidable ⇒ inferred ⇒ confirm); Tier 3+ always dialogs; Undo chip; injection-suspect always confirms; pre-suspend validation; origin-classification eval family |
| **D-F0 over-corrects — resourcefulness drifts back into fabrication or over-eager approximation** | The doctrine changes read-side behavior only; every write stays gated/blocked exactly as today; resourcefulness evals score honesty AND partial-value together with fabrication counter-cases; hard blocks (R5-E) stay hard |
| Memory poisoning / privacy exposure | D-M3 write gate + untrusted-origin ban; governance envelope + provenance on read; Tier-3 erasure + user view/delete surface; memory-poisoning eval families |
| **Satellite coordination — Compose r2 runs day-one on seams the core hasn't stabilized** | Seam contract (§3.1) published as core Phase A FIRST tasks; core-owns-AI-internals rule + hot-path registry + `/conflict-check`; Compose Phase 0 = spikes that don't need the seams |
| ContextEnvelope grows the prompt / breaks caching | Cache-stable assembly is a design-time NFR (stable prefix, budgeted slices, beyond-window recall stays tool-call per ADR-040); per-slice token budgets measured in eval |
| r1 P4 slips and r2 starts on unfinished ground | §10 contingency rows; project-pipeline re-checks r1 state at spec time; core P0 includes a reconciliation task |

---

## 14. What /design-to-spec should produce (for THIS project — the core)

- **Pre-spec inputs authored FIRST** (v0.3 — these are inputs TO the spec, not outputs of it): `notes/d-f0-eval-family-spec.md` (D-F0(e) taxonomy/rubric/≥20 cases) and the Policy v2 origin-classification decision tree with E-1..E-6 ruled rows (D-F1). **Phase-0 discovery obligations** (verify in repo before citing with authority — per both assessments' validation discipline): golden-utterance suite file location/format, `OpenAiFunctionSchemaValidator` extension points for the row-15 hoist, deterministic schema-card rendering (D-M2 check), Gate-ledger property surface.
- FRs grouped by the core gates (**G-R2-A / G-R2-B / G-R2-D**), each carrying its **browser UAT script as acceptance criteria** (§4) — r1 browser rule verbatim, operator-executed on spaarkedev1.
- **D-F0 as the FIRST wave, in parallel with Phase A0 contract publication** (§3.4, ruling R-3): A0 ships the seven contracts walking-skeleton-style (contract + thin producer/consumer + contract test) and gates the Completion/Memory/Binder waves; the seam-publication tasks (§3.1) that unblock Compose r2 ride A0. The row-15 triple-twin hoist lands in Phase A BEFORE any catalog-row task (§10 row 15).
- **Daily Briefing hallucination remediation as the entry fix wave** (Wave 0): grounding/citation repair on BRIEF-NARRATE prompts + collector — enumerated tasks, r1-style catalog-data + eval-case pattern, no new architecture.
- **Compose handoff export**: one task that lands §8 into Compose r2's design.md (with the full v0.1 D-C text) and files the cross-project seam obligations both ways.
- NFRs carried from r1: publish-size ceiling + per-task verification; eval-suite-green merge gate; grep-zero retirement verification; NFR-07 no-content telemetry; NFR-03 untrusted-input posture (extended to memory writes); prompt-cache stability for ContextEnvelope; latency budgets.
- New NFRs (v0.3-extended): memory governance envelope completeness (all 14 fields, D-M1); user memory view/delete surface; UI-action ack coverage for every UI-claiming tool; OutcomeCard coverage for every side-effect path **including async job states**; **refusal-affordance coverage — no refusal/block ships without an actionable affordance**; resourcefulness eval family as merge gate; **per-slice ContextEnvelope token budgets with breach-fails-eval** (D-M2); **contract-first coverage — no A0-gated feature merges without its contract + contract test** (§3.4); the ten legal-work scenario evals layered above the golden-utterance suite (D-F0(e)); ingestion-parity invariant verification (§8, R-2).
- ADR-041 + ADR-042 authoring tasks with promotion gates (mirror the 039/040 pattern).
- The §10 inherited-backlog rows as enumerated tasks (contingent rows resolved at pipeline time; rows 2–3 exported with the Compose handoff).
- Named deferrals filed via `/defer` at close: workspace-intelligence goal tracking; Work IQ/Foundry IQ runtime providers; admin observability dashboards (carried from r1).
- Wave structure with pre-authored `/goal` conditions per wave IF the r1 pilot is judged proven at r1 close (check `notes/goal-feature-evaluation.md` promotion status); `/goal` never wraps a gate.

---

*DRAFT v0.3 — assessment round dispositioned and ratified 2026-07-08 (§0.2). Remaining before `/design-to-spec`: §12 Q1/Q2/Q4/Q5 answers (Q3 closed) + the two pre-spec notes (§14 first bullet). Then run `/design-to-spec projects/spaarke-ai-architecture-redesign-r2`.*
