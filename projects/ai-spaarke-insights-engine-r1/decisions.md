# Decisions Log — Spaarke Insights Engine (r1)

> **Status**: Living document — decisions added as made, marked superseded when changed.
> **Last Updated**: 2026-05-22 (two-pass update: first pass added D-46–D-51 LAVERN-derived decisions, fixed D-01/D-02/D-03 wording, added LAVERN coordination items, added DEF-12–DEF-15; second pass back-propagated architecture-doc r2 additions D-39–D-45 and DEF-08–DEF-11 to close doc-drift gap and resolved DEF-15)
> **Anchor doc**: read this first; for full rationale see [design.md](design.md) section references and [lavern-pattern-assessment.md](lavern-pattern-assessment.md) for LAVERN-pattern decision basis.

Each entry: `D-XX | Decision | Rationale (one line) | design.md ref or lavern-pattern-assessment.md §`. Decisions are durable until superseded; supersessions are explicit and reference the superseding entry.

---

## Identity

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-01 | Name: **Spaarke Insights Engine** (plural, canonical; internal: `Insights Engine`; code: `Insights*`; project folder: `projects/ai-spaarke-insights-engine-r1/`) | "Engine" honestly scopes — signals in, context out. The Engine **owns** the resolver + synthesis + sync/extraction Functions + Insight Index + Insight Graph + Precedent Board. Source systems (Dataverse, SPE, AI sessions) are **consumed but not owned**; presentation surfaces are **emitted to but not owned**. Distinct from IQ Stack (marketing umbrella). | §1.1 |
| D-02 | Engine boundary by **ownership**: **owns** the resolver + synthesis layer + sync/extraction Functions + Insight Index + Insight Graph + Precedent Board. **Consumes but does not own**: source systems (Dataverse, SPE, AI session storage) as signal inputs — they feed the Engine but are upstream concerns with their own teams, schemas, lifecycles. **Emits to but does not own**: presentation surfaces (pane, ribbon, Outlook, Teams, public API) — they render `InsightResponse` payloads but evolve independently. | Single responsibility; replaceable; independently deployable. Ownership boundary determines what ships when "Insights Engine v2" deploys (Engine components) vs what doesn't (sources and surfaces). | §1.1, §3.1 |

## Conceptual model

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-03 | **Four-artifact taxonomy**: Fact / Observation / **Precedent** / Inference. Every artifact carries this `type`. Phase 1 ships architecture + scaffold for all four tiers (D-A26, D-A27); Precedent lifecycle automation ships in Phase 1.5. | Different trust profiles, stores, APIs, presentation rules. Mixing them is what makes "intelligence" silently dishonest. The 3-tier model lacked a layer above Observations for cross-matter patterns that accumulate evidence and get SME-confirmed as institutional rules — Precedent fills that gap. See D-46 and `lavern-pattern-assessment.md` §3.1 for rationale; LAVERN ADR 10.1. | §2.1 |
| D-04 | **Provenance is the API contract.** Every Observation and Inference carries `evidence[]`. Surfaces that can't render provenance can't display Inferences. | The architectural mechanism that prevents dishonesty by construction. | §2.4 |
| D-05 | Every Observation stores **`producedBy.version`** (e.g., `closure-extraction@v3`). Mandatory field. | Without this, you can't selectively re-extract when extraction playbooks improve. Confirmed by knowledge research. | §2.2 |
| D-06 | **Evidence sufficiency is a per-question rule** in the question catalog (e.g., `comparableMatters.min: 12`). Insufficient → structured `insufficient_evidence` response, never silent fallback to generic AI. | The architectural enforcement of the IQ Stack honesty contract. | §5.4 |

## Substrate

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-07 | **Azure AI Search** for vector + structured retrieval. Reuse existing `spaarke-search-prod` / `spaarke-search-dev`. New indexes: `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`. | Already in stack. Mirrors the proven `spaarke-rag-references` pattern. | §4.1 |
| D-08 | **`text-embedding-3-large`** (3072 dim) for Insight artifact embeddings. | "No reason to knowingly underbuild" for a foundational choice. Cost differential vs. -small is real but not material at our scale. Recall on "comparable matters" is critical. | §4.1.4 |
| D-09 | **Cosmos NoSQL with adjacency-list documents** for the Insight Graph. NOT Cosmos Gremlin. | Our traversal needs are 2-3 hops with filters, not deep graph algorithms. Cosmos NoSQL gives vector co-location, MS strategic direction, simpler ops. | §4.2 |
| D-10 | **`IInsightGraph` abstraction** in C# — consumers see named traversal patterns (`FindMattersInvolvingPartyAsync`), not raw query syntax. | Preserves swap path if Phase 2+ ever needs purpose-built graph DB. | §4.2.2 |
| D-11 | **Live Facts via direct Dataverse queries** (computed on-read). Materialized Facts (expensive aggregates) stored in Insight Index alongside Observations. | Cheap, fresh, no staleness for live; fast retrieval for expensive aggregates via single hybrid retrieval. | §4.3 |
| D-12 | **`tenantId` is a first-class field on every new index.** | The existing `spaarke-records-index` lacks this (acknowledged gap); new indexes MUST NOT repeat. Required for `vectorFilterMode=preFilter` ACL trimming. | §4.1.3, §7.3, §10.3 |

## Synthesis layer

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-13 | **Custom Insights Agent in `Sprk.Bff.Api`**, NOT Foundry-hosted. | Packagability + consistency with current Spaarke pattern. Foundry's superpowers (durable, HITL, A2A) aren't used by this component. | §5.1 |
| D-14 | Insights Agent **reuses existing `IChatClient` + `UseFunctionInvocation` pipeline + tool framework** (`IAiToolHandler`). | All scaffolding exists in `Sprk.Bff.Api`. Don't duplicate. | §5.1, ai-inventory.md §4 |
| D-15 | **Two-tier memory pattern via Redis** (user_profile + chat_summary). Borrowed from Foundry; implemented locally. | Foundry's pattern is right; we want it without the Foundry hosting commitment. | §5.2 |
| D-16 | **Streaming + verification + cross-matter strip** safety controls (existing Spaarke patterns) apply to Insights Agent. | Consistency. The existing rules from the unification design extend naturally to graph evidence. | §5.5 |

## Sync architecture

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-17 | **Azure Functions on Flex Consumption** for sync, reconciliation, extraction triggers, re-indexing. | Microsoft's current default. Bicep-deployable per tenant with UAMI. Permitted by updated ADR-001 (commit `84cec9f9`). | §3.2, §11.2 |
| D-18 | **Service Bus topic** as real-time backbone + **Timer-triggered Function** over Dataverse change-tracking for reconciliation. | Canonical Microsoft pattern per knowledge research. Webhooks are triggers only; consumers re-fetch records. | §6.1, §6.2 |
| D-19 | **NO Dataverse plugin assemblies**, NO Power Automate flows. Webhook registration via plugin registration tool is acceptable (that's just the registration mechanism, not custom plugin code). | Confirmed by user. Plugins have same SAS limitation; Power Automate adds complexity without solving auth. | §6.1, §11.2 |
| D-20 | **NO Durable Functions.** Multi-step orchestration uses Service Bus + state machine. | ADR-001 (preserved from original). | ADR-001 |
| D-21 | **Closure-extraction is a JPS playbook ending in `DeliverToIndexNodeExecutor`.** Function triggers it via BFF API endpoint. | Reuse `PlaybookExecutionEngine`; single playbook execution path; no duplicate orchestration in Function. | §6.4 |

## Auth (CRITICAL — confirmed against Microsoft docs + Spaarke Phase C work)

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-22 | **Intake Function is the auth trust boundary.** Only entry point that accepts external traffic. | Dataverse native Azure Service Bus integration uses SAS only; we will not have SAS keys on the bus. | §6.1, §6.2 |
| D-23 | **Dataverse → Intake Function auth: `clientState` shared secret in header (transitional) → HMAC-SHA256 signature validation (target, Phase C #044).** Copy validation code from BFF webhook handlers — do NOT recreate. | The documented Dataverse webhook auth options for an intermediate-Function pattern. Phase C #044 is in flight; ship initially with clientState, drop in HMAC when it lands. | §6.2, §6.2.2 |
| D-24 | **All other hops use Managed Identity + Azure RBAC** (UAMI). Zero SAS keys at target state. | The transitional clientState is the only shared secret; it disappears post Phase C #044. | §6.2.1 |
| D-25 | **`Microsoft.Identity.Web.AddMicrosoftIdentityWebApi`** for non-webhook endpoints on the Function App. NOT `@spaarke/auth` (which is client-side TypeScript only and does not validate inbound tokens). | `@spaarke/auth` is npm/MSAL.js; Function is .NET. Microsoft.Identity.Web is the canonical inbound JWT validator. Reference: `Sprk.Bff.Api/Program.cs`. | §6.2.2, §6.2.3 |
| D-26 | **`AzureAd:TenantId` is the explicit tenant GUID, never `common` or `organizations`.** Coordinate with Phase C task 047 (fixes template). | Per-tenant deployment threat model (D-AUTH-5). | §6.2.2, §6.2.3 |
| D-27 | **`DefaultAzureCredential` for all outbound calls.** No `ClientSecretCredential` in new Functions. | Aligns with Phase C tasks 041 (Graph outbound) + 042 (Dataverse outbound). New Functions inherit the managed-identity-only discipline from day one. | §6.2.4 |
| D-28 | **Do NOT call out to a separate "auth service" for JWT validation.** Validation is a local crypto check; an extra hop adds latency without security benefit. | Microsoft.Identity.Web validates iss/aud/tid/signature/expiry locally with cached AAD metadata. | §6.2.3 |

## Identity resolution

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-29 | **Probabilistic graph with `SAME_AS` edges + confidence scores.** No canonical registry requiring human curation. | Human curation isn't realistic at law-firm scale. AI-driven matching at ingestion + periodic re-evaluation as more signal accumulates. | §4.2.4, §6.5 |
| D-30 | **Scope identity resolution by relevance threshold** — only invest effort on entities meeting N+ matters or N+ outbound edges. Long-tail mentions stay as standalone vertices. | Don't over-invest. Queries follow SAME_AS edges; standalone duplicates don't pollute aggregates. | §6.5 |

## Privilege model

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-31 | **Physical per-tenant isolation** — separate AI Search service (or strictly-isolated indexes), separate Cosmos account, separate Function App with per-tenant UAMI, separate Service Bus topic. | Legal data privilege boundaries are physical, not just logical. Also matches packagability requirement. | §3.5, §7.2 |
| D-32 | **Within tenant: queries trim at execution time** via `accessibleMatterSet` filter at every Search query + every graph traversal vertex-touch. | The substrate stores everything for the tenant; users see different subsets per session. Pre-materialized per-user views don't survive access changes. | §7.1, §7.3 |
| D-33 | **`vectorFilterMode=preFilter`** mandatory for ACL trimming in vector queries. | Per Microsoft guidance; required for high-cardinality access groups. | §4.1.3, §7.3.1 |
| D-34 | **Counts vs. IDs trimming distinction.** Aggregate Insights (counts, ranges) can cross the access boundary; specific Insights (linked matter IDs) cannot. | Allows useful aggregate insights ("Acme has appeared in 8 of your firm's matters") without leaking inaccessible details. | §7.1, §7.3.2 |
| D-35 | **Cross-matter pivot rule extension.** When user pivots Matter A → Matter B, prior matter-specific evidence cleared from history. | Extension of existing Spaarke rule for documents to graph evidence. Privilege leakage vector. | §7.4 |

## Packagability

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-36 | **Everything as code**: Bicep for Azure resources, JSON for AI Search index schemas, Bicep params per tenant, idempotent deployment script with backfill step. | Multi-tenant ISV requirement. Per-tenant deployment unit must spin up cleanly. | §9 |
| D-37 | **Tenant-list-as-configuration in Bicep** for r1 (parameter file per tenant). Evolve to tenant-list-as-data control plane around ~10 tenants. | Fits current scale; the inflection point is documented for Phase 2. | §9.5 |

## Repo / governance

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-38 | **ADR-001 narrowed**: BFF endpoints MUST be Minimal API (no Functions hosting BFF endpoints); Azure Functions permitted for narrow out-of-band integration; Durable Functions still rejected. Commit `84cec9f9`. | Original concern (don't fragment BFF runtime) preserved. New scope unblocks Insights Engine sync work. Propagated to 16 files. | ADR-001, design.md §13 |

## Architecture-doc r2 additions — data, evaluation, surfacing, MCP, refinements

These decisions were introduced in the `INSIGHTS-ENGINE-ARCHITECTURE.md` r2 expansion (2026-05-21) and back-propagated to this canonical decisions log (2026-05-22). They concern customer-onboarding workflow, evaluation quality, MCP integration, and operational refinements.

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-39 | **Historical backfill as Phase 1 capability.** `BulkReExtraction` Function and customer-onboarding workflow ship in Phase 1 / early Phase 2, not Phase 3. Backfill priority algorithm: most recent closed matters first, customer's strategic practice areas first, most-comparable matters for highest-value question templates. Throttle-aware, resumable, skips records at current `producedBy.version`. | Without backfill, new customers see insufficient-evidence responses for 6+ months — commercially unacceptable. Backfill turns a 6-month organic build into a 2-week migration for customers with prior history. Per LAVERN's "build for real data from the onset" principle. | INSIGHTS-ENGINE-ARCHITECTURE.md §11.4, §19.9 |
| D-40 | **Golden dataset + eval harness as Phase 1 CI gate.** 50–100 curated `(question, expected-answer, expected-evidence)` tuples; RAG-triad evaluators (Retrieval / Groundedness / Relevance); CI gate fails on groundedness regression or insufficient-evidence miscalls. Phase 1 v1 ships 10–15 tuples; thresholds set permissively for mock-data behavior. | The honesty contract becomes measurable rather than just structural. Required for defensibility to enterprise legal buyers. Every Inference question template MUST have a golden-dataset entry before ship. | INSIGHTS-ENGINE-ARCHITECTURE.md §14, §19.9 |
| D-41 | **MCP server (`Sprk.Insights.Mcp`) as Phase 1 contract, Phase 2 implementation.** Engine exposes question catalog as MCP tools + resources + prompts; OBO auth; read-only in V1. Tool signatures aligned with question catalog. | The integration posture is "be consumed by Microsoft's tools." Honesty contract survives all consumption paths via the renderer prompts. Drafting the contract in Phase 1 alongside the question catalog ensures they evolve together. | INSIGHTS-ENGINE-ARCHITECTURE.md §15, §19.9 |
| D-42 | **Streaming verification mechanism: rule-based + embedding-similarity, NOT a second LLM call.** Checks claim-evidence binding, citation existence, embedding-similarity threshold (default 0.55), confidence-band sanity. Achievable within 200ms budget. | Cheap, bounded, no LLM-verifier hallucination risk. Complementary to but distinct from D-47 (`GroundingVerifier`): D-42 is response-level claim/evidence binding; D-47 is per-citation mechanical substring check. Both run in the response pipeline. | INSIGHTS-ENGINE-ARCHITECTURE.md §9.4, §19.9 |
| D-43 | **Sync re-fetch backpressure mitigation.** 30-second deduplication window per `(entityType, entityId)` in `InsightsSyncFunction`; per-tenant token-bucket throttling for known-burst operations; nightly reconciliation catches drift. | Prevents Dataverse throttling during bulk imports, end-of-quarter closures, and historical backfill. Correctness via reconciliation, not via real-time exactness. | INSIGHTS-ENGINE-ARCHITECTURE.md §6.3, §19.9 |
| D-44 | **Graph adjacency-list write amplification mitigation.** Adjacency-list pattern for Phase 1; migrate high-degree vertices (≥500 edges OR ≥200KB document size) to edges-as-documents in Phase 2 via nightly reconciliation; `IInsightGraph` abstraction unchanged. Edge-count monitoring telemetry from day one. | Cosmos document-size and RU cost limits at scale. Phase 1 trigger unlikely to fire on freshly-bootstrapped corpus; migration tooling designed in Phase 2. Abstraction preserves swap path. | INSIGHTS-ENGINE-ARCHITECTURE.md §8.2, §19.9 |
| D-45 | **Embedding model lifecycle.** Dual-write versioned index pattern: provision `insight-matters-v{N+1}-emb{NewModel}`, re-embed via `ScheduledReIndexer`, dual-read grace period, cutover via config flag, decommission old. | When `text-embedding-3-large` is succeeded (text-embedding-4 or later), the entire Insight Index needs re-embedding. Same pattern as playbook-version migration; only the trigger differs. Playbook validated end-to-end on non-production tenant before real cutover. | INSIGHTS-ENGINE-ARCHITECTURE.md §8.1, §19.9 |

## LAVERN-derived decisions (Pattern adoption from `ai-advanced-capabilities-development` project)

These decisions are responses to the LAVERN analysis (`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md`) — 12 patterns derived from the AnttiHero/lavern Apache 2.0 repo. Phase 1 adopts 5 patterns; full rationale + verdicts on all 12 in `lavern-pattern-assessment.md`.

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-46 | **Precedent Board layer in 4-tier taxonomy.** Architecture + scaffold in Phase 1 (D-A26, D-A27); lifecycle automation in Phase 1.5. Includes: `sprk_precedent` Dataverse entity + Cosmos `Precedent` vertex + `OBSERVATION_SUPPORTS_PRECEDENT` / `PRECEDENT_RELATED_TO_PRECEDENT` edges + `IPrecedentBoard` interface + `insight-precedents` AI Search embedding index. References LAVERN ADR 10.1 (proposed, not yet ratified). | Pre-production is the right moment to get the conceptual model right. Building "3-tier with room for 4th" is harder than "4-tier from day one." Without Precedent: (a) every Inference re-derives patterns from raw Observations (cost + quality issue), (b) no path for SME confirmation of institutional rules (`Mode 4` from use-case doc doesn't exist), (c) the system architecturally mirrors generic AI hedging the marketing disavows. Phase 1 scaffold + Phase 1.5 lifecycle is the disciplined middle ground. | lavern-pattern-assessment.md §3.1; LAVERN ADR 10.1 |
| D-47 | **`GroundingVerifier` post-Agent citation check** — mechanical zero-LLM substring + sliding-window verifier, **MANDATORY** in `InsightsResolverService` after Insights Agent before returning. Failed citations stripped or annotated `[citation could not be verified]`. Platform primitive shared with Action Engine. References LAVERN ADR 10.6 (proposed). Complementary to but distinct from architecture-doc D-42 (rule-based + embedding-similarity streaming safety verifier): D-47 is mechanical citation check on individual evidence refs; D-42 is response-level claim/evidence binding check. Both run in the response pipeline. | D-04 declares provenance is the API contract but the existing design never verifies cited evidence actually contains the claim's quoted text. Without verification D-04 is principle, not enforcement. Mechanical primitive: ~milliseconds, zero LLM cost. Closes the gap between the honesty contract's promise and what the code mechanically guarantees. | lavern-pattern-assessment.md §3.2; LAVERN ADR 10.6 |
| D-48 | **`EvidenceGuard.Validate` runtime non-empty guard** on every evidence-bearing tool handler. Throws `EvidenceRequiredException` on empty `Evidence`. Applied to `IFindComparableMattersTool`, `IGetMatterFactsTool`, `IAssessEvidenceSufficiencyTool`, `ISearchPrecedentsTool`. References LAVERN Pattern #6. | C# type system enforces shape, not non-empty contents. Tests, direct callers, or future code constructing payloads programmatically could pass `Evidence: []` and silently bypass D-04. Belt-and-suspenders: ~1 day cost; meaningful defense. | lavern-pattern-assessment.md §3.3 |
| D-49 | **`IDeclineToFindTool` as first-class tool verb** — replaces "Agent reasons about whether to decline" prose with a deterministic tool the Agent invokes when `IAssessEvidenceSufficiencyTool` returns insufficient. Returns structured `DeclineResponse { Reason, Explanation, MinimumEvidenceNeeded, SuggestedActions, ConfidenceInDecline }`. References LAVERN Pattern #7. | D-06 declares no silent fallback to generic AI, but enforcement is currently an LLM call that can be coerced ("only 4 comparable matters but let me give a rough estimate"). A dedicated tool makes uncertainty a deterministic affordance the Agent must invoke. | lavern-pattern-assessment.md §3.4 |
| D-50 | **`ISanitizer` + `Smacl1Sanitizer` ingest primitive** — strips prompt-injection vectors (zero-width Unicode U+200B–U+200F + U+202A–U+202E + U+2060–U+206F + U+FEFF, HTML comments, ANSI escapes, control chars) before any LLM sees text. Audit log to App Insights custom events. **MANDATORY** at all AI-facing ingest paths in the Engine; Phase 1 wires it into closure-extraction stub ingest paths so Phase 2 real-document path inherits sanitization by default. Platform primitive shared with Action Engine. References LAVERN ADR 10.6 (proposed). | Every AI-facing ingest path without canonical sanitization is its own prompt-injection attack surface. "Build for real data from the onset" (user-stated principle, 2026-05-22) means closure-extraction's real-document path in Phase 2 must inherit sanitization, not retrofit it. Build the primitive in Phase 1 even though Phase 1 itself uses mock data. | lavern-pattern-assessment.md §3.5; LAVERN ADR 10.6 |
| D-51 | **Shared GateResolver consumption for Phase 2+ write-back paths.** Action Engine MVP is the implementer of `IGateResolver` interface + 4 implementations per LAVERN ADR 10.3 (proposed); Insights Engine consumes the same primitive when write-back paths land (Phase 2+). The §8.4 "extends existing PendingPlanManager" plan in design.md is superseded by GateResolver consumption. | One canonical approval primitive across Spaarke beats per-subsystem reimplementation. Coordination assessment §4.6 (new) tracks this. Phase 1 Insights is read-only so no implementation work; design discipline is to reference the planned primitive rather than building our own. | lavern-pattern-assessment.md §6; coordination-assessment-with-insights-engine.md §4.6; LAVERN ADR 10.3 |

---

## Explicit "do not do" — the negative space

These are decided NOT to do. Recording them prevents drift:

- **Do not** host the Insights Agent in Foundry (D-13)
- **Do not** use Durable Functions (D-20)
- **Do not** use Dataverse plugin assemblies or Power Automate flows for sync integration (D-19)
- **Do not** put SAS keys on Service Bus (D-22, D-24)
- **Do not** use `ClientSecretCredential` in new Function code (D-27)
- **Do not** call a separate "auth service" for JWT validation (D-28)
- **Do not** use `common` or `organizations` as `TenantId` (D-26)
- **Do not** use `@spaarke/auth` for server-side inbound validation (D-25 — it's client-side only)
- **Do not** create any new index without `tenantId` as a first-class field (D-12)
- **Do not** require human curation of identity resolution (D-29)
- **Do not** return generic AI hedging when Inference evidence is insufficient — return structured `insufficient_evidence` (D-06)
- **Do not** put document content into cross-matter aggregates (privilege leakage — §7.4)
- **Do not** skip `GroundingVerifier` post-Agent step on any evidence-bearing Insight response (D-47)
- **Do not** allow empty `Evidence` arrays through evidence-bearing tool handlers — `EvidenceGuard.Validate` MUST fire (D-48)
- **Do not** write decline prose from the Agent; invoke `IDeclineToFindTool` for structured `DeclineResponse` (D-49)
- **Do not** route external text to any LLM step without first passing through `ISanitizer` (D-50)
- **Do not** create Precedents without supporting Observations referenced via `OBSERVATION_SUPPORTS_PRECEDENT` edges (D-46)
- **Do not** hardcode Precedent lifecycle thresholds (`CONFIRM_THRESHOLD`, decay rate, drift threshold) — Phase 1.5 makes them configurable (DEF-12)
- **Do not** reimplement an approval primitive in Phase 2+ — consume Action Engine's `IGateResolver` (D-51)
- **Do not** spell the project name singular ("Insight Engine") — D-01 is plural canonical
- **Do not** confuse architecture+scaffold (Phase 1 D-A26) with full Precedent Board (Phase 1.5) — scaffold means: entity, interface, vertex+edges, index, tool stubs, admin endpoint. Full means: decay job, promotion job, drift detection, curator, SME review queue, hybrid retrieval dedup

---

## Phase C coordination items

The Spaarke auth Phase C work (currently in flight) interlocks with Phase 1 of this project. Coordinate:

| Phase C task | Coordination requirement | Impact on Phase 1 |
|---|---|---|
| **#041** — Managed identity for Graph outbound | Insights Engine Functions inherit `DefaultAzureCredential` discipline | New Functions use MI from day one; no `ClientSecretCredential` |
| **#042** — Managed identity for Dataverse outbound | Same | Re-fetch in `InsightsSyncFunction` uses `DefaultAzureCredential` |
| **#044** — HMAC-SHA256 webhook signature validation | Ship Phase 1 initially with `clientState`; drop in HMAC when #044 lands | Copy the same validator code; do not fork |
| **#047** — Non-`common` `TenantId` in `appsettings.template.json` | Insights Engine Function appsettings adopt the fix from day one | Per-tenant explicit TenantId in every Bicep deployment |
| **`.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md`** | Active auth design canon until ADR-027 lands | Insights Engine auth design must be consistent with this canon |

---

## LAVERN coordination items

The `ai-advanced-capabilities-development` project authored the LAVERN analysis + ADR proposals 10.1–10.6 (`projects/ai-advanced-capabilities-development/LAVERN-ANALYSIS-AND-PLAN.md` §10). Coordinate:

| LAVERN ADR | Coordination requirement | Impact on Phase 1 |
|---|---|---|
| **10.1** — Precedent Board | Ratification before D-A26 design freeze. Insights Engine is the implementer (scaffold in Phase 1, lifecycle in Phase 1.5). Action Engine is downstream consumer (AI Tools cite Precedents in R2+). | Phase 1 D-A26 + D-A27 (scaffold); Phase 1.5 (lifecycle automation) |
| **10.3** — GateResolver interface | Joint ratification with Action Engine. Action Engine MVP is the implementer; Insights consumes for Phase 2+ write-back paths. See D-51. | No Phase 1 implementation; reference only |
| **10.4** — Provider tier abstraction | Required *before* EvaluatorGate (LAVERN Pattern #2) ships (Phase 2+ Action Engine concern). Insights stays on hardcoded D-08 embedding model; tier abstraction is platform/JPS concern. | No Phase 1 change; awareness only |
| **10.6** — Sanitization + Citation Verification Standard | Insights builds the primitives in Phase 1 (D-A22 GroundingVerifier, D-A25 Sanitizer). Action Engine consumes when webhook/signal triggers land (R2). See D-47, D-50. | Phase 1 D-A22 + D-A25 |
| **`ai-advanced-capabilities-development` project status** | Currently working document; ADRs 10.1–10.6 are proposed, not yet ratified. Phase 1 design proceeds in parallel with explicit cross-references to LAVERN ADR proposals; final ADR docs land in `docs/adr/` post-ratification. | Coordination is documented, not blocking |
| **Cross-project artifacts** | `coordination-assessment-with-insights-engine.md` in Action Engine project tracks joint decisions (now §4.1–§4.8 with LAVERN-derived §4.6/§4.7/§4.8 additions) | Anchor doc for cross-project alignment; both projects reference |

---

## Open items (NOT decided — listed for transparency)

| ID | Item | Owner | When needed |
|---|---|---|---|
| O-01 | Closure-extraction playbook: JPS or specialized format? | Architecture | Before Phase 1 design freeze |
| O-02 | `accessibleMatterSet` source: unified access control project or own? | Architecture | Before Phase 1 sync wiring |
| O-03 | Per-question evidence-sufficiency thresholds — global default or per-question? | Product | Before first Inference question ships |
| O-04 | Outlook/Teams surface integration — Phase 1 or Phase 2? | Product | During Phase 1 |

---

## Deferred (will decide later)

| ID | Item | When to reconsider |
|---|---|---|
| DEF-01 | Add purpose-built graph DB (Cosmos Gremlin or Neo4j) | If/when Phase 2+ needs deep multi-hop algorithmic queries that adjacency-list can't serve |
| DEF-02 | Bump AI Search to S2 tier | When indexed artifacts approach ~5M per tenant |
| DEF-03 | Migrate `PlaybookIndexingBackgroundService` from BackgroundService to a Function | Phase 3 cleanup (the old ADR-001 mandate is removed; migration is now permissible) |
| DEF-04 | Migrate `spaarke-records-index` to add `tenantId` | Phase 3 cleanup; coordinate with multi-tenant rollout |
| DEF-05 | Tenant control plane (config-driven → data-driven) | When tenant count approaches 10 |
| DEF-06 | Foundry-hosted agent for separate multi-day diligence surfaces | When those surfaces are designed (separate project) |
| DEF-07 | Cross-tenant insights (industry benchmarks) — opt-in only | Possibly Phase 3+; major privacy/legal work first |
| DEF-08 | **Foundry IQ / Agentic Retrieval as a narrow-scope evidence tool** — evaluate as a future evidence channel for narrative-content artifacts only; not a replacement for hybrid AI Search. | Phase 2+ evaluation; gate on A/B-test improvement against plain hybrid search on the golden dataset (per D-40). |
| DEF-09 | **Engine extraction from BFF (`Sprk.Insights.Api`)** — extract the Engine into its own Minimal API service when BFF publish size, deployment cadence, or team-autonomy concerns warrant. Trigger criteria: BFF publish size > 100 MB, OR Insights team needs independent release cadence, OR Engine touches >50% of BFF PRs. | Phase 3 candidate; conditional on trigger criteria. |
| DEF-10 | **MCP server write operations** — current D-41 ships read-only V1. Write operations (save Inference, confirm Precedent, mark Insight stale) require agent-initiated-write auth/audit model. | Phase 2+ once the auth/audit model is hardened; ties to D-51 (GateResolver consumption). |
| DEF-11 | **Spaarke-specific fine-tuned models** — fine-tune an embedding or completion model on Spaarke-curated legal corpus once eval harness baseline (D-40) exists and per-question performance plateaus on generic models. | Phase 3+ option once corpus and eval harness exist; ROI gated on quality lift measured against golden dataset. |
| DEF-12 | Precedent Board lifecycle automation tuning (`CONFIRM_THRESHOLD`, decay rate, drift threshold) | Calibrate with real Observations flowing in Phase 1.5; do NOT pre-tune in Phase 1 |
| DEF-13 | SME review queue UI surface for Precedent confirmation/drift (workspace context pane vs dedicated Code Page vs Teams app) | Product input from real SME during Phase 1.5; the surface choice affects Mode 4 from `ADVANCED-AI-USE-CASE-PATTERNS.md` |
| DEF-14 | Cross-tenant Precedent publishing (Spaarke-curated shared pool of confirmed Precedents) | Major privacy/legal work first; Phase 2+ if at all |
| ~~DEF-15~~ | **RESOLVED 2026-05-22**: Back-propagation of architecture-doc r2 additions (D-39–D-45, DEF-08–DEF-11) into this anchor doc completed. See entries above. | — |

---

## How to use this document

- **Citing a decision**: use the ID. E.g., "Per D-13, we don't host the Agent in Foundry."
- **Proposing a change**: add a new decision (D-39, D-40...) that explicitly supersedes the old one with `Supersedes: D-XX` line in its rationale. Don't edit superseded entries — strike-through them in place and add a "**SUPERSEDED by D-YY**" tag.
- **Closing an open item**: convert it to a `D-XX` row with rationale; remove from the Open list.
- **Adding to "do not do"**: only add if you've actively considered and rejected something. Don't pre-fill speculative anti-patterns.
