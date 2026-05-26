# Spaarke Connect — Design Follow-ups

> Companion to [design.md](design.md). Addresses four questions raised after the initial plan review: data transformation, legacy-model mapping, third-party tooling, and where AI fits.

---

## 1. Data Transformation — How and Where

### 1.1 The transformation surface area

Five places in the pipeline where data is transformed:

| Stage | Direction | What's transformed | Where it runs |
|---|---|---|---|
| **Inbound parse** | external → canonical | Vendor payload (e.g., iManage webhook body) → Spaarke canonical envelope | `Sprk.Connect.Api` (inline, ~5ms budget) |
| **Field mapping** | canonical → Dataverse | Canonical envelope → `sprk_document` / `sprk_matter` / `sprk_externalref` upsert | `Sprk.Connect.Worker` (job handler) |
| **Reference data crosswalk** | external codes → Spaarke option set | `docType=CONTRACT` → `sprk_documenttype=100000000` | Inside field mapping (same step) |
| **Binary normalization** | external file → AI-ready content | iManage version blob → text via Document Intelligence (when needed) | BFF `AnalysisOrchestrationService` (existing) — Connect just hands off |
| **Outbound writeback** | Spaarke result → external payload | `sprk_analysis.sprk_finaloutput` → iManage custom fields PATCH body | `Sprk.Connect.Worker` (writeback handler) |

Crucially: **transformation is not one big step**. It's a chain. Each stage has a defined input/output contract, runs in a specific component, and is testable in isolation.

### 1.2 The transformation runtime (`Spaarke.Connect.Transform`)

A small, deterministic library — not a scripting engine. Rules are JSON, executed by a typed expression tree. No arbitrary code execution.

**Rule shape**:
```json
{
  "from": "$.customFields.MatterNumber",
  "to": "sprk_matter@odata.bind",
  "transform": "lookupMatterByAlternateKey('sprk_mattercode')",
  "onMissing": "skip | error | default('UNMAPPED')",
  "validate": "regex:^M-\\d+$"
}
```

**Built-in transform functions** (the entire MVP catalog, fewer than 20):

| Category | Functions |
|---|---|
| **String** | `trim`, `lower`, `upper`, `truncate(n)`, `regex(pattern, group)`, `concat(...)`, `substringBefore(delim)`, `substringAfter(delim)` |
| **Type** | `toInt`, `toDecimal`, `toDate(format)`, `toBool` |
| **Lookup** | `optionSetLookup(table, dictionary)`, `lookupContactByEmail`, `lookupMatterByAlternateKey(field)`, `lookupRefDataByCode(crosswalkId)` |
| **Conditional** | `coalesce(val1, val2, ...)`, `default(val)`, `iif(condition, then, else)` |
| **Structural** | `flatten`, `unwrap`, `jsonPath(expr)` |

**JSONPath as the source addressing language** — every `from` is a JSONPath expression evaluated against the inbound canonical payload. This means every connector can produce a tree-shaped payload (matters how iManage and NetDocs do) and the mapping rules don't care which.

**Validation runs after transform** — `validate` is a small set of declarative checks (`regex`, `required`, `enum`, `minLength`, `maxLength`). Failures route to `sprk_externalevent` with severity `Warning` (skipped record) or `Error` (whole job DLQs), per per-rule policy.

### 1.3 Why not arbitrary code

Three reasons we **do not** expose a Python/C#/JS sandbox for transforms in v1:

1. **Security** — sandbox escapes are CVE factories. We avoid the category entirely.
2. **Testability** — declarative rules are unit-testable as data; arbitrary code is not.
3. **Ease of setup** — a consultant configuring mappings should not need to write code (per the customer's "easy as possible" requirement).

Escape hatch when needed: a **custom-handler hook** that lets a per-customer transform run **outside** Connect (in a customer-owned Azure Function the canonical payload is forwarded to). The Function returns the transformed payload, Connect proceeds. Phase 2 only, opt-in per connection. The Function lives in the customer's environment; we never run their code in ours.

### 1.4 What transformation does NOT solve

Transformation is field-to-field. It does not solve:
- **Entity resolution** — "which Spaarke matter is this iManage workspace?" — see §2.3.
- **Semantic gaps** — "iManage doesn't have an Engagement Letter type; what should we do?" — see §2.4.
- **Cross-record consistency** — "the matter says GMC client; the invoice says GM Capital" — flagged for human review.

These are mapping-design problems, addressed in §2.

---

## 2. Legacy Data Model Mapping

This is the harder problem. Every external system has its own:
- **Entity model** (iManage Workspace vs Aderant Matter vs NetDocs Cabinet)
- **Field shapes and names**
- **Reference data** (their `DocType` values, their statuses, their practice areas)
- **ID strategies**
- **Lifecycle semantics** (versioning, retention, finalization)

### 2.1 Three-layer mapping model

We split the problem into independently solvable layers — each with its own storage, lifecycle, and UX:

| Layer | What it answers | Storage |
|---|---|---|
| **Schema mapping** | "iManage `Document` → Spaarke `sprk_document`. Which fields go where?" | `sprk_externalfieldmapping` (per connection) |
| **Reference data crosswalk** | "iManage `DocType=CONTRACT` ≡ Spaarke `sprk_documenttype=Contract`. What's the full lookup table?" | `sprk_referencedatacrosswalk` (per connection × per crosswalk type) |
| **Entity resolution** | "iManage workspace `IPLIT_2024_AcmeCorp` is the same matter as Spaarke matter `M-04217`. How do we know?" | `sprk_externalref` (per linked record) + alt-key lookups |

Separating these is the most important architectural decision in mapping. They have different change cadences (reference data drifts daily; schema mapping changes rarely; entity links are created continuously). They have different UX (crosswalk is a table editor; schema mapping is side-by-side field picker; entity resolution is a search + confirm flow). And they have different failure modes.

### 2.2 Schema mapping — the source schema problem

A mapping rule references a source field by JSONPath. But which fields exist? Each connector must publish a **source schema** at startup:

```json
{
  "connector": "iManageWork10",
  "version": "1.2.0",
  "entities": [
    {
      "name": "Document",
      "fields": [
        { "path": "$.id", "type": "string", "required": true, "description": "iManage document ID" },
        { "path": "$.name", "type": "string", "maxLength": 256 },
        { "path": "$.author.email", "type": "string", "format": "email" },
        { "path": "$.customFields.MatterNumber", "type": "string", "optional": true },
        { "path": "$.docType", "type": "enum", "values": ["CONTRACT", "PLEADING", "MEMO", "..."] },
        { "path": "$.profileFields", "type": "object", "additionalProperties": true }
      ]
    }
  ]
}
```

The Connect Admin Code Page renders the mapping UI **from this schema** — fields the connector publishes are the only valid `from` paths (catches typos at design time, not at first webhook). Schema is versioned per connector release; updating the connector triggers an admin review of mappings that reference removed/renamed fields.

This also drives:
- **Default mappings shipped per connector** (`mapping.default.json`) — the 80% case, no customer config required.
- **AI-assisted suggestions** (see §4.2) — Spaarke proposes the rest based on Spaarke's own schema.

### 2.3 Entity resolution — the join problem

When an iManage webhook arrives saying "document filed in workspace IPLIT_2024_AcmeCorp," which Spaarke matter does that correspond to?

Three resolution strategies, tried in order:

1. **Alternate key match** — if customer has populated `sprk_matter.sprk_externalkey = "IPLIT_2024_AcmeCorp"` in their import, use that. Cleanest, deterministic.
2. **Configured crosswalk lookup** — the mapping rule explicitly says `lookupMatterByAlternateKey('sprk_mattercode')` with a specific field. Works when customer's iManage workspace name *is* the matter code.
3. **AI-assisted match** — a `RecordMatchingService` call (existing capability at [Services/Ai/RecordMatching/](src/server/api/Sprk.Bff.Api/Services/Ai/RecordMatching/)) using fuzzy name match + recency + practice area. Returns confidence score. Below threshold → routed to a `pending-resolution` queue surfaced in the Admin Code Page for a human to confirm.

**The unresolved queue is a first-class feature**, not an error path. Customers in the legal-tech space *always* have stragglers — matters that were created in iManage before they had a Spaarke equivalent, projects whose internal numbering changed, etc. The queue lets a consultant resolve these in batch during onboarding.

Once resolved, the link is persisted in `sprk_externalref` and never re-resolved.

### 2.4 Reference data crosswalks

Document types, matter types, status values, practice areas, jurisdictions — every system has its own enumeration, and they never line up.

**Approach**:
- **Shipped defaults per connector**: each connector includes `mapping.default.json` covering the obvious mappings (`CONTRACT → Contract`, `NDA → NDA`).
- **Customer overrides via the Admin Code Page**: a simple two-column editor — left side is external values (auto-populated from observed data), right side is the Spaarke option set picker. Unmapped values flagged.
- **First-seen detection**: when a webhook arrives with a `docType` value never seen before, Connect logs it to `sprk_externalevent` with severity `Info` and surfaces it in the Admin Code Page "Unmapped Values" view. Consultant maps it; next webhook with that value is mapped automatically.
- **Semantic fallback** *(Phase 2)*: when an external value has no mapping, Spaarke's AI proposes the closest Spaarke option set value based on label semantics. Consultant approves or overrides.

Stored as a JSON document per (connection, crosswalk type). Diffable, exportable, importable — moves cleanly between DEV/QA/PROD.

### 2.5 What customers should NOT have to do

Things that would be reasonable to ask a customer to do in a typical integration project — and that Spaarke Connect **avoids**:

| Common ask | Why we don't | What we do instead |
|---|---|---|
| Hand-write SQL or KQL queries | Not their job | JSONPath in the UI, picked from connector schema |
| Understand Dataverse Web API URL conventions | Internal detail | Mapping engine generates the OData call |
| Know which fields are required by which Spaarke playbook | Implementation leak | Capability definitions declare their input contract; UI gates "enable" on missing mappings |
| Discover the source system's schema themselves | Time sink | Connector publishes schema |
| Maintain a mapping changelog | Forgotten quickly | Dataverse audit log on `sprk_externalfieldmapping` |
| Test mappings against production data | Risky | "Validate against sample" button in the UI — runs against a captured payload, never against live |

---

## 3. Third-Party Tools — Build vs Buy vs Embed

The fundamental question: should Spaarke Connect be a native lightweight integration plane, or should it lean on an existing iPaaS platform (Azure Data Factory, Logic Apps, MuleSoft, Boomi, Workato)?

### 3.1 Evaluation matrix

| Tool | Fits our model? | Pros | Cons | Verdict |
|---|---|---|---|---|
| **Azure Data Factory** | Poorly | Mature ELT, 100+ connectors, copy activity is robust | Batch-oriented (poll-based, not webhook-driven), heavyweight Integration Runtime, designed for ETL not transactional integration, customer sees ADF UX directly, per-pipeline-execution cost adds up at scale, IP/private-link complexity in customer tenants | ❌ Wrong shape for webhook-driven document analysis. Right shape for one-off bulk historical loads. |
| **Azure Logic Apps** | Partially | Serverless, 1000+ connectors, visual designer non-developers can read, native iManage/NetDocs/Salesforce connectors exist | Per-execution cost at scale, harder to unit-test, latency 200ms-2s per step, vendor lock-in, customer must provision a Logic Apps subscription, our product UX becomes "go configure these in your Logic Apps instance" | ⚠️ Useful for *customer-side* adapters in the BYO pattern. Not the right runtime for *Spaarke's* core. |
| **Azure Functions** | Yes — but isn't a transformation framework | Lightweight, scales well, .NET-native (matches our stack), cheap | It's just code — not a mapping DSL, not a connector framework. Building Connect on Functions = building everything in this design doc from scratch in Functions. | ✅ Use Functions as the *hosting model* for `Sprk.Connect.Worker` if we don't want a Container App. Not a substitute for the design. |
| **MuleSoft / Boomi / Workato** | No | Mature iPaaS, many legal-tech firms already license one | Customer pays for them, integration logic lives outside Spaarke, we lose product control over the experience, debugging integration issues now requires three teams, our value prop becomes "Spaarke + MuleSoft" | ❌ Acceptable if the customer brings it — they POST to our BYO REST endpoint. Not something we mandate or rebuild around. |
| **Azure Event Grid** | Yes, narrowly | First-class CloudEvents support, scale, retry semantics built-in, low cost | Not a transformation layer — only routes events. Adds a hop in latency-sensitive paths. | ✅ Phase 2/3 candidate for the **outbound** event bus if customer subscription count grows. Premature in MVP — Service Bus + HTTP webhook delivery handles v1. |
| **Azure API Management** | Yes, narrowly | Quota/throttling, OAuth2 token validation, rate limiting, dev portal for the OpenAPI spec | Adds latency and operational overhead. Most of its value (auth, rate limiting) we already need to do for HMAC + per-tenant keys. | ⚠️ Add when (not before) the BYO surface has many third-party callers and we need a dev portal. Phase 3+. |
| **dbt / Fivetran / Stitch** | No | Excellent for data warehousing | Wrong paradigm — we don't have a warehouse; we have transactional document analysis | ❌ Out of scope. |
| **Dataverse Connectors / Power Automate** | Partially | Already in the Power Platform stack, customers are familiar | Per-flow run cost, complex throttling, hard to embed into our own product UX, can't be the system of record for our integration state | ⚠️ Useful for *customer* workflows that consume Spaarke events (Phase 2 outbound). Not for Spaarke's core. |

### 3.2 Recommendation

**Build Spaarke Connect natively as a small, focused integration plane** — the architecture in [design.md](design.md). Specifically:
- Native code (`Sprk.Connect.Api` + `Sprk.Connect.Worker`) for the core pipeline. .NET 8 minimal APIs, matches our existing stack, no third-party runtime dependency.
- **Service Bus** (existing Azure infra) for async + retry. No new tool.
- **Native HTTP outbound** for writeback. Polly for retry policies (already used in `Sprk.Bff.Api`).
- **Application Insights** for telemetry (existing).
- **Key Vault** for secrets (existing).

**Use third-party tools at the seams, not in the core**:
- **Customer's iPaaS as a BYO adapter** — if a customer has MuleSoft, they POST to our BYO REST endpoint from a Mule flow. Their cost, their team, their choice.
- **Azure Data Factory for bulk historical migration** *(Phase 2 only)* — when a customer wants to backfill 10 years of iManage docs, run an ADF pipeline that POSTs to Connect in batches. ADF is exactly right for the bulk one-time case where Connect's webhook model would be too slow.
- **Logic Apps as a customer-facing builder** *(Phase 3 optional)* — publish Spaarke as a custom Logic Apps connector once the public surface is stable. Lets customers compose Spaarke into their own automations without touching code. We don't *run* on Logic Apps; we *appear in* Logic Apps.

### 3.3 Pros and cons of the build-native call

**Pros**:
- Spaarke is the product. The UX, the debugging surface, the error model, the security model — all ours.
- No per-execution third-party cost (matters at the scale a successful legal-tech AI platform reaches — millions of docs).
- Latency we control (webhook-to-analysis under 2s is achievable; same path through ADF or Logic Apps would be 10–30s).
- Versioning under our control. Vendor connector API changes don't ripple through someone else's runtime.
- One auth model (per-tenant API key + HMAC) — not Spaarke's auth + Logic Apps' auth + the vendor's auth.

**Cons**:
- More code to maintain. The connector framework, the DSL runtime, the OAuth refresh logic — all ours to own.
- Slower to onboard *novel* connectors than picking one off a Logic Apps shelf — but the prebuilt connector library closes this gap as it grows.
- No marketing line that says "100+ connectors out of the box on day one."

**The tradeoff is correct for this product** because integration is on the critical path of the customer's perceived Spaarke experience, not a back-office concern. The connector *is* the product when the customer doesn't migrate.

---

## 4. Where AI Fits — and the Role of Agents

Spaarke is an AI platform. The integration layer is a vehicle for delivering AI to data that lives elsewhere. There are **two distinct places AI shows up** in Spaarke Connect, and they should not be confused:

### 4.1 AI as the workload (the customer's reason for connecting)

This is what Spaarke Connect *delivers* — the existing AI capabilities, invoked on external data.

- **Document classification + entity extraction** (per [design.md §5 Phase 1](design.md))
- **Contract review playbook** (JPS-driven, configurable, already exists)
- **Semantic search** over the linked external corpus (Azure AI Search, RRF hybrid)
- **Invoice → matter matching** (existing `RecordMatchingService`)
- **Chat over external matter context** (Phase 2 — entity-scoped chat where the binding entity's content is fetched on demand from iManage rather than from SPE)
- **Daily briefing / analytics across external matters** (Phase 3)

Architecturally, this is **the BFF's existing AI surface, called by Connect Worker over a stable internal API**. Nothing AI-specific needs to be built here. The Worker is "just" a client of the existing AI orchestration. This was the whole point of the Connect design: the AI engine doesn't have to know it's serving external data.

### 4.2 AI inside the integration plane (Spaarke uses AI to make Connect itself easier)

This is where the design becomes genuinely interesting and reinforces the "easy as possible" requirement. Use Spaarke's own AI to take work off the consultant/admin doing setup. Five concrete opportunities, ordered by leverage:

#### 4.2.1 Mapping suggestion agent

**Job**: when a new connection is created, Spaarke's AI inspects the connector's published source schema, compares it to the Spaarke target schema, and proposes a complete mapping. Admin reviews and approves field-by-field.

**Why it matters**: today, field mapping is the longest step of any integration project (often weeks). With AI suggestion, it's a 20-minute review.

**Where it runs**: existing AI infra (`AnalysisOrchestrationService` with a new JPS action `connect-mapping-suggest`). Inputs: source schema JSON, target schema JSON, optional sample payload. Output: a draft `sprk_externalfieldmapping` document for review.

**Phase**: 2.

#### 4.2.2 Reference data crosswalk assistant

**Job**: when a new external value appears (e.g., iManage `docType=ENG_LTR_FNL`), AI proposes the Spaarke option-set value most likely to match by label semantics + frequency in similar customers.

**Why it matters**: removes the most-tedious-and-error-prone task of integration setup. The list of unmapped values shrinks itself over time.

**Where it runs**: lightweight scoring call (no playbook needed). Input: external label + Spaarke option set. Output: ranked suggestions with confidence.

**Phase**: 2.

#### 4.2.3 Entity resolution agent

**Job**: when an external record can't be resolved to a Spaarke matter via deterministic keys, an AI agent uses fuzzy name match + recency + practice area + party overlap to propose candidates. Surfaces a confidence-ranked shortlist to a human.

**Why it matters**: closes the entity-resolution gap (§2.3) without forcing customers to clean their data before onboarding.

**Where it runs**: existing `RecordMatchingService` — already in production for invoice→matter. Connect calls it with extended inputs (workspace name + custodian + create date).

**Phase**: 1 (this exists; Connect just consumes it).

#### 4.2.4 Sync reconciliation agent

**Job**: monitors the sync state across a connection. Flags anomalies: "iManage filed 200 contracts this week but your Spaarke analyses only show 187 — here are the 13 with errors and a likely cause." Proactive, not reactive.

**Why it matters**: turns the Event Log from a forensic tool into a conversation. "Why is sync failing?" gets an explanation, not a stack trace.

**Where it runs**: a small JPS playbook (`connect-health-summary`) over a window of `sprk_externalevent` records. Produces a markdown summary for the admin dashboard.

**Phase**: 2.

#### 4.2.5 Migration promotion agent

**Job**: when a customer is ready to promote external references to managed Spaarke documents (the migration on-ramp), an AI agent inspects the candidate set ("all closed matters older than 5 years that have been viewed less than once in the last year"), proposes a migration plan, estimates duration, and surfaces risks (missing metadata, non-standard types).

**Why it matters**: makes migration approachable. The customer asks "what should we migrate next?" instead of designing the migration themselves.

**Where it runs**: JPS playbook + the existing analytics infrastructure.

**Phase**: 3.

### 4.3 Should we architect these as agents specifically?

The word "agent" carries weight in this codebase — there's an Agent Framework migration in flight ([knowledge/agent-framework/](knowledge/agent-framework/)), JPS playbooks already function as agentic flows, and `sprk-chat` is an agent product.

**Recommendation: use the existing JPS playbook framework as the default execution model for all five AI features above.** Do not introduce a parallel "Connect Agent" concept. Reasons:

1. **JPS playbooks already are agentic**. They orchestrate tools, include knowledge retrieval, support conditional logic. Each of the five features above is naturally a playbook with 1–5 tool calls.
2. **One AI execution surface to operate, monitor, and version.** Splitting "playbooks for analysis" and "agents for integration" doubles the operational surface.
3. **Customer-extensible by the same mechanism.** A customer can author their own JPS playbook to extend Connect's AI behavior — the same authoring tools, the same testing, the same library.
4. **The Agent Framework migration applies uniformly.** When playbook execution moves to Agent Framework, Connect's AI moves with it. No second migration.

The few times "agent" is the right shape and "playbook" is not — interactive, long-lived, multi-turn — apply to **Connect Admin chat** *(Phase 3)*: a chat surface in the Admin Code Page where an admin can ask "show me all unmapped iManage doc types" or "why did this morning's sync fail?" and the chat agent has tools that query Connect's own state. That is a `sprk-chat`-shaped product, and it should reuse the chat infrastructure, not a parallel one.

### 4.4 Where AI explicitly does NOT belong

- **In the transformation runtime.** Mappings are deterministic. Stochasticity in field transformation is unacceptable for an audit-grade legal product. AI *suggests* mappings; the deterministic engine *executes* them.
- **In webhook routing.** The mapping from an incoming webhook to a `JobType` is a table lookup, not a model call.
- **In secret handling, auth, or HMAC verification.** No AI involvement, ever. Pure code paths.
- **In the BFF when called by Connect.** The BFF doesn't know or care whether its caller is a human, a UI, or Connect. Adding "this came from Connect" logic into AI code would couple layers that the design deliberately decouples.

### 4.5 Summary

| Layer | AI's role | Implementation |
|---|---|---|
| **Workload Spaarke delivers** | Core value — analyses, classifications, search, chat | Existing BFF AI services, called over internal API |
| **Setup ergonomics** | Suggestion, not execution — proposes mappings, crosswalks, entity links | New JPS playbooks (`connect-mapping-suggest`, `connect-crosswalk-suggest`) — Phase 2 |
| **Operational health** | Reconciliation, anomaly detection, summarization | JPS playbook over event log — Phase 2 |
| **Migration** | Plan generation, candidate selection, risk identification | JPS playbook — Phase 3 |
| **Admin chat** | Interactive, multi-turn assistance over Connect state | `sprk-chat` instance bound to Connect tools — Phase 3 |
| **Transformation, routing, auth, secrets** | None | Deterministic code |

---

## 5. Net Effect on the Plan

These follow-ups don't change the v1 architecture in [design.md](design.md). They sharpen the design by making three things explicit:

1. **Transformation is a chained, declarative, deterministic pipeline** — not a single step, not a sandbox, not AI-driven.
2. **Mapping is three independent layers** — schema mapping, reference data crosswalks, entity resolution — each with its own UX and storage. Conflating them is the most common failure mode in integration products.
3. **AI's role is "make Connect easier to set up and operate" — not "execute the integration."** The first AI-assisted ergonomics arrive in Phase 2 (mapping suggestion, crosswalk assistant, reconciliation). Migration assistant and Connect Admin chat in Phase 3.

No third-party iPaaS dependency for the core. Customers' existing iPaaS investments flow through the BYO REST endpoint. Azure Data Factory makes sense only for one-time bulk historical loads in Phase 2. Logic Apps is a customer-facing publication channel in Phase 3, not a runtime we host on.
