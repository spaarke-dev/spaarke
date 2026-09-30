# Spaarke Legal Operations Intelligence — Ontology Platform R1 · Design

> **Status**: **DRAFT for review** — 2026-09-30. Not yet through `/design-to-spec`.
> **Evidence base**: [`notes/mvp-technical-spec.md`](notes/mvp-technical-spec.md) (~800 lines — field-level
> detail, live-verified schema, defect forensics). **This document holds the decisions; the spec holds the
> evidence.** Where they disagree, this document is newer.
> **Vocabulary**: [`notes/ontology-component-model.md`](notes/ontology-component-model.md) §3 is authoritative.
> **Audience**: owner review · `/design-to-spec` · `code-review` · future sessions.

---

## 1. What this project is

**One sentence**: consolidate data from the customer's third-party systems, resolve it against Spaarke's
objects, match it against the customer's own declared rules, and record what was decided.

The ontology is not a new database. It is a **declaration layer** over Dataverse — objects and links,
binding declarations, attribute ownership, fact supply, Policy, actions and gates, and a Decision Record —
plus the machinery that walks it: **bind → resolve → compute facts → match to action → execute under gate →
record**.

**The differentiating claim, stated so it can be falsified:**

> A commitment with financial consequence was made in correspondence, and the budget does not reflect it yet.

That predicate requires the **email** and the **budget**. An e-billing system holds the second and never sees
the first, so it cannot produce this at any price. Everything else in the MVP exists to make that one sentence
computable, recordable, and re-tunable without a deployment.

### 1.1 What this project is NOT

- **Not budget variance.** Legal Tracker, Onit and Brightflag already ship it. It is the plumbing proof, never
  the pitch — the customer's honest answer is *"we can already do that"* (spec §0).
- **Not the Action Engine.** The MVP needs an *Action* (a data row), not the engine (decision 9).
- **Not Authority.** Every action is human-confirmed in the MVP, so the human *is* the authority (decision 8).
- **Not a migration.** The line between overlay and system-of-record is **authority, not storage**.
- **Not an agent.** The LLM classifies, bounded by data; deterministic code evaluates; a human acts
  (decision 15). A predicate evaluated by a model makes the Decision Record an anecdote.

---

## 2. Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                              <!-- LANDED: RiConfidenceScorer, CommsPolicyOptions, CommunicationRuleGate, CommunicationRiActionService, TaskActionCore, ActionSeam/IActionSeam, DailyBriefingCollector. PLANNED: predicate evaluator, Decision Record writer, LookupChoicesResolver guidance injection -->
  <spaarke-ai>Y</spaarke-ai>                <!-- The worklist surface (D-3) is a PRODUCT decision to be made in design, not left conditional. Y until D-3 resolves; if the worklist ships as a sprk_gridconfiguration row alone this becomes N and the block is CORRECTED here, not left ambiguous -->
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
| **Worklist** | `sprk_gridconfiguration` + `<DataGrid configId=… />`; `needs-review.gridconfiguration.json` computes membership in fetchXml and renders via `DataGridOverrides.columnRenderers` | **Reuse — author a ROW, build no UI** | Signals stay invisible, exactly as `sprk_spendsignal` has been since it shipped |
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

| # | Item | Shape | Note |
|---|---|---|---|
| 1 | **Cross-source predicate evaluator** | code + `sprk_policy` / `sprk_policyversion` | The differentiated claim. Blocked on the **D-1 spike** for test data |
| 2 | **Decision Record** | table + a one-line writer | Needs D-2 (field list) |
| 3 | **Worklist** | a `sprk_gridconfiguration` row | Needs D-3 (surface + row subject) |
| 4 | **`sprk_memo` as signal source #2** | one Action row + taxonomy decision | Needs D-4 (reuse taxonomy or not) |
| 5 | **Guidance injection** | small change to `LookupChoicesResolver` | No decision needed. Makes the ten authored `sprk_classifierguidance` rows live -- they are inert today |
| 6 | **Space-bearing matter-number tokenizer** | `IdentifierReverseLookupRung` regex + tests | **Pulled in per §5.0.** `WellFormedTokenPattern` requires an alpha prefix *immediately* followed by `-`/`.`, so live matter `"Form D - 2023"` never tokenizes and `ExplicitReference` never fires -- even when the subject carries the number *and* the name verbatim. The rung's own docstring supplies the safety argument: *"precision comes from the EXACT reverse lookup, not this pattern -- an over-match simply resolves to no record."* Touches ADR-045 precision/cost, so it ships with tests and a measured query-count delta |
| 7 | **The false association `reason` string** | one interpolation + a test | **Pulled in per §5.0.** Reports *"Reinforced confidence 0.97 in [0.50, 0.85)"* -- 0.97 is not in that band. The status band is computed from `topDeterministicConfidence` while the message interpolates `topConfidence`. AP-12 in **runtime-generated prose**, which is worse than a stale comment because it is written into data that later diagnoses trust |

**Deferred from this discussion, to be resolved in this project's design/spec** (owner, 2026-09-30 -- *"we have
more to finalize before we can make this specific decision"*):

| Item | Why it is not settled yet |
|---|---|
| **Generic `sprk_signal`** (replacing `sprk_spendsignal`) | `sprk_spendsignal`'s `sprk_snapshot` lookup makes it structurally spend-only, so a memo- or communication-derived signal will not fit; it has zero client references and no production data, so replacing it is free **now** and a data migration later. But the right shape depends on decisions not yet made -- the Decision Record's fields (D-2), whether the worklist reads signals or matters (D-3), and how many producers exist at MVP (D-4). Also: `sprk_signaltype` / `sprk_signalvalue` are **already taken** as columns on `sprk_affinity`, so the naming is not free either. **Decide before the evaluator writes its first signal** -- that is the last moment it stays cheap |

### Out (with rationale)

| Item | Why |
|---|---|
| Connection Engine / MM connector | Largest single item; open owner decision. The MVP's signals come from data Spaarke **already holds** |
| Action Engine | The MVP needs an Action row, not the engine (decision 9) |
| Authority | The human is the authority while every action is confirmed (decision 8) |
| MCP server | Deferred to `spaarke-mcp-server-r1`, which must absorb `spaarkeai-word-native-r1`'s off-master design |
| Bitemporal / as-of | Immutable policy **versions** buy ~80% of the defensibility without it |
| Quantifying the commitment | Extracting "$45k" from prose is new extraction work that is sometimes wrong. **The join is the differentiator, not the arithmetic.** v2 |
| LEDES intake | Opens LEDES code mapping — that is for when we are the system of record. Pull from the platform, which holds reconciled data |

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
7. The worklist row renders from a `sprk_gridconfiguration` row with **zero new UI code**.
8. A new taxonomy category — with guidance — changes classifier behaviour with no deployment.
9. **The §0 differentiation test passes**: for every shipped capability, we can name the data it requires that
   the incumbent does not hold.

---

## 8. Open decisions

| ID | Decision | Blocks |
|---|---|---|
| **D-1** | **SCOPING SPIKE -- spend data.** Not a question to answer in the abstract; owner decision 2026-09-30: *"we can evaluate this as a project scoping spike."* See §8.1 for what the spike must produce | Criterion 2. **The differentiated claim cannot be tested at all today** |
| **D-2** | Decision Record field list — approve or amend | Item 2 |
| **D-3** | Worklist surface: Console, MDA, or both? Rows = matters or communications? | Item 4; the `spaarke-ai` hot-path flag |
| **D-4** | Does `sprk_memo` reuse `sprk_triagecategory` or get its own taxonomy? *(Recommend reuse — one bounded set keeps cross-source rules simple)* | Item 5 |
| **D-5** | Is the MM connector in MVP? Export-only ≈2 months; API mirror adds ≈2 | Scope |
| **CM-2** | Object-definition registry — only if the MCP server must describe itself | Post-MVP |
| **CM-4** | Reuse "disposition" for an Inquiry's typed outcome? | Item 2 naming |

---

**Former D-6 / D-7 are now §5 In items 6 and 7** -- pulled in per §5.0 because this project holds the
diagnosis. **Former D-8 is [ISS-001](https://github.com/spaarke-dev/spaarke/issues/1048)** -- another domain's
to fix, but scheduled rather than listed. Two further items that would otherwise have been lost are filed as
[ISS-002](https://github.com/spaarke-dev/spaarke/issues/1049) (silent `$choices` degradation) and
[ISS-003](https://github.com/spaarke-dev/spaarke/issues/1050) (49 `sprk_event` rows stranded in Draft).

### 8.1 D-1 -- the spend-data scoping spike

**Why a spike and not a question**: three unknowns compound, and the answer to each changes the next. Timebox
**one day**. Deliverable: a one-page finding in `notes/`, and either a working snapshot or a costed statement
of what standing one up requires.

The spike answers, in order:

1. **What populates `sprk_spendsnapshot` today?** `SpendSnapshotGenerationJobHandler` exists -- is it triggered
   by a Service Bus message, a schedule, or an endpoint? Can it be invoked on demand in dev? *(Open item in
   spec §9 that this session never closed.)*
2. **Does it assume Spaarke-originated invoices?** If it reads `sprk_billingevent` rows that only a Spaarke
   billing flow produces, the connector work is materially larger than spec §8 implies -- which feeds D-5.
3. **What is the cheapest honest path to one over-budget matter?** Two candidates already carry budgets:
   `REAL-2026-123456.01` and `REAL-2026-123456.02`. Seeding `sprk_billingevent` via MCP is fastest; a real UI
   path is more faithful. Pick on what the MVP must eventually demo, not on what is quickest to fake.

**Exit condition**: a matter exists with a `sprk_spendsnapshot` where `sprk_invoicedamount >= sprk_budgetamount`,
**and** a communication on that same matter classified `Scope / budget change` or `Fee / rate change`. That
pair is the minimum input to criterion 2 -- until it exists, item 1 cannot be built against anything real.

---

## 9. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **The cross-source rule fires on nothing real.** It is the only unproven item; everything else is mechanism | **High** | Make criterion 2 a **gate**, not a checkbox. Prove it on real data (D-1) before building the worklist on top |
| The taxonomy does double duty — triage routing *and* signal input | Medium | If it strains, add an `sprk_signalrelevant` flag to category rows. **Do not add more categories** — a 10-way single-select degrades |
| `$choices` resolution is best-effort (NFR-04); a Dataverse read failure degrades **silently** to the pre-2026-09-04 behaviour (category null on 100% of captures) | Medium | Monitor. The failure is invisible by construction |
| Swallow-and-log paths are undiagnosable without App Insights | Medium | Recorded in `current-task.md`: appId `6a76b012-…`, `traces` for `[comms-policy]`/`[comms-ri]`, `exceptions` for swallowed throws. **This is how the silent `InvalidCastException` was found** |
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

---

## 11. Dependencies

| On | What |
|---|---|
| **PR #1032** | Must merge first — items 1–6 build on the repaired pipeline |
| `spaarke-legal-front-door-r1` | Pre-spec. Must be told CM-5 likely absorbs `sprk_legalrequest`; its roadmap says that decision "gates everything" |
| `spaarke-connect-integration-module-r1` | Design harvested, not forked. Its README still needs a status note |
| Communication-intelligence owner | Review of the PR #1032 semantics, especially `statuscode = Open` |
| App Insights | The only way to diagnose the swallow-and-log paths |

---

## 12. Next steps

1. **Review + iterate this document.**
2. **Run the D-1 spike** (§8.1, one day) -- without spend data the differentiated claim is untestable, which
   makes it the true critical path, ahead of any build item.
3. Settle D-2, D-3, D-4 (each unblocks one §5 item), and the `sprk_signal` question (§5, deferred-in-project)
   **before the evaluator writes its first signal**.
4. `/design-to-spec` → `/project-pipeline` → `task-execute`.
5. Merge PR #1032.
6. Run the §0 differentiation test retroactively across strategy-synopsis §8 Wave 2 / Wave 3 before any of those modules is specced.
