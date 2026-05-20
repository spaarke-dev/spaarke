# Decisions Log — Spaarke Insights Engine (r1)

> **Status**: Living document — decisions added as made, marked superseded when changed.
> **Last Updated**: 2026-05-19
> **Anchor doc**: read this first; for full rationale see [design.md](design.md) section references.

Each entry: `D-XX | Decision | Rationale (one line) | design.md ref`. Decisions are durable until superseded; supersessions are explicit and reference the superseding entry.

---

## Identity

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-01 | Name: **Spaarke Insights Engine** (internal: `Insights Engine`, code: `Insights*`) | Distinct from IQ Stack (marketing umbrella). "Engine" honestly scopes — signals in, context out; sources and surfaces are out of scope. | §1.1 |
| D-02 | Engine boundary: ONLY the resolver + synthesis layer + sync/extraction Functions + Insight Index + Insight Graph. **Sources (Dataverse, SPE) and surfaces (pane, ribbon, Outlook) are out of scope.** | Single responsibility; replaceable; independently deployable. | §1.1, §3.1 |

## Conceptual model

| ID | Decision | Rationale | Ref |
|---|---|---|---|
| D-03 | **Three-artifact taxonomy**: Fact / Observation / Inference. Every artifact carries this `type`. | Different trust profiles, stores, APIs, presentation rules. Mixing them is what makes "intelligence" silently dishonest. | §2.1 |
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

---

## How to use this document

- **Citing a decision**: use the ID. E.g., "Per D-13, we don't host the Agent in Foundry."
- **Proposing a change**: add a new decision (D-39, D-40...) that explicitly supersedes the old one with `Supersedes: D-XX` line in its rationale. Don't edit superseded entries — strike-through them in place and add a "**SUPERSEDED by D-YY**" tag.
- **Closing an open item**: convert it to a `D-XX` row with rationale; remove from the Open list.
- **Adding to "do not do"**: only add if you've actively considered and rejected something. Don't pre-fill speculative anti-patterns.
