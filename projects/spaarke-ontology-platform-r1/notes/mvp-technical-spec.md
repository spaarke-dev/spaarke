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
