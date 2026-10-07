# Spaarke LOI Platform — MVP Synopsis

> **Status**: Draft for owner review — **updated 2026-10-01 (owner feedback round 2)**. Not a spec.
> **Authoritative for decisions**: [`../design.md`](../design.md) **rev 4**. Where this synopsis and `design.md`
> disagree, `design.md` is newer. **Vocabulary**: [`ontology-component-model.md`](ontology-component-model.md) §3.
> **Companions**: [`ontology-component-model.md`](ontology-component-model.md) (definitions) · [`spaarke-ontology-strategy-synopsis-v2.md`](spaarke-ontology-strategy-synopsis-v2.md) (strategy) · [`phase0-codebase-inventory.md`](phase0-codebase-inventory.md) (verified state)

---

## 1. The MVP in one sentence

> **Bind a customer's matter-management and e-billing data, evaluate it against rules the customer owns, surface what needs attention in the Spaarke Console, let the user act, and record what was decided.**

This is §8's second bootcamp SKU, *"if the buyer's pain is spend, run the inquiry loop"*.

> ### 🔴 Scope correction — 2026-10-01 (owner)
>
> **Two changes that reach most of this document.**
>
> **(a) R1 is intelligence-forward.** It builds the intelligence layer **from the Spaarke data model out**, and
> **not** the connector that brings a system of record's data in. Every capability is specced against data
> already in Dataverse — because Spaarke *is* the system of record for it (matters, communications, documents,
> tasks, memos), or because a later phase with the customer mirrors it in (invoice, budget, spend metrics). The
> Connection Engine is **out of scope** (`design.md` §1.0 / §5 Out); the one carve-out is the **landing
> contract** — pointer columns + attribute ownership — because it is free now and a migration later.
>
> **(b) No LEDES intake.** Invoice, budget and spend-variance values arrive from the e-billing platform as
> **computed metrics**, not as raw LEDES files we parse. This retires §4.1's claim that *"half the binding story
> needs no connector at all"* — that argument was LEDES-shaped and no longer applies.
>
> **And one expansion** (owner items 2–3): the shipped **Daily Briefing dissolves into the worklist** rather than
> sitting beside it. Its overdue/upcoming work becomes a **Do lane** with rule-declared membership, its news
> becomes narrative and Context, its tiles become filters, and its LLM stops choosing priority. Four of the six
> changes are configuration or deletion. Full analysis: [`daily-briefing-ontology-fit.md`](daily-briefing-ontology-fit.md);
> scheduled as BR-1..BR-6 in `design.md` §8.0b.

> ### 🚩 Positioning correction — 2026-09-24
>
> **Budget variance is the plumbing proof, not the pitch.** Budget *and* spend both live in the e-billing system, so Legal Tracker, Onit and Brightflag already do variance, thresholds, accruals and AI line-item review. A demo built on it earns *"we can already do that."*
>
> **The differentiated demo is the second rule, and it is cross-source:**
>
> > *"Matter 4471 is 18% over budget — **and** outside counsel flagged scope creep in a thread three weeks ago that nobody acted on."*
>
> That needs **spend + communication**. Legal Tracker has no email, and the 13-rung association ladder is our strongest shipped asset.
>
> ### 🔴 Correction to the correction — 2026-09-30
>
> **The sentence above is the aspiration, not the shipped predicate.** It implies a variance computation
> ("18% over budget") that the MVP predicate does **not** perform. What the corrected predicate actually
> tests (`mvp-technical-spec.md` §11.4, Path B) is:
>
> > A communication on this matter was classified as a **fee or scope change** in the window, **and** no
> > **budget revision** was recorded in that window.
>
> Still two sources, still impossible for an incumbent — but it compares *correspondence against budget
> movement*, not invoiced against budgeted. The spend snapshot is attached as **evidence** so the user sees
> the position; the rule asserts no comparison it did not compute.
>
> **Why this correction exists.** The first cross-source predicate contained **no budget term at all** while
> its message claimed *"unreconciled against its budget"* — single-source, and asserting what the code never
> established. Generalized as spec **§0.3: a capability must TEST what its message CLAIMS.** Data being
> theoretically available is not the test.
>
> Two consequences for scope: **`sprk_budgetrevision` is new in MVP** (verified — `sprk_budget` has no
> revision history and Dataverse auditing is not wired), and **the `Existence` rule type is new in MVP**
> (the predicate is an EXISTS and fitted no allowed rule body, so it could not have been saved).
>
> **The test to apply to every capability before scoping it** — see [`mvp-technical-spec.md`](mvp-technical-spec.md) §0:
>
> > **Name the data it requires that the incumbent does not hold. If you cannot, it is not differentiated.**
>
> Two design consequences: the signal entity must **not** be spend-shaped (`sprk_spendsignal`'s `sprk_snapshot` lookup won't carry cross-source evidence — replaced by generic `sprk_signal`, spec §2.5), and **signals are Insights Engine outputs**, with `SignalEvaluationService` becoming one producer among several. That extends CM-1.

## 2. The slice — all six pipeline stages, end to end

| Stage | MVP implementation | New? |
|---|---|---|
| **bind** | **Declared, not built** — the landing contract only (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof` + attribute ownership). Data is assumed present: Spaarke-owned, or mirrored by a later phase | 🟡 **columns only** *(was "MM connector + LEDES drop")* |
| **resolve** | Matter-number match → extend the existing rung ladder | 🟡 extend |
| **compute facts** | **`ILiveFactResolver`** — already generic, keyed `(subject-scheme, predicate)`, with Matter / Invoice / Project implementations — plus native rollup + calculated columns per CM-3. `sprk_spendsnapshot` is read as **one materialization, not the mechanism** | 🟢 **built** *(corrected 2026-10-01 — see §4.3a)* |
| **match** | Policy rows + the cross-source evaluator → **generic `sprk_signal`**. `SignalEvaluationService` becomes **one producer among several**, reading policy rows instead of appsettings thresholds | 🟡 extend · 🔴 evaluator is new |
| **execute under gate** | Inquiry action through the existing gate engine | 🟢 built · data only |
| **record** | Decision Record entity + subgrid on the matter + list view | 🔴 **new** |
| **surface** | One worklist row component over open signals, membership from configuration | 🔴 **new** |

**Five of seven stages are built or configuration.** With the connector out of scope, the work concentrates at
**one** end — closing the loop once something fires — plus the cross-source evaluator that makes the claim
differentiated at all.

## 3. What already exists — verified 2026-09-24

This is why the MVP is small. Discovered during verification, not assumed:

| Component | State |
|---|---|
| **`SignalEvaluationService`** | Deterministic threshold evaluation, no AI, idempotent upsert, `ISignalRule` strategy pattern for new rules |
| **`sprk_spendsignal`** | **The flag entity already exists** — matter lookup, signal type, severity |
| **`sprk_spendsnapshot`** | **The materialized fact already exists**, including `sprk_budgetamount` |
| **`SpendSnapshotGenerationJobHandler`** | Snapshot generation job |
| **`FinanceRollupService` · `FinanceSummaryService`** | Rollup + summary |
| **Gate engine** | `ConfirmationPolicyEngine` · `GateDecisionV2` · `SideEffectGateAIFunction` |
| **Action dispatch** | ADR-039 Binding catalog · 8 dispositions · `OutputRouter` |
| **Resolution** | 13-rung ladder · `AffinityStore` · `RecordMatching` |
| **Workspace hosting** | LegalWorkspace sections already self-fetch via `Xrm.WebApi` / `authenticatedFetch` — **a worklist widget needs no workspace changes** |
| **`sprk_servicerequest`** | Exists and is wired into the Association Engine — absorbs Inquiry (CM-5) |

**Critical gap found:** `sprk_spendsignal` has **zero client-side references**. Signals are computed and stored today and **nothing in any UI shows them.** The worklist is the missing half of work already done.

## 4. Two new capability areas — and everything else is data or already built

> **(1) Close the loop** — the worklist row + Decision Record · **(2) the cross-source evaluator**
>
> 🔴 **Changed 2026-10-01**: the Connection Engine was Area 1 and is now **out of scope** (§1 correction (a)).
> What remains of it in R1 is the landing contract — columns, not machinery.

### 4.1 ~~Area 1 — Connection Engine~~ → the landing contract only `[rewritten 2026-10-01]`

**The Connection Engine is out of R1** (§1 correction (a); `design.md` §1.0). Kept here, struck through, so the
decision is visible rather than silently dropped:

| Deliverable | Disposition in R1 |
|---|---|
| ~~LEDES parser + invoice landing~~ | **Out, and not the plan.** Invoice / budget / spend-variance arrive as **computed metrics** from the e-billing platform. Parsing LEDES opens code mapping — work for when we are the system of record |
| ~~MM connector (mirror Matter)~~ | **Out.** A later phase, with the customer |
| **Landing contract** | ✅ **IN.** Pointer fields (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`) + attribute ownership on any entity a later ingest touches. In R1 because it is **free now and a data migration later** (component model §11) |
| ~~Raw payload retention (Cosmos)~~ | **Out** — belongs with the ingest that produces payloads |
| **Freshness surfacing** | ✅ **IN**, and load-bearing rather than cosmetic: prototype finding 3 — *an absence clause is only as true as its source is fresh*. A `notExists` conjunct over a stale mirror is a false negative with a confident face |

### 4.2 Area 2 — Close the loop

Where the verified gap is: **`sprk_spendsignal` is computed and stored today with zero client-side references.** Signals exist, nothing shows them, and nothing happens off the back of one.

| Deliverable | Notes |
|---|---|
| **Worklist** | Rows are **matters**; the worklist **reads signals and groups them by matter** (D-3, settled by the Console prototype). Each row: the object · **why it fired** (policy name + version + the values) · its actions. ⚠️ **Not "narrower than it sounds"** — the 2026-09-24 text claimed the upgrade was only membership + reason + actions, and the prototype disproved it (finding 9): the row needs expandable evidence tiers, a *Why this fired* disclosure, outcome cards and a gate host. **One row component, built once**, carried all three signal shapes (finding 10) |
| **The Do lane** | The Briefing's overdue/upcoming work, with membership from **declared `Temporal` policies** instead of the collector's own queries — which buys a stated reason, versioning, suppression and freshness for free. `Acted` on one's own work writes **no** Decision Record (BR-1); dismissal does. See [`daily-briefing-ontology-fit.md`](daily-briefing-ontology-fit.md) §4.2 |
| **Decision Record entity** | Append-only (no Update/Delete privileges for anyone), **written at the gate**, subgrid on the matter + list view. The Context pane already renders session-ledger entries via `ISessionTraceReader` — an existing pattern to extend |

### 4.3 Small additions inside existing code — *not* a new engine

`SignalEvaluationService` already evaluates correctly (deterministic, no AI, idempotent, `ISignalRule` strategy pattern). **Do not move working code for MVP.**

| Change | Notes |
|---|---|
| **`sprk_policy` + `sprk_policyversion` entities** | **Three** rule types: `Threshold`, `Switch`, **`Existence`** *(added 2026-09-30 — the cross-source predicate is an EXISTS over a related entity and fitted neither of the first two; spec §3.5 refuses to save an invalid body, so the differentiated capability was unsavable without it)* |
| **`sprk_budgetrevision` entity** | *Added 2026-09-30.* Smallest possible: matter · prior amount · new amount · `sprk_revisedon` · reason · author. **Verified necessary**: `sprk_budget` carries only `sprk_totalbudget` + `modifiedon`, with no revision rows, and auditing is not a fallback (zero `RetrieveRecordChangeHistory` usage anywhere). Path B's second conjunct cannot be expressed without it. ⚠️ `sprk_budget.modifiedon` is **not** a substitute — any unrelated edit bumps it, which would suppress a true signal |
| **`SignalEvaluationService` reads policy rows** | Replaces `FinanceOptions` appsettings thresholds |
| **Stamp policy code + version onto each signal** | This is what makes the "why" citable and the old signals still explicable after a threshold changes |

### 4.3a Do we need entity-specific fact and signal tables? `[new 2026-10-01 — owner item 5]`

**Recommendation: no.** Verified on master 2026-10-01:

- **Facts** already have a generic mechanism — `ILiveFactResolver`, keyed `(subject-scheme, predicate)` with
  `MatterLiveFactResolver` / `InvoiceLiveFactResolver` / `ProjectLiveFactResolver`, plus `SubjectParser` and
  `SubjectSchemeCatalogOptions`. **CM-3** independently says materialize facts as **rollup + calculated columns,
  zero C#**. So `sprk_spendsnapshot` is **one materialization, not a pattern to replicate**: keep it, read it as
  evidence, and add no per-entity `sprk_*snapshot` sibling.
- **Signals** go the same way: generic **`sprk_signal`** replaces `sprk_spendsignal`, whose `sprk_snapshot`
  lookup makes it structurally spend-only, and `SignalEvaluationService` becomes **one producer among several**.

This is the same mistake twice if we get it wrong twice — a spend-shaped fact table and a spend-shaped signal
table. Tracked as **D-8** in `design.md` §8.0a. It also makes §5's *"unifying `sprk_spendsignal` with
`InsightArtifact`"* obsolete as written: there is nothing left to unify, and the live question — sibling or
subtype — is **D-7**.

> **CM-1 (Policy lives in Insights) is a destination, not an MVP move.** The `IPolicyEvaluator` contract in Insights gets built when a *second* consumer appears. For MVP, Policy is Dataverse rows that Finance's existing service reads.

### 4.4 Data, not build

| Item | Why it's not a build |
|---|---|
| **Inquiry action** | One Action + one Binding. Dispatch already exists: ADR-039 Binding catalog · `ConfirmationPolicyEngine` · `GateDecisionV2` · `SideEffectGateAIFunction` · `OutputRouter` (8 dispositions). Uses `sprk_servicerequest` with an outbound direction + **disposition** (CM-5) |
| **Policy authoring UI** | Model-driven form over `sprk_policy` / `sprk_policyversion`. Dataverse gives the form, version list, row security and audit for free |

### 4.5 Console hosting — RESOLVED 2026-09-24

`sprk_spaarkeai` deploys as a **single-file HTML web resource** (`scripts/Deploy-SpaarkeAi.ps1` → build → upload → publish). **Verified working**: opening it with both `appid` and `navbar=off` gives a **full-width, chrome-free, three-pane Console inside the Dataverse shell**:

```
main.aspx?appid={guid}&pagetype=webresource&webresourceName=sprk_spaarkeai&navbar=off
```

`appid` supplies app context (removes the *"open it in a specific application"* banner and the **Choose an app** button); `navbar=off` suppresses the sitemap nav and the Office app launcher. [Documented](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/best-practices/business-logic/consider-disabling-navbar-programmatically-opening-entity-forms-views) as a `main.aspx` parameter.

**Rejected alternatives** (record so they aren't re-proposed):

| Option | Why not |
|---|---|
| Model-driven app with a web-resource subarea | Brings a permanent ~48px nav rail — **two competing navigation surfaces** alongside Spaarke's own Navigator pane |
| Custom page + PCF code component | Supported and full-page, but requires wrapping the SPA as a PCF, and **PCF uses `context.webAPI`, not the global `Xrm.WebApi`** that LegalWorkspace's 48 files depend on |
| Standalone SPA (Static Web Apps + MSAL) | Full control, but loses `Xrm` entirely — a 48-file refactor |
| Canvas app | Cannot meaningfully host an arbitrary React SPA |

**Residual risk + mitigation:** a user who lands without `navbar=off` gets the nav back. Fix with a **self-healing redirect in `main.tsx`** (~5 lines): if `navbar !== 'off'`, set it and `location.replace`. Idempotent, no loop, and it never constructs `appid` itself — which matters because `appid` is environment-specific. Then control entry points (sitemap **URL** page in Spaarke Matter Management, which [opens in a new tab](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/app-navigation); bookmark; tile).

**Still to verify:** does `navbar=off` survive a wizard launch + return (`Create a matter` hands off via sessionStorage to an OOB form / wizard code page)? The self-heal catches it regardless.

**Solution packaging — CLOSED for R1** `[2026-10-01, owner item 8-6]`: we are **not using managed solutions**, and
R1 builds and deploys **in dev only**. Packaging is a deployment question for a later phase, not a design
constraint here.

**Should `sprk_spaarkeai` be renamed to "Console"?** — **RESOLVED 2026-10-02.** The concept was never in doubt:
**SpaarkeAi becomes the Console** (component model decision 19). The question was whether the *identifier*
follows, and the answer is a split, because the blast radius is not where it looks:

| Layer | Blast radius | Decision |
|---|---|---|
| **Product / UI surface** — titles, labels, docs, how we talk about it | — | ✅ **Renamed now.** Free |
| **Live source + config** — `src/**` (16 files), 3 deploy scripts, 1 workflow, 1 ribbon XML | **32 occurrences, ~21 files** | ⏸ **Deferred** → [#1095](https://github.com/spaarke-dev/spaarke/issues/1095) |
| **The Dataverse web-resource name `sprk_spaarkeai`** | the deployed artifact; deep links **server-generated** in `HandoffUrlBuilder`, plus ribbon XML + 16 launch points | ⏸ **Deferred** → [#1095](https://github.com/spaarke-dev/spaarke/issues/1095) |
| **Historical project docs** | 705 occurrences / 256 files | ❌ Leave — rewriting history adds risk and no value |

> ### 🔴 The decisive number is concurrency, not file count
>
> The identifier rename is **~21 files and entirely mechanical** — on its own, an afternoon. What makes it
> expensive is who else is in those files: `projects/INDEX.md` lists **37 of 62 active projects with
> `SpaarkeAi = Y`**, and several (the whole Compose line, `spaarkeai-assistant-enhancements-r3`/`r4`) are
> actively editing the exact files that hold the name. Renaming now buys 37 merge conflicts that other projects
> pay for.
>
> It is also **not blocked by the thing I first said blocked it.** R1 is dev-only with no managed solutions, so
> broken bookmarks are not the constraint — a coordinated redeploy would regenerate every server-generated deep
> link. The constraint is purely the in-flight worktree count.
>
> **Same pattern as decision 10** (~~ledger~~ → Decision Record customer-facing, *ledger* in engineering docs):
> customer-facing name and engineering identifier are allowed to differ. **Scheduled, not listed**, per §5.0.

> **Architectural dependency to record: the Xrm wrapper is load-bearing.** Because the Console runs inside `main.aspx`, the global `Xrm.WebApi` is available — which is *why* LegalWorkspace sections can self-fetch and why the worklist needs no workspace changes. Every rejected alternative above costs that, which is the real reason this one wins.

## 5. Explicitly out of scope

| Out | Why |
|---|---|
| **The Connection Engine** — connector, parser, sync engine, scheduler | **Out by owner decision 2026-10-01** (§1 correction (a)). The carve-out is the landing contract in §4.1 |
| **LEDES intake** | **Out, and not the plan.** Computed metrics from the e-billing platform instead |
| **The Action *Engine*** (not Actions) | ⚠️ **The distinction, since it has been asked**: an **Action** is a *data row* — an Action + an ADR-039 Binding — dispatched by the spine that already ships (`ConfirmationPolicyEngine` · `GateDecisionV2` · `SideEffectGateAIFunction` · `OutputRouter`, 8 dispositions). The **Action Engine** is the management plane above it: tool registry, action definitions, templates, triggers, scheduling, three invocation paths, run records, meta-tools. R1 needs the row and the shipped spine. **If the Engine is built later, R1's rows are its input, not a rewrite** — which is what makes deferring it safe |
| **Moving *Policy* into the Insights Engine** (CM-1) | ⚠️ **Re-worded 2026-10-01.** The **Insights Engine is built and consumed by R1** — verified on master: `Services/Insights/{Graph,LiveFacts,Observations,Precedents}`, `InsightsOrchestrator`, `InsightArtifact`, `EvidenceRef`, Insight endpoints. R1 reads facts through `ILiveFactResolver` and renders the Fact / Observation tiers, and **adds one new output type, the signal.** What waits for a second consumer is only the `IPolicyEvaluator` contract. The earlier phrasing read as though Insights itself were out of scope |
| ~~**Unifying `sprk_spendsignal` with `InsightArtifact`**~~ | **Obsolete as written** (§4.3a): `sprk_spendsignal` is being *replaced* by generic `sprk_signal`, so there is nothing to unify. The live question — is `sprk_signal` an `InsightArtifact` subtype or a sibling table? — is **D-7**, recommended **sibling**, because `InsightArtifact` is a response/evidence contract while a signal is a durable queue row with a lifecycle and suppression |
| **Per-entity fact / signal tables** | `ILiveFactResolver` + rollup/calculated columns cover fact supply (§4.3a, D-8) |
| **Authority** | While every action requires human confirmation, the human *is* the authority and Dataverse security answers "may this person confirm" |
| **Writeback to source** (Patterns C/D) | Not needed — Pattern A/B only. Inquiry acts through email, not the e-billing API |
| **MCP egress** | Reach, not function. No dependency either way |
| **Document binding / iManage** | Reference-mode documents are a separate slice |
| **Email/Exchange binding** | Already works bespoke; refactoring it into Connection Engine is post-MVP |
| **The other five rule types** | `Threshold` + `Switch` cover the spend case |
| **Party / org resolution** | Matter-number match suffices; transitive lookup resolution is post-MVP |
| **Model 1 vs Model 2 tenancy** | Deferred by owner decision |
| **Privilege inheritance on derived data** | Deferred by owner decision; separate ADR |
| **Fabric IQ projection · Foundry IQ retrieval swap** | Neither is on the MVP path |

## 6. Success criteria

1. ⚠️ **Replaced 2026-10-01** (no LEDES, no connector). **Invoice, budget and spend values present in Spaarke
   carry source attribution and a freshness stamp, and the UI shows a stale or unbound source as a gap — never
   as a zero.** *(The landing contract is what R1 owes here; producing the rows is the later connector phase.
   The freshness half is not cosmetic: prototype finding 3 — an absence clause is only as true as its source is
   fresh.)*
2. ⚠️ **Replaced 2026-10-01.** **Attribute ownership holds on a mirrored entity**: projected fields are
   read-only and derived fields writable **in the same row**, demonstrable on seeded data without a connector.
3. A legal-ops admin can change a budget-variance threshold **in a form, without a deploy**, and the change produces a new policy version that the old signals still reference.
4. A matter breaching the threshold appears in the Console worklist, showing **why** (policy code + version + the triggering values).
5. One click sends a budget inquiry through the gate; the inquiry is tracked with an SLA; the reply resolves it with a **disposition**.
6. The matter's Decision Record shows the full entry: what was proposed, the fact that triggered it, the policy version, who confirmed, and the outcome.
7. Nothing was written back to the e-billing system.
8. **The differentiation criterion** — *strengthened 2026-09-30*: at least one signal type fires on a predicate that **reads** both a communication field and a budget field. **A demo that only shows budget variance has not met the bar — and neither has a rule that merely *mentions* the budget in its message.**

   > 🔴 **Why the wording changed.** The original read *"fires on evidence the e-billing system does not hold."*
   > The broken first predicate — which read **no budget field at all** and fired on a classified email alone
   > — **would have passed it**, because an email *is* evidence an e-billing system does not hold. A success
   > criterion that the failure satisfies is not a criterion. The test is now about what the predicate
   > **reads**, not what the signal is *about* (spec §0.3).

9. **Something happens in the world.** A confirmed signal produces an Inquiry through the existing gate; the Inquiry carries an SLA; the reply resolves it with a **disposition** queryable per matter and per outside firm. *(This was always criterion 5 here; recorded again because `design.md` rev 1 dropped it and had to restore it — criteria that only cover detect/record/surface describe an alerting product.)*
10. **Classifier recall meets a stated floor.** Recall on `Scope / budget change` and `Fee / rate change` is measured against a labelled set, with the floor recorded. *(New 2026-09-30. The predicate is a **conjunction**, so it inherits its weakest input — the LLM classifier. At 70% recall the differentiated claim silently misses 30% of real cases while every other criterion above still passes green.)*

## 7. Open items before this becomes a spec

> **All seven answered by the owner on 2026-10-01.** Kept with their resolutions rather than deleted, so the
> reasoning survives. Live decisions now sit in `design.md` §8.

| # | Item | Resolution (2026-10-01) |
|---|---|---|
| 1 | **Which MM system** for the first connector? | ✅ **Moot for R1 — no connector in this project** (§1 correction (a)). The question returns with the connector phase, and is still customer-determined |
| 2 | **Where does budget come from?** | ✅ **Not an R1 question.** Budget is **created manually in dev for development testing**; R1 does not define its provenance. This collapses the D-1 spike into the §8.1 seeding checklist and **removes D-1 from the critical path**. Two obligations survive: the smallest `sprk_budgetrevision` must still be created (no revision history exists in any form), and §8.1's **exit pair** must exist before criterion 2 can be shown. ⚠️ Seeded data proves the predicate *evaluates*; it cannot prove it *fires on reality* — `design.md` §9's top risk is not retired by seeding |
| 3 | **Does `sprk_spendsignal` need extending?** | ✅ **Assessed — it needs replacing, not extending.** Its `sprk_snapshot` lookup makes it structurally spend-only, so it cannot carry cross-source evidence. Generic **`sprk_signal`** instead, free now (zero client references, no production data) and a migration later. **Shape from D-3** (prototype): a **polymorphic subject** — the SLA flag is *about* an Inquiry — **plus an always-populated matter lookup** as the grouping key, derived from the subject. Carries policy name + version + the values that fired, a `sprk_dedupekey` alternate key, `sprk_resolutiontype` (`ConditionCleared` / `Acted` / `Dismissed` / `Superseded` / `PolicyRetired`), `sprk_lastevaluated`, and a **nullable** decision reference (BR-1). See §4.3a and D-7/D-8 |
| 4 | **Tell Front Door before it specs** | ✅ **Confirmed: `sprk_legalrequest` is absorbed by the existing `sprk_servicerequest`** with a direction discriminator (CM-5). `spaarke-legal-front-door-r1` is pre-spec and **must be told**, since its roadmap says this decision "gates everything" — the only open part is the telling |
| 5 | ~~**Contention** — `Services/Ai/` owned by `spaarke-ai-architecture-redesign-r2`~~ | ✅ **Void — that project is CLOSED** and will not reopen (verified 2026-10-01 in `projects/INDEX.md`). `Services/Ai/` has **no sole owner**, so whatever AI-side work this project needs is **done in this project**. `/conflict-check` still applies for in-flight neighbours (`spaarkeai-assistant-enhancements-r3/r4`, the Compose line) |
| 6 | **Is `sprk_spaarkeai` in the managed solution?** | ✅ **Not a question for R1.** We are **not using managed solutions**, and R1 builds and deploys **dev only**. **New question in its place — should `sprk_spaarkeai` be renamed "Console"?** Answered in §4.5 with the measured blast radius: **rename the product surface now, keep the web-resource identifier**, because deep links are server-generated and the name is embedded in ribbon XML and 16 launch points |
| 7 | **Scope call: is the MM connector in MVP?** | ✅ **No — out of scope** (§1 correction (a)). Decided, not deferred |

## 8. Why this is the right MVP

- **Proves the pipeline from the Spaarke data model forward**, which is the half we can build without a customer
  in the room.
- **Rides existing work** — the fact layer (`ILiveFactResolver`), the Insights Engine, the evaluation engine, the
  gate and the dispatch spine are all built; we are adding the policy layer above and the surface below.
- ~~Needs no vendor cooperation for half the binding (LEDES is a file).~~ **Retired 2026-10-01** — this argument
  was LEDES-shaped. The replacement is stronger: **R1 needs no vendor cooperation at all**, because it assumes
  the data is already in Spaarke.
- **Closes two real gaps today** — signals are computed and invisible, and the Daily Briefing surfaces work with
  no reason, no action and no record.
- **It is a sellable bootcamp**, not a demo: a live spend-governance loop on the customer's own data in a bootcamp window.

**But only if the cross-source rule ships with it.** The spend loop alone proves the plumbing and loses the pitch. What makes the bootcamp sellable is the second rule — an assessment the customer's own e-billing platform structurally cannot produce, delivered with the evidence, the rule version that flagged it, and the record of what was decided.
