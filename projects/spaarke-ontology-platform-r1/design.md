# Spaarke Legal Operations Intelligence — Ontology Platform R1 · Design

> **Status**: **DRAFT for review — rev 4**, 2026-10-01. Not yet through `/design-to-spec`.
> **Evidence base**: [`notes/mvp-technical-spec.md`](notes/mvp-technical-spec.md) (~800 lines — field-level
> detail, live-verified schema, defect forensics). **This document holds the decisions; the spec holds the
> evidence.** Where they disagree, this document is newer.
> **Vocabulary**: [`notes/ontology-component-model.md`](notes/ontology-component-model.md) §3 is authoritative.
> **Review input**: [`notes/ontology-architecture-feedback.md`](notes/ontology-architecture-feedback.md)
> (2026-09-30). Rev 3 applies its findings; §8 carries CM-6..CM-11 from its §6.
> **Rev 4 inputs** (owner feedback 2026-10-01): [`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md)
> (how the shipped Briefing's items enter the worklist) · the **Console prototype** at
> `c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\` (v2.1, findings 1–17) — the
> **UI/UX contract for this project**, see §11 and §12.
> **Audience**: owner review · `/design-to-spec` · `code-review` · future sessions.

---

## 0. The differentiation test — binding

> **For every capability we scope, name the data it requires that the incumbent does not hold. If we
> cannot, it is not differentiated — however good the architecture underneath is.**

Inlined here in rev 3: decision 12, success criterion 9 and §1.1 all cite "§0", and it previously existed
only in `notes/mvp-technical-spec.md`. A binding test that lives in another document is a dangling
reference in the document that claims to hold the decisions.

| Category | Differentiated? | Why |
|---|---|---|
| Single-source computation — budget variance, rate compliance, accruals, invoice-line review | ❌ **No** | Legal Tracker, Onit and Brightflag already do this, often with AI. Budget **and** spend both live in e-billing |
| **Requires data the incumbent does not hold** — email, documents, requests, obligations | ✅ Yes | Structurally impossible for them |
| **A rule about an object they do not model** | ✅ Yes | You cannot write a rule about a thing their schema lacks |
| **The record of what was decided and what resulted** | ✅ Yes | They *alert*; they do not capture the intervention or its typed outcome — and it **compounds** |

### 0.1 Budget variance is the plumbing proof, NOT the pitch

The spend loop exercises every pipeline stage at the lowest cost, which makes it the right *first*
implementation. **It is not a differentiated demo and must not be presented as one** — the customer's honest
answer is *"we can already do that."*

### 0.2 The signal entity must not be spend-shaped

`sprk_spendsignal` has a `sprk_snapshot` lookup, so it is structurally spend-only. The second signal type
forces generalization anyway, and it is far cheaper now, while the table has zero client references and no
production data (§5, deferred-in-project).

### 0.3 A capability must TEST what its message CLAIMS `[added rev 3]`

The generalized form of the review's blocking finding. The first cross-source predicate read **no budget
field** while its message asserted *"unreconciled against its budget"*. Data being *theoretically available*
is not the test — **the predicate must actually read it**. A capability that names data in its output but not
in its `where` clause fails §0 regardless of what the platform holds, and it fails [AP-12](../../.claude/FAILURE-MODES.md)
as runtime-generated prose stating something the code never established.

Corollary: the **fourth** row of the table above is unexercised by detection alone. Recording *what was
decided and what resulted* needs an action that changes something in the world — which is why criterion 10
was restored.

---

## 1. What this project is

**One sentence**: consolidate data from the customer's third-party systems, resolve it against Spaarke's
objects, match it against the customer's own declared rules, and record what was decided.

The ontology is not a new database. It is a **declaration layer** over Dataverse — objects and links,
binding declarations, attribute ownership, fact supply, Policy, actions and gates, and a Decision Record —
plus the machinery that walks it: **bind → resolve → compute facts → match to action → execute under gate →
record**.

### 1.0 The project boundary — intelligence forward `[rev 4]`

**R1 builds the intelligence layer from the Spaarke data model forward. It does not build the connector that
brings a system of record's data in.** Every capability is specced against data that is *already in Dataverse* —
either because Spaarke **is** the system of record for it (matters, communications, documents, tasks, memos) or
because a later phase, with the customer, mirrors it in (invoice, budget, spend metrics).

Consequence: the pipeline's **bind** stage is **declared, not built**. R1 owns the *landing contract* — the
pointer columns and attribute ownership — so that when a connector arrives it has somewhere to land and the UI
can state freshness. R1 does **not** own the connector, the parser, the sync engine or the scheduler
(owner decision 2026-10-01; §5 Out, with the one carve-out in §5 In).

**No LEDES.** Invoice, budget and spend-variance values arrive from the e-billing platform as **computed
metrics**, not as raw LEDES files for us to parse. LEDES intake is not in R1 and is not the near-term plan
(owner decision 2026-10-01 — this supersedes `mvp-synopsis.md` §4.1 as originally written, and is why half the
"binding story needs no connector" argument there no longer applies).

**The differentiating claim, stated as the predicate actually tests it** (Path B — §8 CM-6):

> A communication on this matter was classified as a **fee or scope change** within the window, **and** no
> **budget revision** was recorded in that same window.

Two conjuncts, two sources. An e-billing system holds the budget and never sees the email; a mail system sees
the email and holds no budget. **Only a system holding both can evaluate the conjunction** — which is what
makes this pass §0 rather than merely sound like it does.

⚠️ **What the claim does NOT assert.** It does not compute variance, does not compare invoiced to budget, and
does not say the commitment exceeds the budget. The spend snapshot is attached as **evidence**, so a human
sees the budget position, but the rule asserts no comparison it did not make. Rev 1 of this document claimed
*"the budget does not reflect it yet"* over a predicate containing **no budget term at all** — the §0.3
failure, committed on the headline claim.

Everything else in the MVP exists to make that one sentence computable, recordable, and re-tunable without a
deployment.

### 1.1 What this project is NOT

- **Not budget variance.** Legal Tracker, Onit and Brightflag already ship it. It is the plumbing proof, never
  the pitch — the customer's honest answer is *"we can already do that"* (spec §0).
- **Not a connector project.** See §1.0. The data is assumed present; R1 earns its value on what it does with it.
- **Not the Action Engine — and the distinction is precise** `[rev 4, answering owner item 7]`. An **Action** is a
  *data row*: an Action + an ADR-039 Binding, dispatched by the spine that already ships —
  `ConfirmationPolicyEngine` · `GateDecisionV2` · `SideEffectGateAIFunction` · `OutputRouter` and its eight
  dispositions. The **Action Engine** is the management plane *above* that spine: tool registry, action
  definitions, templates, triggers, scheduling, three invocation paths, run records, meta-tools. R1 needs the row
  and the shipped spine (decision 9). If the Action Engine is later built, R1's rows are its **input**, not a
  rewrite — which is what makes deferring it safe rather than merely convenient.
- **Not the Insights Engine — that already exists** `[rev 4, answering owner item 7]`. Verified 2026-10-01 on
  master: `Services/Insights/{Graph,LiveFacts,Observations,Precedents}`, `InsightsOrchestrator`,
  `InsightArtifact`, `EvidenceRef` and the Insight endpoints are shipped. R1 **consumes** it — `ILiveFactResolver`
  for facts (§5), and the Fact / Observation tiers for the row's epistemic rendering — and **adds one new output
  type**, the signal. What is deferred is not the engine but **CM-1**: moving *Policy evaluation* inside it behind
  an `IPolicyEvaluator` contract, which waits for a second consumer.
- **Not Authority.** Every action is human-confirmed in the MVP, so the human *is* the authority (decision 8).
- **Not a migration.** The line between overlay and system-of-record is **authority, not storage**.
- **Not an agent.** The LLM classifies, bounded by data; deterministic code evaluates; a human acts
  (decision 15). A predicate evaluated by a model makes the Decision Record an anecdote.

---

## 2. Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                              <!-- LANDED: RiConfidenceScorer, CommsPolicyOptions, CommunicationRuleGate, CommunicationRiActionService, TaskActionCore, ActionSeam/IActionSeam, DailyBriefingCollector. PLANNED: predicate evaluator, Decision Record writer, LookupChoicesResolver guidance injection -->
  <spaarke-ai>Y</spaarke-ai>                <!-- SETTLED rev 4: D-3 resolved by the Console prototype — the worklist needs a new row component (prototype findings 9, 10), so this is Y permanently, not provisionally. The "sprk_gridconfiguration row alone" branch that would have made this N is dead -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>Y</skill-directives>    <!-- .claude/FAILURE-MODES.md (AP-14 + an AP-12 instance) and .claude/CHANGELOG.md. NOT .claude/skills/** -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

> **This block is a live contract, not a one-time declaration.** `/conflict-check` consumes it, so an
> ambiguous or stale entry is itself the hazard. When D-3 resolves, correct `spaarke-ai` here in the same
> change. Over-declaring to be safe is acceptable only as a *temporary* state with a named decision that ends it.

**Coordination.** This project has already merged into **communication-intelligence territory** (PR #1032) —
`CommunicationRiActionService`, `CommunicationRuleGate`, `DailyBriefingCollector`, `TaskActionCore`. That code
is settled rather than in flight, but a domain owner must review the semantics, in particular
`TaskActionCore` now setting `statuscode = Open` for **every** consumer. `/conflict-check` before every BFF PR.
`spaarke-legal-front-door-r1` is pre-spec and must be told that CM-5 likely absorbs its proposed
`sprk_legalrequest`.

---

## 3. Placement justification (CLAUDE.md §10 / §11)

### 3.1 Already landed (PR #1032)

Every row modifies an existing file; no new service, no DI change, no new package, no endpoint.

| Component | Existing overlap | Extend or new? | Cost of doing nothing |
|---|---|---|---|
| `RiConfidenceScorer` | itself | **Changed in place** — product → weighted sum | A communication the engine could not file scored exactly 0 regardless of urgency, so the notification path could never fire for it. One asked approval of ~$140–145k and produced nothing |
| `CommsPolicyOptions` | itself | **Extended** — threshold 0.8 → 0.45, two due-day fallbacks | 0.8 was unreachable under the sum (max 0.825 at High); 0.35 would have authorized ~90% of mail |
| `CommunicationRiActionService` | itself | **Fixed in place** — read a lookup by shape, set due dates | An `InvalidCastException` inside the NFR-05 swallow killed the whole RI action while the logs read as success |
| `TaskActionCore` | itself | **Extended** — set `statuscode = Open`, write `sprk_finalduedate` | Every task the platform created was invisible to the briefing that exists to surface it (49 rows stranded in Draft) |
| `DailyBriefingCollector` | itself | **Fixed in place** — `sprk_eventdescription` → `sprk_description` | The column does not exist; Dataverse rejected the retrieve, so the briefing was blind to tasks entirely |
| `Deploy-ActionMirrors.ps1` | itself | **Extended** — JPS mirrors + sidecar schemas | Could not deploy a JPS mirror and printed `UNCHANGED`; same failure class its own header documents, hidden behind green output |

**No new NuGet packages. Publish-size delta ≈ 0** (one private method, two int properties); measured per §10
bullet 4 — 45.58 MB vs a 45.42 MB recorded baseline, the difference being master drift, not this branch.

### 3.2 Planned

| Component | Existing overlap | Extend or new? | Cost of doing nothing |
|---|---|---|---|
| **Cross-source predicate evaluator** | `SignalEvaluationService` (`ISignalRule` strategy, spend-only, threshold-shaped); `CommunicationRuleGate` (scope + threshold, **no predicate at all**) | **New, modelled on both** — reuse `CommunicationRuleGate`'s scope-match semantics verbatim (blank tenant = all, empty matter = all, lowest `sprk_priority` wins, deterministic id tiebreak, fail-closed) and `ISignalRule`'s strategy shape. Neither can be extended: one is bound to `sprk_spendsnapshot`, the other has no rule body | The differentiated claim is not computable. This is the **only** item whose value is unproven; items below are mechanism |
| **`sprk_policy` + `sprk_policyversion`** | `sprk_communicationrule` — a shipped declarative rule table with enable/priority/scope/threshold | **New, copying its column semantics** — it has **no rule body**; a predicate over arbitrary subjects is a different shape, not a column addition | No versioned, immutable statement of what the rule was when it fired. Without the version, the Decision Record cannot be defended |
| **Decision Record** | `CommunicationRuleDecision` **already carries the full decision on both authorize and deny paths** and is written to `ILogger` and lost. Also `SessionGate` (ADR-040 — gate ledger, no authority/policy-version/evidence) and `sprk_emailreviewlog` (append-only, domain-specific, apply-time re-validation) | **New table; persist the EXISTING value.** One row inserted from `RuleGatedAssessedConsumer`, which already holds the object — **a line before the branch. The gate is not touched.** `sprk_emailreviewlog` becomes a producer, not a migration | Suppression is impossible (a signal dismissed three times keeps firing → users mute the system). Thresholds stay guesses forever. No answer to *"why did nothing happen?"*, which Dataverse audit structurally cannot give because nothing changed |
| **`sprk_signal`** (generic) | `sprk_spendsignal` — computed, stored, marked active, **zero client references** | **Replace while it is free** — its `sprk_snapshot` lookup makes it structurally spend-only. ⚠️ `sprk_signaltype`/`sprk_signalvalue` are **taken** (columns on `sprk_affinity`) | A communication- or memo-derived signal will not fit; generalizing later costs a data migration instead of nothing |
| **Worklist** | `sprk_gridconfiguration` + `<DataGrid configId=… />`; `needs-review.gridconfiguration.json` computes membership in fetchXml and renders via `DataGridOverrides.columnRenderers` | **Reuse the membership mechanism; BUILD the row component.** ⚠️ **Corrected rev 4** — rev 3 said "author a ROW, build no UI". The prototype disproved it (finding 9): the row contract needs expandable evidence tiers, a *Why this fired* disclosure, outcome cards and a gate, none of which a column renderer gives. Membership still comes from configuration; the **row is new UI, built once** (finding 10: one row component carried all three signal shapes) | Signals stay invisible, exactly as `sprk_spendsignal` has been since it shipped |
| **Fact supply** | **`ILiveFactResolver`** — already keyed on `(subject-scheme, predicate)` with `Matter` / `Invoice` / `Project` implementations, plus `SubjectParser` + `SubjectSchemeCatalogOptions` | **Reuse, extend only if a predicate is missing** | The evaluator would otherwise grow its own per-entity fact readers, and we would end up with the per-entity snapshot tables §5 now rules out |
| **`sprk_memo` classification** | `triage-email` Action + `$choices` + `LookupChoicesResolver`. ⚠️ `agreement-classify` uses a **bespoke** C# assembler — the anti-pattern not to repeat | **One new Action row, reusing the `$choices` contract** | The commitment made on a phone call is never captured. Human-authored, so accuracy is high and cheap |
| **Guidance injection** | `LookupChoicesResolver` resolves `sprk_name` only | **Extend** — emit `name — guidance`, schema `enum` unchanged | The ten authored `sprk_classifierguidance` rows are inert; the model still sees bare labels and cannot tell `Fee / rate change` from `Invoice / Billing` |

---

## 4. Why the MVP is smaller than it looked

Four real emails on 2026-09-29 proved that **most of what the ontology needs was already built and had simply
never run**. Seven of eight defects were in code shipped months earlier that had never executed its happy path.

**Now demonstrably working** (record ids in spec §10.6, §17):

- classify → bounded category → triage persist, with a **zero-deployment** taxonomy (a new row is live on the
  next enrichment: resolved per run into the prompt *and* as a constrained-decoding `enum`)
- the policy gate — scope match, threshold, fail-closed, decision produced on **both** paths
- the whole notification spine — `sprk_event` task + outbox + SignalR ping + app-notification — and Daily
  Briefing surfacing

**Still net-new**: the seven rows in §3.2. Only the first carries product risk.

---

## 5. Scope

### 5.0 The scope rule

**Owner directive, 2026-09-30**: *"if there is work identified in the course of creating this project that
can be addressed most efficiently in this project, then that is the correct approach -- do not defer and hand
off if can be done more efficiently now. The key/critical point is we do not want to bury or miss or lose any
work items -- so they must be either fixed or scheduled, not merely added to a list."*

Scope is therefore decided by **where the work is cheapest**, not by topical purity -- and **nothing is ever
merely listed**:

| Situation | Disposition |
|---|---|
| This project already holds the diagnosis; re-deriving it later costs more | **Pull into §5 In.** Items 6 and 7 below arrived this way |
| Belongs to another domain, or needs a decision this project cannot make | **[`notes/defer-issues.md`](notes/defer-issues.md) + a GitHub Issue.** `push-to-github` Step 1.6 blocks a push if an entry has no Issue URL |
| Genuinely out of scope | **§5 Out**, with rationale |

### In

Grouped by what they serve. **Referenced by NAME, not number** — rev 2 renumbered this table and left §8
pointing at stale indices, so numbers are no longer load-bearing anywhere in this document.

**The differentiated path** — these four together are the §0 claim; none of them is useful alone:

| Item | Shape | Note |
|---|---|---|
| **Cross-source predicate evaluator** | code + `sprk_policy` / `sprk_policyversion` | ⚠️ **Unblocked rev 4** — D-1 resolved (seed dev data, §8.1). Needs the §8.1 exit pair present before criterion 2 can be demonstrated, but is no longer gated on an investigation |
| **`Existence` rule type** | one addition to the closed rule-type set + its JSON Schema | **New in rev 3 (CM-7).** The §11.4 predicate is an EXISTS over a related entity; MVP allowed only `Threshold` and `Switch`, and spec §3.5 refuses to save an invalid body — so **the differentiated capability was literally unsavable**. Implemented as a FetchXML `link-entity` filter, so CM-3 holds |
| **`sprk_budgetrevision`** | new table, smallest possible | **New in rev 3 (CM-10, now answered).** `sprk_budget` carries `sprk_totalbudget` + `modifiedon` only — **no revision history exists, and Dataverse auditing is not a fallback** (component model §6: zero `RetrieveRecordChangeHistory` usage, no audit policy). Path B's second conjunct is not expressible without it |
| **Inquiry action** | one Action row + one Binding | **Restored in rev 3 (CM-11).** Present in `mvp-synopsis.md` §4.4 as *data, not build*; it fell out of rev 1 because §5 was rebuilt as "what is left to **build**". Without it the §0 table's fourth row is unexercised and nothing compounds into the Report Card |

**Mechanism** — these work, and carry no product risk:

| Item | Shape | Note |
|---|---|---|
| **Decision Record** | table + a one-line writer | Needs D-2. `sprk_factsnapshot` is **mandatory**, not optional (§8 D-2) |
| **Worklist** | a `sprk_gridconfiguration` row | Needs D-3. Row contract in review §4 — it must offer at least one action that changes something |
| **`sprk_memo` as signal source #2** | one Action row + taxonomy decision | Needs D-4 |
| **Guidance injection** | small change to `LookupChoicesResolver` | No decision needed. The ten authored `sprk_classifierguidance` rows are inert today |

**Already-built signals — the Daily Briefing dissolves into the worklist** `[rev 4, owner items 2 + 3]`.
Full analysis: [`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md). Four of the six
rows below are **configuration or deletion**, which is why this broadens the MVP without lengthening it:

| Item | Shape | Note |
|---|---|---|
| **Do lane — Temporal policies over work Spaarke already holds** | 3 policy rows | Overdue task · task due within 3 days · work assignment past `sprk_responseduedate`. Membership moves from `DailyBriefingCollector`'s own queries to **declared rules**, which buys a stated reason, versioning, suppression, freshness and a deterministic answer to *"why isn't this here?"* — with no new evaluator and no new widget |
| **Know → narrative + Context pane** | configuration | New/updated matters, projects, documents and monitored-record activity **fail row-contract requirement 4** (nothing to do that changes anything), so they are not rows. They stay as awareness, where awareness belongs |
| **First Know-promotion rule** | one policy row | *New matter with no budget after 5 days* — `Absence` shape over Spaarke-held data. *"A new matter was opened"* is news; *"a new matter has no budget"* is work. **That difference is the ontology**: membership by rule evaluation, not by recency |
| **Retire *Critical Today* as a list** | configuration | `sprk_highpriority` becomes a **rank input**; `sprk_monitor` becomes a **subscription** to Know. Neither is a reason in the ontology sense — a flag says *someone cares*, not *what is wrong*. Nothing it meant is lost |
| **Remove the LLM-chosen "Top action"** | deletion | The first row *is* the top action, because rank is deterministic. A model choosing priority breaks row-contract requirement 2 and decision 15 |
| **Email awaiting a matter match** | 1 policy row + a scope check | The association ladder already produces this; it surfaces as a Do row where resolution came back Ambiguous. ⚠️ Carries prototype finding 7 — **misresolution dismissals must not count toward suppression**, or fixing our own bad match teaches the system to stop asking |

⚠️ **§0 still applies to all six.** Overdue tasks and new matters are **not differentiated** — every
matter-management system shows them. They join for completeness and adoption (*"this is where I start my day"*),
never for the pitch. Their presence creates a new obligation: the **lane split, Decide above Do**, exists to stop
them burying the cross-source flags that justify the product. On the production Briefing's own numbers, 11
overdue tasks would push 5 decisions off the screen.

⚠️ **Dependency**: a due-date rule is only as good as the task data under it. [ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050)
(49 `sprk_event` rows stranded in `Draft`) must land before the Do lane ships, or the lane under-reports.

**The minimum Connection Engine work — columns, not machinery** `[rev 4, owner item 6]`:

| Item | Shape | Note |
|---|---|---|
| **Landing contract only** | columns + freshness surfacing | The pointer columns (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`) and attribute ownership on any entity a later ingest will touch. **In R1 for two reasons**: it is free now and a data migration later (component model §11), and **freshness is load-bearing in the UI** — prototype finding 3, *an absence clause is only as true as its source is fresh*. A `notExists` conjunct evaluated over a stale mirror is a false negative wearing a confident face, which is the §0.3 failure in a new costume |

**Repairs pulled in per §5.0** — this project holds the diagnosis, so fixing is cheaper than handing off:

| Item | Shape | Note |
|---|---|---|
| **Space-bearing matter-number tokenizer** | `IdentifierReverseLookupRung` regex + tests | `WellFormedTokenPattern` needs an alpha prefix *immediately* followed by `-`/`.`, so live matter `"Form D - 2023"` never tokenizes and `ExplicitReference` never fires — even with the number *and* name verbatim in the subject. Ships with a measured query-count delta (ADR-045 precision/cost) |
| **The false association `reason` string** | one interpolation + a test | Reports *"Reinforced confidence 0.97 in [0.50, 0.85)"*; 0.97 is not in that band. The band is computed from `topDeterministicConfidence` while the message interpolates `topConfidence`. AP-12 in **runtime-generated prose** |

**Deferred from this discussion, to be resolved in this project's design/spec** (owner, 2026-09-30):

| Item | Why it is not settled yet |
|---|---|
| **Generic `sprk_signal`** | Replacing `sprk_spendsignal` is free **now** (zero client references, no production data) and a migration later. ⚠️ **Two of its three dependencies are now closed (rev 4)**: **D-3** is settled — the worklist **reads signals and groups by matter**, so the shape needs a **polymorphic subject plus an always-populated matter lookup** derived from it — and **D-4** is settled (reuse `sprk_triagecategory`). What remains is **D-2**, plus **BR-1**'s nullable decision reference and **D-7** (sibling of `InsightArtifact`, not subtype). ⚠️ `sprk_signaltype` / `sprk_signalvalue` are **already taken** on `sprk_affinity`. **Per review §1.4 the shape question now also covers resolution semantics, not only columns** (CM-9): a `sprk_dedupekey` alternate key, an `sprk_resolutiontype` option set (`ConditionCleared`/`Acted`/`Dismissed`/`Superseded`/`PolicyRetired`), `sprk_lastevaluated`, and a resolution **sweep** so a flag whose predicate stops holding is closed rather than left to rot. `sprk_spendsignal`'s idempotent upsert works today only because its key is implicitly (matter, signaltype) — that does not survive a polymorphic subject. **Decide before the evaluator writes its first signal** |

### Out (with rationale)

| Item | Why |
|---|---|
| **Connection Engine** — connector, parser, sync engine, scheduler | **Out by owner decision 2026-10-01** (§1.0). R1 is intelligence-forward: it assumes the data is in Spaarke, whether mirrored by a later phase with the customer or because Spaarke is the system of record. **The carve-out is the landing contract in §5 In — columns, not machinery.** Rev 3 called this "an open owner decision"; it is now decided |
| **LEDES intake** | **Out, and not the plan** `[rev 4 — owner items 4, 8-1, 8-7]`. Invoice, budget and spend-variance arrive as **computed metrics from the e-billing platform**. Parsing LEDES opens code mapping, which is work for when we are the system of record. This retires the synopsis §4.1 claim that *"half the binding story needs no connector at all"* |
| **The Action *Engine*** (not Actions) | R1 needs an Action **row** plus the shipped ADR-039 dispatch spine; the management plane above it is R2, and R1's rows are its input, not a rewrite (§1.1) |
| **Moving *Policy* into the Insights Engine** (CM-1) | ⚠️ **Re-worded rev 4** — the Insights Engine is **built and consumed** by R1 (§1.1). What waits for a second consumer is only the `IPolicyEvaluator` contract. Rev 3's phrasing read as though Insights itself were out |
| ~~Unifying `sprk_spendsignal` with `InsightArtifact`~~ | **Obsolete as written** `[rev 4 — owner item 7]`. `sprk_spendsignal` is being **replaced** by generic `sprk_signal` (§5 deferred-in-project), so there is nothing left to unify. The live question is whether `sprk_signal` is an `InsightArtifact` subtype or a sibling — now **D-7** |
| Authority | The human is the authority while every action is confirmed (decision 8) |
| MCP server | Deferred to `spaarke-mcp-server-r1`, which must absorb `spaarkeai-word-native-r1`'s off-master design |
| Bitemporal / as-of | Immutable policy **versions** plus a mandatory `sprk_factsnapshot` buy the defensibility without it — see **§8.2**, which explains what is and is not given up |
| Quantifying the commitment | Extracting "$45k" from prose is new extraction work that is sometimes wrong. **The join is the differentiator, not the arithmetic.** v2 |
| Per-entity fact snapshot tables | `ILiveFactResolver` + native rollup/calculated columns already cover fact supply (CM-3). `sprk_spendsnapshot` is **one materialization, not a pattern to replicate** — see §8 D-8 |

---

## 6. ADR tensions (CLAUDE.md §6.5)

| ADR | Tension | Proposed path |
|---|---|---|
| **ADR-039** — Binding owns dispatch routing | A Policy evaluator is a second *rule* surface. `CommunicationRuleGate` already accepted this for its own store (owner decision 2026-07-22, `notes/041-rule-store-decision.md`) | **SURFACED — path deferred by owner decision (2026-09-30): "evaluate/define/revise this ADR once we get the solution actually decided."** The candidate is (A) project-scoped exception, on the reasoning that Policy decides *whether a claim is true* while Binding decides *what executes* — different axes. **Not adopted.** Must be resolved before the evaluator ships; §6.5 forbids silent compliance either way |
| **ADR-040** — session ledger | The Decision Record overlaps `SessionGate`, which carries `Kind`/`Status`/`SideEffectClass`/`BindingId`/`Turn` but **no** authority, policy version, evidence or confirmer | **SURFACED — path deferred, same owner decision.** The candidate is (B) amendment: ADR-040 is chat-session-shaped and Redis→Cosmos-tiered, while a Decision Record must be a durable Dataverse row queryable per matter, so extending ADR-040 to name the two as siblings beats forcing one to serve both. **This is the heavier claim and the likelier to be wrong** — it needs the Decision Record shape settled first (D-2) |
| **ADR-015** — privilege is flagged, never decided | The Decision Record must carry `PrivilegeFlagged` forward without acting on it | **(C) Comply.** The gate already does this; the writer copies the flag and never branches on it |
| **ADR-013** — `PublicContracts` facade | The evaluator must not inject AI-internal types | **(C) Comply.** It consumes `LiveFactResolver` outputs and Dataverse reads only |
| **ADR-045** — association engine | Fixing the space-bearing matter-number tokenizer changes the engine's precision/cost profile | **Deferred as its own decision** (D-6). Not bundled into this project |
| **ADR-024** — polymorphic regarding | Reused unchanged as the extensibility seam for every signal source | No tension |
| **ADR-038** — testing strategy | A shape-only unit test **pinned** a non-existent column for months (spec §17). Coverage was green over a query that always threw on every run | **(C) Comply**, and the spec carries two rules, per owner decision 2026-09-30 (*"tests should be actually testing what exists (or should exist) — and if they consistently throw errors identify why and fix"*): **(i)** any test asserting a Dataverse **column list, entity name, or option-set value** must be paired with something that touches the real schema, or it asserts nothing about reality; **(ii)** a path that throws consistently is a **defect to diagnose**, never noise to tolerate — the `sprk_eventdescription` retrieve failed on *every* briefing run for months and nobody read the exception |

---

## 7. Success criteria (measurable — for the spec)

1. A Policy row's predicate can be changed, and the change takes effect, **with no deployment**.
2. The cross-source rule fires on a matter that has **both** an over-budget snapshot and a scope/fee-classified
   communication — and does **not** fire when either is absent.
3. Every authorize **and** every deny writes a Decision Record; the deny path is queryable per matter.
4. A signal dismissed three times on the same matter stops being raised (suppression reads the record).
5. `sprk_confidencethreshold` is set from the observed confirmed-vs-dismissed distribution, not by guess.
6. A second signal producer (`sprk_memo`) writes the **same** `sprk_signal` shape as the first, with no
   change to the consumer.
7. ⚠️ **Amended rev 4.** The worklist's **membership and column/action configuration** come from a
   `sprk_gridconfiguration` row with no code change; **one row component** renders every signal shape.
   *(Rev 3 said "zero new UI code". The prototype disproved it — finding 9: the row contract needs expandable
   evidence tiers, a "Why this fired" disclosure, outcome cards and a gate host, none of which a column renderer
   provides. Finding 10 is the recovered half of the claim: **one** component carried all three shapes, so the
   cost is build-once, not build-per-signal.)*
8. A new taxonomy category — with guidance — changes classifier behaviour with no deployment.
9. **The §0 differentiation test passes** (§0): for every shipped capability, we can name the data it
   requires that the incumbent does not hold — **and the predicate reads that data**, per §0.3.
10. **Something happens in the world.** A confirmed signal produces an Inquiry through the existing gate; the
    Inquiry carries an SLA; the reply resolves it with a **typed outcome**<sup>†</sup> queryable per matter and
    per outside firm.

    > <sup>†</sup> ⚠️ **Term provisional — pending CM-4** (§8.0a). Component model §3 marks *disposition*
    > ✅ Keep and flags *typed outcome* with "consider reusing disposition". If CM-4 resolves as recommended,
    > **"typed outcome" is swept from this criterion and from all four project documents** (15 occurrences) in
    > favour of **disposition**. Do not harden either phrasing into a schema name until then. *(Restored rev 3 from `mvp-synopsis.md` §6 criterion 5 — see review §1.3. Criteria 1–9
    cover detect, record, surface, suppress, tune and render; **not one of them required an effect**, which
    leaves the §0 table's fourth row unexercised and is also how an incumbent alerting product behaves.)*
11. **Classifier recall meets a stated floor.** Recall on `Scope / budget change` and `Fee / rate change` is
    measured against a labelled set, with the floor recorded. *(New rev 3 — review §1.6b. The predicate is a
    **conjunction**, so it inherits its weakest input, and that input is the LLM classifier. At 70% recall
    the differentiated claim silently misses 30% of real cases and every other criterion still passes.
    Spec §16.5 nominates the first eval case; the floor makes it a gate.)*

**Standing metric, not a criterion** — *action rate per policy* = acted ÷ surfaced, a zero-code Dataverse
rollup. Below roughly half, a policy is generating noise and is a candidate for retirement or
re-thresholding (review §4.4).

---

## 8. Open decisions

### 8.0 Resolved in rev 4 (owner decisions 2026-10-01)

| ID | Was | Resolution |
|---|---|---|
| **D-1** | Spend-data scoping spike | **RESOLVED — seed dev data.** Owner: *"we can manually or systematically add test data to dev."* The one-day investigation collapses to a **seeding checklist** (§8.1, rewritten). D-1 is **no longer the critical path**, which unblocks every build item. Two obligations survive: the smallest `sprk_budgetrevision` still has to be created (CM-10), and the §8.1 **exit pair** must exist in dev before criterion 2 can be demonstrated |
| **D-3** | Worklist surface · rows = matters or communications? | **RESOLVED by the Console prototype** (its README §"D-3 recommendation"). **Rows are matters; the worklist reads signals and groups them by matter.** One-row-per-communication strands two of the three shapes (threshold and SLA have no communication subject); one-row-per-flag hides that a single inquiry can answer two flags. **Both surfaces, split by verb**: Console acts, the MDA authors/administers/audits. **Consequence for `sprk_signal`**: a polymorphic **subject** (the SLA flag is *about* an Inquiry) **plus an always-populated matter lookup** as the grouping key, derived from the subject. `spaarke-ai` stays **Y** (§2) |
| **D-4** | `sprk_memo` taxonomy | **RESOLVED — reuse `sprk_triagecategory`.** One bounded set keeps cross-source rules simple and the zero-deployment taxonomy property intact |
| **D-5** | MM connector in MVP? | **RESOLVED — out of scope** (§1.0 / §5 Out). Not deferred-as-undecided; decided |

### 8.0a Still open

| ID | Decision | Blocks |
|---|---|---|
| **D-2** | Decision Record field list — approve or amend. **Four constraints now**: (i) `sprk_factsnapshot` is **mandatory** — it is what lets you reconstruct *what the data said*, the residual gap left by ruling out bitemporality (**§8.2** explains this); (ii) a **nullable action reference** (a deny-path record has no action); (iii) a **nullable decision reference on the flag**, because a Do-lane item resolved as one's own work closes `Acted` with no Decision Record (briefing analysis §4.2 / BR-1); (iv) **Decision Record → flag is 1:N** — one decision can resolve several flags on a matter, and the gate must record which (prototype finding 4) | **Decision Record** · `sprk_signal` shape |
| **D-7** | **Is `sprk_signal` an `InsightArtifact` subtype or a sibling table?** `[new rev 4 — owner item 7]` *(Recommend **sibling**: `InsightArtifact` is a response/evidence contract returned from a query, whereas a signal is a durable queue row with a lifecycle — `Open` → `Acted`/`Dismissed`/`ConditionCleared`/`Superseded`/`PolicyRetired` — plus dedupe and suppression. Forcing one shape to serve both repeats the `sprk_spendsignal` mistake in the other direction. A signal **cites** `InsightArtifact`s as evidence)* | `sprk_signal` shape |
| **D-8** | **Do we need entity-specific fact tables at all?** `[new rev 4 — owner item 5]` *(Recommend **no**. Verified 2026-10-01: `ILiveFactResolver` is already generic — keyed `(subject-scheme, predicate)` with Matter / Invoice / Project implementations — and **CM-3** already says materialize facts as rollup + calculated columns, zero C#. So `sprk_spendsnapshot` is **one materialization, not a pattern**: keep it, read it as evidence, and add no `sprk_*snapshot` sibling per entity. The same logic retires `sprk_spendsignal` in favour of generic `sprk_signal`, with `SignalEvaluationService` becoming one producer among several.)* ⚠️ **One thing to settle inside this**: §8.1's exit condition and the synopsis §2 *compute facts* row both still name `sprk_spendsnapshot` as **the** fact mechanism. If D-8 lands as recommended, those read as the exception, not the rule | `sprk_signal` shape · the evaluator's fact reads |
| **CM-2** | **Object-definition registry.** *What it is*: a machine-readable description of the ontology's own objects — which entities exist, how they link, which attributes are owned where — so an external consumer can **discover** the model instead of having it hardcoded. *Implication for R1*: **none, and it stays post-MVP**, because its only consumer is the MCP server (out of scope, §5 Out). Worth recording that a seed already exists — `SubjectParser` + `SubjectSchemeCatalogOptions` are a subject-scheme registry — so if a second consumer appears, we extend rather than start | Post-MVP |
| **CM-4** | **Terminology — "disposition".** ⚠️ **Needs owner confirmation, because the premise differs from recollection.** Component model §3 (authoritative) marks **"Action · gate · disposition" ✅ Keep** and flags only *typed outcome* with ⚠️ *"consider reusing disposition"*. **"Disposition" was never retired**; what §3 retired was *ledger* → **Decision Record**, plus *binding registry*, *connector manifest*, *originate* and *field classes*. **Recommendation: resolve CM-4 by adopting §3's own suggestion** — use **disposition** for an Inquiry's outcome and drop *typed outcome* as a separate term, then sweep *typed outcome* out of criterion 10, the synopsis and the review. That gives the single vocabulary owner item 9 asks for. If you would rather retire *disposition*, say so and it becomes an amendment to component model §3 — the one place a terminology change is allowed to originate | **Decision Record** naming · **Inquiry action** · criterion 10 wording |

### 8.0b From the Daily Briefing analysis `[rev 4]`

Proposed in [`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md) §8 and scheduled here
per §5.0 (nothing merely listed).

| ID | Decision | Recommendation | Blocks |
|---|---|---|---|
| **BR-1** | Does acting on one's **own assigned work** write a Decision Record? | **No.** Close the flag `Acted` with a **null decision reference**; the object's own history is the record. **Dismissal still writes one**, with a reason, and still counts toward suppression. Amends row-contract requirement 5 to *"acting through a gate writes a Decision Record; acting on one's own assigned work is recorded on the object."* Rationale: 11 overdue tasks against 5 decisions would fill the record with housekeeping, and the Decision Record is what becomes the Matter Report Card | D-2 · `sprk_signal` shape |
| **BR-2** | Which Do rules ship first | Overdue task · due within 3 days · work assignment past `sprk_responseduedate`. All `Temporal`, all over data Spaarke already holds | Do lane |
| **BR-3** | `sprk_highpriority` / `sprk_monitor` semantics | Rank input (and optional rule input) · subscription to Know. Retire the separate *Critical Today* list | Rank function · narrative |
| **BR-4** | When does the Briefing widget get replaced? | Replace the Workspace's *Daily Briefing* tab with the worklist (narrative on) **once Decide and Do lanes both exist**; keep the old widget until then | Console hosting · widget registry |
| **BR-5** | Lane order and volume | Decide always above Do; each lane its own count filters; the narrative **summarises** Do volume rather than listing it | Worklist layout |
| **BR-6** | First Know-promotion rules | *New matter with no budget after 5 days* (`Absence`, Spaarke-held). **Defer document-based Absence rules until documents are mirrored** — feedback §1.5: an Absence rule is impossible over reference-mode data | Policy catalog |

---

**Former D-6 / the former D-7 are now §5 In repairs** — pulled in per §5.0 because this project holds the
diagnosis. *(The identifier `D-7` is reused above for the `sprk_signal`-vs-`InsightArtifact` decision; the
retired one was the association-engine precision item, now folded into the tokenizer repair.)*
**Former D-8 is [ISS-001](https://github.com/spaarke-dev/spaarke/issues/1048)** — another domain's to fix, but
scheduled rather than listed. Two further items that would otherwise have been lost are
[ISS-002](https://github.com/spaarke-dev/spaarke/issues/1049) (silent `$choices` degradation) and
[ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050) (49 `sprk_event` rows stranded in Draft).

### 8.0 From the architecture review (CM-6..CM-11) `[rev 3]`

| ID | Decision | Disposition |
|---|---|---|
| **CM-6** | Path A (single-source, honest message, **fails §0**) or Path B (add the budget-unchanged conjunct) | **Path B adopted.** Path A would mean rewriting the claim to match a weaker implementation — the wrong direction when the claim *is* the product. Consequence: D-1 becomes a hard blocker and `sprk_budgetrevision` enters scope |
| **CM-7** | Add `Existence` to the closed rule types? | **Yes — in scope.** Without it the differentiated capability cannot be saved at all (spec §3.5 validation). One type only; `Transition` and `Trend` stay out |
| **CM-8** | Separate `sprk_detectionshape` from `sprk_ruletype`? | **Deferred — adopt the taxonomy, not the column.** The review's observation that the closed set of seven is *closed over the wrong axis* (≈3½ detection, 2 authorization, 1 control) is correct and worth recording. But storing the shape needs a **reader**: evaluator selection can come from `sprk_ruletype`, and "worklist rendering defaults" is not a consumer until the worklist exists. Per §11, use the eight shapes as a **derivation method** for the signal catalog — the review's own §5 fallback — and revisit when a second consumer is real |
| **CM-9** | `sprk_signal` resolution semantics — dedupe alternate key, `sprk_resolutiontype`, resolution sweep | **Folded into the deferred `sprk_signal` shape decision** (§5), so it is taken once rather than twice. Taking it twice makes the second one a migration |
| **CM-10** | Does a budget-revision record exist, or need creating? | **ANSWERED: it must be created.** `sprk_budget` has `sprk_totalbudget` + `modifiedon` + `versionnumber` and **no revision rows**; no `sprk_budgetrevision` entity exists; auditing is not a fallback (component model §6 — zero `RetrieveRecordChangeHistory`, no audit policy). Now a §5 In item and D-1's fourth spike question |
| **CM-11** | Restore the Inquiry action, or declare its absence? | **Restored** as a §5 In item and criterion 10 |

#### 8.0.1 Two implementation constraints the review's §3.3 body would have broken

**(a) No cross-clause variable passing.** The proposed body binds `"commitment"` in clause 1 and references
`"$commitment.sprk_receiveddate"` in clause 2. That is **not one Dataverse filter**, so it breaks **CM-3**
(*rule body = a Dataverse filter*) — and it reintroduces the very thing that made predicates preferable to
node graphs. The case for predicates was that the four hard parts of graphs — sequencing, state, **variable
passing**, error handling — are absent. That body puts one back.

**The form to build instead**: two independent clauses over a fixed window, no binding —

```
all: [ exists    sprk_communication   … sprk_receiveddate >= now-30d … ,
       notExists sprk_budgetrevision  … sprk_revisedon    >= now-30d ]
```

Slightly less precise (a revision could land early in the window, before the commitment) but it is two ANDed
filters, expressible in FetchXML via an outer `link-entity` + null test, and the architectural property holds.
A quantified, correlated variant is a later change **if** evidence shows the imprecision matters.

**(b) `sprk_budget.modifiedon` is NOT an acceptable shortcut.** It is tempting — a change date already
exists, zero new schema, testable today. **Reject it**: any unrelated field edit bumps `modifiedon`, so a
stray edit reads as *"the budget was revised"* and **suppresses a true signal**. That is a false **negative**,
the exact direction decision 13 (recall over precision for notifying) rules against. Recorded so nobody
rediscovers it as a cost saving.

### 8.1 Dev test data — a seeding checklist `[rewritten rev 4]`

**No longer a spike.** Owner decision 2026-10-01: *"we can manually or systematically add test data to dev"*, and
budget is *"manually created for development testing — for R1 we do not need to define where it comes from."*
Both of the questions that made this a one-day investigation are therefore closed by fiat rather than by
research, and **D-1 stops being the critical path**. What remains is a checklist with an exit condition.

| Step | What | Note |
|---|---|---|
| 1 | **Create the smallest `sprk_budgetrevision`** | matter · prior amount · new amount · `sprk_revisedon` · reason · author. Still required (CM-10): `sprk_budget` carries `sprk_totalbudget` + `modifiedon` only, with no revision rows, and auditing is not a fallback. ⚠️ `sprk_budget.modifiedon` remains rejected as a substitute — §8.0.1(b) |
| 2 | **Seed spend on a matter that already carries a budget** | `REAL-2026-123456.01` / `.02` both carry budgets. Write `sprk_spendsnapshot` directly, or invoke `SpendSnapshotGenerationJobHandler` if it can be triggered in dev. **Either is acceptable now** — the provenance question that made this interesting belongs to the connector phase, which is out of scope (§1.0) |
| 3 | **Classify one real communication on that matter** | `Scope / budget change` or `Fee / rate change`, through the live enrichment path so the classifier — not a hand-written row — produces it. This is the half most easily faked and least useful if faked, because criterion 11 measures this classifier's recall |
| 4 | **Seed the Do lane and a Know-promotion case** | `[new rev 4]` An overdue `sprk_event` task and a new matter with no budget, so BR-2 and BR-6 have something to fire on. ⚠️ [ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050) first — 49 rows stranded in `Draft` will otherwise make the Do lane under-report |

**Exit condition** (unchanged, and still a gate): a matter exists with a `sprk_spendsnapshot` where
`sprk_invoicedamount >= sprk_budgetamount`, **and** a communication on that same matter classified
`Scope / budget change` or `Fee / rate change`, **and** no `sprk_budgetrevision` inside the window. That triple is
the minimum input to criterion 2.

⚠️ **What seeding does not buy.** Hand-made data proves the predicate *evaluates*; it cannot prove the predicate
*fires on reality*. §9's top risk ("the cross-source rule fires on nothing real") is **not** retired by this
checklist — it is retired by criterion 2 passing on data someone did not author to make it pass. Keep the two
apart when reporting.

### 8.2 Why "bitemporal / as-of" is out, and what that costs `[new rev 4 — answering owner item 9, D-2]`

**What bitemporality means.** Storing two independent time axes per fact: **valid time** (when it was true in the
world) and **transaction time** (when our system recorded it). With both, you can ask *"what did we believe on
12 March about the budget as it stood on 1 March?"* and get an exact answer.

**What Dataverse gives us instead.** One axis, and it is destructive: a row holds current values, `modifiedon`
tells you when it last changed, and the previous value is gone unless auditing is on — which it is not
(component model §6: zero `RetrieveRecordChangeHistory` usage, no audit policy). So **"what did the data say when
the rule fired?" is unanswerable by querying the data.**

**Why that matters here specifically.** A Decision Record's job is to be defensible months later. If it cites
*"$236,400 invoiced against a $200,000 budget"* and the only way to check is to re-read `sprk_spendsnapshot`
today, the record is worthless the moment the numbers move — which they do, continuously. The decision would be
unreconstructable precisely when someone asks about it.

**The two compensating controls, and the division of labour:**

| Control | Covers | Mechanism |
|---|---|---|
| **Immutable `sprk_policyversion`** | *What the rule was* | A version row is never updated, so "the threshold was 115% then, it is 120% now" is answerable, and old signals keep citing the version that raised them |
| **Mandatory `sprk_factsnapshot`** on the Decision Record | *What the data said* | Copy the fact values **into** the record at decision time. The record becomes self-contained — it does not depend on the source row still holding those values |

Together these cover the defensibility we actually need, which is why D-2 treats `sprk_factsnapshot` as
**mandatory rather than optional**. Dropping it to "nice to have" re-opens the gap that ruling out bitemporality
created, and the gap is invisible until the first time someone audits an old decision.

**The residual, stated honestly.** We can reconstruct *the facts the rule read*. We **cannot** reconstruct facts
nobody recorded — a value that mattered but was not in the snapshot, or the state of a record the predicate never
touched. Full as-of querying would cover that; we are choosing not to pay for it. Prototype finding 3 is the live
example of the residual biting: an absence clause (*"no budget revision since"*) over a **stale** source is a
false negative, and the snapshot records the staleness but cannot repair it.

---

## 9. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **The cross-source rule fires on nothing real.** It is the only unproven item; everything else is mechanism | **High** | Make criterion 2 a **gate**, not a checkbox. ⚠️ **Sharpened rev 4**: D-1's resolution (seed dev data) makes the predicate *testable*, **not** proven. Seeded data proves it **evaluates**; only data nobody authored to make it pass proves it **fires on reality**. Do not let a green criterion 2 over seeded rows retire this risk — the two readings of "it works" are different, and §8.1 states which is which |
| The taxonomy does double duty — triage routing *and* signal input | Medium | If it strains, add an `sprk_signalrelevant` flag to category rows. **Do not add more categories** — a 10-way single-select degrades |
| `$choices` resolution is best-effort (NFR-04); a Dataverse read failure degrades **silently** to the pre-2026-09-04 behaviour (category null on 100% of captures) | Medium | Monitor. The failure is invisible by construction |
| Swallow-and-log paths are undiagnosable without App Insights | Medium | Recorded in `current-task.md`: appId `6a76b012-…`, `traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for swallowed throws. **This is how the silent `InvalidCastException` was found** |
| **Classifier recall is the predicate's weakest link** | **High** | The predicate is a conjunction, so it inherits its weakest input — an LLM classifier whose recall nobody has measured. Every other criterion can pass while the claim silently misses a third of real cases. **Criterion 11 makes the floor a gate**, and review §1.6b is the source |
| **Flags never auto-resolve, so the worklist rots** | **High** | Nothing closes a flag when the condition clears on its own — budget revised, communication later dismissed, matter closed. This is how internal alerting surfaces die, and it also **miscounts suppression**: a flag that auto-cleared is indistinguishable from one a human dismissed, so the three-dismissal rule is wrong in both directions. Addressed by CM-9's `sprk_resolutiontype` + resolution sweep, folded into the `sprk_signal` decision |
| Recall-first produces notification fatigue | Medium | Thresholds are declared on the rule row; the Decision Record makes tuning evidence-based (criterion 5) |
| Work surfaced in passing gets buried, missed or lost | **High** | **Owner rule (2026-09-30): every item is either FIXED or SCHEDULED — never merely listed.** Where this project already holds the diagnosis, fixing now is *cheaper* than handing off, so it is the correct choice and scope purity does not override it (that is why the tokenizer and the `reason` string were pulled INTO §5 rather than deferred). Where the work genuinely belongs elsewhere, it is filed in [`notes/defer-issues.md`](notes/defer-issues.md) **and** as a GitHub Issue, and `push-to-github` Step 1.6 refuses to let an entry through without an Issue URL |

---

## 10. Decisions already settled — do not re-litigate

Carried from `current-task.md`; full rationale in the notes.

1. **Naming**: Spaarke Console · Spaarke Matter Management · Spaarke External Access · Connection Engine (component) / Spaarke Connect (SKU)
2. **Three engines**: Connection · Insights · Action — decoupled *by* the ontology, not chained. Agents are a **surface**
3. **CM-1**: Policy lives in the Insights Engine; signals are Insights outputs
4. **CM-5**: Inquiry → `sprk_servicerequest` with a direction discriminator
5. **CM-3**: rule body = a Dataverse filter; materialize facts as rollup + calculated columns (zero C#)
6. **Ingestion = Option A** — our own worker on `UpsertMultiple`. Dataflows cannot be deployed by a service principal; Fabric needs F capacity per tenant. Deployment-model constraints, not capability ones
7. **Console hosting**: web resource + `appid` + **`navbar=off`**
8. **Authority is post-MVP**
9. **Action Engine is not in MVP**
10. **Terminology**: Spaarke Connect's existing entities. "Ledger" → **Decision Record** customer-facing
11. **Connection Engine stays in this project** — harvest, don't fork
12. **§0 differentiation test is binding**
13. **Recall over precision for NOTIFYING, never for FILING.** Auto-file stays at 0.85 — filing writes data, where a false positive is worse than a miss; notifying only surfaces, where a miss is worse than noise
14. **Policy knobs are DECLARED on the rule row**, with options as fallback only
15. **LLM classifies; deterministic code decides; a human acts**
16. **R1 is intelligence-forward** — build from the Spaarke data model out; the connector is a later phase with
    the customer (§1.0, owner 2026-10-01)
17. **No LEDES intake.** Invoice / budget / spend-variance arrive as **computed metrics** from the e-billing
    platform (owner 2026-10-01)
18. **The Daily Briefing dissolves into the worklist** — same archetype with narrative on; its work becomes
    lanes, its news becomes narrative and Context, its tiles become filters, and its AI stops choosing priority
    (owner 2026-10-01; analysis in `notes/daily-briefing-ontology-fit.md`)
19. **Worklist rows are matters; the worklist reads signals and groups by matter** (D-3, settled by the prototype)
20. **No per-entity fact or signal tables.** `ILiveFactResolver` + rollup/calculated columns supply facts;
    generic `sprk_signal` replaces `sprk_spendsignal`. `sprk_spendsnapshot` is a materialization, not a pattern
    (D-8 recommendation)
21. **`spaarke-ai-architecture-redesign-r2` is CLOSED** — verified 2026-10-01 in `projects/INDEX.md`. `Services/Ai/`
    has no sole owner, so AI-side work this project needs is **done in this project** (owner 2026-10-01,
    answering synopsis §7 item 5). `/conflict-check` still applies for in-flight neighbours
22. **The Console prototype is the UI/UX contract** — not a sketch to admire and re-derive (§11, §12)

---

## 11. Dependencies

| On | What |
|---|---|
| **PR #1032** | Must merge first — items 1–6 build on the repaired pipeline |
| `spaarke-legal-front-door-r1` | Pre-spec. Must be told CM-5 likely absorbs `sprk_legalrequest`; its roadmap says that decision "gates everything" |
| `spaarke-connect-integration-module-r1` | Design harvested, not forked. Its README still needs a status note |
| Communication-intelligence owner | Review of the PR #1032 semantics, especially `statuscode = Open` |
| App Insights | The only way to diagnose the swallow-and-log paths |
| **The Console prototype** | `c:\code_files\spaarke-prototype\projects\2026-10-spaarke-console\` (v2.1). **The UI/UX contract for this project**, carried forward into `/design-to-spec`: the closed component kit (one shape per verb), the single review process, progressive disclosure, count filters as lenses on membership, and findings 1–17. Three of those findings **contradict** documents in this project and are reconciled here — finding 9 (criterion 7, §7), finding 4 (Decision Record → flag is 1:N, D-2), finding 2 (the §8.0.1(a) predicate cannot produce per-clause witnesses). It is still in review (round 2 pending), so treat the **kit** as settled and the **row copy** as draft |

---

## 12. Next steps

1. **Review + iterate this document.** Rev 4 answers owner feedback of 2026-10-01; **CM-4 needs an explicit
   confirmation** because its premise differs from recollection (§8.0a).
2. **Seed the dev data** (§8.1 checklist) — no longer a spike, no longer the critical path.
3. Settle **D-2**, **D-7**, **D-8** and the `sprk_signal` shape (§5 deferred-in-project, incl. CM-9 resolution
   semantics and D-3's polymorphic-subject + matter-lookup consequence) **before the evaluator writes its first
   signal**. Fold in **BR-1** at the same time — it changes the same field list.
3a. **Close the prototype loop**: round-2 review, then agree the kit as the Console's component contract and
   carry findings 1–17 into the spec (§11).
3b. Record the shape × binding-mode feasibility matrix (review §1.5) in component model §4.5 or §4.7 —
   non-blocking, but it is the concrete reason to ask a customer for API access rather than accepting
   MCP-only: **API access buys the obligation module; MCP alone does not.**
4. `/design-to-spec` → `/project-pipeline` → `task-execute`.
5. Merge PR #1032.
6. Run the §0 differentiation test retroactively across strategy-synopsis §8 Wave 2 / Wave 3 before any of those modules is specced.
