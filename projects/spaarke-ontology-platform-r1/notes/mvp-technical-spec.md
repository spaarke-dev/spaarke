# MVP Technical Specification — the Signal → Decision Loop

> *(Renamed 2026-10-01 from "Spend Governance Loop". The spend loop is one producer, not the subject — see the
> scope correction below.)*
>
> **Status**: Draft for review — **field-level detail current to 2026-10-01**. Concrete enough to act on; not yet
> task-decomposed. **`design.md` rev 4 holds the decisions; this document holds the evidence.** Where they
> disagree, `design.md` is newer.
> **Marking convention**: `[VERIFIED]` = read from source in this repo today · `[PROPOSED]` = new, needs review · `[MUST VERIFY]` = I could not confirm and it changes the design.
> **Companions**: [`mvp-synopsis.md`](mvp-synopsis.md) (scope) · [`ontology-component-model.md`](ontology-component-model.md) (definitions, **authoritative vocabulary** §3) · [`daily-briefing-ontology-fit.md`](daily-briefing-ontology-fit.md) (how the shipped Briefing's items enter the worklist)

> ## 🔴 Scope correction — 2026-10-01 (owner feedback round 2)
>
> Four changes reach this document. Sections affected are stamped inline; **nothing has been deleted**, so the
> superseded reasoning stays readable.
>
> **(1) R1 is intelligence-forward — no connector.** The project builds from the Spaarke data model out and
> assumes the data is present: Spaarke-owned, or mirrored by a later phase with the customer. **§7 (Connector
> contract) is therefore OUT of R1** and retained as the design the later phase inherits. The carve-out R1 does
> own is the **landing contract** — the pointer columns in §5 and `sourceasof` freshness — because it is free now
> and a data migration later.
>
> **(2) No LEDES intake.** Invoice / budget / spend-variance arrive from the e-billing platform as **computed
> metrics**, not raw LEDES files we parse. Trace step ① in §1 is restated accordingly.
>
> **(3) No per-entity fact or signal tables.** `ILiveFactResolver` is **already generic** — keyed
> `(subject-scheme, predicate)` with Matter / Invoice / Project implementations, plus `SubjectParser` and
> `SubjectSchemeCatalogOptions` — and CM-3 already says materialize facts as rollup + calculated columns, zero
> C#. So **`sprk_spendsnapshot` is one materialization, not the mechanism**, and `sprk_spendsignal` is replaced
> by generic `sprk_signal` with `SignalEvaluationService` as one producer among several. Tracked as **D-8** in
> `design.md` §8.0a; the §2.1 / §2.2 descriptions below remain accurate as *descriptions of what exists*.
>
> **(4) The Daily Briefing dissolves into the worklist.** Its overdue/upcoming work becomes a **Do lane** whose
> membership comes from declared `Temporal` policies rather than `DailyBriefingCollector`'s own queries; its news
> becomes narrative and Context; its tiles become filters; its LLM stops choosing priority. This adds signal
> **producers**, not mechanism. Analysis: [`daily-briefing-ontology-fit.md`](daily-briefing-ontology-fit.md);
> scheduled as BR-1..BR-6 in `design.md` §8.0b.
>
> **Also settled**: the worklist **reads signals and groups them by matter** (D-3, settled by the Console
> prototype), so `sprk_signal` needs a polymorphic subject **plus an always-populated matter lookup** derived
> from it. And **D-2 gains two constraints**: a *nullable decision reference* (BR-1 — a Do item resolved as one's
> own work writes no Decision Record) and **Decision Record → flag is 1:N** (prototype finding 4).

---

## 0. The differentiation test — read before scoping anything `[Added 2026-09-24]`

> **For every capability we scope, name the data it requires that the incumbent does not hold. If we cannot, it is not differentiated — however good the architecture underneath is.**

| Category | Differentiated? | Why |
|---|---|---|
| Single-source computation — budget variance, rate compliance, accruals, invoice line review | ❌ **No** | Legal Tracker, Onit, Brightflag already do this, often with AI. Budget **and** spend both live in e-billing |
| **Requires data the incumbent doesn't hold** — email, documents, requests, obligations | ✅ Yes | structurally impossible for them |
| **A rule about an object they don't model** | ✅ Yes | you cannot write a rule about a thing their schema lacks |
| **The record of what was decided and what resulted** | ✅ Yes | they *alert*; they do not capture the intervention or its **disposition** — and it **compounds** |

### 0.1 Consequence: budget variance is the plumbing proof, NOT the pitch

The spend loop in §1 exercises all seven pipeline stages at the lowest possible cost, which makes it the right *first* implementation. **It is not a differentiated demo, and must not be presented as one** — the customer's honest response is *"we can already do that."*

**The differentiated demo is the second rule, and it is cross-source:**

> *"Matter 4471 is 18% over budget — **and** outside counsel flagged scope creep in a thread three weeks ago that nobody acted on."*

That requires **spend + communication**. Legal Tracker has no email. And we already own the hard part: the 13-rung association ladder resolving email to matters is our strongest shipped asset.

Other candidates that pass the test: *obligation due in 14 days, contract in iManage, no owner* (document + obligation + matter) · *spend spike with an unanswered inquiry* (spend + originated) · *new request against an already-over-budget matter* (intake + spend).

### 0.2 Consequence: the signal entity must not be spend-shaped

`sprk_spendsignal` has a `sprk_snapshot` lookup — it is structurally spend-only, and a communication- or obligation-derived signal will not fit it. **The second signal type forces generalization anyway**, and it is far cheaper now, while the table has zero client references and no production data. See §2.5.

---

### 0.3 Consequence: a capability must TEST what its message CLAIMS `[added 2026-09-30]`

The generalized form of the §11.1 failure. §11.4's first predicate contained **no budget field** while its
message asserted *"unreconciled against its budget"*.

**Data being theoretically available is not the test — the predicate must actually read it.** A capability
that names data in its output but not in its `where` clause fails §0 regardless of what the platform holds,
and it independently fails [AP-12](../../../.claude/FAILURE-MODES.md) as runtime-generated prose stating
something the code never established.

Corollary: the **fourth** row of the §0 table — *the record of what was decided and what resulted* — is
unexercised by detection alone. It needs an action that changes something in the world, which is why the
Inquiry action is back in MVP scope (`design.md` §5, criterion 10).

---

## 1. The end-to-end trace, with real field names

This is the whole MVP. Everything in §§2–7 exists to make this run.

```
① Invoice / budget metrics are present in Spaarke            [OUT OF R1 — later phase]
     (computed metrics from the e-billing platform; NOT a
      LEDES file we parse. In R1 this data is SEEDED in dev —
      design.md §8.1. R1 owns only the landing contract: the
      pointer columns + sourceasof freshness.)
     → sprk_billingevent rows
       sprk_sourcesystem = "legaltracker"
       sprk_sourceid     = "INV-8832-L22"
       sprk_sourceasof   = 2026-03-12T04:00Z
       alt key (sprk_sourcesystem, sprk_sourceid) → UpsertMultiple

② Resolve to the matter                                     Resolution  [EXTEND]
     matter number "2024-00871" → sprk_matter GUID

③ Snapshot regenerates                                      SpendSnapshotGenerationJobHandler  [VERIFIED — exists]
     → sprk_spendsnapshot
       sprk_invoicedamount = 236,400
       sprk_budgetamount   = 200,000
       sprk_velocitypct    = 12.4
       sprk_periodtype     = 100000003 (ToDate)

④ Rules evaluate                                            SignalEvaluationService  [VERIFIED — exists]
     BudgetExceededRule → 236400/200000 = 1.182 ≥ 1.0 → FIRES

⑤ Signal upserted                                           [VERIFIED — exists; NEEDS 3 FIELDS]
     → sprk_spendsignal
       sprk_matter      = {matter}
       sprk_snapshot    = {snapshot}      ← carries the triggering values
       sprk_signaltype  = 100000000 (BudgetExceeded)
       sprk_severity    = 100000002 (Critical)
       sprk_message     = "Spend 236,400 exceeds budget 200,000 (118%)"
       sprk_isactive    = true
       sprk_generatedat = 2026-03-12T04:05Z
       ────────── new ──────────
       sprk_policy        = {sprk_policy}         [PROPOSED]
       sprk_policyversion = {sprk_policyversion}  [PROPOSED]
       sprk_proposedaction= "send-budget-inquiry" [PROPOSED]

⑥ Worklist renders                                          [PROPOSED — new widget]
     query: sprk_spendsignal WHERE sprk_isactive = true
     row:   Matter 4471 · "118% of budget" · why: POL-BUDGET v3 · [Ask counsel]

⑦ User clicks → gate                                        ConfirmationPolicyEngine  [VERIFIED — exists]

⑧ Inquiry created + sent                                    sprk_servicerequest  [EXTEND]

⑨ Decision Record written at the gate                       [PROPOSED — new entity]

⑩ Reply arrives → classified → disposition → signal closed
```

**Four of ten steps already work.** Steps ③④⑤ and ⑦ are shipped.

---

## 2. What is verified to exist

`[VERIFIED]` — read from [`SignalEvaluationService.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Finance/SignalEvaluationService.cs) on 2026-09-24.

### 2.1 `sprk_spendsnapshot`

| Field | Notes |
|---|---|
| `sprk_matter` | lookup |
| `sprk_periodtype` | option set — `Month` = 100000000, `ToDate` = 100000003 |
| `sprk_periodkey` · `sprk_bucketkey` | period identity |
| `sprk_invoicedamount` | money |
| **`sprk_budgetamount`** | money — **budget already has a home** |
| `sprk_velocitypct` | decimal |

### 2.2 `sprk_spendsignal`

> 🔴 **The inventory below is INCOMPLETE — corrected by live re-verification 2026-10-02.** Two columns exist on
> this table that no section of this spec listed:
>
> | Column | Type | Why it matters |
> |---|---|---|
> | **`sprk_spendsignalstatus`** | choice — Active · Acknowledged · Resolved · **Auto Resolved** | **"Auto Resolved" is `ConditionCleared`.** §2.5.1 argues for a new `sprk_resolutiontype` on the premise that *"nothing anywhere closes a flag when the condition clears on its own"* — the **vocabulary** was already here |
> | **`sprk_resolutionnotes`** | multiline text | the dismissal/resolution reason field §2.5.1 proposes |
>
> **What this changes**: CM-9's resolution semantics are **a model to copy onto `sprk_signal`, not to invent**.
> **What it does not change**: the table holds **0 rows**, so nothing has ever exercised these columns and the
> *sweep* that would set `Auto Resolved` almost certainly does not exist. The gap is behaviour, not schema —
> which is a cheaper gap, and a different one from the one §2.5.1 describes.

| Field | Notes |
|---|---|
| `sprk_matter` | lookup |
| `sprk_snapshot` | lookup → snapshot. **Carries the triggering values** |
| `sprk_signaltype` | `BudgetExceeded` 100000000 · `BudgetWarning` 100000001 · `VelocitySpike` 100000002 |
| `sprk_severity` | `Info` 100000000 · `Warning` 100000001 · `Critical` 100000002 |
| `sprk_message` | text — human-readable reason |
| `sprk_isactive` | bool — **the worklist filter** |
| `sprk_generatedat` | datetime |

### 2.3 The evaluation contract

```csharp
// Strategy pattern, already in place
interface ISignalRule {
    bool Evaluate(SpendSnapshotData snapshot, FinanceOptions options, out Signal signal);
}
// Registered: BudgetExceededRule, BudgetWarningRule, VelocitySpikeRule
// "order matters for consistent evaluation"
```

`EvaluateAsync(matterId)` → query snapshot IDs → retrieve fields → evaluate each rule → **upsert** signal (idempotent, no duplicates) → telemetry.

**Thresholds today** come from `FinanceOptions` (appsettings): BudgetExceeded fixed at ≥100%; BudgetWarning default 80%; VelocitySpike default 50%.

### 2.4 The gap, precisely

> **`sprk_spendsignal` has zero client-side references.** Signals are computed, stored and marked active — and nothing in any UI reads them.

**And it is spend-shaped.** The `sprk_snapshot` lookup binds it to `sprk_spendsnapshot`. That is fine for one producer and wrong for three. Zero client references + no production data is exactly why replacing it is cheap **now** (§2.5).

### 2.5 `sprk_signal` — the generic replacement `[PROPOSED]`

**Signals are Insights Engine outputs, not Finance outputs.** `SignalEvaluationService` becomes *one producer among several* (spend · communication · obligation), all writing the same shape. This extends **CM-1**: not only Policy, but the claims policy produces, belong to Insights.

| Field | Type | Notes |
|---|---|---|
| `sprk_subjecttype` · `sprk_subjectid` | text + guid | **polymorphic subject** per ADR-024 — matter, communication, obligation, invoice line |
| `sprk_matter` → Matter | lookup | denormalized for subgrid + rollup |
| `sprk_signaltype` | option set | extensible; spend values carried over unchanged |
| `sprk_severity` | option set | `Info` · `Warning` · `Critical` — unchanged |
| `sprk_message` | text | human-readable reason |
| **`sprk_evidencerefs`** | memo (JSON) | **replaces `sprk_snapshot`** — array of typed refs: `[{ "kind":"snapshot","ref":"sprk_spendsnapshot:{guid}" }, { "kind":"communication","ref":"sprk_communication:{guid}" }]`. This is what lets one row carry **cross-source** evidence |
| `sprk_policy` · `sprk_policyversion` | lookup | **what authorized the assessment** |
| `sprk_producedby` | text | `spend` · `communication` · `obligation` — which producer |
| `sprk_proposedaction` | text | e.g. `send-budget-inquiry` |
| `sprk_isactive` | bool | **the worklist filter** |
| `sprk_generatedat` | datetime | |

**Relationship to `InsightArtifact`**: `sprk_signal` is the *persisted, queryable* form of a policy-produced claim. `InsightArtifact` remains the wire/index form. They carry the same semantics — subject · predicate · value · evidence · producedBy — and reconciling them fully is post-MVP, but the field names are chosen to align so the later merge is mechanical rather than a migration.

**Migration**: `sprk_spendsignal` has no client references and no production data. Replace it rather than extend it.

---

### 2.5.1 Resolution semantics — not optional `[added 2026-09-30, CM-9]`

`sprk_spendsignal`'s idempotent upsert works today **only because its key is implicitly
(matter, signaltype)**. That implicit key does not survive a polymorphic subject, so the generic entity must
state it. And nothing anywhere closes a flag when the condition **clears on its own** — the budget is
revised, the communication is later dismissed, the matter closes.

Two consequences, both load-bearing:

- **Stale rows accumulate and the worklist rots.** This is how internal alerting surfaces die.
- **Suppression miscounts.** §12.2 suppresses after three dismissals. If an auto-cleared flag is
  indistinguishable from a human dismissal, that count is wrong in **both** directions.

| Field | Type | Purpose |
|---|---|---|
| `sprk_dedupekey` | text, **alternate key** | `signaltype + subjecttype + subjectid + periodkey`. The idempotency contract, explicit rather than implied |
| `sprk_resolutiontype` | option set | `ConditionCleared` · `Acted` · `Dismissed` · `Superseded` · `PolicyRetired`. What suppression counts and what the scorecard reads |
| `sprk_lastevaluated` | datetime | Staleness. Distinguishes *"still true"* from *"not re-evaluated"* |

Plus a **resolution sweep** in the same evaluation pass: a flag whose predicate no longer holds is closed
with `ConditionCleared`, never left open. Scope the sweep by changed-subject sets with a bounded full pass,
or it will not scale.

> **Decide this WITH the `sprk_signal` shape, not after it.** Taken separately, the second decision is a
> migration.

---

## 3. Policy — schema and rule body `[PROPOSED]`

### 3.1 `sprk_policy` — stable identity

| Field | Type | Notes |
|---|---|---|
| `sprk_code` | text, unique | `POL-BUDGET`. **What a decision cites** |
| `sprk_name` | text | "Budget Variance" |
| `sprk_category` | option set | Spend · Intake · Communication · Obligation |
| `sprk_ownerid` | lookup user/team | who owns the rule |
| `sprk_statecode` | state | Active / Inactive |

### 3.2 `sprk_policyversion` — immutable

| Field | Type | Notes |
|---|---|---|
| `sprk_policy` | lookup | parent |
| `sprk_versionnumber` | int | 1, 2, 3 — **never reused** |
| `sprk_effectivefrom` / `sprk_effectiveto` | datetime | `effectiveto` null = current |
| `sprk_ruletype` | option set | **MVP: `Threshold`, `Switch`, `Existence`** — `Existence` added 2026-09-30 (CM-7). Without it the §11.4 predicate is an EXISTS over a related entity that fits **no** allowed body, and §3.5 refuses to save an invalid body, so **the differentiated capability was literally unsavable.** `Transition` and `Trend` stay out; `Trend` needs baselines and therefore history |
| `sprk_rulebody` | memo (JSON) | validated per rule type |
| `sprk_authoredby` · `sprk_approvedby` · `sprk_approvedon` | lookup/datetime | provenance |

**Immutability is enforced by security role**: no Update, no Delete on `sprk_policyversion` for any role. A change creates a new version and sets `sprk_effectiveto` on the prior one — the only permitted update, performed by a plugin or the service, not a user.

### 3.3 Rule body — `Threshold`

```jsonc
{
  "type": "Threshold",
  "subject": "sprk_spendsnapshot",
  "field":   "computed:budgetUtilization",   // from the predicate registry, §4
  "op":      ">=",
  "value":   1.0,
  "when": {                                   // optional scoping predicate
    "sprk_periodtype": 100000003              // ToDate only
  },
  "then": {
    "signalType": 100000000,                  // BudgetExceeded
    "severity":   100000002,                  // Critical
    "messageTemplate": "Spend {invoicedamount:C0} exceeds budget {budgetamount:C0} ({budgetUtilization:P0})",
    "proposedAction": "send-budget-inquiry"
  }
}
```

### 3.4 Rule body — `Switch`

```jsonc
{
  "type": "Switch",
  "subject": "tenant",
  "enabled": false,
  "then": { "suppressSignalTypes": [100000002] }   // kill-switch for VelocitySpike
}
```

### 3.4a Rule body — `Existence` `[added 2026-09-30, CM-7]`

Asserts that a related object matching a bounded filter does, or does not, exist for the subject. Covers the
Correlation and Absence detection shapes in their EXISTS / NOT EXISTS forms. Implemented as a FetchXML
`link-entity` filter (outer link + null test for `notExists`), so **CM-3 holds unchanged** — the body is
still a Dataverse filter.

```jsonc
{
  "type": "Existence",
  "subject": "sprk_matter",
  "when": { },                                   // scope filter; empty = all subjects
  "all": [                                       // every clause must hold; no cross-clause references
    {
      "exists": "sprk_communication",
      "path":   "sprk_regardingmatter",
      "filter": {
        "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d",
                                "8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],
        "sprk_receiveddate":  { ">=": "now-30d" },
        "sprk_reviewoutcome": { "<>": 100000003 }
      }
    },
    {
      "notExists": "sprk_budgetrevision",
      "path":   "sprk_matter",
      "filter": { "sprk_revisedon": { ">=": "now-30d" } }
    }
  ],
  "then": { }                                    // as §3.3
}
```

**🔴 NO cross-clause variable passing.** An earlier proposal bound a value in clause 1
(`"bind": "commitment"`) and dereferenced it in clause 2 (`"$commitment.sprk_receiveddate"`). That is **not
one Dataverse filter** — it needs two queries plus a correlation step, which breaks CM-3 — and it
reintroduces the very property that made predicates preferable to node graphs. The case for predicates was
that the four hard parts of graphs (sequencing, state, **variable passing**, error handling) are absent. A
bound reference puts one back.

Clauses are therefore **independent**, each over a fixed window relative to *now*. The cost is precision: a
budget revision could land early in the window, before the commitment, and still satisfy `notExists`. That
imprecision is accepted for v1; a correlated variant is a later change **if** evidence shows it matters.

**Filter values are ids, never labels.** `sprk_triagecategory` is named by GUID so a category rename does
not silently change which rules match.

### 3.5 Schema validation

Each `sprk_ruletype` has a JSON Schema shipped in the solution. `sprk_rulebody` is validated on save (plugin or service). **An invalid body cannot be saved** — this is what keeps the closed-rule-type guard enforceable rather than aspirational.

---

## 4. Fact predicates — the registry `[PROPOSED]`

A policy may only reference fields that exist. Three kinds, and the `computed:` prefix distinguishes the second:

| Predicate | Formula | Inputs | Type | Implementation |
|---|---|---|---|---|
| `computed:budgetUtilization` | `invoicedamount / budgetamount` | snapshot | decimal | **Dataverse calculated column** — zero code |
| `computed:budgetVariance` | `budgetUtilization − 1` | snapshot | decimal | calculated column |
| `computed:velocityPct` | existing | snapshot | decimal | **[VERIFIED] already exists** |
| `computed:invoicedToDate` | `SUM(sprk_billingevent.amount)` | billingevent | money | **Dataverse rollup** — zero code |
| `field:sprk_periodtype` | — | snapshot | optionset | direct |

**Division-by-zero**: `budgetUtilization` returns null when `budgetamount` is null or 0. A `Threshold` policy evaluating a null input **does not fire** and logs a `MissingInput` event to `sprk_externalevent`. It must never fire as if zero. `[PROPOSED — decide and test explicitly]`

**Registry** = a shipped Dataverse table `sprk_factpredicate` (code, display name, subject entity, return type, implementation kind) so the policy authoring form can offer a picklist rather than free text. This is the MVP form of D-u.

---

## 5. Canonical mirrored fields `[PROPOSED]`

> **`[MUST VERIFY]` — I have not read `sprk_matter`'s current schema.** The list below is what the MVP *requires*; overlap with existing columns must be checked before any are created.

**The rule that generated this list**: each field is here because a shipped predicate, policy, worklist column or action needs it. Nothing is here because the source system exposes it.

### 5.1 `sprk_matter` — additions

| Field | Class | Required by |
|---|---|---|
| `sprk_sourcesystem` · `sprk_sourceid` · `sprk_sourceetag` · `sprk_sourceasof` | **pointer** | landing contract, upsert alt key, freshness |
| `sprk_clientmatternumber` | projected | **resolution natural key** |
| `sprk_matterstatus` | projected | worklist filter |
| `sprk_client` → Organization | projected · **edge** | spend rollup, conflicts |
| `sprk_responsibleattorney` → Contact | projected · **edge** | inquiry addressee, routing |
| `sprk_practicearea` | projected — **text, not a lookup** | policy `when` scoping. Fails the edge test |
| `sprk_matterphase` | projected | **the MVP policy `when` clause** |
| `sprk_openeddate` · `sprk_closeddate` | projected | staleness predicates |
| `sprk_outsidefirm` → Organization | projected · **edge** | **the Engagement edge** — inquiry addressee, performance accumulation |

**Alt key**: `(sprk_sourcesystem, sprk_sourceid)` — required for `UpsertMultiple`.

### 5.2 `sprk_billingevent` (invoice line) — additions

| Field | Class | Required by |
|---|---|---|
| pointer fields + alt key | **pointer** | landing |
| `sprk_matter` → Matter | projected · **edge** | rollup to snapshot |
| `sprk_timekeeper` → Contact | projected · **edge** | rate/performance predicates |
| `sprk_amount` · `sprk_hours` · `sprk_rate` | projected | **`invoicedToDate` rollup** |
| `sprk_servicedate` | projected | period bucketing |
| `sprk_utbmstaskcode` · `sprk_utbmsactivitycode` | projected | OCG policies (post-MVP, cheap now) |
| `sprk_narrative` | projected | future extraction input |
| `sprk_adjustmentamount` | projected | approved-vs-submitted variance |

**Deliberately excluded** (projected-set discipline, §1.4.4): originating office, billing partner, rate exceptions, WIP, AR aging, trust accounting, task-code sets. **None is required by a shipped predicate or policy.**

---

## 6. Decision Record `[PROPOSED]`

`sprk_decisionrecord` — append-only, **written at the gate**, one row per governed decision.

| Field | Type | Notes |
|---|---|---|
| `sprk_subjecttype` · `sprk_subjectid` | text + guid | **object-keyed** — polymorphic regarding per ADR-024 |
| `sprk_matter` → Matter | lookup | denormalized for the subgrid + rollups |
| `sprk_proposedaction` | text | `send-budget-inquiry` |
| `sprk_triggersignal` → spendsignal | lookup | what fired |
| `sprk_policy` · `sprk_policyversion` | lookup | **what authorized the proposal** |
| `sprk_factsnapshot` | memo (JSON) | **the input values at decision time**, frozen. 🔴 **MANDATORY, not one row among many** (2026-09-30). Ruling out bitemporality (component model §10 ADR-3) on the grounds that immutable policy versions buy ~80% of the defensibility is a fair trade — but the residual gap is that you can reconstruct *which rule was in force* and **not** *what the data said*. This field is the only thing that closes it |
| `sprk_gateoutcome` | option set | Approved · Rejected · TimedOut · AutoApproved |
| `sprk_actorid` → systemuser | lookup | **key on `systemuserid` so agents share the model later** |
| `sprk_decidedon` | datetime | |
| `sprk_resultref` | text | `sprk_servicerequest:{guid}` — **nullable, deliberately.** A Decision Record and an action record are **different objects**: a deny-path record has no action, and an action taken outside a signal has no decision. The MVP choice (persist the value that already exists at the gate) is right, but the field list must not foreclose the nullable reference |
| `sprk_evidencerefs` | memo (JSON) | array of typed refs |
| `sprk_hash` | text | SHA-256 over the canonical serialization, chained to prior entry per ADR-015 |

**Append-only enforcement**: security role grants Create + Read only. No role, including System Administrator in the app, gets Update or Delete.

**The write point** is `ConfirmationPolicyEngine` / `GateDecisionV2` — **one entry per gate decision, including rejections and timeouts.** A rejected gate writes no Dataverse row anywhere else, which is precisely why audit cannot substitute.

---

## 7. Connector contract `[PROPOSED]` — 🔴 **OUT OF R1 SCOPE (2026-10-01)**

> **Retained, not deleted.** Owner decision 2026-10-01: R1 is intelligence-forward and does not build the
> connector (`design.md` §1.0 / §5 Out). This section is the design the **later mirroring phase inherits**, so
> deleting it would mean re-deriving it with the customer in the room.
>
> **What R1 does keep from this section**: nothing in the interface or the pipeline below, and **only the landing
> contract** — the pointer columns (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`) and attribute
> ownership in §5. Those are in R1 because retrofitting them is a data migration, and because **freshness is
> load-bearing in the UI**: an absence clause (`notExists …`) evaluated over a stale mirror is a false negative
> wearing a confident face (Console prototype finding 3).
>
> ⚠️ The **"MVP adapters"** line at the end of this section is void for R1 — there is no `FileExportConnector`
> and no `RestConnector` in this project.

```csharp
public interface ISourceConnector
{
    string SourceSystemKey { get; }              // "legaltracker"

    IAsyncEnumerable<SourceRecord> EnumerateAsync(
        string objectClass, CancellationToken ct);

    IAsyncEnumerable<SourceRecord> ChangedSinceAsync(
        string objectClass, DateTimeOffset watermark, CancellationToken ct);

    Task<SourceRecord?> FetchAsync(
        string objectClass, string sourceId, CancellationToken ct);
}

public sealed record SourceRecord(
    string SourceId,
    string? SourceETag,
    DateTimeOffset SourceAsOf,
    IReadOnlyDictionary<string, object?> Fields);   // raw source field names
```

**Landing pipeline** — owned by us regardless of transport:

1. Connector yields `SourceRecord`s
2. **Raw payload persisted to Cosmos**, keyed by `(runId, sourceSystem, sourceId)` — buys replay
3. Mapping applied (`sprk_externalfieldmapping` JSON DSL) → canonical field names
4. Resolution: lookups resolved via the rung ladder; unresolved → human queue, row still lands
5. `UpsertMultiple` on the alt key, **chunked at 200** — standard tables roll the whole batch back on any error
6. Per-row failures → `sprk_externalevent`
7. Watermark advanced only on full success

**MVP adapters:** `FileExportConnector` (CSV/Excel with declarable column mapping — works against *any* platform's export) and one `RestConnector` for the first customer's platform.

---

## 8. Exact change list

| # | Change | Kind |
|---|---|---|
| 1 | `sprk_policy` + `sprk_policyversion` entities + JSON Schemas + MDA forms | new |
| 2 | `sprk_factpredicate` registry table + seed rows | new |
| 3 | **`sprk_signal`** generic entity replacing `sprk_spendsignal` (§2.5) — polymorphic subject + `sprk_evidencerefs` JSON + policy refs | **replace, not extend** — zero client refs, no prod data |
| 4 | `SignalEvaluationService`: `ISignalRule` implementations read policy rows instead of `FinanceOptions`; stamp policy refs; write to `sprk_signal` | **modify, don't move** — it becomes *one producer* |
| 4b | **Second producer: communication-derived rule** — the cross-source demo (§0.1). Consumes the existing association ladder | **this is the differentiated capability** |
| 5 | `sprk_matter` + `sprk_billingevent`: pointer fields, alt keys, §5 projected fields | schema |
| 6 | `budgetUtilization` calculated column; `invoicedToDate` rollup | **zero code** |
| ~~7~~ | ~~`ISourceConnector` + landing pipeline + `FileExportConnector`~~ | 🔴 **OUT OF R1 (2026-10-01)** — §7. Rev 3 called this *"the bulk of the work"*; removing it is why R1 now concentrates on the loop. **Replaced by 7a** |
| **7a** | **Landing contract only** — pointer columns (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`) + attribute ownership on any entity a later ingest touches, and `sourceasof` **surfaced in the UI as a gap when stale** | schema + a render rule. In R1 because it is free now and a migration later |
| 8 | Worklist: **one row component** over `sprk_signal` where unresolved, **grouped by matter** (D-3). ⚠️ Not `sprk_spendsignal`, and ⚠️ **not "zero new UI code"** — prototype finding 9 disproved that; finding 10 is the recovered half (one component carried all three signal shapes, so the cost is build-once) | new |
| 9 | `sprk_decisionrecord` entity + gate write + subgrid + view. ⚠️ **1:N to flags** (prototype finding 4) and the flag's decision reference is **nullable** (BR-1) | new |
| 10 | `sprk_servicerequest`: outbound direction + **disposition** (CM-4 resolved 2026-10-02 — reuses ADR-039's existing vocabulary rather than adding a term) | extend |
| **11** | **Do-lane `Temporal` policy rows** — overdue task · due within 3 days · work assignment past `sprk_responseduedate` (BR-2). Membership moves off `DailyBriefingCollector`'s own queries onto declared rules | 3 data rows, **no new evaluator** |
| **12** | **Retire *Critical Today* as a list** — `sprk_highpriority` → rank input, `sprk_monitor` → subscription (BR-3); **remove the LLM-chosen "Top action"** (it breaks row-contract requirement 2 and decision 15) | configuration + deletion |
| **13** | **First Know-promotion rule** — *new matter with no budget after 5 days* (`Absence`, Spaarke-held data) (BR-6). ⚠️ Document-based Absence rules wait until documents are mirrored — impossible over reference-mode data | one data row |

---

## 9. Open items that change the design

| # | Item | Why it matters |
|---|---|---|
| 1 | **`sprk_matter` current schema** — read before adding anything in §5.1 | several fields may already exist under different names |
| ~~2~~ | ~~**`sdkmessagefilters`** — is `UpsertMultiple` supported?~~ | ✅ **Moot for R1** — `UpsertMultiple` was the *ingest* mechanism, and there is no ingest in R1 (§7, out of scope). Returns with the mirroring phase |
| ~~3~~ | ~~**What populates `sprk_spendsnapshot` today?**~~ | ✅ **CLOSED 2026-10-01 — no longer an R1 question.** Budget and spend are **seeded manually in dev** for development testing; R1 does not define their provenance (owner, synopsis §7 item 2). The question returns with the mirroring phase. Replaced by the §8.1 seeding checklist in `design.md`. ⚠️ Seeded data proves the predicate *evaluates*, never that it *fires on reality* |
| 4 | **Null-budget behaviour** (§4) | decide and test; silent wrong-firing is the failure mode |
| ~~5~~ | ~~**Which platform is first**~~ | ✅ **Moot for R1 — no connector** (§7). Still customer-determined when the mirroring phase starts |
| ~~6~~ | ~~**Does `sprk_servicerequest` have a direction/outcome model already?**~~ | ✅ **CLOSED 2026-10-02 — verified ABSENT, both halves.** Live `describe`: **no direction discriminator** and **no outcome/disposition option set**. CM-5's "absorbs Inquiry" therefore requires adding **both**, which is what §8 item 10 already proposes — the proposal is confirmed correct rather than redundant. ⓘ It *does* carry **13 polymorphic `sprk_regarding*` fields** incl. `todo` and `workassignment`, so the subject side of an Inquiry needs nothing |
| ~~7~~ | ~~**What is the second (cross-source) rule, precisely?**~~ | ✅ **ANSWERED — §11.4** (Path B, rewritten 2026-09-30): a communication classified fee/scope in the window **AND** no `sprk_budgetrevision` in that window. ⚠️ Note the example phrasing in the original cell (*"over budget AND … with no disposition"*) is **exactly the §0.3 failure** — it asserts a budget comparison the predicate never makes. Kept visible as the worked example of the trap |
| **9** | **How many signal producers at MVP, now that the Do lane is in?** `[new 2026-10-01]` | Three were assumed (spend · communication · memo); BR-2 adds three `Temporal` rules and BR-6 one `Absence` rule. **This strengthens the generic-`sprk_signal` case rather than complicating it** — but it changes "shaped by two producers" to "shaped by six", which is the right time to settle the shape (D-2 / D-7 / D-8) |
| 8 | **Run the §0 differentiation test retroactively** across §8 Wave 2 / Wave 3 modules in the strategy synopsis before any is specced | Several may fail it the same way budget variance did |

---

## 10. Verified against the live dev environment `[2026-09-29]`

Queried directly via Dataverse MCP. These are facts, not inferences — earlier sections that
contradict them are wrong.

### 10.1 `sprk_triagecategory` — the taxonomy is data, not code

> ⓘ **Column list and row count below are superseded by §10.7** (live re-verification 2026-10-02): there are
> **10** rows, not nine — `Unclassified` was omitted — and `sprk_classifierguidance` now exists and is populated
> on all ten. Both changes were made *by* this project in §14/§16.4; §10.1 simply predates them.

The table has **only** `sprk_name`, `sprk_enabled`, `sprk_priorityweight`. **No description column.**
Nine rows after this session's additions:

| Weight | Name | Added |
|---|---|---|
| 10 | Marketing / Noise | seeded |
| 30 | Administrative | seeded |
| 50 | Scheduling | seeded |
| 60 | Invoice / Billing | seeded — *reactive: a bill arrived* |
| 70 | Opposing counsel | seeded |
| **75** | **Fee / rate change** | **2026-09-29** — `8b62dd84-1fbc-f111-aaaf-3833c5e9614d` |
| 80 | Client instruction | seeded |
| **85** | **Scope / budget change** | **2026-09-29** — `8d62dd84-1fbc-f111-aaaf-3833c5e9614d` |
| 100 | Court / Filing | seeded |

Two rows, not four. The category is a **single-select**: an email proposing extra depositions is
simultaneously a scope change *and* a budget request, so splitting those would scatter one signal
across two categories and weaken the rule's bounded set. The new rows are *leading* (a commitment
about future cost); `Invoice / Billing` stays *reactive* (a bill arrived).

> **`sprk_enabled` defaults to FALSE on create.** Both new rows were created disabled with a null
> weight and had to be corrected. Any future taxonomy row MUST set `sprk_enabled = true` and a
> weight, or it is invisible to the classifier.

### 10.2 The rows live in the ENVIRONMENT, never in the repo

`ActionRunner.ResolveLookupChoicesAsync` queries the taxonomy **per run** and injects the names two ways:

1. into the rendered **prompt**, and
2. as a JSON-Schema **`enum`** on the output property, so Azure OpenAI structured-output decoding
   cannot emit a non-existent value.

A new row is live on the next enrichment — no build, no deploy, no repo change. **This is the
"declared" test passing on the most load-bearing part of the design.**

Residual risk: resolution is best-effort per NFR-04. A Dataverse read failure degrades **silently**
to the pre-fix behaviour (below), logged only at Warning. Needs a monitor.

### 10.3 The 100%-null incident — evidence the mechanism matters

Before 2026-09-04, `structuredOutput: true` meant output fields never rendered into the prompt, so
`$choices` had no path to the model; emitted labels matched no row and
`CommunicationEnrichmentService` left the field unset ("category null on 100% of captures").

Live data confirms the fix: of **270** communications, **28** carry a triage priority but only **15**
carry a category — and **all 13** gap cases predate the fix, newest `2026-09-03T21:20`.

### 10.4 `sprk_communicationrule` — the reach-out path was dark

**Zero rows existed.** The gate fail-closes to `no-matching-rule` then DENY, so
`CommunicationRiActionService` had never been invoked. Created 2026-09-29:

```
sprk_communicationrule  dc423a8b-1fbc-f111-aaaf-3833c5e9614d
  sprk_name                "Default - all matters, 0.70 confidence"
  sprk_tenant              (blank)  -> matches all tenants
  sprk_matter              (empty)  -> matches all matters
  sprk_confidencethreshold 0.70
  sprk_enabled             true     sprk_priority 500     sprk_flagprivilege false
```

**0.70 is a guess, and that is the point.** Nothing today can say whether it is right. Once Decision
Records exist, the confidence distribution of *confirmed* vs *dismissed* sets it from evidence. This
is the clearest single instance of the record feeding back into the process (section 12.2).

### 10.5 What "the system reaching out" actually is — ALL SHIPPED

Four writes, composed from existing seams by `CommunicationRiActionService`. **Nothing here is
proposed; only the rule row was missing.**

| # | Write | Seam |
|---|---|---|
| 1 | `sprk_event` (event type = task), owner = `sprk_communication.ownerid` | `IActionSeam.CreateTaskAsync` |
| 2 | Outbox row `kind=communication-assessed`, written FIRST | `OutboxService` |
| 3 | SignalR ping, best-effort, AFTER the outbox | `SignalRDeliveryService` |
| 4 | App-notification | `NotificationService` |

Recipient is the communication's owner; matter-team fan-out is a documented future enhancement.

### 10.6 Live end-to-end test — 2026-09-29

One real email sent through the capture path 20 minutes after the two taxonomy rows were created.
Communication `3449bd41-24bc-f111-aaaf-3833c5e9614d`, subject
*"EMPL-307998 Intellectual Asset Management Lifecycle Optimization"*, body proposing two additional
depositions at roughly $40-45k beyond budget, one PDF attachment.

**What worked — the whole classification chain, including the brand-new row:**

| Stage | Result |
|---|---|
| Association | **Auto-filed, `Resolved`.** `sprk_regardingmatter = 9f6f23e4-…`. Deterministic 0.90 / reinforced 0.997 vs auto-file threshold 0.85. Five rungs fired: ExplicitReference (`EMPL-307998` reverse lookup), RecordNameMatch (0.97), ParticipantCorrelation, DocumentAssociation, AiClassification |
| Attachment | **Captured** — `sprk_attachmentcount = 1`; `Invoice-10044725.pdf` drove the DocumentAssociation rung, surfacing three additional candidate matters and an invoice, all correctly left unwritten |
| Rung-5 classification | freeform `category = budget-discussion`, `urgency = elevated`, `obligations = [budget-approval]`, `actions = [confirm-budget-increase, link-to-matter]` |
| Triage mapping | **`budget-discussion` → "Scope / budget change"** — the row created 20 minutes earlier, live with **zero deployment**. §10.2 proven end to end |
| Triage output | priority **High**, reviewOutcome **Update**, summary: *"The email requests confirmation of a budget increase of approximately $40-45k for two additional depositions. It requires approval and linkage to the relevant matter."* |

**What did NOT fire, and why it matters more than what did:** `sprk_riconfidence = 0.68` against the
rule's `0.70` threshold → gate **DENIED** (`rule-matched-confidence-below-threshold`). No `sprk_event`,
no ping, no notification. Verified: zero `sprk_event` rows created after `2026-09-29T12:00`.

**A high-priority, correctly-classified, correctly-filed, genuinely important email produced no
outreach — because a threshold was set by guess, two hundredths too high.** This is §12.2 item 2, in
production data, on the first attempt. Threshold lowered to **0.60** on `dc423a8b-…`; the rule is now
named *"Default - all matters, 0.60 confidence"*. 0.60 is still a guess — just a better-informed one.
Only Decision Records turn it into evidence.

> ⚠️ **Reported as two failures, and neither was one.** The owner observed "it did not match to related
> record or include the attachment". Both had in fact succeeded — the match auto-filed at 0.997 and the
> attachment was captured. Enrichment is asynchronous (received `12:38:15`, row created `12:38:56`), so a
> surface read too early, or not refreshed, shows neither. **A UI that cannot distinguish "not yet
> enriched" from "enrichment found nothing" will keep generating false defect reports** — worth a visible
> pending state on the reconciliation surfaces.

---


### 10.7 Live re-verification — 2026-10-02 `[authoritative row counts]`

Re-run against the **live dev environment** via Dataverse MCP, because `phase0-codebase-inventory.md` could not
reach it on 2026-09-19 and used repo docs instead. **Every figure below is LIVE-MCP.** This subsection supersedes
any schema claim elsewhere in the project notes, including §10.1's earlier column lists.

| Entity | Exists? | Rows | Note |
|---|---|---|---|
| `sprk_signal` | ❌ **ABSENT** | — | To be created |
| `sprk_policy` · `sprk_policyversion` | ❌ **ABSENT** | — | To be created |
| `sprk_budgetrevision` | ❌ **ABSENT** | — | To be created (CM-10 confirmed) |
| `sprk_spendsignal` | ✅ | **0** | Replace-not-extend is confirmed free. `sprk_snapshot` lookup is **NOT NULL** → structurally spend-only. ⚠️ Carries `sprk_spendsignalstatus` (incl. **Auto Resolved**) + `sprk_resolutionnotes` — see §2.2 |
| `sprk_spendsnapshot` | ✅ | **0** | `sprk_budgetamount` · `sprk_invoicedamount` · `sprk_velocitypct` all confirmed, plus `sprk_budgetvariance`/`pct` |
| `sprk_budget` | ✅ | **2** | `sprk_totalbudget` + status/period/category/year/dates. **No revision history of any kind** |
| `sprk_billingevent` | ✅ | **1** | `sprk_amount` · `sprk_costtype` (Fee/Expense) · `sprk_rate` · `sprk_timekeeper` · `sprk_vendororg` |
| `sprk_affinity` | ✅ | 0 | ⚠️ **Collision confirmed**: `sprk_signaltype` *and* `sprk_signalvalue` both exist here |
| `sprk_servicerequest` | ⚠️ **PARTIAL** | — | **No direction discriminator, no outcome/disposition option set** — both must be added (§9 item 6). Has **13 polymorphic `sprk_regarding*`** fields incl. `todo` + `workassignment` |
| `sprk_triagecategory` | ✅ | **10** | ⚠️ **Not 9** — §10.1 omitted `Unclassified` (weight 40). **All 10 enabled, all 10 with `sprk_classifierguidance` populated.** Weights: Court/Filing 100 · Scope/budget 85 · Client instruction 80 · Fee/rate 75 · Opposing counsel 70 · Invoice/Billing 60 · Scheduling 50 · Unclassified 40 · Administrative 30 · Marketing/Noise 10 |
| `sprk_communicationrule` | ✅ | **1** | `sprk_confidencethreshold` · `sprk_taskduedays` · `sprk_taskfinalduedays` · scope + priority |
| `sprk_gridconfiguration` | ✅ | **22** | Reuse inventory — incl. *My Tasks (Assistant)*, *Communication Reconciliation – Needs Review*, *Event Default – Calendar Widget*, *Invoice Matter Budget Performance* |

**The Do lane has real data to fire on** — which matters, because BR-2's rules were specced before anyone counted:

| Entity | Rows | Due-date column | Status mechanism |
|---|---|---|---|
| `sprk_event` | **73** — Draft **48** · Open **17** · Completed 7 · Cancelled 1 | — | `statuscode`: Draft 1 · Open **659490001** · Completed 659490002 · …; type via `sprk_eventtype_ref` |
| `sprk_todo` | **50** | `sprk_duedate` | `statuscode`: Open 1 · Completed 2 · In Progress 659490001 · Dismissed 659490002 |
| `sprk_workassignment` | **22** | **`sprk_responseduedate` confirmed** | `sprk_priority`, plus `sprk_monitor` |

⚠️ **[ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050) re-measured: 48 Draft rows, not 49.** Still
the blocker it was — 48 of 73 `sprk_event` rows are invisible to any Open-filtered query, so a Do-lane overdue
rule shipped today would under-report by roughly two thirds.

---

## 11. The cross-source predicate `[PROPOSED]`

### 11.1 Why the first attempt was wrong

The rule first drafted here was *"over budget AND at least one pending-review email in 30 days."*
**That is two unrelated facts sharing a matter id.** An over-budget matter has many emails; none need
have any bearing on the money. The conjunction established correlation by coincidence.

It was also **lagging**: "over budget" means the money is already spent, and every e-billing vendor
reports it.

**And the second attempt was wrong too — worse, and self-inflicted** `[2026-09-30]`. The v1 predicate below
(as first written) tested **only** the communication side: it fired on any matter carrying a recent,
non-dismissed, scope-or-fee-classified email. **Nothing in its `where` clause read spend, budget or a
snapshot.** Two failures at once:

1. **Single-source, so it failed §0** — the §0.1 claim is that the capability requires *spend + communication*;
   the predicate required communication only. What would have shipped is *"we classified an email."*
2. **The message asserted a conclusion the computation never reached** — *"unreconciled against its budget"*
   when no budget was consulted. §0.3, committed on the headline claim, in the same section that says
   *"the join is the differentiator, not the arithmetic."* **The join was what got dropped, not the
   arithmetic.**

§11.4 below is the corrected form.

### 11.2 The signal that works — leading, not lagging

**The aspiration** — what a user should understand the signal to mean:

> A commitment with financial consequence was made in correspondence, and the budget does not reflect it yet.

**What the predicate actually tests** (Path B, CM-6) — and therefore what the message may say:

> A communication on this matter was classified as a **fee or scope change** within the window, **and** no
> **budget revision** was recorded in that same window.

Two conjuncts, two sources. The snapshot is attached as **evidence** so a human sees the budget position, but
the rule asserts no comparison it did not make.

Worked case: counsel emails *"given the new claims we need two more depositions, roughly $40-45k
beyond budget."* The e-billing system shows the matter at $180k of $250k — on budget, silent, and it
stays silent for 30-60 days until the invoice lands.

**Differentiation test (section 0)**: the commitment is in the mail; the budget is in e-billing. Only
a system holding both can compare them. Legal Tracker cannot produce this at any price because it
never sees the email.

### 11.3 Every join already exists

```
sprk_communication.sprk_regardingmatter  -> sprk_matter   (populated by association rungs 0-3)
sprk_communication.sprk_triagecategory   -> sprk_triagecategory row
sprk_spendsnapshot.sprk_matter           -> sprk_matter
sprk_spendsnapshot.sprk_budgetamount / .sprk_invoicedamount
```

🔴 **Corrected 2026-09-30 — one join does NOT exist.** Path B's second conjunct needs *"no budget revision
since"*, which requires budget **with a change date**. Verified against the live environment:
`sprk_budget` carries `sprk_totalbudget`, `modifiedon` and `versionnumber` — **no revision rows, and no
`sprk_budgetrevision` entity exists.** Dataverse auditing is not a fallback either: component model §6 found
**zero** `RetrieveRecordChangeHistory` usage and no audit policy.

So the smallest possible `sprk_budgetrevision` (matter · prior amount · new amount · `sprk_revisedon` ·
reason · author) is **in MVP scope** (CM-10) and is D-1's fourth spike question.

> **`sprk_budget.modifiedon` is NOT an acceptable substitute.** It is tempting — a change date already
> exists, zero new schema, testable today. **Reject it**: any unrelated field edit bumps `modifiedon`, so a
> stray edit reads as *"the budget was revised"* and **suppresses a true signal**. That is a false
> **negative**, the exact direction the recall-over-precision decision rules against. Recorded so nobody
> rediscovers it as a cost saving.

Otherwise the graph is connected; what is missing is a rule that walks it.

### 11.4 The predicate — corrected `[rewritten 2026-09-30]`

Bounded set = the literal rows `{Fee / rate change, Scope / budget change}` (§10.1), named **by id** in the
rule body. Not a prompt instruction, not a heuristic — stored ids, editable without a deploy.

Expressed as an `Existence` body (§3.4a). Two independent clauses, no cross-clause references:

```
type:    Existence
subject: sprk_matter
all:
  - exists    sprk_communication   via sprk_regardingmatter
      sprk_triagecategory IN {8b62dd84-..., 8d62dd84-...}
      sprk_receiveddate   >= now - 30d
      sprk_reviewoutcome  <> 100000003                     -- not Dismiss
  - notExists sprk_budgetrevision  via sprk_matter
      sprk_revisedon      >= now - 30d
then:
  message   "A commitment with financial consequence was raised in the last 30 days and the budget has
             not been revised in that period."
  evidence  the triggering sprk_communication  +  latest sprk_spendsnapshot for the matter
  action    send-budget-inquiry
```

**What changed from the first draft, and why each matters:**

| Change | Why |
|---|---|
| Added the `notExists sprk_budgetrevision` clause | Without it the predicate is **single-source** and fails §0. This is the entire differentiation |
| The message states only what the predicate tests | The first version said *"unreconciled against its budget"* having read no budget — §0.3 |
| The snapshot moved from implied condition to explicit **evidence** | The user sees the budget position; the rule does not claim a comparison it never computed |
| Clauses are independent, both windowed on *now* | Keeps the body a single Dataverse filter (CM-3). A bound cross-reference would reintroduce variable passing — see §3.4a |

**Still deliberately unquantified.** Extracting "$45k" from prose is new extraction work that is sometimes
wrong; the human judges the amount. The claim holds anyway, because **the join is the differentiator, not the
arithmetic** — and this time the predicate actually contains the join.

**The weakest link is the classifier, not the logic.** The predicate is a conjunction, so it inherits its
weakest input: whether `Scope / budget change` is *detected* at all. Nothing has measured that. At 70% recall
the differentiated claim silently misses 30% of real cases while every other success criterion still passes
— which is why `design.md` criterion 11 makes a recall floor a gate, and §16.5's first eval case is the
start of the corpus.

### 11.5 Two placements = two different products

| | Subject | Surface | Value |
|---|---|---|---|
| **(a)** | communication | A column/badge in the four shipped reconciliation grids, via the existing `DataGridOverrides.columnRenderers` seam | "why this email matters" — *on-ramp; proves the mechanism inside a surface users already open* |
| **(b)** | matter | A new `sprk_gridconfiguration` worklist | "why this matter needs attention" — *the differentiated one* |

Today triage says *what kind* of email this is. It never says *this email conflicts with something
else you know*. **(a) closes that gap cheaply; the data and the render seam already ship.**

---

## 12. Decision Record — corrected `[PROPOSED]`

### 12.1 It already exists as a value and is thrown away

`CommunicationRuleGate.EvaluateAsync` returns `CommunicationRuleDecision` — authorize, matched rule
id, confidence, threshold, privilege flag, reason — **on both the authorize and deny paths**,
fail-closed, with deny as a value rather than an exception. Then `LogDecision` writes it to
`ILogger` and it is gone.

**The Decision Record is not a new concept to design. It is this value, given a table.**

### 12.2 How it loops back to the process

Strongest first:

1. **Suppression.** Three dismissals of the same signal on the same matter, then stop raising it. A
   functional requirement, impossible without reading prior decisions. It is the difference between
   a signal system people keep on and one they mute.
2. **Threshold tuning.** Sets `sprk_confidencethreshold` (section 10.4) from the confirmed/dismissed
   confidence distribution instead of by guess.
3. **Rule scorecard.** Per policy: fired N, confirmed X, dismissed Y, never-acted Z — a Dataverse
   rollup, zero C#. A rule at 90% dismissal is visibly noise. **This is what makes "configurable
   platform" true rather than a brochure claim.**
4. **Dismiss reasons become new rules.** "Already approved offline" forty times is a missing *fact*
   (approval), not a bad rule.

### 12.3 One general table, typed payload

The decision *facts* are identical for every producer — subject, rule + version, facts-as-of,
proposed action, decider, decision, reason. Only the **evidence** differs, so that goes in a JSON
payload column.

Rejected: specialized per-producer tables. *"Show me everything decided about this matter"* is the
ontology's reason to exist, and specialized tables make it a union of N queries that grows with every
producer. Suppression and scorecards likewise only work against one place to look.

### 12.4 Blast radius is one line — NOT the gate

`RuleGatedAssessedConsumer.PublishAsync` **already holds** the `decision` object and merely branches
on it. Persisting it is a line **before** `if (decision.Authorize)`. `CommunicationRuleGate` is not
touched, its logic does not change, and the fail-closed behaviour is preserved.

`sprk_emailreviewlog` stays as-is and becomes a **producer into** the general record — not a migration.

---

## 13. Signal sources `[PROPOSED]`

| Source | State | MVP |
|---|---|---|
| **Email** | Ready — classify, triage, bounded category, persisted | yes |
| **`sprk_memo`** | **No classification at all.** Polymorphic across 15 parents (matter, budget, invoice, communication, document, agreement, contact, project, service request, timekeeper, work assignment, event, organization, analysis, report card); `sprk_memobody` + `sprk_searchprofile` | **yes — in MVP** |
| **Documents** | Extraction yes, **bounded classification no** — `agreement-classify` uses a bespoke C# assembler (`AgreementTypeRegistryPromptAssembler`), not `$choices` | yes |
| Messages (Teams/ACS) | Channel modelled (`sprk_acsmessageid`/`sprk_acsthreadid`); triage coverage unverified | later |
| `sprk_event` (calendar) | Commitments with dates | later |
| `sprk_servicerequest` | Inbound requests (Front Door) | later |
| `sprk_externalevent` | Spaarke Connect change feed from bound systems | later |
| Document revisions | A redline is a change in commitment, not just a file | later |

**`sprk_memo` is the strongest second source**: human-authored (so accuracy is high), and it covers
the case where the commitment was made on a phone call rather than in writing — *"client agreed to
expand scope on today's call"* is a leading indicator no email will ever contain.

**The extensibility mechanism already exists**: ADR-024 polymorphic `sprk_regarding*` +
`sprk_recordtype_ref`. Every source resolves to the same parent set, so a rule written against
"a classified thing regarding this matter" does not care which source produced it.

> **One bounded-classification contract, applied per source.** `$choices` +
> `LookupChoicesResolver` is the contract. The `agreement-classify` bespoke assembler is the
> **anti-pattern not to repeat** — a third implementation would fragment the mechanism the whole
> design rests on.

---

## 14. Taxonomy classifier guidance `[PROPOSED]`

**The problem**: `sprk_triagecategory` has no description column (section 10.1), so the model receives
**bare labels** and must infer meaning from the words alone. Nothing tells it how
`Fee / rate change` differs from `Invoice / Billing`. With nine categories and two newly added
near-neighbours, this is the accuracy risk in the design.

**The fix, fitting the existing pattern**:

1. ✅ **DONE 2026-09-29** — added `sprk_classifierguidance` (MULTILINE TEXT, 2000) to
   `sprk_triagecategory`, and populated **all nine rows** with contrastive guidance.
2. ⬜ **CODE CHANGE REQUIRED** — `LookupChoicesResolver` must emit `name — guidance` pairs into the
   **prompt**. It currently resolves `lookup:sprk_triagecategory.sprk_name`, i.e. the **name attribute
   only**.
3. ⬜ The schema `enum` stays **names only**, so constrained decoding is unchanged.

> 🔴 **The guidance is INERT until step 2 ships.** The column exists and is populated, but the resolver
> reads only `sprk_name`, so the model still sees bare labels today. Do not mistake populated data for a
> live improvement.

**What the guidance is written to do**: the definitions matter less than the **tie-breakers**. Each row
names what belongs *and* redirects the near-neighbour case, because that is where a single-select
classifier actually fails. The three-way money boundary is the one that earns its keep:

| Row | Boundary it defends |
|---|---|
| `Invoice / Billing` | money **already billed** — redirects future cost away |
| `Fee / rate change` | the **price** per unit of work — redirects quantity away |
| `Scope / budget change` | the **quantity** of work / a budget revision — redirects already-billed away |

Others carry dominance rules: a court-set date beats `Scheduling`; an asserted obligation, deadline or
cost beats `Administrative`; a court deadline beats `Opposing counsel`.

Definitions become data: editable without a deploy, same "declared" property as the rows themselves.
Small change, large accuracy payoff, and it generalises to every future taxonomy.

---

## 15. Documentation obligations — facts that must not be re-broken

Each of these was *already* got wrong once, by code or by this spec.

| # | Fact | Why it gets broken |
|---|---|---|
| 1 | **Spaarke does NOT use OOB `task` / `activitypointer`.** Tasks are `sprk_event` with `sprk_eventtype_ref`. `TaskActionCore` writes `new Entity("sprk_event")`; zero OOB task writes exist in `src/` | **The facade lies.** `IActionSeam.CreateTaskAsync` returns `CreateTaskResult.TaskId` — the API says "task", the write is `sprk_event`. An earlier implementation *did* write `new Entity("task")` and was fixed in email-communication-intelligence-r2 |
| 2 | **Daily Briefing is deterministic-query-based.** `DailyBriefingCollector` line 4: *"no appNotification dependency, no scheduled playbooks."* Six channels: `sprk_event`, `sprk_todo`, `sprk_document`, `sprk_matter`, `sprk_project`, `sprk_monitor`. The `NotificationCategoryDto` references are **output** shape (counts), not input | `CommunicationRiActionService`'s own docstring claims the app-notification *"mirrors the action so it surfaces in Daily Briefing"* — **false as written.** The RI action DOES reach the briefing, but via the `sprk_event` it creates, not via the notification |
| 3 | **Taxonomy rows live in Dataverse only** (section 10.2) — never add them to the repo | The seeded rows are absent from `infra/`, which reads like an omission rather than the design |
| 4 | **`sprk_enabled` defaults to false** on `sprk_triagecategory` create (section 10.1) | Silent: a disabled row is simply invisible to the classifier |
| 5 | **`sprk_signaltype` / `sprk_signalvalue` are TAKEN** — they are columns on `sprk_affinity` (`AffinityStore`), unrelated to spend | Section 2.5 proposed a generic `sprk_signal` using those exact field names |

Items 1 and 2 are repo-wide and live in `.claude/FAILURE-MODES.md` — item 1 as **AP-14** (authored as
AP-13, renumbered on merge: `unified-access-control-r2` had already published an AP-13 to master), item 2
as a new worked instance in **AP-12**.

---

## 16. Findings from the live tests and the prompt-structure work `[2026-09-29]`

### 16.1 🔴 RI confidence multiplies by deterministic agreement — a structural zero

`RiConfidenceScorer`: **`confidence = urgencyWeight × deterministicAgreement`**, each clamped to [0,1].
Urgency weights: Urgent 1.0 · High 0.75 · Medium 0.5 · Low 0.25 (unrecognised → 0.5).

Two live emails, same formula:

| Email | Association | urgency | × deterministic | RI confidence | Gate |
|---|---|---|---|---|---|
| `3449bd41…` *EMPL-307998…* | **Resolved** (auto-filed, det 0.90) | High 0.75 | 0.90 | **0.675** | DENY at 0.70 |
| `4cded5d5…` *Form D - 2023…* | **Suggested** (no matter number in subject) | High 0.75 | **0.0** | **0.000** | DENY at 0.60 |

**The reach-out path can never fire for a communication that did not deterministically associate.**
`deterministicAgreement` is a multiplicative factor, so a zero there zeroes the product regardless of
how urgent the message is. The scorer's own docstring states it plainly: *"the deterministic-agreement
factor (which is 0 when no association has resolved yet)"*.

Those unresolved communications are **exactly the ones a human most needs to be told about** — the
system could not file them, so nobody is going to stumble across them. The second email asked for
approval of 100 extra hours at ~$140–145k and produced no outreach of any kind.

**The defect is conceptual, not arithmetic.** One number is carrying two unrelated questions:

- *How sure am I **which matter** this belongs to?* → `deterministicAgreement`
- *How much does this **matter**?* → `urgencyWeight`

A $145k budget request is important whether or not we know where to file it. Multiplying makes filing
confidence a veto over importance.

**Also a ceiling**: with High = 0.75, **any threshold above 0.75 is unreachable** at High priority no
matter how perfect the association. Only `Urgent` (1.0) can clear it. A threshold of 0.70 was therefore
satisfiable only by Urgent, or by High with deterministic ≥ 0.933 — which is why the first email, at a
near-ideal 0.90 deterministic, still failed.

**Options, in order of preference:**

1. **Two numbers, two decisions.** Keep RI confidence for *filing* confidence; add a separate
   *significance* score for notification-worthiness. They answer different questions and should not be
   multiplied.
2. **Additive blend** — `w₁·urgency + w₂·deterministic` — so neither factor can veto the other.
3. **Floor the deterministic factor** (e.g. `max(det, 0.3)`). Cheapest, but keeps the conflation.

Until one lands, the shipped behaviour is: **notify only about mail we already filed confidently.**

### 16.2 🔴 `Deploy-ActionMirrors.ps1` cannot deploy a JPS mirror — and reports `UNCHANGED`

The script's `$fieldMap` reads top-level **`systemPrompt`**, `outputSchema`, `inputSchema`, `name`,
`description`, `tags`. A JPS-shaped mirror such as `triage-email.action.json` has **none of the first
three** — its prompt *is* the document (`instruction` / `input` / `output` / `examples`), which the
file's own `$comment` states is deliberately *"UNLIKE the flat-systemPrompt sibling files in this
directory."*

The loop `continue`s on every absent key, `$body` ends up empty, and the script prints:

```
  UNCHANGED    triage-email
```

Verified: reordering `output.fields` and adding three examples produced `Changed: 0`.

**This is the same failure class the script's own header documents** — compose-r8 added
`target_para_id` to four actions, the mirror never reached Dataverse, the model was still asked for
`target_text`, and every AI edit arrived with no anchor; it reached UAT on 2026-08-26. The difference
is that the gap is now **hidden behind a green success message**, which is worse than the original
missing deployer.

**What the runtime actually stores**: `sprk_systemprompt` holds the JPS subset — exactly the keys
`$schema`, `$version`, `instruction`, `input`, `output`, `examples`, `metadata`, indented, with no
`$comment*` keys and none of the row-level fields (`actionCode`, `name`, `description`, `actionType`,
`modelTier`, `temperature`). **Fix**: for a mirror with no `systemPrompt` key, serialize that subset.

### 16.3 `triage-email`'s output schema is not version-controlled

`infra/dataverse/outputschemas/` holds ten-plus schemas; there is **no `triage-email.schema.json`**.
The contract that governs structured-output emission order therefore exists **only in Dataverse** —
unversioned, unreviewable, invisible to code review. It should be added to the repo, and
`Deploy-ActionMirrors.ps1` extended to own it.

### 16.4 Prompt-structure changes implemented `[LIVE]`

All applied to Action row `c1fa96bf-2697-f111-b8dc-7ced8ddc4a05` **and** the repo mirror.

| # | Change | State |
|---|---|---|
| 1 | **Field order: evidence before conclusions.** `summary → obligations → category → priority → reviewOutcome` (was `category` first) | ✅ live in **both** `sprk_systemprompt` *and* `sprk_outputschemajson` |
| 2 | **Explicit abstain**: `Unclassified` taxonomy row, weight 40, guidance framing it as abstention rather than a default — `92066a73-2abc-f111-aaaf-3833c5e9614d` | ✅ live |
| 3 | **Boundary examples**: 1 → 4, adding the three-way money boundary (Scope / budget change · Invoice / Billing · Fee / rate change) | ✅ live |
| 4 | `sprk_classifierguidance` populated on all ten rows | ⚠️ **inert** — `LookupChoicesResolver` reads `sprk_name` only (§14 step 2) |

> 🚩 **The schema is the lever, not the prompt.** Structured-output decoding emits properties in the
> order the **schema** declares them; the JPS `output.fields` list governs the rendered prompt and the
> `$choices` mapping. Reordering only the prompt would have been cosmetic. Both `properties` **and**
> `required` were reordered in `sprk_outputschemajson`.

**Why field order is load-bearing**: decoding is autoregressive, so a field emitted earlier conditions
every field after it. Leading with `category` made the model commit to a taxonomy row *before*
articulating what the email said. Leading with `summary` and `obligations` lets the classification
condition on the model's own reading. The `$comment-field-order` key in the mirror records this so the
order is not "tidied" back.

### 16.5 Not yet verified

The reorder is **theoretically motivated and untested**. Its value is measurable — the same corpus
re-triaged before and after — and the honest position is that no measurement has been taken. The
boundary examples added in §16.4 are the beginning of that corpus; the first email
(`EMPL-307998…`, an invoice PDF attached *and* future scope proposed, classified `Scope / budget
change`) should be pinned as the first eval case, since it is a real confusable that resolved correctly.

---

## 17. Fixes applied `[2026-09-29]`

### 17.1 `RiConfidenceScorer` — product → weighted sum `[FIXED]`

```
was:  confidence = urgencyWeight × deterministicAgreement
now:  confidence = (0.7 × urgencyWeight) + (0.3 × deterministicAgreement)
```

Owner decision: *"reduce the thresholds because we would rather have to screen out the noise and make
adjustments than miss a flag."* Urgency is weighted higher because notification-worthiness is driven by
how much a message **matters**, not by how confident we are about **where to file it**.

|  | det=0.0 | det=0.90 | det=1.0 |
|---|---|---|---|
| Urgent 1.00 | 0.70 | 0.97 | 1.00 |
| High 0.75 | **0.525** | 0.795 | 0.825 |
| Medium 0.50 | 0.35 | 0.62 | 0.65 |
| Low 0.25 | 0.175 | 0.445 | 0.475 |

Under the product form **the entire `det=0.0` column was 0.000.**

`CommsPolicyOptions.DefaultConfidenceThreshold` **0.8 → 0.35**. Two reasons, the first of which alone
makes 0.8 wrong: the sum's maximum at `High` is 0.825 (0.795 at a realistic 0.90 agreement), so a 0.8
gate would deny every High-priority email however well associated — only `Urgent` could pass. Second,
recall-first: at 0.35 everything Medium-and-above surfaces regardless of association, and Low surfaces
only when reasonably associated. Live rule row `dc423a8b-…` also set to 0.35.

> **Auto-filing is untouched.** The association engine's 0.85 auto-file threshold and its
> deterministic-eligibility rules are unchanged. Filing **writes** data, where a false positive is worse
> than a miss; notification only **surfaces**, where a miss is worse than noise. Recall-first applies to
> notifying, not to filing.

**Regression guard**: `Compute_WhenOneFactorIsZero_StillReturnsNonZero_SoNeitherFactorVetoesTheOther`
pins all four one-factor-zero cases. Every one returned 0.0 under the product form; if any returns 0.0
again, the product has been reintroduced. 39 tests pass (2 pre-existing assertions updated: the theory
table, and `TriagePersistenceSeamTests`' persisted value 0.8 → 0.94).

### 17.2 `Deploy-ActionMirrors.ps1` — JPS mirrors + sidecar schemas `[FIXED]`

Added `Get-JpsSystemPrompt` + `Remove-CommentKeys`: when a mirror has no top-level `systemPrompt`, the
JPS subset (`$schema`/`$version`/`instruction`/`input`/`output`/`examples`/`metadata`, `$comment*`
stripped recursively, depth 40) is serialized into `sprk_systemprompt`. Also added sidecar ownership —
`infra/dataverse/outputschemas/<actionCode>.schema.json` now populates `sprk_outputschemajson`.

Verified in all three directions:

| Check | Result |
|---|---|
| Detects the change it previously missed | `Changed: 1` (was `UNCHANGED` with `Changed: 0`) |
| Round-trip preserves the contract | top keys, field order, 4 examples, **all three `$choices`**, `structuredOutput`, `obligations` array/items/maxItems — all intact; **no `$comment` leaked** |
| Truthfully idempotent | immediate re-run → `UNCHANGED` |
| No regression on flat mirrors | 17 examined, flat mirrors behave as before |

### 17.3 `triage-email.schema.json` added to the repo `[FIXED]`

`infra/dataverse/outputschemas/triage-email.schema.json` — the contract governing structured-output
emission order is now version-controlled and owned by the deployer.

### 17.4 🚩 Pre-existing mirror drift the fix exposes — NOT deployed

With the deployer working, `-Filter '*' -DryRun` reports **3 of 17 mirrors out of sync**:

| Mirror | Delta | Read |
|---|---|---|
| `create-task-from-email` | `sprk_outputschemajson` 2920 → 2375 | formatting (indented → compact) — cosmetic |
| `propose-field-updates` | `sprk_outputschemajson` 2784 → 2306 | formatting — cosmetic |
| **`suggest-followups`** | **`sprk_systemprompt` 3620 → 4678 (+1058)**, `sprk_description` 741 → 816 | **substantive: the repo prompt is newer than the live row, so the model is being asked with a stale contract right now** |

`suggest-followups` is the compose-r8 failure class **live**. Deliberately **not** deployed here — it is
another project's domain and deploying it changes that capability's behaviour. Owner decision required.

### 17.5 🔴 Two defects found while diagnosing — now IN MVP SCOPE, not deferred

> **Disposition changed 2026-09-30.** Both were filed as deferred decisions (D-6, D-7). Owner rule: *"if
> there is work identified in the course of creating this project that can be addressed most efficiently in
> this project, then that is the correct approach — do not defer and hand off."* This project holds the
> complete diagnosis for both, so re-deriving them later costs more than fixing them now. They are
> `design.md` §5 In items. The text below stands as the diagnosis.

**(a) Matter numbers containing spaces are invisible to the deterministic identifier rung.**
`IdentifierReverseLookupRung.WellFormedTokenPattern` is
`\b[A-Za-z]{2,}[-.][A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b` — it requires an alpha prefix **immediately**
followed by `-` or `.`, with no space. Live matter `accb692e-…` is numbered **`"Form D - 2023"`**, whose
spaces around the hyphen defeat the pattern; the bare-numeric fallback (`\b\d{4,}\b`) extracts `"2023"`,
which reverse-looks-up to no matter. Result: `ExplicitReference` **never fired** — absent from
`rungsFired` — even though the subject contained the matter number *and* the matter name verbatim.

The rung's own docstring supplies the argument for broadening safely: *"Precision comes from the EXACT
reverse lookup, not this pattern — an over-match simply resolves to no record."* The cost of broadening
is extra reverse-lookup queries, not extra false positives. Not changed here because it alters the
association engine's precision/cost profile (ADR-045 territory) and deserves its own decision.

**(b) The association decision's `reason` string is false.** For that email it reads *"Reinforced
confidence 0.97 in [0.50, 0.85) ⇒ Suggested"* — **0.97 is not in [0.50, 0.85)**. The status band is
evidently computed from `topDeterministicConfidence` (0) while the message interpolates `topConfidence`
(0.97). This is [AP-12](../../../.claude/FAILURE-MODES.md) in *runtime-generated* prose, which is worse
than a stale comment: it is written into data that users and future diagnoses read as authoritative.
`RecordNameMatch` had in fact identified the matter at 0.97 **and written it** — it is simply excluded
from the deterministic pool by design (surface-for-review, never auto-file).

Neither blocks the MVP: §17.1 makes such an email notifiable regardless, which was the point.
