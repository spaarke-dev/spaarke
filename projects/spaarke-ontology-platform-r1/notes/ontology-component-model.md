# Spaarke LOI Platform — Component Model, Terminology & Definitions

> **Status**: Working reference from the 2026-09-21 → 09-24 owner design sessions. **Not ratified.** Companion to [`spaarke-ontology-strategy-synopsis-v2.md`](spaarke-ontology-strategy-synopsis-v2.md) (strategy) and [`phase0-codebase-inventory.md`](phase0-codebase-inventory.md) (verified state).
> **Purpose**: fix the vocabulary and the component boundaries so downstream design work stops re-deriving them. §10 lists what should become ADRs.
> **Audience**: Ralph (owner); `design-to-spec` / `project-pipeline`; any future session picking this up.

---

## 1. What the platform does

> **Spaarke consolidates information from the systems a legal organization already runs, resolves it into one governed picture of its matters and parties, matches that picture against the organization's own rules to propose actions, and accumulates the record of what was decided.**

Four clauses, each load-bearing. Drop **consolidate** → a point solution. Drop **resolve** → a dashboard over mismatched data. Drop **match + act** → a data warehouse. Drop **record** → automation, not intelligence.

### 1.1 Naming `[2026-09-24]`

| Scope | Name |
|---|---|
| The platform | **Spaarke Legal Operations Intelligence Platform** — or just **Spaarke** |
| The three-pane user app | **Spaarke Console** |
| The model-driven app | **Spaarke Matter Management** |
| The external SPA | **Spaarke External Access** |
| Source-system integration (SKU) | **Spaarke Connect** (component name: **Connection Engine**; code namespace `Sprk.Connect.*`) |

### 1.2 The value hierarchy — what is the product and what is load-bearing

> **Orchestration is the product. Linked evidence is load-bearing, not supplemental. Co-display alone is the trap.**

A portal over three systems is a category with a bad history — users keep their bookmarks, the wrapper is always slightly worse than the native app, and it has to be maintained against three vendors' release cycles. **Never lead with the integrated view**; lead with the orchestration, and the view becomes *"and of course you can see everything behind it."*

But the integrated view is **not merely supplemental**, for three reasons:

1. **It is the evidence surface.** When the briefing says *"matter 4471 is over budget, ask Fenwick,"* the user's first reaction is *"is that right?"* **If they cannot verify, they will not confirm.** The view is the show-your-work layer without which recommendations don't get acted on.
2. **It is where the Decision Record is read.** *"What did we decide about this matter"* is only useful while looking at the matter.
3. **It is the visible proof that consolidation happened** — the capability is invisible until one screen shows four systems' data.

**The distinction that decides whether it is a wrapper:**

> **Co-display is a portal. Linked display is the ontology made visible.**

Showing a matter's iManage documents, TyMetrix invoices and Exchange thread *side by side* is a portal, and anyone can build it. Showing them because **the rung ladder resolved that thread to that matter**, the invoice lines were matched, and the documents were associated — that is something no source system can do, because each holds only its own third of the picture (§4.3, links are the moat).

**So the view is only a wrapper if the links aren't real**, which makes Resolution the component that turns the view from portal into product.

**Build rule that follows:**

> **Build read surfaces for what you act on — not for everything you bind.**

No general browsing wrapper over TyMetrix and iManage. Instead: for every item the briefing flags, the user can drill into the facts behind it. Much smaller surface, keeps us out of the portal business, gives the orchestration its credibility.

---

## 2. The architecture model

```
                 ONTOLOGY  (declared model)
             objects · links · bindings · policies
              action definitions · identity rules
                          ▲
          ┌───────────────┼───────────────┐
          │               │               │
    CONNECTION        INSIGHTS         ACTION
      ENGINE           ENGINE          ENGINE
   bind · resolve   fact · match    execute · gate
          │               │               │
          └───────────────┼───────────────┘
                          ▼
                  DECISION RECORD  (output)
```

**The engines are decoupled by the ontology, not chained to each other.** They communicate through Dataverse rows and Service Bus, never by direct call. That is why they can ship independently, and why many worktrees can touch the platform concurrently.

### 2.1 The six-stage pipeline

**bind → resolve → compute facts → match to action → execute under gate → record**

| Stage | Owner | State (Phase 0 + source review, 2026-09) |
|---|---|---|
| **bind** | Connection Engine | ❌ zero code; full design exists |
| **resolve** | Resolution (shared service) | ✅ strong — but email→record only |
| **compute facts** | Insights Engine | ⚠️ real but scattered |
| **match** | **Policy** (home TBD — §9) | ❌ hardcoded in ~7 places |
| **execute under gate** | Action Engine | ✅ strongest layer; **authority missing** |
| **record** | Decision Record | ⚠️ substrate exists; record type does not |

**The two weak stages are the front and the middle — `bind` and `match`.** That is the sequencing answer: binding leads (consolidation is the defining act), Policy follows (nothing to match until data is consolidated), Decision Record is downstream of both.

---

## 3. Terminology decisions

**Principle: do not invent vocabulary where the repo already has it, and do not adopt Palantir terms unless they are industry terms of art.**

| Term | Verdict | Why |
|---|---|---|
| **Ontology** | ✅ **Keep** | Now industry vocabulary — Microsoft Fabric IQ uses it in its own docs |
| **Connection Engine** | ✅ **Adopt** as the architecture component name | Parallel to Insights Engine / Action Engine. Keep `Sprk.Connect.*` code namespaces; "Spaarke Connect" remains available as a commercial SKU name |
| ~~binding registry~~ | ❌ **Drop** → `sprk_externalconnection` | Already designed in `spaarke-connect-integration-module-r1` |
| ~~connector manifest~~ | ❌ **Drop** → `sprk_externalfieldmapping` + its JSON mapping DSL | Same |
| **"Binding"** (source-side) | ⚠️ **Collision — do not use.** Use the Spaarke Connect entities | ADR-039 already uses *Binding* for the Action↔consumer pairing (`PublicContracts/Binding.cs`, `BindingId`, `sprk_playbookconsumer`) |
| ~~ledger~~ (customer-facing) | ❌ → **Decision Record** | "Ledger" connotes append-only correctly but describes nothing. Keep *ledger* in engineering docs |
| ~~originate~~ (binding mode) | ⚠️ Prefer **"system of record"** | Palantir-flavoured; SOR is the industry term |
| ~~field classes~~ | ⚠️ Prefer **"attribute ownership"** | Plainer, and says what it does |
| ~~typed outcome~~ | ⚠️ Consider reusing **"disposition"** | We already have 8 action dispositions + File/File+Act/Route/Hold/Dismiss for communications; an Inquiry's outcome is the same shape |
| **reference / mirror** | ✅ Keep | Ordinary data-integration terms, not Palantir's |
| **Action · gate · disposition** | ✅ Keep | ADR-039, already ours |
| **Fact / Observation / Precedent / Inference** | ✅ Keep | Insights Engine, already shipped |
| **Authority** | ✅ Keep | Generic; UAC-adjacent |

---

## 4. Core definitions

### 4.1 "Declared"

**Declared = expressed as data the runtime reads. Code = a `.cs` file the runtime executes.**

> **The test: can you change it without a deployment?**

| Declared today | Code today |
|---|---|
| `sprk_analysisaction` rows (prompt, output schema, scopes) | `AiClassificationRung.cs` |
| `sprk_communicationrule` rows | `MatterLiveFactResolver.cs` |
| `sprk_gridconfiguration` rows | `AutoFileGate.cs` |

A **fact predicate is currently on the wrong side of this line**: adding `matter.daysSinceLastActivity` requires C# and a deploy, though it behaves like a declaration. That is the D-u gap, and it is the ceiling on Policy's configurability.

### 4.2 The three-way split

> **States what should be true or is permitted → ontology.
> Executes → platform.
> Records what happened → output.**

| Ontology (declared) | Platform (compiled) | Output |
|---|---|---|
| object classes + links | connectors, transport | **Decision Record** |
| binding + attribute ownership | resolution engine | resolution map |
| policies | policy **evaluator** | claims produced |
| action definitions | dispatch + gate engine | |
| identity rules | widgets, surfaces | |

Declarations are **edited**. Output is **append-only**. Rulebook vs scorebook.

### 4.3 Object

A Dataverse table is storage. An ontology **object** is that table plus six declarations:

| | Declares | Lives in |
|---|---|---|
| **links** | which typed relationships carry governance | Dataverse relationships |
| **binding** | which source, which mode, per-attribute authority | `sprk_externalconnection` + `sprk_externalfieldmapping` |
| **attribute ownership** | pointer / projected / derived, per field | mapping DSL (needs extending) |
| **predicates** | what can be deterministically computed about it | `ILiveFactResolver` (code today) |
| **permitted actions** | which Actions may target it, and what gate each needs | `sprk_analysisaction` + Binding |
| **identity rules** | how a source record resolves to an instance | the rung ladder |

> **There is no "object definition" artifact.** An object's definition is *assembled* from six places that already exist. Build a registry only if the definition must be queryable as a unit — which the MCP server may require to describe itself. **Open (§9).**

### 4.4 Attribute ownership (was "field classes")

Three classes, coexisting **in one row**:

| Class | Fields | Rules |
|---|---|---|
| **pointer** | `sourcesystem`, `sourceid`, `sourceurl`, `sourceetag` | always stored, never edited |
| **projected** | the minimum for routing, filtering, display, edge construction | stored with `sourceasof`; read-only in UI when the source is authoritative |
| **derived** | embeddings, extractions, classifications, scores, epistemic class | Spaarke-owned, always writable |

**The mode is per-field, not per-row.** A mirrored `sprk_matter` is simultaneously untouchable pointer fields, read-only projected fields, and fully writable derived fields. That is what lets Spaarke attach an intake disposition, a priority and a score to *someone else's* matter. A row-level lock makes the layer useless.

### 4.5 Binding modes

| Mode | Behaviour | Access required |
|---|---|---|
| **reference** | rows appear **lazily**, when something surfaces the item. No complete inventory | retrieval only — **MCP is sufficient** |
| **mirror** | a complete, continuously synchronized set | **enumeration + change detection** — API, export, or replica |
| **system of record** | Spaarke is authoritative | none |

**Both reference and mirror create Dataverse rows** — Matter is the spine, so `sprk_document`, `sprk_servicerequest`, `sprk_communication` and `sprk_todo` all need a lookup to it. The distinction is enumeration and change detection, which is where cost and vendor dependency live.

**Hard constraint: exactly one system is authoritative per attribute, declared explicitly.**

### 4.6 Connector vs binding — one connector, many bindings

- **Connector** = the *mechanism*. Code + credentials. "How do I call Aderant." One per source system; **one MCP client serves all MCP sources.**
- **Binding** = a *declaration* pairing one object class to one source via a connector, with mappings, per-attribute authority, and cadence.

The Aderant connector serves the Matter binding, the Timekeeper binding and the Client binding. And one class can carry multiple bindings — matter identity from the MM system, matter financials from e-billing — which is exactly **why authority is per attribute, not per class**.

### 4.7 Which mechanism per class

> **MCP feeds interaction. APIs and exports feed state.**

MCP gives session-scoped, user-permissioned retrieval — no enumeration, no CDC, no stable schema contract, no writeback. And results are *per-user*, so two people see different corpora. **You cannot build stable state from user-scoped retrieval.**

| Class | Mode | Mechanism |
|---|---|---|
| **Document** | reference | **MCP** — the one case where it is right (iManage, NetDocuments both ship servers) |
| **Matter** | mirror | vendor REST API (Aderant, Elite 3E, ProLaw) · DB read replica for on-prem · scheduled export |
| **Invoice** | mirror | **LEDES file drop** — vendor-neutral, zero connector; most orgs already produce it monthly |

### 4.8 Policy

**Policy is the `match` stage** — the rule that gets from context to an action. It is not governance bolted on; versioning and citation are free byproducts of moving the rule out of code.

**What the customer writes** (rules they already own, today living in someone's head, an email, or a hardcoded threshold):

- *"Any invoice over $50k needs my approval before payment."*
- *"Litigation matters over 15% variance at phase 3 need a partner explanation."*
- *"Outside counsel can't bill travel without pre-approval."*
- *"Requests from Sales about customer contracts route to Jane."*

**Schema** — two entities, different lifetimes:

- **`sprk_policy`** — stable identity: code, name, owner, category, scope, status. Never versioned; this is what a decision refers to.
- **`sprk_policyversion`** — immutable, 1..N: version number, effective from/to, author, approver, approval date, **rule body**.

**Seven closed rule types**: `Threshold` · `AllowList` · `Switch` · `DecisionTable` · `Envelope` · `SlaDefinition` · `DelegationLimit`

**Policy appears twice in the pipeline** — as **evaluator** (does this state breach a rule?) and as **router** (which action does the breach warrant, and who may authorize it?).

**Three moments the customer touches it:**

| When | What they see |
|---|---|
| authoring | a model-driven list, "Our Rules" → open → edit → new version with an effective date |
| in flow | a task appears, *"flagged by Budget Variance Policy v3"*, linked |
| afterwards | *"why did this happen?"* → the exact rule text in force that day |

**What it replaces** — the seven shapes Phase 0 found: `sprk_communicationrule` · `CommsPolicyOptions` · `AffinityOptions` · `AutoFileGate`/`CategoryRoutingGate`/`TrackingFooterGate` · `SignalEvaluationService` thresholds · `ConfirmationPolicyEngine` risk tiers · `sprk_emailupdatefield` allow-lists. **Success is measurable: seven shapes → one.**

### 4.9 Policy vs access control — orthogonal

| | Access control (UAC / Dataverse roles) | Policy |
|---|---|---|
| Question | **"May this person see or edit this record?"** | **"Given the state of these records, what should happen?"** |
| Subject | a **user** | the **data** |
| Answer | yes / no | an action, classification, or threshold result |
| Visibility | **invisible when working** | **visible when working** — it is why a task appeared |

> **Remove access control → everyone sees everything. Remove policy → nothing happens.**

**They meet at authority**, which is why UAC cannot absorb Policy. A paralegal may have *write access* to an invoice row and hold no *authority* to approve a $50k write-off. Dataverse security expresses the first and cannot express the second.

> **Access is the floor · policy is the rule · authority is policy narrowing an action beyond what access already permits.**

### 4.10 Policy vs Action

| | Answers | Authored by | Versioned |
|---|---|---|---|
| **Action** | *how* — capability: inputs, tools, output schema, disposition, gate required | **we ship it** | no |
| **Policy** | *whether, when, and by whom* | **the customer** | **yes** |

Today they are **fused** — the Action Definition carries its own trigger spec and `humanGate.approvalRoles`, and thresholds live in C#. Three reasons to separate:

1. **One Action, many policies** — `send-budget-inquiry` at 15% for one customer and 25% for another, without cloning the Action.
2. **Policies are citable; Actions are not.** You cite "under POL-BUDGET v3"; you never cite "under send-email v2."
3. It is the platform/package split applied to this pair: **shipped capability, customer-authored conditions.**

### 4.11 Policy is not a playbook

The node-playbook failure was declaring **control flow** in JSON. A policy declares a **predicate**.

| | Playbook | Policy |
|---|---|---|
| Declares | do A, then B, if C then D, pass output, retry, gate | `variance > 0.15 AND phase == 3` → RED |
| Sequencing / state / variable passing / error handling | **all four** | **none** |
| Testable | no | **totally** — enumerate inputs, check outputs |
| Growing it | new **node type** = code | new **input field**, which is bounded |

**Natural language never reaches the runtime**: **LLM at author time, deterministic at run time.** The customer types a sentence, an LLM proposes a typed rule body, **a human approves the compiled form**, and the runtime sees only the typed form. Compare playbooks, where the declared artifact *was* the executed artifact.

> **Failure condition, stated plainly: if we cannot hold seven closed rule types, Policy becomes a playbook and we have repeated the mistake.** Defense: adding a rule type is a **code change with review**, never a config change — which makes the ratchet visible rather than gradual.

**Alternative framing, equally true:** a policy is **a bounded query plus a consequence plus a version.** Technically legible (Advanced Find, SQL view, alert rule); commercially it is "our rules," and nobody versions or cites a query.

### 4.12 Policy configurability — the closed vocabulary

Two layers, both data:

| Layer | Artifact | Authored by |
|---|---|---|
| the rule | a `sprk_policyversion` row | legal-ops admin, in a model-driven form |
| the perception (only where narrative reading is needed) | a prompted `sprk_analysisaction` + output schema | domain services, or an advanced admin |

**Where narrative judgment is genuinely needed — the LLM does perception, the policy does judgment.** OCG example: the LLM extracts `{activityType: "travel", preApprovalReferenced: false}` as a confidence-scored, quote-grounded Observation; the policy then evaluates deterministically. D-F0 holds — the model call happened *upstream*, producing a fact-shaped input.

**A policy may reference only fields that exist**: Dataverse columns, `ILiveFactResolver` predicates, or extraction-schema outputs. **A customer cannot write a policy about something we do not extract.**

**The honest limit — configurable *content*, not configurable *concepts*:**

| Adding | Costs |
|---|---|
| a rule *instance* | a row |
| an extractable *concept* | an Action (data) |
| a **rule type** | **code** |
| a **fact predicate** | **code today** — the D-u gap |

### 4.13 Decision Record

**An append-only business record of decisions made** — not a notification, not a task.

```
2026-03-12 14:22   Matter 4471
  Proposed:  Send budget inquiry to Fenwick
  Because:   budget_variance = +18.4%  (from INV-8830..8832)
             evaluated by POL-BUDGET v3
  Gate:      Confirmation required (risk tier: financial-external)
  Decided:   M. Reyes, Legal Ops Manager — own-role authority
  Result:    INQ-0042 created; email sent
```

> **Work queue = things not yet decided. Decision Record = things decided.**

**Written at the gate. Every gate decision — approved, rejected, timed out, auto-approved — produces exactly one entry, regardless of how many rows it touched. No gate, no entry.**

**Three uses, in the order that matters commercially:**
1. **It becomes the Matter Report Card** — 200 entries across four quarters is outside-counsel performance data nobody has ever compiled. *This is the asset.*
2. It answers *"why did we do this in March?"*
3. It is what makes agents permissible to run unsupervised.

**It is NOT `SessionLedgerEntries`.** That is a *conversation trace* for debugging and replay, keyed by chat session. Overlap is ~3 of 10 fields and none of the important ones.

| Transfers | Does not |
|---|---|
| storage tiering (ADR-015 Cosmos Tier-2) | the schema |
| **the gate as the write trigger** | the key |
| `ISessionTraceReader` as a read-seam pattern | retention model, semantics |

The two coexist cleanly — session trace keeps doing debugging; Decision Record does the business record. No migration.

**Why not Dataverse audit:** values >5 KB truncated · Web API `RetrieveRecordChangeHistory` omits `AuditRecord` (no who/when) · audit is blind to *rejected* gates, which write no row · one decision spanning four tables yields four disconnected entries · **no as-of query exists, and none is on the roadmap.** Audit is corroboration on the authority/delegation tables; budget Log capacity if enabled broadly.

### 4.14 Authority

Two things, neither of which exists:

- **who is permitted** — `requiredRole`, `delegationPolicyRef` **+ version**
- **who actually did it** — `actorId`, `actedAt`, `authorityBasis` ∈ {own-role, delegated, escalated}

Today `ConfirmationPolicyEngine` asks *"is this risky enough to need confirmation?"* It never asks *"is this person allowed?"*

**Build as an extension to UAC**, whose r2 design already has the hard parts for the *access* question: an append-only access-event log, a **versioned evaluator** so historical answers stay reproducible, and an attestation surface. **Key the authority table on `systemuserid` so humans and agent users share one model** — free now, expensive later.

**The test:** fourteen months later, in a dispute — *"who approved this, under what authority, and was that authority valid on that date?"*

### 4.15 How third-party data lands — and why Policies and Actions work over it

**Simple version:**

> **When third-party data comes in, it becomes a normal Dataverse row of an entity we already have, plus a few columns saying where it came from. There is no separate "external data" store.**

A matter from Aderant is a `sprk_matter` row. To forms, views, queries, widgets, policies and actions it looks like every other matter. The only differences:

- extra columns — `sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`
- some columns are **read-only**, because the source owns them (attribute ownership, §4.4)

> **Why this makes Policies and Actions work: they operate on entities, not on sources.** A policy says *"matters where variance > 15% at phase 3"* — it queries `sprk_matter` and never knows the row came from Aderant. An Action declares it targets `sprk_matter`. Neither has any notion of a connector.

**That is the payoff of the whole design in one sentence: bind once, and everything downstream works unchanged.**

Two corollaries worth stating, because they are easy to get wrong:

1. **The fact layer is source-agnostic by construction.** `DailyBriefingCollector` does not change when Aderant is bound — consolidation happens *below* the query layer. The only widget-visible change is a freshness stamp (*"as of 4 hours ago"*) on source-derived numbers, which is platform-level, not per-widget.
2. **"Spaarke's own data" vs "third-party data" is not a meaningful distinction at query time.** Once mirrored, `sprk_matter` **is** the third-party data. The distinction that matters is *attribute ownership* — who may write which column — not where the row is stored.

**Where a policy's inputs come from** — three sources, all in the closed vocabulary (§4.12):

| Input kind | Example | Mechanism |
|---|---|---|
| stored column (incl. projected from source) | `matter.phase` | Dataverse |
| **computed fact** | `matter.budgetVariance` | **materialize it** — see below — or `ILiveFactResolver` |
| extraction output | `invoiceLine.activityType` | Observation from a prompted Action |

#### 4.15.1 Materialize the fact — the Dataverse-native path `[2026-09-24]`

Worked example, budget variance, end to end:

| Field | Type | Code required |
|---|---|---|
| `matter.budget` | currency column (manual entry or CSV import for MVP; mirrored later) | **none** |
| `matter.invoicedToDate` | **Dataverse rollup** — SUM over `sprk_billingevent` | **none** |
| `matter.budgetVariance` | **Dataverse calculated column** — `invoicedToDate / budget − 1` | **none** |

> **The fact computation is two native Dataverse column types and zero C#.**

Which cascades: the worklist becomes a **Dataverse view** filtered on `budgetVariance >= 0.15 AND phase = 3`, membership needs no server-side evaluation service, and **a Policy for MVP is a stored filter + a target action + a version number — with Dataverse itself as the evaluator.** That is "a bounded query plus a consequence plus a version" (§4.11) made literal, and it closes **CM-3** for MVP.

**Limits to respect:** rollups recalculate on a schedule (~hourly) and cannot traverse many hops; calculated columns cannot express everything. Both are fine at MVP scale. `ILiveFactResolver` remains the path for facts that outgrow them — and the policy's input vocabulary is unchanged either way, so outgrowing a rollup is an implementation swap, not a redesign.

**Note on the budget itself:** LEDES carries invoice *lines*, not budgets. Budget is a separate input — a column the customer populates for MVP; a second binding, or a `sprk_budget` entity with phases, later.

**Landing pattern — direct, with raw retained:**

| Option | Verdict |
|---|---|
| **Direct landing** — the connector writes straight into `sprk_matter` | ✅ **Recommended.** Simpler; no second Dataverse table |
| Staged landing — write to a staging entity, then project | ❌ adds a table and a hop |
| **Plus: retain the raw payload in Cosmos keyed by sync run** (ADR-015 tier) | ✅ **Required.** This is what buys replay — fix a mapping rule and re-run, rather than repairing 4,000 rows. It recovers Foundry's immutable-raw property without building Foundry |

### 4.16 The ingest template — seven steps

Email intelligence already implements all seven **bespoke, for one channel**. That makes `Services/Communication/` simultaneously the proof the pipeline works and the thing to absorb.

| # | Step | Email does it via | Should become |
|---|---|---|---|
| 1 | **ingest** | Graph webhook | Connection Engine |
| 2 | **land** | create `sprk_communication` | Connection Engine (+ pointer fields) |
| 3 | **resolve** | rung ladder → matter | shared Resolution service |
| 4 | **evaluate** | `sprk_communicationrule` (proto-policy) | **Policy** |
| 5 | **surface** | communication queue | the configurable list widget (§6.1) |
| 6 | **act** | dispositions | Action Engine |
| 7 | **record** | regarding + status | **Decision Record** |

**Replicating the template for a new source is: write step 1, declare steps 2–4, reuse 5–7.** That is the concrete meaning of "compiled once for everyone, configured per customer."

---

## 5. The engines

### 5.1 Connection Engine (`Sprk.Connect.*`)

**Purpose:** get external data in and keep it linked. Owns `bind`.

**Status:** design-only, ~12-week MVP scoped in `spaarke-connect-integration-module-r1`. **Deliberately outside `Sprk.Bff.Api`.**

**Components:** `Sprk.Connect.Api` · `.Worker` · `.Transform` (mapping DSL runtime) · `.Connectors.iManageWork10` (pilot) · Connect Admin Code Page (side-by-side mapper + event log with DLQ replay).

**Entities:** `sprk_externalconnection` (per tenant + connector instance; KV secret refs) · `sprk_externalref` (per-record stub incl. `externalEtag`, `lastSyncedAt`) · `sprk_externalfieldmapping` (JSON DSL bodies) · `sprk_externalevent` (append-only, idempotency keys). Plus `sprk_storagemode` Internal/External and the `(sprk_externalsystem, sprk_externalkey)` alt key on `sprk_document`.

**Three gaps vs what the ontology needs:**
1. **Reference-mode only.** Commitment #1 is "reference, don't replicate." **Enumeration, change-detection and writeback seams do not exist.** Mirror mode for Matter and Invoice is net-new.
2. **No per-attribute authority.** The DSL maps fields; it never declares which system wins.
3. **Document-centric.** Extending to Matter, Invoice, Party, Timekeeper is additive but real.

**Writeback to source — no mechanism designed.** Patterns A–D are defined conceptually in the strategy synopsis §3.4, but nothing is built and Connect explicitly lacks the seam. It assembles from existing parts rather than being a new subsystem:

- a queued job (ADR-004 `IJobHandler<T>`) calling the source API
- `IIdempotencyService` for the idempotency key
- conflict detection on `sourceetag`
- status tracked on the originating Decision Record entry
- **per-action, per-system enablement in `sprk_externalconnection.enabledCapabilities`** — that field already exists in the Connect design

A job handler plus a declaration. Scope it properly; it is not large.

### 5.2 Insights Engine

**Purpose:** produce claims with provenance. Owns `compute facts` — and, recommended, `match`.

**Status:** **shipped r1/r2**, narrowly surfaced (`/api/insights/ask|search|assistant/query`; client-side essentially one widget — `InsightSummaryCard` on `MatterHeaderView` and `VisualHost`).

**Key asset:** `InsightArtifact` — a polymorphic record carrying `Subject` · `Predicate` · `Value` · `Evidence[]` · `AsOf` · `ProducedBy{Kind,Id,Version}` · `Scope` · `ValidFrom`/`ValidTo`, with **four** tiers: `Fact` (confidence 1.0) · `Observation` (probabilistic, confidence-gated, human review) · `Precedent` (SME-confirmed) · `Inference` (never stored). **That is a claim triple with provenance and temporal validity — the ontology's claim layer, already built, under a different name.**

**Note:** a deterministic **policy evaluation is a `Fact`**, `producedBy: {kind:"query", id:"policy://POL-X", version:"v3"}` — *not* an Observation. Mislabelling inherits review semantics it does not need and muddies D-F0.

**Gaps:** declarative predicates (3 `LiveFactResolver`s today) · `IInsightGraph` is stub-only · `ProducedBy.Kind` lacks a fourth kind for **human / external-party** answers (needed for typed outcomes).

### 5.3 Action Engine

**Purpose:** execute what the rules select, under gates. Owns `execute under gate`.

**Status:** design-only, **on hold at Phase 0 (~5%)**, re-based on `spaarke-ai-architecture-redesign-r1`, resumption gated on G-P3 **plus owner direction**.

**Reusable regardless:** Tool Registry metadata (`Classification`/`CostClass`/`LatencyClass`/`Idempotency`/`AuthMode`/`ModelTier`/`EvidenceRequired`/`PhaseRestrictions`) · the **template + variable model** (Who/What/Which/When/Window/Threshold/Where/How; 15–25 templates claimed to cover ≥90%) · `IGateResolver` + `sprk_gate_approval` with poll/webhook resume · **Monitor** (binds an Insights signal to an Action — the Fact→Inquiry trigger path, already designed).

**Two things it repeats rather than solves:** the **run record is keyed by run, not object** (the session-ledger limitation one level up), and **Human Gate is a static `approvalRoles` list** — authority's shape without authority's semantics.

### 5.4 Resolution — a shared service, not an engine

The 13-rung ladder with an explicit deterministic/AI partition (`RungKind.cs`), `AffinityStore`, `sprk_recordtype_ref`'s 7 identifier types, and the `RecordMatching` service. `AiClassificationRung` is **structurally barred** from emitting a record GUID or auto-filing.

**Used by both Connection Engine and the email path** — which is why it is a shared service. Per the strategy synopsis it is the **central engineering asset**: consolidating from N systems *is* the identity problem, and we have forsworn the FDE headcount Palantir uses to solve it, so it must ship pre-built.

**Extension needed:** party/org resolution across bound systems, with **stub-and-resolve** (create on first reference keyed `(sourcesystem, sourceid)`; merge later via the ladder or a human queue) and **resolution decisions kept as reversible records** — source key → target GUID, rung fired, confidence, confirmer, timestamp — with the Dataverse lookup *derived* from that rather than treated as truth. That recovers Palantir's replay/bulk-correction property in an object architecture.

**Bounding rule for lookups:** promote a lookup to an edge only if the edge **carries governance or analytic weight** (Matter→Client = yes; Matter→Originating Office = text). Same guard as projected-set creep, applied to relationships.

---

## 6. Surfaces

| Surface | For | Ontology's contribution |
|---|---|---|
| **Spaarke Console** (three-pane app) | everyday users | **widgets** they select; the Assistant launches them |
| **Spaarke Matter Management** (model-driven app) | admin / operator / full matter management | forms over ontology entities |
| **Spaarke External Access** (SPA) | requesters, Legal Front Door | Request origination |
| **Office add-ins** | Outlook / Word | capture, save, share |
| **MCP server** | external agents + Foundry IQ as a knowledge source | object read, link traversal, fact query, gated action invocation |
| **Copilot declarative agent** | M365 | existing |

**Two scope reductions that fell out:** policy authoring and connection admin are **model-driven app forms**, not custom surfaces. Dataverse gives the form, version list, row security and audit for free.

**The configurability answer:** **the widget catalog is platform code; widget selection and layout is per-tenant / per-user configuration** — `WorkspaceWidgetRegistry`, six system layouts, `WorkspaceLayoutWizard`, PaneEventBus. Already built. That satisfies *compiled once for everyone, never per customer*.

### 6.1 The worklist — one widget archetype, not five `[2026-09-24]`

A **worklist** is a list whose **membership is computed by rule evaluation**, not by a user's filter, where each row carries: the object · **why it is there** · the triggering facts · the actions that resolve it.

| | Dataverse view | **Worklist** |
|---|---|---|
| Membership | "matters where status = open" | "matters where `POL-BUDGET v3` fired, or an SLA breached, or a gate is pending" |
| Row says | column values | **why it appeared** |
| Actions | navigate | the action that resolves it, gated |

**Prior art — this is a well-worn pattern, not a new idea:**

- **Palantir / Workshop** — an *object set* filtered by a function or rule, rendered as a table with **Action Type buttons** per row; object detail views show properties, links and available Actions as buttons.
- **ServiceNow** — "My Work": tasks placed there by workflow rules, actions inline.
- **Dynamics Sales Accelerator** — a prioritized work list driven by sequences, next action on each row.
- **Salesforce** — next-best-action panels.

**Daily Briefing, the work queue and email triage are the same widget** — Daily Briefing with a narrative wrapper, the work queue without one, email triage filtered to communications. That collapses "4–5 new widgets" into **one configurable archetype plus a narrative variant**, and makes a customer's briefing a *configuration*.

**Guard:** over-generalizing UI is the playbook trap in another costume. The configurable dimensions must be a **closed set** (facts shown · policies evaluated · actions offered · grouping/sort · narrative on/off), not an open one.

#### 6.1.1 How a worklist row is actually produced

**Evaluate on write and persist the flag** — not evaluate on read.

| | A — evaluate on read | **B — evaluate on write** ✅ |
|---|---|---|
| Widget asks | a service: "give me flagged items" | **Dataverse: a view** |
| The reason | recomputed every load | **a stored field** |
| Cost | evaluation over every matter, every load | evaluation once, on change |
| Reportable / historizable | no | **yes** |

**The flow:**

1. Something changes (an invoice lands; or a nightly job runs)
2. Policy evaluates → if true, **write a flag row**: subject · policy code + **version** · the fact values that fired it · timestamp · `status = open`
3. **Worklist = a view over open flags**
4. *"Why it appeared"* = those stored fields
5. User acts → gate → **Decision Record** written · flag closed

So the worklist's data is a **flag entity**; the Decision Record is written when the gate closes. Two entities, clean lifecycle, and the Decision Record stays append-only.

**Check before building:** `SignalEvaluationService` (Finance) is described as a threshold engine producing signals — this may already exist in shape.

### 6.2 Charts and reports — we are not BI, but users still want them `[2026-09-24]`

"Not a dashboard" means **charts are not the primary interaction**, not that charts are unwelcome. Palantir ships Quiver and Contour for exactly this reason.

> **The worklist is the work surface. Charts and reports are a supporting surface — and Power BI over Dataverse covers them at zero build cost, with the customer's own analysts able to extend them.**

**Daily Briefing is the upgrade target, not a bad example.** Tested against the four clauses it exercises only presentation — no policy evaluation, no actions, no record. But the shape is right, and because the fact layer is source-agnostic (§4.15) the upgrade is mostly configuration plus a freshness stamp. The demo version:

> *Morning. Four things need you today:*
> *— 2 matters breached **Budget Variance v3** → [Ask outside counsel]*
> *— 1 inquiry to Fenwick is 4 days overdue → [Chase]*
> *— 1 invoice over $50k awaiting approval → [Approve] [Query]*
>
> Each says **why** it appeared. Acting writes the Decision Record.

**Email triage is the better exemplar of the loop** — it runs five of six stages today (§4.16). The target interaction is **Daily Briefing's presentation applied to email triage's loop, over consolidated third-party data.**

**Agents are a surface, not an engine.** An agent is a *caller* — it consumes all three engines and owns no pipeline stage. Its concerns already have homes: runtime → rented (Microsoft Agent Framework); tool catalog → Action Engine; identity → Entra Agent ID; **authority → the gap**; session state → ADR-040. **Revisit if** agents become registered, versioned, governed, monitored long-lived entities — which Agent 365 implies.

---

## 7. Approach decisions made

1. **Binding leads, Policy follows, Decision Record is downstream.** Front and middle of one pipeline, not competing priorities.
2. **MVP binding slice = two object classes × two modes** — Matter mirror, Document reference. Not a manifest covering every class.
3. **MCP for reference, API/export for mirror.** MCP structurally cannot feed state.
4. **Rent the transport; own the landing contract, resolution, projection and freshness.** A hand-built ingestion framework competes with ADF and loses.
5. **The connector count is ~6, not ~30** — one MCP client + one LEDES parser + ~6 MM mappings. We build an *index entry* (10–15 projected fields), not a warehouse row.
6. **Build for departments; bind for firms.** Firm-side consolidation layers (Entegrata, Intapp, Iridium) already exist — bind to them; one binding replaces six. Departments typically have none.
7. **Policy: seven closed rule types.** Adding one is a code change with review.
8. **LLM at author time, deterministic at run time.**
9. **One "needs attention" widget, not four queues.** Same argument as a universal ledger: four places to look means nobody looks.
10. **Policy authoring + connection admin = model-driven forms.**
11. **Decision Record is a new record type**, not an extension of `SessionLedgerEntries`; they coexist.
12. **Authority extends UAC**, keyed on `systemuserid` for humans and agents alike.
13. **`Services/Communication/` is the pipeline built once, vertically, for one channel** — both the proof it works and the eventual refactor target (its ingest → Connection Engine, resolve → shared resolver, dispositions → Action Engine).
14. **Fabric IQ is L5, not L1** — read-only, no instance write API, no policy versioning, and its Dataverse path strips row-level security. Legitimate as a downstream analytical projection; disqualified as authoritative.
15. **Third-party data lands directly into the existing entity** (+ source columns), with the **raw payload retained in Cosmos keyed by sync run** for replay. No staging entity.
16. **One configurable widget archetype**, not one widget per capability — with a closed set of configurable dimensions.
17. **Build read surfaces for what you act on**, not for everything you bind. No general browsing wrapper.
18. **Orchestration is the product; linked evidence is load-bearing; co-display alone is the trap.** The view is only a wrapper if the links aren't real — which makes Resolution the component that turns it from portal into product.
19. **Naming**: Spaarke Console (three-pane app) · Spaarke Matter Management (MDA) · Spaarke External Access (SPA) · Spaarke Connect (SKU) / Connection Engine (component).

---

## 8. Provenance & freshness (standing rules)

- Every projected object needs **visible source attribution and freshness**; every gap shows as a gap, not a zero.
- **Freshness honesty is not rentable.** Every transport goes stale silently. A sync run record, per-object `sourceasof`, and visible staleness in the UI are platform work.
- **Resolution quality is a measured, reported deployment metric.** An overlay that quietly mis-resolves 8% of parties is worse than one that says so.
- **Projected sets and edge promotions are declared and reviewed, never accreted.**

---

## 9. Open decisions from these sessions

| ID | Decision | Notes |
|---|---|---|
| **CM-1** | **Policy: a component inside Insights Engine, or a fourth engine?** | Recommended: inside Insights — a policy evaluation *is* a claim, `Services/Insights/` is already Zone B (D-F0-compatible), and §11 says extend before adding. **Counter:** if `match` is a pipeline stage peer to bind/fact/act, symmetry argues for an engine; it has 2 entities, 7 rule types, an evaluator and an authoring surface |
| **CM-2** | **Is an object-definition registry needed?** | Only if the assembled definition must be queryable as a unit — the MCP server may need it to describe itself |
| ~~**CM-3**~~ | ~~Rule body form — bespoke JSON DSL or Dataverse filter?~~ | **CLOSED for MVP (2026-09-24): a Dataverse filter.** Materializing the fact as a rollup + calculated column (§4.15.1) removes the "breaks down for computed facts" objection, and Dataverse becomes the evaluator. Revisit only when a fact outgrows a rollup |
| **CM-4** | **Reuse "disposition" for an Inquiry's typed outcome?** | We already have 8 action dispositions + the communication set |
| **CM-5** | **Are Inquiry and Request one object with a direction discriminator?** | Structural mirror images; would also settle `sprk_servicerequest` vs `sprk_legalrequest` |

Carried from the strategy synopsis and still open: D-f (binding-mode promotion), D-g (benchmarking consent), D-m (Wave 1.5 registry), D-o…D-v.

---

## 10. What should become an ADR

In dependency order. Each is small; the point is to fix the invariants that are **expensive to retrofit** before other projects create entities and gates.

| # | ADR | Fixes |
|---|---|---|
| 1 | **Ontology spine** (thin — ~3 pages) | attribute ownership + pointer fields + `sourceasof` on any new dual-mode entity · authority-basis fields on every gate · Decision Record is object-keyed and written at the gate · binding mode is declared, never hardwired |
| 2 | **Policy object** | schema, seven closed rule types, evaluator placement, the closed-vocabulary rule, the "adding a rule type is code" guard |
| 3 | **Temporal semantics** | immutable policy versions + effective/expiry on delegation rows as the substitute for absent bitemporal Dataverse; what audit is and is not for |
| 4 | **Authority** | extension to UAC; `systemuserid` keying; delegation semantics |
| 5 | **Connection Engine extension** | mirror mode, per-attribute authority, beyond documents — amending the Spaarke Connect design rather than replacing it |

The **full ontology specification** (object catalog, link taxonomy, ERD) should be an **assembly document written after** 1–2 land, not first. The expensive 80% of it is the part most likely to be wrong before a real customer touches it.

---

## 11. MVP scope `[2026-09-24]`

> **LEDES invoice ingest → threshold policy → worklist → inquiry action → Decision Record**

One slice that exercises **all six pipeline stages end to end**, needs **zero vendor connectors**, and is §8's second bootcamp SKU — *"if the buyer's pain is spend, run a LEDES drop plus the inquiry loop."*

| In | Out — and why |
|---|---|
| File-drop ingest (SFTP / upload) | Matter mirror + MM connectors — LEDES needs no vendor cooperation |
| `matter.budget` column + rollup + calculated variance | `ILiveFactResolver` extension — native Dataverse covers MVP (§4.15.1) |
| Policy as a stored filter + action + version | The full seven rule types |
| Policy authoring as a **model-driven form** | A custom authoring UI |
| One **worklist** widget + flag entity | The other widget archetypes |
| **Decision Record** entity + subgrid on parent + list view | — |
| Inquiry action through the existing gate | — |
| | **Authority** — while every action needs human confirmation, *the human is the authority* and Dataverse security answers "may this person confirm." Post-MVP |
| | **Writeback** (Patterns C/D) · **MCP egress** · **Fabric IQ projection** · **party/org resolution** |

**Two things to do now because they are free and expensive later:**
1. **Key any decision/approval table on `systemuserid`**, so humans and agent users share one model when authority arrives.
2. **Pointer fields + `sourceasof` on any entity the ingest touches**, per §4.4.

### 11.1 Workspace assessment — the one open question

Four concerns raised on 2026-09-24 reduced to one after review:

| Concern | Verdict |
|---|---|
| Can a workspace widget push to the Context pane? | **Non-issue** — `PaneEventBus` `context` channel, `ContextPaneController`, `useContextEventBridge` already exist; a widget publishes `context_update` the same way the Assistant does |
| Server-side membership / event-driven refresh | **Not required.** On-load refresh is sufficient — and §4.15.1 makes membership a plain Dataverse view |
| Conditional sections by binding mode | **Not required.** Build widgets that are binding-agnostic or binding-specific; layout config selects them |
| Tab lifecycle vs the 8-tab FIFO eviction | **Already solved** by pinning (`spaarke:workspace:pinned-list`) |

**The remaining question: can a worklist widget source its rows from a plain Dataverse query inside `WorkspaceLayoutWidget` / `LegalWorkspaceApp` embedded mode?** If yes, the existing workspace architecture supports the Console unchanged. That is the assessment to run.

---

## 12. Changelog

| Date | Change |
|---|---|
| 2026-09-24 | Created from the 09-21 → 09-24 design sessions. Supersedes scattered terminology in the strategy synopsis §§1.4, 4.7–4.8, 5.5, 6.6 |
| 2026-09-24 (b) | Added §1.1 naming (**Spaarke Console**) · §1.2 value hierarchy (orchestration vs linked evidence vs co-display) · §4.15 how third-party data lands and why Policies/Actions work over it · §4.16 the seven-step ingest template · §6.1 one widget archetype + Daily Briefing as upgrade target · writeback mechanism sketch in §5.1 · decisions 15–19 |
| 2026-09-24 (c) | **Scope-reduction pass.** §4.15.1 **materialize the fact** — rollup + calculated column, zero C#, which makes a worklist a Dataverse view and **closes CM-3**. §6.1 renamed to **worklist** with prior art (Palantir Workshop, ServiceNow, Dynamics) and §6.1.1 how a row is produced (evaluate-on-write + flag entity, not evaluate-on-read). §6.2 charts/reports are supporting surfaces via Power BI — "not BI" ≠ "no charts". **§11 MVP scope** — LEDES → threshold → worklist → inquiry → Decision Record. §11.1 workspace assessment reduced to one question. **Authority moved post-MVP** (human confirmation *is* the authority while every action is gated) |
