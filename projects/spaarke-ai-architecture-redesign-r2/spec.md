# Spaarke AI Architecture Redesign R2 (Core) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-08
> **Source**: `design.md` (DRAFT v0.4) + `notes/d-f0-eval-family-spec.md` + `notes/policy-v2-origin-classification-decision-tree.md`
> **Parent epic**: #421 SPAARKE AI
> **Builds on**: `spaarke-ai-architecture-redesign-r1` (P0–P3 shipped; ADR-039 + ADR-040 Accepted + binding)
> **Project type**: platform CORE (judgment + memory) — sole owner of `Services/Ai/` internals; publishes seams satellites consume

---

## Executive Summary

R2-core refines R1's coarse-grained AI platform into a **refined experience** along two owner-prioritized axes: **judgment/friction** (a resourcefulness doctrine + deterministic confirmation policy + first-class completion UX + traceability) and **memory** (a Memory Service with two active scopes — **Record** `(entityType,entityId)` + **User** `userId` — over the ledger, plus a Context Binder that assembles one governed `ContextEnvelope` per turn). It builds strictly ON ADR-039 (grounded execution, closed catalogs) and ADR-040 (session ledger) — no second dispatch protocol, no parallel session cache. The Compose editor/document-lifecycle ships as the parallel **Compose r2** satellite consuming this core's seams; this project publishes those seams and enforces the document ingestion-parity invariant.

**One-line mission** (design §2): *Copilot's judgment and transparency wrapped around Spaarke's execution and governance.*

---

## Scope

### In Scope

**Contract-first foundation (Phase A0 — walking skeleton):** seven versioned contracts, each shipped with a thin reference producer + consumer + a contract test — `ContextEnvelope v1`, `OutcomeCard v1`, `MemoryItem v1`, `GateDecision v2`, `TraceEvent v1`, `ComposeDisposition v1`, `JobAwareCompletionState v1`.

**Area 1 — Judgment + friction (gate G-R2-A):**
- D-F0 Resourcefulness Doctrine (strategy meta-prompt, read/write safety asymmetry, graceful-degradation ladder, affordance-carrying refusals) + its enforcement eval family
- D-F1 Confirmation Policy v2 (deterministic gate-engine policy over risk-tier × origin × completeness; E-1..E-6 ruled rows; origin-classification eval family; gate pre-suspend validation)
- D-F2 Completion Engine + OutcomeCard across ALL side-effect paths, **job-aware** (async multi-step pipelines)
- D-F3 UI-action truthfulness (client-ack gating)
- D-F4 Decision-traceability view + live plan narration + server ToolChain read surface
- D-F5 Progressive render (store-before-render preserved)
- ADR-041 authoring (Judgment, Confirmation & Completion Policy)

**Area 2 — Memory (gate G-R2-B):**
- D-M1 Spaarke Memory Service — two active scopes: **Record** (generic `(entityType,entityId)` — matters/projects/invoices/work-assignments/events/documents; generalizes existing `MatterMemoryService`) + **User** (general per-user); Conversation stays the ledger; Organizational/Semantic interface-only. NEW Cosmos container **partitioned by subject** (`entityId`/`userId`, not `/tenantId`); structured objects with a governance envelope
- D-M2 Context Binder + `ContextEnvelope` assembly (generalizes six R1 primitives; cache-stable; per-slice token budgets)
- D-M3 memory writes are **AI-initiated + silent + provenance-tagged** (no write-gate); user review/delete is the control; full poisoning threat model + semantic-retrieval trust boundary DEFERRED (governance project)
- D-M4 workspace-intelligence precursors only (next-step chips + workspace-scope memory)
- ADR-042 authoring (Memory Architecture & Governance)

**Area 3 — Hardening (gate G-R2-D):** publish-size verification, eval-suite-green merge gate, **cross-satellite seam-fork verification**, Track-B hygiene, and the §10 backlog hardening rows (21 audit re-key, 4 legacy workspace tools, 9 TimeProvider, 11 script drift, 12 orphan verify, 10 test-repair).

**Inherited backlog (design §10):** rows dispositioned to the core are FRs herein; contingent rows (4, 5, 6, 8, 12) are re-checked at pipeline time against r1 P4-close state.

### Out of Scope

- **Compose editor + document lifecycle** — the **Compose r2** satellite (`projects/spaarkeai-compose-r2/`, its own design + gate). Core ships seams only + enforces the ingestion-parity invariant (design §8).
- **Daily Briefing hallucination remediation** — **handled in a separate project** (owner ruling 2026-07-08; supersedes design §3.2's "core Wave 0" default). This spec references it as a downstream consumer of the GroundednessCheck threshold→action pattern but enumerates **no** Briefing tasks.
- **Insights Engine Widget refurbish** — separate satellite after core Phase A (design §3.2).
- **Work IQ / Foundry IQ runtime integration** — provider *interface* is in scope (read-only inbound); the researcher spike + any runtime integration are **deferred** (owner ruling 2026-07-08; design §9).
- **Multi-agent orchestration, Fabric, workspace-intelligence goal-tracking subsystem, new Dataverse tables for the manifest, re-opening R1's ratified architecture** (design §9).
- **Spaarke-as-MCP-server outbound surface** — separate architectural seam (design D-M1, §7.2).

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/Ai/**` — gate engine + Policy v2, Context Binder, Memory Service, Completion Engine, directive layer, catalog governance, disposition-enum extension (**core is sole owner**)
- `src/server/api/Sprk.Bff.Api/Services/Memory/**` (new) — Memory Service + Cosmos store
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/**` — seam facades consumed by satellites
- `src/solutions/SpaarkeAi/**` — Completion UX, OutcomeCard render, decision-traceability view, live plan narration
- `src/client/shared/Spaarke.AI.Widgets/**` — OutcomeCard component, trace widget extension
- `tests/unit/Sprk.Bff.Api.Tests/**`, `tests/integration/contract/**` (new) — contract tests, service tests
- `infra/dataverse/**` + catalog seed mirrors — via the triple-twin hoist (single authored source)
- `docs/adr/ADR-041-*.md`, `docs/adr/ADR-042-*.md` + `.claude/adr/**` concise mirrors
- `.claude/skills/jps-*` — memory-scope guidance + D-F0 doctrine reference (hot-path skill-directives=Y)

---

## Requirements

FRs are grouped by delivery phase and the core gate they satisfy. Each gate's browser-UAT acceptance script is in **Success Criteria**. `[R#]` = design §10 backlog row; `[D-*]` = design decision.

### Phase 0 — Reconciliation & Discovery (foundation)

1. **FR-P0-01 — r1 P4-close reconciliation**: re-check the design §10 contingent rows (4, 5, 6, 8, 12) against actual r1 P4-close state; record dispositions in project notes. Acceptance: each contingent row resolved to in-scope-FR / accept-as-ruled / verified-closed with evidence.
2. **FR-P0-02 — Measure-first prompt-assembly baseline** [D-M2]: instrument and measure r1's **actual as-built** prompt assembly (per-slice token counts) before fixing any ContextEnvelope budget. Acceptance: a measured baseline table exists; §D-M2 budgets are set against it, not a priori.
3. **FR-P0-03 — Business-slice determinism check** [D-M2, F-2/T-3]: verify the Dataverse schema-card render is deterministic (stable property ordering, no timestamps, no per-request GUIDs). Acceptance: either determinism confirmed (Business slice stays in the stable prefix) OR the caching NFR is re-scoped honestly with the Business slice moved out of the prefix.
4. **FR-P0-04 — Discovery obligations** (design §14): verify in-repo before citing with authority — golden-utterance suite file location/format; `OpenAiFunctionSchemaValidator` extension points (for the row-15 hoist); Gate-ledger property surface; `dispatchUncertain` seam shape; `ServiceBusJobProcessor`/Job Contract status surface. Acceptance: a discovery note records each with file path + line evidence.

### Phase A0 — Contract-first (walking skeleton; §3.4)

Each FR ships the contract (with versioning + tolerant-reader rules, example payloads, client-render + server-persist expectations, failure/partial-completion states) **WITH** one thin reference producer + consumer + a contract test in `tests/integration/contract/**` (ADR-038 KEEP path). A0 gates the Completion/Memory/Binder waves; **D-F0 runs in parallel** (ruling R-3).

5. **FR-A0-01 — `ContextEnvelope v1`** [D-M2]: the canonical per-turn context contract {User, Workspace, Business, Memory, Organizational, Semantic}, with stability classes + per-slice budget fields.
6. **FR-A0-02 — `OutcomeCard v1`** [D-F2]: formalizes shipped R1 primitives (server-composed record link, UserSummary audience split) into one disposition-level contract.
7. **FR-A0-03 — `MemoryItem v1`** [D-M1]: structured memory object + the 14-field governance envelope (FR-B-02). Genuinely new design.
8. **FR-A0-04 — `GateDecision v2`** [D-F1]: gate outcome contract carrying tier, origin, completeness, overlay results, confirmation state.
9. **FR-A0-05 — `TraceEvent v1`** [D-F4]: largely NAMES the existing ledger ToolChain markers as a read contract.
10. **FR-A0-06 — `ComposeDisposition v1`** [§3.1 seam]: the `compose` disposition member + its SSE frame shape — **published FIRST** so Compose r2 is never blocked. Consumed (never forked) by Compose r2.
11. **FR-A0-07 — `JobAwareCompletionState v1`** [D-F2, R20]: queued/running/partial/completed/failed/poisoned/cancelled/retry-pending/user-action-required states, per-step; integrates the EXISTING Job Contract / `ServiceBusJobProcessor` — no new job model. Load-bearing for the ingestion-parity invariant.
12. **FR-A0-08 — Seam publication ordering**: the §3.1 seam set (ComposeDisposition, OutcomeCard slice, ContextEnvelope workspace slice, ledger provenance `{bindingId}@t{n}`, Policy v2 tier table) is scheduled as the FIRST A0 tasks. Acceptance: Compose r2 can consume every seam before core implements its own dependent features; cross-project obligation filed both ways.

### Phase A — Infrastructure (before any catalog-row task)

13. **FR-A-01 — Triple-twin validator hoist** [R15]: hoist the guidance/contract text from three hand-maintained twins (live catalog `sprk_description` ↔ handler `Metadata` description ↔ `infra/dataverse/` seed mirror) to **one authored source** with generated/validated mirrors; extend `OpenAiFunctionSchemaValidator`/health-check to enforce parity. **Sequenced BEFORE any task that adds/modifies a catalog row** (r2 adds rows in ≥3 core waves). Acceptance: a single-source edit propagates to all three surfaces with validator-enforced parity; the `memory.*` rows are the first consumers authored through it.
14. **FR-A-02 — Test-repair** [R10]: repair the 3 SpaarkeAi failing suites + 8 AI.Widgets failing suites (verified pre-existing at HEAD). Acceptance: suites green; TEST-MODIFYING rigor (code-review + adr-check) applied per CLAUDE.md §8. (AnalysisWorkspace jest-ESM debt clears via retire-at-parity in Compose r2, NOT here.)

### Gate G-R2-A — Judgment + Friction (Area 1)

15. **FR-A1-01 — D-F0 Resourcefulness Doctrine** [D-F0(a)-(d)] — **FIRST work item, parallel with A0**: strategy meta-prompt (decompose → inventory tools → verify-before-act → act-or-approximate → deliver partial value + next step); read/write safety-asymmetry rule (reads always free); graceful-degradation ladder; refusals/blocks carry actionable affordances. Audit the G-P3 rounds-1–6 pin accretion against the strategy block (fold instances in; keep scenario-specific contracts as catalog data). Acceptance: browser UAT (§G-R2-A script) + eval family FR-A1-02.
16. **FR-A1-02 — Resourcefulness eval family** [D-F0(e)]: implement per `notes/d-f0-eval-family-spec.md` — 5-family taxonomy, per-case rubric (`no_fabrication` gate-critical at 100%; `verified_first`/`partial_value_delivered`/`affordance_present`/`no_unneeded_confirm` ≥90% operator-tunable), ≥20-case baseline, + the 10-scenario E2E band. Joins the golden-utterance suite as a merge gate.
17. **FR-A1-03 — Confirmation Policy v2 gate-engine policy** [D-F1]: implement per `notes/policy-v2-origin-classification-decision-tree.md` — the risk-tier table (0/1/2a/2b/2c/3/4), overlay precedence (injection-suspect → safety-perimeter degradation → incomplete-args → origin → tier), the deterministic fail-closed origin classifier, and the **E-1..E-6 ruled rows**. Risk classification is **catalog-declared DATA** (extends ADR-039 `side_effect_class`), never runtime LLM judgment. Confirmation state is a Gate-ledger property (a second ask is structurally impossible). Acceptance: browser UAT + FR-A1-04.
18. **FR-A1-04 — Origin-classification eval family** [D-F1]: generated from the E-1..E-6 table (≥1 positive + ≥1 negative per row); merge gate.
19. **FR-A1-05 — Gate pre-suspend validation** [R16]: run the handler's `ValidateChat` BEFORE suspending into a dialog, so a doomed call renders an honest ❌ (with a D-F0(d) affordance) instead of Confirm→❌.
20. **FR-A1-06 — Completion Engine + OutcomeCard (all side-effect paths)** [D-F2]: generalize the shipped R1 link + UserSummary primitives from the gated path to ALL side-effect paths (gated + auto-executed + event-path) as one disposition-level contract; add next-step chips (from the Binding's declared transitions) + a trace reference; upgrade markdown links to the OutcomeCard component.
21. **FR-A1-07 — Job-aware completion** [D-F2, R20]: OutcomeCards integrate async job status via `JobAwareCompletionState v1` so document creation, analysis, indexing, and Compose save-back show durable multi-step progress + failure recovery; the user can distinguish "record exists" from "downstream analysis/indexing finished." This is how the core enforces the R-2 ingestion-parity invariant WITHOUT owning the Compose pipeline.
22. **FR-A1-08 — UI-action truthfulness** [D-F3]: UI-affecting tools (open tab, open Compose, navigation) complete their tool result only on a **client acknowledgment event** (ack referencing the emitted frame id) or fail honestly on timeout.
23. **FR-A1-09 — Decision traceability + live plan narration + server read surface** [D-F4, R14]: extend the ExecutionTraceWidget into the decision-traceability view (request → context slices used → memory items consulted → tools selected → gate/approval path → outcome), sourced from ledger `ToolChain` + `Gate` + a new ContextEnvelope-fingerprint entry (identifiers/counts only, NFR-07). Add (i) live plan-narration streaming from real tool-chain events (never model-claimed) and (ii) a **server ToolChain read surface** (restore payload or GET) so trace survives hard refresh.
24. **FR-A1-10 — Progressive render** [D-F5, R7]: dispatched capability outputs render progressively while KEEPING ledger-write-before-render. Preferred: section-keyed streaming per amended ADR-037; fallback: client-side progressive reveal of the stored terminal chunk (spec-time engineering call).
25. **FR-A1-11 — Refusal-affordance links** [D-F0(d), R13]: first case — the R5-E `sprk_document` hard-block message deep-links the Document Upload code page, pre-scoped to the host record where possible. NFR: no refusal/block ships without an actionable affordance.
26. **FR-A1-12 — Capability-discovery READ endpoint** [R1]: for deterministic soft-slash launchers (gate-038 deferral).
27. **FR-A1-13 — Cataloged create-matter capability** [R19]: a Binding + prompted Action like create-task; OutcomeCard links target the created matter.
28. **FR-A1-14 — ADR-041 authoring** [D-F0+D-F1+D-F2]: "Judgment, Confirmation & Completion Policy" — principle-level (D-F0 doctrine preamble + D-F1/D-F2 policy tables). Proposed at spec → Accepted at G-R2-A (mirror the 039/040 promotion-gate pattern; concise `.claude/adr/` + full `docs/adr/`).

### Gate G-R2-B — Memory (Area 2)

29. **FR-B-01 — Spaarke Memory Service (TWO active scopes + ledger + interfaces)** [D-M1; refined 2026-07-08 per owner review — see Owner Clarifications]: the active memory scopes are **Record** and **User**; Conversation stays the ledger facade; Organizational + Semantic stay interface-only.
    - **Record memory** — **generic entity scope keyed by `(entityType, entityId)`** (matters, projects, invoices, work assignments, events, documents — NOT matter-only). Holds **derived/synthesized** knowledge that is NOT already a Dataverse field (distilled conclusions, prior-analysis findings, cross-document synthesis, working notes) — the Binder reads live Dataverse fields directly (FR-B-04), so memory never duplicates them. **Generalizes the existing `Services/Ai/Memory/MatterMemoryService` (`MatterMemory`→`RecordMemory`)**: reuse the `MemoryFact` model + ETag/versioning + GDPR erasure + budget-serialization logic; generalize the matter-specific fact types + keying.
    - **User memory** — ONE **general per-user** store keyed by `userId` (preferences, standing facts, drafting style) spanning everything; **NOT per-user-per-matter**.
    - **Store** — a **NEW Cosmos container partitioned by SUBJECT** (`entityId` for record memory, `userId` for user memory), **NOT `/tenantId`** (deployments are customer-dedicated → one tenant per DB → `/tenantId` is a single hot logical partition against the 20 GB cap; subject-partitioning spreads naturally and loses nothing since isolation is the deployment boundary). Cosmos partition keys can't be changed in place, so this is a new container reusing the existing service code — "reuse the logic, not the container" (resolves Q4). Reconciled at task 050.
    - **Insights direction (named, wiring deferred)**: the Insights Engine today computes insights on demand + TTL-caches them (`InsightsPlaybookExecutionCache`) with NO durable store. Record memory is shaped to become that durable store (a fact may carry `source: insights-engine`); **wiring the Insights Engine to write into it is a follow-on**, not core-r2 — the envelope must not preclude it.
30. **FR-B-02 — MemoryFact / envelope (generic entity keying)** [D-M1]: extend the existing `MemoryFact` with governance fields — `scope` (record|user), `subjectType/subjectId` (or `userId`), `source (+sessionId/turnId provenance)`, `confidence`, `sensitivity`, `expiration`, `deletionPolicy`, `retentionClass`, `created/updated/createdBy` (reuse existing `Type/Key/Value/Source/ConfirmedByUser/Confidence/RecordedAt`). Structured objects, NOT embeddings. Live-doc migration: tolerant-reader defaults for existing facts. (`sourceTrustLevel` field may be present but its **enforcement is deferred** — see FR-B-08.)
31. **FR-B-03 — Memory governance** [D-M1; **RESCOPED TO MINIMAL, operator ruling 2026-07-09** — "at this stage we only need minimal"]: user-visible **review/delete surface** for user memory; record-memory read honoring the record's authorization (read access to the record ⇒ read its memory — structural, no parallel ACL); GDPR erasure preserved; retention = `retentionClass` → per-item Cosmos `ttl` at write (no reaper/custom expiry machinery); minimal audit on write/delete via the existing AuditLogService (ids/counts only). `sensitivity`/`deletionPolicy` persist as **inert fields** (no enforcement machinery); deletion = simple point-delete (no cascade subsystem). Memory items are ADR-015 Tier 3 (user-owned, GDPR-erasable). **DEFERRED (owner ruling 2026-07-08): litigation-hold interaction** → separate governance project.
32. **FR-B-04 — Context Binder + ContextEnvelope assembly** [D-M2]: assemble ONE `ContextEnvelope` per turn; cache-stable (stable-prefix slices — identity, schema cards, environment facts, preferences — precede the volatile ledger tail). Generalize the SIX R1 primitives: `BuildLedgerOutputsContext` → Memory.Conversation (+ fresh-retrieval policy, FR-B-07); host-identity line → Business; host-record schema card + per-table write contracts + lookup metadata → Business (Dataverse metadata as first-class context, assembled once); `BuildCurrentDateDirective` → Environment facts; caller-contact resolution → User (FR-B-06); gate-outcome persistence → Memory.Conversation.
33. **FR-B-05 — ContextEnvelope token budgets** [D-M2, F-2/Amendment 6]: per-slice budgets (Environment ≤50, User ≤300, Business ≤1,200, Record memory ≤600, Conversation ≤2,000; **envelope ceiling ≤4,200**) — **starting estimates fixed against FR-P0-02 measurement**. Rules: full document text never copied unless the invoked capability requires it; ledger entries travel as references; per-slice counts logged per turn (identifiers/counts only, NFR-07); **a budget breach fails the eval run**.
    - **FR-P0-02 measurement (task AIR2-002, 2026-07-08) — estimate vs. measured**: see [`notes/prompt-assembly-baseline.md`](notes/prompt-assembly-baseline.md) for the full per-slice table. Summary on a representative record-context turn: **Environment measured 111 vs. ≤50 estimate (EXCEEDS by +122%, deterministic/every-turn — `BuildCurrentDateDirective` alone)**; **Business measured 1,118 vs. ≤1,200 (at/near ceiling — two unconditional directives, `SideEffectHonestyDirective` 779 + compact-formatting 189, consume 81% of budget before any playbook content, and bypass the shared `IPromptBudgetTracker`)**; Record-memory measured 157 vs. ≤600 (comfortable margin); Conversation measured ~620–970 on this normal turn vs. ≤2,000, but **structurally unbounded up to ~8,000** (`ChatHistoryManager.BuildLedgerOutputsContext`'s `MaxContextOutputs=8 × MaxContextPayloadChars=4,000` ceiling sits entirely outside the budget tracker). Task 054 MUST reconcile Environment + Business against the measured floor and decide the Conversation/ledger-context tracker-wiring question before finalizing these budgets as binding.
34. **FR-B-06 — Caller-contact self-assignment resolution** [R17, D-M2 User slice]: deterministic server-side claims→contact resolution so "assign to me" stops being model guesswork.
35. **FR-B-07 — Portfolio fresh-retrieval bias** [R18, D-M2]: portfolio-/aggregate-level questions bias to FRESH queries, never extrapolation from a prior turn's result (Memory.Conversation retrieval policy).
36. **FR-B-08 — Memory writes are AI-initiated, silent, provenance-tagged** [D-M3; refined 2026-07-08 per owner ruling]: `memory.write` is a **low-friction, AI-initiated** capture — the assistant persists salient facts as a normal part of operating, **no confirmation dialog and no explicit user instruction required** (automatic memory IS the value proposition; requiring "save this" defeats the feature and no user will do it). Two lightweight controls are KEPT because they are near-free and forward-compatible: (1) **provenance on every item** — `source` (user | ai-derived | insights-engine), `bindingId`/`ledgerRef`, and a `trustLevel` tag, written at capture time as **metadata, NOT a gate** — so future hardening needs no data migration; (2) the **user review/delete surface** (FR-B-03) is the product-appropriate control + undo. **DEFERRED (owner ruling): untrusted-origin ban, `trustLevel` enforcement, poisoning evals, semantic-retrieval boundary, litigation-hold** → separate governance project. **Interim defense-in-depth (already present, not new work): content-safety/PromptShield on inputs + memory scope-isolation (per-record/per-user) + the user delete surface.** *Accepted residual risk (recorded): document-injection poisoning persisting across sessions is accepted for this project, covered in the interim by the above; full hardening tracked as a deferral.* Consumers (Compose r2 FR-30) persist AI-derived insights via this path with provenance — no gate.
37. ~~**FR-B-09 — Semantic-retrieval ↔ memory trust boundary**~~ — **DEFERRED (owner ruling 2026-07-08)** to a separate governance project (hard governance rule; not core-r2). Provenance tagging + the user review/delete surface (FR-B-08 / FR-B-03) are the interim controls (the explicit-only floor was removed 2026-07-08).
38. ~~**FR-B-10 — Memory-poisoning eval families**~~ — **DEFERRED (owner ruling 2026-07-08)** with the governance rules above. (Resourcefulness + origin-classification eval families remain in scope; memory-poisoning families move to the governance project.)
39. **FR-B-11 — Organizational-scope provider interface** [D-M1, F-7]: **read-only INBOUND** interface (Spaarke receives organizational context; Work IQ = named candidate provider). Interface ships; runtime integration + the researcher spike are **deferred** (owner ruling; see Deferrals). Outbound Spaarke-as-MCP-server is explicitly out.
40. **FR-B-12 — Semantic-scope provider interface** [D-M1]: interface over the EXISTING Azure AI Search + SPE retrieval (implementation exists; honors the our-AI-Search-not-Dataverse-search ruling).
41. **FR-B-13 — Workspace-intelligence precursors** [D-M4]: next-step chips on OutcomeCards (FR-A1-06) + record-scope memory items (FR-B-01). Full goal-tracking is a named deferral, NOT a subsystem here.
42. **FR-B-14 — Matter-level retrieval ACL verification spike** [R22]: bounded read-only spike — are user-level matter walls enforced in the AI Search filters at RETRIEVAL time (load-bearing ethical-wall control) or only at history-sanitization? If a gap exists it escalates as security-sensitive (own project, not core scope). Escalation path pre-declared.
43. **FR-B-15 — ADR-040 inline size-cap enforcement home** [R5]: take r1's ruling; if r2 owns it → memory/ledger hardening in this gate.
44. **FR-B-16 — ADR-042 authoring** [D-M1+D-M3]: "Memory Architecture & Governance" — the two active scopes (Record `(entityType,entityId)` + User `userId`) + ledger + interfaces; **subject-partitioning rationale** (dedicated-env / no `/tenantId`); governance envelope; the AI-initiated + silent + provenance-tagged write posture (NO write-gate — refined 2026-07-08, explicit-only floor removed) + the deferred hard-governance boundary; erasure semantics (ADR-015 Tier 3); the not-a-parallel-session-cache rule; the Insights-Engine-as-consumer direction. Proposed at spec → Accepted at G-R2-B.

### Gate G-R2-D — Hardening (Area 3)

45. **FR-D-01 — Publish-size verification** [NFR-01]: per-BFF-touching-task `dotnet publish` measurement; report absolute + diff; ceiling **≤60 MB compressed**; escalation thresholds (≥+5 MB single-task, ≥55 MB cumulative, ≥60 MB HARD STOP). Baseline ~46.8 MB at r1 G-P3-close.
46. **FR-D-02 — Eval-suite-green merge gate** [NFR-02]: golden-utterance + resourcefulness + origin-classification families all green as a CI merge gate; budget-breach-fails-eval (FR-B-05). (Memory-poisoning families deferred with FR-B-10.)
47. **FR-D-03 — Cross-satellite seam-fork verification**: verify no satellite (Compose r2, Insights refurbish) forked an AI-internal seam; the core is the only project modifying `Services/Ai/` internals. Enforced via hot-path registry + `/conflict-check` + a grep/NetArchTest check.
48. **FR-D-04 — Track-B hygiene sweep** [R9, R11]: `Task.Delay → TimeProvider` probes; `Refresh-ScopeModelIndex.ps1` drift + dead App Service env keys.
49. **FR-D-05 — Audit-container partition re-key** [R21]: re-key the permanent audit container off bare `/tenantId` (hierarchical or time-bucketed) while small, to avoid the Cosmos 20 GB logical-partition cap. Cheap now.
50. **FR-D-06 — Legacy workspace tools verdict** [R4, contingent]: if r1 P4 didn't finish — re-point or retire Get/Update/Close Workspace Tab + 4 artifact variants on the orphaned `IWorkspaceStateService`.
51. **FR-D-07 — Orphan verification** [R12, contingent]: verify the DAILY-BRIEFING-NARRATE + `spaarke-playbook-embeddings` orphans on spaarkedev1 are closed by r1 P4.

---

## Non-Functional Requirements

- **NFR-01 — Publish size** ≤60 MB compressed, per-task verified (FR-D-01).
- **NFR-02 — Eval-green merge gate** across all families (FR-D-02).
- **NFR-03 — Untrusted-input posture** — memory items are provenance-tagged (FR-B-08); interim defense = content-safety + scope-isolation + user delete surface; OBO-everywhere unchanged. (Full untrusted-origin memory ban deferred to the governance project.)
- **NFR-04 — Prompt-cache stability** for ContextEnvelope (stable prefix, budgeted slices; beyond-window recall stays tool-call per ADR-040).
- **NFR-05 — Per-slice token budgets** with **breach-fails-eval** (FR-B-05).
- **NFR-06 — Memory governance completeness** — governance envelope fields present (retention/expiration/sensitivity/provenance/deletion); user review/delete surface shipped. (Litigation-hold + `sourceTrustLevel` enforcement deferred.)
- **NFR-07 — No-content telemetry** — trace/fingerprint/budget logging carries identifiers/counts only, never content.
- **NFR-08 — UI-action ack coverage** — every UI-claiming tool is ack-gated or fails honestly.
- **NFR-09 — OutcomeCard coverage** — every side-effect path yields an OutcomeCard, including async job states.
- **NFR-10 — Refusal-affordance coverage** — no refusal/block ships without an actionable affordance.
- **NFR-11 — Contract-first coverage** — no A0-gated feature merges without its contract + contract test (§3.4).
- **NFR-12 — Ingestion-parity invariant** — a bare `sprk_document` row is never a successful document operation; verified by gate policy + JobAwareCompletionState (design §8 R-2).
- **NFR-13 — Grep-zero retirement verification** for anything retired.
- **NFR-14 — Latency budgets** carried from r1 (Binder runs inside the turn).

---

## Technical Constraints

### Applicable ADRs

- **ADR-039** (grounded execution, closed catalogs) — **Accepted, binding**. Risk factors are catalog-declared data; no second intent mechanism.
- **ADR-040** (session ledger) — **Accepted, binding**. Conversation memory = ledger facade; no parallel session cache; storage-before-render.
- **ADR-013** (AI facade discipline, refined 2026-05-20) — CRUD consumes AI only via `Services/Ai/PublicContracts/`.
- **ADR-037** (amended) — section-keyed streaming for progressive render.
- **ADR-015** — memory items are Tier 3 (user-owned, GDPR-erasable).
- **ADR-029** — publish-size governance.
- **ADR-032** — Null-Object kill-switch for any feature-gated service.
- **ADR-038** — testing pyramid; contract tests are a KEEP path; eval-green stays a gate.
- Standing set: 008, 009/014, 010, 016, 018, 019, 028, 030, 031, 036.

### MUST Rules

- ✅ MUST build ON ADR-039/040 — every mechanism expressible as catalog rows, ledger entry types/readers, gate-engine policy, context assembly, or client rendering of stored entries.
- ✅ MUST make side effects deterministic; initiative on reads is free (D-F0(b)).
- ✅ MUST store before render (ADR-040).
- ✅ MUST use structured memory objects, not embeddings, for User/Record scopes.
- ✅ MUST partition the memory container by SUBJECT (`entityId`/`userId`), never `/tenantId` (customer-dedicated deployments).
- ✅ MUST key Record memory generically by `(entityType, entityId)` — not matter-only.
- ✅ MUST publish seams FIRST (Phase A0) so Compose r2 is never blocked.
- ✅ MUST sequence the triple-twin hoist (FR-A-01) BEFORE any catalog-row task.
- ❌ MUST NOT create a second dispatch protocol, a parallel session cache, or routing config outside the Binding table.
- ✅ MUST tag every memory item with provenance (`source`, `bindingId`/`ledgerRef`, `trustLevel`) at capture time. Memory writes are **AI-initiated and silent** (no write-gate); the user review/delete surface is the control. [Full untrusted-origin hardening deferred to the governance project.]
- ❌ MUST NOT weaken a gate or hard block via D-F0 — the degradation ladder operates BELOW the side-effect line.

### Existing Patterns to Follow

- Reasoning Runtime = `SprkChatAgentFactory` + `SessionDispatchOrchestrator` + gate store (FORMALIZED, not rebuilt).
- Seam facades: `Services/Ai/PublicContracts/`.
- ADR promotion gate: mirror the ADR-039/040 Proposed→Accepted pattern.
- Catalog authoring: `jps-action-create` + `jps-validate`, mirror-first via `infra/dataverse/inputschemas/`, through the FR-A-01 hoisted source.

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-040 | "No parallel session cache" | The Memory Service adds Record/User scopes above the session | **C — Comply** | Resolved by construction: Conversation scope = ledger facade; Record/User are a DIFFERENT concern (cross-session governed objects, subject-partitioned), not a session cache. State explicitly in code + ADR-042. |
| ADR-040 | "Disposition is the only rendering contract" | The new `compose` disposition member | **C — Comply** | Extending the ENUM is the compliant move; the core owns the extension, Compose r2 consumes it. |
| ADR-039 | "Closed catalogs; one intent mechanism" | Policy v2 risk tiers + memory-write side-effect class | **C — Comply** | Risk factors are catalog-declared DATA extending `side_effect_class` — NOT a runtime intent mechanism. Any model-judged risk classification would violate this; explicitly banned in FR-A1-03. |
| D-F0(b) "reads always free" | ADR-039 budget-8 loop bound | (no tension) | **C — Comply** | The budget stays; the doctrine changes model WILLINGNESS within the budget, not the bound. |

> No other tensions anticipated. Policy v2 refines gate behavior WITHIN the one-gate rule. ADR-041 + ADR-042 are new (promotion-gated), not amendments to existing ADRs.

---

## Success Criteria (browser-UAT-gated per design §4)

Every gate is verified by an **operator-executed browser UAT script on spaarkedev1** (r1 rule verbatim — a passing curl or green test never satisfies a gate).

1. [ ] **G-R2-A (Judgment + Friction)** — "create a follow-up task due Friday, assign it to me" → created with **no dialog** (explicit+complete Tier 2b), ✅ + clickable record chip + next-step chips. An ambiguous/inferred write confirms **exactly once, one modality**. A partially-blocked request → verify state, do what's possible, hand over the rest (extracted values + prepared content + working deep link, e.g. the doc block links the Document Upload page). Every claimed UI action backed by a real client event; failures render ❌ with the real reason; "how did you decide?" opens the traceability view with live plan narration; long outputs render progressively. *Verify by: the 10-scenario E2E band + browser script.*
2. [ ] **G-R2-B (Memory)** — open the assistant on a record (any of matter/project/invoice/work-assignment/event/document) and it already knows: user identity, host record (name/id/schema), earlier-in-conversation, record-scoped derived facts, standing user preferences — **without re-prompting**. Preferences stated once persist across sessions. Memory is captured **automatically** (AI-initiated, no "save this" step); the user can **see + delete** any item; every item is provenance-tagged. *Verify by: browser script (memory-poisoning eval families deferred to the governance project).*
3. [ ] **G-R2-D (Hardening)** — everything above is reliable, telemetered, eval-gated, publish-size verified, on a codebase not larger than r1 left it; cross-satellite verification confirms no forked AI-internal seam. *Verify by: CI merge-gate green + publish-size report + seam-fork check.*
4. [ ] **ADR-041 + ADR-042** Accepted at their respective gates (promotion-gated like 039/040).
5. [ ] **Contract-first** — all seven A0 contracts have a contract test; no A0-gated feature merged without it (NFR-11).

---

## Dependencies

### Prerequisites

- r1 P4 close (contingent §10 rows re-checked at FR-P0-01); if P4 slips, FR-P0-01 is the reconciliation task.
- Existing R7 W12 LinearConsumers + Job Contract / `ServiceBusJobProcessor` (consumed by FR-A0-07).
- New Cosmos container in the existing account (FR-B-01), **partitioned by subject** (`entityId`/`userId`) — one new Azure dependency; reuses `MatterMemoryService` code, not its container.

### External / Cross-project

- **Compose r2** (`projects/spaarkeai-compose-r2/`) consumes core seams (already re-based onto them, 2026-07-08). Core owes: seam contracts published Phase A0 FIRST; D-F3 ack plumbing; Policy v2 Tier-2c classification for save-back/creation writes; OutcomeCard rendering for Compose side effects. `/conflict-check` + `projects/INDEX.md` enforce at PR time.
- **Daily Briefing remediation** — separate project (out of scope here); it will consume the GroundednessCheck threshold→action pattern.
- **Insights Engine Widget refurbish** — separate satellite after core Phase A; consumes OutcomeCard + ContextEnvelope.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Compose r1 | Absorb into Compose r2? | **No — keep separate** (r1 was a full executed+closed project, not "never started") | design §0.3 C-1/C-2 corrected; §12 Q1 CLOSED; no absorb/archive |
| AnalysisWorkspace | Freeze + retire-at-parity? | **Yes** | Core enforces freeze (catalog governance); Compose r2 executes hard-cutover deletion (out of core scope) |
| Policy v2 tier line | Auto-execute line? | **Sub-tier split** (ruling R-1): 2a/2b auto-execute explicit+complete; 2c preview/confirm; Tier 3+ always dialog; injection-suspect wins | FR-A1-03 tier table |
| Memory store | Cosmos? | **Yes** — new container, existing account; nothing memory-shaped in Dataverse | FR-B-01 |
| Work IQ | Interface + spike? | **Interface as FR; spike DEFERRED** | FR-B-11 ships interface; spike → Deferrals |
| Compose↔core seams | Reconcile divergence? | **Core keeps FULL seam set**; Compose re-bases onto it (already done 2026-07-08) | FR-A0 seam set unchanged; NFR-12 |
| Daily Briefing | In this spec? | **No — separate project**; notate only | Out of scope; no Briefing FRs |
| Memory scope model | Five scopes, matter-centric? | **No — TWO active scopes**: Record (generic `(entityType,entityId)`) + User (general per-user, NOT per-matter). Generalize existing `MatterMemoryService` | FR-B-01/02 rewritten |
| Memory partition key | `/tenantId`? | **No — partition by SUBJECT** (`entityId`/`userId`); dedicated-per-customer envs make `/tenantId` a single hot partition | FR-B-01; MUST rule added |
| Stored record memory | Needed / expensive? | **Yes, and cheap** (small JSON, point-read). Holds derived knowledge (not Dataverse duplicates) | FR-B-01 |
| Insights Engine | Memory as its source? | **Direction adopted; wiring DEFERRED** — Insights currently TTL-cached, no durable store; Record memory shaped to become it | FR-B-01 note; Deferrals |
| Memory governance-security | In this project? | **No — DEFERRED**: untrusted-origin ban, semantic-retrieval trust boundary (FR-B-09), litigation-hold, memory-poisoning evals (FR-B-10). **Memory writes are AI-initiated + silent (automatic memory = the value prop); the explicit-only floor was REMOVED as over-engineered.** Interim = provenance tags + content-safety + scope-isolation + user delete surface | FR-B-08/09/10, FR-B-03 |

## Assumptions

*Spec-time engineering decisions taken with stated defaults (design flags each as a spec-time call; operator may override):*

- **Token budgets**: the §D-M2 numbers are STARTING estimates; FR-P0-02 measures r1's actual assembly first and fixes them against measurement.
- **Undo expiry semantics** (2a/2b Undo chips): spec-time decision; default to a bounded TTL declared per compensating action — to be set during FR-A1-03.
- **Tier 2c preview UX**: r2 minimum is preview/confirm; the full preview surface is revisited post-G-R2-A.
- **Exact ≥90% eval thresholds**: the eval-family notes set floors + shape; concrete integers ratified during FR-A1-02/FR-A1-04.
- **Progressive render mechanism** (FR-A1-10): section-keyed streaming preferred; client-reveal fallback — chosen at implementation time.
- **Wave/`/goal` structure**: pre-authored `/goal` conditions per wave IF the r1 pilot is judged proven at r1 close (`notes/goal-feature-evaluation.md`); `/goal` never wraps a gate. Governed at plan/task time by the merged **CLAUDE.md §8.5** (Sonnet-5 execution model tiering + `/goal` wave-loop eligibility: machine-verifiable end-state, ≥3 low-ambiguity tasks, never security/deploy/irreversible).
- **Execution model tiering (plan-time, not spec-time)**: per CLAUDE.md §8.5, `task-create` assigns each task a `<model-tier>` (sonnet default; opus/fable for high-blast-radius / architectural / ADR-migration / security work) + `<effort>`. Expect this project's **contract, gate-engine, memory-governance, and ADR-041/042 tasks to tier UP to opus/fable**; mechanical/catalog-row/test-repair tasks stay sonnet. Assigned by `/project-pipeline`, not here.

## Deferrals (filed via `/defer` at close)

- **Work IQ / Foundry IQ researcher spike + runtime providers** (owner ruling — interface ships, spike deferred).
- **Workspace-intelligence goal-tracking subsystem** (D-M4 — precursors only in r2).
- **Admin observability dashboards** (carried from r1).
- **Spaarke-as-MCP-server outbound surface** (separate architectural seam).
- **Memory hard-governance rules → separate governance project** (owner ruling 2026-07-08): full untrusted-origin ban + `trustLevel` enforcement, semantic-retrieval↔memory trust boundary (FR-B-09), litigation-hold interaction, memory-poisoning eval families (FR-B-10). *Interim controls: provenance tags + content-safety/PromptShield + scope-isolation + user delete surface. Accepted residual risk (operator decision): document-injection poisoning persisting across sessions.*
- **Insights-Engine → Record-memory write wiring** (direction adopted this project; wiring is a follow-on — Record memory's envelope must not preclude `source: insights-engine`).

## Unresolved Questions

- [ ] **FR-P0-01 contingent rows** — resolve §10 rows 4/5/6/8/12 against actual r1 P4-close state at `/project-pipeline` time. Blocks: FR-D-06, FR-D-07, FR-B-15 final disposition.
- [ ] **Cosmos container naming + partition strategy** — FR-B-01 partitions by subject (`entityId`/`userId`); coordinate with FR-D-05 (audit re-key) so both adopt the subject/scalable-key pattern from day one. Blocks: FR-B-01 store implementation.
- [ ] **`MatterMemory` live-doc migration** — existing `memory` container holds matter-keyed docs on dev; decide migrate-vs-fresh-container-and-leave-legacy at task 050 (partition key can't change in place). Blocks: FR-B-01/02 store cutover.

---

## Hot-Path Declaration (design §11)

`<hot-path-declaration>` BFF=**Y** · SpaarkeAi=**Y** · ci-workflows=**N** · skill-directives=**Y** · root-CLAUDE.md=**N** `</hot-path-declaration>`

## Placement Justification (CLAUDE.md §10)

Memory Service, Context Binder, Completion Engine, gate-policy extension, and the seam surface live in `Sprk.Bff.Api` — same ADR-013 criteria as r1 (latency + transactional coupling with session/SSE state; the Binder runs inside the turn). New Azure dependency: one Cosmos container (existing account). Compose endpoints live in the Compose r2 project's own placement justification. Publish-size per-task verification continues (NFR-01).

---

*AI-optimized specification. Original design: `design.md` v0.4. Pre-spec inputs: `notes/d-f0-eval-family-spec.md`, `notes/policy-v2-origin-classification-decision-tree.md`. Validated 2026-07-08 against merged procedures (CLAUDE.md §8.5 Sonnet-5 tiering + `/goal`; AIP layer retired) — no requirements change; plan-time execution-tiering note added to Assumptions.*
