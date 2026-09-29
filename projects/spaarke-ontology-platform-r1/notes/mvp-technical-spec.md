# MVP Technical Specification — Spend Governance Loop

> **Status**: Draft for review, 2026-09-24. Concrete enough to act on; not yet task-decomposed.
> **Marking convention**: `[VERIFIED]` = read from source in this repo today · `[PROPOSED]` = new, needs review · `[MUST VERIFY]` = I could not confirm and it changes the design.
> **Companions**: [`mvp-synopsis.md`](mvp-synopsis.md) (scope) · [`ontology-component-model.md`](ontology-component-model.md) (definitions)

---

## 0. The differentiation test — read before scoping anything `[Added 2026-09-24]`

> **For every capability we scope, name the data it requires that the incumbent does not hold. If we cannot, it is not differentiated — however good the architecture underneath is.**

| Category | Differentiated? | Why |
|---|---|---|
| Single-source computation — budget variance, rate compliance, accruals, invoice line review | ❌ **No** | Legal Tracker, Onit, Brightflag already do this, often with AI. Budget **and** spend both live in e-billing |
| **Requires data the incumbent doesn't hold** — email, documents, requests, obligations | ✅ Yes | structurally impossible for them |
| **A rule about an object they don't model** | ✅ Yes | you cannot write a rule about a thing their schema lacks |
| **The record of what was decided and what resulted** | ✅ Yes | they *alert*; they do not capture the intervention or its typed outcome — and it **compounds** |

### 0.1 Consequence: budget variance is the plumbing proof, NOT the pitch

The spend loop in §1 exercises all seven pipeline stages at the lowest possible cost, which makes it the right *first* implementation. **It is not a differentiated demo, and must not be presented as one** — the customer's honest response is *"we can already do that."*

**The differentiated demo is the second rule, and it is cross-source:**

> *"Matter 4471 is 18% over budget — **and** outside counsel flagged scope creep in a thread three weeks ago that nobody acted on."*

That requires **spend + communication**. Legal Tracker has no email. And we already own the hard part: the 13-rung association ladder resolving email to matters is our strongest shipped asset.

Other candidates that pass the test: *obligation due in 14 days, contract in iManage, no owner* (document + obligation + matter) · *spend spike with an unanswered inquiry* (spend + originated) · *new request against an already-over-budget matter* (intake + spend).

### 0.2 Consequence: the signal entity must not be spend-shaped

`sprk_spendsignal` has a `sprk_snapshot` lookup — it is structurally spend-only, and a communication- or obligation-derived signal will not fit it. **The second signal type forces generalization anyway**, and it is far cheaper now, while the table has zero client references and no production data. See §2.5.

---

## 1. The end-to-end trace, with real field names

This is the whole MVP. Everything in §§2–7 exists to make this run.

```
① LEDES/export lands                                        Connection Engine  [PROPOSED]
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

⑩ Reply arrives → classified → typed outcome → signal closed
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
| `sprk_ruletype` | option set | **MVP: `Threshold`, `Switch` only** |
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
| `sprk_factsnapshot` | memo (JSON) | **the input values at decision time**, frozen |
| `sprk_gateoutcome` | option set | Approved · Rejected · TimedOut · AutoApproved |
| `sprk_actorid` → systemuser | lookup | **key on `systemuserid` so agents share the model later** |
| `sprk_decidedon` | datetime | |
| `sprk_resultref` | text | `sprk_servicerequest:{guid}` |
| `sprk_evidencerefs` | memo (JSON) | array of typed refs |
| `sprk_hash` | text | SHA-256 over the canonical serialization, chained to prior entry per ADR-015 |

**Append-only enforcement**: security role grants Create + Read only. No role, including System Administrator in the app, gets Update or Delete.

**The write point** is `ConfirmationPolicyEngine` / `GateDecisionV2` — **one entry per gate decision, including rejections and timeouts.** A rejected gate writes no Dataverse row anywhere else, which is precisely why audit cannot substitute.

---

## 7. Connector contract `[PROPOSED]`

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
| 7 | `ISourceConnector` + landing pipeline + `FileExportConnector` | new — **the bulk of the work** |
| 8 | Worklist widget over `sprk_spendsignal WHERE sprk_isactive = true` | new |
| 9 | `sprk_decisionrecord` entity + gate write + subgrid + view | new |
| 10 | `sprk_servicerequest`: outbound direction + typed outcome | extend |

---

## 9. Open items that change the design

| # | Item | Why it matters |
|---|---|---|
| 1 | **`sprk_matter` current schema** — read before adding anything in §5.1 | several fields may already exist under different names |
| 2 | **`sdkmessagefilters` on `sprk_matter` / `sprk_billingevent`** — is `UpsertMultiple` supported? | bulk messages are not available on every table; plugin registrations can disable them |
| 3 | **What populates `sprk_spendsnapshot` today?** | determines whether ① and ③ connect, or whether the snapshot job needs rework |
| 4 | **Null-budget behaviour** (§4) | decide and test; silent wrong-firing is the failure mode |
| 5 | **Which platform is first** | determines the `RestConnector` and whether Tier-1 (API) or Tier-2 (export) is the MVP path |
| 6 | **Does `sprk_servicerequest` have a direction/outcome model already?** | CM-5 assumed it absorbs Inquiry; unverified |
| 7 | **What is the second (cross-source) rule, precisely?** Needs a concrete predicate over communication + spend — e.g. *"over budget AND an inbound communication in the last 30 days classified as scope/fee-related with no disposition"* | **This is the differentiated capability (§0.1). Spec it before building the first rule, so `sprk_signal` is shaped by two producers rather than one** |
| 8 | **Run the §0 differentiation test retroactively** across §8 Wave 2 / Wave 3 modules in the strategy synopsis before any is specced | Several may fail it the same way budget variance did |

---

## 10. Verified against the live dev environment `[2026-09-29]`

Queried directly via Dataverse MCP. These are facts, not inferences — earlier sections that
contradict them are wrong.

### 10.1 `sprk_triagecategory` — the taxonomy is data, not code

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

## 11. The cross-source predicate `[PROPOSED]`

### 11.1 Why the first attempt was wrong

The rule first drafted here was *"over budget AND at least one pending-review email in 30 days."*
**That is two unrelated facts sharing a matter id.** An over-budget matter has many emails; none need
have any bearing on the money. The conjunction established correlation by coincidence.

It was also **lagging**: "over budget" means the money is already spent, and every e-billing vendor
reports it.

### 11.2 The signal that works — leading, not lagging

> **A commitment with financial consequence was made in correspondence, and the budget does not
> reflect it yet.**

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

**No new relationships are required.** The graph is connected; what is missing is a rule that walks it.

### 11.4 The predicate, v1 — deliberately unquantified

Bounded set = the literal rows `{Fee / rate change, Scope / budget change}` (section 10.1), named by
id in the rule body. Not a prompt instruction, not a heuristic — stored ids, editable without deploy.

```
subject: sprk_matter
where:   EXISTS sprk_communication c
           c.sprk_regardingmatter   = subject
       AND c.sprk_triagecategory   IN {8b62dd84-..., 8d62dd84-...}
       AND c.sprk_receiveddate     >= now - 30d
       AND c.sprk_reviewoutcome    <> 100000003        -- not Dismiss
then:    signal "a spend-relevant commitment on this matter is unreconciled against its budget"
```

**v1 does not quantify the amount.** Extracting "$45k" from prose is new extraction work that is
sometimes wrong; the human judges the amount. Still differentiated, because **the join is the
differentiator, not the arithmetic.** Quantification is v2.

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

1. ✅ **DONE 2026-09-29** — added `sprk_classifier_guidance` (MULTILINE TEXT, 2000) to
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

Items 1 and 2 are repo-wide and belong in `.claude/FAILURE-MODES.md`, not only here.
