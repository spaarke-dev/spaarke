# Schema draft — the five new tables

> ## 🟡 STATUS: DRAFT for owner validation — 2026-10-02
>
> Requested so the tables can be **created manually in Dataverse before the project starts**. Five new tables:
> `sprk_signal` · `sprk_decisionrecord` · `sprk_policy` · `sprk_policyversion` · `sprk_budgetrevision`.
>
> **Decisions this encodes** (all settled): D-2 (field list) · D-3 (polymorphic subject + always-populated
> matter lookup) · D-4 (reuse `sprk_triagecategory`) · D-7 (sibling of `InsightArtifact`, not subtype) ·
> D-8 (no per-entity fact/signal tables) · CM-4 (`disposition`) · CM-9 (resolution semantics) · BR-1 (nullable
> decision reference). Live schema facts from [`mvp-technical-spec.md`](mvp-technical-spec.md) §10.7.
> **Naming**: no underscores between words — `sprk_taskduedays`, never `sprk_task_due_days`.

---

## 0. Read this before creating anything — three things to verify or decide

**(a) `sprk_signaltype` / `sprk_signalvalue` — the "collision" needs one check.** `design.md` §5 warns these are
*"already taken"* on `sprk_affinity`. **In Dataverse, attribute logical names are unique per ENTITY, not
globally**, so `sprk_signal.sprk_signaltype` would be legal. The real risk is narrower: **if `sprk_affinity`'s
`sprk_signaltype` is a GLOBAL choice set**, the option-set name collides. Check that before reusing the name.
Either way I have **avoided both names below** — not because they are illegal, but because two different meanings
under one name is how the `MetricCard` and `getXrm` confusion in the component audit happened.

**(b) The subject pattern — one decision.** `sprk_servicerequest` uses **thirteen typed `sprk_regarding*`
lookups plus a generic trio** (`recordid` / `recordtype` / `recordname`). That is the established pattern, and
consistency matters since CM-5 puts Inquiry on that table.

For `sprk_signal` I recommend **the generic trio + one typed matter lookup**, not thirteen lookups:

| Option | Pros | Cons |
|---|---|---|
| **Trio + matter lookup (recommended)** | 4 columns; the matter lookup gives the grouping join and security trim that actually matter (D-3); extensible to any subject with no schema change | No referential integrity or cascade on the non-matter subject; a subject deleted out from under a Signal leaves a dangling id |
| Thirteen typed lookups | Full RI; consistent with `sprk_servicerequest` | 13 columns on every row for a set we expect to grow; a new subject type becomes a schema change, which undercuts the zero-deployment property |

The dangling-id risk is mitigated by the **resolution sweep** (CM-9), which already has to run: a Signal whose
subject no longer resolves closes as `Superseded`. Typed lookups can be added later for a specific entity if a
query needs them — additive, not a migration.

**(c) Append-only and immutable are enforced by PRIVILEGES, not columns.** `sprk_decisionrecord` must have
**no Update and no Delete** privilege for any security role (component model §4.13); `sprk_policyversion` must
have **no Update** after create. Easy to forget when creating tables by hand, and the whole defensibility
argument rests on it.

---

## 1. `sprk_signal` — a condition that held

One row per unresolved condition. Replaces `sprk_spendsignal` (0 rows live, so free). **Sibling of
`InsightArtifact`, never a subtype** (D-7) — it cites artifacts as evidence.

### Identity + subject

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | Primary name. The **short headline** — prototype finding 14: the list cannot show a full sentence |
| `sprk_signalnumber` | Autonumber | | `SIG-{SEQNUM:00000}` |
| `sprk_subjecttype` | Choice | ✅ | The subject's entity logical name: `sprk_matter` · `sprk_communication` · `sprk_event` · `sprk_todo` · `sprk_workassignment` · `sprk_servicerequest` · `sprk_invoice` · `sprk_document` |
| `sprk_subjectid` | Text (50) | ✅ | Subject GUID. ⚠️ Store **lowercase, braces stripped** — see the `cleanGuid` finding (audit §8.3 U1); two regexes are in circulation today |
| `sprk_subjectname` | Text (400) | | Denormalized display name so the list renders with no join |
| **`sprk_matter`** | **Lookup → `sprk_matter`** | ✅ | **The grouping key — ALWAYS populated, derived from the subject** (D-3). This is what makes "rows are matters, grouping Signals" a query rather than client-side work |
| `sprk_tenant` | Text (100) | | Scope, matching `sprk_communicationrule`'s pattern |

### Why it fired

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_policy` | Lookup → `sprk_policy` | ✅ | |
| `sprk_policyversion` | Lookup → `sprk_policyversion` | ✅ | **Immutable** — what the rule *was* when it fired. Old Signals stay explicable after a threshold changes |
| `sprk_policycode` | Text (50) | ✅ | Denormalized; survives policy deletion and renders without a join |
| `sprk_sentence` | Multiline (2000) | ✅ | The rendered sentence. ⚠️ **§0.3-bound**: it may assert only what the predicate read |
| `sprk_evidencerefs` | Multiline (JSON) | ✅ | Typed refs — `[{"kind":"snapshot","ref":"sprk_spendsnapshot:{guid}","tier":"Fact"},{"kind":"communication","ref":"sprk_communication:{guid}","tier":"Observation","confidence":0.94}]`. **Replaces `sprk_spendsignal.sprk_snapshot`**, which was a hard lookup and is why that table was spend-only. `tier` drives the epistemic rendering (Fact flat, Observation hedged) |
| `sprk_factsnapshot` | Multiline (JSON) | ✅ | **The fact values at DETECTION time.** 🔴 **Deliberately on this table as well as on `sprk_decisionrecord`** — they answer different questions, and a Signal that auto-closes as `ConditionCleared` never produces a Decision Record, so without this the detection-time facts are lost entirely. See §2 note (b) |

### Lane, rank, severity

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_lane` | Choice | ✅ | `Decide` (100000000) · `Do` (100000001) — BR-5's two lanes, Decide always above Do |
| `sprk_severity` | Choice | | `Info` · `Warning` · `Critical` — vocabulary copied from live `sprk_spendsignal` |
| `sprk_rankscore` | Whole number | | **Deterministic** rank (row-contract req. 2). `sprk_highpriority` on the subject is an *input* here, not a separate list (BR-3) |

### Lifecycle — CM-9, and the vocabulary already half-exists

> 🔬 Live `sprk_spendsignal` already carries **`sprk_spendsignalstatus`** (Active / Acknowledged / Resolved /
> **Auto Resolved**) and **`sprk_resolutionnotes`** (spec §10.7). *Auto Resolved* **is** `ConditionCleared`. So
> this is vocabulary to **copy**, not invent. What does not exist is the **sweep that sets it** — the table has
> 0 rows, so nothing ever has.

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_signalstatus` | Choice | ✅ | `Open` · `Acknowledged` · `Resolved`. The worklist queries `Open`/`Acknowledged` |
| `sprk_resolutiontype` | Choice | | **Null while open.** `Acted` · `Dismissed` · `ConditionCleared` · `Superseded` · `PolicyRetired`. 🔴 **Load-bearing in two places**: it keeps the three-dismissal suppression rule honest (an auto-clear must not count as a human dismissal), **and** it is where *action rate = acted ÷ surfaced* is computed from for **Do** rules, since BR-1 means those write no Decision Record |
| `sprk_resolutionnotes` | Multiline (2000) | | The dismissal reason. Clustered over time, these are the input to *"'already approved offline' forty times is a missing fact, not a bad rule"* |
| `sprk_resolvedon` | Date/Time | | |
| `sprk_resolvedby` | Lookup → `systemuser` | | **Keyed on `systemuserid`** so humans and agent users share one model when Authority arrives (component model §11, item 1) |
| **`sprk_decisionrecord`** | **Lookup → `sprk_decisionrecord`** | | 🔴 **NULLABLE — this is BR-1.** A Decide item resolved through the gate points at its record; a **Do** item completed as one's own work closes `Acted` with this **null**. See §6 for the rationale |

### Dedupe + the sweep

| Column | Type | Req | Notes |
|---|---|---|---|
| **`sprk_dedupekey`** | Text (400) | ✅ | **ALTERNATE KEY.** `{policycode}|{subjecttype}|{subjectid}` → re-evaluation **upserts** instead of duplicating. ⚠️ Live `sprk_spendsignal` gets away with an implicit `(matter, signaltype)` key via `GenerateDeterministicId` (`SignalEvaluationService.cs:241-258`), which **does not survive a polymorphic subject** — this column is the replacement |
| `sprk_firstdetected` | Date/Time | ✅ | |
| `sprk_lastevaluated` | Date/Time | ✅ | The **sweep** stamps this. A still-Open Signal whose predicate no longer holds gets `ConditionCleared`. Without the sweep the worklist rots — `design.md` §9 |
| `sprk_suppresseduntil` | Date/Time | | Denormalized so the worklist query is cheap rather than counting Decision Records per load |

### Carried, never acted on

| Column | Type | Notes |
|---|---|---|
| `sprk_privilegeflagged` | Yes/No | **ADR-015 — flagged, never decided.** Copied forward; nothing branches on it |

---

## 2. `sprk_decisionrecord` — D-2's field list

**Append-only** (no Update, no Delete privileges — §0(c)).

> 🔴 **The relationship direction is easy to get backwards.** Prototype finding 4: **one decision can resolve
> several Signals**, so the relationship is **Decision Record 1 → N Signals**. The foreign key therefore lives on
> **`sprk_signal.sprk_decisionrecord`** (§1), and there is **no signal lookup on this table**. A subgrid of
> "Signals closed by this decision" renders from that side.

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | Primary |
| `sprk_decisionnumber` | Autonumber | | `DR-{SEQNUM:00000}` |
| `sprk_matter` | Lookup → `sprk_matter` | ✅ | Grouping + the matter subgrid + Report Card accrual |
| `sprk_policyversion` | Lookup → `sprk_policyversion` | ✅ | **What the rule was.** Immutable, so the decision stays defensible after retuning |
| **`sprk_factsnapshot`** | **Multiline (JSON)** | ✅ **MANDATORY** | **What the data said at DECISION time.** D-2's binding constraint and the compensating control for ruling out bitemporality (`design.md` §8.2). 🔴 **Do not make this optional** — the gap it closes is invisible until the first time someone audits an old decision |
| `sprk_proposedaction` | Text (400) | ✅ | What was put to the human |
| `sprk_decisionoutcome` | Choice | ✅ | `Authorized` · `Denied` · `Dismissed`. ⚠️ **Distinct from `sprk_disposition`** — see the note below |
| `sprk_reason` | Multiline (2000) | | Required by the UI on `Denied`/`Dismissed`; nullable at the schema level |
| **`sprk_action`** | Lookup (or Text) | | 🔴 **NULLABLE — D-2 constraint.** A **deny path has no action**. Equally, an action taken outside a Signal has no decision |
| `sprk_confirmedby` | Lookup → `systemuser` | ✅ | The human. Keyed on `systemuserid` for the Authority model later |
| `sprk_decidedon` | Date/Time | ✅ | |
| `sprk_authority` | Text (100) | | **Post-MVP, do not foreclose** (decision 8: while every action is confirmed, the human *is* the authority) |
| `sprk_gatesessionid` | Text (100) | | Links to the **ADR-040 `SessionGate`** ledger entry. This is the "siblings, not one serving both" relationship from `design.md` §6's ADR-040 tension |
| `sprk_privilegeflagged` | Yes/No | | ADR-015 carry-forward |

> **⚠️ Two outcome vocabularies, deliberately. CM-4 adopted `disposition`, and it belongs on the Inquiry.**
> `sprk_decisionoutcome` here answers *"what did the human decide at the gate?"* (Authorized / Denied /
> Dismissed). **`sprk_disposition` on `sprk_servicerequest`** answers *"how did the Inquiry turn out?"* —
> write-off, budget revised, scope approved, no action — and that is what accrues per matter and per outside firm
> for criterion 10 and the Report Card. One word per concept; do not merge them.

**Also required on `sprk_servicerequest`** (verified absent, spec §9 item 6): a **direction discriminator**
(`Inbound` / `Outbound`) and **`sprk_disposition`** (the choice above). CM-5 says this table absorbs Inquiry;
these two columns are what that costs.

---

## 3. `sprk_policy` — stable identity

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | **A plain display name** — prototype finding 15: *"Budget Variance Policy"*, not `POL-BUDGET v3`. Codes belong in *How this was determined* |
| `sprk_policycode` | Text (50) | ✅ | **Alternate key.** `POL-COMMIT-BUDGET` |
| `sprk_enabled` | Yes/No | ✅ | Default **No** — ⚠️ a new row must not go live before it is reviewed. *(The taxonomy rows created on 2026-09-29 defaulted to disabled and were invisible to the classifier until noticed; same trap, opposite direction.)* |
| `sprk_priority` | Whole number | ✅ | Default **500**. **Lowest wins**, ties broken by record id — copy `CommunicationRuleGate.cs:129-133` verbatim |
| `sprk_tenant` | Text (100) | | **Blank = all tenants** (`:175-181`) |
| `sprk_matter` | Lookup → `sprk_matter` | | **Empty = all matters** (`:183-188`) |
| `sprk_lane` | Choice | ✅ | `Decide` · `Do` |
| `sprk_subjecttype` | Choice | ✅ | Same option set as `sprk_signal.sprk_subjecttype` |
| `sprk_currentversion` | Lookup → `sprk_policyversion` | | Convenience pointer |

## 4. `sprk_policyversion` — immutable

**No Update privilege after create** (§0(c)). This is what makes a Decision Record defensible.

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | `POL-COMMIT-BUDGET v3` |
| `sprk_policy` | Lookup → `sprk_policy` | ✅ | |
| `sprk_versionnumber` | Whole number | ✅ | |
| `sprk_ruletype` | Choice | ✅ | `Threshold` · `Switch` · **`Existence`** (CM-7). ⚠️ **Closed set** — adding one is a code change with review (component model decision 7) |
| `sprk_rulebody` | Multiline (JSON) | ✅ | The predicate. **CM-3: a Dataverse filter, nothing more.** ⚠️ **No cross-clause variable passing** (§8.0.1(a)) — two independent ANDed clauses over a fixed window. Validated against a JSON Schema per rule type; spec §3.5 **refuses to save an invalid body** |
| `sprk_shortheadline` | Text (200) | ✅ | Finding 14 — what the list row shows |
| `sprk_messagetemplate` | Multiline (2000) | ✅ | Renders `sprk_signal.sprk_sentence`. 🔴 **§0.3-bound**: may reference only fields the body reads |
| `sprk_proposedaction` | Text (400) | ✅ | Finding 16 — *"a rule must declare its proposed action to be reviewable"*; review shows the rule's recommendation first |
| `sprk_confidencethreshold` | Decimal (2dp) | | **Declared on the row** (decision 14), with `CommsPolicyOptions` as fallback only |
| `sprk_inforcefrom` | Date/Time | ✅ | |
| `sprk_inforceto` | Date/Time | | **Null = currently in force** |
| `sprk_authoredby` | Lookup → `systemuser` | ✅ | |

## 5. `sprk_budgetrevision` — smallest possible

Verified necessary (CM-10): `sprk_budget` has `sprk_totalbudget` + `modifiedon` only, **no revision rows**, and
auditing is not wired. ⚠️ **`sprk_budget.modifiedon` is NOT a substitute** — any unrelated edit bumps it, which
would read as *"the budget was revised"* and **suppress a true signal** (a false negative, against decision 13).

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | Primary |
| `sprk_matter` | Lookup → `sprk_matter` | ✅ | |
| `sprk_budget` | Lookup → `sprk_budget` | | |
| `sprk_prioramount` | Currency | | |
| `sprk_newamount` | Currency | ✅ | |
| **`sprk_revisedon`** | **Date/Time** | ✅ | 🔴 **This is the column Path B's second conjunct reads.** Everything else here is context |
| `sprk_reason` | Multiline (2000) | | |
| `sprk_revisedby` | Lookup → `systemuser` | ✅ | |

---

## 6. BR-1 — why a Do-lane completion writes no Decision Record

The owner asked: *"why does it matter if the decision was completed by one's own assigned work?"* Fair question.
**My first answer led with the weaker argument**, so here is the real one.

### The rationale: a Decision Record records a JUDGEMENT; a task completion records WORK

| | Decide item | Do item, completed |
|---|---|---|
| The system says | *"A commitment was raised and the budget has not moved"* | *"Your task is 7 days overdue"* |
| The human does | **Chooses among options under uncertainty** — send inquiry / revise budget / approve variance / dismiss | **Does the work** |
| What new information exists afterwards | The human's judgement about an ambiguous situation — **which existed nowhere before** | None. The work is done |
| Who already records it | Nothing — hence the Decision Record | **The task itself**: its status, its owner, when it changed, the reschedule reason. With better fidelity than a copy would have |

So the record is not withheld for tidiness — **there is no judgement to record.** Writing one would duplicate
what `sprk_event`/`sprk_todo` already hold, with fewer fields.

### The test, stated so it generalizes

> **Does the action say something about the RULE, or does it just execute the work the rule surfaced?**

That is why the asymmetry falls where it does: **dismissing a Do item *is* a judgement** — *"not mine"*, *"no
longer needed"* is a statement about the rule having fired wrongly — so a dismissal **does** write a Decision
Record and **does** count toward suppression. Completing it says nothing about the rule at all.

### What I got wrong the first time

I led with **volume** — 11 overdue tasks against 5 decisions would swamp the record. That is a *consequence*, not
a reason. "Too many rows" argues for filtering a query, never for discarding data; if the Decision Record is the
asset, losing entries because there are many of them is backwards. Volume only matters once you have established
that the entries carry no information, which is the argument above.

### What your challenge actually surfaced — one real dependency

If Do completions write no Decision Record, **where does *action rate = acted ÷ surfaced* come from for a Do
rule?** From **`sprk_signal.sprk_resolutiontype`**, not from the Decision Record.

That makes `sprk_resolutiontype` **mandatory rather than nice-to-have** (§1), and it is now recorded there. Had
we not asked this, the metric could have been built against the Decision Record and silently reported ~0% action
rate on every Do rule — a policy generating noise and a policy working perfectly would look identical.

### When to reverse this

Two conditions, either of which flips it:

1. **If the Report Card needs completed work in the same place as judgements.** The cheaper fix is for the Report
   Card to read task history for work and Decision Records for judgements. But if "one auditable table" is a
   product requirement, write the records and accept the dilution.
2. **If a completion ever becomes gated** — e.g. closing a task with an unmet obligation requires confirmation.
   Then it *is* a judgement, *"no gate, no entry"* applies in the affirmative, and it writes a record.

Reversing is **additive** — start writing records for completions — so this is a safe default rather than a
one-way door. The field that would have to change (`sprk_signal.sprk_decisionrecord` nullable) stays nullable
either way, which is why D-2 required that constraint.
