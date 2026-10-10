# Schema draft — the five new tables

> ## ✅ STATUS: CREATED IN DEV — 2026-10-02
>
> All five tables exist in `spaarkedev1`, in solution **`OntologyPlatformSolution`** ("Ontology Platform
> Solution" v1.0.0.0) under publisher **Spaarke** (prefix `sprk`). **130 `sprk_` columns · 24 lookups ·
> 2 alternate keys.** Verified: **zero** logical names with an underscore between words.
>
> | Table | OTC | `sprk_` columns |
> |---|---|---|
> | `sprk_signal` | 11003 | 59 |
> | `sprk_decisionrecord` | 11002 | 22 |
> | `sprk_policy` | 11000 | 17 |
> | `sprk_policyversion` | 11001 | 17 |
> | `sprk_budgetrevision` | 10999 | 15 |
>
> Alternate keys (`EntityKeyIndexStatus: Pending` → activates asynchronously): `sprk_signal.sprk_dedupekey`
> (the idempotent-upsert key, CM-9) and `sprk_policy.sprk_policycode`.

> ## 🔴 The MCP `create_table` tool could NOT be used — and why that mattered
>
> The owner asked for creation **via MCP**. It is not safe for this, and the reason is worth keeping:
>
> 1. **`mcp__dataverse__create_table` has no publisher or solution parameter.** It derives the logical name from
>    a display name and uses the environment's **default** solution.
> 2. **This environment's default publisher is `new`**, not `sprk` — verified: `Default Solution` → *Default
>    Publisher for spaarkedev1* → prefix **`new`**; `Common Data Services Default Solution` → **`cr140`**. All
>    Spaarke work lives in ~30 named solutions under the **Spaarke** publisher. So the tool would have produced
>    **`new_signal`** or **`cr140_signal`**.
> 3. 🔴 **Dataverse logical names are immutable.** A wrong prefix is not a rename — it is **delete and recreate**,
>    after every code reference is already written against it.
> 4. `mcp__dataverse__invoke_api` only invokes **Custom APIs**, so it cannot POST metadata either.
>
> **The method that works** (and what to reuse): Dataverse Web API `POST /api/data/v9.2/EntityDefinitions` with
> an explicit **PascalCase `SchemaName`** (`sprk_BudgetRevision` → logical `sprk_budgetrevision`, which is exactly
> how you get no underscores between words), plus the **`MSCRM.SolutionUniqueName`** header to target the
> solution. Lookups are separate `POST RelationshipDefinitions` calls; alternate keys are
> `POST EntityDefinitions(LogicalName='x')/Keys`. Token via
> `az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`.
>
> **Three traps hit while doing it, recorded so the next person does not:**
>
> - 🔴 **Every `DateTimeAttributeMetadata` MUST carry `DateTimeBehavior`.** Omit it and the attribute is created
>   with behavior `None`, which breaks filtered-view generation — and then **every later relationship on that
>   entity fails** with the misleading `Failure in generation Filtered<Entity> for attribute <x>`. The error
>   names the datetime column, not the lookup you were creating.
> - The **solution-create POST must not carry `MSCRM.SolutionUniqueName`** (it validates the header against a
>   solution that does not exist yet → `404 not valid`).
> - **Money columns auto-create `_base` twins** (`sprk_newamount_base`). Those are the only logical names with a
>   second underscore, they are Dataverse's own, and they match the existing `sprk_budget.sprk_totalbudget_base`.
>
> **Still to do by hand** (deliberately not scripted): the **privileges** that make
> `sprk_decisionrecord` append-only (no Update, no Delete) and `sprk_policyversion` no-update-after-create —
> owner said permissions are managed as appropriate, and they are a security-role change, not metadata.

> ## Task 007 additions (2026-10-07) - see [`007-schema-verification.md`](007-schema-verification.md)
>
> Created live in `spaarkedev1` (OntologyPlatformSolution) by the recipe above: `sprk_signal.sprk_duedate` (DateOnly);
> `sprk_corerecordtype` (lookup to `sprk_recordtype_ref`) + `sprk_corerecordid` (text 100) on `sprk_signal` and `sprk_decisionrecord` (D-36);
> `sprk_decisionrecord.sprk_project` + `sprk_workassignment` (typed lineage lookups), `sprk_steps` / `sprk_followons` (JSON), `sprk_gatetier`;
> `sprk_policy.sprk_severity` (D-30), `sprk_shortname`, `sprk_worktype`, `sprk_retiredreason`, statuscode **Retired (100000000)** on Inactive;
> `sprk_policyversion.sprk_decisionplan` (JSON).
> **Dedupe key (D-13)**: `sprk_dedupekey` is `{policycode}|{subjecttype}|{subjectid}|{episode}`; MaxLength stays 400 (fits), alternate key still Active, no `sprk_episode` column.
> **D-35**: `sprk_matter` is optional (RequiredLevel None) on `sprk_signal` and `sprk_decisionrecord`; the "required" marks for it in sections 1-2 are superseded.
> **S-7**: `sprk_policyversion.sprk_inforceto` IS written at publish (D-14): null = currently in force; publishing a new version stamps the superseded one.

> ## Original draft notes
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

## 0. The three questions from the first draft — all answered

*(Rewritten 2026-10-02 after owner review. None of these is an open issue; two were my error.)*

**(a) The `sprk_signaltype` "collision" is a non-issue, and raising it was noise.**

- In Dataverse, **attribute logical names are unique per ENTITY, not globally.** `sprk_affinity.sprk_signaltype`
  therefore does **not** prevent a `sprk_signal.sprk_signaltype`. The only globally-unique names are entity
  logical names and **global choice-set** names.
- **And it is moot anyway: this draft has no `sprk_signaltype` column.** What kind of thing a Signal is comes
  from **which policy fired** (`sprk_policy` / `sprk_policycode`), not from a type column — so the concept has no
  home to collide over.
- `design.md` §5's warning was inherited from the earlier `sprk_spendsignal` analysis and I carried it forward
  without checking it. **Disregard it.** If a future column genuinely wants the name, it is legal; just make the
  choice **local** to the entity rather than global.

**(b) Use the full regarding resolver. My "trio instead of typed lookups" recommendation was wrong.**

The owner asked why we would not want the full resolver functionality, and the answer is that we should — my
framing was a **false dichotomy**. `PolymorphicResolverService.applyResolverFields` writes the **typed lookup AND
the denormalized trio** in one call; they are not alternatives. Verified: the service already handles
`sprk_regardingmatter`, `sprk_regardingcommunication`, `sprk_regardingproject`, `sprk_regardingservicerequest`,
`sprk_regardingworkassignment`, … **plus** `sprk_regardingrecordid` / `recordtype` / `recordname` / `recordnumber`
/ `recordurl`.

Three things the typed lookups buy that a text GUID cannot, and the first is the one I should have led with:

1. 🔴 **Security trim.** Dataverse trims **lookups** by the user's access to the target record. A denormalized
   **text GUID is not trimmed** — so a user could see a Signal about a record they cannot open. The Dataverse
   write-path architecture requires security to **fail closed** (WP-6); a text subject id breaks that.
2. **Subgrids and referential integrity.** "Signals on this To Do" as a subgrid needs a real lookup. So do
   cascade behaviours when the subject is deleted — which also retires the dangling-id problem I had proposed to
   patch with the sweep.
3. **Advanced Find, views, reporting, Power BI** all join on lookups, not on text ids. The Report Card is a
   reporting surface.

And the cost I worried about does not hold up: *"a new subject type becomes a schema change"* is true but
irrelevant — the **zero-deployment property was always about taxonomy rows and policy rules**, never about
adding a whole new entity to the ontology. Adding a subject type already means new predicates, new fact
resolution and new rule bodies; one more column is the least of it. **Also** it is CLAUDE.md §11 behaviour:
`PolymorphicResolverService` is the one utility the component audit found **genuinely canonical with zero
reimplementations** (§8.5), so reusing it is the default and declining it needed a much better reason than I had.

⚠️ **Note the two matter lookups, which are not redundant**: `sprk_regardingmatter` is *"the subject is this
matter"*; **`sprk_matter` is the always-populated grouping key** derived from the subject (D-3). For a Signal
about a communication on Matter X, the subject is the communication and the grouping matter is X. Both are needed,
and conflating them breaks the worklist's grouping.

**(c) Permissions — managed as appropriate, not a design question.** Owner, 2026-10-02. Recorded once so it is not
forgotten at create time: `sprk_decisionrecord` append-only, `sprk_policyversion` no-update-after-create. That is
all this needs to say.

---

## 1. `sprk_signal` — a condition that held

One row per unresolved condition. Replaces `sprk_spendsignal` (0 rows live, so free). **Sibling of
`InsightArtifact`, never a subtype** (D-7) — it cites artifacts as evidence.

### Identity + subject

| Column | Type | Req | Notes |
|---|---|---|---|
| `sprk_name` | Text (200) | ✅ | Primary name. The **short headline** — prototype finding 14: the list cannot show a full sentence |
| `sprk_signalnumber` | Autonumber | | `SIG-{SEQNUM:00000}` |
| **`sprk_regarding*` typed lookups** | Lookups | ✅ (one of) | **The subject, via `PolymorphicResolverService.applyResolverFields`** — the same ADR-024 pattern as `sprk_servicerequest` and `sprk_todo`: `sprk_regardingmatter` · `sprk_regardingcommunication` · `sprk_regardingproject` · `sprk_regardingservicerequest` · `sprk_regardingworkassignment` · `sprk_regardingtodo` · `sprk_regardingevent` · `sprk_regardinginvoice` · `sprk_regardingdocument`. 🔴 **Lookups, not a text id — this is what gives Dataverse security trim** (§0(b)) |
| `sprk_regardingrecordtype` · `recordid` · `recordname` · `recordnumber` · `recordurl` | Text | ✅ | The denormalized trio+, written by the **same** resolver call. Renders the list with no join. ⚠️ Store ids **lowercase, braces stripped** — audit §8.3 U1 found two regexes in circulation |
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
| **`sprk_decisionrecord`** | **Lookup → `sprk_decisionrecord`** | | **Nullable — but for a narrower reason than BR-1 originally gave** (reversed 2026-10-02, §6). **Every HUMAN resolution now writes a Decision Record and populates this**, Do lane included. It is null only for **system** closures, where nobody decided anything: `ConditionCleared` · `Superseded` · `PolicyRetired` |

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
| **`sprk_recordclass`** | **Choice** | ✅ | 🔴 **New 2026-10-02 — this is what replaces BR-1's carve-out.** `Judgement` (a choice under uncertainty — the Decide lane, and any Do resolution that sends mail, creates a follow-on or closes a record) · `Routine` (a bare completion or reschedule of one's own assigned work) · `Dismissal`. **Every resolution writes a row; the class is how the Report Card and the action-rate metric filter.** Filtering is reversible; absent data is not |
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
| **`sprk_countstowardsuppression`** | **Yes/No** | ✅ | **New 2026-10-02.** Default **Yes**. Set **No** for rules whose dismissals are a judgement about *our own resolution* rather than about the rule — the email→record match being the known case. 🔴 **This is what lets a segregated surface keep the SHARED mechanism** instead of being excluded from it (§6, and it supersedes `design.md` §5.1's exclusion) |

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

## 6. BR-1 — REVERSED. Every human resolution writes a Decision Record

*Rewritten 2026-10-02. The owner's challenge was correct and my rule was drawn at the wrong granularity.*

### First, the thing we already agreed on

**Tasks, To Dos, new matters and new documents ARE in the worklist.** That was never in question — it is the
**Do lane** (BR-2) plus the Know-promotion rules (BR-6), and `design.md` §5 In carries them. The Briefing's items
are *enhanced into* Work Items, not excluded. My earlier wording made this sound contested; it was not.

The narrow question was only whether a **completion** writes a `sprk_decisionrecord` row. I said no. **That was
wrong.**

### Why it was wrong

My distinction was *judgement vs work*: a Decide item is a choice under uncertainty, a Do item is just executing
assigned work, and the task's own history already records the latter. That holds for a **bare** "mark complete."

**It does not survive what resolving a Do item actually involves.** The owner's point: *"there will also be notes
and other add-ons such as send an email, create another task/to do, close a record."* Those are not
record-keeping — they are **acts with side effects, and sending mail is outward-facing.** A rule that says "Do
items write no record" would silently drop the record of an email sent to outside counsel because it happened to
be triggered from the Do lane rather than the Decide lane. **The lane is not a safe proxy for whether a judgement
occurred.**

### And I violated my own rule

Two messages earlier I wrote that volume *"argues for filtering a query, never for discarding data — if the
Decision Record is the asset, losing entries because there are many of them is backwards."* Then I used Report
Card dilution as a reason to not write the rows. **Same error, one message apart.** The fix is the one I had
already named: **write everything, type it, filter on read.**

### The resolution

| | What happens |
|---|---|
| **Every human resolution** — Decide or Do, complete / reschedule / reassign / send email / create follow-on / close record / dismiss | **Writes a `sprk_decisionrecord`** and populates `sprk_signal.sprk_decisionrecord` |
| **`sprk_recordclass`** distinguishes them | `Judgement` · `Routine` · `Dismissal` — so the Report Card shows judgements, the audit trail shows everything, and *action rate* can be computed either way |
| **System closures** — `ConditionCleared`, `Superseded`, `PolicyRetired` | **No** Decision Record, and `sprk_signal.sprk_decisionrecord` stays **null**. Nobody decided anything; the sweep closed it. **This is the only remaining reason the column is nullable** — and it is a real one, which is why D-2's nullable constraint still stands |

**One mechanism, uniformly applied**, which is the owner's stated principle: *"the underlying intelligence
tracking and decisions should be the same."*

### The same principle corrects §5.1 (email reconciliation)

`design.md` §5.1 kept email→record reconciliation as a separate surface — **right** — but justified it partly by
**excluding** it from the Signal/suppression mechanism, because a misresolution dismissal must not teach the
system to stop asking. **That exclusion is the same mistake in a different place.**

Better: keep the **surface** separate (bulk triage is a different interaction from a gated single decision, and
volume would bury the Decide lane) and keep the **mechanism shared**, with the semantic conflict handled by
**`sprk_policy.sprk_countstowardsuppression = No`** on that rule. The dismissals are still recorded, still
queryable, still clustered for *"forty misresolutions on this sender is a tokenizer bug"* — they simply do not
feed the three-dismissal suppression counter.

So the rule generalizes: **segregate on presentation and volume; never fork the mechanism.** A separate Task List
or Docket is fine for the same reason — a presentation choice over one Signal + Decision Record spine.

### What this costs, stated honestly

More rows, and a Report Card that must filter by `sprk_recordclass` instead of getting a pre-filtered table for
free. That is the correct trade: the filter is one clause, and it is reversible. Had we gone the other way, the
missing rows would only have surfaced the first time someone asked *"who closed this, and what else did they do
at the time?"* — and the answer would have been nowhere.
