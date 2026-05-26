# Test Data Requirements
## How to prime Spaarke's AI capabilities for demos, validation, and edge-case testing

> **Date**: 2026-05-20
> **Status**: Working design document
> **Owner**: ralph.schroeder@hotmail.com
> **Purpose**: Define a coherent strategy for generating, seeding, and maintaining test data across the demo, validation, and edge-case dimensions of Spaarke's AI capabilities. Without this, the Insight Engine has no signal, the Precedent Board is empty, and demos start cold.
> **Companion documents**: [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md), [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md)

---

## 1. Purpose and scope

The Insight Engine, Action Engine, Precedent Board, and most of the Lavern-derived patterns are **data-dependent**: they only produce visible value when the system has accumulated enough matters, observations, precedents, and historical outcomes to reason across.

A fresh Spaarke environment with no data:
- Cannot run Mode 3 (Conversational cross-matter inference) — IFindComparableMatters returns nothing
- Cannot run Mode 4 (Precedent curation) — no Tentative Precedents to confirm
- Cannot demonstrate Mode 6 (Background digests) — nothing to digest
- Cannot demonstrate Mode 2 (Proactive triage) — no historical pattern signal to inform triage decisions
- Cannot show the Precedent Board lifecycle (decay, drift, promotion) — no temporal spread

This document specifies how to bootstrap each layer of data and how to refresh it for ongoing validation.

It does **not** cover production data migration, customer onboarding, or anonymization of real client data — those are separate workstreams.

---

## 2. The three test data needs

Each has different volume, fidelity, and lifecycle requirements.

| Purpose | Volume | Fidelity | Refresh cadence | Audience |
|---|---|---|---|---|
| **Demo to prospects + execs** | Small, high-quality (~50 matters, ~200 docs) | Polished, looks real, no AI tells, no profanity, no sensitive content | Per-release; stable between releases | Sales, execs, prospects, marketing |
| **Validate engines end-to-end** | Medium (~500 matters, ~2000 docs) | Realistic but synthetic acceptable; spread across practice areas | Regenerated per major release; persisted in dev env | Engineering, QA, design |
| **Stress + edge-case test** | Small targeted fixtures (~50 cases) | Adversarial, weird, broken, malformed | Updated as new edge cases discovered | Engineering, security, QA |

Failure to separate these leads to: demos that break because validation data has gaps; validation suites that pass because demo data is too clean; edge cases that hide because they're mixed into demo flows.

---

## 3. Strategy by data layer

Six layers. Each maps to specific Spaarke storage targets.

### Layer A — Reference data (cross-tenant, system-owned)

**Storage**: Azure AI Search `spaarke-reference-clauses` index; Dataverse `sprk_clausetype` taxonomy

**Source**: Public legal NLP datasets identified in [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md) §5.9 and §16:

| Dataset | Content | License | Phase |
|---|---|---|---|
| CUAD | 510 commercial contracts × 41 clause types | CC BY 4.0 ✅ | Phase 1 |
| MAUD | 152 merger agreements × 92 deal points | CC BY 4.0 ✅ | Phase 1 |
| ACORD | Clause retrieval IR benchmark | CC BY 4.0 ✅ | Phase 2 |
| UNFAIR-ToS | 5.5K ToS sentences × 8 unfairness labels | CC BY-SA 4.0 ⚠️ | Phase 3 (legal review) |
| LEDGAR | 60K contract provisions × 98 clause types | CC BY-SA 4.0 ⚠️ | Phase 3 (legal review) |

**Status**: solved by Pattern #9 in the analysis doc. Nothing to invent. Implement the seed-data ingestion job; gives us the document corpus and CUAD's 41-clause taxonomy "for free."

**Volume**: ~tens of MB Phase 1; larger if Phase 3 proceeds.

**Refresh**: quarterly check upstream for dataset updates; otherwise stable.

### Layer B — Spaarke business records (matters, parties, closures)

This is the hard layer. We need realistic legal-ops business records that drive cross-matter Inference and Precedent formation.

**Storage**: Dataverse — `sprk_matter`, `sprk_party`, `sprk_matter_party` (associative), `sprk_closure`, `sprk_matter_document` (links to documents)

**Required volume** (validation tier):
- 500 fake matters
- 50–100 recurring fake parties / counterparties
- Closure outcomes with realistic distributions (e.g., 60% settled, 30% withdrawn, 10% litigated)
- Each matter linked to 1–5 documents
- 3–4 year temporal spread for decay logic testing

**Recommended generation approach — hybrid: CUAD-anchored matters + targeted clusters**

#### B.1 CUAD-anchored matter generation

Write a generator script that creates a fake Spaarke matter for each CUAD contract:

- **Matter name**: derived from CUAD contract title pattern, e.g., "Acme Corp – Software License – 2024"
- **Parties**: extracted from CUAD's party annotations
- **Practice area**: inferred from contract type (CUAD has these labels)
- **Matter opened date**: distributed across last 4 years
- **Closure outcomes**: synthesized with realistic distributions
- **Documents**: linked to the underlying CUAD PDF (already in the reference index)

This gives us **510 plausible matters** anchored to real document content. Inferences run against them will have actual clause text to cite.

#### B.2 Targeted matter clusters

Hand-design ~20 matter "clusters" that exercise specific Precedent patterns. Each cluster: 5–8 matters with a deliberate shared pattern. Examples:

| Cluster | Pattern | Purpose |
|---|---|---|
| BigCorp counterparty cluster | 8 matters w/ BigCorp, all with extended cure period (15→30 days) | Drives a Tentative→Confirmed Precedent for demos |
| Software M&A earnout cluster | 6 matters w/ earnout disputes, settling at 60–70% | Drives Mode 3 demo query |
| SaaS indemnity carve-out cluster | 7 matters w/ AI training data carve-outs | Demonstrates emergent Precedent |
| Acme termination cluster | 5 matters with same counterparty, escalating termination disputes | Demonstrates drift detection (introduce contradicting outcomes) |
| ... | ... | ... |

Each cluster has explicit "demo intent" — what it's meant to enable.

#### B.3 Counterparty repetition

Ensure 10–15 counterparties recur across multiple matters. Without repetition, the Insight Engine can't find comparable matters keyed on counterparty.

Suggested counterparty pool (sized to drive realistic clusters):
- 5 "frequent flyer" counterparties (8–15 matters each)
- 10 "regular" counterparties (3–7 matters each)
- 30 "one-off" counterparties (1–2 matters each)

#### B.4 Temporal spread

Distribute matter dates across 3–4 years. Specifically:
- ~10% of matters: >3 years old (drives decay logic + stale Precedent demo)
- ~30%: 1–3 years old (drives Confirmed Precedent demos)
- ~50%: 3 months to 1 year old (recent activity)
- ~10%: <3 months old (active demo content)

### Layer C — Precedent Board seed state

Don't make demos start from empty. Pre-seed the `sprk_precedent` table with a curated set tied to Layer B clusters.

| State | Count | Purpose |
|---|---|---|
| Confirmed | 15–20 | Citable in Mode 3 inference responses immediately |
| Tentative (ready for confirmation) | 4–5 | Drive Mode 4 curation demo |
| Tentative (below threshold, still accumulating) | 5–8 | Show "in progress" patterns |
| Under Drift Review | 1–2 | Show drift detection UI |
| Stale (decaying) | 5–10 | Show decay UI in Precedent management |
| Deprecated | 3–5 | Show lifecycle terminus |

Each Confirmed Precedent must have its full supporting Observation chain wired up — without supporting Observations, "Confirmed" looks like a configuration value, not a derived state.

### Layer D — Action Engine demo manifests

10–15 pre-built Action manifests covering the journey types from [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md):

| Manifest name | Mode | Use case |
|---|---|---|
| Vendor Contract Triage | 2 | Inbox-driven contract intake |
| Weekly Precedent Digest | 6 | GC weekly briefing |
| M&A Earnout Outcome Tracker | 6 + 3 | Cross-matter portfolio scan |
| High-Risk Indemnity Escalation | 2 | Standing risk monitor |
| Approaching Termination Notice Scanner | 6 | Schedule-driven |
| Counterparty Behavior Drift Monitor | 6 | Detects counterparty position shifts |
| New Matter Conflict Check | 1+2 | Intake conflict screening |
| KYC Workflow for New Counterparty | 1+2 | Onboarding |
| Quarterly Risk Pattern Report | 6 | C-suite reporting |
| Client Inquiry Auto-Triage | 2 | Inbound email routing |

Each ships pre-configured but **disabled**. Users can enable, customize, or use as templates. Storage: JSON files in the repo plus deployment via standard JPS publishing pipeline.

### Layer E — Pending approvals

Pre-create 3–5 GateApproval records in Pending state so demos start with a populated approval queue.

| Approval | Gate type | Surface hint | Purpose |
|---|---|---|---|
| Vendor MSA escalation (uncapped indemnity) | EthicsCritical | Workspace | Mode 2 demo |
| Drafted indemnity deviates from standard cap | FinalDelivery | Word | Mode 5 demo |
| New Tentative Precedent ready for SME confirmation | Custom (PrecedentConfirmation) | Workspace | Mode 4 demo |
| Conflict-check found possible adverse interest | EngagementAcceptance | Teams | Mode 2 demo |
| Action Engine workflow modified — requires partner sign-off | Custom (WorkflowChange) | Workspace | Governance demo |

### Layer F — Adversarial / edge-case fixtures

For validation, not demo. Smaller, targeted, kept separate from demo data.

| Fixture category | Count | Tests |
|---|---|---|
| Prompt-injection documents (zero-width unicode, hidden HTML comments, ANSI escape attacks) | 3–5 | Sanitizer (Pattern #10) |
| Matters with `<12` comparables for predict-cost queries | 3–5 | DeclineToFind (Pattern #7) |
| Precedents with deliberately conflicting outcomes | 3–5 | Drift detection (Pattern #1) |
| Documents with broken or fabricated citations | 3–5 | GroundingVerifier (Pattern #3) |
| Multi-language documents (EN + JA, EN + DE, etc.) | 3 | Parser language handling |
| OCR'd scans with character errors | 3 | Parser robustness |
| Tables-heavy PDFs (term sheets, fee schedules) | 3 | Table extraction |
| Empty / near-empty documents | 2 | Empty-input handling |
| Documents exceeding 10 MB (lavern's cap reference) | 1 | Size-cap behavior |
| Documents with malformed character encoding | 2 | Encoding handling |
| Matters in Tentative→Confirmed promotion right at threshold | 2 | Promotion gate boundary |
| Matters causing Precedent score = 0.05 (deep decay) | 2 | Decay boundary |

Each fixture has a corresponding test case in the validation suite. Failures here block release; failures in Layer B do not (just regenerate).

---

## 4. Recommended bootstrap project structure

Create a new dev-time utility — `Sprk.DevData.Seeder` — a console application (or alternatively an Azure Function) that:

```
sprk-seed-data
  --target=dev|demo|test
  --layer=reference|business|precedents|actions|approvals|adversarial|all
  --force                 # re-seed even if already present
  --counterparties=15     # how many recurring counterparties
  --matters=500           # how many fake matters
  --year-span=4           # temporal spread
  --cluster-set=standard  # named cluster configurations
```

**Design principles** (mirrored from lavern's `seed-knowledge-base.ts`, Pattern #9):

- **Idempotent**: re-running without `--force` is a no-op when data already present
- **Per-environment**: refuses to run against `prod` target — strict environment separation
- **Per-layer**: re-seed just one layer when iterating on a feature
- **Logging**: every record created/modified is logged with a correlation ID for easy cleanup
- **Cleanup**: optional `--purge` flag to wipe seeded data without affecting real records (uses correlation ID + system user pattern)
- **Cluster configurations**: named sets of cluster definitions checked into the repo as JSON, so demo data is reproducible across environments
- **Fast**: full seed of 500 matters + Layer C/D/E should complete in <10 minutes

**Storage targets per layer**:

| Layer | Target | Mechanism |
|---|---|---|
| A — Reference | Azure AI Search index `spaarke-reference-clauses` + `sprk_clausetype` | Download + parse + bulk-insert |
| B — Business records | Dataverse `sprk_matter`, `sprk_party`, etc. in target env | Web API bulk create with explicit system user |
| C — Precedent state | Dataverse `sprk_precedent` + `sprk_precedent_observation` | Web API bulk create |
| D — Action manifests | JSON files in repo + Dataverse JPS Action records | Standard JPS publishing pipeline |
| E — Pending approvals | Dataverse `sprk_gate_approval` | Web API bulk create |
| F — Adversarial fixtures | Mixed — files in repo + records in test env | Per-fixture; checked into test suite |

**Where this lives in source**: suggest `src/server/tools/Sprk.DevData.Seeder/` as a separate project in the BFF solution; per CLAUDE.md §10 governance it does not run in production.

---

## 5. What NOT to do

- **Don't use LLM-generated fake legal text for the document corpus.** AI-generated contract language has tells (overly-clean formatting, paraphrased standard clauses, no realistic typos or irregular formatting, no idiomatic boilerplate). Stick with CUAD / MAUD for document body text. LLM generation is acceptable for *metadata* (matter names, party names, descriptions, closure notes) but never for document body.
- **Don't anonymize real customer data for demo without explicit DPA-level review.** Synthesizing on top of CUAD is faster, lower risk, and equally convincing.
- **Don't ship demo data into production environments.** The seeder must refuse to run against `prod` target. Per-environment seed lockfiles or runtime checks both work.
- **Don't conflate validation data and demo data.** A perfect demo can hide a real validation gap; a thorough validation suite can produce content too messy or sensitive for a demo. Keep them in separate seed configurations.
- **Don't pre-seed Layer C in a way that bypasses Layer B Observation linkage.** A "Confirmed Precedent" without supporting Observations is a configuration value, not a derived state — it makes the Precedent Board look like a static knowledge base rather than a learning system.
- **Don't let the seeder accumulate undocumented business logic.** Every cluster, every approval, every Action manifest in the seeder should be traceable to a documented purpose (which demo or validation case it serves).
- **Don't rely on the seeder for performance testing.** 500 matters is enough for behavioral validation, not scale testing. Performance test data is a separate concern (millions of records, synthetic but mechanical).

---

## 6. Sequencing — where this fits in the plan

This is a parallel workstream that must precede the Precedent Board v1 (Sprint 6) milestone in [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md) §11. Without populated matters and Precedent seed state, the Precedent Board cannot be tested or demoed.

**Suggested addition to LAVERN-ANALYSIS-AND-PLAN.md §11 sequencing**:

> **Sprint 0.5 — Demo and validation data scaffolding** (parallel to Sprint 1 ADR ratification):
> - Build `Sprk.DevData.Seeder` scaffold with Layers A, B, C wired up
> - Generate 510 CUAD-anchored matters in dev environment
> - Hand-design and check in 20 matter cluster configurations
> - Pre-seed 30+ Precedents across all lifecycle states
> - Output: a fresh demo environment can be spun up in <10 minutes with realistic, demo-ready data

Sprint 0.5 should not block Sprint 1 (ADR ratification), but should be in flight before Sprint 5 (EvaluatorGate) and must be complete before Sprint 6 (Precedent Board v1).

---

## 7. Validation scenarios per interaction mode

Cross-references to [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md). For each mode, the data prerequisites and the validation scenarios it enables:

### Mode 1 — Reactive document review
- **Data prereqs**: Layer A (reference clauses), 1+ matters in Layer B with linked documents
- **Validation scenarios**: standard review, review on document with multiple risk flags, review where Citation verifier strips fabricated quotes, review where EvaluatorGate triggers revision, review that returns DeclineToFind on a sub-claim

### Mode 2 — Proactive monitoring + triage
- **Data prereqs**: Layer D (Action manifests), Layer B (historical matters for Precedent context in cards)
- **Validation scenarios**: webhook fires → sanitization → playbook → gate routed to Teams → approval → matter created; rejected path; auto-reject on timeout; idempotency (duplicate webhook); evidence rendering in card

### Mode 3 — Conversational cross-matter inference
- **Data prereqs**: Layer B (≥12 comparable matters per intended query), Layer C (Confirmed Precedents)
- **Validation scenarios**: query returns Inference with citations; query returns DeclineToFind (insufficient evidence); query returns mix of citations + DeclineToFind on a sub-claim; query with Precedent references; multi-turn refinement

### Mode 4 — Precedent curation
- **Data prereqs**: Layer C state across all six states (Confirmed, Tentative-ready, Tentative-accumulating, Drift Review, Stale, Deprecated)
- **Validation scenarios**: Confirm a Tentative; reject a Tentative; modify a Tentative's text; resolve a Drift Review (re-confirm or deprecate); promote a Stale back to active

### Mode 5 — Drafting with grounded suggestions
- **Data prereqs**: Layer A (CUAD reference clauses), Layer C (Confirmed Precedents)
- **Validation scenarios**: insert clause from Precedent; insert clause from CUAD baseline; insert clause that triggers gate (deviates from standard); insertion preserves citations as Word comments; fallback when no Precedent applies

### Mode 6 — Long-running background insights
- **Data prereqs**: Layer C lifecycle events (promotions, drifts) over time; Layer E (pending items); portfolio metrics aggregate
- **Validation scenarios**: digest renders correctly with all sections populated; digest renders with empty sections gracefully; mobile rendering; actionable links work; opt-out per section persists

---

## 8. Open questions

1. **Where does the demo environment live?** Dedicated Spaarke-hosted Dataverse env, or per-prospect spin-up? Affects seeder design (run-once vs run-many).
2. **What's the refresh strategy when CUAD or MAUD ship upstream updates?** Re-seed all Layer B (breaking matter IDs) vs incremental upsert (preserving IDs)?
3. **Should adversarial fixtures (Layer F) be visible in the demo environment?** Probably no — they distract — but they need to be runnable in a near-identical environment.
4. **Do we want a "reset to clean state" button in the demo environment?** And what does "clean" mean — empty, or restored to last-known-good seeded state?
5. **How do we handle the LEDGAR / UNFAIR-ToS legal review timeline?** If Phase 3 ingestion is blocked indefinitely, do Mode 2/5 demos that need broader clause patterns suffer? Probably fine on CUAD + MAUD alone for v1.
6. **Multi-tenant test data**: when we want to validate per-tenant isolation, do we run the seeder per tenant, or seed a single test env and rely on Dataverse security model? Affects multi-tenant test isolation strategy.

---

## 9. Cross-references

- [`LAVERN-ANALYSIS-AND-PLAN.md`](LAVERN-ANALYSIS-AND-PLAN.md) — patterns, schemas, ADRs, sequencing; §5.9 covers Layer A in detail; §9 covers the schemas Layer B-E populate; §11 sequencing aligns with this doc's Sprint 0.5 callout
- [`ADVANCED-AI-USE-CASE-PATTERNS.md`](ADVANCED-AI-USE-CASE-PATTERNS.md) — the six interaction modes this data must support; §7 cross-references map back here
- [`projects/ai-spaarke-insights-engine-r1/`](../ai-spaarke-insights-engine-r1/) — Insight Engine design referencing matter/observation/inference data shape
- [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) — Action Engine design referencing Action manifests
