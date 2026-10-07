# Spaarke Ontology Platform R1 — AI Implementation Specification

> **Status**: Ready for `/project-pipeline`
> **Created**: 2026-10-03 (by `/design-to-spec`)
> **Source**: [`design.md`](design.md) **rev 10** — design-complete, §8 has no open items, §10 holds 29
> settled decisions, six owner feedback rounds absorbed. `design.md` is preserved verbatim and remains the
> decision authority; **where this spec and the design disagree, the design wins on decisions and this spec
> wins on field-level and sequencing detail.**
> **Vocabulary**: [`notes/ontology-component-model.md`](notes/ontology-component-model.md) §3 is
> authoritative; §3.1 holds the Signal → Work Item chain.
> **Evidence base**: [`notes/mvp-technical-spec.md`](notes/mvp-technical-spec.md) (§10.7 = authoritative live
> row counts) · [`notes/schema-draft.md`](notes/schema-draft.md) (the five tables, field by field) ·
> [`notes/reuse-verification-2026-10-02.md`](notes/reuse-verification-2026-10-02.md) (C-1..C-27) ·
> [`notes/security-roles.md`](notes/security-roles.md) (privileges; §7 = second verification pass)
> **UI/UX contract**: the Console prototype's
> [`HANDOFF.md`](https://github.com/spaarke-dev/spaarke-prototype/blob/ae1cc9f/projects/2026-10-spaarke-console/HANDOFF.md)
> — `spaarke-dev/spaarke-prototype`, branch `feature/2026-10-spaarke-console`, path `projects/2026-10-spaarke-console/`,
> **pinned commit `ae1cc9f` (v4, findings 1–40)**; local copy
> `c:\code_files\spaarke-prototype-wt-spaarke-console\projects\2026-10-spaarke-console\HANDOFF.md`. Read its §0 ("what
> changed since v3") first. **The design contract, not the implementation.** Do not port prototype code. Before any
> UI task starts, check it against HANDOFF §1 (binding behaviour), §3 (data needs) and §4.1 (wizard host).

---

## 0. How to read this specification

**Four decisions were made during this `/design-to-spec` pass** — D-9..D-12, recorded in §9. They are not
re-litigations of settled design decisions; they are numbers and hosts the design never stated, plus one path
that CLAUDE.md §6.5 forbids an agent choosing silently. **They bind every requirement below.**

**Three schema deltas were found** by querying the built tables against the draft (group A). The five tables
exist; three columns that settled decisions require do not.

**Two things are the easiest in this project to lose into implementation.** Both are stated where they land
rather than only here: **Signal ownership** (FR-14) and the fact that **both Path B conjuncts are new code**
(FR-06, NFR-07).

---

## 1. Executive Summary

R1 builds the **intelligence layer from the Spaarke data model forward**: a declaration layer over Dataverse
that matches already-present data against the customer's own declared rules and **records what was decided**.
It does not build the connector that brings a third-party system's data in, and it does not parse LEDES —
invoice, budget and spend values arrive as computed metrics (decisions 16, 17).

The project earns its keep on **one predicate**, stated as the code actually tests it:

> A communication on this matter was classified as a **fee or scope change** within the window, **and** no
> **budget revision** was recorded in that same window.

Two conjuncts, two sources. An e-billing platform holds the budget and never sees the email; a mail system
sees the email and holds no budget. **Only a system holding both can evaluate the conjunction** — which is
what makes this pass the §0 differentiation test rather than merely sound like it does.

Everything else — the Policy tables, the Signal lifecycle, the Decision Record, the worklist row, the Do lane
— exists to make that sentence **computable, recordable and re-tunable without a deployment**.

**The chain**: a Policy declares a condition → evaluation writes a **Signal** (`sprk_signal`, the trigger) →
the Signal presents as a **Work Item** (the actionable unit) → acting **dispositions** it and writes a
**Decision Record**. A worklist row is a **matter grouping Work Items** (decision 28).

---

## 2. Scope

### 2.1 In scope

**The differentiated path** — these four together are the §0 claim; none is useful alone:

| Item | Shape |
|---|---|
| Cross-source predicate evaluator | code + `sprk_policy` / `sprk_policyversion` |
| `Existence` rule type | one addition to the closed rule-type set + its JSON Schema |
| `sprk_budgetrevision` | table **already created**; Path B's second conjunct reads `sprk_revisedon` |
| Inquiry action | one Action row + one Binding (criterion 10) |

**Mechanism** — works, carries no product risk: Decision Record · worklist · `sprk_memo` as signal source #2
· classifier guidance injection.

**Already-built signals folded in** — the Daily Briefing's items are **enhanced into Work Items** and
rendered through the *same* row component and row contract: the **Do lane** (three temporal policies) · Know →
narrative + Context pane · the first Know-promotion rule · retire *Critical Today* as a list · remove the
LLM-chosen "Top action".

**Landing contract only** — the pointer columns (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`)
and attribute ownership. Columns, not machinery. In R1 because it is free now and a data migration later, and
because **freshness is load-bearing in the UI** (prototype finding 3).

**Email→record reconciliation as a separate Console tab** — one `SectionRegistration` plus one aggregate Work
Item. The surface itself is already built.

**Component cleanup C-1..C-27** per the §5.0 scope rule (*fixed or scheduled, never merely listed*). Only
**C-1, C-3, C-4** gate the worklist row.

**Two repairs** this project already holds the diagnosis for: the space-bearing matter-number tokenizer and
the false association `reason` string.

### 2.2 Out of scope

| Item | Why |
|---|---|
| **Connection Engine** — connector, parser, sync engine, scheduler | Owner decision 2026-10-01. The carve-out is the landing contract (columns only) |
| **LEDES intake** | Not in R1 and not the near-term plan. Values arrive as computed metrics |
| **The Action *Engine*** (not Actions) | R1 needs an Action **row** plus the shipped ADR-039 dispatch spine. The management plane above it is R2, and R1's rows are its **input**, not a rewrite |
| Moving *Policy* into the Insights Engine (CM-1) | The Insights Engine is built and **consumed** by R1; only the `IPolicyEvaluator` contract waits for a second consumer |
| Authority | The human is the authority while every action is confirmed (decision 8) |
| MCP server | Deferred to `spaarke-mcp-server-r1` |
| Bitemporal / as-of | Immutable policy versions plus a mandatory `sprk_factsnapshot` buy the defensibility; design §8.2 states what that costs |
| Quantifying the commitment | Extracting "$45k" from prose is new extraction work that is sometimes wrong. **The join is the differentiator, not the arithmetic** |
| Per-entity fact or signal tables | `ILiveFactResolver` + rollup/calculated columns supply facts (D-8). `sprk_spendsnapshot` is one materialization, not a pattern |

### 2.3 Affected areas

| Path | What |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Signals/` | **New** — the evaluator, the Signal writer, the lifecycle/closure pass |
| `src/server/api/Sprk.Bff.Api/Services/Insights/LiveFacts/` | **Extend** — new predicates on `ILiveFactResolver` (a closed `switch`; see NFR-07) |
| `src/server/api/Sprk.Bff.Api/Services/Communications/` | `RuleGatedAssessedConsumer` — one line before the branch to write the Decision Record; the classification event hook |
| `src/server/api/Sprk.Bff.Api/Services/Ai/` → `LookupChoicesResolver` | **Extend** — emit `name — guidance` |
| `src/client/shared/Spaarke.UI.Components/src/` | The genuinely-new status/severity badge (C-4); `MetricCard` disambiguation (C-3) |
| `src/client/shared/Spaarke.DailyBriefing.Components/` | C-1 — delete the dead hooks; also the row component's ancestry |
| `src/solutions/SpaarkeAi/src/` | Widget registration; the two stale comments (C-1) |
| Dataverse (`spaarkedev1`) | Three column adds (group A); policy + taxonomy rows; §8.1 seed data |

---

## 3. Requirements

### 3.1 Functional requirements

#### Group A — Schema completion (deltas verified by query, 2026-10-03)

The five tables exist (`sprk_signal` 59 cols · `sprk_decisionrecord` 22 · `sprk_policy` 17 ·
`sprk_policyversion` 17 · `sprk_budgetrevision` 15 — 130 `sprk_` columns, 24 lookups, both alternate keys
**Active**, all five at **0 rows**). Three columns that settled decisions require are **missing**.

> **Apply via the recorded Web API recipe** ([`notes/schema-draft.md`](notes/schema-draft.md) banner): an
> explicit **PascalCase `SchemaName`**, the **`MSCRM.SolutionUniqueName`** header, and **`DateTimeBehavior`**
> on every datetime. **Not** MCP `create_table` — it has no publisher parameter, this environment's default
> publisher is `new`, and **logical names are immutable**, so a wrong prefix is delete-and-recreate after the
> code is already written against it.

- **FR-01**: Add **`sprk_decisionrecord.sprk_action`** — nullable lookup (or text).
  *Acceptance*: exists in `OntologyPlatformSolution` under prefix `sprk` with no underscore between words; is
  **nullable**; a Decision Record written on a **deny** path saves with it null.
  *Why*: one of **D-2's four binding constraints** — a deny path has no action. Verified absent.
- **FR-02**: Add to **`sprk_servicerequest`**: a **direction discriminator** (`Inbound` / `Outbound`) and
  **`sprk_disposition`** (write-off · budget revised · scope approved · no action).
  *Acceptance*: both exist; the Inquiry created by FR-26 sets direction `Outbound`; `sprk_disposition` is
  queryable **per matter and per outside firm**.
  *Why*: **CM-5** and **success criterion 10** both depend on them. Verified absent across all 17 `sprk_`
  columns.
- **FR-03** *(amended 2026-10-07, D-13)*: Compose **`sprk_dedupekey`** as
  `{policycode}|{regardingrecordtype}|{regardingrecordid}|{episode}`.
  *Acceptance*: the writer reads `sprk_regardingrecordtype` + `sprk_regardingrecordid` (there is **no**
  `sprk_subjecttype` on `sprk_signal` — only on `sprk_policy`); ids stored **lowercase, braces stripped**
  (audit §8.3 U1 found two divergent regexes in circulation); **within one episode** re-evaluation **upserts**
  rather than duplicating. The **episode** is a whole number starting at **1**, incremented when the subject
  re-raises after its previous Signal for the same policy was resolved (Acted, Dismissed, ConditionCleared,
  Superseded or PolicyRetired). A re-raise happens only when the subject is **not suppressed** (FR-17) **and**:
  *Decide lane* — if the previous episode ended in a dismissal, the policy's **quiet window** (FR-17a) has passed
  since it; *Do lane* — the subject's date has **changed since the Signal was resolved**. A Signal closed as
  **Superseded** re-raises as a new episode under the new version whenever the new version still holds (FR-16).
  The existing dev-seed Signals are re-keyed as episode 1. **The evaluator implements the episode before it writes
  its first Signal** (task 031).
  *Why*: the key is a unique alternate key and the built writer keeps a Resolved row Resolved, so without an
  episode a dismissed, acted, superseded or cleared Signal can never come back — and Path B's subject is the
  matter, so one dismissal would mute it on that matter for good (`notes/v4-prototype-vs-solution.md` #13, #40).
  *Amended 2026-10-07, D-34/D-35*: the key stays **per subject**; nothing in it is matter-specific. "Not suppressed"
  in the re-raise rule is evaluated per **(policy, core record)** (FR-17 as amended); for a D-35 item with no core
  record it is per (policy, subject). *(Amended 2026-10-07, D-36/D-39: "not suppressed" is per (policy, core record) for every
  core type, and per (policy, item) for an item with no core record — decided.)*
- **FR-04**: Re-verify privileges after the FR-01/FR-02 column adds.
  *Acceptance*: `prvWritesprk_DecisionRecord` and `prvDeletesprk_DecisionRecord` remain **absent** from all
  three Spaarke Ontology roles, and the union check still shows only platform/admin roles holding them
  (`notes/security-roles.md` §7.4).
  *Why*: append-only is a property of the **union** of a user's roles, and a column add touches the table the
  guarantee protects.

#### Group B — Policy and rule bodies

- **FR-05**: Add **`Existence`** to the closed rule-type set, with its JSON Schema.
  *Acceptance*: `sprk_policyversion.sprk_ruletype` accepts `Threshold` · `Switch` · `Existence` and nothing
  else; adding a fourth remains a code change with review (component model decision 7).
  *Why*: without it the differentiated capability is **literally unsavable** — validation refuses an invalid
  body, and the MVP allowed only `Threshold` and `Switch` (CM-7).
  *Amended 2026-10-07, D-16*: R1 **compiles `Existence` and `Threshold`** (FR-46). `Switch` stays in the closed set
  but is still refused at validation as `RuleTypeUnsupported` — deferred after R1, as is the inquiry-SLA rule (D-20).
- **FR-06**: Implement the `Existence` body as **two independent ANDed clauses over a fixed window, with no
  cross-clause variable passing**:
  ```
  all: [ exists    sprk_communication  … sprk_receiveddate >= now-30d … ,
         notExists sprk_budgetrevision … sprk_revisedon    >= now-30d ]
  ```
  *Acceptance*: the body compiles to **one** Dataverse filter (CM-3); the `exists` half uses an INNER
  `link-entity` + `LinkCriteria`; the `notExists` half uses an outer `link-entity` + null test; **no clause
  binds a variable another clause reads**.
  *Why*: the reviewed body bound `"commitment"` in clause 1 and read `$commitment.sprk_receiveddate` in
  clause 2 — not one filter, so it breaks CM-3 and reintroduces variable passing, the very thing that made
  predicates preferable to node graphs (design §8.0.1a).
  🔴 **The `notExists` half has no prior art anywhere in the repo.** `EXISTS` does
  (`DataversePrecedentBoard.cs:182-190`); every `ConditionOperator.Null` in the repo filters a column on the
  *primary* entity and every `LeftOuter` join enriches rather than anti-joins. **Treat it as new code with no
  template and prove it on real data before anything is built on top.**
- **FR-07**: Reject `sprk_budget.modifiedon` as a substitute for `sprk_budgetrevision`.
  *Acceptance*: no predicate anywhere reads `sprk_budget.modifiedon` as evidence of a revision.
  *Why*: any unrelated field edit bumps it, so a stray edit reads as "the budget was revised" and
  **suppresses a true signal** — a false negative, the direction decision 13 rules against.
- **FR-08**: Rule-body validation **refuses to save** an invalid body.
  *Acceptance*: a body failing its rule-type schema is rejected at save with a field-level error; no
  `sprk_policyversion` row can exist with an unparseable `sprk_rulebody`.
- **FR-09**: Copy `CommunicationRuleGate` scope semantics **verbatim**.
  *Acceptance*: blank `sprk_tenant` matches all tenants (`:175-181`); empty `sprk_matter` matches all matters
  (`:183-188`); ordering is `OrderBy(sprk_priority ?? 500).ThenBy(Id)` (`:129-133`); a rule-store read failure
  **fails closed** with `Deny(…, "rule-store-read-failed")` (`:108-126`).
  *Why*: these semantics are already settled and tested in a shipped gate. Re-deriving them is how they drift.
- **FR-10**: Policy authoring defaults: `sprk_enabled` defaults **No**; `sprk_priority` defaults **500**,
  lowest wins.
  *Acceptance*: a newly created policy row is inert until explicitly enabled.
  *Why*: the taxonomy rows created on 2026-09-29 defaulted to disabled and were invisible to the classifier
  until someone noticed — the same trap, and a new rule going live before review is the worse direction.

#### Group C — The evaluator and the Signal lifecycle (D-12)

> **The shape of this group follows from one fact**: Path B's window is relative to *now*, so **the
> predicate's truth value changes even when nobody writes anything.** Of four state transitions, two have
> **no event to hook** — *death by time* (the communication ages out of the window) and *birth by time* (an
> old budget revision ages out, re-truthing the NOT-EXISTS half). Event-driven evaluation alone would
> therefore never fire `ConditionCleared`, which is design §9's "the worklist rots" risk verbatim.

- **FR-11**: Build **one** re-evaluating component, not an evaluator plus a separate sweep.
  *Acceptance*: a single service both (a) upserts Signals whose predicate now holds and (b) closes Signals
  whose predicate no longer holds, in one pass, stamping `sprk_lastevaluated` on every Signal it touches.
  *Why*: because *birth by time* exists, the nightly pass cannot be closure-only. One component deciding both
  directions is also the only way the two cannot disagree and miscount suppression. Satisfies CLAUDE.md §11
  (one component that works well over two that partially overlap).
- **FR-12**: Host it as an **ADR-036 `IScheduledJob`, nightly**.
  *Acceptance*: registered alongside the six existing scheduled jobs; `SecureRecordIsolationCensusJob` is the
  nearest shape to copy. **No new hand-rolled timer `BackgroundService`** (ADR-052).
- **FR-13**: Add **two ADR-004 `IJobHandler` event triggers** that run the same evaluator for the affected
  matter only:
  (a) **communication classification** — so a Decide-lane Work Item appears within about a minute of the
  email being classified, not the next morning;
  (b) **`sprk_budgetrevision` create** — so revising a budget clears the Work Item immediately.
  *Acceptance*: an email classified `Fee / rate change` at 09:14 produces a Work Item the same morning; a
  budget revision recorded at 14:00 closes it as `ConditionCleared` without waiting for the nightly pass.
  *Why (b) specifically*: without it, the Work Item keeps asserting *"no budget revision was recorded"* for up
  to 24 hours after one was — a sentence that is no longer true, in the surface the product's credibility
  rests on. That is §0.3 in a new costume, and prototype finding 3's "absence clause over a stale source is a
  false negative wearing a confident face."
- **FR-14**: 🔴 **Set the Signal's owner (or owning business unit) from its grouping matter at creation.**
  *Acceptance*: every `sprk_signal` row created by the evaluator has an owner derived from
  `sprk_matter`, never the service identity by default.
  *Why*: a Signal is secured by **its own** owner, not by the matter it points at, while `sprk_sentence` can
  carry matter detail — so a service-owned Signal at depth-4 read could expose a matter the reader cannot
  open, with the matter lookup rendering **blank** while the sentence tells them anyway. Live across **6
  business units**. `Spaarke Ontology Service` already holds **`Assign`**, so this needs no privilege change.
  One line now; a re-own of every row later, and invisible until then because nothing errors.
  *Amended 2026-10-07 (D-31, D-33)*: ownership is decided by the uac-r2 `RecordOwnershipResolver` with the grouping
  matter **and the subject** as parents. **Secure** answer (a parent is owned in the `Secure Record` business unit) →
  `ownerid` = the `Secure Record Owners` team, set **in the create** (owning BU derives from the team). **Otherwise**
  → today's path (owner = writer, owning BU = the matter's BU). A **To Do with no matter** (D-31) takes the **To Do
  owner's business unit** — a second, non-matter ownership path. A resolver **refusal** skips, logs and counts the
  subject.
  *Amended 2026-10-07, D-34/D-35*: read "grouping matter" throughout as the item's **core record** — a **matter or a project** — derived by uac-r2's `CoreAncestorResolver` (FR-26 stamps; both are CORE; a matter does not inherit from a project). The Signal carries it in
  `sprk_matter` **or** the new `sprk_project` (task 007); the resolver's parents are the core record and the subject;
  the non-secure owning BU is the **core record's** BU. **D-35 replaces D-31's ownership wording**: an item with **no
  matter and no project** gets a Signal **owned by the To Do's owner and visible to that owner only** (shown in their
  Do lane under a **"Not filed"** group); both core-record columns are null for this case only.
  *Amended 2026-10-07, D-36/D-37*: the core record is **any** of the access-control core types —
  `CoreAncestorResolver.CoreRecordEntities`: matter, project, work assignment, service request today — each its own
  grouping, security and suppression grain. A work-assignment or service-request subject is **its own** core record.
  When an item has more than one core stamp, its **direct filed-under (regarding) core record** wins, then matter over
  project (D-37). The Signal carries the core record in a **generic** pair, `sprk_corerecordtype` (lookup to the
  `sprk_recordtype_ref` catalog) + `sprk_corerecordid` (task 007), which every piece of logic reads; adding a core type
  costs at most a catalog row (plus, for a new **secure-root** type, one typed lookup and a uac-r2 lineage entry). This
  supersedes the D-34 `sprk_project` column on the Signal.
- **FR-14a** *(added 2026-10-07, D-15; **amended 2026-10-07, D-33 — the Restricted/Limited skip is withdrawn**)*:
  ~~The evaluator **skips Restricted and Limited matters**.~~ **Signals and Decision Records on Secure matters are
  secure children** (D-33); Restricted and Limited matters get Signals normally.
  *Amended acceptance (D-33)*: `sprk_signal` and `sprk_decisionrecord` are registered in `SecureChildLineage.cs` and
  `config/secure-record-owner-role.json` (reviewed by the access-control project, each with a recorded live
  refusal); a Signal or Decision Record on a Secure matter is owned by the `Secure Record Owners` team at create and
  receives the matter's sharees within the 2-minute secure sync; unsecuring the matter releases it to the BU default
  team; the Decision Record stays append-only (no human role holds Write/Delete, so mirrored rights are inert). The
  **only** skip is the narrow case D-33 names: a To Do with **no matter** filed under a **Secure** project (its Decision
  Record would have no lineage) — skipped, logged and counted. The load the nightly pass adds to the 2-minute secure
  sync is measured. *Why*: Restricted/Limited exclude **external contacts** only (ADR-003); the staff wall is the
  **Secure** flag (`sprk_issecure`), which the D-15 skip missed (`notes/secure-signals-scoping.md` §1).
  *Amended 2026-10-07, D-34*: "Secure matter" reads **Secure core record** — a Secure **project** (`sprk_issecure` is
  on all three secure-root types: project, matter, work assignment; spaarkedev1 holds 2 Secure projects, 0 Secure
  matters, read-only 2026-10-07) protects its Signals and Decision Records the same way, through a `sprk_project`
  lineage entry on both tables. D-34 gives a project-filed To Do a core record, which removes the premise of the narrow
  skip above (its Decision Record now has a lineage lookup); whether the skip is therefore retired is **not decided**
  (§11.1 O-24) and it stays until the owner says otherwise.
  *Amended 2026-10-07, D-38*: **the narrow skip is dropped — no skips anywhere.** Every item is protected through its
  core record (secure child where the core record is Secure) or is owner-only (D-35). *Amended 2026-10-07, D-36*: Secure
  **work assignments** are covered like Secure matters and projects (spaarkedev1: 0 Secure work assignments, read-only
  2026-10-07). **Service requests cannot be Secure** (`sprk_servicerequest` has no `sprk_issecure`; uac-r2's
  `SecureChildLineage` excludes them), so service-request-grouped items stay on the non-secure path.
  *Original D-15 text, superseded:*
  *Acceptance*: no `sprk_signal` row is written for a subject whose grouping matter's Access Permission is
  Restricted or Limited — the subject is skipped, logged and counted (never an error and never a silent drop);
  a Signal that already exists on such a matter is not refreshed. Secured matters have **no** Signals in R1, so
  extending the access-control project's **secure-record mirror** to `sprk_signal` + `sprk_decisionrecord` later
  needs **no data migration** (follow-up issue, task 079).
  *Why*: a Signal is owned by the writer with the matter's business unit and read at Parent: Child BU depth, and
  `sprk_signal` is not in `SecureChildLineage` — so every Console User in the BU tree could read the sentence and
  evidence of a Signal on a Restricted matter they cannot open (`notes/v4-prototype-vs-solution.md` #17).
- **FR-15**: Cadence is derived from **`sprk_lane`** — no new column.
  *Acceptance*: `Decide` policies are evaluated on both the event triggers and the nightly pass; `Do`
  policies are evaluated **nightly only**.
  *Why*: the Do lane's rules are `Temporal` over Spaarke-held data — "overdue" is a date comparison that
  changes at midnight, so nightly is correct rather than a compromise. `sprk_lane` already exists, so a
  per-policy trigger column would be new surface with no cost-of-doing-nothing to cite (CLAUDE.md §11).
- **FR-16**: Closure semantics. A still-open Signal whose predicate no longer holds closes with
  `sprk_resolutiontype` = `ConditionCleared`; superseded by a newer policy version → `Superseded`; policy
  disabled or deleted → `PolicyRetired`.
  *Acceptance*: **a Signal whose communication has simply aged out of the window closes as
  `ConditionCleared`** — the condition genuinely no longer holds; `sprk_signal.sprk_decisionrecord` stays
  **null** for all three, because nobody decided anything.
  *Amended 2026-10-07 (D-13, D-14; Retire per the owner's 2026-10-05 admin decision and reconciliation M-5)*:
  **Superseded** — publishing a new version closes the open Signals citing the old version as `Superseded` and
  stamps the old version's `sprk_inforceto` (D-14); where the **new** version still holds for that subject, the
  evaluator raises a **new episode** citing the new version (FR-03). **Off** (`sprk_enabled = No`): open Signals
  close as `PolicyRetired` at the next pass; switching it **On** again re-raises the subjects that still hold as new
  episodes (*amended 2026-10-07, D-32*). **Retire** (admin, reason required; `statuscode` Retired +
  `sprk_retiredreason`): open Signals close as `PolicyRetired` **immediately**. None of the three writes a Decision
  Record; Retire and Off are audited admin changes. **The evaluator never re-closes a Signal that is already
  Resolved** — a task completed or a budget revised *through a decision* is closed as `Acted` by the commit route
  (FR-51), and the next pass must not close it again as `ConditionCleared` (`notes/v4-prototype-vs-solution.md` #15).
- **FR-17**: Suppression counts dismissals per **(policy, matter)** and expires after **30 days** (D-11).
  *Acceptance*: three dismissals of any Signal for the same policy on the same matter set
  `sprk_suppresseduntil` to now + 30 days; a dismissal on a policy with
  `sprk_countstowardsuppression = No` is **recorded and queryable but does not increment the counter**; an
  **auto-closure never counts** as a human dismissal.
  *Why the grain*: `sprk_dedupekey` is per *subject* but criterion 4 says per *matter*, and worklist rows
  **are** matters — so matter grain suppresses what the user actually dismissed three times. Per-subject grain
  would never suppress three dismissals of three different communications on one matter, which is the exact
  fatigue case criterion 4 exists to prevent.
  *Why the expiry*: a permanent mute is indistinguishable from a broken rule, and the condition may still be
  true and by then more serious.
  *Amended 2026-10-07, D-34*: the grain is **(policy, core record)** — matter **or project** (D-11 as amended by
  D-34): three dismissals for one policy on one core record suppress that pair. A D-35 item (no core record) counts per
  (policy, subject), since it has no shared record to group under (**not decided** beyond D-35's owner-only rule:
  §11.1 O-25). *(Amended 2026-10-07, D-36/D-39 — decided: the grain is (policy, core-record type, core-record id) for
  every core type, read from the generic columns; an item with no core record is counted per **(policy, item)**.)*
- **FR-17a** *(added 2026-10-07, D-13)*: **Quiet window.** After a Decide-lane Signal is dismissed, the same
  subject is **not re-raised** under that policy until the policy's quiet window has passed.
  *Acceptance*: the window is a knob **in the rule body** (decision 14, knobs on the row; A-1), defaulting to
  **14 days** when the body does not declare it; it applies after a 1st or 2nd dismissal (the 3rd triggers FR-17's
  30-day suppression, which takes precedence while it lasts); changing the knob takes effect with no deployment.
  The Do lane uses the date-change rule of FR-03 instead.
  *Why*: without it a dismissed Decide item whose predicate still holds comes straight back on the next run — the
  dismissal would be meaningless and the user would dismiss it again, feeding suppression with noise.

#### Group D — The Decision Record

- **FR-18**: **Every human resolution writes a `sprk_decisionrecord`** and populates
  `sprk_signal.sprk_decisionrecord` — Decide lane and Do lane alike, including complete, reschedule,
  reassign, send email, create follow-on, close record and dismiss (BR-1 **as reversed**).
  *Acceptance*: no human-resolved Signal has a null `sprk_decisionrecord`; the only null cases are the three
  system closures in FR-16.
  *Why*: the lane is **not** a safe proxy for whether a judgement occurred. Resolving a Do item is rarely a
  bare "mark complete" — it also sends mail, creates follow-ons and closes records, and one of those is
  outward-facing. The earlier rule would have silently dropped the record of an email sent to outside counsel
  because it was triggered from the Do lane.
  *Amended 2026-10-07, D-17*: every human resolution goes through **one BFF commit route** (FR-51), and **one
  review writes exactly one record** that lists the **actual outcome** of every action the rule offered (taken or
  skipped, with the values used) and every Next step created. Cancel, close and browsing away write nothing. Once
  the BFF closes Signals, **Spaarke Console User loses `prvWritesprk_Signal`**, so no client path can close a
  Signal without a record — the "no null `sprk_decisionrecord`" acceptance above becomes privilege-enforced rather
  than a convention (`notes/v4-prototype-vs-solution.md` #16).
  *Amended 2026-10-07, D-34/D-35*: the record's **subject is the item's core record** — `sprk_matter` **or** the new
  `sprk_decisionrecord.sprk_project` (task 007), the same record its Signal groups under; for a D-35 item both are null.
  *Amended 2026-10-07, D-36/D-39*: the subject is carried in the generic `sprk_corerecordtype` + `sprk_corerecordid`
  pair for every core type, plus the typed lineage lookup (`sprk_matter`, `sprk_project` or `sprk_workassignment`)
  when the core type is a secure root — uac-r2's lineage keys on typed lookups (task 007). For an item with **no core
  record**, the record is **owned by the item's owner** and visible like its Signal (D-39).
- **FR-19**: Type each record with **`sprk_recordclass`** — `Judgement` · `Routine` · `Dismissal`.
  *Acceptance*: the Report Card and the action-rate metric **filter on read**; a bare completion or reschedule
  of one's own assigned work is `Routine`; any Do resolution that sends mail, creates a follow-on or closes a
  record is `Judgement`.
  *Why*: write everything, type it, filter on read. Filtering is reversible; absent data is not.
- **FR-20**: `sprk_factsnapshot` is **mandatory** on the Decision Record, and separately on the Signal.
  *Acceptance*: neither row saves without it. On the Signal it holds the fact values at **detection** time; on
  the record, at **decision** time.
  *Why*: the two answer different questions, **and a Signal that auto-closes as `ConditionCleared` never
  produces a Decision Record at all** — so without it on the Signal the detection-time facts are lost
  entirely. This is the compensating control for ruling out bitemporality; dropping it to "nice to have"
  reopens a gap that is invisible until the first time someone audits an old decision.
- **FR-21**: Append-only is enforced **by privilege**, not by column or plugin.
  *Acceptance*: as a non-admin user holding only the Spaarke roles, an update to a `sprk_decisionrecord` row
  is **refused**; the same for `sprk_policyversion` after create. Narrow auditing on both tables gives
  detection where prevention is impossible.
  *Why*: ADR-002 forbids plugins, and System Administrator always holds every privilege — so this is the
  honest limit, recorded rather than overclaimed.
  *Amended 2026-10-07, D-14*: **Spaarke Ontology Administrator is granted Write on `sprk_policyversion`** so that
  publishing a new version can stamp the old version's `sprk_inforceto`. The policy-version guarantee therefore
  becomes **"the rule body is immutable after publish, enforced in the BFF publish service"** — code, not privilege.
  The acceptance above now reads: an update to a `sprk_decisionrecord` row is refused for every Spaarke role
  (unchanged); an update to a published version's `sprk_rulebody`, `sprk_ruletype`, `sprk_messagetemplate` or
  `sprk_decisionplan` **through the BFF** is refused; and the union-of-roles check records exactly which roles hold
  `prvWritesprk_PolicyVersion` (Spaarke Ontology Administrator plus platform/admin roles), with narrow auditing on
  the table as detection for writes made outside the BFF.
- **FR-22**: The relationship is **Decision Record 1 → N Signals**, with the FK on the Signal.
  *Acceptance*: there is **no** signal lookup on `sprk_decisionrecord`; "Signals closed by this decision"
  renders as a subgrid from the Signal side.
  *Why*: one decision can resolve several Signals (prototype finding 4), and this direction is easy to build
  backwards.
- **FR-23**: Write the record from `RuleGatedAssessedConsumer`, which **already holds the decision object on
  both the authorize and deny paths** — one line before the branch. **The gate itself is not touched.**
  *Acceptance*: every authorize **and** every deny writes a record; the deny path is queryable per matter. *(Amended
  2026-10-07, D-34: per core record — the communication's matter or project from `CoreAncestorResolver`.)*
  *Why*: `CommunicationRuleDecision` already carries the full decision and is currently written to `ILogger`
  and lost.

#### Group E — The worklist surface

- **FR-24**: Membership, columns and actions come from a **`sprk_gridconfiguration`** row with no code change.
  *Acceptance*: changing which Signals appear, and in what order, is a configuration edit; membership is
  computed by **rule evaluation**, never by a user-authored filter.
  *Amended 2026-10-07, D-15*: the worklist reads Signals **only through the BFF Signal read route** (FR-54). The
  route executes the membership-and-order FetchXML from the `sprk_gridconfiguration` row, **removes every row whose
  matter the caller cannot read**, and returns **only the caller's own work** in the Do lane. The widget renders a
  card list from the route's response, not `<DataGrid>`. Actions come from the policy version's **decision plan**
  (FR-49), never from the grid configuration.
  *Amended 2026-10-07, D-33*: the read route and Do-lane scoping stand. On a Secure matter the caller's read of the
  matter comes through its share, and the Signal itself reaches them through the mirrored share, so the two checks
  agree. A **matterless To Do** Signal (D-31) has no matter to check: what the route checks instead is open (§11.1 O-21).
  *Amended 2026-10-07, D-34/D-35*: the route checks the caller can read each row's **core record** (matter or project);
  a D-35 row (no core record) is returned **only to the To Do's owner**, in their Do lane under **"Not filed"**. The
  grid configuration selects both core-record columns and orders a core record's items together.
  *Amended 2026-10-07, D-36*: "core record" covers all four core types; the access check, grouping and card labels are
  driven by `sprk_corerecordtype` (the catalog row's logical name, display-name and number fields) and
  `sprk_corerecordid`, with **no per-type code** in the route or the worklist.
- **FR-25**: Build **one** row component rendering **every** signal shape, with data-driven variants.
  *Acceptance*: the three signal shapes (threshold, cross-source, SLA) and the Do-lane items all render
  through the same component; **a second row component is a design failure, not a feature.**
  *Why*: prototype finding 10 is the evidence this is achievable — one component carried all three shapes once
  five things were present in the data. Finding 9 is why it is new UI at all: the row contract needs expandable
  evidence tiers, a *Why this fired* disclosure, outcome cards and a gate host, none of which a column renderer
  provides.
- **FR-26**: The row satisfies all **five row-contract requirements**, each as a testable criterion:
  1. it **resolves to an object**, never a text string;
  2. **membership and rank are deterministic** — the model writes the sentence and never selects the queue or
     the order;
  3. it carries **evidence with provenance and freshness**, separated by epistemic tier (Fact stated flatly,
     Observation hedged with a confidence and a citation);
  4. it offers **at least one action that changes something** — no action, no signal;
  5. **acting writes the Decision Record and closes the Signal**; dismissing writes one too, with a reason.
- **FR-27**: Reuse is **binding**, not advisory (design §1.3). Use `SprkModal` + its six presets ·
  `<DataGrid configId=… />` · `WorkspaceWidgetRegistry` + `PaneEventBus` · `Spaarke.DailyBriefing.Components`
  for the row's ancestry · `CalendarSection`'s shared-lib-widget-plus-thin-shim pattern.
  *Acceptance*: **three named components are extended rather than rebuilt** —
  **`WorkspaceShell/MetricCard` + `MetricCardRow`** (already clickable, `role="button"`) for count-filter
  cards, **not** `StatTiles` which has no `onClick` at all (`StatTiles.tsx:95-111`);
  **`DocumentRowMenu`** (`DocumentRowMenu.tsx:150-208`) for the row's ⋮ menu, **not** a fourth hand-rolled
  `<Menu>` — three bespoke ones already exist; **`OutcomeCard`** (`SprkChat/OutcomeCard.tsx:93-367`) extended
  with the Signal statuses for the post-action surface.
  *Why*: building past a named existing component is a CLAUDE.md §11 violation with no cost-of-doing-nothing
  to cite.
- **FR-28**: Anything genuinely new **lands in a shared library**, not in the Console app.
  *Acceptance*: the one legitimately-new component — a **generic status/severity badge** (every badge in the
  shared libraries is domain-specific: `CitationBadge`, `PinnedMemoryProvenanceBadge`, `ChannelBadge`) —
  ships in `@spaarke/ui-components`; it is Fluent v9, **tokens only**, correct in both light and dark, with no
  hardcoded colors.
  *Amended 2026-10-07, D-24 — the Console kit is named, and reuse comes first.* v4's kit (HANDOFF §1.3) maps as
  follows. **Reuse, no new component**: *Disclosure* → Fluent `Accordion`; *ObjectLink* → Fluent `Link`;
  *DiscardDialog* → `ConfirmModal` (custom labels, nested in the wizard); *BrowseNav* → `SprkModal`'s `nav`;
  *StatusChip* → task 012's `StatusBadge` (plus a `success` tone, `notes/v4-prototype-vs-solution.md` #22);
  *CountFilters* → **extend** `WorkspaceShell/MetricCard` + `MetricCardRow` with `selected`, `note` and `progress`
  (and a non-square layout option). **New shared components, only these**: `EvidenceLine`, `StatusBar` (a thin
  composition on Fluent `MessageBar`), `RecordRow`, `AggregateCard`, and the one row component `MatterCard` +
  `IssueLine` (FR-25). Each new one lands in `@spaarke/ui-components` under the same token/dark-mode rule and
  carries its own §11 justification (task 057); anything else the wizard or worklist needs is built from these or
  escalated.
- **FR-29**: The worklist carries **one aggregate Work Item** linking to the reconciliation tab —
  *"14 emails await a match confirmation →"*.
  *Acceptance*: one `SectionRegistration` mounts the existing reconciliation surface as a Console tab
  (following `dailyBriefing.registration.ts` as the template); the aggregate Work Item is the only
  reconciliation presence in the worklist.
  *Why*: decision 9 — one "needs attention" surface, not four queues. A deliberate task you navigate to is not
  a competing surface, but it still needs one place to start from.

#### Group F — The Do lane and the Briefing fold-in

- **FR-30**: Ship BR-2's three temporal policies as a **predicate migration, not a collector rewrite**.
  *Acceptance*: `DailyBriefingCollector`'s query *shape* — entities, columns, joins — is reused **verbatim**;
  only its hardcoded predicates move into policy rows: the task-type GUID (`:101`), `statuscode = Open`
  (`:104`), the **`TaskOverdueDaysPast = 5` C# constant** (`:116`), and `highpriority OR monitor`
  (`:500-502`). The three rules are: overdue task · task due within 3 days · work assignment past
  `sprk_responseduedate`.
  *Note*: `QueryTodosAsync` (`:999`) is hardcoded to `owninguser = systemUserId` with **no resolver call at
  all**, because `sprk_todo` carries no membership-bearing fields — so a To Do rule is **per-user by
  construction**. Do not spec around this as though it were configurable.
  *Amended 2026-10-07 (D-16, D-25, D-27)*: the Do-lane rules are **subject-only rules in the extended grammar**
  (FR-45) — a `when` filter on the subject's own columns, relative dates `now-Nd` **and `now+Nd`**, and the grouping
  matter derived through the subject's `sprk_regardingmatter` — evaluated by **the one evaluator** (FR-11). The
  collector's entities, columns and status values are the **reference for the rule bodies**; the collector itself
  is **not** made to read policy rows (that would be a second evaluator). Event due date is **`sprk_duedate`**
  (D-27, FR-61); "today" for a Date Only comparison is judged **per item** (D-25, FR-47). The Do lane shows each
  reader only their own work (D-15, FR-54). *(Amended 2026-10-07, D-34: a task filed under a project groups under the
  project; the following sentence is answered.)* What a task filed under a **project** (no matter) does — skip, or
  group under the project's matter — is decided and flagged in the grammar/writer task (D-16).
- **FR-31**: Know items become **narrative + Context pane**, not rows.
  *Acceptance*: new/updated matters, projects, documents and monitored-record activity do not appear as Work
  Items — they fail row-contract requirement 4 (nothing to do that changes anything).
- **FR-32**: Retire *Critical Today* as a list and **remove the LLM-chosen "Top action"**.
  *Acceptance*: `sprk_highpriority` becomes a **rank input** and `sprk_monitor` a **subscription** to Know;
  the first row **is** the top action because rank is deterministic.
  *Why*: a model choosing priority breaks row-contract requirement 2 and decision 15.
- **FR-33**: Ship the first Know-promotion rule — *new matter with no budget after 5 days* (`Absence` shape
  over Spaarke-held data).
  *Acceptance*: it fires on seeded data. **Document-based `Absence` rules are deferred** until documents are
  mirrored — an Absence rule is impossible over reference-mode data.
- **FR-34**: Lane order and volume: **Decide always above Do**; each lane carries its own count filters; the
  narrative **summarises** Do volume rather than listing it.
  *Why*: on the production Briefing's own numbers, 11 overdue tasks would push 5 decisions off the screen —
  burying the cross-source Work Items that justify the product.
- **FR-35**: Replace the Workspace's *Daily Briefing* tab with the worklist **once Decide and Do both exist**;
  keep the old widget until then (BR-4).

#### Group G — Something happens in the world (criterion 10)

- **FR-36**: Ship the **Inquiry** as one Action row plus one Binding on the shipped ADR-039 spine.
  *Acceptance*: a confirmed Signal produces an Inquiry through `ConfirmationPolicyEngine` → `GateDecisionV2`
  → `sprk_servicerequest` outbound **with an SLA**; the Decision Record is written at the gate and the Signal
  closes with `Acted`.
  *Amended 2026-10-07 (D-17, D-20)*: the Inquiry is **one executor called by the decision commit route** (FR-51),
  not wired from a row; the Decision Record is written **last** by that route, listing the send's actual outcome.
  **The inquiry SLA is deferred after R1** — no SLA rule, and no *Escalate* / *Extend SLA* / *Close inquiry*
  actions. The budget-inquiry send and the reply disposition (FR-37) stay. How the confirmation tier is computed
  outside a chat session (the gate is reachable only from chat today, `notes/v4-prototype-vs-solution.md` #25) is
  **not decided** and is an escalation point in task 043.
- **FR-37**: The reply resolves the Inquiry with a **`sprk_disposition`** queryable per matter and per outside
  firm.
  *Acceptance*: the association ladder links the reply to the Inquiry; `sprk_disposition` accrues for the
  Report Card.
  *Note*: **two outcome vocabularies, deliberately.** `sprk_decisionrecord.sprk_decisionoutcome` answers
  *"what did the human decide at the gate?"* (`Authorized` / `Denied` / `Dismissed`);
  `sprk_servicerequest.sprk_disposition` answers *"how did the Inquiry turn out?"*. One word per concept — do
  not merge them.

#### Group H — The classifier

- **FR-38**: Extend `LookupChoicesResolver` to emit **`name — guidance`**, with the schema `enum` unchanged.
  *Acceptance*: the ten already-authored `sprk_classifierguidance` rows stop being inert; the model can
  distinguish `Fee / rate change` from `Invoice / Billing`; constrained decoding still binds to the bare
  category names.
- **FR-39**: Add `sprk_memo` as **signal source #2** — one new Action row reusing the `$choices` contract and
  `sprk_triagecategory` (D-4).
  *Acceptance*: the second producer writes the **same** `sprk_signal` shape as the first, with **no change to
  the consumer** (criterion 6). Do **not** copy `agreement-classify`, which uses a bespoke C# assembler — the
  anti-pattern not to repeat.
- **FR-40**: Measure classifier recall against a labelled set and **gate the project exit on it** (D-10).
  *Acceptance*: recall on `Scope / budget change` and `Fee / rate change` is **≥ 80%** on a **≥ 50-item**
  labelled set, and the measured number is recorded in the project notes.
  *Why*: the predicate is a **conjunction**, so it inherits its weakest input — and that input is the LLM
  classifier. At 70% recall the differentiated claim **silently misses 30% of real cases while every other
  success criterion passes green.** The floor is what turns criterion 11 from an observation into a gate.

#### Group I — Component cleanup (C-1..C-27, per the §5.0 scope rule)

- **FR-41**: Fix the three items that **gate the worklist row**, sequenced first.
  *Acceptance*:
  **C-1** — delete the three dead briefing hooks (`useBriefingNotifications`, `useBriefingNarration`,
  `useBriefingActions`) and fix the **two stale comments** at
  `LegalWorkspace/.../dailyBriefing.registration.ts:48` and `SpaarkeAi/src/main.tsx:252`. These are **not**
  barrel-exported (verified), so the hazard is misdirection, not a compile path: anyone reading either file to
  learn how the Briefing gets its data — exactly what a worklist implementer does — is pointed at
  `appnotification` read-state as if it were the dismiss path. **Writing Dismiss there would record bell-panel
  read-state instead of a Decision Record**: it would appear to work, record nothing, break row-contract
  requirement 5 and starve criterion 4's suppression input.
  **C-3** — disambiguate `MetricCard`; always cite the full path, because a second unrelated `MetricCard`
  exists at `Spaarke.Visuals/src/components/MetricCard.tsx` serving the `VisualHost` PCF.
  **C-4** — add the generic status badge (FR-28).
- **FR-42**: Resolve the remaining C-items, with these six 🔴 hazards called out individually — **none caused
  by this project**, all found by its audit:
  **C-19** delete the `CommandRegistry` cluster — dead infra from a **deleted** PCF whose `deleteCommand()`
  loops `webAPI.deleteRecord` over **every selected record**, sitting beside the live `CommandExecutor` and
  reading as the generic privilege-aware command builder, so a dev adding a grid toolbar could ship an
  untested bulk delete · **C-21** resolve the Pillar-9 `getAgentVisibleState` shim — an **ADR-015 privacy
  contract** that looks enforcing and is structurally bypassed · **C-22** resolve `InsightSummaryCard`, whose
  mount bundle **does not exist in the repo**, so production renders a placeholder forever · **C-5** delete
  `composeCommentThreadsToDocxAnnotations`, which **already caused silent comment loss on save** and is still
  barrel-exported · **C-10** a **live bug**: the To-Do urgency scorer's three copies have drifted —
  `useKanbanColumns.ts:85-89` never received `todoScoring.ts:71-75`'s local-midnight fix, so they disagree **by
  one day in every negative-UTC-offset zone** and a To Do can sit in a different Kanban column than its own
  detail view · **C-23** root `CLAUDE.md` is wrong about the two Calendar variants.
  *Acceptance*: every one of C-1..C-27 is **fixed or has a GitHub Issue URL** — never merely listed
  (`push-to-github` Step 1.6 enforces this).

#### Group J — Repairs this project holds the diagnosis for

- **FR-43**: Fix the **space-bearing matter-number tokenizer**.
  *Acceptance*: `WellFormedTokenPattern` tokenizes live matter `"Form D - 2023"`; ships with a **measured
  query-count delta** per ADR-045's precision/cost profile.
  *Why*: the pattern needs an alpha prefix *immediately* followed by `-`/`.`, so that matter never tokenizes
  and `ExplicitReference` never fires — even with the number **and** name verbatim in the subject.
- **FR-44**: Fix the **false association `reason` string**.
  *Acceptance*: the band and the confidence in the message come from the same variable.
  *Why*: it reports *"Reinforced confidence 0.97 in [0.50, 0.85)"* — 0.97 is not in that band. The band is
  computed from `topDeterministicConfidence` while the message interpolates `topConfidence`. AP-12 in
  runtime-generated prose.

> **Groups K–P were added 2026-10-07** from the owner's decisions D-13..D-29 (§9) on the Console prototype v4
> comparison (`notes/v4-prototype-vs-solution.md`, entries `#n`) and the reconciliation
> (`notes/v4-reconciliation.md`, rows such as R-11, S-10). Where a requirement below needs a choice that D-13..D-29
> did not make, it says so and the owning task carries an escalation trigger; §11 lists those points.

#### Group K — Rule grammar and evaluation (D-16, D-23, D-25)

- **FR-45** *(added 2026-10-07, D-16)*: **Extend the rule grammar for the Do lane.**
  *Acceptance*: (a) an `Existence` body may be **subject-only** — a `when` filter on the subject's own columns
  with **zero** `exists`/`notExists` clauses (today the schema requires `minItems: 1`); (b) relative dates accept
  **`now+Nd`** as well as `now` and `now-Nd`; (c) `sprk_event`, `sprk_todo` and `sprk_workassignment` are valid
  subjects, read org-wide by the evaluating principal, with the grouping matter derived through the subject's
  `sprk_regardingmatter`; (d) the rule body accepts the **quiet-window knob** of FR-17a; (e) everything outside
  this grammar is still **refused at save and at evaluation** (FR-08, fail closed) and still compiles to **one**
  Dataverse filter (CM-3). A subject with no derivable matter (a task filed under a project; a To Do with no
  regarding record) never produces a Signal with a null `sprk_matter` (FR-14, D-3).
  *Amended 2026-10-07 (D-31, D-33)*: **except a To Do**: the overdue-To-Do rule covers **all** To Dos, and a To Do with
  no matter gets a Signal with no `sprk_matter`, owned by the To Do owner's BU (FR-14 as amended) — unless it is filed
  under a **Secure** project, the one narrow skip (FR-14a as amended).
  *Amended 2026-10-07, D-34/D-35*: the grouping record is the subject's **core record** from `CoreAncestorResolver`
  (matter or project), not only `sprk_regardingmatter`: a task or To Do filed under a **project** groups under that
  project (this answers D-16's "task filed under a project"). Only an item with **no matter and no project** is
  matterless, and D-35 makes its Signal owner-only.
  *Amended 2026-10-07, D-36/D-37/D-38*: a work-assignment subject groups under itself; ties follow D-37 (direct
  filed-under core record, then matter over project); there are no skips (D-38).
  *Why*: only Path B can be written or evaluated today; the Do lane, the overdue rules and the To Do subject are
  hard refusals in the validator, the compiler and the writer (#12). D-16 chose the smallest extension that keeps
  **one** evaluator.
- **FR-46** *(added 2026-10-07, D-16)*: Author the **`Threshold` rule type** — JSON Schema plus compiler — for the
  spend threshold.
  *Acceptance*: a `Threshold` body compiles through `PolicyVersionValidator` to the same compiled-predicate
  contract `Existence` produces, so the evaluator runs it **with no evaluator change**; an invalid body is refused
  at save (FR-08); `Switch` is still refused.
- **FR-47** *(added 2026-10-07, D-25)*: **"Today" is judged per item.** For a Date Only comparison (overdue, due
  within N days), each item's "today" is the calendar date in its **assignee's time zone, else its owner's, else
  UTC** — the same rule and the **same shared helper** as To Do generation (task 098).
  *Acceptance*: an item due yesterday in its assignee's zone is overdue even when it is still "yesterday" in UTC,
  and vice versa; no second time-zone helper exists; a nightly organisation-wide pass gives the same answer as
  the user's own Briefing and SmartTodo colours.
- **FR-48** *(added 2026-10-07, D-23)*: **Evidence lines carry the witnesses.** The rule's sentence stays the
  rule's literal text (task 022's template rule is unchanged); the **witness values** — which communication matched
  an `exists` clause, its sender and received date — are written as **evidence lines** in `sprk_evidencerefs`, one
  witness per `exists` clause, each naming the clause it tested; context that no clause tested (for example the
  spend snapshot) is labelled *context, not tested*.
  *Acceptance*: no witness value appears in `sprk_sentence`; every evidence line names its clause or is marked
  context; a `notExists` clause produces no witness (there is nothing to cite). The **UI composes the row
  headline** from the Signal's display columns (subject name, due state, rule short name) — a display, not a
  claim, so §0.3 is not engaged (D-23).

#### Group L — Recording a decision (D-17, D-18, D-19, D-21)

- **FR-49** *(added 2026-10-07; D-16, D-18, D-19)*: Each policy version carries a **decision plan**
  (`sprk_policyversion.sprk_decisionplan`, JSON): the ordered action codes the rule offers (the first is the
  recommendation) and its Next-steps set. A plan change is a new version, so an old decision stays explicable.
  *Acceptance*: every enabled Decide-lane version has a plan with at least one action that changes something
  (FR-26 req 4); every code in a plan exists in the action catalog (FR-50); the plan is immutable after publish
  (FR-21 as amended).
- **FR-50** *(added 2026-10-07; D-19)*: A **closed action catalog in the BFF** (C#), exposed read-only: per action
  its label, work type, parameters, effect lines, `excludes`, and the record class it produces when taken;
  per lane the closed **dismissal-reason** list with a *counts toward suppression* flag. Adding an action is a code
  change with review (R1 has no Action Engine, §2.2).
  *Acceptance*: the catalog contains *Send budget inquiry*, *Revise budget* (D-18), *Approve variance* (D-19,
  record-only) and the Do-lane actions the Do policies' plans name; *Approve variance*'s effect lines say the
  Decision Record is the approval and nothing else is written. The record class of *Reassign* and *Extend response
  date* is **not decided** (reconciliation C-3) — escalation in task 036.
- **FR-51** *(added 2026-10-07, D-17)*: **One BFF decision commit route, record last.** Order: validate → run the
  internal (Dataverse) writes → send email **last** → write the **one** Decision Record listing actual outcomes and
  the Next steps created → close the resolved Signals.
  *Acceptance*: nothing executes before *Record decision*; on any failure **no record is written** and the
  response tells the user **which writes landed**; the route is **idempotent per review** (a retry of the same
  review neither duplicates a write nor writes a second record); a decision may close several **Decide** Signals on
  the same matter, only those the user ticked (*Also resolve*), and a Do item never resolves another (*amended
  2026-10-07, D-34*: "same matter" reads "same core record"); the route is
  classified in the #1312 route-authorization census and checks, **as the caller**, that they can write each
  Signal's subject and append to the matter; a caller who cannot be resolved gets #1312's single **403** (D-29).
- **FR-52** *(added 2026-10-07; D-18, D-19)*: **Executors** the commit route calls. *Revise budget*: the BFF
  writer **creates** the `sprk_budgetrevision` (Spaarke Ontology Service is granted Create, D-18) **after checking
  the caller can write the target `sprk_budget`**, and the revision **also updates that budget's amount**; when a
  matter has several budgets the user picks one. *Approve variance*: **record-only** — the Decision Record (outcome
  Authorized) is the approval and nothing else is written (D-19). *Send budget inquiry* (FR-36). The Do-lane
  actions (complete, reschedule, reassign, send a reminder) reuse the existing event, To Do and communications
  cores; reschedule writes **`sprk_duedate`** (D-27). The Next-step creators run **inside** the commit, before the
  record, and their ids are listed in the record (`sprk_followons`).
  *Acceptance*: a revision recorded through a decision closes the Path B Signal as `Acted`, and the
  budget-revision trigger (FR-13b) does not re-close it as `ConditionCleared`. Which identity writes the new
  budget **amount** onto `sprk_budget` is **not decided** — escalation in task 044.
- **FR-53** *(added 2026-10-07, D-21)*: **Server-side work-assignment create.** `sprk_workassignment` is created
  through the BFF (WP-3; a work assignment is a secure-record root), so *Assign Work* can be a Next step inside the
  decision.
  *Acceptance*: the server create preserves the existing client wizard's business-unit cascade semantics; the
  existing wizard can call it; the work-assignment area owner has reviewed it.

#### Group M — Reading Signals and the decision wizard (D-15, D-26)

- **FR-54** *(added 2026-10-07, D-15; amended 2026-10-07, D-33; amended 2026-10-07, D-34/D-35 — read access on the
  core record, owner-only for rows with none; see FR-24)*: **The BFF Signal read route.** The worklist (and anything else in the
  Console) reads `sprk_signal` only through this route.
  *Acceptance*: the route runs the worklist configuration's FetchXML (FR-24), then removes every row whose grouping
  matter the **caller** cannot read; in the Do lane it returns **only the caller's own work**; it passes the #1312
  census; a caller who reads nothing gets an empty list, not an error. Which subject field makes a work assignment
  "mine" (assignee or assigner) is **not decided** — escalation in task 038.
- **FR-55** *(added 2026-10-07, D-26 and the v4 baseline)*: **Every decision happens in one decision wizard**,
  `WizardShell` launched **in-app** from the worklist widget (never a `navigateTo` code page): *What was found* →
  one step per plan action (skip allowed; `excludes` honoured) → Next steps → Confirm (record class shown) →
  *Record decision*; skip everything → a dismissal with a required reason; a decided item reopens in the **same
  wizard, read-only**, showing its fact snapshot under a status bar.
  *Acceptance*: HANDOFF §1.1 and §1.4 "The wizard" (L127-136) @ `ae1cc9f` hold, checked per the reconciliation
  §B.6 pre-start rule; the wizard writes nothing itself — it calls the commit route (FR-51).

#### Group N — Ontology admin (D-22, D-14)

- **FR-56** *(added 2026-10-07, D-22)*: An **"Ontology admin" workspace tab**, shown only when a **BFF
  capability probe** says the caller can create `sprk_policyversion` (checked **as the caller**). Every admin write
  goes through the BFF **as the caller**, so Dataverse enforces the Spaarke Ontology Administrator role and the
  audit names the human. The five ontology tables are added to *Spaarke Platform* **read-only**; Spaarke Ontology
  Administrator gains Create/Write/Read on `sprk_triagecategory`; the role is assigned to the owner's spaarkedev1
  account.
  *Acceptance*: a user without the role never sees the tab and gets 403 from every admin write route; the probe is
  not the security boundary — Dataverse privileges are.
- **FR-57** *(added 2026-10-07; D-22 slice a)*: **Rules, read-only**, plus **On/Off** and **Retire** (reason
  required; closes open Signals immediately, FR-16).
- **FR-58** *(added 2026-10-07; D-22 slice b, D-14)*: **Rule authoring wizard.** Save calls
  `PolicyVersionValidator.ValidateForSave`; the condition editor's catalog is **generated from the compiler's
  verified joins and readable tables**, so it can only offer what compiles (#46); *Test* is a **dry run** over
  current data using the evaluator's side-effect-free core, run **as the evaluator principal**, with the displayed
  rows filtered by the caller's access (#39); a new rule saves **Off**; publishing supersedes open items, re-raises
  those that still hold (FR-16) and stamps the old version's `sprk_inforceto` (D-14).
- **FR-59** *(added 2026-10-07; D-22 slice c)*: **Classification admin** — triage categories with guidance,
  On/Off, which rules read each, 30-day volume; edit in a form; a rename is blocked while a rule reads the
  category; a new category saves Off. Where measured recall is stored (reconciliation C-16) is **not decided** —
  escalation in task 103.

#### Group O — Modal system (D-26)

- **FR-60** *(added 2026-10-07, D-26)*: **One canonical modal approach** per `notes/modal-wizard-canonical-approach.md`:
  `SprkModal` is the only envelope; `WizardShell` is the only wizard engine and becomes `SprkModal`'s wizard preset
  (`WizardModal` is deleted); a Spaarke React caller opens wizards **in-app**; `navigateTo` web-resource dialogs
  remain only for hostless ribbon/command scripts. ADR-050 is amended (path B). **Today's wizards launched from
  Spaarke React surfaces are migrated off `navigateTo`** in this project.
  *Acceptance*: no in-app launcher in SpaarkeAi or LegalWorkspace opens a wizard through
  `navigateTo(webresource)`; wizards ignore Escape/backdrop (`dismiss="explicit"`) and scale with `uiScale`; every
  non-embedded `WizardShell` consumer passes its regression row (modal note §5.2); no CSS or DOM is injected into
  platform chrome.

#### Group P — Events and To Dos (D-27, D-28, D-29)

- **FR-61** *(added 2026-10-07, D-27)*: The Do lane, Reschedule **and the Daily Briefing** use **`sprk_duedate`**
  as the event due date, always; `sprk_finalduedate` becomes informational.
- **FR-62** *(added 2026-10-07, D-28)*: **`statuscode` is the authoritative event status**; `sprk_eventstatus` is
  deprecated. Its remaining readers are inventoried before removal; *Completed* stays an Active-state status.
- **FR-63** *(added 2026-10-07, D-29)*: The **To Do composite score** counts **calendar days** (as the due label
  already does), computed by **one shared function**; the boards re-rank once.
  *Acceptance*: the urgency component and the due label agree for every due date; no second copy of the scorer
  exists.

### 3.2 Non-functional requirements

- **NFR-01**: **Publish-size** measured per CLAUDE.md §10 on every BFF-touching task — against a **fresh
  build of master** (never the recorded baseline number), from a **short path** (deep paths trigger `MSB3030`
  and produce a *smaller* partial zip that reads as an improvement), with **file counts compared on both
  sides** and the zip tool named. Ceiling **≤ 60 MB**; ≥ +5 MB single-task delta needs explicit justification.
- **NFR-02**: **§0.3 is binding** — a capability must TEST what its message CLAIMS. `sprk_sentence` and
  `sprk_policyversion.sprk_messagetemplate` may assert **only** what the predicate actually read. A template
  referencing a field the body does not read is a defect, not a wording choice.
- **NFR-03**: **Fail closed.** Any rule-store or fact read failure denies rather than proceeding
  (`CommunicationRuleGate.cs:108-126`). Security fails closed per the Dataverse write-path architecture
  (WP-6).
- **NFR-04**: **Append-only verified as a property of the union of roles**, not of any one role — Dataverse
  privileges are additive across every role a user holds, the effective depth is the maximum, and **there is
  no deny**.
- **NFR-05**: **Testing (ADR-038), two rules that came out of this project's own forensics.** (i) Any test
  asserting a Dataverse **column list, entity name or option-set value** must be paired with something that
  touches the real schema — a shape-only unit test **pinned a non-existent column for months** while coverage
  stayed green over a query that threw on every single run. (ii) A path that throws consistently is a **defect
  to diagnose, never noise to tolerate**: the `sprk_eventdescription` retrieve failed on *every* briefing run
  for months and nobody read the exception.
- **NFR-06**: **Observability of swallowed exceptions.** Any new swallow-and-log path emits to App Insights
  (appId `6a76b012-46d9-412f-b4ab-4905658a9559`; `traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for
  swallowed throws). This is how the silent `InvalidCastException` was found.
- **NFR-07**: **No "already generic" claims without a verified extension point.** `ILiveFactResolver`'s
  *dispatch* is config-driven by `(subject-scheme, predicate)`, but its **predicates are a closed C# `switch`**
  (`MatterLiveFactResolver.cs:166-174` et al). Nothing reads `sprk_communication`. **Neither Path B conjunct is
  reachable by adding a `case`** — both halves are new code. Likewise `ISignalRule` is a **private nested
  interface** (`SignalEvaluationService.cs:268`) instantiated in the constructor (`:116-120`), **not** an
  extension point, despite an XML doc claiming a strategy pattern the code does not deliver; and its
  `GenerateDeterministicId(matterId, signalType)` (`:241-258`) cannot be reused because it has no room for a
  polymorphic subject.
- **NFR-08**: **Security trim via typed lookups.** The subject is written by
  `PolymorphicResolverService.applyResolverFields`, which sets the typed `sprk_regarding*` lookup **and** the
  denormalized trio in one call. Dataverse trims **lookups** by the user's access; a denormalized **text GUID
  is not trimmed**. Both are required — they are not alternatives.
- **NFR-09**: **Zero-deployment properties must not degrade silently.** A new taxonomy row is live on the next
  enrichment because `$choices` resolves per run into both the prompt and the constrained-decoding `enum`. That
  resolution is **best-effort**, and a Dataverse read failure degrades to the pre-2026-09-04 behaviour
  (category null on 100% of captures) **invisibly** — tracked as
  [ISS-002](https://github.com/spaarke-dev/spaarke/issues/1049).
- **NFR-10** *(added 2026-10-07; D-17, D-22, D-29)*: **Every new BFF route is authorized as the caller and
  classified in the #1312 route-authorization census** (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`),
  with a record-level check. A caller who cannot be resolved gets **#1312's single 403** — no new 401/503 split.
- **NFR-11** *(added 2026-10-07; D-14, D-17, D-18, D-22, D-29)*: **Every role edit is re-verified as a union.** The
  owner-approved role edits (writer AppendTo on four tables and Create on `sprk_budgetrevision`; administrator Write
  on `sprk_policyversion` and Create/Write/Read on `sprk_triagecategory`; Console User losing Write on `sprk_signal`;
  *amended 2026-10-07, D-33*: Secure Record Owner Read on `sprk_signal` and `sprk_decisionrecord`, Spaarke Basic User
  Basic Read on both, Spaarke Ontology Service Assign on `sprk_decisionrecord` — owner only, never content)
  are each followed by the task-002 union check, and `prvWritesprk_DecisionRecord` / `prvDeletesprk_DecisionRecord`
  stay absent from every Spaarke role. **Deploy order (D-33)**: in every environment the role edits land **before** a
  BFF carrying the new secure-child config, or every Secure-matter create fails with `0x80040299`.

---

## 4. Technical constraints

### 4.1 Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-002** — no plugins; invariants on the server | Append-only and immutability are **privileges**, never plugins. Each invariant has ONE BFF server-side owner |
| **ADR-004** — queue `IJobHandler` | The two event triggers (FR-13) |
| **ADR-036** — scheduled `IScheduledJob` | The nightly evaluator (FR-12) |
| **ADR-052** — workload placement | Decided per workload; tie-breaker is **fewer moving parts**. Both land in the BFF; no new timer `BackgroundService` |
| **ADR-024** — polymorphic regarding | The subject seam, reused unchanged |
| **ADR-039** — Binding owns dispatch | See §6 — **path A** |
| **ADR-040** — session ledger | See §6 — **path B** |
| **ADR-015** — privilege flagged, never decided | `sprk_privilegeflagged` is copied forward; **nothing branches on it** |
| **ADR-013** — `PublicContracts` facade | The evaluator must not inject AI-internal types |
| **ADR-038** — testing strategy | NFR-05 |
| **ADR-045** — association engine | FR-43 changes its precision/cost profile; ships with a measured delta |
| **ADR-050** — canonical modal shell | `SprkModal` + presets for every modal |

### 4.2 MUST / MUST NOT

- ✅ MUST express every rule body as **one Dataverse filter** (CM-3) with **no cross-clause variable passing**
- ✅ MUST keep the rule-type set **closed**; adding one is a code change with review
- ✅ MUST write the Decision Record on **both** the authorize and the deny path
- ✅ MUST set the Signal owner from its grouping core record (matter or project) at creation (FR-14; *amended 2026-10-07, D-34*)
- ✅ MUST have deterministic **membership and rank** — the model never selects the queue or the order
- ❌ MUST NOT read `sprk_budget.modifiedon` as evidence of a revision (FR-07)
- ❌ MUST NOT let a predicate be evaluated by the model. *A predicate evaluated by a model makes the Decision
  Record an anecdote* — the single most important constraint in this project
- ❌ MUST NOT build a second row component (FR-25)
- ❌ MUST NOT place new UI in `src/solutions/SpaarkeAi/` when it could live in a shared library (FR-28)
- ❌ MUST NOT use MCP `create_table` for any Dataverse metadata (group A banner)
- ❌ MUST NOT raise the auto-file threshold or let the AI rung reach it. **Recall over precision for
  NOTIFYING, never for FILING** — filing writes data, where a false positive is worse than a miss (decision 13)

### 4.3 Existing patterns to follow

| For | Copy |
|---|---|
| Scope match, ordering, fail-closed | `CommunicationRuleGate.cs:108-188` |
| A scheduled census/sweep | `SecureRecordIsolationCensusJob` (one of six live `IScheduledJob`s) |
| `EXISTS` via `link-entity` | `DataversePrecedentBoard.cs:182-190` (the **only** in-repo prior art; `notExists` has none) |
| Polymorphic subject | `PolymorphicResolverService.applyResolverFields` — the one utility the audit found genuinely canonical with **zero** reimplementations |
| Narrative LLM output | the shipped [Playbook-driven LLM Output Pattern](../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md) — Layer 1 resolves the template, Layer 2 renders `## Input` |
| Widget registration | `dailyBriefing.registration.ts` |
| Resolution vocabulary | live `sprk_spendsignal.sprk_spendsignalstatus` (Active / Acknowledged / Resolved / **Auto Resolved**) — *Auto Resolved* **is** `ConditionCleared`, so this is vocabulary to **copy, not invent**. What does not exist is anything that **writes** it |

---

## 5. Placement and new components (CLAUDE.md §10 / §11)

### 5.1 Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>Y</root-claude-md>
</hot-path-declaration>
```

`bff` — LANDED: `RiConfidenceScorer`, `CommsPolicyOptions`, `CommunicationRuleGate`,
`CommunicationRiActionService`, `TaskActionCore`, `ActionSeam`, `DailyBriefingCollector`. PLANNED: the
evaluator, the Decision Record writer, the `LookupChoicesResolver` guidance extension.
`spaarkeai` — Y permanently (D-3: the worklist needs a new row component).
`skill-directives` — `.claude/FAILURE-MODES.md` and `.claude/CHANGELOG.md`, **not** `.claude/skills/**`.
`root-claude-md` — §1.1 product-names table added 2026-10-02, with its CHANGELOG entry.

**Coordination**: this project has already merged into communication-intelligence territory (PR #1032).
`/conflict-check` before **every** BFF PR. `spaarke-legal-front-door-r1` is pre-spec and must be told that
CM-5 likely absorbs its proposed `sprk_legalrequest`. A domain owner must review the PR #1032 semantics, in
particular `TaskActionCore` now setting `statuscode = Open` for **every** consumer.

### 5.2 Placement justification

| Component | Placement | Decision criteria |
|---|---|---|
| Predicate evaluator | **BFF** `Services/Signals/` | Needs Dataverse reads, the rule store and `ILiveFactResolver`; no AI-internal types (ADR-013). Nightly schedule + two queue triggers, both shipped BFF seams (ADR-052: fewer moving parts) |
| Decision Record writer | **BFF**, inside `RuleGatedAssessedConsumer` | The object already exists there on both paths; the write is a line before the branch. No new service |
| Row component + status badge | **Shared library** `@spaarke/ui-components` | FR-28. A component in `src/solutions/SpaarkeAi/` is not reusable, and the componentization audit already lists that coupling as debt |

### 5.3 New components — the §11 three-question gate

| New component | Existing overlap (verified) | Can extend instead? | Cost of doing nothing (concrete) |
|---|---|---|---|
| **Cross-source predicate evaluator** | `SignalEvaluationService`; `CommunicationRuleGate` (scope + threshold, **no predicate at all**) | **No** — `ISignalRule` is a private nested interface instantiated in the ctor, not an extension point (NFR-07) | The differentiated claim is **not computable**. This is the only item in the project whose value is unproven |
| **`Existence` rule type** | `Threshold`, `Switch` | **No** — neither expresses an EXISTS/NOT-EXISTS over a related entity | Validation **refuses to save** the body, so the differentiated capability is literally unsavable |
| **`sprk_decisionrecord.sprk_action`** (FR-01) | nothing | n/a — a missing column from a settled decision | D-2's "deny paths have no action" constraint is unimplementable; a deny path cannot save |
| **`sprk_servicerequest` direction + `sprk_disposition`** (FR-02) | nothing on that table | n/a — same | Criterion 10 cannot be measured; nothing accrues per matter or per outside firm |
| **Generic status/severity badge** | `CitationBadge`, `PinnedMemoryProvenanceBadge`, `ChannelBadge` — all **domain-specific** | **No** — none takes a generic status | The row cannot render Signal status; each consumer hand-rolls one, which is how the three ⋮ menus happened |
| **Worklist row component** | `HighPrioritySection` (already a worklist in structure: type badge, status chip, per-row menu) | **Partially** — it is the ancestry, but it has no evidence tiers, no *Why this fired* disclosure, no outcome cards and no gate host (prototype finding 9) | Signals stay invisible, exactly as `sprk_spendsignal` has been since it shipped |

Everything else in this project is **modify-only** and needs no justification.

---

## 6. ADR tensions (CLAUDE.md §6.5 — MANDATORY)

Both tensions were **surfaced in design §6 and left path-deferred** by owner decision 2026-09-30:
*"evaluate/define/revise this ADR once we get the solution actually decided."* The solution is now decided and
D-2's table is built, so the condition is met. **Resolved by the owner 2026-10-03 (D-9).**

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-039** | Binding owns dispatch routing | A Policy evaluator is a second *rule* surface | **A — project-scoped exception** | **Policy decides whether a claim is TRUE; Binding decides what EXECUTES** — different axes, not competing routers. `CommunicationRuleGate` already accepted exactly this for its own store (owner decision 2026-07-22, `projects/spaarke-notification-spine-r1/notes/041-rule-store-decision.md`), so this is a precedent being followed, not created. Documented here, cited in the PR, code-review approves explicitly |
| **ADR-040** | Session ledger owns gate decisions | The Decision Record overlaps `SessionGate`, which carries `Kind`/`Status`/`SideEffectClass`/`BindingId`/`Turn` but **no** authority, policy version, evidence or confirmer | **B — ADR amendment** | ADR-040 is chat-session-shaped and Redis→Cosmos-tiered; a Decision Record must be a **durable Dataverse row queryable per matter**. Extending ADR-040 to name the two as **siblings** beats forcing either to serve both. Design §6 called this "the heavier claim and the likelier to be wrong" and said it needed the Decision Record shape settled first — **D-2 is now settled and the table is built**, so the precondition is met. `sprk_decisionrecord.sprk_gatesessionid` is the link. ⚠️ **The amendment must merge before or alongside the evaluator** |

**Alternatives genuinely considered.** For ADR-039, (B) amendment — rejected because the ADR is not wrong, it
simply does not speak to truth evaluation; amending it would broaden it for no gain. For ADR-040, (A)
exception — rejected because a second durable-ledger consumer is foreseeable and the exception would have to
be re-argued each time. For both, (C) comply — rejected because complying means either evaluating predicates
inside Binding or writing decisions into a chat-session ledger, each of which produces a worse system than the
documented deviation.

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-028** | Constraints MUST: "use `DefaultAzureCredential` (managed identity) for all server app-only outbound — … Dataverse service identity …"; A4 App-only row: "`TokenCredential` → `DefaultAzureCredential` pinned to the UAMI". (A4's single-shared-provider MUST governs CONFIDENTIAL-CLIENT credentials only and is not engaged here: the writer builds no confidential client.) | Task 030's Signal writer (`OntologyWriterCredentialFactory` / `OntologyWriterDataverseClient`) deliberately builds a SECOND, separate credential pinned to a DIFFERENT UAMI (`mi-ontology-writer-dev`, task 006) and uses `ManagedIdentityCredential` directly rather than `DefaultAzureCredential` | **A — project-scoped exception (✅ APPROVED by owner 2026-10-04 — raised by the task 030 independent review, resolved same day)** | The shared factory is pinned to the BFF's SYSTEM ADMINISTRATOR identity's config keys (`Graph:ManagedIdentity:ClientId` / `ManagedIdentity:ClientId`); reusing it would authenticate the writer as sysadmin — the exact defect task 006's dedicated identity exists to remove. `ManagedIdentityCredential` (not `DefaultAzureCredential`) is deliberate: no credential-chain fallback exists, so there is no code path by which a future change could silently route the writer through a different identity (code-review findings F7/F13/F14 — verified by removing `DataverseServiceClientImpl`'s `TENANT_ID`/`API_APP_ID` branch from the writer's own code entirely, not merely bypassing it). This is the literal recipe task 006's owner-approved plan specifies (`notes/006-writer-identity-plan.md`) |

**Alternatives considered for ADR-028** (presented to, and accepted by, the owner alongside the approval above): (B) amend ADR-028 A4 to permit a second shared factory per dedicated identity — rejected as premature; only one such identity exists today, and generalizing the rule before a second case justifies it risks writing a rule for a shape not yet seen. (C) comply — would require going through `ManagedIdentityCredentialFactory`, a `DefaultAzureCredential` factory keyed to the sysadmin UAMI, by adding a parameter for an alternate config-key pair — rejected because that would re-introduce a BRANCH into the one place code-review found must have none (F7).

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-009** | "MUST use `IDistributedCache` for cross-request caching"; "MUST NOT add L1 cache without profiling proof" | Task 096 (PR #1294, fixing issue #1295 outside the ontology code) adds an in-process, cross-request cache of each AI tool schema's meta-schema verdict in `Draft202012MetaSchemaValidator`. The verdict must be serialized under one process-wide lock (Json.Schema.Net is not thread-safe), and both validation sites run on **every chat message** | **A — project-scoped exception (✅ APPROVED by owner 2026-10-05)** | The cache holds the result of a pure function of the schema text (valid flag + error list): no store data, nothing per tenant or user, so there is nothing to keep consistent across instances. Profiling proof is in the PR: 0.12–0.19 ms per small schema and 15–108 ms per 32 KB schema uncached, about 0.1–7 µs cached; without the cache chat turns queue on the lock. Capped at 1,024 entries / 2,000,000 characters; past the cap it evaluates under the lock (still correct). A Redis round trip would cost more than the evaluation it replaces |

**Alternatives considered for ADR-009**: (B) amend ADR-009 to allow in-process memoization of pure functions — not
proposed from a single case; (C) comply — either no cache (correct, but every chat turn serializes on the lock) or a
Redis-backed verdict cache (a network round trip to avoid a microsecond lookup, plus a new dependency on the hot path).

| Rule | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ci-cd-unit-test-remediation-r1 FR-A02** (`ci-tier1-blocking.yml` header: "do not extend without spec amendment") | Tier 1 holds a fixed set of blocking jobs; new jobs need a spec amendment | Task 081 (PR #1309, outside the ontology code) adds a blocking Tier 1 job running the Xrm capability guard (`xrmCapabilityUsage.guard.test.ts`, the TypeScript-AST analyzer `xrmCapabilityAnalyzer.ts`), gated on a new `classify-tier1` output for `src/client/**`, `src/solutions/**`, `.github/workflows/**`, failing the Router | **A — project-scoped exception (✅ APPROVED by owner 2026-10-06)** | The guard catches a defect class that fails **silently** in Dataverse (code reading an Xrm member from a frame that lacks it → a button that does nothing, a name that never resolves); only a blocking check stops it at PR time, and the Router is the only check the master ruleset requires. Cost ≈70 s on client-touching PRs only; master is clean, so it blocks only a PR that introduces a violation. Scoped to this one job. **Extended by the owner 2026-10-06** to one router fix: `ci-router.yml`'s `docs_only` flag counted client code plus any doc as "docs-only" and skipped ALL of Tier 1 (found by the round-7 review; proof run 32098200093) — now docs-only only when every changed file is documentation |

**Alternatives considered for FR-A02**: (B) amend the CI project's spec to allow client-guard jobs generally — not
proposed from a single case; (C) comply — keep the guard advisory in legacy SDAP CI, where a job-level
`continue-on-error` and the ruleset (Router only) mean a violation never stops a merge; also "Tier 1 advisory first"
(the DataGrid gate's pattern) — declined by the owner.

---

## 7. Success criteria

| # | Criterion | Verify by |
|---|---|---|
| 1 | A Policy row's predicate can be changed and the change takes effect **with no deployment** | Edit `sprk_rulebody`, re-run the evaluator, observe new membership |
| 2 | The cross-source rule fires on a matter with **both** an over-budget snapshot and a scope/fee-classified communication, and does **not** fire when either is absent | The §8.1 exit triple **plus two negative controls** (see Assumptions A-3) |
| 3 | Every authorize **and** every deny writes a Decision Record; the deny path is queryable per matter | FetchXML per matter over both outcomes |
| 4 | A Signal dismissed three times on the same matter stops being raised *(amended 2026-10-07, D-34: same core record — matter or project)* | Three dismissals → `sprk_suppresseduntil` set; per D-11 grain (as amended by D-34) and expiry |
| 5 | `sprk_confidencethreshold` is set from the observed confirmed-vs-dismissed distribution, not by guess | Query the Decision Records and show the derivation |
| 6 | A second producer (`sprk_memo`) writes the **same** `sprk_signal` shape, with no consumer change | Diff the consumer; it must be untouched |
| 7 | **Membership and column/action configuration** come from a `sprk_gridconfiguration` row with no code change; **one** row component renders every shape | Config edit + component count = 1 |
| 8 | A new taxonomy category — with guidance — changes classifier behaviour with no deployment | Add a row, re-run enrichment |
| 9 | **The §0 differentiation test passes** for every shipped capability — and **the predicate reads the data the message names** (§0.3) | Per-capability table, naming the data and the `where` clause |
| 10 | **Something happens in the world**: a confirmed Signal produces an Inquiry through the gate, ~~carrying an SLA~~ *(SLA deferred after R1 — amended 2026-10-07, D-20)*, and the reply resolves it with a `sprk_disposition` queryable per matter and per outside firm | End-to-end on seeded data |
| 11 | **Classifier recall meets its floor** — **≥ 80% on ≥ 50 labelled items** (D-10) | Labelled-set measurement, number recorded |

**Standing metric, not a criterion**: *action rate per policy* = acted ÷ surfaced, a zero-code Dataverse
rollup. Below roughly half, a policy is generating noise and is a candidate for retirement or re-thresholding.
⚠️ For **Do** rules this must be computed from **`sprk_signal.sprk_resolutiontype`** — built the other way it
would report ~0% action rate on every Do rule, making a noisy policy and a perfect one look identical.

---

## 8. Dependencies

### 8.1 Prerequisites

| On | State |
|---|---|
| **PR #1032** | ✅ Merged to master as `93634db58` |
| **The five tables** | ✅ Created in `spaarkedev1`, publisher Spaarke, both alternate keys **Active** |
| **Three security roles** | ✅ Created, assigned and privilege-verified; auditing on all five tables and at org level |
| **Group A column adds** | ⬜ FR-01, FR-02 — blocks the deny path and criterion 10 respectively |
| **The five tables in `Spaarke Platform`** | ⬜ In progress (owner). Without it there is no form to author a Policy or inspect a Signal by hand, which §8.1 seeding and all debugging need |
| **§8.1 dev seed data** | ⬜ All five tables at 0 rows. The exit triple is criterion 2's minimum input |
| **[ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050)** | ⬜ 48 `sprk_event` rows stranded in `Draft` — must land **before the Do lane ships**, or the lane under-reports |
| **ADR-040 amendment** | ⬜ Per D-9 path B; merges before or alongside the evaluator |
| **uac-r2 batch 4 deployed in dev** *(added 2026-10-07, D-33)* | ⬜ Its secure-share synchronizer and reconciler jobs must be running, or a Secure-team-owned Signal is readable by nobody; task 039 checks it |
| **Merge master** | ⬜ Branch is 44+ behind and master moves fast (70 commits in hours, observed). **Merge before any deploy**, or you revert other projects' work |

### 8.2 External

App Insights (the only way to diagnose the swallow-and-log paths) · the Console prototype contract —
`HANDOFF.md` @ `ae1cc9f` (v4, findings 1–40) in `spaarke-dev/spaarke-prototype`, branch
`feature/2026-10-spaarke-console`; **owner-accepted as the baseline 2026-10-05**, refined during UAT. Inconsistencies
between it and what the solution can actually do are flagged for the owner (`notes/v4-prototype-vs-solution.md`);
its reconciliation against this spec is `notes/v4-reconciliation.md` · a communication-intelligence domain owner for
the PR #1032 semantics review.


### 8.3 Coordination with uac-r2 (owner rule, 2026-10-07)

Every task that touches Restricted, Limited or Secure matters, record access, secure children, ownership or grants
is coordinated with the **latest** unified-access-control-r2 code, because that project is actively changing access
control. Such a task (tagged **[uac]** in `tasks/TASK-INDEX.md`; today 008, 024, 031, 032, 034, 036, 037, 038, 039,
040, 042, 043, 044, 045, 046, 049, 079, 101, 104) MUST: (1) before starting, fetch `origin/master`, re-read the uac-r2
implementation it depends on (`SecureChildLineage.cs`, `config/secure-record-owner-role.json`, `RecordOwnershipResolver`
(I-6), `RecordRouteAccessAuthorizationFilter`, `CallerRecordAccessProbe`, `RouteAuthorizationGuardTests.Ledger.cs`,
ADR-034 and its amendments, `ExternalCallerContext` (ADR-003), `CoreAncestorResolver` (FR-26 stamps, D-34)) and name the files in its completion record; (2) check
uac-r2's open PRs and active work and run `/conflict-check`; (3) reuse uac-r2's mechanisms and never build a parallel
one, routing any change to uac-r2-owned files through uac-r2's review with the PR linked; (4) stop and escalate if
uac-r2's code has changed in a way that invalidates the task's plan.

---

## 9. Owner clarifications

Captured during this `/design-to-spec` pass, 2026-10-03. **Record these in `design.md` §8 as D-9..D-12** so
the two documents do not disagree.

| ID | Question | Answer | Implementation impact |
|---|---|---|---|
| **D-9** | design §6 left ADR-039 and ADR-040 path-deferred pending "once the solution is actually decided." That condition is now met, and §6.5 forbids choosing silently | **ADR-039 → path A** (project-scoped exception); **ADR-040 → path B** (amendment naming `SessionGate` and the Decision Record as siblings) | §6 above. The ADR-040 amendment is a **prerequisite** of the evaluator task |
| **D-10** | Criterion 11 makes recall a gate but states no floor — and a gate without a number cannot gate | **≥ 80% recall on a ≥ 50-item labelled set** | FR-40. Deliberately above the 70% §1.2 warns about; ≥ 50 items means one miss moves the number ~2%, not ~10%. Below the floor, R1 does not exit until recall improves or the §0 claim is re-scoped |
| **D-11** | Criterion 4 says "three times on the same **matter**" but `sprk_dedupekey` is per **subject** — two grains; and `sprk_suppresseduntil` is a date with no stated duration | **Count per (policy, matter); expire after 30 days** | FR-17. Matter grain matches what the user actually dismissed and the fact that rows **are** matters; per-subject grain would never suppress three dismissals of three different communications on one matter. The expiry stops a still-true condition being muted forever |
| **D-12** | ADR-052 requires naming the host, and the design never said where the evaluator or the closure pass run, or how often | **One evaluator, cadence by lane, two event hooks**: nightly ADR-036 `IScheduledJob` for both lanes, plus ADR-004 `IJobHandler` triggers on communication classification and `sprk_budgetrevision` create. Do lane nightly only | FR-11..FR-16. Driven by the finding that **two of four state transitions have no event** (death by time, birth by time), so event-only would never fire `ConditionCleared`. Cadence derives from the existing `sprk_lane` — **no new schema**. One component decides both directions, so they cannot disagree and miscount suppression |
| **D-13** (R-11 / C-1, decided 2026-10-07) | `sprk_dedupekey` is unique and the writer keeps Resolved rows Resolved, so a dismissed / acted / superseded / cleared Signal can never re-raise — breaking D-11's 30-day expiry, Do-lane re-raise and FR-16 supersede-and-re-raise | **Episode-scoped key** `{policycode}\|{type}\|{id}\|{episode}`, incremented when a subject re-raises after its Signal was resolved. Re-raise only when the subject is not suppressed (D-11) **and**: Decide lane — the per-rule **quiet window** after a dismissal has passed (default **14 days**, a knob in the rule body); Do lane — the subject's date changed since resolution. Publishing a new version supersedes open items and re-raises those that still hold, as a new episode | FR-03 (key), FR-16 (supersede → new episode), new FR-17a (quiet window). Task 007 changes the key composition in the schema/writer contract; task 030's writer computes the episode; **031 must implement it before writing its first Signal**; 033/034 per `notes/v4-reconciliation.md` rows 227–228. Existing Signals (dev seed only) are re-keyed as episode 1 |
| **D-14** (C-12, decided 2026-10-07) | Superseding a policy version needs the old version's in-force window closed, but no role can write `sprk_policyversion` | **Grant a write privilege** so publishing stamps the old version's `sprk_inforceto` (owner chose this over deriving the end from the successor's `sprk_inforcefrom`) | Role edit (owner-approved by this decision): add **Write** on `sprk_policyversion` to the role that publishes — **Spaarke Ontology Administrator** (the admin publish path); scope it to `sprk_inforceto` stamping in code (the version's rule body stays immutable after publish — enforced in the publish service, not by privilege). Verify the union of roles per NFR (append-only claim on versions now = "body immutable by code"). Lands with the Ontology admin publish slice; 031/033 read `sprk_inforceto` |

**v4 consolidated decisions (owner, 2026-10-07)** — from `notes/v4-prototype-vs-solution.md` (cited `#n`) and the earlier
open list. Each changes the requirements and the task plan; FR text is amended where noted.

| ID | Topic | Decision | Implementation impact |
|---|---|---|---|
| **D-15** | Signal visibility (#17) | R1: the evaluator **skips Restricted and Limited matters**; the worklist reads Signals **only through a BFF route** that checks the caller can read each row's matter; the Do lane shows only the reader's own work. Later: extend the access-control project's **secure-record mirror** to `sprk_signal` + `sprk_decisionrecord` (no data migration — secured matters have no Signals until then) | 031 skip; new BFF read route (worklist); follow-up issue to uac for the mirror. Remove `prvWritesprk_Signal` from Console User once the BFF closes Signals (D-17) |
| **D-16** | Rules R1 ships (#12) | Path B **plus** a grammar extension for the Do lane (subject-only rules, `now+Nd`, matter derivation for event / To Do / work assignment via `sprk_regardingmatter`) **plus the Threshold rule type** (spend threshold). Switch and the inquiry SLA are deferred | New tasks: grammar extension; Threshold schema + compiler; writer accepts Do-lane subjects. What a task filed under a project (no matter) does is decided in that task (skip, or group under the project's matter) and flagged |
| **D-17** | Recording a decision (#24, #16) | **One BFF commit route, record last**: validate → internal writes → email last → the one Decision Record listing actual outcomes → close Signals. On failure no record is written and the user sees which writes landed; idempotent per review; passes the #1312 route-authorization census. Console User loses Write on Signal | Task 043 (commit route); 040 writer; role edit |
| **D-18** | Revise budget (#26) | The **BFF writer creates** the budget revision (grant Create on `sprk_budgetrevision` to Spaarke Ontology Service), after checking the caller can write the target `sprk_budget`; the revision **also updates that budget's amount** | Role edit; commit-route action; which-budget picker when a matter has several |
| **D-19** | Approve variance (#27) | **Record-only action**: the Decision Record (outcome Authorized) is the approval; the UI says nothing else is written | Action definition only |
| **D-20** | Inquiry SLA (#28) | **Deferred after R1** (rule, Escalate / Extend SLA / Close inquiry). The budget inquiry send + reply disposition (FR-36/37) stay | Remove from R1 task scope |
| **D-21** | Assign Work next step (#31) | **This project builds the server-side create** for `sprk_workassignment` through the BFF (WP-3; secure-record root), so Assign Work is part of the decision | New task; coordinate with the work-assignment area owner; preserve the existing client wizard's business-unit cascade semantics server-side |
| **D-22** | Ontology admin (#41, #42, #47) | A **workspace tab "Ontology admin"**, shown only when a BFF capability probe says the caller can create `sprk_policyversion`; admin writes go through the BFF **as the caller**; grant the admin role Create/Write/Read on `sprk_triagecategory`; add the five tables to *Spaarke Platform* read-only; **assign the admin role to the owner's account in spaarkedev1** | Admin slices (a)/(b)/(c); role edits; probe route |
| **D-23** | Headlines (#10) | The **UI composes the row headline** from the Signal's display columns; the rule sentence stays literal; witness values (sender, date) are evidence lines. Task 022's template rule is unchanged | Row component (051) |
| **D-24** | UI kit (#20) | **Amend FR-28** to name the kit; reuse first (Fluent Accordion / MessageBar / Link, `ConfirmModal`, extend `MetricCard` with selected/note/progress); only the remainder become new shared components | FR-28 amendment; task 057 |
| **D-25** | Overdue day (#18) | Same rule as To Do generation (098): each item judged by its **assignee's time zone → owner's → UTC** | Evaluator computes "today" per item for Date Only comparisons |
| **D-26** | Modals | **Adopt** the canonical approach (`SprkModal` envelope + `WizardShell` engine, launched in-app, ADR-050 amendment = path B) **and migrate today's wizards off `navigateTo`** in this project (~12–17 dev-days, own phase) | ADR-050 amendment (main session, `.claude/`); modal phase tasks |
| **D-27** | Event due date | The Do lane and Reschedule use **`sprk_duedate` always**; the Daily Briefing changes to match; `sprk_finalduedate` becomes informational | Briefing reader change; evaluator; reschedule route |
| **D-28** | Event status column | **`statuscode` is authoritative**; `sprk_eventstatus` is deprecated (a task checks remaining readers before removal); Completed stays Active-state | Small cleanup task |
| **D-30** (O-1, 2026-10-07) | Severity source | A **severity column on `sprk_policy`** reusing the Signal's Info / Warning / Critical; UI labels High / Medium / Low; set per rule by the admin | Task 007 |
| **D-31** (O-5, 2026-10-07) | Overdue To Do rule | **In R1, for ALL To Dos.** A To Do filed under a matter groups under that matter; a To Do with **no matter** gets a Signal secured by the **To Do owner's business unit** (a second ownership path in the writer) | Tasks 037, 061 |
| **D-32** (O-19, 2026-10-07) | Off → On | Re-enabling a policy **re-raises** its PolicyRetired subjects that still hold, as new episodes | Task 031/033 |
| **D-33** (O-17, 2026-10-07 — **REPLACES D-15's skip**) | Signals on secured matters | Scoping (`notes/secure-signals-scoping.md`) found D-15's premise wrong: **Restricted/Limited only exclude external contacts** (ADR-003); the staff wall is the **Secure flag** (`sprk_issecure`), which D-15 did not skip. Owner: **register `sprk_signal` and `sprk_decisionrecord` as secure children in R1** (`SecureChildLineage.cs` + `config/secure-record-owner-role.json`, reviewed by the access-control project), writer change (~40–60 lines), role edits: **Read on both tables for the Secure Record Owner role; Basic Read on both for Spaarke Basic User; Assign on `sprk_decisionrecord` for Spaarke Ontology Service** (owner, never content). **No skip** except the narrow case of a Decision Record/Signal for a To Do under a Secure project with no matter. Restricted/Limited matters get Signals normally. Deploy order: role edits before the BFF carrying the config. Measure the 2-minute secure-sync job's load from nightly re-evaluation | New task (~3 dev-days); amend 008, 031, 037, 040, 079 (now coordination/review with uac, not a later mirror); D-15's BFF read route and Do-lane scoping still stand |
| **D-34** (2026-10-07) | Grouping record | Signals and Decision Records group under the item's **CORE record — matter OR project** — the same core-record notion the access-control project uses (`CoreAncestorResolver`, FR-26 stamps; matter and project are both CORE, a matter does NOT inherit from a project). Grouping, owning BU / secure-child protection (D-33), suppression counting (D-11 becomes per (policy, core record)) and the Decision Record's subject all follow the core record. Reuse `CoreAncestorResolver` — never a parallel derivation | Task 007 (project lookup + core-record fields on `sprk_signal` / `sprk_decisionrecord`), 031, 034, 037, 038, 039, 040, 050/059 (project rows in the worklist); spec FRs that say "matter" for grouping/suppression amended |
| **D-35** (O-21, 2026-10-07) | Items with no core record | A To Do (or other Do-lane item) with **no matter and no project** gets a Signal visible to the **To Do's owner only**, shown in their Do lane under a "Not filed" group; the core-record columns are optional for this case only | 007, 037, 038, 059 |
| **D-36** (O-22, 2026-10-07) | Core record types | Support **all four** core types the access-control project defines — **matter, project, work assignment, service request** — each its own worklist row, security and suppression grain. **The model must be extensible**: adding a future core type must not require changes to the evaluator, writer grouping, suppression or worklist code — it is driven by the access-control core taxonomy (`CoreAncestorResolver`) plus catalog data (e.g. `sprk_recordtype_ref`), with at most a schema addition (a lookup/catalog row) | 007 (schema shape chosen for extensibility — not one hard-coded column per type in logic), 031, 034, 037, 038, 039 (Secure WA too), 040, 059, 061 |
| **D-37** (O-23) | Two core records | The item's **direct filed-under (regarding) core record** wins; if ambiguous, matter over project | 037 |
| **D-38** (O-24) | Narrow skip | **Dropped** — no skips; every item is protected via its core record, or owner-only (D-35) | 031, 037 |
| **D-39** (O-25) | No-core items | Suppression per **(policy, item)**; the Decision Record is **owned by the item's owner** and visible like its Signal | 034, 040 |
| **D-40** (2026-10-07, task 024 escalation) | Date ranges in rules | A date field may take **one lower plus one upper bound** (e.g. `{">=": "now", "<=": "now+3d"}`) — nothing more (no OR, no third bound, no join); still one Dataverse filter. Needed so "due within 3 days" excludes overdue items | Task 024 |
| **D-41** (2026-10-07, task 024 finding) | To Do dates | `sprk_todo` due dates are UTC timestamps of the user's local midnight (not converted by 098). **Convert To Do date columns to Date Only as their own task**, modelled on 098 (inventory columns + readers/writers first, convert in spaarkedev1 with before/after evidence, fix readers/writers, per-environment procedure, own PR) | New task 106; 031 depends on it for per-item "today" (D-25) |
| **D-29** | Smaller | To Do composite score → **calendar days**, one shared function (boards re-rank once). Writer gets **AppendTo** on `sprk_communication`, `sprk_event`, `sprk_todo`, `sprk_workassignment` (closes F26). Caller-unresolved stays **#1312's single 403**. Tier 2 ADR Compliance timeout → **5 min** (own small PR) | Role edits; To Do scoring task; CI PR |

---

## 10. Assumptions

- **A-1**: The 30-day window is declared **per policy** in `sprk_rulebody` (decision 14 — knobs on the row),
  not as a global constant. The `now-30d` in design §8.0.1(a) is the first policy's value, not a platform
  setting.
- **A-2**: `sprk_recordclass` is **derived from which action was taken**, not chosen by the user — a bare
  completion or reschedule maps to `Routine`; send-email, create-follow-on and close-record map to
  `Judgement`; dismiss maps to `Dismissal`. The spec treats the mapping as code, not configuration.
- **A-3**: Criterion 2's *"does not fire when either is absent"* half needs **two negative controls** that the
  §8.1 checklist does not currently list: a matter with the classified communication **but** a budget revision
  inside the window, and a matter with a budget-exceeding snapshot **but** no classified communication.
  **Added to the seeding work** — without them criterion 2 is only half-tested.
- **A-4**: All 27 cleanup items are R1 deliverables per §5.0, **including** the one ongoing
  `Spaarke.Events.Components` migration. If that migration proves larger than S once started, it is the
  candidate to convert to a scheduled GitHub Issue rather than silently carried.
- **A-5**: The aggregate reconciliation Work Item (FR-29) is **not** a Signal — it is a computed count
  rendered in the worklist. Making it a Signal would make the association engine a policy producer, which it
  is not (design §5.1 reason 3).

---

## 11. Unresolved questions

- [ ] **Which rank function?** Criterion 7 and row-contract requirement 2 both demand a **deterministic**
  rank, and `sprk_rankscore` exists, but no formula is specified. Inputs available: severity, policy priority,
  `sprk_highpriority` on the subject (BR-3), age. *Blocks*: the worklist ordering task, not the evaluator.
  Resolvable at task-creation time; must not be left to the implementer's taste, because rank chosen ad hoc is
  rank that cannot be explained.
- [ ] **Does the Decide lane need its own Console tab before the Do lane exists?** BR-4 replaces the Daily
  Briefing tab only *once both lanes exist*, which leaves the Decide lane without a home during the interim.
  *Blocks*: nothing structural — it is a sequencing choice for `/project-pipeline`.
- [ ] **PR #1032 semantics review** — `TaskActionCore` now sets `statuscode = Open` for **every** consumer.
  Settled code rather than in flight, but a domain owner has not reviewed it. *Blocks*: nothing in R1; it is a
  correctness risk in a neighbouring domain that this project introduced.

### 11.1 Still open after D-13..D-29 (recorded 2026-10-07 while updating the task plan)

Each item is carried as an `<escalation><trigger>` in the task named; none blocks the critical path to 031 except
where noted.

| # | Open point | Source | Task |
|---|---|---|---|
| ~~O-1~~ | ✅ **Decided D-30**: a severity column on `sprk_policy` | reconciliation C-2, #1 | 007 |
| O-2 | **Rank formula** — §11 Q1 above; whether `sprk_highpriority` is the 2nd key | C-9, W-13, W-14 | 038, 062 |
| O-3 | **Record class** of *Reassign* and *Extend response date* | C-3 | 036 |
| O-4 | **Initial overdue threshold** — v4's 1 day or the collector's 5 | C-6 | 061 |
| ~~O-5~~ | ✅ **Decided D-31**: overdue-To-Do rule for all To Dos; no matter → Signal owned by the To Do owner's BU | C-7, W-15, S-4 | 037, 061 |
| O-6 | **Work assignment "mine"** — assignee or assigner (`sprk_createdbyperson`) | C-8, #3 | 038 |
| O-7 | Drop the row ⋮ menu (`DocumentRowMenu`) and the `OutcomeCard` extension | C-10, W-4, Z-15 | 052 |
| O-8 | **Console Decision Record tab** in R1 | C-14 | 045 |
| O-9 | **Association-confirmed trigger** (filing wakes the evaluator) | C-15, X-4 | 032 |
| O-10 | Where measured **recall** is stored for Classification admin | C-16, S-11 | 103 |
| O-11 | Source freshness / *Missing* beyond rendering a null fact as Missing | C-17, H-5 | 057 |
| O-12 | Which action the **Know-promotion rule** offers | C-19, X-5 | 063 |
| O-13 | **Gate tier outside a chat session** (the gate is chat-only) | #25 | 043, 070 |
| O-14 | **Assistant drafts** inside the wizard (facade call vs templated drafts) | #32 | 058 |
| O-15 | *Record the response* on a work assignment (no column; #3 recommends deactivate + record) | #3, #30 | 044 |
| O-16 | Which identity writes the **new budget amount** onto `sprk_budget` (D-18) | D-18, #26 | 044 |
| ~~O-17~~ | ✅ **Decided D-33** (replaces D-15's skip): Signals and Decision Records become secure children; one narrow skip | D-15 | 031, 039 |
| O-18 | Does the Inquiry still stamp `sprk_responseduedate` now the SLA is deferred? | D-20 | 070 |
| ~~O-19~~ | ✅ **Decided D-32**: yes, still-true subjects re-raise as new episodes | D-13 | 031, 033 |
| ~~O-20~~ | ✅ **Dissolved by D-33**: Restricted/Limited restrict only external contacts; gate records on Secure matters are secure children like any Decision Record | D-15 | 042 |
| ~~O-21~~ | ✅ **Decided D-35**: no matter and no project → owner-only Signal in the owner's Do lane under "Not filed"; the core-record columns are optional for this case only | D-31 | 007, 037, 038, 059 |
| ~~O-22~~ | ✅ **Decided D-36**: all four core types (matter, project, work assignment, service request), each its own row, security and suppression grain; extensible through the core taxonomy + `sprk_recordtype_ref` catalog | D-34 | 007, 031, 034, 037, 038, 039, 040, 059, 061 |
| ~~O-23~~ | ✅ **Decided D-37**: the direct filed-under (regarding) core record wins; if ambiguous, matter over project | D-34 | 037 |
| ~~O-24~~ | ✅ **Decided D-38**: the narrow skip is dropped; no skips anywhere | D-33, D-34 | 031, 037 |
| ~~O-25~~ | ✅ **Decided D-39**: no-core items suppress per (policy, item); their Decision Record is owned by the item's owner and visible like its Signal | D-35 | 034, 040 |

---

*AI-optimized specification. Original design preserved at [`design.md`](design.md) (rev 10).*
