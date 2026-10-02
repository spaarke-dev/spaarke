# Ontology Architecture — Review Feedback

> ## ✅ STATUS: APPLIED — this is a historical INPUT, not a live document `[stamped 2026-10-01]`
>
> Every finding in this review has been dispositioned. **Do not act from this file; act from `design.md`.**
>
> | Where it went | What |
> |---|---|
> | `design.md` §0.3 | §1.1's blocking finding, generalized — *a capability must TEST what its message CLAIMS* |
> | `design.md` §8.0 (CM-6..CM-11) | §6's open decisions, each resolved — Path B adopted, `Existence` added, `sprk_budgetrevision` created, Inquiry restored |
> | `design.md` §7 criteria 10 + 11 | §1.3 (the action loop regressed) and §1.6b (classifier recall) |
> | `design.md` §8.0.1 | §3.3's proposed predicate body, **rejected** — it passes a variable between clauses, which breaks CM-3 |
> | `mvp-technical-spec.md` §3.4a, §11.4 | the `Existence` rule body and the corrected predicate |
> | the Console prototype | §4's row contract, built and then **corrected by it** — findings 2, 4, 8 and 9 contradict §4.2/§4.4 |
>
> ⚠️ **Three places this review is now known to be wrong**, so read it with them in hand: its §4.2 target row
> over-claims in its sentence's verb (prototype finding 1); its §3.3 body cannot produce the per-clause witnesses
> its own §4 requires (finding 2); and its §4.4 *acted ÷ surfaced* metric needs a better denominator (finding 8).

> **Status**: External review input, 2026-09-30. Not a design document and not a competing one.
> **Reviewed**: `design.md` (Ontology Platform R1, draft 2026-09-30) · `mvp-synopsis.md` (2026-09-24) ·
> `mvp-technical-spec.md` (2026-09-24, verified 09-29) · `ontology-component-model.md` (2026-09-24)
> **Vocabulary**: `ontology-component-model.md` §3 is authoritative. Where this document's source
> conversation used different terms, §5 below records the supersession explicitly.
> **Audience**: Claude Code · `/design-to-spec` · owner review.

---

## 0. How to use this document

Three parts, in priority order:

1. **§1 — findings against the current drafts.** Six items. The first two are blocking; the third is a
   scope regression between two of the drafts.
2. **§2–4 — material to absorb.** The eight signal shapes, the per-shape declaration contract, and the
   action-experience requirements for a worklist row. These were developed in review and are not yet in
   any repo document.
3. **§5–6 — terminology supersession and open decisions to add to the register.**

Everything proposed here is tagged `[PROPOSED]`; nothing has been read from source. Items marked
`[VALIDATION NEEDED]` change the design if they come back the other way.

**What this document does not do.** It does not revisit decisions the component model has settled:
CM-3 (rule body = Dataverse filter; facts materialized as rollup + calculated column), Fabric IQ as L5
only, Authority post-MVP, Action Engine out of MVP, evaluate-on-write with a flag entity, the
`sprk_policy` / `sprk_policyversion` split, or LLM-at-author-time. Those are correct and this document
builds on them.

---

## 1. Findings

### 1.1 🔴 The §11.4 predicate does not test the budget — the differentiation claim is unmet

`mvp-technical-spec.md` §11.4, v1 predicate:

```
subject: sprk_matter
where:   EXISTS sprk_communication c
           c.sprk_regardingmatter   = subject
       AND c.sprk_triagecategory   IN {Fee / rate change, Scope / budget change}
       AND c.sprk_receiveddate     >= now - 30d
       AND c.sprk_reviewoutcome    <> Dismiss
then:    signal "a spend-relevant commitment on this matter is unreconciled against its budget"
```

**Nothing in that `where` clause reads spend, budget or a snapshot.** It fires on any matter carrying a
recent, non-dismissed, scope-or-fee-classified communication. Two consequences:

**(a) It is single-source, so it fails the §0 differentiation test.** §0.1 states the capability
"requires **spend + communication**." The predicate requires communication only. An incumbent cannot
produce it because it has no email — but the budget half of the claim is never computed, so what ships
is "we classified an email," not "we compared two systems."

**(b) The message asserts a conclusion the computation never reached.** *"unreconciled against its
budget"* when no budget was consulted. This is the **AP-12 class the spec itself catches in §17.5(b)** —
runtime-generated prose stating something the code did not establish — this time on the headline claim
rather than a diagnostic string.

§11.2's worked case is right and should be preserved: counsel proposes $40–45k beyond budget while
e-billing shows $180k of $250k and stays silent for 30–60 days. Expressing *"the budget does not reflect
it yet"* requires a second conjunct — **no budget revision on this matter after `c.sprk_receiveddate`** —
which needs budget **with a change date**, not only a current `sprk_spendsnapshot.sprk_budgetamount`.

**This makes D-1 harder than currently scoped.** §8.1 asks what populates the snapshot; it should also
ask whether budget carries history. Add as a fourth spike question.

**Two honest paths — pick explicitly, do not leave implicit:**

| Path | Consequence |
|---|---|
| **A — keep v1 single-source**, rewrite the message to claim only what is tested (*"a spend-relevant commitment was flagged and not dispositioned"*) | Ships sooner; **does not pass §0**; must be labelled a stepping stone in §1.1 of `design.md`, not the differentiated demo |
| **B — add the second conjunct** | Passes §0; **D-1 becomes a hard blocker**; probably requires budget-with-history or a `sprk_budget` entity with revision rows |

Path B with a minimal budget-revision record is the recommendation. The corrected predicate is in §3.3.

**Blast radius of this finding**: `design.md` §1 (the falsifiable claim), §7 criterion 2, §7 criterion 9,
§8.1 (D-1 exit condition), `mvp-technical-spec.md` §0.1, §11.2, §11.4.

---

### 1.2 🔴 The cross-source predicate has no rule type — it cannot be saved

`mvp-technical-spec.md` §3.2 restricts MVP to `sprk_ruletype` ∈ {`Threshold`, `Switch`}. §3.3's
`Threshold` body is a scalar comparison:

```jsonc
{ "type":"Threshold", "subject":…, "field":…, "op":…, "value":…, "when":{…} }
```

§11.4's predicate is an **EXISTS over a related entity** with an IN-list, a date window and a
not-equals. It does not fit that schema, and §3.5 enforces schema validation on save — *"an invalid body
cannot be saved."* **The differentiated capability is therefore unsavable under the MVP rule-type set.**

The component model's full set of seven (§4.8 — `Threshold` · `AllowList` · `Switch` · `DecisionTable` ·
`Envelope` · `SlaDefinition` · `DelegationLimit`) has no join or existence type either, so this is not an
MVP-subset artifact.

Under CM-3 the body is a Dataverse filter, and **FetchXML `link-entity` with a filter expresses EXISTS
natively**, so the implementation is small. What is missing is a *name* in the closed vocabulary — and by
the component model's own guard (*"adding a rule type is a code change with review, never a config
change"*), that is a decision to take deliberately before the evaluator ships.

**Minimum MVP addition: one rule type, `Existence`.** Specified in §3.2 below.

See §2.2 for why the closed set of seven is closed over the wrong axis, which is the general form of
this problem.

---

### 1.3 🟠 The action loop regressed between `mvp-synopsis.md` and `design.md`

`mvp-synopsis.md` (09-24) has the loop closed:

- §4.4 — *"Inquiry action | One Action + one Binding. Dispatch already exists… Uses `sprk_servicerequest`
  with an outbound direction + typed outcome (CM-5)"* — classified as **data, not build**.
- §6 criterion 5 — *"One click sends a budget inquiry through the gate; the inquiry is tracked with an
  SLA; the reply resolves it with a **typed outcome**."*

`design.md` (09-30) drops it. §5 In lists seven items, none of which is the Inquiry action. §7's nine
criteria contain **no criterion in which anything happens in the world** — they cover detect, record,
surface, suppress, tune and render. The action vocabulary is reduced to disposition (confirm / dismiss).

This matters for two reasons:

1. **The §0 test as written asks only about data.** An incumbent alerting product also detects and
   notifies. What it cannot produce is *the record of the intervention and its typed outcome* — which
   `mvp-technical-spec.md` §0 itself lists as the fourth differentiated category. Without an effect
   action, that row of the table is unexercised.
2. **Nothing compounds.** Criterion 5's threshold tuning works from confirmed-vs-dismissed. But the
   Matter Report Card — which §4.13 of the component model calls *"the asset"* — accrues only from typed
   outcomes. No Inquiry, no outcome, no Report Card data.

**Recommendation:** restore the synopsis criterion as `design.md` §7 criterion 10, verbatim in substance:

> **10.** A confirmed signal produces an Inquiry through the existing gate; the Inquiry carries an SLA;
> the reply resolves it with a **typed outcome** queryable per matter and per outside firm.

The cost is one Action row plus one Binding (§4.4 of the synopsis — dispatch already exists). If it
genuinely cannot fit, say so explicitly in `design.md` §1.1 — *"the MVP proves detection and
defensibility; the effect loop is R2"* — so nobody demos it as an operating system. Right now the
omission is silent rather than declared.

---

### 1.4 🟡 No auto-resolution path; dedupe key unspecified on the generic signal

`ontology-component-model.md` §6.1.1 closes a flag when the user acts (*"User acts → gate → Decision
Record written · flag closed"*). **Nothing closes a flag when the condition clears on its own** — the
budget gets revised, the communication is later dismissed, the matter closes.

Two consequences:

- **Stale rows accumulate in the worklist.** This is how internal alerting surfaces die; §9 of
  `design.md` already names notification fatigue as a Medium risk, and un-resolving flags are a larger
  contributor than threshold tuning.
- **Suppression miscounts.** §12.2 item 1 suppresses after three dismissals. If a flag that auto-cleared
  is indistinguishable from one a human dismissed, the count is wrong in both directions.

**`sprk_spendsignal`'s idempotent upsert works today** because the subject is implicitly
(matter, signaltype). That implicit key does not survive the polymorphic subject on generic `sprk_signal`
(`mvp-technical-spec.md` §2.5).

**Recommendation `[PROPOSED]`** — three small additions to `sprk_signal`:

| Field | Purpose |
|---|---|
| `sprk_dedupekey` — text, **alternate key** | `signaltype + subjecttype + subjectid + periodkey`. The idempotency contract, explicit rather than implied |
| `sprk_resolutiontype` — option set | `ConditionCleared` · `Acted` · `Dismissed` · `Superseded` · `PolicyRetired`. What suppression counts and what the scorecard reads |
| `sprk_lastevaluated` — datetime | Staleness. Distinguishes *"still true"* from *"not re-evaluated"* |

Plus a **resolution sweep** in the same evaluation pass: a flag whose predicate no longer holds is closed
with `ConditionCleared`, not left open. Scope the sweep by changed-subject sets with a bounded full pass,
or it will not scale.

This connects to the deferred generic-`sprk_signal` decision in `design.md` §5: **the shape question
should include the dedupe key and resolution semantics, not only columns.** Otherwise it gets decided
twice, and the second time is a migration.

---

### 1.5 🟡 Shape availability is constrained by binding mode — and that consequence is not drawn

`ontology-component-model.md` §4.5 defines reference mode as *"rows appear lazily… no complete
inventory,"* and §4.7 puts Document in reference mode via MCP. The consequence for signals is not stated
anywhere:

> **Absence and trend shapes are structurally impossible in reference mode.** You cannot detect
> *"no DPA on this engagement"* or *"obligations due this week"* across a set you never enumerated.

| Shape | reference | mirror | system of record |
|---|---|---|---|
| Threshold · Deviation · Transition | on surfaced rows only | ✅ | ✅ |
| Temporal | on surfaced rows only | ✅ | ✅ |
| **Absence** | ❌ **impossible** | ✅ | ✅ |
| **Trend** | ❌ (no history) | ✅ | ✅ |
| Correlation | partial — only across enumerated sides | ✅ | ✅ |
| External | n/a | n/a | n/a |

Worth one line in §4.5 or §4.7 of the component model, because it is the concrete reason to ask a
customer for API access rather than accepting MCP-only: **API access buys the obligation module; MCP
alone does not.** That is a better sales argument than any architectural one.

---

### 1.6 Smaller items

| # | Item | Where |
|---|---|---|
| a | **Make evidence-value snapshotting a D-2 requirement, not an option.** Ruling bitemporality out (component model §10 ADR-3) on the grounds that immutable policy versions buy ~80% of the defensibility is a fair trade. The residual gap is that you can reconstruct *which rule was in force* but not *what the data said*. `sprk_decisionrecord.sprk_factsnapshot` (spec §6) closes it — so it should be mandatory in the D-2 field list rather than one row among many | `design.md` D-2 |
| b | **Add a classifier-recall criterion.** The predicate is a conjunction, so it inherits its weakest input — the communication classifier. Criterion 8 proves a new category changes behaviour; nothing measures whether `Scope / budget change` is *detected*. At 70% recall the differentiated claim silently misses 30% of cases. §16.5 already nominates the first eval case; make the floor a criterion | `design.md` §7 |
| c | **§0 is referenced four times and does not exist in `design.md`** (decision 12, criterion 9, §1.1's "(spec §0)"). For a document whose header says *"this document holds the decisions,"* a binding test living only in the spec is a dangling reference. Inline it | `design.md` |
| d | **Off-by-one between §5 In and §8.** D-3 is listed as blocking "Item 4" but the worklist is item 3; D-4 blocks "Item 5" but memo is item 4. §12 step 3 inherits it. Mechanical, but this document feeds `/design-to-spec` programmatically | `design.md` §5/§8/§12 |
| e | **Decision Record and an action record are different objects.** A deny-path Decision Record has no action; an action taken outside a signal has no decision. The MVP choice — persist the value that already exists at the gate — is right. Just ensure the D-2 field list does not foreclose a nullable action reference | `design.md` D-2 |

---

## 2. The eight signal shapes

### 2.1 The shapes

Every monitoring condition in legal operations is one of eight shapes applied to an object. This is a
**detection** taxonomy — what kind of condition is being recognized — and it is orthogonal to the rule
*body grammar* (§2.2).

| Shape | Recognizes | Legal instances |
|---|---|---|
| **Threshold** | a computed measure crosses a bound | budget variance, rate above card, matter load, cycle time |
| **Temporal** | due, overdue, or stale | obligation due date, SLA breach, no activity in 30 days, filing deadline |
| **Absence** | something expected did not happen | no reply to an Inquiry, missing DPA, open matter with no budget, no invoice from a firm in 60 days |
| **Deviation** | observed differs from what policy permits | clause outside playbook, OCG violation, off-panel firm, unapproved signatory |
| **Transition** | an object entered a state needing attention | matter closed with open obligations, request unassigned > 4h, superseded document still referenced |
| **Correlation** | two independent conditions hold on one subject | **the MVP claim** — a commitment in correspondence *and* a budget that does not reflect it |
| **Trend** | rate of change against a baseline or peer group | spend acceleration, firm response latency degrading |
| **External** | an inbound event matched to a subject | docket entry, IP renewal window, regulatory change |

Why this is worth keeping as an explicit taxonomy rather than an implicit one:

- **It makes the catalog derivable rather than brainstormed.** Eight shapes × the object classes gives a
  finite candidate space. A brainstormed alert list is infinite and unprioritized.
- **Each shape is one generic evaluator**, so a new signal is a `sprk_policyversion` row. That is the
  package-not-code rule applied to detection.
- **Correlation is the only shape no single source system can satisfy**, which is why it is the right
  MVP choice and the right demo. That judgment in `design.md` §1 is correct and this taxonomy explains
  *why* it is correct.

### 2.2 Mapping to the seven closed rule types — the closed set is closed over the wrong axis

The component model's seven types (§4.8) mix two different things: shapes that **detect a condition**
and shapes that **authorize an action**.

| Closed type | What it actually is | Detection shape it covers |
|---|---|---|
| `Threshold` | detection | Threshold |
| `AllowList` | detection | Deviation (membership form) |
| `DecisionTable` | detection | Threshold / Deviation (multi-input form) |
| `SlaDefinition` | detection | Temporal (SLA-specific only) |
| `Switch` | **control** — suppression | none |
| `Envelope` | **authorization** | none |
| `DelegationLimit` | **authorization** | none |

So of seven types, roughly three-and-a-half are detection shapes, two are authorization, one is control.
**Four detection shapes have no type at all: Absence, Correlation, Transition, Trend** — and those four
are precisely the ones that pass the §0 differentiation test, because they are the ones requiring data or
history a single incumbent does not hold.

**Recommendation `[PROPOSED]`:** separate the two axes on `sprk_policyversion`.

- `sprk_ruletype` — the **body grammar** the JSON Schema validates against (§3.5 keeps working unchanged)
- `sprk_detectionshape` — the **shape**, declared, used to select the evaluator and to drive the
  worklist's rendering and suppression defaults

Both remain closed sets, both remain code-changes-with-review. The benefit is that adding `Absence` later
does not require inventing a new body grammar if an existing one fits — and the evaluator selection stops
being inferred from the body's structure.

### 2.3 The minimum MVP addition

Do not add four types now. Add **one**, and only because §1.2 shows the differentiated capability cannot
be saved without it:

> **`Existence`** — asserts that a related object matching a bounded filter does, or does not, exist for
> the subject. Covers the Correlation and Absence shapes in their EXISTS / NOT EXISTS forms. Implemented
> as a FetchXML `link-entity` filter, so CM-3 holds unchanged.

`Transition` and `Trend` stay out of MVP. `Trend` needs baselines and therefore history, and cannot
honestly be promised in a bootcamp window.

---

## 3. The declaration — what a policy version must carry per shape

This is **not** a new entity. It is the field list a `sprk_policyversion` rule body needs so that adding a
signal is a row rather than a deployment, expressed against the schema already specced in
`mvp-technical-spec.md` §3.

### 3.1 Common to every shape

| Element | Home | Notes |
|---|---|---|
| policy code · version · effective window · author · approver | `sprk_policy` / `sprk_policyversion` | ✅ already specced §3.1–3.2 |
| `sprk_detectionshape` | `sprk_policyversion` | `[PROPOSED]` — §2.2 |
| `subject` | rule body | the object class the signal is about |
| `when` — scope filter | rule body | ✅ §3.3. Practice area, phase, firm, region |
| `then.signalType` · `then.severity` · `then.messageTemplate` · `then.proposedAction` | rule body | ✅ §3.3 |
| `then.evidenceRefs` | rule body | `[PROPOSED]` — **which** typed refs to attach, so §2.5's `sprk_evidencerefs` is populated by declaration rather than by evaluator-specific code |
| `suppression` — period, re-raise condition | rule body | `[PROPOSED]` — §1.4. Today suppression is implied by counting Decision Records; it should be declared |
| `requiredBindings` | `sprk_policyversion` | `[PROPOSED]` — object classes and modes this policy needs. A policy whose bindings are unsatisfied reports as **dormant** in admin rather than silently returning nothing (component model §8 freshness honesty, applied to policy) |

### 3.2 Per-shape body parameters

| Shape | Additional body fields |
|---|---|
| **Threshold** | `field` (predicate code) · `op` · `value` — ✅ as specced |
| **Temporal** | `dateField` or predicate · `window` · `businessDaysOnly` · `clockOwner` (legal-held vs requester-held — the distinction `design.md` already makes for SLA) |
| **Existence** (Correlation / Absence) | `exists` \| `notExists` · `relatedEntity` · `relationshipPath` · `filter` (bounded; **ids, never labels** — §11.4 is right to name taxonomy rows by GUID) · `window` relative to subject or to the related row |
| **Deviation** | `observedField` · `permittedSource` (allow-list ref, rate card, playbook version) |
| **Transition** | `fromState` · `toState` · `graceWindow` |
| **Trend** | `measure` · `baseline` (prior period \| peer percentile) · `deltaOp` · `deltaValue` · `minSampleSize` |

### 3.3 The corrected §11.4 predicate as an `Existence` body `[PROPOSED]`

Path B from §1.1. Two conjuncts: the commitment exists, and the budget has not moved since.

```jsonc
{
  "detectionShape": "Correlation",
  "type": "Existence",
  "subject": "sprk_matter",
  "when": { },                                  // all matters; scope later if needed
  "all": [
    {
      "exists": "sprk_communication",
      "path":   "sprk_regardingmatter",
      "filter": {
        "sprk_triagecategory": ["8b62dd84-1fbc-f111-aaaf-3833c5e9614d",   // Fee / rate change
                                "8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],  // Scope / budget change
        "sprk_receiveddate":  { ">=": "now-30d" },
        "sprk_reviewoutcome": { "<>": 100000003 }                          // not Dismiss
      },
      "bind": "commitment"                      // referenceable by the next clause
    },
    {
      "notExists": "sprk_budgetrevision",       // [VALIDATION NEEDED] — does any budget history exist?
      "path":   "sprk_matter",
      "filter": { "sprk_revisedon": { ">": "$commitment.sprk_receiveddate" } }
    }
  ],
  "then": {
    "signalType": 100000003,
    "severity":   100000001,
    "messageTemplate":
      "A commitment with financial consequence was raised on {commitment.sprk_receiveddate:d} and the budget has not been revised since.",
    "proposedAction": "send-budget-inquiry",
    "evidenceRefs": [
      { "kind": "communication", "ref": "$commitment" },
      { "kind": "snapshot",      "ref": "latest(sprk_spendsnapshot where sprk_matter = $subject)" }
    ]
  },
  "suppression": { "perSubjectPeriod": "P14D", "reRaiseOn": ["newCommitment", "budgetRevised"] }
}
```

Three things to note. The message now states **only what the predicate tests** — no claim about variance,
which the predicate does not compute. The snapshot is attached as **evidence** rather than as a condition,
so the user sees the budget position without the rule asserting a comparison it did not make. And
`sprk_budgetrevision` is `[VALIDATION NEEDED]`: if no budget-history record exists, D-1 must either
establish one or Path A applies.

**If Path A is chosen instead**, drop the second clause and the message becomes *"A commitment with
financial consequence was raised on {date} and has not been dispositioned."* Honest, shippable, and
explicitly not the §0 demo.

---

## 4. The action experience — what a worklist row has to do

`ontology-component-model.md` §6.1 defines a worklist correctly: membership computed by rule evaluation,
each row carrying the object, why it is there, the triggering facts, and the actions that resolve it.
This section is the concrete contract for that last clause, because it is the part that separates this
from alerting.

### 4.1 Five requirements for every row

1. **It resolves to an object**, never to a text string.
2. **Membership and rank are deterministic.** The model writes the sentence; it never selects the queue
   or the order. Otherwise there is no answer to *"why didn't this surface on Tuesday?"*
3. **It carries evidence with provenance and freshness**, separated by epistemic tier.
4. **It offers at least one action that changes something.** No action, no signal.
5. **Acting writes the Decision Record and closes the flag.** Dismissing writes one too, with a reason.

### 4.2 The row, expanded

Using the component model's own example (§4.13) for continuity.

```
⚠  Matter 4471 · Acme v. Northwind                                    Budget · POL-BUDGET v3

   A commitment with financial consequence was raised on 12 Mar and the
   budget has not been revised since.

   ── Evidence ───────────────────────────────────────────────────────────────
   Fact         $236,400 invoiced against a $200,000 budget (118%).
                6 invoices, INV-8830..8832 latest. Source: legaltracker,
                as of 4 hours ago.
   Observation  Email from R. Salazar (Northwind counsel), 12 Mar, classified
                Scope / budget change — "…three additional custodians…"
                confidence 0.94 · View thread ›
   Fact         No budget revision recorded since 12 Mar.

   ── Actions ────────────────────────────────────────────────────────────────
   [Send budget inquiry]   [Revise budget]   [Approve variance]
   [Dismiss — reason required]

   Flagged by Budget Variance Policy v3 · in force since 1 Feb 2026
```

**Send budget inquiry** → gate (`ConfirmationPolicyEngine`) → `sprk_servicerequest` outbound with an SLA
→ Decision Record written at the gate → flag closed with `Acted`. The reply arrives, the association
ladder links it to the Inquiry, and the Inquiry resolves with a **typed outcome** — write-off, budget
revision, scope approved, no action — which accumulates on the Engagement and becomes Report Card data.

**Dismiss** → reason captured → Decision Record written → flag closed with `Dismissed` → suppression
window opens. Dismissal reasons clustered over time are the input to §12.2 item 4 (*"already approved
offline" forty times is a missing fact, not a bad rule*).

### 4.3 Epistemic rendering rules

The Insights tiers already exist; these are the display consequences:

- A **Fact** is stated flatly. No hedging, no confidence shown.
- An **Observation** is marked as an interpretation, carries a confidence and a citation to the source
  passage, and is never phrased as though it were established.
- A stale or unbound source **shows as a gap**, never as a zero and never silently omitted (component
  model §8).
- The **policy code and version in force** appear on the row, not only in the Decision Record. That is
  what makes the row auditable at a glance and what §4.8's *"flagged by Budget Variance Policy v3"*
  moment refers to.

### 4.4 The restored criterion

Per §1.3, as `design.md` §7 criterion 10:

> **10.** A confirmed signal produces an Inquiry through the existing gate; the Inquiry carries an SLA;
> the reply resolves it with a typed outcome queryable per matter and per outside firm.

And the standing product metric, which §12.2 item 3 already describes as a zero-code Dataverse rollup:

> **Action rate per policy** — acted ÷ surfaced. Below roughly half, the policy is generating noise and
> is a candidate for retirement or re-thresholding.

---

## 5. Terminology — what is superseded

`ontology-component-model.md` §3 is authoritative. The review conversation that produced this document
used earlier terms, several of which are now **wrong** and should not be reintroduced by a future session
reading the strategy synopsis.

| Do not use | Use instead | Why |
|---|---|---|
| "binding" (source-side) | the Spaarke Connect entities — `sprk_externalconnection`, `sprk_externalfieldmapping` | Collides with ADR-039's Action↔consumer `Binding` |
| "binding registry" | `sprk_externalconnection` | Already designed |
| "connector manifest" | `sprk_externalfieldmapping` + its JSON mapping DSL | Already designed |
| "field classes" | **attribute ownership** | Component model §4.4 |
| "originate" (mode) | **system of record** | Industry term; §3 |
| "action ledger" | **Decision Record** | §4.13 |
| "observation" (as a lifecycle object) | **signal** / **flag** | `sprk_signal`; "Observation" is reserved for the Insights epistemic tier |
| "measure registry" | **fact predicate** — materialized as rollup + calculated column first, `ILiveFactResolver` past the rollup ceiling | CM-3 is closed; §4.15.1 |
| "three planes" | the three **engines** (Connection · Insights · Action), with Fabric IQ as a downstream analytical projection only | Component model §2 and decision 14 |

One term this document introduces that is **not** in §3 and needs a verdict: **detection shape**
(§2.2). If `sprk_detectionshape` is rejected, the eight shapes remain useful as a derivation method for
the catalog even without a stored field.

---

## 6. Open decisions to add to the register

| ID | Decision | Blocks |
|---|---|---|
| **CM-6** | **Path A or Path B on the cross-source predicate** (§1.1). A = single-source, honest message, does not pass §0. B = add the budget-unchanged conjunct, requires budget history | `design.md` §1 claim · criteria 2 and 9 · D-1 scope |
| **CM-7** | **Add `Existence` to the closed rule types?** (§1.2, §2.3). Without it the differentiated capability cannot be saved under §3.5 validation | The evaluator — **before it ships** |
| **CM-8** | **Separate `sprk_detectionshape` from `sprk_ruletype`?** (§2.2) | `sprk_policyversion` schema |
| **CM-9** | **`sprk_signal` resolution semantics** — dedupe alternate key, `sprk_resolutiontype`, resolution sweep (§1.4). Fold into the already-deferred `sprk_signal` shape decision rather than taking it twice | Generic `sprk_signal`; suppression; the scorecard |
| **CM-10** | **Does a budget-revision record exist or need creating?** (§3.3, `[VALIDATION NEEDED]`) | CM-6 Path B · D-1 fourth spike question |
| **CM-11** | **Restore the Inquiry action to MVP scope, or declare its absence?** (§1.3) | `design.md` §5 In · criterion 10 |

Also recommended, non-blocking: record the shape × binding-mode feasibility matrix (§1.5) in component
model §4.5 or §4.7, and make `sprk_factsnapshot` a mandatory element of the D-2 field list (§1.6a).

---

## 7. What this review did not find

Stated so the absence is informative rather than ambiguous.

The four documents are internally consistent on vocabulary, and the component model's §3 discipline — do
not invent vocabulary the repo already has, do not adopt Palantir terms unless they are terms of art — is
working. §4.15's *"third-party data becomes a normal Dataverse row of an entity we already have"* is the
clearest statement of the overlay architecture in any document, mine included. §4.15.1's materialized
fact (rollup + calculated column, zero C#) is a better answer than the measure-registry approach the
review conversation originally proposed. Decision 14's disqualification of Fabric IQ as authoritative —
on the grounds that its Dataverse path strips row-level security — settles a question this review would
otherwise have raised. Decision 6, *"build for departments; bind for firms,"* is a strategic insight that
was not in the strategy synopsis and should be promoted into it.

§4 of `design.md` — seven of eight defects in code shipped months earlier that had never executed its
happy path — is the finding that most justifies the Phase 0 discipline, and the reason the MVP is
correctly scoped smaller than it first appeared.
