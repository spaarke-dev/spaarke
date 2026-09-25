# Spaarke LOI Platform — MVP Synopsis

> **Status**: Draft for owner review, 2026-09-24. Not a spec.
> **Companions**: [`ontology-component-model.md`](ontology-component-model.md) (definitions) · [`spaarke-ontology-strategy-synopsis-v2.md`](spaarke-ontology-strategy-synopsis-v2.md) (strategy) · [`phase0-codebase-inventory.md`](phase0-codebase-inventory.md) (verified state)

---

## 1. The MVP in one sentence

> **Bind a customer's matter-management and e-billing data, evaluate it against rules the customer owns, surface what needs attention in the Spaarke Console, let the user act, and record what was decided.**

This is §8's second bootcamp SKU — *"if the buyer's pain is spend, run a LEDES drop plus the inquiry loop"* — extended with matter mirror so the binding story is proven rather than dodged.

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
> **The test to apply to every capability before scoping it** — see [`mvp-technical-spec.md`](mvp-technical-spec.md) §0:
>
> > **Name the data it requires that the incumbent does not hold. If you cannot, it is not differentiated.**
>
> Two design consequences: the signal entity must **not** be spend-shaped (`sprk_spendsignal`'s `sprk_snapshot` lookup won't carry cross-source evidence — replaced by generic `sprk_signal`, spec §2.5), and **signals are Insights Engine outputs**, with `SignalEvaluationService` becoming one producer among several. That extends CM-1.

## 2. The slice — all six pipeline stages, end to end

| Stage | MVP implementation | New? |
|---|---|---|
| **bind** | One MM connector (mirror Matter) + LEDES file drop (mirror Invoice) | 🔴 **new — Area 1** |
| **resolve** | Matter-number match → extend the existing rung ladder | 🟡 extend |
| **compute facts** | `sprk_spendsnapshot` (`invoicedamount`, `budgetamount`, `velocitypct`) | 🟢 **built** |
| **match** | `SignalEvaluationService` → `sprk_spendsignal`; swap appsettings thresholds for policy rows | 🟢 built · 🟡 small change |
| **execute under gate** | Inquiry action through the existing gate engine | 🟢 built · data only |
| **record** | Decision Record entity + subgrid on the matter + list view | 🔴 **new — Area 2** |
| **surface** | Worklist widget over open signals | 🔴 **new — Area 2** |

**Four of seven stages are already built.** The work concentrates at the two ends — getting data in, and closing the loop once something fires.

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

> **(1) Connection Engine** — new, zero code, the bulk of the work
> **(2) Close the loop** — worklist widget + Decision Record

### 4.1 Area 1 — Connection Engine

The only genuinely new engine. Nothing exists today.

| Deliverable | Notes |
|---|---|
| **LEDES parser + invoice landing** | Vendor-neutral; **no vendor cooperation required**. Half the binding story needs no connector at all |
| **MM connector** (mirror Matter) | First connector; builds the pattern for every subsequent one |
| **Landing contract** | Pointer fields (`sourcesystem` · `sourceid` · `sourceetag` · `sourceasof`) + attribute ownership on `sprk_matter` |
| **Raw payload retention** | Cosmos, keyed by sync run — buys replay when a mapping rule is wrong (§4.15.1 of the component model) |
| **Freshness surfacing** | Per-object `sourceasof`, visible in the UI. Not rentable; platform work |

### 4.2 Area 2 — Close the loop

Where the verified gap is: **`sprk_spendsignal` is computed and stored today with zero client-side references.** Signals exist, nothing shows them, and nothing happens off the back of one.

| Deliverable | Notes |
|---|---|
| **Worklist widget** | View over open signals. Each row: the object · **why it fired** (policy code + version + triggering values) · its actions. **Narrower than it sounds** — Daily Briefing's "Critical Today" is already a worklist in structure (type badge, status chip, per-row ⋮ menu). The upgrade is *where membership comes from*, *the reason*, and *wiring the actions* |
| **Decision Record entity** | Append-only (no Update/Delete privileges for anyone), **written at the gate**, subgrid on the matter + list view. The Context pane already renders session-ledger entries via `ISessionTraceReader` — an existing pattern to extend |

### 4.3 Small additions inside existing code — *not* a new engine

`SignalEvaluationService` already evaluates correctly (deterministic, no AI, idempotent, `ISignalRule` strategy pattern). **Do not move working code for MVP.**

| Change | Notes |
|---|---|
| **`sprk_policy` + `sprk_policyversion` entities** | Two rule types only: `Threshold`, `Switch` |
| **`SignalEvaluationService` reads policy rows** | Replaces `FinanceOptions` appsettings thresholds |
| **Stamp policy code + version onto each signal** | This is what makes the "why" citable and the old signals still explicable after a threshold changes |

> **CM-1 (Policy lives in Insights) is a destination, not an MVP move.** The `IPolicyEvaluator` contract in Insights gets built when a *second* consumer appears. For MVP, Policy is Dataverse rows that Finance's existing service reads.

### 4.4 Data, not build

| Item | Why it's not a build |
|---|---|
| **Inquiry action** | One Action + one Binding. Dispatch already exists: ADR-039 Binding catalog · `ConfirmationPolicyEngine` · `GateDecisionV2` · `SideEffectGateAIFunction` · `OutputRouter` (8 dispositions). Uses `sprk_servicerequest` with an outbound direction + typed outcome (CM-5) |
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

**Still open:** solution packaging — the deploy script does a direct Web API upload, not a managed-solution import, and there are **no `AppModule`/`SiteMap` XML definitions in the repo**. Customer deployment goes through the provisioning control plane, so both need resolving (§7 item 6).

> **Architectural dependency to record: the Xrm wrapper is load-bearing.** Because the Console runs inside `main.aspx`, the global `Xrm.WebApi` is available — which is *why* LegalWorkspace sections can self-fetch and why the worklist needs no workspace changes. Every rejected alternative above costs that, which is the real reason this one wins.

## 5. Explicitly out of scope

| Out | Why |
|---|---|
| **The Action Engine** | The MVP needs an *Action*, not the *Action Engine*. Tool Registry, Action Definitions, templates, triggers, scheduling, three invocation paths, run records and meta-tools are a management plane — on hold at 5% and not on the MVP path. The dispatch spine already exists (§4.4) |
| **Moving Policy into Insights Engine** | CM-1 is the destination. For MVP, Policy is Dataverse rows that `SignalEvaluationService` reads in place. Build the `IPolicyEvaluator` contract when a second consumer appears |
| **Unifying `sprk_spendsignal` with `InsightArtifact`** | Conceptually right (a policy evaluation is a Fact, §4.2 of the component model) but **zero MVP payoff** |
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

1. A LEDES file dropped for a customer produces invoice rows linked to the correct matters, with source attribution and a freshness stamp.
2. Matter data from the MM system appears in Spaarke with projected fields read-only and derived fields writable **in the same row**.
3. A legal-ops admin can change a budget-variance threshold **in a form, without a deploy**, and the change produces a new policy version that the old signals still reference.
4. A matter breaching the threshold appears in the Console worklist, showing **why** (policy code + version + the triggering values).
5. One click sends a budget inquiry through the gate; the inquiry is tracked with an SLA; the reply resolves it with a **typed outcome**.
6. The matter's Decision Record shows the full entry: what was proposed, the fact that triggered it, the policy version, who confirmed, and the outcome.
7. Nothing was written back to the e-billing system.
8. **The differentiation criterion**: at least one signal type fires on evidence the e-billing system does not hold — the cross-source rule (spend + communication). **A demo that only shows budget variance has not met the bar.**

## 7. Open items before this becomes a spec

| # | Item | Owner |
|---|---|---|
| 1 | **Which MM system** for the first connector? Aderant / Elite 3E / other — determined by the first customer | Owner |
| 2 | **Where does budget come from?** `sprk_spendsnapshot.sprk_budgetamount` exists — is it populated today, and by what? If manual, MVP is fine; if it needs the MM system, that's connector scope | Verify |
| 3 | **Does `sprk_spendsignal` need extending** to carry policy code + version + triggering values, or does it already? | Verify |
| 4 | **Tell Front Door before it specs** — `sprk_servicerequest` with a direction discriminator likely absorbs both `sprk_legalrequest` and Inquiry (CM-5). Its roadmap says this decision "gates everything" | Owner |
| 5 | **Contention** — `Services/Ai/` is owned by `spaarke-ai-architecture-redesign-r2`; **Finance is quieter, which is where the MVP's server work lands.** Favourable | Coordinate |
| 6 | **Is `sprk_spaarkeai` in the managed solution**, or deployed per-environment by script? Determines how the Console reaches a customer environment | Verify |
| 7 | **Scope call: is the MM connector in MVP?** It is the single largest item. LEDES-only is ~2 months and still proves all seven stages; MM mirror adds ~2 months and proves the *binding* story properly | Owner |

## 8. Why this is the right MVP

- **Proves all six pipeline stages** with the cheapest possible binding pair.
- **Rides existing work** — the fact layer and the evaluation engine are built; we are adding the policy layer above and the surface below.
- **Needs no vendor cooperation** for half the binding (LEDES is a file).
- **Closes a real gap today** — signals are already computed and invisible.
- **It is a sellable bootcamp**, not a demo: a live spend-governance loop on the customer's own data in a bootcamp window.

**But only if the cross-source rule ships with it.** The spend loop alone proves the plumbing and loses the pitch. What makes the bootcamp sellable is the second rule — an assessment the customer's own e-billing platform structurally cannot produce, delivered with the evidence, the rule version that flagged it, and the record of what was decided.
